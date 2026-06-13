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
    public class CicloController : Controller
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

        // GET: Ciclo
        public async Task<ActionResult> Index()
        {
            if (Session["UserEmail"] == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                var ciclos = await db.CiclosAcademicos
                    .Where(c => c.DocenteEmail == email)
                    .OrderByDescending(c => c.Nombre)
                    .ToListAsync();
                
                return View(ciclos);
            }
        }

        // POST: Ciclo/Create
        [HttpPost]
        public async Task<ActionResult> Create(string nombre)
        {
            if (Session["UserEmail"] == null)
                return Json(new { success = false, message = "Sesión expirada." });

            if (string.IsNullOrEmpty(nombre))
                return Json(new { success = false, message = "El nombre del ciclo es obligatorio." });

            string email = Session["UserEmail"].ToString();

            using (var db = new ScibmContext())
            {
                try
                {
                    // Verificar si ya existe
                    bool exists = await db.CiclosAcademicos.AnyAsync(c => c.DocenteEmail == email && c.Nombre == nombre);
                    if (exists)
                    {
                        return Json(new { success = false, message = "Ya existe un ciclo con este nombre." });
                    }

                    string accessToken = await GetValidAccessTokenAsync(db, email);
                    string rootFolderId = Session["DriveFolderId"]?.ToString();
                    string cicloFolderId = null;

                    if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(rootFolderId))
                    {
                        cicloFolderId = await GoogleDriveHelper.GetOrCreateFolderAsync(nombre, rootFolderId, accessToken);
                    }

                    var ciclo = new CicloAcademico
                    {
                        Nombre = nombre,
                        DocenteEmail = email,
                        DriveFolderId = cicloFolderId
                    };
                    
                    db.CiclosAcademicos.Add(ciclo);
                    await db.SaveChangesAsync();

                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error al crear ciclo: " + ex.Message });
                }
            }
        }
    }
}
