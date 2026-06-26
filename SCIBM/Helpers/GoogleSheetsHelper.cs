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
                // Aumentar grosor (altura) de las filas de los títulos (Filas 1 a 4)
                new
                {
                    updateDimensionProperties = new
                    {
                        range = new { sheetId = 0, dimension = "ROWS", startIndex = 0, endIndex = 4 },
                        properties = new { pixelSize = 35 }, // Mayor grosor de fila
                        fields = "pixelSize"
                    }
                },
                // Poner en negrita y tamaño de letra más grande para los títulos (Filas 1 a 4)
                new
                {
                    repeatCell = new
                    {
                        range = new { sheetId = 0, startRowIndex = 0, endRowIndex = 4, startColumnIndex = 0, endColumnIndex = numColumns },
                        cell = new { userEnteredFormat = new { horizontalAlignment = "CENTER", verticalAlignment = "MIDDLE", textFormat = new { bold = true, fontSize = 14 } } },
                        fields = "userEnteredFormat(horizontalAlignment,verticalAlignment,textFormat.bold,textFormat.fontSize)"
                    }
                },
                // Poner en negrita las estadísticas (Filas 6 a 9)
                new
                {
                    repeatCell = new
                    {
                        range = new { sheetId = 0, startRowIndex = 6, endRowIndex = 10 },
                        cell = new { userEnteredFormat = new { textFormat = new { bold = true } } },
                        fields = "userEnteredFormat.textFormat.bold"
                    }
                },
                // Merge para las 4 primeras filas (Títulos centrados)
                new { mergeCells = new { range = new { sheetId = 0, startRowIndex = 0, endRowIndex = 1, startColumnIndex = 0, endColumnIndex = numColumns }, mergeType = "MERGE_ALL" } },
                new { mergeCells = new { range = new { sheetId = 0, startRowIndex = 1, endRowIndex = 2, startColumnIndex = 0, endColumnIndex = numColumns }, mergeType = "MERGE_ALL" } },
                new { mergeCells = new { range = new { sheetId = 0, startRowIndex = 2, endRowIndex = 3, startColumnIndex = 0, endColumnIndex = numColumns }, mergeType = "MERGE_ALL" } },
                new { mergeCells = new { range = new { sheetId = 0, startRowIndex = 3, endRowIndex = 4, startColumnIndex = 0, endColumnIndex = numColumns }, mergeType = "MERGE_ALL" } },
                // Contorno y bordes para la tabla de ESTADÍSTICAS (Filas 7, 8, 9, 10 - index 6 a 10)
                new {
                    updateBorders = new {
                        range = new { sheetId = 0, startRowIndex = 6, endRowIndex = 10, startColumnIndex = 0, endColumnIndex = 4 },
                        top = new { style = "SOLID_MEDIUM", color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        bottom = new { style = "SOLID_MEDIUM", color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        left = new { style = "SOLID_MEDIUM", color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        right = new { style = "SOLID_MEDIUM", color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        innerHorizontal = new { style = "SOLID", color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        innerVertical = new { style = "SOLID", color = new { red = 0.0, green = 0.0, blue = 0.0 } }
                    }
                },
                // Dar estilo de cabecera (Fondo azul) a la fila 11 (índice 10)
                new
                {
                    repeatCell = new
                    {
                        range = new { sheetId = 0, startRowIndex = 10, endRowIndex = 11, startColumnIndex = 0, endColumnIndex = numColumns },
                        cell = new
                        {
                            userEnteredFormat = new
                            {
                                backgroundColor = new { red = 0.1, green = 0.45, blue = 0.9 }, // Azul rey
                                horizontalAlignment = "CENTER",
                                verticalAlignment = "MIDDLE",
                                textFormat = new { bold = true, foregroundColor = new { red = 1.0, green = 1.0, blue = 1.0 }, fontSize = 12 }
                            }
                        },
                        fields = "userEnteredFormat(backgroundColor,textFormat,horizontalAlignment,verticalAlignment)"
                    }
                },
                // Añadir bordes a toda la tabla principal (desde la fila 11 hasta la penúltima fila)
                new {
                    updateBorders = new {
                        range = new { sheetId = 0, startRowIndex = 10, endRowIndex = values.Count - 1, startColumnIndex = 0, endColumnIndex = numColumns },
                        top = new { style = "SOLID", width = 1, color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        bottom = new { style = "SOLID", width = 1, color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        left = new { style = "SOLID", width = 1, color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        right = new { style = "SOLID", width = 1, color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        innerHorizontal = new { style = "SOLID", width = 1, color = new { red = 0.8, green = 0.8, blue = 0.8 } },
                        innerVertical = new { style = "SOLID", width = 1, color = new { red = 0.8, green = 0.8, blue = 0.8 } }
                    }
                },
                // Pintar la fila final del PROMEDIO DEL CURSO
                new {
                    repeatCell = new {
                        range = new { sheetId = 0, startRowIndex = values.Count - 1, endRowIndex = values.Count, startColumnIndex = 0, endColumnIndex = numColumns },
                        cell = new {
                            userEnteredFormat = new {
                                backgroundColor = new { red = 0.2, green = 0.2, blue = 0.3 },
                                horizontalAlignment = "RIGHT",
                                verticalAlignment = "MIDDLE",
                                textFormat = new { bold = true, foregroundColor = new { red = 1.0, green = 1.0, blue = 1.0 }, fontSize = 12 }
                            }
                        },
                        fields = "userEnteredFormat(backgroundColor,horizontalAlignment,verticalAlignment,textFormat)"
                    }
                },
                new {
                    updateBorders = new {
                        range = new { sheetId = 0, startRowIndex = values.Count - 1, endRowIndex = values.Count, startColumnIndex = 0, endColumnIndex = numColumns },
                        top = new { style = "SOLID_MEDIUM", color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        bottom = new { style = "SOLID_MEDIUM", color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        left = new { style = "SOLID_MEDIUM", color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        right = new { style = "SOLID_MEDIUM", color = new { red = 0.0, green = 0.0, blue = 0.0 } },
                        innerVertical = new { style = "SOLID", color = new { red = 1.0, green = 1.0, blue = 1.0 } }
                    }
                },
                // Formato condicional: Aprobado
                new {
                    addConditionalFormatRule = new {
                        rule = new {
                            ranges = new[] { new { sheetId = 0, startRowIndex = 11, endRowIndex = values.Count, startColumnIndex = numColumns - 1, endColumnIndex = numColumns } },
                            booleanRule = new {
                                condition = new { type = "TEXT_CONTAINS", values = new[] { new { userEnteredValue = "Aprobado" } } },
                                format = new { backgroundColor = new { red = 0.8, green = 1.0, blue = 0.8 }, textFormat = new { bold = true, foregroundColor = new { red = 0.0, green = 0.4, blue = 0.0 } } }
                            }
                        },
                        index = 0
                    }
                },
                // Formato condicional: Regular
                new {
                    addConditionalFormatRule = new {
                        rule = new {
                            ranges = new[] { new { sheetId = 0, startRowIndex = 11, endRowIndex = values.Count, startColumnIndex = numColumns - 1, endColumnIndex = numColumns } },
                            booleanRule = new {
                                condition = new { type = "TEXT_CONTAINS", values = new[] { new { userEnteredValue = "Regular" } } },
                                format = new { backgroundColor = new { red = 1.0, green = 0.9, blue = 0.6 }, textFormat = new { bold = true, foregroundColor = new { red = 0.6, green = 0.4, blue = 0.0 } } }
                            }
                        },
                        index = 1
                    }
                },
                // Formato condicional: Desaprobado
                new {
                    addConditionalFormatRule = new {
                        rule = new {
                            ranges = new[] { new { sheetId = 0, startRowIndex = 11, endRowIndex = values.Count, startColumnIndex = numColumns - 1, endColumnIndex = numColumns } },
                            booleanRule = new {
                                condition = new { type = "TEXT_CONTAINS", values = new[] { new { userEnteredValue = "Desaprobado" } } },
                                format = new { backgroundColor = new { red = 1.0, green = 0.8, blue = 0.8 }, textFormat = new { bold = true, foregroundColor = new { red = 0.6, green = 0.0, blue = 0.0 } } }
                            }
                        },
                        index = 2
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
