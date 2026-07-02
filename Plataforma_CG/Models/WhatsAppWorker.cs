using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class WhatsAppWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhatsAppWorker> _logger;

    public WhatsAppWorker(IServiceScopeFactory scopeFactory, ILogger<WhatsAppWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WhatsAppWorker iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var service = scope.ServiceProvider.GetRequiredService<WhatsAppReporteService>();

                var ahora = DateTime.Now;

                _logger.LogInformation("Revisión automática WhatsApp: {fecha}", ahora);

                await service.EnviarReportesProgramadosAsync(
                    ahora.Month,
                    ahora.Year,
                    stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en WhatsAppWorker.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}