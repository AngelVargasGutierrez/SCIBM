-- SCRIPT COMPLETO DE CONFIGURACIÓN DE LA BASE DE DATOS SCIBM
-- Este script crea la base de datos desde cero e inicializa todas las tablas.

USE master;
GO

IF EXISTS (SELECT name FROM master.dbo.sysdatabases WHERE name = N'ScibmDB')
BEGIN
    ALTER DATABASE ScibmDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE ScibmDB;
END
GO

CREATE DATABASE ScibmDB;
GO

USE ScibmDB;
GO

-- 2. Eliminar tablas si ya existen
DROP TABLE IF EXISTS RespuestaAlumno;
DROP TABLE IF EXISTS ExamenAlumno;
DROP TABLE IF EXISTS Pregunta;
DROP TABLE IF EXISTS Examen;
DROP TABLE IF EXISTS Unidad;
DROP TABLE IF EXISTS AlumnoMatriculado;
DROP TABLE IF EXISTS Seccion;
DROP TABLE IF EXISTS Curso;
DROP TABLE IF EXISTS CicloAcademico;
DROP TABLE IF EXISTS Carrera;
DROP TABLE IF EXISTS EscuelaProfesional;
DROP TABLE IF EXISTS Facultad;
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

-- 4. Crear Tabla Facultad
CREATE TABLE Facultad (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Nombre NVARCHAR(150) NOT NULL,
    Siglas NVARCHAR(20) NOT NULL,
    CONSTRAINT PK_Facultad PRIMARY KEY (Id)
);

-- 5. Crear Tabla EscuelaProfesional
CREATE TABLE EscuelaProfesional (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    FacultadId UNIQUEIDENTIFIER NOT NULL,
    Nombre NVARCHAR(150) NOT NULL,
    Siglas NVARCHAR(20) NOT NULL,
    CONSTRAINT PK_EscuelaProfesional PRIMARY KEY (Id),
    CONSTRAINT FK_Escuela_Facultad FOREIGN KEY (FacultadId) REFERENCES Facultad(Id) ON DELETE CASCADE
);

-- 6. Crear Tabla Carrera
CREATE TABLE Carrera (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    EscuelaProfesionalId UNIQUEIDENTIFIER NOT NULL,
    Nombre NVARCHAR(150) NOT NULL,
    CiclosTotales INT NOT NULL DEFAULT 10,
    CONSTRAINT PK_Carrera PRIMARY KEY (Id),
    CONSTRAINT FK_Carrera_Escuela FOREIGN KEY (EscuelaProfesionalId) REFERENCES EscuelaProfesional(Id) ON DELETE CASCADE
);

-- 7. Crear Tabla CicloAcademico
CREATE TABLE CicloAcademico (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Nombre NVARCHAR(50) NOT NULL,
    DocenteEmail NVARCHAR(150) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_CicloAcademico PRIMARY KEY (Id),
    CONSTRAINT FK_CicloAcademico_Docente FOREIGN KEY (DocenteEmail) REFERENCES Docente(Email) ON DELETE CASCADE
);

-- 8. Crear Tabla Curso
CREATE TABLE Curso (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Nombre NVARCHAR(150) NOT NULL,
    Codigo NVARCHAR(50) NOT NULL,
    CicloAcademicoId UNIQUEIDENTIFIER NOT NULL,
    CarreraId UNIQUEIDENTIFIER NOT NULL,
    CicloRomano NVARCHAR(20) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Curso PRIMARY KEY (Id),
    CONSTRAINT FK_Curso_CicloAcademico FOREIGN KEY (CicloAcademicoId) REFERENCES CicloAcademico(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Curso_Carrera FOREIGN KEY (CarreraId) REFERENCES Carrera(Id) ON DELETE CASCADE
);

-- 9. Crear Tabla Seccion
CREATE TABLE Seccion (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CursoId UNIQUEIDENTIFIER NOT NULL,
    Nombre NVARCHAR(100) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Seccion PRIMARY KEY (Id),
    CONSTRAINT FK_Seccion_Curso FOREIGN KEY (CursoId) REFERENCES Curso(Id) ON DELETE CASCADE
);

-- 10. Crear Tabla AlumnoMatriculado
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

-- 11. Crear Tabla Unidad
CREATE TABLE Unidad (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    SeccionId UNIQUEIDENTIFIER NOT NULL,
    NombreUnidad NVARCHAR(50) NOT NULL, 
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Unidad PRIMARY KEY (Id),
    CONSTRAINT FK_Unidad_Seccion FOREIGN KEY (SeccionId) REFERENCES Seccion(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_Seccion_Unidad UNIQUE (SeccionId, NombreUnidad)
);

-- 12. Crear Tabla Examen (Versiones Múltiples)
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

-- 13. Crear Tabla Pregunta
CREATE TABLE Pregunta (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ExamenId UNIQUEIDENTIFIER NOT NULL,
    PreguntaPadreId UNIQUEIDENTIFIER NULL,
    Inciso NVARCHAR(10) NULL,
    NumeroPregunta INT NOT NULL,
    Pagina INT NOT NULL DEFAULT 1,
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
    CONSTRAINT FK_Pregunta_PreguntaPadre FOREIGN KEY (PreguntaPadreId) REFERENCES Pregunta(Id),
    CONSTRAINT UQ_Examen_NumeroPregunta UNIQUE (ExamenId, NumeroPregunta, Inciso)
);

-- 14. Crear Tabla ExamenAlumno
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

-- 15. Crear Tabla RespuestaAlumno
CREATE TABLE RespuestaAlumno (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ExamenAlumnoId UNIQUEIDENTIFIER NOT NULL,
    NumeroPregunta INT NOT NULL,
    Inciso NVARCHAR(10) NULL,
    RespuestaDada NVARCHAR(150) NOT NULL,
    EsCorrecta BIT NOT NULL,
    CONSTRAINT PK_RespuestaAlumno PRIMARY KEY (Id),
    CONSTRAINT FK_RespuestaAlumno_ExamenAlumno FOREIGN KEY (ExamenAlumnoId) REFERENCES ExamenAlumno(Id) ON DELETE CASCADE
);

-- INSERCIONES DE DISTRIBUCION (DATOS INICIALES)

DECLARE @FacId UNIQUEIDENTIFIER;
DECLARE @EscId UNIQUEIDENTIFIER;

-- FACSA
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, N'Facultad de Ciencias de la Salud', N'FACSA');

  -- EPMH
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, N'Medicina Humana', N'EPMH');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, N'Medicina Humana', 12);

  -- EPO
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, N'Odontología', N'EPO');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, N'Odontología', 10);

  -- EPTM
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, N'Tecnología Médica', N'EPTM');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, N'Terapia Física y Rehabilitación', 10);
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, N'Laboratorio Clínico y Anatomía Patológica', 10);

-- FAING
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, N'Facultad de Ingeniería', N'FAING');

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Ingeniería Civil', N'EPIC');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Ingeniería Civil', 10 FROM EscuelaProfesional WHERE Siglas = N'EPIC';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Ingeniería de Sistemas', N'EPIS');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Ingeniería de Sistemas', 10 FROM EscuelaProfesional WHERE Siglas = N'EPIS';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Ingeniería Electrónica', N'EPIE');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Ingeniería Electrónica', 10 FROM EscuelaProfesional WHERE Siglas = N'EPIE';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Ingeniería Industrial', N'EPII');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Ingeniería Industrial', 10 FROM EscuelaProfesional WHERE Siglas = N'EPII';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Ingeniería Ambiental', N'EPIA');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Ingeniería Ambiental', 10 FROM EscuelaProfesional WHERE Siglas = N'EPIA';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Ingeniería Agroindustrial', N'EPAG');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Ingeniería Agroindustrial', 10 FROM EscuelaProfesional WHERE Siglas = N'EPAG';

-- FACEM
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, N'Facultad de Ciencias Empresariales', N'FACEM');

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Administración de Negocios Internacionales', N'EPAN');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Administración de Negocios Internacionales', 10 FROM EscuelaProfesional WHERE Siglas = N'EPAN';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Administración Turístico-Hotelera', N'EPAT');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Administración Turístico-Hotelera', 10 FROM EscuelaProfesional WHERE Siglas = N'EPAT';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Ciencias Contables y Financieras', N'EPCC_FACEM');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Ciencias Contables y Financieras', 10 FROM EscuelaProfesional WHERE Siglas = N'EPCC_FACEM';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Ingeniería Comercial', N'EPICOM');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Ingeniería Comercial', 10 FROM EscuelaProfesional WHERE Siglas = N'EPICOM';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Economía y Microfinanzas', N'EPEM');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Economía y Microfinanzas', 10 FROM EscuelaProfesional WHERE Siglas = N'EPEM';

-- FAEDCOH
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, N'Facultad de Educación, Comunicación y Humanidades', N'FAEDCOH');

  -- EPED
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, N'Educación', N'EPED');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, N'Educación Inicial', 10);
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, N'Educación Primaria', 10);
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, N'Educación Física y Deportes', 10);

  -- EPCC (Comunicacion)
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Ciencias de la Comunicación', N'EPCC_FAEDCOH');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Ciencias de la Comunicación', 10 FROM EscuelaProfesional WHERE Siglas = N'EPCC_FAEDCOH';

  -- EPPS
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Psicología', N'EPPS');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Psicología', 10 FROM EscuelaProfesional WHERE Siglas = N'EPPS';

-- FADE
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, N'Facultad de Derecho y Ciencias Políticas', N'FADE');

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, N'Derecho', N'EPD');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, N'Derecho', 12 FROM EscuelaProfesional WHERE Siglas = N'EPD';

-- FAU
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, N'Facultad de Arquitectura y Urbanismo', N'FAU');

  -- EPAU
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, N'Arquitectura y Urbanismo', N'EPAU');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, N'Arquitectura', 10);
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, N'Urbanismo', 10);

PRINT 'Base de datos, tablas y datos iniciales (UPT) creados exitosamente.';
GO
