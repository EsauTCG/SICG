using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Plataforma_CG.ViewModels.DashboardVentas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    /// <summary>
    /// Dashboard de ventas alineado con la consulta operativa de presupuestos.
    ///
    /// Reglas principales:
    /// - CEDIS: presupuesto desde PresupuestoCedis.
    /// - VENDEDOR: presupuesto desde PresupuestoVendedor.
    /// - CEDIS venta real: surtido validado menos devoluciones de bodega.
    /// - VENDEDOR venta real: surtido normal más distribución proporcional del CEDIS.
    /// - Transferencias CEDIS se consideran en la distribución del surtido a vendedores.
    /// - Precio: OrdenVentaProducto.Precio ponderado por OrdenVentaProducto.Peso.
    ///
    /// Se conserva el mismo contrato JSON que consume dashboard-ventas.js.
    /// </summary>
    [Authorize]
    [Route("Comercial")]
    public class DashboardVentasController : Controller
    {
        private readonly IConfiguration _configuration;

        public DashboardVentasController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ============================================================
        // VISTA
        // ============================================================
        [HttpGet("DashboardVentas")]
        public IActionResult DashboardVentas()
        {
            return View("~/Views/Comercial/DashboardVentas.cshtml");
        }

        // ============================================================
        // CATÁLOGOS
        // ============================================================
        [HttpGet("DashboardVentasCatalogos")]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Client, NoStore = false)]
        public async Task<IActionResult> Catalogos(CancellationToken ct = default)
        {
            const string sql = @"
/* 1) ÚLTIMA FECHA DE MOVIMIENTO QUE ALIMENTA VENTA REAL */
SELECT UltimaFechaVenta = MAX(Fecha)
FROM
(
    SELECT Fecha = MAX(CAST(se.FechaValidacion AS date))
    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
    WHERE se.FechaValidacion IS NOT NULL

    UNION ALL

    SELECT Fecha = MAX(CAST(t.FechaSolicitud AS date))
    FROM dbo.Transferencias t WITH (NOLOCK)
    INNER JOIN dbo.TransferenciaSurtido ts WITH (NOLOCK)
        ON ts.TransferenciaId = t.Id
    WHERE t.FechaSolicitud IS NOT NULL
      AND t.Estatus >= 5
      AND ISNULL(ts.KgSurtido, 0) > 0
) f;

/* 2) AÑOS CON SURTIDO REAL O PRESUPUESTO */
SELECT Anio
FROM
(
    SELECT DISTINCT YEAR(se.FechaValidacion) AS Anio
    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
    WHERE se.FechaValidacion IS NOT NULL

    UNION

    SELECT DISTINCT YEAR(t.FechaSolicitud) AS Anio
    FROM dbo.Transferencias t WITH (NOLOCK)
    INNER JOIN dbo.TransferenciaSurtido ts WITH (NOLOCK)
        ON ts.TransferenciaId = t.Id
    WHERE t.FechaSolicitud IS NOT NULL
      AND t.Estatus >= 5
      AND ISNULL(ts.KgSurtido, 0) > 0

    UNION

    SELECT DISTINCT pv.Anio
    FROM dbo.PresupuestoVendedor pv WITH (NOLOCK)
    WHERE pv.Anio IS NOT NULL

    UNION

    SELECT DISTINCT pc.Anio
    FROM dbo.PresupuestoCedis pc WITH (NOLOCK)
    WHERE pc.Anio IS NOT NULL
) x
WHERE Anio BETWEEN 2020 AND 2100
ORDER BY Anio DESC;

/* 3) MÁSTERS */
SELECT DISTINCT
    Master = COALESCE(
        NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
        'SIN_MASTER'
    )
FROM dbo.ArticuloSap a WITH (NOLOCK)
WHERE a.ProductoCodigo IS NOT NULL
  AND a.ProductoCodigo <> ''
ORDER BY Master;

/* 4) SKUS */
SELECT
    Sku = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
    Nombre = ISNULL(a.ProductoNombre, ''),
    Master = COALESCE(
        NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
        'SIN_MASTER'
    )
FROM dbo.ArticuloSap a WITH (NOLOCK)
WHERE a.ProductoCodigo IS NOT NULL
  AND a.ProductoCodigo <> ''
ORDER BY Master, Sku;

/* 5) VENDEDORES / SUCURSALES
   ============================================================
   Sólo se muestran opciones que realmente tengan información:
   - Vendedor normal: ventas reales o presupuesto.
   - CEDIS: ventas reales o presupuesto en PresupuestoCedis.
   ============================================================ */
WITH Base AS
(
    SELECT
        VendedorId = c.VendedorId,
        VendedorNombre = LTRIM(RTRIM(ISNULL(c.VendedorNombre, ''))),
        Canal = UPPER(LTRIM(RTRIM(ISNULL(c.U_CANAL, '')))),
        Cliente = c.Cliente,
        EsCedis =
            CASE
                WHEN UPPER(LTRIM(RTRIM(ISNULL(c.U_CANAL, '')))) LIKE 'CEDIS%'
                    THEN 1
                ELSE 0
            END
    FROM dbo.ClienteSap c WITH (NOLOCK)
    WHERE c.VendedorId IS NOT NULL
      AND c.VendedorId > 0
),

VendedoresConVenta AS
(
    SELECT DISTINCT
        cs.VendedorId
    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = sd.Articulo
    INNER JOIN dbo.ClienteSap cs WITH (NOLOCK)
        ON cs.Cliente = se.CodigoSap
    WHERE se.FechaValidacion IS NOT NULL
      AND UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) NOT LIKE 'CEDIS%'
),

VendedoresConPresupuesto AS
(
    SELECT DISTINCT
        p.VendedorId
    FROM dbo.PresupuestoVendedor p WITH (NOLOCK)
    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = p.ProductoCodigo
    WHERE ISNULL(p.PresupuestoAsignado, 0) <> 0
),

CedisConVenta AS
(
    SELECT DISTINCT
        Canal = UPPER(LTRIM(RTRIM(cs.U_CANAL)))
    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = sd.Articulo
    INNER JOIN dbo.ClienteSap cs WITH (NOLOCK)
        ON cs.Cliente = se.CodigoSap
    WHERE se.FechaValidacion IS NOT NULL
      AND UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) LIKE 'CEDIS%'
),

CedisConPresupuesto AS
(
    SELECT DISTINCT
        Canal = UPPER(LTRIM(RTRIM(pc.Canal)))
    FROM dbo.PresupuestoCedis pc WITH (NOLOCK)
    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = pc.ProductoCodigo
    WHERE NULLIF(LTRIM(RTRIM(ISNULL(pc.Canal, ''))), '') IS NOT NULL
      AND ISNULL(pc.PresupuestoAsignado, 0) <> 0
),

VendedoresCatalogo AS
(
    SELECT VendedorId FROM VendedoresConVenta
    UNION
    SELECT VendedorId FROM VendedoresConPresupuesto
),

CedisCatalogo AS
(
    SELECT Canal FROM CedisConVenta
    UNION
    SELECT Canal FROM CedisConPresupuesto
),

Catalogo AS
(
    /* ========================================================
       VENDEDORES
       Igual que el reporte de presupuesto: PresupuestoVendedor
       conserva al vendedor aunque también tenga relación CEDIS.
       ======================================================== */
    SELECT
        Id = CONCAT('VENDEDOR|', vc.VendedorId),
        Nombre = COALESCE(
            NULLIF(MAX(b.VendedorNombre), ''),
            CONCAT('VENDEDOR ', vc.VendedorId)
        )
    FROM VendedoresCatalogo vc
    LEFT JOIN Base b
        ON b.VendedorId = vc.VendedorId
    GROUP BY vc.VendedorId

    UNION ALL

    /* ========================================================
       CEDIS
       Se toma directamente de venta real o PresupuestoCedis.
       ======================================================== */
    SELECT
        Id = CONCAT('CEDIS|', cc.Canal),
        Nombre = cc.Canal
    FROM CedisCatalogo cc
    WHERE NULLIF(LTRIM(RTRIM(ISNULL(cc.Canal, ''))), '') IS NOT NULL
)

SELECT
    Id,
    Nombre
FROM Catalogo
ORDER BY Nombre;";

            await using var con = await AbrirConexionAsync(ct);

            using var multi = await con.QueryMultipleAsync(
                new CommandDefinition(
                    sql,
                    commandTimeout: 60,
                    cancellationToken: ct));

            var ultima = await multi.ReadSingleAsync<UltimaFechaSql>();

            var anios = (await multi.ReadAsync<int>())
                .Distinct()
                .OrderByDescending(x => x)
                .ToList();

            var masters = (await multi.ReadAsync<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            var skus = (await multi.ReadAsync<DashboardSkuCatalogoVm>())
                .ToList();

            var vendedores = (await multi.ReadAsync<VendedorFiltroSqlRow>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .Where(x => !string.IsNullOrWhiteSpace(x.Nombre))
                .ToList();

            if (anios.Count == 0)
                anios.Add(ultima.UltimaFechaVenta?.Year ?? DateTime.Today.Year);

            return Json(new
            {
                anios,
                masters,
                skus,
                vendedores,
                ultimaFechaVenta = ultima.UltimaFechaVenta
            });
        }

        // ============================================================
        // KPIs GENERALES
        // MISMA LÓGICA QUE EL REPORTE DE PRESUPUESTOS:
        // - CEDIS: PresupuestoCedis + Venta real neta de devoluciones.
        // - VENDEDOR: PresupuestoVendedor + surtido real, incluyendo
        //   la distribución proporcional del surtido CEDIS.
        // ============================================================
        [HttpGet("DashboardVentasResumen")]
        public async Task<IActionResult> Resumen(
            int anio,
            int mes,
            int dia,
            string master = "",
            string sku = "",
            string vendedorId = "",
            string compararContra = "presupuesto",
            CancellationToken ct = default)
        {
            if (!TryPeriodo(
                    anio,
                    mes,
                    dia,
                    out var inicio,
                    out var fechaCorte,
                    out var finExclusivo,
                    out var error))
            {
                return BadRequest(new { error });
            }

            var filtros = PrepararFiltrosDashboard(master, sku, vendedorId);

            var diasLaborablesMes = ContarDiasLaborables(
                inicio,
                inicio.AddMonths(1).AddDays(-1));

            var diaLaboral = ContarDiasLaborables(inicio, fechaCorte);

            var factorAlcance = diasLaborablesMes <= 0
                ? 0m
                : (decimal)diaLaboral / diasLaborablesMes;

            var modo = NormalizarComparacion(compararContra);

            var rows = await ObtenerPresupuestoVentaDashboardAsync(
                inicio,
                finExclusivo,
                anio,
                mes,
                filtros,
                ct);

            var ventaReal = rows.Sum(x => x.VentaReal);
            var presupuestoMensual = rows.Sum(x => x.PresupuestoMensual);

            var ultimaFechaVenta = rows
                .Where(x => x.VentaReal > 0 && x.UltimaFechaVenta.HasValue)
                .Select(x => x.UltimaFechaVenta)
                .Max();

            var alcance = Redondear(
                presupuestoMensual * factorAlcance);

            var referencia = modo == "alcance"
                ? alcance
                : presupuestoMensual;

            var cumplimiento = referencia > 0
                ? ventaReal / referencia * 100m
                : 0m;

            var brechaKg = ventaReal - alcance;

            var brechaPct = alcance > 0
                ? brechaKg / alcance * 100m
                : 0m;

            var vm = new DashboardVentasResumenVm
            {
                Anio = anio,
                Mes = mes,
                Dia = fechaCorte.Day,
                FechaCorte = fechaCorte,
                DiasLaborablesMes = diasLaborablesMes,
                DiaLaboral = diaLaboral,
                VentaReal = Redondear(ventaReal),
                PresupuestoMensual = Redondear(presupuestoMensual),
                Alcance = alcance,
                Referencia = Redondear(referencia),
                CumplimientoPct = Redondear(cumplimiento),
                BrechaAlcanceKg = Redondear(brechaKg),
                BrechaAlcancePct = Redondear(brechaPct),
                CompararContra = modo,
                UltimaFechaVenta = ultimaFechaVenta,
                ConsultadoEn = DateTime.Now
            };

            return Json(vm);
        }

        // ============================================================
        // 1) VENTAS X MÁSTER
        // Usa la misma base de Presupuesto / Venta Real que el KPI.
        // ============================================================
        [HttpGet("DashboardVentasMaster")]
        public async Task<IActionResult> VentasPorMaster(
            int anio,
            int mes,
            int dia,
            string master = "",
            string sku = "",
            string vendedorId = "",
            string compararContra = "presupuesto",
            CancellationToken ct = default)
        {
            if (!TryPeriodo(
                    anio,
                    mes,
                    dia,
                    out var inicio,
                    out var fechaCorte,
                    out var finExclusivo,
                    out var error))
            {
                return BadRequest(new { error });
            }

            var filtros = PrepararFiltrosDashboard(master, sku, vendedorId);

            var diasLaborablesMes = ContarDiasLaborables(
                inicio,
                inicio.AddMonths(1).AddDays(-1));

            var diaLaboral = ContarDiasLaborables(inicio, fechaCorte);

            var factorAlcance = diasLaborablesMes <= 0
                ? 0m
                : (decimal)diaLaboral / diasLaborablesMes;

            var modo = NormalizarComparacion(compararContra);

            var baseRows = await ObtenerPresupuestoVentaDashboardAsync(
                inicio,
                finExclusivo,
                anio,
                mes,
                filtros,
                ct);

            var rows = baseRows
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Master)
                    ? "SIN_MASTER"
                    : x.Master)
                .Select(g => new MasterSqlRow
                {
                    Master = g.Key,
                    VentaReal = g.Sum(x => x.VentaReal),
                    PresupuestoMensual = g.Sum(x => x.PresupuestoMensual)
                })
                .ToList();

            var temp = rows
                .Select(x =>
                {
                    var alcance = x.PresupuestoMensual * factorAlcance;

                    var referencia = modo == "alcance"
                        ? alcance
                        : x.PresupuestoMensual;

                    return new DashboardMasterItemVm
                    {
                        Master = string.IsNullOrWhiteSpace(x.Master)
                            ? "SIN_MASTER"
                            : x.Master,
                        VentaReal = Redondear(x.VentaReal),
                        PresupuestoMensual = Redondear(x.PresupuestoMensual),
                        Alcance = Redondear(alcance),
                        Referencia = Redondear(referencia),
                        AvancePct = referencia > 0
                            ? Redondear(x.VentaReal / referencia * 100m)
                            : 0m
                    };
                })
                .ToList();

            var totalReferencia = temp.Sum(x => x.Referencia);

            foreach (var item in temp)
            {
                item.ParticipacionPct = totalReferencia > 0
                    ? Redondear(item.Referencia / totalReferencia * 100m)
                    : 0m;
            }

            return Json(
                temp
                    .OrderByDescending(x => x.Referencia)
                    .ThenByDescending(x => x.VentaReal)
                    .ThenBy(x => x.Master)
                    .ToList()
            );
        }

        // ============================================================
        // 2) VENTAS X VENDEDOR / SUCURSAL
        // CEDIS y VENDEDOR usan exactamente la misma base que Resumen.
        // ============================================================
        [HttpGet("DashboardVentasVendedor")]
        public async Task<IActionResult> VentasPorVendedor(
            int anio,
            int mes,
            int dia,
            string master = "",
            string sku = "",
            string vendedorId = "",
            string compararContra = "presupuesto",
            CancellationToken ct = default)
        {
            if (!TryPeriodo(
                    anio,
                    mes,
                    dia,
                    out var inicio,
                    out var fechaCorte,
                    out var finExclusivo,
                    out var error))
            {
                return BadRequest(new { error });
            }

            var filtros = PrepararFiltrosDashboard(master, sku, vendedorId);

            var diasLaborablesMes = ContarDiasLaborables(
                inicio,
                inicio.AddMonths(1).AddDays(-1));

            var diaLaboral = ContarDiasLaborables(inicio, fechaCorte);

            var factorAlcance = diasLaborablesMes <= 0
                ? 0m
                : (decimal)diaLaboral / diasLaborablesMes;

            var modo = NormalizarComparacion(compararContra);

            var baseRows = await ObtenerPresupuestoVentaDashboardAsync(
                inicio,
                finExclusivo,
                anio,
                mes,
                filtros,
                ct);

            var rows = baseRows
                .GroupBy(x => new
                {
                    x.Origen,
                    Grupo = x.Origen == "CEDIS"
                        ? (x.Canal ?? "")
                        : x.VendedorId.ToString()
                })
                .Select(g => new VendedorSqlRow
                {
                    VendedorId = g.Key.Origen == "CEDIS"
                        ? 0
                        : g.First().VendedorId,
                    Vendedor = g.Key.Origen == "CEDIS"
                        ? (g.First().Canal ?? "SIN CEDIS")
                        : (g.First().Vendedor ?? "SIN VENDEDOR"),
                    VentaReal = g.Sum(x => x.VentaReal),
                    PresupuestoMensual = g.Sum(x => x.PresupuestoMensual)
                })
                .ToList();

            var result = rows
                .Select(x =>
                {
                    var alcance = x.PresupuestoMensual * factorAlcance;

                    var referencia = modo == "alcance"
                        ? alcance
                        : x.PresupuestoMensual;

                    return new DashboardVendedorItemVm
                    {
                        VendedorId = x.VendedorId,
                        Vendedor = string.IsNullOrWhiteSpace(x.Vendedor)
                            ? "SIN VENDEDOR"
                            : x.Vendedor,
                        VentaReal = Redondear(x.VentaReal),
                        PresupuestoMensual = Redondear(x.PresupuestoMensual),
                        Alcance = Redondear(alcance),
                        Referencia = Redondear(referencia),
                        CumplimientoPct = referencia > 0
                            ? Redondear(x.VentaReal / referencia * 100m)
                            : 0m
                    };
                })
                .OrderByDescending(x => x.VentaReal)
                .ThenByDescending(x => x.Referencia)
                .ThenBy(x => x.Vendedor)
                .ToList();

            return Json(result);
        }

        // ============================================================
        // 3) ANÁLISIS DE PRECIOS (POR SKU)
        //
        // CEDIS:
        // - Se identifica por ClienteSap.U_CANAL LIKE 'CEDIS%'.
        // - Se agrupa por U_CANAL.
        // - Se muestra U_CANAL como nombre.
        //
        // VENDEDOR NORMAL:
        // - Se conserva VendedorId / Vendedor.
        // ============================================================
        [HttpGet("DashboardVentasPrecios")]
        public async Task<IActionResult> Precios(
            int anio,
            int mes,
            int dia,
            string master = "",
            string sku = "",
            string vendedorId = "",
            CancellationToken ct = default)
        {
            if (!TryPeriodo(
                    anio,
                    mes,
                    dia,
                    out var inicio,
                    out _,
                    out var finExclusivo,
                    out var error))
            {
                return BadRequest(new { error });
            }

            var filtros = PrepararFiltrosDashboard(master, sku, vendedorId);

            const string sql = @"
WITH PrecioBase AS
(
    SELECT
        Grupo =
            CASE
                WHEN UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) LIKE 'CEDIS%'
                    THEN 'CEDIS|' + UPPER(LTRIM(RTRIM(cs.U_CANAL)))
                ELSE 'VENDEDOR|' + CONVERT(
                    VARCHAR(20),
                    COALESCE(
                        NULLIF(o.VendedorId, 0),
                        cs.VendedorId,
                        0
                    )
                )
            END,
        VendedorId =
            CASE
                WHEN UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) LIKE 'CEDIS%'
                    THEN 0
                ELSE COALESCE(
                    NULLIF(o.VendedorId, 0),
                    cs.VendedorId,
                    0
                )
            END,
        Vendedor =
            CASE
                WHEN UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) LIKE 'CEDIS%'
                    THEN UPPER(LTRIM(RTRIM(cs.U_CANAL)))
                ELSE COALESCE(
                    NULLIF(LTRIM(RTRIM(o.Vendedor)), ''),
                    NULLIF(LTRIM(RTRIM(cs.VendedorNombre)), ''),
                    CASE
                        WHEN COALESCE(
                            NULLIF(o.VendedorId, 0),
                            cs.VendedorId,
                            0
                        ) = 0
                            THEN 'SIN VENDEDOR'
                        ELSE CONCAT(
                            'VENDEDOR ',
                            COALESCE(
                                NULLIF(o.VendedorId, 0),
                                cs.VendedorId,
                                0
                            )
                        )
                    END
                )
            END,
        Peso = CAST(ISNULL(op.Peso, 0) AS DECIMAL(18,4)),
        Precio = CAST(ISNULL(op.Precio, 0) AS DECIMAL(18,4))
    FROM dbo.OrdenVenta o WITH (NOLOCK)
    INNER JOIN dbo.OrdenVentaProducto op WITH (NOLOCK)
        ON op.PedidoId = o.Id
    LEFT JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = op.ProductoCodigo
    LEFT JOIN dbo.ClienteSap cs WITH (NOLOCK)
        ON cs.Cliente = o.Cliente
    WHERE o.FechaEntrega >= @Inicio
      AND o.FechaEntrega <  @FinExclusivo
      AND ISNULL(o.Estatus, 0) <> 0
      AND (op.Eliminado IS NULL OR op.Eliminado = 0)
      AND ISNULL(op.Precio, 0) > 0
      AND ISNULL(op.Peso, 0) > 0
      AND NULLIF(
            LTRIM(RTRIM(ISNULL(op.ProductoCodigo, ''))),
            ''
          ) IS NOT NULL
      AND (
            @TieneMaster = 0
            OR COALESCE(
                NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
                'SIN_MASTER'
            ) IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@MastersCsv, ',') WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL)
          )
      AND (
            @TieneSku = 0
            OR UPPER(LTRIM(RTRIM(ISNULL(op.ProductoCodigo, '')))) IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@SkusCsv, ',') WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL)
          )
      AND (
            @TieneVendedor = 0
            OR UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@CanalesCsv, ',') WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL)
            OR (
                COALESCE(
                    NULLIF(o.VendedorId, 0),
                    cs.VendedorId,
                    0
                ) IN (SELECT TRY_CONVERT(int, value) FROM STRING_SPLIT(@VendedorIdsCsv, ',') WHERE TRY_CONVERT(int, value) IS NOT NULL)
                AND UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) NOT LIKE 'CEDIS%'
            )
          )
)
SELECT
    VendedorId = MAX(VendedorId),
    Vendedor = MAX(Vendedor),
    PrecioPonderado = CAST(
        SUM(Peso * Precio) /
        NULLIF(SUM(Peso), 0)
        AS DECIMAL(18,4)
    ),
    Kilos = CAST(
        SUM(Peso)
        AS DECIMAL(18,4)
    )
FROM PrecioBase
GROUP BY Grupo
HAVING SUM(Peso) > 0
ORDER BY Kilos DESC
OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);

            var items = (
                await con.QueryAsync<DashboardPrecioItemVm>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Inicio = inicio,
                            FinExclusivo = finExclusivo,
                            filtros.TieneMaster,
                            filtros.TieneSku,
                            filtros.TieneVendedor,
                            Masters = filtros.MastersSql,
                            Skus = filtros.SkusSql,
                            VendedorIds = filtros.VendedorIdsSql,
                            CanalesCedis = filtros.CanalesCedisSql,
                            MastersCsv = string.Join(",", filtros.Masters),
                            SkusCsv = string.Join(",", filtros.Skus),
                            VendedorIdsCsv = string.Join(",", filtros.VendedorIds),
                            CanalesCsv = string.Join(",", filtros.CanalesCedis)
                        },
                        commandTimeout: 60,
                        cancellationToken: ct
                    )
                )
            ).ToList();

            foreach (var item in items)
            {
                item.PrecioPonderado = Redondear(item.PrecioPonderado);
                item.Kilos = Redondear(item.Kilos);
            }

            var kgTotal = items.Sum(x => x.Kilos);

            var promedio = kgTotal > 0
                ? items.Sum(x => x.PrecioPonderado * x.Kilos) / kgTotal
                : 0m;

            return Json(new DashboardPreciosVm
            {
                PrecioPromedioPonderado = Redondear(promedio),
                Items = items,
                Nota = "Precio de Orden de Venta ponderado por los KG registrados en OrdenVentaProducto.Peso."
            });
        }

        // ============================================================
        // 4) TENDENCIA ACUMULADA
        // La venta acumulada utiliza las mismas reglas del reporte:
        // - CEDIS = venta bruta - devoluciones de bodega, mínimo 0 por SKU/CEDIS.
        // - VENDEDOR = surtido normal + distribución proporcional del CEDIS.
        // ============================================================
        [HttpGet("DashboardVentasTendencia")]
        public async Task<IActionResult> Tendencia(
            int anio,
            int mes,
            int dia,
            string master = "",
            string sku = "",
            string vendedorId = "",
            CancellationToken ct = default)
        {
            if (!TryPeriodo(
                    anio,
                    mes,
                    dia,
                    out var inicio,
                    out var fechaCorte,
                    out var finExclusivo,
                    out var error))
            {
                return BadRequest(new { error });
            }

            var filtros = PrepararFiltrosDashboard(master, sku, vendedorId);

            var finMes = inicio.AddMonths(1).AddDays(-1);
            var laborables = FechasLaborables(inicio, finMes).ToList();

            var baseRows = await ObtenerPresupuestoVentaDashboardAsync(
                inicio,
                finExclusivo,
                anio,
                mes,
                filtros,
                ct);

            var presupuestoMensual = baseRows.Sum(x => x.PresupuestoMensual);

            var eventos = await ObtenerEventosVentaDashboardAsync(
                inicio,
                inicio.AddMonths(1),
                anio,
                mes,
                filtros,
                ct);

            var eventosPorFecha = eventos
                .GroupBy(x => x.Fecha.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            var estados = new Dictionary<string, EstadoVentaAcumulada>(
                StringComparer.OrdinalIgnoreCase);

            var result = new List<DashboardTendenciaItemVm>();

            var cursor = inicio;
            var indiceLaboral = 0;

            while (cursor <= finMes)
            {
                if (eventosPorFecha.TryGetValue(cursor.Date, out var eventosDia))
                {
                    foreach (var evento in eventosDia)
                    {
                        var key = evento.Origen == "CEDIS"
                            ? $"CEDIS|{evento.Canal}|{evento.Sku}"
                            : $"VENDEDOR|{evento.VendedorId}|{evento.Sku}";

                        if (!estados.TryGetValue(key, out var estado))
                        {
                            estado = new EstadoVentaAcumulada
                            {
                                Origen = evento.Origen
                            };
                            estados[key] = estado;
                        }

                        if (evento.Origen == "CEDIS")
                        {
                            estado.VentaBruta += evento.VentaBruta;
                            estado.Devoluciones += evento.Devoluciones;
                        }
                        else
                        {
                            estado.VentaVendedor += evento.VentaVendedor;
                        }
                    }
                }

                if (EsDiaLaboral(cursor))
                {
                    indiceLaboral++;

                    var ventaAcumulada = estados.Values.Sum(estado =>
                    {
                        if (estado.Origen == "CEDIS")
                        {
                            var neta = estado.VentaBruta - estado.Devoluciones;
                            return neta < 0 ? 0m : neta;
                        }

                        return estado.VentaVendedor;
                    });

                    var alcance = laborables.Count > 0
                        ? presupuestoMensual
                            * indiceLaboral
                            / laborables.Count
                        : 0m;

                    decimal? real = cursor <= fechaCorte
                        ? Redondear(ventaAcumulada)
                        : null;

                    decimal? brecha = real.HasValue
                        ? Redondear(real.Value - alcance)
                        : null;

                    result.Add(new DashboardTendenciaItemVm
                    {
                        DiaLaboral = indiceLaboral,
                        Fecha = cursor,
                        VentaAcumulada = real,
                        AlcanceAcumulado = Redondear(alcance),
                        Brecha = brecha
                    });
                }

                cursor = cursor.AddDays(1);
            }

            return Json(new DashboardTendenciaVm
            {
                PresupuestoMensual = Redondear(presupuestoMensual),
                DiasLaborablesMes = laborables.Count,
                Items = result
            });
        }

        // ============================================================
        // BASE ÚNICA DE PRESUPUESTO / VENTA REAL
        // ============================================================
        private async Task<List<PresupuestoVentaSqlRow>> ObtenerPresupuestoVentaDashboardAsync(
            DateTime inicio,
            DateTime finExclusivo,
            int anio,
            int mes,
            DashboardFiltrosInternos filtros,
            CancellationToken ct)
        {
            const string sql = @"
;WITH Productos AS
(
    SELECT
        SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
        Master = COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        )
    FROM dbo.ArticuloSap a WITH (NOLOCK)
    WHERE NULLIF(LTRIM(RTRIM(ISNULL(a.ProductoCodigo, ''))), '') IS NOT NULL
),
Clientes AS
(
    SELECT
        Cliente = UPPER(LTRIM(RTRIM(c.Cliente))),
        VendedorId = c.VendedorId,
        VendedorNombre = LTRIM(RTRIM(ISNULL(c.VendedorNombre, ''))),
        Canal = UPPER(LTRIM(RTRIM(ISNULL(c.U_CANAL, ''))))
    FROM dbo.ClienteSap c WITH (NOLOCK)
),
Vendedores AS
(
    SELECT
        VendedorId,
        VendedorNombre = COALESCE(
            NULLIF(MAX(VendedorNombre), ''),
            CONCAT('VENDEDOR ', VendedorId)
        )
    FROM Clientes
    WHERE VendedorId IS NOT NULL
    GROUP BY VendedorId
),
CanalVendedores AS
(
    SELECT DISTINCT
        Canal,
        VendedorId
    FROM Clientes
    WHERE VendedorId IS NOT NULL
      AND Canal LIKE 'CEDIS%'
),
PresupuestoVendedor AS
(
    SELECT
        VendedorId = pv.VendedorId,
        SKU = UPPER(LTRIM(RTRIM(pv.ProductoCodigo))),
        Presupuesto = SUM(CAST(ISNULL(pv.PresupuestoAsignado, 0) AS DECIMAL(18,4)))
    FROM dbo.PresupuestoVendedor pv WITH (NOLOCK)
    WHERE pv.Anio = @Anio
      AND pv.Mes = @Mes
    GROUP BY
        pv.VendedorId,
        UPPER(LTRIM(RTRIM(pv.ProductoCodigo)))
),
PresupuestoCedis AS
(
    SELECT
        Canal = UPPER(LTRIM(RTRIM(pc.Canal))),
        SKU = UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
        Presupuesto = SUM(CAST(ISNULL(pc.PresupuestoAsignado, 0) AS DECIMAL(18,4)))
    FROM dbo.PresupuestoCedis pc WITH (NOLOCK)
    WHERE pc.Anio = @Anio
      AND pc.Mes = @Mes
    GROUP BY
        UPPER(LTRIM(RTRIM(pc.Canal))),
        UPPER(LTRIM(RTRIM(pc.ProductoCodigo)))
),
PresVendedorXCanal AS
(
    SELECT
        cv.Canal,
        pv.SKU,
        PresTotalCanal = SUM(CAST(pv.Presupuesto AS DECIMAL(18,4)))
    FROM PresupuestoVendedor pv
    INNER JOIN CanalVendedores cv
        ON cv.VendedorId = pv.VendedorId
    GROUP BY
        cv.Canal,
        pv.SKU
),
VentaRealBase AS
(
    SELECT
        SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
        VendedorId = cli.VendedorId,
        Canal = cli.Canal,
        KgVendidos = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4))),
        UltimaFechaVenta = MAX(CAST(se.FechaValidacion AS date))
    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    LEFT JOIN Clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(se.CodigoSap)))
    WHERE se.FechaValidacion >= @Inicio
      AND se.FechaValidacion < @FinExclusivo
    GROUP BY
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        cli.VendedorId,
        cli.Canal
),
VentaRealCedis AS
(
    SELECT
        Canal,
        SKU,
        VentaRealBruta = SUM(KgVendidos),
        UltimaFechaVenta = MAX(UltimaFechaVenta)
    FROM VentaRealBase
    WHERE Canal LIKE 'CEDIS%'
    GROUP BY Canal, SKU
),
DevolucionesCedis AS
(
    SELECT
        Canal = UPPER(LTRIM(RTRIM(ISNULL(c.Canal, '')))),
        SKU = UPPER(LTRIM(RTRIM(d.Articulo))),
        KgDevoluciones = SUM(CAST(ISNULL(d.Peso, 0) AS DECIMAL(18,4)))
    FROM dbo.DevolucionMeat d WITH (NOLOCK)
    INNER JOIN Clientes c
        ON UPPER(LTRIM(RTRIM(d.CodigoSap))) = c.Cliente
    WHERE d.FechaDevolucion >= @Inicio
      AND d.FechaDevolucion < @FinExclusivo
      AND (
            d.Remision LIKE '%SUC01%'
            OR d.Remision LIKE '%SUC02%'
          )
      AND EXISTS
      (
          SELECT 1
          FROM dbo.Subpedido sp WITH (NOLOCK)
          WHERE CONVERT(varchar(100), sp.U_DocMeat) = CONVERT(varchar(100), d.SolicitudSurtidoId)
      )
      AND ISNULL(
            UPPER(LTRIM(RTRIM(d.AlmacenDevolucionNombre))),
            ''
          ) NOT LIKE 'FRIGORIFICO%'
      AND c.Canal LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(ISNULL(c.Canal, '')))),
        UPPER(LTRIM(RTRIM(d.Articulo)))
),
SurtidoOvCedis AS
(
    SELECT
        Canal = cli.Canal,
        SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
        KgSurtido = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4))),
        UltimaFechaVenta = MAX(CAST(se.FechaValidacion AS date))
    FROM dbo.OrdenVenta o WITH (NOLOCK)
    INNER JOIN dbo.Series ser WITH (NOLOCK)
        ON ser.NombreSerie = o.Serie
    INNER JOIN dbo.Subpedido sp WITH (NOLOCK)
        ON sp.OrdenVentaId = o.Id
    INNER JOIN dbo.SurtidoEncabezado se WITH (NOLOCK)
        ON se.SolicitudSurtidoId = TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(sp.U_DocMeat)), ''))
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    INNER JOIN Clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.Estatus <> 0
      AND se.FechaValidacion >= @Inicio
      AND se.FechaValidacion < @FinExclusivo
      AND ser.Sucursal = 'MATRIZ'
      AND cli.Canal LIKE 'CEDIS%'
    GROUP BY
        cli.Canal,
        UPPER(LTRIM(RTRIM(sd.Articulo)))
),
SurtidoTransferenciasCedis AS
(
    SELECT
        Canal = UPPER(LTRIM(RTRIM(s.Canal))),
        SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
        KgSurtido = SUM(CAST(ISNULL(ts.KgSurtido, 0) AS DECIMAL(18,4))),
        UltimaFechaVenta = MAX(CAST(t.FechaSolicitud AS date))
    FROM dbo.TransferenciaSurtido ts WITH (NOLOCK)
    INNER JOIN dbo.Transferencias t WITH (NOLOCK)
        ON t.Id = ts.TransferenciaId
    INNER JOIN dbo.Series s WITH (NOLOCK)
        ON s.Sucursal = t.Sucursal
    WHERE t.FechaSolicitud >= @Inicio
      AND t.FechaSolicitud < @FinExclusivo
      AND t.Estatus >= 5
      AND ISNULL(ts.KgSurtido, 0) > 0
      AND UPPER(LTRIM(RTRIM(ISNULL(s.Canal, '')))) LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(s.Canal))),
        UPPER(LTRIM(RTRIM(ts.Sku)))
),
SurtidoCedisBase AS
(
    SELECT
        Canal,
        SKU,
        KgSurtido = SUM(KgSurtido),
        UltimaFechaVenta = MAX(UltimaFechaVenta)
    FROM
    (
        SELECT * FROM SurtidoOvCedis
        UNION ALL
        SELECT * FROM SurtidoTransferenciasCedis
    ) x
    GROUP BY Canal, SKU
),
SurtidoVendedorNormal AS
(
    SELECT
        VendedorId = cli.VendedorId,
        SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
        KgSurtido = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4))),
        UltimaFechaVenta = MAX(CAST(se.FechaValidacion AS date))
    FROM dbo.OrdenVenta o WITH (NOLOCK)
    INNER JOIN dbo.Series ser WITH (NOLOCK)
        ON ser.NombreSerie = o.Serie
    INNER JOIN dbo.Subpedido sp WITH (NOLOCK)
        ON sp.OrdenVentaId = o.Id
    INNER JOIN dbo.SurtidoEncabezado se WITH (NOLOCK)
        ON se.SolicitudSurtidoId = TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(sp.U_DocMeat)), ''))
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    INNER JOIN Clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.Estatus <> 0
      AND se.FechaValidacion >= @Inicio
      AND se.FechaValidacion < @FinExclusivo
      AND ser.Sucursal = 'MATRIZ'
      AND ISNULL(cli.Canal, '') NOT LIKE 'CEDIS%'
      AND cli.VendedorId IS NOT NULL
    GROUP BY
        cli.VendedorId,
        UPPER(LTRIM(RTRIM(sd.Articulo)))
),
SurtidoVendedorDesdeCedis AS
(
    SELECT
        VendedorId = pv.VendedorId,
        SKU = pv.SKU,
        KgSurtido = SUM(
            CASE
                WHEN ISNULL(pxc.PresTotalCanal, 0) <= 0 THEN 0
                ELSE sb.KgSurtido
                     * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal)
            END
        ),
        UltimaFechaVenta = MAX(sb.UltimaFechaVenta)
    FROM PresupuestoVendedor pv
    INNER JOIN CanalVendedores cv
        ON cv.VendedorId = pv.VendedorId
    INNER JOIN PresVendedorXCanal pxc
        ON pxc.Canal = cv.Canal
       AND pxc.SKU = pv.SKU
    INNER JOIN SurtidoCedisBase sb
        ON sb.Canal = cv.Canal
       AND sb.SKU = pv.SKU
    GROUP BY
        pv.VendedorId,
        pv.SKU
),
SurtidoVendedorTotal AS
(
    SELECT
        VendedorId,
        SKU,
        KgSurtido = SUM(KgSurtido),
        UltimaFechaVenta = MAX(UltimaFechaVenta)
    FROM
    (
        SELECT * FROM SurtidoVendedorNormal
        UNION ALL
        SELECT * FROM SurtidoVendedorDesdeCedis
    ) x
    GROUP BY VendedorId, SKU
),
CedisKeys AS
(
    SELECT Canal, SKU FROM PresupuestoCedis
    UNION
    SELECT Canal, SKU FROM VentaRealCedis
),
VendedorKeys AS
(
    SELECT VendedorId, SKU FROM PresupuestoVendedor
    UNION
    SELECT VendedorId, SKU FROM SurtidoVendedorTotal
),
Base AS
(
    SELECT
        Origen = CONVERT(varchar(10), 'CEDIS'),
        Canal = ck.Canal,
        VendedorId = CONVERT(int, 0),
        Vendedor = ck.Canal,
        SKU = ck.SKU,
        Master = ISNULL(pr.Master, 'SIN_MASTER'),
        PresupuestoMensual = CAST(ISNULL(pc.Presupuesto, 0) AS DECIMAL(18,4)),
        VentaReal = CAST(
            CASE
                WHEN ISNULL(vrc.VentaRealBruta, 0) - ISNULL(dc.KgDevoluciones, 0) < 0
                    THEN 0
                ELSE ISNULL(vrc.VentaRealBruta, 0) - ISNULL(dc.KgDevoluciones, 0)
            END
            AS DECIMAL(18,4)
        ),
        UltimaFechaVenta = vrc.UltimaFechaVenta
    FROM CedisKeys ck
    LEFT JOIN PresupuestoCedis pc
        ON pc.Canal = ck.Canal
       AND pc.SKU = ck.SKU
    LEFT JOIN VentaRealCedis vrc
        ON vrc.Canal = ck.Canal
       AND vrc.SKU = ck.SKU
    LEFT JOIN DevolucionesCedis dc
        ON dc.Canal = ck.Canal
       AND dc.SKU = ck.SKU
    LEFT JOIN Productos pr
        ON pr.SKU = ck.SKU

    UNION ALL

    SELECT
        Origen = CONVERT(varchar(10), 'VENDEDOR'),
        Canal = CONVERT(varchar(100), NULL),
        VendedorId = vk.VendedorId,
        Vendedor = ISNULL(v.VendedorNombre, CONCAT('VENDEDOR ', vk.VendedorId)),
        SKU = vk.SKU,
        Master = ISNULL(pr.Master, 'SIN_MASTER'),
        PresupuestoMensual = CAST(ISNULL(pv.Presupuesto, 0) AS DECIMAL(18,4)),
        VentaReal = CAST(ISNULL(srv.KgSurtido, 0) AS DECIMAL(18,4)),
        UltimaFechaVenta = srv.UltimaFechaVenta
    FROM VendedorKeys vk
    LEFT JOIN PresupuestoVendedor pv
        ON pv.VendedorId = vk.VendedorId
       AND pv.SKU = vk.SKU
    LEFT JOIN SurtidoVendedorTotal srv
        ON srv.VendedorId = vk.VendedorId
       AND srv.SKU = vk.SKU
    LEFT JOIN Vendedores v
        ON v.VendedorId = vk.VendedorId
    LEFT JOIN Productos pr
        ON pr.SKU = vk.SKU
)
SELECT
    Origen,
    Canal,
    VendedorId,
    Vendedor,
    Sku = SKU,
    Master,
    PresupuestoMensual,
    VentaReal,
    UltimaFechaVenta
FROM Base
WHERE
    (
        ISNULL(PresupuestoMensual, 0) > 0
        OR ISNULL(VentaReal, 0) > 0
    )
    AND (
        @TieneMaster = 0
        OR Master IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@MastersCsv, ',') WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL)
    )
    AND (
        @TieneSku = 0
        OR SKU IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@SkusCsv, ',') WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL)
    )
    AND (
        @TieneVendedor = 0
        OR (Origen = 'CEDIS' AND Canal IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@CanalesCsv, ',') WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL))
        OR (Origen = 'VENDEDOR' AND VendedorId IN (SELECT TRY_CONVERT(int, value) FROM STRING_SPLIT(@VendedorIdsCsv, ',') WHERE TRY_CONVERT(int, value) IS NOT NULL))
    )
ORDER BY Origen, Canal, VendedorId, SKU
OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);

            return (
                await con.QueryAsync<PresupuestoVentaSqlRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Inicio = inicio,
                            FinExclusivo = finExclusivo,
                            Anio = anio,
                            Mes = mes,
                            filtros.TieneMaster,
                            filtros.TieneSku,
                            filtros.TieneVendedor,
                            Masters = filtros.MastersSql,
                            Skus = filtros.SkusSql,
                            VendedorIds = filtros.VendedorIdsSql,
                            CanalesCedis = filtros.CanalesCedisSql,
                            MastersCsv = string.Join(",", filtros.Masters),
                            SkusCsv = string.Join(",", filtros.Skus),
                            VendedorIdsCsv = string.Join(",", filtros.VendedorIds),
                            CanalesCsv = string.Join(",", filtros.CanalesCedis)
                        },
                        commandTimeout: 180,
                        cancellationToken: ct
                    )
                )
            ).ToList();
        }

        // ============================================================
        // EVENTOS DIARIOS PARA TENDENCIA
        // ============================================================
        private async Task<List<VentaDiaDetalleSqlRow>> ObtenerEventosVentaDashboardAsync(
            DateTime inicio,
            DateTime finExclusivo,
            int anio,
            int mes,
            DashboardFiltrosInternos filtros,
            CancellationToken ct)
        {
            const string sql = @"
;WITH Productos AS
(
    SELECT
        SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
        Master = COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        )
    FROM dbo.ArticuloSap a WITH (NOLOCK)
    WHERE NULLIF(LTRIM(RTRIM(ISNULL(a.ProductoCodigo, ''))), '') IS NOT NULL
),
Clientes AS
(
    SELECT
        Cliente = UPPER(LTRIM(RTRIM(c.Cliente))),
        VendedorId = c.VendedorId,
        Canal = UPPER(LTRIM(RTRIM(ISNULL(c.U_CANAL, ''))))
    FROM dbo.ClienteSap c WITH (NOLOCK)
),
CanalVendedores AS
(
    SELECT DISTINCT Canal, VendedorId
    FROM Clientes
    WHERE VendedorId IS NOT NULL
      AND Canal LIKE 'CEDIS%'
),
PresupuestoVendedor AS
(
    SELECT
        VendedorId = pv.VendedorId,
        SKU = UPPER(LTRIM(RTRIM(pv.ProductoCodigo))),
        Presupuesto = SUM(CAST(ISNULL(pv.PresupuestoAsignado, 0) AS DECIMAL(18,4)))
    FROM dbo.PresupuestoVendedor pv WITH (NOLOCK)
    WHERE pv.Anio = @Anio
      AND pv.Mes = @Mes
    GROUP BY
        pv.VendedorId,
        UPPER(LTRIM(RTRIM(pv.ProductoCodigo)))
),
PresVendedorXCanal AS
(
    SELECT
        cv.Canal,
        pv.SKU,
        PresTotalCanal = SUM(CAST(pv.Presupuesto AS DECIMAL(18,4)))
    FROM PresupuestoVendedor pv
    INNER JOIN CanalVendedores cv
        ON cv.VendedorId = pv.VendedorId
    GROUP BY cv.Canal, pv.SKU
),
VentaCedisDiaria AS
(
    SELECT
        Canal = cli.Canal,
        SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Fecha = CAST(se.FechaValidacion AS date),
        VentaBruta = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    INNER JOIN Clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(se.CodigoSap)))
    WHERE se.FechaValidacion >= @Inicio
      AND se.FechaValidacion < @FinExclusivo
      AND cli.Canal LIKE 'CEDIS%'
    GROUP BY
        cli.Canal,
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        CAST(se.FechaValidacion AS date)
),
DevolucionesCedisDiarias AS
(
    SELECT
        Canal = c.Canal,
        SKU = UPPER(LTRIM(RTRIM(d.Articulo))),
        Fecha = CAST(d.FechaDevolucion AS date),
        Devoluciones = SUM(CAST(ISNULL(d.Peso, 0) AS DECIMAL(18,4)))
    FROM dbo.DevolucionMeat d WITH (NOLOCK)
    INNER JOIN Clientes c
        ON UPPER(LTRIM(RTRIM(d.CodigoSap))) = c.Cliente
    WHERE d.FechaDevolucion >= @Inicio
      AND d.FechaDevolucion < @FinExclusivo
      AND (
            d.Remision LIKE '%SUC01%'
            OR d.Remision LIKE '%SUC02%'
          )
      AND EXISTS
      (
          SELECT 1
          FROM dbo.Subpedido sp WITH (NOLOCK)
          WHERE CONVERT(varchar(100), sp.U_DocMeat) = CONVERT(varchar(100), d.SolicitudSurtidoId)
      )
      AND ISNULL(
            UPPER(LTRIM(RTRIM(d.AlmacenDevolucionNombre))),
            ''
          ) NOT LIKE 'FRIGORIFICO%'
      AND c.Canal LIKE 'CEDIS%'
    GROUP BY
        c.Canal,
        UPPER(LTRIM(RTRIM(d.Articulo))),
        CAST(d.FechaDevolucion AS date)
),
SurtidoOvCedisDiario AS
(
    SELECT
        Canal = cli.Canal,
        SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Fecha = CAST(se.FechaValidacion AS date),
        KgSurtido = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o WITH (NOLOCK)
    INNER JOIN dbo.Series ser WITH (NOLOCK)
        ON ser.NombreSerie = o.Serie
    INNER JOIN dbo.Subpedido sp WITH (NOLOCK)
        ON sp.OrdenVentaId = o.Id
    INNER JOIN dbo.SurtidoEncabezado se WITH (NOLOCK)
        ON se.SolicitudSurtidoId = TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(sp.U_DocMeat)), ''))
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    INNER JOIN Clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.Estatus <> 0
      AND se.FechaValidacion >= @Inicio
      AND se.FechaValidacion < @FinExclusivo
      AND ser.Sucursal = 'MATRIZ'
      AND cli.Canal LIKE 'CEDIS%'
    GROUP BY
        cli.Canal,
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        CAST(se.FechaValidacion AS date)
),
SurtidoTransferenciasCedisDiario AS
(
    SELECT
        Canal = UPPER(LTRIM(RTRIM(s.Canal))),
        SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
        Fecha = CAST(t.FechaSolicitud AS date),
        KgSurtido = SUM(CAST(ISNULL(ts.KgSurtido, 0) AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts WITH (NOLOCK)
    INNER JOIN dbo.Transferencias t WITH (NOLOCK)
        ON t.Id = ts.TransferenciaId
    INNER JOIN dbo.Series s WITH (NOLOCK)
        ON s.Sucursal = t.Sucursal
    WHERE t.FechaSolicitud >= @Inicio
      AND t.FechaSolicitud < @FinExclusivo
      AND t.Estatus >= 5
      AND ISNULL(ts.KgSurtido, 0) > 0
      AND UPPER(LTRIM(RTRIM(ISNULL(s.Canal, '')))) LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(s.Canal))),
        UPPER(LTRIM(RTRIM(ts.Sku))),
        CAST(t.FechaSolicitud AS date)
),
SurtidoCedisDiario AS
(
    SELECT
        Canal,
        SKU,
        Fecha,
        KgSurtido = SUM(KgSurtido)
    FROM
    (
        SELECT * FROM SurtidoOvCedisDiario
        UNION ALL
        SELECT * FROM SurtidoTransferenciasCedisDiario
    ) x
    GROUP BY Canal, SKU, Fecha
),
SurtidoVendedorNormalDiario AS
(
    SELECT
        VendedorId = cli.VendedorId,
        SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Fecha = CAST(se.FechaValidacion AS date),
        KgSurtido = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o WITH (NOLOCK)
    INNER JOIN dbo.Series ser WITH (NOLOCK)
        ON ser.NombreSerie = o.Serie
    INNER JOIN dbo.Subpedido sp WITH (NOLOCK)
        ON sp.OrdenVentaId = o.Id
    INNER JOIN dbo.SurtidoEncabezado se WITH (NOLOCK)
        ON se.SolicitudSurtidoId = TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(sp.U_DocMeat)), ''))
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    INNER JOIN Clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.Estatus <> 0
      AND se.FechaValidacion >= @Inicio
      AND se.FechaValidacion < @FinExclusivo
      AND ser.Sucursal = 'MATRIZ'
      AND ISNULL(cli.Canal, '') NOT LIKE 'CEDIS%'
      AND cli.VendedorId IS NOT NULL
    GROUP BY
        cli.VendedorId,
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        CAST(se.FechaValidacion AS date)
),
SurtidoVendedorDesdeCedisDiario AS
(
    SELECT
        VendedorId = pv.VendedorId,
        SKU = pv.SKU,
        Fecha = sc.Fecha,
        KgSurtido = SUM(
            CASE
                WHEN ISNULL(pxc.PresTotalCanal, 0) <= 0 THEN 0
                ELSE sc.KgSurtido
                     * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal)
            END
        )
    FROM PresupuestoVendedor pv
    INNER JOIN CanalVendedores cv
        ON cv.VendedorId = pv.VendedorId
    INNER JOIN PresVendedorXCanal pxc
        ON pxc.Canal = cv.Canal
       AND pxc.SKU = pv.SKU
    INNER JOIN SurtidoCedisDiario sc
        ON sc.Canal = cv.Canal
       AND sc.SKU = pv.SKU
    GROUP BY
        pv.VendedorId,
        pv.SKU,
        sc.Fecha
),
Eventos AS
(
    SELECT
        Origen = CONVERT(varchar(10), 'CEDIS'),
        Canal = v.Canal,
        VendedorId = CONVERT(int, 0),
        SKU = v.SKU,
        Fecha = v.Fecha,
        VentaBruta = v.VentaBruta,
        Devoluciones = CAST(0 AS DECIMAL(18,4)),
        VentaVendedor = CAST(0 AS DECIMAL(18,4))
    FROM VentaCedisDiaria v

    UNION ALL

    SELECT
        Origen = CONVERT(varchar(10), 'CEDIS'),
        Canal = d.Canal,
        VendedorId = CONVERT(int, 0),
        SKU = d.SKU,
        Fecha = d.Fecha,
        VentaBruta = CAST(0 AS DECIMAL(18,4)),
        Devoluciones = d.Devoluciones,
        VentaVendedor = CAST(0 AS DECIMAL(18,4))
    FROM DevolucionesCedisDiarias d

    UNION ALL

    SELECT
        Origen = CONVERT(varchar(10), 'VENDEDOR'),
        Canal = CONVERT(varchar(100), NULL),
        VendedorId = v.VendedorId,
        SKU = v.SKU,
        Fecha = v.Fecha,
        VentaBruta = CAST(0 AS DECIMAL(18,4)),
        Devoluciones = CAST(0 AS DECIMAL(18,4)),
        VentaVendedor = v.KgSurtido
    FROM SurtidoVendedorNormalDiario v

    UNION ALL

    SELECT
        Origen = CONVERT(varchar(10), 'VENDEDOR'),
        Canal = CONVERT(varchar(100), NULL),
        VendedorId = v.VendedorId,
        SKU = v.SKU,
        Fecha = v.Fecha,
        VentaBruta = CAST(0 AS DECIMAL(18,4)),
        Devoluciones = CAST(0 AS DECIMAL(18,4)),
        VentaVendedor = v.KgSurtido
    FROM SurtidoVendedorDesdeCedisDiario v
)
SELECT
    e.Origen,
    e.Canal,
    e.VendedorId,
    e.SKU AS Sku,
    Master = ISNULL(p.Master, 'SIN_MASTER'),
    e.Fecha,
    VentaBruta = SUM(e.VentaBruta),
    Devoluciones = SUM(e.Devoluciones),
    VentaVendedor = SUM(e.VentaVendedor)
FROM Eventos e
LEFT JOIN Productos p
    ON p.SKU = e.SKU
WHERE
    (
        @TieneMaster = 0
        OR ISNULL(p.Master, 'SIN_MASTER') IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@MastersCsv, ',') WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL)
    )
    AND (
        @TieneSku = 0
        OR e.SKU IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@SkusCsv, ',') WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL)
    )
    AND (
        @TieneVendedor = 0
        OR (e.Origen = 'CEDIS' AND e.Canal IN (SELECT UPPER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(@CanalesCsv, ',') WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL))
        OR (e.Origen = 'VENDEDOR' AND e.VendedorId IN (SELECT TRY_CONVERT(int, value) FROM STRING_SPLIT(@VendedorIdsCsv, ',') WHERE TRY_CONVERT(int, value) IS NOT NULL))
    )
GROUP BY
    e.Origen,
    e.Canal,
    e.VendedorId,
    e.SKU,
    ISNULL(p.Master, 'SIN_MASTER'),
    e.Fecha
ORDER BY e.Fecha, e.Origen, e.Canal, e.VendedorId, e.SKU
OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);

            return (
                await con.QueryAsync<VentaDiaDetalleSqlRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Inicio = inicio,
                            FinExclusivo = finExclusivo,
                            Anio = anio,
                            Mes = mes,
                            filtros.TieneMaster,
                            filtros.TieneSku,
                            filtros.TieneVendedor,
                            Masters = filtros.MastersSql,
                            Skus = filtros.SkusSql,
                            VendedorIds = filtros.VendedorIdsSql,
                            CanalesCedis = filtros.CanalesCedisSql,
                            MastersCsv = string.Join(",", filtros.Masters),
                            SkusCsv = string.Join(",", filtros.Skus),
                            VendedorIdsCsv = string.Join(",", filtros.VendedorIds),
                            CanalesCsv = string.Join(",", filtros.CanalesCedis)
                        },
                        commandTimeout: 180,
                        cancellationToken: ct
                    )
                )
            ).ToList();
        }

        // ============================================================
        // HELPERS
        // ============================================================
        private string GetConnectionString()
        {
            var cs = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(cs))
            {
                throw new InvalidOperationException(
                    "No se encontró ConnectionStrings:DefaultConnection.");
            }

            return cs;
        }

        private async Task<SqlConnection> AbrirConexionAsync(CancellationToken ct)
        {
            var con = new SqlConnection(GetConnectionString());
            await con.OpenAsync(ct);
            return con;
        }

        private static bool TryPeriodo(
            int anio,
            int mes,
            int dia,
            out DateTime inicio,
            out DateTime fechaCorte,
            out DateTime finExclusivo,
            out string error)
        {
            inicio = default;
            fechaCorte = default;
            finExclusivo = default;
            error = "";

            if (anio < 2020 || anio > 2100)
            {
                error = "Año inválido.";
                return false;
            }

            if (mes < 1 || mes > 12)
            {
                error = "Mes inválido.";
                return false;
            }

            var maxDia = DateTime.DaysInMonth(anio, mes);
            dia = Math.Clamp(dia <= 0 ? maxDia : dia, 1, maxDia);

            inicio = new DateTime(anio, mes, 1);
            fechaCorte = new DateTime(anio, mes, dia);
            finExclusivo = fechaCorte.AddDays(1);

            return true;
        }

        // La operación considera laboral de lunes a sábado.
        private static bool EsDiaLaboral(DateTime fecha)
            => fecha.DayOfWeek != DayOfWeek.Sunday;

        private static int ContarDiasLaborables(DateTime desde, DateTime hasta)
        {
            if (hasta < desde)
                return 0;

            var total = 0;

            for (var d = desde.Date; d <= hasta.Date; d = d.AddDays(1))
            {
                if (EsDiaLaboral(d))
                    total++;
            }

            return total;
        }

        private static IEnumerable<DateTime> FechasLaborables(
            DateTime desde,
            DateTime hasta)
        {
            for (var d = desde.Date; d <= hasta.Date; d = d.AddDays(1))
            {
                if (EsDiaLaboral(d))
                    yield return d;
            }
        }

        private static List<string> ParseCsvValores(string? value)
        {
            return (value ?? "")
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries
                )
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static DashboardFiltrosInternos PrepararFiltrosDashboard(
            string? master,
            string? sku,
            string? vendedorId)
        {
            var result = new DashboardFiltrosInternos
            {
                Masters = ParseCsvValores(master)
                    .Select(x => x.ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),

                Skus = ParseCsvValores(sku)
                    .Select(x => x.ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            foreach (var filtro in ParseCsvValores(vendedorId))
            {
                if (filtro.StartsWith(
                        "CEDIS|",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var canal = filtro
                        .Substring("CEDIS|".Length)
                        .Trim()
                        .ToUpperInvariant();

                    if (!string.IsNullOrWhiteSpace(canal))
                        result.CanalesCedis.Add(canal);

                    continue;
                }

                if (filtro.StartsWith(
                        "VENDEDOR|",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var valor = filtro
                        .Substring("VENDEDOR|".Length)
                        .Trim();

                    if (int.TryParse(valor, out var id) && id > 0)
                        result.VendedorIds.Add(id);

                    continue;
                }

                // Compatibilidad con valores antiguos: vendedorId=28
                if (int.TryParse(filtro, out var idAnterior) && idAnterior > 0)
                    result.VendedorIds.Add(idAnterior);
            }

            result.VendedorIds = result.VendedorIds
                .Distinct()
                .ToList();

            result.CanalesCedis = result.CanalesCedis
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return result;
        }

        private static string NormalizarComparacion(string? value)
        {
            var x = (value ?? "").Trim().ToLowerInvariant();
            return x is "alcance" or "reach"
                ? "alcance"
                : "presupuesto";
        }

        private static decimal Redondear(decimal value)
            => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private sealed class DashboardFiltrosInternos
        {
            public List<string> Masters { get; set; } = new();
            public List<string> Skus { get; set; } = new();
            public List<int> VendedorIds { get; set; } = new();
            public List<string> CanalesCedis { get; set; } = new();

            public int TieneMaster =>
                Masters.Count > 0 ? 1 : 0;

            public int TieneSku =>
                Skus.Count > 0 ? 1 : 0;

            public int TieneVendedor =>
                (VendedorIds.Count > 0 || CanalesCedis.Count > 0)
                    ? 1
                    : 0;

            public IEnumerable<string> MastersSql =>
                Masters.Count > 0
                    ? Masters
                    : new List<string> { "__SIN_MASTER_FILTRO__" };

            public IEnumerable<string> SkusSql =>
                Skus.Count > 0
                    ? Skus
                    : new List<string> { "__SIN_SKU_FILTRO__" };

            public IEnumerable<int> VendedorIdsSql =>
                VendedorIds.Count > 0
                    ? VendedorIds
                    : new List<int> { -1 };

            public IEnumerable<string> CanalesCedisSql =>
                CanalesCedis.Count > 0
                    ? CanalesCedis
                    : new List<string> { "__SIN_CEDIS_FILTRO__" };
        }

        // ============================================================
        // FILAS INTERNAS DAPPER
        // ============================================================
        private sealed class UltimaFechaSql
        {
            public DateTime? UltimaFechaVenta { get; set; }
        }

        private sealed class ResumenVentaSql
        {
            public decimal VentaReal { get; set; }
            public DateTime? UltimaFechaVenta { get; set; }
        }

        private sealed class ResumenPresupuestoSql
        {
            public decimal PresupuestoMensual { get; set; }
        }

        private sealed class MasterSqlRow
        {
            public string Master { get; set; } = "";
            public decimal VentaReal { get; set; }
            public decimal PresupuestoMensual { get; set; }
        }

        private sealed class VendedorFiltroSqlRow
        {
            public string Id { get; set; } = "";
            public string Nombre { get; set; } = "";
        }

        private sealed class VendedorSqlRow
        {
            public int VendedorId { get; set; }
            public string Vendedor { get; set; } = "";
            public decimal VentaReal { get; set; }
            public decimal PresupuestoMensual { get; set; }
        }

        private sealed class PresupuestoVentaSqlRow
        {
            public string Origen { get; set; } = "";
            public string? Canal { get; set; }
            public int VendedorId { get; set; }
            public string? Vendedor { get; set; }
            public string Sku { get; set; } = "";
            public string Master { get; set; } = "SIN_MASTER";
            public decimal PresupuestoMensual { get; set; }
            public decimal VentaReal { get; set; }
            public DateTime? UltimaFechaVenta { get; set; }
        }

        private sealed class VentaDiaDetalleSqlRow
        {
            public string Origen { get; set; } = "";
            public string? Canal { get; set; }
            public int VendedorId { get; set; }
            public string Sku { get; set; } = "";
            public string Master { get; set; } = "SIN_MASTER";
            public DateTime Fecha { get; set; }
            public decimal VentaBruta { get; set; }
            public decimal Devoluciones { get; set; }
            public decimal VentaVendedor { get; set; }
        }

        private sealed class EstadoVentaAcumulada
        {
            public string Origen { get; set; } = "";
            public decimal VentaBruta { get; set; }
            public decimal Devoluciones { get; set; }
            public decimal VentaVendedor { get; set; }
        }

        private sealed class VentaDiaSqlRow
        {
            public DateTime Fecha { get; set; }
            public decimal Kilos { get; set; }
        }
    }
}
