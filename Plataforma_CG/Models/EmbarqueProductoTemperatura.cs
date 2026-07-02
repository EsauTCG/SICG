using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    public class EmbarqueProductoTemperatura
    {
        public int Id { get; set; }

        public int EmbarqueId { get; set; }

        [MaxLength(30)]
        public string TipoDocumento { get; set; } = null!; // OV / TRANSFERENCIA

        public int DocumentoId { get; set; }

        [MaxLength(80)]
        public string? DocumentoConsecutivo { get; set; }

        public int OrigenDetalleId { get; set; } // PedidoVentaProducto.Id o PedidoTransferenciaDetalle.Id

        [MaxLength(80)]
        public string ProductoCodigo { get; set; } = null!;

        [MaxLength(250)]
        public string? ProductoNombre { get; set; }

        [MaxLength(80)]
        public string? Almacen { get; set; }

        public int Cajas { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Kilos { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal? Temperatura { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }

        public DateTime FechaRegistro { get; set; }

        [MaxLength(256)]
        public string? UsuarioRegistro { get; set; }

        public DateTime? FechaActualizacion { get; set; }

        [MaxLength(256)]
        public string? UsuarioActualiza { get; set; }

        public Embarque Embarque { get; set; } = null!;
    }

    public class EmbarqueProductoTemperaturaItemVm
    {
        public int Id { get; set; }

        public string TipoDocumento { get; set; } = "";
        public int DocumentoId { get; set; }
        public string DocumentoConsecutivo { get; set; } = "";
        public int OrigenDetalleId { get; set; }

        public string ProductoCodigo { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
        public string Almacen { get; set; } = "";

        public int Cajas { get; set; }
        public decimal Kilos { get; set; }

        public decimal? Temperatura { get; set; }
        public string? Observaciones { get; set; }

        public DateTime? FechaUltimaCaptura { get; set; }
        public string? UsuarioUltimaCaptura { get; set; }

        public bool Capturado => Temperatura.HasValue;
        public string DocumentoCliente { get; set; } = "";
    }
}