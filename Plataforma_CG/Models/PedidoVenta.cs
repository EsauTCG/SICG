using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models
{
    public class PedidoVenta
    {
        public int Id { get; set; }

        // Relación con la OV original
        public int OrdenVentaId { get; set; }
        [MaxLength(50)]
        public string OrdenVentaConsecutivo { get; set; } = string.Empty;

        // Snapshot de algunos datos clave (opcional pero útil)
        [MaxLength(200)]
        public string Cliente { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Vendedor { get; set; } = string.Empty;

        public DateTime? FechaEntrega { get; set; }

        // Gestión
        public DateTime? FechaEmbarque { get; set; }

        [MaxLength(50)]
        public string AlmacenSurtir { get; set; } = string.Empty;

        // Observaciones de la gestión (distintas a las de la OV)
        [MaxLength(1000)]
        public string? ObservacionGestion { get; set; }

        public DateTime FechaGestion { get; set; } = DateTime.UtcNow;

        // Totales (para consulta rápida)
        public decimal TotalImporte { get; set; }
        public decimal TotalPeso { get; set; }

   

        // Navegación
        public ICollection<PedidoVentaProducto> Productos { get; set; } = new List<PedidoVentaProducto>();
    }
}
