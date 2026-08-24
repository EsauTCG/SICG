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
  AND TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')) IN (1, 2)
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
  AND TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')) IN (1, 2)
ORDER BY Master, Sku;

/* 5) VENDEDORES / SUCURSALES
   ============================================================
   Sólo se muestran opciones que realmente tengan información:
   - Vendedor normal: ventas reales o presupuesto.
   - CEDIS: ventas reales o presupuesto en PresupuestoCedis.
   ============================================================ */
;WITH Base AS
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
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)
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
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)
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
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)
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
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)
),

Catalogo AS
(
    /* ========================================================
       VENDEDORES NORMALES
       Se excluyen los que no tengan venta ni presupuesto.
       ======================================================== */
    SELECT
        Id = CONCAT('VENDEDOR|', b.VendedorId),

        Nombre = COALESCE(
            NULLIF(MAX(b.VendedorNombre), ''),
            CONCAT('VENDEDOR ', b.VendedorId)
        )

    FROM Base b

    WHERE b.EsCedis = 0

      /* Si el mismo VendedorId está asociado a un CEDIS,
         no duplicarlo como vendedor normal. */
      AND NOT EXISTS
      (
          SELECT 1
          FROM Base bx
          WHERE bx.VendedorId = b.VendedorId
            AND bx.EsCedis = 1
      )

      /* Debe tener venta o presupuesto */
      AND
      (
          EXISTS
          (
              SELECT 1
              FROM VendedoresConVenta vv
              WHERE vv.VendedorId = b.VendedorId
          )
          OR EXISTS
          (
              SELECT 1
              FROM VendedoresConPresupuesto vp
              WHERE vp.VendedorId = b.VendedorId
          )
      )

    GROUP BY b.VendedorId

    UNION ALL

    /* ========================================================
       CEDIS
       Se agrupa por U_CANAL y sólo aparece si tiene venta
       real o presupuesto en PresupuestoCedis.
       ======================================================== */
    SELECT
        Id = CONCAT('CEDIS|', b.Canal),
        Nombre = b.Canal

    FROM Base b

    WHERE b.EsCedis = 1
      AND b.Canal <> ''

      AND
      (
          EXISTS
          (
              SELECT 1
              FROM CedisConVenta cv
              WHERE cv.Canal = b.Canal
          )
          OR EXISTS
          (
              SELECT 1
              FROM CedisConPresupuesto cp
              WHERE cp.Canal = b.Canal
          )
      )

    GROUP BY b.Canal
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
        // Venta Real = KG validados en SurtidoDetalle
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

            const string sql = @"
/* VENTA REAL FILTRADA */
SELECT
    VentaReal = CAST(
        ISNULL(
            SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4))),
            0
        )
        AS DECIMAL(18,4)
    ),
    UltimaFechaVenta = MAX(CAST(se.FechaValidacion AS date))
FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
LEFT JOIN dbo.ArticuloSap a WITH (NOLOCK)
    ON a.ProductoCodigo = sd.Articulo
LEFT JOIN dbo.ClienteSap cs WITH (NOLOCK)
    ON cs.Cliente = se.CodigoSap
WHERE se.FechaValidacion >= @Inicio
  AND se.FechaValidacion <  @FinExclusivo
  AND TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')) IN (1, 2)
  AND (
        @TieneMaster = 0
        OR COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        ) IN @Masters
      )
  AND (
        @TieneSku = 0
        OR UPPER(LTRIM(RTRIM(ISNULL(sd.Articulo, '')))) IN @Skus
      )
  AND (
        @TieneVendedor = 0
        OR UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) IN @CanalesCedis
        OR (
            cs.VendedorId IN @VendedorIds
            AND UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) NOT LIKE 'CEDIS%'
        )
      );

/* PRESUPUESTO FILTRADO */
SELECT
    PresupuestoMensual = CAST(
        ISNULL(
            SUM(CAST(ISNULL(p.PresupuestoAsignado, 0) AS DECIMAL(18,4))),
            0
        )
        AS DECIMAL(18,4)
    )
FROM dbo.PresupuestoVendedor p WITH (NOLOCK)
LEFT JOIN dbo.ArticuloSap a WITH (NOLOCK)
    ON a.ProductoCodigo = p.ProductoCodigo
WHERE p.Anio = @Anio
  AND p.Mes = @Mes
  AND TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')) IN (1, 2)
  AND (
        @TieneMaster = 0
        OR COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        ) IN @Masters
      )
  AND (
        @TieneSku = 0
        OR UPPER(LTRIM(RTRIM(ISNULL(p.ProductoCodigo, '')))) IN @Skus
      )
  AND (
        @TieneVendedor = 0
        OR p.VendedorId IN @VendedorIds
        OR EXISTS
        (
            SELECT 1
            FROM dbo.ClienteSap cv WITH (NOLOCK)
            WHERE cv.VendedorId = p.VendedorId
              AND UPPER(LTRIM(RTRIM(ISNULL(cv.U_CANAL, '')))) IN @CanalesCedis
        )
      )
OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);

            using var multi = await con.QueryMultipleAsync(
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
                        CanalesCedis = filtros.CanalesCedisSql
                    },
                    commandTimeout: 60,
                    cancellationToken: ct));

            var venta = await multi.ReadSingleAsync<ResumenVentaSql>();
            var presupuesto = await multi.ReadSingleAsync<ResumenPresupuestoSql>();

            var alcance = Redondear(
                presupuesto.PresupuestoMensual * factorAlcance);

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

            const string sql = @"
;WITH Ventas AS
(
    /* ============================================================
       VENTA REAL POR MÁSTER

       CEDIS:
       - Se filtra por ClienteSap.U_CANAL.
       - Ejemplo: CEDIS-MXL.

       VENDEDOR NORMAL:
       - Se filtra por ClienteSap.VendedorId.

       Únicamente Tipo SKU:
       1 = PRIMARIO
       2 = SECUNDARIO
       ============================================================ */
    SELECT
        Master = COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        ),

        VentaReal = SUM(
            CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4))
        )

    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)

    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId

    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = sd.Articulo

    LEFT JOIN dbo.ClienteSap cs WITH (NOLOCK)
        ON cs.Cliente = se.CodigoSap

    WHERE se.FechaValidacion >= @Inicio
      AND se.FechaValidacion <  @FinExclusivo

      /* SOLO PRIMARIO / SECUNDARIO */
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)

      AND (
            @TieneMaster = 0
            OR COALESCE(
                NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
                'SIN_MASTER'
            ) IN @Masters
          )

      AND (
            @TieneSku = 0
            OR UPPER(LTRIM(RTRIM(ISNULL(sd.Articulo, '')))) IN @Skus
          )

      AND (
            @TieneVendedor = 0

            /* CEDIS */
            OR UPPER(
                LTRIM(
                    RTRIM(
                        ISNULL(cs.U_CANAL, '')
                    )
                )
            ) IN @CanalesCedis

            /* VENDEDOR NORMAL */
            OR (
                cs.VendedorId IN @VendedorIds
                AND UPPER(
                    LTRIM(
                        RTRIM(
                            ISNULL(cs.U_CANAL, '')
                        )
                    )
                ) NOT LIKE 'CEDIS%'
            )
          )

    GROUP BY
        COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        )
),

PresupuestoVendedorBase AS
(
    /* ============================================================
       PRESUPUESTO DE VENDEDORES NORMALES
       Fuente: dbo.PresupuestoVendedor

       No se usan aquí los vendedores que estén ligados a CEDIS,
       ya que el presupuesto del CEDIS sale de PresupuestoCedis.
       ============================================================ */
    SELECT
        Master = COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        ),

        Presupuesto = CAST(
            ISNULL(p.PresupuestoAsignado, 0)
            AS DECIMAL(18,4)
        )

    FROM dbo.PresupuestoVendedor p WITH (NOLOCK)

    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = p.ProductoCodigo

    WHERE p.Anio = @Anio
      AND p.Mes = @Mes

      /* SOLO PRIMARIO / SECUNDARIO */
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)

      /* Evitar tomar como vendedor normal un presupuesto de CEDIS */
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ClienteSap cx WITH (NOLOCK)
          WHERE cx.VendedorId = p.VendedorId
            AND UPPER(
                LTRIM(
                    RTRIM(
                        ISNULL(cx.U_CANAL, '')
                    )
                )
            ) LIKE 'CEDIS%'
      )

      AND (
            @TieneMaster = 0
            OR COALESCE(
                NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
                'SIN_MASTER'
            ) IN @Masters
          )

      AND (
            @TieneSku = 0
            OR UPPER(
                LTRIM(
                    RTRIM(
                        ISNULL(p.ProductoCodigo, '')
                    )
                )
            ) IN @Skus
          )

      AND (
            @TieneVendedor = 0
            OR p.VendedorId IN @VendedorIds
          )
),

PresupuestoCedisBase AS
(
    /* ============================================================
       PRESUPUESTO DE CEDIS
       Fuente: dbo.PresupuestoCedis

       La llave es:
           PresupuestoCedis.Canal
                   =
           ClienteSap.U_CANAL

       Ejemplo:
           CEDIS-MXL = CEDIS-MXL

       El Máster se toma desde ArticuloSap según ProductoCodigo,
       para no depender de que PresupuestoCedis.Master esté actualizado.
       ============================================================ */
    SELECT
        Master = COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        ),

        Presupuesto = CAST(
            ISNULL(pc.PresupuestoAsignado, 0)
            AS DECIMAL(18,4)
        )

    FROM dbo.PresupuestoCedis pc WITH (NOLOCK)

    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = pc.ProductoCodigo

    WHERE pc.Anio = @Anio
      AND pc.Mes = @Mes

      AND NULLIF(
            LTRIM(
                RTRIM(
                    ISNULL(pc.Canal, '')
                )
            ),
            ''
          ) IS NOT NULL

      /* SOLO PRIMARIO / SECUNDARIO */
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)

      AND (
            @TieneMaster = 0
            OR COALESCE(
                NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
                'SIN_MASTER'
            ) IN @Masters
          )

      AND (
            @TieneSku = 0
            OR UPPER(
                LTRIM(
                    RTRIM(
                        ISNULL(pc.ProductoCodigo, '')
                    )
                )
            ) IN @Skus
          )

      AND (
            @TieneVendedor = 0
            OR UPPER(
                LTRIM(
                    RTRIM(
                        pc.Canal
                    )
                )
            ) IN @CanalesCedis
          )
),

PresupuestoBase AS
(
    SELECT
        Master,
        Presupuesto
    FROM PresupuestoVendedorBase

    UNION ALL

    SELECT
        Master,
        Presupuesto
    FROM PresupuestoCedisBase
),

Presupuesto AS
(
    SELECT
        Master,
        PresupuestoMensual = SUM(Presupuesto)
    FROM PresupuestoBase
    GROUP BY Master
)

SELECT
    Master =
        COALESCE(
            v.Master,
            p.Master
        ),

    VentaReal =
        CAST(
            ISNULL(v.VentaReal, 0)
            AS DECIMAL(18,4)
        ),

    PresupuestoMensual =
        CAST(
            ISNULL(p.PresupuestoMensual, 0)
            AS DECIMAL(18,4)
        )

FROM Ventas v

FULL OUTER JOIN Presupuesto p
    ON p.Master = v.Master

WHERE
       ISNULL(v.VentaReal, 0) <> 0
    OR ISNULL(p.PresupuestoMensual, 0) <> 0

OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);

            var rows = (
                await con.QueryAsync<MasterSqlRow>(
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
                            CanalesCedis = filtros.CanalesCedisSql
                        },
                        commandTimeout: 60,
                        cancellationToken: ct
                    )
                )
            ).ToList();

            var temp = rows
                .Select(x =>
                {
                    var alcance =
                        x.PresupuestoMensual * factorAlcance;

                    var referencia =
                        modo == "alcance"
                            ? alcance
                            : x.PresupuestoMensual;

                    return new DashboardMasterItemVm
                    {
                        Master =
                            string.IsNullOrWhiteSpace(x.Master)
                                ? "SIN_MASTER"
                                : x.Master,

                        VentaReal =
                            Redondear(x.VentaReal),

                        PresupuestoMensual =
                            Redondear(x.PresupuestoMensual),

                        Alcance =
                            Redondear(alcance),

                        Referencia =
                            Redondear(referencia),

                        AvancePct =
                            referencia > 0
                                ? Redondear(
                                    x.VentaReal /
                                    referencia *
                                    100m)
                                : 0m
                    };
                })
                .ToList();

            var totalReferencia =
                temp.Sum(x => x.Referencia);

            foreach (var item in temp)
            {
                item.ParticipacionPct =
                    totalReferencia > 0
                        ? Redondear(
                            item.Referencia /
                            totalReferencia *
                            100m)
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
        //
        // CEDIS:
        // - Se identifica por ClienteSap.U_CANAL LIKE 'CEDIS%'.
        // - Se agrupa por U_CANAL.
        // - Se muestra U_CANAL como nombre.
        //
        // VENDEDOR NORMAL:
        // - Se agrupa por VendedorId.
        // - Se muestra VendedorNombre.
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

            const string sql = @"
;WITH VentasBase AS
(
    /* ============================================================
       VENTA REAL
       - CEDIS: se identifica y agrupa por ClienteSap.U_CANAL.
       - Vendedor normal: se agrupa por ClienteSap.VendedorId.
       - Sólo SKU Tipo 1=PRIMARIO y 2=SECUNDARIO.
       ============================================================ */
    SELECT
        Grupo =
            CASE
                WHEN UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) LIKE 'CEDIS%'
                    THEN 'CEDIS|' + UPPER(LTRIM(RTRIM(cs.U_CANAL)))
                ELSE 'VENDEDOR|' + CONVERT(VARCHAR(20), ISNULL(cs.VendedorId, 0))
            END,

        VendedorId =
            CASE
                WHEN UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) LIKE 'CEDIS%'
                    THEN 0
                ELSE ISNULL(cs.VendedorId, 0)
            END,

        Vendedor =
            CASE
                WHEN UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) LIKE 'CEDIS%'
                    THEN UPPER(LTRIM(RTRIM(cs.U_CANAL)))
                ELSE COALESCE(
                    NULLIF(LTRIM(RTRIM(cs.VendedorNombre)), ''),
                    CASE
                        WHEN ISNULL(cs.VendedorId, 0) = 0
                            THEN 'SIN VENDEDOR'
                        ELSE CONCAT('VENDEDOR ', ISNULL(cs.VendedorId, 0))
                    END
                )
            END,

        Kg = CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4))

    FROM dbo.SurtidoEncabezado se WITH (NOLOCK)

    INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId

    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = sd.Articulo

    LEFT JOIN dbo.ClienteSap cs WITH (NOLOCK)
        ON cs.Cliente = se.CodigoSap

    WHERE se.FechaValidacion >= @Inicio
      AND se.FechaValidacion <  @FinExclusivo

      /* ÚNICAMENTE PRIMARIO Y SECUNDARIO */
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)

      AND (
            @TieneMaster = 0
            OR COALESCE(
                NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
                'SIN_MASTER'
            ) IN @Masters
          )

      AND (
            @TieneSku = 0
            OR UPPER(LTRIM(RTRIM(ISNULL(sd.Articulo, '')))) IN @Skus
          )

      AND (
            @TieneVendedor = 0

            /* CEDIS seleccionado */
            OR UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) IN @CanalesCedis

            /* VENDEDOR NORMAL seleccionado */
            OR (
                cs.VendedorId IN @VendedorIds
                AND UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) NOT LIKE 'CEDIS%'
            )
          )
),

Ventas AS
(
    SELECT
        Grupo,
        VendedorId = MAX(VendedorId),
        Vendedor = MAX(Vendedor),
        VentaReal = SUM(Kg)
    FROM VentasBase
    GROUP BY Grupo
),

PresupuestoVendedorBase AS
(
    /* ============================================================
       PRESUPUESTO DE VENDEDORES NORMALES
       Fuente: dbo.PresupuestoVendedor

       Importante:
       Si el VendedorId pertenece a clientes CEDIS, NO se usa aquí.
       El CEDIS se presupuestará exclusivamente desde PresupuestoCedis.
       ============================================================ */
    SELECT
        Grupo =
            'VENDEDOR|' + CONVERT(VARCHAR(20), p.VendedorId),

        VendedorId = p.VendedorId,

        Vendedor = COALESCE(
            NULLIF(
                (
                    SELECT TOP (1)
                        LTRIM(RTRIM(cv.VendedorNombre))
                    FROM dbo.ClienteSap cv WITH (NOLOCK)
                    WHERE cv.VendedorId = p.VendedorId
                      AND UPPER(LTRIM(RTRIM(ISNULL(cv.U_CANAL, '')))) NOT LIKE 'CEDIS%'
                      AND NULLIF(LTRIM(RTRIM(ISNULL(cv.VendedorNombre, ''))), '') IS NOT NULL
                    ORDER BY cv.Cliente
                ),
                ''
            ),
            CONCAT('VENDEDOR ', p.VendedorId)
        ),

        Presupuesto = CAST(
            ISNULL(p.PresupuestoAsignado, 0)
            AS DECIMAL(18,4)
        )

    FROM dbo.PresupuestoVendedor p WITH (NOLOCK)

    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = p.ProductoCodigo

    WHERE p.Anio = @Anio
      AND p.Mes = @Mes

      /* ÚNICAMENTE PRIMARIO Y SECUNDARIO */
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)

      /* No mezclar presupuesto de vendedor con presupuesto CEDIS */
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ClienteSap cx WITH (NOLOCK)
          WHERE cx.VendedorId = p.VendedorId
            AND UPPER(LTRIM(RTRIM(ISNULL(cx.U_CANAL, '')))) LIKE 'CEDIS%'
      )

      AND (
            @TieneMaster = 0
            OR COALESCE(
                NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
                'SIN_MASTER'
            ) IN @Masters
          )

      AND (
            @TieneSku = 0
            OR UPPER(LTRIM(RTRIM(ISNULL(p.ProductoCodigo, '')))) IN @Skus
          )

      AND (
            @TieneVendedor = 0
            OR p.VendedorId IN @VendedorIds
          )
),

PresupuestoCedisBase AS
(
    /* ============================================================
       PRESUPUESTO DE CEDIS
       Fuente correcta: dbo.PresupuestoCedis
       Relación: PresupuestoCedis.Canal = ClienteSap.U_CANAL
       ============================================================ */
    SELECT
        Grupo =
            'CEDIS|' + UPPER(LTRIM(RTRIM(pc.Canal))),

        VendedorId = 0,

        Vendedor =
            UPPER(LTRIM(RTRIM(pc.Canal))),

        Presupuesto = CAST(
            ISNULL(pc.PresupuestoAsignado, 0)
            AS DECIMAL(18,4)
        )

    FROM dbo.PresupuestoCedis pc WITH (NOLOCK)

    INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
        ON a.ProductoCodigo = pc.ProductoCodigo

    WHERE pc.Anio = @Anio
      AND pc.Mes = @Mes

      AND NULLIF(
            LTRIM(RTRIM(ISNULL(pc.Canal, ''))),
            ''
          ) IS NOT NULL

      /* ÚNICAMENTE PRIMARIO Y SECUNDARIO */
      AND TRY_CONVERT(
            int,
            NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')
          ) IN (1, 2)

      AND (
            @TieneMaster = 0
            OR COALESCE(
                NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
                'SIN_MASTER'
            ) IN @Masters
          )

      AND (
            @TieneSku = 0
            OR UPPER(LTRIM(RTRIM(ISNULL(pc.ProductoCodigo, '')))) IN @Skus
          )

      AND (
            @TieneVendedor = 0
            OR UPPER(LTRIM(RTRIM(pc.Canal))) IN @CanalesCedis
          )
),

PresupuestoBase AS
(
    SELECT
        Grupo,
        VendedorId,
        Vendedor,
        Presupuesto
    FROM PresupuestoVendedorBase

    UNION ALL

    SELECT
        Grupo,
        VendedorId,
        Vendedor,
        Presupuesto
    FROM PresupuestoCedisBase
),

Presupuesto AS
(
    SELECT
        Grupo,
        VendedorId = MAX(VendedorId),
        Vendedor = MAX(Vendedor),
        PresupuestoMensual = SUM(Presupuesto)
    FROM PresupuestoBase
    GROUP BY Grupo
)

SELECT
    VendedorId =
        COALESCE(v.VendedorId, p.VendedorId, 0),

    Vendedor =
        COALESCE(
            NULLIF(v.Vendedor, ''),
            NULLIF(p.Vendedor, ''),
            'SIN VENDEDOR'
        ),

    VentaReal =
        CAST(
            ISNULL(v.VentaReal, 0)
            AS DECIMAL(18,4)
        ),

    PresupuestoMensual =
        CAST(
            ISNULL(p.PresupuestoMensual, 0)
            AS DECIMAL(18,4)
        )

FROM Ventas v

FULL OUTER JOIN Presupuesto p
    ON p.Grupo = v.Grupo

WHERE
       ISNULL(v.VentaReal, 0) <> 0
    OR ISNULL(p.PresupuestoMensual, 0) <> 0

ORDER BY
    VentaReal DESC,
    Vendedor

OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);

            var rows = (
                await con.QueryAsync<VendedorSqlRow>(
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
                            CanalesCedis = filtros.CanalesCedisSql
                        },
                        commandTimeout: 60,
                        cancellationToken: ct
                    )
                )
            ).ToList();

            var result = rows
                .Select(x =>
                {
                    var alcance =
                        x.PresupuestoMensual * factorAlcance;

                    var referencia =
                        modo == "alcance"
                            ? alcance
                            : x.PresupuestoMensual;

                    return new DashboardVendedorItemVm
                    {
                        VendedorId = x.VendedorId,

                        Vendedor =
                            string.IsNullOrWhiteSpace(x.Vendedor)
                                ? "SIN VENDEDOR"
                                : x.Vendedor,

                        VentaReal =
                            Redondear(x.VentaReal),

                        PresupuestoMensual =
                            Redondear(x.PresupuestoMensual),

                        Alcance =
                            Redondear(alcance),

                        Referencia =
                            Redondear(referencia),

                        CumplimientoPct =
                            referencia > 0
                                ? Redondear(
                                    x.VentaReal /
                                    referencia *
                                    100m)
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
;WITH PrecioBase AS
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
      AND TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')) IN (1, 2)
      AND (
            @TieneMaster = 0
            OR COALESCE(
                NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
                'SIN_MASTER'
            ) IN @Masters
          )
      AND (
            @TieneSku = 0
            OR UPPER(LTRIM(RTRIM(ISNULL(op.ProductoCodigo, '')))) IN @Skus
          )
      AND (
            @TieneVendedor = 0
            OR UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) IN @CanalesCedis
            OR (
                COALESCE(
                    NULLIF(o.VendedorId, 0),
                    cs.VendedorId,
                    0
                ) IN @VendedorIds
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
                            CanalesCedis = filtros.CanalesCedisSql
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
            string vendedorId = "",
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

            var filtros = PrepararFiltrosDashboard(master, sku, vendedorId);

            var finMes = inicio.AddMonths(1).AddDays(-1);
            var laborables = FechasLaborables(inicio, finMes).ToList();

            const string sql = @"
/* VENTA REAL DIARIA FILTRADA */
SELECT
    Fecha = CAST(se.FechaValidacion AS date),
    Kilos = CAST(
        SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
        AS DECIMAL(18,4)
    )
FROM dbo.SurtidoEncabezado se WITH (NOLOCK)
INNER JOIN dbo.SurtidoDetalle sd WITH (NOLOCK)
    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
INNER JOIN dbo.ArticuloSap a WITH (NOLOCK)
    ON a.ProductoCodigo = sd.Articulo
LEFT JOIN dbo.ClienteSap cs WITH (NOLOCK)
    ON cs.Cliente = se.CodigoSap
WHERE se.FechaValidacion >= @Inicio
  AND se.FechaValidacion <  @FinMesExclusivo
  AND TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')) IN (1, 2)
  AND (
        @TieneMaster = 0
        OR COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        ) IN @Masters
      )
  AND (
        @TieneSku = 0
        OR UPPER(LTRIM(RTRIM(ISNULL(sd.Articulo, '')))) IN @Skus
      )
  AND (
        @TieneVendedor = 0
        OR UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) IN @CanalesCedis
        OR (
            cs.VendedorId IN @VendedorIds
            AND UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) NOT LIKE 'CEDIS%'
        )
      )
GROUP BY CAST(se.FechaValidacion AS date)
ORDER BY Fecha;

/* PRESUPUESTO PARA CURVA DE ALCANCE */
SELECT
    PresupuestoMensual = CAST(
        ISNULL(
            SUM(CAST(ISNULL(p.PresupuestoAsignado, 0) AS DECIMAL(18,4))),
            0
        )
        AS DECIMAL(18,4)
    )
FROM dbo.PresupuestoVendedor p WITH (NOLOCK)
LEFT JOIN dbo.ArticuloSap a WITH (NOLOCK)
    ON a.ProductoCodigo = p.ProductoCodigo
WHERE p.Anio = @Anio
  AND p.Mes = @Mes
  AND TRY_CONVERT(int, NULLIF(LTRIM(RTRIM(a.U_TipoporSKU)), '')) IN (1, 2)
  AND (
        @TieneMaster = 0
        OR COALESCE(
            NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), ''),
            'SIN_MASTER'
        ) IN @Masters
      )
  AND (
        @TieneSku = 0
        OR UPPER(LTRIM(RTRIM(ISNULL(p.ProductoCodigo, '')))) IN @Skus
      )
  AND (
        @TieneVendedor = 0
        OR p.VendedorId IN @VendedorIds
        OR EXISTS
        (
            SELECT 1
            FROM dbo.ClienteSap cv WITH (NOLOCK)
            WHERE cv.VendedorId = p.VendedorId
              AND UPPER(LTRIM(RTRIM(ISNULL(cv.U_CANAL, '')))) IN @CanalesCedis
        )
      )
OPTION (RECOMPILE);";

            await using var con = await AbrirConexionAsync(ct);

            using var multi = await con.QueryMultipleAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Inicio = inicio,
                        FinMesExclusivo = inicio.AddMonths(1),
                        Anio = anio,
                        Mes = mes,
                        filtros.TieneMaster,
                        filtros.TieneSku,
                        filtros.TieneVendedor,
                        Masters = filtros.MastersSql,
                        Skus = filtros.SkusSql,
                        VendedorIds = filtros.VendedorIdsSql,
                        CanalesCedis = filtros.CanalesCedisSql
                    },
                    commandTimeout: 60,
                    cancellationToken: ct));

            var ventasDiarias = (
                await multi.ReadAsync<VentaDiaSqlRow>()
            ).ToList();

            var presupuesto =
                await multi.ReadSingleAsync<ResumenPresupuestoSql>();

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
                        ? presupuesto.PresupuestoMensual
                            * indiceLaboral
                            / laborables.Count
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

        private sealed class VentaDiaSqlRow
        {
            public DateTime Fecha { get; set; }
            public decimal Kilos { get; set; }
        }
    }
}
