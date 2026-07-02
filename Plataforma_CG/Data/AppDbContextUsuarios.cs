using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Models;

namespace Plataforma_CG.Data
{
    public class AppDbContextUsuarios : DbContext
    {
        public AppDbContextUsuarios(DbContextOptions<AppDbContextUsuarios> options) : base(options) { }

        // Puedes dejar este si ya lo usan otras pantallas
        public DbSet<UsuarioSQL> Usuarios { get; set; }

        public DbSet<UsuarioSQL> UsuarioSQL { get; set; }

        public DbSet<Perfil> Perfiles { get; set; }
        public DbSet<UsuarioAD> UsuariosAD { get; set; }

        public DbSet<Vista> Vistas { get; set; }
        public DbSet<Permiso> Permisos { get; set; }

        public DbSet<CarouselPerfil> CarouselPerfil { get; set; }

        public DbSet<Models.Sidebar.SidebarModulo> SidebarModulos { get; set; }
        public DbSet<Models.Sidebar.SidebarPermiso> SidebarPermisos { get; set; }
        public DbSet<Models.Sidebar.SidebarCategoria> SidebarCategorias { get; set; }

        public DbSet<ModulosSistema> ModulosSistema { get; set; }
        public DbSet<PerfilPermisoModulo> PerfilPermisoModulo { get; set; }

        public DbSet<KpiCatalogo> KpiCatalogo { get; set; }
        public DbSet<UsuarioKpiPermiso> UsuarioKpiPermiso { get; set; }
        public DbSet<PerfilKpiPermiso> PerfilKpiPermiso { get; set; }

        public DbSet<Series> Series { get; set; }
        public DbSet<UsuarioSerie> UsuarioSeries { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ✅ IMPORTANTE:
            // Esto evita que EF busque una tabla llamada "Usuarios"
            modelBuilder.Entity<UsuarioSQL>(e =>
            {
                e.ToTable("UsuarioSQL");
                e.HasKey(x => x.Id);
            });

            modelBuilder.Entity<Series>(e =>
            {
                e.ToTable("Series");
                e.HasKey(x => x.Id);
            });

            modelBuilder.Entity<UsuarioSerie>(e =>
            {
                e.ToTable("UsuarioSerie");
                e.HasKey(x => x.Id);

                e.HasIndex(x => new { x.UsuarioId, x.SerieId })
                    .IsUnique();

                e.HasOne(x => x.Usuario)
                    .WithMany(x => x.UsuarioSeries)
                    .HasForeignKey(x => x.UsuarioId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Serie)
                    .WithMany()
                    .HasForeignKey(x => x.SerieId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}