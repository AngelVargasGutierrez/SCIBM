using System;
using System.Collections.Generic;
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
    public class CursoController : Controller
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

        // GET: Curso
        public async Task<ActionResult> Index(Guid? cicloId)
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            if (!cicloId.HasValue)
            {
                return RedirectToAction("Index", "Ciclo");
            }

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                var ciclo = await db.CiclosAcademicos.FirstOrDefaultAsync(c => c.Id == cicloId.Value && c.DocenteEmail == email);
                if (ciclo == null) return HttpNotFound("Ciclo no encontrado.");

                var cursos = await db.Cursos
                    .Include(c => c.Secciones)
                    .Where(c => c.CicloAcademicoId == cicloId.Value)
                    .OrderBy(c => c.Nombre)
                    .ToListAsync();
                
                Session["MisCursos"] = cursos.ToDictionary(c => c.Id, c => c.Nombre);
                ViewBag.CicloId = cicloId.Value;
                ViewBag.CicloNombre = ciclo.Nombre;

                return View(cursos);
            }
        }

        // POST: Curso/Create
        [HttpPost]
        public async Task<ActionResult> Create(Guid cicloId, string nombre, string codigo)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada." });

            if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(codigo))
                return Json(new { success = false, message = "El nombre y el código de curso son obligatorios." });

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                try
                {
                    var ciclo = await db.CiclosAcademicos.FirstOrDefaultAsync(c => c.Id == cicloId && c.DocenteEmail == email);
                    if (ciclo == null) return Json(new { success = false, message = "Ciclo inválido." });

                    string accessToken = await GetValidAccessTokenAsync(db, email);
                    string rootFolderId = ciclo.DriveFolderId; // La carpeta padre ahora es el ciclo
                    string courseFolderId = null;

                    if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(rootFolderId))
                    {
                        courseFolderId = await GoogleDriveHelper.GetOrCreateFolderAsync(nombre, rootFolderId, accessToken);
                    }

                    var curso = new Curso
                    {
                        Nombre = nombre,
                        Codigo = codigo,
                        CicloAcademicoId = cicloId,
                        DriveFolderId = courseFolderId
                    };
                    db.Cursos.Add(curso);

                    // Crear sección por defecto
                    string seccionFolderId = null;
                    if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(courseFolderId))
                    {
                        seccionFolderId = await GoogleDriveHelper.GetOrCreateFolderAsync("Sección Única", courseFolderId, accessToken);
                    }

                    var seccion = new Seccion
                    {
                        CursoId = curso.Id,
                        Nombre = "Sección Única",
                        DriveFolderId = seccionFolderId
                    };
                    db.Secciones.Add(seccion);

                    await db.SaveChangesAsync();

                    var cursosReload = await db.Cursos.Include(c => c.CicloAcademico).Where(c => c.CicloAcademico.DocenteEmail == email).ToListAsync();
                    Session["MisCursos"] = cursosReload.ToDictionary(c => c.Id, c => c.Nombre);

                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al crear curso: " + ex.Message });
                }
            }
        }

        // GET: Curso/Detail/5
        public async Task<ActionResult> Detail(Guid id)
        {
            if (Session["UserEmail"] == null)
                return RedirectToAction("Login", "Auth");

            using (var db = new ScibmContext())
            {
                var curso = await db.Cursos
                    .Include(c => c.Secciones)
                    .Include(c => c.CicloAcademico)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (curso == null || curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                    return HttpNotFound("El curso no existe o no tiene permisos.");

                return View(curso);
            }
        }

        // GET: Curso/Reporte/5
        public async Task<ActionResult> Reporte(Guid id)
        {
            if (Session["UserEmail"] == null)
                return RedirectToAction("Login", "Auth");

            using (var db = new ScibmContext())
            {
                var curso = await db.Cursos
                    .Include(c => c.Secciones)
                    .Include(c => c.CicloAcademico)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (curso == null || curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                    return HttpNotFound();

                return View(curso);
            }
        }
    }
}
