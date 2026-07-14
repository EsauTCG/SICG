using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    /// <summary>
    /// Entity keyless - solo lectura desde BD SIGO.
    /// Tabla: dbo.SurtidoEncabezado
    /// Relación: SolicitudSurtidoId = Subpedido.U_DocMeat
    /// </summary>
    [Table("SurtidoEncabezado")]
    public class SurtidoEncabezado
    {
        [Key]
        public int SolicitudSurtidoId { get; set; }

        [MaxLength(50)]
        public string? Pedido { get; set; }

        [MaxLength(50)]
        public string? Remision { get; set; }

        public DateTime? FechaValidacion { get; set; }

        [MaxLength(100)]
        public string? Sucursal { get; set; }

        public DateTime? FechaActualizacion { get; set; }

        [MaxLength(50)]
        public string? CodigoSap { get; set; }
    }
}
