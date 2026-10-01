using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Plataforma_CG.ViewModels.DashboardVentas
{
    public sealed class DashboardVentasCatalogosVm
    {
        [JsonPropertyName("anios")]
        public List<int> Anios { get; set; } = new();

        [JsonPropertyName("masters")]
        public List<string> Masters { get; set; } = new();

        [JsonPropertyName("skus")]
        public List<DashboardSkuCatalogoVm> Skus { get; set; } = new();

        [JsonPropertyName("vendedores")]
        public List<DashboardVendedorCatalogoVm> Vendedores { get; set; } = new();
    }

    public sealed class DashboardSkuCatalogoVm
    {
        [JsonPropertyName("sku")]
        public string Sku { get; set; } = "";

        [JsonPropertyName("nombre")]
        public string Nombre { get; set; } = "";

        [JsonPropertyName("master")]
        public string Master { get; set; } = "SIN_MASTER";
    }

    public sealed class DashboardVendedorCatalogoVm
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("nombre")]
        public string Nombre { get; set; } = "";
    }

    public sealed class DashboardVentasResumenVm
    {
        [JsonPropertyName("anio")]
        public int Anio { get; set; }

        [JsonPropertyName("mes")]
        public int Mes { get; set; }

        [JsonPropertyName("dia")]
        public int Dia { get; set; }

        [JsonPropertyName("fechaCorte")]
        public DateTime FechaCorte { get; set; }

        [JsonPropertyName("diasLaborablesMes")]
        public int DiasLaborablesMes { get; set; }

        [JsonPropertyName("diaLaboral")]
        public int DiaLaboral { get; set; }

        [JsonPropertyName("ventaReal")]
        public decimal VentaReal { get; set; }

        [JsonPropertyName("presupuestoMensual")]
        public decimal PresupuestoMensual { get; set; }

        [JsonPropertyName("alcance")]
        public decimal Alcance { get; set; }

        [JsonPropertyName("referencia")]
        public decimal Referencia { get; set; }

        [JsonPropertyName("cumplimientoPct")]
        public decimal CumplimientoPct { get; set; }

        [JsonPropertyName("brechaAlcanceKg")]
        public decimal BrechaAlcanceKg { get; set; }

        [JsonPropertyName("brechaAlcancePct")]
        public decimal BrechaAlcancePct { get; set; }

        [JsonPropertyName("compararContra")]
        public string CompararContra { get; set; } = "presupuesto";

        [JsonPropertyName("ultimaFechaVenta")]
        public DateTime? UltimaFechaVenta { get; set; }

        [JsonPropertyName("consultadoEn")]
        public DateTime ConsultadoEn { get; set; }
    }

    public sealed class DashboardMasterItemVm
    {
        [JsonPropertyName("master")]
        public string Master { get; set; } = "SIN_MASTER";

        [JsonPropertyName("ventaReal")]
        public decimal VentaReal { get; set; }

        [JsonPropertyName("presupuestoMensual")]
        public decimal PresupuestoMensual { get; set; }

        [JsonPropertyName("alcance")]
        public decimal Alcance { get; set; }

        [JsonPropertyName("referencia")]
        public decimal Referencia { get; set; }

        [JsonPropertyName("participacionPct")]
        public decimal ParticipacionPct { get; set; }

        [JsonPropertyName("avancePct")]
        public decimal AvancePct { get; set; }
    }

    public sealed class DashboardVendedorItemVm
    {
        [JsonPropertyName("vendedorId")]
        public int VendedorId { get; set; }

        [JsonPropertyName("vendedor")]
        public string Vendedor { get; set; } = "SIN VENDEDOR";

        [JsonPropertyName("ventaReal")]
        public decimal VentaReal { get; set; }

        [JsonPropertyName("presupuestoMensual")]
        public decimal PresupuestoMensual { get; set; }

        [JsonPropertyName("alcance")]
        public decimal Alcance { get; set; }

        [JsonPropertyName("referencia")]
        public decimal Referencia { get; set; }

        [JsonPropertyName("cumplimientoPct")]
        public decimal CumplimientoPct { get; set; }
    }

    public sealed class DashboardPrecioItemVm
    {
        [JsonPropertyName("vendedorId")]
        public int VendedorId { get; set; }

        [JsonPropertyName("vendedor")]
        public string Vendedor { get; set; } = "SIN VENDEDOR";

        [JsonPropertyName("precioPonderado")]
        public decimal PrecioPonderado { get; set; }

        [JsonPropertyName("kilos")]
        public decimal Kilos { get; set; }
    }

    public sealed class DashboardPreciosVm
    {
        [JsonPropertyName("precioPromedioPonderado")]
        public decimal PrecioPromedioPonderado { get; set; }

        [JsonPropertyName("items")]
        public List<DashboardPrecioItemVm> Items { get; set; } = new();

        [JsonPropertyName("nota")]
        public string Nota { get; set; } = "";
    }

    public sealed class DashboardTendenciaItemVm
    {
        [JsonPropertyName("diaLaboral")]
        public int DiaLaboral { get; set; }

        [JsonPropertyName("fecha")]
        public DateTime Fecha { get; set; }

        [JsonPropertyName("ventaAcumulada")]
        public decimal? VentaAcumulada { get; set; }

        [JsonPropertyName("alcanceAcumulado")]
        public decimal AlcanceAcumulado { get; set; }

        [JsonPropertyName("brecha")]
        public decimal? Brecha { get; set; }
    }

    public sealed class DashboardTendenciaVm
    {
        [JsonPropertyName("presupuestoMensual")]
        public decimal PresupuestoMensual { get; set; }

        [JsonPropertyName("diasLaborablesMes")]
        public int DiasLaborablesMes { get; set; }

        [JsonPropertyName("items")]
        public List<DashboardTendenciaItemVm> Items { get; set; } = new();
    }
}
