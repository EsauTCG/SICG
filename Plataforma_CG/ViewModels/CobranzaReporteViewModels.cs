using System;
using System.Collections.Generic;

namespace Plataforma_CG.ViewModels
{
    public class CobranzaReporteViewModel
    {
        public string? Estatus { get; set; }
        public string? Cliente { get; set; }
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }

        public int Total { get; set; }
        public int Pendientes { get; set; }
        public int Incumplidos { get; set; }
        public int Parciales { get; set; }
        public int Cumplidos { get; set; }
        public int Revisar { get; set; }
        public decimal SaldoPendiente { get; set; }

        public List<CobranzaReporteItemViewModel> Items { get; set; } = new();
    }

    public class CobranzaReporteItemViewModel
    {
        public int Id { get; set; }
        public string OrdenVentaConsecutivo { get; set; } = "";
        public string ClienteCodigo { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public decimal SaldoVencidoInicial { get; set; }
        public decimal SaldoPendienteActual { get; set; }
        public DateTime FechaCompromiso { get; set; }
        public string Estatus { get; set; } = "";
        public string UsuarioRegistro { get; set; } = "";
        public DateTime FechaRegistro { get; set; }
        public DateTime? FechaUltimaValidacion { get; set; }
        public int Facturas { get; set; }
        public DateTime? UltimoPagoSap { get; set; }
    }

    public class CobranzaDetalleViewModel
    {
        public int Id { get; set; }
        public int OrdenVentaId { get; set; }
        public string OrdenVentaConsecutivo { get; set; } = "";
        public string ClienteCodigo { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public decimal SaldoVencidoInicial { get; set; }
        public decimal SaldoPendienteActual { get; set; }
        public DateTime FechaCompromiso { get; set; }
        public string Motivo { get; set; } = "";
        public string Estatus { get; set; } = "";
        public string UsuarioRegistro { get; set; } = "";
        public DateTime FechaRegistro { get; set; }
        public string? UsuarioUltimaValidacion { get; set; }
        public DateTime? FechaUltimaValidacion { get; set; }
        public string? ObservacionCobranza { get; set; }

        public List<CobranzaFacturaDetalleViewModel> Facturas { get; set; } = new();
        public List<CobranzaArchivoDetalleViewModel> Archivos { get; set; } = new();
        public List<CobranzaHistoricoDetalleViewModel> Historico { get; set; } = new();
    }

    public class CobranzaFacturaDetalleViewModel
    {
        public int Id { get; set; }
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
        public List<CobranzaPagoSapDetalleViewModel> Pagos { get; set; } = new();
    }

    public class CobranzaPagoSapDetalleViewModel
    {
        public long Id { get; set; }
        public int SapPagoDocEntry { get; set; }
        public int? SapPagoDocNum { get; set; }
        public DateTime? FechaPagoSap { get; set; }
        public decimal MontoAplicado { get; set; }
        public string? Referencia { get; set; }
        public string? Comentario { get; set; }
        public DateTime FechaDeteccion { get; set; }
    }

    public class CobranzaArchivoDetalleViewModel
    {
        public int Id { get; set; }
        public string NombreOriginal { get; set; } = "";
        public string? TipoContenido { get; set; }
        public string? Extension { get; set; }
        public long TamanoBytes { get; set; }
        public DateTime FechaRegistro { get; set; }
    }

    public class CobranzaHistoricoDetalleViewModel
    {
        public long Id { get; set; }
        public int? CobranzaCompromisoFacturaId { get; set; }
        public string TipoEvento { get; set; } = "";
        public string? EstatusAnterior { get; set; }
        public string? EstatusNuevo { get; set; }
        public decimal? PendienteAnterior { get; set; }
        public decimal? PendienteNuevo { get; set; }
        public DateTime FechaEvento { get; set; }
        public string? UsuarioProceso { get; set; }
        public string? Detalle { get; set; }
    }
}
