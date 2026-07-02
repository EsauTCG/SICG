namespace Plataforma_CG.Models
{
    public class PrecioProductoPdfRow
    {
        public string Sku { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Vendedor { get; set; } = "";
        public string Demanda { get; set; } = "";
        public decimal? PrecioBase { get; set; }

        public PrecioCanalPdf? PrecioSpot { get; set; }
        public PrecioCanalPdf? PrecioActivo { get; set; }
        public PrecioCanalPdf? PrecioEstrategico { get; set; }

        public List<PrecioClienteTag>? Clientes { get; set; }
    }

    public class PrecioCanalPdf
    {
        public decimal? Descuento { get; set; }
        public decimal? PrecioFinal { get; set; }
        public bool EsNoVender { get; set; }
    }

    public class PrecioClienteTag
    {
        public string CodigoCliente { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Canal { get; set; } = "";
    }

    public class PrecioProductoPdfResult
    {
        public List<PrecioProductoPdfRow> Datos { get; set; } = new();
        public int Total { get; set; }
    }
}
