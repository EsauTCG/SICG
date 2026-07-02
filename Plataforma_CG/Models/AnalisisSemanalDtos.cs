namespace Plataforma_CG.Models
{
    public class PrecioCompetenciaDto
    {
        public DateTime? FechaCorte { get; set; }
        public string Sku { get; set; } = string.Empty;
        public decimal? Denes { get; set; }
        public decimal? Tc { get; set; }
        public decimal? Freasa { get; set; }
        public string? Comentarios { get; set; }
    }

    public class AnalisisSemanalPrecioDto
    {
        public string Clasificacion { get; set; } = "";
        public string Master { get; set; } = "";
        public string Sku { get; set; } = "";
        public string SkuProd { get; set; } = "";

        public decimal InvInicialRefer { get; set; }
        public decimal InventarioAnterior { get; set; }
        public decimal InventarioActual { get; set; }
        public decimal InventarioIdeal { get; set; }
        public decimal Pedidos { get; set; }
        public decimal KgVenta { get; set; }
        public decimal DiasInventario { get; set; }

        public decimal Semana1 { get; set; }
        public decimal Semana2 { get; set; }
        public decimal Semana3 { get; set; }

        public decimal PpVentaReal { get; set; }     

        public string Competidores { get; set; } = "";
        public decimal Prom { get; set; }
        public decimal DifPct { get; set; }
        public string Recomendacion { get; set; } = "";
        public string Comentarios { get; set; } = "";
    }
}
