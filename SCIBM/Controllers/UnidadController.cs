using System;
using System.Configuration;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using SCIBM.Helpers;
using SCIBM.Models;

namespace SCIBM.Controllers
{
    public class UnidadController : Controller
    {
        // Soporte para Access Token
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

        // POST: Unidad/Create
        [HttpPost]
        public async Task<ActionResult> Create(Guid seccionId, string nombreUnidad)
        {
            if (Session["UserEmail"] == null)
            {
                return Json(new { success = false, message = "Sesión expirada." });
            }

            if (string.IsNullOrEmpty(nombreUnidad))
            {
                return Json(new { success = false, message = "El nombre de la unidad es requerido." });
            }

            nombreUnidad = nombreUnidad.Trim();
            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                try
                {
                    var seccion = await db.Secciones.Include(s => s.Curso.CicloAcademico).FirstOrDefaultAsync(s => s.Id == seccionId);
                    if (seccion == null || seccion.Curso.CicloAcademico.DocenteEmail != email)
                    {
                        return Json(new { success = false, message = "Sección no encontrada o sin autorización." });
                    }

                    // Validar si ya existe
                    bool existe = await db.Unidades.AnyAsync(u => u.SeccionId == seccionId && u.NombreUnidad.Equals(nombreUnidad, StringComparison.OrdinalIgnoreCase));
                    if (existe)
                    {
                        return Json(new { success = false, message = "Ya existe una unidad con ese nombre en esta sección." });
                    }

                    // Crear la subcarpeta en Google Drive
                    string accessToken = await GetValidAccessTokenAsync(db, email);
                    string courseFolderId = seccion.DriveFolderId;
                    string unitFolderId = null;

                    if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(courseFolderId))
                    {
                        unitFolderId = await GoogleDriveHelper.GetOrCreateFolderAsync(nombreUnidad, courseFolderId, accessToken);
                    }

                    var unidad = new Unidad
                    {
                        SeccionId = seccionId,
                        NombreUnidad = nombreUnidad,
                        DriveFolderId = unitFolderId
                    };

                    db.Unidades.Add(unidad);
                    await db.SaveChangesAsync();

                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al crear la unidad: " + ex.Message });
                }
            }
        }

        // GET: Unidad/Detail/5
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

            using (var db = new ScibmContext())
            {
                var unidad = await db.Unidades
                    .Include(u => u.Seccion.Curso.CicloAcademico)
                    .Include(u => u.Examenes.Select(e => e.Preguntas))
                    .FirstOrDefaultAsync(u => u.Id == id.Value);

                if (unidad == null || unidad.Seccion.Curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                {
                    return HttpNotFound("La unidad no existe o no tiene permisos.");
                }

                // Cargar calificaciones si existe examen
                if (unidad.Examenes.FirstOrDefault() != null)
                {
                    var examId = unidad.Examenes.FirstOrDefault().Id;
                    var calificaciones = await db.ExamenesAlumnos
                        .Where(ea => ea.ExamenId == examId)
                        .OrderBy(ea => ea.NombreAlumno)
                        .ToListAsync();
                    
                    ViewBag.Calificaciones = calificaciones;
                    ViewBag.TotalPreguntas = await db.Preguntas.CountAsync(p => p.ExamenId == examId);

                    if (TempData["ShowReviewModal"] != null && (bool)TempData["ShowReviewModal"])
                    {
                        ViewBag.PreguntasIA = await db.Preguntas.Where(p => p.ExamenId == examId).OrderBy(p => p.NumeroPregunta).ToListAsync();
                    }
                }

                return View(unidad);
            }
        }

        // POST: Unidad/LimpiarPdfsLocales
        [HttpPost]
        public async Task<ActionResult> LimpiarPdfsLocales(Guid unidadId)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            using (var db = new ScibmContext())
            {
                var unidad = await db.Unidades
                    .Include(u => u.Seccion.Curso.CicloAcademico)
                    .Include(u => u.Examenes)
                    .FirstOrDefaultAsync(u => u.Id == unidadId);

                if (unidad == null || unidad.Seccion.Curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                {
                    return HttpNotFound();
                }

                if (unidad.Examenes.FirstOrDefault() == null)
                {
                    TempData["Error"] = "No existe un examen asociado a esta unidad para limpiar.";
                    return RedirectToAction("Detail", new { id = unidadId });
                }

                try
                {
                    int cleanedCount = 0;
                    var exam = unidad.Examenes.FirstOrDefault();

                    // 1. Limpiar examen original (plantilla) si ya se subió a Drive
                    if (exam.SincronizadoDrive && !string.IsNullOrEmpty(exam.RutaPdfOriginal))
                    {
                        string fullPath = Server.MapPath(exam.RutaPdfOriginal);
                        if (System.IO.File.Exists(fullPath))
                        {
                            System.IO.File.Delete(fullPath);
                            // Dejamos la ruta pero marcamos que está borrado localmente o la dejamos tal cual
                            // En nuestro flujo, no pasa nada si la ruta se mantiene, solo validamos existencia al leer
                            cleanedCount++;
                        }
                    }

                    // 2. Limpiar PDFs de los alumnos sincronizados
                    var alumnosConPdf = await db.ExamenesAlumnos
                        .Where(ea => ea.ExamenId == exam.Id && ea.SincronizadoDrive)
                        .ToListAsync();

                    foreach (var al in alumnosConPdf)
                    {
                        if (!string.IsNullOrEmpty(al.RutaPdfRespuesta))
                        {
                            string fullPath = Server.MapPath(al.RutaPdfRespuesta);
                            if (System.IO.File.Exists(fullPath))
                            {
                                System.IO.File.Delete(fullPath);
                                cleanedCount++;
                            }
                        }
                    }

                    TempData["Success"] = $"Limpieza completada. Se eliminaron {cleanedCount} archivos PDF locales que ya están respaldados de forma segura en Google Drive.";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Error durante la limpieza de archivos: " + ex.Message;
                }

                return RedirectToAction("Detail", new { id = unidadId });
            }
        }
    }
}
