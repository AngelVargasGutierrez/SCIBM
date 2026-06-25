# Sistema de Calificación Inteligente (SCIBM) - Documentación General

Este documento describe el contexto completo de la aplicación, desglosado por módulos, dispositivos de hardware necesarios, estimaciones de costos en moneda local (Soles Peruanos - PEN) y una lista exhaustiva de requerimientos funcionales y no funcionales, tomando en cuenta todas las características actuales y las que se implementarán próximamente.

## 1. Contexto y Descripción del Sistema

La aplicación está diseñada para digitalizar, agilizar y automatizar el proceso de calificación de exámenes físicos en instituciones educativas. A través de la integración con la Inteligencia Artificial (Gemini Vision), el almacenamiento en la nube (Google Drive/Sheets) y herramientas visuales interactivas, SCIBM reduce drásticamente el tiempo de corrección, la carga administrativa y mejora la retroalimentación académica.

### Módulos de la Aplicación

1. **Módulo de Autenticación (Auth):**
   - Sistema de inicio de sesión seguro, que incluye la capacidad de **Autenticación mediante Google (SSO)** para que los docentes ingresen rápidamente usando su cuenta institucional, además del login estándar.

2. **Módulo de Gestión Académica (CRUD Completo):**
   - **Gestión de Ciclos Académicos (Semestres):** Creación, visualización, modificación y eliminación de semestres académicos.
   - **Gestión de Cursos:** Registro, actualización y filtrado/búsqueda rápida de los cursos que el docente tiene a cargo.
   - **Gestión de Secciones y Alumnos:** División de alumnos por curso, permitiendo la **Importación de listas de estudiantes mediante un archivo Excel**. Incluye la creación dinámica e inteligente del nombre de las secciones (ej. A, B, C).
   - **Gestión de Unidades:** Agrupación de evaluaciones, permitiendo versiones diferentes del mismo examen (ej. Examen A, Examen B, Recuperación) vinculando directamente la nota obtenida al estudiante en esa unidad.

3. **Módulo de Evaluación y Calificación Inteligente (Core OCR/IA):**
   - **Subida de PDF Maestro:** Permite cargar un PDF que servirá como plantilla (puede ser el examen vacío en blanco o una hoja de respuestas ya resuelta de ejemplo).
   - **Calibración Interactiva del Examen:** Un editor visual (Drag & Drop) donde el docente calibra, mapea y modifica la cuadrícula del examen (definir zonas de opción múltiple, verdadero/falso, desarrollo y puntajes).
   - **Procesamiento de Lotes (IA):** Permite subir los PDFs con todos los exámenes físicos rendidos por los estudiantes. La IA de **Gemini Vision** analiza y extrae texto, marcas y contenido manuscrito para procesar la calificación.
   - **Visor de Revisión y Estampado:** Un panel interactivo que contrasta la respuesta del estudiante con el solucionario. El docente puede revisar, modificar la nota y, lo más importante, el sistema dibuja y **Estampa/Sella visualmente las notas, los "checks" (✔️) y cruces (❌)** directamente sobre el archivo PDF del examen.

4. **Módulo de Reportes e Integración en la Nube (Google Drive & Sheets):**
   - **Sincronización con Google Drive:** El sistema automatiza la creación de carpetas ordenadas (por Semestre/Curso/Sección) en el Drive del usuario logueado. Sube y guarda automáticamente los PDFs de los exámenes ya calificados y estampados.
   - **Visualización de Reportes Internos:** Una vista de tabla interactiva en "Reportes" donde se puede visualizar y filtrar todas las notas de los alumnos y secciones.
   - **Exportación a Google Sheets y Excel:** Exporta el consolidado de notas vinculándolo o creándolo directamente en la cuenta de Google Drive como un archivo Sheets, con formato avanzado (colores, bordes y cálculos de promedios/tendencias).
   - **Dashboard Estadístico Dinámico:** (Fase 2) Visualización en gráficos con **Chart.js** sobre el rendimiento del curso, permitiendo filtrar tendencias por alumno, por sección y comparar todas las unidades.

5. **Módulo de Experiencia de Usuario y Personalización (UI/UX):**
   - **Toggle de Tema Visual:** Control total sobre la apariencia de la interfaz (Tema Claro ☀️ y Oscuro 🌙), con transiciones fluidas en todas las vistas, tablas y modales.

---

## 2. Contexto de Dispositivos y Hardware Necesario

El sistema requiere de una conexión hardware-software, enfocada especialmente en la digitalización de alto volumen.

* **Ordenador / Laptop para el Usuario:** 
  Dado que el procesamiento OCR, la IA y la gestión de base de datos se ejecutan en servidores en la nube y mediante APIs, el usuario solo necesita un equipo estándar capaz de ejecutar un navegador moderno de forma fluida.
* **Escáner Automático de Documentos (ADF de Alta Capacidad):** 
  El hardware esencial del flujo de trabajo. Un escáner departamental al cual el docente le coloca la pila de exámenes físicos (cientos o miles de hojas). El equipo se encarga de jalar y escanear hoja por hoja **a doble cara simultáneamente**, a gran velocidad, y guarda toda la pila en un solo archivo PDF masivo o en red, el cual luego se carga en SCIBM en cuestión de segundos.

### Estimación de Costos (PEN - Soles)

| Elemento / Hardware | Descripción | Costo Aproximado (S/.) |
| :--- | :--- | :--- |
| **Laptop / PC Básica** | Procesador estándar (Core i3/Ryzen 3), 8GB RAM. | S/. 1,500 - S/. 2,500 |
| **Escáner ADF (Doble Cara / Lotes)** | Ej: *Epson WorkForce, Fujitsu ScanSnap, Brother ADS*. Digitalizan decenas de hojas por minuto en una pasada. | S/. 1,500 - S/. 4,500 |
| **Alojamiento (Hosting & DB)** | Servidor Windows/Linux en la nube y Base de Datos SQL Server. | S/. 150 - S/. 400 / mes |
| **IA (Gemini Vision API)** | Cobro por millón de tokens procesados. | S/. 20 - S/. 100 / mes |
| **Google Cloud API** | Cuotas gratuitas muy amplias para la API de Drive/Sheets. | S/. 0 |

> [!TIP]
> Si la institución ya posee fotocopiadoras multifuncionales grandes de pasillo (Ricoh, Konica Minolta) conectadas en red, el costo de hardware de escaneo es cero. Estas máquinas hacen el mismo trabajo y envían los lotes en PDF a una carpeta de la PC del docente.

---

## 3. Lista de Requerimientos del Sistema

### Requerimientos Funcionales (RF)

**Gestión de Cuentas:**
* **RF01 - Autenticación con Google:** El sistema debe permitir iniciar sesión y vincular la cuenta a través de Google OAuth 2.0 (SSO).
* **RF02 - Login Tradicional:** El sistema debe soportar un inicio de sesión convencional con usuario y contraseña.

**Gestión Académica e Importación:**
* **RF03 - Gestión de Semestres (Ciclos):** CRUD completo (Crear, Leer, Actualizar, Modificar, Eliminar) para semestres académicos.
* **RF04 - Gestión de Cursos:** CRUD completo para los cursos impartidos por el docente.
* **RF05 - Búsqueda y Filtrado General:** La interfaz debe proveer barras de búsqueda y selectores para filtrar rápidamente cursos, secciones y alumnos.
* **RF06 - Gestión de Secciones y Unidades:** CRUD de secciones bajo un curso y de unidades bajo una sección.
* **RF07 - Auto-sugerencia de Secciones:** Sugerir secuencialmente nombres como "Sección A", "Sección B", con un popup de confirmación directa.
* **RF08 - Importación de Alumnos (Excel):** Capacidad para cargar archivos Excel y poblar automáticamente la lista de estudiantes matriculados en una sección.
* **RF09 - Gestión de Múltiples Versiones:** Soporte para agrupar versiones de exámenes (ej. A, B, Recuperación) cuyas notas consoliden al estudiante en la unidad principal.

**Evaluación, OCR e Inteligencia Artificial:**
* **RF10 - Subida de PDF Maestro:** Capacidad para cargar el PDF original (vacío o resuelto) como plantilla base.
* **RF11 - Calibración del Examen (Plantillas):** Editor que permite al usuario mapear interactivamente las zonas, asignar respuestas correctas y puntajes mediante un entorno drag & drop.
* **RF12 - Subida Masiva de Evaluaciones:** Aceptar un PDF pesado (lote de estudiantes) para ser procesado.
* **RF13 - Procesamiento OCR mediante IA (Gemini):** Enviar las hojas a Gemini Vision para reconocer texto manuscrito, cruces o marcas, y calcular las respuestas del alumno en contraste con la plantilla.
* **RF14 - Modificación y Revisión Manual:** Panel web donde el profesor revisa el veredicto de la IA, modifica la nota o corrige los errores de lectura manualmente si fuera necesario.
* **RF15 - Estampado sobre PDF:** El sistema incrustará (sellará) de manera visual marcas de corrección, checks, cruces y la gran Nota Final directamente sobre las páginas del PDF.

**Reportes e Integración con Google (Drive/Sheets):**
* **RF16 - Organización en Google Drive:** El sistema debe comunicarse con el Google Drive del profesor, creando una estructura de carpetas lógica (Ciclo > Curso > Sección > Unidad).
* **RF17 - Exportación de PDFs Estampados a Drive:** Guardado automático de los exámenes corregidos en la carpeta correspondiente de Drive.
* **RF18 - Tablas de Reporte Web:** Visualización interna en "Reportes" de todo el consolidado de notas, con filtrado cruzado.
* **RF19 - Exportación a Excel y Google Sheets:** Volcado de la lista consolidada de notas directamente en un Google Sheets (o descarga local Excel).
* **RF20 - Formato Avanzado en Sheets:** Enriquecimiento visual del archivo de Google Sheets exportado (colores, identificación de nota media de la clase, cuadros y bordes).
* **RF21 - Dashboards con Chart.js:** Despliegue de gráficos de barras, líneas o pastel en la plataforma web que ilustren el progreso académico de estudiantes, tendencias por sección y analíticas del curso.

**Configuración Visual:**
* **RF22 - Cambio de Tema Oscuro/Claro:** Botón (Toggle) global que cambie dinámicamente el CSS (fondos, fuentes, paneles interactivos, modales) entre claro y oscuro y guarde la preferencia en el cliente.

### Requerimientos No Funcionales (RNF)

* **RNF01 - Diseño Premium y Responsivo:** Uso extensivo de técnicas UI modernas, particularmente *Glassmorphism*, evitando colores planos primitivos, proveyendo micro-interacciones (hover, focus) para sentirse como una herramienta moderna y profesional.
* **RNF02 - Desempeño y Retroalimentación:** Los procesos pesados (escaneo con Gemini API, subida masiva, exportación a Drive) deben ser asíncronos y mostrar siempre *loaders*, barras de progreso e indicadores claros para evitar la frustración del usuario.
* **RNF03 - Fiabilidad de Sincronización:** Los tokens de Google (Drive/Sheets API) deben gestionarse de forma segura para no interrumpir la conectividad.
* **RNF04 - Mantención del Esquema de Datos:** La ampliación de funcionalidades en front-end o lógicas analíticas no debe alterar la arquitectura de la base de datos subyacente.
* **RNF05 - Interacción Predictiva:** La interfaz de corrección interactiva debe minimizar el número de clics requeridos por el profesor (workflow fluido entre calibrar y aprobar).
