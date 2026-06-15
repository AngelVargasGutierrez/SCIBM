-- SCRIPT COMPLETO DE CONFIGURACIÓN DE LA BASE DE DATOS SCIBM
-- Este script crea la base de datos desde cero e inicializa todas las tablas.

-- 1. Crear Base de Datos (Si no existe)
IF NOT EXISTS (SELECT name FROM master.dbo.sysdatabases WHERE name = N'ScibmDB')
BEGIN
    CREATE DATABASE ScibmDB;
END
GO

USE ScibmDB;
GO

-- 2. Eliminar tablas si ya existen (para que el script sea re-ejecutable)
DROP TABLE IF EXISTS RespuestaAlumno;
DROP TABLE IF EXISTS ExamenAlumno;
DROP TABLE IF EXISTS Pregunta;
DROP TABLE IF EXISTS Examen;
DROP TABLE IF EXISTS Unidad;
DROP TABLE IF EXISTS AlumnoMatriculado;
DROP TABLE IF EXISTS Seccion;
DROP TABLE IF EXISTS Curso;
DROP TABLE IF EXISTS CicloAcademico;
DROP TABLE IF EXISTS Docente;

-- 3. Crear Tabla Docente (La raíz del sistema)
CREATE TABLE Docente (
    Email NVARCHAR(150) NOT NULL,
    Nombre NVARCHAR(100) NOT NULL,
    Apellido NVARCHAR(100) NOT NULL,
    GoogleDriveFolderId NVARCHAR(100) NULL,
    RefreshToken NVARCHAR(250) NULL,
    UltimoAcceso DATETIME NOT NULL,
    CONSTRAINT PK_Docente PRIMARY KEY (Email)
);

-- 4. Crear Tabla CicloAcademico
CREATE TABLE CicloAcademico (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Nombre NVARCHAR(50) NOT NULL,
    DocenteEmail NVARCHAR(150) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_CicloAcademico PRIMARY KEY (Id),
    CONSTRAINT FK_CicloAcademico_Docente FOREIGN KEY (DocenteEmail) REFERENCES Docente(Email) ON DELETE CASCADE
);

-- 5. Crear Tabla Curso
CREATE TABLE Curso (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Nombre NVARCHAR(150) NOT NULL,
    Codigo NVARCHAR(50) NOT NULL,
    CicloAcademicoId UNIQUEIDENTIFIER NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Curso PRIMARY KEY (Id),
    CONSTRAINT FK_Curso_CicloAcademico FOREIGN KEY (CicloAcademicoId) REFERENCES CicloAcademico(Id) ON DELETE CASCADE
);

-- 6. Crear Tabla Seccion
CREATE TABLE Seccion (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CursoId UNIQUEIDENTIFIER NOT NULL,
    Nombre NVARCHAR(100) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Seccion PRIMARY KEY (Id),
    CONSTRAINT FK_Seccion_Curso FOREIGN KEY (CursoId) REFERENCES Curso(Id) ON DELETE CASCADE
);

-- 7. Crear Tabla AlumnoMatriculado
CREATE TABLE AlumnoMatriculado (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    SeccionId UNIQUEIDENTIFIER NOT NULL,
    NombreCompleto NVARCHAR(250) NOT NULL,
    Apellidos NVARCHAR(120) NOT NULL,
    Nombres NVARCHAR(120) NOT NULL,
    CONSTRAINT PK_AlumnoMatriculado PRIMARY KEY (Id),
    CONSTRAINT FK_AlumnoMatriculado_Seccion FOREIGN KEY (SeccionId) REFERENCES Seccion(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_Seccion_Alumno UNIQUE (SeccionId, NombreCompleto)
);

-- 8. Crear Tabla Unidad
CREATE TABLE Unidad (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    SeccionId UNIQUEIDENTIFIER NOT NULL,
    NombreUnidad NVARCHAR(50) NOT NULL, 
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Unidad PRIMARY KEY (Id),
    CONSTRAINT FK_Unidad_Seccion FOREIGN KEY (SeccionId) REFERENCES Seccion(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_Seccion_Unidad UNIQUE (SeccionId, NombreUnidad)
);

-- 9. Crear Tabla Examen (Versiones Múltiples)
CREATE TABLE Examen (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    UnidadId UNIQUEIDENTIFIER NOT NULL,
    NombreVersion NVARCHAR(100) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    RutaPdfOriginal NVARCHAR(250) NOT NULL,
    DriveFileIdBlanco NVARCHAR(100) NULL,
    DriveFileIdSolucionario NVARCHAR(100) NULL,
    SincronizadoDrive BIT NOT NULL DEFAULT 0,
    StampX FLOAT NOT NULL DEFAULT 450,
    StampY FLOAT NOT NULL DEFAULT 50,
    StampWidth FLOAT NOT NULL DEFAULT 100,
    StampHeight FLOAT NOT NULL DEFAULT 40,
    CONSTRAINT PK_Examen PRIMARY KEY (Id),
    CONSTRAINT FK_Examen_Unidad FOREIGN KEY (UnidadId) REFERENCES Unidad(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_Unidad_Version UNIQUE (UnidadId, NombreVersion)
);

-- 10. Crear Tabla Pregunta
CREATE TABLE Pregunta (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ExamenId UNIQUEIDENTIFIER NOT NULL,
    NumeroPregunta INT NOT NULL,
    Enunciado NVARCHAR(MAX) NOT NULL,
    Tipo NVARCHAR(30) NOT NULL,
    RespuestaCorrecta NVARCHAR(150) NOT NULL,
    Puntaje FLOAT NOT NULL,
    PosX FLOAT NOT NULL,
    PosY FLOAT NOT NULL,
    Width FLOAT NOT NULL,
    Height FLOAT NOT NULL,
    OpcionesJson NVARCHAR(MAX) NULL,
    CONSTRAINT PK_Pregunta PRIMARY KEY (Id),
    CONSTRAINT FK_Pregunta_Examen FOREIGN KEY (ExamenId) REFERENCES Examen(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_Examen_NumeroPregunta UNIQUE (ExamenId, NumeroPregunta)
);

-- 11. Crear Tabla ExamenAlumno
CREATE TABLE ExamenAlumno (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ExamenId UNIQUEIDENTIFIER NOT NULL,
    NombreAlumno NVARCHAR(250) NOT NULL,
    AlumnoMatriculadoId UNIQUEIDENTIFIER NULL,
    Nota FLOAT NOT NULL,
    RutaPdfRespuesta NVARCHAR(250) NOT NULL,
    DriveFileId NVARCHAR(100) NULL,
    SincronizadoDrive BIT NOT NULL DEFAULT 0,
    TieneObservacion BIT NOT NULL DEFAULT 0,
    Observacion NVARCHAR(250) NULL,
    FechaCalificacion DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_ExamenAlumno PRIMARY KEY (Id),
    CONSTRAINT FK_ExamenAlumno_Examen FOREIGN KEY (ExamenId) REFERENCES Examen(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ExamenAlumno_Alumno FOREIGN KEY (AlumnoMatriculadoId) REFERENCES AlumnoMatriculado(Id)
);

-- 12. Crear Tabla RespuestaAlumno
CREATE TABLE RespuestaAlumno (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ExamenAlumnoId UNIQUEIDENTIFIER NOT NULL,
    NumeroPregunta INT NOT NULL,
    RespuestaDada NVARCHAR(150) NOT NULL,
    EsCorrecta BIT NOT NULL,
    CONSTRAINT PK_RespuestaAlumno PRIMARY KEY (Id),
    CONSTRAINT FK_RespuestaAlumno_ExamenAlumno FOREIGN KEY (ExamenAlumnoId) REFERENCES ExamenAlumno(Id) ON DELETE CASCADE
);

PRINT 'Base de datos y tablas creadas exitosamente.';
