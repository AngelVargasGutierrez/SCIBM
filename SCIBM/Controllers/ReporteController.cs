using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using SCIBM.Helpers;
using SCIBM.Models;

namespace SCIBM.Controllers
{
    public class ReporteController : Controller
    {
        // GET: Reporte/Index (Dashboard Maestro de Reportes)
        public async Task<ActionResult> Index()
        {
            if (Session["UserEmail"] == null)
                return RedirectToAction("Login", "Auth");

            using (var db = new ScibmContext())
            {
                string email = Session["UserEmail"].ToString();

                // Cargar los semestres académicos (ciclos) del docente
                var ciclos = await db.CiclosAcademicos
                    .Where(c => c.DocenteEmail == email)
                    .OrderByDescending(c => c.Nombre)
                    .Select(c => new { c.Id, c.Nombre })
                    .ToListAsync();

                ViewBag.Ciclos = ciclos.Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Nombre
                }).ToList();

                // Cargar todas las facultades disponibles
                var facultades = await db.Facultades
                    .OrderBy(f => f.Nombre)
                    .Select(f => new { f.Id, f.Nombre })
                    .ToListAsync();

                ViewBag.Facultades = facultades.Select(f => new SelectListItem
                {
                    Value = f.Id.ToString(),
                    Text = f.Nombre
                }).ToList();
            }

            return View();
        }

        // AJAX: Obtener Escuelas por Facultad
        [HttpGet]
        public async Task<ActionResult> GetEscuelas(Guid facultadId)
        {
            using (var db = new ScibmContext())
            {
                var escuelas = await db.EscuelasProfesionales
                    .Where(e => e.FacultadId == facultadId)
                    .OrderBy(e => e.Nombre)
                    .Select(e => new { e.Id, Nombre = e.Siglas + "_" + e.Nombre })
                    .ToListAsync();

                return Json(escuelas, JsonRequestBehavior.AllowGet);
            }
        }

        // AJAX: Obtener Carreras por Escuela
        [HttpGet]
        public async Task<ActionResult> GetCarreras(Guid escuelaId)
        {
            using (var db = new ScibmContext())
            {
                var carreras = await db.Carreras
                    .Where(c => c.EscuelaProfesionalId == escuelaId)
                    .OrderBy(c => c.Nombre)
                    .Select(c => new { c.Id, c.Nombre })
                    .ToListAsync();

                return Json(carreras, JsonRequestBehavior.AllowGet);
            }
        }

        // AJAX: Obtener Cursos por Carrera y Ciclo Académico
        [HttpGet]
        public async Task<ActionResult> GetCursos(Guid carreraId, Guid cicloId)
        {
            using (var db = new ScibmContext())
            {
                var cursos = await db.Cursos
                    .Where(c => c.CarreraId == carreraId && c.CicloAcademicoId == cicloId)
                    .OrderBy(c => c.Nombre)
                    .Select(c => new { c.Id, c.Nombre, c.CicloRomano })
                    .ToListAsync();

                return Json(cursos, JsonRequestBehavior.AllowGet);
            }
        }

        // AJAX: Obtener Secciones por Curso
        [HttpGet]
        public async Task<ActionResult> GetSecciones(Guid cursoId)
        {
            using (var db = new ScibmContext())
            {
                var secciones = await db.Secciones
                    .Where(s => s.CursoId == cursoId)
                    .OrderBy(s => s.Nombre)
                    .Select(s => new { s.Id, s.Nombre })
                    .ToListAsync();

                return Json(secciones, JsonRequestBehavior.AllowGet);
            }
        }

        // AJAX: Obtener Unidades por Sección
        [HttpGet]
        public async Task<ActionResult> GetUnidades(Guid seccionId)
        {
            using (var db = new ScibmContext())
            {
                var unidades = await db.Unidades
                    .Where(u => u.SeccionId == seccionId)
                    .OrderBy(u => u.NombreUnidad)
                    .Select(u => new { u.Id, u.NombreUnidad })
                    .ToListAsync();

                return Json(unidades, JsonRequestBehavior.AllowGet);
            }
        }

        // AJAX: Obtener Versiones de Examen por Unidad
        [HttpGet]
        public async Task<ActionResult> GetVersiones(Guid unidadId)
        {
            using (var db = new ScibmContext())
            {
                var versiones = await db.Examenes
                    .Where(e => e.UnidadId == unidadId)
                    .OrderBy(e => e.NombreVersion)
                    .Select(e => new { e.Id, e.NombreVersion })
                    .ToListAsync();

                return Json(versiones, JsonRequestBehavior.AllowGet);
            }
        }

        // AJAX: Obtener Datos Dinámicos para la Tabla y Estadísticas
        // Los parámetros opcionales permiten filtrar a diferentes niveles de granularidad
        [HttpGet]
        public async Task<ActionResult> GetReporteDinamico(Guid cursoId, Guid? seccionId = null, Guid? unidadId = null, Guid? examenId = null)
        {
            using (var db = new ScibmContext())
            {
                // Construir query base: desde ExamenAlumno navegando hacia arriba
                var query = db.ExamenesAlumnos
                    .Include(ea => ea.AlumnoMatriculado)
                    .Include(ea => ea.Examen)
                    .Include(ea => ea.Examen.Unidad)
                    .Include(ea => ea.Examen.Unidad.Seccion)
                    .Where(ea => ea.Examen.Unidad.Seccion.CursoId == cursoId);

                // Aplicar filtros opcionales en cascada
                if (seccionId.HasValue)
                    query = query.Where(ea => ea.Examen.Unidad.SeccionId == seccionId.Value);

                if (unidadId.HasValue)
                    query = query.Where(ea => ea.Examen.UnidadId == unidadId.Value);

                if (examenId.HasValue)
                    query = query.Where(ea => ea.ExamenId == examenId.Value);

                var resultados = await query.ToListAsync();

                if (!resultados.Any())
                {
                    return Json(new
                    {
                        success = true,
                        data = new object[0],
                        stats = new { notaMax = 0.0, notaMin = 0.0, notaPromedio = 0.0, alumnosMax = "", alumnosMin = "", total = 0 }
                    }, JsonRequestBehavior.AllowGet);
                }

                // Calcular estadísticas con soporte a empates
                double notaMax = resultados.Max(r => r.Nota);
                double notaMin = resultados.Min(r => r.Nota);
                double notaPromedio = Math.Round(resultados.Average(r => r.Nota), 2);

                var alumnosMax = resultados
                    .Where(r => r.Nota == notaMax)
                    .Select(r => r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno)
                    .Distinct()
                    .ToList();

                var alumnosMin = resultados
                    .Where(r => r.Nota == notaMin)
                    .Select(r => r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno)
                    .Distinct()
                    .ToList();

                // Determinar color del semáforo del promedio
                string colorPromedio = notaPromedio < 10.5 ? "#ff5252" : notaPromedio < 14.0 ? "#ff9f1c" : "#2ec4b6";

                // Construir la data de la tabla
                var data = resultados.Select(r => new
                {
                    nombre = r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno,
                    nota = r.Nota,
                    estado = r.EstadoRendimiento,
                    color = r.ColorRendimiento,
                    seccion = r.Examen.Unidad.Seccion.Nombre,
                    version = r.Examen.NombreVersion,
                    unidad = r.Examen.Unidad.NombreUnidad
                })
                .OrderByDescending(r => r.nota)
                .ToList();

                return Json(new
                {
                    success = true,
                    data,
                    stats = new
                    {
                        notaMax,
                        notaMin,
                        notaPromedio,
                        colorPromedio,
                        alumnosMax = string.Join(", ", alumnosMax),
                        alumnosMin = string.Join(", ", alumnosMin),
                        total = resultados.Count
                    }
                }, JsonRequestBehavior.AllowGet);
            }

        }
        // Método privado para obtener Token de Google (similar al de otros controladores)
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
                    var result = await GoogleDriveHelper.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken);
                    if (result != null)
                    {
                        Session["AccessToken"] = result.Item1;
                        Session["TokenExpiry"] = DateTime.Now.AddSeconds(result.Item2 - 60);
                        return result.Item1;
                    }
                }
                return null;
            }
            return accessToken;
        }

        // POST: Exportar a Google Sheets
        [HttpPost]
        public async Task<ActionResult> ExportarAGoogleSheets(Guid cursoId, Guid? seccionId = null, Guid? unidadId = null, Guid? examenId = null)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada. Por favor inicie sesión de nuevo." });

            try
            {
                using (var db = new ScibmContext())
                {
                    string email = Session["UserEmail"].ToString();
                    string accessToken = await GetValidAccessTokenAsync(db, email);

                    if (string.IsNullOrEmpty(accessToken))
                        return Json(new { success = false, message = "No se pudo obtener acceso a Google Drive." });

                    // 1. Obtener Datos
                    var query = db.ExamenesAlumnos
                        .Include(ea => ea.AlumnoMatriculado)
                        .Include(ea => ea.Examen)
                        .Include(ea => ea.Examen.Unidad)
                        .Include(ea => ea.Examen.Unidad.Seccion)
                        .Include(ea => ea.Examen.Unidad.Seccion.Curso)
                        .Include(ea => ea.Examen.Unidad.Seccion.Curso.Carrera)
                        .Include(ea => ea.Examen.Unidad.Seccion.Curso.Carrera.EscuelaProfesional)
                        .Include(ea => ea.Examen.Unidad.Seccion.Curso.Carrera.EscuelaProfesional.Facultad)
                        .Where(ea => ea.Examen.Unidad.Seccion.CursoId == cursoId);

                    if (seccionId.HasValue) query = query.Where(ea => ea.Examen.Unidad.SeccionId == seccionId.Value);
                    if (unidadId.HasValue) query = query.Where(ea => ea.Examen.UnidadId == unidadId.Value);
                    if (examenId.HasValue) query = query.Where(ea => ea.ExamenId == examenId.Value);

                    var resultados = await query.ToListAsync();
                    if (!resultados.Any())
                        return Json(new { success = false, message = "No hay datos para exportar." });

                    // Extraer info de contexto para el encabezado
                    var primerR = resultados.First();
                    var curso = primerR.Examen.Unidad.Seccion.Curso;
                    var facultad = curso.Carrera.EscuelaProfesional.Facultad.Nombre;
                    var escuela = curso.Carrera.EscuelaProfesional.Nombre;

                    double notaMax = resultados.Max(r => r.Nota);
                    double notaMin = resultados.Min(r => r.Nota);
                    double notaPromedio = Math.Round(resultados.Average(r => r.Nota), 2);

                    var alumnosMax = resultados.Where(r => r.Nota == notaMax)
                        .Select(r => r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno).Distinct().ToList();
                    var alumnosMin = resultados.Where(r => r.Nota == notaMin)
                        .Select(r => r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno).Distinct().ToList();

                    // 2. Construir la estructura del Google Sheet
                    var sheetData = new List<IList<object>>();

                    // Fila 1 y 2: Encabezados Institucionales
                    sheetData.Add(new List<object> { $"UNIVERSIDAD NACIONAL DE SAN ANTONIO ABAD DEL CUSCO" });
                    sheetData.Add(new List<object> { $"FACULTAD DE {facultad.ToUpper()} - {escuela.ToUpper()}" });
                    sheetData.Add(new List<object> { $"CURSO: {curso.Nombre.ToUpper()} ({curso.Codigo})" });
                    sheetData.Add(new List<object> { $"FECHA DE EXPORTACIÓN: {DateTime.Now.ToString("dd/MM/yyyy HH:mm")}" });
                    sheetData.Add(new List<object> { "" });

                    // Filas 6-8: Estadísticas (Empates)
                    sheetData.Add(new List<object> { "--- ESTADÍSTICAS ---" });
                    sheetData.Add(new List<object> { "Nota Promedio:", notaPromedio });
                    sheetData.Add(new List<object> { "Nota Más Alta:", notaMax, "Alumnos:", string.Join(", ", alumnosMax) });
                    sheetData.Add(new List<object> { "Nota Más Baja:", notaMin, "Alumnos:", string.Join(", ", alumnosMin) });
                    sheetData.Add(new List<object> { "" });

                    // Fila 10: Títulos de Tabla
                    sheetData.Add(new List<object> { "N°", "Nombre del Alumno", "Nota", "Rendimiento", "Sección", "Versión de Examen", "Unidad" });

                    // Fila 11+: Datos
                    var dataRows = resultados.OrderByDescending(r => r.Nota).ToList();
                    for (int i = 0; i < dataRows.Count; i++)
                    {
                        var r = dataRows[i];
                        string nombre = r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno;
                        sheetData.Add(new List<object> { i + 1, nombre, r.Nota, r.EstadoRendimiento, r.Examen.Unidad.Seccion.Nombre, r.Examen.NombreVersion, r.Examen.Unidad.NombreUnidad });
                    }

                    // 3. Crear el Google Sheet nativo
                    string contextName = seccionId.HasValue ? resultados.First().Examen.Unidad.Seccion.Nombre : "General";
                    string sheetTitle = $"Reporte de Rendimiento - {curso.Nombre} ({contextName})";
                    
                    string spreadsheetId = await GoogleSheetsHelper.CreateSpreadsheetAsync(sheetTitle, accessToken);
                    if (string.IsNullOrEmpty(spreadsheetId))
                        return Json(new { success = false, message = "No se pudo crear el archivo en Google Sheets." });

                    // 4. Escribir datos y formatear
                    bool wrote = await GoogleSheetsHelper.PopulateAndFormatReportAsync(spreadsheetId, sheetData, 7, accessToken);
                    if (!wrote)
                        return Json(new { success = false, message = "El archivo se creó pero falló la inyección de datos." });

                    // 5. Mover a la carpeta correspondiente en Drive
                    string targetFolderId = null;
                    if (seccionId.HasValue)
                    {
                        // Buscar ID de carpeta de sección
                        var seccion = await db.Secciones.FindAsync(seccionId.Value);
                        if (!string.IsNullOrEmpty(seccion?.DriveFolderId))
                        {
                            targetFolderId = await GoogleDriveHelper.GetOrCreateFolderAsync("Reportes Consolidados", seccion.DriveFolderId, accessToken);
                        }
                    }

                    if (string.IsNullOrEmpty(targetFolderId))
                    {
                        // Si no hay sección o no tiene Drive, usar el Curso
                        if (!string.IsNullOrEmpty(curso.DriveFolderId))
                        {
                            targetFolderId = await GoogleDriveHelper.GetOrCreateFolderAsync("Reportes Consolidados", curso.DriveFolderId, accessToken);
                        }
                    }

                    if (!string.IsNullOrEmpty(targetFolderId))
                    {
                        // Usar MoveFolderAsync (funciona igual para archivos en API v3)
                        await GoogleDriveHelper.MoveFolderAsync(spreadsheetId, targetFolderId, accessToken);
                    }

                    return Json(new { success = true, url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error interno: " + ex.Message });
            }
        }
    }
}
