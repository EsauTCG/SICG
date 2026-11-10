namespace Plataforma_CG.Models.ReporteRetrabajo
{
    public class EntradaVisceras
    {
        public DateTime? FechaEntrada { get; set; }
        public int SolicitudProduccionId { get; set; }
        public string SKU { get; set; } = "";
        public string Producto { get; set; } = "";
        public int CantidadEtiquetas { get; set; }
        public decimal TotalKg { get; set; }
        public int ProduccionId { get; set; }
        public string CodigoEtiqueta { get; set; } = "";
    }

    public class SalidasVisceras
    {
        public DateTime Fecha { get; set; }
        public int SolicitudProduccionId { get; set; }
        public string SKU { get; set; } = "";
        public string Producto { get; set; } = "";
        public int CantidadEtiquetas { get; set; }
        public decimal TotalKg { get; set; }
    }

    public class AdvertenciaVisceras
    {
        public int SolicitudProduccionId { get; set; }
        public DateTime PrimeraSalida { get; set; }
        public DateTime UltimaSalida { get; set; }
    }
    public class ReporteViscerasResultado
    {
        public List<EntradaVisceras> Entradas { get; set; } = new();
        public List<SalidasVisceras> Salidas { get; set; } = new();
        public List<AdvertenciaVisceras> Advertencias { get; set; } = new();
    }
}
