using System;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json.Linq;
using SCIBM.Helpers;
using SCIBM.Models;

namespace SCIBM.Controllers
{
    public class AuthController : Controller
    {
        private static readonly HttpClient httpClient = new HttpClient();

        // GET: Auth/Login
        public ActionResult Login(string error)
        {
            if (Session["UserEmail"] != null)
            {
                return RedirectToAction("Index", "Ciclo");
            }

            ViewBag.Error = error;
            return View();
        }

        // GET: Auth/GoogleLogin
        public ActionResult GoogleLogin()
        {
            string clientId = ConfigurationManager.AppSettings["Google:ClientId"];
            string redirectUri = ConfigurationManager.AppSettings["Google:RedirectUri"];

            // Solicitar openid, email, profile y acceso a Google Drive (drive.file para poder crear y subir archivos en su Drive)
            string scope = "openid email profile https://www.googleapis.com/auth/drive.file";
            
            // Generar la URL de Google OAuth
            string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                              $"client_id={clientId}" +
                              $"&redirect_uri={HttpUtility.UrlEncode(redirectUri)}" +
                              $"&response_type=code" +
                              $"&scope={HttpUtility.UrlEncode(scope)}" +
                              $"&access_type=offline" + // Permite obtener el Refresh Token
                              $"&prompt=select_account"; // Solo pide seleccionar cuenta, sin forzar consentimiento si ya existe

            return Redirect(authUrl);
        }

        // GET: Auth/GoogleCallback
        public async Task<ActionResult> GoogleCallback(string code, string error)
        {
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            {
                return RedirectToAction("Login", new { error = "El inicio de sesión fue cancelado o falló: " + error });
            }

            string clientId = ConfigurationManager.AppSettings["Google:ClientId"];
            string clientSecret = ConfigurationManager.AppSettings["Google:ClientSecret"];
            string redirectUri = ConfigurationManager.AppSettings["Google:RedirectUri"];

            try
            {
                // 1. Intercambiar el código de autorización por tokens
                var tokenUrl = "https://oauth2.googleapis.com/token";
                var requestParams = new FormUrlEncodedContent(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("code", code),
                    new System.Collections.Generic.KeyValuePair<string, string>("client_id", clientId),
                    new System.Collections.Generic.KeyValuePair<string, string>("client_secret", clientSecret),
                    new System.Collections.Generic.KeyValuePair<string, string>("redirect_uri", redirectUri),
                    new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "authorization_code")
                });

                var tokenResponse = await httpClient.PostAsync(tokenUrl, requestParams);
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    return RedirectToAction("Login", new { error = "Error al intercambiar el código por tokens con Google." });
                }

                var responseText = await tokenResponse.Content.ReadAsStringAsync();
                var tokenJson = JObject.Parse(responseText);

                string accessToken = tokenJson["access_token"]?.ToString();
                string idToken = tokenJson["id_token"]?.ToString();
                string refreshToken = tokenJson["refresh_token"]?.ToString(); // Puede ser nulo si no es el primer login
                int expiresIn = int.Parse(tokenJson["expires_in"]?.ToString() ?? "3600");

                if (string.IsNullOrEmpty(accessToken))
                {
                    return RedirectToAction("Login", new { error = "No se recibió un Access Token válido." });
                }

                // 2. Obtener el perfil del usuario mediante el UserInfo Endpoint
                var userInfoUrl = "https://www.googleapis.com/oauth2/v3/userinfo";
                var profileRequest = new HttpRequestMessage(HttpMethod.Get, userInfoUrl);
                profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var profileResponse = await httpClient.SendAsync(profileRequest);
                if (!profileResponse.IsSuccessStatusCode)
                {
                    return RedirectToAction("Login", new { error = "Error al obtener la información de perfil del usuario." });
                }

                var profileText = await profileResponse.Content.ReadAsStringAsync();
                var profileJson = JObject.Parse(profileText);

                string email = profileJson["email"]?.ToString().ToLower();
                string name = profileJson["name"]?.ToString();
                string firstName = profileJson["given_name"]?.ToString();
                string lastName = profileJson["family_name"]?.ToString();
                string picture = profileJson["picture"]?.ToString();

                if (string.IsNullOrEmpty(email))
                {
                    return RedirectToAction("Login", new { error = "No se pudo obtener el correo de la cuenta de Google." });
                }

                // 3. Validar dominio de correo institucional UPT (@upt.pe o @virtual.upt.pe)
                if (!email.EndsWith("@upt.pe") && !email.EndsWith("@virtual.upt.pe"))
                {
                    return RedirectToAction("Login", new { error = "¡Hola! Por favor, inicia sesión con tu correo institucional (por ejemplo, terminado en @virtual.upt.pe)." });
                }

                // 4. Registrar o actualizar docente en la base de datos local
                using (var db = new ScibmContext())
                {
                    var docente = await db.Docentes.FirstOrDefaultAsync(d => d.Email == email);
                    if (docente == null)
                    {
                        docente = new Docente
                        {
                            Email = email,
                            Nombre = firstName ?? "Docente",
                            Apellido = lastName ?? "UPT",
                            RefreshToken = refreshToken,
                            UltimoAcceso = DateTime.Now
                        };
                        db.Docentes.Add(docente);
                    }
                    else
                    {
                        // Actualizar token si se recibió uno nuevo
                        if (!string.IsNullOrEmpty(refreshToken))
                        {
                            docente.RefreshToken = refreshToken;
                        }
                        docente.UltimoAcceso = DateTime.Now;
                        db.Entry(docente).State = EntityState.Modified;
                    }
                    await db.SaveChangesAsync();

                    // 5. Crear la carpeta principal de Google Drive si aún no existe
                    if (string.IsNullOrEmpty(docente.GoogleDriveFolderId))
                    {
                        // Formatear: Primera letra del nombre + primer apellido (ej: Enrique Lanchipa -> SCIBM_Elanchipa)
                        string fLetter = !string.IsNullOrEmpty(docente.Nombre) ? docente.Nombre.Substring(0, 1).ToUpper() : "U";
                        string lName = "Docente";
                        if (!string.IsNullOrEmpty(docente.Apellido))
                        {
                            var parts = docente.Apellido.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0)
                            {
                                lName = parts[0];
                                // Normalizar para evitar espacios y poner la primera en mayúscula
                                lName = char.ToUpper(lName[0]) + lName.Substring(1).ToLower();
                            }
                        }
                        
                        string rootFolderName = $"SCIBM_{fLetter}{lName}";
                        
                        // Crear carpeta principal en Google Drive
                        string folderId = await GoogleDriveHelper.GetOrCreateFolderAsync(rootFolderName, null, accessToken);
                        if (!string.IsNullOrEmpty(folderId))
                        {
                            docente.GoogleDriveFolderId = folderId;
                            db.Entry(docente).State = EntityState.Modified;
                            await db.SaveChangesAsync();
                        }
                    }

                    // 6. Guardar variables de sesión del usuario
                    Session["UserEmail"] = docente.Email;
                    Session["UserName"] = $"{docente.Nombre} {docente.Apellido}";
                    Session["UserFirstName"] = docente.Nombre;
                    Session["UserLastName"] = docente.Apellido;
                    Session["UserPicture"] = picture ?? "/Content/images/user-default.png";
                    Session["AccessToken"] = accessToken;
                    Session["TokenExpiry"] = DateTime.Now.AddSeconds(expiresIn - 60); // 60s antes de que expire
                    Session["RefreshToken"] = docente.RefreshToken;
                    Session["DriveFolderId"] = docente.GoogleDriveFolderId;
                }

                return RedirectToAction("Index", "Ciclo");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en GoogleCallback: " + ex.Message);
                return RedirectToAction("Login", new { error = "Ocurrió un error inesperado durante la autenticación: " + ex.Message });
            }
        }

        // GET: Auth/Logout
        public ActionResult Logout()
        {
            Session.Clear();
            Session.Abandon();
            return RedirectToAction("Login");
        }
    }
}
