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
                    .Include(c => c.Carrera)
                    .Include(c => c.Carrera.EscuelaProfesional)
                    .Include(c => c.Carrera.EscuelaProfesional.Facultad)
                    .Where(c => c.CicloAcademicoId == cicloId.Value)
                    .OrderBy(c => c.Nombre)
                    .ToListAsync();
                
                Session["MisCursos"] = cursos.ToDictionary(c => c.Id, c => c.Nombre);
                ViewBag.CicloId = cicloId.Value;
                ViewBag.CicloNombre = ciclo.Nombre;
                ViewBag.Facultades = await db.Facultades.OrderBy(f => f.Nombre).ToListAsync();

                return View(cursos);
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetEscuelas(Guid id)
        {
            using (var db = new ScibmContext())
            {
                var escuelas = await db.EscuelasProfesionales
                    .Where(e => e.FacultadId == id)
                    .Select(e => new { id = e.Id, text = e.Nombre })
                    .OrderBy(e => e.text)
                    .ToListAsync();
                return Json(escuelas, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetCarreras(Guid id)
        {
            using (var db = new ScibmContext())
            {
                var carreras = await db.Carreras
                    .Where(c => c.EscuelaProfesionalId == id)
                    .Select(c => new { id = c.Id, text = c.Nombre, ciclosTotales = c.CiclosTotales })
                    .OrderBy(c => c.text)
                    .ToListAsync();
                return Json(carreras, JsonRequestBehavior.AllowGet);
            }
        }

        // POST: Curso/Create
        [HttpPost]
        public async Task<ActionResult> Create(Guid cicloId, Guid carreraId, string cicloRomano, string nombre, string codigo)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada." });

            if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(codigo) || string.IsNullOrEmpty(cicloRomano))
                return Json(new { success = false, message = "Todos los campos son obligatorios." });

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                try
                {
                    var ciclo = await db.CiclosAcademicos.FirstOrDefaultAsync(c => c.Id == cicloId && c.DocenteEmail == email);
                    if (ciclo == null) return Json(new { success = false, message = "Ciclo inválido." });

                    var carrera = await db.Carreras
                        .Include(c => c.EscuelaProfesional)
                        .Include(c => c.EscuelaProfesional.Facultad)
                        .FirstOrDefaultAsync(c => c.Id == carreraId);
                    if (carrera == null) return Json(new { success = false, message = "Carrera inválida." });

                    string accessToken = await GetValidAccessTokenAsync(db, email);
                    string rootFolderId = ciclo.DriveFolderId; // La carpeta padre es el ciclo (Semestre)
                    string courseFolderId = null;

                    if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(rootFolderId))
                    {
                        // Construir la jerarquía completa
                        // Ruta en Drive: FAING -> EPIS_Ingeniería de Sistemas -> V CICLO -> Álgebra
                        string[] path = new string[] 
                        { 
                            carrera.EscuelaProfesional.Facultad.Siglas, 
                            $"{carrera.EscuelaProfesional.Siglas}_{carrera.Nombre.Replace(" ", "")}", 
                            cicloRomano, 
                            nombre // El último nodo es la carpeta del Curso
                        };

                        courseFolderId = await GoogleDriveHelper.GetOrCreatePathAsync(path, rootFolderId, accessToken);

                        // Las carpetas específicas se crearán bajo demanda.
                    }

                    var curso = new Curso
                    {
                        Nombre = nombre,
                        Codigo = codigo,
                        CicloAcademicoId = cicloId,
                        CarreraId = carreraId,
                        CicloRomano = cicloRomano,
                        DriveFolderId = courseFolderId
                    };
                    db.Cursos.Add(curso);

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

        // POST: Curso/Delete
        [HttpPost]
        public async Task<ActionResult> Delete(Guid id)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada." });

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                try
                {
                    var curso = await db.Cursos
                        .Include(c => c.CicloAcademico)
                        .FirstOrDefaultAsync(c => c.Id == id && c.CicloAcademico.DocenteEmail == email);
                    
                    if (curso == null)
                        return Json(new { success = false, message = "Curso no encontrado o sin permisos." });

                    db.Cursos.Remove(curso);
                    await db.SaveChangesAsync();

                    var cursosReload = await db.Cursos.Include(c => c.CicloAcademico).Where(c => c.CicloAcademico.DocenteEmail == email).ToListAsync();
                    Session["MisCursos"] = cursosReload.ToDictionary(c => c.Id, c => c.Nombre);

                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al eliminar: " + ex.Message });
                }
            }
        }

        // GET: Curso/Detail/5
        public async Task<ActionResult> Detail(Guid? id)
        {
            if (Session["UserEmail"] == null)
                return RedirectToAction("Login", "Auth");

            if (!id.HasValue)
                return RedirectToAction("Index", "Ciclo");

            using (var db = new ScibmContext())
            {
                var curso = await db.Cursos
                    .Include(c => c.Secciones)
                    .Include(c => c.CicloAcademico)
                    .FirstOrDefaultAsync(c => c.Id == id.Value);

                if (curso == null || curso.CicloAcademico.DocenteEmail != Session["UserEmail"].ToString())
                    return HttpNotFound("El curso no existe o no tiene permisos.");

                return View(curso);
            }
        }

        // POST: Curso/Edit
        [HttpPost]
        public async Task<ActionResult> Edit(Guid id, Guid facultadId, Guid escuelaId, Guid carreraId, string cicloRomano, string nombre, string codigo)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada." });

            if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(codigo) || string.IsNullOrEmpty(cicloRomano))
                return Json(new { success = false, message = "Todos los campos son obligatorios." });

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                try
                {
                    var curso = await db.Cursos
                        .Include(c => c.Carrera)
                        .Include(c => c.CicloAcademico)
                        .FirstOrDefaultAsync(c => c.Id == id && c.CicloAcademico.DocenteEmail == email);
                    
                    if (curso == null)
                        return Json(new { success = false, message = "Curso no encontrado o sin permisos." });

                    bool exists = await db.Cursos.AnyAsync(c => c.CicloAcademicoId == curso.CicloAcademicoId && c.CarreraId == carreraId && c.CicloRomano == cicloRomano && c.Nombre == nombre && c.Id != id);
                    if (exists)
                        return Json(new { success = false, message = "Ya existe otro curso con este nombre en este ciclo y carrera." });

                    bool pathChanged = curso.CarreraId != carreraId || curso.CicloRomano != cicloRomano;
                    bool nameChanged = curso.Nombre != nombre;

                    var nuevaCarrera = await db.Carreras
                        .Include(c => c.EscuelaProfesional)
                        .Include(c => c.EscuelaProfesional.Facultad)
                        .FirstOrDefaultAsync(c => c.Id == carreraId);

                    if (nuevaCarrera == null) return Json(new { success = false, message = "Carrera inválida." });

                    string accessToken = await GetValidAccessTokenAsync(db, email);

                    if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(curso.DriveFolderId))
                    {
                        string oldParentId = await GoogleDriveHelper.GetParentIdAsync(curso.DriveFolderId, accessToken);
                        string rootFolderId = curso.CicloAcademico.DriveFolderId; // Semestre

                        if (pathChanged)
                        {
                            // 1. Obtener/Crear nueva ruta
                            string[] newPath = new string[] 
                            { 
                                nuevaCarrera.EscuelaProfesional.Facultad.Siglas, 
                                $"{nuevaCarrera.EscuelaProfesional.Siglas}_{nuevaCarrera.Nombre.Replace(" ", "")}", 
                                cicloRomano
                            };
                            
                            string newParentId = await GoogleDriveHelper.GetOrCreatePathAsync(newPath, rootFolderId, accessToken);

                            // 2. Mover la carpeta del curso a la nueva ruta
                            if (!string.IsNullOrEmpty(newParentId))
                            {
                                await GoogleDriveHelper.MoveFolderAsync(curso.DriveFolderId, newParentId, accessToken);
                            }
                        }

                        if (nameChanged)
                        {
                            await GoogleDriveHelper.RenameFolderAsync(curso.DriveFolderId, nombre, accessToken);
                        }

                        // 3. Limpiar el rastro de la ruta vieja si quedó vacía
                        if (pathChanged && !string.IsNullOrEmpty(oldParentId))
                        {
                            await GoogleDriveHelper.CleanupEmptyParentsAsync(oldParentId, rootFolderId, accessToken);
                        }
                    }

                    curso.Nombre = nombre;
                    curso.Codigo = codigo;
                    curso.CarreraId = carreraId;
                    curso.CicloRomano = cicloRomano;

                    await db.SaveChangesAsync();

                    var cursosReload = await db.Cursos.Include(c => c.CicloAcademico).Where(c => c.CicloAcademico.DocenteEmail == email).ToListAsync();
                    Session["MisCursos"] = cursosReload.ToDictionary(c => c.Id, c => c.Nombre);

                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al editar: " + ex.Message });
                }
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
