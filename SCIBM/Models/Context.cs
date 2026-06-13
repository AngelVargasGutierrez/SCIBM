using System.Data.Entity;
using System.Data.Entity.ModelConfiguration.Conventions;

namespace SCIBM.Models
{
    public class ScibmContext : DbContext
    {
        public ScibmContext() : base("name=ScibmConnection")
        {
            // Desactivar el inicializador automático porque usamos scripts SQL manuales (RecrearBD.sql)
            Database.SetInitializer<ScibmContext>(null);
        }

        public DbSet<CicloAcademico> CiclosAcademicos { get; set; }
        public DbSet<Docente> Docentes { get; set; }
        public DbSet<Curso> Cursos { get; set; }
        public DbSet<Seccion> Secciones { get; set; }
        public DbSet<AlumnoMatriculado> AlumnosMatriculados { get; set; }
        public DbSet<Unidad> Unidades { get; set; }
        public DbSet<Examen> Examenes { get; set; }
        public DbSet<Pregunta> Preguntas { get; set; }
        public DbSet<ExamenAlumno> ExamenesAlumnos { get; set; }
        public DbSet<RespuestaAlumno> RespuestasAlumnos { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            // Evitar pluralización automática de nombres de tablas
            modelBuilder.Conventions.Remove<PluralizingTableNameConvention>();

            // Configurar relación 1 a muchos: Docente -> CicloAcademico (Cascada al borrar docente)
            modelBuilder.Entity<CicloAcademico>()
                .HasRequired(c => c.Docente)
                .WithMany(d => d.CiclosAcademicos)
                .HasForeignKey(c => c.DocenteEmail)
                .WillCascadeOnDelete(true);

            // Configurar relación 1 a muchos: CicloAcademico -> Curso (Cascada al borrar ciclo)
            modelBuilder.Entity<Curso>()
                .HasRequired(c => c.CicloAcademico)
                .WithMany(d => d.Cursos)
                .HasForeignKey(c => c.CicloAcademicoId)
                .WillCascadeOnDelete(true);

            // Configurar relación 1 a muchos: Curso -> Seccion
            modelBuilder.Entity<Seccion>()
                .HasRequired(s => s.Curso)
                .WithMany(c => c.Secciones)
                .HasForeignKey(s => s.CursoId)
                .WillCascadeOnDelete(true);

            // Configurar relación 1 a muchos: Seccion -> AlumnoMatriculado (Cascada)
            modelBuilder.Entity<AlumnoMatriculado>()
                .HasRequired(am => am.Seccion)
                .WithMany(s => s.AlumnosMatriculados)
                .HasForeignKey(am => am.SeccionId)
                .WillCascadeOnDelete(true);

            // Configurar relación 1 a muchos: Seccion -> Unidad (Cascada)
            modelBuilder.Entity<Unidad>()
                .HasRequired(u => u.Seccion)
                .WithMany(s => s.Unidades)
                .HasForeignKey(u => u.SeccionId)
                .WillCascadeOnDelete(true);

            // Configurar relación 1 a muchos: Unidad -> Examen (Versiones Múltiples)
            modelBuilder.Entity<Examen>()
                .HasRequired(e => e.Unidad)
                .WithMany(u => u.Examenes)
                .HasForeignKey(e => e.UnidadId)
                .WillCascadeOnDelete(true);

            // Configurar relación 1 a muchos: Examen -> Pregunta (Cascada)
            modelBuilder.Entity<Pregunta>()
                .HasRequired(p => p.Examen)
                .WithMany(e => e.Preguntas)
                .HasForeignKey(p => p.ExamenId)
                .WillCascadeOnDelete(true);

            // Configurar relación 1 a muchos: Examen -> ExamenAlumno (Cascada)
            modelBuilder.Entity<ExamenAlumno>()
                .HasRequired(ea => ea.Examen)
                .WithMany(e => e.ExamenesAlumnos)
                .HasForeignKey(ea => ea.ExamenId)
                .WillCascadeOnDelete(true);

            // Configurar relación 1 a muchos: ExamenAlumno -> RespuestaAlumno (Cascada)
            modelBuilder.Entity<RespuestaAlumno>()
                .HasRequired(ra => ra.ExamenAlumno)
                .WithMany(ea => ea.RespuestasAlumnos)
                .HasForeignKey(ra => ra.ExamenAlumnoId)
                .WillCascadeOnDelete(true);

            // Configurar relación opcional: AlumnoMatriculado -> ExamenAlumno (Sin cascada para evitar rutas múltiples)
            modelBuilder.Entity<ExamenAlumno>()
                .HasOptional(ea => ea.AlumnoMatriculado)
                .WithMany(am => am.ExamenesAlumnos)
                .HasForeignKey(ea => ea.AlumnoMatriculadoId)
                .WillCascadeOnDelete(false);

            // Restricciones de Unicidad (Índices únicos compuestos)
            
            // 1. Alumno único por seccion
            modelBuilder.Entity<AlumnoMatriculado>()
                .HasIndex(am => new { am.SeccionId, am.NombreCompleto })
                .IsUnique();

            // 2. Nombre de unidad única por seccion
            modelBuilder.Entity<Unidad>()
                .HasIndex(u => new { u.SeccionId, u.NombreUnidad })
                .IsUnique();

            // 2.5 Nombre de version unico por unidad
            modelBuilder.Entity<Examen>()
                .HasIndex(e => new { e.UnidadId, e.NombreVersion })
                .IsUnique();

            // 3. Número de pregunta única por examen
            modelBuilder.Entity<Pregunta>()
                .HasIndex(p => new { p.ExamenId, p.NumeroPregunta })
                .IsUnique();

            // 4. Respuesta única de pregunta por alumno
            modelBuilder.Entity<RespuestaAlumno>()
                .HasIndex(ra => new { ra.ExamenAlumnoId, ra.NumeroPregunta })
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }
    }
}
