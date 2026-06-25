# Arquitectura de Almacenamiento de PDFs y Despliegue en Contenedores

Este documento registra el análisis arquitectónico sobre la mejor estrategia para almacenar los PDFs de los exámenes (plantillas y escaneos de alumnos) en SCIBM, especialmente con miras a un futuro despliegue mediante contenedores (Docker).

## Análisis de Volumen de Datos

Aunque un PDF de examen procesado pese un máximo de **800 KB**, a nivel institucional el crecimiento es exponencial:

- **1 Alumno:** ~0.8 MB (800 KB)
- **1 Sección (30 alumnos):** ~24 MB por examen.
- **Ciclo completo por Sección (3 evaluaciones):** ~72 MB.
- **Escala Institucional (ej. 150 secciones):** ~10.8 GB por semestre.

## Opciones de Almacenamiento

### Opción 1: Guardar PDFs en la Base de Datos SQL (`VARBINARY`)
**Veredicto:** ❌ Altamente Desaconsejado

> [!WARNING]
> Guardar archivos binarios masivos en la base de datos SQL degrada severamente el rendimiento y dificulta el mantenimiento.

* **Beneficios:** Garantiza la integridad referencial absoluta (si se borra el registro, se borra el binario de inmediato) y facilita backups monolíticos.
* **Contras:** 
  * Crecimiento desmedido de la base de datos (Gigabytes por semestre).
  * Impacto negativo en el consumo de RAM del servidor SQL al realizar consultas.
  * Backups extremadamente pesados y lentos.

### Opción 2: Almacenamiento en Sistema de Archivos (Actual)
**Veredicto:** ✅ Recomendado para la arquitectura actual

La base de datos almacena únicamente la ruta física (ej. `/App_Data/Examenes/...`) y las llaves foráneas (`AlumnoMatriculadoId`), mientras que el archivo binario reposa en el disco del servidor.

* **Beneficios:**
  * Base de datos extremadamente ligera y rápida.
  * Menor costo computacional al servir los archivos directamente desde disco.
* **El Reto de los Contenedores:** Los contenedores en Docker son efímeros. Si el contenedor se destruye o actualiza, se perdería toda la carpeta interna `/App_Data` y los archivos almacenados.

## Estrategia de Despliegue con Docker

Para mantener el alto rendimiento de la "Opción 2" sin sufrir pérdida de datos en un entorno contenerizado, la solución estándar de la industria es el uso de **Volúmenes Persistentes (Docker Volumes)**.

### Configuración con Volúmenes de Docker

Al desplegar SCIBM usando `docker-compose`, se debe mapear la ruta donde la aplicación guarda los PDFs hacia el disco duro real del servidor anfitrión (*host*).

**Ejemplo de configuración `docker-compose.yml`:**
```yaml
version: '3.8'

services:
  scibm-web:
    image: scibm-app:latest
    ports:
      - "80:80"
    volumes:
      # Formato: /ruta/segura/del/host : /ruta/interna/del/contenedor
      - /mnt/storage/scibm-pdfs:/app/App_Data/Examenes/Calificados
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
```

> [!TIP]
> Al configurar el volumen, la aplicación dentro de Docker "cree" que está guardando el PDF en su propio disco, pero el motor de Docker lo escribe directamente y de forma segura en `/mnt/storage/scibm-pdfs` del servidor principal. Si el contenedor se apaga o se borra, los PDFs permanecen intactos.

## El Siguiente Nivel: Object Storage (S3)

Para una arquitectura 100% *Stateless* y de alta disponibilidad (múltiples servidores atendiendo solicitudes en paralelo), la evolución natural es:

1. Modificar el código C# para enviar el archivo directamente a **Amazon S3, Google Cloud Storage o MinIO** (On-Premise).
2. Guardar la URL del archivo que retorna el servicio en la tabla `ExamenAlumno`.

Esta arquitectura elimina por completo la necesidad de sincronizar discos duros locales entre servidores. Actualmente, la integración que SCIBM tiene con **Google Drive** cumple una función muy similar a esta, actuando como el repositorio final en la nube.
