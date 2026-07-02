using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class WhatsAppReporteHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhatsAppReporteHostedService> _logger;

    public WhatsAppReporteHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<WhatsAppReporteHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WhatsAppReporteHostedService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var servicio = scope.ServiceProvider.GetRequiredService<WhatsAppReporteService>();

                var ahora = DateTime.Now;

                _logger.LogInformation("Revisión automática WhatsApp: {Fecha}", ahora);

                await servicio.EnviarReportesProgramadosAsync(
                    ahora.Month,
                    ahora.Year,
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("WhatsAppReporteHostedService detenido.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en WhatsAppReporteHostedService");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}