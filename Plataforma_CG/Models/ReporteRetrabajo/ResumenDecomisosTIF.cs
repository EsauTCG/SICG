namespace Plataforma_CG.Models.ReporteRetrabajo
{
    public class ResumenDecomisosTIF
    {

        public DateTime Fecha { get; set; }

        public string SKU { get; set; } = "";

        public string Producto { get; set; } = "";

        public decimal? TotalKg { get; set; }

        public decimal? Porcentaje { get; set; }
    }
}
