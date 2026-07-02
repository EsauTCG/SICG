using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data.Entities;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Chat;
using Plataforma_CG.Models.Reportes;
using Plataforma_CG.ViewModels;
using Plataforma_CG.Views.Sidebar;

namespace Plataforma_CG.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // =========================
        // ========== DBSETS =========
        // =========================

        public DbSet<PresupuestoVendedor> PresupuestoVendedor { get; set; }

        public DbSet<OrdenVenta> OrdenVenta { get; set; }
        public DbSet<OrdenVentaProducto> OrdenVentaProducto { get; set; }

        public DbSet<Presupuesto> Presupuestos { get; set; }
        public DbSet<Plataforma_CG.Models.PresupuestoCedis> PresupuestoCedis { get; set; } = default!;

        public DbSet<ArticuloSap> ArticuloSap { get; set; }

        public DbSet<Presentacion> Presentacion { get; set; }
        public DbSet<ClienteSap> ClienteSap { get; set; }

        public DbSet<CatalogoPrecioSap> CatalogoPrecioSap { get; set; }

        public DbSet<PedidoVenta> PedidoVenta { get; set; }
        public DbSet<PedidoVentaProducto> PedidoVentaProducto { get; set; }

        public DbSet<UsuarioSQL> UsuarioSQL { get; set; }
        public DbSet<UsuarioAD> UsuarioAD { get; set; }

        // Subpedido
        public DbSet<Subpedido> Subpedidos => Set<Subpedido>();
        public DbSet<SubpedidoProducto> SubpedidoProductos => Set<SubpedidoProducto>();

        public DbSet<Transferencia> Transferencias { get; set; }       

        public DbSet<TransferenciaDetalle> TransferenciaDetalles { get; set; }

        public DbSet<Series> Series { get; set; }

        // Chat
        public DbSet<ChatArea> ChatAreas { get; set; }
        public DbSet<ChatConversacion> ChatConversaciones { get; set; }
        public DbSet<ChatMensaje> ChatMensajes { get; set; }

        // Logs / otros
        public DbSet<PresupuestoLineaHistorico> PresupuestoLineasHistorico { get; set; }
        public DbSet<EntregaSapLog> EntregaSapLogs { get; set; } = null!;
        public DbSet<InventarioScanEtiqueta> InventarioScanEtiquetas { get; set; } = null!;
        public DbSet<PlanDeshueseKpiRow> PlanDeshueseKpiRows { get; set; } = null!;
        public DbSet<Plataforma_CG.Models.SkuConversion> SkuConversion { get; set; } = null!;

        public DbSet<Plantilla> Plantilla { get; set; }
        public DbSet<PlantillaGrupo> PlantillaGrupo { get; set; }
        public DbSet<SkuGrupo> SkuGrupo { get; set; }
        public DbSet<SkuGrupoItem> SkuGrupoItem { get; set; }

        public DbSet<AppSetting> AppSettings => Set<AppSetting>();

        public DbSet<PedidoTransferencia> PedidosTransferencia { get; set; }
        public DbSet<PedidoTransferenciaDetalle> PedidosTransferenciaDetalle { get; set; }

        // =========================
        // ======= VISTAS / DTO =====
        // =========================
        public DbSet<BalanceMasterView> BalanceMasterView { get; set; }
        public DbSet<Factor> Factor { get; set; }
        public DbSet<BalancePresupuestoView> BalancePresupuestoView { get; set; }
        public DbSet<BalancePlanProduccionView> BalancePlanProduccionView { get; set; }
        public DbSet<InventarioSigoView> InventarioSigoView { get; set; }
        public DbSet<ReportePresupuestoViewModel> ReportePresupuestoViewModel { get; set; }

        public DbSet<VOrdenesVentaPorVendedor> VOrdenesVentaPorVendedor { get; set; } = null!;

        public DbSet<DireccionCliente> DireccionesCliente { get; set; }

        public DbSet<RomaneoTransferenciasRowVM> RomaneoTransferencias { get; set; }

        public DbSet<PrecioLineasHistorico> PrecioLineasHistorico { get; set; }

        public DbSet<ReglaComercial> ReglaComercial { get; set; }

        public DbSet<DemandaProducto> DemandaProducto { get; set; }

        public DbSet<DemandaUmbral> DemandaUmbral { get; set; }

        public DbSet<WhatsAppAPI> WhatsAppAPI => Set<WhatsAppAPI>();

        public DbSet<WhatsAppDestino> WhatsAppDestino => Set<WhatsAppDestino>();

        public DbSet<VentasHistoricas> VentasHistoricas { get; set; }

        public DbSet<ModulosSistema> ModulosSistema { get; set; }

        public DbSet<Perfil> Perfiles { get; set; }
        public DbSet<PerfilPermisoModulo> PerfilPermisoModulo { get; set; }

        public DbSet<InventarioSistemas> InventarioSistemas { get; set; }
        public DbSet<MovimientoInventario> MovimientoInventario { get; set; }
        public DbSet<RegistroHistorial> RegistroHistorial { get; set; }

        public DbSet<ControlRedIp> ControlIPs { get; set; }
        public DbSet<VlanRed> VlanRedes { get; set; }
        public DbSet<LogMovimientoRed> LogsMovimientoRed { get; set; }
        public DbSet<PrecioCompetenciaSemana> PrecioCompetenciaSemana { get; set; }

        public DbSet<UsuarioModel> UsuariosAutoArticulos { get; set; }
        public DbSet<CategoriaModel> CategoriasAutoArticulos { get; set; }
        public DbSet<PermisoModel> PermisosAutoArticulos { get; set; }
        public DbSet<LogExcepcionModel> LogsExcepcionesArticulos { get; set; }
        public DbSet<PinArticulosModel> PinArticulos { get; set; }

        public DbSet<ListaPreciosSap> ListaPreciosSap { get; set; }

        //==================================
        // Reporteador(Reportes)
        //Desmadre Diego
        //===================================
        public DbSet<OrdenVentaCabecera> OrdenVentaCabecera { get; set; }
        public DbSet<DetallesClientesCabecera> DetallesClientes { get; set; }
        public DbSet<TransferenciasDetallesCabecera> TransferenciasDetallesCabeceras { get; set; }

        public DbSet<UsuarioSerie> UsuarioSeries { get; set; }

        // =========================
        // ======= MODEL CONFIG =====
        // =========================
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =========================
            // OrdenVenta ↔ OrdenVentaProducto
            // =========================
            modelBuilder.Entity<OrdenVenta>()
                .HasMany(o => o.Productos)
                .WithOne(p => p.Pedido)
                .HasForeignKey(p => p.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);

            // 👇 Desactivar OUTPUT para OrdenVentaProducto (tú ya lo necesitabas)
            modelBuilder.Entity<OrdenVentaProducto>()
                .ToTable(tb => tb.UseSqlOutputClause(false));

            // =========================
            // PedidoVentaProducto
            // =========================
            modelBuilder.Entity<PedidoVentaProducto>(e =>
            {
                e.ToTable("PedidoVentaProducto");
                e.HasKey(x => x.Id);

                e.Property(x => x.ProductoCodigo).HasMaxLength(50);
                e.Property(x => x.ProductoNombre).HasMaxLength(200);

                e.Property(x => x.Almacen)
                    .HasColumnName("Almacen")
                    .HasMaxLength(50)
                    .IsUnicode(true)
                    .ValueGeneratedNever();
            });

            // =========================
            // PedidoVenta ↔ PedidoVentaProducto
            // =========================
            modelBuilder.Entity<PedidoVenta>(e =>
            {
                e.ToTable("PedidoVenta");
                e.HasKey(x => x.Id);

                e.Property(x => x.OrdenVentaConsecutivo).HasMaxLength(50);
                e.Property(x => x.Cliente).HasMaxLength(200);
                e.Property(x => x.Vendedor).HasMaxLength(100);
                e.Property(x => x.AlmacenSurtir).HasMaxLength(50);
                e.Property(x => x.ObservacionGestion).HasMaxLength(1000);

                e.Property(x => x.TotalImporte).HasColumnType("decimal(18,2)");
                e.Property(x => x.TotalPeso).HasColumnType("decimal(18,3)");

                e.HasMany(x => x.Productos)
                    .WithOne(x => x.PedidoVenta)
                    .HasForeignKey(x => x.PedidoVentaId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => x.OrdenVentaId);
            });
            // =========================
            // Subpedido (✅ SOLO UNA VEZ)
            // =========================
            modelBuilder.Entity<Subpedido>(e =>
            {
                e.ToTable("Subpedido");
                e.HasKey(x => x.Id);

                e.Property(x => x.ConsecutivoOV).HasMaxLength(50);
                e.Property(x => x.SubFolio).HasMaxLength(70);
                e.Property(x => x.Almacen).HasMaxLength(50);
                e.Property(x => x.Cliente).HasMaxLength(200);
                e.Property(x => x.Vendedor).HasMaxLength(200);

                e.Property(x => x.TotalPeso).HasColumnType("decimal(18,3)");
                e.Property(x => x.TotalImporte).HasColumnType("decimal(18,2)");

                e.HasIndex(x => x.OrdenVentaId);
                e.HasIndex(x => new { x.OrdenVentaId, x.SubFolio }).IsUnique(false);

                // ✅ Usa navegación si existe; si NO existe, esto compila igual si la prop está en clase
                e.HasOne(x => x.OrdenVenta)
                 .WithMany() // o .WithMany(o => o.Subpedidos) si lo tienes en OrdenVenta
                 .HasForeignKey(x => x.OrdenVentaId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // =========================
            // Chat: Area -> Conversaciones -> Mensajes
            // =========================
            modelBuilder.Entity<ChatArea>()
                .HasMany(a => a.Conversaciones)
                .WithOne(c => c.Area)
                .HasForeignKey(c => c.IdArea);

            modelBuilder.Entity<ChatConversacion>()
                .HasMany(c => c.Mensajes)
                .WithOne(m => m.Conversacion)
                .HasForeignKey(m => m.IdConversacion);

            // =========================
            // EntregaSapLog
            // =========================
            modelBuilder.Entity<EntregaSapLog>(e =>
            {
                e.ToTable("EntregaSapLog");
                e.HasIndex(x => new { x.Referencia, x.Source }).IsUnique();

                e.Property(x => x.Referencia).HasMaxLength(80).IsRequired();
                e.Property(x => x.Source).HasMaxLength(10).IsRequired();
                e.Property(x => x.Mensaje).HasMaxLength(300);
                e.Property(x => x.Usuario).HasMaxLength(80);
                e.Property(x => x.FechaIntento).HasColumnType("datetime2(0)");
            });

            // =========================
            // InventarioScanEtiqueta
            // =========================
            modelBuilder.Entity<InventarioScanEtiqueta>(e =>
            {
                e.ToTable("InventarioScanEtiqueta");
                e.HasKey(x => x.Id);

                e.HasIndex(x => new { x.Almacen, x.CodigoEtiqueta }).IsUnique();

                e.Property(x => x.Almacen).HasMaxLength(20).IsRequired();
                e.Property(x => x.CodigoEtiqueta).HasMaxLength(60).IsRequired();
                e.Property(x => x.Sku).HasMaxLength(60).IsRequired();
                e.Property(x => x.Origen).HasMaxLength(10).IsRequired();
                e.Property(x => x.Usuario).HasMaxLength(120);
            });

            // =========================
            // Plantillas / Grupos
            // =========================
            modelBuilder.Entity<Plantilla>(e =>
            {
                e.ToTable("Plantilla");
                e.HasKey(x => x.PlantillaId);
                e.Property(x => x.Codigo).HasMaxLength(20);
            });

            modelBuilder.Entity<SkuGrupo>(e =>
            {
                e.ToTable("SkuGrupo");
                e.HasKey(x => x.GrupoId);
                e.Property(x => x.MasterSku).HasMaxLength(50);
            });

            modelBuilder.Entity<SkuGrupoItem>(e =>
            {
                e.ToTable("SkuGrupoItem");
                e.HasKey(x => new { x.GrupoId, x.Sku }); // ✅ compuesto
                e.Property(x => x.Sku).HasMaxLength(50);
                e.Property(x => x.ParentSku).HasMaxLength(50);
                e.Property(x => x.TipoRelacion).HasMaxLength(20);
            });

            modelBuilder.Entity<PlantillaGrupo>(e =>
            {
                e.ToTable("PlantillaGrupo");
                e.HasKey(x => new { x.PlantillaId, x.GrupoId }); // ✅ compuesto
            });

            // =========================
            // AppSettings
            // =========================
            modelBuilder.Entity<AppSetting>(e =>
            {
                e.ToTable("AppSettings");
                e.HasKey(x => x.Key);
                e.Property(x => x.Key).HasMaxLength(100);
                e.Property(x => x.Value).HasMaxLength(400).IsRequired();
                e.Property(x => x.UpdatedBy).HasMaxLength(256);
                e.Property(x => x.RowVer).IsRowVersion();
            });

            modelBuilder.Entity<EmbarqueDocumento>()
            .HasOne(ed => ed.Embarque)
            .WithMany(e => e.Documentos)
            .HasForeignKey(ed => ed.EmbarqueId);

            modelBuilder.Entity<PedidoTransferencia>(e =>
            {
                e.ToTable("PedidosTransferencia");
                e.HasKey(x => x.Id);

                e.Property(x => x.Consecutivo)
                    .HasMaxLength(20)
                    .IsRequired();

                e.Property(x => x.Destino)
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.Observacion)
                    .HasMaxLength(500);

                e.Property(x => x.UsuarioSolicita)
                    .HasMaxLength(100);

                e.HasMany(x => x.Detalles)
                    .WithOne(x => x.PedidoTransferencia)
                    .HasForeignKey(x => x.PedidoTransferenciaId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PedidoTransferenciaDetalle>(e =>
            {
                e.ToTable("PedidosTransferenciaDetalle");
                e.HasKey(x => x.Id);

                e.Property(x => x.ProductoCodigo)
                    .HasMaxLength(30)
                    .IsRequired();

                e.Property(x => x.CantidadKg)
                    .HasColumnType("decimal(18,4)");

                e.HasIndex(x => x.PedidoTransferenciaId);
            });

            // =========================
            // ===== VISTAS / NO KEY =====
            // =========================
            modelBuilder.Entity<BalanceMasterView>().HasNoKey();
            modelBuilder.Entity<Factor>().HasNoKey();
            modelBuilder.Entity<BalancePresupuestoView>().HasNoKey();
            modelBuilder.Entity<BalancePlanProduccionView>().HasNoKey();
            modelBuilder.Entity<InventarioSigoView>().HasNoKey();
            modelBuilder.Entity<ReportePresupuestoViewModel>().HasNoKey();

            modelBuilder.Entity<PresupuestoCedisView>().HasNoKey().ToView(null);
            modelBuilder.Entity<OrdenParaMapaView>().HasNoKey();
            modelBuilder.Entity<PalletParaMapaDto>().HasNoKey().ToView(null);

            modelBuilder.Entity<VOrdenesVentaPorVendedor>(e =>
            {
                e.HasNoKey();
                e.ToView("VOrdenesVentaPorVendedor");
            });

            modelBuilder.Entity<PlanDeshueseKpiRow>().HasNoKey();

            modelBuilder.Entity<PlanProduccionRealRow>(e =>
            {
                e.HasNoKey();
                e.ToView(null);
            });

            modelBuilder.Entity<RomaneoTransferenciasRowVM>(e =>
            {
                e.HasNoKey();
                e.ToView("RomaneoTransferencias", "dbo"); // nombre EXACTO de la vista SQL
            });

            modelBuilder.Entity<VentasHistoricas>(entity =>
            {
                entity.ToTable("VentasHistoricas");
                entity.HasNoKey();

                entity.Property(e => e.ClienteID).HasColumnName("ClienteID");
                entity.Property(e => e.SKU).HasColumnName("SKU");
                entity.Property(e => e.Peso).HasColumnName("Peso");
            });

            modelBuilder.Entity<VentaRealRow>().HasNoKey();

            modelBuilder.Entity<PermisoModel>()
            .HasKey(p => new { p.UsuarioId, p.CategoriaId });


            //===========================
            //===   Reporteador       ===
            //===========================
            // Vista OrdenVentaCabecera
            modelBuilder.Entity<OrdenVentaCabecera>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("vw_OrdenVentaCabecera");
            });

            //Vista DetallesClientes
            modelBuilder.Entity<DetallesClientesCabecera>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("vw_DetallesClientes");
            });

            //Vista TransferenciaDetalles
            modelBuilder.Entity<TransferenciasDetallesCabecera>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("vw_TransferenciasDetalles_P");
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
