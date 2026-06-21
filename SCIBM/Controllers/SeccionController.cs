using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using OfficeOpenXml;
using SCIBM.Helpers;
using SCIBM.Models;

namespace SCIBM.Controllers
{
    public class SeccionController : Controller
    {
        private async Task<string> GetValidAccessTokenAsync(ScibmContext db, string email)
        {
            string accessToken = Session["AccessToken"]?.ToString();
            DateTime? expiry = Session["TokenExpiry"] as DateTime?;

            if (string.IsNullOrEmpty(accessToken) || !expiry.HasValue || DateTime.Now >= expiry.Value)
            {
                string clientId = ConfigurationManager.AppSettings["Google:ClientId"];
                string clientSecret = ConfigurationManager.AppSettings["Google:ClientSecret"];
                string refreshToken = Session["RefreshToken"]?.ToString();

                if (string.IsNullOrEmpty(refreshToken))
                {
                    var docente = await db.Docentes.FindAsync(email);
                    refreshToken = docente?.RefreshToken;
                }

                if (!string.IsNullOrEmpty(refreshToken))
                {
                    var tokenResult = await GoogleDriveHelper.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken);
                    if (tokenResult != null)
                    {
                        accessToken = tokenResult.Item1;
                        Session["AccessToken"] = accessToken;
                        Session["TokenExpiry"] = DateTime.Now.AddSeconds(tokenResult.Item2 - 60);
                        return accessToken;
                    }
                }
            }
            return accessToken;
        }

        // POST: Seccion/Create
        [HttpPost]
        public async Task<ActionResult> Create(Guid cursoId, string nombreSeccion)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada." });

            if (string.IsNullOrEmpty(nombreSeccion))
                return Json(new { success = false, message = "El nombre de la sección es obligatorio." });

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                try
                {
                    var curso = await db.Cursos.Include(c => c.CicloAcademico).FirstOrDefaultAsync(c => c.Id == cursoId);
                    if (curso == null || curso.CicloAcademico.DocenteEmail != email)
                        return Json(new { success = false, message = "Curso no encontrado o sin permisos." });

                    string accessToken = await GetValidAccessTokenAsync(db, email);
                    string seccionFolderId = null;

                    if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(curso.DriveFolderId))
                    {
                        seccionFolderId = await GoogleDriveHelper.GetOrCreateFolderAsync(nombreSeccion, curso.DriveFolderId, accessToken);
                    }

                    var seccion = new Seccion
                    {
                        CursoId = curso.Id,
                        Nombre = nombreSeccion,
                        DriveFolderId = seccionFolderId
                    };
                    db.Secciones.Add(seccion);
                    await db.SaveChangesAsync();

                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al crear sección: " + ex.Message });
                }
            }
        }

        // POST: Seccion/Rename
        [HttpPost]
        public async Task<ActionResult> Rename(Guid id, string newName)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada." });

            if (string.IsNullOrEmpty(newName))
                return Json(new { success = false, message = "El nombre no puede estar vacío." });

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                try
                {
                    var seccion = await db.Secciones.Include(s => s.Curso.CicloAcademico).FirstOrDefaultAsync(s => s.Id == id && s.Curso.CicloAcademico.DocenteEmail == email);
                    if (seccion == null)
                        return Json(new { success = false, message = "Sección no encontrada o sin permisos." });

                    bool exists = await db.Secciones.AnyAsync(s => s.CursoId == seccion.CursoId && s.Nombre == newName && s.Id != id);
                    if (exists)
                        return Json(new { success = false, message = "Ya existe otra sección con este nombre en este curso." });

                    seccion.Nombre = newName;

                    if (!string.IsNullOrEmpty(seccion.DriveFolderId))
                    {
                        string accessToken = await GetValidAccessTokenAsync(db, email);
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            await GoogleDriveHelper.RenameFolderAsync(seccion.DriveFolderId, newName, accessToken);
                        }
                    }

                    await db.SaveChangesAsync();

                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al renombrar: " + ex.Message });
                }
            }
        }

        // GET: Seccion/Detail/5
        public async Task<ActionResult> Detail(Guid? id)
        {
            if (Session["UserEmail"] == null)
                return RedirectToAction("Login", "Auth");

            if (!id.HasValue)
                return RedirectToAction("Index", "Ciclo");

            using (var db = new ScibmContext())
            {
                var seccion = await db.Secciones
                    .Include(s => s.Curso)
                    .Include(s => s.AlumnosMatriculados)
                    .Include(s => s.Unidades)
                    .FirstOrDefaultAsync(s => s.Id == id.Value);

                if (seccion == null || seccion.Curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                    return HttpNotFound("La sección no existe o no tiene permisos.");

                // Cargar también los exámenes existentes por cada unidad
                var unidadesIds = seccion.Unidades.Select(u => u.Id).ToList();
                var examenes = await db.Examenes
                    .Where(e => unidadesIds.Contains(e.Id))
                    .ToListAsync();
                
                ViewBag.Examenes = examenes;

                return View(seccion);
            }
        }

        private string FixMojibake(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            // Reemplazos manuales de caracteres que la conversión de bytes no puede procesar
            // porque cayeron en el rango de caracteres de control C1 (como el \u0091 de la Ñ mayúscula)
            input = input.Replace("Ã\u0091", "Ñ")
                         .Replace("Ã\u00B1", "ñ")
                         .Replace("Ã±", "ñ")
                         .Replace("Ã‘", "Ñ");

            if (input.Contains("Ã"))
            {
                // Reemplazos de emergencia comunes
                input = input.Replace("Ã¡", "á")
                             .Replace("Ã©", "é")
                             .Replace("Ã\u00AD", "í")
                             .Replace("Ã³", "ó")
                             .Replace("Ãº", "ú")
                             .Replace("Ã\u0081", "Á")
                             .Replace("Ã\u0089", "É")
                             .Replace("Ã\u008D", "Í")
                             .Replace("Ã\u0093", "Ó")
                             .Replace("Ã\u009A", "Ú");

                try
                {
                    byte[] bytes = System.Text.Encoding.GetEncoding(28591).GetBytes(input); // ISO-8859-1
                    string decoded = System.Text.Encoding.UTF8.GetString(bytes);
                    if (!decoded.Contains("") && !decoded.Contains("?")) return decoded;
                }
                catch { }
            }
            return input;
        }

        [HttpGet]
        public async Task<ActionResult> CheckExamenesCalificados(Guid seccionId)
        {
            if (Session["UserEmail"] == null) return Json(new { hasGraded = false }, JsonRequestBehavior.AllowGet);
            
            using (var db = new ScibmContext())
            {
                bool hasGraded = await db.ExamenesAlumnos.AnyAsync(e => e.AlumnoMatriculado != null && e.AlumnoMatriculado.SeccionId == seccionId);
                return Json(new { hasGraded = hasGraded }, JsonRequestBehavior.AllowGet);
            }
        }

        // POST: Seccion/ImportarAlumnos
        [HttpPost]
        public async Task<ActionResult> ImportarAlumnos(Guid seccionId, HttpPostedFileBase archivoExcel, string strategy = "Conservar")
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada. Por favor inicie sesión de nuevo." });

            if (archivoExcel == null || archivoExcel.ContentLength == 0)
            {
                return Json(new { success = false, message = "Debe seleccionar un archivo Excel válido." });
            }

            using (var db = new ScibmContext())
            {
                var seccion = await db.Secciones.Include(s => s.Curso).FirstOrDefaultAsync(s => s.Id == seccionId);
                if (seccion == null || seccion.Curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                    return Json(new { success = false, message = "Sección no encontrada o no tiene permisos." });

                try
                {
                    using (var package = new ExcelPackage(archivoExcel.InputStream))
                    {
                        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                        if (worksheet == null || worksheet.Dimension == null)
                        {
                            return Json(new { success = false, message = "Coloque la lista de alumnos en una sola columna sin encabezado en la primera hoja del archivo Excel." });
                        }

                        // Limpieza inteligente
                        var existingStudents = await db.AlumnosMatriculados.Where(a => a.SeccionId == seccionId).ToListAsync();
                        foreach (var student in existingStudents)
                        {
                            if (strategy == "Continuar")
                            {
                                // Borrar a todos. Los examenes quedan sin alumno asociado.
                                var exams = await db.ExamenesAlumnos.Where(e => e.AlumnoMatriculadoId == student.Id).ToListAsync();
                                foreach (var ex in exams)
                                {
                                    ex.AlumnoMatriculadoId = null;
                                    ex.TieneObservacion = true;
                                    ex.Observacion = "Alumno eliminado al reemplazar Excel de matrícula.";
                                }
                                db.AlumnosMatriculados.Remove(student);
                            }
                            else // Conservar
                            {
                                // Borrar solo los que no tienen examen
                                bool hasExams = await db.ExamenesAlumnos.AnyAsync(e => e.AlumnoMatriculadoId == student.Id);
                                if (!hasExams)
                                {
                                    db.AlumnosMatriculados.Remove(student);
                                }
                            }
                        }
                        await db.SaveChangesAsync();

                        int rowCount = worksheet.Dimension.Rows;
                        int importedCount = 0;
                        HashSet<string> addedNames = new HashSet<string>();

                        // Pre-cargar los que quedaron
                        var remainingStudents = await db.AlumnosMatriculados.Where(a => a.SeccionId == seccionId).Select(a => a.NombreCompleto).ToListAsync();
                        foreach (var r in remainingStudents) addedNames.Add(r);

                        for (int row = 1; row <= rowCount; row++)
                        {
                            var cellValue = worksheet.Cells[row, 1].Value?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(cellValue)) continue;

                            cellValue = FixMojibake(cellValue); // CORRECCIÓN DE Ñ Y TILDES

                            string nombres = "";
                            string apellidos = "";
                            string nombreCompleto = "";

                            if (cellValue.Contains(","))
                            {
                                var parts = cellValue.Split(new[] { ',' }, 2);
                                apellidos = parts[0].Trim();
                                nombres = parts[1].Trim();
                                nombreCompleto = $"{apellidos}, {nombres}";
                            }
                            else
                            {
                                var parts = cellValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 4)
                                {
                                    nombres = $"{parts[0]} {parts[1]}";
                                    apellidos = string.Join(" ", parts.Skip(2));
                                }
                                else if (parts.Length == 3)
                                {
                                    nombres = parts[0];
                                    apellidos = $"{parts[1]} {parts[2]}";
                                }
                                else if (parts.Length == 2)
                                {
                                    nombres = parts[0];
                                    apellidos = parts[1];
                                }
                                else
                                {
                                    nombres = parts[0];
                                    apellidos = "S/A";
                                }
                                nombreCompleto = $"{apellidos}, {nombres}";
                            }

                            if (!addedNames.Contains(nombreCompleto))
                            {
                                var alumno = new AlumnoMatriculado
                                {
                                    SeccionId = seccionId,
                                    NombreCompleto = nombreCompleto,
                                    Apellidos = apellidos,
                                    Nombres = nombres
                                };
                                db.AlumnosMatriculados.Add(alumno);
                                addedNames.Add(nombreCompleto);
                                importedCount++;
                            }
                        }

                        await db.SaveChangesAsync();
                        TempData["Success"] = $"Se importaron con éxito {importedCount} alumnos y se limpió la lista antigua.";
                        return Json(new { success = true });
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al procesar el archivo Excel: " + ex.Message });
                }
            }
        }

        // GET: Seccion/Reporte/5
        public async Task<ActionResult> Reporte(Guid id)
        {
            if (Session["UserEmail"] == null)
                return RedirectToAction("Login", "Auth");

            using (var db = new ScibmContext())
            {
                var seccion = await db.Secciones
                    .Include(s => s.Curso)
                    .Include(s => s.Curso.CicloAcademico)
                    .Include(s => s.Unidades)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (seccion == null || seccion.Curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                    return HttpNotFound();

                var unidadesIds = seccion.Unidades.Select(u => u.Id).ToList();
                var examenes = await db.Examenes
                    .Where(e => unidadesIds.Contains(e.Id))
                    .ToListAsync();
                
                ViewBag.Examenes = examenes;

                return View(seccion);
            }
        }

        // GET: Seccion/GetReporteGeneralData
        [HttpGet]
        public async Task<ActionResult> GetReporteGeneralData(Guid seccionId)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión vencida" }, JsonRequestBehavior.AllowGet);

            using (var db = new ScibmContext())
            {
                var seccion = await db.Secciones
                    .Include(s => s.Unidades)
                    .FirstOrDefaultAsync(s => s.Id == seccionId);

                if (seccion == null)
                    return Json(new { success = false, message = "Sección no encontrada" }, JsonRequestBehavior.AllowGet);

                var unidades = seccion.Unidades.OrderBy(u => u.NombreUnidad).ToList();
                var alumnos = await db.AlumnosMatriculados
                    .Where(a => a.SeccionId == seccionId)
                    .OrderBy(a => a.NombreCompleto)
                    .ToListAsync();

                var unidadesIds = unidades.Select(u => u.Id).ToList();
                var examenes = await db.Examenes.Where(e => unidadesIds.Contains(e.Id)).ToListAsync();
                var examenesIds = examenes.Select(e => e.Id).ToList();

                var notas = await db.ExamenesAlumnos
                    .Where(ea => examenesIds.Contains(ea.ExamenId))
                    .ToListAsync();

                var rows = new List<Dictionary<string, object>>();

                // 1. Alumnos Matriculados
                foreach (var al in alumnos)
                {
                    var row = new Dictionary<string, object>();
                    row["NombreAlumno"] = al.NombreCompleto;

                    foreach (var un in unidades)
                    {
                        var examenDeUnidad = examenes.FirstOrDefault(e => e.UnidadId == un.Id);
                        if (examenDeUnidad != null)
                        {
                            var notaAlumno = notas.FirstOrDefault(n => n.ExamenId == examenDeUnidad.Id && n.AlumnoMatriculadoId == al.Id);
                            row[un.NombreUnidad] = (notaAlumno != null) ? (object)notaAlumno.Nota : "-";
                        }
                        else
                        {
                            row[un.NombreUnidad] = "-";
                        }
                    }
                    rows.Add(row);
                }

                // 2. Alumnos NO Matriculados (solo detectados por IA)
                var alumnosNoMatriculados = notas
                    .Where(n => n.AlumnoMatriculadoId == null && !string.IsNullOrEmpty(n.NombreAlumno))
                    .Select(n => n.NombreAlumno)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                foreach (var noMatr in alumnosNoMatriculados)
                {
                    var row = new Dictionary<string, object>();
                    row["NombreAlumno"] = $"{noMatr} (No Matr.)";

                    foreach (var un in unidades)
                    {
                        var examenDeUnidad = examenes.FirstOrDefault(e => e.UnidadId == un.Id);
                        if (examenDeUnidad != null)
                        {
                            var notaAlumno = notas.FirstOrDefault(n => n.ExamenId == examenDeUnidad.Id && n.AlumnoMatriculadoId == null && n.NombreAlumno == noMatr);
                            row[un.NombreUnidad] = (notaAlumno != null) ? (object)notaAlumno.Nota : "-";
                        }
                        else
                        {
                            row[un.NombreUnidad] = "-";
                        }
                    }
                    rows.Add(row);
                }

                return Json(new
                {
                    success = true,
                    unidades = unidades.Select(u => u.NombreUnidad).ToList(),
                    data = rows
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // GET: Seccion/ExportarReporteGeneralExcel
        public async Task<ActionResult> ExportarReporteGeneralExcel(Guid seccionId)
        {
            if (Session["UserEmail"] == null)
                return RedirectToAction("Login", "Auth");

            using (var db = new ScibmContext())
            {
                var seccion = await db.Secciones
                    .Include(s => s.Curso)
                    .Include(s => s.Unidades)
                    .FirstOrDefaultAsync(s => s.Id == seccionId);

                if (seccion == null) return HttpNotFound();

                var unidades = seccion.Unidades.OrderBy(u => u.NombreUnidad).ToList();
                var alumnos = await db.AlumnosMatriculados
                    .Where(a => a.SeccionId == seccionId)
                    .OrderBy(a => a.NombreCompleto)
                    .ToListAsync();

                var unidadesIds = unidades.Select(u => u.Id).ToList();
                var examenes = await db.Examenes.Where(e => unidadesIds.Contains(e.Id)).ToListAsync();
                var examenesIds = examenes.Select(e => e.Id).ToList();

                var notas = await db.ExamenesAlumnos
                    .Where(ea => examenesIds.Contains(ea.ExamenId))
                    .ToListAsync();

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Notas Consolidadas");

                    worksheet.Cells[1, 1].Value = "Estudiante / Nombre Completo";
                    int col = 2;
                    foreach (var un in unidades)
                    {
                        worksheet.Cells[1, col].Value = un.NombreUnidad;
                        col++;
                    }

                    using (var range = worksheet.Cells[1, 1, 1, col - 1])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(26, 37, 48));
                        range.Style.Font.Color.SetColor(System.Drawing.Color.White);
                        range.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    }

                    int rowIdx = 2;
                    
                    // 1. Alumnos Matriculados
                    foreach (var al in alumnos)
                    {
                        worksheet.Cells[rowIdx, 1].Value = al.NombreCompleto;
                        int colIdx = 2;
                        foreach (var un in unidades)
                        {
                            var examenDeUnidad = examenes.FirstOrDefault(e => e.UnidadId == un.Id);
                            if (examenDeUnidad != null)
                            {
                                var notaAlumno = notas.FirstOrDefault(n => n.ExamenId == examenDeUnidad.Id && n.AlumnoMatriculadoId == al.Id);
                                worksheet.Cells[rowIdx, colIdx].Value = (notaAlumno != null) ? (object)notaAlumno.Nota : "-";
                            }
                            else
                            {
                                worksheet.Cells[rowIdx, colIdx].Value = "-";
                            }
                            colIdx++;
                        }
                        rowIdx++;
                    }

                    // 2. Alumnos No Matriculados
                    var alumnosNoMatriculados = notas
                        .Where(n => n.AlumnoMatriculadoId == null && !string.IsNullOrEmpty(n.NombreAlumno))
                        .Select(n => n.NombreAlumno)
                        .Distinct()
                        .OrderBy(n => n)
                        .ToList();

                    foreach (var noMatr in alumnosNoMatriculados)
                    {
                        worksheet.Cells[rowIdx, 1].Value = $"{noMatr} (No Matr.)";
                        int colIdx = 2;
                        foreach (var un in unidades)
                        {
                            var examenDeUnidad = examenes.FirstOrDefault(e => e.UnidadId == un.Id);
                            if (examenDeUnidad != null)
                            {
                                var notaAlumno = notas.FirstOrDefault(n => n.ExamenId == examenDeUnidad.Id && n.AlumnoMatriculadoId == null && n.NombreAlumno == noMatr);
                                worksheet.Cells[rowIdx, colIdx].Value = (notaAlumno != null) ? (object)notaAlumno.Nota : "-";
                            }
                            else
                            {
                                worksheet.Cells[rowIdx, colIdx].Value = "-";
                            }
                            colIdx++;
                        }
                        rowIdx++;
                    }

                    worksheet.Cells.AutoFitColumns();

                    var stream = new MemoryStream();
                    package.SaveAs(stream);
                    stream.Position = 0;

                    string nombreArchivo = $"Reporte_Notas_{seccion.Curso.Nombre}_{seccion.Nombre}.xlsx".Replace(" ", "_");
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
        }
    }
}
