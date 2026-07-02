using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    public class VOrdenesVentaPorVendedor
    {
        public int Id { get; set; }
        public string? Consecutivo { get; set; }
        public DateTime FechaRegistro { get; set; }
        public DateTime? FechaEntrega { get; set; } // puede venir null
        public string? Cliente { get; set; }
        public string? ClienteNombre { get; set; }
        public string? Vendedor { get; set; }

        public string? Observacion { get; set; }

        public string? Serie { get; set; }
        public int Estatus { get; set; }
        public decimal KgTotales { get; set; }
        public decimal Importe { get; set; }

        public int VendedorId { get; set; }       

        // NUEVO: coincide con tu CASE en la vista SQL
        public string? AutorizacionPendiente { get; set; }
    }
}
