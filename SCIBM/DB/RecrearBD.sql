-- 0. ELIMINAR TABLAS EXISTENTES
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

-- 1. Crear Tabla Facultad
CREATE TABLE Facultad (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Nombre NVARCHAR(150) NOT NULL,
    Siglas NVARCHAR(20) NOT NULL,
    CONSTRAINT PK_Facultad PRIMARY KEY (Id)
);

-- 2. Crear Tabla EscuelaProfesional
CREATE TABLE EscuelaProfesional (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    FacultadId UNIQUEIDENTIFIER NOT NULL,
    Nombre NVARCHAR(150) NOT NULL,
    Siglas NVARCHAR(20) NOT NULL,
    CONSTRAINT PK_EscuelaProfesional PRIMARY KEY (Id),
    CONSTRAINT FK_Escuela_Facultad FOREIGN KEY (FacultadId) REFERENCES Facultad(Id) ON DELETE CASCADE
);

-- 3. Crear Tabla Carrera
CREATE TABLE Carrera (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    EscuelaProfesionalId UNIQUEIDENTIFIER NOT NULL,
    Nombre NVARCHAR(150) NOT NULL,
    CiclosTotales INT NOT NULL DEFAULT 10,
    CONSTRAINT PK_Carrera PRIMARY KEY (Id),
    CONSTRAINT FK_Carrera_Escuela FOREIGN KEY (EscuelaProfesionalId) REFERENCES EscuelaProfesional(Id) ON DELETE CASCADE
);

-- 4. Crear Tabla de CicloAcademico
CREATE TABLE CicloAcademico (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Nombre NVARCHAR(50) NOT NULL,
    DocenteEmail NVARCHAR(150) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_CicloAcademico PRIMARY KEY (Id),
    CONSTRAINT FK_CicloAcademico_Docente FOREIGN KEY (DocenteEmail) REFERENCES Docente(Email) ON DELETE CASCADE
);

-- 5. Crear Tabla de Curso
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

-- 6. Crear Tabla de Seccion
CREATE TABLE Seccion (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CursoId UNIQUEIDENTIFIER NOT NULL,
    Nombre NVARCHAR(100) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Seccion PRIMARY KEY (Id),
    CONSTRAINT FK_Seccion_Curso FOREIGN KEY (CursoId) REFERENCES Curso(Id) ON DELETE CASCADE
);

-- 7. Crear Tabla de AlumnoMatriculado
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

-- 8. Crear Tabla de Unidad
CREATE TABLE Unidad (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    SeccionId UNIQUEIDENTIFIER NOT NULL,
    NombreUnidad NVARCHAR(50) NOT NULL, 
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Unidad PRIMARY KEY (Id),
    CONSTRAINT FK_Unidad_Seccion FOREIGN KEY (SeccionId) REFERENCES Seccion(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_Seccion_Unidad UNIQUE (SeccionId, NombreUnidad)
);

-- 9. Crear Tabla de Examen (Con soporte para Versiones Múltiples)
CREATE TABLE Examen (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    UnidadId UNIQUEIDENTIFIER NOT NULL,
    NombreVersion NVARCHAR(100) NOT NULL, -- Ej: 'Fila A'
    DriveFolderId NVARCHAR(100) NULL, -- Carpeta en Drive específica para esta versión
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

-- 10. Crear Tabla de Pregunta
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

-- 11. Crear Tabla de ExamenAlumno
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

-- 12. Crear Tabla de RespuestaAlumno
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
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, 'Facultad de Ciencias de la Salud', 'FACSA');

  -- EPMH
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, 'Medicina Humana', 'EPMH');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, 'Medicina Humana', 12);

  -- EPO
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, 'Odontología', 'EPO');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, 'Odontología', 10);

  -- EPTM
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, 'Tecnología Médica', 'EPTM');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, 'Terapia Física y Rehabilitación', 10);
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, 'Laboratorio Clínico y Anatomía Patológica', 10);

-- FAING
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, 'Facultad de Ingeniería', 'FAING');

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Ingeniería Civil', 'EPIC');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Ingeniería Civil', 10 FROM EscuelaProfesional WHERE Siglas = 'EPIC';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Ingeniería de Sistemas', 'EPIS');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Ingeniería de Sistemas', 10 FROM EscuelaProfesional WHERE Siglas = 'EPIS';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Ingeniería Electrónica', 'EPIE');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Ingeniería Electrónica', 10 FROM EscuelaProfesional WHERE Siglas = 'EPIE';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Ingeniería Industrial', 'EPII');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Ingeniería Industrial', 10 FROM EscuelaProfesional WHERE Siglas = 'EPII';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Ingeniería Ambiental', 'EPIA');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Ingeniería Ambiental', 10 FROM EscuelaProfesional WHERE Siglas = 'EPIA';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Ingeniería Agroindustrial', 'EPAG');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Ingeniería Agroindustrial', 10 FROM EscuelaProfesional WHERE Siglas = 'EPAG';

-- FACEM
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, 'Facultad de Ciencias Empresariales', 'FACEM');

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Administración de Negocios Internacionales', 'EPAN');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Administración de Negocios Internacionales', 10 FROM EscuelaProfesional WHERE Siglas = 'EPAN';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Administración Turístico-Hotelera', 'EPAT');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Administración Turístico-Hotelera', 10 FROM EscuelaProfesional WHERE Siglas = 'EPAT';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Ciencias Contables y Financieras', 'EPCC_FACEM');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Ciencias Contables y Financieras', 10 FROM EscuelaProfesional WHERE Siglas = 'EPCC_FACEM';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Ingeniería Comercial', 'EPICOM');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Ingeniería Comercial', 10 FROM EscuelaProfesional WHERE Siglas = 'EPICOM';

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Economía y Microfinanzas', 'EPEM');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Economía y Microfinanzas', 10 FROM EscuelaProfesional WHERE Siglas = 'EPEM';

-- FAEDCOH
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, 'Facultad de Educación, Comunicación y Humanidades', 'FAEDCOH');

  -- EPED
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, 'Educación', 'EPED');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, 'Educación Inicial', 10);
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, 'Educación Primaria', 10);
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, 'Educación Física y Deportes', 10);

  -- EPCC (Comunicacion)
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Ciencias de la Comunicación', 'EPCC_FAEDCOH');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Ciencias de la Comunicación', 10 FROM EscuelaProfesional WHERE Siglas = 'EPCC_FAEDCOH';

  -- EPPS
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Psicología', 'EPPS');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Psicología', 10 FROM EscuelaProfesional WHERE Siglas = 'EPPS';

-- FADE
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, 'Facultad de Derecho y Ciencias Políticas', 'FADE');

  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (NEWID(), @FacId, 'Derecho', 'EPD');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) 
  SELECT Id, Id, 'Derecho', 12 FROM EscuelaProfesional WHERE Siglas = 'EPD';

-- FAU
SET @FacId = NEWID();
INSERT INTO Facultad (Id, Nombre, Siglas) VALUES (@FacId, 'Facultad de Arquitectura y Urbanismo', 'FAU');

  -- EPAU
  SET @EscId = NEWID();
  INSERT INTO EscuelaProfesional (Id, FacultadId, Nombre, Siglas) VALUES (@EscId, @FacId, 'Arquitectura y Urbanismo', 'EPAU');
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, 'Arquitectura', 10);
  INSERT INTO Carrera (Id, EscuelaProfesionalId, Nombre, CiclosTotales) VALUES (NEWID(), @EscId, 'Urbanismo', 10);

PRINT 'Tablas recreadas con exito';
GO
