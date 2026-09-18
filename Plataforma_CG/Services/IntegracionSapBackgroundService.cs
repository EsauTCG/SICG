using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma_CG.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    /// <summary>
    /// Envía en segundo plano las integraciones pendientes de P1 y TIF.
    /// El botón de la vista controla este worker mediante
    /// IntegracionSapAutomaticoState.
    /// </summary>
    public sealed class IntegracionSapBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IntegracionSapAutomaticoState _automaticoState;
        private readonly ILogger<IntegracionSapBackgroundService> _logger;

        public IntegracionSapBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            IntegracionSapAutomaticoState automaticoState,
            ILogger<IntegracionSapBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _automaticoState = automaticoState;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            var esperaInicial = TimeSpan.FromSeconds(
                GetInt(
                    "IntegracionesSap:Automatico:EsperaInicialSegundos",
                    15,
                    0,
                    600));

            if (esperaInicial > TimeSpan.Zero)
                await Task.Delay(esperaInicial, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var intervalo = TimeSpan.FromSeconds(
                    GetInt(
                        "IntegracionesSap:Automatico:IntervaloSegundos",
                        60,
                        10,
                        86400));

                if (_automaticoState.EstaActivo())
                {
                    _automaticoState.MarcarInicioCiclo();

                    try
                    {
                        var resumen = await ProcesarCicloAsync(stoppingToken);
                        _automaticoState.MarcarFinCiclo(resumen);
                    }
                    catch (OperationCanceledException)
                        when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error general en el proceso automático de integraciones SAP.");

                        _automaticoState.MarcarError(
                            $"Error automático: {ex.GetBaseException().Message}");
                    }
                }

                await _automaticoState.EsperarSiguienteRevisionAsync(
                    intervalo,
                    stoppingToken);
            }
        }

        private async Task<string> ProcesarCicloAsync(
            CancellationToken stoppingToken)
        {
            var diasAtras = GetInt(
                "IntegracionesSap:Automatico:DiasAtras",
                30,
                0,
                3650);

            var maxPorCiclo = GetInt(
                "IntegracionesSap:Automatico:MaxPorCiclo",
                100,
                1,
                500);

            var plantas = GetList(
                "IntegracionesSap:Automatico:Plantas",
                new[] { "P1", "TIF" })
                .Select(NormalizeSource)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tipos = GetList(
                "IntegracionesSap:Automatico:Tipos",
                new[]
                {
                    "ENTRADA",
                    "TRANSFERENCIA_ENTRADA",
                    "SALIDA"
                })
                .Select(NormalizeTipo)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var revisadas = 0;
            var correctas = 0;
            var yaEnviadas = 0;
            var fallidas = 0;
            var bloqueadas = 0;

            foreach (var planta in plantas)
            {
                if (!_automaticoState.EstaActivo())
                    break;

                foreach (var tipo in tipos)
                {
                    if (!_automaticoState.EstaActivo())
                        break;

                    stoppingToken.ThrowIfCancellationRequested();

                    // Las transferencias Tipo 2 viven únicamente en Next/P1.
                    if (tipo == "TRANSFERENCIA_ENTRADA" && planta != "P1")
                        continue;

                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider
                        .GetRequiredService<IIntegracionSapService>();

                    IntegracionSapIndexVM pendientes;

                    try
                    {
                        pendientes = await service.ListarAsync(
                            new IntegracionSapFiltroVM
                            {
                                Source = planta,
                                Tipo = tipo,
                                Desde = DateTime.Today.AddDays(-diasAtras),
                                Hasta = DateTime.Today,
                                Estatus = 0,
                                Folio = null,
                                Top = maxPorCiclo
                            });
                    }
                    catch (Exception ex)
                    {
                        fallidas++;

                        _logger.LogError(
                            ex,
                            "No se pudieron listar pendientes SAP. Planta={Planta} Tipo={Tipo}",
                            planta,
                            tipo);

                        continue;
                    }

                    var filas = pendientes.Rows
                        .Where(x => !x.Enviado)
                        .OrderBy(x => x.FechaDocumento)
                        .ThenBy(x => x.IntegracionId)
                        .Take(maxPorCiclo)
                        .ToList();

                    foreach (var fila in filas)
                    {
                        if (!_automaticoState.EstaActivo())
                            break;

                        stoppingToken.ThrowIfCancellationRequested();
                        revisadas++;

                        if (fila.TieneErrorConfiguracion)
                        {
                            bloqueadas++;

                            _logger.LogWarning(
                                "Integración omitida por configuración inválida. " +
                                "Id={Id} Planta={Planta} Tipo={Tipo} Lineas={Lineas} UbicacionesSinResolver={Ubicaciones}",
                                fila.IntegracionId,
                                planta,
                                tipo,
                                fila.CantidadLineas,
                                fila.UbicacionesSinResolver);

                            continue;
                        }

                        var resultado = await service.EnviarAsync(
                            fila.IntegracionId,
                            planta,
                            tipo,
                            "AUTO_SAP",
                            false);

                        if (resultado.Ok)
                        {
                            if (resultado.YaEnviado)
                                yaEnviadas++;
                            else
                                correctas++;

                            _logger.LogInformation(
                                "Integración automática procesada. Id={Id} Planta={Planta} Tipo={Tipo} YaEnviado={YaEnviado} DocNum={DocNum}",
                                fila.IntegracionId,
                                planta,
                                tipo,
                                resultado.YaEnviado,
                                resultado.DocNum);
                        }
                        else
                        {
                            fallidas++;

                            _logger.LogWarning(
                                "Integración automática fallida. Id={Id} Planta={Planta} Tipo={Tipo} Error={Error}",
                                fila.IntegracionId,
                                planta,
                                tipo,
                                resultado.Error ?? resultado.Mensaje);
                        }
                    }
                }
            }

            if (!_automaticoState.EstaActivo())
            {
                return
                    "El automático fue apagado. Se detuvo antes de iniciar nuevos envíos. " +
                    $"Revisadas: {revisadas}; enviadas: {correctas}; ya existentes: {yaEnviadas}; " +
                    $"fallidas: {fallidas}; bloqueadas: {bloqueadas}.";
            }

            return
                $"Ciclo terminado. Revisadas: {revisadas}; enviadas: {correctas}; " +
                $"ya existentes: {yaEnviadas}; fallidas: {fallidas}; bloqueadas: {bloqueadas}.";
        }

        private int GetInt(
            string key,
            int defaultValue,
            int min,
            int max)
        {
            var value = int.TryParse(
                _configuration[key],
                out var parsed)
                ? parsed
                : defaultValue;

            return Math.Clamp(value, min, max);
        }

        private IReadOnlyList<string> GetList(
            string key,
            IReadOnlyList<string> defaultValues)
        {
            var children = _configuration
                .GetSection(key)
                .GetChildren()
                .Select(x => x.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToList();

            if (children.Count > 0)
                return children;

            var raw = _configuration[key];

            if (!string.IsNullOrWhiteSpace(raw))
            {
                var values = raw
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();

                if (values.Count > 0)
                    return values;
            }

            return defaultValues;
        }

        private static string NormalizeSource(string? source) =>
            string.Equals(
                source?.Trim(),
                "TIF",
                StringComparison.OrdinalIgnoreCase)
                ? "TIF"
                : "P1";

        private static string NormalizeTipo(string? tipo)
        {
            var value = (tipo ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(' ', '_')
                .Replace('-', '_');

            return value switch
            {
                "SALIDA" => "SALIDA",
                "TRANSFERENCIA" => "TRANSFERENCIA_ENTRADA",
                "TRANSFERENCIA_ENTRADA" => "TRANSFERENCIA_ENTRADA",
                "ENTRADA_TRANSFERENCIA" => "TRANSFERENCIA_ENTRADA",
                _ => "ENTRADA"
            };
        }
    }
}