using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public class CobranzaBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CobranzaBackgroundService> _logger;

        public CobranzaBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<CobranzaBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            var minutos =
                _configuration.GetValue<int?>(
                    "Cobranza:IntervaloMinutos") ?? 15;

            // Seguridad para no bombardear SAP por error de configuracion.
            if (minutos < 5)
                minutos = 5;

            // Espera inicial para no competir con el arranque de IIS.
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(30),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            using var timer =
                new PeriodicTimer(
                    TimeSpan.FromMinutes(minutos));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope =
                        _scopeFactory.CreateScope();

                    var sync =
                        scope.ServiceProvider
                            .GetRequiredService<CobranzaSapSyncService>();

                    var revisados =
                        await sync.SincronizarTodosAsync(
                            "AUTO_SAP",
                            stoppingToken);

                    _logger.LogInformation(
                        "Cobranza automatica SAP ejecutada. " +
                        "Compromisos revisados={Cantidad}",
                        revisados);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error general en sincronizacion automatica de cobranza.");
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(
                            stoppingToken))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
