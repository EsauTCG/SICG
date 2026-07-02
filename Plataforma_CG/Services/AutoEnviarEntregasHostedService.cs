using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plataforma_CG.Services
{
    public class AutoEnviarEntregasHostedService : BackgroundService
    {
        private readonly ILogger<AutoEnviarEntregasHostedService> _log;
        private readonly IAutoSapSettingsStore _store;
        private readonly IServiceScopeFactory _scopeFactory;

        public AutoEnviarEntregasHostedService(
            ILogger<AutoEnviarEntregasHostedService> log,
            IAutoSapSettingsStore store,
            IServiceScopeFactory scopeFactory)
        {
            _log = log;
            _store = store;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var all = _store.GetAll(); // P1 + TIF

                // si ninguno está activo, duerme corto
                if (all.All(x => x.Enabled == false))
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var query = scope.ServiceProvider.GetRequiredService<IEntregasQueryService>();
                    var sender = scope.ServiceProvider.GetRequiredService<IEnviarEntregaService>();

                    // procesa cada planta por separado
                    foreach (var s in all.Where(x => x.Enabled))
                    {
                        var pendientes = await query.GetPendientesAsync(s.Source, stoppingToken);

                        foreach (var e in pendientes)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            var (ok, msg) = await sender.EnviarAsync(e.Referencia, e.Source, stoppingToken);

                            if (!ok)
                                _log.LogWarning("Auto SAP fallo {Ref} {Source}: {Msg}", e.Referencia, e.Source, msg);
                            else
                                _log.LogInformation("Auto SAP OK {Ref} {Source}", e.Referencia, e.Source);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error en AutoEnviarEntregasHostedService");
                }

                // usa el menor interval activo (para no dormir de más)
                var minInterval = all.Where(x => x.Enabled).Select(x => x.IntervalMs).DefaultIfEmpty(5000).Min();
                await Task.Delay(Math.Max(1000, minInterval), stoppingToken);
            }
        }
    }
}
