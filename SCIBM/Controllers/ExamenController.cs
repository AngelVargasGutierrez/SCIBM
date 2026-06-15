using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SCIBM.Helpers;
using SCIBM.Models;
using SCIBM.Services;

namespace SCIBM.Controllers
{
    public class ExamenController : Controller
    {
        // Soporte para Google OAuth Access Token
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

        // POST: Examen/CreateVersion
        [HttpPost]
        public async Task<ActionResult> CreateVersion(Guid unidadId, string nombreVersion, HttpPostedFileBase pdfFile)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            if (string.IsNullOrEmpty(nombreVersion))
            {
                TempData["Error"] = "Debe ingresar un nombre de versión.";
                return RedirectToAction("Detail", "Unidad", new { id = unidadId });
            }

            if (pdfFile == null || pdfFile.ContentLength == 0)
            {
                TempData["Error"] = "Debe adjuntar una plantilla PDF obligatoriamente.";
                return RedirectToAction("Detail", "Unidad", new { id = unidadId });
            }

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                var unidad = await db.Unidades.Include(u => u.Seccion.Curso.CicloAcademico).FirstOrDefaultAsync(u => u.Id == unidadId);
                if (unidad == null || unidad.Seccion.Curso.CicloAcademico.DocenteEmail != email)
                {
                    return HttpNotFound();
                }

                try
                {
                    var nuevoExamen = new Examen
                    {
                        Id = Guid.NewGuid(),
                        UnidadId = unidadId,
                        NombreVersion = nombreVersion,
                        SincronizadoDrive = false,
                        RutaPdfOriginal = "Pendiente" // Se actualiza en un momento
                    };

                    // 1. Guardar el archivo PDF
                    string folderPath = Server.MapPath("~/App_Data/Examenes");
                    if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                    string fileExtension = Path.GetExtension(pdfFile.FileName);
                    string fileName = $"{nuevoExamen.Id}_plantilla{fileExtension}";
                    string relativePath = $"~/App_Data/Examenes/{fileName}";
                    string physicalPath = Path.Combine(folderPath, fileName);
                    pdfFile.SaveAs(physicalPath);

                    nuevoExamen.RutaPdfOriginal = relativePath;

                    db.Examenes.Add(nuevoExamen);
                    await db.SaveChangesAsync();

                    // Intentar crear la carpeta en Drive
                    string token = await GetValidAccessTokenAsync(db, email);
                    if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(unidad.DriveFolderId))
                    {
                        string folderName = "Versión: " + nombreVersion;
                        string folderId = await GoogleDriveHelper.GetOrCreateFolderAsync(token, folderName, unidad.DriveFolderId);
                        
                        if (!string.IsNullOrEmpty(folderId))
                        {
                            nuevoExamen.DriveFolderId = folderId;
                            db.Entry(nuevoExamen).State = EntityState.Modified;
                            await db.SaveChangesAsync();
                        }
                    }

                    TempData["Success"] = $"Versión '{nombreVersion}' creada con éxito.";
                    return RedirectToAction("Detail", "Examen", new { id = nuevoExamen.Id });
                }
                catch (System.Data.Entity.Validation.DbEntityValidationException ex)
                {
                    var errorMessages = ex.EntityValidationErrors
                        .SelectMany(x => x.ValidationErrors)
                        .Select(x => x.PropertyName + ": " + x.ErrorMessage);
                    string fullErrorMessage = string.Join(" | ", errorMessages);
                    TempData["Error"] = "Error de validación al crear versión: " + fullErrorMessage;
                    return RedirectToAction("Detail", "Unidad", new { id = unidadId });
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Error al crear versión: " + ex.Message;
                    return RedirectToAction("Detail", "Unidad", new { id = unidadId });
                }
            }
        }

        // GET: Examen/Detail/5
        public async Task<ActionResult> Detail(Guid? id)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            if (!id.HasValue)
            {
                return RedirectToAction("Index", "Ciclo");
            }

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes
                    .Include(e => e.Unidad)
                    .Include(e => e.Unidad.Seccion)
                    .Include(e => e.Unidad.Seccion.Curso)
                    .Include(e => e.Unidad.Seccion.Curso.CicloAcademico)
                    .Include(e => e.Preguntas)
                    .FirstOrDefaultAsync(e => e.Id == id.Value);

                if (examen == null || examen.Unidad.Seccion.Curso.CicloAcademico.DocenteEmail != email)
                {
                    return HttpNotFound();
                }

                // Cargar calificaciones de los alumnos
                var calificaciones = await db.ExamenesAlumnos
                    .Include(ea => ea.AlumnoMatriculado)
                    .Where(ea => ea.ExamenId == id)
                    .OrderByDescending(ea => ea.FechaCalificacion)
                    .ToListAsync();

                ViewBag.Calificaciones = calificaciones;
                ViewBag.TotalPreguntas = examen.Preguntas.Count;
                
                // Si TempData indica que debemos mostrar el modal de revisión IA, pasar las preguntas
                if (TempData["ShowReviewModal"] != null && (bool)TempData["ShowReviewModal"])
                {
                    ViewBag.PreguntasIA = examen.Preguntas.OrderBy(p => p.NumeroPregunta).ToList();
                }

                return View(examen);
            }
        }

        // POST: Examen/Rename
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
                    var examen = await db.Examenes.Include(e => e.Unidad.Seccion.Curso.CicloAcademico).FirstOrDefaultAsync(e => e.Id == id && e.Unidad.Seccion.Curso.CicloAcademico.DocenteEmail == email);
                    if (examen == null)
                        return Json(new { success = false, message = "Examen no encontrado o sin permisos." });

                    bool exists = await db.Examenes.AnyAsync(e => e.UnidadId == examen.UnidadId && e.NombreVersion == newName && e.Id != id);
                    if (exists)
                        return Json(new { success = false, message = "Ya existe otra versión con este nombre en esta unidad." });

                    examen.NombreVersion = newName;

                    if (!string.IsNullOrEmpty(examen.DriveFolderId))
                    {
                        string accessToken = await GetValidAccessTokenAsync(db, email);
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            await GoogleDriveHelper.RenameFolderAsync(examen.DriveFolderId, newName, accessToken);
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

        // POST: Examen/ScanTemplateWithGemini
        [HttpPost]
        public async Task<ActionResult> ScanTemplateWithGemini(Guid examenId)
        {
            if (Session["UserEmail"] == null) return Json(new { success = false, message = "No autorizado" });

            try
            {
                using (var db = new ScibmContext())
                {
                    var examen = await db.Examenes.FindAsync(examenId);
                    if (examen == null || string.IsNullOrEmpty(examen.RutaPdfOriginal))
                        return Json(new { success = false, message = "Examen o PDF no encontrado." });

                    string physicalPath = Server.MapPath(examen.RutaPdfOriginal);
                    if (!System.IO.File.Exists(physicalPath))
                        return Json(new { success = false, message = "El archivo físico PDF no se encuentra." });

                    var gemini = new GeminiApiService();
                    
                    string prompt = @"Eres un asistente de OCR avanzado para exámenes. Analiza la primera página de este examen en blanco.
Identifica todas las preguntas. Para cada pregunta, extrae:
1. 'numeroPregunta' (int): Identifica el número de la pregunta. Puede estar en números arábigos (1, 2, 3...) o romanos (I, II, III, IV...). Conviértelo siempre a un entero (ej: III -> 3).
2. 'enunciado' (texto de la pregunta).
3. 'tipo': Puede ser 'OpcionMultiple', 'VerdaderoFalso', 'RespuestaLibre' (espacios, líneas o huecos cortos para llenar palabras) o 'Desarrollo' (grandes rectángulos o áreas extensas vacías destinadas a redactar párrafos completos).
4. 'puntaje' (float): Busca SOLO DENTRO del texto de la pregunta específica (NO en el título ni encabezado del examen) si menciona puntos (ej: '(2 ptos)', '0.5 puntos'). Extrae solo ese número. REGLAS ESTRICTAS: Si la pregunta no dice sus propios puntos, asume 1.0. Si el número es mayor a 20, es un error, asume 1.0. NUNCA asocies el puntaje total del examen a una sola pregunta.
5. 'posX', 'posY', 'width', 'height': Coordenadas relativas en porcentaje (0.0 a 100.0) de la ubicación aproximada del bloque de respuestas (las letras, paréntesis o líneas). Para Desarrollo, intenta abarcar todo el espacio destinado a la respuesta.

Devuelve ÚNICAMENTE un JSON válido con esta estructura estricta:
{
  ""preguntas"": [
    {
      ""numeroPregunta"": 1,
      ""enunciado"": ""¿Qué es la célula? (2 ptos)"",
      ""tipo"": ""OpcionMultiple"",
      ""puntaje"": 2.0,
      ""posX"": 10.5, ""posY"": 20.0, ""width"": 50.0, ""height"": 15.0
    }
  ]
}";

                    string jsonResponse = await gemini.AnalyzePdfAsync(physicalPath, prompt);

                    if (jsonResponse.StartsWith("```json"))
                    {
                        jsonResponse = jsonResponse.Substring(7);
                        if (jsonResponse.EndsWith("```")) jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3);
                    }

                    return Content(jsonResponse, "application/json");
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // POST: Examen/SubirPlantillaExistente
        [HttpPost]
        public async Task<ActionResult> SubirPlantillaExistente(Guid examenId, HttpPostedFileBase pdfFile)
        {
            if (Session["UserEmail"] == null) return RedirectToAction("Login", "Auth");

            if (pdfFile == null || pdfFile.ContentLength == 0)
            {
                TempData["Error"] = "Debe subir un archivo PDF válido.";
                return RedirectToAction("Detail", new { id = examenId });
            }

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes
                    .Include(e => e.Unidad.Seccion.Curso.CicloAcademico)
                    .FirstOrDefaultAsync(e => e.Id == examenId);

                if (examen == null || examen.Unidad.Seccion.Curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                {
                    return HttpNotFound();
                }

                try
                {
                    string folderPath = Server.MapPath("~/App_Data/Examenes");
                    if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                    string fileExtension = Path.GetExtension(pdfFile.FileName);
                    string fileName = $"{examen.Id}_plantilla{fileExtension}";
                    string relativePath = $"~/App_Data/Examenes/{fileName}";
                    string physicalPath = Path.Combine(folderPath, fileName);
                    pdfFile.SaveAs(physicalPath);

                    examen.RutaPdfOriginal = relativePath;
                    examen.SincronizadoDrive = false;
                    db.Entry(examen).State = EntityState.Modified;
                    await db.SaveChangesAsync();

                    // Return to Detail directly since we removed classic grading
                    TempData["Success"] = "Plantilla subida con éxito.";
                    return RedirectToAction("Detail", new { id = examen.Id });
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Error al subir la plantilla: " + ex.Message;
                    return RedirectToAction("Detail", new { id = examenId });
                }
            }
        }

        // GET: Examen/ScanTemplate/5
        public async Task<ActionResult> ScanTemplate(Guid? id)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            if (id == null)
            {
                return RedirectToAction("Index", "Curso");
            }

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes
                    .Include(e => e.Unidad)
                    .Include(e => e.Unidad.Seccion.Curso.CicloAcademico)
                    .Include(e => e.Preguntas)
                    .Include(e => e.Preguntas.Select(p => p.SubPreguntas))
                    .FirstOrDefaultAsync(e => e.Id == id.Value);

                if (examen == null || examen.Unidad.Seccion.Curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                {
                    return HttpNotFound();
                }

                return View(examen);
            }
        }

        [HttpPost]
        public async Task<ActionResult> AnalizarSolucionarioGemini(Guid examenId, string titulo, HttpPostedFileBase pdfFile)
        {
            if (Session["UserEmail"] == null) return RedirectToAction("Login", "Auth");

            if (pdfFile == null || pdfFile.ContentLength == 0)
            {
                TempData["Error"] = "Debe subir un archivo PDF válido.";
                return RedirectToAction("Detail", "Examen", new { id = examenId });
            }

            try
            {
                // Guardar PDF temporalmente
                string tempFolder = Server.MapPath("~/App_Data/Examenes/Temp");
                if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

                string tempFileName = $"{Guid.NewGuid()}_solucionario.pdf";
                string physicalPath = Path.Combine(tempFolder, tempFileName);
                pdfFile.SaveAs(physicalPath);

                // Llamar a Gemini
                var gemini = new GeminiApiService();
                string prompt = @"Eres un calificador experto. Este es un examen ya resuelto por el profesor (solucionario).
Extrae todas las preguntas. Para cada pregunta, indica:
- NumeroPregunta (entero secuencial)
- Enunciado (texto corto de la pregunta)
- Tipo ('OpcionMultiple', 'RespuestaLibre' o 'VerdaderoFalso')
- RespuestaCorrecta (La letra correcta si es múltiple, o la respuesta escrita si es libre/VF. Se muy exacto con lo que marcó el profesor.)
- Puntaje (El valor en puntos de esta pregunta, búscalo en el texto. Si no lo indica claramente, asume 1.0)

Devuelve ÚNICAMENTE un arreglo JSON válido (sin formato markdown adicional), estrictamente con las claves: NumeroPregunta, Enunciado, Tipo, RespuestaCorrecta, Puntaje.";

                string jsonResponse = await gemini.AnalyzePdfAsync(physicalPath, prompt);

                if (jsonResponse.StartsWith("```json"))
                {
                    jsonResponse = jsonResponse.Substring(7);
                    if (jsonResponse.EndsWith("```")) jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3);
                }

                var preguntasExtraidas = JsonConvert.DeserializeObject<List<PreguntaTemp>>(jsonResponse);

                using (var db = new ScibmContext())
                {
                    var examen = await db.Examenes.Include(e => e.Preguntas).FirstOrDefaultAsync(e => e.Id == examenId);
                    
                    if (examen != null)
                    {
                        // Borrar las preguntas anteriores para reemplazarlas
                        db.Preguntas.RemoveRange(examen.Preguntas);
                        examen.RutaPdfOriginal = "/App_Data/Examenes/Temp/" + tempFileName;
                        
                        foreach (var p in preguntasExtraidas)
                        {
                            string safeTipo = string.IsNullOrEmpty(p.Tipo) ? "RespuestaLibre" : p.Tipo;
                            if (safeTipo.Length > 30) safeTipo = safeTipo.Substring(0, 30);

                            string safeRespuesta = p.RespuestaCorrecta ?? "";
                            if (safeRespuesta.Length > 150) safeRespuesta = safeRespuesta.Substring(0, 150);

                            db.Preguntas.Add(new Pregunta
                            {
                                ExamenId = examen.Id,
                                NumeroPregunta = p.NumeroPregunta,
                                Enunciado = string.IsNullOrEmpty(p.Enunciado) ? $"Pregunta {p.NumeroPregunta}" : p.Enunciado,
                                Tipo = safeTipo,
                                RespuestaCorrecta = safeRespuesta,
                                Puntaje = p.Puntaje > 0 ? p.Puntaje : 1.0
                            });
                        }
                        
                        await db.SaveChangesAsync();
                    }
                }

                TempData["Success"] = $"¡Solucionario procesado con éxito! Se extrajeron {preguntasExtraidas.Count} preguntas automáticamente. Por favor revisa y confirma los datos.";
                TempData["ShowReviewModal"] = true;
                return RedirectToAction("Detail", "Examen", new { id = examenId });
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al analizar solucionario con Gemini: " + ex.Message;
                return RedirectToAction("Detail", "Examen", new { id = examenId });
            }
        }

// POST: Examen/GuardarPlantilla
        [HttpPost]
        public async Task<ActionResult> GuardarPlantilla(Guid examenId, string preguntasJson, double stampX, double stampY, double stampWidth, double stampHeight)
        {
            if (Session["UserEmail"] == null) return Json(new { success = false, message = "No autorizado" });

            try
            {
                using (var db = new ScibmContext())
                {
                    var examen = await db.Examenes.Include(e => e.Preguntas).FirstOrDefaultAsync(e => e.Id == examenId);
                    if (examen == null) return Json(new { success = false, message = "Examen no encontrado" });

                    examen.StampX = stampX;
                    examen.StampY = stampY;
                    examen.StampWidth = stampWidth;
                    examen.StampHeight = stampHeight;

                    // Borrar preguntas anteriores
                    db.Preguntas.RemoveRange(examen.Preguntas);
                    await db.SaveChangesAsync();

                    var preguntasFront = JsonConvert.DeserializeObject<List<PreguntaTemp>>(preguntasJson);

                    foreach(var pFront in preguntasFront)
                    {
                        string safeTipo = string.IsNullOrEmpty(pFront.Tipo) ? "OpcionMultiple" : pFront.Tipo;
                        if (safeTipo.Length > 30) safeTipo = safeTipo.Substring(0, 30);

                        string safeRespuesta = string.IsNullOrWhiteSpace(pFront.RespuestaCorrecta) ? "-" : pFront.RespuestaCorrecta;
                        if (safeRespuesta.Length > 150) safeRespuesta = safeRespuesta.Substring(0, 150);

                        var pPadre = new Pregunta
                        {
                            Id = Guid.NewGuid(),
                            ExamenId = examen.Id,
                            PreguntaPadreId = null,
                            Inciso = null,
                            NumeroPregunta = pFront.NumeroPregunta,
                            Pagina = pFront.Pagina > 0 ? pFront.Pagina : 1,
                            Enunciado = string.IsNullOrEmpty(pFront.Enunciado) ? $"Pregunta {pFront.NumeroPregunta}" : pFront.Enunciado,
                            Tipo = safeTipo,
                            RespuestaCorrecta = safeRespuesta,
                            Puntaje = pFront.Puntaje > 0 ? pFront.Puntaje : 0.0,
                            PosX = pFront.PosX,
                            PosY = pFront.PosY,
                            Width = pFront.Width,
                            Height = pFront.Height,
                            OpcionesJson = pFront.OpcionesJson
                        };

                        db.Preguntas.Add(pPadre);

                        var opciones = string.IsNullOrEmpty(pFront.OpcionesJson) ? new List<OpcionTemp>() : JsonConvert.DeserializeObject<List<OpcionTemp>>(pFront.OpcionesJson);
                        bool isInciso = (pFront.Tipo == "RespuestaLibre" || pFront.Tipo == "VerdaderoFalso") && opciones.Count > 1;

                        if (isInciso && opciones.Count > 0)
                        {
                            try {
                                if (opciones != null && opciones.Count > 1) 
                                {
                                    double puntajeHijo = pFront.Puntaje;
                                    pPadre.Puntaje = 0; // Contenedor padre no suma para no duplicar

                                    foreach (var opt in opciones)
                                    {
                                        var pHija = new Pregunta
                                        {
                                            Id = Guid.NewGuid(),
                                            ExamenId = examen.Id,
                                            PreguntaPadreId = pPadre.Id,
                                            Inciso = opt.label,
                                            NumeroPregunta = pFront.NumeroPregunta,
                                            Pagina = pFront.Pagina > 0 ? pFront.Pagina : 1,
                                            Enunciado = $"{pPadre.Enunciado} - Inciso {opt.label}",
                                            Tipo = pFront.Tipo,
                                            RespuestaCorrecta = string.IsNullOrWhiteSpace(opt.val) ? "-" : (opt.val.Length > 150 ? opt.val.Substring(0,150) : opt.val),
                                            Puntaje = puntajeHijo,
                                            PosX = opt.x,
                                            PosY = opt.y,
                                            Width = opt.w,
                                            Height = opt.h,
                                            OpcionesJson = "[]"
                                        };
                                        db.Preguntas.Add(pHija);
                                    }
                                }
                            } catch {}
                        }
                    }

                    await db.SaveChangesAsync();
                    TempData["Success"] = "¡Plantilla de examen y coordenadas guardadas exitosamente!";
                    return Json(new { success = true, redirectUrl = Url.Action("Detail", "Examen", new { id = examenId }) });
                }
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException ex)
            {
                var errorMessages = ex.EntityValidationErrors
                    .SelectMany(x => x.ValidationErrors)
                    .Select(x => x.PropertyName + ": " + x.ErrorMessage);
                string fullErrorMessage = string.Join(" | ", errorMessages);
                return Json(new { success = false, message = "Error de validación: " + fullErrorMessage });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

// POST: Examen/ConfirmarSolucionario
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ConfirmarSolucionario(Guid examenId, List<Pregunta> preguntasEditadas)
        {
            if (preguntasEditadas == null || !preguntasEditadas.Any())
            {
                TempData["Error"] = "No se recibieron preguntas para actualizar.";
                return RedirectToAction("Detail", "Examen", new { id = examenId });
            }

            try
            {
                using (var db = new ScibmContext())
                {
                    for (int i = 0; i < preguntasEditadas.Count; i++)
                    {
                        var pEdit = preguntasEditadas[i];
                        var pDb = await db.Preguntas.FirstOrDefaultAsync(p => p.Id == pEdit.Id);
                        if (pDb != null)
                        {
                            pDb.Enunciado = pEdit.Enunciado;
                            pDb.Tipo = pEdit.Tipo;
                            pDb.RespuestaCorrecta = pEdit.RespuestaCorrecta;
                            
                            // Recuperar puntaje original del Request para saltar el problema de la coma decimal
                            var rawPuntaje = Request.Form[$"preguntasEditadas[{i}].Puntaje"];
                            if (!string.IsNullOrEmpty(rawPuntaje))
                            {
                                rawPuntaje = rawPuntaje.Replace(",", "."); // Normalizar
                                double puntajeParseado;
                                if (double.TryParse(rawPuntaje, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out puntajeParseado))
                                {
                                    pDb.Puntaje = puntajeParseado;
                                }
                                else
                                {
                                    pDb.Puntaje = pEdit.Puntaje > 0 ? pEdit.Puntaje : 1.0;
                                }
                            }
                            else 
                            {
                                pDb.Puntaje = pEdit.Puntaje > 0 ? pEdit.Puntaje : 1.0;
                            }
                        }
                    }
                    await db.SaveChangesAsync();
                }
                TempData["Success"] = "¡Solucionario guardado y confirmado correctamente!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al guardar los cambios: " + ex.Message;
            }

            return RedirectToAction("Detail", "Examen", new { id = examenId });
        }

        [HttpPost]
        public async Task<ActionResult> CalificarAlumnosGemini(Guid examenId, List<HttpPostedFileBase> archivosAlumnos)
        {
            if (Session["UserEmail"] == null) return RedirectToAction("Login", "Auth");

            if (archivosAlumnos == null || archivosAlumnos.Count == 0 || archivosAlumnos.First() == null)
            {
                TempData["Error"] = "Debe subir al menos un examen de alumno en formato PDF.";
                return RedirectToAction("Detail", "Unidad", new { id = examenId }); // El examenId es igual al unidadId
            }

            try
            {
                using (var db = new ScibmContext())
                {
                    var examen = await db.Examenes
                        .Include(e => e.Preguntas)
                        .FirstOrDefaultAsync(e => e.Id == examenId);

                    if (examen == null || !examen.Preguntas.Any())
                    {
                        TempData["Error"] = "No se encontró el examen o no tiene preguntas registradas desde el solucionario.";
                        return RedirectToAction("Detail", "Unidad", new { id = examenId });
                    }

                    string tempFolder = Server.MapPath("~/App_Data/Examenes/Alumnos");
                    if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

                    var gemini = new GeminiApiService();
                    var tempFiles = new List<string>();
                    var resultadosJS = new List<object>();

                    // Construir el texto de las preguntas esperadas (jerárquico)
                    var preguntasRaiz = examen.Preguntas
                        .Where(p => p.PreguntaPadreId == null)
                        .OrderBy(p => p.NumeroPregunta).ToList();

                    var sb = new System.Text.StringBuilder();
                    foreach (var padre in preguntasRaiz)
                    {
                        var hijos = examen.Preguntas
                            .Where(h => h.PreguntaPadreId == padre.Id)
                            .OrderBy(h => h.Inciso).ToList();

                        if (hijos.Any())
                        {
                            sb.AppendLine($"PREGUNTA {padre.NumeroPregunta}: \"{padre.Enunciado}\" — TIENE {hijos.Count} INCISOS:");
                            foreach (var hijo in hijos)
                            {
                                sb.AppendLine($"  → Inciso \"{hijo.Inciso}\": Tipo={hijo.Tipo}, RespuestaCorrecta=\"{hijo.RespuestaCorrecta}\", Puntaje={hijo.Puntaje}");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"PREGUNTA {padre.NumeroPregunta}: \"{padre.Enunciado}\" — Tipo={padre.Tipo}, RespuestaCorrecta=\"{padre.RespuestaCorrecta}\", Puntaje={padre.Puntaje}");
                        }
                    }
                    var preguntasInfo = sb.ToString();

                    foreach (var file in archivosAlumnos)
                    {
                        if (file != null && file.ContentLength > 0)
                        {
                            string tempFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                            string physicalPath = Path.Combine(tempFolder, tempFileName);
                            file.SaveAs(physicalPath);
                            
                            string relativePath = "/App_Data/Examenes/Alumnos/" + tempFileName;
                            tempFiles.Add(relativePath);

                            // Preparar prompt con contexto detallado
                            string prompt = $@"Eres un sistema de OCR especializado en exámenes universitarios escaneados. 
Tu ÚNICA función es LEER y TRANSCRIBIR lo que el estudiante escribió o marcó en su hoja de examen.
NO eres un calificador. NO juzgues si las respuestas son correctas o incorrectas.

═══════════════════════════════════════════
  REGLA CRÍTICA: ANTI-CONTAMINACIÓN
═══════════════════════════════════════════
Más abajo te proporcionaré la plantilla con las respuestas correctas SOLO como referencia 
de estructura para que entiendas cuántas preguntas hay, de qué tipo son y cuántos incisos tiene cada una.

ESTÁ TERMINANTEMENTE PROHIBIDO que copies, sustituyas o te inspires en las respuestas correctas 
de la plantilla. Si el alumno escribió ""B"" y la correcta es ""A"", DEBES devolver ""B"".
Si no puedes leer lo que escribió, devuelve cadena vacía """".
JAMÁS inventes ni asumas una respuesta.

═══════════════════════════════════════════
  PASO 1: NOMBRE DEL ALUMNO
═══════════════════════════════════════════
Busca en la CABECERA del documento (parte superior, primeras líneas) un campo etiquetado como:
""Nombre:"", ""Nombre y Apellido:"", ""Alumno:"", ""Estudiante:"", ""Apellidos y Nombres:"" 
o cualquier variante similar.

Extrae TEXTUALMENTE lo que está escrito (a mano o impreso) DESPUÉS de esa etiqueta.
- Si hay apellidos y nombres separados por coma, mantenlos tal cual aparecen.
- Si el campo está vacío o completamente ilegible, devuelve ""Ilegible"".
- NO inventes un nombre. Solo transcribe lo que ves.

═══════════════════════════════════════════
  PASO 2: MAPEO DE NUMERACIÓN
═══════════════════════════════════════════
Las preguntas en el PDF pueden estar numeradas de CUALQUIERA de estas formas:
  Arábigos: 1, 2, 3 | 1., 2., 3. | 1), 2), 3) | 1.-, 2.-, 3.-
  Romanos:  I, II, III, IV, V | I., II., III. | I), II)
  Letras:   A, B, C (como secciones)

DEBES mapear SIEMPRE la numeración del PDF al número arábigo (entero) 
que corresponde según la plantilla que te doy abajo.
Ejemplo: ""III"" en el PDF = Pregunta 3 en mi plantilla.

Los INCISOS o sub-ítems dentro de una pregunta pueden aparecer como:
  a), b), c) | a., b., c. | (a), (b) | A), B), C) | a-, b-, c-

NORMALIZA siempre cada inciso a su LETRA MINÚSCULA sin puntuación: a, b, c, d, e...

═══════════════════════════════════════════
  REGLA CRÍTICA: DESAMBIGUACIÓN INCISO vs OPCIÓN
═══════════════════════════════════════════
En un examen, las LETRAS pueden cumplir DOS roles completamente distintos. 
NO los confundas:

▸ INCISO (sub-pregunta): Es una SUBDIVISIÓN de una pregunta mayor.
  Ejemplo: la Pregunta 1 tiene los incisos a), b), c). 
  Cada inciso es una pregunta independiente con su propia respuesta.
  → Va en el campo ""Inciso"" del JSON (""a"", ""b"", ""c"").

▸ OPCIÓN DE RESPUESTA: Es una ALTERNATIVA que el alumno puede elegir/marcar
  como su respuesta a una pregunta o inciso.
  Ejemplo: A) Lima  B) Cusco  C) Arequipa. El alumno marca una.
  → Va en el campo ""RespuestaDada"" del JSON (""A"", ""B"", ""C"").

AMBOS pueden usar letras mayúsculas o minúsculas. La diferencia NO es el formato,
sino su FUNCIÓN en el examen:
  - ¿Es un sub-ítem que CONTIENE una pregunta? → Es un INCISO.
  - ¿Es una alternativa que el alumno ELIGE como respuesta? → Es una OPCIÓN.

Ejemplo concreto de un examen:
  1. Marque la respuesta correcta en cada inciso:
     a) ¿Capital de Perú?      ← INCISO ""a""
        A) Lima  B) Cusco       ← OPCIONES de respuesta
     b) ¿Capital de Chile?     ← INCISO ""b""
        A) Valparaíso  B) Santiago  ← OPCIONES de respuesta

Si el alumno marcó A en el inciso a) y B en el inciso b), el JSON sería:
  {{ ""NumeroPregunta"": 1, ""Inciso"": ""a"", ""RespuestaDada"": ""A"" }}
  {{ ""NumeroPregunta"": 1, ""Inciso"": ""b"", ""RespuestaDada"": ""B"" }}

USA LA PLANTILLA que te doy abajo para saber cuáles preguntas TIENEN incisos 
y cuáles NO. La plantilla es tu guía de estructura.

═══════════════════════════════════════════
  PASO 3: EXTRACCIÓN DE RESPUESTAS
═══════════════════════════════════════════
Analiza TODAS las páginas del PDF de principio a fin.

Aquí está la plantilla de preguntas esperada (SOLO referencia de estructura):
{preguntasInfo}

Para CADA pregunta y CADA inciso, extrae lo que el alumno respondió según estas reglas:

▸ OPCIÓN MÚLTIPLE (letras A, B, C, D, E):
  - Identifica cuál letra fue MARCADA: círculo, relleno, X, check, subrayado 
    o cualquier marca visible sobre o junto a la letra.
  - Si el alumno TACHÓ una opción y luego MARCÓ otra diferente, 
    toma la que NO está tachada (es la corrección del alumno).
  - Si hay dos marcas sin tachar y no puedes distinguir cuál es la definitiva, 
    elige la que tenga el trazo más intenso o claro.
  - Si no marcó ninguna opción, devuelve """".
  - Devuelve SOLO la letra en MAYÚSCULA: ""A"", ""B"", ""C"", ""D"" o ""E"".

▸ VERDADERO / FALSO:
  El alumno puede expresar su respuesta de muchas formas:
  - Escribir: V, F, Verdadero, Falso, Verd., Fals.
  - Escribir: SI, NO, S, N, Sí
  - Marcar: ✓, ✗, X sobre la V o sobre la F
  - Circular o subrayar la palabra Verdadero o Falso
  
  NORMALIZA siempre según esta tabla:
    V, Verdadero, Verd., SI, S, Sí, ✓  →  devuelve ""V""
    F, Falso, Fals., NO, N, ✗           →  devuelve ""F""
  Si está en blanco o ilegible, devuelve """".

▸ RESPUESTA LIBRE / COMPLETAR / RELLENAR ESPACIO:
  - Si hay una línea en blanco (______) dentro de una oración impresa, 
    extrae SOLAMENTE lo que el alumno ESCRIBIÓ A MANO en ese espacio.
    IGNORA el texto impreso que rodea el espacio.
  - Si es un campo abierto (varias líneas para responder), 
    transcribe todo el texto manuscrito del alumno fielmente.
  - NO corrijas ortografía, gramática ni puntuación del alumno.
  - NO sustituyas la respuesta del alumno por la respuesta correcta de la plantilla.
  - Si el espacio está completamente vacío, devuelve """".

▸ PREGUNTAS CON INCISOS / SUB-ÍTEMS:
  - Si una pregunta tiene sub-ítems (a, b, c, d...), genera un objeto JSON 
    SEPARADO por CADA inciso, con su campo ""Inciso"" correspondiente.
  - NUNCA agrupes múltiples incisos en una sola respuesta.
  - Cada inciso es independiente y debe tener su propio objeto en el arreglo.

═══════════════════════════════════════════
  PASO 4: FORMATO DE SALIDA
═══════════════════════════════════════════
Devuelve ÚNICAMENTE un JSON válido con esta estructura exacta:
{{
  ""NombreAlumno"": ""TEXTO EXACTO leído después de la etiqueta Nombre"",
  ""Respuestas"": [
    {{ ""NumeroPregunta"": 1, ""Inciso"": ""a"", ""RespuestaDada"": ""V"" }},
    {{ ""NumeroPregunta"": 1, ""Inciso"": ""b"", ""RespuestaDada"": ""F"" }},
    {{ ""NumeroPregunta"": 2, ""Inciso"": null, ""RespuestaDada"": ""B"" }},
    {{ ""NumeroPregunta"": 3, ""Inciso"": null, ""RespuestaDada"": ""funcional"" }}
  ]
}}

Reglas del JSON:
- ""NumeroPregunta"": siempre número arábigo entero (1, 2, 3...), nunca romanos.
- ""Inciso"": null si la pregunta NO tiene sub-ítems. Letra minúscula (""a"", ""b"", ""c""...) si SÍ tiene.
- ""RespuestaDada"": EXACTAMENTE lo que el alumno escribió o marcó. NUNCA la respuesta correcta de la plantilla.
- CRÍTICO: NUNCA uses comillas dobles ("") DENTRO de los valores de texto. Si el alumno escribió comillas, cámbialas a comillas simples ('') para evitar romper el formato JSON (Unterminated string).
- CRÍTICO: NUNCA uses saltos de línea literales (Enter) dentro de los valores de texto. Si la respuesta tiene varias líneas, usa el texto literal '\\n' o simplemente únelo en una sola línea con espacios.
- NO incluyas campos adicionales. NO incluyas explicaciones. SOLO el JSON.";

                            try
                            {
                                string jsonResponse = await gemini.AnalyzePdfAsync(physicalPath, prompt);

                                // Limpiar posibles bloques de markdown que devuelve Gemini
                                jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();

                                var resultadoAlumno = JsonConvert.DeserializeObject<ResultadoAlumnoGemini>(jsonResponse);

                                // Validar que la deserialización fue exitosa y tiene respuestas
                                if (resultadoAlumno == null || resultadoAlumno.Respuestas == null)
                                {
                                    resultadosJS.Add(new {
                                        tempFilePath = relativePath,
                                        fileName = Path.GetFileName(file.FileName),
                                        nombreAlumnoRaw = "Error de lectura IA",
                                        alumnoMatriculadoId = (string)null,
                                        tieneObservacion = true,
                                        observacion = "La IA no pudo interpretar el PDF correctamente. Respuesta vacía o malformada.",
                                        respuestas = new List<object>()
                                    });
                                    continue;
                                }

                                // Adaptar al formato que espera GradeOverview.cshtml
                                var respuestasFormatoJS = new List<object>();
                                foreach (var r in resultadoAlumno.Respuestas)
                                {
                                    respuestasFormatoJS.Add(new {
                                        numeroPregunta = r.NumeroPregunta,
                                        inciso = r.Inciso,
                                        respuestaDada = r.RespuestaDada ?? ""
                                    });
                                }

                                resultadosJS.Add(new {
                                    tempFilePath = relativePath,
                                    fileName = Path.GetFileName(file.FileName),
                                    nombreAlumnoRaw = string.IsNullOrEmpty(resultadoAlumno.NombreAlumno) ? "Desconocido" : resultadoAlumno.NombreAlumno,
                                    alumnoMatriculadoId = (string)null, // Se asociará en el frontend
                                    tieneObservacion = false,
                                    observacion = "",
                                    respuestas = respuestasFormatoJS
                                });
                            }
                            catch (Exception pdfEx)
                            {
                                // Un PDF individual falló, pero no abortamos el lote completo
                                resultadosJS.Add(new {
                                    tempFilePath = relativePath,
                                    fileName = Path.GetFileName(file.FileName),
                                    nombreAlumnoRaw = "Error de lectura",
                                    alumnoMatriculadoId = (string)null,
                                    tieneObservacion = true,
                                    observacion = $"Error al procesar este PDF: {pdfEx.Message}",
                                    respuestas = new List<object>()
                                });
                            }
                        }
                    }

                    // Pasar a sesión para la vista de revisión
                    Session["TempCalificacionesFiles"] = tempFiles;
                    Session["AIGradingResults"] = JsonConvert.SerializeObject(resultadosJS);
                    TempData["Success"] = $"¡Se procesaron exitosamente {resultadosJS.Count} exámenes con Inteligencia Artificial! Revisa y guarda las calificaciones.";
                }

                return RedirectToAction("GradeOverview", new { examenId = examenId });
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error durante la calificación con Gemini: " + ex.Message;
                return RedirectToAction("Detail", "Unidad", new { id = examenId });
            }
        }

        // GET: Examen/GradeOverview
        public async Task<ActionResult> GradeOverview(Guid examenId)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            var tempFiles = Session["TempCalificacionesFiles"] as List<string>;
            if (tempFiles == null || tempFiles.Count == 0)
            {
                return RedirectToAction("Detail", "Unidad", new { id = examenId });
            }

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes
                    .Include(e => e.Unidad)
                    .Include(e => e.Preguntas)
                    .FirstOrDefaultAsync(e => e.Id == examenId);

                if (examen == null) return HttpNotFound();

                // Cargar alumnos matriculados de la sección para el dropdown manual y autocompletado difuso
                var alumnos = await db.AlumnosMatriculados
                    .Where(a => a.SeccionId == examen.Unidad.SeccionId)
                    .OrderBy(a => a.NombreCompleto)
                    .ToListAsync();

                ViewBag.AlumnosMatriculados = alumnos;
                ViewBag.TempFiles = tempFiles;

                return View(examen);
            }
        }

        // POST: Examen/GuardarNotas (AJAX)
        [HttpPost]
        public async Task<ActionResult> GuardarNotas(Guid examenId, string resultadosJson)
        {
            if (Session["UserEmail"] == null)
            {
                return Json(new { success = false, message = "Sesión expirada" });
            }

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes.Include(e => e.Preguntas).FirstOrDefaultAsync(e => e.Id == examenId);
                if (examen == null) return Json(new { success = false, message = "Examen no encontrado" });

                try
                {
                    var resultados = JsonConvert.DeserializeObject<List<ResultadoAlumnoTemp>>(resultadosJson);
                    string finalFolder = Server.MapPath("~/App_Data/Examenes/Calificados");
                    if (!Directory.Exists(finalFolder))
                    {
                        Directory.CreateDirectory(finalFolder);
                    }

                    foreach (var res in resultados)
                    {
                        string tempPhysicalPath = Server.MapPath(res.TempFilePath);
                        if (!System.IO.File.Exists(tempPhysicalPath)) continue;

                        // 1. Guardar de forma definitiva el examen del alumno
                        string safeOriginalName = Path.GetFileNameWithoutExtension(res.TempFilePath);
                        if (safeOriginalName.Length > 50) safeOriginalName = safeOriginalName.Substring(0, 50);
                        string finalFileName = $"{Guid.NewGuid()}_{safeOriginalName}.pdf";
                        string finalRelativePath = $"~/App_Data/Examenes/Calificados/{finalFileName}";
                        string finalPhysicalPath = Path.Combine(finalFolder, finalFileName);

                        // 2. Calcular la nota final en base a aciertos
                        double notaFinal = 0;
                        var respuestasDb = new List<RespuestaAlumno>();

                        foreach (var resp in res.Respuestas)
                        {
                            // Buscar por clave compuesta: NumeroPregunta + Inciso
                            var pregunta = examen.Preguntas.FirstOrDefault(p =>
                                p.NumeroPregunta == resp.NumeroPregunta &&
                                (string.IsNullOrEmpty(resp.Inciso)
                                    // Si no tiene inciso, buscar la pregunta simple (no padre de hijos)
                                    ? p.PreguntaPadreId == null && !examen.Preguntas.Any(h => h.PreguntaPadreId == p.Id)
                                    // Si tiene inciso, buscar la hija con ese inciso exacto
                                    : p.Inciso == resp.Inciso)
                            );
                            
                            bool esCorrecta = false;
                            
                            if (pregunta != null)
                            {
                                esCorrecta = string.Equals(pregunta.RespuestaCorrecta, resp.RespuestaDada, StringComparison.OrdinalIgnoreCase);
                                if (resp.EsCorrectoManual.HasValue && resp.EsCorrectoManual.Value)
                                {
                                    esCorrecta = true; // Override manual del profesor
                                }
                                
                                if (esCorrecta)
                                {
                                    notaFinal += pregunta.Puntaje;
                                }
                            }

                            string safeRespuesta = resp.RespuestaDada;
                            if (string.IsNullOrWhiteSpace(safeRespuesta)) safeRespuesta = "-";
                            if (safeRespuesta.Length > 150) safeRespuesta = safeRespuesta.Substring(0, 150);

                            respuestasDb.Add(new RespuestaAlumno
                            {
                                NumeroPregunta = resp.NumeroPregunta,
                                Inciso = resp.Inciso,
                                RespuestaDada = safeRespuesta,
                                EsCorrecta = esCorrecta
                            });
                        }

                        // 3. Preparar datos de estampado de correcciones
                        var correctionData = new List<PdfStamperHelper.CorrectionStampData>();
                        int qIndex = 0;
                        foreach (var resp in res.Respuestas)
                        {
                            var pregunta = examen.Preguntas.FirstOrDefault(p =>
                                p.NumeroPregunta == resp.NumeroPregunta &&
                                (string.IsNullOrEmpty(resp.Inciso) ? p.PreguntaPadreId == null && !examen.Preguntas.Any(h => h.PreguntaPadreId == p.Id) : p.Inciso == resp.Inciso)
                            );

                            if (pregunta != null)
                            {
                                bool isCorrect = string.Equals(pregunta.RespuestaCorrecta, resp.RespuestaDada, StringComparison.OrdinalIgnoreCase);
                                if (resp.EsCorrectoManual.HasValue && resp.EsCorrectoManual.Value) isCorrect = true;

                                double cx = pregunta.PosX;
                                double cy = pregunta.PosY;

                                // Si el profesor ajustó las coordenadas, las usamos
                                if (res.CorrectionStamps != null && res.CorrectionStamps.ContainsKey(qIndex.ToString()))
                                {
                                    cx = res.CorrectionStamps[qIndex.ToString()].x;
                                    cy = res.CorrectionStamps[qIndex.ToString()].y;
                                }

                                correctionData.Add(new PdfStamperHelper.CorrectionStampData
                                {
                                    X = cx,
                                    Y = cy,
                                    Page = pregunta.Pagina,
                                    IsCorrect = isCorrect,
                                    CorrectAnswerText = pregunta.RespuestaCorrecta
                                });
                            }
                            qIndex++;
                        }

                        // 4. Estampar la nota final y las correcciones en el PDF
                        string notaTexto = notaFinal.ToString("0.0");
                        PdfStamperHelper.StampGradeAndCorrections(tempPhysicalPath, finalPhysicalPath, notaTexto, res.Stamp, correctionData);

                        // 4. Guardar registros en base de datos (Evitar duplicados)
                        ExamenAlumno examenAlumno = null;
                        
                        if (res.AlumnoMatriculadoId.HasValue)
                        {
                            examenAlumno = await db.ExamenesAlumnos.FirstOrDefaultAsync(ea => ea.ExamenId == examenId && ea.AlumnoMatriculadoId == res.AlumnoMatriculadoId);
                        }
                        else
                        {
                            examenAlumno = await db.ExamenesAlumnos.FirstOrDefaultAsync(ea => ea.ExamenId == examenId && ea.NombreAlumno == res.NombreAlumnoRaw && ea.AlumnoMatriculadoId == null);
                        }

                        if (examenAlumno != null)
                        {
                            // Actualizar existente
                            examenAlumno.Nota = notaFinal;
                            examenAlumno.RutaPdfRespuesta = finalRelativePath;
                            examenAlumno.TieneObservacion = res.TieneObservacion;
                            examenAlumno.Observacion = res.Observacion;
                            examenAlumno.SincronizadoDrive = false; // Marcar para re-sincronizar
                            
                            // Borrar respuestas anteriores
                            var oldAnswers = db.RespuestasAlumnos.Where(r => r.ExamenAlumnoId == examenAlumno.Id);
                            db.RespuestasAlumnos.RemoveRange(oldAnswers);
                            db.Entry(examenAlumno).State = EntityState.Modified;
                        }
                        else
                        {
                            // Crear nuevo
                            examenAlumno = new ExamenAlumno
                            {
                                ExamenId = examenId,
                                NombreAlumno = res.NombreAlumnoRaw,
                                AlumnoMatriculadoId = res.AlumnoMatriculadoId,
                                Nota = notaFinal,
                                RutaPdfRespuesta = finalRelativePath,
                                TieneObservacion = res.TieneObservacion,
                                Observacion = res.Observacion,
                                SincronizadoDrive = false
                            };
                            db.ExamenesAlumnos.Add(examenAlumno);
                        }
                        
                        await db.SaveChangesAsync(); // Generar ID para asociar respuestas o aplicar borrado

                        foreach (var r in respuestasDb)
                        {
                            r.ExamenAlumnoId = examenAlumno.Id;
                            db.RespuestasAlumnos.Add(r);
                        }

                        // Eliminar archivo temporal
                        System.IO.File.Delete(tempPhysicalPath);
                    }

                    await db.SaveChangesAsync();
                    Session["TempCalificacionesFiles"] = null; // Limpiar temporales de la sesión
                    Session["AIGradingResults"] = null; // Limpiar resultados de IA de la sesión

                    return Json(new { success = true });
                }
                catch (System.Data.Entity.Validation.DbEntityValidationException ex)
                {
                    var errorMessages = ex.EntityValidationErrors
                        .SelectMany(x => x.ValidationErrors)
                        .Select(x => x.ErrorMessage);
                    string fullErrorMessage = string.Join("; ", errorMessages);
                    return Json(new { success = false, message = "Error de validación: " + fullErrorMessage });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al guardar calificaciones: " + ex.Message });
                }
            }
        }

        // GET: Examen/ViewPdf/5
        [HttpGet]
        public async Task<ActionResult> ViewPdf(Guid id)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            using (var db = new ScibmContext())
            {
                var calificacion = await db.ExamenesAlumnos.FindAsync(id);
                if (calificacion == null || string.IsNullOrEmpty(calificacion.RutaPdfRespuesta))
                {
                    return HttpNotFound("El PDF no se encuentra disponible.");
                }

                string physicalPath = Server.MapPath(calificacion.RutaPdfRespuesta);
                if (!System.IO.File.Exists(physicalPath))
                {
                    return HttpNotFound("El archivo físico no se encontró en el servidor.");
                }

                return File(physicalPath, "application/pdf");
            }
        }

        // POST: Examen/DeleteCalificacion
        [HttpPost]
        public async Task<ActionResult> DeleteCalificacion(Guid calificacionId)
        {
            if (Session["UserEmail"] == null)
            {
                return Json(new { success = false, message = "Sesión expirada" });
            }

            using (var db = new ScibmContext())
            {
                try
                {
                    var calificacion = await db.ExamenesAlumnos.FindAsync(calificacionId);
                    if (calificacion == null)
                    {
                        return Json(new { success = false, message = "La calificación no existe." });
                    }

                    // Borrar el PDF si existe localmente
                    if (!string.IsNullOrEmpty(calificacion.RutaPdfRespuesta))
                    {
                        string physicalPath = Server.MapPath(calificacion.RutaPdfRespuesta);
                        if (System.IO.File.Exists(physicalPath))
                        {
                            System.IO.File.Delete(physicalPath);
                        }
                    }

                    // Las respuestas hijas deberían eliminarse automáticamente por Cascade Delete en EF,
                    // pero podemos forzarlo por seguridad:
                    var respuestas = db.RespuestasAlumnos.Where(r => r.ExamenAlumnoId == calificacionId);
                    db.RespuestasAlumnos.RemoveRange(respuestas);

                    db.ExamenesAlumnos.Remove(calificacion);
                    await db.SaveChangesAsync();

                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al eliminar: " + ex.Message });
                }
            }
        }

        // POST: Examen/SincronizarExamenConDrive
        [HttpPost]
        public async Task<ActionResult> SincronizarExamenConDrive(Guid examenId)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes
                    .Include(e => e.Preguntas)
                    .Include(e => e.Unidad)
                    .Include(e => e.Unidad.Seccion.Curso)
                    .FirstOrDefaultAsync(e => e.Id == examenId);

                if (examen == null || examen.Unidad.Seccion.Curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                {
                    return HttpNotFound();
                }

                try
                {
                    string accessToken = await GetValidAccessTokenAsync(db, Session["UserEmail"].ToString());
                    string unitFolderId = examen.Unidad.DriveFolderId;

                    if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(unitFolderId))
                    {
                        TempData["Error"] = "No se pudo establecer conexión con Google Drive.";
                        return RedirectToAction("Detail", "Unidad", new { id = examen.UnidadId });
                    }

                    // 1. Subir plantilla en blanco
                    if (string.IsNullOrEmpty(examen.DriveFileIdBlanco))
                    {
                        string localPath = Server.MapPath(examen.RutaPdfOriginal);
                        if (System.IO.File.Exists(localPath))
                        {
                            string fileId = await GoogleDriveHelper.UploadPdfFileAsync("Plantilla_Examen_Blanco.pdf", localPath, unitFolderId, accessToken);
                            examen.DriveFileIdBlanco = fileId;
                        }
                    }

                    // 2. Subir solucionario
                    if (string.IsNullOrEmpty(examen.DriveFileIdSolucionario))
                    {
                        string localSolucionarioPath = Server.MapPath($"~/App_Data/Examenes/{examenId}_solucionario.pdf");
                        if (System.IO.File.Exists(localSolucionarioPath))
                        {
                            string fileId = await GoogleDriveHelper.UploadPdfFileAsync("Plantilla_Examen_Solucionario.pdf", localSolucionarioPath, unitFolderId, accessToken);
                            examen.DriveFileIdSolucionario = fileId;
                        }
                    }

                    examen.SincronizadoDrive = true;
                    db.Entry(examen).State = EntityState.Modified;

                    // 3. Subir exámenes calificados de alumnos renombrados
                    var alumnosCalificados = await db.ExamenesAlumnos
                        .Include(ea => ea.AlumnoMatriculado)
                        .Where(ea => ea.ExamenId == examenId && !ea.SincronizadoDrive)
                        .ToListAsync();

                    int syncedCount = 0;
                    foreach (var al in alumnosCalificados)
                    {
                        string localPath = Server.MapPath(al.RutaPdfRespuesta);
                        if (System.IO.File.Exists(localPath))
                        {
                            // Determinar nombre del alumno para renombrar el archivo en Drive
                            string nameInDrive = al.NombreAlumno;
                            if (al.AlumnoMatriculadoId.HasValue && al.AlumnoMatriculado != null)
                            {
                                nameInDrive = al.AlumnoMatriculado.NombreCompleto;
                            }
                            
                            // Asegurarse de sanitizar el nombre para archivos
                            nameInDrive = nameInDrive.Replace(",", "").Replace("/", "-").Replace("\\", "-");
                            string driveFileName = $"{nameInDrive}.pdf";

                            string fileId = await GoogleDriveHelper.UploadPdfFileAsync(driveFileName, localPath, unitFolderId, accessToken);
                            if (!string.IsNullOrEmpty(fileId))
                            {
                                al.DriveFileId = fileId;
                                al.SincronizadoDrive = true;
                                db.Entry(al).State = EntityState.Modified;
                                syncedCount++;
                            }
                        }
                    }

                    await db.SaveChangesAsync();
                    TempData["Success"] = $"Sincronización finalizada. Se subieron las plantillas y {syncedCount} exámenes calificados a Google Drive con éxito.";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Ocurrió un error al sincronizar con Google Drive: " + ex.Message;
                }

                return RedirectToAction("Detail", "Unidad", new { id = examen.UnidadId });
            }
        }

        // GET: Examen/GetReporteDetalladoData
        [HttpGet]
        public async Task<ActionResult> GetReporteDetalladoData(Guid examenId)
        {
            if (Session["UserEmail"] == null)
            {
                return Json(new { success = false, message = "Sesión vencida" }, JsonRequestBehavior.AllowGet);
            }

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes
                    .Include(e => e.Preguntas)
                    .FirstOrDefaultAsync(e => e.Id == examenId);

                if (examen == null)
                    return Json(new { success = false, message = "Examen no encontrado" }, JsonRequestBehavior.AllowGet);

                // Solo preguntas calificables (excluir padres que son contenedores de incisos)
                var preguntasCalificables = examen.Preguntas
                    .Where(p => !examen.Preguntas.Any(h => h.PreguntaPadreId == p.Id)) // No tiene hijos = es calificable
                    .OrderBy(p => p.NumeroPregunta)
                    .ThenBy(p => p.Inciso)
                    .ToList();

                var calificaciones = await db.ExamenesAlumnos
                    .Include(ea => ea.AlumnoMatriculado)
                    .Include(ea => ea.RespuestasAlumnos)
                    .Where(ea => ea.ExamenId == examenId)
                    .OrderBy(ea => ea.NombreAlumno)
                    .ToListAsync();

                var rows = new List<Dictionary<string, object>>();

                foreach (var cal in calificaciones)
                {
                    var row = new Dictionary<string, object>();
                    row["NombreAlumno"] = cal.AlumnoMatriculadoId.HasValue ? cal.AlumnoMatriculado.NombreCompleto : $"{cal.NombreAlumno} (No Matr.)";
                    row["NotaTotal"] = cal.Nota;

                    foreach (var p in preguntasCalificables)
                    {
                        // Clave única: P1a, P1b, P2, P3, etc.
                        string colKey = string.IsNullOrEmpty(p.Inciso) ? $"P{p.NumeroPregunta}" : $"P{p.NumeroPregunta}{p.Inciso}";

                        // Buscar por clave compuesta: NumeroPregunta + Inciso
                        var resp = cal.RespuestasAlumnos.FirstOrDefault(r =>
                            r.NumeroPregunta == p.NumeroPregunta &&
                            (string.IsNullOrEmpty(p.Inciso) ? string.IsNullOrEmpty(r.Inciso) : r.Inciso == p.Inciso)
                        );

                        if (resp != null)
                        {
                            row[colKey] = $"{resp.RespuestaDada} {(resp.EsCorrecta ? "✓" : "✗")}";
                        }
                        else
                        {
                            row[colKey] = "-";
                        }
                    }
                    rows.Add(row);
                }

                return Json(new
                {
                    success = true,
                    preguntas = preguntasCalificables.Select(p =>
                        string.IsNullOrEmpty(p.Inciso) ? $"P{p.NumeroPregunta}" : $"P{p.NumeroPregunta}{p.Inciso}"
                    ).ToList(),
                    data = rows
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // GET: Examen/ExportarReporteDetalladoExcel
        public async Task<ActionResult> ExportarReporteDetalladoExcel(Guid examenId)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes
                    .Include(e => e.Preguntas)
                    .Include(e => e.Unidad)
                    .Include(e => e.Unidad.Seccion.Curso)
                    .FirstOrDefaultAsync(e => e.Id == examenId);

                if (examen == null) return HttpNotFound();

                // Solo preguntas calificables (excluir padres contenedores)
                var preguntasCalificables = examen.Preguntas
                    .Where(p => !examen.Preguntas.Any(h => h.PreguntaPadreId == p.Id))
                    .OrderBy(p => p.NumeroPregunta)
                    .ThenBy(p => p.Inciso)
                    .ToList();

                var calificaciones = await db.ExamenesAlumnos
                    .Include(ea => ea.AlumnoMatriculado)
                    .Include(ea => ea.RespuestasAlumnos)
                    .Where(ea => ea.ExamenId == examenId)
                    .OrderBy(ea => ea.NombreAlumno)
                    .ToListAsync();

                using (var package = new OfficeOpenXml.ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Detalle Respuestas");

                    // Estilo del Encabezado
                    worksheet.Cells[1, 1].Value = "Estudiante / Nombre Completo";
                    int col = 2;
                    foreach (var p in preguntasCalificables)
                    {
                        string header = string.IsNullOrEmpty(p.Inciso)
                            ? $"P{p.NumeroPregunta} ({p.Puntaje} pts)"
                            : $"P{p.NumeroPregunta}{p.Inciso} ({p.Puntaje} pts)";
                        worksheet.Cells[1, col].Value = header;
                        col++;
                    }
                    worksheet.Cells[1, col].Value = "Nota Total";

                    // Formato Cabecera (Azul Marino con letras blancas)
                    using (var range = worksheet.Cells[1, 1, 1, col])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(26, 37, 48));
                        range.Style.Font.Color.SetColor(System.Drawing.Color.White);
                        range.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                    }

                    // Llenar Datos
                    int rowIdx = 2;
                    foreach (var cal in calificaciones)
                    {
                        worksheet.Cells[rowIdx, 1].Value = cal.AlumnoMatriculadoId.HasValue ? cal.AlumnoMatriculado.NombreCompleto : $"{cal.NombreAlumno} (No Matr.)";
                        
                        int colIdx = 2;
                        foreach (var p in preguntasCalificables)
                        {
                            var resp = cal.RespuestasAlumnos.FirstOrDefault(r =>
                                r.NumeroPregunta == p.NumeroPregunta &&
                                (string.IsNullOrEmpty(p.Inciso) ? string.IsNullOrEmpty(r.Inciso) : r.Inciso == p.Inciso)
                            );
                            if (resp != null)
                            {
                                worksheet.Cells[rowIdx, colIdx].Value = $"{resp.RespuestaDada} {(resp.EsCorrecta ? "C" : "I")}";
                            }
                            else
                            {
                                worksheet.Cells[rowIdx, colIdx].Value = "-";
                            }
                            colIdx++;
                        }
                        worksheet.Cells[rowIdx, colIdx].Value = cal.Nota;
                        rowIdx++;
                    }

                    worksheet.Cells.AutoFitColumns();

                    var stream = new MemoryStream();
                    package.SaveAs(stream);
                    stream.Position = 0;

                    string nombreArchivo = $"Reporte_Detalle_{examen.NombreVersion.Replace(" ", "_")}.xlsx";
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
        }
        [HttpGet]
        public ActionResult GetExamenPdf(string filename, bool isTemp = false, string folderType = "")
        {
            if (string.IsNullOrEmpty(filename) || filename.Contains("..") || filename.Contains("/") || filename.Contains("\\"))
                return HttpNotFound("Acceso denegado.");

            string folder = "~/App_Data/Examenes/";
            if (folderType == "alumnos")
            {
                folder = "~/App_Data/Examenes/Alumnos/";
            }
            else if (isTemp)
            {
                folder = "~/App_Data/Examenes/Temp/";
            }

            var path = Server.MapPath(folder + filename);
            if (!System.IO.File.Exists(path))
                return HttpNotFound("Archivo físico no encontrado.");

            return File(path, "application/pdf");
        }
        [HttpPost]
        public async Task<ActionResult> RegistrarAlumnoRapido(Guid examenId, string nombreCompleto)
        {
            if (Session["UserEmail"] == null)
            {
                return Json(new { success = false, message = "Sesión vencida." });
            }

            if (string.IsNullOrWhiteSpace(nombreCompleto))
            {
                return Json(new { success = false, message = "El nombre no puede estar vacío." });
            }

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes
                    .Include(e => e.Unidad.Seccion)
                    .FirstOrDefaultAsync(e => e.Id == examenId);

                if (examen == null)
                    return Json(new { success = false, message = "Examen no encontrado." });

                var seccionId = examen.Unidad.SeccionId;

                // Crear el nuevo alumno
                var nuevoAlumno = new AlumnoMatriculado
                {
                    SeccionId = seccionId,
                    NombreCompleto = nombreCompleto.Trim()
                };

                db.AlumnosMatriculados.Add(nuevoAlumno);
                await db.SaveChangesAsync();

                return Json(new { 
                    success = true, 
                    alumnoId = nuevoAlumno.Id,
                    nombreCompleto = nuevoAlumno.NombreCompleto
                });
            }
        }
    }

    // Clases DTO para recibir JSON en los controladores
    public class PreguntaTemp
    {
        public int NumeroPregunta { get; set; }
        public string Enunciado { get; set; }
        public string Tipo { get; set; }
        public string RespuestaCorrecta { get; set; }
        public double Puntaje { get; set; }
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string OpcionesJson { get; set; }
        public bool TieneSubpreguntas { get; set; }
        public int Pagina { get; set; }
    }

    public class OpcionTemp
    {
        public string label { get; set; }
        public string val { get; set; }
        public double x { get; set; }
        public double y { get; set; }
        public double w { get; set; }
        public double h { get; set; }
    }

    public class StampInfoTemp
    {
        public double x { get; set; }
        public double y { get; set; }
        public double w { get; set; }
        public double h { get; set; }
    }

    public class ResultadoAlumnoTemp
    {
        public string TempFilePath { get; set; }
        public string NombreAlumnoRaw { get; set; }
        public Guid? AlumnoMatriculadoId { get; set; }
        public bool TieneObservacion { get; set; }
        public string Observacion { get; set; }
        public List<RespuestaAlumnoTemp> Respuestas { get; set; }
        public StampInfoTemp Stamp { get; set; }
        public Dictionary<string, StampInfoTemp> CorrectionStamps { get; set; }
    }

    public class RespuestaAlumnoTemp
    {
        public int NumeroPregunta { get; set; }
        public string Inciso { get; set; }
        public string RespuestaDada { get; set; }
        public bool? EsCorrectoManual { get; set; }
    }

    public class ResultadoAlumnoGemini
    {
        public string NombreAlumno { get; set; }
        public List<RespuestaAlumnoTemp> Respuestas { get; set; }
    }
}



