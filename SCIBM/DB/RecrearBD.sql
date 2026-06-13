-- 0. ELIMINAR TABLAS EXISTENTES (Excepto Docente)
-- Para evitar errores de llaves foráneas, borramos en orden inverso
DROP TABLE IF EXISTS RespuestaAlumno;
DROP TABLE IF EXISTS ExamenAlumno;
DROP TABLE IF EXISTS Pregunta;
DROP TABLE IF EXISTS Examen;
DROP TABLE IF EXISTS Unidad;
DROP TABLE IF EXISTS AlumnoMatriculado;
DROP TABLE IF EXISTS Seccion;
DROP TABLE IF EXISTS Curso;
DROP TABLE IF EXISTS CicloAcademico;

-- 1. Crear Tabla de CicloAcademico
CREATE TABLE CicloAcademico (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Nombre NVARCHAR(50) NOT NULL,
    DocenteEmail NVARCHAR(150) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_CicloAcademico PRIMARY KEY (Id),
    CONSTRAINT FK_CicloAcademico_Docente FOREIGN KEY (DocenteEmail) REFERENCES Docente(Email) ON DELETE CASCADE
);

-- 2. Crear Tabla de Curso
CREATE TABLE Curso (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Nombre NVARCHAR(150) NOT NULL,
    Codigo NVARCHAR(50) NOT NULL,
    CicloAcademicoId UNIQUEIDENTIFIER NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Curso PRIMARY KEY (Id),
    CONSTRAINT FK_Curso_CicloAcademico FOREIGN KEY (CicloAcademicoId) REFERENCES CicloAcademico(Id) ON DELETE CASCADE
);

-- 3. Crear Tabla de Seccion
CREATE TABLE Seccion (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CursoId UNIQUEIDENTIFIER NOT NULL,
    Nombre NVARCHAR(100) NOT NULL,
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Seccion PRIMARY KEY (Id),
    CONSTRAINT FK_Seccion_Curso FOREIGN KEY (CursoId) REFERENCES Curso(Id) ON DELETE CASCADE
);

-- 4. Crear Tabla de AlumnoMatriculado
CREATE TABLE AlumnoMatriculado (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CursoId UNIQUEIDENTIFIER NOT NULL,
    NombreCompleto NVARCHAR(250) NOT NULL,
    Apellidos NVARCHAR(120) NOT NULL,
    Nombres NVARCHAR(120) NOT NULL,
    CONSTRAINT PK_AlumnoMatriculado PRIMARY KEY (Id),
    CONSTRAINT FK_AlumnoMatriculado_Curso FOREIGN KEY (CursoId) REFERENCES Curso(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_Curso_Alumno UNIQUE (CursoId, NombreCompleto)
);

-- 5. Crear Tabla de Unidad
CREATE TABLE Unidad (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    SeccionId UNIQUEIDENTIFIER NOT NULL,
    NombreUnidad NVARCHAR(50) NOT NULL, 
    DriveFolderId NVARCHAR(100) NULL,
    CONSTRAINT PK_Unidad PRIMARY KEY (Id),
    CONSTRAINT FK_Unidad_Seccion FOREIGN KEY (SeccionId) REFERENCES Seccion(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_Seccion_Unidad UNIQUE (SeccionId, NombreUnidad)
);

-- 6. Crear Tabla de Examen (Con soporte para Versiones Múltiples)
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

-- 7. Crear Tabla de Pregunta
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

-- 8. Crear Tabla de ExamenAlumno
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

-- 9. Crear Tabla de RespuestaAlumno
CREATE TABLE RespuestaAlumno (
    Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ExamenAlumnoId UNIQUEIDENTIFIER NOT NULL,
    NumeroPregunta INT NOT NULL,
    RespuestaDada NVARCHAR(150) NOT NULL,
    EsCorrecta BIT NOT NULL,
    CONSTRAINT PK_RespuestaAlumno PRIMARY KEY (Id),
    CONSTRAINT FK_RespuestaAlumno_ExamenAlumno FOREIGN KEY (ExamenAlumnoId) REFERENCES ExamenAlumno(Id) ON DELETE CASCADE
);
