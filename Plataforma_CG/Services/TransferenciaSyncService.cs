using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;

namespace Plataforma_CG.Services
{
    public class TransferenciaSyncService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _cfg;
        private readonly ILogger<TransferenciaSyncService> _logger;

        private const int BatchSize = 50;
        private const int MaxIntentos = 5;
        private const int Cmd = 600;

        private const int UsuarioId = 1000;
        private const string DeviceId = "TRANSFERENCIA_WEB_SIGO";

        public TransferenciaSyncService(
            AppDbContext context,
            IConfiguration cfg,
            ILogger<TransferenciaSyncService> logger)
        {
            _context = context;
            _cfg = cfg;
            _logger = logger;
        }

        private sealed class SyncJobRow
        {
            public int JobId { get; set; }
            public int TransferenciaId { get; set; }
            public string Estado { get; set; } = "";
        }

        private sealed class SyncDetRow
        {
            public int SyncDetalleId { get; set; }
            public int JobId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
            public string Estado { get; set; } = "";
            public int Intentos { get; set; }
        }

        private sealed class TransferenciaDestinoRow
        {
            public int TransferenciaId { get; set; }
            public string Sucursal { get; set; } = "";
            public string DestinoAlmacen { get; set; } = "";
        }

        private sealed class EtiquetaActivaRow
        {
            public string CodigoEtiqueta { get; set; } = "";
            public int ProduccionId { get; set; }
        }

        private static string NormalizarEtiqueta(string? value) =>
            (value ?? "").Trim().ToUpperInvariant();

        /// <summary>
        /// NUEVO:
        /// Llamar esto justo después de crear el Job/Detalle.
        /// Resuelve de inmediato las etiquetas que YA viven en P1.
        /// Lo que no esté en P1 se queda pendiente para el worker/TIF.
        /// </summary>
        public async Task<int> ProcesarPendientesP1DirectoAsync(int jobId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            var job = await conn.QueryFirstOrDefaultAsync<SyncJobRow>(@"
SELECT TOP 1
    j.JobId,
    j.TransferenciaId,
    j.Estado
FROM dbo.TransferenciaSyncJob j
WHERE j.JobId = @JobId;",
                new { JobId = jobId });

            if (job == null)
                return 0;

            var t = await conn.QueryFirstOrDefaultAsync<TransferenciaDestinoRow>(@"
SELECT TOP 1
    t.Id AS TransferenciaId,
    UPPER(LTRIM(RTRIM(t.Sucursal))) AS Sucursal,
    LTRIM(RTRIM(s.AlmacenTransitoId)) AS DestinoAlmacen
FROM dbo.Transferencias t
LEFT JOIN dbo.series s
    ON UPPER(LTRIM(RTRIM(s.Sucursal))) = UPPER(LTRIM(RTRIM(t.Sucursal)))
WHERE t.Id = @TransferenciaId;",
                new { job.TransferenciaId });

            if (t == null || string.IsNullOrWhiteSpace(t.DestinoAlmacen))
                return 0;

            var csP1 = _cfg.GetConnectionString("CadenaMeatP1");
            if (string.IsNullOrWhiteSpace(csP1))
                return 0;

            var procesadas = await ResolverPendientesLocalesEnP1Async(
                conn,
                job.JobId,
                job.TransferenciaId,
                t.DestinoAlmacen,
                csP1,
                ct);

            await RecalcularEstadoJobAsync(conn, job.JobId, job.TransferenciaId);
            return procesadas;
        }

        public async Task ProcesarSiguienteLoteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            var job = await conn.QueryFirstOrDefaultAsync<SyncJobRow>(@"
SELECT TOP 1
    j.JobId,
    j.TransferenciaId,
    j.Estado
FROM dbo.TransferenciaSyncJob j
WHERE j.Estado IN ('Pendiente', 'EnProceso', 'ErrorParcial')
  AND EXISTS
  (
      SELECT 1
      FROM dbo.TransferenciaSyncDetalle d
      WHERE d.JobId = j.JobId
        AND d.Estado = 'Pendiente'
        AND d.Intentos < @MaxIntentos
  )
ORDER BY
    CASE j.Estado
        WHEN 'EnProceso' THEN 0
        WHEN 'Pendiente' THEN 1
        ELSE 2
    END,
    j.JobId;",
                new { MaxIntentos });

            if (job == null)
                return;

            await conn.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncJob
   SET Estado = 'EnProceso',
       Intentos = ISNULL(Intentos, 0) + 1,
       FechaInicio = ISNULL(FechaInicio, GETDATE())
 WHERE JobId = @JobId;",
                new { job.JobId });

            var t = await conn.QueryFirstOrDefaultAsync<TransferenciaDestinoRow>(@"
SELECT TOP 1
    t.Id AS TransferenciaId,
    UPPER(LTRIM(RTRIM(t.Sucursal))) AS Sucursal,
    LTRIM(RTRIM(s.AlmacenTransitoId)) AS DestinoAlmacen
FROM dbo.Transferencias t
LEFT JOIN dbo.series s
    ON UPPER(LTRIM(RTRIM(s.Sucursal))) = UPPER(LTRIM(RTRIM(t.Sucursal)))
WHERE t.Id = @TransferenciaId;",
                new { job.TransferenciaId });

            if (t == null)
            {
                await conn.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncJob
   SET Estado = 'Error',
       UltimoError = 'No se encontró la transferencia origen.',
       FechaFin = GETDATE()
 WHERE JobId = @JobId;",
                    new { job.JobId });
                return;
            }

            if (string.IsNullOrWhiteSpace(t.DestinoAlmacen))
            {
                await conn.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncJob
   SET Estado = 'Error',
       UltimoError = 'No se encontró AlmacenTransitoId para la sucursal.',
       FechaFin = GETDATE()
 WHERE JobId = @JobId;",
                    new { job.JobId });
                return;
            }

            var csP1 = _cfg.GetConnectionString("CadenaMeatP1");
            if (string.IsNullOrWhiteSpace(csP1))
            {
                await conn.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncJob
   SET Estado = 'Error',
       UltimoError = 'Falta connection string de P1.',
       FechaFin = GETDATE()
 WHERE JobId = @JobId;",
                    new { job.JobId });
                return;
            }

            // NUEVO:
            // Antes de mandar nada a TIF, resolvemos en bloque lo que ya existe en P1.
            await ResolverPendientesLocalesEnP1Async(
                conn,
                job.JobId,
                job.TransferenciaId,
                t.DestinoAlmacen,
                csP1,
                ct);

            var detalles = (await conn.QueryAsync<SyncDetRow>(@"
SELECT TOP (@BatchSize)
    d.SyncDetalleId,
    d.JobId,
    d.CodigoEtiqueta,
    d.Estado,
    d.Intentos
FROM dbo.TransferenciaSyncDetalle d
WHERE d.JobId = @JobId
  AND d.Estado = 'Pendiente'
  AND d.Intentos < @MaxIntentos
ORDER BY d.SyncDetalleId;",
                new
                {
                    job.JobId,
                    BatchSize,
                    MaxIntentos
                })).ToList();

            if (detalles.Count == 0)
            {
                await RecalcularEstadoJobAsync(conn, job.JobId, job.TransferenciaId);
                return;
            }

            var csTif = _cfg.GetConnectionString("CadenaMeatTIF");
            if (string.IsNullOrWhiteSpace(csTif))
            {
                await conn.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncJob
   SET Estado = 'ErrorParcial',
       UltimoError = 'Falta connection string de TIF para las etiquetas no localizadas en P1.',
       FechaFin = GETDATE()
 WHERE JobId = @JobId;",
                    new { job.JobId });
                return;
            }

            foreach (var d in detalles)
            {
                ct.ThrowIfCancellationRequested();
                await ProcesarDetalleAsync(
                    conn,
                    d,
                    t.DestinoAlmacen,
                    csP1,
                    csTif);
            }

            await RecalcularEstadoJobAsync(conn, job.JobId, job.TransferenciaId);
        }

        /// <summary>
        /// NUEVO:
        /// Busca todas las etiquetas pendientes del job que ya existan activas en P1,
        /// las mueve al almacén destino y las deja en Ok sin esperar al worker de TIF.
        /// </summary>
        private async Task<int> ResolverPendientesLocalesEnP1Async(
            IDbConnection connMain,
            int jobId,
            int transferenciaId,
            string destinoAlmacen,
            string csP1,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var detalles = (await connMain.QueryAsync<SyncDetRow>(@"
SELECT
    d.SyncDetalleId,
    d.JobId,
    d.CodigoEtiqueta,
    d.Estado,
    d.Intentos
FROM dbo.TransferenciaSyncDetalle d
WHERE d.JobId = @JobId
  AND d.Estado = 'Pendiente'
  AND d.Intentos < @MaxIntentos
ORDER BY d.SyncDetalleId;",
                new
                {
                    JobId = jobId,
                    MaxIntentos
                })).ToList();

            if (detalles.Count == 0)
                return 0;

            var mapaP1 = await BuscarEtiquetasActivasConProduccionIdEnDbAsync(
                csP1,
                detalles.Select(x => x.CodigoEtiqueta));

            if (mapaP1.Count == 0)
                return 0;

            var locales = detalles
                .Select(d => new
                {
                    Detalle = d,
                    Etiqueta = NormalizarEtiqueta(d.CodigoEtiqueta)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Etiqueta) && mapaP1.ContainsKey(x.Etiqueta))
                .ToList();

            if (locales.Count == 0)
                return 0;

            // Marcamos intento/EnProceso solo para las locales en P1
            await connMain.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncDetalle
   SET Estado = 'EnProceso',
       Intentos = ISNULL(Intentos, 0) + 1,
       FechaUltimoIntento = GETDATE(),
       UltimoError = NULL
 WHERE SyncDetalleId = @SyncDetalleId;",
                locales.Select(x => new { x.Detalle.SyncDetalleId }));

            var etiquetasLocales = locales
                .Select(x => x.Etiqueta)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var r = await UpdateEnDb(csP1, etiquetasLocales, destinoAlmacen);

            if (r.prodActualizadas == 0 || r.etiquetasEncontradas.Count == 0)
            {
                // Si por alguna razón no se actualizaron, regresan a pendiente
                await connMain.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncDetalle
   SET Estado = 'Pendiente',
       UltimoError = NULL
 WHERE SyncDetalleId = @SyncDetalleId;",
                    locales.Select(x => new { x.Detalle.SyncDetalleId }));

                return 0;
            }

            var etiquetasOk = new HashSet<string>(
                r.etiquetasEncontradas.Select(NormalizarEtiqueta),
                StringComparer.OrdinalIgnoreCase);

            var updatesOk = locales
                .Where(x => etiquetasOk.Contains(x.Etiqueta))
                .Select(x => new
                {
                    x.Detalle.SyncDetalleId,
                    ProduccionIdP1 = mapaP1[x.Etiqueta]
                })
                .ToList();

            if (updatesOk.Count > 0)
            {
                await connMain.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncDetalle
   SET Estado = 'Ok',
       UltimoError = NULL,
       ProduccionIdP1 = @ProduccionIdP1
 WHERE SyncDetalleId = @SyncDetalleId;",
                    updatesOk);
            }

            var updatesPendiente = locales
                .Where(x => !etiquetasOk.Contains(x.Etiqueta))
                .Select(x => new { x.Detalle.SyncDetalleId })
                .ToList();

            if (updatesPendiente.Count > 0)
            {
                await connMain.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncDetalle
   SET Estado = 'Pendiente',
       UltimoError = NULL
 WHERE SyncDetalleId = @SyncDetalleId;",
                    updatesPendiente);
            }

            return updatesOk.Count;
        }

        /// <summary>
        /// NUEVO:
        /// Devuelve etiqueta + ProduccionId de todo lo activo en P1.
        /// Esto evita consultas individuales por cada etiqueta local.
        /// </summary>
        private async Task<Dictionary<string, int>> BuscarEtiquetasActivasConProduccionIdEnDbAsync(
            string cs,
            IEnumerable<string> etiquetas)
        {
            var etqs = (etiquetas ?? Enumerable.Empty<string>())
                .Select(NormalizarEtiqueta)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (etqs.Count == 0 || string.IsNullOrWhiteSpace(cs))
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();

            var rows = (await cn.QueryAsync<EtiquetaActivaRow>(@"
SELECT
    UPPER(LTRIM(RTRIM(P.CodigoEtiqueta))) AS CodigoEtiqueta,
    MAX(P.ProduccionId) AS ProduccionId
FROM dbo.Produccion P
WHERE UPPER(LTRIM(RTRIM(P.CodigoEtiqueta))) IN @Etiquetas
  AND P.Estatus = 1
GROUP BY UPPER(LTRIM(RTRIM(P.CodigoEtiqueta)));",
                new { Etiquetas = etqs },
                commandTimeout: Cmd)).ToList();

            return rows.ToDictionary(
                x => NormalizarEtiqueta(x.CodigoEtiqueta),
                x => x.ProduccionId,
                StringComparer.OrdinalIgnoreCase);
        }

        private async Task ProcesarDetalleAsync(
            IDbConnection connMain,
            SyncDetRow d,
            string destinoAlmacen,
            string csP1,
            string csTif)
        {
            var etq = (d.CodigoEtiqueta ?? "").Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(etq))
                return;

            await connMain.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncDetalle
   SET Estado = 'EnProceso',
       Intentos = ISNULL(Intentos, 0) + 1,
       FechaUltimoIntento = GETDATE(),
       UltimoError = NULL
 WHERE SyncDetalleId = @SyncDetalleId;",
                new { d.SyncDetalleId });

            try
            {
                var enP1 = await BuscarEtiquetasActivasEnDb(csP1, new[] { etq });

                var enTif = new List<string>();
                if (enP1.Count == 0)
                    enTif = await BuscarEtiquetasActivasEnDb(csTif, new[] { etq });

                var copiadas = new List<string>();
                if (enP1.Count == 0 && enTif.Count > 0)
                    copiadas = await CopiarEtiquetasTifAP1Async(csTif, csP1, enTif);

                var aProcesarEnP1 = enP1
                    .Concat(copiadas)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (aProcesarEnP1.Count == 0)
                    throw new Exception("La etiqueta no existe activa en P1 ni en TIF.");

                var r = await UpdateEnDb(csP1, aProcesarEnP1, destinoAlmacen);

                if (r.prodActualizadas == 0)
                    throw new Exception("No se actualizó ninguna producción en P1.");

                var produccionIdP1 = await ObtenerProduccionIdP1Async(csP1, etq);

                await connMain.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncDetalle
   SET Estado = 'Ok',
       UltimoError = NULL,
       ProduccionIdP1 = @ProduccionIdP1
 WHERE SyncDetalleId = @SyncDetalleId;",
                    new
                    {
                        d.SyncDetalleId,
                        ProduccionIdP1 = produccionIdP1
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando etiqueta {Etiqueta}", etq);

                var estadoError = (d.Intentos + 1) >= MaxIntentos ? "Error" : "Pendiente";

                await connMain.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncDetalle
   SET Estado = @Estado,
       UltimoError = @UltimoError
 WHERE SyncDetalleId = @SyncDetalleId;",
                    new
                    {
                        d.SyncDetalleId,
                        Estado = estadoError,
                        UltimoError = ex.Message
                    });
            }
        }

        private async Task RecalcularEstadoJobAsync(IDbConnection conn, int jobId, int transferenciaId)
        {
            var resumen = await conn.QueryFirstAsync(@"
SELECT
    Total      = COUNT(1),
    Pendientes = SUM(CASE WHEN Estado = 'Pendiente' THEN 1 ELSE 0 END),
    EnProceso  = SUM(CASE WHEN Estado = 'EnProceso' THEN 1 ELSE 0 END),
    OkCount    = SUM(CASE WHEN Estado = 'Ok' THEN 1 ELSE 0 END),
    ErrCount   = SUM(CASE WHEN Estado = 'Error' THEN 1 ELSE 0 END)
FROM dbo.TransferenciaSyncDetalle
WHERE JobId = @JobId;",
                new { JobId = jobId });

            int total = resumen.Total;
            int pendientes = resumen.Pendientes;
            int enProceso = resumen.EnProceso;
            int okCount = resumen.OkCount;
            int errCount = resumen.ErrCount;

            int procesadas = okCount + errCount;

            string estadoFinal;
            if (okCount == total)
                estadoFinal = "Completado";
            else if (pendientes > 0 || enProceso > 0)
                estadoFinal = "EnProceso";
            else if (okCount > 0 && errCount > 0)
                estadoFinal = "ErrorParcial";
            else
                estadoFinal = "Error";

            await conn.ExecuteAsync(@"
UPDATE dbo.TransferenciaSyncJob
   SET Estado = @Estado,
       Procesadas = @Procesadas,
       Exitosas = @Exitosas,
       Fallidas = @Fallidas,
       FechaFin = CASE WHEN @Estado IN ('Completado','ErrorParcial','Error') THEN GETDATE() ELSE NULL END
 WHERE JobId = @JobId;",
                new
                {
                    JobId = jobId,
                    Estado = estadoFinal,
                    Procesadas = procesadas,
                    Exitosas = okCount,
                    Fallidas = errCount
                });

            if (estadoFinal == "Completado")
            {
                await conn.ExecuteAsync(@"
UPDATE dbo.Transferencias
   SET Estatus = 5
 WHERE Id = @TransferenciaId;",
                    new { TransferenciaId = transferenciaId });
            }
        }

        private async Task<int?> ObtenerProduccionIdP1Async(string csP1, string codigoEtiqueta)
        {
            await using var cn = new SqlConnection(csP1);
            await cn.OpenAsync();

            return await cn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1 ProduccionId
FROM dbo.Produccion
WHERE UPPER(LTRIM(RTRIM(CodigoEtiqueta))) = @CodigoEtiqueta;",
                new { CodigoEtiqueta = codigoEtiqueta });
        }

        private async Task<List<string>> BuscarEtiquetasActivasEnDb(string cs, IEnumerable<string> etiquetas)
        {
            var etqs = (etiquetas ?? Enumerable.Empty<string>())
                .Select(x => (x ?? "").Trim().ToUpper())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (etqs.Count == 0 || string.IsNullOrWhiteSpace(cs))
                return new List<string>();

            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();

            var found = (await cn.QueryAsync<string>(@"
SELECT DISTINCT UPPER(LTRIM(RTRIM(P.CodigoEtiqueta)))
FROM dbo.Produccion P
WHERE UPPER(LTRIM(RTRIM(P.CodigoEtiqueta))) IN @Etiquetas
  AND P.Estatus = 1;",
                new { Etiquetas = etqs },
                commandTimeout: Cmd)).ToList();

            return found;
        }

        private async Task<(int refsEliminadas, int prodActualizadas, int logsInsertados, List<string> etiquetasEncontradas)>
            UpdateEnDb(string cs, List<string> etqs, string destinoAlmacen)
        {
            if (etqs == null || etqs.Count == 0)
                return (0, 0, 0, new List<string>());

            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                var etiquetasEncontradas = (await cn.QueryAsync<string>(@"
SELECT DISTINCT UPPER(LTRIM(RTRIM(P.CodigoEtiqueta)))
FROM dbo.Produccion P
WHERE UPPER(LTRIM(RTRIM(P.CodigoEtiqueta))) IN @Etiquetas
  AND P.Estatus = 1;",
                    new { Etiquetas = etqs },
                    transaction: tx,
                    commandTimeout: Cmd)).ToList();

                if (etiquetasEncontradas.Count == 0)
                {
                    await tx.CommitAsync();
                    return (0, 0, 0, new List<string>());
                }

                var logsInsertados = await cn.ExecuteAsync(@"
INSERT INTO dbo.ProduccionLog
(
    ProduccionLogId,
    ProduccionId,
    ProcesoId,
    CodigoEtiqueta,
    Almacen,
    Articulo,
    Unidades,
    Peso,
    UsuarioId,
    DeviceId,
    FechaHora,
    FechaHoraServer
)
SELECT
    ISNULL((SELECT MAX(L.ProduccionLogId) FROM dbo.ProduccionLog L), 0)
        + ROW_NUMBER() OVER (ORDER BY P.ProduccionId),
    P.ProduccionId,
    19,
    P.CodigoEtiqueta,
    @DestinoAlmacen,
    P.Articulo,
    ISNULL(P.Unidades, 0),
    ISNULL(P.PesoNeto, 0),
    @UsuarioId,
    @DeviceId,
    GETDATE(),
    GETDATE()
FROM dbo.Produccion P
WHERE UPPER(LTRIM(RTRIM(P.CodigoEtiqueta))) IN @Etiquetas
  AND P.Estatus = 1;",
                    new
                    {
                        Etiquetas = etiquetasEncontradas,
                        DestinoAlmacen = destinoAlmacen,
                        UsuarioId,
                        DeviceId
                    },
                    transaction: tx,
                    commandTimeout: Cmd);

                var refsEliminadas = await cn.ExecuteAsync(@"
DELETE PR
FROM dbo.ProduccionReferencia PR
INNER JOIN dbo.Produccion P
    ON P.ProduccionId = PR.ProduccionId
WHERE UPPER(LTRIM(RTRIM(P.CodigoEtiqueta))) IN @Etiquetas
  AND P.Estatus = 1;",
                    new { Etiquetas = etiquetasEncontradas },
                    transaction: tx,
                    commandTimeout: Cmd);

                var prodActualizadas = await cn.ExecuteAsync(@"
UPDATE P
   SET P.Almacen = @DestinoAlmacen
FROM dbo.Produccion P
WHERE UPPER(LTRIM(RTRIM(P.CodigoEtiqueta))) IN @Etiquetas
  AND P.Estatus = 1;",
                    new
                    {
                        DestinoAlmacen = destinoAlmacen,
                        Etiquetas = etiquetasEncontradas
                    },
                    transaction: tx,
                    commandTimeout: Cmd);

                await tx.CommitAsync();
                return (refsEliminadas, prodActualizadas, logsInsertados, etiquetasEncontradas);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private async Task<List<string>> CopiarEtiquetasTifAP1Async(string csTif, string csP1, IEnumerable<string> etiquetas)
        {
            var etqs = (etiquetas ?? Enumerable.Empty<string>())
                .Select(x => (x ?? "").Trim().ToUpper())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (etqs.Count == 0) return new List<string>();

            static int? ToIntN(object? v) =>
                v == null || v is DBNull ? null : Convert.ToInt32(v);

            static decimal? ToDecN(object? v) =>
                v == null || v is DBNull ? null : Convert.ToDecimal(v);

            static DateTime? ToDateN(object? v) =>
                v == null || v is DBNull ? null : Convert.ToDateTime(v);

            static bool? ToBoolN(object? v) =>
                v == null || v is DBNull ? null : Convert.ToBoolean(v);

            static string? ToStr(object? v) =>
                v == null || v is DBNull ? null : Convert.ToString(v);

            var copiadas = new List<string>();

            await using var cnTif = new SqlConnection(csTif);
            await using var cnP1 = new SqlConnection(csP1);

            await cnTif.OpenAsync();
            await cnP1.OpenAsync();

            await using var tx = await cnP1.BeginTransactionAsync();

            try
            {
                foreach (var etq in etqs)
                {
                    var p = await cnTif.QueryFirstOrDefaultAsync(@"
SELECT
    ProduccionId,
    LoteId,
    CodigoEtiqueta,
    CantidadImpresiones,
    Consecutivo,
    Articulo,
    Unidades,
    CalidadId,
    TipoEtiquetaId,
    Almacen,
    Estatus,
    PesoNeto,
    UltimoProcesoId,
    FechaProduccion,
    FechaInventario,
    FechaHora,
    FechaHoraServer
FROM dbo.Produccion
WHERE UPPER(LTRIM(RTRIM(CodigoEtiqueta))) = @Etq
  AND Estatus = 1;",
                        new { Etq = etq },
                        commandTimeout: Cmd);

                    if (p == null)
                        continue;

                    var codigoEtiqueta = ToStr(p.CodigoEtiqueta)?.Trim().ToUpper();
                    if (string.IsNullOrWhiteSpace(codigoEtiqueta))
                        continue;

                    var existeP1 = await cnP1.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.Produccion
WHERE UPPER(LTRIM(RTRIM(CodigoEtiqueta))) = @Etq;",
                        new { Etq = codigoEtiqueta },
                        transaction: tx,
                        commandTimeout: Cmd);

                    if (existeP1 > 0)
                        continue;

                    var tifProduccionId = ToIntN(p.ProduccionId);
                    var tifLoteId = ToIntN(p.LoteId);

                    if (!tifProduccionId.HasValue || !tifLoteId.HasValue)
                        continue;

                    var sp = await cnTif.QueryFirstOrDefaultAsync(@"
SELECT
    SolicitudProduccionId,
    Articulo,
    Cantidad,
    ProcesoId,
    EstatusId,
    FechaProduccion,
    FechaProgramada,
    FechaHora,
    FechaHoraServer,
    TipoSolicitudProduccionId
FROM dbo.SolicitudProduccion
WHERE SolicitudProduccionId = @SolicitudProduccionId;",
                        new { SolicitudProduccionId = tifLoteId.Value },
                        commandTimeout: Cmd);

                    if (sp == null)
                        throw new Exception($"No existe SolicitudProduccion {tifLoteId.Value} en TIF para la etiqueta {codigoEtiqueta}.");

                    var existeSpP1 = await cnP1.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.SolicitudProduccion
WHERE SolicitudProduccionId = @SolicitudProduccionId;",
                        new { SolicitudProduccionId = tifLoteId.Value },
                        transaction: tx,
                        commandTimeout: Cmd);

                    if (existeSpP1 == 0)
                    {
                        await cnP1.ExecuteAsync(
                            "SET IDENTITY_INSERT dbo.SolicitudProduccion ON;",
                            transaction: tx,
                            commandTimeout: Cmd);

                        await cnP1.ExecuteAsync(@"
INSERT INTO dbo.SolicitudProduccion
(
    SolicitudProduccionId,
    Articulo,
    Cantidad,
    ProcesoId,
    EstatusId,
    FechaProduccion,
    FechaProgramada,
    FechaHora,
    FechaHoraServer,
    TipoSolicitudProduccionId
)
VALUES
(
    @SolicitudProduccionId,
    @Articulo,
    @Cantidad,
    @ProcesoId,
    @EstatusId,
    @FechaProduccion,
    @FechaProgramada,
    @FechaHora,
    @FechaHoraServer,
    @TipoSolicitudProduccionId
);",
                            new
                            {
                                SolicitudProduccionId = ToIntN(sp.SolicitudProduccionId),
                                Articulo = ToStr(sp.Articulo),
                                Cantidad = ToIntN(sp.Cantidad),
                                ProcesoId = ToIntN(sp.ProcesoId),
                                EstatusId = ToIntN(sp.EstatusId),
                                FechaProduccion = ToDateN(sp.FechaProduccion),
                                FechaProgramada = ToDateN(sp.FechaProgramada),
                                FechaHora = ToDateN(sp.FechaHora),
                                FechaHoraServer = ToDateN(sp.FechaHoraServer),
                                TipoSolicitudProduccionId = ToIntN(sp.TipoSolicitudProduccionId)
                            },
                            transaction: tx,
                            commandTimeout: Cmd);

                        await cnP1.ExecuteAsync(
                            "SET IDENTITY_INSERT dbo.SolicitudProduccion OFF;",
                            transaction: tx,
                            commandTimeout: Cmd);
                    }

                    var lote = await cnTif.QueryFirstOrDefaultAsync(@"
SELECT
    LoteId,
    Nombre,
    PesoTotal,
    Unidad,
    Maquila,
    TipoLoteId,
    EstatusId,
    FechaProduccion,
    FechaHora,
    FechaHoraServer
FROM dbo.Lote
WHERE LoteId = @LoteId;",
                        new { LoteId = tifLoteId.Value },
                        commandTimeout: Cmd);

                    if (lote == null)
                        throw new Exception($"No existe Lote {tifLoteId.Value} en TIF para la etiqueta {codigoEtiqueta}.");

                    var existeLoteP1 = await cnP1.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.Lote
WHERE LoteId = @LoteId;",
                        new { LoteId = tifLoteId.Value },
                        transaction: tx,
                        commandTimeout: Cmd);

                    if (existeLoteP1 == 0)
                    {
                        await cnP1.ExecuteAsync(@"
INSERT INTO dbo.Lote
(
    LoteId,
    Nombre,
    PesoTotal,
    Unidad,
    Maquila,
    TipoLoteId,
    EstatusId,
    FechaProduccion,
    FechaHora,
    FechaHoraServer
)
VALUES
(
    @LoteId,
    @Nombre,
    @PesoTotal,
    @Unidad,
    @Maquila,
    @TipoLoteId,
    @EstatusId,
    @FechaProduccion,
    @FechaHora,
    @FechaHoraServer
);",
                            new
                            {
                                LoteId = ToIntN(lote.LoteId),
                                Nombre = ToStr(lote.Nombre),
                                PesoTotal = ToDecN(lote.PesoTotal),
                                Unidad = ToIntN(lote.Unidad),
                                Maquila = ToBoolN(lote.Maquila),
                                TipoLoteId = ToIntN(lote.TipoLoteId),
                                EstatusId = ToIntN(lote.EstatusId),
                                FechaProduccion = ToDateN(lote.FechaProduccion),
                                FechaHora = ToDateN(lote.FechaHora),
                                FechaHoraServer = ToDateN(lote.FechaHoraServer)
                            },
                            transaction: tx,
                            commandTimeout: Cmd);
                    }

                    var nuevaProduccionId = await cnP1.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.Produccion
(
    LoteId,
    CodigoEtiqueta,
    CantidadImpresiones,
    Consecutivo,
    Articulo,
    Unidades,
    CalidadId,
    TipoEtiquetaId,
    Almacen,
    Estatus,
    PesoNeto,
    UltimoProcesoId,
    FechaProduccion,
    FechaInventario,
    FechaHora,
    FechaHoraServer
)
VALUES
(
    @LoteId,
    @CodigoEtiqueta,
    @CantidadImpresiones,
    @Consecutivo,
    @Articulo,
    @Unidades,
    @CalidadId,
    @TipoEtiquetaId,
    @Almacen,
    @Estatus,
    @PesoNeto,
    @UltimoProcesoId,
    @FechaProduccion,
    @FechaInventario,
    @FechaHora,
    @FechaHoraServer
);

SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        new
                        {
                            LoteId = tifLoteId.Value,
                            CodigoEtiqueta = codigoEtiqueta,
                            CantidadImpresiones = ToIntN(p.CantidadImpresiones),
                            Consecutivo = ToIntN(p.Consecutivo),
                            Articulo = ToStr(p.Articulo),
                            Unidades = ToDecN(p.Unidades),
                            CalidadId = ToIntN(p.CalidadId),
                            TipoEtiquetaId = ToIntN(p.TipoEtiquetaId),
                            Almacen = ToStr(p.Almacen),
                            Estatus = ToBoolN(p.Estatus),
                            PesoNeto = ToDecN(p.PesoNeto),
                            UltimoProcesoId = ToIntN(p.UltimoProcesoId),
                            FechaProduccion = ToDateN(p.FechaProduccion),
                            FechaInventario = ToDateN(p.FechaInventario),
                            FechaHora = ToDateN(p.FechaHora),
                            FechaHoraServer = ToDateN(p.FechaHoraServer)
                        },
                        transaction: tx,
                        commandTimeout: Cmd);

                    var pesos = (await cnTif.QueryAsync(@"
SELECT
    ProduccionId,
    TipoPesoId,
    Peso,
    FechaHora,
    FechaHoraServer
FROM dbo.PesoProducto
WHERE ProduccionId = @ProduccionId;",
                        new { ProduccionId = tifProduccionId.Value },
                        commandTimeout: Cmd)).ToList();

                    foreach (var x in pesos)
                    {
                        await cnP1.ExecuteAsync(@"
INSERT INTO dbo.PesoProducto
(
    ProduccionId,
    TipoPesoId,
    Peso,
    FechaHora,
    FechaHoraServer
)
VALUES
(
    @ProduccionId,
    @TipoPesoId,
    @Peso,
    @FechaHora,
    @FechaHoraServer
);",
                            new
                            {
                                ProduccionId = nuevaProduccionId,
                                TipoPesoId = ToIntN(x.TipoPesoId),
                                Peso = ToDecN(x.Peso),
                                FechaHora = ToDateN(x.FechaHora),
                                FechaHoraServer = ToDateN(x.FechaHoraServer)
                            },
                            transaction: tx,
                            commandTimeout: Cmd);
                    }

                    var costeo = (await cnTif.QueryAsync(@"
SELECT
    LoteId,
    ProduccionId,
    ProcesoId,
    Articulo,
    TipoCosteoId,
    Precio,
    FactorPrecio,
    FactorContribucion,
    CostoUnitario,
    MargenContribucion,
    PorcentajeMargenContribucion,
    CostoLote,
    PrecioLote,
    FechaHora
FROM dbo.Costeo
WHERE ProduccionId = @ProduccionId;",
                        new { ProduccionId = tifProduccionId.Value },
                        commandTimeout: Cmd)).ToList();

                    foreach (var x in costeo)
                    {
                        await cnP1.ExecuteAsync(@"
INSERT INTO dbo.Costeo
(
    LoteId,
    ProduccionId,
    ProcesoId,
    Articulo,
    TipoCosteoId,
    Precio,
    FactorPrecio,
    FactorContribucion,
    CostoUnitario,
    MargenContribucion,
    PorcentajeMargenContribucion,
    CostoLote,
    PrecioLote,
    FechaHora
)
VALUES
(
    @LoteId,
    @ProduccionId,
    @ProcesoId,
    @Articulo,
    @TipoCosteoId,
    @Precio,
    @FactorPrecio,
    @FactorContribucion,
    @CostoUnitario,
    @MargenContribucion,
    @PorcentajeMargenContribucion,
    @CostoLote,
    @PrecioLote,
    @FechaHora
);",
                            new
                            {
                                LoteId = tifLoteId.Value,
                                ProduccionId = nuevaProduccionId,
                                ProcesoId = ToIntN(x.ProcesoId),
                                Articulo = ToStr(x.Articulo),
                                TipoCosteoId = ToIntN(x.TipoCosteoId),
                                Precio = ToDecN(x.Precio),
                                FactorPrecio = ToDecN(x.FactorPrecio),
                                FactorContribucion = ToDecN(x.FactorContribucion),
                                CostoUnitario = ToDecN(x.CostoUnitario),
                                MargenContribucion = ToDecN(x.MargenContribucion),
                                PorcentajeMargenContribucion = ToDecN(x.PorcentajeMargenContribucion),
                                CostoLote = ToDecN(x.CostoLote),
                                PrecioLote = ToDecN(x.PrecioLote),
                                FechaHora = ToDateN(x.FechaHora)
                            },
                            transaction: tx,
                            commandTimeout: Cmd);
                    }

                    var prodCosteo = (await cnTif.QueryAsync(@"
SELECT
    LoteId,
    ProduccionId,
    ProcesoId,
    Articulo,
    CodigoEtiqueta,
    FactorUnidad,
    CostoCanal,
    FechaProduccion,
    FechaHora,
    FechaHoraServer
FROM dbo.ProduccionCosteo
WHERE ProduccionId = @ProduccionId;",
                        new { ProduccionId = tifProduccionId.Value },
                        commandTimeout: Cmd)).ToList();

                    foreach (var x in prodCosteo)
                    {
                        await cnP1.ExecuteAsync(@"
INSERT INTO dbo.ProduccionCosteo
(
    LoteId,
    ProduccionId,
    ProcesoId,
    Articulo,
    CodigoEtiqueta,
    FactorUnidad,
    CostoCanal,
    FechaProduccion,
    FechaHora,
    FechaHoraServer
)
VALUES
(
    @LoteId,
    @ProduccionId,
    @ProcesoId,
    @Articulo,
    @CodigoEtiqueta,
    @FactorUnidad,
    @CostoCanal,
    @FechaProduccion,
    @FechaHora,
    @FechaHoraServer
);",
                            new
                            {
                                LoteId = tifLoteId.Value,
                                ProduccionId = nuevaProduccionId,
                                ProcesoId = ToIntN(x.ProcesoId),
                                Articulo = ToStr(x.Articulo),
                                CodigoEtiqueta = ToStr(x.CodigoEtiqueta),
                                FactorUnidad = ToDecN(x.FactorUnidad),
                                CostoCanal = ToDecN(x.CostoCanal),
                                FechaProduccion = ToDateN(x.FechaProduccion),
                                FechaHora = ToDateN(x.FechaHora),
                                FechaHoraServer = ToDateN(x.FechaHoraServer)
                            },
                            transaction: tx,
                            commandTimeout: Cmd);
                    }

                    copiadas.Add(codigoEtiqueta);
                }

                await tx.CommitAsync();
                return copiadas;
            }
            catch
            {
                try
                {
                    await cnP1.ExecuteAsync(
                        "SET IDENTITY_INSERT dbo.SolicitudProduccion OFF;",
                        transaction: tx,
                        commandTimeout: Cmd);
                }
                catch
                {
                }

                await tx.RollbackAsync();
                throw;
            }
        }
    }
}