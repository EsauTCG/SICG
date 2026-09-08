using System;

namespace Plataforma_CG.ViewModels
{
    public class EstadoFacturaCobranzaSapViewModel
    {
        public int DocEntry { get; set; }
        public int DocNum { get; set; }
        public string CardCode { get; set; } = "";
        public decimal DocTotal { get; set; }
        public decimal PaidToDate { get; set; }
        public decimal Pendiente { get; set; }
        public string DocumentStatus { get; set; } = "";
        public bool Cancelada { get; set; }
        public DateTime? FechaDocumento { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public string Moneda { get; set; } = "";
    }

    public class PagoAplicadoFacturaSapViewModel
    {
        public int FacturaDocEntry { get; set; }
        public int PagoDocEntry { get; set; }
        public int? PagoDocNum { get; set; }
        public DateTime? FechaPago { get; set; }
        public decimal MontoAplicado { get; set; }
        public string? Referencia { get; set; }
        public string? Comentario { get; set; }
    }
}
