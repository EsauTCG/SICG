using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class PedidoTransferenciaDetalle
{
    [Key]
    public int Id { get; set; }

    public int PedidoTransferenciaId { get; set; }

    // Opcional: para amarrar con el detalle original si quieres
    public int? TransferenciaDetalleIdOriginal { get; set; }

    [Required, StringLength(30)]
    public string ProductoCodigo { get; set; } = "";

   
   
    [Column(TypeName = "decimal(18,4)")]
    public decimal CantidadKg { get; set; }

    public int Cajas { get; set; }  // en lugar de int?

    // Para mantener el orden como lo ves en pantalla
    public int Orden { get; set; } = 0;
    public PedidoTransferencia? PedidoTransferencia { get; set; }
}
