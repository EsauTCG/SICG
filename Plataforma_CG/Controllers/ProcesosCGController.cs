using ClosedXML.Excel;
using Dapper;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plataforma_CG.Data;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using Plataforma_CG.Services;
using Plataforma_CG.ViewModels;
using QRCoder;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using RVM = Plataforma_CG.ViewModels;

namespace Plataforma_CG.Controllers
{
    [Route("ProcesosCG")]
    public class ProcesosCgController : Controller
    {
        private readonly PrintRestClient _print;
        private readonly ILogger<ProcesosCgController> _logger;

        private const string BASE_URL = "http://10.1.1.2/PrintRestService/PrintRestService.svc";

        private readonly IEntregasSapService _data;
        private readonly ISapServiceLayerClient _sap;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _db;
        private readonly IAutoSapSettingsStore _autoStore;

        public ProcesosCgController(
            PrintRestClient print,
            IEntregasSapService data,
            ISapServiceLayerClient sap,
            IConfiguration configuration,
            AppDbContext db,
            IAutoSapSettingsStore autoStore,
            ILogger<ProcesosCgController> logger)
        {
            _print = print;
            _data = data;
            _sap = sap;
            _configuration = configuration;
            _db = db;
            _logger = logger;
            _autoStore = autoStore;
        }

        // =======================================================
        // SAP SERVICE LAYER SQLQUERIES
        // Reemplazo de consultas directas ODBC/HANA.
        // Estas consultas deben existir en SAP Business One Service Layer.
        // Endpoint de ejecución: SQLQueries('<SqlCode>')/List
        // =======================================================
        private const string SAP_SQL_INV_LOTE_DISPONIBLE = "CG_INV_LOTE_DISPONIBLE";
        private const string SAP_SQL_DEV_APLICADAS_PEDIDO = "CG_DEV_APLICADAS_PEDIDO";
        private const string SAP_SQL_CONCILIACION_INVENTARIO = "CG_CONCILIACION_INVENTARIO";
        private const string SAP_SQL_CONCILIACION_INVENTARIO_P1 = "CG_CONCILIACION_INVENTARIO_P1";
        private const string SAP_SQL_CONCILIACION_INVENTARIO_TIF = "CG_CONCILIACION_INVENTARIO_TIF";

        private sealed class SapSqlQueryDefinitionVM
        {
            public string SqlCode { get; set; } = "";
            public string SqlName { get; set; } = "";
            public string ParamList { get; set; } = "";
            public string SqlText { get; set; } = "";
        }

        [HttpGet("SapSqlQueriesRequeridas")]
        [RevisarPermiso("ENTREGAS_SAP", "LEER")]
        public IActionResult SapSqlQueriesRequeridas()
        {
            return Ok(new
            {
                ok = true,
                msg = "Crea estas SQLQueries en SAP Business One Service Layer para reemplazar la conexión ODBC/HANA.",
                queries = GetSapSqlQueriesRequeridas()
            });
        }

        private static List<SapSqlQueryDefinitionVM> GetSapSqlQueriesRequeridas()
        {
            return new List<SapSqlQueryDefinitionVM>
            {
                new SapSqlQueryDefinitionVM
                {
                    SqlCode = SAP_SQL_INV_LOTE_DISPONIBLE,
                    SqlName = "CG - Inventario lote disponible",
                    ParamList = "itemCode,whsCode,lote",
                    SqlText = @"SELECT
    T0.""ItemCode"" AS ""Articulo"",
    T0.""WhsCode"" AS ""Almacen"",
    T1.""DistNumber"" AS ""Lote"",
    SUM(IFNULL(T0.""Quantity"", 0)) AS ""CantidadSap"",
    SUM(IFNULL(T0.""CommitQty"", 0)) AS ""ComprometidoSap"",
    SUM(IFNULL(T0.""Quantity"", 0) - IFNULL(T0.""CommitQty"", 0)) AS ""DisponibleSap""
FROM OBTQ T0
INNER JOIN OBTN T1
    ON T0.""ItemCode"" = T1.""ItemCode""
   AND T0.""SysNumber"" = T1.""SysNumber""
WHERE
    T0.""ItemCode"" = :itemCode
    AND T0.""WhsCode"" = :whsCode
    AND T1.""DistNumber"" = :lote
GROUP BY
    T0.""ItemCode"",
    T0.""WhsCode"",
    T1.""DistNumber"""
                },
                new SapSqlQueryDefinitionVM
                {
                    SqlCode = SAP_SQL_DEV_APLICADAS_PEDIDO,
                    SqlName = "CG - Devoluciones aplicadas por pedido",
                    ParamList = "pedidoSap",
                    SqlText = @"WITH P AS (
    SELECT :pedidoSap AS ""PedidoSap"" FROM DUMMY
),
Entrega AS (
    SELECT
        T0.""DocEntry"",
        T0.""DocNum"",
        T0.""DocDate"",
        T0.""CardCode"",
        T0.""CardName"",
        T0.""NumAtCard""
    FROM ODLN T0, P
    WHERE
        T0.""NumAtCard"" = P.""PedidoSap""
        OR T0.""Comments"" LIKE '%' || P.""PedidoSap"" || '%'
),
Factura AS (
    SELECT
        T0.""DocEntry"",
        T0.""DocNum"",
        T0.""DocDate"",
        T0.""CardCode"",
        T0.""CardName"",
        T0.""NumAtCard""
    FROM OINV T0, P
    WHERE
        T0.""NumAtCard"" = P.""PedidoSap""
        OR T0.""Comments"" LIKE '%' || P.""PedidoSap"" || '%'
),
DetalleSap AS (
    SELECT
        'ORDN/RDN1' AS ""TipoAplicacion"",
        E.""NumAtCard"" AS ""PedidoSap"",
        E.""DocNum"" AS ""DocNumOriginal"",
        E.""DocEntry"" AS ""DocEntryOriginal"",
        D0.""DocEntry"" AS ""DocEntryDevolucion"",
        D0.""DocNum"" AS ""DocNumDevolucion"",
        D0.""DocDate"" AS ""FechaDevolucion"",
        D1.""ItemCode"",
        BTN.""DistNumber"" AS ""Lote"",
        ABS(IFNULL(TLD.""Quantity"", D1.""Quantity"")) AS ""CantidadDevuelta""
    FROM Entrega E
    INNER JOIN RDN1 D1
        ON D1.""BaseType"" = 15
        AND D1.""BaseEntry"" = E.""DocEntry""
    INNER JOIN ORDN D0
        ON D0.""DocEntry"" = D1.""DocEntry""
    LEFT JOIN OITL TL
        ON TL.""DocType"" = 16
        AND TL.""DocEntry"" = D0.""DocEntry""
        AND TL.""DocLine"" = D1.""LineNum""
        AND TL.""ItemCode"" = D1.""ItemCode""
    LEFT JOIN ITL1 TLD
        ON TLD.""LogEntry"" = TL.""LogEntry""
    LEFT JOIN OBTN BTN
        ON BTN.""AbsEntry"" = TLD.""MdAbsEntry""
    WHERE
        D0.""CANCELED"" = 'N'

    UNION ALL

    SELECT
        'ORIN/RIN1' AS ""TipoAplicacion"",
        F.""NumAtCard"" AS ""PedidoSap"",
        F.""DocNum"" AS ""DocNumOriginal"",
        F.""DocEntry"" AS ""DocEntryOriginal"",
        NC0.""DocEntry"" AS ""DocEntryDevolucion"",
        NC0.""DocNum"" AS ""DocNumDevolucion"",
        NC0.""DocDate"" AS ""FechaDevolucion"",
        NC1.""ItemCode"",
        BTN.""DistNumber"" AS ""Lote"",
        ABS(IFNULL(TLD.""Quantity"", NC1.""Quantity"")) AS ""CantidadDevuelta""
    FROM Factura F
    INNER JOIN RIN1 NC1
        ON NC1.""BaseType"" = 13
        AND NC1.""BaseEntry"" = F.""DocEntry""
    INNER JOIN ORIN NC0
        ON NC0.""DocEntry"" = NC1.""DocEntry""
    LEFT JOIN OITL TL
        ON TL.""DocType"" = 14
        AND TL.""DocEntry"" = NC0.""DocEntry""
        AND TL.""DocLine"" = NC1.""LineNum""
        AND TL.""ItemCode"" = NC1.""ItemCode""
    LEFT JOIN ITL1 TLD
        ON TLD.""LogEntry"" = TL.""LogEntry""
    LEFT JOIN OBTN BTN
        ON BTN.""AbsEntry"" = TLD.""MdAbsEntry""
    WHERE
        NC0.""CANCELED"" = 'N'
)
SELECT
    UPPER(TRIM(""PedidoSap"")) AS ""PedidoSap"",
    UPPER(TRIM(""ItemCode"")) AS ""Articulo"",
    UPPER(TRIM(IFNULL(""Lote"", 'SIN_LOTE'))) AS ""Lote"",
    SUM(""CantidadDevuelta"") AS ""KgDevueltosSap"",
    MAX(""DocNumDevolucion"") AS ""DocNumDevolucion"",
    MAX(""DocEntryDevolucion"") AS ""DocEntryDevolucion"",
    MAX(""FechaDevolucion"") AS ""FechaDevolucionSap"",
    STRING_AGG(""TipoAplicacion"", ', ') AS ""TipoAplicacion""
FROM DetalleSap
GROUP BY
    UPPER(TRIM(""PedidoSap"")),
    UPPER(TRIM(""ItemCode"")),
    UPPER(TRIM(IFNULL(""Lote"", 'SIN_LOTE')))
ORDER BY
    ""Articulo"",
    ""Lote"""
                },
                new SapSqlQueryDefinitionVM
                {
                    SqlCode = SAP_SQL_CONCILIACION_INVENTARIO_P1,
                    SqlName = "CG - Conciliacion inventario SAP P1",
                    ParamList = "",
                    SqlText = @"SELECT
    T0.ItemCode AS ProductoCodigo,
    T0.WhsCode AS Sucursal,
    T0.Quantity AS CantidadSap,
    T0.CommitQty AS ComprometidoSap,
    T1.ItemName AS DescripcionSap,
    T2.DistNumber AS Lote
FROM OBTQ T0
INNER JOIN OITM T1
    ON T1.ItemCode = T0.ItemCode
INNER JOIN OBTN T2
    ON T2.ItemCode = T0.ItemCode
   AND T2.SysNumber = T0.SysNumber
WHERE
    T0.Quantity > 0
    AND T0.WhsCode = 'PLAP1GEN'
ORDER BY
    T0.WhsCode,
    T0.ItemCode,
    T2.DistNumber"
                },
                new SapSqlQueryDefinitionVM
                {
                    SqlCode = SAP_SQL_CONCILIACION_INVENTARIO_TIF,
                    SqlName = "CG - Conciliacion inventario SAP TIF",
                    ParamList = "",
                    SqlText = @"SELECT
    T0.ItemCode AS ProductoCodigo,
    T0.WhsCode AS Sucursal,
    T0.Quantity AS CantidadSap,
    T0.CommitQty AS ComprometidoSap,
    T1.ItemName AS DescripcionSap,
    T2.DistNumber AS Lote
FROM OBTQ T0
INNER JOIN OITM T1
    ON T1.ItemCode = T0.ItemCode
INNER JOIN OBTN T2
    ON T2.ItemCode = T0.ItemCode
   AND T2.SysNumber = T0.SysNumber
WHERE
    T0.Quantity > 0
    AND T0.WhsCode = 'PLATIFGE'
ORDER BY
    T0.WhsCode,
    T0.ItemCode,
    T2.DistNumber"
                }            };
        }
        [HttpGet]
        public async Task<IActionResult> ObtenerPermisosVistaEntregas()
        {
            var login = (User?.Identity?.Name ?? "").Trim();

            var permiso = await (
                from u in _db.UsuarioSQL
                join p in _db.Perfiles on u.PerfilId equals p.Id
                join ppm in _db.PerfilPermisoModulo on p.Id equals ppm.PerfilId
                join m in _db.ModulosSistema on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == "ENTREGAS_SAP"
                      && ppm.Activo
                      && m.Activo
                select new
                {
                    ppm.PuedeLeer,
                    ppm.PuedeEscribir,
                    ppm.PuedeEliminar
                }
            ).FirstOrDefaultAsync();

            if (permiso == null)
            {
                return Json(new { puedeLeer = false, puedeEscribir = false, puedeEliminar = false });
            }

            return Json(new
            {
                puedeLeer = permiso.PuedeLeer,
                puedeEscribir = permiso.PuedeEscribir,
                puedeEliminar = permiso.PuedeEliminar
            });
        }
        // ========================= ENTREGAS SAP =========================

        [HttpGet("EntregasSap")]
        [RevisarPermiso("ENTREGAS_SAP", "LEER")]
        public async Task<IActionResult> EntregasSap(DateTime? desde, DateTime? hasta, string source = "P1", string cliente = "")
        {
            var d1 = (desde ?? DateTime.Today).Date;
            var d2 = (hasta ?? DateTime.Today).Date.AddDays(1);

            var rows = await _data.ListarAsync(d1, d2, source);

            if (!string.IsNullOrWhiteSpace(cliente))
            {
                rows = rows
                    .Where(x => !string.IsNullOrWhiteSpace(x.Cliente) &&
                                x.Cliente.Contains(cliente, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            ViewBag.Source = source;
            ViewBag.Desde = d1;
            ViewBag.Hasta = d2.AddDays(-1);
            ViewBag.Cliente = cliente ?? "";

            return View(rows);
        }

        [HttpGet("EntregaJson")]
        [RevisarPermiso("ENTREGAS_SAP", "ESCRIBIR")]
        public async Task<IActionResult> EntregaJson(string referencia, string source = "P1")
        {
            var json = await _data.BuildJsonAsync(referencia, source);
            return Content(json ?? "{}", "application/json");
        }


        [HttpPost("EnviarEntrega")]
        [RevisarPermiso("ENTREGAS_SAP", "ESCRIBIR")]
        public async Task<IActionResult> EnviarEntrega([FromForm] string referencia, [FromForm] string source = "P1")
        {
            var sapEndpoint = "DeliveryNotes";

            try
            {
                var json = await _data.BuildJsonAsync(referencia, source);

                string? uDocMeat = null;
                string? numAtCard = null;

                try
                {
                    using var jd = JsonDocument.Parse(json);
                    var root = jd.RootElement;

                    if (root.TryGetProperty("U_DocMeat", out var p1) && p1.ValueKind == JsonValueKind.String)
                        uDocMeat = p1.GetString();

                    if (root.TryGetProperty("NumAtCard", out var p2) && p2.ValueKind == JsonValueKind.String)
                        numAtCard = p2.GetString();
                }
                catch { }

                var (found, docEntryExist, docNumExist) = await BuscarEntregaEnSapAsync(uDocMeat ?? "", numAtCard ?? "");

                if (found)
                {
                    await UpsertEntregaSapLogAsync(referencia, source, true, "Ya está en SAP.", docEntryExist, docNumExist);

                    return Ok(new
                    {
                        ok = true,
                        yaExiste = true,
                        msg = "Ya está en SAP.",
                        referencia,
                        docEntry = docEntryExist,
                        docNum = docNumExist
                    });
                }

                var r = await _sap.PostJsonAsync(sapEndpoint, json);

                if (!r.ok)
                {
                    await UpsertEntregaSapLogAsync(referencia, source, false, r.error ?? "No se pudo enviar a SAP.");

                    return BadRequest(new
                    {
                        ok = false,
                        msg = "No se pudo enviar a SAP.",
                        referencia,
                        error = r.error,
                        detalle = r.response
                    });
                }

                int? docEntry = null;
                int? docNum = null;

                try
                {
                    using var doc = JsonDocument.Parse(r.response);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("DocEntry", out var a) && a.ValueKind == JsonValueKind.Number)
                        docEntry = a.GetInt32();

                    if (root.TryGetProperty("DocNum", out var b) && b.ValueKind == JsonValueKind.Number)
                        docNum = b.GetInt32();
                }
                catch { }

                await UpsertEntregaSapLogAsync(referencia, source, true, "Enviado con éxito.", docEntry, docNum);

                return Ok(new
                {
                    ok = true,
                    yaExiste = false,
                    msg = "Entrega enviada con éxito a SAP.",
                    referencia,
                    docEntry,
                    docNum
                });
            }
            catch (Exception ex)
            {
                await UpsertEntregaSapLogAsync(referencia, source, false, ex.Message);

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error interno al enviar.",
                    referencia,
                    error = ex.Message
                });
            }
        }

        [HttpPost("EnviarEntregas")]
        [RevisarPermiso("ENTREGAS_SAP", "ESCRIBIR")]
        public async Task<IActionResult> EnviarEntregas([FromBody] string[] referencias, [FromQuery] string source = "P1")
        {
            var sapEndpoint = "DeliveryNotes";
            var results = new List<object>();

            foreach (var refx in referencias ?? Array.Empty<string>())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(refx))
                    {
                        results.Add(new
                        {
                            referencia = refx,
                            ok = false,
                            msg = "Referencia vacía."
                        });

                        continue;
                    }

                    var json = await _data.BuildJsonAsync(refx, source);

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        await UpsertEntregaSapLogAsync(refx, source, false, "No se pudo construir el JSON.");

                        results.Add(new
                        {
                            referencia = refx,
                            ok = false,
                            msg = "No se pudo construir el JSON."
                        });

                        continue;
                    }

                    var r = await _sap.PostJsonAsync(sapEndpoint, json);

                    if (!r.ok)
                    {
                        await UpsertEntregaSapLogAsync(refx, source, false, r.error ?? "No se pudo enviar a SAP.");

                        results.Add(new
                        {
                            referencia = refx,
                            ok = false,
                            msg = "No se pudo enviar.",
                            error = r.error,
                            detalle = r.response
                        });

                        continue;
                    }

                    int? docEntry = null;
                    int? docNum = null;

                    try
                    {
                        using var doc = JsonDocument.Parse(r.response);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("DocEntry", out var p1) && p1.ValueKind == JsonValueKind.Number)
                            docEntry = p1.GetInt32();

                        if (root.TryGetProperty("DocNum", out var p2) && p2.ValueKind == JsonValueKind.Number)
                            docNum = p2.GetInt32();
                    }
                    catch { }

                    await UpsertEntregaSapLogAsync(refx, source, true, "Enviado con éxito.", docEntry, docNum);

                    results.Add(new
                    {
                        referencia = refx,
                        ok = true,
                        msg = "Enviado con éxito.",
                        docEntry,
                        docNum
                    });
                }
                catch (Exception ex)
                {
                    await UpsertEntregaSapLogAsync(refx, source, false, ex.Message);

                    results.Add(new
                    {
                        referencia = refx,
                        ok = false,
                        msg = "Error interno.",
                        error = ex.Message
                    });
                }
            }

            var okCount = results.Count(x =>
            {
                var prop = x.GetType().GetProperty("ok");
                return prop != null && prop.GetValue(x) is bool ok && ok;
            });

            var failCount = results.Count - okCount;

            return Ok(new
            {
                ok = true,
                total = results.Count,
                enviados = okCount,
                fallidos = failCount,
                results
            });
        }

        [HttpGet("ValidarTrazabilidadEntrega")]
        [RevisarPermiso("ENTREGAS_SAP", "LEER")]
        public async Task<IActionResult> ValidarTrazabilidadEntrega([FromQuery] string referencia, [FromQuery] string source = "P1")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(referencia))
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = "Referencia vacía.",
                        detalle = new List<object>(),
                        devoluciones = new List<object>(),
                        devolucionError = ""
                    });
                }

                var json = await _data.BuildJsonAsync(referencia, source);

                var resultado = await ValidarTrazabilidadDesdeJsonAsync(json)
                                ?? new List<TrazabilidadSapVM>();

                var hayErrorTrazabilidad = resultado.Any(x =>
                    x.Estatus == "NO EXISTE LOTE EN SAP" ||
                    x.Estatus == "SIN DISPONIBLE" ||
                    x.Estatus == "FALTAN KG" ||
                    x.Estatus == "JSON SIN LOTES" ||
                    x.Estatus == "JSON VACÍO" ||
                    x.Estatus == "SIN DETALLE DE TRAZABILIDAD");

                var devoluciones = new List<DevolucionComparativoVM>();
                string devolucionError = "";

                try
                {
                    devoluciones = await ValidarDevolucionesAplicadasDesdeJsonAsync(json, source, referencia)
                                   ?? new List<DevolucionComparativoVM>();
                }
                catch (Exception exDev)
                {
                    devolucionError = exDev.Message;

                    _logger.LogWarning(exDev,
                        "Error al validar devoluciones Meat vs SAP. Ref={Referencia} Source={Source}",
                        referencia,
                        source);
                }

                var hayErrorDevolucion = devoluciones.Any(x =>
                    x.Estatus == "PENDIENTE SAP" ||
                    x.Estatus == "DIFERENCIA KG" ||
                    x.Estatus == "FALTA LOTE SAP");

                string msg;

                if (hayErrorTrazabilidad && hayErrorDevolucion)
                {
                    msg = "La entrega tiene diferencias de trazabilidad y devoluciones pendientes en SAP.";
                }
                else if (hayErrorTrazabilidad)
                {
                    msg = "La entrega tiene diferencias de trazabilidad. Revisa lotes y kg.";
                }
                else if (hayErrorDevolucion)
                {
                    msg = "La entrega tiene devoluciones pendientes o diferencias contra SAP.";
                }
                else if (!string.IsNullOrWhiteSpace(devolucionError))
                {
                    msg = "Trazabilidad correcta. No se pudo consultar devoluciones Meat vs SAP.";
                }
                else
                {
                    msg = "Trazabilidad correcta. Todos los lotes tienen kg disponibles.";
                }

                return Ok(new
                {
                    ok = !hayErrorTrazabilidad && !hayErrorDevolucion,
                    msg,
                    detalle = resultado,
                    devoluciones,
                    devolucionError
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error al validar trazabilidad.",
                    error = ex.Message,
                    detalle = new List<object>(),
                    devoluciones = new List<object>(),
                    devolucionError = ""
                });
            }
        }

        private sealed class DevolucionMeatSapRow
        {
            public string PedidoSap { get; set; } = "";
            public string RemisionMeat { get; set; } = "";
            public string CodigoSap { get; set; } = "";
            public string Cliente { get; set; } = "";
            public string Articulo { get; set; } = "";
            public string Lote { get; set; } = "";
            public int CajasDevueltas { get; set; }
            public decimal KgDevueltosMeat { get; set; }
            public DateTime? PrimeraFechaDevolucion { get; set; }
            public DateTime? UltimaFechaDevolucion { get; set; }
        }

        private sealed class DevolucionSapAplicadaRow
        {
            public string PedidoSap { get; set; } = "";
            public string Articulo { get; set; } = "";
            public string Lote { get; set; } = "";
            public decimal KgDevueltosSap { get; set; }
            public int? DocNumDevolucion { get; set; }
            public int? DocEntryDevolucion { get; set; }
            public DateTime? FechaDevolucionSap { get; set; }
            public string TipoAplicacion { get; set; } = "";
        }

        private sealed class DevolucionComparativoVM
        {
            public string PedidoSap { get; set; } = "";
            public string RemisionMeat { get; set; } = "";
            public string CodigoSap { get; set; } = "";
            public string Cliente { get; set; } = "";
            public string Articulo { get; set; } = "";
            public string Lote { get; set; } = "";
            public int CajasDevueltas { get; set; }
            public decimal KgDevueltosMeat { get; set; }
            public decimal KgDevueltosSap { get; set; }
            public decimal DiferenciaKg { get; set; }
            public int? DocNumDevolucion { get; set; }
            public int? DocEntryDevolucion { get; set; }
            public DateTime? FechaDevolucionSap { get; set; }
            public string TipoAplicacion { get; set; } = "";
            public string Estatus { get; set; } = "";
        }

        private async Task<List<DevolucionComparativoVM>> ValidarDevolucionesAplicadasDesdeJsonAsync(string json, string source, string referencia)
        {
            var pedidoSapJson = ExtraerNumAtCardDesdeJson(json);

            /*
              Primero obtenemos Meat/SIGO. Si esto trae registros, los vamos a mostrar
              aunque falle la consulta SAP. Así el modal ya no se queda vacío cuando
              SAP/HANA marca error.
            */
            var meat = await ObtenerDevolucionesMeatAsync(pedidoSapJson, referencia, source);

            var sap = new List<DevolucionSapAplicadaRow>();

            var pedidosSap = meat
                .Select(x => x.PedidoSap)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!pedidosSap.Any() && !string.IsNullOrWhiteSpace(pedidoSapJson))
                pedidosSap.Add(pedidoSapJson);

            foreach (var pedido in pedidosSap)
            {
                try
                {
                    var sapPedido = await ObtenerDevolucionesAplicadasSapAsync(pedido);
                    sap.AddRange(sapPedido);
                }
                catch (Exception exSap)
                {
                    _logger.LogWarning(exSap,
                        "No se pudo consultar devolución aplicada en SAP. PedidoSap={PedidoSap} Referencia={Referencia} Source={Source}",
                        pedido,
                        referencia,
                        source);
                }
            }

            const decimal toleranciaKg = 0.10m;

            var meatDict = meat.ToDictionary(
                x => KeyDevolucion(x.PedidoSap, x.Articulo, x.Lote),
                x => x
            );

            var sapDict = sap.ToDictionary(
                x => KeyDevolucion(x.PedidoSap, x.Articulo, x.Lote),
                x => x
            );

            var keys = meatDict.Keys
                .Union(sapDict.Keys)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var resultado = new List<DevolucionComparativoVM>();

            foreach (var key in keys)
            {
                meatDict.TryGetValue(key, out var m);
                sapDict.TryGetValue(key, out var s);

                var kgMeat = m?.KgDevueltosMeat ?? 0m;
                var kgSap = s?.KgDevueltosSap ?? 0m;
                var diferencia = kgMeat - kgSap;

                string estatus;

                if (kgMeat > 0 && kgSap <= 0)
                    estatus = "PENDIENTE SAP";
                else if (kgMeat <= 0 && kgSap > 0)
                    estatus = "SOBRANTE SAP";
                else if (string.IsNullOrWhiteSpace(s?.Lote) || (s?.Lote ?? "") == "SIN_LOTE")
                    estatus = "FALTA LOTE SAP";
                else if (Math.Abs(diferencia) <= toleranciaKg)
                    estatus = "APLICADA SAP";
                else
                    estatus = "DIFERENCIA KG";

                resultado.Add(new DevolucionComparativoVM
                {
                    PedidoSap = m?.PedidoSap ?? s?.PedidoSap ?? "",
                    RemisionMeat = m?.RemisionMeat ?? "",
                    CodigoSap = m?.CodigoSap ?? "",
                    Cliente = m?.Cliente ?? "",
                    Articulo = m?.Articulo ?? s?.Articulo ?? "",
                    Lote = m?.Lote ?? s?.Lote ?? "",
                    CajasDevueltas = m?.CajasDevueltas ?? 0,
                    KgDevueltosMeat = kgMeat,
                    KgDevueltosSap = kgSap,
                    DiferenciaKg = diferencia,
                    DocNumDevolucion = s?.DocNumDevolucion,
                    DocEntryDevolucion = s?.DocEntryDevolucion,
                    FechaDevolucionSap = s?.FechaDevolucionSap,
                    TipoAplicacion = s?.TipoAplicacion ?? "",
                    Estatus = estatus
                });
            }

            return resultado;
        }

        private async Task<List<DevolucionMeatSapRow>> ObtenerDevolucionesMeatAsync(string pedidoSap, string referencia, string source)
        {
            /*
              DevolucionMeat y SurtidoEncabezado están en la base SIGO.
              En appsettings esta conexión es DefaultConnection.
              No usar CadenaMeatP1 ni CadenaMeatTIF para esta consulta.
            */
            var cs = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(cs))
                throw new Exception("No existe la cadena de conexión 'DefaultConnection' para consultar SIGO.");

            const string sql = @"
SELECT
    UPPER(LTRIM(RTRIM(a.Pedido))) AS PedidoSap,
    MAX(b.Remision) AS RemisionMeat,
    RIGHT(a.Pedido, 8) AS FolioPedidoSap,
    UPPER(LTRIM(RTRIM(b.CodigoSap))) AS CodigoSap,
    MAX(b.Cliente) AS Cliente,
    UPPER(LTRIM(RTRIM(b.Articulo))) AS Articulo,
    UPPER(LTRIM(RTRIM(b.Lote))) AS Lote,
    COUNT(DISTINCT b.CodigoEtiqueta) AS CajasDevueltas,
    CAST(SUM(ISNULL(b.Peso, 0)) AS DECIMAL(18,3)) AS KgDevueltosMeat,
    MIN(b.FechaDevolucion) AS PrimeraFechaDevolucion,
    MAX(b.FechaDevolucion) AS UltimaFechaDevolucion
FROM dbo.SurtidoEncabezado a
INNER JOIN dbo.DevolucionMeat b
    ON UPPER(LTRIM(RTRIM(a.Remision))) = UPPER(LTRIM(RTRIM(b.Remision)))
WHERE
    UPPER(LTRIM(RTRIM(a.Pedido))) = UPPER(LTRIM(RTRIM(@PedidoSap)))
    OR UPPER(LTRIM(RTRIM(a.Remision))) = UPPER(LTRIM(RTRIM(@Referencia)))
    OR UPPER(LTRIM(RTRIM(b.Remision))) = UPPER(LTRIM(RTRIM(@Referencia)))
GROUP BY
    UPPER(LTRIM(RTRIM(a.Pedido))),
    RIGHT(a.Pedido, 8),
    UPPER(LTRIM(RTRIM(b.CodigoSap))),
    UPPER(LTRIM(RTRIM(b.Articulo))),
    UPPER(LTRIM(RTRIM(b.Lote)))
ORDER BY
    Articulo,
    Lote;";

            using var cn = new SqlConnection(cs);
            var rows = await cn.QueryAsync<DevolucionMeatSapRow>(sql, new
            {
                PedidoSap = pedidoSap,
                Referencia = referencia
            });

            return rows.ToList();
        }

        private async Task<List<DevolucionSapAplicadaRow>> ObtenerDevolucionesAplicadasSapAsync(string pedidoSap)
        {
            pedidoSap = (pedidoSap ?? "").Trim();

            if (string.IsNullOrWhiteSpace(pedidoSap))
                return new List<DevolucionSapAplicadaRow>();

            var rows = await EjecutarSapSqlQueryListAsync(
                SAP_SQL_DEV_APLICADAS_PEDIDO,
                new Dictionary<string, string>
                {
                    ["pedidoSap"] = pedidoSap
                }
            );

            return rows.Select(x => new DevolucionSapAplicadaRow
            {
                PedidoSap = JsonStr(x, "PedidoSap"),
                Articulo = JsonStr(x, "Articulo"),
                Lote = JsonStr(x, "Lote"),
                KgDevueltosSap = JsonDecimal(x, "KgDevueltosSap"),
                DocNumDevolucion = JsonIntNullable(x, "DocNumDevolucion"),
                DocEntryDevolucion = JsonIntNullable(x, "DocEntryDevolucion"),
                FechaDevolucionSap = JsonDateNullable(x, "FechaDevolucionSap"),
                TipoAplicacion = JsonStr(x, "TipoAplicacion")
            }).ToList();
        }

        private static string ExtraerNumAtCardDesdeJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("NumAtCard", out var p) &&
                    p.ValueKind == JsonValueKind.String)
                {
                    return p.GetString() ?? "";
                }
            }
            catch { }

            return "";
        }

        private static string KeyDevolucion(string pedidoSap, string articulo, string lote)
        {
            return $"{NormalizeKey(pedidoSap)}|{NormalizeKey(articulo)}|{NormalizeKey(lote)}";
        }

        private static string NormalizeKey(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private async Task<List<TrazabilidadSapVM>> ValidarTrazabilidadDesdeJsonAsync(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            {
                return new List<TrazabilidadSapVM>
                {
                    new TrazabilidadSapVM
                    {
                        Estatus = "JSON VACÍO"
                    }
                };
            }

            var lotesReq = new List<(string ItemCode, string WhsCode, string Lote, decimal Kg)>();

            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;

                if (!root.TryGetProperty("DocumentLines", out var lines) ||
                    lines.ValueKind != JsonValueKind.Array)
                {
                    return new List<TrazabilidadSapVM>
                    {
                        new TrazabilidadSapVM
                        {
                            Estatus = "JSON SIN LOTES"
                        }
                    };
                }

                foreach (var line in lines.EnumerateArray())
                {
                    var itemCode = line.TryGetProperty("ItemCode", out var pItem)
                        ? pItem.GetString() ?? ""
                        : "";

                    var whsCode = line.TryGetProperty("WarehouseCode", out var pWhs)
                        ? pWhs.GetString() ?? ""
                        : "";

                    if (!line.TryGetProperty("BatchNumbers", out var batches) ||
                        batches.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var batch in batches.EnumerateArray())
                    {
                        var lote = batch.TryGetProperty("BatchNumber", out var pLote)
                            ? pLote.GetString() ?? ""
                            : "";

                        decimal kg = 0;

                        if (batch.TryGetProperty("Quantity", out var pQty) &&
                            pQty.ValueKind == JsonValueKind.Number)
                        {
                            kg = pQty.GetDecimal();
                        }

                        if (!string.IsNullOrWhiteSpace(itemCode) &&
                            !string.IsNullOrWhiteSpace(whsCode) &&
                            !string.IsNullOrWhiteSpace(lote) &&
                            kg > 0)
                        {
                            lotesReq.Add((itemCode.Trim(), whsCode.Trim(), lote.Trim(), kg));
                        }
                    }
                }
            }

            var agrupado = lotesReq
                .GroupBy(x => new
                {
                    ItemCode = NormalizeKey(x.ItemCode),
                    WhsCode = NormalizeKey(x.WhsCode),
                    Lote = NormalizeKey(x.Lote)
                })
                .Select(g => new
                {
                    ItemCode = g.First().ItemCode,
                    WhsCode = g.First().WhsCode,
                    Lote = g.First().Lote,
                    Kg = g.Sum(x => x.Kg)
                })
                .ToList();

            if (!agrupado.Any())
            {
                return new List<TrazabilidadSapVM>
                {
                    new TrazabilidadSapVM
                    {
                        Estatus = "JSON SIN LOTES"
                    }
                };
            }

            var resultado = new List<TrazabilidadSapVM>();

            foreach (var x in agrupado)
            {
                // Reutilizamos la conciliación SAP por almacén fijo y filtramos en C#.
                // Esto evita usar parámetros string en SQLQueries, que en tu Service Layer está regresando "Parameter error".
                var sapInv = await ObtenerInventarioSapConciliacionAsync(x.WhsCode, x.ItemCode, x.Lote);

                var sap = sapInv
                    .Where(r =>
                        NormalizeKey(r.ProductoCodigo) == NormalizeKey(x.ItemCode) &&
                        NormLoteInv(r.Lote) == NormLoteInv(x.Lote))
                    .GroupBy(r => KeyInv(r.Sucursal, r.ProductoCodigo, r.Lote))
                    .Select(g => new InventarioSapConciliacionRow
                    {
                        Sucursal = g.First().Sucursal,
                        ProductoCodigo = g.First().ProductoCodigo,
                        DescripcionSap = g.First().DescripcionSap,
                        Lote = g.First().Lote,
                        CantidadSap = g.Sum(r => r.CantidadSap),
                        ComprometidoSap = g.Sum(r => r.ComprometidoSap),
                        DisponibleSap = g.Sum(r => r.DisponibleSap)
                    })
                    .FirstOrDefault();

                decimal cantidadSap = sap?.CantidadSap ?? 0m;
                decimal comprometidoSap = sap?.ComprometidoSap ?? 0m;
                decimal disponibleSap = sap?.DisponibleSap ?? 0m;

                decimal kgFaltantes = 0m;
                decimal kgSobrantes = 0m;
                string estatus;

                if (sap == null)
                {
                    kgFaltantes = x.Kg;
                    estatus = "NO EXISTE LOTE EN SAP";
                }
                else if (disponibleSap <= 0)
                {
                    kgFaltantes = x.Kg;
                    estatus = "SIN DISPONIBLE";
                }
                else if (disponibleSap + 0.01m < x.Kg)
                {
                    kgFaltantes = x.Kg - disponibleSap;
                    estatus = "FALTAN KG";
                }
                else if (Math.Abs(disponibleSap - x.Kg) <= 0.01m)
                {
                    estatus = "OK EXACTO";
                }
                else if (disponibleSap > x.Kg)
                {
                    kgSobrantes = disponibleSap - x.Kg;
                    estatus = "OK CON SOBRANTE";
                }
                else
                {
                    estatus = "REVISAR";
                }

                resultado.Add(new TrazabilidadSapVM
                {
                    Articulo = sap?.ProductoCodigo ?? x.ItemCode,
                    Almacen = sap?.Sucursal ?? x.WhsCode,
                    Lote = sap?.Lote ?? x.Lote,
                    KgSolicitadosJson = x.Kg,
                    CantidadSap = cantidadSap,
                    ComprometidoSap = comprometidoSap,
                    DisponibleSap = disponibleSap,
                    KgFaltantes = kgFaltantes,
                    KgSobrantes = kgSobrantes,
                    Estatus = estatus
                });
            }

            return resultado
                .OrderBy(x => x.Articulo)
                .ThenBy(x => x.Lote)
                .ToList();
        }

        private static string SqlText(string value)
        {
            return (value ?? "").Replace("'", "''");
        }

        private async Task<List<JsonElement>> EjecutarSapSqlQueryListAsync(
            string sqlCode,
            Dictionary<string, string>? parameters = null)
        {
            if (string.IsNullOrWhiteSpace(sqlCode))
                throw new Exception("Código de SQLQuery SAP vacío.");

            parameters ??= new Dictionary<string, string>();

            var endpoint = $"SQLQueries('{ODataEscape(sqlCode)}')/List";
            var rows = new List<JsonElement>();

            (bool ok, string? response, string? error) r;

            // Para SQLQueries sin parámetros, usar GET.
            // Esto evita errores de "Parameter error" en Service Layer cuando el body va vacío.
            if (parameters.Count == 0)
            {
                var g = await _sap.GetAsync(endpoint);
                r = (g.ok, g.response, g.error);
            }
            else
            {
                var paramList = string.Join(",", parameters.Select(x =>
                    $"{x.Key}={SapSqlParamValue(x.Value)}"));

                var body = JsonSerializer.Serialize(new { ParamList = paramList });
                var p = await _sap.PostJsonAsync(endpoint, body);
                r = (p.ok, p.response, p.error);
            }

            if (!r.ok)
            {
                throw new Exception(
                    $"No se pudo ejecutar SQLQuery SAP '{sqlCode}'. " +
                    $"Verifica que exista en Service Layer y que el usuario tenga permisos. " +
                    $"Error: {r.error}. Respuesta: {r.response}");
            }

            string? nextLink = LeerRowsSqlQueryResponse(r.response, rows);

            var guard = 0;
            while (!string.IsNullOrWhiteSpace(nextLink) && guard < 200)
            {
                guard++;

                var nextEndpoint = NormalizarSapEndpoint(nextLink);
                var next = await _sap.GetAsync(nextEndpoint);

                if (!next.ok)
                {
                    throw new Exception(
                        $"No se pudo leer la siguiente página de SQLQuery SAP '{sqlCode}'. " +
                        $"Error: {next.error}. Respuesta: {next.response}");
                }

                nextLink = LeerRowsSqlQueryResponse(next.response, rows);
            }

            return rows;
        }

        private static string? LeerRowsSqlQueryResponse(string? response, List<JsonElement> rows)
        {
            if (string.IsNullOrWhiteSpace(response))
                return null;

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                    rows.Add(item.Clone());

                return null;
            }

            if (root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                    rows.Add(item.Clone());
            }

            if (root.TryGetProperty("@odata.nextLink", out var next1) && next1.ValueKind == JsonValueKind.String)
                return next1.GetString();

            if (root.TryGetProperty("odata.nextLink", out var next2) && next2.ValueKind == JsonValueKind.String)
                return next2.GetString();

            return null;
        }

        private static string NormalizarSapEndpoint(string endpointOrUrl)
        {
            var s = (endpointOrUrl ?? "").Trim();

            if (string.IsNullOrWhiteSpace(s))
                return s;

            var idx = s.IndexOf("/b1s/v1/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return s.Substring(idx + "/b1s/v1/".Length);

            idx = s.IndexOf("/b1s/v2/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return s.Substring(idx + "/b1s/v2/".Length);

            return s.TrimStart('/');
        }

        //PARA VER QUERYS REQUERIDAS
        //http://localhost:5019/ProcesosCG/SapSqlQueriesRequeridas


        //pra enviar los endpoint o querys a service layer
        //POST https://TU_SERVIDOR:50000/b1s/v2/SQLQueries

        //BODY
        //        {
        //  "SqlCode": "CG_CONCILIACION_INVENTARIO",
        //  "SqlName": "CG - Conciliacion inventario SAP",
        //  "ParamList": "sucursal,articulo,lote",
        //  "SqlText": "SELECT T0.\"WhsCode\" AS \"Sucursal\", T0.\"ItemCode\" AS \"ProductoCodigo\", IFNULL(T1.\"ItemName\", '') AS \"DescripcionSap\", IFNULL(NULLIF(T2.\"DistNumber\", ''), '-') AS \"Lote\", SUM(IFNULL(T0.\"Quantity\", 0)) AS \"CantidadSap\", SUM(IFNULL(T0.\"CommitQty\", 0)) AS \"ComprometidoSap\", SUM(IFNULL(T0.\"Quantity\", 0) - IFNULL(T0.\"CommitQty\", 0)) AS \"DisponibleSap\" FROM OBTQ T0 INNER JOIN OITM T1 ON T1.\"ItemCode\" = T0.\"ItemCode\" INNER JOIN OBTN T2 ON T2.\"ItemCode\" = T0.\"ItemCode\" AND T2.\"SysNumber\" = T0.\"SysNumber\" WHERE T0.\"Quantity\" > 0 AND (:sucursal = '' OR T0.\"WhsCode\" = :sucursal) AND (:articulo = '' OR UPPER(T0.\"ItemCode\") LIKE '%' || UPPER(:articulo) || '%') AND (:lote = '' OR UPPER(T2.\"DistNumber\") LIKE '%' || UPPER(:lote) || '%') GROUP BY T0.\"WhsCode\", T0.\"ItemCode\", T1.\"ItemName\", T2.\"DistNumber\" ORDER BY T0.\"WhsCode\", T0.\"ItemCode\", T2.\"DistNumber\""
        //}
        private static string SapSqlParamValue(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace(",", " ")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static bool TryGetJsonProperty(JsonElement row, string name, out JsonElement value)
        {
            if (row.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in row.EnumerateObject())
                {
                    if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = p.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string JsonStr(JsonElement row, string name)
        {
            if (!TryGetJsonProperty(row, name, out var value))
                return "";

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.ToString() ?? ""
            };
        }

        private static decimal JsonDecimal(JsonElement row, string name)
        {
            if (!TryGetJsonProperty(row, name, out var value))
                return 0m;

            try
            {
                if (value.ValueKind == JsonValueKind.Number)
                    return value.GetDecimal();

                if (value.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return d;
            }
            catch { }

            return 0m;
        }

        private static int? JsonIntNullable(JsonElement row, string name)
        {
            if (!TryGetJsonProperty(row, name, out var value))
                return null;

            try
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
                    return n;

                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var s))
                    return s;
            }
            catch { }

            return null;
        }

        private static DateTime? JsonDateNullable(JsonElement row, string name)
        {
            if (!TryGetJsonProperty(row, name, out var value))
                return null;

            if (value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString() ?? "";

                if (DateTime.TryParse(s, out var d))
                    return d;
            }

            return null;
        }

        private static decimal ToDecimalSafe(object value)
        {
            if (value == null || value == DBNull.Value)
                return 0;

            return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int? ToIntSafe(object value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            try
            {
                return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                if (int.TryParse(value.ToString(), out var n))
                    return n;

                return null;
            }
        }

        private static DateTime? ToDateTimeSafe(object value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            if (value is DateTime d)
                return d;

            if (DateTime.TryParse(value.ToString(), out var parsed))
                return parsed;

            return null;
        }

        private async Task UpsertEntregaSapLogAsync(
            string referencia,
            string source,
            bool estatus,
            string? mensaje = null,
            int? docEntry = null,
            int? docNum = null)
        {
            var usuario = User?.Identity?.Name;

            var row = await _db.EntregaSapLogs
                .FirstOrDefaultAsync(x => x.Referencia == referencia && x.Source == source);

            if (row == null)
            {
                row = new EntregaSapLog { Referencia = referencia, Source = source };
                _db.EntregaSapLogs.Add(row);
            }

            row.Estatus = estatus;
            row.Mensaje = (mensaje ?? (estatus ? "Enviado con éxito." : "No se pudo enviar."));
            if (row.Mensaje.Length > 300) row.Mensaje = row.Mensaje.Substring(0, 300);

            row.DocEntry = docEntry;
            row.DocNum = docNum;
            row.FechaIntento = DateTime.Now;
            row.Usuario = usuario;

            await _db.SaveChangesAsync();
        }

        private static string ODataEscape(string s) => (s ?? "").Replace("'", "''");

        private async Task<(bool found, int? docEntry, int? docNum)> BuscarEntregaEnSapAsync(string uDocMeat, string numAtCard)
        {
            var filters = new List<string>();

            if (!string.IsNullOrWhiteSpace(uDocMeat))
                filters.Add($"U_DocMeat eq '{ODataEscape(uDocMeat)}'");

            if (!string.IsNullOrWhiteSpace(numAtCard))
                filters.Add($"NumAtCard eq '{ODataEscape(numAtCard)}'");

            if (filters.Count == 0) return (false, null, null);

            var filter = string.Join(" or ", filters);

            var endpoint =
                $"DeliveryNotes?$select=DocEntry,DocNum&$top=1&$filter={Uri.EscapeDataString(filter)}";

            var g = await _sap.GetAsync(endpoint);
            if (!g.ok || string.IsNullOrWhiteSpace(g.response)) return (false, null, null);

            try
            {
                using var doc = JsonDocument.Parse(g.response);
                var root = doc.RootElement;

                if (root.TryGetProperty("value", out var val) &&
                    val.ValueKind == JsonValueKind.Array &&
                    val.GetArrayLength() > 0)
                {
                    var first = val[0];

                    int? docEntry = null;
                    int? docNum = null;

                    if (first.TryGetProperty("DocEntry", out var p1) && p1.ValueKind == JsonValueKind.Number)
                        docEntry = p1.GetInt32();

                    if (first.TryGetProperty("DocNum", out var p2) && p2.ValueKind == JsonValueKind.Number)
                        docNum = p2.GetInt32();

                    return (true, docEntry, docNum);
                }
            }
            catch { }

            return (false, null, null);
        }


        private async Task<(bool found, int? docEntry, int? docNum)> BuscarFacturaEnSapAsync(string uDocMeat, string numAtCard)
        {
            var filters = new List<string>();

            if (!string.IsNullOrWhiteSpace(uDocMeat))
                filters.Add($"U_DocMeat eq '{ODataEscape(uDocMeat)}'");

            if (!string.IsNullOrWhiteSpace(numAtCard))
                filters.Add($"NumAtCard eq '{ODataEscape(numAtCard)}'");

            if (filters.Count == 0) return (false, null, null);

            var filter = string.Join(" or ", filters);

            // Invoices = OINV (facturas)
            var endpoint =
                $"Invoices?$select=DocEntry,DocNum&$top=1&$filter={Uri.EscapeDataString(filter)}";

            var g = await _sap.GetAsync(endpoint);
            if (!g.ok || string.IsNullOrWhiteSpace(g.response)) return (false, null, null);

            try
            {
                using var doc = JsonDocument.Parse(g.response);
                var root = doc.RootElement;

                if (root.TryGetProperty("value", out var val) &&
                    val.ValueKind == JsonValueKind.Array &&
                    val.GetArrayLength() > 0)
                {
                    var first = val[0];

                    int? docEntry = null;
                    int? docNum = null;

                    if (first.TryGetProperty("DocEntry", out var p1) && p1.ValueKind == JsonValueKind.Number)
                        docEntry = p1.GetInt32();

                    if (first.TryGetProperty("DocNum", out var p2) && p2.ValueKind == JsonValueKind.Number)
                        docNum = p2.GetInt32();

                    return (true, docEntry, docNum);
                }
            }
            catch { }

            return (false, null, null);
        }


        // =======================================================
        // CONCILIACIÓN INVENTARIO LOCAL VS SAP
        // =======================================================

        [HttpGet("ConciliacionInventarioJson")]
        [RevisarPermiso("ENTREGAS_SAP", "LEER")]
        public async Task<IActionResult> ConciliacionInventarioJson(
            string sucursal = "",
            string articulo = "",
            string lote = "",
            string estatus = "")
        {
            try
            {
                var rows = await ObtenerConciliacionInventarioAsync(
                    sucursal,
                    articulo,
                    lote,
                    estatus
                );

                return Ok(new
                {
                    ok = true,
                    total = rows.Count,
                    rows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ConciliacionInventarioJson");

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error al consultar conciliación de inventario.",
                    error = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        private async Task<List<ConciliacionInventarioRowVM>> ObtenerConciliacionInventarioAsync(
            string sucursal,
            string articulo,
            string lote,
            string estatus)
        {
            sucursal = (sucursal ?? "").Trim();
            articulo = (articulo ?? "").Trim();
            lote = (lote ?? "").Trim();
            estatus = (estatus ?? "").Trim();

            var local = await ObtenerInventarioLocalConciliacionAsync(sucursal, articulo, lote);
            var sap = await ObtenerInventarioSapConciliacionAsync(sucursal, articulo, lote);

            var localDict = local
                .GroupBy(x => KeyInv(x.Sucursal, x.ProductoCodigo, x.Lote))
                .ToDictionary(
                    g => g.Key,
                    g => new InventarioLocalConciliacionRow
                    {
                        Sucursal = g.First().Sucursal,
                        ProductoCodigo = g.First().ProductoCodigo,
                        Lote = g.First().Lote,
                        KgLocal = g.Sum(x => x.KgLocal)
                    }
                );

            var sapDict = sap
                .GroupBy(x => KeyInv(x.Sucursal, x.ProductoCodigo, x.Lote))
                .ToDictionary(
                    g => g.Key,
                    g => new InventarioSapConciliacionRow
                    {
                        Sucursal = g.First().Sucursal,
                        ProductoCodigo = g.First().ProductoCodigo,
                        DescripcionSap = g.First().DescripcionSap,
                        Lote = g.First().Lote,
                        CantidadSap = g.Sum(x => x.CantidadSap),
                        ComprometidoSap = g.Sum(x => x.ComprometidoSap),
                        DisponibleSap = g.Sum(x => x.DisponibleSap)
                    }
                );

            var keys = localDict.Keys
                .Union(sapDict.Keys)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var resultado = new List<ConciliacionInventarioRowVM>();

            foreach (var key in keys)
            {
                localDict.TryGetValue(key, out var l);
                sapDict.TryGetValue(key, out var s);

                var kgLocal = l?.KgLocal ?? 0;
                var kgSap = s?.CantidadSap ?? 0;
                var diferencia = kgLocal - kgSap;

                string estatusCalc;

                if (l == null && s != null)
                {
                    estatusCalc = "FALTA LOCAL";
                }
                else if (l != null && s == null)
                {
                    estatusCalc = "FALTA SAP";
                }
                else if (Math.Abs(diferencia) <= 0.01m)
                {
                    estatusCalc = "OK";
                }
                else
                {
                    estatusCalc = "DIFERENCIA KG";
                }

                resultado.Add(new ConciliacionInventarioRowVM
                {
                    Sucursal = l?.Sucursal ?? s?.Sucursal ?? "",
                    ProductoCodigo = l?.ProductoCodigo ?? s?.ProductoCodigo ?? "",
                    DescripcionSap = s?.DescripcionSap ?? "",
                    Lote = l?.Lote ?? s?.Lote ?? "",

                    KgLocal = kgLocal,
                    CantidadSap = kgSap,
                    ComprometidoSap = s?.ComprometidoSap ?? 0,
                    DisponibleSap = s?.DisponibleSap ?? 0,

                    DiferenciaKg = diferencia,
                    DiferenciaAbs = Math.Abs(diferencia),
                    Estatus = estatusCalc
                });
            }

            if (!string.IsNullOrWhiteSpace(estatus))
            {
                resultado = resultado
                    .Where(x => x.Estatus.Equals(estatus, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return resultado
                .OrderBy(x => x.Sucursal)
                .ThenBy(x => x.ProductoCodigo)
                .ThenBy(x => x.Lote)
                .ToList();
        }

        private async Task<List<InventarioLocalConciliacionRow>> ObtenerInventarioLocalConciliacionAsync(
            string sucursal,
            string articulo,
            string lote)
        {
            const string sql = @"
WITH LocalInv AS
(
    SELECT 
        CASE 
            WHEN Sucursal = 'PLANTA 1' THEN 'PLAP1GEN'
            WHEN Sucursal = 'TIF' THEN 'PLATIFGE'
            ELSE Sucursal
        END AS Sucursal,

        LTRIM(RTRIM(ProductoCodigo)) AS ProductoCodigo,

        ISNULL(NULLIF(LTRIM(RTRIM(Lote)), ''), '-') AS Lote,

        SUM(ISNULL(Kg, 0)) AS KgLocal
    FROM dbo.InventarioSigo
    GROUP BY 
        CASE 
            WHEN Sucursal = 'PLANTA 1' THEN 'PLAP1GEN'
            WHEN Sucursal = 'TIF' THEN 'PLATIFGE'
            ELSE Sucursal
        END,
        LTRIM(RTRIM(ProductoCodigo)),
        ISNULL(NULLIF(LTRIM(RTRIM(Lote)), ''), '-')
)
SELECT
    Sucursal,
    ProductoCodigo,
    Lote,
    KgLocal
FROM LocalInv
WHERE (@Sucursal = '' OR Sucursal = @Sucursal)
  AND (@Articulo = '' OR ProductoCodigo LIKE '%' + @Articulo + '%')
  AND (@Lote = '' OR Lote LIKE '%' + @Lote + '%')
ORDER BY
    Sucursal,
    ProductoCodigo,
    Lote;
";

            var cn = _db.Database.GetDbConnection();
            var shouldClose = cn.State == ConnectionState.Closed;

            if (shouldClose)
                await cn.OpenAsync();

            try
            {
                var rows = await cn.QueryAsync<InventarioLocalConciliacionRow>(sql, new
                {
                    Sucursal = sucursal ?? "",
                    Articulo = articulo ?? "",
                    Lote = lote ?? ""
                });

                return rows.ToList();
            }
            finally
            {
                if (shouldClose)
                    await cn.CloseAsync();
            }
        }

        private async Task<List<InventarioSapConciliacionRow>> ObtenerInventarioSapConciliacionAsync(
            string sucursal,
            string articulo,
            string lote)
        {
            sucursal = (sucursal ?? "").Trim().ToUpperInvariant();
            articulo = (articulo ?? "").Trim().ToUpperInvariant();
            lote = (lote ?? "").Trim().ToUpperInvariant();

            var sqlCodes = new List<string>();

            if (string.IsNullOrWhiteSpace(sucursal))
            {
                // Cuando la pantalla está en "Todos", consultamos las dos SQLQueries fijas
                // para evitar parámetros string en Service Layer.
                sqlCodes.Add(SAP_SQL_CONCILIACION_INVENTARIO_P1);
                sqlCodes.Add(SAP_SQL_CONCILIACION_INVENTARIO_TIF);
            }
            else if (sucursal == "PLAP1GEN")
            {
                sqlCodes.Add(SAP_SQL_CONCILIACION_INVENTARIO_P1);
            }
            else if (sucursal == "PLATIFGE")
            {
                sqlCodes.Add(SAP_SQL_CONCILIACION_INVENTARIO_TIF);
            }
            else
            {
                return new List<InventarioSapConciliacionRow>();
            }

            var salida = new List<InventarioSapConciliacionRow>();

            foreach (var sqlCode in sqlCodes)
            {
                // Estas consultas ya tienen el almacén fijo dentro del SQLText,
                // por eso no mandamos ParamList.
                var rows = await EjecutarSapSqlQueryListAsync(sqlCode);

                foreach (var x in rows)
                {
                    var item = JsonStr(x, "ProductoCodigo").Trim();
                    var loteSap = JsonStr(x, "Lote").Trim();

                    if (!string.IsNullOrWhiteSpace(articulo) &&
                        !item.ToUpperInvariant().Contains(articulo))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(lote) &&
                        !loteSap.ToUpperInvariant().Contains(lote))
                    {
                        continue;
                    }

                    var cantidad = JsonDecimal(x, "CantidadSap");
                    var comprometido = JsonDecimal(x, "ComprometidoSap");

                    salida.Add(new InventarioSapConciliacionRow
                    {
                        Sucursal = JsonStr(x, "Sucursal"),
                        ProductoCodigo = item,
                        DescripcionSap = JsonStr(x, "DescripcionSap"),
                        Lote = string.IsNullOrWhiteSpace(loteSap) ? "-" : loteSap,
                        CantidadSap = cantidad,
                        ComprometidoSap = comprometido,
                        DisponibleSap = cantidad - comprometido
                    });
                }
            }

            return salida;
        }

        private static string KeyInv(string sucursal, string producto, string lote)
        {
            return $"{NormInv(sucursal)}|{NormInv(producto)}|{NormLoteInv(lote)}";
        }

        private static string NormInv(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private static string NormLoteInv(string value)
        {
            var v = (value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(v))
                return "-";

            return v.ToUpperInvariant();
        }

        private static string SqlTextInv(string value)
        {
            return (value ?? "").Replace("'", "''");
        }

        private static decimal ToDecimalInv(object value)
        {
            if (value == null || value == DBNull.Value)
                return 0;

            return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public class ConciliacionInventarioRowVM
        {
            public string Sucursal { get; set; } = "";
            public string ProductoCodigo { get; set; } = "";
            public string DescripcionSap { get; set; } = "";
            public string Lote { get; set; } = "";

            public decimal KgLocal { get; set; }
            public decimal CantidadSap { get; set; }
            public decimal ComprometidoSap { get; set; }
            public decimal DisponibleSap { get; set; }

            public decimal DiferenciaKg { get; set; }
            public decimal DiferenciaAbs { get; set; }

            public string Estatus { get; set; } = "";
        }

        private class InventarioLocalConciliacionRow
        {
            public string Sucursal { get; set; } = "";
            public string ProductoCodigo { get; set; } = "";
            public string Lote { get; set; } = "";
            public decimal KgLocal { get; set; }
        }

        private class InventarioSapConciliacionRow
        {
            public string Sucursal { get; set; } = "";
            public string ProductoCodigo { get; set; } = "";
            public string DescripcionSap { get; set; } = "";
            public string Lote { get; set; } = "";

            public decimal CantidadSap { get; set; }
            public decimal ComprometidoSap { get; set; }
            public decimal DisponibleSap { get; set; }
        }

        [HttpGet("ConciliacionInventarioExcel")]
        [RevisarPermiso("ENTREGAS_SAP", "LEER")]
        public async Task<IActionResult> ConciliacionInventarioExcel(
            string sucursal = "",
            string articulo = "",
            string lote = "",
            string estatus = "")
        {
            try
            {
                var rows = await ObtenerConciliacionInventarioAsync(
                    sucursal,
                    articulo,
                    lote,
                    estatus
                );

                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Conciliacion");

                ws.Cell(1, 1).Value = "Sucursal";
                ws.Cell(1, 2).Value = "Artículo";
                ws.Cell(1, 3).Value = "Descripción SAP";
                ws.Cell(1, 4).Value = "Lote";
                ws.Cell(1, 5).Value = "Kg Local";
                ws.Cell(1, 6).Value = "Cantidad SAP";
                ws.Cell(1, 7).Value = "Comprometido SAP";
                ws.Cell(1, 8).Value = "Disponible SAP";
                ws.Cell(1, 9).Value = "Diferencia Kg";
                ws.Cell(1, 10).Value = "Estatus";

                var header = ws.Range(1, 1, 1, 10);
                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.DarkRed;
                header.Style.Font.FontColor = XLColor.White;
                header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var row = i + 2;

                    ws.Cell(row, 1).Value = r.Sucursal;
                    ws.Cell(row, 2).Value = r.ProductoCodigo;
                    ws.Cell(row, 3).Value = r.DescripcionSap;
                    ws.Cell(row, 4).Value = r.Lote;
                    ws.Cell(row, 5).Value = r.KgLocal;
                    ws.Cell(row, 6).Value = r.CantidadSap;
                    ws.Cell(row, 7).Value = r.ComprometidoSap;
                    ws.Cell(row, 8).Value = r.DisponibleSap;
                    ws.Cell(row, 9).Value = r.DiferenciaKg;
                    ws.Cell(row, 10).Value = r.Estatus;

                    if (r.Estatus == "OK")
                    {
                        ws.Cell(row, 10).Style.Fill.BackgroundColor = XLColor.LightGreen;
                    }
                    else
                    {
                        ws.Cell(row, 10).Style.Fill.BackgroundColor = XLColor.LightPink;
                    }
                }

                ws.Columns().AdjustToContents();

                ws.Column(5).Style.NumberFormat.Format = "#,##0.00";
                ws.Column(6).Style.NumberFormat.Format = "#,##0.00";
                ws.Column(7).Style.NumberFormat.Format = "#,##0.00";
                ws.Column(8).Style.NumberFormat.Format = "#,##0.00";
                ws.Column(9).Style.NumberFormat.Format = "#,##0.00";

                ws.SheetView.FreezeRows(1);
                ws.RangeUsed()?.SetAutoFilter();

                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                ms.Position = 0;

                var fileName = $"ConciliacionInventario_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(
                    ms.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ConciliacionInventarioExcel");

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error al generar Excel de conciliación.",
                    error = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }



















        // ========================= PESO LOTE / AUDITORIA =========================

        [HttpGet("AuditoriaPesoManual")]
        public IActionResult AuditoriaPesoManual()
        {
            return View("~/Views/ProcesosCG/AuditoriaPesoManual.cshtml");
        }

        [HttpGet("PesoLote")]
        public async Task<IActionResult> PesoLote([FromQuery] PesoLoteFiltroVm model)
        {
            var cs = _configuration.GetConnectionString("CadenaMeatTIF");

            var hoy = DateTime.Today;
            model.Desde ??= hoy.AddDays(-1);
            model.Hasta ??= hoy;

            var desde = model.Desde.Value.Date;
            var hastaExclusivo = model.Hasta.Value.Date.AddDays(1);

            var tipo = (model.TipoPeso ?? "").Trim().ToUpper();
            if (tipo != "MANUAL" && tipo != "AUTOMATICO") tipo = "";

            using var cn = new SqlConnection(cs);

            // ✅ UNA SOLA consulta a la vista con tabla temporal
            const string sqlTodo = @"
SELECT *
INTO #TempPesoLote
FROM dbo.vw_PesoLote_Encabezado
WHERE FechaProduccionMin >= @Desde
  AND FechaProduccionMin <  @Hasta
  AND ( @Tipo = '' OR Tipo = @Tipo );

SELECT DISTINCT
    CAST(LoteId AS varchar(20)) + '|' + ISNULL(NombreLote, '') AS KeyText
FROM #TempPesoLote
ORDER BY KeyText;

SELECT *
FROM #TempPesoLote
ORDER BY LoteId, Desde;

DROP TABLE #TempPesoLote;";

            using var multi = await cn.QueryMultipleAsync(sqlTodo, new
            {
                Desde = desde,
                Hasta = hastaExclusivo,
                Tipo = tipo
            });

            var lotesRaw = (await multi.ReadAsync<string>()).ToList();
            var rows = (await multi.ReadAsync<PesoLoteEncRow>()).ToList();

            model.Lotes = lotesRaw
                .Select(x =>
                {
                    var parts = (x ?? "").Split('|');
                    var nom = parts.Length > 1 ? parts[1] : "";
                    return string.IsNullOrWhiteSpace(nom) ? "-" : nom.Trim();
                })
                .ToList();

            var ids = (model.LotesSeleccionados ?? new List<int>()).Distinct().ToList();

            model.Resultados = ids.Count == 0
                ? rows
                : rows.Where(r => ids.Contains(r.LoteId)).ToList();

            return View("~/Views/ProcesosCG/AuditoriaPesoManual.cshtml", model);
        }



        [HttpGet("PesoLoteDetalleApi")]
        public async Task<IActionResult> PesoLoteDetalleApi(int loteId, DateTime desde, DateTime hasta)
        {
            var cs = _configuration.GetConnectionString("CadenaMeatTIF");

            const string sql = @"
SELECT *
FROM dbo.vw_PesoLote_Detallado
WHERE LoteId = @LoteId
  AND (
        (FechaSolicitud IS NOT NULL AND FechaSolicitud >= @Desde AND FechaSolicitud <= @Hasta)
        OR
        (FechaSolicitud IS NULL AND FechaProduccion >= @Desde AND FechaProduccion <= @Hasta)
      )
ORDER BY FechaProduccion, FechaSolicitud;";

            using var cn = new SqlConnection(cs);
            var rows = await cn.QueryAsync(sql, new { LoteId = loteId, Desde = desde, Hasta = hasta });

            return Json(new { ok = true, rows });
        }

        [HttpGet("PesoLoteExcel")]
        public async Task<IActionResult> PesoLoteExcel(DateTime? desde, DateTime? hasta, int? loteId)
        {
            var cs = _configuration.GetConnectionString("CadenaMeatTIF");

            var hoy = DateTime.Today;
            desde ??= hoy.AddDays(-1);
            hasta ??= hoy;

            var d1 = desde.Value.Date;
            var d2 = hasta.Value.Date.AddDays(1);

            const string sqlEnc = @"
SELECT *
FROM dbo.vw_PesoLote_Encabezado
WHERE (@LoteId IS NULL OR LoteId = @LoteId)
  AND FechaProduccionMin >= @Desde
  AND FechaProduccionMin <  @Hasta
ORDER BY LoteId, Desde;";

            using var cn = new SqlConnection(cs);
            var rows = (await cn.QueryAsync<PesoLoteEncRow>(sqlEnc, new { LoteId = loteId, Desde = d1, Hasta = d2 })).ToList();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Encabezado");

            ws.Cell(1, 1).Value = "LoteId";
            ws.Cell(1, 2).Value = "NombreLote";
            ws.Cell(1, 3).Value = "Tipo";
            ws.Cell(1, 4).Value = "Desde";
            ws.Cell(1, 5).Value = "Hasta";
            ws.Cell(1, 6).Value = "Proceso";
            ws.Cell(1, 7).Value = "Solicitante";
            ws.Cell(1, 8).Value = "Autoriza";
            ws.Cell(1, 9).Value = "Estacion";
            ws.Cell(1, 10).Value = "Accion";

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                ws.Cell(i + 2, 1).Value = r.LoteId;
                ws.Cell(i + 2, 2).Value = r.NombreLote;
                ws.Cell(i + 2, 3).Value = r.Tipo;
                ws.Cell(i + 2, 4).Value = r.Desde;
                ws.Cell(i + 2, 5).Value = r.Hasta;
                ws.Cell(i + 2, 6).Value = r.Proceso;
                ws.Cell(i + 2, 7).Value = r.Solicitante;
                ws.Cell(i + 2, 8).Value = r.Autoriza;
                ws.Cell(i + 2, 9).Value = r.Estacion;
                ws.Cell(i + 2, 10).Value = r.Accion;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;

            var fileName = $"PesoLote_Encabezado_{d1:yyyyMMdd}_{hasta.Value:yyyyMMdd}.xlsx";
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpGet("PesoLoteDetalleExcel")]
        public async Task<IActionResult> PesoLoteDetalleExcel(int loteId, DateTime desde, DateTime hasta)
        {
            var cs = _configuration.GetConnectionString("CadenaMeatTIF");

            const string sql = @"
SELECT *
FROM dbo.vw_PesoLote_Detallado
WHERE LoteId = @LoteId
  AND (
        (FechaSolicitud IS NOT NULL AND FechaSolicitud >= @Desde AND FechaSolicitud <= @Hasta)
        OR
        (FechaSolicitud IS NULL AND FechaProduccion >= @Desde AND FechaProduccion <= @Hasta)
      )
ORDER BY FechaProduccion, FechaSolicitud;";

            using var cn = new SqlConnection(cs);
            var rows = (await cn.QueryAsync<PesoLoteDetRow>(sql, new { LoteId = loteId, Desde = desde, Hasta = hasta })).ToList();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Detalle");

            ws.Cell(1, 1).Value = "ProduccionId";
            ws.Cell(1, 2).Value = "Articulo";
            ws.Cell(1, 3).Value = "NombreArticulo";
            ws.Cell(1, 4).Value = "TipoPeso";
            ws.Cell(1, 5).Value = "FechaProduccion";
            ws.Cell(1, 6).Value = "FechaSolicitud";
            ws.Cell(1, 7).Value = "Solicitante";
            ws.Cell(1, 8).Value = "Autoriza";
            ws.Cell(1, 9).Value = "Estacion";
            ws.Cell(1, 10).Value = "Accion";
            ws.Cell(1, 11).Value = "ValorAnterior";
            ws.Cell(1, 12).Value = "ValorActual";

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                ws.Cell(i + 2, 1).Value = r.ProduccionId;
                ws.Cell(i + 2, 2).Value = r.Articulo;
                ws.Cell(i + 2, 3).Value = r.NombreArticulo;
                ws.Cell(i + 2, 4).Value = r.TipoPeso;
                ws.Cell(i + 2, 5).Value = r.FechaProduccion;
                ws.Cell(i + 2, 6).Value = r.FechaSolicitud;
                ws.Cell(i + 2, 7).Value = r.Solicitante;
                ws.Cell(i + 2, 8).Value = r.Autoriza;
                ws.Cell(i + 2, 9).Value = r.Estacion;
                ws.Cell(i + 2, 10).Value = r.Accion;
                ws.Cell(i + 2, 11).Value = r.ValorAnterior;
                ws.Cell(i + 2, 12).Value = r.ValorActual;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;

            var fileName = $"PesoLote_Detalle_Lote{loteId}_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.xlsx";
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }


        // ========================= REIMPRESION ETIQUETAS =========================

        [HttpGet("Reimpresion")]
        public IActionResult Reimpresion(string source = "TIF")
        {
            source = NormalizeSource(source);

            var vm = new RVM.ReimpresionEtiquetaVM
            {
                Source = source,
                PrinterName = DefaultPrinterForSource(source),
                ClaveReporte = "3",
                EmpresaEysId = "CARNG",
                TipoImpresion = 3
            };

            return View("~/Views/ProcesosCG/Reimpresion.cshtml", vm);
        }

        // ✅ GET /ProcesosCG/InstalledPrinters?source=P1  ó  ?source=TIF
        [HttpGet("InstalledPrinters")]
        public async Task<IActionResult> InstalledPrinters(string source = "P1")
        {
            source = NormalizeSource(source);

            _logger.LogInformation(">>> HIT InstalledPrinters. User={User} source={Source}", User?.Identity?.Name, source);
            Response.Headers["X-ENDPOINT-HIT"] = "InstalledPrinters";

            try
            {
                // ✅ URL por planta
                var baseUrl = GetPrintBaseUrl(source);

                // Nota: según tu cliente, puede regresar List<string> / IEnumerable<string> / string[]
                var printersRaw = await _print.InstalledPrintersAsync(baseUrl);

                // Normaliza a lista para evitar temas de tipo
                var printers = printersRaw?.ToList() ?? new List<string>();

                // ✅ Filtrado por planta (si allow está vacío, regresa todas)
                var filtered = FilterPrintersBySource(printers, source);

                return Ok(new { ok = true, source, printers = filtered });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InstalledPrinters ERROR");
                return StatusCode(500, new { ok = false, msg = ex.Message });
            }
        }

        [HttpPost("Imprimir")]
        [Produces("application/json")]
        public async Task<IActionResult> ReimpresionImprimir([FromBody] RVM.ReimpresionRequestVM req)
        {
            _logger.LogInformation(">>> HIT Imprimir. User={User}", User?.Identity?.Name);
            Response.Headers["X-ENDPOINT-HIT"] = "Imprimir";

            try
            {
                if (req is null)
                    return BadRequest(new { ok = false, msg = "Request vacío" });

                req.Source = NormalizeSource(req.Source);
                if (req.TipoImpresion <= 0) req.TipoImpresion = 3;
                if (string.IsNullOrWhiteSpace(req.EmpresaEysId)) req.EmpresaEysId = "CARNG";

                if (string.IsNullOrWhiteSpace(req.PrinterName))
                    return BadRequest(new { ok = false, msg = "Selecciona impresora" });

                if (req.Items == null || req.Items.Count == 0)
                    return BadRequest(new { ok = false, msg = "Agrega al menos una etiqueta" });

                var cs = GetMeatConnectionString(req.Source);
                if (string.IsNullOrWhiteSpace(cs))
                    return StatusCode(500, new { ok = false, msg = $"No existe cadena de conexión para source={req.Source} (CadenaMeatP1/CadenaMeatTIF)" });

                // ✅ URL por planta para imprimir
                var baseUrl = GetPrintBaseUrl(req.Source);

                const string sqlFindProduccion = @"
SELECT TOP 1 ProduccionId
FROM Produccion
WHERE CodigoEtiqueta = @CodigoEtiqueta
ORDER BY ProduccionId DESC;";

                using var cn = new SqlConnection(cs);
                await cn.OpenAsync();

                async Task<int?> ResolveProduccionIdAsync(string code)
                {
                    if (int.TryParse(code, out var prodId))
                        return prodId;

                    return await cn.ExecuteScalarAsync<int?>(
                        sqlFindProduccion,
                        new { CodigoEtiqueta = code }
                    );
                }

                var results = new List<RVM.ReimpresionRowResultVM>();

                foreach (var it in req.Items)
                {
                    var code = (it.CodigoEtiqueta ?? "").Trim();
                    var qty = it.Cantidad <= 0 ? 0 : it.Cantidad;

                    var clave = ((it.ClaveReporte ?? "")).Trim();
                    if (string.IsNullOrWhiteSpace(clave))
                        clave = (req.ClaveReporte ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(code) || qty <= 0)
                    {
                        results.Add(new RVM.ReimpresionRowResultVM
                        {
                            CodigoEtiqueta = code,
                            Cantidad = it.Cantidad,
                            Ok = false,
                            Msg = "Código vacío o cantidad inválida"
                        });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(clave))
                    {
                        results.Add(new RVM.ReimpresionRowResultVM
                        {
                            CodigoEtiqueta = code,
                            Cantidad = qty,
                            Ok = false,
                            Msg = "Selecciona Etiquetación (ColectorId)"
                        });
                        continue;
                    }

                    var produccionId = await ResolveProduccionIdAsync(code);
                    if (produccionId == null)
                    {
                        results.Add(new RVM.ReimpresionRowResultVM
                        {
                            CodigoEtiqueta = code,
                            Cantidad = qty,
                            Ok = false,
                            Msg = $"No se encontró ProduccionId para '{code}' en source={req.Source}"
                        });
                        continue;
                    }

                    bool okAll = true;
                    string lastMsg = "";

                    for (int i = 0; i < qty; i++)
                    {
                        var innerObj = new
                        {
                            TipoImpresion = req.TipoImpresion,
                            EmpresaEysId = req.EmpresaEysId,
                            IdBusqueda = produccionId.Value.ToString(),
                            ClaveReporte = clave,          // ColectorId
                            PrinterName = req.PrinterName
                        };

                        var innerJson = JsonSerializer.Serialize(innerObj);

                        // ✅ Imprime usando URL por planta (NO BASE_URL fijo)
                        var pr = await _print.PrintAsync(baseUrl, innerJson);

                        lastMsg = pr?.Mensaje ?? "";

                        if (pr == null || pr.Estado != 0)
                        {
                            okAll = false;
                            if (string.IsNullOrWhiteSpace(lastMsg)) lastMsg = "Error al imprimir";
                            break;
                        }
                    }

                    results.Add(new RVM.ReimpresionRowResultVM
                    {
                        CodigoEtiqueta = code,
                        Cantidad = qty,
                        Ok = okAll,
                        Msg = okAll ? "OK" : lastMsg
                    });
                }

                return Ok(new
                {
                    ok = true,
                    total = results.Count,
                    okCount = results.Count(x => x.Ok),
                    failCount = results.Count(x => !x.Ok),
                    rows = results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Imprimir ERROR");
                return StatusCode(500, new { ok = false, msg = ex.Message });
            }
        }



        // ✅ GET /ProcesosCG/Etiquetaciones?source=P1  ó  ?source=TIF
        [HttpGet("Etiquetaciones")]
        [Produces("application/json")]
        public async Task<IActionResult> Etiquetaciones(string source = "TIF")
        {
            try
            {
                source = NormalizeSource(source);

                var cs = GetMeatConnectionString(source);
                if (string.IsNullOrWhiteSpace(cs))
                    return StatusCode(500, new { ok = false, msg = "No existe connection string para source." });

                // ✅ Cambia el DB según planta
                // TIF  -> TIF_CommerciaNET
                // P1   -> CommerciaNET
                var db = (source == "TIF") ? "TIF_CommerciaNET" : "CommerciaNET";

                var sql = $@"
                     SELECT
                         ColectorId,
                         Nombre AS Etiquetacion
                     FROM {db}.dbo.COLECTOR
                     WHERE SistemaId = 'eti'
                     ORDER BY Nombre;";

                using var cn = new SqlConnection(cs);
                var rows = (await cn.QueryAsync(sql)).ToList();

                return Ok(new { ok = true, source, rows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Etiquetaciones ERROR");
                return StatusCode(500, new { ok = false, msg = ex.Message });
            }
        }


        // ========================= Helpers =========================

        private static string NormalizeSource(string source)
        {
            var s = (source ?? "P1").Trim().ToUpper();
            return (s == "TIF") ? "TIF" : "P1";
        }

        private string GetMeatConnectionString(string source)
        {
            var s = NormalizeSource(source);
            var key = (s == "TIF") ? "CadenaMeatTIF" : "CadenaMeatP1";
            return _configuration.GetConnectionString(key);
        }

        private static string DefaultPrinterForSource(string source)
        {
            var s = NormalizeSource(source);

            // ⚠️ Ajusta nombres reales
            return (s == "TIF") ? "ZD JAIME_TIF" : "ZD JAIME_P1";
        }

        private static List<string> FilterPrintersBySource(IEnumerable<string> printers, string source)
        {
            var s = NormalizeSource(source);

            // ⚠️ Agrega nombres EXACTOS como los regresa InstalledPrintersAsync
            // Si lo dejas vacío, NO filtra (regresa todas).
            var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (s == "P1")
            {
                allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // "ZD JAIME_P1",
                    // "ZEBRA_P1_02",
                };
            }
            else // TIF
            {
                allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // "ZD JAIME_TIF",
                    // "ZEBRA_TIF_02",
                };
            }

            var list = (printers ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p)
                .ToList();

            if (allow.Count == 0) return list;

            return list.Where(p => allow.Contains(p)).ToList();
        }

        private string GetPrintBaseUrl(string source)
        {
            var s = NormalizeSource(source);

            // appsettings.json recomendado:
            // "PrintRestService": {
            //   "BaseUrlP1":  "http://x.x.x.x/PrintRestService/PrintRestService.svc",
            //   "BaseUrlTIF": "http://10.1.1.2/PrintRestService/PrintRestService.svc"
            // }

            var urlP1 = _configuration["PrintRestService:BaseUrlP1"];
            var urlTif = _configuration["PrintRestService:BaseUrlTIF"];

            // fallback a BaseUrl (si solo existe uno)
            if (string.IsNullOrWhiteSpace(urlP1))
                urlP1 = _configuration["PrintRestService:BaseUrl"];

            if (string.IsNullOrWhiteSpace(urlTif))
                urlTif = _configuration["PrintRestService:BaseUrl"];

            return (s == "TIF") ? urlTif : urlP1;
        }


        [HttpGet("AutoSapStatus")]
        public IActionResult AutoSapStatus([FromQuery] string source = "P1")
        {
            // si piden ALL, regresamos ambos
            if ((source ?? "").Trim().ToUpper() == "ALL")
            {
                var all = _autoStore.GetAll();
                return Json(new { ok = true, all });
            }

            var s = _autoStore.Get(source);
            return Json(new { ok = true, enabled = s.Enabled, source = s.Source, intervalMs = s.IntervalMs });
        }

        [HttpPost("AutoSapSet")]
        public IActionResult AutoSapSet([FromForm] bool enabled, [FromForm] string source, [FromForm] int intervalMs)
        {
            var src = (source ?? "P1").Trim().ToUpper();

            if (src == "ALL")
            {
                _autoStore.Set(new AutoSapSettings { Enabled = enabled, Source = "P1", IntervalMs = intervalMs });
                _autoStore.Set(new AutoSapSettings { Enabled = enabled, Source = "TIF", IntervalMs = intervalMs });

                return Json(new { ok = true, msg = enabled ? "Auto SAP activado (P1+TIF)" : "Auto SAP desactivado (P1+TIF)" });
            }

            _autoStore.Set(new AutoSapSettings
            {
                Enabled = enabled,
                Source = src == "TIF" ? "TIF" : "P1",
                IntervalMs = intervalMs <= 0 ? 5000 : intervalMs
            });

            return Json(new { ok = true, msg = enabled ? $"Auto SAP activado ({src})" : $"Auto SAP desactivado ({src})" });
        }


        [HttpPost("EnviarFacturaReserva")]
        public async Task<IActionResult> EnviarFacturaReserva([FromForm] string referencia, [FromForm] string source = "P1")
        {
            var sapEndpoint = "Invoices";

            try
            {
                _logger.LogInformation("Reserva: INICIO. Ref={Ref} Source={Source}", referencia, source);

                // 1) Construir JSON
                _logger.LogInformation("Reserva: construyendo JSON. Ref={Ref}", referencia);
                var json = await _data.BuildReserveInvoiceJsonAsync(referencia, source);

                if (string.IsNullOrWhiteSpace(json))
                    return BadRequest(new { ok = false, msg = "No se pudo construir el JSON de factura de reserva.", referencia });

                // 2) Extraer U_DocMeat / NumAtCard
                string? uDocMeat = null;
                string? numAtCard = null;

                try
                {
                    using var jd = JsonDocument.Parse(json);
                    var root = jd.RootElement;

                    if (root.TryGetProperty("U_DocMeat", out var p1) && p1.ValueKind == JsonValueKind.String)
                        uDocMeat = p1.GetString();

                    if (root.TryGetProperty("NumAtCard", out var p2) && p2.ValueKind == JsonValueKind.String)
                        numAtCard = p2.GetString();
                }
                catch (Exception exParse)
                {
                    _logger.LogWarning(exParse, "Reserva: no se pudo parsear JSON para U_DocMeat/NumAtCard. Ref={Ref}", referencia);
                }

                _logger.LogInformation("Reserva: U_DocMeat={U} NumAtCard={N} Ref={Ref}", uDocMeat, numAtCard, referencia);

                // 3) Anti-duplicado en SAP
                _logger.LogInformation("Reserva: buscando factura en SAP. Ref={Ref}", referencia);
                var (found, docEntryExist, docNumExist) = await BuscarFacturaEnSapAsync(uDocMeat ?? "", numAtCard ?? "");

                if (found)
                {
                    await UpsertEntregaSapLogAsync(referencia, source, true, "Factura de reserva ya existe en SAP.", docEntryExist, docNumExist);

                    return Ok(new
                    {
                        ok = true,
                        yaExiste = true,
                        msg = "Factura de reserva ya existe en SAP.",
                        referencia,
                        docEntry = docEntryExist,
                        docNum = docNumExist
                    });
                }

                // 4) POST a SAP
                _logger.LogInformation("Reserva: POST a SAP endpoint={Endpoint}. Ref={Ref}", sapEndpoint, referencia);
                var r = await _sap.PostJsonAsync(sapEndpoint, json);

                _logger.LogInformation("Reserva: respuesta SAP ok={Ok}. Ref={Ref}", r.ok, referencia);

                if (!r.ok)
                {
                    await UpsertEntregaSapLogAsync(referencia, source, false, r.error ?? "No se pudo enviar factura de reserva a SAP.");

                    return BadRequest(new
                    {
                        ok = false,
                        msg = "No se pudo enviar la Factura de Reserva a SAP.",
                        referencia,
                        error = r.error,
                        detalle = r.response
                    });
                }

                // 5) Parse DocEntry/DocNum
                int? docEntry = null;
                int? docNum = null;

                try
                {
                    using var doc = JsonDocument.Parse(r.response);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("DocEntry", out var a) && a.ValueKind == JsonValueKind.Number) docEntry = a.GetInt32();
                    if (root.TryGetProperty("DocNum", out var b) && b.ValueKind == JsonValueKind.Number) docNum = b.GetInt32();
                }
                catch (Exception exParseResp)
                {
                    _logger.LogWarning(exParseResp, "Reserva: no se pudo parsear respuesta SAP (DocEntry/DocNum). Ref={Ref} Resp={Resp}", referencia, r.response);
                }

                _logger.LogInformation("Reserva: guardando log OK. Ref={Ref} DocEntry={DocEntry} DocNum={DocNum}", referencia, docEntry, docNum);
                await UpsertEntregaSapLogAsync(referencia, source, true, "Factura de reserva enviada con éxito.", docEntry, docNum);

                return Ok(new
                {
                    ok = true,
                    yaExiste = false,
                    msg = "Factura de reserva enviada con éxito a SAP.",
                    referencia,
                    docEntry,
                    docNum
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reserva: ERROR. Ref={Ref} Source={Source}", referencia, source);

                // intenta guardar log, pero que NO tape el error real si también falla el log
                try
                {
                    await UpsertEntregaSapLogAsync(referencia, source, false, ex.Message);
                }
                catch (Exception exLog)
                {
                    _logger.LogError(exLog, "Reserva: falló UpsertEntregaSapLogAsync. Ref={Ref}", referencia);
                }

                // DEBUG: regresa detalle
                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error interno al enviar factura de reserva.",
                    referencia,
                    error = ex.Message,
                    inner = ex.InnerException?.Message,
                    stack = ex.StackTrace
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EnviarFacturaReservaKg([FromBody] ReservaKgVM vm)
        {
            if (vm == null || vm.Kg <= 0)
                return Json(new { ok = false, msg = "KG inválido" });

            try
            {
                // 1️⃣ Generar factura de reserva BASE (YA EXISTE EN TU SERVICE)
                var json = await _data.BuildReserveInvoiceJsonAsync(
                    vm.Referencia,
                    vm.Source
                );

                if (string.IsNullOrWhiteSpace(json) || json == "{}")
                    return Json(new { ok = false, msg = "No se pudo generar la factura base" });

                // 2️⃣ Parsear JSON
                var doc = JsonNode.Parse(json)?.AsObject();
                if (doc == null)
                    return Json(new { ok = false, msg = "JSON inválido" });

                var lines = doc["DocumentLines"]?.AsArray();
                if (lines == null || lines.Count == 0)
                    return Json(new { ok = false, msg = "Factura sin líneas" });

                // 3️⃣ Calcular KG disponibles
                decimal kgDisponible = 0m;
                foreach (var l in lines.OfType<JsonObject>())
                {
                    if (l["Quantity"] != null)
                        kgDisponible += l["Quantity"]!.GetValue<decimal>();
                }

                if (vm.Kg > kgDisponible)
                {
                    return Json(new
                    {
                        ok = false,
                        msg = $"KG excede el disponible ({kgDisponible:N2})"
                    });
                }

                // 4️⃣ Prorratear SOLO KG
                decimal restante = vm.Kg;

                foreach (var l in lines.OfType<JsonObject>())
                {
                    if (restante <= 0)
                    {
                        l["Quantity"] = 0;
                        continue;
                    }

                    var original = l["Quantity"]!.GetValue<decimal>();
                    var usar = Math.Min(original, restante);

                    l["Quantity"] = usar;
                    restante -= usar;
                }

                // 5️⃣ Comentarios de auditoría
                doc["Comments"] =
                    $"FACTURA DE RESERVA MANUAL POR KG\n" +
                    $"Referencia: {vm.Referencia}\n" +
                    $"KG: {vm.Kg:N2}\n" +
                    $"Usuario: {User.Identity?.Name}\n" +
                    $"Fecha: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";

                // 6️⃣ Enviar a SAP (MISMO CLIENTE QUE YA USAS)
                var result = await _sap.PostJsonAsync(
                       "Invoices",
                       doc.ToJsonString()
                   );

                if (!result.ok)
                {
                    _logger.LogError(
                        "Error SAP Reserva Manual | Ref {ref} | {err}",
                        vm.Referencia,
                        result.error
                    );

                    return Json(new
                    {
                        ok = false,
                        msg = "Error al enviar factura de reserva manual",
                        error = result.error,
                        response = result.response
                    });
                }

                return Json(new
                {
                    ok = true,
                    msg = "Factura de reserva MANUAL enviada correctamente"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error Reserva Manual KG");
                return Json(new { ok = false, msg = ex.Message });
            }
        }



        [HttpGet("GetReservaManualLines")]
        public async Task<IActionResult> GetReservaManualLines(string referencia, string source)
        {
            // 🔥 USAS LA ENTREGA ORIGINAL, NO SAP
            var json = await _data.BuildJsonAsync(referencia, source);

            if (string.IsNullOrWhiteSpace(json))
                return Ok(Array.Empty<object>());

            var obj = JsonNode.Parse(json) as JsonObject;
            var lines = obj?["DocumentLines"] as JsonArray;

            if (lines == null)
                return Ok(Array.Empty<object>());

            var result = lines.Select(l => new
            {
                ItemCode = l!["ItemCode"]!.GetValue<string>(),
                BaseLine = l["LineNum"]?.GetValue<int>() ?? 0,
                QuantityOriginal = l["Quantity"]!.GetValue<decimal>(),
                Quantity = 0m   // 👈 el usuario solo toca esto
            });

            return Ok(result);
        }


        [HttpGet("GetEntregaLines")]
        [RevisarPermiso("ENTREGAS_SAP", "ESCRIBIR")]
        public async Task<IActionResult> GetEntregaLines(string referencia, string source)
        {
            // 🔥 USAR ENTREGA JSON (NO SAP)
            var json = await _data.BuildJsonAsync(referencia, source);

            if (string.IsNullOrWhiteSpace(json))
                return Ok(Array.Empty<object>());

            var root = JsonNode.Parse(json) as JsonObject;
            var lines = root?["DocumentLines"] as JsonArray;

            if (lines == null || lines.Count == 0)
                return Ok(Array.Empty<object>());

            var result = lines.Select(l => new
            {
                baseLine = l!["BaseLine"]?.GetValue<int>()
                           ?? l["LineNum"]?.GetValue<int>()
                           ?? 0,

                itemCode = l["ItemCode"]!.GetValue<string>(),

                openQuantity = l["Quantity"]!.GetValue<decimal>()
            });

            return Ok(result);
        }


        [HttpPost("EnviarFacturaReservaManual")]
        public async Task<IActionResult> EnviarFacturaReservaManual(
            [FromBody] ReservaManualRequest req)
        {
            var json = await _data.BuildReserveInvoiceJsonManualAsync(
                    req.Referencia,
                    req.Source,
                    req.Lineas);

            var login = await _sap.EnsureLoginAsync();
            if (!login.ok)
                return StatusCode(500, login.error);

            var resp = await _sap.PostJsonAsync("/Invoices", json);
            if (!resp.ok)
                return StatusCode(500, resp.error);

            return Ok(resp.response);
        }




        // ========================= COSTEO =========================

        [HttpGet("Costeos")]
        public IActionResult Costeos()
        {
            return View("~/Views/ProcesosCG/Costeos.cshtml");
        }

        [HttpGet("Costeo")]
        public async Task<IActionResult> Costeo([FromQuery] CosteoFiltroVM model)
        {
            model ??= new CosteoFiltroVM();

            model.Source = string.IsNullOrWhiteSpace(model.Source) ? "P1" : NormalizeSource(model.Source);
            model.TipoProceso = string.IsNullOrWhiteSpace(model.TipoProceso) ? "CAJAS" : model.TipoProceso.Trim().ToUpper();
            model.Modo = string.IsNullOrWhiteSpace(model.Modo) ? "DIA" : model.Modo.Trim().ToUpper();

            if (model.FechaInicial == default) model.FechaInicial = DateTime.Today;
            if (model.FechaFinal == default) model.FechaFinal = model.FechaInicial;

            using var cn = new SqlConnection(_configuration.GetConnectionString("CadenaMeatTIF"));

            var bitacora = (await cn.QueryAsync<Plataforma_CG.ViewModels.CosteoBitacoraRowVM>(@"
SELECT TOP 100
    Id,
    FechaEjecucion,
    FechaInicioReal,
    FechaFinReal,
    Source,
    TipoProceso,
    SpEjecutado,
    FechaInicial,
    FechaFinal,
    LoteId,
    TipoCosteoId,
    CONVERT(varchar(5), HoraProgramada, 108) AS HoraProgramada,
    EsAutomatico,
    BrincarSinCosto,
    ContinuarConError,
    Ok,
    Mensaje,
    Usuario,
    ISNULL(Parametros,
        CONCAT(
            'FI=', ISNULL(CONVERT(varchar(10), FechaInicial, 23), 'NULL'),
            ', FF=', ISNULL(CONVERT(varchar(10), FechaFinal, 23), 'NULL'),
            ', LoteId=', ISNULL(CONVERT(varchar(20), LoteId), 'NULL'),
            ', HoraProgramada=', ISNULL(CONVERT(varchar(5), HoraProgramada, 108), 'NULL'),
            ', EsAutomatico=', ISNULL(CONVERT(varchar(5), EsAutomatico), 'NULL'),
            ', BrincarSinCosto=', ISNULL(CONVERT(varchar(5), BrincarSinCosto), 'NULL'),
            ', ContinuarConError=', ISNULL(CONVERT(varchar(5), ContinuarConError), 'NULL')
        )
    ) AS Parametros
FROM dbo.meat_CosteoBitacora
ORDER BY FechaEjecucion DESC, Id DESC;")).ToList();

            model.Resultados = bitacora;

            return View("~/Views/ProcesosCG/Costeo.cshtml", model);
        }

        [HttpPost("EjecutarCosteo")]
        public async Task<IActionResult> EjecutarCosteo(
     [FromBody] Plataforma_CG.ViewModels.CosteoFiltroVM model,
     [FromServices] Plataforma_CG.Services.ICosteoRunnerService runner)
        {
            try
            {
                if (model == null)
                    return BadRequest(new { ok = false, msg = "Modelo vacío." });

                var results = await runner.EjecutarAsync(model, model.Automatico);

                return Ok(new
                {
                    ok = true,
                    total = results.Count,
                    results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar costeo");
                return StatusCode(500, new { ok = false, msg = ex.Message });
            }
        }

        [HttpGet("CosteoBitacora")]
        public async Task<IActionResult> CosteoBitacora(
      int top = 100,
      string source = null,
      string tipoProceso = null,
      DateTime? fechaDesde = null,
      DateTime? fechaHasta = null,
      bool? soloErrores = null,
      bool? soloAutomatico = null)
        {
            try
            {
                using var cn = new SqlConnection(_configuration.GetConnectionString("CadenaMeatTIF"));

                var sql = @"
SELECT TOP (@Top)
    Id,
    FechaEjecucion,
    FechaInicioReal,
    FechaFinReal,
    Source,
    TipoProceso,
    SpEjecutado,
    FechaInicial,
    FechaFinal,
    LoteId,
    TipoCosteoId,
    CONVERT(varchar(5), HoraProgramada, 108) AS HoraProgramada,
    EsAutomatico,
    BrincarSinCosto,
    ContinuarConError,
    Ok,
    Mensaje,
    Usuario,
    ISNULL(Parametros,
        CONCAT(
            'FI=', ISNULL(CONVERT(varchar(10), FechaInicial, 23), 'NULL'),
            ', FF=', ISNULL(CONVERT(varchar(10), FechaFinal, 23), 'NULL'),
            ', LoteId=', ISNULL(CONVERT(varchar(20), LoteId), 'NULL'),
            ', HoraProgramada=', ISNULL(CONVERT(varchar(5), HoraProgramada, 108), 'NULL'),
            ', EsAutomatico=', ISNULL(CONVERT(varchar(5), EsAutomatico), 'NULL'),
            ', BrincarSinCosto=', ISNULL(CONVERT(varchar(5), BrincarSinCosto), 'NULL'),
            ', ContinuarConError=', ISNULL(CONVERT(varchar(5), ContinuarConError), 'NULL')
        )
    ) AS Parametros
FROM dbo.meat_CosteoBitacora
WHERE 1 = 1
  AND (@Source IS NULL OR Source = @Source)
  AND (@TipoProceso IS NULL OR TipoProceso = @TipoProceso)
  AND (@FechaDesde IS NULL OR FechaEjecucion >= @FechaDesde)
  AND (@FechaHasta IS NULL OR FechaEjecucion < DATEADD(DAY, 1, @FechaHasta))
  AND (@SoloErrores IS NULL OR (@SoloErrores = 1 AND Ok = 0) OR (@SoloErrores = 0))
  AND (@SoloAutomatico IS NULL OR (@SoloAutomatico = 1 AND EsAutomatico = 1) OR (@SoloAutomatico = 0))
ORDER BY FechaEjecucion DESC, Id DESC;";

                var rows = await cn.QueryAsync(sql, new
                {
                    Top = top <= 0 ? 100 : top,
                    Source = string.IsNullOrWhiteSpace(source) || source == "ALL" ? null : source.Trim().ToUpper(),
                    TipoProceso = string.IsNullOrWhiteSpace(tipoProceso) || tipoProceso == "AMBOS" ? null : tipoProceso.Trim().ToUpper(),
                    FechaDesde = fechaDesde,
                    FechaHasta = fechaHasta,
                    SoloErrores = soloErrores,
                    SoloAutomatico = soloAutomatico
                });

                return Json(new { ok = true, rows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar bitácora de costeo");
                return StatusCode(500, new { ok = false, msg = ex.Message });
            }
        }
        [HttpGet("CosteoAutoStatus")]
        public async Task<IActionResult> CosteoAutoStatus()
        {
            try
            {
                using var cn = new SqlConnection(_configuration.GetConnectionString("CadenaMeatTIF"));

                var rows = await cn.QueryAsync(@"
SELECT
    Id,
    Source,
    TipoProceso,
    TipoCosteoId,
    CONVERT(varchar(5), HoraProgramada, 108) AS HoraProgramada,
    BrincarSinCosto,
    ContinuarConError,
    Activo,
    UsuarioAlta,
    FechaAlta
FROM dbo.meat_CosteoProgramado
ORDER BY Source, TipoProceso, FechaAlta DESC, Id DESC;");

                return Json(new { ok = true, rows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar configuración automática de costeo");
                return StatusCode(500, new { ok = false, msg = ex.Message });
            }
        }

        [HttpPost("CosteoAutoSet")]
        public async Task<IActionResult> CosteoAutoSet(
            [FromForm] string source,
            [FromForm] string tipoProceso,
            [FromForm] int tipoCosteoId,
            [FromForm] string horaProgramada,
            [FromForm] bool brincarSinCosto,
            [FromForm] bool continuarConError,
            [FromForm] bool activo)
        {
            try
            {
                var src = (source ?? "P1").Trim().ToUpper();
                var proc = (tipoProceso ?? "CAJAS").Trim().ToUpper();
                var user = User?.Identity?.Name ?? "sistema";

                using var cn = new SqlConnection(_configuration.GetConnectionString("CadenaMeatTIF"));

                await cn.ExecuteAsync(@"
IF EXISTS (
    SELECT 1
    FROM dbo.meat_CosteoProgramado
    WHERE Source = @Source
      AND TipoProceso = @TipoProceso
)
BEGIN
    UPDATE dbo.meat_CosteoProgramado
       SET TipoCosteoId = @TipoCosteoId,
           HoraProgramada = CAST(@HoraProgramada AS time),
           BrincarSinCosto = @BrincarSinCosto,
           ContinuarConError = @ContinuarConError,
           Activo = @Activo,
           UsuarioModifica = @UsuarioModifica,
           FechaModifica = GETDATE()
     WHERE Source = @Source
       AND TipoProceso = @TipoProceso;
END
ELSE
BEGIN
    INSERT INTO dbo.meat_CosteoProgramado
    (
        Source,
        TipoProceso,
        TipoCosteoId,
        HoraProgramada,
        BrincarSinCosto,
        ContinuarConError,
        Activo,
        UsuarioAlta,
        FechaAlta
    )
    VALUES
    (
        @Source,
        @TipoProceso,
        @TipoCosteoId,
        CAST(@HoraProgramada AS time),
        @BrincarSinCosto,
        @ContinuarConError,
        @Activo,
        @UsuarioAlta,
        GETDATE()
    );
END
", new
                {
                    Source = src,
                    TipoProceso = proc,
                    TipoCosteoId = tipoCosteoId,
                    HoraProgramada = string.IsNullOrWhiteSpace(horaProgramada) ? "18:00" : horaProgramada,
                    BrincarSinCosto = brincarSinCosto,
                    ContinuarConError = continuarConError,
                    Activo = activo,
                    UsuarioAlta = user,
                    UsuarioModifica = user
                });

                return Json(new { ok = true, msg = "Configuración guardada correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar programación automática de costeo");
                return StatusCode(500, new { ok = false, msg = ex.Message });
            }
        }

        private async Task<object> EjecutarSpCosteoInterno(
            SqlConnection cn,
            string source,
            string tipoProceso,
            string spName,
            Plataforma_CG.ViewModels.CosteoFiltroVM model,
            bool esAutomatico = false,
            string horaProgramada = null)
        {
            var inicio = DateTime.Now;
            var ok = true;
            var msg = "OK";

            try
            {
                await cn.ExecuteAsync(
                    spName,
                    new
                    {
                        FechaInicial = model.FechaInicial.Date,
                        FechaFinal = model.FechaFinal.Date,
                        TipoCosteoId = model.TipoCosteoId,
                        LoteId = model.LoteId,
                        BrincarSinCosto = model.BrincarSinCosto,
                        ContinuarConError = model.ContinuarConError
                    },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 0
                );
            }
            catch (Exception ex)
            {
                ok = false;
                msg = ex.Message;
            }

            var fin = DateTime.Now;

            await GuardarBitacoraCosteoAsync(
                source,
                tipoProceso,
                spName,
                model,
                ok,
                msg,
                inicio,
                fin,
                esAutomatico,
                horaProgramada
            );

            return new
            {
                source,
                tipoProceso,
                spEjecutado = spName,
                fechaInicial = model.FechaInicial.ToString("yyyy-MM-dd"),
                fechaFinal = model.FechaFinal.ToString("yyyy-MM-dd"),
                loteId = model.LoteId,
                tipoCosteoId = model.TipoCosteoId,
                horaProgramada,
                esAutomatico,
                brincarSinCosto = model.BrincarSinCosto,
                continuarConError = model.ContinuarConError,
                ok,
                msg,
                inicio,
                fin
            };
        }

        private async Task GuardarBitacoraCosteoAsync(
            string source,
            string tipoProceso,
            string spEjecutado,
            Plataforma_CG.ViewModels.CosteoFiltroVM model,
            bool ok,
            string mensaje,
            DateTime? fechaInicioReal,
            DateTime? fechaFinReal,
            bool esAutomatico,
            string horaProgramada)
        {
            try
            {
                using var cn = new SqlConnection(_configuration.GetConnectionString("CadenaMeatTIF"));

                var usuario = esAutomatico
                    ? "SISTEMA"
                    : (User?.Identity?.Name ?? "sistema");

                var msgFinal = mensaje ?? "";
                if (msgFinal.Length > 2000)
                    msgFinal = msgFinal.Substring(0, 2000);

                await cn.ExecuteAsync(@"
INSERT INTO dbo.meat_CosteoBitacora
(
    FechaEjecucion,
    FechaInicioReal,
    FechaFinReal,
    Source,
    TipoProceso,
    SpEjecutado,
    FechaInicial,
    FechaFinal,
    LoteId,
    TipoCosteoId,
    HoraProgramada,
    EsAutomatico,
    BrincarSinCosto,
    ContinuarConError,
    Ok,
    Mensaje,
    Usuario,
    Parametros
)
VALUES
(
    GETDATE(),
    @FechaInicioReal,
    @FechaFinReal,
    @Source,
    @TipoProceso,
    @SpEjecutado,
    @FechaInicial,
    @FechaFinal,
    @LoteId,
    @TipoCosteoId,
    CAST(@HoraProgramada AS time),
    @EsAutomatico,
    @BrincarSinCosto,
    @ContinuarConError,
    @Ok,
    @Mensaje,
    @Usuario,
    @Parametros
);",
                new
                {
                    FechaInicioReal = fechaInicioReal,
                    FechaFinReal = fechaFinReal,
                    Source = source,
                    TipoProceso = tipoProceso,
                    SpEjecutado = spEjecutado,
                    FechaInicial = model.FechaInicial == default ? (DateTime?)null : model.FechaInicial.Date,
                    FechaFinal = model.FechaFinal == default ? (DateTime?)null : model.FechaFinal.Date,
                    LoteId = model.LoteId,
                    TipoCosteoId = model.TipoCosteoId,
                    HoraProgramada = string.IsNullOrWhiteSpace(horaProgramada) ? null : horaProgramada,
                    EsAutomatico = esAutomatico,
                    BrincarSinCosto = model.BrincarSinCosto,
                    ContinuarConError = model.ContinuarConError,
                    Ok = ok,
                    Mensaje = msgFinal,
                    Usuario = usuario,
                    Parametros =
                        $"FI={model.FechaInicial:yyyy-MM-dd}, " +
                        $"FF={model.FechaFinal:yyyy-MM-dd}, " +
                        $"LoteId={(model.LoteId?.ToString() ?? "NULL")}, " +
                        $"TipoCosteoId={model.TipoCosteoId}, " +
                        $"HoraProgramada={horaProgramada}, " +
                        $"Automatico={esAutomatico}, " +
                        $"BrincarSinCosto={model.BrincarSinCosto}, " +
                        $"ContinuarConError={model.ContinuarConError}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo guardar bitácora de costeo");
            }
        }

        [HttpGet("CamarasInventario")]
        public IActionResult CamarasInventario()
        {
            return View("~/Views/ProcesosCG/CamarasInventario.cshtml");
        }


        [HttpGet("ObtenerCamaras")]
        public async Task<IActionResult> ObtenerCamaras(string planta = "P1")
        {
            planta = NormalizeSource(planta);

            var cs = GetMeatConnectionString(planta);

            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, new { ok = false, mensaje = $"No existe cadena para planta {planta}" });

            var dbCommercia = planta == "TIF"
                ? "TIF_CommerciaNet"
                : "CommerciaNet";

            var sql = $@"
SELECT Nombre
FROM {dbCommercia}.dbo.Almacen
WHERE Calle <> '.'
ORDER BY Nombre;";

            using var cn = new SqlConnection(cs);

            var camaras = (await cn.QueryAsync<string>(sql)).ToList();

            return Json(new
            {
                ok = true,
                planta,
                camaras
            });
        }



        [HttpGet("InventarioCamaras")]
        public async Task<IActionResult> InventarioCamaras(string planta = "P1", string camara = "")
        {
            planta = NormalizeSource(planta);

            var cs = GetMeatConnectionString(planta);

            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, new { ok = false, mensaje = $"No existe cadena para planta {planta}" });

            var dbCommercia = planta == "TIF" ? "TIF_CommerciaNet" : "CommerciaNet";
            var dbMeat = planta == "TIF" ? "TIF_Meat" : "Meat";

            camara = (camara ?? "").Trim();

            var sql = $@"
SELECT 
    '-' AS Fecha,
    alm.Nombre AS Almacen,
    CASE 
        WHEN MAX(SUBSTRING(prod.CodigoEtiqueta,1,4)) = 'SACT' 
            THEN ISNULL(a2.ArticuloId,'-') 
        ELSE prod.Articulo 
    END AS Sku,
    CASE 
        WHEN MAX(SUBSTRING(prod.CodigoEtiqueta,1,4)) = 'SACT' 
            THEN ISNULL(a2.Nombre,'Sin Clasificar') 
        ELSE a.Nombre 
    END AS Nombre,
    COUNT(1) AS Cajas,
    SUM(Prod.PesoNeto) AS Kg,
    ROUND(SUM(Prod.PesoNeto) / COUNT(1), 2) AS Prom,
    a.LineaId,
    CASE 
        WHEN Prod.Estatus = 1 THEN 'Activo' 
        ELSE 'No activo' 
    END AS Estatus,
    '-' AS Ubic
FROM Produccion Prod
INNER JOIN {dbCommercia}.dbo.Articulo a 
    ON Prod.Articulo = a.ArticuloId
INNER JOIN {dbCommercia}.dbo.Almacen alm 
    ON Prod.Almacen = alm.AlmacenId
LEFT JOIN {dbMeat}.dbo.CanalDetalle cd 
    ON Prod.ProduccionId = cd.ProduccionId
LEFT JOIN {dbCommercia}.dbo.Articulo a2 
    ON cd.ClasificacionId = a2.Clasifica1
WHERE Prod.Estatus = 1
  AND (
        @Camara = '' 
        OR LTRIM(RTRIM(alm.Nombre)) = LTRIM(RTRIM(@Camara))
      )
GROUP BY 
    alm.Nombre,
    a2.ArticuloId,
    prod.Articulo,
    a2.Nombre,
    a.Nombre,
    a.LineaId,
    prod.Estatus
ORDER BY alm.Nombre, Nombre;";

            using var cn = new SqlConnection(cs);

            var rows = (await cn.QueryAsync(sql, new
            {
                Camara = camara
            })).ToList();

            return Json(new
            {
                ok = true,
                servidor = cn.DataSource,
                bd = cn.Database,
                planta,
                camara,
                camaraLen = camara.Length,
                total = rows.Count,
                rows
            });
        }

        [HttpGet("TrazabilidadCamara")]
        public async Task<IActionResult> TrazabilidadCamara(string planta = "P1", string sku = "", string camara = "")
        {
            planta = NormalizeSource(planta);

            var cs = GetMeatConnectionString(planta);

            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, new { ok = false, mensaje = $"No existe cadena para planta {planta}" });

            sku = (sku ?? "").Trim();
            camara = (camara ?? "").Trim();

            if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(camara))
                return Json(new { ok = false, mensaje = "Falta SKU o cámara." });

            var dbCommercia = planta == "TIF" ? "TIF_CommerciaNet" : "CommerciaNet";
            var dbMeat = planta == "TIF" ? "TIF_Meat" : "Meat";
            var prefijoCanal = planta == "TIF" ? "SACT" : "SACC";

            var usaReclasificacion = planta == "TIF";

            var selectReclasificacion = usaReclasificacion
                ? "ISNULL(logp.ClasificacionPesoCaliente,'-') AS Reclasificacion,"
                : "'-' AS Reclasificacion,";

            var joinReclasificacion = usaReclasificacion
                ? @"
LEFT JOIN LogPesoCalienteReclasificacion logp 
    ON Prod.ProduccionId = logp.ProduccionId"
                : "";

            var groupByReclasificacion = usaReclasificacion
                ? @",
    logp.ClasificacionPesoCaliente"
                : "";

            var sql = $@"
SELECT
    CONVERT(date, Prod.FechaProduccion) AS Fecha,
    Prod.CodigoEtiqueta,
    CASE 
        WHEN SUBSTRING(Prod.CodigoEtiqueta,1,4) = @PrefijoCanal 
            THEN artt.ArticuloId 
        ELSE Prod.Articulo 
    END AS Sku,
    CASE 
        WHEN SUBSTRING(Prod.CodigoEtiqueta,1,4) = @PrefijoCanal 
            THEN ISNULL(cn.Nombre,'-') 
        ELSE a.Nombre 
    END AS Producto,
    Prod.PesoNeto AS Kg,
    alm.Nombre,
    lot.Nombre AS Lote,
    ISNULL(STRING_AGG(emp.Nombre,' - '),'-') AS Empaque,
    ISNULL(STRING_AGG(CONVERT(varchar(50), ct.Cantidad),' - '),'-') AS Cantidad,
    ISNULL(STRING_AGG(tar.Nombre,'-'), '-') AS Tar,
    ISNULL(STRING_AGG(CONVERT(varchar(50), tar.Estatus),'-'), '-') AS ActTar,
    ISNULL(PR.Referencia, '-') AS Ubic,

    MIN(logIngreso.FechaIngresoCamara) AS FechaIngresoCamara,

    {selectReclasificacion}
    CASE
        WHEN alm.Nombre = 'TIF CAMARA FRESCO' THEN DATEADD(DAY,30,CONVERT(date,Prod.FechaProduccion)) 
        WHEN alm.Nombre = 'TIF ALMACEN CEDIS' THEN DATEADD(DAY,365,CONVERT(date,Prod.FechaProduccion)) 
        WHEN alm.Nombre = 'TIF ALMACEN RETENCION' THEN DATEADD(DAY,365,CONVERT(date,Prod.FechaProduccion)) 
        ELSE DATEADD(DAY,365,CONVERT(date,Prod.FechaProduccion)) 
    END AS Fechas_Caducidad
FROM Produccion Prod
INNER JOIN {dbCommercia}.dbo.Articulo a 
    ON Prod.Articulo = a.ArticuloId
INNER JOIN {dbCommercia}.dbo.Almacen alm 
    ON Prod.Almacen = alm.AlmacenId  
INNER JOIN Lote lot 
    ON Prod.LoteId = lot.LoteId
LEFT JOIN {dbMeat}.dbo.CanalDetalle cd 
    ON Prod.ProduccionId = cd.ProduccionId
LEFT JOIN {dbMeat}.dbo.Clasificacion cn 
    ON cd.ClasificacionId = cn.ClasificacionId
LEFT JOIN {dbCommercia}.dbo.Articulo artt 
    ON cn.ClasificacionId = artt.Clasifica1 
LEFT JOIN TarimaDetalle tarD 
    ON tarD.ProduccionId = Prod.ProduccionId
LEFT JOIN Tarima tar 
    ON tar.TarimaId = tarD.TarimaId
LEFT JOIN ProduccionReferencia PR 
    ON PR.ProduccionId = Prod.ProduccionId
LEFT JOIN {dbMeat}.dbo.CajaTara CT 
    ON CT.ProduccionId = Prod.ProduccionId        
LEFT JOIN {dbMeat}.dbo.Empaque emp 
    ON emp.EmpaqueId = CT.EmpaqueId

OUTER APPLY (
    SELECT TOP 1
        pl.FechaHora AS FechaIngresoCamara
    FROM {dbMeat}.dbo.ProduccionLog pl
    WHERE pl.ProduccionId = Prod.ProduccionId
      AND pl.CodigoEtiqueta = Prod.CodigoEtiqueta
      AND (
            LTRIM(RTRIM(pl.Almacen)) = LTRIM(RTRIM(alm.AlmacenId))
         OR LTRIM(RTRIM(pl.Almacen)) = LTRIM(RTRIM(alm.Nombre))
      )
    ORDER BY 
        pl.FechaHora ASC,
        pl.ProduccionLogId ASC
) logIngreso

{joinReclasificacion}
WHERE 
    Prod.Estatus = 1 
    AND (
        CASE 
            WHEN SUBSTRING(Prod.CodigoEtiqueta,1,4) = @PrefijoCanal 
                THEN artt.ArticuloId 
            ELSE Prod.Articulo 
        END
    ) = @Sku
    AND LTRIM(RTRIM(alm.Nombre)) = LTRIM(RTRIM(@Camara))
GROUP BY
    Prod.CodigoEtiqueta, 
    Prod.FechaProduccion, 
    Prod.Articulo, 
    a.Nombre, 
    Prod.PesoNeto, 
    alm.Nombre, 
    alm.AlmacenId,
    lot.Nombre,
    PR.Referencia,
    cn.Nombre,
    artt.ArticuloId
    {groupByReclasificacion}
ORDER BY Prod.FechaProduccion DESC, Prod.CodigoEtiqueta;";

            using var cnSql = new SqlConnection(cs);

            var rows = (await cnSql.QueryAsync(sql, new
            {
                Sku = sku,
                Camara = camara,
                PrefijoCanal = prefijoCanal
            })).ToList();

            return Json(new
            {
                ok = true,
                planta,
                sku,
                camara,
                prefijoCanal,
                total = rows.Count,
                rows
            });
        }


        [HttpGet("DetalleCamaraCompleta")]
        public async Task<IActionResult> DetalleCamaraCompleta(string planta = "P1", string camara = "")
        {
            planta = NormalizeSource(planta);

            var cs = GetMeatConnectionString(planta);

            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, new { ok = false, mensaje = $"No existe cadena para planta {planta}" });

            camara = (camara ?? "").Trim();

            if (string.IsNullOrWhiteSpace(camara))
                return Json(new { ok = false, mensaje = "Falta cámara/almacén." });

            var dbCommercia = planta == "TIF" ? "TIF_CommerciaNet" : "CommerciaNet";
            var dbMeat = planta == "TIF" ? "TIF_Meat" : "Meat";
            var prefijoCanal = planta == "TIF" ? "SACT" : "SACC";

            var sql = $@"
SELECT
    CONVERT(date, Prod.FechaProduccion) AS Fecha,
    Prod.CodigoEtiqueta,
    CASE 
        WHEN SUBSTRING(Prod.CodigoEtiqueta,1,4) = @PrefijoCanal 
            THEN artt.ArticuloId 
        ELSE Prod.Articulo 
    END AS Sku,
    CASE 
        WHEN SUBSTRING(Prod.CodigoEtiqueta,1,4) = @PrefijoCanal 
            THEN ISNULL(cn.Nombre,'-') 
        ELSE a.Nombre 
    END AS Producto,
    Prod.PesoNeto AS Kg,
    alm.Nombre,
    lot.Nombre AS Lote,
    ISNULL(STRING_AGG(emp.Nombre,' - '),'-') AS Empaque,
    ISNULL(STRING_AGG(CONVERT(varchar(50), ct.Cantidad),' - '),'-') AS Cantidad,
    ISNULL(STRING_AGG(tar.Nombre,'-'), '-') AS Tar,
    ISNULL(STRING_AGG(CONVERT(varchar(50), tar.Estatus),'-'), '-') AS ActTar,
    ISNULL(PR.Referencia, '-') AS Ubic,
    MIN(logIngreso.FechaIngresoCamara) AS FechaIngresoCamara,
    '-' AS Reclasificacion,
    CASE
        WHEN alm.Nombre = 'TIF CAMARA FRESCO' 
            THEN DATEADD(DAY,30,CONVERT(date,Prod.FechaProduccion)) 
        ELSE DATEADD(DAY,365,CONVERT(date,Prod.FechaProduccion)) 
    END AS Fechas_Caducidad
FROM Produccion Prod
INNER JOIN {dbCommercia}.dbo.Articulo a 
    ON Prod.Articulo = a.ArticuloId
INNER JOIN {dbCommercia}.dbo.Almacen alm 
    ON Prod.Almacen = alm.AlmacenId
INNER JOIN Lote lot 
    ON Prod.LoteId = lot.LoteId
LEFT JOIN {dbMeat}.dbo.CanalDetalle cd 
    ON Prod.ProduccionId = cd.ProduccionId
LEFT JOIN {dbMeat}.dbo.Clasificacion cn 
    ON cd.ClasificacionId = cn.ClasificacionId
LEFT JOIN {dbCommercia}.dbo.Articulo artt 
    ON cn.ClasificacionId = artt.Clasifica1 
LEFT JOIN TarimaDetalle tarD 
    ON tarD.ProduccionId = Prod.ProduccionId
LEFT JOIN Tarima tar 
    ON tar.TarimaId = tarD.TarimaId
LEFT JOIN ProduccionReferencia PR 
    ON PR.ProduccionId = Prod.ProduccionId
LEFT JOIN {dbMeat}.dbo.CajaTara CT 
    ON CT.ProduccionId = Prod.ProduccionId        
LEFT JOIN {dbMeat}.dbo.Empaque emp 
    ON emp.EmpaqueId = CT.EmpaqueId

OUTER APPLY (
    SELECT TOP 1
        pl.FechaHora AS FechaIngresoCamara
    FROM {dbMeat}.dbo.ProduccionLog pl
    WHERE pl.ProduccionId = Prod.ProduccionId
      AND pl.CodigoEtiqueta = Prod.CodigoEtiqueta
      AND (
            LTRIM(RTRIM(pl.Almacen)) = LTRIM(RTRIM(alm.AlmacenId))
         OR LTRIM(RTRIM(pl.Almacen)) = LTRIM(RTRIM(alm.Nombre))
      )
    ORDER BY 
        pl.FechaHora ASC,
        pl.ProduccionLogId ASC
) logIngreso

WHERE 
    Prod.Estatus = 1
    AND LTRIM(RTRIM(alm.Nombre)) = LTRIM(RTRIM(@Camara))
GROUP BY
    Prod.CodigoEtiqueta, 
    Prod.FechaProduccion, 
    Prod.Articulo, 
    a.Nombre, 
    Prod.PesoNeto, 
    alm.Nombre,
    alm.AlmacenId,
    lot.Nombre,
    PR.Referencia,
    cn.Nombre,
    artt.ArticuloId
ORDER BY 
    PR.Referencia, 
    Producto, 
    Prod.FechaProduccion DESC;";

            using var cnSql = new SqlConnection(cs);

            var rows = (await cnSql.QueryAsync(sql, new
            {
                Camara = camara,
                PrefijoCanal = prefijoCanal
            })).ToList();

            return Json(new
            {
                ok = true,
                planta,
                camara,
                total = rows.Count,
                rows
            });
        }


        // ========================= AVISOS MOVILIZACION SENASICA =========================
        // Fuente oficial: ConnectionStrings:CadenaMeatTIF
        // Requiere la vista SQL: dbo.vw_AvisosMovilizacion_TIF

        public sealed class AvisosMovilizacionSenasicaRequest
        {
            public string Source { get; set; } = "TIF";
            public string CorreoMedico { get; set; } = "";
            public string Comentarios { get; set; } = "";
            public List<string> Solicitudes { get; set; } = new();
            public List<string> Referencias { get; set; } = new();
        }

        public sealed class AvisoMovilizacionTifRow
        {
            public string Planta { get; set; } = "";
            public string Solicitud_Surtido_Id { get; set; } = "";
            public string Venta { get; set; } = "";
            public string Cliente { get; set; } = "";
            public DateTime? Fecha_Venta { get; set; }
            public string Sku { get; set; } = "";
            public string Producto { get; set; } = "";
            public string Lote { get; set; } = "";
            public DateTime? Fecha_Sacrificio { get; set; }
            public DateTime? Fecha_Produccion { get; set; }
            public DateTime? Fecha_Caducidad { get; set; }
            public int Cuenta_De_Etiqueta { get; set; }
            public decimal Suma_De_Kg { get; set; }

            public string FechaVentaTxt => Fecha_Venta?.ToString("dd/MM/yyyy") ?? "";
            public string FechaSacrificioTxt => Fecha_Sacrificio?.ToString("dd/MM/yyyy") ?? "";
            public string FechaProduccionTxt => Fecha_Produccion?.ToString("dd/MM/yyyy") ?? "";
            public string FechaCaducidadTxt => Fecha_Caducidad?.ToString("dd/MM/yyyy") ?? "";
        }

        public sealed class AvisosMovilizacionPdfVM
        {
            public DateTime FechaGeneracion { get; set; } = DateTime.Now;
            public string Comentarios { get; set; } = "";
            public string Usuario { get; set; } = "";
            public List<AvisoMovilizacionTifRow> Rows { get; set; } = new();

            public int TotalSolicitudes => Rows.Select(x => x.Solicitud_Surtido_Id).Distinct().Count();
            public int TotalCajas => Rows.Sum(x => x.Cuenta_De_Etiqueta);
            public decimal TotalKg => Rows.Sum(x => x.Suma_De_Kg);
        }


        [HttpGet("AvisosMovilizacionPdf")]
        [RevisarPermiso("AVISOS_PDF", "LEER")]
        public async Task<IActionResult> AvisosMovilizacionPdf(
       [FromQuery] string ids,
       [FromQuery] string comentarios = "")
        {
            try
            {
                var solicitudes = ParseSolicitudesAvisoInt(ids);

                if (!solicitudes.Any())
                    return BadRequest("No se recibieron solicitudes válidas para generar el aviso.");

                if (solicitudes.Count > 50)
                    return BadRequest("Selecciona máximo 50 solicitudes por PDF para evitar tiempo de espera.");

                var solicitudesTxt = solicitudes
                    .Select(x => x.ToString())
                    .ToList();

                var rows = await ObtenerDetalleAvisosMovilizacionTifAsync(solicitudesTxt);

                if (rows == null || !rows.Any())
                    return NotFound("No se encontró información para las solicitudes seleccionadas en CadenaMeatTIF.");

                var vm = new Plataforma_CG.ViewModels.AvisosMovilizacionPdfVM
                {
                    FechaGeneracion = DateTime.Now,
                    Comentarios = comentarios ?? "",
                    Usuario = User?.Identity?.Name ?? "",
                    Rows = rows
                };

                return View("~/Views/ProcesosCG/AvisosMovilizacionPdf.cshtml", vm);
            }
            catch (SqlException ex) when (ex.Number == -2)
            {
                _logger.LogError(ex, "Timeout SQL al generar AvisosMovilizacionPdf. Ids={Ids}", ids);

                return StatusCode(504,
                    "Timeout SQL al consultar avisos de movilización. Revisa índice de solicitud_surtido_id o la vista vw_AvisosMovilizacion_TIF.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar AvisosMovilizacionPdf. Ids={Ids}", ids);
                return StatusCode(500, "Error al generar el aviso de movilización: " + ex.Message);
            }
        }


        private static List<string> ParseSolicitudesAviso(string ids)
        {
            return (ids ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<AvisoMovilizacionTifRow>> ObtenerAvisosMovilizacionTifAsync(IEnumerable<int> solicitudes)
        {
            var ids = (solicitudes ?? Array.Empty<int>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (!ids.Any())
                return new List<AvisoMovilizacionTifRow>();

            var cs = _configuration.GetConnectionString("CadenaMeatTIF");

            if (string.IsNullOrWhiteSpace(cs))
                throw new Exception("No existe la cadena de conexión 'CadenaMeatTIF' en appsettings.");

            const string sql = @"
SELECT
    planta AS Planta,
    CONVERT(nvarchar(50), solicitud_surtido_id) AS Solicitud_Surtido_Id,
    venta AS Venta,
    cliente AS Cliente,
    fecha_venta AS Fecha_Venta,
    sku AS Sku,
    producto AS Producto,
    lote AS Lote,
    fecha_sacrificio AS Fecha_Sacrificio,
    fecha_produccion AS Fecha_Produccion,
    fecha_caducidad AS Fecha_Caducidad,
    cuenta_de_etiqueta AS Cuenta_De_Etiqueta,
    suma_de_kg AS Suma_De_Kg
FROM dbo.vw_AvisosMovilizacion_TIF
WHERE solicitud_surtido_id IN @Solicitudes
ORDER BY
    fecha_venta,
    venta,
    cliente,
    sku,
    producto,
    lote,
    fecha_produccion,
    fecha_caducidad
OPTION (RECOMPILE);";

            using var cn = new SqlConnection(cs);

            var rows = await cn.QueryAsync<AvisoMovilizacionTifRow>(
                new CommandDefinition(
                    sql,
                    new { Solicitudes = ids },
                    commandTimeout: 180
                )
            );

            return rows.ToList();
        }

        private static string BuildAvisosMovilizacionHtml(
            List<AvisoMovilizacionTifRow> rows,
            string comentarios,
            string usuario)
        {
            static string H(string value)
                => System.Net.WebUtility.HtmlEncode(value ?? "");

            var totalSolicitudes = rows.Select(x => x.Solicitud_Surtido_Id).Distinct().Count();
            var totalCajas = rows.Sum(x => x.Cuenta_De_Etiqueta);
            var totalKg = rows.Sum(x => x.Suma_De_Kg);

            var sb = new System.Text.StringBuilder();

            sb.Append($@"
<html>
<head>
<meta charset=""utf-8"">
<style>
    body {{ font-family: Arial, Helvetica, sans-serif; color:#222; font-size:12px; }}
    h2 {{ color:#8b0000; margin-bottom:4px; }}
    .muted {{ color:#666; }}
    .summary {{ margin:12px 0; padding:10px; background:#f7eeee; border:1px solid #c99; }}
    table {{ border-collapse:collapse; width:100%; }}
    th {{ background:#8b0000; color:#fff; padding:6px; border:1px solid #6b0000; }}
    td {{ padding:5px; border:1px solid #ddd; }}
    .right {{ text-align:right; }}
</style>
</head>
<body>
    <h2>Avisos de movilización TIF</h2>
    <div class=""muted"">Generado: {DateTime.Now:dd/MM/yyyy HH:mm} | Usuario: {H(usuario)}</div>

    <div class=""summary"">
        <strong>Total solicitudes:</strong> {totalSolicitudes}<br>
        <strong>Total cajas:</strong> {totalCajas:N0}<br>
        <strong>Total kg:</strong> {totalKg:N3}
    </div>");

            if (!string.IsNullOrWhiteSpace(comentarios))
            {
                sb.Append($@"
    <div class=""summary"">
        <strong>Comentarios:</strong><br>
        {H(comentarios)}
    </div>");
            }

            sb.Append(@"
    <table>
        <thead>
            <tr>
                <th>Solicitud</th>
                <th>Venta</th>
                <th>Cliente</th>
                <th>Fecha venta</th>
                <th>SKU</th>
                <th>Producto</th>
                <th>Lote</th>
                <th>F. sacrificio</th>
                <th>F. producción</th>
                <th>F. caducidad</th>
                <th>Cajas</th>
                <th>Kg</th>
            </tr>
        </thead>
        <tbody>");

            foreach (var r in rows)
            {
                sb.Append($@"
            <tr>
                <td>{H(r.Solicitud_Surtido_Id)}</td>
                <td>{H(r.Venta)}</td>
                <td>{H(r.Cliente)}</td>
                <td>{H(r.FechaVentaTxt)}</td>
                <td>{H(r.Sku)}</td>
                <td>{H(r.Producto)}</td>
                <td>{H(r.Lote)}</td>
                <td>{H(r.FechaSacrificioTxt)}</td>
                <td>{H(r.FechaProduccionTxt)}</td>
                <td>{H(r.FechaCaducidadTxt)}</td>
                <td class=""right"">{r.Cuenta_De_Etiqueta:N0}</td>
                <td class=""right"">{r.Suma_De_Kg:N3}</td>
            </tr>");
            }

            sb.Append(@"
        </tbody>
    </table>
</body>
</html>");

            return sb.ToString();
        }

        private static byte[] CrearExcelAvisosMovilizacion(List<AvisoMovilizacionTifRow> rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Avisos");

            var headers = new[]
            {
                "Planta",
                "Solicitud",
                "Venta",
                "Cliente",
                "Fecha venta",
                "SKU",
                "Producto",
                "Lote",
                "Fecha sacrificio",
                "Fecha producción",
                "Fecha caducidad",
                "Cajas",
                "Kg"
            };

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
                ws.Cell(1, c + 1).Style.Font.Bold = true;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var row = i + 2;

                ws.Cell(row, 1).Value = r.Planta;
                ws.Cell(row, 2).Value = r.Solicitud_Surtido_Id;
                ws.Cell(row, 3).Value = r.Venta;
                ws.Cell(row, 4).Value = r.Cliente;
                ws.Cell(row, 5).Value = r.Fecha_Venta;
                ws.Cell(row, 6).Value = r.Sku;
                ws.Cell(row, 7).Value = r.Producto;
                ws.Cell(row, 8).Value = r.Lote;
                ws.Cell(row, 9).Value = r.Fecha_Sacrificio;
                ws.Cell(row, 10).Value = r.Fecha_Produccion;
                ws.Cell(row, 11).Value = r.Fecha_Caducidad;
                ws.Cell(row, 12).Value = r.Cuenta_De_Etiqueta;
                ws.Cell(row, 13).Value = r.Suma_De_Kg;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private async Task EnviarCorreoAvisosSenasicaAsync(
            string destinatarios,
            string asunto,
            string html,
            byte[] attachmentBytes,
            string attachmentFileName)
        {
            var host = (_configuration["Smtp:Host"] ?? "").Trim();

            if (string.IsNullOrWhiteSpace(host))
                throw new Exception("No existe configuración Smtp:Host en appsettings.");

            var from = (_configuration["Smtp:From"] ?? "").Trim();
            var user = (_configuration["Smtp:User"] ?? "").Trim();
            var password = _configuration["Smtp:Password"] ?? "";

            if (string.IsNullOrWhiteSpace(from))
                from = user;

            if (string.IsNullOrWhiteSpace(from))
                throw new Exception("No existe configuración Smtp:From o Smtp:User en appsettings.");

            var port = 587;
            if (int.TryParse(_configuration["Smtp:Port"], out var p))
                port = p;

            var enableSsl = true;
            if (bool.TryParse(_configuration["Smtp:EnableSsl"], out var ssl))
                enableSsl = ssl;

            using var msg = new System.Net.Mail.MailMessage();
            msg.From = new System.Net.Mail.MailAddress(from);
            msg.Subject = asunto;
            msg.Body = html;
            msg.IsBodyHtml = true;

            foreach (var email in SplitEmails(destinatarios))
                msg.To.Add(email);

            if (msg.To.Count == 0)
                throw new Exception("No hay destinatarios válidos para enviar el aviso.");

            using var ms = new MemoryStream(attachmentBytes ?? Array.Empty<byte>());
            using var att = new System.Net.Mail.Attachment(
                ms,
                attachmentFileName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            );

            msg.Attachments.Add(att);

            using var smtp = new System.Net.Mail.SmtpClient(host, port);
            smtp.EnableSsl = enableSsl;

            if (!string.IsNullOrWhiteSpace(user))
            {
                smtp.Credentials = new System.Net.NetworkCredential(user, password);
            }

            await smtp.SendMailAsync(msg);
        }

        private static IEnumerable<string> SplitEmails(string raw)
        {
            return (raw ?? "")
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }


        [HttpGet("OrdenVentaPdf")]
        public async Task<IActionResult> OrdenVentaPdf(
            string id,
            string referencia,
            string source = "P1",
            string origenDatos = "remision",
            string comentarios = "")
        {
            origenDatos = (origenDatos ?? "remision").Trim().ToLowerInvariant();

            if (origenDatos == "remision")
            {
                return await OrdenVentaPdfDesdeJsonRemisionAsync(
                    id,
                    referencia,
                    source,
                    comentarios
                );
            }

            return await OrdenVentaPdfDesdeOrdenVentaAsync(
                id,
                source,
                comentarios
            );
        }

        private async Task<IActionResult> OrdenVentaPdfDesdeOrdenVentaAsync(
            string id,
            string source = "P1",
            string comentarios = "")
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id requerido.");

            // id = Subpedido.U_DocMeat
            // Subpedido.OrdenVentaId = OrdenVenta.Id
            var sub = await _db.Subpedidos
                .AsNoTracking()
                .Where(s => s.U_DocMeat == id)
                .OrderByDescending(s => s.OrdenVentaId)
                .FirstOrDefaultAsync();

            if (sub == null)
                return NotFound("No se encontró relación con la orden de venta.");

            var ordenVentaId = sub.OrdenVentaId;

            var header = await _db.VOrdenesVentaPorVendedor
                .AsNoTracking()
                .Where(x => x.Id == ordenVentaId)
                .Select(x => new OrdenVentaPdfVm
                {
                    Id = x.Id,
                    Consecutivo = x.Consecutivo,
                    FechaRegistro = x.FechaRegistro,
                    FechaEntrega = x.FechaEntrega,
                    Cliente = x.Cliente,
                    ClienteNombre = x.ClienteNombre,
                    Vendedor = x.Vendedor,
                    Serie = x.Serie,
                    Estatus = x.Estatus,
                    KgTotales = x.KgTotales,
                    Importe = x.Importe,
                    Observacion = x.Observacion
                })
                .FirstOrDefaultAsync();

            if (header == null)
                return NotFound("No se encontró la orden.");

            if (!string.IsNullOrWhiteSpace(comentarios))
            {
                header.Observacion = comentarios;
            }

            var clienteSap = await _db.ClienteSap
                .AsNoTracking()
                .Where(c => c.Cliente == header.Cliente)
                .FirstOrDefaultAsync();

            if (clienteSap != null)
            {
                header.ClienteNombre = clienteSap.Nombrecliente;
            }

            var direccion = await _db.DireccionesCliente
                .AsNoTracking()
                .Where(d => d.Cliente == header.Cliente && d.EsPrincipal == true)
                .OrderByDescending(d => d.Id)
                .FirstOrDefaultAsync();

            if (direccion != null)
            {
                header.DireccionCliente =
                    $"{direccion.Calle} {direccion.Colonia} {direccion.Ciudad} {direccion.Estado} {direccion.CodigoPostal}";
            }

            header.SubpedidoId = sub.Id;
            header.DocumentoSAP = sub.DocumentoSAP.ToString();
            header.SubFolio = sub.SubFolio;
            header.Almacen = sub.Almacen;
            header.TotalPesoSap = sub.TotalPeso;
            header.TotalImporteSap = sub.TotalImporte;

            header.Lineas = await _db.OrdenVentaProducto
                .AsNoTracking()
                .Where(l => l.PedidoId == ordenVentaId)
                .OrderBy(l => l.Id)
                .Select(l => new OrdenVentaPdfLineaVm
                {
                    ProductoCodigo = l.ProductoCodigo,
                    ProductoNombre = l.ProductoNombre,
                    Peso = l.Peso,
                    Cajas = l.Cajas,
                    Precio = l.Precio,
                    Kg = l.Peso,
                    Importe = l.Importe
                })
                .ToListAsync();

            header.KgTotales = header.Lineas.Sum(x => x.Kg);
            header.TotalPesoSap = header.KgTotales;
            header.Subtotal = header.Lineas.Sum(x => x.Importe);
            header.Total = header.Subtotal;

            return View("OrdenVentaPdf", header);
        }

        private async Task<IActionResult> OrdenVentaPdfDesdeJsonRemisionAsync(
       string id,
       string referencia,
       string source,
       string comentarios)
        {
            if (string.IsNullOrWhiteSpace(referencia))
                return BadRequest("Referencia de remisión requerida.");

            var json = await _data.BuildJsonAsync(referencia, source);

            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return NotFound("No se encontró JSON para la remisión: " + referencia);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var cardCode = GetJsonString(root, "CardCode");
            var numAtCard = GetJsonString(root, "NumAtCard");
            var commentsJson = GetJsonString(root, "Comments");
            var uDocMeat = GetJsonString(root, "U_DocMeat");

            if (string.IsNullOrWhiteSpace(uDocMeat))
                uDocMeat = referencia;

            if (!root.TryGetProperty("DocumentLines", out var documentLines) ||
                documentLines.ValueKind != JsonValueKind.Array)
            {
                return NotFound("El JSON no contiene DocumentLines.");
            }

            /*
                1) Leemos exactamente lo surtido en el JSON.
                   Los KG siempre salen del JSON real.
            */
            var partidasJson = new List<(string ItemCode, string WarehouseCode, string Lote, decimal Kg)>();

            foreach (var line in documentLines.EnumerateArray())
            {
                var itemCode = GetJsonString(line, "ItemCode");
                var warehouseCode = GetJsonString(line, "WarehouseCode");
                var quantityLine = GetJsonDecimal(line, "Quantity");

                if (string.IsNullOrWhiteSpace(itemCode))
                    continue;

                if (line.TryGetProperty("BatchNumbers", out var batches) &&
                    batches.ValueKind == JsonValueKind.Array &&
                    batches.GetArrayLength() > 0)
                {
                    foreach (var batch in batches.EnumerateArray())
                    {
                        var lote = GetJsonString(batch, "BatchNumber");
                        var kgBatch = GetJsonDecimal(batch, "Quantity");

                        partidasJson.Add((
                            ItemCode: itemCode.Trim(),
                            WarehouseCode: warehouseCode.Trim(),
                            Lote: lote.Trim(),
                            Kg: kgBatch
                        ));
                    }
                }
                else
                {
                    partidasJson.Add((
                        ItemCode: itemCode.Trim(),
                        WarehouseCode: warehouseCode.Trim(),
                        Lote: "",
                        Kg: quantityLine
                    ));
                }
            }

            if (!partidasJson.Any())
                return NotFound("El JSON no contiene partidas válidas para generar el PDF.");

            /*
                2) Buscamos la orden original relacionada.
                   Importante:
                   - id puede ser la solicitud que ya usaba el PDF antes.
                   - referencia/uDocMeat es la remisión real del JSON.
                   - numAtCard es el pedido SAP del JSON.
            */
            var clavesBusqueda = new[]
            {
        id,
        referencia,
        uDocMeat,
        numAtCard
    }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            var sub = await _db.Subpedidos
                .AsNoTracking()
                .Where(s =>
                    clavesBusqueda.Contains(s.U_DocMeat) ||
                    clavesBusqueda.Contains(s.SubFolio))
                .OrderByDescending(s => s.OrdenVentaId)
                .FirstOrDefaultAsync();

            int? ordenVentaId = sub?.OrdenVentaId;

            /*
                3) Si encontramos la orden, tomamos de ahí:
                   - vendedor
                   - fecha
                   - datos base de la orden
            */
            var headerOrden = ordenVentaId.HasValue
                ? await _db.VOrdenesVentaPorVendedor
                    .AsNoTracking()
                    .Where(x => x.Id == ordenVentaId.Value)
                    .Select(x => new
                    {
                        x.Id,
                        x.Consecutivo,
                        x.FechaRegistro,
                        x.FechaEntrega,
                        x.Cliente,
                        x.ClienteNombre,
                        x.Vendedor,
                        x.Serie,
                        x.Estatus,
                        x.Observacion
                    })
                    .FirstOrDefaultAsync()
                : null;

            /*
                4) Tomamos nombre de producto y precio de la orden original.
                   Los KG NO se toman de la orden, se quedan desde el JSON.
            */
            var itemCodes = partidasJson
                .Select(x => x.ItemCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var productosRows = ordenVentaId.HasValue
                ? await _db.OrdenVentaProducto
                    .AsNoTracking()
                    .Where(x => x.PedidoId == ordenVentaId.Value)
                    .Where(x => itemCodes.Contains(x.ProductoCodigo))
                    .Select(x => new
                    {
                        x.Id,
                        x.ProductoCodigo,
                        x.ProductoNombre,
                        x.Precio
                    })
                    .ToListAsync()
                : new List<object>()
                    .Select(x => new
                    {
                        Id = 0,
                        ProductoCodigo = "",
                        ProductoNombre = "",
                        Precio = 0m
                    })
                    .ToList();

            /*
                Fallback:
                Si no encontró la orden por Subpedidos, buscamos al menos el último
                nombre/precio conocido por ItemCode. Esto evita que quede N028 solo.
            */
            if (!productosRows.Any())
            {
                productosRows = await _db.OrdenVentaProducto
                    .AsNoTracking()
                    .Where(x => itemCodes.Contains(x.ProductoCodigo))
                    .Select(x => new
                    {
                        x.Id,
                        x.ProductoCodigo,
                        x.ProductoNombre,
                        x.Precio
                    })
                    .ToListAsync();
            }

            var productosInfo = productosRows
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductoCodigo))
                .GroupBy(x => x.ProductoCodigo.Trim().ToUpperInvariant())
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var r = g
                            .OrderByDescending(x => x.Id)
                            .FirstOrDefault();

                        return new
                        {
                            ProductoNombre = r?.ProductoNombre ?? "",
                            Precio = r?.Precio ?? 0m
                        };
                    }
                );

            /*
                5) Armamos las líneas:
                   - Código: JSON
                   - Nombre: OrdenVentaProducto
                   - KG: JSON
                   - Precio: OrdenVentaProducto
                   - Importe: KG JSON * Precio orden
            */
            var lineas = partidasJson
                .Select(x =>
                {
                    var key = (x.ItemCode ?? "").Trim().ToUpperInvariant();

                    var productoNombre = x.ItemCode;
                    var precio = 0m;

                    if (productosInfo.TryGetValue(key, out var info))
                    {
                        if (!string.IsNullOrWhiteSpace(info.ProductoNombre))
                            productoNombre = info.ProductoNombre;

                        precio = info.Precio;
                    }

                    var importe = Math.Round(x.Kg * precio, 2);

                    return new OrdenVentaPdfLineaVm
                    {
                        ProductoCodigo = x.ItemCode,
                        ProductoNombre = productoNombre,

                        // Los kilos reales salen del JSON.
                        Peso = x.Kg,
                        Kg = x.Kg,

                        // El JSON no trae cajas reales.
                        Cajas = 0,

                        // Precio como la orden de venta.
                        Precio = precio,

                        // Total calculado con KG surtidos reales.
                        Importe = importe
                    };
                })
                .ToList();

            var kgTotal = lineas.Sum(x => x.Kg);
            var importeTotal = lineas.Sum(x => x.Importe);

            /*
                6) Header del PDF.
                   Vendedor y precio vienen de la orden.
                   Cliente/remisión/pedido vienen del JSON real.
            */
            var header = new OrdenVentaPdfVm
            {
                Id = headerOrden?.Id ?? 0,

                // Consecutivo es string.
                Consecutivo = headerOrden?.Consecutivo ?? numAtCard ?? "",

                FechaRegistro = headerOrden?.FechaRegistro ?? DateTime.Now,
                FechaEntrega = headerOrden?.FechaEntrega ?? DateTime.Now,

                Cliente = cardCode,
                ClienteNombre = !string.IsNullOrWhiteSpace(headerOrden?.ClienteNombre)
                    ? headerOrden.ClienteNombre
                    : cardCode,

                // Aquí ya queda el vendedor como la orden de venta.
                Vendedor = headerOrden?.Vendedor ?? "",

                Serie = "JSON",

                // Estatus es int.
                Estatus = headerOrden?.Estatus ?? 0,

                KgTotales = kgTotal,
                Importe = importeTotal,

                Observacion = !string.IsNullOrWhiteSpace(comentarios)
                    ? comentarios
                    : commentsJson,

                DocumentoSAP = uDocMeat,
                SubFolio = numAtCard,
                Almacen = source,

                TotalPesoSap = kgTotal,
                TotalImporteSap = importeTotal,

                Lineas = lineas,
                Subtotal = importeTotal,
                Total = importeTotal
            };

            if (sub != null)
            {
                header.SubpedidoId = sub.Id;

                if (sub.DocumentoSAP != null)
                    header.DocumentoSAP = sub.DocumentoSAP.ToString();

                if (!string.IsNullOrWhiteSpace(sub.SubFolio))
                    header.SubFolio = sub.SubFolio;

                if (!string.IsNullOrWhiteSpace(sub.Almacen))
                    header.Almacen = sub.Almacen;
            }

            /*
                7) Nombre y dirección del cliente SAP.
            */
            var clienteSap = await _db.ClienteSap
                .AsNoTracking()
                .Where(c => c.Cliente == cardCode)
                .FirstOrDefaultAsync();

            if (clienteSap != null)
            {
                header.ClienteNombre = clienteSap.Nombrecliente;
            }

            var direccion = await _db.DireccionesCliente
                .AsNoTracking()
                .Where(d => d.Cliente == cardCode && d.EsPrincipal == true)
                .OrderByDescending(d => d.Id)
                .FirstOrDefaultAsync();

            if (direccion != null)
            {
                header.DireccionCliente =
                    $"{direccion.Calle} {direccion.Colonia} {direccion.Ciudad} {direccion.Estado} {direccion.CodigoPostal}";
            }

            return View("OrdenVentaPdf", header);
        }

        private static string GetJsonString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return "";

            if (!element.TryGetProperty(propertyName, out var prop))
                return "";

            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? "";

            if (prop.ValueKind == JsonValueKind.Number ||
                prop.ValueKind == JsonValueKind.True ||
                prop.ValueKind == JsonValueKind.False)
            {
                return prop.ToString();
            }

            return "";
        }

        private static decimal GetJsonDecimal(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return 0m;

            if (!element.TryGetProperty(propertyName, out var prop))
                return 0m;

            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetDecimal();

            if (prop.ValueKind == JsonValueKind.String &&
                decimal.TryParse(
                    prop.GetString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return value;
            }

            return 0m;
        }

        private static string BuildProductoNombreJson(string itemCode, string warehouseCode, string lote)
        {
            var partes = new List<string>();

            if (!string.IsNullOrWhiteSpace(itemCode))
                partes.Add(itemCode);

            if (!string.IsNullOrWhiteSpace(lote))
                partes.Add("Lote: " + lote);

            if (!string.IsNullOrWhiteSpace(warehouseCode))
                partes.Add("Alm: " + warehouseCode);

            return string.Join(" | ", partes);
        }

        // =======================================================
        // MÉTODOS PARA EL MÓDULO AUTOARTICULOS 
        // =======================================================
        // Metodo para cargar la pagina principal
        [HttpGet("AutoArticulos")]
        [RevisarPermiso("AUTO_ARTICULOS", "LEER")]
        public IActionResult AutoArticulos()
        {
            return View("~/Views/ProcesosCG/AutoArticulos.cshtml");
        }

        // Consultas de Lectura
        [HttpGet("ObtenerDatosBase")]
        [RevisarPermiso("AUTO_ARTICULOS", "LEER")]
        public async Task<IActionResult> ObtenerDatosBase()
        {
            try
            {
                var usuarios = await _db.UsuariosAutoArticulos.ToListAsync();
                var categorias = await _db.CategoriasAutoArticulos.ToListAsync();
                return Json(new { ok = true, usuarios, categorias });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet("ObtenerTablaPermisos")]
        [RevisarPermiso("AUTO_ARTICULOS", "LEER")]
        public async Task<IActionResult> ObtenerTablaPermisos()
        {
            try
            {
                var datos = await _db.UsuariosAutoArticulos
                    .Select(u => new
                    {
                        UsuarioId = u.Id,
                        Nombre = u.Nombre,
                        Departamento = u.Departamento,
                        TokenGafete = u.TokenGafete,
                        Categorias = _db.PermisosAutoArticulos
                                        .Where(p => p.UsuarioId == u.Id)
                                        .Select(p => p.Categoria)
                                        .ToList()
                    })
                    .ToListAsync();

                return Json(new { ok = true, datos });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet("ObtenerPermisosUsuario")]
        [RevisarPermiso("AUTO_ARTICULOS", "LEER")]
        public async Task<IActionResult> ObtenerPermisosUsuario(int usuarioId)
        {
            try
            {
                var permisos = await _db.PermisosAutoArticulos
                    .Where(p => p.UsuarioId == usuarioId)
                    .Select(p => p.CategoriaId)
                    .ToListAsync();

                return Json(new { ok = true, permisos });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet("ValidarEscaneo")]
        [RevisarPermiso("AUTO_ARTICULOS", "LEER")]
        public async Task<IActionResult> ValidarEscaneo(string token)
        {
            try
            {
                var usuario = await _db.UsuariosAutoArticulos
                    .FirstOrDefaultAsync(u => u.TokenGafete == token);

                if (usuario == null)
                    return Json(new { concedido = false, mensaje = "Gafete no registrado." });

                var categorias = await _db.PermisosAutoArticulos
                    .Where(p => p.UsuarioId == usuario.Id)
                    .Select(p => p.Categoria)
                    .ToListAsync();

                return Json(new { concedido = true, usuario, categorias });
            }
            catch (Exception ex)
            {
                return Json(new { concedido = false, mensaje = ex.Message });
            }
        }

        [HttpGet("GenerarQrGafete")]
        [RevisarPermiso("AUTO_ARTICULOS", "LEER")]
        public IActionResult GenerarQrGafete(string token)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(token, QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                return File(qrCode.GetGraphic(10), "image/png");
            }
        }

        [HttpGet("ObtenerBitacoraExcepciones")]
        [RevisarPermiso("AUTO_ARTICULOS", "LEER")]
        public async Task<IActionResult> ObtenerBitacoraExcepciones()
        {
            try
            {
                var logs = await _db.LogsExcepcionesArticulos
                    .Include(l => l.Usuario)
                    .Include(l => l.Categoria)
                    .OrderByDescending(l => l.Fecha)
                    .Take(100)
                    .Select(l => new
                    {
                        l.Id,
                        Fecha = l.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                        Colaborador = l.Usuario.Nombre,
                        Departamento = l.Usuario.Departamento,
                        Articulo = l.ArticuloIngresado,
                        Categoria = l.Categoria.Nombre,
                        Criticidad = l.Categoria.Criticidad,
                        Supervisor = l.Supervisor
                    })
                    .ToListAsync();

                return Json(new { ok = true, logs });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        // Acciones de Escritura
        [HttpPost("GuardarPermisos")]
        [RevisarPermiso("AUTO_ARTICULOS", "ESCRIBIR")]
        public async Task<IActionResult> GuardarPermisos([FromBody] GuardarPermisosDto dto)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Borrar permisos anteriores
                var permisosActuales = await _db.PermisosAutoArticulos
                    .Where(p => p.UsuarioId == dto.UsuarioId).ToListAsync();
                _db.PermisosAutoArticulos.RemoveRange(permisosActuales);

                // Insertar nuevos permisos
                if (dto.CategoriasIds != null && dto.CategoriasIds.Any())
                {
                    var nuevosPermisos = dto.CategoriasIds.Select(catId => new PermisoModel
                    {
                        UsuarioId = dto.UsuarioId,
                        CategoriaId = catId
                    });
                    await _db.PermisosAutoArticulos.AddRangeAsync(nuevosPermisos);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { ok = true, mensaje = "Permisos guardados." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpPost("GuardarUsuario")]
        [RevisarPermiso("AUTO_ARTICULOS", "ESCRIBIR")]
        public async Task<IActionResult> GuardarUsuario([FromBody] UsuarioModel nuevoUsuario)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nuevoUsuario.TokenGafete))
                    return Json(new { ok = false, mensaje = "El código es obligatorio." });

                bool existe = await _db.UsuariosAutoArticulos.AnyAsync(u => u.TokenGafete == nuevoUsuario.TokenGafete);
                if (existe)
                    return Json(new { ok = false, mensaje = "Este código ya está asignado." });

                _db.UsuariosAutoArticulos.Add(nuevoUsuario);
                await _db.SaveChangesAsync();

                return Json(new { ok = true, mensaje = "Registrado correctamente.", usuario = nuevoUsuario });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpPost("ValidarPinSupervisor")]
        [RevisarPermiso("AUTO_ARTICULOS", "ESCRIBIR")]
        public IActionResult ValidarPinSupervisor([FromForm] string pin)
        {
            string pinMaestro = ObtenerPinSupervisor();

            if (pin == pinMaestro)
            {
                return Json(new { ok = true });
            }

            return Json(new { ok = false, mensaje = "PIN de supervisor incorrecto." });
        }

        [HttpPost("AutorizarExcepcion")]
        [RevisarPermiso("AUTO_ARTICULOS", "LEER")]
        public async Task<IActionResult> AutorizarExcepcion(string pin, int usuarioId, string motivo, int categoriaId)
        {
            string pinMaestro = ObtenerPinSupervisor();

            if (pin != pinMaestro)
                return Json(new { ok = false, mensaje = "PIN de supervisor incorrecto." });

            try
            {
                var supervisorLogueado = User?.Identity?.Name ?? "SISTEMA";

                var log = new LogExcepcionModel
                {
                    UsuarioId = usuarioId,
                    ArticuloIngresado = motivo,
                    CategoriaId = categoriaId,
                    Supervisor = supervisorLogueado,
                    Fecha = DateTime.Now
                };

                _db.LogsExcepcionesArticulos.Add(log);
                await _db.SaveChangesAsync();

                return Json(new { ok = true, mensaje = "Acceso autorizado y registrado en bitácora." });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error al guardar bitácora: " + ex.Message });
            }
        }

        [HttpPost("CambiarPinSupervisor")]
        [RevisarPermiso("AUTO_ARTICULOS", "ESCRIBIR")]
        public IActionResult CambiarPinSupervisor([FromForm] string pinActual, [FromForm] string pinNuevo)
        {
            string pinGuardado = ObtenerPinSupervisor();

            if (pinActual != pinGuardado)
                return Json(new { ok = false, mensaje = "El PIN actual es incorrecto." });

            if (string.IsNullOrWhiteSpace(pinNuevo) || pinNuevo.Length < 4)
                return Json(new { ok = false, mensaje = "El nuevo PIN debe tener al menos 4 caracteres." });

            GuardarPinSupervisor(pinNuevo);

            return Json(new { ok = true, mensaje = "PIN actualizado correctamente." });
        }
        // =======================================================
        // MÉTODOS INTERNOS DE SEGURIDAD
        // =======================================================

        private string ObtenerPinSupervisor()
        {
            var config = _db.PinArticulos.FirstOrDefault(c => c.Clave == "PIN_SUPERVISOR");

            if (config != null && !string.IsNullOrWhiteSpace(config.Valor))
            {
                return config.Valor;
            }

            // Si no existe, lo creamos por defecto
            _db.PinArticulos.Add(new PinArticulosModel
            {
                Clave = "PIN_SUPERVISOR",
                Valor = "3911",
                Descripcion = "PIN de autorización para módulo AutoArticulos"
            });
            _db.SaveChanges();

            return "3911";
        }

        private void GuardarPinSupervisor(string nuevoPin)
        {
            var config = _db.PinArticulos.FirstOrDefault(c => c.Clave == "PIN_SUPERVISOR");

            if (config != null)
            {
                config.Valor = nuevoPin;
            }
            else
            {
                _db.PinArticulos.Add(new PinArticulosModel
                {
                    Clave = "PIN_SUPERVISOR",
                    Valor = nuevoPin,
                    Descripcion = "PIN de autorización para módulo AutoArticulos"
                });
            }

            _db.SaveChanges();
        }

        // Metodo para que la vista JS consulte los permisos
        [HttpGet("ObtenerPermisosVistaAutoArticulos")]
        public async Task<IActionResult> ObtenerPermisosVistaAutoArticulos()
        {
            var login = (User?.Identity?.Name ?? "").Trim();

            var permiso = await (
                from u in _db.UsuarioSQL
                join p in _db.Perfiles on u.PerfilId equals p.Id
                join ppm in _db.PerfilPermisoModulo on p.Id equals ppm.PerfilId
                join m in _db.ModulosSistema on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == "AUTO_ARTICULOS"
                      && ppm.Activo
                      && m.Activo
                select new
                {
                    ppm.PuedeLeer,
                    ppm.PuedeEscribir,
                    ppm.PuedeEliminar
                }
            ).FirstOrDefaultAsync();

            if (permiso == null)
            {
                return Json(new { puedeLeer = false, puedeEscribir = false, puedeEliminar = false });
            }

            return Json(new
            {
                puedeLeer = permiso.PuedeLeer,
                puedeEscribir = permiso.PuedeEscribir,
                puedeEliminar = permiso.PuedeEliminar
            });
        }
        //========================================================================================================================
        // ========================= EMPAQUE POR SKU =========================
        [HttpGet("EmpaqueArticulos")]
        [RevisarPermiso("EMPAQUES_ARTICULOS", "LEER")]

        public async Task<IActionResult> EmpaqueArticulos(string planta = "TIF")
        {
            try
            {
                planta = string.IsNullOrWhiteSpace(planta) ? "TIF" : planta.ToUpper();
                ViewBag.PlantaActual = planta;

                string nombreCadena = (planta == "P1") ? "CadenaMeatP1" : "CadenaMeatTIF";
                string dbCatalogo = (planta == "P1") ? "CommerciaNet" : "TIF_CommerciaNet";
                string cs = _configuration.GetConnectionString(nombreCadena);

                // Ejecutar ambas consultas en paralelo
                var tareaCatalogos = Task.Run(async () =>
                {
                    using var cn2 = new Microsoft.Data.SqlClient.SqlConnection(cs);
                    await cn2.OpenAsync();
                    return (await cn2.QueryAsync<dynamic>(@"
         SELECT e.EmpaqueId as Id, e.Nombre as Descripcion, t.Tipo 
         FROM Empaque e 
         INNER JOIN TipoEmpaque t ON e.TipoEmpaqueId = t.TipoEmpaqueId")).ToList();
                });

                using var cn = new Microsoft.Data.SqlClient.SqlConnection(cs);
                await cn.OpenAsync();

                var rawData = (await cn.QueryAsync<dynamic>($@"
     SELECT 
         art.ArticuloId AS Sku,
         art.Nombre AS Descripcion,
         e.EmpaqueId,
         e.Nombre AS NombreEmpaque,
         t.Tipo AS TipoEmpaque,
         ea.PiezaMinima, ea.PiezaDefault, ea.PiezaMaxima,
         ea.PesoMinimo, ea.PesoMaximo,
         ea.FechaHora
     FROM [{dbCatalogo}].dbo.Articulo art
     LEFT JOIN EmpaqueArticulo ea ON LTRIM(RTRIM(art.ArticuloId)) = LTRIM(RTRIM(ea.Articulo))
     LEFT JOIN Empaque e ON ea.EmpaqueId = e.EmpaqueId
     LEFT JOIN TipoEmpaque t ON e.TipoEmpaqueId = t.TipoEmpaqueId
 ")).ToList();

                var catalogos = await tareaCatalogos;

                ViewBag.EmpaquesInternos = catalogos.Where(x => x.Tipo.ToString().Contains("BOLSA") || x.Tipo.ToString().Contains("VACIO")).ToList();
                ViewBag.EmpaquesExternos = catalogos.Where(x => x.Tipo.ToString().Contains("CAJA") || x.Tipo.ToString().Contains("CARRO")).ToList();

                var listaFinal = new List<Plataforma_CG.Models.EmpaqueRowVM>();
                var agrupadoPorSku = rawData.GroupBy(x => x.Sku.ToString());

                foreach (var grupo in agrupadoPorSku)
                {
                    var primerRegistro = grupo.First();

                    var internos = grupo.Where(x => x.TipoEmpaque != null && (x.TipoEmpaque.ToString().Contains("BOLSA") || x.TipoEmpaque.ToString().Contains("VACIO")))
                                        .GroupBy(x => x.EmpaqueId).Select(g => g.First()).ToList();

                    var externos = grupo.Where(x => x.TipoEmpaque != null && (x.TipoEmpaque.ToString().Contains("CAJA") || x.TipoEmpaque.ToString().Contains("CARRO")))
                                        .GroupBy(x => x.EmpaqueId).Select(g => g.First()).ToList();

                    int maxFilas = Math.Max(1, Math.Max(internos.Count, externos.Count));

                    for (int i = 0; i < maxFilas; i++)
                    {
                        var row = new Plataforma_CG.Models.EmpaqueRowVM
                        {
                            Sku = grupo.Key,
                            Descripcion = primerRegistro.Descripcion ?? ("Art. " + grupo.Key)
                        };

                        if (i < internos.Count)
                        {
                            row.EmpaqueInt = internos[i].EmpaqueId.ToString();
                            row.EmpaqueIntDesc = internos[i].NombreEmpaque;
                            row.PzMin = (int)internos[i].PiezaMinima;
                            row.PzDef = (int)internos[i].PiezaDefault;
                            row.PzMax = (int)internos[i].PiezaMaxima;
                            row.PesoMin = (decimal)internos[i].PesoMinimo;
                            row.PesoMax = (decimal)internos[i].PesoMaximo;
                        }

                        if (i < externos.Count)
                        {
                            row.EmpaqueExt = externos[i].EmpaqueId.ToString();
                            row.EmpaqueExtDesc = externos[i].NombreEmpaque;
                            row.BolsaMin = (int)externos[i].PiezaMinima;
                            row.BolsaDef = (int)externos[i].PiezaDefault;
                            row.BolsaMax = (int)externos[i].PiezaMaxima;
                            row.PesoExtMin = (decimal)externos[i].PesoMinimo;
                            row.PesoExtMax = (decimal)externos[i].PesoMaximo;
                        }

                        listaFinal.Add(row);
                    }
                }

                return View("~/Views/ProcesosCG/EmpaqueArticulos.cshtml", listaFinal);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View("~/Views/ProcesosCG/EmpaqueArticulos.cshtml", new List<Plataforma_CG.Models.EmpaqueRowVM>());
            }
        }

        [HttpGet("ObtenerHistorialEmpaque")]
        public async Task<IActionResult> ObtenerHistorialEmpaque(string planta = "TIF", string sku = null)
        {
            try
            {
                planta = string.IsNullOrWhiteSpace(planta) ? "TIF" : planta.ToUpper();

                // Ajuste de cadena de conexion
                string nombreCadena = (planta == "P1") ? "CadenaMeatP1" : "CadenaMeatTIF";
                string cs = _configuration.GetConnectionString(nombreCadena);

                string dbEmpaque = (planta == "P1") ? "Meat" : "TIF_Meat";
                using var cn = new Microsoft.Data.SqlClient.SqlConnection(cs);
                await cn.OpenAsync();

                // Consulta base
                string sql = $@"
    SELECT TOP 200 l.*, e.Nombre AS NombreEmpaque
    FROM SIGO.dbo.EmpaqueArticuloLog l
    LEFT JOIN [{dbEmpaque}].dbo.Empaque e ON l.EmpaqueId = e.EmpaqueId
    WHERE l.Planta = @Planta ";

                // Filtro condicional
                if (!string.IsNullOrEmpty(sku))
                {
                    sql += " AND LTRIM(RTRIM(l.Sku)) = LTRIM(RTRIM(@Sku)) ";
                }

                sql += " ORDER BY l.FechaHora DESC";

                // Ejecutar consulta
                var historial = await cn.QueryAsync<EmpaqueArticuloLogVM>(sql, new { Planta = planta, Sku = sku });

                return Json(new { ok = true, data = historial });
            }
            catch (Exception ex)
            {
                // Retorno estructurado para errores
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpPost("GuardarConfiguracionEmpaque")]
        [RevisarPermiso("EMPAQUES_ARTICULOS", "ESCRIBIR")]
        public async Task<IActionResult> GuardarConfiguracionEmpaque([FromBody] Plataforma_CG.Models.EmpaqueRequestVm payload, [FromQuery] string planta = "TIF")
        {
            // Validar planta actual
            planta = string.IsNullOrWhiteSpace(planta) ? "TIF" : planta.ToUpper();
            string nombreCadena = (planta == "P1") ? "CadenaMeatP1" : "CadenaMeatTIF";
            string cs = _configuration.GetConnectionString(nombreCadena);

            using var cn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await cn.OpenAsync();

            using var tx = cn.BeginTransaction();

            try
            {
                // Validar datos de entrada
                if (payload == null || payload.Skus == null || !payload.Skus.Any())
                    return Json(new { ok = false, mensaje = "Datos invalidos." });

                int? idInt = string.IsNullOrEmpty(payload.EmpaqueInterno) ? null : int.Parse(payload.EmpaqueInterno);
                int? idExt = string.IsNullOrEmpty(payload.EmpaqueExterno) ? null : int.Parse(payload.EmpaqueExterno);
                var usuario = User?.Identity?.Name ?? "Sistema";

                foreach (var sku in payload.Skus)
                {
                    // Alta en catalogo maestro
                    if (payload.ModoAlta)
                    {
                        var existeArticulo = await cn.QueryFirstOrDefaultAsync<int>(@"
             SELECT COUNT(1) FROM TIF_CommerciaNet.dbo.Articulo WHERE ArticuloId = @Sku", new { Sku = sku }, tx);

                        if (existeArticulo == 0)
                        {
                            await cn.ExecuteAsync(@"
                 INSERT INTO TIF_CommerciaNet.dbo.Articulo (ArticuloId, Nombre) 
                 VALUES (@Sku, @Desc)",
                                new { Sku = sku, Desc = payload.NuevoSkuDesc }, tx);
                        }
                    }

                    // Guardar empaque interno
                    if (idInt.HasValue)
                    {
                        var existeInt = await cn.QueryFirstOrDefaultAsync<int>(
                            "SELECT COUNT(1) FROM EmpaqueArticulo WHERE Articulo = @Sku AND EmpaqueId = @EmpId",
                            new { Sku = sku, EmpId = idInt.Value }, tx);

                        if (existeInt > 0)
                        {
                            // Actualizar registro existente
                            await cn.ExecuteAsync(@"
                 UPDATE EmpaqueArticulo SET 
                 PiezaMinima = @PzMin, PiezaDefault = @PzDef, PiezaMaxima = @PzMax, 
                 PesoMinimo = @PesoMin, PesoMaximo = @PesoMax, FechaHora = GETDATE()
                 WHERE Articulo = @Sku AND EmpaqueId = @EmpId",
                                new
                                {
                                    EmpId = idInt.Value,
                                    Sku = sku,
                                    PzMin = payload.PzaMin ?? 0,
                                    PzDef = payload.PzaDef ?? 0,
                                    PzMax = payload.PzaMax ?? 0,
                                    PesoMin = payload.PesoMin ?? 0,
                                    PesoMax = payload.PesoMax ?? 0
                                }, tx);

                            await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'UPDATE', '', @Nuevos, @Usuario)",
                                new
                                {
                                    Planta = planta,
                                    Sku = sku,
                                    EmpId = idInt.Value,
                                    Nuevos = JsonSerializer.Serialize(new { payload.PzaMin, payload.PzaDef, payload.PzaMax, payload.PesoMin, payload.PesoMax }),
                                    Usuario = usuario
                                }, tx);
                        }
                        else
                        {
                            // Insertar nuevo registro
                            await cn.ExecuteAsync(@"
                 INSERT INTO EmpaqueArticulo 
                 (EmpaqueId, Articulo, PiezaMinima, PiezaDefault, PiezaMaxima, PesoMinimo, PesoMaximo, FechaHora)
                 VALUES 
                 (@EmpId, @Sku, @PzMin, @PzDef, @PzMax, @PesoMin, @PesoMax, GETDATE())",
                                new
                                {
                                    EmpId = idInt.Value,
                                    Sku = sku,
                                    PzMin = payload.PzaMin ?? 0,
                                    PzDef = payload.PzaDef ?? 0,
                                    PzMax = payload.PzaMax ?? 0,
                                    PesoMin = payload.PesoMin ?? 0,
                                    PesoMax = payload.PesoMax ?? 0
                                }, tx);

                            await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'INSERT', '', @Nuevos, @Usuario)",
                                new
                                {
                                    Planta = planta,
                                    Sku = sku,
                                    EmpId = idInt.Value,
                                    Nuevos = JsonSerializer.Serialize(new { payload.PzaMin, payload.PzaDef, payload.PzaMax, payload.PesoMin, payload.PesoMax }),
                                    Usuario = usuario
                                }, tx);
                        }
                    }

                    // Guardar empaque externo
                    if (idExt.HasValue)
                    {
                        var existeExt = await cn.QueryFirstOrDefaultAsync<int>(
                            "SELECT COUNT(1) FROM EmpaqueArticulo WHERE Articulo = @Sku AND EmpaqueId = @EmpId",
                            new { Sku = sku, EmpId = idExt.Value }, tx);

                        if (existeExt > 0)
                        {
                            // Actualizar registro existente
                            await cn.ExecuteAsync(@"
                 UPDATE EmpaqueArticulo SET 
                 PiezaMinima = @PzMin, PiezaDefault = @PzDef, PiezaMaxima = @PzMax, 
                 PesoMinimo = @PesoMin, PesoMaximo = @PesoMax, FechaHora = GETDATE()
                 WHERE Articulo = @Sku AND EmpaqueId = @EmpId",
                                new
                                {
                                    EmpId = idExt.Value,
                                    Sku = sku,
                                    PzMin = payload.BolsaMin ?? 0,
                                    PzDef = payload.BolsaDef ?? 0,
                                    PzMax = payload.BolsaMax ?? 0,
                                    PesoMin = payload.PesoExtMin ?? 0,
                                    PesoMax = payload.PesoExtMax ?? 0
                                }, tx);

                            await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'UPDATE', '', @Nuevos, @Usuario)",
                                new
                                {
                                    Planta = planta,
                                    Sku = sku,
                                    EmpId = idExt.Value,
                                    Nuevos = JsonSerializer.Serialize(new { payload.BolsaMin, payload.BolsaDef, payload.BolsaMax, payload.PesoExtMin, payload.PesoExtMax }),
                                    Usuario = usuario
                                }, tx);
                        }
                        else
                        {
                            // Insertar nuevo registro
                            await cn.ExecuteAsync(@"
                 INSERT INTO EmpaqueArticulo 
                 (EmpaqueId, Articulo, PiezaMinima, PiezaDefault, PiezaMaxima, PesoMinimo, PesoMaximo, FechaHora)
                 VALUES 
                 (@EmpId, @Sku, @PzMin, @PzDef, @PzMax, @PesoMin, @PesoMax, GETDATE())",
                                new
                                {
                                    EmpId = idExt.Value,
                                    Sku = sku,
                                    PzMin = payload.BolsaMin ?? 0,
                                    PzDef = payload.BolsaDef ?? 0,
                                    PzMax = payload.BolsaMax ?? 0,
                                    PesoMin = payload.PesoExtMin ?? 0,
                                    PesoMax = payload.PesoExtMax ?? 0
                                }, tx);

                            await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'INSERT', '', @Nuevos, @Usuario)",
                                new
                                {
                                    Planta = planta,
                                    Sku = sku,
                                    EmpId = idExt.Value,
                                    Nuevos = JsonSerializer.Serialize(new { payload.BolsaMin, payload.BolsaDef, payload.BolsaMax, payload.PesoExtMin, payload.PesoExtMax }),
                                    Usuario = usuario
                                }, tx);
                        }
                    }
                }

                tx.Commit();
                return Json(new { ok = true, mensaje = "Guardado correctamente." });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Json(new { ok = false, mensaje = "Error BD: " + ex.Message });
            }
        }


        [HttpPost("EliminarConfiguracionEmpaque")]
        [RevisarPermiso("EMPAQUES_ARTICULOS", "ELIMINAR")]
        public async Task<IActionResult> EliminarConfiguracionEmpaque([FromBody] Plataforma_CG.Models.EmpaqueRequestVm payload, [FromQuery] string planta = "TIF")
        {
            planta = string.IsNullOrWhiteSpace(planta) ? "TIF" : planta.ToUpper();
            string nombreCadena = (planta == "P1") ? "CadenaMeatP1" : "CadenaMeatTIF";
            string cs = _configuration.GetConnectionString(nombreCadena);

            using var cn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            try
            {
                if (payload == null || payload.Skus == null || !payload.Skus.Any())
                    return Json(new { ok = false, mensaje = "Datos invalidos." });

                int? idInt = string.IsNullOrEmpty(payload.EmpaqueInterno) ? null : int.Parse(payload.EmpaqueInterno);
                int? idExt = string.IsNullOrEmpty(payload.EmpaqueExterno) ? null : int.Parse(payload.EmpaqueExterno);
                var usuario = User?.Identity?.Name ?? "Sistema";

                foreach (var sku in payload.Skus)
                {
                    // Borra la bolsa si fue seleccionada
                    if (idInt.HasValue)
                    {
                        await cn.ExecuteAsync("DELETE FROM EmpaqueArticulo WHERE Articulo = @Sku AND EmpaqueId = @EmpId",
                            new { Sku = sku, EmpId = idInt.Value }, tx);

                        await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'DELETE', '', '', @Usuario)",
                            new { Planta = planta, Sku = sku, EmpId = idInt.Value, Usuario = usuario }, tx);
                    }

                    // Borra la caja si fue seleccionada
                    if (idExt.HasValue)
                    {
                        await cn.ExecuteAsync("DELETE FROM EmpaqueArticulo WHERE Articulo = @Sku AND EmpaqueId = @EmpId",
                            new { Sku = sku, EmpId = idExt.Value }, tx);

                        await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'DELETE', '', '', @Usuario)",
                            new { Planta = planta, Sku = sku, EmpId = idExt.Value, Usuario = usuario }, tx);
                    }
                }

                tx.Commit();
                return Json(new { ok = true, mensaje = "Empaque(s) desvinculado(s) correctamente." });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Json(new { ok = false, mensaje = "Error BD: " + ex.Message });
            }
        }
        [HttpPost("CargarMasivoEmpaques")]
        [RevisarPermiso("EMPAQUES_ARTICULOS", "ESCRIBIR")]
        public async Task<IActionResult> CargarMasivoEmpaques([FromForm] IFormFile archivo, [FromQuery] string planta = "TIF")
        {
            if (archivo == null || archivo.Length == 0)
                return Json(new { ok = false, mensaje = "El archivo está vacío o es inválido." });

            planta = string.IsNullOrWhiteSpace(planta) ? "TIF" : planta.ToUpper();
            string nombreCadena = (planta == "P1") ? "CadenaMeatP1" : "CadenaMeatTIF";
            string dbCatalogo = (planta == "P1") ? "CommerciaNet" : "TIF_CommerciaNet";
            string cs = _configuration.GetConnectionString(nombreCadena);

            using var cn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            try
            {
                using var reader = new System.IO.StreamReader(archivo.OpenReadStream());
                string content = await reader.ReadToEndAsync();
                var lineas = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (lineas.Length <= 1)
                    return Json(new { ok = false, mensaje = "El archivo no contiene registros." });

                int procesados = 0;
                int noEncontrados = 0;
                List<string> errores = new List<string>();

                char separador = lineas[0].Contains(";") ? ';' : ',';
                string CleanCsv(string val) => val?.Replace("\"", "").Trim() ?? "";
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var usuario = User?.Identity?.Name ?? "Sistema";

                for (int i = 1; i < lineas.Length; i++)
                {
                    var cols = lineas[i].Split(separador);
                    if (cols.Length < 14) continue;

                    string sku = CleanCsv(cols[0]);
                    string desc = CleanCsv(cols[1]);

                    string empIntStr = CleanCsv(cols[2]);
                    decimal.TryParse(CleanCsv(cols[3]), System.Globalization.NumberStyles.Any, culture, out decimal pzMin);
                    decimal.TryParse(CleanCsv(cols[4]), System.Globalization.NumberStyles.Any, culture, out decimal pzDef);
                    decimal.TryParse(CleanCsv(cols[5]), System.Globalization.NumberStyles.Any, culture, out decimal pzMax);
                    decimal.TryParse(CleanCsv(cols[6]), System.Globalization.NumberStyles.Any, culture, out decimal pesoMin);
                    decimal.TryParse(CleanCsv(cols[7]), System.Globalization.NumberStyles.Any, culture, out decimal pesoMax);

                    string empExtStr = CleanCsv(cols[8]);
                    decimal.TryParse(CleanCsv(cols[9]), System.Globalization.NumberStyles.Any, culture, out decimal bsMin);
                    decimal.TryParse(CleanCsv(cols[10]), System.Globalization.NumberStyles.Any, culture, out decimal bsDef);
                    decimal.TryParse(CleanCsv(cols[11]), System.Globalization.NumberStyles.Any, culture, out decimal bsMax);
                    decimal.TryParse(CleanCsv(cols[12]), System.Globalization.NumberStyles.Any, culture, out decimal pesoExtMin);
                    decimal.TryParse(CleanCsv(cols[13]), System.Globalization.NumberStyles.Any, culture, out decimal pesoExtMax);

                    if (string.IsNullOrEmpty(sku)) continue;

                    var existeArt = await cn.QueryFirstOrDefaultAsync<int>($"SELECT COUNT(1) FROM [{dbCatalogo}].dbo.Articulo WHERE LTRIM(RTRIM(ArticuloId)) = @Sku", new { Sku = sku }, tx);
                    if (existeArt == 0)
                    {
                        await cn.ExecuteAsync($"INSERT INTO [{dbCatalogo}].dbo.Articulo (ArticuloId, Nombre) VALUES (@Sku, @Desc)", new { Sku = sku, Desc = desc }, tx);
                    }

                    // Buscador inteligente protegido
                    async Task<int?> ObtenerIdEmpaque(string valor)
                    {
                        if (string.IsNullOrEmpty(valor) || valor.ToUpper() == "SIN ASIGNAR" || valor == "-") return null;

                        // Si mandan un número, validamos que exista en la tabla Empaque primero
                        if (int.TryParse(valor, out int id))
                        {
                            var existe = await cn.QueryFirstOrDefaultAsync<int>("SELECT COUNT(1) FROM Empaque WHERE EmpaqueId = @Id", new { Id = id }, tx);
                            if (existe > 0) return id;
                        }

                        return await cn.QueryFirstOrDefaultAsync<int?>(@"
             SELECT TOP 1 EmpaqueId 
             FROM Empaque 
             WHERE UPPER(LTRIM(RTRIM(Nombre))) = UPPER(@Nom) 
                OR Nombre LIKE '%' + @Nom + '%'",
                            new { Nom = valor }, tx);
                    }

                    int? idInt = await ObtenerIdEmpaque(empIntStr);
                    int? idExt = await ObtenerIdEmpaque(empExtStr);

                    if (!idInt.HasValue && !idExt.HasValue && (!string.IsNullOrEmpty(empIntStr) || !string.IsNullOrEmpty(empExtStr)))
                    {
                        errores.Add($"SKU {sku}: No halló '{empIntStr}' ni '{empExtStr}'");
                        noEncontrados++;
                        continue;
                    }

                    // Guardar BOLSA
                    if (idInt.HasValue)
                    {
                        var existe = await cn.QueryFirstOrDefaultAsync<int>("SELECT COUNT(1) FROM EmpaqueArticulo WHERE LTRIM(RTRIM(Articulo)) = @Sku AND EmpaqueId = @EmpId", new { Sku = sku, EmpId = idInt.Value }, tx);
                        if (existe > 0)
                        {
                            await cn.ExecuteAsync(@"UPDATE EmpaqueArticulo SET PiezaMinima=@PzMin, PiezaDefault=@PzDef, PiezaMaxima=@PzMax, PesoMinimo=@PesoMin, PesoMaximo=@PesoMax, FechaHora=GETDATE() WHERE LTRIM(RTRIM(Articulo))=@Sku AND EmpaqueId=@EmpId",
                                new { Sku = sku, EmpId = idInt.Value, PzMin = pzMin, PzDef = pzDef, PzMax = pzMax, PesoMin = pesoMin, PesoMax = pesoMax }, tx);

                            await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'UPDATE', '', @Nuevos, @Usuario)",
                                new { Planta = planta, Sku = sku, EmpId = idInt.Value, Nuevos = JsonSerializer.Serialize(new { pzMin, pzDef, pzMax, pesoMin, pesoMax }), Usuario = usuario }, tx);
                        }
                        else
                        {
                            await cn.ExecuteAsync(@"INSERT INTO EmpaqueArticulo (EmpaqueId, Articulo, PiezaMinima, PiezaDefault, PiezaMaxima, PesoMinimo, PesoMaximo, FechaHora) VALUES (@EmpId, @Sku, @PzMin, @PzDef, @PzMax, @PesoMin, @PesoMax, GETDATE())",
                                new { Sku = sku, EmpId = idInt.Value, PzMin = pzMin, PzDef = pzDef, PzMax = pzMax, PesoMin = pesoMin, PesoMax = pesoMax }, tx);

                            await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'INSERT', '', @Nuevos, @Usuario)",
                                new { Planta = planta, Sku = sku, EmpId = idInt.Value, Nuevos = JsonSerializer.Serialize(new { pzMin, pzDef, pzMax, pesoMin, pesoMax }), Usuario = usuario }, tx);
                        }
                    }

                    // Guardar CAJA
                    if (idExt.HasValue)
                    {
                        var existe = await cn.QueryFirstOrDefaultAsync<int>("SELECT COUNT(1) FROM EmpaqueArticulo WHERE LTRIM(RTRIM(Articulo)) = @Sku AND EmpaqueId = @EmpId", new { Sku = sku, EmpId = idExt.Value }, tx);
                        if (existe > 0)
                        {
                            await cn.ExecuteAsync(@"UPDATE EmpaqueArticulo SET PiezaMinima=@BsMin, PiezaDefault=@BsDef, PiezaMaxima=@BsMax, PesoMinimo=@PesoExtMin, PesoMaximo=@PesoExtMax, FechaHora=GETDATE() WHERE LTRIM(RTRIM(Articulo))=@Sku AND EmpaqueId=@EmpId",
                                new { Sku = sku, EmpId = idExt.Value, BsMin = bsMin, BsDef = bsDef, BsMax = bsMax, PesoExtMin = pesoExtMin, PesoExtMax = pesoExtMax }, tx);

                            await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'UPDATE', '', @Nuevos, @Usuario)",
                                new { Planta = planta, Sku = sku, EmpId = idExt.Value, Nuevos = JsonSerializer.Serialize(new { bsMin, bsDef, bsMax, pesoExtMin, pesoExtMax }), Usuario = usuario }, tx);
                        }
                        else
                        {
                            await cn.ExecuteAsync(@"INSERT INTO EmpaqueArticulo (EmpaqueId, Articulo, PiezaMinima, PiezaDefault, PiezaMaxima, PesoMinimo, PesoMaximo, FechaHora) VALUES (@EmpId, @Sku, @BsMin, @BsDef, @BsMax, @PesoExtMin, @PesoExtMax, GETDATE())",
                                new { Sku = sku, EmpId = idExt.Value, BsMin = bsMin, BsDef = bsDef, BsMax = bsMax, PesoExtMin = pesoExtMin, PesoExtMax = pesoExtMax }, tx);

                            await cn.ExecuteAsync("INSERT INTO SIGO.dbo.EmpaqueArticuloLog (Planta, Sku, EmpaqueId, Operacion, ValoresAnteriores, ValoresNuevos, Usuario) VALUES (@Planta, @Sku, @EmpId, 'INSERT', '', @Nuevos, @Usuario)",
                                new { Planta = planta, Sku = sku, EmpId = idExt.Value, Nuevos = JsonSerializer.Serialize(new { bsMin, bsDef, bsMax, pesoExtMin, pesoExtMax }), Usuario = usuario }, tx);
                        }
                    }
                    procesados++;
                }

                tx.Commit();

                string mensajeFinal = $"Carga terminada. {procesados} SKUs actualizados.";
                if (errores.Any())
                {
                    mensajeFinal += $"\n\nSe ignoraron {noEncontrados} filas porque no se halló el nombre del empaque en el catálogo. Errores:\n" + string.Join("\n", errores.Take(5));
                }

                return Json(new { ok = true, mensaje = mensajeFinal });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Json(new { ok = false, mensaje = "Error al procesar archivo: " + ex.Message });
            }
        }


        public class EtiquetacionActualRequest
        {
            public string? Source { get; set; }
            public List<string> Codigos { get; set; } = new();
        }

        [HttpPost("EtiquetacionActualEtiquetas")]
        [Produces("application/json")]
        public async Task<IActionResult> EtiquetacionActualEtiquetas([FromBody] EtiquetacionActualRequest req)
        {
            try
            {
                var codigos = (req?.Codigos ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpper())
                    .Distinct()
                    .ToList();

                if (!codigos.Any())
                    return Ok(new { ok = true, rows = Array.Empty<object>() });

                var source = NormalizeSource(req?.Source ?? "P1");

                var cs = GetMeatConnectionString(source);

                if (string.IsNullOrWhiteSpace(cs))
                    return StatusCode(500, new
                    {
                        ok = false,
                        msg = $"No existe cadena de conexión para source={source}."
                    });

                var dbColector = source == "TIF"
                    ? "TIF_CommerciaNET"
                    : "CommerciaNET";

                var sql = $@"
;WITH UltimoLog AS
(
    SELECT
        CodigoEtiqueta = UPPER(LTRIM(RTRIM(a.CodigoEtiqueta))),
        b.EtiquetacionId,
        rn = ROW_NUMBER() OVER (
            PARTITION BY UPPER(LTRIM(RTRIM(a.CodigoEtiqueta)))
            ORDER BY b.FechaHoraEvento DESC
        )
    FROM dbo.Produccion a
    INNER JOIN dbo.ProduccionEtiquetacionLog b
        ON a.ProduccionId = b.ProduccionId
    WHERE UPPER(LTRIM(RTRIM(a.CodigoEtiqueta))) IN @Codigos
)
SELECT
    u.CodigoEtiqueta,
    ColectorId = CONVERT(varchar(20), u.EtiquetacionId),
    Etiquetacion = ISNULL(c.Nombre, CONVERT(varchar(20), u.EtiquetacionId))
FROM UltimoLog u
LEFT JOIN {dbColector}.dbo.COLECTOR c
    ON c.ColectorId = u.EtiquetacionId
   AND c.SistemaId = 'eti'
WHERE u.rn = 1
ORDER BY u.CodigoEtiqueta;
";

                using var cn = new SqlConnection(cs);

                var rows = (await cn.QueryAsync(sql, new { Codigos = codigos })).ToList();

                return Ok(new
                {
                    ok = true,
                    source,
                    rows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EtiquetacionActualEtiquetas ERROR");

                return StatusCode(500, new
                {
                    ok = false,
                    msg = ex.Message
                });
            }
        }
        // ========================= AVISOS DE MOVILIZACION SENASICA =========================
        // Modulo independiente de Entregas SAP.
        // Fuente oficial: ConnectionStrings:CadenaMeatTIF
        // Requiere: dbo.vw_AvisosMovilizacion_TIF

        [HttpGet("AvisosMovilizacion")]
        [RevisarPermiso("AVISOS_MOVILIZACION", "LEER")]
        public IActionResult AvisosMovilizacion(
            DateTime? desde,
            DateTime? hasta,
            string cliente = "",
            string venta = "",
            string lote = "")
        {
            var d1 = (desde ?? DateTime.Today).Date;
            var d2Visible = (hasta ?? DateTime.Today).Date;

            var vm = new AvisosMovilizacionPageVM
            {
                Desde = d1,
                Hasta = d2Visible,
                Cliente = cliente ?? "",
                Venta = venta ?? "",
                Lote = lote ?? "",

                // Importante: aquí NO se cargan datos pesados.
                // La vista se abre rápido y después llama AvisosMovilizacionData por AJAX.
                Rows = new List<AvisoMovilizacionResumenVM>()
            };

            return View("~/Views/ProcesosCG/AvisosMovilizacion.cshtml", vm);
        }

        [HttpGet("AvisosMovilizacionData")]
        [RevisarPermiso("AVISOS_MOVILIZACION", "LEER")]
        public async Task<IActionResult> AvisosMovilizacionData(
            DateTime? desde,
            DateTime? hasta,
            string cliente = "",
            string venta = "",
            string lote = "")
        {
            try
            {
                var d1 = (desde ?? DateTime.Today).Date;
                var d2Visible = (hasta ?? DateTime.Today).Date;
                var d2Exclusive = d2Visible.AddDays(1);

                var rows = await ObtenerResumenAvisosMovilizacionTifAsync(
                    d1,
                    d2Exclusive,
                    cliente ?? "",
                    venta ?? "",
                    lote ?? ""
                );

                return Ok(new
                {
                    ok = true,
                    desde = d1.ToString("yyyy-MM-dd"),
                    hasta = d2Visible.ToString("yyyy-MM-dd"),
                    totalSolicitudes = rows.Count,
                    totalCajas = rows.Sum(x => x.TotalCajas),
                    totalKg = rows.Sum(x => x.TotalKg),
                    rows
                });
            }
            catch (SqlException ex) when (ex.Number == -2)
            {
                _logger.LogError(ex, "Timeout SQL en AvisosMovilizacionData");

                return StatusCode(504, new
                {
                    ok = false,
                    msg = "Timeout SQL al cargar avisos de movilización.",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en AvisosMovilizacionData");

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error al cargar avisos de movilización.",
                    error = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        [HttpGet("AvisosMovilizacionDetalle")]
        [RevisarPermiso("AVISOS_MOVILIZACION", "LEER")]
        public async Task<IActionResult> AvisosMovilizacionDetalle([FromQuery] string id)
        {
            try
            {
                var ids = ParseSolicitudesAvisos(id);

                if (!ids.Any())
                    return BadRequest(new { ok = false, msg = "Solicitud requerida." });

                var rows = await ObtenerDetalleAvisosMovilizacionTifAsync(ids);

                return Ok(new
                {
                    ok = true,
                    solicitud = ids.FirstOrDefault() ?? "",
                    totalPartidas = rows.Count,
                    totalCajas = rows.Sum(x => x.CuentaDeEtiqueta),
                    totalKg = rows.Sum(x => x.SumaDeKg),
                    rows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar detalle de aviso de movilización. Id={Id}", id);

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error al consultar detalle.",
                    error = ex.Message
                });
            }
        }


        [HttpPost("EnviarAvisosMovilizacionSenasica")]
        [RevisarPermiso("AVISOS_MOVILIZACION", "ESCRIBIR")]
        public async Task<IActionResult> EnviarAvisosMovilizacionSenasica(
            [FromBody] EnviarAvisosMovilizacionRequest req)
        {
            try
            {
                if (req == null)
                    return BadRequest(new { ok = false, msg = "Solicitud vacía." });

                var solicitudes = (req.Solicitudes ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!solicitudes.Any())
                    return BadRequest(new { ok = false, msg = "No se recibieron solicitudes para enviar." });

                var correoMedico = (req.CorreoMedico ?? "").Trim();

                if (string.IsNullOrWhiteSpace(correoMedico))
                    correoMedico = (_configuration["Senasica:CorreoMedicoDefault"] ?? "").Trim();

                if (string.IsNullOrWhiteSpace(correoMedico))
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = "Captura el correo del médico SENASICA o configura Senasica:CorreoMedicoDefault en appsettings."
                    });
                }

                var rows = await ObtenerDetalleAvisosMovilizacionTifAsync(solicitudes);

                if (!rows.Any())
                {
                    return NotFound(new
                    {
                        ok = false,
                        msg = "No se encontró información para las solicitudes seleccionadas en CadenaMeatTIF."
                    });
                }

                var usuario = User?.Identity?.Name ?? "";
                var asunto = string.Format("Avisos de movilización TIF - {0:dd/MM/yyyy HH:mm}", DateTime.Now);
                var html = BuildAvisosMovilizacionHtml(rows, req.Comentarios ?? "", usuario);
                var excelBytes = CrearExcelAvisosMovilizacion(rows);
                var excelName = string.Format("Avisos_Movilizacion_TIF_{0:yyyyMMdd_HHmm}.xlsx", DateTime.Now);

                await EnviarCorreoAvisosMovilizacionAsync(
                    correoMedico,
                    asunto,
                    html,
                    excelBytes,
                    excelName
                );

                return Ok(new
                {
                    ok = true,
                    msg = "Aviso(s) de movilización enviados al médico SENASICA.",
                    totalSolicitudes = rows.Select(x => x.SolicitudSurtidoId).Distinct().Count(),
                    totalPartidas = rows.Count,
                    totalCajas = rows.Sum(x => x.CuentaDeEtiqueta),
                    totalKg = rows.Sum(x => x.SumaDeKg),
                    correoMedico
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al enviar avisos de movilización SENASICA.");

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error interno al enviar avisos de movilización SENASICA.",
                    error = ex.Message,
                    inner = ex.InnerException?.Message,
                    detalle = ex.ToString()
                });
            }
        }


        private async Task<List<AvisoMovilizacionResumenVM>> ObtenerResumenAvisosMovilizacionTifAsync(
            DateTime desde,
            DateTime hasta,
            string cliente,
            string venta,
            string lote)
        {
            var cs = _configuration.GetConnectionString("CadenaMeatTIF");

            if (string.IsNullOrWhiteSpace(cs))
                throw new Exception("No existe la cadena de conexión 'CadenaMeatTIF' en appsettings.");

            const string sql = @"
;WITH Base AS
(
    SELECT
        solicitud_surtido_id,
        venta,
        cliente,
        fecha_venta,
        sku,
        producto,
        lote,
        fecha_sacrificio,
        fecha_produccion,
        fecha_caducidad,
        cuenta_de_etiqueta,
        suma_de_kg
    FROM dbo.vw_AvisosMovilizacion_TIF
    WHERE fecha_venta >= @Desde
      AND fecha_venta <  @Hasta
      AND (@Cliente = '' OR cliente LIKE '%' + @Cliente + '%')
      AND (@Venta   = '' OR venta   LIKE '%' + @Venta   + '%')
      AND (@Lote    = '' OR lote    LIKE '%' + @Lote    + '%')
)
SELECT
    CONVERT(nvarchar(50), b.solicitud_surtido_id) AS SolicitudSurtidoId,
    MAX(b.venta) AS Venta,
    MAX(b.cliente) AS Cliente,
    b.fecha_venta AS FechaVenta,
    ISNULL(lx.Lotes, '') AS Lotes,
    COUNT(1) AS TotalPartidas,
    SUM(ISNULL(b.cuenta_de_etiqueta, 0)) AS TotalCajas,
    CAST(SUM(ISNULL(b.suma_de_kg, 0)) AS decimal(18,3)) AS TotalKg,
    MIN(b.fecha_sacrificio) AS FechaSacrificioMin,
    MAX(b.fecha_sacrificio) AS FechaSacrificioMax,
    MIN(b.fecha_produccion) AS FechaProduccionMin,
    MAX(b.fecha_produccion) AS FechaProduccionMax,
    MIN(b.fecha_caducidad) AS FechaCaducidadMin,
    MAX(b.fecha_caducidad) AS FechaCaducidadMax
FROM Base b
OUTER APPLY
(
    SELECT Lotes = STUFF((
        SELECT N', ' + x.Lote
        FROM
        (
            SELECT DISTINCT
                Lote = NULLIF(LTRIM(RTRIM(b2.lote)), N'')
            FROM Base b2
            WHERE b2.solicitud_surtido_id = b.solicitud_surtido_id
        ) x
        WHERE x.Lote IS NOT NULL
        ORDER BY x.Lote
        FOR XML PATH(''), TYPE
    ).value('.', 'nvarchar(max)'), 1, 2, N'')
) lx
GROUP BY
    b.solicitud_surtido_id,
    b.fecha_venta,
    lx.Lotes
ORDER BY
    b.fecha_venta,
    MAX(b.venta),
    MAX(b.cliente),
    b.solicitud_surtido_id;";

            using var cn = new SqlConnection(cs);

            var rows = await cn.QueryAsync<AvisoMovilizacionResumenVM>(sql, new
            {
                Desde = desde,
                Hasta = hasta,
                Cliente = (cliente ?? "").Trim(),
                Venta = (venta ?? "").Trim(),
                Lote = (lote ?? "").Trim()
            });

            return rows.ToList();
        }

        private static List<int> ParseSolicitudesAvisosInt(IEnumerable<string> solicitudes)
        {
            if (solicitudes == null)
                return new List<int>();

            return solicitudes
                .SelectMany(x => (x ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => int.TryParse(x, out var n) ? n : 0)
                .Where(x => x > 0)
                .Distinct()
                .ToList();
        }

        private static List<int> ParseSolicitudesAvisoInt(string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return new List<int>();

            return ids
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => int.TryParse(x, out var n) ? n : 0)
                .Where(x => x > 0)
                .Distinct()
                .ToList();
        }

        private async Task<List<AvisoMovilizacionDetalleVM>> ObtenerDetalleAvisosMovilizacionTifAsync(IEnumerable<string> solicitudes)
        {
            var ids = ParseSolicitudesAvisosInt(solicitudes);

            if (!ids.Any())
                return new List<AvisoMovilizacionDetalleVM>();

            var cs = _configuration.GetConnectionString("CadenaMeatTIF");

            if (string.IsNullOrWhiteSpace(cs))
                throw new Exception("No existe la cadena de conexión 'CadenaMeatTIF' en appsettings.");

            const string sql = @"
SELECT
    planta AS Planta,
    CONVERT(nvarchar(50), solicitud_surtido_id) AS SolicitudSurtidoId,
    venta AS Venta,
    cliente AS Cliente,
    fecha_venta AS FechaVenta,
    sku AS Sku,
    producto AS Producto,
    lote AS Lote,
    fecha_sacrificio AS FechaSacrificio,
    fecha_produccion AS FechaProduccion,
    fecha_caducidad AS FechaCaducidad,
    cuenta_de_etiqueta AS CuentaDeEtiqueta,
    suma_de_kg AS SumaDeKg
FROM dbo.vw_AvisosMovilizacion_TIF
WHERE solicitud_surtido_id IN @Solicitudes
ORDER BY
    fecha_venta,
    venta,
    cliente,
    sku,
    producto,
    lote,
    fecha_produccion,
    fecha_caducidad
OPTION (RECOMPILE);";

            using var cn = new SqlConnection(cs);

            var rows = await cn.QueryAsync<AvisoMovilizacionDetalleVM>(
                new CommandDefinition(
                    sql,
                    new { Solicitudes = ids },
                    commandTimeout: 180
                )
            );

            return rows.ToList();
        }

        private static List<string> ParseSolicitudesAvisos(string ids)
        {
            return (ids ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildAvisosMovilizacionHtml(
            List<AvisoMovilizacionDetalleVM> rows,
            string comentarios,
            string usuario)
        {
            string H(string value)
            {
                return System.Net.WebUtility.HtmlEncode(value ?? "");
            }

            var totalSolicitudes = rows.Select(x => x.SolicitudSurtidoId).Distinct().Count();
            var totalCajas = rows.Sum(x => x.CuentaDeEtiqueta);
            var totalKg = rows.Sum(x => x.SumaDeKg);

            var sb = new System.Text.StringBuilder();

            sb.Append($@"
<html>
<head>
<meta charset=""utf-8"">
<style>
    body {{ font-family: Arial, Helvetica, sans-serif; color:#222; font-size:12px; }}
    h2 {{ color:#8b0000; margin-bottom:4px; }}
    .muted {{ color:#666; }}
    .summary {{ margin:12px 0; padding:10px; background:#f7eeee; border:1px solid #c99; }}
    table {{ border-collapse:collapse; width:100%; }}
    th {{ background:#8b0000; color:#fff; padding:6px; border:1px solid #6b0000; }}
    td {{ padding:5px; border:1px solid #ddd; }}
    .right {{ text-align:right; }}
</style>
</head>
<body>
    <h2>Avisos de movilización TIF</h2>
    <div class=""muted"">Generado: {DateTime.Now:dd/MM/yyyy HH:mm} | Usuario: {H(usuario)}</div>

    <div class=""summary"">
        <strong>Total solicitudes:</strong> {totalSolicitudes}<br>
        <strong>Total cajas:</strong> {totalCajas:N0}<br>
        <strong>Total kg:</strong> {totalKg:N3}
    </div>");

            if (!string.IsNullOrWhiteSpace(comentarios))
            {
                sb.Append($@"
    <div class=""summary"">
        <strong>Comentarios:</strong><br>
        {H(comentarios)}
    </div>");
            }

            sb.Append(@"
    <table>
        <thead>
            <tr>
                <th>Solicitud</th>
                <th>Venta</th>
                <th>Cliente</th>
                <th>Fecha venta</th>
                <th>SKU</th>
                <th>Producto</th>
                <th>Lote</th>
                <th>F. sacrificio</th>
                <th>F. producción</th>
                <th>F. caducidad</th>
                <th>Cajas</th>
                <th>Kg</th>
            </tr>
        </thead>
        <tbody>");

            foreach (var r in rows)
            {
                sb.Append($@"
            <tr>
                <td>{H(r.SolicitudSurtidoId)}</td>
                <td>{H(r.Venta)}</td>
                <td>{H(r.Cliente)}</td>
                <td>{H(r.FechaVentaTxt)}</td>
                <td>{H(r.Sku)}</td>
                <td>{H(r.Producto)}</td>
                <td>{H(r.Lote)}</td>
                <td>{H(r.FechaSacrificioTxt)}</td>
                <td>{H(r.FechaProduccionTxt)}</td>
                <td>{H(r.FechaCaducidadTxt)}</td>
                <td class=""right"">{r.CuentaDeEtiqueta:N0}</td>
                <td class=""right"">{r.SumaDeKg:N3}</td>
            </tr>");
            }

            sb.Append(@"
        </tbody>
    </table>
</body>
</html>");

            return sb.ToString();
        }

        private static byte[] CrearExcelAvisosMovilizacion(List<AvisoMovilizacionDetalleVM> rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Avisos");

            var headers = new[]
            {
        "Planta",
        "Solicitud",
        "Venta",
        "Cliente",
        "Fecha venta",
        "SKU",
        "Producto",
        "Lote",
        "Fecha sacrificio",
        "Fecha producción",
        "Fecha caducidad",
        "Cajas",
        "Kg"
    };

            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
                ws.Cell(1, c + 1).Style.Font.Bold = true;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var row = i + 2;

                ws.Cell(row, 1).Value = r.Planta;
                ws.Cell(row, 2).Value = r.SolicitudSurtidoId;
                ws.Cell(row, 3).Value = r.Venta;
                ws.Cell(row, 4).Value = r.Cliente;
                ws.Cell(row, 5).Value = r.FechaVenta;
                ws.Cell(row, 6).Value = r.Sku;
                ws.Cell(row, 7).Value = r.Producto;
                ws.Cell(row, 8).Value = r.Lote;
                ws.Cell(row, 9).Value = r.FechaSacrificio;
                ws.Cell(row, 10).Value = r.FechaProduccion;
                ws.Cell(row, 11).Value = r.FechaCaducidad;
                ws.Cell(row, 12).Value = r.CuentaDeEtiqueta;
                ws.Cell(row, 13).Value = r.SumaDeKg;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private async Task EnviarCorreoAvisosMovilizacionAsync(
            string destinatarios,
            string asunto,
            string html,
            byte[] attachmentBytes,
            string attachmentFileName)
        {
            var host = (_configuration["Smtp:Host"] ?? "").Trim();

            if (string.IsNullOrWhiteSpace(host))
                throw new Exception("No existe configuración Smtp:Host en appsettings.");

            var from = (_configuration["Smtp:From"] ?? "").Trim();
            var user = (_configuration["Smtp:User"] ?? "").Trim();
            var password = _configuration["Smtp:Password"] ?? "";

            if (string.IsNullOrWhiteSpace(from))
                from = user;

            if (string.IsNullOrWhiteSpace(from))
                throw new Exception("No existe configuración Smtp:From o Smtp:User en appsettings.");

            var port = 587;

            if (int.TryParse(_configuration["Smtp:Port"], out var p))
                port = p;

            var enableSsl = true;

            if (bool.TryParse(_configuration["Smtp:EnableSsl"], out var ssl))
                enableSsl = ssl;

            using var msg = new System.Net.Mail.MailMessage();
            msg.From = new System.Net.Mail.MailAddress(from);
            msg.Subject = asunto;
            msg.Body = html;
            msg.IsBodyHtml = true;

            foreach (var email in SplitEmailsAvisos(destinatarios))
                msg.To.Add(email);

            if (msg.To.Count == 0)
                throw new Exception("No hay destinatarios válidos para enviar el aviso.");

            using var ms = new MemoryStream(attachmentBytes ?? new byte[0]);
            using var att = new System.Net.Mail.Attachment(
                ms,
                attachmentFileName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            );

            msg.Attachments.Add(att);

            using var smtp = new System.Net.Mail.SmtpClient(host, port);
            smtp.EnableSsl = enableSsl;

            if (!string.IsNullOrWhiteSpace(user))
                smtp.Credentials = new System.Net.NetworkCredential(user, password);

            await smtp.SendMailAsync(msg);
        }

        private static IEnumerable<string> SplitEmailsAvisos(string raw)
        {
            return (raw ?? "")
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }





        // ========================= INVENTARIO INICIAL / DIRECCIÓN GENERAL =========================

        [HttpGet("InventarioInicial")]
        public IActionResult InventarioInicial()
        {
            return View("~/Views/ProcesosCG/InventarioInicial.cshtml");
        }

        private sealed class InventarioInicialMasterFiltroVM
        {
            public string MasterProducto { get; set; } = "";
            public int TotalSkus { get; set; }
        }

        private sealed class InventarioInicialSkuFiltroVM
        {
            public string Sku { get; set; } = "";
            public string Articulo { get; set; } = "";
            public string MasterProducto { get; set; } = "";
        }

        private sealed class InventarioInicialDisponibleVM
        {
            public DateTime? Fecha { get; set; }

            public string Sku { get; set; } = "";
            public string MasterProducto { get; set; } = "";
            public string Articulo { get; set; } = "";

            public int TotalSkus { get; set; }
            public int TotalMasters { get; set; }

            public decimal CajasInventario { get; set; }
            public decimal KgInventario { get; set; }
            public decimal KgPromedioCaja { get; set; }

            public decimal CajasPedido { get; set; }
            public decimal KgPedido { get; set; }
            public decimal ImportePedido { get; set; }

            public decimal CajasProduccion { get; set; }
            public decimal KgProduccion { get; set; }

            public decimal CajasDisponible { get; set; }

            public decimal? KgPedidoEstimado { get; set; }
            public decimal? KgDisponibleEstimado { get; set; }

            public decimal KgDisponible { get; set; }

            public DateTime? PrimeraFechaEntrega { get; set; }
            public DateTime? UltimaFechaEntrega { get; set; }

            public string EstatusDisponible { get; set; } = "";
            public bool InventarioCapturado { get; set; }
        }

        [HttpGet("InventarioInicialFiltros")]
        public async Task<IActionResult> InventarioInicialFiltros()
        {
            try
            {
                const string sqlMasters = @"
SELECT
    MasterProducto,
    COUNT(DISTINCT Sku) AS TotalSkus
FROM dbo.InventarioInicialCatalogo WITH (NOLOCK)
WHERE Activo = 1
GROUP BY
    MasterProducto
ORDER BY
    CASE WHEN MasterProducto = 'SIN MASTER' THEN 1 ELSE 0 END,
    MasterProducto;
";

                const string sqlSkus = @"
SELECT
    Sku,
    Articulo,
    MasterProducto
FROM dbo.InventarioInicialCatalogo WITH (NOLOCK)
WHERE Activo = 1
ORDER BY
    MasterProducto,
    Sku;
";

                var cn = _db.Database.GetDbConnection();
                var shouldClose = cn.State == ConnectionState.Closed;

                if (shouldClose)
                    await cn.OpenAsync();

                try
                {
                    var mastersRaw = (await cn.QueryAsync<InventarioInicialMasterFiltroVM>(
                        sqlMasters,
                        commandTimeout: 60
                    )).ToList();

                    var skusRaw = (await cn.QueryAsync<InventarioInicialSkuFiltroVM>(
                        sqlSkus,
                        commandTimeout: 60
                    )).ToList();

                    var masters = mastersRaw
                        .Select(x => new
                        {
                            value = string.IsNullOrWhiteSpace(x.MasterProducto)
                                ? "SIN MASTER"
                                : x.MasterProducto.Trim().ToUpper(),

                            text = string.IsNullOrWhiteSpace(x.MasterProducto)
                                ? "SIN MASTER"
                                : x.MasterProducto.Trim().ToUpper(),

                            totalSkus = x.TotalSkus
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.value))
                        .OrderBy(x => x.text == "SIN MASTER" ? 1 : 0)
                        .ThenBy(x => x.text)
                        .ToList();

                    var skus = skusRaw
                        .Select(x => new
                        {
                            value = (x.Sku ?? "").Trim().ToUpper(),

                            text = string.IsNullOrWhiteSpace(x.Articulo)
                                ? (x.Sku ?? "").Trim().ToUpper()
                                : $"{(x.Sku ?? "").Trim().ToUpper()} - {x.Articulo.Trim()}",

                            articulo = x.Articulo ?? "",

                            master = string.IsNullOrWhiteSpace(x.MasterProducto)
                                ? "SIN MASTER"
                                : x.MasterProducto.Trim().ToUpper()
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.value))
                        .OrderBy(x => x.master)
                        .ThenBy(x => x.value)
                        .ToList();

                    return Ok(new
                    {
                        ok = true,
                        totalMasters = masters.Count,
                        totalSkus = skus.Count,
                        masters,
                        skus
                    });
                }
                finally
                {
                    if (shouldClose)
                        await cn.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar InventarioInicialFiltros");

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error al consultar filtros de inventario inicial.",
                    error = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        [HttpGet("InventarioInicialDatos")]
        public async Task<IActionResult> InventarioInicialDatos(
            string sku = "",
            string skusCsv = "",
            string mastersCsv = "",
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null,
            int dias = 23)
        {
            try
            {
                sku = (sku ?? "").Trim().ToUpper();
                skusCsv = (skusCsv ?? "").Trim().ToUpper();
                mastersCsv = (mastersCsv ?? "").Trim().ToUpper();

                const string sql = @"
EXEC dbo.sp_InventarioInicial
    @Sku = @Sku,
    @SkusCsv = @SkusCsv,
    @MastersCsv = @MastersCsv,
    @FechaInicio = @FechaInicio,
    @FechaFin = @FechaFin,
    @Dias = @Dias;
";

                var cn = _db.Database.GetDbConnection();
                var shouldClose = cn.State == ConnectionState.Closed;

                if (shouldClose)
                    await cn.OpenAsync();

                try
                {
                    var rows = (await cn.QueryAsync<InventarioInicialDisponibleVM>(
                        sql,
                        new
                        {
                            Sku = string.IsNullOrWhiteSpace(sku) ? null : sku,
                            SkusCsv = string.IsNullOrWhiteSpace(skusCsv) ? null : skusCsv,
                            MastersCsv = string.IsNullOrWhiteSpace(mastersCsv) ? null : mastersCsv,
                            FechaInicio = fechaInicio,
                            FechaFin = fechaFin,
                            Dias = dias
                        },
                        commandTimeout: 180
                    )).ToList();

                    return Ok(new
                    {
                        ok = true,
                        total = rows.Count,
                        data = rows
                    });
                }
                finally
                {
                    if (shouldClose)
                        await cn.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar InventarioInicialDatos");

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error al consultar inventario inicial.",
                    error = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }






    }
}

