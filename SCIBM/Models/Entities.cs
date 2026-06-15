using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCIBM.Models
{
    public class Docente
    {
        [Key]
        [StringLength(150)]
        public string Email { get; set; }

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; }

        [Required]
        [StringLength(100)]
        public string Apellido { get; set; }

        [StringLength(100)]
        public string GoogleDriveFolderId { get; set; }

        [StringLength(250)]
        public string RefreshToken { get; set; }

        public DateTime UltimoAcceso { get; set; }

        // Propiedades de navegación
        public virtual ICollection<CicloAcademico> CiclosAcademicos { get; set; }
    }

    public class CicloAcademico
    {
        public CicloAcademico()
        {
            Id = Guid.NewGuid();
            Cursos = new HashSet<Curso>();
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Nombre { get; set; }

        [Required]
        [StringLength(150)]
        [ForeignKey("Docente")]
        public string DocenteEmail { get; set; }

        [StringLength(100)]
        public string DriveFolderId { get; set; }

        // Propiedades de navegación
        public virtual Docente Docente { get; set; }
        public virtual ICollection<Curso> Cursos { get; set; }
    }

    public class Curso
    {
        public Curso()
        {
            Id = Guid.NewGuid();
            Secciones = new HashSet<Seccion>();
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Nombre { get; set; }

        [Required]
        [StringLength(50)]
        public string Codigo { get; set; }

        [Required]
        [ForeignKey("CicloAcademico")]
        public Guid CicloAcademicoId { get; set; }

        [StringLength(100)]
        public string DriveFolderId { get; set; }

        // Propiedades de navegación
        public virtual CicloAcademico CicloAcademico { get; set; }
        public virtual ICollection<Seccion> Secciones { get; set; }
    }

    public class Seccion
    {
        public Seccion()
        {
            Id = Guid.NewGuid();
            AlumnosMatriculados = new HashSet<AlumnoMatriculado>();
            Unidades = new HashSet<Unidad>();
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        [ForeignKey("Curso")]
        public Guid CursoId { get; set; }

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } // Ej: "A", "B", "Sección Única"

        [StringLength(100)]
        public string DriveFolderId { get; set; }

        // Propiedades de navegación
        public virtual Curso Curso { get; set; }
        public virtual ICollection<AlumnoMatriculado> AlumnosMatriculados { get; set; }
        public virtual ICollection<Unidad> Unidades { get; set; }
    }

    public class AlumnoMatriculado
    {
        public AlumnoMatriculado()
        {
            Id = Guid.NewGuid();
            ExamenesAlumnos = new HashSet<ExamenAlumno>();
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        [ForeignKey("Seccion")]
        public Guid SeccionId { get; set; }

        [Required]
        [StringLength(250)]
        public string NombreCompleto { get; set; }

        [Required]
        [StringLength(120)]
        public string Apellidos { get; set; }

        [Required]
        [StringLength(120)]
        public string Nombres { get; set; }

        // Propiedades de navegación
        public virtual Seccion Seccion { get; set; }
        public virtual ICollection<ExamenAlumno> ExamenesAlumnos { get; set; }
    }

    public class Unidad
    {
        public Unidad()
        {
            Id = Guid.NewGuid();
            Examenes = new HashSet<Examen>();
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        [ForeignKey("Seccion")]
        public Guid SeccionId { get; set; }

        [Required]
        [StringLength(50)]
        public string NombreUnidad { get; set; } // Ej: 'U1', 'U1R', 'U2'

        [StringLength(100)]
        public string DriveFolderId { get; set; }

        // Propiedades de navegación
        public virtual Seccion Seccion { get; set; }
        public virtual ICollection<Examen> Examenes { get; set; }
    }

    public class Examen
    {
        public Examen()
        {
            Id = Guid.NewGuid();
            Preguntas = new HashSet<Pregunta>();
            ExamenesAlumnos = new HashSet<ExamenAlumno>();
            SincronizadoDrive = false;
            StampX = 450;
            StampY = 50;
            StampWidth = 100;
            StampHeight = 40;
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        [ForeignKey("Unidad")]
        public Guid UnidadId { get; set; }

        [Required]
        [StringLength(100)]
        public string NombreVersion { get; set; } // Ej: "Fila A"

        [StringLength(100)]
        public string DriveFolderId { get; set; } // Carpeta específica en Drive para la versión

        [Required]
        [StringLength(250)]
        public string RutaPdfOriginal { get; set; }

        [StringLength(100)]
        public string DriveFileIdBlanco { get; set; }

        [StringLength(100)]
        public string DriveFileIdSolucionario { get; set; }

        public bool SincronizadoDrive { get; set; }

        public double StampX { get; set; }
        public double StampY { get; set; }
        public double StampWidth { get; set; }
        public double StampHeight { get; set; }

        // Propiedades de navegación
        public virtual Unidad Unidad { get; set; }
        public virtual ICollection<Pregunta> Preguntas { get; set; }
        public virtual ICollection<ExamenAlumno> ExamenesAlumnos { get; set; }
    }

    public class Pregunta
    {
        public Pregunta()
        {
            Id = Guid.NewGuid();
            SubPreguntas = new HashSet<Pregunta>();
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        [ForeignKey("Examen")]
        public Guid ExamenId { get; set; }

        [ForeignKey("PreguntaPadre")]
        public Guid? PreguntaPadreId { get; set; }

        [StringLength(10)]
        public string Inciso { get; set; }

        [Required]
        public int NumeroPregunta { get; set; }

        [Required]
        public string Enunciado { get; set; }

        [Required]
        [StringLength(30)]
        public string Tipo { get; set; } // 'OpcionMultiple' o 'RespuestaLibre'

        [Required]
        [StringLength(150)]
        public string RespuestaCorrecta { get; set; }

        [Required]
        public double Puntaje { get; set; }

        public double PosX { get; set; }
        public double PosY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public string OpcionesJson { get; set; } // [{"label":"A","x":10,"y":20,"w":5,"h":5},...]

        // Propiedades de navegación
        public virtual Examen Examen { get; set; }
        public virtual Pregunta PreguntaPadre { get; set; }
        public virtual ICollection<Pregunta> SubPreguntas { get; set; }
    }

    public class ExamenAlumno
    {
        public ExamenAlumno()
        {
            Id = Guid.NewGuid();
            RespuestasAlumnos = new HashSet<RespuestaAlumno>();
            SincronizadoDrive = false;
            TieneObservacion = false;
            FechaCalificacion = DateTime.Now;
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        [ForeignKey("Examen")]
        public Guid ExamenId { get; set; }

        [Required]
        [StringLength(250)]
        public string NombreAlumno { get; set; }

        [ForeignKey("AlumnoMatriculado")]
        public Guid? AlumnoMatriculadoId { get; set; }

        [Required]
        public double Nota { get; set; }

        [Required]
        [StringLength(250)]
        public string RutaPdfRespuesta { get; set; }

        [StringLength(100)]
        public string DriveFileId { get; set; }

        public bool SincronizadoDrive { get; set; }

        public bool TieneObservacion { get; set; }

        [StringLength(250)]
        public string Observacion { get; set; }

        public DateTime FechaCalificacion { get; set; }

        // Propiedades de navegación
        public virtual Examen Examen { get; set; }
        public virtual AlumnoMatriculado AlumnoMatriculado { get; set; }
        public virtual ICollection<RespuestaAlumno> RespuestasAlumnos { get; set; }
    }

    public class RespuestaAlumno
    {
        public RespuestaAlumno()
        {
            Id = Guid.NewGuid();
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        [ForeignKey("ExamenAlumno")]
        public Guid ExamenAlumnoId { get; set; }

        [Required]
        public int NumeroPregunta { get; set; }

        [StringLength(10)]
        public string Inciso { get; set; }

        [Required]
        [StringLength(150)]
        public string RespuestaDada { get; set; }

        [Required]
        public bool EsCorrecta { get; set; }

        // Propiedades de navegación
        public virtual ExamenAlumno ExamenAlumno { get; set; }
    }
}
