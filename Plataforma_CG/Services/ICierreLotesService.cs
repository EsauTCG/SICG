using Plataforma_CG.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public interface ICierreLotesService
    {
        Task<List<CierreLoteListaRowVM>> ListarLotesAsync(string source, DateTime desde, DateTime hasta, string estado = "ABIERTOS");
        Task<CierreLoteDiagnosticoVM> DiagnosticarAsync(string source, int loteId, bool validarCosteo = true);
        Task<long> CrearSolicitudAsync(string source, int loteId, string usuario, string motivo, string ip, string userAgent, CierreLoteDiagnosticoVM diagnostico);
        Task<List<CierreLoteSolicitudVM>> ObtenerSolicitudesPendientesAsync(string source, int top = 200);
        Task<CierreLoteAutorizacionEstadoVM> ObtenerAutorizacionEstadoAsync(string source, int loteId, string diagnosticoHash);
        Task<CierreLoteAutorizacionEstadoVM> RegistrarDecisionAsync(string source, long solicitudId, string usuario, string decision, string motivo, string ip, string userAgent);
        Task MarcarSolicitudCerradaAsync(string source, long? solicitudId, string usuario);
        Task CerrarLoteAsync(string source, int loteId, string usuario, string movimientoHash, string diagnosticoHash, long? solicitudId, string detalleCosteoJson);
        Task RegistrarBitacoraAsync(string source, int loteId, long? solicitudId, string accion, string usuario, string detalle, bool ok);
        Task<List<CierreLoteCompatibilidadVM>> ListarCompatibilidadAsync(string source, string texto = "");
        Task GuardarCompatibilidadAsync(CierreLoteCompatibilidadRequestVM req, string usuario);
        Task EliminarCompatibilidadAsync(string source, long compatibilidadId, string usuario);
        Task<CierreLoteTipoConfigVM?> ObtenerTipoConfigAsync(string source, int tipoLoteId);
    }
}
