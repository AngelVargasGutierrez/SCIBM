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

                // Cargar solo las facultades donde el docente tiene cursos
                var facultades = await db.Facultades
                    .Where(f => db.Cursos.Any(cu =>
                        cu.CicloAcademico.DocenteEmail == email &&
                        cu.Carrera.EscuelaProfesional.FacultadId == f.Id))
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
                string email = Session["UserEmail"]?.ToString();
                var escuelas = await db.EscuelasProfesionales
                    .Where(e => e.FacultadId == facultadId &&
                        db.Cursos.Any(cu =>
                            cu.CicloAcademico.DocenteEmail == email &&
                            cu.Carrera.EscuelaProfesionalId == e.Id))
                    .OrderBy(e => e.Nombre)
                    .Select(e => new { e.Id, Nombre = e.Nombre })
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
                string email = Session["UserEmail"]?.ToString();
                var carreras = await db.Carreras
                    .Where(c => c.EscuelaProfesionalId == escuelaId &&
                        db.Cursos.Any(cu =>
                            cu.CicloAcademico.DocenteEmail == email &&
                            cu.CarreraId == c.Id))
                    .OrderBy(c => c.Nombre)
                    .Select(c => new { c.Id, c.Nombre })
                    .ToListAsync();

                return Json(carreras, JsonRequestBehavior.AllowGet);
            }
        }

        // AJAX: Obtener Ciclos Romano (I, II, III...) por Carrera del docente
        [HttpGet]
        public async Task<ActionResult> GetCiclosCarrera(Guid carreraId, Guid? cicloAcademicoId = null)
        {
            using (var db = new ScibmContext())
            {
                string email = Session["UserEmail"]?.ToString();
                var query = db.Cursos
                    .Where(c => c.CarreraId == carreraId &&
                        c.CicloAcademico.DocenteEmail == email);

                if (cicloAcademicoId.HasValue)
                    query = query.Where(c => c.CicloAcademicoId == cicloAcademicoId.Value);

                var ciclos = await query
                    .Select(c => c.CicloRomano)
                    .Distinct()
                    .ToListAsync();

                // Ordenar romano correctamente, limpiando la palabra 'CICLO' si existe en la BD
                var ordenRomano = new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };
                
                var resultado = ciclos
                    .Select(c => new 
                    { 
                        id = c, 
                        clean = c != null ? c.ToUpper().Replace("CICLO", "").Trim() : ""
                    })
                    .OrderBy(x => Array.IndexOf(ordenRomano, x.clean) >= 0 ? Array.IndexOf(ordenRomano, x.clean) : 99)
                    .Select(x => new { id = x.id, nombre = "Ciclo " + x.clean })
                    .ToList();

                return Json(resultado, JsonRequestBehavior.AllowGet);
            }
        }

        // AJAX: Obtener Cursos por Carrera (cicloAcademicoId y cicloRomano opcionales)
        [HttpGet]
        public async Task<ActionResult> GetCursos(Guid carreraId, Guid? cicloId = null, string cicloRomano = null)
        {
            using (var db = new ScibmContext())
            {
                string email = Session["UserEmail"]?.ToString();
                var query = db.Cursos
                    .Where(c => c.CarreraId == carreraId &&
                        c.CicloAcademico.DocenteEmail == email);

                if (cicloId.HasValue)
                    query = query.Where(c => c.CicloAcademicoId == cicloId.Value);

                if (!string.IsNullOrEmpty(cicloRomano))
                    query = query.Where(c => c.CicloRomano == cicloRomano);

                var cursos = await query
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

                // Obtener TODAS las unidades correspondientes al filtro actual
                List<string> unidadesPresentes = new List<string>();
                if (unidadId.HasValue) unidadesPresentes = db.Unidades.Where(u => u.Id == unidadId.Value).Select(u => u.NombreUnidad).ToList();
                else if (seccionId.HasValue) unidadesPresentes = db.Unidades.Where(u => u.SeccionId == seccionId.Value).Select(u => u.NombreUnidad).Distinct().OrderBy(u => u).ToList();
                else unidadesPresentes = db.Unidades.Where(u => u.Seccion.CursoId == cursoId).Select(u => u.NombreUnidad).Distinct().OrderBy(u => u).ToList();

                if (!resultados.Any())
                {
                    return Json(new
                    {
                        success = true,
                        unidades = unidadesPresentes,
                        data = new object[0],
                        stats = new { notaMax = 0.0, notaMin = 0.0, notaPromedio = 0.0, alumnosMax = "", alumnosMin = "", total = 0 }
                    }, JsonRequestBehavior.AllowGet);
                }

                var alumnosAgrupados = resultados.GroupBy(r => r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno)
                    .Select(g => {
                        var notasUnidad = new Dictionary<string, double?>();
                        foreach(var u in unidadesPresentes) {
                            var ea = g.FirstOrDefault(x => x.Examen.Unidad.NombreUnidad == u);
                            notasUnidad[u] = ea?.Nota;
                        }
                        var promedio = g.Average(x => x.Nota);
                        var estado = promedio < 10.5 ? "Desaprobado" : promedio < 14.0 ? "Regular" : "Aprobado";
                        var color = promedio < 10.5 ? "#ff5252" : promedio < 14.0 ? "#ff9f1c" : "#2ec4b6";

                        return new {
                            nombre = g.Key,
                            notasUnidad = notasUnidad,
                            promedio = Math.Round(promedio, 2),
                            estado = estado,
                            color = color
                        };
                    })
                    .OrderByDescending(x => x.promedio)
                    .ToList();

                // Calcular estadísticas sobre los promedios
                double notaMax = alumnosAgrupados.Max(r => r.promedio);
                double notaMin = alumnosAgrupados.Min(r => r.promedio);
                double notaPromedioGeneral = Math.Round(alumnosAgrupados.Average(r => r.promedio), 2);

                var alumnosMax = alumnosAgrupados.Where(r => r.promedio == notaMax).Select(r => r.nombre).ToList();
                var alumnosMin = alumnosAgrupados.Where(r => r.promedio == notaMin).Select(r => r.nombre).ToList();

                double minDiff = alumnosAgrupados.Min(r => Math.Abs(r.promedio - notaPromedioGeneral));
                var alumnosMedia = alumnosAgrupados.Where(r => Math.Abs(r.promedio - notaPromedioGeneral) == minDiff).Select(r => r.nombre).ToList();

                string colorPromedio = notaPromedioGeneral < 10.5 ? "#ff5252" : notaPromedioGeneral < 14.0 ? "#ff9f1c" : "#2ec4b6";

                return Json(new
                {
                    success = true,
                    unidades = unidadesPresentes,
                    data = alumnosAgrupados,
                    stats = new
                    {
                        notaMax,
                        notaMin,
                        notaPromedio = notaPromedioGeneral,
                        colorPromedio,
                        alumnosMax = string.Join(", ", alumnosMax),
                        alumnosMin = string.Join(", ", alumnosMin),
                        alumnosMedia = string.Join(", ", alumnosMedia),
                        total = alumnosAgrupados.Count
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

        // AJAX: Obtener Datos de Tendencia para Gráficos
        [HttpGet]
        public async Task<ActionResult> GetTendenciaAlumnos(Guid cursoId, Guid? seccionId = null)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada." }, JsonRequestBehavior.AllowGet);

            using (var db = new ScibmContext())
            {
                // Consultar todas las calificaciones relacionadas al curso (y opcionalmente sección)
                var query = db.ExamenesAlumnos
                    .Include(ea => ea.AlumnoMatriculado)
                    .Include(ea => ea.Examen.Unidad)
                    .Include(ea => ea.Examen.Unidad.Seccion)
                    .Where(ea => ea.Examen.Unidad.Seccion.CursoId == cursoId);

                if (seccionId.HasValue)
                {
                    query = query.Where(ea => ea.Examen.Unidad.SeccionId == seccionId.Value);
                }

                var resultados = await query.ToListAsync();

                if (!resultados.Any())
                {
                    return Json(new { success = true, labels = new string[0], datasets = new object[0], promedioGeneral = new double[0] }, JsonRequestBehavior.AllowGet);
                }

                // Identificar y ordenar todas las unidades únicas por nombre
                var unidades = resultados
                    .Select(r => r.Examen.Unidad)
                    .GroupBy(u => u.Id)
                    .Select(g => g.First())
                    .OrderBy(u => u.NombreUnidad) // U1, U1R, U2...
                    .ToList();

                var labels = unidades.Select(u => u.NombreUnidad).ToList();

                // Agrupar por alumno
                var alumnosGroup = resultados.GroupBy(r => r.AlumnoMatriculadoId.HasValue 
                    ? r.AlumnoMatriculado.NombreCompleto 
                    : r.NombreAlumno);

                var datasets = new List<object>();

                foreach (var ag in alumnosGroup)
                {
                    var notasPorUnidad = new List<double?>();
                    foreach (var u in unidades)
                    {
                        // Buscar si el alumno tiene nota en esta unidad
                        var examenesEnUnidad = ag.Where(r => r.Examen.UnidadId == u.Id).ToList();
                        
                        if (examenesEnUnidad.Any())
                        {
                            // Si tiene varias versiones en la misma unidad, promediamos
                            notasPorUnidad.Add(Math.Round(examenesEnUnidad.Average(e => e.Nota), 2));
                        }
                        else
                        {
                            notasPorUnidad.Add(null); // No rindió en esta unidad
                        }
                    }

                    datasets.Add(new
                    {
                        alumno = ag.Key,
                        notas = notasPorUnidad
                    });
                }

                // Calcular promedio general por unidad
                var promedioGeneral = new List<double?>();
                foreach (var u in unidades)
                {
                    var todasNotasEnUnidad = resultados.Where(r => r.Examen.UnidadId == u.Id).ToList();
                    if (todasNotasEnUnidad.Any())
                    {
                        promedioGeneral.Add(Math.Round(todasNotasEnUnidad.Average(e => e.Nota), 2));
                    }
                    else
                    {
                        promedioGeneral.Add(null);
                    }
                }

                return Json(new
                {
                    success = true,
                    labels = labels,
                    datasets = datasets,
                    promedioGeneral = promedioGeneral
                }, JsonRequestBehavior.AllowGet);
            }
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
                    var escuelaSiglas = curso.Carrera.EscuelaProfesional.Siglas ?? curso.Carrera.EscuelaProfesional.Nombre;
                    var carreraNombre = curso.Carrera.Nombre;

                    double notaMax = resultados.Max(r => r.Nota);
                    double notaMin = resultados.Min(r => r.Nota);
                    double notaPromedio = Math.Round(resultados.Average(r => r.Nota), 2);

                    var alumnosMax = resultados.Where(r => r.Nota == notaMax)
                        .Select(r => r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno).Distinct().ToList();
                    var alumnosMin = resultados.Where(r => r.Nota == notaMin)
                        .Select(r => r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno).Distinct().ToList();
                    
                    double minDiff = resultados.Min(r => Math.Abs(r.Nota - notaPromedio));
                    var alumnosMedia = resultados.Where(r => Math.Abs(r.Nota - notaPromedio) == minDiff)
                        .Select(r => r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno).Distinct().ToList();

                    // 2. Construir la estructura del Google Sheet
                    var sheetData = new List<IList<object>>();

                    // Fila 1 y 2: Encabezados Institucionales
                    sheetData.Add(new List<object> { $"UNIVERSIDAD PRIVADA DE TACNA" });
                    sheetData.Add(new List<object> { $"FACULTAD DE {facultad.ToUpper()} - {escuelaSiglas.ToUpper()} - {carreraNombre.ToUpper()}" });
                    string headerContext = $"CURSO: {curso.Nombre.ToUpper()} ({curso.Codigo})";
                    if (seccionId.HasValue) headerContext += $" - SECCIÓN: {primerR.Examen.Unidad.Seccion.Nombre.ToUpper()}";
                    if (unidadId.HasValue) headerContext += $" - UNIDAD: {primerR.Examen.Unidad.NombreUnidad.ToUpper()}";
                    if (examenId.HasValue) headerContext += $" - VERSIÓN: {primerR.Examen.NombreVersion.ToUpper()}";

                    sheetData.Add(new List<object> { headerContext });
                    sheetData.Add(new List<object> { $"FECHA DE EXPORTACIÓN: {DateTime.Now.ToString("dd/MM/yyyy HH:mm")}" });
                    sheetData.Add(new List<object> { "" });

                    // Filas 6-8: Estadísticas (Empates)
                    sheetData.Add(new List<object> { "--- ESTADÍSTICAS ---" });
                    sheetData.Add(new List<object> { "Nota Promedio:", notaPromedio, "Alumnos:", string.Join(", ", alumnosMedia) });
                    sheetData.Add(new List<object> { "Nota Más Alta:", notaMax, "Alumnos:", string.Join(", ", alumnosMax) });
                    sheetData.Add(new List<object> { "Nota Más Baja:", notaMin, "Alumnos:", string.Join(", ", alumnosMin) });
                    sheetData.Add(new List<object> { "" });

                    // Agrupar la data y pivotar por unidad (misma lógica que GetReporteDinamico)
                    List<string> unidadesPresentes = new List<string>();
                    if (unidadId.HasValue) unidadesPresentes = db.Unidades.Where(u => u.Id == unidadId.Value).Select(u => u.NombreUnidad).ToList();
                    else if (seccionId.HasValue) unidadesPresentes = db.Unidades.Where(u => u.SeccionId == seccionId.Value).Select(u => u.NombreUnidad).Distinct().OrderBy(u => u).ToList();
                    else unidadesPresentes = db.Unidades.Where(u => u.Seccion.CursoId == cursoId).Select(u => u.NombreUnidad).Distinct().OrderBy(u => u).ToList();

                    var alumnosAgrupados = resultados.GroupBy(r => r.AlumnoMatriculado != null ? r.AlumnoMatriculado.NombreCompleto : r.NombreAlumno)
                        .Select(g => {
                            var notasUnidad = new Dictionary<string, double?>();
                            foreach(var u in unidadesPresentes) {
                                var ea = g.FirstOrDefault(x => x.Examen.Unidad.NombreUnidad == u);
                                notasUnidad[u] = ea?.Nota;
                            }
                            var promedio = g.Average(x => x.Nota);
                            var estado = promedio < 10.5 ? "Desaprobado" : promedio < 14.0 ? "Regular" : "Aprobado";
                            return new {
                                nombre = g.Key,
                                notasUnidad = notasUnidad,
                                promedio = Math.Round(promedio, 2),
                                estado = estado
                            };
                        })
                        .OrderByDescending(x => x.promedio)
                        .ToList();

                    // Fila 11: Títulos de Tabla (Dinámico)
                    var titulosTabla = new List<object> { "N°", "Nombre del Alumno" };
                    foreach (var u in unidadesPresentes) titulosTabla.Add(u);
                    titulosTabla.Add("Promedio Final");
                    titulosTabla.Add("Rendimiento");
                    sheetData.Add(titulosTabla);

                    // Fila 11+: Datos
                    for (int i = 0; i < alumnosAgrupados.Count; i++)
                    {
                        var r = alumnosAgrupados[i];
                        var row = new List<object> { i + 1, r.nombre };
                        foreach(var u in unidadesPresentes) {
                            if (r.notasUnidad[u].HasValue) row.Add(r.notasUnidad[u].Value);
                            else row.Add("-");
                        }
                        row.Add(r.promedio);
                        row.Add(r.estado);
                        sheetData.Add(row);
                    }
                    
                    // Fila Final: Promedio General de la clase
                    var filaPromedioFinal = new List<object> { "", "PROMEDIO DEL CURSO:" };
                    for(int j=0; j<unidadesPresentes.Count; j++) filaPromedioFinal.Add("");
                    filaPromedioFinal.Add(notaPromedio);
                    filaPromedioFinal.Add("");
                    sheetData.Add(filaPromedioFinal);

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
    