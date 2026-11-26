namespace Plataforma_CG.Models.ReporteRetrabajo
{
    public class DetalleNonatosTIF
    {

        public int SolicitudProduccionId { get; set; }

        public int ProduccionId { get; set; }

        public string TipoMovimineto { get; set; } = "";

        public string SKU { get; set; } = "";

        public string Producto { get; set; } = "";

        public decimal Kg { get; set; }

        public DateTime Fecha { get; set; }

        public int? SolicitudOriginalId { get; set; }
    }
}
