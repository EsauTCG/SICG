using Microsoft.AspNetCore.Http;

namespace Plataforma_CG.ViewModels
{
    public class CompromisoPagoFacturaViewModel
    {
        public string? Factura { get; set; }
        public DateTime? FechaCompromisoPago { get; set; }
        public string? Motivo { get; set; }
        public IFormFile? Evidencia { get; set; }
    }
}