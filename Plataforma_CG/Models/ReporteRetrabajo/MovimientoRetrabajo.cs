namespace Plataforma_CG.Models.ReporteRetrabajo
{
    public class MovimientoRetrabajo
    {
        public string TipoMovimiento { get; set; } = "";
        public int SolicitudProduccionID { get; set; }
        public int ProduccionID { get; set; }

        public DateTime Fecha { get; set; }
        public string CodigoEtiqueta { get; set; } = "";

        public string SKU { get; set; } = "";
        public string Producto { get; set; } = "";

        public decimal Kg { get; set; }

        public string Lote { get; set; } = "";
        public string Almacen { get; set; } = "";
        public string Estatus { get; set; } = "";

        public string Clasificacion { get; set; } = "";
    }

    public class ResumenRetrabajo
    {
        public int SolicitudProduccionID { get; set; }
        public string TipoMovimiento { get; set; } = "";
        public string SKU { get; set; } = "";
        public string Producto { get; set; } = "";

        public int CantidadEtiquetas { get; set; }
        public decimal TotalKg { get; set; }

    }
}
