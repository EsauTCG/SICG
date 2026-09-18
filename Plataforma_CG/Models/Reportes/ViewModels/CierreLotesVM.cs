using System;
using System.Collections.Generic;

namespace Plataforma_CG.ViewModels
{
    public sealed class CierreLoteListaRowVM
    {
        public int LoteId { get; set; }
        public string Nombre { get; set; } = "";
        public int TipoLoteId { get; set; }
        public int EstatusId { get; set; }
        public DateTime? FechaProduccion { get; set; }
        public int Entradas { get; set; }
        public decimal KgEntrada { get; set; }
        public int Salidas { get; set; }
        public decimal KgSalida { get; set; }
        public decimal DiferenciaKg => KgEntrada - KgSalida;
        public decimal RendimientoPct => KgEntrada <= 0 ? 0 : Math.Round(KgSalida / KgEntrada * 100m, 2);
        public string EstadoTexto => EstatusId == 3 ? "CERRADO" : "ABIERTO";
    }

    public sealed class CierreLoteMovimientoVM
    {
        public string Tipo { get; set; } = "";
        public int ProduccionId { get; set; }
        public int? LoteId { get; set; }
        public string LoteNombre { get; set; } = "";
        public string Articulo { get; set; } = "";
        public string Producto { get; set; } = "";
        public string CodigoEtiqueta { get; set; } = "";
        public decimal PesoNeto { get; set; }
        public int Estatus { get; set; }
        public int? UltimoProcesoId { get; set; }
    }

    public sealed class CierreLoteAnomaliaVM
    {
        public string Codigo { get; set; } = "";
        public string Nivel { get; set; } = ""; // BLOQUEO | AUTORIZACION | ADVERTENCIA | INFO
        public string Titulo { get; set; } = "";
        public string Detalle { get; set; } = "";
        public string ArticuloEntrada { get; set; } = "";
        public string ArticuloSalida { get; set; } = "";
        public decimal? Valor { get; set; }
        public decimal? Limite { get; set; }
        public bool RequiereAutorizacion => string.Equals(Nivel, "AUTORIZACION", StringComparison.OrdinalIgnoreCase);
        public bool Bloquea => string.Equals(Nivel, "BLOQUEO", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class CierreLoteTipoConfigVM
    {
        public int TipoLoteId { get; set; }
        public string TipoProceso { get; set; } = "";
        public bool RequiereEntradasLogistica { get; set; }
        public bool ValidarCompatibilidad { get; set; }
        public decimal VariacionAdvertenciaPct { get; set; }
        public decimal VariacionBloqueoPct { get; set; }
        public int AprobacionesRequeridas { get; set; } = 1;
        public bool BrincarSinCosto { get; set; }
        public bool Activo { get; set; }
    }

    public sealed class CierreLoteDiagnosticoVM
    {
        public string Source { get; set; } = "";
        public int LoteId { get; set; }
        public string LoteNombre { get; set; } = "";
        public int TipoLoteId { get; set; }
        public int EstatusId { get; set; }
        public DateTime? FechaProduccion { get; set; }
        public string TipoProceso { get; set; } = "";
        public int AprobacionesRequeridas { get; set; } = 1;
        public int? TipoPesoIdCanal { get; set; }
        public string TipoPesoCanal { get; set; } = "";

        public int Entradas { get; set; }
        public decimal KgEntrada { get; set; }
        public int Salidas { get; set; }
        public decimal KgSalida { get; set; }
        public decimal DiferenciaKg { get; set; }
        public decimal VariacionPct { get; set; }
        public decimal RendimientoPct { get; set; }

        public decimal CostoEntradaCalculado { get; set; }
        public decimal CostoEntradaAlterno { get; set; }
        public decimal CostoSalidaCalculado { get; set; }
        public decimal CostoSalidaGuardado { get; set; }
        public decimal DiferenciaCosto { get; set; }
        public int SalidasSinCosteo { get; set; }
        public int SalidasConCostoDuplicado { get; set; }
        public int SalidasConProduccionCosteoDuplicado { get; set; }

        public bool PuedeCerrarSinAutorizacion { get; set; }
        public bool RequiereAutorizacion { get; set; }
        public bool TieneBloqueos { get; set; }
        public string MovimientoHash { get; set; } = "";
        public string DiagnosticoHash { get; set; } = "";

        public List<CierreLoteMovimientoVM> MovimientosEntrada { get; set; } = new();
        public List<CierreLoteMovimientoVM> MovimientosSalida { get; set; } = new();
        public List<CierreLoteAnomaliaVM> Anomalias { get; set; } = new();
    }

    public sealed class CierreLoteSolicitudRequestVM
    {
        public string Source { get; set; } = "TIF";
        public int LoteId { get; set; }
        public string Motivo { get; set; } = "";
    }

    public sealed class CierreLoteDecisionRequestVM
    {
        public string Source { get; set; } = "TIF";
        public long SolicitudId { get; set; }
        public string Motivo { get; set; } = "";
    }

    public sealed class CierreLoteCerrarRequestVM
    {
        public string Source { get; set; } = "TIF";
        public int LoteId { get; set; }
    }

    public sealed class CierreLoteSolicitudVM
    {
        public long SolicitudId { get; set; }
        public string Source { get; set; } = "";
        public int LoteId { get; set; }
        public string LoteNombre { get; set; } = "";
        public int TipoLoteId { get; set; }
        public string Estado { get; set; } = "";
        public string UsuarioSolicita { get; set; } = "";
        public DateTime FechaSolicitud { get; set; }
        public string MotivoSolicitud { get; set; } = "";
        public string DiagnosticoHash { get; set; } = "";
        public int AprobacionesRequeridas { get; set; }
        public int AprobacionesActuales { get; set; }
        public int Rechazos { get; set; }
        public string ResumenAnomalias { get; set; } = "";
        public string Autorizadores { get; set; } = "";
    }

    public sealed class CierreLoteAutorizacionEstadoVM
    {
        public bool ExisteSolicitud { get; set; }
        public bool Aprobada { get; set; }
        public bool Rechazada { get; set; }
        public long? SolicitudId { get; set; }
        public int AprobacionesRequeridas { get; set; }
        public int AprobacionesActuales { get; set; }
        public int Rechazos { get; set; }
        public string Estado { get; set; } = "";
        public string Autorizadores { get; set; } = "";
    }

    public sealed class CierreLoteCompatibilidadVM
    {
        public long CompatibilidadId { get; set; }
        public string Source { get; set; } = "";
        public string ArticuloEntrada { get; set; } = "";
        public string ArticuloSalida { get; set; } = "";
        public bool Permitido { get; set; }
        public string Motivo { get; set; } = "";
        public bool Activo { get; set; }
        public string Usuario { get; set; } = "";
        public DateTime FechaHora { get; set; }
    }

    public sealed class CierreLoteCompatibilidadRequestVM
    {
        public string Source { get; set; } = "TIF";
        public string ArticuloEntrada { get; set; } = "";
        public string ArticuloSalida { get; set; } = "";
        public bool Permitido { get; set; }
        public string Motivo { get; set; } = "";
    }
}
