using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SCIBM.Controllers;
using SCIBM.Models;

namespace SCIBM.Helpers
{
    public class PdfStamperHelper
    {
        // 1. Estampar la nota en el PDF de respuesta del alumno
        public static void StampGrade(string inputPdfPath, string outputPdfPath, string gradeText, double xPct, double yPct, double wPct, double hPct)
        {
            try
            {
                if (!File.Exists(inputPdfPath))
                    throw new FileNotFoundException("No se encontró el archivo PDF original para estampar la nota.");

                // Abrir el documento en modo modificación
                using (PdfDocument document = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Modify))
                {
                    if (document.Pages.Count == 0)
                        return;

                    // Estampamos en la primera página
                    PdfPage page = document.Pages[0];
                    double pageWidth = page.Width.Point;
                    double pageHeight = page.Height.Point;

                    // Convertir los porcentajes a coordenadas de puntos físicos del PDF
                    double x = pageWidth * (xPct / 100.0);
                    double y = pageHeight * (yPct / 100.0);
                    double w = pageWidth * (wPct / 100.0);
                    double h = pageHeight * (hPct / 100.0);

                    // Margen de seguridad para evitar coordenadas negativas o fuera de la página
                    if (w <= 0) w = 120;
                    if (h <= 0) h = 45;
                    if (x < 0) x = 0;
                    if (y < 0) y = 0;
                    if (x + w > pageWidth) x = pageWidth - w;
                    if (y + h > pageHeight) y = pageHeight - h;

                    using (XGraphics gfx = XGraphics.FromPdfPage(page))
                    {
                        // 1. Dibujar el fondo del recuadro (Glassmorphism sutil o blanco translúcido)
                        XColor bgColor = XColor.FromArgb(235, 255, 255, 255); // Blanco semi-transparente
                        XSolidBrush bgBrush = new XSolidBrush(bgColor);
                        XPen borderPen = new XPen(XColors.DarkRed, 2.5);

                        // Esquinas redondeadas para un look premium
                        gfx.DrawRoundedRectangle(borderPen, bgBrush, x, y, w, h, 8, 8);

                        // 2. Escribir el texto de la nota centrado
                        // Ajustamos dinámicamente el tamaño de la fuente según el alto de la caja
                        double fontSize = Math.Max(10, Math.Min(16, h * 0.4));
                        XFont font = new XFont("Arial", fontSize, XFontStyle.Bold);

                        XStringFormat format = new XStringFormat();
                        format.LineAlignment = XLineAlignment.Center;
                        format.Alignment = XStringAlignment.Center;

                        // Rectángulo interno con padding para el texto
                        XRect textRect = new XRect(x, y, w, h);
                        gfx.DrawString(gradeText, font, XBrushes.DarkRed, textRect, format);
                    }

                    // Guardar los cambios
                    document.Save(outputPdfPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al estampar nota en PDF: " + ex.Message);
                // Si falla el estampado, copiamos el archivo original intacto para no interrumpir el flujo
                if (inputPdfPath != outputPdfPath)
                {
                    File.Copy(inputPdfPath, outputPdfPath, true);
                }
            }
        }

        public class CorrectionStampData
        {
            public double X { get; set; }
            public double Y { get; set; }
            public int Page { get; set; } // Página donde se debe estampar
            public bool IsCorrect { get; set; }
            public string CorrectAnswerText { get; set; }
        }

        // 1.5 Estampar nota final y correcciones (Check/X)
        public static void StampGradeAndCorrections(string inputPdfPath, string outputPdfPath, string gradeText, StampInfoTemp stampInfo, List<CorrectionStampData> corrections)
        {
            try
            {
                if (!File.Exists(inputPdfPath))
                    throw new FileNotFoundException("No se encontró el archivo PDF original para estampar.");

                using (PdfDocument document = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Modify))
                {
                    if (document.Pages.Count == 0) return;

                    PdfPage page = document.Pages[0];
                    double pageWidth = page.Width.Point;
                    double pageHeight = page.Height.Point;

                    using (XGraphics gfx = XGraphics.FromPdfPage(page))
                    {
                        // A) Estampar Nota
                        if (stampInfo != null)
                        {
                            double x = pageWidth * (stampInfo.x / 100.0);
                            double y = pageHeight * (stampInfo.y / 100.0);
                            double w = pageWidth * (stampInfo.w / 100.0);
                            double h = pageHeight * (stampInfo.h / 100.0);

                            if (w <= 0) w = 120;
                            if (h <= 0) h = 45;

                            XColor bgColor = XColor.FromArgb(120, 255, 159, 28);
                            XSolidBrush bgBrush = new XSolidBrush(bgColor);
                            XPen borderPen = new XPen(XColor.FromArgb(255, 255, 159, 28), 2);
                            borderPen.DashStyle = XDashStyle.Dash;

                            gfx.DrawRectangle(borderPen, bgBrush, x, y, w, h);

                            double fontSize = Math.Max(10, Math.Min(18, h * 0.4));
                            XFont font = new XFont("Arial", fontSize, XFontStyle.Bold);
                            XStringFormat format = new XStringFormat { LineAlignment = XLineAlignment.Center, Alignment = XStringAlignment.Center };
                            gfx.DrawString(gradeText, font, new XSolidBrush(XColor.FromArgb(255, 255, 159, 28)), new XRect(x, y, w, h), format);
                        }
                    }

                    // B) Estampar Correcciones (Check / X) por página
                    if (corrections != null)
                    {
                        var correctionsByPage = new Dictionary<int, List<CorrectionStampData>>();
                        foreach (var c in corrections)
                        {
                            int pNum = c.Page > 0 ? c.Page : 1;
                            if (!correctionsByPage.ContainsKey(pNum))
                                correctionsByPage[pNum] = new List<CorrectionStampData>();
                            correctionsByPage[pNum].Add(c);
                        }

                        XFont markFont = new XFont("Arial", 16, XFontStyle.Bold);
                        XFont textFont = new XFont("Arial", 10, XFontStyle.Regular);

                        foreach (var kvp in correctionsByPage)
                        {
                            int targetPageZeroIndexed = kvp.Key - 1;
                            if (targetPageZeroIndexed >= document.Pages.Count) continue;

                            PdfPage currentPdfPage = document.Pages[targetPageZeroIndexed];
                            double currentPageWidth = currentPdfPage.Width.Point;
                            double currentPageHeight = currentPdfPage.Height.Point;

                            using (XGraphics currentGfx = XGraphics.FromPdfPage(currentPdfPage))
                            {
                                foreach (var cor in kvp.Value)
                                {
                                    double cx = currentPageWidth * (cor.X / 100.0);
                                    double cy = currentPageHeight * (cor.Y / 100.0);
                                
                                // Para compensar diferencias entre HTML y PDF, desplazamos cy ligeramente
                                cy += 12; // Base line offset aprox

                                if (cor.IsCorrect)
                                {
                                    // Verde (Check)
                                    // Unicode checkmark "✓" is U+2713
                                    currentGfx.DrawString("✓", markFont, XBrushes.DarkCyan, cx, cy);
                                }
                                else
                                {
                                    // Rojo (X)
                                    currentGfx.DrawString("✗", markFont, XBrushes.DarkRed, cx, cy);
                                    // Respuesta esperada
                                    currentGfx.DrawString($" Cor: {cor.CorrectAnswerText}", textFont, XBrushes.DarkRed, cx + 15, cy);
                                }
                            }
                        }
                    }
                    } // Cierra if (corrections != null)
                    
                    // Guardar los cambios
                    document.Save(outputPdfPath);
                } // Cierra using(PdfDocument)
            } // Cierra try
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al estampar nota/correcciones en PDF: " + ex.Message);
                if (inputPdfPath != outputPdfPath) File.Copy(inputPdfPath, outputPdfPath, true);
            }
        }

        // 2. Dibujar solucionario de respuestas correctas en la plantilla del examen
        public static void DrawSolucionario(string inputPdfPath, string outputPdfPath, IEnumerable<Pregunta> preguntas)
        {
            try
            {
                if (!File.Exists(inputPdfPath))
                    throw new FileNotFoundException("No se encontró el archivo PDF de plantilla.");

                using (PdfDocument document = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Modify))
                {
                    // Agrupar preguntas por página (en nuestro modelo la pregunta puede asociarse a la página)
                    // Nota: Si no guardamos página en Pregunta, asumimos la primera página o que la coordenada Y nos ayuda, 
                    // pero para simplificar, usaremos page.Index si las coordenadas mapean directo.
                    // Para que sea robusto, mapearemos en la página 1 las preguntas del visor, o si el visor guarda la página,
                    // podemos agregar 'NumeroPagina' o deducirlo del JSON. 
                    // Asumiremos que el visor de pdf.js registra las coordenadas de la página correspondiente.
                    // Dibujaremos las respuestas en la primera página por defecto o según la página indicada.

                    for (int i = 0; i < document.Pages.Count; i++)
                    {
                        PdfPage page = document.Pages[i];
                        double pageWidth = page.Width.Point;
                        double pageHeight = page.Height.Point;

                        using (XGraphics gfx = XGraphics.FromPdfPage(page))
                        {
                            foreach (var p in preguntas)
                            {
                                // Si las coordenadas del control de la pregunta están en esta página
                                // Para simplificar, si no tenemos número de página en la pregunta,
                                // asumiremos que las coordenadas pertenecen a la primera página (i == 0)
                                // o mapearemos basándonos en la estructura. Asumiremos página 1 (i == 0) para demostración 
                                // o si se guarda en OpcionesJson la página de cada opción.
                                
                                double qx = pageWidth * (p.PosX / 100.0);
                                double qy = pageHeight * (p.PosY / 100.0);
                                double qw = pageWidth * (p.Width / 100.0);
                                double qh = pageHeight * (p.Height / 100.0);

                                if (p.Tipo == "OpcionMultiple" && !string.IsNullOrEmpty(p.OpcionesJson))
                                {
                                    try
                                    {
                                        // Analizar las coordenadas de las sub-opciones
                                        var opciones = JArray.Parse(p.OpcionesJson);
                                        foreach (var opt in opciones)
                                        {
                                            string label = opt["label"]?.ToString();
                                            if (label != null && label.Equals(p.RespuestaCorrecta, StringComparison.OrdinalIgnoreCase))
                                            {
                                                // Coordenadas de la opción correcta
                                                double oxPct = opt["x"] != null ? double.Parse(opt["x"].ToString()) : 0;
                                                double oyPct = opt["y"] != null ? double.Parse(opt["y"].ToString()) : 0;
                                                double owPct = opt["w"] != null ? double.Parse(opt["w"].ToString()) : 0;
                                                double ohPct = opt["h"] != null ? double.Parse(opt["h"].ToString()) : 0;

                                                // Mapear a puntos físicos
                                                double ox = pageWidth * (oxPct / 100.0);
                                                double oy = pageHeight * (oyPct / 100.0);
                                                double ow = pageWidth * (owPct / 100.0);
                                                double oh = pageHeight * (ohPct / 100.0);

                                                if (ow <= 0) ow = 14;
                                                if (oh <= 0) oh = 14;

                                                // Dibujar círculo verde traslúcido sobre la opción correcta
                                                XColor circleColor = XColor.FromArgb(120, 46, 204, 113); // Verde esmeralda translúcido
                                                XSolidBrush brush = new XSolidBrush(circleColor);
                                                XPen pen = new XPen(XColors.Green, 1.5);

                                                gfx.DrawEllipse(pen, brush, ox, oy, ow, oh);
                                            }
                                        }
                                    }
                                    catch (Exception jsonEx)
                                    {
                                        System.Diagnostics.Debug.WriteLine("Error al parsear OpcionesJson: " + jsonEx.Message);
                                    }
                                }
                                else if (p.Tipo == "RespuestaLibre")
                                {
                                    // Dibujar el texto de la respuesta sobre el espacio en blanco
                                    XColor textBgColor = XColor.FromArgb(80, 46, 204, 113);
                                    XSolidBrush bgBrush = new XSolidBrush(textBgColor);
                                    gfx.DrawRectangle(bgBrush, qx, qy, qw, qh);

                                    XFont font = new XFont("Courier New", Math.Max(8, qh * 0.7), XFontStyle.Bold);
                                    XStringFormat format = new XStringFormat
                                    {
                                        LineAlignment = XLineAlignment.Center,
                                        Alignment = XStringAlignment.Near
                                    };

                                    gfx.DrawString(" " + p.RespuestaCorrecta, font, XBrushes.DarkGreen, new XRect(qx, qy, qw, qh), format);
                                }
                            }
                        }
                    }

                    document.Save(outputPdfPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al generar solucionario PDF: " + ex.Message);
                if (inputPdfPath != outputPdfPath)
                {
                    File.Copy(inputPdfPath, outputPdfPath, true);
                }
            }
        }
    }
}
