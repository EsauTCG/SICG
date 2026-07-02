using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Models;

namespace Plataforma_CG.Data
{
    public class AppDbContextQR : DbContext
    {
        public AppDbContextQR(DbContextOptions<AppDbContextQR> options) : base(options) { }

        public DbSet<Embarque> Embarque { get; set; }
        public DbSet<EmbarqueOrden> EmbarqueOrden { get; set; }
        public DbSet<EmbarqueQR> EmbarqueQR { get; set; }
        public DbSet<EmbarqueDocumento> EmbarqueDocumento { get; set; }
        public DbSet<EmbarqueArchivo> EmbarqueArchivo { get; set; }
        public DbSet<EmbarqueProductoTemperatura> EmbarqueProductoTemperaturas { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ================================================
            // RELACIÓN 1:N  Embarque → EmbarqueOrdenes
            // ================================================
            modelBuilder.Entity<EmbarqueOrden>()
                .HasOne(eo => eo.Embarque)
                .WithMany(e => e.Ordenes)
                .HasForeignKey(eo => eo.EmbarqueId);

            // ================================================
            // RELACIÓN 1:1  Embarque → EmbarqueQR
            // ================================================
            modelBuilder.Entity<Embarque>()
                .HasOne(e => e.QR)
                .WithOne(q => q.Embarque)
                .HasForeignKey<EmbarqueQR>(q => q.EmbarqueId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Embarque>()
                .Property(e => e.Consecutivo)
                .ValueGeneratedOnAdd(); // 👈 CLAVE

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EmbarqueArchivo>()
                .HasOne(a => a.Embarque)
                .WithMany(e => e.Archivos)
                .HasForeignKey(a => a.EmbarqueId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EmbarqueArchivo>()
                .Property(a => a.Tipo)
                .HasMaxLength(50);

            modelBuilder.Entity<EmbarqueArchivo>()
                .Property(a => a.RutaArchivo)
                .HasMaxLength(500);

            modelBuilder.Entity<EmbarqueArchivo>()
                .Property(a => a.UsuarioRegistro)
                .HasMaxLength(150);

            // ================================================
            // RELACIÓN 1:N  Embarque → EmbarqueDocumento
            // ================================================
            modelBuilder.Entity<EmbarqueDocumento>()
                .HasOne(ed => ed.Embarque)
                .WithMany(e => e.Documentos)
                .HasForeignKey(ed => ed.EmbarqueId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EmbarqueProductoTemperatura>(entity =>
            {
                entity.ToTable("EmbarqueProductoTemperatura");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.TipoDocumento)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.DocumentoConsecutivo)
                    .HasMaxLength(80);

                entity.Property(x => x.ProductoCodigo)
                    .HasMaxLength(80)
                    .IsRequired();

                entity.Property(x => x.ProductoNombre)
                    .HasMaxLength(250);

                entity.Property(x => x.Almacen)
                    .HasMaxLength(80);

                entity.Property(x => x.Kilos)
                    .HasColumnType("decimal(18,2)");

                entity.Property(x => x.Temperatura)
                    .HasColumnType("decimal(10,2)");

                entity.Property(x => x.Observaciones)
                    .HasMaxLength(500);

                entity.Property(x => x.UsuarioRegistro)
                    .HasMaxLength(256);

                entity.Property(x => x.UsuarioActualiza)
                    .HasMaxLength(256);

                entity.HasIndex(x => new
                {
                    x.EmbarqueId,
                    x.TipoDocumento,
                    x.DocumentoId,
                    x.OrigenDetalleId
                }).IsUnique();

                entity.HasOne(x => x.Embarque)
                    .WithMany(x => x.ProductosTemperatura)
                    .HasForeignKey(x => x.EmbarqueId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
