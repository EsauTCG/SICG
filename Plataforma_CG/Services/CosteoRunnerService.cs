using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plataforma_CG.ViewModels;

namespace Plataforma_CG.Services
{
    /// <summary>
    /// Ejecuta el costeo de manera secuencial.
    ///
    /// Orden por planta y bloque:
    ///   1. Canales
    ///   2. Cajas, cuando corresponda
    ///   3. Retrabajo, cuando corresponda
    ///
    /// En modo MES genera un bloque por cada día, desde el día 1 hasta el último
    /// día aplicable del mes. Para el mes actual se detiene en la fecha de hoy.
    /// </summary>
    public class CosteoRunnerService : ICosteoRunnerService
    {
        private const string SpCanales = "dbo.meat_CosteoCanales";
        private const string SpCajas = "dbo.meat_CosteoCajas_SIGO";
        private const string SpRetrabajo = "dbo.meat_CosteoRetrabajo_SIGO";

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
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var source = NormalizarSource(model.Source);
            var tipoProceso = NormalizarTipoProceso(model.TipoProceso);
            var modo = NormalizarModo(model.Modo);

            NormalizarFechas(model, modo);

            if (string.IsNullOrWhiteSpace(model.HoraProgramada))
                model.HoraProgramada = "18:00";

            var sources = source == "ALL"
                ? new[] { "P1", "TIF" }
                : new[] { source };

            var bloques = ConstruirBloques(model, modo);
            var results = new List<object>();

            // La fecha es el ciclo exterior para garantizar:
            // día 1 completo, después día 2 completo, después día 3, etc.
            foreach (var bloque in bloques)
            {
                foreach (var src in sources)
                {
                    var modelBloque = CrearModeloBloque(model, bloque, modo, src, tipoProceso);
                    var connectionString = ObtenerCadena(src);

                    if (string.IsNullOrWhiteSpace(connectionString))
                        throw new InvalidOperationException($"No existe cadena de conexión para la planta {src}.");

                    using var cn = new SqlConnection(connectionString);
                    await cn.OpenAsync();

                    // CANALES:
                    // - En DIA/RANGO/MES se conserva la secuencia normal y el SP trabaja por fecha.
                    // - En LOTE solo se ejecuta cuando el proceso solicitado es CANALES.
                    //   El procedimiento ajustado recibe @LoteIdFiltro y queda aislado al lote seleccionado.
                    var ejecutarCanales = modo != "LOTE" || tipoProceso == "CANALES";
                    if (ejecutarCanales)
                    {
                        var canales = await EjecutarSpCanalesInterno(
                            cn,
                            src,
                            modelBloque,
                            esAutomatico,
                            model.HoraProgramada);

                        results.Add(canales);

                        if (!canales.ok)
                        {
                            if (!model.ContinuarConError)
                                return results;

                            // No ejecutar etapas dependientes del mismo bloque con canales incompletos.
                            continue;
                        }

                        // Cuando el usuario selecciona CANALES, el bloque termina aquí.
                        if (tipoProceso == "CANALES")
                            continue;
                    }

                    if (tipoProceso == "CAJAS" || tipoProceso == "AMBOS")
                    {
                        var cajas = await EjecutarSpCosteoInterno(
                            cn,
                            src,
                            "CAJAS",
                            SpCajas,
                            modelBloque,
                            esAutomatico,
                            model.HoraProgramada);

                        results.Add(cajas);

                        if (!cajas.ok)
                        {
                            if (!model.ContinuarConError)
                                return results;

                            // Retrabajo puede depender del costo de cajas del mismo bloque.
                            continue;
                        }
                    }

                    if (tipoProceso == "RETRABAJO" || tipoProceso == "AMBOS")
                    {
                        var retrabajo = await EjecutarSpCosteoInterno(
                            cn,
                            src,
                            "RETRABAJO",
                            SpRetrabajo,
                            modelBloque,
                            esAutomatico,
                            model.HoraProgramada);

                        results.Add(retrabajo);

                        if (!retrabajo.ok && !model.ContinuarConError)
                            return results;
                    }
                }
            }

            return results;
        }

        private static string NormalizarSource(string source)
        {
            var value = (source ?? "P1").Trim().ToUpperInvariant();

            if (value != "P1" && value != "TIF" && value != "ALL")
                throw new ArgumentException("Source debe ser P1, TIF o ALL.");

            return value;
        }

        private static string NormalizarTipoProceso(string tipoProceso)
        {
            var value = (tipoProceso ?? "CAJAS").Trim().ToUpperInvariant();

            if (value != "CANALES" && value != "CAJAS" && value != "RETRABAJO" && value != "AMBOS")
                throw new ArgumentException("TipoProceso debe ser CANALES, CAJAS, RETRABAJO o AMBOS.");

            return value;
        }

        private static string NormalizarModo(string modo)
        {
            var value = (modo ?? "DIA").Trim().ToUpperInvariant();

            if (value != "DIA" && value != "RANGO" && value != "MES" && value != "LOTE")
                throw new ArgumentException("Modo debe ser DIA, RANGO, MES o LOTE.");

            return value;
        }

        private static void NormalizarFechas(CosteoFiltroVM model, string modo)
        {
            if (model.FechaInicial == default)
                model.FechaInicial = DateTime.Today;

            model.FechaInicial = model.FechaInicial.Date;

            if (modo == "DIA")
            {
                model.FechaFinal = model.FechaInicial;
                model.LoteId = null;
                return;
            }

            if (modo == "MES")
            {
                var inicioMes = new DateTime(model.FechaInicial.Year, model.FechaInicial.Month, 1);

                if (inicioMes > DateTime.Today)
                    throw new ArgumentException("No se puede ejecutar el costeo de un mes futuro.");

                var finMes = inicioMes.AddMonths(1).AddDays(-1);

                // En el mes actual se procesa únicamente hasta hoy.
                if (finMes > DateTime.Today)
                    finMes = DateTime.Today;

                model.FechaInicial = inicioMes;
                model.FechaFinal = finMes;
                model.LoteId = null;
                return;
            }

            if (model.FechaFinal == default)
                model.FechaFinal = model.FechaInicial;

            model.FechaFinal = model.FechaFinal.Date;

            if (model.FechaFinal < model.FechaInicial)
                throw new ArgumentException("La FechaFinal no puede ser menor que la FechaInicial.");

            if (modo == "LOTE")
            {
                if (!model.LoteId.HasValue || model.LoteId.Value <= 0)
                    throw new ArgumentException("Captura un LoteId válido para el modo LOTE.");
            }
            else
            {
                model.LoteId = null;
            }
        }

        private static List<BloqueFecha> ConstruirBloques(CosteoFiltroVM model, string modo)
        {
            var bloques = new List<BloqueFecha>();

            if (modo != "MES")
            {
                bloques.Add(new BloqueFecha
                {
                    Numero = 1,
                    FechaInicial = model.FechaInicial.Date,
                    FechaFinal = model.FechaFinal.Date
                });

                return bloques;
            }

            var numero = 0;
            for (var fecha = model.FechaInicial.Date;
                 fecha <= model.FechaFinal.Date;
                 fecha = fecha.AddDays(1))
            {
                numero++;
                bloques.Add(new BloqueFecha
                {
                    Numero = numero,
                    FechaInicial = fecha,
                    FechaFinal = fecha
                });
            }

            return bloques;
        }

        private static CosteoFiltroVM CrearModeloBloque(
            CosteoFiltroVM original,
            BloqueFecha bloque,
            string modoOriginal,
            string source,
            string tipoProceso)
        {
            return new CosteoFiltroVM
            {
                Source = source,
                TipoProceso = tipoProceso,
                Modo = modoOriginal == "LOTE" ? "LOTE" : "DIA",
                FechaInicial = bloque.FechaInicial,
                FechaFinal = bloque.FechaFinal,
                TipoCosteoId = original.TipoCosteoId,
                LoteId = modoOriginal == "LOTE" ? original.LoteId : null,
                BrincarSinCosto = original.BrincarSinCosto,
                ContinuarConError = original.ContinuarConError,
                Automatico = original.Automatico,
                HoraProgramada = original.HoraProgramada
            };
        }

        private string ObtenerCadena(string source)
        {
            return (source == "TIF"
                ? _configuration.GetConnectionString("CadenaMeatTIF")
                : _configuration.GetConnectionString("CadenaMeatP1")) ?? string.Empty;
        }

        private async Task<CosteoEjecucionResultado> EjecutarSpCanalesInterno(
            SqlConnection cn,
            string source,
            CosteoFiltroVM model,
            bool esAutomatico,
            string horaProgramada)
        {
            var inicio = DateTime.Now;
            var ok = true;
            var msg = "OK";

            try
            {
                var existe = await cn.ExecuteScalarAsync<int>(@"
SELECT CASE
           WHEN OBJECT_ID('dbo.meat_CosteoCanales', 'P') IS NULL THEN 0
           ELSE 1
       END;");

                if (existe != 1)
                {
                    throw new InvalidOperationException(
                        $"No existe {SpCanales} en la base de la planta {source}. " +
                        "Instala el procedimiento antes de ejecutar el costeo.");
                }

                await cn.ExecuteAsync(
                    SpCanales,
                    new
                    {
                        FechaInicial = model.FechaInicial.Date,
                        FechaFinal = model.FechaFinal.Date,

                        // El procedimiento detecta y sustituye el TipoPesoId por lote.
                        // El valor 1 funciona como valor inicial/fallback compatible.
                        TipoPesoId = 1,

                        // NULL mantiene el comportamiento histórico por fecha.
                        // En modo LOTE obliga al SP a procesar exclusivamente este lote.
                        LoteIdFiltro = string.Equals(model.Modo, "LOTE", StringComparison.OrdinalIgnoreCase)
                            ? model.LoteId
                            : null
                    },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 0);
            }
            catch (Exception ex)
            {
                ok = false;
                msg = ex.GetBaseException().Message;

                _logger.LogError(
                    ex,
                    "Error ejecutando {SpName} para {Source}, bloque {Fecha}",
                    SpCanales,
                    source,
                    model.FechaInicial.Date);
            }

            var fin = DateTime.Now;

            var resultado = CrearResultado(
                source,
                "CANALES",
                SpCanales,
                model,
                esAutomatico,
                horaProgramada,
                ok,
                msg,
                inicio,
                fin);

            await GuardarBitacoraCosteoAsync(resultado, model);
            return resultado;
        }

        private async Task<CosteoEjecucionResultado> EjecutarSpCosteoInterno(
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
                    commandTimeout: 0);
            }
            catch (Exception ex)
            {
                ok = false;
                msg = ex.GetBaseException().Message;

                _logger.LogError(
                    ex,
                    "Error ejecutando {SpName} para {Source} {TipoProceso}, bloque {Fecha}",
                    spName,
                    source,
                    tipoProceso,
                    model.FechaInicial.Date);
            }

            var fin = DateTime.Now;

            var resultado = CrearResultado(
                source,
                tipoProceso,
                spName,
                model,
                esAutomatico,
                horaProgramada,
                ok,
                msg,
                inicio,
                fin);

            await GuardarBitacoraCosteoAsync(resultado, model);
            return resultado;
        }

        private static CosteoEjecucionResultado CrearResultado(
            string source,
            string tipoProceso,
            string spName,
            CosteoFiltroVM model,
            bool esAutomatico,
            string horaProgramada,
            bool ok,
            string msg,
            DateTime inicio,
            DateTime fin)
        {
            return new CosteoEjecucionResultado
            {
                source = source,
                tipoProceso = tipoProceso,
                spEjecutado = spName,
                fechaInicial = model.FechaInicial.ToString("yyyy-MM-dd"),
                fechaFinal = model.FechaFinal.ToString("yyyy-MM-dd"),
                loteId = model.LoteId,
                tipoCosteoId = model.TipoCosteoId,
                horaProgramada = horaProgramada,
                esAutomatico = esAutomatico,
                brincarSinCosto = model.BrincarSinCosto,
                continuarConError = model.ContinuarConError,
                ok = ok,
                msg = msg,
                inicio = inicio,
                fin = fin
            };
        }

        private async Task GuardarBitacoraCosteoAsync(
            CosteoEjecucionResultado resultado,
            CosteoFiltroVM model)
        {
            try
            {
                var bitacoraCs =
                    _configuration.GetConnectionString("CadenaMeatTIF") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(bitacoraCs))
                    throw new InvalidOperationException(
                        "No existe la cadena CadenaMeatTIF para guardar la bitácora.");

                using var cn = new SqlConnection(bitacoraCs);

                var usuario = resultado.esAutomatico
                    ? "SISTEMA"
                    : (_httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "sistema");

                var mensaje = resultado.msg ?? string.Empty;
                if (mensaje.Length > 2000)
                    mensaje = mensaje.Substring(0, 2000);

                var parametros =
                    $"ModoBloque={model.Modo}, " +
                    $"FechaInicial={model.FechaInicial:yyyy-MM-dd}, " +
                    $"FechaFinal={model.FechaFinal:yyyy-MM-dd}, " +
                    $"LoteId={(model.LoteId.HasValue ? model.LoteId.Value.ToString() : "NULL")}, " +
                    $"TipoCosteoId={model.TipoCosteoId}, " +
                    $"BrincarSinCosto={model.BrincarSinCosto}, " +
                    $"ContinuarConError={model.ContinuarConError}";

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
                        FechaInicioReal = resultado.inicio,
                        FechaFinReal = resultado.fin,
                        Source = resultado.source,
                        TipoProceso = resultado.tipoProceso,
                        SpEjecutado = resultado.spEjecutado,
                        FechaInicial = model.FechaInicial.Date,
                        FechaFinal = model.FechaFinal.Date,
                        LoteId = model.LoteId,
                        TipoCosteoId = model.TipoCosteoId,
                        HoraProgramada = string.IsNullOrWhiteSpace(resultado.horaProgramada)
                            ? "18:00"
                            : resultado.horaProgramada,
                        EsAutomatico = resultado.esAutomatico,
                        BrincarSinCosto = model.BrincarSinCosto,
                        ContinuarConError = model.ContinuarConError,
                        Ok = resultado.ok,
                        Mensaje = mensaje,
                        Usuario = usuario,
                        Parametros = parametros
                    });
            }
            catch (Exception ex)
            {
                // Un error de bitácora no debe ocultar el resultado real del costeo.
                _logger.LogError(ex, "No se pudo guardar la bitácora de costeo");
            }
        }

        private sealed class BloqueFecha
        {
            public int Numero { get; set; }
            public DateTime FechaInicial { get; set; }
            public DateTime FechaFinal { get; set; }
        }

        private sealed class CosteoEjecucionResultado
        {
            public string source { get; set; } = string.Empty;
            public string tipoProceso { get; set; } = string.Empty;
            public string spEjecutado { get; set; } = string.Empty;
            public string fechaInicial { get; set; } = string.Empty;
            public string fechaFinal { get; set; } = string.Empty;
            public int? loteId { get; set; }
            public int tipoCosteoId { get; set; }
            public string horaProgramada { get; set; } = string.Empty;
            public bool esAutomatico { get; set; }
            public bool brincarSinCosto { get; set; }
            public bool continuarConError { get; set; }
            public bool ok { get; set; }
            public string msg { get; set; } = string.Empty;
            public DateTime inicio { get; set; }
            public DateTime fin { get; set; }
        }
    }
}
