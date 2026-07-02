namespace Plataforma_CG.Models
{
    public class OrdenVentaRow
    {

        public int Id { get; set; }
        public string Consecutivo { get; set; } = "";
        public DateTime FechaRegistro { get; set; }
        public DateTime? FechaEntrega { get; set; } // <- IMPORTANTE: nullable
        public string Cliente { get; set; } = "";
        public string? ClienteNombre { get; set; } = "";
        public string Vendedor { get; set; } = "";
        public int Estatus { get; set; }
        public decimal KgTotales { get; set; }
        public decimal Importe { get; set; }

        // ✅ AGREGA ESTA LÍNEA
        public int? VendedorId { get; set; }

        public string Serie { get; set; } = "";

        public string? AutorizacionPendiente { get; set; }
    }
}
