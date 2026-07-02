using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Plataforma_CG.ViewModels;

namespace Plataforma_CG.Services
{
    public class CosteoRunnerService : ICosteoRunnerService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<CosteoRunnerService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CosteoRunnerService(
            IConfiguration configuration,
            ILogger<CosteoRunnerService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<List<object>> EjecutarAsync(CosteoFiltroVM model, bool esAutomatico)
        {
            var source = (model.Source ?? "P1").Trim().ToUpper();
            var tipoProceso = (model.TipoProceso ?? "CAJAS").Trim().ToUpper();
            var modo = (model.Modo ?? "DIA").Trim().ToUpper();

            if (model.FechaInicial == default)
                model.FechaInicial = DateTime.Today;

            if (modo == "DIA")
                model.FechaFinal = model.FechaInicial.Date;

            if (model.FechaFinal == default)
                model.FechaFinal = model.FechaInicial.Date;

            if (string.IsNullOrWhiteSpace(model.HoraProgramada))
                model.HoraProgramada = "18:00";

            var sources = source == "ALL"
                ? new[] { "P1", "TIF" }
                : new[] { source == "TIF" ? "TIF" : "P1" };

            var results = new List<object>();

            foreach (var src in sources)
            {
                var cs = src == "TIF"
                    ? _configuration.GetConnectionString("CadenaMeatTIF")
                    : _configuration.GetConnectionString("CadenaMeatP1");

                using var cn = new SqlConnection(cs);
                await cn.OpenAsync();

                if (tipoProceso == "CAJAS" || tipoProceso == "AMBOS")
                {
                    var r = await EjecutarSpCosteoInterno(
                        cn,
                        src,
                        "CAJAS",
                        "dbo.meat_CosteoCajas_SIGO",
                        model,
                        esAutomatico,
                        model.HoraProgramada
                    );
                    results.Add(r);
                }

                if (tipoProceso == "RETRABAJO" || tipoProceso == "AMBOS")
                {
                    var r = await EjecutarSpCosteoInterno(
                        cn,
                        src,
                        "RETRABAJO",
                        "dbo.meat_CosteoRetrabajo_SIGO",
                        model,
                        esAutomatico,
                        model.HoraProgramada
                    );
                    results.Add(r);
                }
            }

            return results;
        }

        private async Task<object> EjecutarSpCosteoInterno(
            SqlConnection cn,
            string source,
            string tipoProceso,
            string spName,
            CosteoFiltroVM model,
            bool esAutomatico,
            string horaProgramada)
        {
            var inicio = DateTime.Now;
            var ok = true;
            var msg = "OK";

            try
            {
                await cn.ExecuteAsync(
                    spName,
                    new
                    {
                        FechaInicial = model.FechaInicial.Date,
                        FechaFinal = model.FechaFinal.Date,
                        TipoCosteoId = model.TipoCosteoId,
                        LoteId = model.LoteId,
                        BrincarSinCosto = model.BrincarSinCosto,
                        ContinuarConError = model.ContinuarConError
                    },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 0
                );
            }
            catch (Exception ex)
            {
                ok = false;
                msg = ex.Message;
                _logger.LogError(ex, "Error ejecutando {SpName} para {Source} {TipoProceso}", spName, source, tipoProceso);
            }

            var fin = DateTime.Now;

            await GuardarBitacoraCosteoAsync(
                source,
                tipoProceso,
                spName,
                model,
                ok,
                msg,
                inicio,
                fin,
                esAutomatico,
                horaProgramada
            );

            return new
            {
                source,
                tipoProceso,
                spEjecutado = spName,
                fechaInicial = model.FechaInicial.ToString("yyyy-MM-dd"),
                fechaFinal = model.FechaFinal.ToString("yyyy-MM-dd"),
                loteId = model.LoteId,
                tipoCosteoId = model.TipoCosteoId,
                horaProgramada,
                esAutomatico,
                brincarSinCosto = model.BrincarSinCosto,
                continuarConError = model.ContinuarConError,
                ok,
                msg,
                inicio,
                fin
            };
        }

        private async Task GuardarBitacoraCosteoAsync(
            string source,
            string tipoProceso,
            string spEjecutado,
            CosteoFiltroVM model,
            bool ok,
            string mensaje,
            DateTime? fechaInicioReal,
            DateTime? fechaFinReal,
            bool esAutomatico,
            string horaProgramada)
        {
            using var cn = new SqlConnection(_configuration.GetConnectionString("CadenaMeatTIF"));

            var usuario = esAutomatico
                ? "SISTEMA"
                : (_httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "sistema");

            var msgFinal = mensaje ?? "";
            if (msgFinal.Length > 2000)
                msgFinal = msgFinal.Substring(0, 2000);

            var parametros = $"FechaInicial={model.FechaInicial:yyyy-MM-dd}, FechaFinal={model.FechaFinal:yyyy-MM-dd}, LoteId={model.LoteId}, TipoCosteoId={model.TipoCosteoId}, BrincarSinCosto={model.BrincarSinCosto}, ContinuarConError={model.ContinuarConError}";

            await cn.ExecuteAsync(@"
INSERT INTO dbo.meat_CosteoBitacora
(
    FechaEjecucion,
    FechaInicioReal,
    FechaFinReal,
    Source,
    TipoProceso,
    SpEjecutado,
    FechaInicial,
    FechaFinal,
    LoteId,
    TipoCosteoId,
    HoraProgramada,
    EsAutomatico,
    BrincarSinCosto,
    ContinuarConError,
    Ok,
    Mensaje,
    Usuario,
    Parametros
)
VALUES
(
    GETDATE(),
    @FechaInicioReal,
    @FechaFinReal,
    @Source,
    @TipoProceso,
    @SpEjecutado,
    @FechaInicial,
    @FechaFinal,
    @LoteId,
    @TipoCosteoId,
    CAST(@HoraProgramada AS time),
    @EsAutomatico,
    @BrincarSinCosto,
    @ContinuarConError,
    @Ok,
    @Mensaje,
    @Usuario,
    @Parametros
);",
            new
            {
                FechaInicioReal = fechaInicioReal,
                FechaFinReal = fechaFinReal,
                Source = source,
                TipoProceso = tipoProceso,
                SpEjecutado = spEjecutado,
                FechaInicial = model.FechaInicial == default ? (DateTime?)null : model.FechaInicial.Date,
                FechaFinal = model.FechaFinal == default ? (DateTime?)null : model.FechaFinal.Date,
                LoteId = model.LoteId,
                TipoCosteoId = model.TipoCosteoId,
                HoraProgramada = string.IsNullOrWhiteSpace(horaProgramada) ? "18:00" : horaProgramada,
                EsAutomatico = esAutomatico,
                BrincarSinCosto = model.BrincarSinCosto,
                ContinuarConError = model.ContinuarConError,
                Ok = ok,
                Mensaje = msgFinal,
                Usuario = usuario,
                Parametros = parametros
            });
        }
    }
}