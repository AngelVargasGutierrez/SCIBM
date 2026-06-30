import os

append_data = {
    "RF-06.md": """

---

### Flujo Alterno (Excepción: Archivo inválido o sin preguntas)

| # | Usuario | Sistema |
|---|---|---|
| 1 | El docente sube un archivo que no es PDF o es un PDF escaneado sin texto legible (solo imágenes pixeladas). | |
| 2 | | El sistema envía el archivo a la IA para su análisis. |
| 3 | | La IA responde con un arreglo vacío al no encontrar estructura de examen ni texto identificable. |
| 4 | | El sistema oculta el modal de carga y despliega una alerta SweetAlert roja de "Error". |
| 5 | | El sistema indica al usuario: "No se encontraron preguntas legibles. Asegúrese de que el PDF contenga texto." |
| 6 | El docente debe volver a intentarlo subiendo un archivo PDF válido. | |
""",

    "RF-07.md": """

---

### Flujo Alterno (Excepción: Lista Vacía)

| # | Usuario | Sistema |
|---|---|---|
| 1 | El docente borra accidentalmente todas las preguntas de la tabla y presiona "Confirmar Solucionario". | |
| 2 | | El sistema captura la lista (con tamaño 0) y la envía al backend. |
| 3 | | El controlador valida la longitud del arreglo recibido. |
| 4 | | El sistema rechaza la actualización para evitar dañar o borrar por completo el examen sin justificación. |
| 5 | | La interfaz muestra un Toast amarillo `#ffc107` advirtiendo: "No se recibieron preguntas. Analice nuevamente el PDF." |

---

### Flujo Alterno (Excepción: Error de Validación)

| # | Usuario | Sistema |
|---|---|---|
| 1 | El docente ingresa un puntaje extremadamente alto o texto excesivo y presiona Guardar. | |
| 2 | | El sistema intenta guardar los cambios mediante `SaveChangesAsync()`. |
| 3 | | Entity Framework dispara un error de validación en la base de datos debido a que los límites exceden las reglas de negocio. |
| 4 | | El sistema captura el error y devuelve un mensaje detallado a la interfaz. |
| 5 | | La vista muestra una alerta notificando al docente que revise la longitud o el valor numérico de los campos ingresados. |
""",

    "RF-08.md": """

---

### Flujo Alterno (Excepción: Coordenadas Fuera de Rango)

| # | Usuario | Sistema |
|---|---|---|
| 1 | El docente arrastra un pin circular o el rectángulo del sello más allá de los bordes del canvas del PDF. | |
| 2 | | El sistema detecta (mediante Javascript) que las coordenadas (X, Y) calculadas superan el tamaño máximo del contenedor o adquieren un valor negativo. |
| 3 | | El sistema corrige automáticamente el valor, limitándolo al mínimo `0` o al valor máximo del alto/ancho del documento disponible. |
| 4 | | El elemento arrastrado salta visualmente hacia el borde interno más cercano, impidiendo que el docente lo pierda de vista. |
""",

    "RF-09.md": """

---

### Flujo Alterno (Excepción: Sesión Caducada o Sin Permisos)

| # | Usuario | Sistema |
|---|---|---|
| 1 | El docente intenta modificar un solucionario pero su sesión ha expirado tras horas de inactividad, o intenta acceder a la fuerza a un curso que no le pertenece. | |
| 2 | | El sistema recibe la petición HTTP asíncrona y verifica el Token de Autenticación del usuario. |
| 3 | | El sistema determina que el usuario actual no cuenta con privilegios sobre este curso (`DocenteEmail` no coincide con el dueño). |
| 4 | | El sistema bloquea irrevocablemente cualquier guardado o modificación en la base de datos. |
| 5 | | El sistema redirige automáticamente al usuario a la pantalla principal de Inicio de Sesión o le muestra un aviso gigante de "Acceso Denegado". |
""",

    "RF-11.md": """

---

### Flujo Alterno (Excepción: Nombres no Reconocidos)

| # | Usuario | Sistema |
|---|---|---|
| 1 | El docente presiona "Matricular y Vincular" en un registro donde la IA leyó garabatos en lugar de un nombre (ej. "---#--"). | |
| 2 | | El sistema analiza la cadena intentando separarla lógicamente. |
| 3 | | El sistema detecta que la longitud de la cadena es insuficiente (menos de 2 caracteres) o contiene exclusivamente caracteres especiales. |
| 4 | | El sistema bloquea la creación del alumno para evitar corromper la base de datos con usuarios "basura". |
| 5 | | El sistema lanza una alerta Toast roja de error `#dc3545` pidiendo al docente: "Corrija manualmente el nombre del alumno antes de vincular." |
""",

    "RF-15.md": """

---

### Flujo Alterno (Excepción: Autenticación de Google Inválida)

| # | Usuario | Sistema |
|---|---|---|
| 1 | El docente presiona el botón "Exportar a Google Sheets". | |
| 2 | | El sistema entra en modo de carga y contacta a la API de Google Drive utilizando el `accessToken` almacenado del usuario. |
| 3 | | Los servidores de Google rechazan la petición respondiendo con un código de error `401 Unauthorized` porque la sesión caducó o el docente revocó los permisos de la App. |
| 4 | | El sistema detiene inmediatamente el proceso de exportación y oculta el spinner giratorio de la pantalla. |
| 5 | | El sistema dibuja un modal advirtiendo: "Conexión con Google caducada. Por favor, vuelva a vincular su cuenta institucional para poder exportar." |
""",

    "RF-17.md": """

---

### Flujo Alterno (Excepción: Alumno Sin Calificaciones)

| # | Usuario | Sistema |
|---|---|---|
| 1 | El docente ingresa a la vista de reportes de una sección nueva, donde los alumnos están formalmente matriculados pero aún no han rendido ninguna prueba. | |
| 2 | | El sistema realiza la consulta habitual cruzando datos con las tablas de calificaciones. |
| 3 | | El algoritmo de agrupamiento detecta que el arreglo de notas para ese estudiante se encuentra completamente vacío. |
| 4 | | El sistema, previniendo un error por división entre cero, asigna automáticamente un valor nulo o cero al promedio. |
| 5 | | En la máquina de estados lógicos, el sistema obvia las etiquetas habituales y le asigna el estado textual "Sin Datos". |
| 6 | | La tabla (DataTables) reacciona a este texto dibujando una celda con una insignia (Badge) gris neutro `#6c757d` que indica "No Evaluado", descartando la semaforización. |
"""
}

base_path = "NARRATIVA"
for filename, extra_content in append_data.items():
    path = os.path.join(base_path, filename)
    if os.path.exists(path):
        with open(path, "a", encoding="utf-8") as f:
            f.write(extra_content)
        print(f"Agregado flujo de excepcion a {filename}")

