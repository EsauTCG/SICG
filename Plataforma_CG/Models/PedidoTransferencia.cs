using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class PedidoTransferencia
{
    [Key]
    public int Id { get; set; }

    // Referencia a la Transferencia base (la que ya existe en Transferencias)
    public int TransferenciaId { get; set; }

    [Required, StringLength(20)]
    public string Consecutivo { get; set; } = "";

    // En tu caso "Sucursal" lo estás usando como destino
    [Required, StringLength(50)]
    public string Destino { get; set; } = "";

    public DateTime? FechaSolicitud { get; set; }

    [StringLength(500)]
    public string Observacion { get; set; } = "";

    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    public int Estatus { get; set; } = 0;

    [StringLength(100)]
    public string UsuarioSolicita { get; set; } = "";

    public List<PedidoTransferenciaDetalle> Detalles { get; set; } = new();
}
