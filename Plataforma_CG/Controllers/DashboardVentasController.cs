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
    /// Dashboard de ventas basado en surtido REAL validado.
    ///
    /// Venta real:
    /// - dbo.SurtidoEncabezado.FechaValidacion
    /// - dbo.SurtidoDetalle.Kg
    ///
    /// Relaciones:
    /// - SurtidoDetalle.Articulo -> ArticuloSap.ProductoCodigo -> U_MASTER
    /// - SurtidoEncabezado.SolicitudSurtidoId -> Subpedido.U_DocMeat -> OrdenVenta
    /// - Vendedor: primero OrdenVenta.VendedorId/Vendedor; si no existe OV, ClienteSap
    /// - Precio: OrdenVentaProducto.Precio ponderado por OrdenVentaProducto.Peso
    /// - Presupuesto: PresupuestoVendedor
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
/* 1) ÚLTIMA FECHA REAL VALIDADA */
SELECT UltimaFechaVenta = MAX(CAST(se.FechaValidacion AS date))
FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
WHERE se.FechaValidacion IS NOT NULL;

/* 2) AÑOS CON SURTIDO REAL O PRESUPUESTO */
SELECT Anio
FROM
(
    SELECT DISTINCT YEAR(se.FechaValidacion) AS Anio
    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
    WHERE se.FechaValidacion IS NOT NULL

    UNION

    SELECT DISTINCT pv.Anio
    FROM dbo.PresupuestoVendedor pv WITH (NOLOCK)
    WHERE pv.Anio IS NOT NULL
) x
WHERE Anio BETWEEN 2020 AND 2100
ORDER BY Anio DESC;

/* 3) MÁSTERS */
SELECT DISTINCT
    Master = COALESCE(NULLIF(LTRIM(RTRIM(a.U_MASTER)), ''), 'SIN_MASTER')
FROM dbo.ArticuloSap a WITH (NOLOCK)
WHERE a.ProductoCodigo IS NOT NULL
  AND a.ProductoCodigo <> ''
ORDER BY Master;

/* 4) SKUS */
SELECT
    Sku = a.ProductoCodigo,
    Nombre = ISNULL(a.ProductoNombre, ''),
    Master = COALESCE(NULLIF(LTRIM(RTRIM(a.U_MASTER)), ''), 'SIN_MASTER')
FROM dbo.ArticuloSap a WITH (NOLOCK)
WHERE a.ProductoCodigo IS NOT NULL
  AND a.ProductoCodigo <> ''
ORDER BY Master, Sku;

/* 5) VENDEDORES - catálogo actual. Evitamos escanear OrdenVenta completa. */
SELECT
    Id = c.VendedorId,
    Nombre = COALESCE(
        NULLIF(MAX(LTRIM(RTRIM(c.VendedorNombre))), ''),
        CONCAT('VENDEDOR ', c.VendedorId)
    )
FROM dbo.ClienteSap c WITH (NOLOCK)
WHERE c.VendedorId IS NOT NULL
  AND c.VendedorId > 0
GROUP BY c.VendedorId
ORDER BY Nombre;";

            await using var con = await AbrirConexionAsync(ct);
            using var multi = await con.QueryMultipleAsync(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: ct));

            var ultima = await multi.ReadSingleAsync<UltimaFechaSql>();

            var vm = new DashboardVentasCatalogosVm
            {
                Anios = (await multi.ReadAsync<int>())
                    .Distinct()
                    .OrderByDescending(x => x)
                    .ToList(),

                Masters = (await multi.ReadAsync<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList(),

                Skus = (await multi.ReadAsync<DashboardSkuCatalogoVm>()).ToList(),
                Vendedores = (await multi.ReadAsync<DashboardVendedorCatalogoVm>()).ToList()
            };

            if (vm.Anios.Count == 0)
                vm.Anios.Add(ultima.UltimaFechaVenta?.Year ?? DateTime.Today.Year);

            // Se devuelve anónimo para no obligarte a modificar el ViewModel actual.
            return Json(new
            {
                anios = vm.Anios,
                masters = vm.Masters,
                skus = vm.Skus,
                vendedores = vm.Vendedores,
                ultimaFechaVenta = ultima.UltimaFechaVenta
            });
        }

        // ============================================================
        // KPIs GENERALES
        // Venta Real = KG validados en SurtidoDetalle
        // ============================================================
        [HttpGet("DashboardVentasResumen")]
        public async Task<IActionResult> Resumen(
            int anio,
            int mes,
            int dia,
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

            var diasLaborablesMes = ContarDiasLaborables(
                inicio,
                inicio.AddMonths(1).AddDays(-1));

            var diaLaboral = ContarDiasLaborables(inicio, fechaCorte);
            var factorAlcance = diasLaborablesMes <= 0
                ? 0m
                : (decimal)diaLaboral / diasLaborablesMes;

            var modo = NormalizarComparacion(compararContra);

            const string sql = @"
/* VENTA REAL SURTIDA / VALIDADA */
SELECT
    VentaReal = CAST(
        ISNULL(SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4))), 0)
        AS DECIMAL(18,4)),
    UltimaFechaVenta = MAX(CAST(se.FechaValidacion AS date))
FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
WHERE se.FechaValidacion >= @Inicio
  AND se.FechaValidacion <  @FinExclusivo;

/* PRESUPUESTO MENSUAL */
SELECT
    PresupuestoMensual = CAST(
        ISNULL(SUM(CAST(ISNULL(p.PresupuestoAsignado, 0) AS DECIMAL(18,4))), 0)
        AS DECIMAL(18,4))
FROM dbo.PresupuestoVendedor p WITH (NOLOCK)
WHERE p.Anio = @Anio
  AND p.Mes = @Mes
OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);
            using var multi = await con.QueryMultipleAsync(new CommandDefinition(
                sql,
                new
                {
                    Inicio = inicio,
                    FinExclusivo = finExclusivo,
                    Anio = anio,
                    Mes = mes
                },
                commandTimeout: 60,
                cancellationToken: ct));

            var venta = await multi.ReadSingleAsync<ResumenVentaSql>();
            var presupuesto = await multi.ReadSingleAsync<ResumenPresupuestoSql>();

            var alcance = Redondear(presupuesto.PresupuestoMensual * factorAlcance);
            var referencia = modo == "alcance"
                ? alcance
                : presupuesto.PresupuestoMensual;

            var cumplimiento = referencia > 0
                ? venta.VentaReal / referencia * 100m
                : 0m;

            var brechaKg = venta.VentaReal - alcance;
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
                VentaReal = Redondear(venta.VentaReal),
                PresupuestoMensual = Redondear(presupuesto.PresupuestoMensual),
                Alcance = alcance,
                Referencia = Redondear(referencia),
                CumplimientoPct = Redondear(cumplimiento),
                BrechaAlcanceKg = Redondear(brechaKg),
                BrechaAlcancePct = Redondear(brechaPct),
                CompararContra = modo,
                UltimaFechaVenta = venta.UltimaFechaVenta,
                ConsultadoEn = DateTime.Now
            };

            return Json(vm);
        }

        // ============================================================
        // 1) VENTAS X MÁSTER
        // Filtro Vendedor afecta SOLO este gráfico.
        // Vendedor histórico: OrdenVenta; fallback: ClienteSap.
        // ============================================================
        [HttpGet("DashboardVentasMaster")]
        public async Task<IActionResult> VentasPorMaster(
            int anio,
            int mes,
            int dia,
            int? vendedorId = null,
            string compararContra = "presupuesto",
            CancellationToken ct = default)
        {
            if (!TryPeriodo(anio, mes, dia, out var inicio, out var fechaCorte, out var finExclusivo, out var error))
                return BadRequest(new { error });

            var diasLaborablesMes = ContarDiasLaborables(inicio, inicio.AddMonths(1).AddDays(-1));
            var diaLaboral = ContarDiasLaborables(inicio, fechaCorte);
            var factorAlcance = diasLaborablesMes <= 0 ? 0m : (decimal)diaLaboral / diasLaborablesMes;
            var modo = NormalizarComparacion(compararContra);

            const string sql = @"
;WITH Ventas AS
(
    SELECT
        Master = COALESCE(NULLIF(LTRIM(RTRIM(a.U_MASTER)), ''), 'SIN_MASTER'),
        VentaReal = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = sd.Articulo
    LEFT JOIN dbo.ClienteSap cs WITH (NOLOCK)
        ON cs.Cliente = se.CodigoSap
    WHERE se.FechaValidacion >= @Inicio
      AND se.FechaValidacion <  @FinExclusivo
      AND (@VendedorId IS NULL OR cs.VendedorId = @VendedorId)
    GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(a.U_MASTER)), ''), 'SIN_MASTER')
),
Presupuesto AS
(
    SELECT
        Master = COALESCE(NULLIF(LTRIM(RTRIM(a.U_MASTER)), ''), 'SIN_MASTER'),
        PresupuestoMensual = SUM(CAST(ISNULL(p.PresupuestoAsignado, 0) AS DECIMAL(18,4)))
    FROM dbo.PresupuestoVendedor p WITH (NOLOCK)
    LEFT JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = p.ProductoCodigo
    WHERE p.Anio = @Anio
      AND p.Mes = @Mes
      AND (@VendedorId IS NULL OR p.VendedorId = @VendedorId)
    GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(a.U_MASTER)), ''), 'SIN_MASTER')
)
SELECT
    Master = COALESCE(v.Master, p.Master),
    VentaReal = CAST(ISNULL(v.VentaReal, 0) AS DECIMAL(18,4)),
    PresupuestoMensual = CAST(ISNULL(p.PresupuestoMensual, 0) AS DECIMAL(18,4))
FROM Ventas v
FULL OUTER JOIN Presupuesto p
    ON p.Master = v.Master
WHERE ISNULL(v.VentaReal, 0) <> 0
   OR ISNULL(p.PresupuestoMensual, 0) <> 0
OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);
            var rows = (await con.QueryAsync<MasterSqlRow>(new CommandDefinition(
                sql,
                new { Inicio = inicio, FinExclusivo = finExclusivo, Anio = anio, Mes = mes, VendedorId = vendedorId },
                commandTimeout: 60,
                cancellationToken: ct))).ToList();

            var temp = rows.Select(x =>
            {
                var alcance = x.PresupuestoMensual * factorAlcance;
                var referencia = modo == "alcance" ? alcance : x.PresupuestoMensual;
                return new DashboardMasterItemVm
                {
                    Master = string.IsNullOrWhiteSpace(x.Master) ? "SIN_MASTER" : x.Master,
                    VentaReal = Redondear(x.VentaReal),
                    PresupuestoMensual = Redondear(x.PresupuestoMensual),
                    Alcance = Redondear(alcance),
                    Referencia = Redondear(referencia),
                    AvancePct = referencia > 0 ? Redondear(x.VentaReal / referencia * 100m) : 0m
                };
            }).ToList();

            var totalReferencia = temp.Sum(x => x.Referencia);
            foreach (var item in temp)
                item.ParticipacionPct = totalReferencia > 0 ? Redondear(item.Referencia / totalReferencia * 100m) : 0m;

            return Json(temp
                .OrderByDescending(x => x.Referencia)
                .ThenByDescending(x => x.VentaReal)
                .ThenBy(x => x.Master)
                .ToList());
        }

        // ============================================================
        // 2) VENTAS X VENDEDOR
        // Filtros MASTER y SKU afectan SOLO este gráfico.
        // SKU se toma de SurtidoDetalle.Articulo.
        // ============================================================
        [HttpGet("DashboardVentasVendedor")]
        public async Task<IActionResult> VentasPorVendedor(
            int anio,
            int mes,
            int dia,
            string master = "",
            string sku = "",
            string compararContra = "presupuesto",
            CancellationToken ct = default)
        {
            if (!TryPeriodo(anio, mes, dia, out var inicio, out var fechaCorte, out var finExclusivo, out var error))
                return BadRequest(new { error });

            master = (master ?? "").Trim();
            sku = (sku ?? "").Trim();

            var diasLaborablesMes = ContarDiasLaborables(inicio, inicio.AddMonths(1).AddDays(-1));
            var diaLaboral = ContarDiasLaborables(inicio, fechaCorte);
            var factorAlcance = diasLaborablesMes <= 0 ? 0m : (decimal)diaLaboral / diasLaborablesMes;
            var modo = NormalizarComparacion(compararContra);

            const string sql = @"
;WITH VendedorNombre AS
(
    SELECT
        c.VendedorId,
        Nombre = COALESCE(
            NULLIF(MAX(LTRIM(RTRIM(c.VendedorNombre))), ''),
            CONCAT('VENDEDOR ', c.VendedorId)
        )
    FROM dbo.ClienteSap c WITH (NOLOCK)
    WHERE c.VendedorId IS NOT NULL
      AND c.VendedorId > 0
    GROUP BY c.VendedorId
),
Ventas AS
(
    SELECT
        VendedorId = ISNULL(cs.VendedorId, 0),
        Vendedor = COALESCE(
            NULLIF(MAX(LTRIM(RTRIM(cs.VendedorNombre))), ''),
            CASE WHEN MAX(ISNULL(cs.VendedorId, 0)) = 0 THEN 'SIN VENDEDOR'
                 ELSE CONCAT('VENDEDOR ', MAX(ISNULL(cs.VendedorId, 0))) END
        ),
        VentaReal = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = sd.Articulo
    LEFT JOIN dbo.ClienteSap cs WITH (NOLOCK)
        ON cs.Cliente = se.CodigoSap
    WHERE se.FechaValidacion >= @Inicio
      AND se.FechaValidacion <  @FinExclusivo
      AND (@Master = '' OR a.U_MASTER = @Master)
      AND (@Sku = '' OR sd.Articulo = @Sku)
    GROUP BY ISNULL(cs.VendedorId, 0)
),
Presupuesto AS
(
    SELECT
        VendedorId = p.VendedorId,
        Vendedor = COALESCE(vn.Nombre, CONCAT('VENDEDOR ', p.VendedorId)),
        PresupuestoMensual = SUM(CAST(ISNULL(p.PresupuestoAsignado, 0) AS DECIMAL(18,4)))
    FROM dbo.PresupuestoVendedor p WITH (NOLOCK)
    LEFT JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = p.ProductoCodigo
    LEFT JOIN VendedorNombre vn
        ON vn.VendedorId = p.VendedorId
    WHERE p.Anio = @Anio
      AND p.Mes = @Mes
      AND (@Master = '' OR a.U_MASTER = @Master)
      AND (@Sku = '' OR p.ProductoCodigo = @Sku)
    GROUP BY p.VendedorId, vn.Nombre
)
SELECT
    VendedorId = COALESCE(v.VendedorId, p.VendedorId),
    Vendedor = COALESCE(NULLIF(v.Vendedor, ''), NULLIF(p.Vendedor, ''), 'SIN VENDEDOR'),
    VentaReal = CAST(ISNULL(v.VentaReal, 0) AS DECIMAL(18,4)),
    PresupuestoMensual = CAST(ISNULL(p.PresupuestoMensual, 0) AS DECIMAL(18,4))
FROM Ventas v
FULL OUTER JOIN Presupuesto p
    ON p.VendedorId = v.VendedorId
WHERE ISNULL(v.VentaReal, 0) <> 0
   OR ISNULL(p.PresupuestoMensual, 0) <> 0
OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);
            var rows = (await con.QueryAsync<VendedorSqlRow>(new CommandDefinition(
                sql,
                new { Inicio = inicio, FinExclusivo = finExclusivo, Anio = anio, Mes = mes, Master = master, Sku = sku },
                commandTimeout: 60,
                cancellationToken: ct))).ToList();

            var result = rows.Select(x =>
            {
                var alcance = x.PresupuestoMensual * factorAlcance;
                var referencia = modo == "alcance" ? alcance : x.PresupuestoMensual;
                return new DashboardVendedorItemVm
                {
                    VendedorId = x.VendedorId,
                    Vendedor = string.IsNullOrWhiteSpace(x.Vendedor) ? "SIN VENDEDOR" : x.Vendedor,
                    VentaReal = Redondear(x.VentaReal),
                    PresupuestoMensual = Redondear(x.PresupuestoMensual),
                    Alcance = Redondear(alcance),
                    Referencia = Redondear(referencia),
                    CumplimientoPct = referencia > 0 ? Redondear(x.VentaReal / referencia * 100m) : 0m
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
        // Fuente de precio:
        // - dbo.OrdenVentaProducto.Precio
        //
        // Volumen para ponderar el precio:
        // - dbo.OrdenVentaProducto.Peso
        //
        // Fecha comercial:
        // - dbo.OrdenVenta.FechaEntrega
        //
        // Este panel NO depende de Subpedido/Surtido para evitar que
        // quede vacío cuando un surtido histórico no tiene relación OV.
        // ============================================================
        [HttpGet("DashboardVentasPrecios")]
        public async Task<IActionResult> Precios(
            int anio,
            int mes,
            int dia,
            string master = "",
            string sku = "",
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

            master = (master ?? "").Trim();
            sku = (sku ?? "").Trim().ToUpperInvariant();

            const string sql = @"
SELECT
    VendedorId = ISNULL(o.VendedorId, 0),

    Vendedor = COALESCE(
        NULLIF(LTRIM(RTRIM(o.Vendedor)), ''),
        'SIN VENDEDOR'
    ),

    PrecioPonderado = CAST(
        SUM(
            CAST(ISNULL(op.Peso, 0) AS DECIMAL(18,4))
            * CAST(ISNULL(op.Precio, 0) AS DECIMAL(18,4))
        )
        /
        NULLIF(
            SUM(CAST(ISNULL(op.Peso, 0) AS DECIMAL(18,4))),
            0
        )
        AS DECIMAL(18,4)
    ),

    Kilos = CAST(
        SUM(CAST(ISNULL(op.Peso, 0) AS DECIMAL(18,4)))
        AS DECIMAL(18,4)
    )

FROM dbo.OrdenVenta o WITH (NOLOCK)
INNER JOIN dbo.OrdenVentaProducto op WITH (NOLOCK)
    ON op.PedidoId = o.Id
LEFT JOIN dbo.ArticuloSap a WITH (NOLOCK)
    ON a.ProductoCodigo = op.ProductoCodigo

WHERE o.FechaEntrega >= @Inicio
  AND o.FechaEntrega <  @FinExclusivo
  AND ISNULL(o.Estatus, 0) <> 0
  AND (op.Eliminado IS NULL OR op.Eliminado = 0)
  AND ISNULL(op.Precio, 0) > 0
  AND ISNULL(op.Peso, 0) > 0
  AND NULLIF(LTRIM(RTRIM(ISNULL(op.ProductoCodigo, ''))), '') IS NOT NULL
  AND (
        @Master = ''
        OR a.U_MASTER = @Master
      )
  AND (
        @Sku = ''
        OR op.ProductoCodigo = @Sku
      )

GROUP BY
    ISNULL(o.VendedorId, 0),
    COALESCE(
        NULLIF(LTRIM(RTRIM(o.Vendedor)), ''),
        'SIN VENDEDOR'
    )

HAVING SUM(CAST(ISNULL(op.Peso, 0) AS DECIMAL(18,4))) > 0
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
                            Master = master,
                            Sku = sku
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
        // Venta diaria real = SurtidoDetalle.Kg por FechaValidacion.
        // ============================================================
        [HttpGet("DashboardVentasTendencia")]
        public async Task<IActionResult> Tendencia(
            int anio,
            int mes,
            int dia,
            string master = "",
            string sku = "",
            CancellationToken ct = default)
        {
            if (!TryPeriodo(
                    anio,
                    mes,
                    dia,
                    out var inicio,
                    out var fechaCorte,
                    out _,
                    out var error))
            {
                return BadRequest(new { error });
            }

            master = (master ?? "").Trim();
            sku = (sku ?? "").Trim();

            var finMes = inicio.AddMonths(1).AddDays(-1);
            var laborables = FechasLaborables(inicio, finMes).ToList();

            const string sql = @"
/* VENTA REAL DIARIA SURTIDA */
SELECT
    Fecha = CAST(se.FechaValidacion AS date),
    Kilos = CAST(
        SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
        AS DECIMAL(18,4))
FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
    ON a.ProductoCodigo = sd.Articulo
WHERE se.FechaValidacion >= @Inicio
  AND se.FechaValidacion <  @FinMesExclusivo
  AND (
        @Master = ''
        OR a.U_MASTER = @Master
      )
  AND (
        @Sku = ''
        OR sd.Articulo = @Sku
      )
GROUP BY CAST(se.FechaValidacion AS date)
ORDER BY Fecha;

/* PRESUPUESTO PARA LA CURVA DE ALCANCE */
SELECT
    PresupuestoMensual = CAST(
        ISNULL(SUM(CAST(ISNULL(p.PresupuestoAsignado, 0) AS DECIMAL(18,4))), 0)
        AS DECIMAL(18,4))
FROM dbo.PresupuestoVendedor p WITH (NOLOCK)
LEFT JOIN dbo.ArticuloSap a WITH (NOLOCK)
    ON a.ProductoCodigo = p.ProductoCodigo
WHERE p.Anio = @Anio
  AND p.Mes = @Mes
  AND (
        @Master = ''
        OR a.U_MASTER = @Master
      )
  AND (
        @Sku = ''
        OR p.ProductoCodigo = @Sku
      );";

            await using var con = await AbrirConexionAsync(ct);
            using var multi = await con.QueryMultipleAsync(new CommandDefinition(
                sql,
                new
                {
                    Inicio = inicio,
                    FinMesExclusivo = inicio.AddMonths(1),
                    Anio = anio,
                    Mes = mes,
                    Master = master,
                    Sku = sku
                },
                commandTimeout: 60,
                cancellationToken: ct));

            var ventasDiarias = (await multi.ReadAsync<VentaDiaSqlRow>()).ToList();
            var presupuesto = await multi.ReadSingleAsync<ResumenPresupuestoSql>();

            var ventaPorFecha = ventasDiarias
                .GroupBy(x => x.Fecha.Date)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(x => x.Kilos));

            var result = new List<DashboardTendenciaItemVm>();
            decimal acumulado = 0m;
            var cursor = inicio;
            var indiceLaboral = 0;

            while (cursor <= finMes)
            {
                if (ventaPorFecha.TryGetValue(cursor.Date, out var kgDia))
                    acumulado += kgDia;

                if (EsDiaLaboral(cursor))
                {
                    indiceLaboral++;

                    var alcance = laborables.Count > 0
                        ? presupuesto.PresupuestoMensual * indiceLaboral / laborables.Count
                        : 0m;

                    decimal? real = cursor <= fechaCorte
                        ? Redondear(acumulado)
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
                PresupuestoMensual = Redondear(presupuesto.PresupuestoMensual),
                DiasLaborablesMes = laborables.Count,
                Items = result
            });
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

        private static string NormalizarComparacion(string? value)
        {
            var x = (value ?? "").Trim().ToLowerInvariant();
            return x is "alcance" or "reach"
                ? "alcance"
                : "presupuesto";
        }

        private static decimal Redondear(decimal value)
            => Math.Round(value, 2, MidpointRounding.AwayFromZero);

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

        private sealed class VendedorSqlRow
        {
            public int VendedorId { get; set; }
            public string Vendedor { get; set; } = "";
            public decimal VentaReal { get; set; }
            public decimal PresupuestoMensual { get; set; }
        }

        private sealed class VentaDiaSqlRow
        {
            public DateTime Fecha { get; set; }
            public decimal Kilos { get; set; }
        }
    }
}
