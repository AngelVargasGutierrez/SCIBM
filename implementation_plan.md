# Plan de Correcciones y Mejoras - Editor Visual

He analizado detalladamente tus 8 puntos y la imagen adjunta. Tienes una visión excelente de cómo debe ser el flujo de trabajo (UX) para el docente. A continuación, te presento mi opinión técnica para cada punto y el plan exacto de cómo lo implementaremos en el código.

## 1. Flujo de Opciones Múltiples (Clic para crear círculos)
**Tu idea:** En lugar de arrastrar, dibujar la caja principal y luego hacer clics adentro para que aparezcan los círculos (A, B, C...).
**Mi opinión:** Brillante. Es mucho más rápido y preciso.
**Implementación:** 
- Modificaremos el comportamiento de la herramienta "Opción Múltiple". Al usarla, solo dibujarás un recuadro vacío (que representará la pregunta en sí).
- Si la caja está seleccionada (activa) y haces doble clic o clic sobre la zona blanca de la caja, el sistema creará dinámicamente un pequeño círculo en esa posición exacta.
- Las opciones se auto-nombrarán secuencialmente (A, B, C...).

## 2. Bloques de Verdadero y Falso Múltiples (Un solo círculo por inciso)
**Tu idea:** Al seleccionar V/F, eliges entre la sub-herramienta "V" o "F". Haces un solo clic por cada inciso (a, b, c). Ese único clic indica la coordenada donde el alumno escribirá y, al mismo tiempo, establece cuál es la respuesta correcta (V o F). El escáner luego leerá si el alumno escribió V o F en esa coordenada.
**Mi opinión:** ¡Ahora entiendo perfectamente! Estás combinando la creación de la coordenada y la asignación de la respuesta correcta en un solo clic. Es extremadamente eficiente para el docente.
**Implementación:**
- Al activar V/F, aparecerán dos botones extra: `Estampar (V)` y `Estampar (F)`.
- Dibujas la caja principal que engloba toda la pregunta I.
- Haces clic en el inciso 'a' usando la sub-herramienta 'V'. Se crea la subpregunta 'a' con respuesta correcta 'V' y se guarda la coordenada.
- En el panel derecho, la pregunta principal listará las subpreguntas (a, b, c...) mostrando su respectiva coordenada y su respuesta correcta ya pre-configurada (aunque modificable), además de su puntaje parcial.

## 3. Panel Derecho: "NaN ptos" y Ordenamiento Completo (Drag & Drop + Flechas)
**Tu idea:** Arreglar el error de suma (NaN) y poder reordenar con flechas Y arrastrando (Drag & Drop), tanto las preguntas principales (1, 2, 3) como las subpreguntas (a, b, c).
**Mi opinión:** El Drag & Drop hace que la experiencia sea "Premium" y muy profesional. Es factible usando la API de HTML5 Drag & Drop.
**Implementación:**
- **NaN:** Interceptaré el cálculo matemático para que asuma `0` si el texto no es un número.
- **Drag & Drop (Preguntas):** Las tarjetas del panel derecho serán "arrastrables". Al soltarlas sobre otra tarjeta, se reordenará el arreglo `preguntas` y se repintará la interfaz actualizando los números (1, 2, 3...).
- **Drag & Drop (Subpreguntas):** Dentro del Card de V/F, las filas de los incisos tendrán un ícono de "agarradera" (grip) para arrastrarlas y reordenarlas. Al reordenar, las letras (a, b, c) se reasignarán automáticamente para mantener el orden alfabético.

## 4. Herramienta "Respuesta Libre" (Desarrollo)
**Tu idea:** Añadir la herramienta de "respuesta libre" para que el profesor la lea y corrija manualmente.
**Mi opinión:** Es vital para exámenes mixtos (marcar alternativas y redactar).
**Implementación:**
- Agregaremos un nuevo botón a la barra de herramientas.
- Esta herramienta dibujará un recuadro rojo o distintivo. 
- En el panel derecho, esta pregunta no te pedirá "Respuesta Correcta", sino únicamente el puntaje máximo. El sistema sabrá que es un área para corrección manual posterior.

## 5. Herramienta Espacio en Blanco (Subpreguntas y Rediseño)
**Tu idea:** Un solo recuadro en texto azul, y que además soporte múltiples incisos (a, b, c) dentro de una misma caja principal (como se ve en la nueva imagen de "III. Completar").
**Mi opinión:** Esta es la pieza clave que faltaba. "Espacio en Blanco" funciona exactamente igual que V/F: una caja grande y múltiples sub-zonas de respuesta.
**Implementación:**
- Simplificaré la "Respuesta Corta" a un solo recuadro semitransparente con texto azul brillante (`#007bff`).
- Al seleccionar la herramienta, dibujarás la caja grande (Ej: bloque III entero).
- Al hacer clic sobre cada línea punteada del PDF, se creará un sub-recuadro (inciso a, b, c...).
- En el panel derecho, el bloque listará todos los incisos. El profesor escribirá la palabra o frase correcta para cada inciso en los inputs del panel.

## 6. IA OCR: Extracción Automática de Puntajes
**Tu idea:** Que Gemini lea el enunciado (Ej: "0.5 ptos cada una"), asigne el puntaje automáticamente y que luego el usuario lo pueda modificar si quiere.
**Mi opinión:** Excelente integración. Hay que aprovechar el OCR.
**Implementación:**
- En el archivo `ExamenController.cs`, actualizaré el "Prompt" que se envía a Gemini.
- Le daré la orden estricta de: *"Busca dentro del texto de la pregunta cualquier mención a puntos (ptos, puntos, pts). Si la encuentras, extrae el número y asígnalo al campo `puntaje` del JSON. Si no encuentras nada, pon 0"*.
- El campo en el panel derecho siempre será editable por si la IA se equivoca.

## 7. Posición del Total de Puntos
**Tu idea:** Mover el Total a la derecha del título "Plantilla Examen".
**Mi opinión:** Mucho mejor jerarquía visual.
**Implementación:**
- Ajustaré el Flexbox del encabezado del panel derecho en `ScanTemplate.cshtml` para que el título y el contador estén en la misma línea, alineados a los extremos.

## 8. Bug del Cursor del Borrador (La "Ambulancia")
**Tu idea:** El cursor del borrador desaparece dentro de las cajas y parece una ambulancia afuera.
**Mi opinión:** Es un conflicto clásico de CSS. El cursor de las cajas (`cursor: move` o `pointer`) sobreescribe al del contenedor principal. Y Windows a veces no entiende íconos de FontAwesome como cursores y pone cursores genéricos de emergencia.
**Implementación:**
- Codificaré un pequeño ícono SVG de un borrador directamente en Base64 dentro del CSS.
- Usaré la regla `cursor: url('...'), auto !important;` y forzaré que las cajas hijas lo hereden usando `pointer-events: none` parcial cuando el modo "Borrador" esté encendido. 

## 9. Cambios en la Base de Datos (Estructura de Subpreguntas)
**Tu duda:** ¿Cómo se guardará esto en la BD y afectará el plan?
**Mi análisis:** Para que puedas calificar independientemente el inciso 'a' y el inciso 'b', la base de datos no puede verlos como un solo bloque; debe verlos como preguntas reales que se pueden corregir por separado.
**Implementación (Nuevos campos en BD):**
- Modificaré el esquema de la tabla `Pregunta` en SQL (`RecrearBD.sql` y `SetupCompletoBD.sql`) y en C# (`Entities.cs`).
- Añadiré dos columnas nuevas: 
  1. `PreguntaPadreId` (NULLable): Si una pregunta es un inciso (a, b, c), guardará el ID de la pregunta principal (la caja grande contenedora).
  2. `Inciso` (VARCHAR): Guardará la letra "a", "b", "c".
- Con esto, el modelo de datos será perfecto: la "caja grande" se guarda como una pregunta contenedora (sin puntaje directo), y todos los clics que haces adentro se guardan como subpreguntas hijas, cada una con su propio puntaje y respuesta correcta. Cuando escaneemos los exámenes, el sistema sabrá evaluar cada inciso de forma 100% individual.

---
## User Review Required
Este es el alcance final que incluye el modelo de Base de Datos para soportar los incisos de manera profesional. Si estás de acuerdo con esta arquitectura, **dame tu aprobación final para empezar a programar.**
