using Dapper;
using Microsoft.Data.SqlClient;

namespace Plataforma_CG.Services
{
    public class CosteoProgramadoService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CosteoProgramadoService> _logger;

        public CosteoProgramadoService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<CosteoProgramadoService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RevisarYEjecutarAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error general en CosteoProgramadoService");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task RevisarYEjecutarAsync(CancellationToken ct)
        {
            var horaActual = DateTime.Now.ToString("HH:mm");

            using var cn = new SqlConnection(_configuration.GetConnectionString("CadenaMeatTIF"));
            await cn.OpenAsync(ct);

            var pendientes = (await cn.QueryAsync<CosteoProgramadoRow>(@"
SELECT
    Id,
    Source,
    TipoProceso,
    TipoCosteoId,
    CONVERT(varchar(5), HoraProgramada, 108) AS HoraProgramada,
    BrincarSinCosto,
    ContinuarConError,
    Activo,
    UltimaEjecucion
FROM dbo.meat_CosteoProgramado
WHERE Activo = 1
  AND CONVERT(varchar(5), HoraProgramada, 108) = @HoraActual
  AND (
        UltimaEjecucion IS NULL
        OR CAST(UltimaEjecucion AS date) < CAST(GETDATE() AS date)
      )
ORDER BY
    CASE
        WHEN TipoProceso = 'CAJAS' THEN 1
        WHEN TipoProceso = 'RETRABAJO' THEN 2
        ELSE 9
    END,
    Source;", new { HoraActual = horaActual })).ToList();

            if (!pendientes.Any())
                return;

            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<ICosteoRunnerService>();

            foreach (var item in pendientes)
            {
                try
                {
                    var model = new Plataforma_CG.ViewModels.CosteoFiltroVM
                    {
                        Source = item.Source,
                        TipoProceso = item.TipoProceso,
                        Modo = "DIA",
                        FechaInicial = DateTime.Today,
                        FechaFinal = DateTime.Today,
                        TipoCosteoId = item.TipoCosteoId,
                        BrincarSinCosto = item.BrincarSinCosto,
                        ContinuarConError = item.ContinuarConError,
                        Automatico = true,
                        HoraProgramada = item.HoraProgramada
                    };

                    var results = await runner.EjecutarAsync(model, true);

                    var algunoConError = results.Any(r =>
                    {
                        var prop = r.GetType().GetProperty("ok");
                        return prop != null && prop.GetValue(r) is bool ok && ok == false;
                    });

                    var mensajeFinal = "OK";

                    if (algunoConError)
                    {
                        mensajeFinal = "Uno o más procesos terminaron con error.";
                    }

                    await cn.ExecuteAsync(@"
UPDATE dbo.meat_CosteoProgramado
   SET UltimaEjecucion = GETDATE(),
       UltimoResultado = @UltimoResultado,
       UltimoMensaje = @UltimoMensaje
 WHERE Id = @Id;", new
                    {
                        item.Id,
                        UltimoResultado = !algunoConError,
                        UltimoMensaje = mensajeFinal
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error ejecutando costeo programado Id={Id}", item.Id);

                    await cn.ExecuteAsync(@"
UPDATE dbo.meat_CosteoProgramado
   SET UltimaEjecucion = GETDATE(),
       UltimoResultado = 0,
       UltimoMensaje = @Msg
 WHERE Id = @Id;", new
                    {
                        item.Id,
                        Msg = ex.Message.Length > 1000 ? ex.Message.Substring(0, 1000) : ex.Message
                    });
                }
            }
        }

        private class CosteoProgramadoRow
        {
            public int Id { get; set; }
            public string Source { get; set; }
            public string TipoProceso { get; set; }
            public int TipoCosteoId { get; set; }
            public string HoraProgramada { get; set; }
            public bool BrincarSinCosto { get; set; }
            public bool ContinuarConError { get; set; }
            public bool Activo { get; set; }
            public DateTime? UltimaEjecucion { get; set; }
        }
    }
}