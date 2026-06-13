using System;
using System.Configuration;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SCIBM.Services
{
    public class GeminiApiService
    {
        private readonly string _apiKey;
        private readonly string _apiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro-preview:generateContent?key=";

        public GeminiApiService()
        {
            _apiKey = ConfigurationManager.AppSettings["Gemini:ApiKey"];
        }

        public async Task<string> AnalyzePdfAsync(string pdfPath, string prompt)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new Exception("La API Key de Gemini no está configurada en Web.config.");
            }

            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("No se encontró el archivo PDF.", pdfPath);
            }

            // Leer y codificar el PDF en Base64
            byte[] pdfBytes = File.ReadAllBytes(pdfPath);
            string base64Pdf = Convert.ToBase64String(pdfBytes);

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = "application/pdf",
                                    data = base64Pdf
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    response_mime_type = "application/json" // Forzamos respuesta en JSON estructurado
                }
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5); // Gemini puede tardar un poco en procesar un PDF completo
                
                int maxRetries = 3;
                int currentRetry = 0;
                string responseString = null;

                while (currentRetry < maxRetries)
                {
                    try
                    {
                        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                        var response = await client.PostAsync(_apiUrl + _apiKey, content);

                        if (!response.IsSuccessStatusCode)
                        {
                            string errorResponse = await response.Content.ReadAsStringAsync();
                            
                            // Si es un error 503 de saturación (ServiceUnavailable), reintentamos automáticamente en silencio
                            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || response.StatusCode == (System.Net.HttpStatusCode)429)
                            {
                                currentRetry++;
                                if (currentRetry >= maxRetries)
                                {
                                    throw new Exception($"Error en la API de Gemini tras {maxRetries} intentos ({response.StatusCode}): {errorResponse}");
                                }
                                await Task.Delay(2500); // Esperar 2.5 segundos antes de reintentar
                                continue;
                            }
                            else
                            {
                                throw new Exception($"Error en la API de Gemini ({response.StatusCode}): {errorResponse}");
                            }
                        }

                        responseString = await response.Content.ReadAsStringAsync();
                        break; // Éxito, salir del bucle de reintentos
                    }
                    catch (HttpRequestException ex)
                    {
                        currentRetry++;
                        if (currentRetry >= maxRetries) throw ex;
                        await Task.Delay(2500);
                    }
                }
                
                // Parsear la respuesta de Gemini (está anidada)
                JObject jsonResponse = JObject.Parse(responseString);
                var textResult = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                if (string.IsNullOrEmpty(textResult))
                {
                    throw new Exception("Gemini no devolvió ningún texto válido.");
                }

                return textResult;
            }
        }
    }
}
