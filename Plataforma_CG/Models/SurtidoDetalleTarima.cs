using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    /// <summary>
    /// Entity keyless - solo lectura desde BD SIGO.
    /// Tabla: dbo.SurtidoDetalleTarimas
    /// Cada fila = un SKU surtido dentro de una tarima específica.
    /// Relación: SolicitudSurtidoId = SurtidoEncabezado.SolicitudSurtidoId = Subpedido.U_DocMeat
    /// </summary>
    [Table("SurtidoDetalleTarimas")]
    public class SurtidoDetalleTarima
    {
        [Key]
        public int SurtidoDetalleTarimaId { get; set; }

        public int SolicitudSurtidoId { get; set; }

        public int TarimaId { get; set; }

        [MaxLength(50)]
        public string? Tarima { get; set; }

        [MaxLength(50)]
        public string? Articulo { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Kg { get; set; }

        public int Cajas { get; set; }

        [MaxLength(100)]
        public string? Sucursal { get; set; }

        public DateTime? FechaActualizacion { get; set; }
    }
}
