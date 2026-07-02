using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{

    [Table("Subpedido")]
    public class Subpedido
    {
        public int Id { get; set; }

        public int OrdenVentaId { get; set; }   // FK real en BD

        public string? ConsecutivoOV { get; set; }
        public string? SubFolio { get; set; }
        public string? Almacen { get; set; }
        public decimal TotalPeso { get; set; }
        public decimal TotalImporte { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

        // Si NO quieres nullable, deja así:
        public DateTime FechaEntrega { get; set; }
        public DateTime FechaEmbarque { get; set; }

        [MaxLength(100)]
        public string? U_DocMeat { get; set; }   // <- SOLO este campo adicional

        public string? DocumentoSAP { get; set; }

        public string? Cliente { get; set; }
        public string? Vendedor { get; set; }

        [ForeignKey(nameof(OrdenVentaId))]           // <- evita que EF invente OrdenVentaId1
        public OrdenVenta? OrdenVenta { get; set; }

        public ICollection<SubpedidoProducto> Productos { get; set; } = new List<SubpedidoProducto>();

        
        
    }
}
