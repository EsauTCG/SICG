using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plataforma_CG.ViewModels;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public sealed class CierreLotesService : ICierreLotesService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<CierreLotesService> _logger;

        public CierreLotesService(
            IConfiguration configuration,
            ILogger<CierreLotesService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private static string NormalizeSource(string source)
        {
            var s = (source ?? "TIF").Trim().ToUpperInvariant();
            return s == "P1" ? "P1" : "TIF";
        }

        private string GetConnectionString(string source)
        {
            source = NormalizeSource(source);
            var key = source == "TIF" ? "CadenaMeatTIF" : "CadenaMeatP1";
            return _configuration.GetConnectionString(key)
                ?? throw new InvalidOperationException($"No existe la cadena de conexión '{key}'.");
        }

        private static string GetCommerciaDb(string source)
            => NormalizeSource(source) == "TIF" ? "TIF_CommerciaNet" : "CommerciaNet";

        public async Task<List<CierreLoteListaRowVM>> ListarLotesAsync(
            string source,
            DateTime desde,
            DateTime hasta,
            string estado = "ABIERTOS")
        {
            source = NormalizeSource(source);
            if (hasta.Date < desde.Date)
                throw new ArgumentException("La fecha final no puede ser menor a la fecha inicial.");

            var cs = GetConnectionString(source);
            const string sql = @"
SELECT
    l.LoteId,
    CONVERT(nvarchar(200), ISNULL(l.Nombre, '')) AS Nombre,
    CONVERT(int, ISNULL(l.TipoLoteId, 0)) AS TipoLoteId,
    CONVERT(int, ISNULL(l.EstatusId, 0)) AS EstatusId,
    TRY_CONVERT(datetime, l.FechaProduccion) AS FechaProduccion,
    ISNULL(e.Entradas, 0) AS Entradas,
    ISNULL(e.KgEntrada, 0) AS KgEntrada,
    ISNULL(s.Salidas, 0) AS Salidas,
    ISNULL(s.KgSalida, 0) AS KgSalida
FROM dbo.Lote l
OUTER APPLY
(
    SELECT
        COUNT(*) AS Entradas,
        SUM(ISNULL(p.PesoNeto, 0)) AS KgEntrada
    FROM
    (
        SELECT DISTINCT pl.ProduccionId
        FROM dbo.ProduccionLogistica pl
        WHERE pl.SolicitudProduccionId = l.LoteId
    ) eu
    INNER JOIN dbo.Produccion p
        ON p.ProduccionId = eu.ProduccionId
) e
OUTER APPLY
(
    SELECT
        COUNT(*) AS Salidas,
        SUM(ISNULL(p.PesoNeto, 0)) AS KgSalida
    FROM dbo.Produccion p
    WHERE p.LoteId = l.LoteId
      AND ISNULL(p.UltimoProcesoId, 0) <> 29
) s
WHERE TRY_CONVERT(date, l.FechaProduccion) >= @Desde
  AND TRY_CONVERT(date, l.FechaProduccion) < DATEADD(day, 1, @Hasta)
  AND
  (
      @Estado = 'TODOS'
      OR (@Estado = 'ABIERTOS' AND ISNULL(l.EstatusId, 0) <> 3)
      OR (@Estado = 'CERRADOS' AND ISNULL(l.EstatusId, 0) = 3)
  )
ORDER BY TRY_CONVERT(datetime, l.FechaProduccion) DESC, l.LoteId DESC;";

            await using var cn = new SqlConnection(cs);
            var rows = await cn.QueryAsync<CierreLoteListaRowVM>(sql, new
            {
                Desde = desde.Date,
                Hasta = hasta.Date,
                Estado = (estado ?? "ABIERTOS").Trim().ToUpperInvariant()
            }, commandTimeout: 120);

            return rows.ToList();
        }

        public async Task<CierreLoteTipoConfigVM?> ObtenerTipoConfigAsync(string source, int tipoLoteId)
        {
            source = NormalizeSource(source);
            var cs = GetConnectionString(source);

            const string sql = @"
SELECT TOP (1)
    TipoLoteId,
    TipoProceso,
    RequiereEntradasLogistica,
    ValidarCompatibilidad,
    VariacionAdvertenciaPct,
    VariacionBloqueoPct,
    AprobacionesRequeridas,
    BrincarSinCosto,
    Activo
FROM dbo.meat_CierreLoteTipoConfig
WHERE TipoLoteId = @TipoLoteId
  AND Activo = 1;";

            await using var cn = new SqlConnection(cs);
            return await cn.QueryFirstOrDefaultAsync<CierreLoteTipoConfigVM>(
                sql,
                new { TipoLoteId = tipoLoteId },
                commandTimeout: 60);
        }

        public async Task<CierreLoteDiagnosticoVM> DiagnosticarAsync(
            string source,
            int loteId,
            bool validarCosteo = true)
        {
            source = NormalizeSource(source);
            if (loteId <= 0)
                throw new ArgumentException("LoteId inválido.");

            var cs = GetConnectionString(source);
            var dbCommercia = GetCommerciaDb(source);

            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();

            var lote = await cn.QueryFirstOrDefaultAsync<LoteDbRow>(@"
SELECT TOP (1)
    LoteId,
    CONVERT(nvarchar(200), ISNULL(Nombre, '')) AS Nombre,
    CONVERT(int, ISNULL(TipoLoteId, 0)) AS TipoLoteId,
    CONVERT(int, ISNULL(EstatusId, 0)) AS EstatusId,
    TRY_CONVERT(datetime, FechaProduccion) AS FechaProduccion
FROM dbo.Lote
WHERE LoteId = @LoteId;",
                new { LoteId = loteId },
                commandTimeout: 60);

            if (lote == null)
                throw new InvalidOperationException($"No existe el LoteId {loteId} en {source}.");

            var config = await cn.QueryFirstOrDefaultAsync<CierreLoteTipoConfigVM>(@"
SELECT TOP (1)
    TipoLoteId,
    TipoProceso,
    RequiereEntradasLogistica,
    ValidarCompatibilidad,
    VariacionAdvertenciaPct,
    VariacionBloqueoPct,
    AprobacionesRequeridas,
    BrincarSinCosto,
    Activo
FROM dbo.meat_CierreLoteTipoConfig
WHERE TipoLoteId = @TipoLoteId
  AND Activo = 1;",
                new { TipoLoteId = lote.TipoLoteId },
                commandTimeout: 60);

            CanalLoteBaseRow? canalBase = null;
            List<CierreLoteMovimientoVM> movimientos;

            if (string.Equals(config?.TipoProceso, "CANALES", StringComparison.OrdinalIgnoreCase))
            {
                canalBase = await ObtenerCanalLoteBaseAsync(cn, loteId);
                movimientos = await ObtenerMovimientosCanalesAsync(
                    cn, null, loteId, dbCommercia, canalBase?.TipoPesoId);
            }
            else
            {
                movimientos = await ObtenerMovimientosAsync(cn, null, loteId, dbCommercia);
            }

            var entradas = movimientos.Where(x => x.Tipo == "ENTRADA").ToList();
            var salidas = movimientos.Where(x => x.Tipo == "SALIDA").ToList();

            var diagnostico = new CierreLoteDiagnosticoVM
            {
                Source = source,
                LoteId = lote.LoteId,
                LoteNombre = lote.Nombre,
                TipoLoteId = lote.TipoLoteId,
                EstatusId = lote.EstatusId,
                FechaProduccion = lote.FechaProduccion,
                TipoProceso = config?.TipoProceso ?? "",
                AprobacionesRequeridas = Math.Max(1, config?.AprobacionesRequeridas ?? 1),
                TipoPesoIdCanal = canalBase?.TipoPesoId,
                TipoPesoCanal = canalBase?.TipoPesoNombre ?? "",
                Entradas = entradas.Count,
                KgEntrada = decimal.Round(entradas.Sum(x => x.PesoNeto), 3),
                Salidas = salidas.Count,
                KgSalida = decimal.Round(salidas.Sum(x => x.PesoNeto), 3),
                MovimientosEntrada = entradas,
                MovimientosSalida = salidas
            };

            diagnostico.DiferenciaKg = decimal.Round(diagnostico.KgEntrada - diagnostico.KgSalida, 3);
            diagnostico.RendimientoPct = diagnostico.KgEntrada <= 0
                ? 0
                : decimal.Round(diagnostico.KgSalida / diagnostico.KgEntrada * 100m, 2);
            diagnostico.VariacionPct = diagnostico.KgEntrada <= 0
                ? 0
                : decimal.Round(Math.Abs(diagnostico.DiferenciaKg) / diagnostico.KgEntrada * 100m, 2);

            if (lote.EstatusId == 3)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "LOTE_YA_CERRADO",
                    Nivel = "BLOQUEO",
                    Titulo = "El lote ya está cerrado",
                    Detalle = "EstatusId=3. No se permite ejecutar un segundo cierre sobre el mismo lote."
                });
            }

            if (config == null)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "TIPO_LOTE_SIN_CONFIGURAR",
                    Nivel = "BLOQUEO",
                    Titulo = "Tipo de lote sin configuración de cierre",
                    Detalle = $"TipoLoteId={lote.TipoLoteId}. Configure dbo.meat_CierreLoteTipoConfig antes de cerrar este tipo de lote."
                });
            }
            else
            {
                if (string.Equals(config.TipoProceso, "CANALES", StringComparison.OrdinalIgnoreCase))
                {
                    await AgregarAnomaliasCanalesPreCosteoAsync(
                        cn, lote, canalBase, diagnostico);
                }

                if (config.RequiereEntradasLogistica && entradas.Count == 0)
                {
                    diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                    {
                        Codigo = "SIN_ENTRADAS",
                        Nivel = "BLOQUEO",
                        Titulo = "Lote sin etiquetas de entrada",
                        Detalle = "El tipo de lote requiere entradas en ProduccionLogistica y no se encontró ninguna."
                    });
                }

                if (salidas.Count == 0)
                {
                    diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                    {
                        Codigo = "ENTRADAS_SIN_SALIDAS",
                        Nivel = "BLOQUEO",
                        Titulo = "Lote sin etiquetas de salida",
                        Detalle = entradas.Count > 0
                            ? $"Existen {entradas.Count:N0} entrada(s), pero no existe ninguna salida activa del lote."
                            : "No existe ninguna salida activa del lote."
                    });
                }

                if (salidas.Any(x => x.PesoNeto <= 0))
                {
                    var n = salidas.Count(x => x.PesoNeto <= 0);
                    diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                    {
                        Codigo = "SALIDA_PESO_INVALIDO",
                        Nivel = "BLOQUEO",
                        Titulo = "Salidas con peso cero o negativo",
                        Detalle = $"Se detectaron {n:N0} salida(s) con PesoNeto <= 0."
                    });
                }

                if (config.RequiereEntradasLogistica && diagnostico.KgEntrada > 0 && diagnostico.KgSalida > 0)
                {
                    if (config.VariacionBloqueoPct > 0 && diagnostico.VariacionPct >= config.VariacionBloqueoPct)
                    {
                        diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                        {
                            Codigo = "KG_DESBALANCE_CRITICO",
                            Nivel = "BLOQUEO",
                            Titulo = "Variación crítica de kg",
                            Detalle = $"Entrada={diagnostico.KgEntrada:N3} kg, salida={diagnostico.KgSalida:N3} kg, variación={diagnostico.VariacionPct:N2}%. Supera el límite de bloqueo configurado.",
                            Valor = diagnostico.VariacionPct,
                            Limite = config.VariacionBloqueoPct
                        });
                    }
                    else if (config.VariacionAdvertenciaPct > 0 && diagnostico.VariacionPct >= config.VariacionAdvertenciaPct)
                    {
                        diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                        {
                            Codigo = "KG_DESBALANCE",
                            Nivel = "AUTORIZACION",
                            Titulo = "Variación de kg fuera de tolerancia",
                            Detalle = $"Entrada={diagnostico.KgEntrada:N3} kg, salida={diagnostico.KgSalida:N3} kg, variación={diagnostico.VariacionPct:N2}%. Requiere autorización para cerrar.",
                            Valor = diagnostico.VariacionPct,
                            Limite = config.VariacionAdvertenciaPct
                        });
                    }
                }

                if (config.RequiereEntradasLogistica)
                {
                    var duplicadosLog = await cn.QueryAsync<DuplicadoLogisticaRow>(@"
SELECT
    pl.ProduccionId,
    COUNT(*) AS Coincidencias
FROM dbo.ProduccionLogistica pl
WHERE pl.SolicitudProduccionId = @LoteId
GROUP BY pl.ProduccionId
HAVING COUNT(*) > 1;", new { LoteId = loteId }, commandTimeout: 60);

                    var dupList = duplicadosLog.ToList();
                    if (dupList.Count > 0)
                    {
                        diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                        {
                            Codigo = "LOGISTICA_DUPLICADA",
                            Nivel = "BLOQUEO",
                            Titulo = "Entradas duplicadas en ProduccionLogistica",
                            Detalle = $"Se detectaron {dupList.Count:N0} ProduccionId con duplicidad. Esto puede multiplicar kg o costo y debe corregirse antes del cierre."
                        });
                    }
                }

                if (config.ValidarCompatibilidad && entradas.Count > 0 && salidas.Count > 0)
                {
                    await AgregarAnomaliasCompatibilidadAsync(cn, source, entradas, salidas, diagnostico.Anomalias);
                }
            }

            if (validarCosteo && salidas.Count > 0)
            {
                await AgregarAnomaliasCosteoAsync(cn, source, dbCommercia, loteId, diagnostico);
            }

            diagnostico.MovimientoHash = CalcularMovimientoHash(entradas, salidas);
            diagnostico.DiagnosticoHash = CalcularDiagnosticoHash(diagnostico, config);
            diagnostico.TieneBloqueos = diagnostico.Anomalias.Any(x => x.Bloquea);
            diagnostico.RequiereAutorizacion = diagnostico.Anomalias.Any(x => x.RequiereAutorizacion);
            diagnostico.PuedeCerrarSinAutorizacion = !diagnostico.TieneBloqueos && !diagnostico.RequiereAutorizacion;

            return diagnostico;
        }

        private async Task<CanalLoteBaseRow?> ObtenerCanalLoteBaseAsync(
            SqlConnection cn,
            int loteId,
            SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT TOP (1)
    l.LoteId,
    TipoPesoId = ISNULL(
        CASE
            WHEN l.TipoLoteId = 12 THEN 14
            WHEN l.TipoLoteId = 1 THEN
                CASE
                    WHEN drl.DocumentoId IS NULL THEN 0
                    ELSE CASE
                        WHEN mov.Peso = SUM(ISNULL(pp1.Peso,0)) THEN 1
                        WHEN mov.Peso = SUM(ISNULL(pp2.Peso,0)) THEN 2
                        WHEN mov.Peso = SUM(ISNULL(pp3.Peso,0)) THEN 3
                    END
                END
        END,
        CASE
            WHEN pro.Clasificacion = 'P' THEN 1
            WHEN pro.Clasificacion = 'C' THEN 2
            WHEN pro.Clasificacion = 'F' THEN 3
        END
    ),
    Documento = CONVERT(nvarchar(100), ISNULL(doc.Folio, '')),
    Cliente = CONVERT(nvarchar(200), ISNULL(rcli.Referencia, ''))
FROM dbo.Lote l
INNER JOIN dbo.Produccion p
    ON p.LoteId = l.LoteId
   AND ISNULL(p.UltimoProcesoId, 0) <> 29
LEFT JOIN dbo.PesoProducto pp1
    ON pp1.ProduccionId = p.ProduccionId AND pp1.TipoPesoId = 1
LEFT JOIN dbo.PesoProducto pp2
    ON pp2.ProduccionId = p.ProduccionId AND pp2.TipoPesoId = 2
LEFT JOIN dbo.PesoProducto pp3
    ON pp3.ProduccionId = p.ProduccionId AND pp3.TipoPesoId = 3
LEFT JOIN dbo.PesoProducto pp4
    ON pp4.ProduccionId = p.ProduccionId AND pp4.TipoPesoId = 14
INNER JOIN dbo.SolicitudReferencia ref
    ON l.LoteId = ref.SolicitudProduccionId
   AND ref.TipoReferenciaId = 1
INNER JOIN {DBCOM}.dbo.Documento doc
    ON ref.Referencia = CONCAT(doc.EmpresaId,'.',doc.SucursalId,'.',doc.OperacionId,'.',doc.Folio)
INNER JOIN {DBCOM}.dbo.Proveedor pro
    ON pro.ProveedorId = doc.ClienteProveedorId
LEFT JOIN {DBCOM}.dbo.DocumentoRelacionado drl
    ON ref.Referencia = drl.DocumentoRelacionadoId
LEFT JOIN
(
    SELECT
        SUM(ISNULL(Unidad,0)) AS Peso,
        CONCAT(EmpresaId,'.',SucursalId,'.',OperacionId,'.',Folio) AS Documento
    FROM {DBCOM}.dbo.Movimiento
    WHERE OperacionId = 'CCOM'
    GROUP BY CONCAT(EmpresaId,'.',SucursalId,'.',OperacionId,'.',Folio)
) mov
    ON drl.DocumentoId = mov.Documento
INNER JOIN dbo.SolicitudReferencia rcli
    ON l.LoteId = rcli.SolicitudProduccionId
   AND rcli.TipoReferenciaId = 3
WHERE l.LoteId = @LoteId
  AND l.TipoLoteId IN (1,12)
  AND doc.Estatus <> 'Z0'
  AND drl.DocumentoId LIKE '%CCOM%'
GROUP BY
    l.LoteId,
    l.TipoLoteId,
    doc.TipoDocumento,
    pro.Clasificacion,
    doc.Folio,
    rcli.Referencia,
    drl.DocumentoId,
    mov.Peso;";

            // DBCOM is selected by the same connection/source logic used by this service.
            var dbCommercia = cn.Database.Equals("TIF_Meat", StringComparison.OrdinalIgnoreCase)
                ? "TIF_CommerciaNet"
                : "CommerciaNet";

            var row = await cn.QueryFirstOrDefaultAsync<CanalLoteBaseRow>(
                sql.Replace("{DBCOM}", dbCommercia),
                new { LoteId = loteId },
                transaction: tx,
                commandTimeout: 120);

            if (row?.TipoPesoId != null)
            {
                row.TipoPesoNombre = await cn.ExecuteScalarAsync<string>(@"
SELECT TOP (1) CONVERT(nvarchar(100), ISNULL(Nombre,''))
FROM dbo.TipoPeso
WHERE TipoPesoId = @TipoPesoId;",
                    new { TipoPesoId = row.TipoPesoId },
                    transaction: tx,
                    commandTimeout: 60) ?? "";
            }

            return row;
        }

        private async Task<List<CierreLoteMovimientoVM>> ObtenerMovimientosCanalesAsync(
            SqlConnection cn,
            SqlTransaction? tx,
            int loteId,
            string dbCommercia,
            int? tipoPesoId)
        {
            var sql = $@"
SELECT
    'SALIDA' AS Tipo,
    p.ProduccionId,
    p.LoteId,
    CONVERT(nvarchar(200), ISNULL(l.Nombre, '')) AS LoteNombre,
    CONVERT(nvarchar(100), ISNULL(p.Articulo, '')) AS Articulo,
    CONVERT(nvarchar(250), ISNULL(a.Nombre, '')) AS Producto,
    CONVERT(nvarchar(200), ISNULL(p.CodigoEtiqueta, '')) AS CodigoEtiqueta,
    CONVERT(decimal(18,3), ISNULL(pp.Peso, 0)) AS PesoNeto,
    CONVERT(int, ISNULL(p.Estatus, 0)) AS Estatus,
    CONVERT(int, p.UltimoProcesoId) AS UltimoProcesoId
FROM dbo.Produccion p
LEFT JOIN dbo.Lote l ON l.LoteId = p.LoteId
LEFT JOIN {dbCommercia}.dbo.Articulo a ON a.ArticuloId = p.Articulo
LEFT JOIN dbo.PesoProducto pp
    ON pp.ProduccionId = p.ProduccionId
   AND pp.TipoPesoId = @TipoPesoId
WHERE p.LoteId = @LoteId
  AND p.TipoEtiquetaId = 1
  AND ISNULL(p.UltimoProcesoId, 0) <> 29
ORDER BY p.ProduccionId;";

            var salidas = (await cn.QueryAsync<CierreLoteMovimientoVM>(
                sql,
                new { LoteId = loteId, TipoPesoId = tipoPesoId ?? -1 },
                transaction: tx,
                commandTimeout: 120)).ToList();

            return salidas;
        }

        private async Task AgregarAnomaliasCanalesPreCosteoAsync(
            SqlConnection cn,
            LoteDbRow lote,
            CanalLoteBaseRow? canalBase,
            CierreLoteDiagnosticoVM diagnostico)
        {
            if (lote.TipoLoteId != 1 && lote.TipoLoteId != 12)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "TIPO_LOTE_CANAL_INVALIDO",
                    Nivel = "BLOQUEO",
                    Titulo = "Tipo de lote no soportado por costeo de canales",
                    Detalle = $"El costeo de canales confirmado procesa TipoLoteId 1 o 12. El lote actual es TipoLoteId={lote.TipoLoteId}."
                });
                return;
            }

            if (canalBase == null)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "CANAL_SIN_REFERENCIA_COMPRA",
                    Nivel = "BLOQUEO",
                    Titulo = "No se pudo resolver compra/tipo de peso del lote",
                    Detalle = "El lote no cumple la relación de SolicitudReferencia + Documento + CCOM que utiliza dbo.meat_CosteoCanales. No se ejecutará el costeo de cierre."
                });
                return;
            }

            if (!canalBase.TipoPesoId.HasValue ||
                (canalBase.TipoPesoId != 1 && canalBase.TipoPesoId != 2 && canalBase.TipoPesoId != 3 && canalBase.TipoPesoId != 14))
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "CANAL_TIPO_PESO_NO_RESUELTO",
                    Nivel = "BLOQUEO",
                    Titulo = "No se pudo determinar el tipo de peso de costeo",
                    Detalle = $"El procedimiento de canales requiere TipoPesoId 1, 2, 3 o 14. Valor resuelto: {(canalBase.TipoPesoId?.ToString() ?? "NULL")}."
                });
            }

            var faltanClasificacion = await cn.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.Produccion p
LEFT JOIN dbo.CanalDetalle cd ON cd.ProduccionId = p.ProduccionId
WHERE p.LoteId = @LoteId
  AND p.TipoEtiquetaId = 1
  AND ISNULL(p.UltimoProcesoId,0) <> 29
  AND cd.ProduccionId IS NULL;",
                new { LoteId = lote.LoteId },
                commandTimeout: 60);

            if (faltanClasificacion > 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "CANALES_SIN_CLASIFICACION",
                    Nivel = "BLOQUEO",
                    Titulo = "Canales sin clasificación",
                    Detalle = $"Se detectaron {faltanClasificacion:N0} canal(es) sin CanalDetalle. El costeo no puede distribuir correctamente el costo por clasificación."
                });
            }

            var compra = await cn.QueryFirstAsync<CanalCompraResumenRow>(@"
SELECT
    Filas = COUNT_BIG(*),
    Articulos = COUNT(DISTINCT m.ArticuloId),
    CostoTotal = CONVERT(decimal(38,6), ISNULL(SUM(ISNULL(m.CostoFinal,0)),0))
FROM dbo.SolicitudReferencia sr
INNER JOIN {DBCOM}.dbo.DocumentoRelacionado dr
    ON sr.Referencia = dr.DocumentoRelacionadoId
   AND dr.Tipo = 'DC'
INNER JOIN {DBCOM}.dbo.Movimiento m
    ON dbo.split(dr.DocumentoId,'.',4) = m.Folio
   AND dbo.split(dr.DocumentoId,'.',3) = m.OperacionId
   AND dbo.split(dr.DocumentoId,'.',2) = m.SucursalId
   AND dbo.split(dr.DocumentoId,'.',1) = m.EmpresaId
WHERE sr.SolicitudProduccionId = @LoteId
  AND sr.TipoReferenciaId = 1;".Replace(
                    "{DBCOM}",
                    cn.Database.Equals("TIF_Meat", StringComparison.OrdinalIgnoreCase)
                        ? "TIF_CommerciaNet"
                        : "CommerciaNet"),
                new { LoteId = lote.LoteId },
                commandTimeout: 120);

            if (compra.Articulos <= 0 || compra.Filas <= 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "CANALES_SIN_COMPRA",
                    Nivel = "BLOQUEO",
                    Titulo = "No existe compra para costear los canales",
                    Detalle = $"No se encontraron movimientos de compra ligados al lote. Documento/Folio: {canalBase.Documento}. Cliente: {canalBase.Cliente}."
                });
            }
            else if (compra.CostoTotal <= 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "CANALES_COMPRA_SIN_COSTO",
                    Nivel = "BLOQUEO",
                    Titulo = "La compra existe pero no tiene costo positivo",
                    Detalle = $"Se encontraron movimientos de compra, pero la suma de CostoFinal no es positiva ({compra.CostoTotal:N2})."
                });
            }
        }

        private async Task<List<CierreLoteMovimientoVM>> ObtenerMovimientosAsync(
            SqlConnection cn,
            SqlTransaction? tx,
            int loteId,
            string dbCommercia)
        {
            var sql = $@"
;WITH EntradasUnicas AS
(
    SELECT DISTINCT pl.ProduccionId
    FROM dbo.ProduccionLogistica pl
    WHERE pl.SolicitudProduccionId = @LoteId
)
SELECT
    'ENTRADA' AS Tipo,
    p.ProduccionId,
    p.LoteId,
    CONVERT(nvarchar(200), ISNULL(l.Nombre, '')) AS LoteNombre,
    CONVERT(nvarchar(100), ISNULL(p.Articulo, '')) AS Articulo,
    CONVERT(nvarchar(250), ISNULL(a.Nombre, '')) AS Producto,
    CONVERT(nvarchar(200), ISNULL(p.CodigoEtiqueta, '')) AS CodigoEtiqueta,
    CONVERT(decimal(18,3), ISNULL(p.PesoNeto, 0)) AS PesoNeto,
    CONVERT(int, ISNULL(p.Estatus, 0)) AS Estatus,
    CONVERT(int, p.UltimoProcesoId) AS UltimoProcesoId
FROM EntradasUnicas eu
INNER JOIN dbo.Produccion p ON p.ProduccionId = eu.ProduccionId
LEFT JOIN dbo.Lote l ON l.LoteId = p.LoteId
LEFT JOIN {dbCommercia}.dbo.Articulo a ON a.ArticuloId = p.Articulo
ORDER BY p.ProduccionId;

SELECT
    'SALIDA' AS Tipo,
    p.ProduccionId,
    p.LoteId,
    CONVERT(nvarchar(200), ISNULL(l.Nombre, '')) AS LoteNombre,
    CONVERT(nvarchar(100), ISNULL(p.Articulo, '')) AS Articulo,
    CONVERT(nvarchar(250), ISNULL(a.Nombre, '')) AS Producto,
    CONVERT(nvarchar(200), ISNULL(p.CodigoEtiqueta, '')) AS CodigoEtiqueta,
    CONVERT(decimal(18,3), ISNULL(p.PesoNeto, 0)) AS PesoNeto,
    CONVERT(int, ISNULL(p.Estatus, 0)) AS Estatus,
    CONVERT(int, p.UltimoProcesoId) AS UltimoProcesoId
FROM dbo.Produccion p
LEFT JOIN dbo.Lote l ON l.LoteId = p.LoteId
LEFT JOIN {dbCommercia}.dbo.Articulo a ON a.ArticuloId = p.Articulo
WHERE p.LoteId = @LoteId
  AND ISNULL(p.UltimoProcesoId, 0) <> 29
ORDER BY p.ProduccionId;";

            using var multi = await cn.QueryMultipleAsync(
                sql,
                new { LoteId = loteId },
                transaction: tx,
                commandTimeout: 120);

            var entradas = (await multi.ReadAsync<CierreLoteMovimientoVM>()).ToList();
            var salidas = (await multi.ReadAsync<CierreLoteMovimientoVM>()).ToList();
            entradas.AddRange(salidas);
            return entradas;
        }

        private async Task AgregarAnomaliasCompatibilidadAsync(
            SqlConnection cn,
            string source,
            List<CierreLoteMovimientoVM> entradas,
            List<CierreLoteMovimientoVM> salidas,
            List<CierreLoteAnomaliaVM> anomalias)
        {
            var rules = (await cn.QueryAsync<CierreLoteCompatibilidadVM>(@"
SELECT
    CompatibilidadId,
    Source,
    ArticuloEntrada,
    ArticuloSalida,
    Permitido,
    Motivo,
    Activo,
    Usuario,
    FechaHora
FROM dbo.meat_CierreLoteCompatibilidadProducto
WHERE Activo = 1
  AND (Source = @Source OR Source = 'ALL');",
                new { Source = source },
                commandTimeout: 60)).ToList();

            if (rules.Count == 0)
            {
                anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "COMPATIBILIDAD_SIN_CATALOGO",
                    Nivel = "ADVERTENCIA",
                    Titulo = "Catálogo de compatibilidad vacío",
                    Detalle = "La validación entrada → salida está activa, pero no existen reglas. Cargue las relaciones reales de producto para detectar transformaciones imposibles."
                });
                return;
            }

            var inputSet = entradas
                .Select(x => (x.Articulo ?? "").Trim().ToUpperInvariant())
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var outSku in salidas
                .Select(x => (x.Articulo ?? "").Trim().ToUpperInvariant())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var reglasSalida = rules
                    .Where(r => string.Equals((r.ArticuloSalida ?? "").Trim(), outSku, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var prohibidasQueAplican = reglasSalida
                    .Where(r => !r.Permitido && inputSet.Contains((r.ArticuloEntrada ?? "").Trim()))
                    .ToList();

                foreach (var r in prohibidasQueAplican)
                {
                    anomalias.Add(new CierreLoteAnomaliaVM
                    {
                        Codigo = "PRODUCTO_TRANSFORMACION_PROHIBIDA",
                        Nivel = "AUTORIZACION",
                        Titulo = "Combinación de producto entrada/salida no permitida",
                        Detalle = string.IsNullOrWhiteSpace(r.Motivo)
                            ? $"La regla marca como no permitida la transformación {r.ArticuloEntrada} → {r.ArticuloSalida}."
                            : $"{r.ArticuloEntrada} → {r.ArticuloSalida}: {r.Motivo}",
                        ArticuloEntrada = r.ArticuloEntrada,
                        ArticuloSalida = r.ArticuloSalida
                    });
                }

                var permitidas = reglasSalida.Where(r => r.Permitido).ToList();
                if (permitidas.Count > 0)
                {
                    var existeOrigenValido = permitidas.Any(r => inputSet.Contains((r.ArticuloEntrada ?? "").Trim()));
                    if (!existeOrigenValido)
                    {
                        anomalias.Add(new CierreLoteAnomaliaVM
                        {
                            Codigo = "PRODUCTO_SALIDA_SIN_ORIGEN_COMPATIBLE",
                            Nivel = "AUTORIZACION",
                            Titulo = "Producto de salida sin entrada compatible",
                            Detalle = $"El artículo de salida {outSku} tiene reglas de origen configuradas, pero ninguna de las entradas del lote es compatible. Ejemplo del control: una entrada de hueso no debería justificar una salida de arrachera.",
                            ArticuloSalida = outSku
                        });
                    }
                }
            }
        }

        private async Task AgregarAnomaliasCosteoAsync(
            SqlConnection cn,
            string source,
            string dbCommercia,
            int loteId,
            CierreLoteDiagnosticoVM diagnostico)
        {
            if (string.Equals(diagnostico.TipoProceso, "CANALES", StringComparison.OrdinalIgnoreCase))
            {
                await AgregarAnomaliasCosteoCanalesAsync(cn, loteId, diagnostico);
                return;
            }

            var sql = $@"
SELECT
    p.ProduccionId,
    CONVERT(nvarchar(100), ISNULL(p.Articulo, '')) AS Articulo,
    CONVERT(nvarchar(200), ISNULL(p.CodigoEtiqueta, '')) AS CodigoEtiqueta,
    CONVERT(decimal(18,6), ISNULL(p.PesoNeto, 0)) AS PesoNeto,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(CONVERT(varchar(max), a.Mensaje), '')))) = 'SINCOSTO'
        THEN CONVERT(bit, 1)
        ELSE CONVERT(bit, 0)
    END AS EsSinCosto,
    ISNULL(c.Filas, 0) AS FilasCosteo,
    CONVERT(decimal(38,12), ISNULL(c.CostoUnitario, 0)) AS CostoUnitario,
    CONVERT(decimal(38,12), ISNULL(c.CostoLote, 0)) AS CostoLote,
    c.FechaHora AS FechaCosteo,
    ISNULL(pc.Filas, 0) AS FilasProduccionCosteo,
    CONVERT(decimal(38,12), ISNULL(pc.CostoCanal, 0)) AS CostoCanal,
    pc.FechaHora AS FechaProduccionCosteo
FROM dbo.Produccion p
LEFT JOIN {dbCommercia}.dbo.Articulo a
    ON a.ArticuloId = p.Articulo
OUTER APPLY
(
    SELECT
        COUNT(*) AS Filas,
        MAX(CONVERT(decimal(38,12), ISNULL(c1.CostoUnitario, 0))) AS CostoUnitario,
        MAX(CONVERT(decimal(38,12), ISNULL(c1.CostoLote, 0))) AS CostoLote,
        MAX(c1.FechaHora) AS FechaHora
    FROM dbo.Costeo c1
    WHERE c1.LoteId = p.LoteId
      AND c1.ProduccionId = p.ProduccionId
      AND c1.TipoCosteoId = 1
) c
OUTER APPLY
(
    SELECT
        COUNT(*) AS Filas,
        MAX(CONVERT(decimal(38,12), ISNULL(pc1.CostoCanal, 0))) AS CostoCanal,
        MAX(pc1.FechaHora) AS FechaHora
    FROM dbo.ProduccionCosteo pc1
    WHERE pc1.LoteId = p.LoteId
      AND pc1.ProduccionId = p.ProduccionId
) pc
WHERE p.LoteId = @LoteId
  AND ISNULL(p.UltimoProcesoId, 0) <> 29
ORDER BY p.ProduccionId;";

            var rows = (await cn.QueryAsync<CostoSalidaRow>(sql, new { LoteId = loteId }, commandTimeout: 120)).ToList();

            diagnostico.SalidasSinCosteo = rows.Count(x => !x.EsSinCosto && (x.FilasCosteo == 0 || x.CostoUnitario <= 0));
            diagnostico.SalidasConCostoDuplicado = rows.Count(x => x.FilasCosteo > 1);
            diagnostico.SalidasConProduccionCosteoDuplicado = rows.Count(x => x.FilasProduccionCosteo > 1);
            diagnostico.CostoSalidaCalculado = decimal.Round(rows.Where(x => !x.EsSinCosto).Sum(x => x.PesoNeto * x.CostoUnitario), 6);
            diagnostico.CostoSalidaGuardado = decimal.Round(rows.Where(x => !x.EsSinCosto).Sum(x => x.CostoLote), 6);
            diagnostico.DiferenciaCosto = decimal.Round(diagnostico.CostoSalidaGuardado - diagnostico.CostoSalidaCalculado, 6);

            var costoEntrada = await CalcularCostoEntradaParaValidacionAsync(
                cn,
                loteId,
                diagnostico.TipoProceso);

            diagnostico.CostoEntradaCalculado = decimal.Round(costoEntrada.CostoPrincipal, 6);
            diagnostico.CostoEntradaAlterno = decimal.Round(costoEntrada.CostoAlterno, 6);

            if ((diagnostico.TipoProceso == "CAJAS" || diagnostico.TipoProceso == "RETRABAJO") &&
                diagnostico.CostoEntradaCalculado <= 0 &&
                diagnostico.CostoEntradaAlterno <= 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "SIN_COSTO_ENTRADA",
                    Nivel = "BLOQUEO",
                    Titulo = "No se encontró costo positivo en las entradas",
                    Detalle = "La validación posterior al costeo no encontró costo de entrada positivo según las fuentes usadas por los diagnósticos de cajas/retrabajo."
                });
            }
            else if (diagnostico.TipoProceso == "CAJAS" || diagnostico.TipoProceso == "RETRABAJO")
            {
                var salida = diagnostico.CostoSalidaCalculado;
                var principalCuadra = diagnostico.CostoEntradaCalculado > 0 &&
                                      Math.Abs(diagnostico.CostoEntradaCalculado - salida) <= 0.05m;
                var alternoCuadra = diagnostico.CostoEntradaAlterno > 0 &&
                                    Math.Abs(diagnostico.CostoEntradaAlterno - salida) <= 0.05m;

                if (!principalCuadra && !alternoCuadra)
                {
                    diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                    {
                        Codigo = "COSTO_ENTRADA_VS_SALIDA_NO_CUADRA",
                        Nivel = "BLOQUEO",
                        Titulo = "Costo de entrada no coincide con costo distribuido a salidas",
                        Detalle = $"Entrada principal={diagnostico.CostoEntradaCalculado:N2}, entrada alterna={diagnostico.CostoEntradaAlterno:N2}, salida distribuida={salida:N2}. Ninguna comparación cuadra dentro de $0.05."
                    });
                }
            }

            if (diagnostico.SalidasSinCosteo > 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "SALIDAS_SIN_COSTEO",
                    Nivel = "BLOQUEO",
                    Titulo = "Salidas sin costo válido",
                    Detalle = $"Se detectaron {diagnostico.SalidasSinCosteo:N0} salida(s) normales sin Costeo TipoCosteoId=1 o con CostoUnitario <= 0."
                });
            }

            if (diagnostico.SalidasConCostoDuplicado > 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "COSTEO_DUPLICADO",
                    Nivel = "BLOQUEO",
                    Titulo = "Costeo duplicado por salida",
                    Detalle = $"Se detectaron {diagnostico.SalidasConCostoDuplicado:N0} salida(s) con más de una fila de Costeo para TipoCosteoId=1."
                });
            }

            if (diagnostico.SalidasConProduccionCosteoDuplicado > 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "PRODUCCION_COSTEO_DUPLICADO",
                    Nivel = "BLOQUEO",
                    Titulo = "ProduccionCosteo duplicado",
                    Detalle = $"Se detectaron {diagnostico.SalidasConProduccionCosteoDuplicado:N0} salida(s) con más de una fila de ProduccionCosteo."
                });
            }

            var desfases = rows
                .Where(x => !x.EsSinCosto && x.FilasCosteo > 0)
                .Where(x => Math.Abs(x.CostoLote - (x.PesoNeto * x.CostoUnitario)) > 0.05m)
                .ToList();

            if (desfases.Count > 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "COSTO_LOTE_NO_CUADRA",
                    Nivel = "BLOQUEO",
                    Titulo = "CostoLote no coincide con Peso × CostoUnitario",
                    Detalle = $"Se detectaron {desfases.Count:N0} salida(s) con diferencia superior a $0.05 entre CostoLote y PesoNeto × CostoUnitario."
                });
            }

            if (Math.Abs(diagnostico.DiferenciaCosto) > 0.05m)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "COSTO_TOTAL_NO_CUADRA",
                    Nivel = "BLOQUEO",
                    Titulo = "El costo distribuido del lote no cuadra",
                    Detalle = $"Costo guardado={diagnostico.CostoSalidaGuardado:N2}, costo calculado por kg={diagnostico.CostoSalidaCalculado:N2}, diferencia={diagnostico.DiferenciaCosto:N2}."
                });
            }
        }

        private async Task AgregarAnomaliasCosteoCanalesAsync(
            SqlConnection cn,
            int loteId,
            CierreLoteDiagnosticoVM diagnostico)
        {
            var tipoPesoId = diagnostico.TipoPesoIdCanal ?? -1;

            const string sql = @"
SELECT
    p.ProduccionId,
    CONVERT(nvarchar(100), ISNULL(p.Articulo,'')) AS Articulo,
    CONVERT(nvarchar(200), ISNULL(p.CodigoEtiqueta,'')) AS CodigoEtiqueta,
    CONVERT(decimal(18,6), ISNULL(pp.Peso,0)) AS PesoCanal,
    ISNULL(pc.Filas,0) AS FilasProduccionCosto,
    CONVERT(decimal(38,12), ISNULL(pc.Costo,0)) AS Costo,
    CONVERT(decimal(18,6), ISNULL(pc.PesoCosto,0)) AS PesoCosto,
    pc.FechaHora
FROM dbo.Produccion p
LEFT JOIN dbo.PesoProducto pp
    ON pp.ProduccionId = p.ProduccionId
   AND pp.TipoPesoId = @TipoPesoId
OUTER APPLY
(
    SELECT
        Filas = COUNT(*),
        Costo = SUM(CONVERT(decimal(38,12), ISNULL(x.Costo,0))),
        PesoCosto = MAX(CONVERT(decimal(18,6), ISNULL(x.Peso,0))),
        FechaHora = MAX(x.FechaCosto)
    FROM dbo.ProduccionCosto x
    WHERE x.ProduccionId = p.ProduccionId
      AND x.TipoCostoId = 0
) pc
WHERE p.LoteId = @LoteId
  AND p.TipoEtiquetaId = 1
  AND ISNULL(p.UltimoProcesoId,0) <> 29
ORDER BY p.ProduccionId;";

            var rows = (await cn.QueryAsync<CanalCostoRow>(
                sql,
                new { LoteId = loteId, TipoPesoId = tipoPesoId },
                commandTimeout: 120)).ToList();

            diagnostico.SalidasSinCosteo = rows.Count(x => x.FilasProduccionCosto == 0 || x.Costo <= 0);
            diagnostico.SalidasConCostoDuplicado = rows.Count(x => x.FilasProduccionCosto > 1);
            diagnostico.SalidasConProduccionCosteoDuplicado = 0;
            diagnostico.CostoSalidaGuardado = decimal.Round(rows.Sum(x => x.Costo), 6);
            diagnostico.CostoSalidaCalculado = diagnostico.CostoSalidaGuardado;
            diagnostico.DiferenciaCosto = 0;

            if (rows.Count == 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "CANALES_SIN_PRODUCCION",
                    Nivel = "BLOQUEO",
                    Titulo = "No existen canales activos para validar el costeo",
                    Detalle = "No se encontró Produccion TipoEtiquetaId=1 activa para el lote."
                });
                return;
            }

            if (diagnostico.SalidasSinCosteo > 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "CANALES_SIN_COSTEO",
                    Nivel = "BLOQUEO",
                    Titulo = "Canales sin ProduccionCosto válido",
                    Detalle = $"Se detectaron {diagnostico.SalidasSinCosteo:N0} canal(es) sin ProduccionCosto TipoCostoId=0 o con costo <= 0."
                });
            }

            if (diagnostico.SalidasConCostoDuplicado > 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "COSTEO_CANALES_DUPLICADO",
                    Nivel = "BLOQUEO",
                    Titulo = "ProduccionCosto duplicado en canales",
                    Detalle = $"Se detectaron {diagnostico.SalidasConCostoDuplicado:N0} canal(es) con más de una fila TipoCostoId=0."
                });
            }

            var pesoDesfasado = rows.Count(x =>
                x.FilasProduccionCosto > 0 &&
                x.PesoCanal > 0 &&
                Math.Abs(x.PesoCanal - x.PesoCosto) > 0.01m);

            if (pesoDesfasado > 0)
            {
                diagnostico.Anomalias.Add(new CierreLoteAnomaliaVM
                {
                    Codigo = "PESO_COSTEO_CANAL_DESACTUALIZADO",
                    Nivel = "BLOQUEO",
                    Titulo = "El peso usado para costear no coincide con el peso actual",
                    Detalle = $"Se detectaron {pesoDesfasado:N0} canal(es) donde ProduccionCosto.Peso difiere del PesoProducto seleccionado por más de 0.01 kg."
                });
            }
        }

        private static async Task<CostoEntradaValidacionRow> CalcularCostoEntradaParaValidacionAsync(
            SqlConnection cn,
            int loteId,
            string tipoProceso)
        {
            tipoProceso = (tipoProceso ?? "").Trim().ToUpperInvariant();

            if (tipoProceso == "CAJAS")
            {
                const string sql = @"
;WITH EntradasUnicas AS
(
    SELECT DISTINCT pl.ProduccionId
    FROM dbo.ProduccionLogistica pl
    WHERE pl.SolicitudProduccionId = @LoteId
), CostoEntrada AS
(
    SELECT
        eu.ProduccionId,
        ISNULL((
            SELECT SUM(ISNULL(pc.Costo,0))
            FROM dbo.ProduccionCosto pc
            WHERE pc.ProduccionId = eu.ProduccionId
              AND pc.TipoCostoId = 0
        ),0) AS CostoTipo0,
        ISNULL((
            SELECT TOP (1) ISNULL(pc2.CostoCanal,0)
            FROM dbo.ProduccionCosteo pc2
            WHERE pc2.ProduccionId = eu.ProduccionId
            ORDER BY ISNULL(pc2.FechaHora,CONVERT(datetime,'19000101',112)) DESC
        ),0) AS UltimoCostoCanal
    FROM EntradasUnicas eu
)
SELECT
    CONVERT(decimal(38,12), ISNULL(SUM(CostoTipo0 + UltimoCostoCanal),0)) AS CostoPrincipal,
    CONVERT(decimal(38,12), 0) AS CostoAlterno
FROM CostoEntrada;";

                return await cn.QueryFirstAsync<CostoEntradaValidacionRow>(sql, new { LoteId = loteId }, commandTimeout: 120);
            }

            if (tipoProceso == "RETRABAJO")
            {
                const string sql = @"
;WITH EntradasUnicas AS
(
    SELECT DISTINCT pl.ProduccionId
    FROM dbo.ProduccionLogistica pl
    WHERE pl.SolicitudProduccionId = @LoteId
), Base AS
(
    SELECT
        p.ProduccionId,
        p.CodigoEtiqueta,
        p.PesoNeto,
        ISNULL(pc.CostoCanal,0) AS CostoCanal,
        ISNULL(NULLIF(pc.FactorUnidad,0),ISNULL(p.PesoNeto,0)) AS FactorUnidad,
        ISNULL(c.CostoUnitario,0) AS CostoUnitario,
        ISNULL((
            SELECT SUM(ISNULL(x.Costo,0))
            FROM dbo.ProduccionCosto x
            WHERE x.ProduccionId = p.ProduccionId
        ),0) AS CostoProduccionCosto
    FROM EntradasUnicas eu
    INNER JOIN dbo.Produccion p ON p.ProduccionId=eu.ProduccionId
    OUTER APPLY
    (
        SELECT TOP (1) pc1.CostoCanal,pc1.FactorUnidad
        FROM dbo.ProduccionCosteo pc1
        WHERE pc1.ProduccionId=p.ProduccionId
        ORDER BY ISNULL(pc1.FechaHora,CONVERT(datetime,'19000101',112)) DESC
    ) pc
    OUTER APPLY
    (
        SELECT TOP (1) c1.CostoUnitario
        FROM dbo.Costeo c1
        WHERE c1.ProduccionId=p.ProduccionId
          AND c1.TipoCosteoId=1
        ORDER BY ISNULL(c1.FechaHora,CONVERT(datetime,'19000101',112)) DESC
    ) c
)
SELECT
    CONVERT(decimal(38,12), ISNULL(SUM(
        CASE
            WHEN ISNULL(CodigoEtiqueta,'') LIKE 'COMP%'
              OR ISNULL(CodigoEtiqueta,'') LIKE 'COMT%'
            THEN CostoCanal
            ELSE CostoUnitario * FactorUnidad
        END
    ),0)) AS CostoPrincipal,
    CONVERT(decimal(38,12), ISNULL(SUM(CostoProduccionCosto),0)) AS CostoAlterno
FROM Base;";

                return await cn.QueryFirstAsync<CostoEntradaValidacionRow>(sql, new { LoteId = loteId }, commandTimeout: 120);
            }

            return new CostoEntradaValidacionRow();
        }

        public async Task<long> CrearSolicitudAsync(
            string source,
            int loteId,
            string usuario,
            string motivo,
            string ip,
            string userAgent,
            CierreLoteDiagnosticoVM diagnostico)
        {
            source = NormalizeSource(source);
            usuario = (usuario ?? "").Trim();
            motivo = (motivo ?? "").Trim();

            if (motivo.Length < 10)
                throw new ArgumentException("Capture un motivo de al menos 10 caracteres.");
            if (diagnostico == null || diagnostico.LoteId != loteId)
                throw new ArgumentException("Diagnóstico inválido.");
            if (diagnostico.TieneBloqueos)
                throw new InvalidOperationException("El lote tiene bloqueos técnicos que no pueden autorizarse.");
            if (!diagnostico.RequiereAutorizacion)
                throw new InvalidOperationException("El lote no requiere autorización; puede continuar con costeo y cierre.");

            var cs = GetConnectionString(source);
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var existente = await cn.QueryFirstOrDefaultAsync<long?>(@"
SELECT TOP (1) SolicitudId
FROM dbo.meat_CierreLoteSolicitud WITH (UPDLOCK, HOLDLOCK)
WHERE LoteId = @LoteId
  AND Source = @Source
  AND DiagnosticoHash = @DiagnosticoHash
  AND Estado IN ('PENDIENTE','APROBADA')
ORDER BY SolicitudId DESC;",
                    new
                    {
                        LoteId = loteId,
                        Source = source,
                        diagnostico.DiagnosticoHash
                    },
                    transaction: tx,
                    commandTimeout: 60);

                if (existente.HasValue)
                {
                    await tx.CommitAsync();
                    return existente.Value;
                }

                var anomaliasJson = JsonSerializer.Serialize(diagnostico.Anomalias);
                var solicitudId = await cn.ExecuteScalarAsync<long>(@"
INSERT INTO dbo.meat_CierreLoteSolicitud
(
    Source, LoteId, LoteNombre, TipoLoteId, Estado,
    UsuarioSolicita, FechaSolicitud, MotivoSolicitud,
    DiagnosticoHash, MovimientoHash,
    AprobacionesRequeridas,
    Entradas, KgEntrada, Salidas, KgSalida, DiferenciaKg, VariacionPct, RendimientoPct,
    AnomaliasJson, IpSolicita, UserAgentSolicita
)
OUTPUT INSERTED.SolicitudId
VALUES
(
    @Source, @LoteId, @LoteNombre, @TipoLoteId, 'PENDIENTE',
    @UsuarioSolicita, SYSDATETIME(), @MotivoSolicitud,
    @DiagnosticoHash, @MovimientoHash,
    @AprobacionesRequeridas,
    @Entradas, @KgEntrada, @Salidas, @KgSalida, @DiferenciaKg, @VariacionPct, @RendimientoPct,
    @AnomaliasJson, @IpSolicita, @UserAgentSolicita
);",
                    new
                    {
                        Source = source,
                        LoteId = loteId,
                        LoteNombre = diagnostico.LoteNombre,
                        TipoLoteId = diagnostico.TipoLoteId,
                        UsuarioSolicita = usuario,
                        MotivoSolicitud = motivo,
                        diagnostico.DiagnosticoHash,
                        diagnostico.MovimientoHash,
                        AprobacionesRequeridas = Math.Max(1, diagnostico.AprobacionesRequeridas),
                        diagnostico.Entradas,
                        diagnostico.KgEntrada,
                        diagnostico.Salidas,
                        diagnostico.KgSalida,
                        diagnostico.DiferenciaKg,
                        diagnostico.VariacionPct,
                        diagnostico.RendimientoPct,
                        AnomaliasJson = anomaliasJson,
                        IpSolicita = ip ?? "",
                        UserAgentSolicita = (userAgent ?? "").Length > 500 ? (userAgent ?? "").Substring(0, 500) : (userAgent ?? "")
                    },
                    transaction: tx,
                    commandTimeout: 60);

                foreach (var a in diagnostico.Anomalias)
                {
                    await cn.ExecuteAsync(@"
INSERT INTO dbo.meat_CierreLoteAnomalia
(
    SolicitudId, Codigo, Nivel, Titulo, Detalle,
    ArticuloEntrada, ArticuloSalida, Valor, Limite, FechaHora
)
VALUES
(
    @SolicitudId, @Codigo, @Nivel, @Titulo, @Detalle,
    @ArticuloEntrada, @ArticuloSalida, @Valor, @Limite, SYSDATETIME()
);",
                        new
                        {
                            SolicitudId = solicitudId,
                            a.Codigo,
                            a.Nivel,
                            a.Titulo,
                            a.Detalle,
                            a.ArticuloEntrada,
                            a.ArticuloSalida,
                            a.Valor,
                            a.Limite
                        }, transaction: tx, commandTimeout: 60);
                }

                await InsertarBitacoraAsync(cn, tx, source, loteId, solicitudId, "SOLICITAR_AUTORIZACION", usuario,
                    $"Motivo: {motivo}. Hash: {diagnostico.DiagnosticoHash}", true);

                await tx.CommitAsync();
                return solicitudId;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<CierreLoteSolicitudVM>> ObtenerSolicitudesPendientesAsync(string source, int top = 200)
        {
            source = NormalizeSource(source);
            top = Math.Clamp(top, 1, 1000);
            var cs = GetConnectionString(source);

            const string sql = @"
SELECT TOP (@Top)
    s.SolicitudId,
    s.Source,
    s.LoteId,
    s.LoteNombre,
    s.TipoLoteId,
    s.Estado,
    s.UsuarioSolicita,
    s.FechaSolicitud,
    s.MotivoSolicitud,
    s.DiagnosticoHash,
    s.AprobacionesRequeridas,
    ISNULL(ap.AprobacionesActuales, 0) AS AprobacionesActuales,
    ISNULL(ap.Rechazos, 0) AS Rechazos,
    ISNULL(an.ResumenAnomalias, '') AS ResumenAnomalias,
    ISNULL(ap.Autorizadores, '') AS Autorizadores
FROM dbo.meat_CierreLoteSolicitud s
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN a.Decision = 'APROBAR' THEN 1 ELSE 0 END) AS AprobacionesActuales,
        SUM(CASE WHEN a.Decision = 'RECHAZAR' THEN 1 ELSE 0 END) AS Rechazos,
        STRING_AGG(CASE WHEN a.Decision='APROBAR' THEN a.UsuarioAutoriza + ': ' + a.Motivo ELSE NULL END, ' | ') AS Autorizadores
    FROM dbo.meat_CierreLoteAutorizacion a
    WHERE a.SolicitudId = s.SolicitudId
) ap
OUTER APPLY
(
    SELECT STRING_AGG(x.Codigo + ' - ' + x.Titulo, ' | ') AS ResumenAnomalias
    FROM dbo.meat_CierreLoteAnomalia x
    WHERE x.SolicitudId = s.SolicitudId
      AND x.Nivel IN ('AUTORIZACION','BLOQUEO')
) an
WHERE s.Estado IN ('PENDIENTE','APROBADA')
ORDER BY s.FechaSolicitud DESC, s.SolicitudId DESC;";

            await using var cn = new SqlConnection(cs);
            return (await cn.QueryAsync<CierreLoteSolicitudVM>(sql, new { Top = top }, commandTimeout: 120)).ToList();
        }

        public async Task<CierreLoteAutorizacionEstadoVM> ObtenerAutorizacionEstadoAsync(
            string source,
            int loteId,
            string diagnosticoHash)
        {
            source = NormalizeSource(source);
            var cs = GetConnectionString(source);

            const string sql = @"
;WITH S AS
(
    SELECT TOP (1) *
    FROM dbo.meat_CierreLoteSolicitud
    WHERE Source = @Source
      AND LoteId = @LoteId
      AND DiagnosticoHash = @DiagnosticoHash
      AND Estado IN ('PENDIENTE','APROBADA','RECHAZADA')
    ORDER BY SolicitudId DESC
)
SELECT
    CONVERT(bit, CASE WHEN EXISTS(SELECT 1 FROM S) THEN 1 ELSE 0 END) AS ExisteSolicitud,
    CONVERT(bit, CASE WHEN EXISTS(SELECT 1 FROM S WHERE Estado='APROBADA') THEN 1 ELSE 0 END) AS Aprobada,
    CONVERT(bit, CASE WHEN EXISTS(SELECT 1 FROM S WHERE Estado='RECHAZADA') THEN 1 ELSE 0 END) AS Rechazada,
    (SELECT TOP 1 SolicitudId FROM S) AS SolicitudId,
    ISNULL((SELECT TOP 1 AprobacionesRequeridas FROM S), 0) AS AprobacionesRequeridas,
    ISNULL((SELECT SUM(CASE WHEN a.Decision='APROBAR' THEN 1 ELSE 0 END) FROM dbo.meat_CierreLoteAutorizacion a INNER JOIN S ON S.SolicitudId=a.SolicitudId), 0) AS AprobacionesActuales,
    ISNULL((SELECT SUM(CASE WHEN a.Decision='RECHAZAR' THEN 1 ELSE 0 END) FROM dbo.meat_CierreLoteAutorizacion a INNER JOIN S ON S.SolicitudId=a.SolicitudId), 0) AS Rechazos,
    ISNULL((SELECT TOP 1 Estado FROM S), '') AS Estado,
    ISNULL((SELECT STRING_AGG(a.UsuarioAutoriza + ': ' + a.Motivo, ' | ') FROM dbo.meat_CierreLoteAutorizacion a INNER JOIN S ON S.SolicitudId=a.SolicitudId WHERE a.Decision='APROBAR'), '') AS Autorizadores;";

            await using var cn = new SqlConnection(cs);
            return await cn.QueryFirstAsync<CierreLoteAutorizacionEstadoVM>(sql,
                new { Source = source, LoteId = loteId, DiagnosticoHash = diagnosticoHash },
                commandTimeout: 60);
        }

        public async Task<CierreLoteAutorizacionEstadoVM> RegistrarDecisionAsync(
            string source,
            long solicitudId,
            string usuario,
            string decision,
            string motivo,
            string ip,
            string userAgent)
        {
            source = NormalizeSource(source);
            usuario = (usuario ?? "").Trim();
            decision = (decision ?? "").Trim().ToUpperInvariant();
            motivo = (motivo ?? "").Trim();

            if (solicitudId <= 0)
                throw new ArgumentException("SolicitudId inválido.");
            if (decision != "APROBAR" && decision != "RECHAZAR")
                throw new ArgumentException("Decisión inválida.");
            if (motivo.Length < 10)
                throw new ArgumentException("El gerente/director debe registrar una justificación de al menos 10 caracteres.");

            var cs = GetConnectionString(source);
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var s = await cn.QueryFirstOrDefaultAsync<SolicitudDbRow>(@"
SELECT TOP (1)
    SolicitudId, Source, LoteId, LoteNombre, Estado,
    UsuarioSolicita, DiagnosticoHash, AprobacionesRequeridas
FROM dbo.meat_CierreLoteSolicitud WITH (UPDLOCK, HOLDLOCK)
WHERE SolicitudId = @SolicitudId;",
                    new { SolicitudId = solicitudId },
                    transaction: tx,
                    commandTimeout: 60);

                if (s == null)
                    throw new InvalidOperationException("No existe la solicitud indicada.");
                if (!string.Equals(s.Source, source, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("La solicitud pertenece a otra planta.");
                if (s.Estado != "PENDIENTE" && s.Estado != "APROBADA")
                    throw new InvalidOperationException($"La solicitud está en estado {s.Estado} y ya no admite decisiones.");
                if (string.Equals(s.UsuarioSolicita, usuario, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("El usuario que solicita la excepción no puede autorizarse a sí mismo.");

                var yaDecidio = await cn.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.meat_CierreLoteAutorizacion WITH (UPDLOCK, HOLDLOCK)
WHERE SolicitudId = @SolicitudId
  AND UPPER(LTRIM(RTRIM(UsuarioAutoriza))) = UPPER(LTRIM(RTRIM(@Usuario)));",
                    new { SolicitudId = solicitudId, Usuario = usuario },
                    transaction: tx,
                    commandTimeout: 60);

                if (yaDecidio > 0)
                    throw new InvalidOperationException("Este usuario ya registró una decisión para la solicitud.");

                await cn.ExecuteAsync(@"
INSERT INTO dbo.meat_CierreLoteAutorizacion
(
    SolicitudId, Decision, UsuarioAutoriza, Motivo,
    FechaHora, IpAutoriza, UserAgentAutoriza
)
VALUES
(
    @SolicitudId, @Decision, @UsuarioAutoriza, @Motivo,
    SYSDATETIME(), @IpAutoriza, @UserAgentAutoriza
);",
                    new
                    {
                        SolicitudId = solicitudId,
                        Decision = decision,
                        UsuarioAutoriza = usuario,
                        Motivo = motivo,
                        IpAutoriza = ip ?? "",
                        UserAgentAutoriza = (userAgent ?? "").Length > 500 ? (userAgent ?? "").Substring(0, 500) : (userAgent ?? "")
                    },
                    transaction: tx,
                    commandTimeout: 60);

                var conteo = await cn.QueryFirstAsync<ConteoDecisionRow>(@"
SELECT
    SUM(CASE WHEN Decision='APROBAR' THEN 1 ELSE 0 END) AS Aprobaciones,
    SUM(CASE WHEN Decision='RECHAZAR' THEN 1 ELSE 0 END) AS Rechazos
FROM dbo.meat_CierreLoteAutorizacion
WHERE SolicitudId = @SolicitudId;",
                    new { SolicitudId = solicitudId },
                    transaction: tx,
                    commandTimeout: 60);

                var nuevoEstado = conteo.Rechazos > 0
                    ? "RECHAZADA"
                    : conteo.Aprobaciones >= Math.Max(1, s.AprobacionesRequeridas)
                        ? "APROBADA"
                        : "PENDIENTE";

                await cn.ExecuteAsync(@"
UPDATE dbo.meat_CierreLoteSolicitud
SET Estado = @Estado,
    FechaUltimaDecision = SYSDATETIME()
WHERE SolicitudId = @SolicitudId;",
                    new { Estado = nuevoEstado, SolicitudId = solicitudId },
                    transaction: tx,
                    commandTimeout: 60);

                await InsertarBitacoraAsync(cn, tx, source, s.LoteId, solicitudId,
                    decision == "APROBAR" ? "AUTORIZAR" : "RECHAZAR",
                    usuario,
                    motivo,
                    true);

                await tx.CommitAsync();

                return await ObtenerAutorizacionEstadoAsync(source, s.LoteId, s.DiagnosticoHash);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task CerrarLoteAsync(
            string source,
            int loteId,
            string usuario,
            string movimientoHash,
            string diagnosticoHash,
            long? solicitudId,
            string detalleCosteoJson)
        {
            source = NormalizeSource(source);
            var cs = GetConnectionString(source);
            var dbCommercia = GetCommerciaDb(source);

            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var lockResult = await cn.ExecuteScalarAsync<int>(@"
DECLARE @r int;
EXEC @r = sys.sp_getapplock
    @Resource = @Resource,
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 10000;
SELECT @r;",
                    new { Resource = $"CIERRE_LOTE_{source}_{loteId}" },
                    transaction: tx,
                    commandTimeout: 20);

                if (lockResult < 0)
                    throw new InvalidOperationException("No se pudo obtener el bloqueo exclusivo del lote para cerrar.");

                var estatus = await cn.QueryFirstOrDefaultAsync<int?>(@"
SELECT CONVERT(int, ISNULL(EstatusId, 0))
FROM dbo.Lote WITH (UPDLOCK, HOLDLOCK)
WHERE LoteId = @LoteId;",
                    new { LoteId = loteId },
                    transaction: tx,
                    commandTimeout: 60);

                if (!estatus.HasValue)
                    throw new InvalidOperationException("El lote dejó de existir.");
                if (estatus.Value == 3)
                {
                    await tx.CommitAsync();
                    return;
                }

                var tipoProcesoActual = await cn.ExecuteScalarAsync<string>(@"
SELECT TOP (1) CONVERT(varchar(20), ISNULL(cfg.TipoProceso,''))
FROM dbo.Lote l
LEFT JOIN dbo.meat_CierreLoteTipoConfig cfg
    ON cfg.TipoLoteId = l.TipoLoteId
   AND cfg.Activo = 1
WHERE l.LoteId = @LoteId;",
                    new { LoteId = loteId },
                    transaction: tx,
                    commandTimeout: 60) ?? "";

                List<CierreLoteMovimientoVM> movimientosActuales;
                if (string.Equals(tipoProcesoActual, "CANALES", StringComparison.OrdinalIgnoreCase))
                {
                    var canalBaseActual = await ObtenerCanalLoteBaseAsync(cn, loteId, tx);
                    movimientosActuales = await ObtenerMovimientosCanalesAsync(
                        cn, tx, loteId, dbCommercia, canalBaseActual?.TipoPesoId);
                }
                else
                {
                    movimientosActuales = await ObtenerMovimientosAsync(cn, tx, loteId, dbCommercia);
                }

                var entradasActuales = movimientosActuales.Where(x => x.Tipo == "ENTRADA").ToList();
                var salidasActuales = movimientosActuales.Where(x => x.Tipo == "SALIDA").ToList();
                var hashActual = CalcularMovimientoHash(entradasActuales, salidasActuales);

                if (!string.Equals(hashActual, movimientoHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "El lote cambió mientras se procesaba el cierre. Se detectaron movimientos nuevos o modificaciones de kg/artículo. Revalide y vuelva a intentar.");
                }

                var filas = await cn.ExecuteAsync(@"
UPDATE dbo.Lote
SET EstatusId = 3
WHERE LoteId = @LoteId
  AND ISNULL(EstatusId, 0) <> 3;",
                    new { LoteId = loteId },
                    transaction: tx,
                    commandTimeout: 60);

                if (filas != 1)
                    throw new InvalidOperationException("No fue posible cambiar EstatusId a 3.");

                if (solicitudId.HasValue)
                {
                    await cn.ExecuteAsync(@"
UPDATE dbo.meat_CierreLoteSolicitud
SET Estado = 'CERRADA',
    UsuarioCierra = @Usuario,
    FechaCierre = SYSDATETIME()
WHERE SolicitudId = @SolicitudId;",
                        new { Usuario = usuario, SolicitudId = solicitudId.Value },
                        transaction: tx,
                        commandTimeout: 60);
                }

                await InsertarBitacoraAsync(
                    cn,
                    tx,
                    source,
                    loteId,
                    solicitudId,
                    "CERRAR_LOTE",
                    usuario,
                    $"EstatusId=3. DiagnosticoHash={diagnosticoHash}. Costeo={detalleCosteoJson}",
                    true);

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task MarcarSolicitudCerradaAsync(string source, long? solicitudId, string usuario)
        {
            if (!solicitudId.HasValue) return;
            source = NormalizeSource(source);
            var cs = GetConnectionString(source);
            await using var cn = new SqlConnection(cs);
            await cn.ExecuteAsync(@"
UPDATE dbo.meat_CierreLoteSolicitud
SET Estado='CERRADA', UsuarioCierra=@Usuario, FechaCierre=SYSDATETIME()
WHERE SolicitudId=@SolicitudId;",
                new { Usuario = usuario, SolicitudId = solicitudId.Value },
                commandTimeout: 60);
        }

        public async Task RegistrarBitacoraAsync(
            string source,
            int loteId,
            long? solicitudId,
            string accion,
            string usuario,
            string detalle,
            bool ok)
        {
            source = NormalizeSource(source);
            var cs = GetConnectionString(source);
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await InsertarBitacoraAsync(cn, null, source, loteId, solicitudId, accion, usuario, detalle, ok);
        }

        private static async Task InsertarBitacoraAsync(
            SqlConnection cn,
            SqlTransaction? tx,
            string source,
            int loteId,
            long? solicitudId,
            string accion,
            string usuario,
            string detalle,
            bool ok)
        {
            if ((detalle ?? "").Length > 8000)
                detalle = (detalle ?? "").Substring(0, 8000);

            await cn.ExecuteAsync(@"
INSERT INTO dbo.meat_CierreLoteBitacora
(
    FechaHora, Source, LoteId, SolicitudId,
    Accion, Usuario, Detalle, Ok
)
VALUES
(
    SYSDATETIME(), @Source, @LoteId, @SolicitudId,
    @Accion, @Usuario, @Detalle, @Ok
);",
                new
                {
                    Source = source,
                    LoteId = loteId,
                    SolicitudId = solicitudId,
                    Accion = accion ?? "",
                    Usuario = usuario ?? "",
                    Detalle = detalle ?? "",
                    Ok = ok
                },
                transaction: tx,
                commandTimeout: 60);
        }

        public async Task<List<CierreLoteCompatibilidadVM>> ListarCompatibilidadAsync(string source, string texto = "")
        {
            source = NormalizeSource(source);
            texto = (texto ?? "").Trim();
            var cs = GetConnectionString(source);

            const string sql = @"
SELECT TOP (1000)
    CompatibilidadId,
    Source,
    ArticuloEntrada,
    ArticuloSalida,
    Permitido,
    Motivo,
    Activo,
    Usuario,
    FechaHora
FROM dbo.meat_CierreLoteCompatibilidadProducto
WHERE Activo = 1
  AND (@Texto = ''
       OR ArticuloEntrada LIKE '%' + @Texto + '%'
       OR ArticuloSalida LIKE '%' + @Texto + '%'
       OR Motivo LIKE '%' + @Texto + '%')
ORDER BY ArticuloSalida, ArticuloEntrada, CompatibilidadId DESC;";

            await using var cn = new SqlConnection(cs);
            return (await cn.QueryAsync<CierreLoteCompatibilidadVM>(sql, new { Texto = texto }, commandTimeout: 60)).ToList();
        }

        public async Task GuardarCompatibilidadAsync(CierreLoteCompatibilidadRequestVM req, string usuario)
        {
            req.Source = NormalizeSource(req.Source);
            req.ArticuloEntrada = (req.ArticuloEntrada ?? "").Trim().ToUpperInvariant();
            req.ArticuloSalida = (req.ArticuloSalida ?? "").Trim().ToUpperInvariant();
            req.Motivo = (req.Motivo ?? "").Trim();

            if (req.ArticuloEntrada.Length == 0 || req.ArticuloSalida.Length == 0)
                throw new ArgumentException("Artículo entrada y artículo salida son obligatorios.");
            if (req.Motivo.Length < 5)
                throw new ArgumentException("Capture un motivo breve para la regla de compatibilidad.");

            var cs = GetConnectionString(req.Source);
            await using var cn = new SqlConnection(cs);
            await cn.ExecuteAsync(@"
IF EXISTS
(
    SELECT 1
    FROM dbo.meat_CierreLoteCompatibilidadProducto
    WHERE Source=@Source
      AND ArticuloEntrada=@ArticuloEntrada
      AND ArticuloSalida=@ArticuloSalida
)
BEGIN
    UPDATE dbo.meat_CierreLoteCompatibilidadProducto
    SET Permitido=@Permitido,
        Motivo=@Motivo,
        Activo=1,
        Usuario=@Usuario,
        FechaHora=SYSDATETIME()
    WHERE Source=@Source
      AND ArticuloEntrada=@ArticuloEntrada
      AND ArticuloSalida=@ArticuloSalida;
END
ELSE
BEGIN
    INSERT INTO dbo.meat_CierreLoteCompatibilidadProducto
    (Source, ArticuloEntrada, ArticuloSalida, Permitido, Motivo, Activo, Usuario, FechaHora)
    VALUES
    (@Source, @ArticuloEntrada, @ArticuloSalida, @Permitido, @Motivo, 1, @Usuario, SYSDATETIME());
END;",
                new
                {
                    req.Source,
                    req.ArticuloEntrada,
                    req.ArticuloSalida,
                    req.Permitido,
                    req.Motivo,
                    Usuario = usuario ?? ""
                },
                commandTimeout: 60);
        }

        public async Task EliminarCompatibilidadAsync(string source, long compatibilidadId, string usuario)
        {
            source = NormalizeSource(source);
            var cs = GetConnectionString(source);
            await using var cn = new SqlConnection(cs);
            var n = await cn.ExecuteAsync(@"
UPDATE dbo.meat_CierreLoteCompatibilidadProducto
SET Activo=0,
    Usuario=@Usuario,
    FechaHora=SYSDATETIME()
WHERE CompatibilidadId=@CompatibilidadId;",
                new { CompatibilidadId = compatibilidadId, Usuario = usuario ?? "" },
                commandTimeout: 60);

            if (n == 0)
                throw new InvalidOperationException("No se encontró la regla de compatibilidad.");
        }

        private static string CalcularMovimientoHash(
            IEnumerable<CierreLoteMovimientoVM> entradas,
            IEnumerable<CierreLoteMovimientoVM> salidas)
        {
            static string Line(CierreLoteMovimientoVM x) =>
                string.Join("|",
                    x.Tipo,
                    x.ProduccionId.ToString(CultureInfo.InvariantCulture),
                    (x.LoteId ?? 0).ToString(CultureInfo.InvariantCulture),
                    (x.Articulo ?? "").Trim().ToUpperInvariant(),
                    (x.CodigoEtiqueta ?? "").Trim().ToUpperInvariant(),
                    x.PesoNeto.ToString("0.000", CultureInfo.InvariantCulture),
                    x.Estatus.ToString(CultureInfo.InvariantCulture),
                    (x.UltimoProcesoId ?? 0).ToString(CultureInfo.InvariantCulture));

            var raw = string.Join("\n",
                entradas.Concat(salidas)
                    .OrderBy(x => x.Tipo)
                    .ThenBy(x => x.ProduccionId)
                    .Select(Line));

            return Sha256(raw);
        }

        private static string CalcularDiagnosticoHash(
            CierreLoteDiagnosticoVM d,
            CierreLoteTipoConfigVM? config)
        {
            var auth = d.Anomalias
                .Where(x => x.Nivel == "AUTORIZACION")
                .OrderBy(x => x.Codigo)
                .ThenBy(x => x.ArticuloEntrada)
                .ThenBy(x => x.ArticuloSalida)
                .Select(x => string.Join("|",
                    x.Codigo,
                    x.ArticuloEntrada ?? "",
                    x.ArticuloSalida ?? "",
                    x.Valor?.ToString(CultureInfo.InvariantCulture) ?? "",
                    x.Limite?.ToString(CultureInfo.InvariantCulture) ?? ""));

            var raw = string.Join("#",
                d.Source,
                d.LoteId,
                d.TipoLoteId,
                d.MovimientoHash,
                d.KgEntrada.ToString("0.000", CultureInfo.InvariantCulture),
                d.KgSalida.ToString("0.000", CultureInfo.InvariantCulture),
                config?.VariacionAdvertenciaPct.ToString(CultureInfo.InvariantCulture) ?? "",
                config?.VariacionBloqueoPct.ToString(CultureInfo.InvariantCulture) ?? "",
                string.Join(";", auth));

            return Sha256(raw);
        }

        private static string Sha256(string value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
            return Convert.ToHexString(bytes);
        }

        private sealed class CanalLoteBaseRow
        {
            public int LoteId { get; set; }
            public int? TipoPesoId { get; set; }
            public string TipoPesoNombre { get; set; } = "";
            public string Documento { get; set; } = "";
            public string Cliente { get; set; } = "";
        }

        private sealed class CanalCompraResumenRow
        {
            public long Filas { get; set; }
            public int Articulos { get; set; }
            public decimal CostoTotal { get; set; }
        }

        private sealed class CanalCostoRow
        {
            public int ProduccionId { get; set; }
            public string Articulo { get; set; } = "";
            public string CodigoEtiqueta { get; set; } = "";
            public decimal PesoCanal { get; set; }
            public int FilasProduccionCosto { get; set; }
            public decimal Costo { get; set; }
            public decimal PesoCosto { get; set; }
            public DateTime? FechaHora { get; set; }
        }

        private sealed class LoteDbRow
        {
            public int LoteId { get; set; }
            public string Nombre { get; set; } = "";
            public int TipoLoteId { get; set; }
            public int EstatusId { get; set; }
            public DateTime? FechaProduccion { get; set; }
        }

        private sealed class DuplicadoLogisticaRow
        {
            public int ProduccionId { get; set; }
            public int Coincidencias { get; set; }
        }

        private sealed class CostoSalidaRow
        {
            public int ProduccionId { get; set; }
            public string Articulo { get; set; } = "";
            public string CodigoEtiqueta { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public bool EsSinCosto { get; set; }
            public int FilasCosteo { get; set; }
            public decimal CostoUnitario { get; set; }
            public decimal CostoLote { get; set; }
            public DateTime? FechaCosteo { get; set; }
            public int FilasProduccionCosteo { get; set; }
            public decimal CostoCanal { get; set; }
            public DateTime? FechaProduccionCosteo { get; set; }
        }

        private sealed class CostoEntradaValidacionRow
        {
            public decimal CostoPrincipal { get; set; }
            public decimal CostoAlterno { get; set; }
        }

        private sealed class SolicitudDbRow
        {
            public long SolicitudId { get; set; }
            public string Source { get; set; } = "";
            public int LoteId { get; set; }
            public string LoteNombre { get; set; } = "";
            public string Estado { get; set; } = "";
            public string UsuarioSolicita { get; set; } = "";
            public string DiagnosticoHash { get; set; } = "";
            public int AprobacionesRequeridas { get; set; }
        }

        private sealed class ConteoDecisionRow
        {
            public int Aprobaciones { get; set; }
            public int Rechazos { get; set; }
        }
    }
}
