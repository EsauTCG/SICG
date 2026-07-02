// Controllers/TransferenciasController.cs
using ClosedXML.Excel;
using Dapper;
using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.Services;
using Plataforma_CG.ViewModels;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static QuestPDF.Helpers.Colors;


namespace Plataforma_CG.Controllers
{
    public class TransferenciasController : Controller
    {
        private readonly AppDbContext _context;
        private readonly SapServiceLayerClient _sap;
        private readonly IConfiguration _cfg;

        // ⬅️ Inyecta también SapServiceLayerClient
        public TransferenciasController(AppDbContext context, SapServiceLayerClient sap, IConfiguration cfg)
        {
            _context = context;
            _sap = sap;
            _cfg = cfg;
        }




        public IActionResult TransferenciasSucursal()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Transferencias/TransferenciasSucursal.cshtml");
        }

        public IActionResult OTransferencia()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Transferencias/OTransferencia.cshtml");
        }

        public IActionResult RomaneoTransferencia()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Transferencias/RomaneoTransferencia.cshtml");
        }

        public IActionResult Calendario()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Transferencias/Calendario.cshtml");
        }


        private async Task<string> GenerarConsecutivoAsync()
        {
            // Buscar el último consecutivo guardado
            var ultimo = await _context.Transferencias
                .OrderByDescending(t => t.Id)
                .Select(t => t.Consecutivo)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(ultimo))
                return "TRANSF-0000001";

            // Extraer el número después del guión
            var numeroStr = ultimo.Replace("TRANSF-", "");
            if (!int.TryParse(numeroStr, out int numero))
                numero = 0;

            return $"TRANSF-{(numero + 1).ToString("D7")}";
        }



        // GET: /Transferencias/TransferenciasCedis
        // GET: /Transferencias/TransferenciasCedis
        [HttpGet]
        public async Task<IActionResult> TransferenciasCedis()
        {
            // Calcula folio como en tu ejemplo (OV)
            int ultimoId = await _context.Transferencias.AnyAsync()
                ? await _context.Transferencias.MaxAsync(t => t.Id)
                : 0;

            string consecutivo = $"TRANSF-{(ultimoId + 1).ToString("D7")}";

            var vm = new SolicitudTransferenciaViewModel
            {
                Consecutivo = consecutivo,
                Sucursal = "",
                FechaSolicitud = DateTime.Today,
                SeriesDisponibles = await _context.Series
                    .Where(s => s.Sucursal != "Matriz" && s.Sucursal != "Lagos") // 🔹 Filtro agregado
                    .OrderBy(s => s.Sucursal)
                    .Select(s => new SelectListItem
                    {
                        Value = s.Sucursal,
                        Text = s.Sucursal
                    })
                    .ToListAsync(),
                Productos = new List<SolicitudTransferenciaProductoVM> { new() }
            };

            return View("~/Views/Transferencias/TransferenciasCedis.cshtml", vm);
        }


        // GET: /Transferencias/Nueva
        [HttpGet]
        public async Task<IActionResult> Nueva()
        {
            var vm = new SolicitudTransferenciaViewModel
            {
                SeriesDisponibles = await _context.Series
                    .OrderBy(s => s.NombreSerie)
                    .Select(s => new SelectListItem { Value = s.NombreSerie, Text = s.NombreSerie })
                    .ToListAsync(),
                Productos = new List<SolicitudTransferenciaProductoVM> { new() }
            };
            return View("NuevaSolicitudTransferencia", vm);
        }




        [HttpGet("Transferencias/ExistenTransferenciasEstatus4")]
        public async Task<IActionResult> ExistenTransferenciasEstatus4()
        {
            var existen = await _context.Transferencias
                .AsNoTracking()
                .AnyAsync(t => t.Estatus == 4);

            return Json(new { existen });
        }
        [HttpGet("Transferencias/EstatusTransferencias")]
        public async Task<IActionResult> EstatusTransferencias()
        {
            const int MINUTOS_ATORADO = 2;
            int minutosAtoradoNeg = -MINUTOS_ATORADO;

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var jobs = (await conn.QueryAsync(@"
        SELECT 
            j.JobId,
            j.TransferenciaId,
            j.Estado,
            j.Procesadas,
            j.TotalEtiquetas,
            j.Exitosas,
            j.Fallidas,
            Atorado =
                CASE 
                    WHEN EXISTS (
                        SELECT 1
                        FROM dbo.TransferenciaSyncDetalle d
                        WHERE d.JobId = j.JobId
                          AND d.Estado = 'EnProceso'
                          AND ISNULL(d.Intentos, 0) < 5
                          AND (
                                d.FechaUltimoIntento IS NULL
                                OR d.FechaUltimoIntento < DATEADD(MINUTE, @MinutosAtoradoNeg, GETDATE())
                              )
                    )
                    THEN CAST(1 AS bit)
                    ELSE CAST(0 AS bit)
                END
        FROM dbo.TransferenciaSyncJob j
        INNER JOIN (
            SELECT TransferenciaId, MAX(JobId) AS JobId
            FROM dbo.TransferenciaSyncJob
            GROUP BY TransferenciaId
        ) ult ON ult.JobId = j.JobId
        WHERE j.TransferenciaId IN (
            SELECT Id FROM dbo.Transferencias WHERE Estatus = 4
        );
    ", new { MinutosAtoradoNeg = minutosAtoradoNeg })).ToList();

            var result = jobs.Select(j => new
            {
                transferenciaId = (int)j.TransferenciaId,
                estado = (string)j.Estado,
                procesadas = (int)j.Procesadas,
                total = (int)j.TotalEtiquetas,
                exitosas = (int)j.Exitosas,
                fallidas = (int)j.Fallidas,
                atorado = (bool)j.Atorado
            });

            return Json(new { existen = jobs.Any(), jobs = result });
        }


        //// ============================
        //// GUARDAR TRANSFERENCIA
        //// ============================
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> Guardar(SolicitudTransferenciaViewModel model, string accion)
        //{
        //    static bool IsAjax(HttpRequest req) =>
        //        string.Equals(req.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        //    // Normalizar acción
        //    if (string.IsNullOrWhiteSpace(accion)) accion = "salir";
        //    try { model.Accion = accion; } catch { }
        //    ModelState.Remove("accion");
        //    ModelState.Remove("Accion");

        //    // FIX Nota requerida (permitir vacía)
        //    if (model.Productos != null)
        //    {
        //        for (int i = 0; i < model.Productos.Count; i++)
        //        {
        //            model.Productos[i].Nota ??= "";
        //            ModelState.Remove($"Productos[{i}].Nota");
        //        }
        //    }

        //    // ============================
        //    // Validaciones mínimas
        //    // ============================
        //    if (string.IsNullOrWhiteSpace(model.Sucursal))
        //        ModelState.AddModelError(nameof(model.Sucursal), "Selecciona una sucursal.");

        //    bool tieneAlMenosUnaLineaValida = false;

        //    if (model.Productos == null || model.Productos.Count == 0)
        //    {
        //        ModelState.AddModelError("", "Debes capturar al menos un artículo con cantidad y cajas.");
        //    }
        //    else
        //    {
        //        for (int i = 0; i < model.Productos.Count; i++)
        //        {
        //            var p = model.Productos[i];

        //            // Sólo validamos si hay SKU
        //            if (!string.IsNullOrWhiteSpace(p.ProductoCodigo))
        //            {
        //                if (p.CantidadKg <= 0)
        //                {
        //                    ModelState.AddModelError($"Productos[{i}].CantidadKg", "La cantidad (kg) debe ser mayor a 0.");
        //                }

        //                if (p.Cajas <= 0)
        //                {
        //                    ModelState.AddModelError($"Productos[{i}].Cajas", "Las cajas deben ser mayores a 0.");
        //                }

        //                if (p.CantidadKg > 0 && p.Cajas > 0)
        //                {
        //                    tieneAlMenosUnaLineaValida = true;
        //                }
        //            }
        //        }

        //        if (!tieneAlMenosUnaLineaValida)
        //        {
        //            ModelState.AddModelError("", "Debes capturar al menos un artículo con cantidad y cajas mayores a 0.");
        //        }
        //    }

        //    if (!ModelState.IsValid)
        //    {
        //        if (IsAjax(Request))
        //        {
        //            var errsDict = ModelState
        //                .Where(x => x.Value.Errors.Any())
        //                .ToDictionary(
        //                    k => k.Key,
        //                    v => v.Value.Errors.Select(e => e.ErrorMessage).ToArray()
        //                );

        //            var resumen = string.Join("\n", errsDict.Select(kvp =>
        //                string.IsNullOrEmpty(kvp.Key)
        //                    ? string.Join(", ", kvp.Value)
        //                    : $"{kvp.Key}: {string.Join(", ", kvp.Value)}"));

        //            return BadRequest(new
        //            {
        //                ok = false,
        //                errors = errsDict,
        //                message = string.IsNullOrWhiteSpace(resumen)
        //                    ? "Validación incompleta."
        //                    : resumen
        //            });
        //        }

        //        model.SeriesDisponibles = await _context.Series
        //            .Where(s => s.Sucursal != "Matriz" && s.Sucursal != "Lagos")
        //            .OrderBy(s => s.Sucursal)
        //            .Select(s => new SelectListItem { Value = s.Sucursal, Text = s.Sucursal })
        //            .ToListAsync();

        //        TempData["Error"] = "Revisa la captura de la solicitud.";
        //        return View("~/Views/Transferencias/TransferenciasCedis.cshtml", model);
        //    }
        //    // ==================================================
        //    // Armado de DETALLE + marca de sobre-presupuesto
        //    // ==================================================
        //    var skusSobrePresupuesto = new List<string>();
        //    var detalles = new List<TransferenciaDetalle>();

        //    // ✅ DECLARAR AQUÍ (ANTES DEL FOREACH)
        //    bool requiereAutorizacionPresupuesto = false;

        //    if (model.Productos != null)
        //    {
        //        foreach (var p in model.Productos
        //                     .Where(p => !string.IsNullOrWhiteSpace(p.ProductoCodigo)
        //                              && p.CantidadKg > 0
        //                              && p.Cajas > 0))
        //        {
        //            var sku = (p.ProductoCodigo ?? "").Trim().ToUpper();

        //            // Regla de autorización de presupuesto
        //            decimal presupuesto = Convert.ToDecimal(p.Presupuesto);
        //            bool lineaSobrePresupuesto =
        //                   presupuesto <= 0m
        //                || p.CantidadKg > presupuesto;

        //            // ✅ SOLO para cabecera / aviso
        //            if (lineaSobrePresupuesto)
        //            {
        //                skusSobrePresupuesto.Add(sku);
        //                requiereAutorizacionPresupuesto = true;
        //            }

        //            var det = new TransferenciaDetalle
        //            {
        //                ProductoCodigo = sku,
        //                ProductoNombre = SoloNombreProducto(p.ProductoNombre, sku).ToUpper(),
        //                CantidadKg = p.CantidadKg,
        //                Nota = p.Nota ?? "",
        //                Cajas = Math.Max(1, (int)Math.Ceiling(p.Cajas)),

        //                // ✅ SIEMPRE 0 AL GUARDAR
        //                AutorizacionPresupuestoLinea = false
        //            };

        //            detalles.Add(det);
        //        }
        //    }


        //    if (detalles.Count == 0)
        //    {
        //        if (IsAjax(Request))
        //            return BadRequest(new { ok = false, message = "No hay líneas válidas para guardar." });

        //        ModelState.AddModelError("", "No hay líneas válidas para guardar.");
        //        model.SeriesDisponibles = await _context.Series
        //            .Where(s => s.Sucursal != "Matriz" && s.Sucursal != "Lagos")
        //            .OrderBy(s => s.Sucursal)
        //            .Select(s => new SelectListItem { Value = s.Sucursal, Text = s.Sucursal })
        //            .ToListAsync();
        //        return View("~/Views/Transferencias/TransferenciasCedis.cshtml", model);
        //    }

        //    // 🔴 Aquí definimos si la TRANSFERENCIA requiere autorización


        //    await using var tx = await _context.Database.BeginTransactionAsync();
        //    try
        //    {
        //        // ============================
        //        // 1) Cabecera con consecutivo TEMPORAL
        //        // ============================
        //        // 36 caracteres -> recortamos a 20 para que quepa en NVARCHAR(20)
        //        string consecutivoTemporal = $"TMP-{Guid.NewGuid():N}".Substring(0, 20);

        //        // Estatus de la transferencia:
        //        // 1 = normal, 2 = requiere autorización de presupuesto
        //        int estatusTransferencia = requiereAutorizacionPresupuesto ? 2 : 1;

        //        var ent = new Transferencia
        //        {
        //            Consecutivo = consecutivoTemporal,
        //            Sucursal = model.Sucursal,
        //            FechaSolicitud = model.FechaSolicitud,
        //            Observacion = model.Observacion,
        //            FechaCreacion = DateTime.Now,
        //            Estatus = estatusTransferencia,
        //            UsuarioSolicita = User?.Identity?.Name ?? "",
        //            Detalles = detalles,

        //            // Si tienes este campo:
        //            // AutorizacionPresupuesto = !requiereAutorizacionPresupuesto
        //        };

        //        _context.Transferencias.Add(ent);
        //        await _context.SaveChangesAsync();      // 🔹 Aquí ya tenemos ent.Id

        //        // ============================
        //        // 2) Folio definitivo con el Id
        //        // ============================
        //        ent.Consecutivo = $"TRANSF-{ent.Id:D7}";

        //        const int maxIntentos = 2;
        //        for (int intento = 1; intento <= maxIntentos; intento++)
        //        {
        //            try
        //            {
        //                _context.Transferencias.Update(ent);
        //                await _context.SaveChangesAsync();
        //                break;  // salió bien, rompemos el for
        //            }
        //            catch (DbUpdateException ex) when (EsDuplicadoConsecutivo(ex))
        //            {
        //                // Extremadamente raro, pero por si acaso
        //                ent.Consecutivo = $"TRANSF-{ent.Id:D7}-{intento}";
        //                if (intento == maxIntentos) throw;
        //            }
        //        }

        //        await tx.CommitAsync();

        //        var redirect = accion == "nuevo"
        //            ? Url.Action(nameof(TransferenciasCedis))
        //            : Url.Action("Inicio", "Home");

        //        // Respuesta AJAX: mandamos también los SKUs sobre presupuesto
        //        if (IsAjax(Request))
        //            return Ok(new
        //            {
        //                ok = true,
        //                folio = ent.Consecutivo,
        //                redirect,
        //                sobrePresupuesto = requiereAutorizacionPresupuesto,
        //                skusSobrePresupuesto
        //            });

        //        // Mensajes para vista normal
        //        if (requiereAutorizacionPresupuesto && skusSobrePresupuesto.Any())
        //        {
        //            TempData["Warning"] =
        //                $"Transferencia guardada con folio {ent.Consecutivo}, " +
        //                $"pero se superó el presupuesto o no existe en: {string.Join(", ", skusSobrePresupuesto)}. " +
        //                "Queda en estatus 2 (pendiente de autorización).";
        //        }
        //        else
        //        {
        //            TempData["Success"] = $"Transferencia guardada con folio {ent.Consecutivo}.";
        //        }

        //        return Redirect(redirect!);
        //    }
        //    catch (DbUpdateException ex)
        //    {
        //        await tx.RollbackAsync();

        //        var detalle = ex.InnerException?.Message ?? ex.Message;

        //        if (IsAjax(Request))
        //            return StatusCode(500, new
        //            {
        //                ok = false,
        //                message = $"Error BD al guardar la transferencia: {detalle}"
        //            });

        //        ModelState.AddModelError("", $"Error BD al guardar la transferencia: {detalle}");
        //    }
        //    catch (Exception)
        //    {
        //        await tx.RollbackAsync();

        //        if (IsAjax(Request))
        //            return StatusCode(500, new { ok = false, message = "Error inesperado al guardar la transferencia." });

        //        ModelState.AddModelError("", "Error inesperado al guardar la transferencia.");
        //    }

        //    // Si llega aquí es porque algo tronó y devolvemos la vista con el modelo
        //    model.SeriesDisponibles = await _context.Series
        //        .Where(s => s.Sucursal != "Matriz" && s.Sucursal != "Lagos")
        //        .OrderBy(s => s.Sucursal)
        //        .Select(s => new SelectListItem { Value = s.Sucursal, Text = s.Sucursal })
        //        .ToListAsync();

        //    return View("~/Views/Transferencias/TransferenciasCedis.cshtml", model);
        //}


        private async Task<PresupuestoConsumoDto?> ObtenerPresupuestoDetalleAsync(
         string sucursal,              // (no se usa en este SQL; si luego lo ocupas, lo metemos)
         string sku,
         DateTime fechaSolicitud,
         int? vendedorId,
         bool esCanalCedis,
         string? canalCliente,
         CancellationToken ct)
        {
            const string sql = @"
DECLARE 
    @Canal NVARCHAR(100),
    @Mes   INT,
    @Anio  INT,
    @SkuN  NVARCHAR(50),
    @VendedorId INT;

SET @SkuN = UPPER(LTRIM(RTRIM(@Sku)));
SET @Mes  = MONTH(@FechaSolicitud);
SET @Anio = YEAR(@FechaSolicitud);
SET @VendedorId = @VendedorIdParam;

-- CANAL (solo si es CEDIS)
IF (@EsCanalCedis = 1)
BEGIN
    SET @Canal = UPPER(LTRIM(RTRIM(@CanalParam)));
END
ELSE
BEGIN
    SET @Canal = NULL;
END;

;WITH
productos AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
        ProductoNombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), ''), a.ProductoCodigo)
    FROM dbo.ArticuloSap a
),
clientes AS (
    SELECT
        Cliente        = UPPER(LTRIM(RTRIM(cs.Cliente))),
        NombreCliente  = COALESCE(NULLIF(LTRIM(RTRIM(cs.NombreCliente)), ''), cs.Cliente),
        VendedorId     = cs.VendedorId,
        VendedorNombre = LTRIM(RTRIM(cs.VendedorNombre)),
        U_CANAL        = UPPER(LTRIM(RTRIM(cs.U_CANAL)))
    FROM dbo.ClienteSap cs
),
vendedores AS (
    SELECT DISTINCT VendedorId, VendedorNombre
    FROM clientes
    WHERE VendedorId IS NOT NULL
),

canal_vendedores AS (
    SELECT DISTINCT
        Canal      = UPPER(LTRIM(RTRIM(c.U_CANAL))),
        VendedorId = c.VendedorId
    FROM dbo.ClienteSap c
    WHERE c.VendedorId IS NOT NULL
      AND UPPER(LTRIM(RTRIM(c.U_CANAL))) LIKE 'CEDIS%'
),

ov AS (
    SELECT
        o.Id,
        o.Cliente,
        o.VendedorId,
        o.Estatus,
        o.Serie,
        FechaDate = TRY_CONVERT(date, o.FechaEntrega)
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.Series ser ON o.Serie = ser.NombreSerie
    WHERE o.FechaEntrega IS NOT NULL
      AND o.Estatus BETWEEN 1 AND 4
      AND ser.Sucursal = 'MATRIZ'
),

presupuestos_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(pc.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
        Mes   = pc.Mes,
        Anio  = pc.Anio,
        Presupuesto = SUM(pc.PresupuestoAsignado)
    FROM dbo.PresupuestoCedis pc
    GROUP BY
        UPPER(LTRIM(RTRIM(pc.Canal))),
        UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
        pc.Mes, pc.Anio
),

consumo_cedis_base AS (
    SELECT Canal, SKU, Mes, Anio, SUM(Kg) Kg
    FROM (
        -- OV CEDIS
        SELECT
            Canal = UPPER(LTRIM(RTRIM(cli.U_CANAL))),
            SKU   = UPPER(op.ProductoCodigo),
            Mes   = MONTH(ov.FechaDate),
            Anio  = YEAR(ov.FechaDate),
            Kg    = SUM(CAST(op.Peso AS DECIMAL(18,4)))
        FROM ov
        JOIN dbo.OrdenVentaProducto op ON op.PedidoId = ov.Id
        JOIN dbo.ClienteSap cli        ON cli.Cliente = ov.Cliente
        WHERE UPPER(LTRIM(RTRIM(cli.U_CANAL))) LIKE 'CEDIS%'
        GROUP BY
            UPPER(LTRIM(RTRIM(cli.U_CANAL))),
            UPPER(op.ProductoCodigo),
            MONTH(ov.FechaDate),
            YEAR(ov.FechaDate)

        UNION ALL

        -- Transferencias
        SELECT
            Canal = UPPER(LTRIM(RTRIM(s.Canal))),
            SKU   = UPPER(td.ProductoCodigo),
            Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
            Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
            Kg    = SUM(CAST(td.CantidadKg AS DECIMAL(18,4)))
        FROM dbo.Transferencias t
        JOIN dbo.TransferenciaDetalles td ON td.TransferenciaId = t.Id
        JOIN dbo.Series s                 ON s.Sucursal = t.Sucursal
        WHERE t.FechaSolicitud IS NOT NULL
          AND t.Estatus BETWEEN 1 AND 4
          AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
        GROUP BY
            UPPER(LTRIM(RTRIM(s.Canal))),
            UPPER(td.ProductoCodigo),
            MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
            YEAR(TRY_CONVERT(date, t.FechaSolicitud))
    ) X
    GROUP BY Canal, SKU, Mes, Anio
),

todo_cedis AS (
    SELECT
        'CEDIS' AS Origen,
        pc.Mes,
        pc.Anio,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        pc.Canal,
        CAST(NULL AS INT) AS VendedorId,
        pc.SKU,
        pc.Presupuesto,
        ISNULL(cc.Kg,0) AS Kg
    FROM presupuestos_cedis pc
    LEFT JOIN consumo_cedis_base cc
        ON cc.Canal = pc.Canal
       AND cc.SKU   = pc.SKU
       AND cc.Mes   = pc.Mes
       AND cc.Anio  = pc.Anio
),

presupuestos_vendedor AS (
    SELECT
        VendedorId,
        SKU = UPPER(pv.ProductoCodigo),
        Mes = pv.Mes,
        Anio = pv.Anio,
        Presupuesto = SUM(pv.PresupuestoAsignado)
    FROM dbo.PresupuestoVendedor pv
    GROUP BY
        pv.VendedorId,
        UPPER(pv.ProductoCodigo),
        pv.Mes,
        pv.Anio
),

pres_vendedor_x_canal AS (
    SELECT
        cv.Canal,
        pv.SKU,
        pv.Mes,
        pv.Anio,
        PresTotalCanal = SUM(CAST(pv.Presupuesto AS DECIMAL(18,4)))
    FROM presupuestos_vendedor pv
    JOIN canal_vendedores cv
        ON cv.VendedorId = pv.VendedorId
    GROUP BY
        cv.Canal, pv.SKU, pv.Mes, pv.Anio
),

consumo_vendedor_normal AS (
    SELECT
        o.VendedorId,
        SKU  = UPPER(op.ProductoCodigo),
        Mes  = MONTH(o.FechaEntrega),
        Anio = YEAR(o.FechaEntrega),
        Kg   = SUM(CAST(op.Peso AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.OrdenVentaProducto op ON op.PedidoId = o.Id
    JOIN dbo.Series s ON s.NombreSerie = o.Serie
    JOIN dbo.ClienteSap c ON c.Cliente = o.Cliente
                         AND ISNULL(UPPER(c.U_CANAL),'') NOT LIKE 'CEDIS%'
    WHERE  o.Estatus BETWEEN 1 AND 4
      AND s.Sucursal = 'MATRIZ'
    GROUP BY
        o.VendedorId,
        UPPER(op.ProductoCodigo),
        MONTH(o.FechaEntrega),
        YEAR(o.FechaEntrega)
),

consumo_vendedor_desde_cedis AS (
    SELECT
        VendedorId = pv.VendedorId,
        SKU        = pv.SKU,
        Mes        = pv.Mes,
        Anio       = pv.Anio,
        Kg = SUM(
            CASE
                WHEN ISNULL(pxc.PresTotalCanal,0) <= 0 THEN 0
                ELSE (cb.Kg * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal))
            END
        )
    FROM presupuestos_vendedor pv
    JOIN canal_vendedores cv
        ON cv.VendedorId = pv.VendedorId
    JOIN pres_vendedor_x_canal pxc
        ON pxc.Canal = cv.Canal
       AND pxc.SKU   = pv.SKU
       AND pxc.Mes   = pv.Mes
       AND pxc.Anio  = pv.Anio
    JOIN consumo_cedis_base cb
        ON cb.Canal = cv.Canal
       AND cb.SKU   = pv.SKU
       AND cb.Mes   = pv.Mes
       AND cb.Anio  = pv.Anio
    GROUP BY
        pv.VendedorId, pv.SKU, pv.Mes, pv.Anio
),

consumo_vendedor_total AS (
    SELECT VendedorId, SKU, Mes, Anio, SUM(Kg) Kg
    FROM (
        SELECT * FROM consumo_vendedor_normal
        UNION ALL
        SELECT * FROM consumo_vendedor_desde_cedis
    ) x
    GROUP BY VendedorId, SKU, Mes, Anio
),

todo_vendedor AS (
    SELECT
        'VENDEDOR' AS Origen,
        pv.Mes,
        pv.Anio,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        pv.VendedorId,
        pv.SKU,
        pv.Presupuesto,
        ISNULL(cv.Kg,0) AS Kg
    FROM presupuestos_vendedor pv
    LEFT JOIN consumo_vendedor_total cv
        ON cv.VendedorId = pv.VendedorId
       AND cv.SKU = pv.SKU
       AND cv.Mes = pv.Mes
       AND cv.Anio = pv.Anio
),

-- ======= surtido (tu lógica) =======
surtido_ov_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(cli.U_CANAL))),
        SKU   = UPPER(sd.Articulo),
        Mes   = MONTH(se.FechaValidacion),
        Anio  = YEAR(se.FechaValidacion),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp           ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se   ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd      ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    JOIN dbo.ClienteSap cli         ON cli.Cliente = o.Cliente
    WHERE o.Estatus <> 0
      AND se.FechaValidacion IS NOT NULL
      AND UPPER(LTRIM(RTRIM(cli.U_CANAL))) LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(cli.U_CANAL))),
        UPPER(sd.Articulo),
        MONTH(se.FechaValidacion),
        YEAR(se.FechaValidacion)
),
surtido_transferencias_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(s.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(ts.Sku))),
        Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
        Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
        KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    JOIN dbo.Transferencias t ON t.Id = ts.TransferenciaId
    JOIN dbo.Series s         ON s.Sucursal = t.Sucursal
    WHERE t.FechaSolicitud IS NOT NULL
      AND t.Estatus >= 5
      AND ts.KgSurtido > 0
      AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(s.Canal))),
        UPPER(LTRIM(RTRIM(ts.Sku))),
        MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
        YEAR(TRY_CONVERT(date, t.FechaSolicitud))
),
surtido_pedidos_transferencia_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(ser.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(ptd.ProductoCodigo))),
        Mes   = MONTH(TRY_CONVERT(date, pt.FechaSolicitud)),
        Anio  = YEAR(TRY_CONVERT(date, pt.FechaSolicitud)),
        KgSurtido = SUM(CAST(ptd.CantidadKg AS DECIMAL(18,4)))
    FROM dbo.PedidosTransferencia pt
    JOIN dbo.PedidosTransferenciaDetalle ptd ON ptd.PedidoTransferenciaId = pt.Id
    JOIN dbo.Series ser ON ser.Sucursal = pt.Destino
    WHERE pt.FechaSolicitud IS NOT NULL
      AND UPPER(LTRIM(RTRIM(ser.Canal))) LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(ser.Canal))),
        UPPER(LTRIM(RTRIM(ptd.ProductoCodigo))),
        MONTH(TRY_CONVERT(date, pt.FechaSolicitud)),
        YEAR(TRY_CONVERT(date, pt.FechaSolicitud))
),
surtido_cedis_base AS (
    SELECT Canal, SKU, Mes, Anio, SUM(KgSurtido) AS KgSurtido
    FROM (
        SELECT Canal, SKU, Mes, Anio, KgSurtido FROM surtido_ov_cedis
        UNION ALL
        SELECT Canal, SKU, Mes, Anio, KgSurtido FROM surtido_transferencias_cedis
        UNION ALL
        SELECT Canal, SKU, Mes, Anio, KgSurtido FROM surtido_pedidos_transferencia_cedis
    ) x
    GROUP BY Canal, SKU, Mes, Anio
),
surtido_vendedor_desde_cedis AS (
    SELECT
        VendedorId = pv.VendedorId,
        SKU        = pv.SKU,
        Mes        = pv.Mes,
        Anio       = pv.Anio,
        KgSurtido  = SUM(
            CASE
                WHEN ISNULL(pxc.PresTotalCanal,0) <= 0 THEN 0
                ELSE (sb.KgSurtido * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal))
            END
        )
    FROM presupuestos_vendedor pv
    JOIN canal_vendedores cv
        ON cv.VendedorId = pv.VendedorId
    JOIN pres_vendedor_x_canal pxc
        ON pxc.Canal = cv.Canal
       AND pxc.SKU   = pv.SKU
       AND pxc.Mes   = pv.Mes
       AND pxc.Anio  = pv.Anio
    JOIN surtido_cedis_base sb
        ON sb.Canal = cv.Canal
       AND sb.SKU   = pv.SKU
       AND sb.Mes   = pv.Mes
       AND sb.Anio  = pv.Anio
    GROUP BY
        pv.VendedorId, pv.SKU, pv.Mes, pv.Anio
),
surtido_vendedor_total AS (
    SELECT VendedorId, SKU, Mes, Anio, SUM(KgSurtido) KgSurtido
    FROM (
        SELECT * FROM surtido_vendedor_desde_cedis
    ) x
    GROUP BY VendedorId, SKU, Mes, Anio
),
surtido_real AS (
    SELECT
        'CEDIS' AS Origen,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        Canal,
        CAST(NULL AS INT) AS VendedorId,
        SKU, Mes, Anio,
        SUM(KgSurtido) AS KgSurtido
    FROM surtido_cedis_base
    GROUP BY Canal, SKU, Mes, Anio

    UNION ALL

    SELECT
        'VENDEDOR',
        CAST(NULL AS NVARCHAR(50)),
        CAST(NULL AS NVARCHAR(100)),
        VendedorId,
        SKU, Mes, Anio,
        SUM(KgSurtido)
    FROM surtido_vendedor_total
    GROUP BY VendedorId, SKU, Mes, Anio
)

SELECT
    t.Origen,
    t.Mes AS MesConsulta,
    t.Anio AS AnioConsulta,
    t.VendedorId,
    t.Canal,
    t.SKU AS ProductoCodigo,
    CAST(t.Presupuesto AS DECIMAL(18,4)) AS PresupuestoAsignado,
    CAST(t.Kg AS DECIMAL(18,4)) AS KgPedidosMes,
    CAST(ISNULL(sr.KgSurtido,0) AS DECIMAL(18,4)) AS KgSurtidoReal,
    CAST(
        CASE
            WHEN t.Presupuesto - ISNULL(t.Kg,0) - ISNULL(sr.KgSurtido,0) < 0 THEN 0
            ELSE t.Presupuesto - ISNULL(t.Kg,0) - ISNULL(sr.KgSurtido,0)
        END
    AS DECIMAL(18,4)) AS DisponibleVenta
FROM (
    -- CEDIS
    SELECT * FROM todo_cedis
    WHERE @EsCanalCedis = 1
      AND Canal = @Canal AND Mes = @Mes AND Anio = @Anio AND SKU = @SkuN

    UNION ALL

    -- VENDEDOR
    SELECT * FROM todo_vendedor
    WHERE @EsCanalCedis = 0
      AND Mes = @Mes AND Anio = @Anio AND SKU = @SkuN
      AND (@VendedorId IS NULL OR VendedorId = @VendedorId)
) t
LEFT JOIN surtido_real sr
    ON sr.Origen = t.Origen
   AND sr.SKU    = t.SKU
   AND sr.Mes    = t.Mes
   AND sr.Anio   = t.Anio
   AND (
        (t.Origen = 'CEDIS'    AND sr.Canal      = t.Canal)
     OR (t.Origen = 'VENDEDOR' AND sr.VendedorId = t.VendedorId)
   )
ORDER BY Origen;";

            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            var rows = (await conn.QueryAsync<PresupuestoConsumoDto>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Sku = sku,
                        FechaSolicitud = fechaSolicitud,
                        VendedorIdParam = vendedorId,
                        EsCanalCedis = esCanalCedis ? 1 : 0,
                        CanalParam = (canalCliente ?? "")
                    },
                    cancellationToken: ct
                ))).ToList();

            // ✅ Selección correcta
            if (esCanalCedis)
            {
                var canalUp = (canalCliente ?? "").Trim().ToUpperInvariant();
                return rows.FirstOrDefault(r =>
                    (r.Origen ?? "") == "CEDIS" &&
                    ((r.Canal ?? "").Trim().ToUpperInvariant() == canalUp));
            }

            if (vendedorId.HasValue)
                return rows.FirstOrDefault(r => (r.Origen ?? "") == "VENDEDOR" && r.VendedorId == vendedorId.Value);

            return rows.FirstOrDefault(r => (r.Origen ?? "") == "VENDEDOR");
        }



        // ============================
        // GUARDAR TRANSFERENCIA
        // ============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guardar(SolicitudTransferenciaViewModel model, string accion)
        {
            static bool IsAjax(HttpRequest req) =>
                string.Equals(req.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

            // Normalizar acción
            if (string.IsNullOrWhiteSpace(accion)) accion = "salir";
            try { model.Accion = accion; } catch { }
            ModelState.Remove("accion");
            ModelState.Remove("Accion");

            // FIX Nota requerida (permitir vacía)
            if (model.Productos != null)
            {
                for (int i = 0; i < model.Productos.Count; i++)
                {
                    model.Productos[i].Nota ??= "";
                    ModelState.Remove($"Productos[{i}].Nota");
                }
            }

            // ============================
            // Validaciones mínimas
            // ============================
            if (string.IsNullOrWhiteSpace(model.Sucursal))
                ModelState.AddModelError(nameof(model.Sucursal), "Selecciona una sucursal.");

            bool tieneAlMenosUnaLineaValida = false;

            if (model.Productos == null || model.Productos.Count == 0)
            {
                ModelState.AddModelError("", "Debes capturar al menos un artículo con cantidad y cajas.");
            }
            else
            {
                for (int i = 0; i < model.Productos.Count; i++)
                {
                    var p = model.Productos[i];

                    // Sólo validamos si hay SKU
                    if (!string.IsNullOrWhiteSpace(p.ProductoCodigo))
                    {
                        if (p.CantidadKg <= 0)
                            ModelState.AddModelError($"Productos[{i}].CantidadKg", "La cantidad (kg) debe ser mayor a 0.");

                        if (p.Cajas <= 0)
                            ModelState.AddModelError($"Productos[{i}].Cajas", "Las cajas deben ser mayores a 0.");

                        if (p.CantidadKg > 0 && p.Cajas > 0)
                            tieneAlMenosUnaLineaValida = true;
                    }
                }

                if (!tieneAlMenosUnaLineaValida)
                    ModelState.AddModelError("", "Debes capturar al menos un artículo con cantidad y cajas mayores a 0.");
            }

            if (!ModelState.IsValid)
            {
                if (IsAjax(Request))
                {
                    var errsDict = ModelState
                        .Where(x => x.Value.Errors.Any())
                        .ToDictionary(
                            k => k.Key,
                            v => v.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        );

                    var resumen = string.Join("\n", errsDict.Select(kvp =>
                        string.IsNullOrEmpty(kvp.Key)
                            ? string.Join(", ", kvp.Value)
                            : $"{kvp.Key}: {string.Join(", ", kvp.Value)}"));

                    return BadRequest(new
                    {
                        ok = false,
                        errors = errsDict,
                        message = string.IsNullOrWhiteSpace(resumen)
                            ? "Validación incompleta."
                            : resumen
                    });
                }

                model.SeriesDisponibles = await _context.Series
                    .Where(s => s.Sucursal != "Matriz" && s.Sucursal != "Lagos")
                    .OrderBy(s => s.Sucursal)
                    .Select(s => new SelectListItem { Value = s.Sucursal, Text = s.Sucursal })
                    .ToListAsync();

                TempData["Error"] = "Revisa la captura de la solicitud.";
                return View("~/Views/Transferencias/TransferenciasCedis.cshtml", model);
            }

            // ==================================================
            // Presupuesto (igual que OV): validar contra DisponibleVenta (SQL)
            // ==================================================
            const decimal TOL = 0.01m;
            var skusSobrePresupuestoSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ✅ bandera cabecera
            bool requiereAutorizacionPresupuesto = false;

            // Datos de sucursal/canal para el SQL
            string sucursalUp = (model.Sucursal ?? "").Trim().ToUpperInvariant();

            var serieSucursal = await _context.Series
              .AsNoTracking()
              .Where(s => s.Sucursal != null && s.Sucursal.ToUpper() == sucursalUp)
              .Select(s => new { s.Canal })
              .FirstOrDefaultAsync(HttpContext.RequestAborted);

            string canalSucursalUp = (serieSucursal?.Canal ?? "").Trim().ToUpperInvariant();
            bool esCanalCedis = canalSucursalUp.StartsWith("CEDIS");

            // Transferencias: normalmente solo validas en modo CEDIS (porque el SQL requiere CanalParam)
            bool aplicarValidacionPresupuesto = esCanalCedis;

            // En transferencias no hay vendedorId (tu SQL en modo CEDIS lo ignora)
            int? vendedorId = null;

            // Kilos por SKU (por si se repite SKU en la captura)
            var productos = model.Productos ?? new List<SolicitudTransferenciaProductoVM>();

            var kilosPorSku = productos
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductoCodigo) && x.CantidadKg > 0 && x.Cajas > 0)
                .GroupBy(x => (x.ProductoCodigo ?? "").Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.Sum(z => z.CantidadKg));

            if (aplicarValidacionPresupuesto && kilosPorSku.Count > 0)
            {
                foreach (var kvp in kilosPorSku)
                {
                    var sku = kvp.Key;
                    var kgPedidoSku = kvp.Value;
                    if (kgPedidoSku <= 0m) continue;

                    var detPres = await ObtenerPresupuestoDetalleAsync(
                        sucursal: model.Sucursal,                 // tu SQL no usa sucursal, pero lo pasas
                        sku: sku,
                        fechaSolicitud: model.FechaSolicitud,
                        vendedorId: vendedorId,                   // null
                        esCanalCedis: true,                       // modo CEDIS
                        canalCliente: canalSucursalUp,            // ✅ CANAL de la sucursal (Series.Canal)
                        ct: HttpContext.RequestAborted
                    );

                    // ✅ Igual que OV: si no hay fila/presupuesto -> FAIL-OPEN (no autorizar)
                    if (detPres == null || detPres.DisponibleVenta == null)
                        continue;

                    var disp = detPres.DisponibleVenta.Value;

                    if (disp <= 0m || kgPedidoSku > (disp + TOL))
                    {
                        requiereAutorizacionPresupuesto = true;
                        skusSobrePresupuestoSet.Add(sku);


                    }
                }
            }

            var skusSobrePresupuesto = skusSobrePresupuestoSet.ToList();

            // ==================================================
            // Armado de DETALLE (sin usar p.Presupuesto del grid)
            // ==================================================
            var detalles = new List<TransferenciaDetalle>();

            if (model.Productos != null)
            {
                foreach (var p in model.Productos
                             .Where(p => !string.IsNullOrWhiteSpace(p.ProductoCodigo)
                                      && p.CantidadKg > 0
                                      && p.Cajas > 0))
                {
                    var sku = (p.ProductoCodigo ?? "").Trim().ToUpperInvariant();

                    var det = new TransferenciaDetalle
                    {
                        ProductoCodigo = sku,
                        ProductoNombre = SoloNombreProducto(p.ProductoNombre, sku).ToUpperInvariant(),
                        CantidadKg = p.CantidadKg,
                        Nota = p.Nota ?? "",
                        Cajas = Math.Max(1, (int)Math.Ceiling(p.Cajas)),

                        // ✅ SIEMPRE false AL GUARDAR (tu regla actual)
                        //AutorizacionPresupuestoLinea = false
                        AutorizacionPresupuestoLinea = true
                    };

                    detalles.Add(det);
                }
            }

            if (detalles.Count == 0)
            {
                if (IsAjax(Request))
                    return BadRequest(new { ok = false, message = "No hay líneas válidas para guardar." });

                ModelState.AddModelError("", "No hay líneas válidas para guardar.");
                model.SeriesDisponibles = await _context.Series
                    .Where(s => s.Sucursal != "Matriz" && s.Sucursal != "Lagos")
                    .OrderBy(s => s.Sucursal)
                    .Select(s => new SelectListItem { Value = s.Sucursal, Text = s.Sucursal })
                    .ToListAsync();
                return View("~/Views/Transferencias/TransferenciasCedis.cshtml", model);
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // ============================
                // 1) Cabecera con consecutivo TEMPORAL
                // ============================
                // 36 caracteres -> recortamos a 20 para que quepa en NVARCHAR(20)
                string consecutivoTemporal = $"TMP-{Guid.NewGuid():N}".Substring(0, 20);

                // Estatus:
                // 1 = normal, 2 = requiere autorización de presupuesto

                //int estatusTransferencia = requiereAutorizacionPresupuesto ? 2 : 1;
                int estatusTransferencia = 1;

                var ent = new Transferencia
                {
                    Consecutivo = consecutivoTemporal,
                    Sucursal = model.Sucursal,
                    FechaSolicitud = model.FechaSolicitud,
                    Observacion = model.Observacion,
                    FechaCreacion = DateTime.Now,
                    Estatus = estatusTransferencia,
                    UsuarioSolicita = User?.Identity?.Name ?? "",
                    Detalles = detalles,

                    // Si tienes este campo y lo usas:
                    // AutorizacionPresupuesto = !requiereAutorizacionPresupuesto
                };

                _context.Transferencias.Add(ent);
                await _context.SaveChangesAsync(); // ya tenemos ent.Id

                // ============================
                // 2) Folio definitivo con el Id
                // ============================
                ent.Consecutivo = $"TRANSF-{ent.Id:D7}";

                const int maxIntentos = 2;
                for (int intento = 1; intento <= maxIntentos; intento++)
                {
                    try
                    {
                        _context.Transferencias.Update(ent);
                        await _context.SaveChangesAsync();
                        break;
                    }
                    catch (DbUpdateException ex) when (EsDuplicadoConsecutivo(ex))
                    {
                        ent.Consecutivo = $"TRANSF-{ent.Id:D7}-{intento}";
                        if (intento == maxIntentos) throw;
                    }
                }

                await tx.CommitAsync();

                var redirect = accion == "nuevo"
                    ? Url.Action(nameof(TransferenciasCedis))
                    : Url.Action("Inicio", "Home");

                // Respuesta AJAX: mandamos SKUs sobre presupuesto
                if (IsAjax(Request))
                    //return Ok(new
                    //{
                    //    ok = true,
                    //    folio = ent.Consecutivo,
                    //    redirect,
                    //    sobrePresupuesto = requiereAutorizacionPresupuesto,
                    //    skusSobrePresupuesto
                    //});
                    return Ok(new
                    {
                        ok = true,
                        folio = ent.Consecutivo,
                        redirect,
                        sobrePresupuesto = false,
                        skusSobrePresupuesto = new List<string>()
                    });

                // Mensajes para vista normal
                if (requiereAutorizacionPresupuesto && skusSobrePresupuesto.Any())
                {
                    TempData["Warning"] =
                        $"Transferencia guardada con folio {ent.Consecutivo}, " +
                        $"pero se superó el disponible o no existe en: {string.Join(", ", skusSobrePresupuesto)}. " +
                        "Queda en estatus 2 (pendiente de autorización).";
                }
                else
                {
                    TempData["Success"] = $"Transferencia guardada con folio {ent.Consecutivo}.";
                }

                return Redirect(redirect!);
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();

                var detalle = ex.InnerException?.Message ?? ex.Message;

                if (IsAjax(Request))
                    return StatusCode(500, new
                    {
                        ok = false,
                        message = $"Error BD al guardar la transferencia: {detalle}"
                    });

                ModelState.AddModelError("", $"Error BD al guardar la transferencia: {detalle}");
            }
            catch (Exception)
            {
                await tx.RollbackAsync();

                if (IsAjax(Request))
                    return StatusCode(500, new { ok = false, message = "Error inesperado al guardar la transferencia." });

                ModelState.AddModelError("", "Error inesperado al guardar la transferencia.");
            }

            // Si llega aquí es porque algo tronó y devolvemos la vista con el modelo
            model.SeriesDisponibles = await _context.Series
                .Where(s => s.Sucursal != "Matriz" && s.Sucursal != "Lagos")
                .OrderBy(s => s.Sucursal)
                .Select(s => new SelectListItem { Value = s.Sucursal, Text = s.Sucursal })
                .ToListAsync();

            return View("~/Views/Transferencias/TransferenciasCedis.cshtml", model);
        }












        // Igualito al de OV
        private static bool EsDuplicadoConsecutivo(DbUpdateException ex)
        {
            return ex.InnerException is SqlException sqlEx
                && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
        }








        // Autocomplete de productos (TRANSFERENCIAS)
        [HttpGet]
        public async Task<IActionResult> BuscarProductosAutocomplete(string term)
        {
            term = (term ?? "").Trim();
            if (term.Length == 0)
                return Json(Array.Empty<object>());

            var productos = await _sap.BuscarProductosAsync(term)
                            ?? new List<ProductoViewModel>();

            var resultado = productos.Select(p => new
            {
                // Lo que ve el usuario en el autocomplete
                label = $"{(p.ItemCode ?? "").ToUpper()} ({(p.ItemName ?? "").ToUpper()})",

                // Lo que usamos como SKU
                value = (p.ItemCode ?? "").ToUpper(),

                // 👇 Muy importante: promedio de kg por caja (U_KilosCaja mapeado a KilosCaja)
                kilosCaja = p.KilosCaja
            });

            return Json(resultado);
        }


        [HttpGet("Transferencias/PresupuestoDisponible")]
        public async Task<IActionResult> PresupuestoDisponible(
            string sucursal,
            string sku,
            DateTime fechaSolicitud,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sucursal) || string.IsNullOrWhiteSpace(sku))
                return BadRequest("Sucursal y SKU son requeridos.");

            const string sql = @"
DECLARE 
    @Canal NVARCHAR(100),
    @Mes   INT,
    @Anio  INT,
    @SkuN  NVARCHAR(50);

SET @SkuN = UPPER(LTRIM(RTRIM(@Sku)));

-- Canal de la sucursal (Transferencias se relacionan por Series.Sucursal)
SELECT TOP (1)
    @Canal = UPPER(LTRIM(RTRIM(s.Canal)))
FROM dbo.Series s
WHERE
    UPPER(LTRIM(RTRIM(s.Sucursal)))     = UPPER(LTRIM(RTRIM(@Sucursal)))
 OR UPPER(LTRIM(RTRIM(s.NombreSerie))) = UPPER(LTRIM(RTRIM(@Sucursal)))
ORDER BY
    CASE
        WHEN UPPER(LTRIM(RTRIM(s.Sucursal))) = UPPER(LTRIM(RTRIM(@Sucursal))) THEN 0
        ELSE 1
    END,
    UPPER(LTRIM(RTRIM(s.Canal)));

SET @Mes  = MONTH(@FechaSolicitud);
SET @Anio = YEAR(@FechaSolicitud);

WITH
-- =====================================================
-- CATÁLOGOS (mínimos necesarios)
-- =====================================================
productos AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
        ProductoNombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), ''), a.ProductoCodigo)
    FROM dbo.ArticuloSap a
),
clientes AS (
    SELECT
        Cliente        = UPPER(LTRIM(RTRIM(cs.Cliente))),
        NombreCliente  = COALESCE(NULLIF(LTRIM(RTRIM(cs.NombreCliente)), ''), cs.Cliente),
        VendedorId     = cs.VendedorId,
        VendedorNombre = LTRIM(RTRIM(cs.VendedorNombre)),
        U_CANAL        = UPPER(LTRIM(RTRIM(cs.U_CANAL)))
    FROM dbo.ClienteSap cs
),
vendedores AS (
    SELECT DISTINCT VendedorId, VendedorNombre
    FROM clientes
    WHERE VendedorId IS NOT NULL
),

-- =====================================================
-- Vendedores por Canal CEDIS (mapeo CANAL -> VENDEDOR)
-- =====================================================
canal_vendedores AS (
    SELECT DISTINCT
        Canal      = UPPER(LTRIM(RTRIM(c.U_CANAL))),
        VendedorId = c.VendedorId
    FROM dbo.ClienteSap c
    WHERE c.VendedorId IS NOT NULL
      AND UPPER(LTRIM(RTRIM(c.U_CANAL))) LIKE 'CEDIS%'
),

-- =====================================================
-- OV (MATRIZ) - incluye estatus 5 (igual a reporte)
-- =====================================================
ov AS (
    SELECT
        o.Id,
        o.Cliente,
        o.VendedorId,
        o.Estatus,
        o.Serie,
        FechaDate = TRY_CONVERT(date, o.FechaEntrega)
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.Series ser ON o.Serie = ser.NombreSerie
    WHERE o.FechaEntrega IS NOT NULL
      AND o.Estatus BETWEEN 1 AND 5
      AND ser.Sucursal = 'MATRIZ'
),

-- OV con surtido existente (para regla Estatus=5)
ov_con_surtido AS (
    SELECT DISTINCT o.Id
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp         ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
),

-- Peso pedido por OV + SKU
ov_peso_agg AS (
    SELECT
        PedidoId = op.PedidoId,
        SKU      = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        KgPedido = SUM(CAST(op.Peso AS DECIMAL(18,4)))
    FROM dbo.OrdenVentaProducto op
    GROUP BY
        op.PedidoId,
        UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
),

-- Surtido validado por OV + SKU
ov_surtido_agg AS (
    SELECT
        PedidoId = o.Id,
        SKU      = UPPER(LTRIM(RTRIM(sd.Articulo))),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp         ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    WHERE se.FechaValidacion IS NOT NULL
    GROUP BY
        o.Id,
        UPPER(LTRIM(RTRIM(sd.Articulo)))
),

-- Pendiente real por OV + SKU (pedido - surtido validado, clamp 0)
ov_pendiente_sku AS (
    SELECT
        ov.Id,
        ov.Cliente,
        ov.VendedorId,
        ov.Estatus,
        ov.FechaDate,
        p.SKU,
        KgPendiente =
            CAST(
                CASE
                    WHEN ov.Estatus = 5 AND os.Id IS NOT NULL THEN 0
                    ELSE
                        CASE
                            WHEN (p.KgPedido - ISNULL(sa.KgSurtido,0)) < 0 THEN 0
                            ELSE (p.KgPedido - ISNULL(sa.KgSurtido,0))
                        END
                END
            AS DECIMAL(18,4))
    FROM ov
    JOIN ov_peso_agg p
        ON p.PedidoId = ov.Id
    LEFT JOIN ov_surtido_agg sa
        ON sa.PedidoId = ov.Id
       AND sa.SKU      = p.SKU
    LEFT JOIN ov_con_surtido os
        ON os.Id = ov.Id
),

-- =====================================================
-- PRESUPUESTO CEDIS
-- =====================================================
presupuestos_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(pc.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
        Mes   = pc.Mes,
        Anio  = pc.Anio,
        Presupuesto = SUM(pc.PresupuestoAsignado)
    FROM dbo.PresupuestoCedis pc
    GROUP BY
        UPPER(LTRIM(RTRIM(pc.Canal))),
        UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
        pc.Mes, pc.Anio
),

-- =====================================================
-- SURTIDO POR TRANSFERENCIA (AGRUPADO) PARA REBAJAR PENDIENTE
-- =====================================================
tr_surtido_agg AS (
    SELECT
        ts.TransferenciaId,
        SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
        KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    GROUP BY
        ts.TransferenciaId,
        UPPER(LTRIM(RTRIM(ts.Sku)))
),

-- =====================================================
-- CONSUMO CEDIS BASE (PENDIENTE REAL)
--   1) OV CEDIS (pendiente real)
--   2) Transferencias (solicitado - surtido), clamp 0
-- =====================================================
consumo_cedis_base AS (
    SELECT Canal, SKU, Mes, Anio, SUM(Kg) Kg
    FROM (
        -- OV CEDIS (pendiente real)
        SELECT
            Canal = UPPER(LTRIM(RTRIM(cli.U_CANAL))),
            SKU   = ovp.SKU,
            Mes   = MONTH(ovp.FechaDate),
            Anio  = YEAR(ovp.FechaDate),
            Kg    = SUM(ovp.KgPendiente)
        FROM ov_pendiente_sku ovp
        JOIN dbo.ClienteSap cli ON cli.Cliente = ovp.Cliente
        WHERE UPPER(LTRIM(RTRIM(cli.U_CANAL))) LIKE 'CEDIS%'
        GROUP BY
            UPPER(LTRIM(RTRIM(cli.U_CANAL))),
            ovp.SKU,
            MONTH(ovp.FechaDate),
            YEAR(ovp.FechaDate)

        UNION ALL

        -- Transferencias (pendiente = solicitado - surtido)
        SELECT
            Canal = UPPER(LTRIM(RTRIM(s.Canal))),
            SKU   = UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
            Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
            Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
            Kg    = SUM(
                      CASE
                          WHEN (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido,0)) < 0 THEN 0
                          ELSE (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido,0))
                      END
                   )
        FROM dbo.Transferencias t
        JOIN dbo.TransferenciaDetalles td ON td.TransferenciaId = t.Id
        JOIN dbo.Series s                 ON s.Sucursal = t.Sucursal
        LEFT JOIN tr_surtido_agg tsa
               ON tsa.TransferenciaId = t.Id
              AND tsa.SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
        WHERE t.FechaSolicitud IS NOT NULL
          AND t.Estatus BETWEEN 1 AND 4
          AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
        GROUP BY
            UPPER(LTRIM(RTRIM(s.Canal))),
            UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
            MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
            YEAR(TRY_CONVERT(date, t.FechaSolicitud))
    ) X
    GROUP BY Canal, SKU, Mes, Anio
),

todo_cedis AS (
    SELECT
        'CEDIS' AS Origen,
        pc.Mes,
        pc.Anio,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        pc.Canal,
        CAST(NULL AS INT) AS VendedorId,
        pc.SKU,
        pc.Presupuesto,
        ISNULL(cc.Kg,0) AS Kg
    FROM presupuestos_cedis pc
    LEFT JOIN consumo_cedis_base cc
        ON cc.Canal = pc.Canal
       AND cc.SKU   = pc.SKU
       AND cc.Mes   = pc.Mes
       AND cc.Anio  = pc.Anio
),

-- =====================================================
-- PRESUPUESTO VENDEDOR + PRORRATEO DESDE CEDIS (igual al reporte)
-- =====================================================
presupuestos_vendedor AS (
    SELECT
        VendedorId,
        SKU = UPPER(LTRIM(RTRIM(pv.ProductoCodigo))),
        Mes = pv.Mes,
        Anio = pv.Anio,
        Presupuesto = SUM(pv.PresupuestoAsignado)
    FROM dbo.PresupuestoVendedor pv
    GROUP BY
        pv.VendedorId,
        UPPER(LTRIM(RTRIM(pv.ProductoCodigo))),
        pv.Mes,
        pv.Anio
),
pres_vendedor_x_canal AS (
    SELECT
        cv.Canal,
        pv.SKU,
        pv.Mes,
        pv.Anio,
        PresTotalCanal = SUM(CAST(pv.Presupuesto AS DECIMAL(18,4)))
    FROM presupuestos_vendedor pv
    JOIN canal_vendedores cv
        ON cv.VendedorId = pv.VendedorId
    GROUP BY
        cv.Canal, pv.SKU, pv.Mes, pv.Anio
),
consumo_vendedor_desde_cedis AS (
    SELECT
        VendedorId = pv.VendedorId,
        SKU        = pv.SKU,
        Mes        = pv.Mes,
        Anio       = pv.Anio,
        Kg = SUM(
            CASE
                WHEN ISNULL(pxc.PresTotalCanal,0) <= 0 THEN 0
                ELSE (cb.Kg * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal))
            END
        )
    FROM presupuestos_vendedor pv
    JOIN canal_vendedores cv
        ON cv.VendedorId = pv.VendedorId
    JOIN pres_vendedor_x_canal pxc
        ON pxc.Canal = cv.Canal
       AND pxc.SKU   = pv.SKU
       AND pxc.Mes   = pv.Mes
       AND pxc.Anio  = pv.Anio
    JOIN consumo_cedis_base cb
        ON cb.Canal = cv.Canal
       AND cb.SKU   = pv.SKU
       AND cb.Mes   = pv.Mes
       AND cb.Anio  = pv.Anio
    GROUP BY
        pv.VendedorId, pv.SKU, pv.Mes, pv.Anio
),
todo_vendedor AS (
    SELECT
        'VENDEDOR' AS Origen,
        pv.Mes,
        pv.Anio,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        pv.VendedorId,
        pv.SKU,
        pv.Presupuesto,
        ISNULL(cv.Kg,0) AS Kg
    FROM presupuestos_vendedor pv
    LEFT JOIN consumo_vendedor_desde_cedis cv
        ON cv.VendedorId = pv.VendedorId
       AND cv.SKU = pv.SKU
       AND cv.Mes = pv.Mes
       AND cv.Anio = pv.Anio
),

-- =====================================================
-- SURTIDO REAL CEDIS (ALINEADO AL REPORTE):
--   OV VALIDADO + TransferenciaSurtido (NO PedidosTransferenciaDetalle)
-- =====================================================
surtido_ov_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(cli.U_CANAL))),
        SKU   = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Mes   = MONTH(se.FechaValidacion),
        Anio  = YEAR(se.FechaValidacion),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp           ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se   ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd      ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    JOIN dbo.ClienteSap cli         ON cli.Cliente = o.Cliente
    WHERE o.Estatus <> 0
      AND se.FechaValidacion IS NOT NULL
      AND UPPER(LTRIM(RTRIM(cli.U_CANAL))) LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(cli.U_CANAL))),
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        MONTH(se.FechaValidacion),
        YEAR(se.FechaValidacion)
),
surtido_transferencias_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(s.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(ts.Sku))),
        Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
        Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
        KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    JOIN dbo.Transferencias t ON t.Id = ts.TransferenciaId
    JOIN dbo.Series s         ON s.Sucursal = t.Sucursal
    WHERE t.FechaSolicitud IS NOT NULL
      AND t.Estatus >= 5
      AND ts.KgSurtido > 0
      AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(s.Canal))),
        UPPER(LTRIM(RTRIM(ts.Sku))),
        MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
        YEAR(TRY_CONVERT(date, t.FechaSolicitud))
),
surtido_cedis_base AS (
    SELECT Canal, SKU, Mes, Anio, SUM(KgSurtido) AS KgSurtido
    FROM (
        SELECT Canal, SKU, Mes, Anio, KgSurtido FROM surtido_ov_cedis
        UNION ALL
        SELECT Canal, SKU, Mes, Anio, KgSurtido FROM surtido_transferencias_cedis
    ) x
    GROUP BY Canal, SKU, Mes, Anio
),
surtido_vendedor_desde_cedis AS (
    SELECT
        VendedorId = pv.VendedorId,
        SKU        = pv.SKU,
        Mes        = pv.Mes,
        Anio       = pv.Anio,
        KgSurtido  = SUM(
            CASE
                WHEN ISNULL(pxc.PresTotalCanal,0) <= 0 THEN 0
                ELSE (sb.KgSurtido * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal))
            END
        )
    FROM presupuestos_vendedor pv
    JOIN canal_vendedores cv
        ON cv.VendedorId = pv.VendedorId
    JOIN pres_vendedor_x_canal pxc
        ON pxc.Canal = cv.Canal
       AND pxc.SKU   = pv.SKU
       AND pxc.Mes   = pv.Mes
       AND pxc.Anio  = pv.Anio
    JOIN surtido_cedis_base sb
        ON sb.Canal = cv.Canal
       AND sb.SKU   = pv.SKU
       AND sb.Mes   = pv.Mes
       AND sb.Anio  = pv.Anio
    GROUP BY
        pv.VendedorId, pv.SKU, pv.Mes, pv.Anio
),
surtido_real AS (
    SELECT
        'CEDIS' AS Origen,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        Canal,
        CAST(NULL AS INT) AS VendedorId,
        SKU, Mes, Anio,
        SUM(KgSurtido) AS KgSurtido
    FROM surtido_cedis_base
    GROUP BY Canal, SKU, Mes, Anio

    UNION ALL

    SELECT
        'VENDEDOR',
        CAST(NULL AS NVARCHAR(50)),
        CAST(NULL AS NVARCHAR(100)),
        VendedorId,
        SKU, Mes, Anio,
        SUM(KgSurtido)
    FROM surtido_vendedor_desde_cedis
    GROUP BY VendedorId, SKU, Mes, Anio
)

SELECT
    t.Origen,
    t.Mes AS MesConsulta,
    t.Anio AS AnioConsulta,
    ISNULL(t.Cliente,'-') AS ClienteCodigo,
    ISNULL(cl.NombreCliente,'-') AS NombreCliente,
    ISNULL(t.Canal,'-') AS Canal,
    ISNULL(COALESCE(vend.VendedorNombre, cl.VendedorNombre), '-') AS VendedorNombre,
    t.SKU AS ProductoCodigo,
    prd.ProductoNombre,
    CAST(t.Presupuesto AS DECIMAL(18,4)) AS PresupuestoAsignado,
    CAST(t.Kg AS DECIMAL(18,4)) AS KgPedidosMes,
    CAST(ISNULL(sr.KgSurtido,0) AS DECIMAL(18,4)) AS KgSurtidoReal,
    CAST(
        CASE
            WHEN (t.Presupuesto - ISNULL(t.Kg,0) - ISNULL(sr.KgSurtido,0)) < 0 THEN 0
            ELSE (t.Presupuesto - ISNULL(t.Kg,0) - ISNULL(sr.KgSurtido,0))
        END
    AS DECIMAL(18,4)) AS DisponibleVenta
FROM (
    -- CEDIS para el canal/sucursal y mes/año/sku
    SELECT * FROM todo_cedis
    WHERE Canal = @Canal AND Mes = @Mes AND Anio = @Anio AND SKU = @SkuN

    UNION ALL

    -- Vendedores ligados a este canal (prorrateo)
    SELECT * FROM todo_vendedor
    WHERE Mes = @Mes AND Anio = @Anio AND SKU = @SkuN
      AND VendedorId IN (SELECT VendedorId FROM canal_vendedores WHERE Canal = @Canal)
) t
LEFT JOIN productos  prd  ON prd.SKU = t.SKU
LEFT JOIN clientes   cl   ON cl.Cliente = t.Cliente
LEFT JOIN vendedores vend ON vend.VendedorId = t.VendedorId
LEFT JOIN surtido_real sr
    ON sr.Origen = t.Origen
   AND sr.SKU    = t.SKU
   AND sr.Mes    = t.Mes
   AND sr.Anio   = t.Anio
   AND (
        (t.Origen = 'CEDIS'    AND sr.Canal      = t.Canal)
     OR (t.Origen = 'VENDEDOR' AND sr.VendedorId = t.VendedorId)
   )
ORDER BY
    Origen,
    AnioConsulta,
    MesConsulta,
    ISNULL(t.Canal,''),
    t.SKU;
";

            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            var rows = await conn.QueryAsync<PresupuestoConsumoDto>(
                new CommandDefinition(
                    sql,
                    new { Sucursal = sucursal, Sku = sku, FechaSolicitud = fechaSolicitud },
                    cancellationToken: ct
                )
            );

            // Mantengo tu salida: solo interesa CEDIS
            var cedis = rows.FirstOrDefault(r => r.Origen == "CEDIS");

            if (cedis == null)
            {
                return Ok(new
                {
                    presupuesto = 0m,
                    disponible = 0m,
                    enPresupuesto = false
                });
            }

            return Ok(new
            {
                presupuesto = cedis.PresupuestoAsignado,
                disponible = cedis.DisponibleVenta,
                enPresupuesto = cedis.PresupuestoAsignado > 0m
            });
        }


        private static (string raw, string username, string usernameEmail) NormalizeLoginTr(string? identityName)
        {
            var raw = (identityName ?? string.Empty).Trim();
            var username = raw.Contains("\\") ? raw.Split("\\").Last() : raw;
            var usernameEmail = username.Contains("@") ? username : $"{username}@carnesg.net";

            return (raw, username, usernameEmail);
        }

        private async Task<List<string>> ObtenerSucursalesPermitidasTransferenciasAsync(CancellationToken ct = default)
        {
            var (raw, username, usernameEmail) = NormalizeLoginTr(User?.Identity?.Name);

            var sucursalesDb = await (
                from u in _context.UsuarioSQL.AsNoTracking()
                join us in _context.UsuarioSeries.AsNoTracking()
                    on u.Id equals us.UsuarioId
                join s in _context.Series.AsNoTracking()
                    on us.SerieId equals s.Id
                where u.Activo
                   && (
                        u.Usuario == raw ||
                        u.Usuario == username ||
                        u.Usuario == usernameEmail ||
                        u.Nombre == raw ||
                        u.Nombre == username
                      )
                   && s.Sucursal != null
                   && s.Sucursal != ""
                select s.Sucursal
            )
            .Distinct()
            .ToListAsync(ct);

            return sucursalesDb
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpper())
                .Distinct()
                .ToList();
        }


        [HttpGet("Transferencias/TransferenciasSucursal")]
        public async Task<IActionResult> TransferenciasSucursal(TransferenciasFiltroVM filtro, string Export)
        {
            // Base query
            IQueryable<Transferencia> q = _context.Transferencias
                .AsNoTracking()
                .Include(t => t.Detalles);

            // =====================================================
            // SEGURIDAD POR SERIES ASIGNADAS AL USUARIO
            // UsuarioSerie -> Series -> Sucursal
            // Si el usuario tiene series configuradas, solo ve esas sucursales.
            // Si no tiene series configuradas, ve todo.
            // =====================================================
            var sucursalesPermitidasUsuario = await ObtenerSucursalesPermitidasTransferenciasAsync(HttpContext.RequestAborted);

            if (sucursalesPermitidasUsuario.Any())
            {
                q = q.Where(t =>
                    t.Sucursal != null &&
                    sucursalesPermitidasUsuario.Contains(t.Sucursal.Trim().ToUpper())
                );
            }

            // ===== Filtros por fecha (usando FechaSolicitud) =====
            if (filtro.Desde.HasValue)
            {
                var desde = filtro.Desde.Value.Date;
                q = q.Where(t => t.FechaSolicitud >= desde);
            }

            if (filtro.Hasta.HasValue)
            {
                var hasta = filtro.Hasta.Value.Date.AddDays(1); // incluye todo el día
                q = q.Where(t => t.FechaSolicitud < hasta);
            }


            // ===== Filtro sucursal MULTI desde tabla Series =====
            // El filtro usa Series.Sucursal: MONTERREY, MERIDA, CANCUN, etc.
            // También soporta si alguna transferencia trae guardado el NombreSerie: Mty, Merida, CANCUN, etc.
            if (filtro.SucursalesSeleccionadas != null && filtro.SucursalesSeleccionadas.Any())
            {
                var sucs = filtro.SucursalesSeleccionadas
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpper())
                    .Distinct()
                    .ToList();

                var seriesRelacionadas = await _context.Series
                    .AsNoTracking()
                    .Where(s => s.Sucursal != null && sucs.Contains(s.Sucursal.Trim().ToUpper()))
                    .Select(s => s.NombreSerie.Trim().ToUpper())
                    .Distinct()
                    .ToListAsync();

                q = q.Where(t =>
                    sucs.Contains((t.Sucursal ?? "").Trim().ToUpper()) ||
                    seriesRelacionadas.Contains((t.Sucursal ?? "").Trim().ToUpper())
                );
            }
            else if (!string.IsNullOrWhiteSpace(filtro.Sucursal))
            {
                var suc = filtro.Sucursal.Trim().ToUpper();

                var seriesRelacionadas = await _context.Series
                    .AsNoTracking()
                    .Where(s => s.Sucursal != null && s.Sucursal.Trim().ToUpper() == suc)
                    .Select(s => s.NombreSerie.Trim().ToUpper())
                    .Distinct()
                    .ToListAsync();

                q = q.Where(t =>
                    (t.Sucursal ?? "").Trim().ToUpper() == suc ||
                    seriesRelacionadas.Contains((t.Sucursal ?? "").Trim().ToUpper())
                );
            }

            // ===== Filtro estatus MULTI =====
            if (filtro.EstatusSeleccionados != null && filtro.EstatusSeleccionados.Any())
            {
                q = q.Where(t => filtro.EstatusSeleccionados.Contains(t.Estatus));
            }
            else if (filtro.Estatus.HasValue)
            {
                var est = filtro.Estatus.Value;
                q = q.Where(t => t.Estatus == est);
            }

            // ===== Filtro buscar (Consecutivo / SKU / Observacion) =====
            if (!string.IsNullOrWhiteSpace(filtro.Buscar))
            {
                var term = filtro.Buscar.Trim().ToUpper();

                q = q.Where(t =>
                    t.Consecutivo.ToUpper().Contains(term) ||
                    (t.Observacion ?? "").ToUpper().Contains(term) ||
                    t.Detalles.Any(d =>
                        d.ProductoCodigo.ToUpper().Contains(term) ||
                        (d.ProductoNombre ?? "").ToUpper().Contains(term)
                    )
                );
            }

            // Proyección al VM
            var queryListado = q
      .OrderBy(t => t.Consecutivo)
      .Select(t => new TransferenciaListadoVM
      {
          Id = t.Id,
          Consecutivo = t.Consecutivo,
          FechaSolicitud = t.FechaSolicitud,
          FechaCreacion = t.FechaCreacion,
          SucursalCodigo = t.Sucursal,
          SucursalNombre = t.Sucursal,
          Observacion = t.Observacion ?? "",

          TotalCajas = t.Detalles.Sum(d => d.Cajas),
          TotalKg = t.Detalles.Sum(d => d.CantidadKg),

          Estatus = t.Estatus,
          UsuarioSolicita = t.UsuarioSolicita
      });

            var resultados = await queryListado.ToListAsync();



            // ============================================================
            //  EXPORTAR A EXCEL / CSV SI EXPORT != null
            // ============================================================
            if (!string.IsNullOrWhiteSpace(Export))
            {
                var datos = resultados
                    .Where(t => t.Estatus != 0)   // ✅ OMITIR CANCELADAS
                    .ToList();

                var ids = datos.Select(d => d.Id).ToList();

                var detalles = await (
                    from d in _context.TransferenciaDetalles.AsNoTracking()
                    join art in _context.ArticuloSap.AsNoTracking()
                        on d.ProductoCodigo equals art.ProductoCodigo
                    where ids.Contains(d.TransferenciaId)
                    select new
                    {
                        d.TransferenciaId,
                        d.ProductoCodigo,
                        d.ProductoNombre,
                        d.CantidadKg,
                        Cajas = d.Cajas == 0 ? 0m : d.Cajas,
                        d.Nota,
                        Master = art.U_MASTER
                    }
                ).ToListAsync();

                var detPorTr = detalles
                    .GroupBy(d => d.TransferenciaId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // ============================================================
                //  XLSX
                // ============================================================
                if (Export.Equals("xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    using var wb = new ClosedXML.Excel.XLWorkbook();
                    var ws = wb.Worksheets.Add("Transferencias");

                    int row = 1;

                    // Headers (como tu screenshot)
                    ws.Cell(row, 1).Value = "FechaDocumento";
                    ws.Cell(row, 2).Value = "Pedido";
                    ws.Cell(row, 3).Value = "Realizado Por";
                    ws.Cell(row, 4).Value = "Sucursal";
                    ws.Cell(row, 5).Value = "Sku";
                    ws.Cell(row, 6).Value = "Producto";
                    ws.Cell(row, 7).Value = "Master";
                    ws.Cell(row, 8).Value = "CajasSolicitadas";
                    ws.Cell(row, 9).Value = "KgSolicitados";
                    ws.Cell(row, 10).Value = "Precio";
                    ws.Cell(row, 11).Value = "FechaSolicitud";
                    ws.Cell(row, 12).Value = "Sucursal2";
                    ws.Cell(row, 13).Value = "Comentario";

                    ws.Range(row, 1, row, 13).Style.Font.Bold = true;
                    row++;

                    foreach (var t in datos)
                    {
                        // ✅ Tus reglas:
                        // Cliente = Sucursal  -> columna 4
                        // Ruta = Sucursal     -> columna 12
                        // FechaSolicitud      -> columna 11
                        var fechaDocumento = t.FechaCreacion;                  // Ajusta si tienes otro campo
                        var pedido = t.Consecutivo ?? "";
                        var realizadoPor = t.UsuarioSolicita ?? "";
                        var sucursal = t.SucursalNombre ?? t.SucursalNombre ?? ""; // usa el que exista en tu VM
                        var fechaSolicitud = t.FechaSolicitud;                 // <-- IMPORTANTE (la “de solicitud”)

                        // Si no tiene detalles, fila “cabecera”
                        if (!detPorTr.TryGetValue(t.Id, out var listaDet) || listaDet.Count == 0)
                        {
                            ws.Cell(row, 1).Value = fechaDocumento;
                            ws.Cell(row, 2).Value = pedido;
                            ws.Cell(row, 3).Value = realizadoPor;
                            ws.Cell(row, 4).Value = sucursal;
                            // 5-10 vacías
                            ws.Cell(row, 11).Value = fechaSolicitud;
                            ws.Cell(row, 12).Value = sucursal; // Sucursal2 = sucursal
                            ws.Cell(row, 13).Value = t.Observacion ?? "";
                            row++;
                            continue;
                        }

                        // Una fila por detalle
                        foreach (var d in listaDet)
                        {
                            ws.Cell(row, 1).Value = fechaDocumento;
                            ws.Cell(row, 2).Value = pedido;
                            ws.Cell(row, 3).Value = realizadoPor;
                            ws.Cell(row, 4).Value = sucursal;
                            ws.Cell(row, 5).Value = d.ProductoCodigo ?? "";
                            ws.Cell(row, 6).Value = SoloNombreProducto(d.ProductoNombre, d.ProductoCodigo);
                            ws.Cell(row, 7).Value = d.Master;
                            ws.Cell(row, 8).Value = d.Cajas;       // entero
                            ws.Cell(row, 9).Value = d.CantidadKg;  // decimal

                            // Precio (si no aplica aún, vacío)
                            ws.Cell(row, 10).Value = "";

                            // FechaSolicitud (en lugar de FechaEmbarcar)
                            ws.Cell(row, 11).Value = fechaSolicitud;

                            // Ruta = Sucursal (Sucursal2)
                            ws.Cell(row, 12).Value = sucursal;

                            // Comentario
                            ws.Cell(row, 13).Value = d.Nota ?? "";

                            ws.Cell(row, 8).Style.NumberFormat.Format = "0";
                            ws.Cell(row, 9).Style.NumberFormat.Format = "0.00";

                            row++;
                        }
                    }

                    ws.Columns().AdjustToContents();

                    using var stream = new MemoryStream();
                    wb.SaveAs(stream);
                    var bytes = stream.ToArray();
                    var fileName = $"Transferencias_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

                    return File(
                        fileContents: bytes,
                        contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        fileDownloadName: fileName
                    );
                }

                // ============================================================
                //  CSV
                // ============================================================
                if (Export.Equals("csv", StringComparison.OrdinalIgnoreCase))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("FechaDocumento;Pedido;Realizado Por;Sucursal;Sku;Producto;Master;CajasSolicitadas;KgSolicitados;Precio;FechaSolicitud;Sucursal2;Comentario");

                    foreach (var t in datos)
                    {
                        var fechaDocumento = t.FechaCreacion?.ToString("dd/MM/yyyy HH:mm") ?? "";
                        var pedido = t.Consecutivo ?? "";
                        var realizadoPor = (t.UsuarioSolicita ?? "").Replace(";", ",");
                        var sucursal = ((t.SucursalNombre ?? t.SucursalCodigo) ?? "").Replace(";", ",");
                        var fechaSolicitud = t.FechaSolicitud?.ToString("dd/MM/yyyy HH:mm") ?? "";

                        if (!detPorTr.TryGetValue(t.Id, out var listaDet) || listaDet.Count == 0)
                        {
                            sb.AppendLine(string.Join(";", new[]
                            {
                    fechaDocumento,
                    pedido,
                    realizadoPor,
                    sucursal,
                    "", "", "", "", "", "",                 // 5-10 vacías
                    fechaSolicitud,
                    sucursal,                               // Sucursal2
                    (t.Observacion ?? "").Replace(";", ",")
                }));
                            continue;
                        }

                        foreach (var d in listaDet)
                        {
                            var sku = (d.ProductoCodigo ?? "").Replace(";", ",");
                            var producto = SoloNombreProducto(d.ProductoNombre, d.ProductoCodigo).Replace(";", ",");
                            var master = (Convert.ToString(d.Master) ?? "").Replace(";", ",");
                            var cajas = d.Cajas.ToString("0");
                            var kg = d.CantidadKg.ToString("0.00");
                            var precio = ""; // si aplica después
                            var comentario = (d.Nota ?? "").Replace(";", ",");

                            sb.AppendLine(string.Join(";", new[]
                            {
                    fechaDocumento,
                    pedido,
                    realizadoPor,
                    sucursal,
                    sku,
                    producto,
                    master,
                    cajas,
                    kg,
                    precio,
                    fechaSolicitud,
                    sucursal,      // Sucursal2
                    comentario
                }));
                        }
                    }

                    var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                    var fileName = $"Transferencias_{DateTime.Now:yyyyMMdd_HHmm}.csv";
                    return File(bytes, "text/csv", fileName);
                }
            }


            // ============================================================
            //  SI NO ES EXPORT, RENDERIZAMOS LA VISTA NORMAL
            // ============================================================
            var querySucursales = _context.Series
     .AsNoTracking()
     .Where(s => s.Sucursal != null && s.Sucursal.Trim() != "");

            if (sucursalesPermitidasUsuario.Any())
            {
                querySucursales = querySucursales
                    .Where(s => sucursalesPermitidasUsuario.Contains(s.Sucursal.Trim().ToUpper()));
            }

            var sucursales = await querySucursales
                .Select(s => s.Sucursal.Trim())
                .Distinct()
                .OrderBy(s => s)
                .Select(s => new SucursalVM
                {
                    Codigo = s,
                    Nombre = s
                })
                .ToListAsync();

            filtro.Sucursales = sucursales;
            filtro.Resultados = resultados;

            return View(filtro);


        }


        // GET: /Transferencias/DetalleApi?id=22
        [HttpGet]
        public async Task<IActionResult> DetalleApi(int id)
        {
            var t = await _context.Transferencias
                .AsNoTracking()
                .Include(x => x.Detalles)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (t == null)
                return NotFound();

            var header = new
            {
                id = t.Id,
                consecutivo = t.Consecutivo ?? "",
                sucursal = t.Sucursal ?? "",
                sucursalNombre = t.Sucursal ?? "",
                fechaSolicitud = t.FechaSolicitud,
                usuarioSolicita = t.UsuarioSolicita ?? "",
                estatus = t.Estatus,
                observacion = t.Observacion ?? ""
            };

            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            // ===== 1) Confirmado real desde PedidosTransferenciaDetalle =====
            var confirmados = (await conn.QueryAsync<(int DetalleIdOriginal, decimal KgConf, int CajasConf)>(@"
        SELECT b.TransferenciaDetalleIdOriginal,
               ISNULL(SUM(b.CantidadKg), 0) AS KgConf,
               ISNULL(SUM(b.Cajas),      0) AS CajasConf
        FROM dbo.PedidosTransferencia a
        INNER JOIN dbo.PedidosTransferenciaDetalle b ON a.Id = b.PedidoTransferenciaId
        WHERE a.TransferenciaId = @id
        GROUP BY b.TransferenciaDetalleIdOriginal;",
                new { id })).ToList();

            var confPorDetalleId = confirmados.ToDictionary(x => x.DetalleIdOriginal, x => x);

            // ===== 2) Líneas pedido + confirmado =====
            var lineas = t.Detalles
                .OrderBy(d => d.Id)
                .Select(d =>
                {
                    confPorDetalleId.TryGetValue(d.Id, out var conf);
                    return new
                    {
                        productoCodigo = (d.ProductoCodigo ?? "").Trim().ToUpper(),
                        productoNombre = d.ProductoNombre ?? "",
                        cantidadKg = d.CantidadKg,
                        cajas = d.Cajas,
                        kgConfirmadas = conf.KgConf,     // viene de PedidosTransferenciaDetalle
                        cajasConfirmadas = conf.CajasConf,  // viene de PedidosTransferenciaDetalle
                        presupuesto = 0m,
                        disponible = 0m,
                        nota = d.Nota ?? ""
                    };
                })
                .ToList();

            // ===== 3) Surtido real =====
            var surt = (await conn.QueryAsync<(string Sku, decimal KgSurtido, int CajasSurtidas)>(@"
        SELECT UPPER(LTRIM(RTRIM(Sku))) AS Sku,
               ISNULL(KgSurtido,   0)  AS KgSurtido,
               ISNULL(CajasSurtidas,0) AS CajasSurtidas
        FROM dbo.TransferenciaSurtido
        WHERE TransferenciaId = @id;",
                new { id })).ToList();

            // ===== 4) Escaneo =====
            var scans = (await conn.QueryAsync<(string Sku, int Etiquetas, decimal KgEscaneado)>(@"
        SELECT UPPER(LTRIM(RTRIM(Sku))) AS Sku,
               COUNT(1)                        AS Etiquetas,
               ISNULL(SUM(ISNULL(Kg, 0)), 0)  AS KgEscaneado
        FROM dbo.TransferenciaScanEtiqueta
        WHERE TransferenciaId = @id
        GROUP BY UPPER(LTRIM(RTRIM(Sku)));",
                new { id })).ToList();

            // ===== 5) Pedido / Confirmado agrupado por SKU =====
            var pedidoPorSku = lineas
                .GroupBy(x => x.productoCodigo)
                .ToDictionary(g => g.Key, g => new
                {
                    KgPedido = g.Sum(z => z.cantidadKg),
                    CajasPedido = g.Sum(z => z.cajas),
                    KgConfirmadas = g.Sum(z => z.kgConfirmadas),
                    CajasConfirmadas = g.Sum(z => z.cajasConfirmadas)
                });

            var surtPorSku = surt.ToDictionary(x => x.Sku, x => x);
            var scanPorSku = scans.ToDictionary(x => x.Sku, x => x);

            var allSkus = new HashSet<string>(pedidoPorSku.Keys);
            foreach (var x in surtPorSku.Keys) allSkus.Add(x);
            foreach (var x in scanPorSku.Keys) allSkus.Add(x);

            // ===== 6) Comparativo por SKU =====
            var surtido = allSkus
                .OrderBy(x => x)
                .Select(sku =>
                {
                    pedidoPorSku.TryGetValue(sku, out var ped);
                    surtPorSku.TryGetValue(sku, out var sur);
                    scanPorSku.TryGetValue(sku, out var sc);

                    return new
                    {
                        sku,
                        kgPedido = ped?.KgPedido ?? 0m,
                        cajasPedido = ped?.CajasPedido ?? 0m,
                        kgConfirmadas = ped?.KgConfirmadas ?? 0m,
                        cajasConfirmadas = ped?.CajasConfirmadas ?? 0m,
                        kgSurtido = sur.KgSurtido,
                        cajasSurtidas = sur.CajasSurtidas,
                        etiquetas = sc.Etiquetas,
                        kgEscaneado = sc.KgEscaneado
                    };
                })
                .ToList();

            return Ok(new
            {
                header,
                lineas,
                surtido
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarFechaSolicitud([FromBody] ActualizarFechaSolicitudReq req)
        {
            if (req == null || req.transferenciaId <= 0)
                return BadRequest(new { ok = false, msg = "Solicitud inválida." });

            if (string.IsNullOrWhiteSpace(req.fechaSolicitud))
                return BadRequest(new { ok = false, msg = "Fecha inválida." });

            // Parse estricto yyyy-MM-dd (input type="date")
            if (!DateTime.TryParseExact(req.fechaSolicitud, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out var fecha))
                return BadRequest(new { ok = false, msg = "Formato de fecha inválido. Usa yyyy-MM-dd." });

            // Traer transferencia
            var tr = await _context.Transferencias.FirstOrDefaultAsync(x => x.Id == req.transferenciaId);
            if (tr == null)
                return NotFound(new { ok = false, msg = "Transferencia no encontrada." });

            // ✅ Regla negocio: solo Pendiente (1). Ajusta si quieres permitir otros.
            if (tr.Estatus != 1 && tr.Estatus != 2)
                return Ok(new { ok = false, msg = "No permitido en este estatus." });

            // Guardar (si tu campo es DateTime? ajusta según tu entidad)
            tr.FechaSolicitud = fecha;

            await _context.SaveChangesAsync();

            return Ok(new { ok = true, msg = "Fecha actualizada." });
        }






       

        // GET https://localhost:7171/Transferencias/OTransferencia?id=1000
        [HttpGet("Transferencias/OTransferencia")]
        public async Task<IActionResult> OTransferencia(int id)
        {
            // id aquí sigue siendo TransferenciaId (como en tu URL)
            var pedido = await _context.PedidosTransferencia
                .AsNoTracking()
                .Include(p => p.Detalles)
                .FirstOrDefaultAsync(p => p.TransferenciaId == id && p.Estatus != 4);

            if (pedido == null)
                return NotFound("PedidoTransferencia no encontrado.");

            // Conexión BD principal (Dapper)
            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            // ==========================================================
            // 1) Acumulados por SKU (TransferenciaSurtido)
            // ==========================================================
            var surtidosRaw = (await conn.QueryAsync<(string Sku, decimal KgSurtido, int CajasSurtidas)>(@"
        SELECT
            Sku = UPPER(LTRIM(RTRIM(Sku))),
            KgSurtido = CAST(ISNULL(KgSurtido,0) AS DECIMAL(18,4)),
            CajasSurtidas = ISNULL(CajasSurtidas,0)
        FROM dbo.TransferenciaSurtido
        WHERE TransferenciaId = @id;",
                new { id }
            )).ToList();

            var dicSurtido = surtidosRaw
                .GroupBy(x => (x.Sku ?? "").Trim().ToUpper())
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        KgSurtido = g.Sum(x => x.KgSurtido),
                        CajasSurtidas = g.Sum(x => x.CajasSurtidas)
                    }
                );

            // ==========================================================
            // 2) Etiquetas recientes (vista previa)
            // ==========================================================
            var etiquetas = (await conn.QueryAsync<TransferenciaEtiquetaVM>(@"
        SELECT TOP 15
            Id             = Id,
            CodigoEtiqueta = CodigoEtiqueta,
            Sku            = UPPER(LTRIM(RTRIM(Sku))),
            Kg             = CAST(ISNULL(Kg,0) AS DECIMAL(18,4)),
            Fecha          = Fecha,
            Usuario        = ISNULL(Usuario,'')
        FROM dbo.TransferenciaScanEtiqueta
        WHERE TransferenciaId = @id
        ORDER BY Fecha DESC;",
                new { id }
            )).ToList();

            // ==========================================================
            // 3) Traer nombres de producto desde ArticuloSap
            //    (SIN ToUpperInvariant en LINQ para evitar error EF)
            // ==========================================================
            var skusPedido = (pedido.Detalles ?? new List<PedidoTransferenciaDetalle>())
                .Select(d => (d.ProductoCodigo ?? "").Trim().ToUpper())  // ✅ OK
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            // Si no hay SKUs, dic vacío
            Dictionary<string, string> nombresPorSku = new();

            if (skusPedido.Count > 0)
            {
                // ✅ Query sin funciones sobre la columna (mejor para índices y evita traducciones raras)
                var articulos = await _context.ArticuloSap
                    .AsNoTracking()
                    .Where(a => skusPedido.Contains(a.ProductoCodigo))
                    .Select(a => new
                    {
                        Sku = a.ProductoCodigo,
                        Nombre = a.ProductoNombre
                    })
                    .ToListAsync();

                // Normalizamos clave en memoria
                nombresPorSku = articulos
                    .GroupBy(x => (x.Sku ?? "").Trim().ToUpper())
                    .ToDictionary(
                        g => g.Key,
                        g => (g.FirstOrDefault()?.Nombre ?? "").Trim()
                    );
            }

            // ==========================================================
            // 4) Armar VM desde PedidoTransferencia*
            // ==========================================================
            var vm = new TransferenciaCargaVM
            {
                TransferenciaId = pedido.TransferenciaId,
                Consecutivo = pedido.Consecutivo ?? "",
                AlmacenOrigen = "CEDIS",
                AlmacenDestino = pedido.Destino ?? "",
                UsuarioSolicita = pedido.UsuarioSolicita ?? "",
                FechaSolicitud = pedido.FechaSolicitud,
                Observacion = pedido.Observacion ?? "",
                Etiquetas = etiquetas,

                Items = (pedido.Detalles ?? new List<PedidoTransferenciaDetalle>())
                    .OrderBy(d => d.Orden)
                    .Select(d =>
                    {
                        var sku = (d.ProductoCodigo ?? "").Trim().ToUpper();

                        decimal kgSurtido = 0m;
                        int cajasSurt = 0;

                        if (dicSurtido.TryGetValue(sku, out var sur))
                        {
                            kgSurtido = sur.KgSurtido;
                            cajasSurt = sur.CajasSurtidas;
                        }

                        var nombre = nombresPorSku.TryGetValue(sku, out var n) ? n : "";

                        return new TransferenciaCargaItemVM
                        {
                            Sku = sku,
                            Producto = nombre,          // ✅ nombre desde ArticuloSap
                            Pedido = d.CantidadKg,      // PedidoTransferenciaDetalle
                            Surtido = kgSurtido,
                            CajasPedido = d.Cajas,
                            CajasSurtido = cajasSurt
                        };
                    })
                    .ToList()
            };

            return View("~/Views/Transferencias/OTransferencia.cshtml", vm);
        }





        // GET https://localhost:7171/Transferencias/SeleccionarParaSurtir
        [HttpGet("Transferencias/SeleccionarParaSurtir")]
        public async Task<IActionResult> SeleccionarParaSurtir(string buscar = "")
        {
            var q = _context.Transferencias
     .AsNoTracking()
     .Where(t => t.Estatus == 4); // SOLO registradas



            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var term = buscar.Trim().ToUpper();
                q = q.Where(t =>
                    (t.Consecutivo ?? "").ToUpper().Contains(term) ||
                    (t.Sucursal ?? "").ToUpper().Contains(term) ||
                    (t.Observacion ?? "").ToUpper().Contains(term)
                );
            }

            var resultados = await q
                .OrderByDescending(t => t.Id)
                .Take(200)
                .Select(t => new TransferenciaListadoVM
                {
                    Id = t.Id,
                    Consecutivo = t.Consecutivo,
                    FechaSolicitud = t.FechaSolicitud,
                    FechaCreacion = t.FechaCreacion,
                    SucursalCodigo = t.Sucursal,
                    SucursalNombre = t.Sucursal,
                    Observacion = t.Observacion ?? "",
                    Estatus = t.Estatus,
                    UsuarioSolicita = t.UsuarioSolicita
                })
                .ToListAsync();

            var vm = new SeleccionarTransferenciaVM
            {
                Buscar = buscar ?? "",
                Resultados = resultados
            };

            return View("~/Views/Transferencias/SeleccionarParaSurtir.cshtml", vm);
        }





        private async Task<(string Sku, decimal Kg)?> BuscarEtiquetaAsync(string cs, string etiqueta)
        {
            if (string.IsNullOrWhiteSpace(cs))
                return null;

            try
            {
                await using var cn = new SqlConnection(cs);
                await cn.OpenAsync();

                const string sql = @"
SELECT TOP 1
    Sku = UPPER(LTRIM(RTRIM(Articulo))),
    Kg  = CAST(PesoNeto AS DECIMAL(18,4))
FROM dbo.Produccion
WHERE UPPER(LTRIM(RTRIM(CodigoEtiqueta))) = @etiqueta
  AND Estatus = 1;
";

                var info = await cn.QueryFirstOrDefaultAsync<(string Sku, decimal Kg)>(sql, new
                {
                    etiqueta = (etiqueta ?? "").Trim().ToUpper()
                });

                if (string.IsNullOrWhiteSpace(info.Sku))
                    return null;

                return info;
            }
            catch
            {
                return null;
            }
        }



        public record ScanEtiquetaReq(int transferenciaId, string codigoEtiqueta, bool forzarAgregar = false);

        [HttpPost("Transferencias/ScanEtiqueta")]
        public async Task<IActionResult> ScanEtiqueta([FromBody] ScanEtiquetaReq req)
        {
            try
            {
                var codigo = (req.codigoEtiqueta ?? "").Trim().ToUpper();

                if (req.transferenciaId <= 0 || string.IsNullOrWhiteSpace(codigo))
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = "TransferenciaId y códigoEtiqueta son requeridos."
                    });
                }

                // ==========================================================
                // 1) INTENTAR COMO ETIQUETA NORMAL
                // ==========================================================
                var r1 = await ProcesarEtiquetaNormalAsync(
                    req.transferenciaId,
                    codigo,
                    null,
                    req.forzarAgregar
                );

                var r1Msg = r1.msg ?? "";

                var r1EsOtraTransferencia = r1Msg.StartsWith("YA_ESCANEADA_OTRA_TRANSFERENCIA|");
                var r1FolioOtra = r1EsOtraTransferencia
                    ? r1Msg.Replace("YA_ESCANEADA_OTRA_TRANSFERENCIA|", "")
                    : "";

                // Si fue OK, duplicada, SKU no solicitado o ya escaneada en otra transferencia,
                // regresamos tal cual. Si no fue encontrada, entonces intentamos como tarima.
                if (r1.ok || r1.duplicada || r1EsOtraTransferencia || (!r1.ok && !string.IsNullOrWhiteSpace(r1.sku)))
                {
                    return Ok(new
                    {
                        ok = r1.ok,
                        sku = r1.sku,
                        kg = r1.kg,
                        codigoEtiqueta = codigo,
                        origen = r1.origen,
                        duplicada = r1.duplicada,

                        skuNoSolicitado =
                            !r1.ok
                            && !string.IsNullOrWhiteSpace(r1.sku)
                            && !r1.duplicada
                            && !r1EsOtraTransferencia,

                        yaEscaneadaEnOtraTransferencia = r1EsOtraTransferencia,
                        transferencia = r1FolioOtra,

                        msg = r1EsOtraTransferencia
                            ? $"Esta etiqueta ya fue escaneada en la transferencia {r1FolioOtra}."
                            : r1.msg
                    });
                }

                // ==========================================================
                // 2) INTENTAR COMO TARIMA
                // ==========================================================
                var csP1 = _cfg.GetConnectionString("CadenaMeatP1");
                var csTif = _cfg.GetConnectionString("CadenaMeatTIF");

                var erroresTarima = new List<string>();

                var rP1 = await BuscarTarimaEtiquetasAsync(csP1, codigo, "CadenaMeatP1");
                var etqs = rP1.Etiquetas;
                var origenTarima = "P1";

                if (!string.IsNullOrWhiteSpace(rP1.Error))
                    erroresTarima.Add(rP1.Error);

                if (etqs.Count == 0)
                {
                    var rTif = await BuscarTarimaEtiquetasAsync(csTif, codigo, "CadenaMeatTIF");
                    etqs = rTif.Etiquetas;
                    origenTarima = "TIF";

                    if (!string.IsNullOrWhiteSpace(rTif.Error))
                        erroresTarima.Add(rTif.Error);
                }

                if (etqs.Count == 0)
                {
                    if (erroresTarima.Any())
                    {
                        return StatusCode(500, new
                        {
                            ok = false,
                            msg = "No se pudo consultar la tarima en Meat.",
                            tarima = codigo,
                            errores = erroresTarima
                        });
                    }

                    return Ok(new
                    {
                        ok = false,
                        msg = $"No se encontró etiqueta ni tarima: {codigo}"
                    });
                }

                var etiquetasTarima = etqs
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToList();

                if (etiquetasTarima.Count == 0)
                {
                    return Ok(new
                    {
                        ok = false,
                        msg = $"La tarima {codigo} no tiene etiquetas válidas."
                    });
                }

                // ==========================================================
                // 3) VALIDAR TODA LA TARIMA ANTES DE INSERTAR
                // ==========================================================
                var tr = await _context.Transferencias
                    .AsNoTracking()
                    .Include(t => t.Detalles)
                    .FirstOrDefaultAsync(t => t.Id == req.transferenciaId);

                if (tr == null)
                {
                    return Ok(new
                    {
                        ok = false,
                        msg = "Transferencia no encontrada."
                    });
                }

                var skusSolicitados = tr.Detalles
                    .Select(d => (d.ProductoCodigo ?? "").Trim().ToUpperInvariant())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToHashSet();

                if (skusSolicitados.Count == 0)
                {
                    return Ok(new
                    {
                        ok = false,
                        esTarima = true,
                        tarima = codigo,
                        origen = origenTarima,
                        msg = "La transferencia no tiene SKU solicitados."
                    });
                }

                var csTarima = origenTarima == "P1" ? csP1 : csTif;

                if (string.IsNullOrWhiteSpace(csTarima))
                {
                    return StatusCode(500, new
                    {
                        ok = false,
                        msg = $"La tarima se detectó en {origenTarima}, pero no existe cadena de conexión configurada.",
                        tarima = codigo,
                        origen = origenTarima
                    });
                }

                var skusNoSolicitados = new List<string>();
                var etiquetasNoEncontradas = new List<string>();
                var skusTarima = new List<string>();

                foreach (var etiquetaTarima in etiquetasTarima)
                {
                    var infoEtiqueta = await BuscarEtiquetaAsync(csTarima, etiquetaTarima);

                    if (infoEtiqueta == null || string.IsNullOrWhiteSpace(infoEtiqueta.Value.Sku))
                    {
                        etiquetasNoEncontradas.Add(etiquetaTarima);
                        continue;
                    }

                    var skuEtiqueta = (infoEtiqueta.Value.Sku ?? "").Trim().ToUpperInvariant();
                    skusTarima.Add(skuEtiqueta);

                    if (!skusSolicitados.Contains(skuEtiqueta))
                        skusNoSolicitados.Add(skuEtiqueta);
                }

                etiquetasNoEncontradas = etiquetasNoEncontradas
                    .Distinct()
                    .ToList();

                skusNoSolicitados = skusNoSolicitados
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList();

                if (etiquetasNoEncontradas.Any())
                {
                    return Ok(new
                    {
                        ok = false,
                        esTarima = true,
                        tarima = codigo,
                        origen = origenTarima,
                        totalEtiquetas = etiquetasTarima.Count,
                        etiquetasNoEncontradas,
                        msg =
                            $"La tarima {codigo} contiene etiquetas que no se pudieron validar:\n" +
                            $"{string.Join(", ", etiquetasNoEncontradas)}\n\n" +
                            "No se permite ingresarla."
                    });
                }

                if (skusNoSolicitados.Any())
                {
                    return Ok(new
                    {
                        ok = false,
                        esTarima = true,
                        tarima = codigo,
                        origen = origenTarima,
                        totalEtiquetas = etiquetasTarima.Count,
                        skuNoSolicitado = true,
                        skusSolicitados = skusSolicitados.OrderBy(x => x).ToList(),
                        skusTarima = skusTarima.Distinct().OrderBy(x => x).ToList(),
                        skusNoSolicitados,
                        msg =
                            $"La tarima {codigo} contiene SKU(s) que NO están solicitados en esta transferencia:\n" +
                            $"{string.Join(", ", skusNoSolicitados)}\n\n" +
                            $"SKU(s) solicitados:\n{string.Join(", ", skusSolicitados.OrderBy(x => x))}\n\n" +
                            "No se permite ingresarla."
                    });
                }

                // ==========================================================
                // 4) VALIDAR SI ALGUNA ETIQUETA DE LA TARIMA ESTÁ EN OTRA TRANSFERENCIA
                // ==========================================================
                if (!req.forzarAgregar)
                {
                    var conn = _context.Database.GetDbConnection();

                    if (conn.State != ConnectionState.Open)
                        await conn.OpenAsync();

                    var escaneadaEnOtra = await conn.QueryFirstOrDefaultAsync<EtiquetaEnOtraTransferenciaDto>(@"
SELECT TOP 1
    s.TransferenciaId,
    ISNULL(t.Consecutivo, CAST(s.TransferenciaId AS varchar(20))) AS Folio
FROM dbo.TransferenciaScanEtiqueta s
LEFT JOIN dbo.Transferencias t 
    ON t.Id = s.TransferenciaId
WHERE UPPER(LTRIM(RTRIM(s.CodigoEtiqueta))) IN @Etiquetas
  AND s.TransferenciaId <> @TransferenciaId
ORDER BY s.TransferenciaId DESC;
", new
                    {
                        Etiquetas = etiquetasTarima,
                        TransferenciaId = req.transferenciaId
                    });

                    if (escaneadaEnOtra != null)
                    {
                        return Ok(new
                        {
                            ok = false,
                            esTarima = true,
                            tarima = codigo,
                            origen = origenTarima,
                            totalEtiquetas = etiquetasTarima.Count,
                            yaEscaneadaEnOtraTransferencia = true,
                            transferencia = escaneadaEnOtra.Folio,
                            msg = $"Una o más etiquetas de la tarima ya fueron escaneadas en la transferencia {escaneadaEnOtra.Folio}."
                        });
                    }
                }

                // ==========================================================
                // 5) YA VALIDADA TODA LA TARIMA, PROCESAR ETIQUETAS
                // ==========================================================
                int okCount = 0;
                int dupCount = 0;
                int failCount = 0;
                int otraTransferenciaCount = 0;

                decimal kgTotal = 0m;

                string folioOtraTransferencia = "";

                foreach (var e in etiquetasTarima)
                {
                    var rr = await ProcesarEtiquetaNormalAsync(
                        req.transferenciaId,
                        e,
                        codigo,
                        req.forzarAgregar
                    );

                    var rrMsg = rr.msg ?? "";

                    var rrEsOtraTransferencia = rrMsg.StartsWith("YA_ESCANEADA_OTRA_TRANSFERENCIA|");

                    if (rrEsOtraTransferencia)
                    {
                        otraTransferenciaCount++;

                        if (string.IsNullOrWhiteSpace(folioOtraTransferencia))
                            folioOtraTransferencia = rrMsg.Replace("YA_ESCANEADA_OTRA_TRANSFERENCIA|", "");

                        continue;
                    }

                    if (rr.ok)
                    {
                        okCount++;
                        kgTotal += rr.kg;
                    }
                    else if (rr.duplicada)
                    {
                        dupCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }

                if (otraTransferenciaCount > 0 && !req.forzarAgregar)
                {
                    return Ok(new
                    {
                        ok = false,
                        esTarima = true,
                        tarima = codigo,
                        origen = origenTarima,
                        totalEtiquetas = etiquetasTarima.Count,
                        okCount,
                        dupCount,
                        failCount,
                        otraTransferenciaCount,
                        yaEscaneadaEnOtraTransferencia = true,
                        transferencia = folioOtraTransferencia,
                        msg = $"Una o más etiquetas de la tarima ya fueron escaneadas en la transferencia {folioOtraTransferencia}."
                    });
                }

                return Ok(new
                {
                    ok = true,
                    esTarima = true,
                    tarima = codigo,
                    origen = origenTarima,
                    totalEtiquetas = etiquetasTarima.Count,
                    okCount,
                    dupCount,
                    failCount,
                    kgTotal,
                    msg = $"TARIMA [{origenTarima}] {codigo}: OK={okCount}, Dup={dupCount}, Fail={failCount}, Kg={kgTotal:N2}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error interno al escanear etiqueta / tarima.",
                    error = ex.Message,
                    detail = ex.InnerException?.Message
                });
            }
        }




        public record DeleteEtiquetaReq(int transferenciaId, string codigoEtiqueta);

        [HttpPost("Transferencias/DeleteEtiqueta")]
        public async Task<IActionResult> DeleteEtiqueta([FromBody] DeleteEtiquetaReq req)
        {
            var etiqueta = (req.codigoEtiqueta ?? "").Trim().ToUpper();
            if (req.transferenciaId <= 0 || string.IsNullOrWhiteSpace(etiqueta))
                return BadRequest(new { ok = false, msg = "transferenciaId y codigoEtiqueta requeridos." });

            // Traer la etiqueta (para saber sku y kg)
            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            var row = await conn.QueryFirstOrDefaultAsync<(string Sku, decimal Kg)>(@"
        SELECT TOP 1
            Sku = UPPER(LTRIM(RTRIM(Sku))),
            Kg
        FROM dbo.TransferenciaScanEtiqueta
        WHERE TransferenciaId = @transferenciaId
          AND UPPER(LTRIM(RTRIM(CodigoEtiqueta))) = @etiqueta
    ", new { req.transferenciaId, etiqueta });

            if (string.IsNullOrWhiteSpace(row.Sku))
                return Ok(new { ok = false, msg = "Etiqueta no encontrada (quizá ya se eliminó)." });

            // 1) Borrar scan
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
        DELETE FROM dbo.TransferenciaScanEtiqueta
        WHERE TransferenciaId = {req.transferenciaId}
          AND UPPER(LTRIM(RTRIM(CodigoEtiqueta))) = {etiqueta};
    ");

            // 2) Restar acumulado (kg y 1 caja)
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE dbo.TransferenciaSurtido
           SET KgSurtido = CASE WHEN KgSurtido - {row.Kg} < 0 THEN 0 ELSE KgSurtido - {row.Kg} END,
               CajasSurtidas = CASE WHEN CajasSurtidas - 1 < 0 THEN 0 ELSE CajasSurtidas - 1 END
         WHERE TransferenciaId = {req.transferenciaId}
           AND UPPER(LTRIM(RTRIM(Sku))) = {row.Sku};
    ");

            return Ok(new { ok = true, msg = $"Eliminada {etiqueta} (-1 caja, -{row.Kg:N2} kg)", sku = row.Sku, kg = row.Kg });
        }

        public record DeleteAllEtiquetasSkuReq(int transferenciaId, string sku);

        [HttpPost("Transferencias/DeleteAllEtiquetasPorSku")]
        public async Task<IActionResult> DeleteAllEtiquetasPorSku([FromBody] DeleteAllEtiquetasSkuReq req)
        {
            var sku = (req.sku ?? "").Trim().ToUpperInvariant();
            if (req.transferenciaId <= 0 || string.IsNullOrWhiteSpace(sku))
                return BadRequest(new { ok = false, msg = "transferenciaId y sku son requeridos." });

            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            // 1) Traer etiquetas a borrar (para saber cuántas y cuánto kg)
            var etiquetas = (await conn.QueryAsync<(string CodigoEtiqueta, decimal Kg)>(@"
        SELECT
            CodigoEtiqueta = UPPER(LTRIM(RTRIM(CodigoEtiqueta))),
            Kg = CAST(ISNULL(Kg,0) AS DECIMAL(18,4))
        FROM dbo.TransferenciaScanEtiqueta
        WHERE TransferenciaId = @transferenciaId
          AND UPPER(LTRIM(RTRIM(Sku))) = @sku;",
                new { transferenciaId = req.transferenciaId, sku }
            )).ToList();

            if (etiquetas.Count == 0)
                return Ok(new { ok = true, msg = "No había etiquetas para eliminar.", total = 0 });

            var totalKg = etiquetas.Sum(x => x.Kg);
            var totalCajas = etiquetas.Count;

            // 2) Borrar todas las etiquetas de ese SKU
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
        DELETE FROM dbo.TransferenciaScanEtiqueta
        WHERE TransferenciaId = {req.transferenciaId}
          AND UPPER(LTRIM(RTRIM(Sku))) = {sku};
    ");

            // 3) Ajustar acumulado en TransferenciaSurtido (resta kg y cajas)
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE dbo.TransferenciaSurtido
           SET KgSurtido = CASE WHEN KgSurtido - {totalKg} < 0 THEN 0 ELSE KgSurtido - {totalKg} END,
               CajasSurtidas = CASE WHEN CajasSurtidas - {totalCajas} < 0 THEN 0 ELSE CajasSurtidas - {totalCajas} END
         WHERE TransferenciaId = {req.transferenciaId}
           AND UPPER(LTRIM(RTRIM(Sku))) = {sku};
    ");

            return Ok(new
            {
                ok = true,
                msg = $"Eliminadas {totalCajas} etiquetas de {sku} (-{totalKg:N2} kg).",
                total = totalCajas,
                kg = totalKg
            });
        }


        public record DeleteTarimaReq(int transferenciaId, string tarima);

        [HttpPost("Transferencias/DeleteTarima")]
        public async Task<IActionResult> DeleteTarima([FromBody] DeleteTarimaReq req)
        {
            var tarima = (req.tarima ?? "").Trim().ToUpperInvariant();
            if (req.transferenciaId <= 0 || string.IsNullOrWhiteSpace(tarima))
                return BadRequest(new { ok = false, msg = "transferenciaId y tarima requeridos." });

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            // 1) Totales antes de borrar
            var rows = (await conn.QueryAsync<(string Sku, decimal Kg)>(@"
        SELECT
            Sku = UPPER(LTRIM(RTRIM(Sku))),
            Kg  = CAST(ISNULL(Kg,0) AS DECIMAL(18,4))
        FROM dbo.TransferenciaScanEtiqueta
        WHERE TransferenciaId = @transferenciaId
          AND UPPER(LTRIM(RTRIM(TarimaCodigo))) = @tarima;",
                new { req.transferenciaId, tarima }
            )).ToList();

            if (rows.Count == 0)
                return Ok(new { ok = true, msg = "No había etiquetas para esa tarima.", total = 0, kg = 0m });

            var totalCajas = rows.Count;
            var totalKg = rows.Sum(x => x.Kg);

            // 2) Borrar scans de esa tarima
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
        DELETE FROM dbo.TransferenciaScanEtiqueta
        WHERE TransferenciaId = {req.transferenciaId}
          AND UPPER(LTRIM(RTRIM(TarimaCodigo))) = {tarima};
    ");

            // 3) Ajustar acumulados por SKU
            //    (resta kg y cajas agrupado por sku)
            var porSku = rows
                .GroupBy(x => x.Sku)
                .Select(g => new { Sku = g.Key, Kg = g.Sum(z => z.Kg), Cajas = g.Count() })
                .ToList();

            foreach (var g in porSku)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.TransferenciaSurtido
               SET KgSurtido = CASE WHEN KgSurtido - {g.Kg} < 0 THEN 0 ELSE KgSurtido - {g.Kg} END,
                   CajasSurtidas = CASE WHEN CajasSurtidas - {g.Cajas} < 0 THEN 0 ELSE CajasSurtidas - {g.Cajas} END
             WHERE TransferenciaId = {req.transferenciaId}
               AND UPPER(LTRIM(RTRIM(Sku))) = {g.Sku};
        ");
            }

            return Ok(new
            {
                ok = true,
                msg = $"Eliminada tarima {tarima}: -{totalCajas} etiquetas, -{totalKg:N2} kg",
                total = totalCajas,
                kg = totalKg
            });
        }







        public record EtiquetasPorSkuReq(int transferenciaId, string sku);

        [HttpPost("Transferencias/EtiquetasPorSku")]
        public async Task<IActionResult> EtiquetasPorSku([FromBody] EtiquetasPorSkuReq req)
        {
            var sku = (req.sku ?? "").Trim().ToUpper();
            if (req.transferenciaId <= 0 || string.IsNullOrWhiteSpace(sku))
                return BadRequest(new { ok = false, msg = "transferenciaId y sku son requeridos." });

            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();
            var etiquetas = (await conn.QueryAsync<TransferenciaEtiquetaVM>(@"
    SELECT 
        Id             = Id,
        CodigoEtiqueta = CodigoEtiqueta,
        Sku            = UPPER(LTRIM(RTRIM(Sku))),
        Kg             = CAST(ISNULL(Kg,0) AS DECIMAL(18,4)),
        Fecha          = Fecha,
        Usuario        = ISNULL(Usuario,''),
        TarimaCodigo   = NULLIF(UPPER(LTRIM(RTRIM(ISNULL(TarimaCodigo,'')))),'')
    FROM dbo.TransferenciaScanEtiqueta
    WHERE TransferenciaId = @transferenciaId
      AND UPPER(LTRIM(RTRIM(Sku))) = @sku
    ORDER BY Fecha DESC;
", new { transferenciaId = req.transferenciaId, sku })).ToList();

            return Ok(new { ok = true, sku, etiquetas });
        }


        public record TransferirReq(int transferenciaId);

        [HttpPost("Transferencias/Transferir")]
        public async Task<IActionResult> Transferir([FromBody] TransferirReq req)
        {
            const int MAX_INTENTOS = 5;
            const int MINUTOS_ATORADO = 2;
            int minutosAtoradoNeg = -MINUTOS_ATORADO;

            if (req.transferenciaId <= 0)
                return BadRequest(new { ok = false, msg = "transferenciaId requerido." });

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var etiquetas = (await conn.QueryAsync<string>(@"
        SELECT DISTINCT UPPER(LTRIM(RTRIM(CodigoEtiqueta)))
        FROM dbo.TransferenciaScanEtiqueta
        WHERE TransferenciaId = @id;",
                new { id = req.transferenciaId }))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpper())
                .Distinct()
                .ToList();

            if (etiquetas.Count == 0)
                return Ok(new { ok = false, msg = "No hay etiquetas escaneadas para transferir." });

            using var tx = await _context.Database.BeginTransactionAsync();
            var dbtx = tx.GetDbTransaction();

            try
            {
                var jobIdExistente = await conn.ExecuteScalarAsync<int?>(@"
            SELECT TOP 1 JobId
            FROM dbo.TransferenciaSyncJob
            WHERE TransferenciaId = @TransferenciaId
              AND Estado IN ('Pendiente', 'EnProceso', 'Error', 'ErrorParcial')
            ORDER BY JobId DESC;",
                    new { TransferenciaId = req.transferenciaId },
                    transaction: dbtx);

                int jobId;

                if (jobIdExistente.HasValue)
                {
                    jobId = jobIdExistente.Value;

                    // 1) Lo que quedó EnProceso viejo o Error recuperable se vuelve a intentar
                    await conn.ExecuteAsync(@"
                UPDATE dbo.TransferenciaSyncDetalle
                SET 
                    Estado = 'Pendiente',
                    UltimoError = NULL
                WHERE JobId = @JobId
                  AND Estado IN ('EnProceso', 'Error', 'ErrorParcial')
                  AND ISNULL(Intentos, 0) < @MaxIntentos
                  AND (
                        Estado <> 'EnProceso'
                        OR FechaUltimoIntento IS NULL
                        OR FechaUltimoIntento < DATEADD(MINUTE, @MinutosAtoradoNeg, GETDATE())
                      )
                  AND (
                        ProduccionIdP1 IS NULL 
                        OR ProduccionIdP1 = 0
                      );",
                        new
                        {
                            JobId = jobId,
                            MaxIntentos = MAX_INTENTOS,
                            MinutosAtoradoNeg = minutosAtoradoNeg
                        },
                        transaction: dbtx);

                    // 2) Los que ya pasaron intentos quedan como Error definitivo
                    await conn.ExecuteAsync(@"
                UPDATE dbo.TransferenciaSyncDetalle
                SET 
                    Estado = 'Error',
                    UltimoError = ISNULL(UltimoError, 'Máximo de intentos alcanzado')
                WHERE JobId = @JobId
                  AND Estado <> 'Ok'
                  AND ISNULL(Intentos, 0) >= @MaxIntentos;",
                        new { JobId = jobId, MaxIntentos = MAX_INTENTOS },
                        transaction: dbtx);
                }
                else
                {
                    jobId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO dbo.TransferenciaSyncJob
                (
                    TransferenciaId, Estado, TotalEtiquetas, Procesadas, Exitosas, Fallidas, Intentos, FechaCreacion
                )
                VALUES
                (
                    @TransferenciaId, 'Pendiente', @TotalEtiquetas, 0, 0, 0, 0, GETDATE()
                );

                SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        new
                        {
                            TransferenciaId = req.transferenciaId,
                            TotalEtiquetas = etiquetas.Count
                        },
                        transaction: dbtx);
                }

                // 3) Insertar solo etiquetas que no existan en ese job
                foreach (var etq in etiquetas)
                {
                    await conn.ExecuteAsync(@"
                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.TransferenciaSyncDetalle
                    WHERE JobId = @JobId
                      AND CodigoEtiqueta = @CodigoEtiqueta
                )
                BEGIN
                    INSERT INTO dbo.TransferenciaSyncDetalle
                    (
                        JobId, CodigoEtiqueta, Estado, Intentos
                    )
                    VALUES
                    (
                        @JobId, @CodigoEtiqueta, 'Pendiente', 0
                    );
                END;",
                        new { JobId = jobId, CodigoEtiqueta = etq },
                        transaction: dbtx);
                }

                // 4) Recalcular avance del job
                await conn.ExecuteAsync(@"
            ;WITH x AS (
                SELECT
                    Total = COUNT(1),
                    Exitosas = SUM(CASE WHEN Estado = 'Ok' THEN 1 ELSE 0 END),
                    Fallidas = SUM(CASE WHEN Estado = 'Error' THEN 1 ELSE 0 END),
                    Pendientes = SUM(CASE WHEN Estado = 'Pendiente' THEN 1 ELSE 0 END),
                    EnProceso = SUM(CASE WHEN Estado = 'EnProceso' THEN 1 ELSE 0 END)
                FROM dbo.TransferenciaSyncDetalle
                WHERE JobId = @JobId
            )
            UPDATE j
            SET
                TotalEtiquetas = x.Total,
                Exitosas = x.Exitosas,
                Fallidas = x.Fallidas,
                Procesadas = x.Exitosas + x.Fallidas,
                Intentos = ISNULL(j.Intentos, 0) + 1,
                Estado =
                    CASE
                        WHEN x.Pendientes > 0 THEN 'Pendiente'
                        WHEN x.EnProceso > 0 THEN 'EnProceso'
                        WHEN x.Fallidas > 0 THEN 'ErrorParcial'
                        ELSE 'Completado'
                    END,
                FechaInicio = ISNULL(j.FechaInicio, GETDATE()),
                FechaFin =
                    CASE
                        WHEN x.Pendientes = 0 AND x.EnProceso = 0 THEN GETDATE()
                        ELSE NULL
                    END
            FROM dbo.TransferenciaSyncJob j
            CROSS JOIN x
            WHERE j.JobId = @JobId;",
                    new { JobId = jobId },
                    transaction: dbtx);

                await tx.CommitAsync();

                return Ok(new
                {
                    ok = true,
                    msg = "Transferencia enviada/reintentada correctamente.",
                    jobId
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error al enviar/reintentar transferencia.",
                    error = ex.Message
                });
            }
        }





        public record CancelarReq(int transferenciaId);

        [HttpPost("Transferencias/Cancelar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancelar([FromBody] CancelarReq req)
        {
            if (req == null || req.transferenciaId <= 0)
                return BadRequest(new { ok = false, msg = "Transferencia inválida." });

            var t = await _context.Transferencias
                .FirstOrDefaultAsync(x => x.Id == req.transferenciaId);

            if (t == null)
                return NotFound(new { ok = false, msg = "Transferencia no encontrada." });

            if (t.Estatus == 0)
            {
                return Ok(new
                {
                    ok = true,
                    msg = "La transferencia ya estaba cancelada.",
                    estatus = t.Estatus
                });
            }

            t.Estatus = 0;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                ok = true,
                msg = "Transferencia cancelada correctamente.",
                estatus = t.Estatus
            });
        }





        private async Task<(List<string> Etiquetas, string Error)> BuscarTarimaEtiquetasAsync(
            string connectionString,
            string tarimaCodigo,
            string origen)
        {
            var list = new List<string>();

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return (list, $"No existe la cadena de conexión {origen}.");
            }

            try
            {
                await using var cn = new SqlConnection(connectionString);
                await cn.OpenAsync();

                const string sql = @"
SELECT 
    p.CodigoEtiqueta
FROM Tarima t
INNER JOIN TarimaDetalle td 
    ON td.TarimaId = t.TarimaId
INNER JOIN Produccion p 
    ON p.ProduccionId = td.ProduccionId
WHERE t.Estatus = 1
  AND p.Estatus = 1
  AND UPPER(LTRIM(RTRIM(t.Nombre))) = @TarimaCodigo;
";

                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = 90;

                cmd.Parameters.Add(new SqlParameter("@TarimaCodigo", SqlDbType.VarChar, 100)
                {
                    Value = (tarimaCodigo ?? "").Trim().ToUpper()
                });

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var etq = (rd["CodigoEtiqueta"]?.ToString() ?? "").Trim().ToUpper();

                    if (!string.IsNullOrWhiteSpace(etq))
                        list.Add(etq);
                }

                return (list.Distinct().ToList(), "");
            }
            catch (Exception ex)
            {
                return (list, $"Error consultando tarima en {origen}: {ex.Message}");
            }
        }




        private class EtiquetaEnOtraTransferenciaDto
        {
            public int TransferenciaId { get; set; }
            public string Folio { get; set; } = "";
        }



        private async Task<(bool ok, string msg, string sku, decimal kg, bool duplicada, string origen)>
        ProcesarEtiquetaNormalAsync(
            int transferenciaId,
            string etiqueta,
            string? tarimaCodigo = null,
            bool forzarAgregar = false)
        {
            (string Sku, decimal Kg)? info = null;
            string origen = "";

            var tr = await _context.Transferencias
                .AsNoTracking()
                .Include(t => t.Detalles)
                .FirstOrDefaultAsync(t => t.Id == transferenciaId);

            if (tr == null)
                return (false, "Transferencia no encontrada.", "", 0m, false, "");

            var csP1 = _cfg.GetConnectionString("CadenaMeatP1");
            var csTif = _cfg.GetConnectionString("CadenaMeatTIF");

            // Buscar etiqueta individual en P1
            if (!string.IsNullOrWhiteSpace(csP1))
            {
                info = await BuscarEtiquetaAsync(csP1, etiqueta);

                if (info != null)
                    origen = "P1";
            }

            // Si no se encontró en P1, buscar en TIF
            if (info == null && !string.IsNullOrWhiteSpace(csTif))
            {
                info = await BuscarEtiquetaAsync(csTif, etiqueta);

                if (info != null)
                    origen = "TIF";
            }

            if (info == null || string.IsNullOrWhiteSpace(info.Value.Sku))
                return (false, $"Etiqueta no encontrada: {etiqueta}", "", 0m, false, "");

            var skuMeat = (info.Value.Sku ?? "").Trim().ToUpperInvariant();

            var skuSolicitado = tr.Detalles.Any(d =>
                ((d.ProductoCodigo ?? "").Trim().ToUpperInvariant()) == skuMeat
            );

            if (!skuSolicitado)
            {
                return (
                    false,
                    $"El SKU {skuMeat} no está en la transferencia.",
                    skuMeat,
                    info.Value.Kg,
                    false,
                    origen
                );
            }

            var tar = string.IsNullOrWhiteSpace(tarimaCodigo)
                ? null
                : tarimaCodigo.Trim().ToUpperInvariant();

            // Validar si ya fue escaneada en otra transferencia
            if (!forzarAgregar)
            {
                var conn = _context.Database.GetDbConnection();

                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                var escaneadaEnOtra = await conn.QueryFirstOrDefaultAsync<EtiquetaEnOtraTransferenciaDto>(@"
SELECT TOP 1
    s.TransferenciaId,
    ISNULL(t.Consecutivo, CAST(s.TransferenciaId AS varchar(20))) AS Folio
FROM dbo.TransferenciaScanEtiqueta s
LEFT JOIN dbo.Transferencias t 
    ON t.Id = s.TransferenciaId
WHERE UPPER(LTRIM(RTRIM(s.CodigoEtiqueta))) = @CodigoEtiqueta
  AND s.TransferenciaId <> @TransferenciaId
ORDER BY s.TransferenciaId DESC;
", new
                {
                    CodigoEtiqueta = etiqueta,
                    TransferenciaId = transferenciaId
                });

                if (escaneadaEnOtra != null)
                {
                    return (
                        false,
                        $"YA_ESCANEADA_OTRA_TRANSFERENCIA|{escaneadaEnOtra.Folio}",
                        skuMeat,
                        info.Value.Kg,
                        false,
                        origen
                    );
                }
            }

            // Insertar escaneo
            try
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO dbo.TransferenciaScanEtiqueta
    (TransferenciaId, Sku, CodigoEtiqueta, Kg, Usuario, TarimaCodigo)
VALUES
    ({transferenciaId}, {skuMeat}, {etiqueta}, {info.Value.Kg}, {User?.Identity?.Name ?? ""}, {tar});
");
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                return (
                    false,
                    $"Etiqueta ya escaneada: {etiqueta}",
                    skuMeat,
                    info.Value.Kg,
                    true,
                    origen
                );
            }

            // Acumular surtido
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
MERGE dbo.TransferenciaSurtido AS T
USING (
    SELECT {transferenciaId} AS TransferenciaId, {skuMeat} AS Sku
) AS S
ON (
    T.TransferenciaId = S.TransferenciaId 
    AND T.Sku = S.Sku
)
WHEN MATCHED THEN
    UPDATE SET 
        KgSurtido = T.KgSurtido + {info.Value.Kg},
        CajasSurtidas = T.CajasSurtidas + 1
WHEN NOT MATCHED THEN
    INSERT (TransferenciaId, Sku, KgSurtido, CajasSurtidas)
    VALUES ({transferenciaId}, {skuMeat}, {info.Value.Kg}, 1);
");

            return (
                true,
                $"OK [{origen}]: {skuMeat} (+1 caja, +{info.Value.Kg:N2} kg){(tar != null ? $" [Tarima {tar}]" : "")}",
                skuMeat,
                info.Value.Kg,
                false,
                origen
            );
        }




        //HELPER
        private static string SoloNombreProducto(string nombre, string sku)
        {
            nombre = (nombre ?? "").Trim();
            sku = (sku ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nombre)) return "";

            if (!string.IsNullOrWhiteSpace(sku))
            {
                // "SKU (NOMBRE)" => "NOMBRE"
                var m = Regex.Match(nombre, $"^{Regex.Escape(sku)}\\s*\\((.*)\\)\\s*$", RegexOptions.IgnoreCase);
                if (m.Success) return (m.Groups[1].Value ?? "").Trim();

                // "SKU - NOMBRE" / "SKU NOMBRE" => quitar prefijo
                nombre = Regex.Replace(nombre, $"^{Regex.Escape(sku)}\\s*[-–—:]?\\s*", "", RegexOptions.IgnoreCase).Trim();
            }

            // "(NOMBRE)" => "NOMBRE"
            var m2 = Regex.Match(nombre, "^\\((.*)\\)$");
            if (m2.Success) return (m2.Groups[1].Value ?? "").Trim();

            return nombre;
        }

        public record EstadoItemsReq(int transferenciaId);

        [HttpPost("Transferencias/EstadoItems")]
        public async Task<IActionResult> EstadoItems([FromBody] EstadoItemsReq req)
        {
            if (req == null || req.transferenciaId <= 0)
                return BadRequest(new { ok = false, msg = "transferenciaId requerido." });

            // 1) SKUs de la transferencia (los que se muestran en la lista)
            var skus = await _context.TransferenciaDetalles
                .AsNoTracking()
                .Where(d => d.TransferenciaId == req.transferenciaId)
                .Select(d => (d.ProductoCodigo ?? "").Trim().ToUpper())
                .ToListAsync();

            skus = skus.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

            // 2) Acumulados actuales (lo que cambió con scan/delete)
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var surtidos = (await conn.QueryAsync<(string Sku, decimal KgSurtido, int CajasSurtidas)>(@"
        SELECT
            Sku = UPPER(LTRIM(RTRIM(Sku))),
            KgSurtido = CAST(ISNULL(KgSurtido,0) AS DECIMAL(18,4)),
            CajasSurtidas = ISNULL(CajasSurtidas,0)
        FROM dbo.TransferenciaSurtido
        WHERE TransferenciaId = @id;",
                new { id = req.transferenciaId }
            )).ToList();

            var dic = surtidos
                .GroupBy(x => (x.Sku ?? "").Trim().ToUpper())
                .ToDictionary(g => g.Key, g => new
                {
                    KgSurtido = g.Sum(z => z.KgSurtido),
                    CajasSurtidas = g.Sum(z => z.CajasSurtidas)
                });

            // 3) Respuesta por SKU (incluye ceros si no existe fila en TransferenciaSurtido)
            var items = skus.Select(sku =>
            {
                if (dic.TryGetValue(sku, out var s))
                {
                    return new { sku, surtido = s.KgSurtido, cajasSurtido = s.CajasSurtidas };
                }
                return new { sku, surtido = 0m, cajasSurtido = 0 };
            }).ToList();

            var hayRegistrado = items.Any(x => x.surtido > 0m || x.cajasSurtido > 0);

            return Ok(new { ok = true, items, hayRegistrado });
        }

        private async Task<List<SerieTransferenciaPermisoDto>> ObtenerSeriesTransferenciasUsuarioActualAsync(CancellationToken ct = default)
        {
            var raw = (User?.Identity?.Name ?? "").Trim();
            var username = raw.Contains("\\") ? raw.Split("\\").Last() : raw;
            var usernameEmail = username.Contains("@") ? username : $"{username}@carnesg.net";

            var data = await (
                from u in _context.UsuarioSQL.AsNoTracking()
                join us in _context.UsuarioSeries.AsNoTracking()
                    on u.Id equals us.UsuarioId
                join s in _context.Series.AsNoTracking()
                    on us.SerieId equals s.Id
                where u.Activo
                   && (
                        u.Usuario == raw ||
                        u.Usuario == username ||
                        u.Usuario == usernameEmail ||
                        u.Nombre == raw ||
                        u.Nombre == username
                      )
                select new SerieTransferenciaPermisoDto
                {
                    Serie = s.NombreSerie ?? "",
                    Sucursal = s.Sucursal ?? ""
                }
            )
            .ToListAsync(ct);

            return data
                .Where(x => !string.IsNullOrWhiteSpace(x.Serie) || !string.IsNullOrWhiteSpace(x.Sucursal))
                .Select(x => new SerieTransferenciaPermisoDto
                {
                    Serie = (x.Serie ?? "").Trim().ToUpper(),
                    Sucursal = (x.Sucursal ?? "").Trim().ToUpper()
                })
                .DistinctBy(x => $"{x.Serie}|{x.Sucursal}")
                .ToList();
        }

        private class SerieTransferenciaPermisoDto
        {
            public string Serie { get; set; } = "";
            public string Sucursal { get; set; } = "";
        }


        // ==========================================================
        // GET: /Almacen/GetTransferencias
        // Lista principal (tabla)
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> GetTransferencias(CancellationToken ct = default)
        {
            var transferencias = await _context.Transferencias
                .AsNoTracking()
                .Where(t => t.Estatus == 1 || t.Estatus == 3)
                .OrderByDescending(t => t.Id)
                .Select(t => new
                {
                    id = t.Id,
                    consecutivo = t.Consecutivo,

                    // Se deja vacío para no depender de serie en transferencias
                    serie = "",

                    sucursal = t.Sucursal,
                    fechaSolicitud = t.FechaSolicitud.HasValue
                        ? t.FechaSolicitud.Value.ToString("yyyy-MM-dd")
                        : "",
                    mes = t.FechaSolicitud.HasValue ? t.FechaSolicitud.Value.Month : (int?)null,
                    anio = t.FechaSolicitud.HasValue ? t.FechaSolicitud.Value.Year : (int?)null,
                    observacion = t.Observacion,
                    estatus = t.Estatus,
                    usuarioSolicita = t.UsuarioSolicita,
                    fechaCreacion = t.FechaCreacion.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToListAsync(ct);

            return Json(transferencias);
        }

        // ==========================================================
        // GET: /Almacen/GetTransferenciaDetalle?id=123
        // Header + detalles para el modal
        // Incluye Cajas + KgPorCaja (ArticuloSap.U_CAJAS)
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> GetTransferenciaDetalle([FromQuery] int id)
        {
            var tr = await _context.Transferencias
                .AsNoTracking()
                .Where(t => t.Id == id && (t.Estatus == 1 || t.Estatus == 3))
                .Select(t => new
                {
                    id = t.Id,
                    consecutivo = t.Consecutivo,
                    sucursal = t.Sucursal,
                    fechaSolicitud = t.FechaSolicitud.HasValue
                        ? t.FechaSolicitud.Value.ToString("yyyy-MM-dd")
                        : "",
                    observacion = t.Observacion,
                    estatus = t.Estatus,
                    usuarioSolicita = t.UsuarioSolicita,
                    fechaCreacion = t.FechaCreacion.ToString("yyyy-MM-dd HH:mm:ss"),

                    items = t.Detalles
                        .OrderBy(d => d.Id)
                        .Select(d => new
                        {
                            detalleId = d.Id,
                            sku = d.ProductoCodigo,
                            producto = d.ProductoNombre,

                            // ✅ Campos para edición en UI
                            cajas = d.Cajas,          // <- asegúrate que exista en tu entidad
                            kg = d.CantidadKg,        // ya lo tenías

                            // ✅ Factor de conversión (promedio) por SKU desde ArticuloSap.U_CAJAS
                            kgPorCaja = _context.ArticuloSap
                                .Where(a => a.ProductoCodigo == d.ProductoCodigo)
                                .Select(a => (decimal?)a.U_KilosCaja) // por si viene null
                                .FirstOrDefault() ?? 0m
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (tr == null)
                return NotFound("Transferencia no encontrada.");

            return Json(tr);
        }

        // ==========================================================
        // POST: /Transferencias/GuardarTransferencia
        // Guarda SKU, KG y Cajas por renglón
        // ==========================================================
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Consumes("application/json")]
        public async Task<IActionResult> GuardarTransferencia([FromBody] GuardarTransferenciaRequest req)
        {
            var trace = new List<string>();

            try
            {
                trace.Add("START GuardarTransferencia");

                trace.Add($"REQ null? {req is null}");
                if (req == null) return BadRequest(new { ok = false, trace, msg = "req null" });

                trace.Add($"REQ.Id={req.Id}");
                if (req.Id <= 0) return BadRequest(new { ok = false, trace, msg = "Id inválido" });

                trace.Add($"Items null? {req.Items is null} Count={(req.Items?.Count ?? 0)}");
                if (req.Items == null || req.Items.Count == 0)
                    return BadRequest(new { ok = false, trace, msg = "No hay renglones" });

                // dump del payload (por si algo llega raro)
                trace.Add("Payload JSON: " + JsonSerializer.Serialize(req));

                // Validación
                for (int i = 0; i < req.Items.Count; i++)
                {
                    var it = req.Items[i];
                    trace.Add($"Validando item {i + 1}: sku='{it?.Sku}', kg={it?.Kg}, cajas={it?.Cajas}");

                    if (string.IsNullOrWhiteSpace(it?.Sku))
                        return BadRequest(new { ok = false, trace, msg = $"SKU vacío en renglón {i + 1}" });
                    if (it.Kg < 0)
                        return BadRequest(new { ok = false, trace, msg = $"KG negativo en renglón {i + 1}" });
                    if (it.Cajas is < 0)
                        return BadRequest(new { ok = false, trace, msg = $"Cajas negativo en renglón {i + 1}" });
                }

                trace.Add("Buscando Transferencia base...");
                var trBase = await _context.Transferencias.FirstOrDefaultAsync(t => t.Id == req.Id);

                trace.Add($"Transferencia encontrada? {trBase != null}");
                if (trBase == null)
                    return NotFound(new { ok = false, trace, msg = "Transferencia no encontrada" });

                await using var tx = await _context.Database.BeginTransactionAsync();
                trace.Add("BEGIN TRANSACTION");

                trace.Add("Buscando PedidoTransferencia...");
                var pedido = await _context.PedidosTransferencia
                    .FirstOrDefaultAsync(p => p.TransferenciaId == req.Id && p.Estatus != 4);

                trace.Add($"Pedido existe? {pedido != null}");

                if (pedido == null)
                {
                    trace.Add("Creando nuevo PedidoTransferencia...");
                    pedido = new PedidoTransferencia
                    {
                        TransferenciaId = trBase.Id,
                        Consecutivo = trBase.Consecutivo,
                        Destino = trBase.Sucursal,
                        FechaSolicitud = trBase.FechaSolicitud,
                        Observacion = trBase.Observacion ?? string.Empty,
                        Estatus = 0,
                        UsuarioSolicita = trBase.UsuarioSolicita ?? string.Empty
                    };

                    _context.PedidosTransferencia.Add(pedido);
                    trace.Add("SaveChanges #1 (insert pedido)...");
                    await _context.SaveChangesAsync();
                    trace.Add($"Pedido insertado. pedido.Id={pedido.Id}");
                }
                else
                {
                    trace.Add($"Actualizando pedido.Id={pedido.Id}...");
                    pedido.Destino = trBase.Sucursal;
                    pedido.FechaSolicitud = trBase.FechaSolicitud;
                    pedido.Observacion = trBase.Observacion ?? pedido.Observacion;

                    trace.Add("Cargando detalles existentes...");
                    var existentes = await _context.PedidosTransferenciaDetalle
                        .Where(d => d.PedidoTransferenciaId == pedido.Id)
                        .ToListAsync();

                    trace.Add($"Detalles existentes: {existentes.Count}");

                    if (existentes.Count > 0)
                    {
                        _context.PedidosTransferenciaDetalle.RemoveRange(existentes);
                        trace.Add("SaveChanges #1 (delete detalles)...");
                        await _context.SaveChangesAsync();
                        trace.Add("Detalles eliminados OK");
                    }
                }

                // Insertar nuevos detalles
                trace.Add("Insertando nuevos detalles...");
                var nuevos = new List<PedidoTransferenciaDetalle>(req.Items.Count);

                for (int i = 0; i < req.Items.Count; i++)
                {
                    var it = req.Items[i];
                    var sku = (it.Sku ?? "").Trim().ToUpperInvariant();

                    nuevos.Add(new PedidoTransferenciaDetalle
                    {
                        TransferenciaDetalleIdOriginal = it.DetalleId,
                        ProductoCodigo = sku,
                        CantidadKg = decimal.Round(it.Kg, 4, MidpointRounding.AwayFromZero),
                        Cajas = it.Cajas.HasValue ? (int)it.Cajas.Value : 0,
                        Orden = i + 1,
                        PedidoTransferenciaId = pedido.Id
                    });

                    trace.Add($"Detalle {i + 1} OK sku={sku} kg={it.Kg} cajas={it.Cajas ?? 0} pedidoId={pedido.Id}");
                }

                _context.PedidosTransferenciaDetalle.AddRange(nuevos);

                trBase.Estatus = 4;
                trace.Add("Set trBase.Estatus=4");

                trace.Add("SaveChanges #2 (insert detalles + update transferencia)...");
                var affected = await _context.SaveChangesAsync();
                trace.Add($"SaveChanges #2 OK affected={affected}");

                await tx.CommitAsync();
                trace.Add("COMMIT OK");

                return Ok(new { ok = true, pedidoId = pedido.Id, affected, trace });
            }
            catch (DbUpdateException dbex)
            {
                trace.Add("CATCH DbUpdateException");
                trace.Add("BaseException: " + dbex.GetBaseException().Message);
                trace.Add("Exception: " + dbex.Message);

                // Esto a veces trae el error interno más claro:
                if (dbex.InnerException != null)
                    trace.Add("Inner: " + dbex.InnerException.Message);

                return StatusCode(500, new { ok = false, message = "DbUpdateException", detail = dbex.GetBaseException().Message, trace });
            }
            catch (Exception ex)
            {
                trace.Add("CATCH Exception");
                trace.Add("BaseException: " + ex.GetBaseException().Message);
                trace.Add("Exception: " + ex.Message);

                if (ex.InnerException != null)
                    trace.Add("Inner: " + ex.InnerException.Message);

                return StatusCode(500, new { ok = false, message = "Exception", detail = ex.GetBaseException().Message, trace });
            }

        }

        [HttpGet]
        public async Task<IActionResult> RomaneoTransferencia(
    DateTime? desde,
    DateTime? hasta,
    List<string> destinosSel,
    List<string> pedidosSel,
    List<string> tarimasSel,
    string? codigoEtiqueta
)
        {
            var hoy = DateTime.Today;
            var d1 = (desde ?? hoy).Date;
            var d2 = (hasta ?? hoy).Date;

            codigoEtiqueta = (codigoEtiqueta ?? "").Trim();

            var baseQ = _context.Set<RomaneoTransferenciasRowVM>()
                .FromSqlRaw("SELECT * FROM dbo.RomaneoTransferencias")
                .AsNoTracking();

            // Solo filtra por fecha si no hay código de etiqueta
            if (string.IsNullOrWhiteSpace(codigoEtiqueta))
            {
                baseQ = baseQ.Where(x => x.Fecha >= d1 && x.Fecha <= d2);
            }

            destinosSel = (destinosSel ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            pedidosSel = (pedidosSel ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            tarimasSel = (tarimasSel ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            var destinos = await baseQ
                .Select(x => x.Destino)
                .Where(x => x != null && x != "")
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var qCombos = baseQ;

            if (destinosSel.Any())
                qCombos = qCombos.Where(x => destinosSel.Contains(x.Destino));

            var pedidos = await qCombos
                .Select(x => x.Pedido)
                .Where(x => x != null && x != "")
                .Distinct()
                .OrderByDescending(x => x)
                .ToListAsync();

            if (pedidosSel.Any())
                qCombos = qCombos.Where(x => pedidosSel.Contains(x.Pedido));

            var tarimas = await qCombos
                .Select(x => x.Tarima)
                .Where(x => x != null && x != "")
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var q = qCombos;

            if (tarimasSel.Any())
                q = q.Where(x => tarimasSel.Contains(x.Tarima));

            var rows = await q
                .OrderBy(x => x.Fecha)
                .ThenBy(x => x.Pedido)
                .ThenBy(x => x.Tarima)
                .ThenBy(x => x.ProductoCodigo)
                .ToListAsync();

            var transferenciaIds = rows
                .Select(x => x.TransferenciaId)
                .Distinct()
                .ToList();

            var detalleEtiquetas = new List<TransferenciaScanEtiquetaDetalleVM>();

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            if (!string.IsNullOrWhiteSpace(codigoEtiqueta))
            {
                // Buscar por código de etiqueta sin restricción de fecha
                detalleEtiquetas = (await conn.QueryAsync<TransferenciaScanEtiquetaDetalleVM>(@"
            SELECT
                TransferenciaId,
                Sku,
                CodigoEtiqueta,
                Kg,
                Fecha,
                Usuario,
                ISNULL(NULLIF(LTRIM(RTRIM(TarimaCodigo)), ''), 'Sin Tarima') AS TarimaCodigo
            FROM dbo.TransferenciaScanEtiqueta
            WHERE CodigoEtiqueta LIKE '%' + @codigo + '%'
            ORDER BY TransferenciaId, TarimaCodigo, Fecha
        ", new { codigo = codigoEtiqueta.Trim() })).ToList();

                // Obtener los TransferenciaIds que matchean
                var idsMatch = detalleEtiquetas
                    .Select(x => x.TransferenciaId)
                    .Distinct()
                    .ToList();

                if (idsMatch.Any())
                {
                    // Obtener solo los pares TransferenciaId+Tarima que tienen la etiqueta
                    var paresMatch = detalleEtiquetas
                        .Select(x => new {
                            x.TransferenciaId,
                            Tarima = string.IsNullOrWhiteSpace(x.TarimaCodigo) ? "Sin Tarima" : x.TarimaCodigo.Trim()
                        })
                        .Distinct()
                        .ToList();

                    var todasRows = await _context.Set<RomaneoTransferenciasRowVM>()
                        .FromSqlRaw("SELECT * FROM dbo.RomaneoTransferencias")
                        .AsNoTracking()
                        .Where(x => idsMatch.Contains(x.TransferenciaId))
                        .OrderBy(x => x.Fecha)
                        .ThenBy(x => x.Pedido)
                        .ThenBy(x => x.Tarima)
                        .ThenBy(x => x.ProductoCodigo)
                        .ToListAsync();

                    // Filtrar solo las rows cuya tarima tiene la etiqueta buscada
                    rows = todasRows
                        .Where(r => paresMatch.Any(m =>
                            m.TransferenciaId == r.TransferenciaId &&
                            string.Equals(
                                m.Tarima,
                                string.IsNullOrWhiteSpace(r.Tarima) ? "Sin Tarima" : r.Tarima.Trim(),
                                StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }
                else
                {
                    rows = new List<RomaneoTransferenciasRowVM>();
                }

                // Recalcular combos en base al resultado filtrado
                destinos = rows
                    .Select(x => x.Destino)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                pedidos = rows
                    .Select(x => x.Pedido)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .OrderByDescending(x => x)
                    .ToList();

                tarimas = rows
                    .Select(x => x.Tarima)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
            }
            else if (transferenciaIds.Any())
            {
                detalleEtiquetas = (await conn.QueryAsync<TransferenciaScanEtiquetaDetalleVM>(@"
            SELECT
                TransferenciaId,
                Sku,
                CodigoEtiqueta,
                Kg,
                Fecha,
                Usuario,
                ISNULL(NULLIF(LTRIM(RTRIM(TarimaCodigo)), ''), 'Sin Tarima') AS TarimaCodigo
            FROM dbo.TransferenciaScanEtiqueta
            WHERE TransferenciaId IN @ids
            ORDER BY TransferenciaId, TarimaCodigo, Fecha
        ", new { ids = transferenciaIds })).ToList();
            }

            var vm = new RomaneoTransferenciasVM
            {
                Desde = d1,
                Hasta = d2,
                Destinos = destinos,
                Pedidos = pedidos,
                Tarimas = tarimas,
                DestinosSeleccionados = destinosSel,
                PedidosSeleccionados = pedidosSel,
                TarimasSeleccionadas = tarimasSel,
                CodigoEtiqueta = codigoEtiqueta ?? "",
                Rows = rows,
                DetalleEtiquetas = detalleEtiquetas
            };

            return View("RomaneoTransferencia", vm);
        }



        [HttpGet]
        public async Task<IActionResult> ExportCaducidadTransferencias(
            DateTime? desde,
            DateTime? hasta,
            List<string> destinosSel,
            List<string> pedidosSel,
            List<string> tarimasSel,
            CancellationToken ct)
        {
            // Defaults (por si llegan vacíos)
            var fDesde = (desde ?? DateTime.Today).Date;
            var fHasta = (hasta ?? DateTime.Today).Date;

            // 1) Primero: resolver lista de PEDIDOS según filtros
            //    - si pedidosSel trae cosas, úsalo tal cual
            //    - si NO trae, deriva pedidos desde RomaneoTransferencias con filtros
            List<string> pedidosAExportar;

            if (pedidosSel != null && pedidosSel.Count > 0)
            {
                pedidosAExportar = pedidosSel
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct()
                    .ToList();
            }
            else
            {
                // Ajusta nombres de columnas a tu tabla real:
                // a.Pedido, a.Destino, a.Tarima, a.Fecha (o FechaHora)
                const string sqlPedidos = @"
SELECT DISTINCT
    Pedido = CONVERT(varchar(30), a.Pedido)
FROM dbo.RomaneoTransferencias a
WHERE CONVERT(date, a.Fecha) >= @Desde
  AND CONVERT(date, a.Fecha) <= @Hasta
  AND (@DestCount = 0 OR a.Destino IN @Destinos)
  AND (@TarCount  = 0 OR a.Tarima  IN @Tarimas)
ORDER BY Pedido;";

                var conn0 = _context.Database.GetDbConnection();
                if (conn0.State != ConnectionState.Open)
                    await conn0.OpenAsync(ct);

                var cmdPedidos = new CommandDefinition(
                    sqlPedidos,
                    new
                    {
                        Desde = fDesde,
                        Hasta = fHasta,
                        DestCount = (destinosSel?.Count ?? 0),
                        TarCount = (tarimasSel?.Count ?? 0),
                        Destinos = destinosSel ?? new List<string>(),
                        Tarimas = tarimasSel ?? new List<string>()
                    },
                    cancellationToken: ct,
                    commandTimeout: 120 // pedidos suele ser rápido
                );

                pedidosAExportar = (await conn0.QueryAsync<string>(cmdPedidos)).ToList();
            }

            if (pedidosAExportar.Count == 0)
                return BadRequest("No hay pedidos para exportar con esos filtros.");

            // 2) SQL de caducidad (EL MISMO QUE YA TIENES)
            const string sqlCaducidad = @"
/* ============================================================
   CADUCIDAD POR TRANSFERENCIA (TIF -> P1) - SIN ERRORES SINTAXIS
   ============================================================ */

DECLARE @Pedido nvarchar(30) = @pPedido;

;WITH BaseEtiquetas AS (
    SELECT
        a.TransferenciaId,
        Pedido = LTRIM(RTRIM(CONVERT(nvarchar(30), a.Consecutivo))) COLLATE Modern_Spanish_CI_AS,
        ProductoCodigo = CONVERT(nvarchar(50), d.ProductoCodigo) COLLATE Modern_Spanish_CI_AS,
        ProductoNombre = CONVERT(nvarchar(200), d.ProductoNombre) COLLATE Modern_Spanish_CI_AS,
        Sku = CONVERT(nvarchar(50), c.Sku) COLLATE Modern_Spanish_CI_AS,
        CodigoEtiqueta = CONVERT(nvarchar(200), LTRIM(RTRIM(c.CodigoEtiqueta))) COLLATE Modern_Spanish_CI_AS,
        Kg = CAST(COALESCE(c.Kg, 0) AS decimal(18,4))
    FROM dbo.PedidosTransferencia a
    INNER JOIN dbo.TransferenciaScanEtiqueta c
        ON a.TransferenciaId = c.TransferenciaId
    INNER JOIN dbo.ArticuloSap d
        ON c.Sku = d.ProductoCodigo
    WHERE LTRIM(RTRIM(CONVERT(nvarchar(30), a.Consecutivo))) COLLATE Modern_Spanish_CI_AS
        = LTRIM(RTRIM(@Pedido)) COLLATE Modern_Spanish_CI_AS
),

EtiquetasPedido AS (
    SELECT DISTINCT CodigoEtiqueta
    FROM BaseEtiquetas
),

LogTIF AS (
    SELECT
        CodigoEtiqueta,
        ProduccionId,
        EtiquetacionId,
        FechaHoraEvento,
        rn = ROW_NUMBER() OVER (PARTITION BY CodigoEtiqueta ORDER BY FechaHoraEvento DESC)
    FROM (
        SELECT
            CodigoEtiqueta = CONVERT(nvarchar(200), LTRIM(RTRIM(pel.CodigoEtiqueta))) COLLATE Modern_Spanish_CI_AS,
            pel.ProduccionId,
            pel.EtiquetacionId,
            pel.FechaHoraEvento
        FROM [Meat_TIF].TIF_MEAT.dbo.ProduccionEtiquetacionLog pel
        INNER JOIN EtiquetasPedido ep
            ON CONVERT(nvarchar(200), LTRIM(RTRIM(pel.CodigoEtiqueta))) COLLATE Modern_Spanish_CI_AS
             = ep.CodigoEtiqueta
    ) x
),

LogP1 AS (
    SELECT
        CodigoEtiqueta,
        ProduccionId,
        EtiquetacionId,
        FechaHoraEvento,
        rn = ROW_NUMBER() OVER (PARTITION BY CodigoEtiqueta ORDER BY FechaHoraEvento DESC)
    FROM (
        SELECT
            CodigoEtiqueta = CONVERT(nvarchar(200), LTRIM(RTRIM(pel.CodigoEtiqueta))) COLLATE Modern_Spanish_CI_AS,
            pel.ProduccionId,
            pel.EtiquetacionId,
            pel.FechaHoraEvento
        FROM [Meat_P1].Meat.dbo.ProduccionEtiquetacionLog pel
        INNER JOIN EtiquetasPedido ep
            ON CONVERT(nvarchar(200), LTRIM(RTRIM(pel.CodigoEtiqueta))) COLLATE Modern_Spanish_CI_AS
             = ep.CodigoEtiqueta
    ) x
),

ConLog AS (
    SELECT
        b.Pedido,
        b.ProductoCodigo,
        b.ProductoNombre,
        b.Sku,
        b.CodigoEtiqueta,
        b.Kg,

        Planta = CONVERT(
            nvarchar(10),
            CASE
                WHEN lt.ProduccionId IS NOT NULL THEN 'TIF'
                WHEN lp.ProduccionId IS NOT NULL THEN 'P1'
                ELSE 'SIN LOG'
            END
        ) COLLATE Modern_Spanish_CI_AS,

        ProduccionId = COALESCE(lt.ProduccionId, lp.ProduccionId),
        EtiquetacionId = COALESCE(lt.EtiquetacionId, lp.EtiquetacionId)
    FROM BaseEtiquetas b
    LEFT JOIN LogTIF lt
        ON lt.CodigoEtiqueta = b.CodigoEtiqueta
       AND lt.rn = 1
    LEFT JOIN LogP1 lp
        ON lp.CodigoEtiqueta = b.CodigoEtiqueta
       AND lp.rn = 1
),

ProdTIF AS (
    SELECT
        pr.ProduccionId,
        SKU = CONVERT(nvarchar(50), pr.Articulo) COLLATE Modern_Spanish_CI_AS,
        FechaProduccion = CONVERT(date, pr.FechaProduccion),
        PesoKg = CAST(pr.PesoNeto AS decimal(18,4)),
        pr.LoteId
    FROM [Meat_TIF].TIF_MEAT.dbo.Produccion pr
),

ProdP1 AS (
    SELECT
        pr.ProduccionId,
        SKU = CONVERT(nvarchar(50), pr.Articulo) COLLATE Modern_Spanish_CI_AS,
        FechaProduccion = CONVERT(date, pr.FechaProduccion),
        PesoKg = CAST(pr.PesoNeto AS decimal(18,4)),
        pr.LoteId
    FROM [Meat_P1].Meat.dbo.Produccion pr
),

LoteTIF AS (
    SELECT
        l.LoteId,
        Lote = CONVERT(nvarchar(200), LTRIM(RTRIM(l.Nombre))) COLLATE Modern_Spanish_CI_AS
    FROM [Meat_TIF].TIF_MEAT.dbo.Lote l
),

LoteP1 AS (
    SELECT
        l.LoteId,
        Lote = CONVERT(nvarchar(200), LTRIM(RTRIM(l.Nombre))) COLLATE Modern_Spanish_CI_AS
    FROM [Meat_P1].Meat.dbo.Lote l
),

ColTIF AS (
    SELECT
        ColectorId,
        DiasVida = TRY_CONVERT(int, Interface)
    FROM [Meat_TIF].tif_CommerciaNet.dbo.colector
    WHERE SistemaId = 'ETI'
),

ColP1 AS (
    SELECT
        ColectorId,
        DiasVida = TRY_CONVERT(int, Interface)
    FROM [Meat_P1].CommerciaNet.dbo.colector
    WHERE SistemaId = 'ETI'
),

Detalle AS (
    SELECT
        c.CodigoEtiqueta,

        Planta = c.Planta,

        SKU = COALESCE(
            NULLIF(c.Sku, N''),
            NULLIF(c.ProductoCodigo, N''),
            N'SIN SKU'
        ) COLLATE Modern_Spanish_CI_AS,

        Producto = COALESCE(
            NULLIF(c.ProductoNombre, N''),
            N'SIN PRODUCTO'
        ) COLLATE Modern_Spanish_CI_AS,

        Lote = COALESCE(
            lt.Lote,
            lp.Lote,
            N'SIN LOTE'
        ) COLLATE Modern_Spanish_CI_AS,

        FechaProduccion = COALESCE(pt.FechaProduccion, pp.FechaProduccion),

        PesoKg = COALESCE(
            pt.PesoKg,
            pp.PesoKg,
            c.Kg,
            CAST(0 AS decimal(18,4))
        ),

        DiasVida = COALESCE(ct.DiasVida, cp.DiasVida, 0),

        FechaCaducidad = CASE
            WHEN COALESCE(pt.FechaProduccion, pp.FechaProduccion) IS NULL THEN NULL
            ELSE DATEADD(
                day,
                COALESCE(ct.DiasVida, cp.DiasVida, 0),
                COALESCE(pt.FechaProduccion, pp.FechaProduccion)
            )
        END,

        FechaSacrificio = TRY_CONVERT(date, sr.Referencia, 103)
    FROM ConLog c
    LEFT JOIN ProdTIF pt
        ON c.Planta = N'TIF'
       AND pt.ProduccionId = c.ProduccionId
    LEFT JOIN ProdP1 pp
        ON c.Planta = N'P1'
       AND pp.ProduccionId = c.ProduccionId
    LEFT JOIN LoteTIF lt
        ON c.Planta = N'TIF'
       AND lt.LoteId = pt.LoteId
    LEFT JOIN LoteP1 lp
        ON c.Planta = N'P1'
       AND lp.LoteId = pp.LoteId
    LEFT JOIN ColTIF ct
        ON c.Planta = N'TIF'
       AND ct.ColectorId = c.EtiquetacionId
    LEFT JOIN ColP1 cp
        ON c.Planta = N'P1'
       AND cp.ColectorId = c.EtiquetacionId
    LEFT JOIN [Meat_TIF].TIF_MEAT.dbo.LOTE ltf
        ON CONVERT(nvarchar(200), LTRIM(RTRIM(ltf.nombre))) COLLATE Modern_Spanish_CI_AS
         = COALESCE(lt.Lote, lp.Lote, N'') COLLATE Modern_Spanish_CI_AS
    LEFT JOIN [Meat_TIF].TIF_MEAT.dbo.SolicitudReferencia sr
        ON sr.solicitudProduccionid = ltf.loteid
       AND sr.tiporeferenciaId = 47
),

Normalizado AS (
    SELECT
        Planta,
        SKU,
        Producto,
        Lote,
        FechaSacrificio,
        FechaProduccion,
        FechaCaducidad,
        PesoKg
    FROM Detalle
)

SELECT
    planta = n.Planta,
    sku = n.SKU,
    producto = n.Producto,
    lote = n.Lote,
    fecha_sacrificio = CONVERT(varchar(10), n.FechaSacrificio, 103),
    fecha_produccion = CONVERT(varchar(10), n.FechaProduccion, 103),
    fecha_caducidad = CONVERT(varchar(10), n.FechaCaducidad, 103),
    Cuenta_de_etiqueta = COUNT(1),
    Suma_de_kg = CAST(SUM(n.PesoKg) AS decimal(18,3))
FROM Normalizado n
GROUP BY
    n.Planta,
    n.SKU,
    n.Producto,
    n.Lote,
    n.FechaSacrificio,
    n.FechaProduccion,
    n.FechaCaducidad
ORDER BY
    n.Planta,
    n.SKU,
    n.Producto,
    n.Lote,
    n.FechaProduccion,
    n.FechaCaducidad;
";

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            // 3) Excel
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("AvisoMovilizacion");

            ws.Cell(1, 1).Value = "pedido";
            ws.Cell(1, 2).Value = "sku";
            ws.Cell(1, 3).Value = "producto";
            ws.Cell(1, 4).Value = "lote";
            ws.Cell(1, 5).Value = "fecha sacrificio";
            ws.Cell(1, 6).Value = "fecha produccion";
            ws.Cell(1, 7).Value = "fecha caducidad";
            ws.Cell(1, 8).Value = "Cuenta de etiqueta";
            ws.Cell(1, 9).Value = "Suma de kg";
            ws.Range(1, 1, 1, 9).Style.Font.Bold = true;

            var row = 2;

            foreach (var pedido in pedidosAExportar)
            {
                var cmd = new CommandDefinition(
                    sqlCaducidad,
                    new { pPedido = pedido },
                    cancellationToken: ct,
                    commandTimeout: 600
                );

                var data = (await conn.QueryAsync<AvisoMovilizacionDTO>(cmd)).ToList();

                foreach (var r in data)
                {
                    ws.Cell(row, 1).Value = pedido;
                    ws.Cell(row, 2).Value = r.sku;
                    ws.Cell(row, 3).Value = r.producto;
                    ws.Cell(row, 4).Value = r.lote;
                    ws.Cell(row, 5).Value = r.fecha_sacrificio;
                    ws.Cell(row, 6).Value = r.fecha_produccion;
                    ws.Cell(row, 7).Value = r.fecha_caducidad;
                    ws.Cell(row, 8).Value = r.Cuenta_de_etiqueta;
                    ws.Cell(row, 9).Value = r.Suma_de_kg;
                    row++;
                }
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;

            var fileName = $"AvisoMovilizacion_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        public record CancelarTransferenciaReq(int Id);

        [HttpPost("Transferencias/CancelarTransferencia")]
        public async Task<IActionResult> CancelarTransferencia([FromBody] CancelarTransferenciaReq req)
        {
            if (req == null || req.Id <= 0)
                return BadRequest(new { ok = false, msg = "Id inválido." });

            var tr = await _context.Transferencias.FirstOrDefaultAsync(x => x.Id == req.Id);

            if (tr == null)
                return NotFound(new { ok = false, msg = "Transferencia no encontrada." });

            // Ajusta el estatus que uses como cancelado
            tr.Estatus = 0;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                ok = true,
                msg = $"La transferencia {tr.Consecutivo ?? tr.Id.ToString()} fue cancelada correctamente."
            });
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
        WHERE UPPER(LTRIM(RTRIM(P.CodigoEtiqueta))) IN @etiquetas
          AND P.Estatus = 1;",
                new { etiquetas = etqs }))
                .ToList();

            return found;
        }

        private async Task<List<string>> CopiarEtiquetasTifAP1Async(string csTif, string csP1, IEnumerable<string> etiquetas)
        {
            var etqs = (etiquetas ?? Enumerable.Empty<string>())
                .Select(x => (x ?? "").Trim().ToUpper())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (etqs.Count == 0) return new List<string>();

            const int CMD = 180;

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
                    // =========================
                    // 1) Producción origen en TIF
                    // =========================
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
                WHERE UPPER(LTRIM(RTRIM(CodigoEtiqueta))) = @etq
                  AND Estatus = 1;",
                        new { etq },
                        commandTimeout: CMD);

                    if (p == null)
                        continue;

                    var codigoEtiqueta = ToStr(p.CodigoEtiqueta)?.Trim().ToUpper();
                    if (string.IsNullOrWhiteSpace(codigoEtiqueta))
                        continue;

                    // Si ya existe en P1, no lo volvemos a copiar
                    var existeP1 = await cnP1.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM dbo.Produccion
                WHERE UPPER(LTRIM(RTRIM(CodigoEtiqueta))) = @etq;",
                        new { etq = codigoEtiqueta },
                        transaction: tx,
                        commandTimeout: CMD);

                    if (existeP1 > 0)
                        continue;

                    var tifProduccionId = ToIntN(p.ProduccionId);
                    var tifLoteId = ToIntN(p.LoteId);

                    if (!tifProduccionId.HasValue || !tifLoteId.HasValue)
                        continue;

                    // =========================
                    // 2) SolicitudProduccion en TIF
                    //    En tu modelo real:
                    //    Lote.LoteId -> SolicitudProduccion.SolicitudProduccionId
                    // =========================
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
                        commandTimeout: CMD);

                    if (sp == null)
                        throw new Exception($"No existe SolicitudProduccion {tifLoteId.Value} en TIF para la etiqueta {codigoEtiqueta}.");

                    var existeSpP1 = await cnP1.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM dbo.SolicitudProduccion
                WHERE SolicitudProduccionId = @SolicitudProduccionId;",
                        new { SolicitudProduccionId = tifLoteId.Value },
                        transaction: tx,
                        commandTimeout: CMD);

                    if (existeSpP1 == 0)
                    {
                        await cnP1.ExecuteAsync(
                            "SET IDENTITY_INSERT dbo.SolicitudProduccion ON;",
                            transaction: tx,
                            commandTimeout: CMD);

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
                            commandTimeout: CMD);

                        await cnP1.ExecuteAsync(
                            "SET IDENTITY_INSERT dbo.SolicitudProduccion OFF;",
                            transaction: tx,
                            commandTimeout: CMD);
                    }

                    // =========================
                    // 3) Lote en TIF
                    //    LoteId NO es identity y debe coincidir con SolicitudProduccionId
                    // =========================
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
                        commandTimeout: CMD);

                    if (lote == null)
                        throw new Exception($"No existe Lote {tifLoteId.Value} en TIF para la etiqueta {codigoEtiqueta}.");

                    var existeLoteP1 = await cnP1.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM dbo.Lote
                WHERE LoteId = @LoteId;",
                        new { LoteId = tifLoteId.Value },
                        transaction: tx,
                        commandTimeout: CMD);

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
                            commandTimeout: CMD);
                    }

                    // =========================
                    // 4) Produccion en P1
                    //    ProduccionId SÍ es identity, por eso no se manda explícito
                    // =========================
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
                        commandTimeout: CMD);

                    // =========================
                    // 5) PesoProducto
                    // =========================
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
                        commandTimeout: CMD)).ToList();

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
                            commandTimeout: CMD);
                    }

                    // =========================
                    // 6) Costeo
                    // =========================
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
                        commandTimeout: CMD)).ToList();

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
                            commandTimeout: CMD);
                    }

                    // =========================
                    // 7) ProduccionCosteo
                    // =========================
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
                        commandTimeout: CMD)).ToList();

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
                            commandTimeout: CMD);
                    }

                    // =========================
                    // 8) Tarima / TarimaDetalle
                    //    Aquí sigo usando TarimaId explícito.
                    //    Si luego Tarima truena por identity, lo ajustamos igual que Produccion.
                    // =========================
                    var tarimas = (await cnTif.QueryAsync(@"
                SELECT
                    TD.TarimaId,
                    TD.ProduccionId,
                    TD.FechaHora,
                    TD.FechaHoraServer,
                    T.Nombre,
                    T.Estatus
                FROM dbo.TarimaDetalle TD
                INNER JOIN dbo.Tarima T
                    ON T.TarimaId = TD.TarimaId
                WHERE TD.ProduccionId = @ProduccionId;",
                        new { ProduccionId = tifProduccionId.Value },
                        commandTimeout: CMD)).ToList();

                    foreach (var x in tarimas)
                    {
                        var tarimaId = ToIntN(x.TarimaId);
                        if (!tarimaId.HasValue)
                            continue;

                        var existeTarimaP1 = await cnP1.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM dbo.Tarima
                    WHERE TarimaId = @TarimaId;",
                            new { TarimaId = tarimaId.Value },
                            transaction: tx,
                            commandTimeout: CMD);

                        if (existeTarimaP1 == 0)
                        {
                            await cnP1.ExecuteAsync(@"
                        INSERT INTO dbo.Tarima
                        (
                            TarimaId,
                            Nombre,
                            Estatus,
                            FechaHora,
                            FechaHoraServer
                        )
                        VALUES
                        (
                            @TarimaId,
                            @Nombre,
                            @Estatus,
                            @FechaHora,
                            @FechaHoraServer
                        );",
                                new
                                {
                                    TarimaId = tarimaId.Value,
                                    Nombre = ToStr(x.Nombre),
                                    Estatus = ToBoolN(x.Estatus),
                                    FechaHora = ToDateN(x.FechaHora),
                                    FechaHoraServer = ToDateN(x.FechaHoraServer)
                                },
                                transaction: tx,
                                commandTimeout: CMD);
                        }

                        var existeTarimaDetalle = await cnP1.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM dbo.TarimaDetalle
                    WHERE TarimaId = @TarimaId
                      AND ProduccionId = @ProduccionId;",
                            new
                            {
                                TarimaId = tarimaId.Value,
                                ProduccionId = nuevaProduccionId
                            },
                            transaction: tx,
                            commandTimeout: CMD);

                        if (existeTarimaDetalle == 0)
                        {
                            await cnP1.ExecuteAsync(@"
                        INSERT INTO dbo.TarimaDetalle
                        (
                            TarimaId,
                            ProduccionId,
                            FechaHora,
                            FechaHoraServer
                        )
                        VALUES
                        (
                            @TarimaId,
                            @ProduccionId,
                            @FechaHora,
                            @FechaHoraServer
                        );",
                                new
                                {
                                    TarimaId = tarimaId.Value,
                                    ProduccionId = nuevaProduccionId,
                                    FechaHora = ToDateN(x.FechaHora),
                                    FechaHoraServer = ToDateN(x.FechaHoraServer)
                                },
                                transaction: tx,
                                commandTimeout: CMD);
                        }
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
                        commandTimeout: CMD);
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