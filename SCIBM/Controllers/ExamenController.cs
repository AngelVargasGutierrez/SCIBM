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
4. 'puntaje' (float): Busca dentro del texto de la pregunta cualquier mención a puntos (ptos, puntos, pts, etc). Si la encuentras, extrae SOLO el número y asígnalo aquí. Si no encuentras nada, pon 0.0.
5. 'posX', 'posY', 'width', 'height': Coordenadas relativas en porcentaje (0.0 a 100.0) de la ubicación aproximada del bloque de respuestas (las letras, paréntesis o líneas). Para Desarrollo, intenta abarcar todo el espacio destinado a la respuesta.
6. 'opciones': Si es OpcionMultiple o VerdaderoFalso, detecta exactamente cuántas alternativas hay. 
   Devuelve un array de objetos JSON para las opciones. Por ejemplo, si hay A, B, C, D, devuelve 4 opciones. Si es VerdaderoFalso, devuelve 2 ('V', 'F'). 
   Cada opción debe tener 'label', 'x', 'y' (coordenadas relativas X, Y relativas al ancho y alto total del documento, no de la pregunta), 'w' (2.5), 'h' (2.5). Pon las coordenadas lo más precisas posible sobre las letras o paréntesis vacíos.
   Para RespuestaLibre, el array de opciones debe estar vacío.

Devuelve ÚNICAMENTE un JSON válido con esta estructura estricta:
{
  ""preguntas"": [
    {
      "numeroPregunta": 1,
      "enunciado": "¿Qué es la célula? (2 ptos)",
      "tipo": "OpcionMultiple",
      "puntaje": 2.0,
      "posX": 10.5, "posY": 20.0, "width": 50.0, "height": 15.0,
      "opciones": [
        { "label": "A", "x": 12.0, "y": 25.0, "w": 2.5, "h": 2.5 },
        { "label": "B", "x": 12.0, "y": 28.0, "w": 2.5, "h": 2.5 }
      ]
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
        public async Task<ActionResult> ScanTemplate(Guid examenId)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            using (var db = new ScibmContext())
            {
                var examen = await db.Examenes
                    .Include(e => e.Unidad)
                    .Include(e => e.Unidad.Seccion.Curso.CicloAcademico)
                    .Include(e => e.Preguntas)
                    .FirstOrDefaultAsync(e => e.Id == examenId);

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

                        string safeRespuesta = pFront.RespuestaCorrecta ?? "";
                        if (safeRespuesta.Length > 150) safeRespuesta = safeRespuesta.Substring(0, 150);

                        var pPadre = new Pregunta
                        {
                            Id = Guid.NewGuid(),
                            ExamenId = examen.Id,
                            PreguntaPadreId = null,
                            Inciso = null,
                            NumeroPregunta = pFront.NumeroPregunta,
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

                        if (!string.IsNullOrEmpty(pFront.OpcionesJson) && pFront.OpcionesJson != "[]" && 
                           (pFront.Tipo == "RespuestaLibre" || pFront.Tipo == "VerdaderoFalso"))
                        {
                            try {
                                var opciones = JsonConvert.DeserializeObject<List<OpcionTemp>>(pFront.OpcionesJson);
                                if (opciones != null && opciones.Count > 1) 
                                {
                                    double puntajeHijo = Math.Round(pFront.Puntaje / opciones.Count, 2);
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
                                            Enunciado = $"{pPadre.Enunciado} - Inciso {opt.label}",
                                            Tipo = pFront.Tipo,
                                            RespuestaCorrecta = string.IsNullOrEmpty(opt.val) ? "Texto" : opt.val,
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

                    // Construir el texto de las preguntas esperadas
                    var preguntasInfo = string.Join("\n", examen.Preguntas.OrderBy(p => p.NumeroPregunta).Select(p => 
                        $"- Pregunta {p.NumeroPregunta}: {p.Enunciado} (Respuesta correcta esperada: {p.RespuestaCorrecta})"
                    ));

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
                            string prompt = $@"Eres un calificador automático avanzado. Este es el examen resuelto por un estudiante.
Primero, busca y extrae el nombre del estudiante si está escrito en alguna parte (apellidos y nombres).
Luego, evalúa las siguientes {examen.Preguntas.Count} preguntas basadas en esta plantilla:
{preguntasInfo}

Extrae las respuestas que el estudiante marcó (A, B, C, etc.) o escribió para cada número de pregunta.
Devuelve ÚNICAMENTE un JSON válido con esta estructura estricta:
{{
  ""NombreAlumno"": ""Nombre Apellido"",
  ""Respuestas"": [
    {{ ""NumeroPregunta"": 1, ""RespuestaDada"": ""A"" }}
  ]
}}";

                            string jsonResponse = await gemini.AnalyzePdfAsync(physicalPath, prompt);

                            if (jsonResponse.StartsWith("```json"))
                            {
                                jsonResponse = jsonResponse.Substring(7);
                                if (jsonResponse.EndsWith("```")) jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3);
                            }

                            var resultadoAlumno = JsonConvert.DeserializeObject<ResultadoAlumnoGemini>(jsonResponse);

                            // Adaptar al formato que espera GradeOverview.cshtml
                            var respuestasFormatoJS = new List<object>();
                            foreach (var r in resultadoAlumno.Respuestas)
                            {
                                respuestasFormatoJS.Add(new {
                                    numeroPregunta = r.NumeroPregunta,
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
                            var pregunta = examen.Preguntas.FirstOrDefault(p => p.NumeroPregunta == resp.NumeroPregunta);
                            bool esCorrecta = false;
                            
                            if (pregunta != null)
                            {
                                esCorrecta = pregunta.RespuestaCorrecta.Equals(resp.RespuestaDada, StringComparison.OrdinalIgnoreCase);
                                if (resp.EsCorrectoManual.HasValue && resp.EsCorrectoManual.Value)
                                {
                                    esCorrecta = true; // Override manual del profesor
                                }
                                
                                if (esCorrecta)
                                {
                                    notaFinal += pregunta.Puntaje;
                                }
                            }

                            string safeRespuesta = resp.RespuestaDada ?? "";
                            if (safeRespuesta.Length > 150) safeRespuesta = safeRespuesta.Substring(0, 150);

                            respuestasDb.Add(new RespuestaAlumno
                            {
                                NumeroPregunta = resp.NumeroPregunta,
                                RespuestaDada = safeRespuesta,
                                EsCorrecta = esCorrecta
                            });
                        }

                        // 3. Estampar la nota final en el PDF usando PdfStamperHelper en la posición calibrada
                        string notaTexto = $"Nota: {notaFinal.ToString("0.0")}";
                        PdfStamperHelper.StampGrade(tempPhysicalPath, finalPhysicalPath, notaTexto, examen.StampX, examen.StampY, examen.StampWidth, examen.StampHeight);

                        // 4. Guardar registros en base de datos
                        var examenAlumno = new ExamenAlumno
                        {
                            ExamenId = examenId,
                            NombreAlumno = res.NombreAlumnoRaw,
                            AlumnoMatriculadoId = res.AlumnoMatriculadoId,
                            Nota = notaFinal,
                            RutaPdfRespuesta = finalRelativePath,
                            TieneObservacion = res.TieneObservacion,
                            Observacion = res.Observacion
                        };

                        db.ExamenesAlumnos.Add(examenAlumno);
                        await db.SaveChangesAsync(); // Generar ID para asociar respuestas

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
                        return RedirectToAction("Detail", "Unidad", new { id = examen.Id });
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

                return RedirectToAction("Detail", "Unidad", new { id = examen.Id });
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

                var preguntas = examen.Preguntas.OrderBy(p => p.NumeroPregunta).ToList();
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

                    foreach (var p in preguntas)
                    {
                        var resp = cal.RespuestasAlumnos.FirstOrDefault(r => r.NumeroPregunta == p.NumeroPregunta);
                        if (resp != null)
                        {
                            row[$"P{p.NumeroPregunta}"] = $"{resp.RespuestaDada} {(resp.EsCorrecta ? "✓" : "✗")}";
                        }
                        else
                        {
                            row[$"P{p.NumeroPregunta}"] = "-";
                        }
                    }
                    rows.Add(row);
                }

                return Json(new
                {
                    success = true,
                    preguntas = preguntas.Select(p => $"P{p.NumeroPregunta}").ToList(),
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

                var preguntas = examen.Preguntas.OrderBy(p => p.NumeroPregunta).ToList();
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
                    foreach (var p in preguntas)
                    {
                        worksheet.Cells[1, col].Value = $"P{p.NumeroPregunta} ({p.Puntaje} pts)";
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
                        foreach (var p in preguntas)
                        {
                            var resp = cal.RespuestasAlumnos.FirstOrDefault(r => r.NumeroPregunta == p.NumeroPregunta);
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

    public class ResultadoAlumnoTemp
    {
        public string TempFilePath { get; set; }
        public string NombreAlumnoRaw { get; set; }
        public Guid? AlumnoMatriculadoId { get; set; }
        public bool TieneObservacion { get; set; }
        public string Observacion { get; set; }
        public List<RespuestaAlumnoTemp> Respuestas { get; set; }
    }

    public class RespuestaAlumnoTemp
    {
        public int NumeroPregunta { get; set; }
        public string RespuestaDada { get; set; }
        public bool? EsCorrectoManual { get; set; }
    }

    public class ResultadoAlumnoGemini
    {
        public string NombreAlumno { get; set; }
        public List<RespuestaAlumnoTemp> Respuestas { get; set; }
    }
}



