using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Plataforma_CG.Services
{
    public class TransferenciaSyncWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public TransferenciaSyncWorker(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<TransferenciaSyncService>();

                    await svc.ProcesarSiguienteLoteAsync(stoppingToken);
                }
                catch
                {
                }

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}