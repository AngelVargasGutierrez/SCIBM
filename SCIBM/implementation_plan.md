# Plan: Paso 5 - Reportes y Exportación a Google Sheets

A continuación, el plan para la implementación final de la exportación a Google Sheets, conectada directamente a las carpetas del curso en Google Drive.

## 1. El Problema y Requisito
Actualmente se pueden generar reportes descargables en Excel. El requerimiento de la "Fase 5" exige que el sistema pueda enviar estos reportes directamente a la nube del profesor en formato nativo de Google Sheets, manteniéndolo organizado.

## 2. Cambios Propuestos

### `Helpers/GoogleDriveHelper.cs`
- [NEW] Crear método `UploadAndConvertToGoogleSheetAsync`: 
  Tomará los bytes del archivo Excel y hará una subida a Google Drive (Multipart Upload). El "truco" clave aquí es especificar en la metadata el `mimeType` como `application/vnd.google-apps.spreadsheet`. Esto le dirá a Google Drive que procese el archivo binario y lo convierta automáticamente en un Google Sheet editable.

### `Controllers/SeccionController.cs`
- [NEW] Crear endpoint `ExportarReporteAGoogleSheets(Guid seccionId)`:
  - Generará internamente el mismo reporte consolidado usando EPPlus.
  - Asegurará la existencia de la carpeta `REPORTES` dentro de la carpeta del Curso en Google Drive.
  - Subirá el reporte y obtendrá el `ID` del archivo de Google Drive.
  - Devolverá un objeto JSON con la URL directa: `https://docs.google.com/spreadsheets/d/{fileId}/edit`.

### `Views/Seccion/Reporte.cshtml`
- [MODIFY] Agregar botón de Exportar a Google Sheets junto al botón actual de Excel.
- [MODIFY] Añadir JavaScript que, al hacer clic, muestre un estado de carga (para evitar doble clic mientras sube) y que al recibir la respuesta del servidor abra automáticamente la pestaña del navegador con el Google Sheet creado.

## 3. Plan de Verificación
1. Compilar el backend para asegurar que no hay errores en EPPlus o `GoogleDriveHelper`.
2. Probar haciendo clic en "Exportar a Google Sheets".
3. Verificar que se abra el enlace con la tabla completa y las calificaciones correctamente vaciadas en las celdas.

---
> [!IMPORTANT]
> **User Review Required**
> Este plan integrará la última fase del flujo (Paso 5). Usaremos la carpeta "REPORTES" dentro de tu curso en Drive. 
> 
> ¿Estás de acuerdo con este plan? Si apruebas, procederé a implementarlo.
