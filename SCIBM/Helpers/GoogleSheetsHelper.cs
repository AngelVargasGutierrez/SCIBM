using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SCIBM.Helpers
{
    public class GoogleSheetsHelper
    {
        private static readonly HttpClient client = new HttpClient();

        /// <summary>
        /// Crea un nuevo Google Sheet nativo en la raíz del Drive y devuelve su SpreadsheetId
        /// </summary>
        public static async Task<string> CreateSpreadsheetAsync(string title, string accessToken)
        {
            var createUrl = "https://sheets.googleapis.com/v4/spreadsheets";

            var body = new
            {
                properties = new
                {
                    title = title
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, createUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                return json["spreadsheetId"]?.ToString();
            }

            return null;
        }

        /// <summary>
        /// Escribe datos simples en un rango y aplica formato y autoresize mediante BatchUpdate
        /// </summary>
        public static async Task<bool> PopulateAndFormatReportAsync(string spreadsheetId, List<IList<object>> values, int numColumns, string accessToken)
        {
            // 1. Escribir los datos base (valores)
            var updateValuesUrl = $"https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}/values/A1:append?valueInputOption=USER_ENTERED";
            
            var valueRange = new
            {
                range = "A1",
                majorDimension = "ROWS",
                values = values
            };

            var request1 = new HttpRequestMessage(HttpMethod.Post, updateValuesUrl);
            request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request1.Content = new StringContent(JsonConvert.SerializeObject(valueRange), Encoding.UTF8, "application/json");

            var res1 = await client.SendAsync(request1);
            if (!res1.IsSuccessStatusCode) return false;

            // 2. Aplicar Formato (Negritas para encabezados y AutoResize de columnas)
            var batchUpdateUrl = $"https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}:batchUpdate";

            var batchRequests = new List<object>
            {
                // AutoResize de las columnas utilizadas
                new
                {
                    autoResizeDimensions = new
                    {
                        dimensions = new
                        {
                            sheetId = 0, // El primer sheet por defecto suele ser 0
                            dimension = "COLUMNS",
                            startIndex = 0,
                            endIndex = numColumns
                        }
                    }
                },
                // Poner en negrita las primeras filas (encabezados de la U y de la tabla)
                new
                {
                    repeatCell = new
                    {
                        range = new
                        {
                            sheetId = 0,
                            startRowIndex = 0,
                            endRowIndex = 4 // Universidad, Facultad, Curso, Fecha
                        },
                        cell = new
                        {
                            userEnteredFormat = new
                            {
                                textFormat = new { bold = true }
                            }
                        },
                        fields = "userEnteredFormat.textFormat.bold"
                    }
                },
                new
                {
                    repeatCell = new
                    {
                        range = new
                        {
                            sheetId = 0,
                            startRowIndex = 9, // Fila 10: Títulos de la tabla
                            endRowIndex = 10
                        },
                        cell = new
                        {
                            userEnteredFormat = new
                            {
                                textFormat = new { bold = true }
                            }
                        },
                        fields = "userEnteredFormat.textFormat.bold"
                    }
                }
            };

            var batchBody = new { requests = batchRequests };

            var request2 = new HttpRequestMessage(HttpMethod.Post, batchUpdateUrl);
            request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request2.Content = new StringContent(JsonConvert.SerializeObject(batchBody), Encoding.UTF8, "application/json");

            var res2 = await client.SendAsync(request2);
            return res2.IsSuccessStatusCode;
        }
    }
}
