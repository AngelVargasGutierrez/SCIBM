using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SCIBM.Helpers
{
    public class GoogleDriveHelper
    {
        private static readonly HttpClient client = new HttpClient();

        // 1. Renovar el access token usando el refresh token
        public static async Task<Tuple<string, int>> RefreshAccessTokenAsync(string clientId, string clientSecret, string refreshToken)
        {
            var requestUrl = "https://oauth2.googleapis.com/token";
            var requestParams = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("client_id", clientId),
                new System.Collections.Generic.KeyValuePair<string, string>("client_secret", clientSecret),
                new System.Collections.Generic.KeyValuePair<string, string>("refresh_token", refreshToken),
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "refresh_token")
            });

            try
            {
                var response = await client.PostAsync(requestUrl, requestParams);
                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(responseText);
                    string accessToken = json["access_token"]?.ToString();
                    int expiresIn = int.Parse(json["expires_in"]?.ToString() ?? "3600");
                    return new Tuple<string, int>(accessToken, expiresIn);
                }
            }
            catch (Exception ex)
            {
                // Registrar log de error
                System.Diagnostics.Debug.WriteLine("Error al refrescar token de Google: " + ex.Message);
            }

            return null;
        }

        // 2. Buscar o crear la carpeta en Google Drive
        public static async Task<string> GetOrCreateFolderAsync(string folderName, string parentId, string accessToken)
        {
            // Primero intentamos buscar si la carpeta ya existe
            string query = $"name = '{folderName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            if (!string.IsNullOrEmpty(parentId))
            {
                query += $" and '{parentId}' in parents";
            }

            var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id)";
            
            var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseText);
                var files = json["files"] as JArray;
                if (files != null && files.Count > 0)
                {
                    return files[0]["id"]?.ToString();
                }
            }

            // Si no existe, la creamos
            var createUrl = "https://www.googleapis.com/drive/v3/files";
            var body = new JObject
            {
                { "name", folderName },
                { "mimeType", "application/vnd.google-apps.folder" }
            };

            if (!string.IsNullOrEmpty(parentId))
            {
                body.Add("parents", new JArray { parentId });
            }

            var createRequest = new HttpRequestMessage(HttpMethod.Post, createUrl);
            createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            createRequest.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

            var createResponse = await client.SendAsync(createRequest);
            if (createResponse.IsSuccessStatusCode)
            {
                var createText = await createResponse.Content.ReadAsStringAsync();
                var json = JObject.Parse(createText);
                return json["id"]?.ToString();
            }

            return null;
        }

        // 2.5 Crear o buscar una ruta completa anidada
        public static async Task<string> GetOrCreatePathAsync(string[] folderNames, string rootId, string accessToken)
        {
            string currentParentId = rootId;

            foreach (var folderName in folderNames)
            {
                if (string.IsNullOrEmpty(folderName)) continue;
                
                string foundId = await GetOrCreateFolderAsync(folderName, currentParentId, accessToken);
                if (string.IsNullOrEmpty(foundId))
                {
                    return null; // Falló al crear algún nivel
                }
                currentParentId = foundId;
            }

            return currentParentId;
        }

        // 3. Subir archivo PDF a Google Drive usando carga Multipart (O SOBREESCRIBIR SI EXISTE)
        public static async Task<string> UploadPdfFileAsync(string fileName, string localFilePath, string parentFolderId, string accessToken)
        {
            // A) Buscar si ya existe un archivo con ese nombre en esa carpeta
            string query = $"name = '{fileName}' and '{parentFolderId}' in parents and trashed = false";
            var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id)";
            
            var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            string existingFileId = null;
            var searchRes = await client.SendAsync(searchReq);
            if (searchRes.IsSuccessStatusCode)
            {
                var json = JObject.Parse(await searchRes.Content.ReadAsStringAsync());
                var files = json["files"] as JArray;
                if (files != null && files.Count > 0)
                {
                    existingFileId = files[0]["id"]?.ToString();
                }
            }

            // B) Configurar URL y Método (POST para nuevo, PATCH para existente)
            string uploadUrl = string.IsNullOrEmpty(existingFileId) 
                ? "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart"
                : $"https://www.googleapis.com/upload/drive/v3/files/{existingFileId}?uploadType=multipart";
            
            var method = string.IsNullOrEmpty(existingFileId) ? HttpMethod.Post : new HttpMethod("PATCH");

            byte[] fileBytes;
            using (var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read))
            {
                fileBytes = new byte[stream.Length];
                await stream.ReadAsync(fileBytes, 0, (int)stream.Length);
            }

            var boundary = "scibm_multipart_boundary_" + Guid.NewGuid().ToString("N");
            var request = new HttpRequestMessage(method, uploadUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var multipartContent = new MultipartContent("related", boundary);
            
            // 1. Parte de Metadatos (JSON)
            var metadata = new JObject
            {
                { "name", fileName },
                { "mimeType", "application/pdf" }
            };

            // Solo agregamos el parent si es un archivo NUEVO (POST). 
            // Si es PATCH, cambiar parents causa error a menos que se use addParents/removeParents.
            if (string.IsNullOrEmpty(existingFileId) && !string.IsNullOrEmpty(parentFolderId))
            {
                metadata.Add("parents", new JArray { parentFolderId });
            }

            var metadataContent = new StringContent(metadata.ToString(), Encoding.UTF8, "application/json");
            metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "UTF-8"
            };
            multipartContent.Add(metadataContent);

            // 2. Parte del binario del PDF
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            multipartContent.Add(fileContent);

            request.Content = multipartContent;

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseText);
                return json["id"]?.ToString();
            }

            return null;
        }

        // 4. Renombrar carpeta o archivo en Google Drive
        public static async Task<bool> RenameFolderAsync(string folderId, string newName, string accessToken)
        {
            if (string.IsNullOrEmpty(folderId)) return false;

            var updateUrl = $"https://www.googleapis.com/drive/v3/files/{folderId}";
            var body = new JObject { { "name", newName } };

            var request = new HttpRequestMessage(new HttpMethod("PATCH"), updateUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine("Error renaming Drive folder: " + error);
            }
            return response.IsSuccessStatusCode;
        }

        // 5. Obtener ID del padre de una carpeta
        public static async Task<string> GetParentIdAsync(string folderId, string accessToken)
        {
            if (string.IsNullOrEmpty(folderId)) return null;
            var getUrl = $"https://www.googleapis.com/drive/v3/files/{folderId}?fields=parents";
            var request = new HttpRequestMessage(HttpMethod.Get, getUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                var parents = json["parents"] as JArray;
                if (parents != null && parents.Count > 0) return parents[0].ToString();
            }
            return null;
        }

        // 6. Mover una carpeta de un padre a otro
        public static async Task<bool> MoveFolderAsync(string folderId, string newParentId, string accessToken)
        {
            if (string.IsNullOrEmpty(folderId) || string.IsNullOrEmpty(newParentId)) return false;

            // Primero, obtener los padres actuales
            var getUrl = $"https://www.googleapis.com/drive/v3/files/{folderId}?fields=parents";
            var getReq = new HttpRequestMessage(HttpMethod.Get, getUrl);
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var getRes = await client.SendAsync(getReq);
            if (!getRes.IsSuccessStatusCode) return false;
            
            var json = JObject.Parse(await getRes.Content.ReadAsStringAsync());
            var parents = json["parents"] as JArray;
            string oldParents = parents != null ? string.Join(",", parents) : "";

            // Si el padre ya es el mismo, no hacemos nada
            if (oldParents.Contains(newParentId)) return true;

            // Actualizar moviendo la carpeta
            var updateUrl = $"https://www.googleapis.com/drive/v3/files/{folderId}?addParents={newParentId}&removeParents={oldParents}";
            var updateReq = new HttpRequestMessage(new HttpMethod("PATCH"), updateUrl);
            updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            
            var updateRes = await client.SendAsync(updateReq);
            return updateRes.IsSuccessStatusCode;
        }

        // 7. Auto-limpieza recursiva de carpetas vacías (Garbage Collection)
        public static async Task CleanupEmptyParentsAsync(string folderId, string rootId, string accessToken)
        {
            string currentId = folderId;
            while (!string.IsNullOrEmpty(currentId) && currentId != rootId)
            {
                // 1. Verificar si la carpeta tiene hijos (excluyendo la papelera)
                var query = $"'{currentId}' in parents and trashed = false";
                var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id)";
                var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var searchRes = await client.SendAsync(searchReq);
                
                if (!searchRes.IsSuccessStatusCode) break;
                
                var json = JObject.Parse(await searchRes.Content.ReadAsStringAsync());
                var files = json["files"] as JArray;
                
                // Si tiene hijos, no la podemos borrar, detenemos la limpieza
                if (files != null && files.Count > 0) break;
                
                // 2. Obtener el padre de esta carpeta ANTES de borrarla (para continuar el ciclo hacia arriba)
                string nextParentId = await GetParentIdAsync(currentId, accessToken);

                // 3. Borrar (enviar a la papelera) la carpeta vacía
                var delReq = new HttpRequestMessage(HttpMethod.Delete, $"https://www.googleapis.com/drive/v3/files/{currentId}");
                delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                await client.SendAsync(delReq);

                // 4. Subir un nivel
                currentId = nextParentId;
            }
        }
    }
}
