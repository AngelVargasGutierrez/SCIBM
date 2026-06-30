using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SCIBM.Services
{
    public class GeminiApiService
    {
        private readonly List<string> _apiKeys;
        private readonly string _model;
        private static int _currentIndex = 0;
        private static readonly object _lock = new object();

        public GeminiApiService()
        {
            // Intentar cargar múltiples keys, fallback a la key única si no existen
            string keysStr = ConfigurationManager.AppSettings["Gemini:ApiKeys"];
            if (!string.IsNullOrEmpty(keysStr))
            {
                _apiKeys = keysStr.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            else
            {
                string singleKey = ConfigurationManager.AppSettings["Gemini:ApiKey"];
                _apiKeys = string.IsNullOrEmpty(singleKey) ? new List<string>() : new List<string> { singleKey };
            }

            _model = ConfigurationManager.AppSettings["Gemini:Model"] ?? "gemini-2.5-flash";
        }

        private string GetNextApiKey()
        {
            if (_apiKeys.Count == 0)
                throw new Exception("No hay API Keys de Gemini configuradas en secrets.config.");

            lock (_lock)
            {
                string key = _apiKeys[_currentIndex];
                _currentIndex = (_currentIndex + 1) % _apiKeys.Count;
                return key;
            }
        }

        public async Task<string> AnalyzePdfAsync(string pdfPath, string prompt)
        {
            if (_apiKeys.Count == 0)
            {
                throw new Exception("La API Key de Gemini no está configurada.");
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
                    responseMimeType = "application/json", // Forzamos respuesta en JSON estructurado
                    temperature = 0.0,
                    maxOutputTokens = 8192
                }
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5); // Gemini puede tardar un poco
                
                int intentos = 0;
                int maxIntentos = _apiKeys.Count > 3 ? _apiKeys.Count : 3;
                string responseString = null;

                while (intentos < maxIntentos)
                {
                    string currentKey = GetNextApiKey();
                    string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={currentKey}";

                    try
                    {
                        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                        var response = await client.PostAsync(apiUrl, content);

                        if (!response.IsSuccessStatusCode)
                        {
                            string errorResponse = await response.Content.ReadAsStringAsync();
                            
                            // Si es un error 429 de saturación o 503, rotamos a la siguiente key
                            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || response.StatusCode == (System.Net.HttpStatusCode)429)
                            {
                                intentos++;
                                if (intentos >= maxIntentos)
                                {
                                    throw new Exception($"Error en la API de Gemini tras {maxIntentos} intentos rotando keys ({response.StatusCode}): {errorResponse}");
                                }
                                await Task.Delay(1000); // Pequeña pausa antes de intentar con la siguiente key
                                continue;
                            }
                            else
                            {
                                throw new Exception($"Error en la API de Gemini ({response.StatusCode}): {errorResponse}");
                            }
                        }

                        responseString = await response.Content.ReadAsStringAsync();
                        break; // Éxito, salir del bucle
                    }
                    catch (HttpRequestException ex)
                    {
                        intentos++;
                        if (intentos >= maxIntentos) throw ex;
                        await Task.Delay(1000);
                    }
                }
                
                // Parsear la respuesta de Gemini
                // Gemini puede devolver el JSON en candidates[0].content.parts[0].text o directamente en el body
                string textResult = null;
                try
                {
                    JObject jsonResponse = JObject.Parse(responseString);
                    textResult = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                    
                    // Si textResult es null pero la respuesta tiene estructura de JSON válido, la usamos directa
                    if (string.IsNullOrEmpty(textResult) && responseString.TrimStart().StartsWith("{"))
                    {
                        textResult = responseString;
                    }
                }
                catch
                {
                    // Si falla el parseo anidado, intentar usar el string directamente
                    if (responseString.TrimStart().StartsWith("{"))
                        textResult = responseString;
                }

                if (string.IsNullOrEmpty(textResult))
                {
                    throw new Exception("Gemini no devolvió ningún texto válido.");
                }

                return textResult;
            }
        }
    }
}
