using Microsoft.EntityFrameworkCore;

namespace Plataforma_CG.Models.Reportes
{
    public class OrdenVentaCabecera
    {
        public int? IdOrdenVenta { get; set; }

        public string? Consecutivo { get; set; }

        public DateTime? FechaEntrega { get; set; }

        public string? Serie { get; set; }

        public string? CodigoCliente { get; set; }

        public string? NombreCliente { get; set; }

        public string? NombreVendedor { get; set; }

        public int? CodigoVendedor { get; set; }

        public decimal? Saldo { get; set; }

        public decimal? Credito { get; set; }

        public DateTime? FechaRegistro { get; set; }

        public int? Estado { get; set; }

        
    }
}
