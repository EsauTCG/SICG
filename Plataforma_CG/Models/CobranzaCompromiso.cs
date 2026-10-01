using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("CobranzaCompromiso")]
    public class CobranzaCompromiso
    {
        [Key]
        public int Id { get; set; }
        public int OrdenVentaId { get; set; }
        public string OrdenVentaConsecutivo { get; set; } = "";
        public string ClienteCodigo { get; set; } = "";
        public string? ClienteNombre { get; set; }
        public decimal SaldoVencidoInicial { get; set; }
        public decimal SaldoPendienteActual { get; set; }
        public DateTime FechaCompromiso { get; set; }
        public string Motivo { get; set; } = "";
        public string Estatus { get; set; } = "PENDIENTE";
        public string UsuarioRegistro { get; set; } = "";
        public DateTime FechaRegistro { get; set; }
        public string? UsuarioUltimaValidacion { get; set; }
        public DateTime? FechaUltimaValidacion { get; set; }
        public string? ObservacionCobranza { get; set; }
    }

    [Table("CobranzaCompromisoFactura")]
    public class CobranzaCompromisoFactura
    {
        [Key]
        public int Id { get; set; }
        public int CobranzaCompromisoId { get; set; }
        public int SapDocEntry { get; set; }
        public int? SapDocNum { get; set; }
        public DateTime? FechaFactura { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public string? Moneda { get; set; }
        public decimal Importe { get; set; }
        public decimal PagadoInicial { get; set; }
        public decimal PendienteInicial { get; set; }
        public decimal PendienteActual { get; set; }
        public bool Pagada { get; set; }
        public DateTime? FechaUltimaValidacion { get; set; }
    }

    [Table("CobranzaCompromisoArchivo")]
    public class CobranzaCompromisoArchivo
    {
        [Key]
        public int Id { get; set; }
        public int CobranzaCompromisoId { get; set; }
        public string NombreOriginal { get; set; } = "";
        public string? TipoContenido { get; set; }
        public string? Extension { get; set; }
        public long TamanoBytes { get; set; }
        public byte[] Contenido { get; set; } = Array.Empty<byte>();
        public string? UsuarioRegistro { get; set; }
        public DateTime FechaRegistro { get; set; }
    }
}
