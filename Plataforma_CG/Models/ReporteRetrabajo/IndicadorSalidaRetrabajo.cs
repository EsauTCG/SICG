namespace Plataforma_CG.Models.ReporteRetrabajo
{
    public class IndicadorSalidaRetrabajo
    {
        public DateTime Fecha { get; set; }

        public string SKU { get; set; } = "";

        public string Producto { get; set; } = "";

        public int CantidadEtiquetas { get; set; }

        public decimal TotalKg { get; set; }
    }
}
