using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("CobranzaCompromisoHistorico")]
    public class CobranzaCompromisoHistorico
    {
        [Key]
        public long Id { get; set; }
        public int CobranzaCompromisoId { get; set; }
        public int? CobranzaCompromisoFacturaId { get; set; }
        [Required, MaxLength(40)] public string TipoEvento { get; set; } = "";
        [MaxLength(30)] public string? EstatusAnterior { get; set; }
        [MaxLength(30)] public string? EstatusNuevo { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? PendienteAnterior { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal? PendienteNuevo { get; set; }
        public DateTime FechaEvento { get; set; } = DateTime.Now;
        [MaxLength(200)] public string? UsuarioProceso { get; set; }
        [MaxLength(1000)] public string? Detalle { get; set; }
    }

    [Table("CobranzaCompromisoPagoSap")]
    public class CobranzaCompromisoPagoSap
    {
        [Key]
        public long Id { get; set; }
        public int CobranzaCompromisoId { get; set; }
        public int CobranzaCompromisoFacturaId { get; set; }
        public int FacturaSapDocEntry { get; set; }
        public int SapPagoDocEntry { get; set; }
        public int? SapPagoDocNum { get; set; }
        public DateTime? FechaPagoSap { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal MontoAplicado { get; set; }
        [MaxLength(200)] public string? Referencia { get; set; }
        [MaxLength(500)] public string? Comentario { get; set; }
        public DateTime FechaDeteccion { get; set; } = DateTime.Now;
        [MaxLength(200)] public string? UsuarioProceso { get; set; }
    }
}
