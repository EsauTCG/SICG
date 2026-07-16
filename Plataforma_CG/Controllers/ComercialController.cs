using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using Plataforma_CG.Data; // Para AppDbContext
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using Plataforma_CG.Services;
using Plataforma_CG.ViewModels;
using System.Data;
using System.Globalization; // Para Pedido y PedidoProducto
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImageMagick;
using System.Globalization;
using System.Text.RegularExpressions;
using Tesseract;
using UglyToad.PdfPig;


namespace Plataforma_CG.Controllers
{


    public class ComercialController : Controller
    {

        private readonly IPresupuestoSettingsService _settings;
        // ⚡ Campos privados
        private readonly SeriesSettings _config;
        private readonly SapServiceLayerClient _sap;
        // 👇 nuevos
        private readonly IConfiguration _configuration;
        private readonly ISapInvoiceSyncService _sync;
        private readonly AppDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly IHttpClientFactory httpClientFactory;      // 👈 NECESARIO
        private readonly Data.AppDbContextUsuarios _uDb;       // tiene Usuarios / UsuariosAD
                                                               // tu servicio SAP
        private readonly IServiceScopeFactory _scopeFactory;

        // Si tu flujo crea otro doc (DeliveryNotes o Invoices), ponlo primero aquí:
        private static readonly string[] ENTIDADES_SAP = new[] { "Orders" };

        private readonly ILogger<ComercialController> _logger;
        private readonly PresupuestoAdminService _presAdmin;
        private readonly ISapDireccionesSyncService _direccionesSync; // 👈 AQUÍ


        // En la clase del Controller (o en un servicio aparte)
        private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

        private static DateTime _cacheExpira = DateTime.MinValue;
        private static string _cachePresupuestos = null;
        private static string _cacheInvIni = null;
        private static string _cacheInvAct = null;
        private static string _cacheVentaReal = null;

        // Tiempo de vida del caché — ajusta según qué tan "en vivo" necesitas los datos
        private static readonly TimeSpan CACHE_TTL = TimeSpan.FromMinutes(5);

        private readonly IMemoryCache _cache;

        // ── Semáforos y constantes (campos de clase) ───────────────────
        private static readonly SemaphoreSlim _semReporte = new(1, 1);
        private static readonly SemaphoreSlim _semInvIni = new(1, 1);
        private static readonly SemaphoreSlim _semInvAct = new(1, 1);
        private static readonly SemaphoreSlim _semVentaReal = new(1, 1);

        private static readonly TimeSpan TTL_REPORTE = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan TTL_INV_INI = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan TTL_INV_ACT = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan TTL_VENTA_REAL = TimeSpan.FromMinutes(5);

        private const string KEY_REPORTE = "cache_presupuestos_mes";
        private const string KEY_INV_INI = "cache_inv_inicial_ayer";
        private const string KEY_INV_ACT = "cache_inv_actual";
        private const string KEY_VENTA_REAL = "cache_venta_real_resumen";

        private async Task<(bool puedeLeer, bool puedeEscribir, bool puedeEliminar)> ObtenerPermisoModuloAsync(string claveModulo)
        {
            var login = (User?.Identity?.Name ?? "").Trim();

            var permiso = await (
                from u in _context.UsuarioSQL
                join p in _context.Perfiles on u.PerfilId equals p.Id
                join ppm in _context.PerfilPermisoModulo on p.Id equals ppm.PerfilId
                join m in _context.ModulosSistema on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == claveModulo
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
                return (false, false, false);

            return (permiso.PuedeLeer, permiso.PuedeEscribir, permiso.PuedeEliminar);
        }


        // 👇 Recibe también el DbContext
        public ComercialController(
            AppDbContext context,
            IOptions<SeriesSettings> configOptions,
             IConfiguration configuration,                 // 👈 nuevo
             ISapInvoiceSyncService sync,                  // 👈 nuevo                          
            SapServiceLayerClient sap,
            IHttpClientFactory httpClientFactory,
            ILogger<ComercialController> logger,
            IServiceScopeFactory scopeFactory,
            IPresupuestoSettingsService settings,
            PresupuestoAdminService presAdmin,
             ISapDireccionesSyncService direccionesSync,   // 👈 AQUÍ
             IMemoryCache cache



            )

        {
            _context = context;  // 👈 inicializa aquí
            _config = configOptions?.Value ?? new SeriesSettings();
            _sap = sap ?? throw new ArgumentNullException(nameof(sap));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration)); // 👈 nuevo
            _sync = sync ?? throw new ArgumentNullException(nameof(sync));
            _logger = logger;
            _httpClient = (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory))).CreateClient("SapServiceLayer");
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory)); // 👈 GUÁRDALO
            _settings = settings;
            _presAdmin = presAdmin;
            _direccionesSync = direccionesSync; // 👈 AQUÍ
            _cache = cache;

        }

        // ── Helper caché  ◄─── AQUÍ, antes de los endpoints ──────────
        private async Task<string> GetOrSetJsonAsync(
            string cacheKey,
            SemaphoreSlim semaphore,
            TimeSpan ttl,
            Func<Task<string>> factory)
        {
            if (_cache.TryGetValue(cacheKey, out string cached))
                return cached;

            await semaphore.WaitAsync();
            try
            {
                if (_cache.TryGetValue(cacheKey, out cached))
                    return cached;

                var result = await factory();

                _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl,
                    Size = 1
                });

                return result;
            }
            finally
            {
                semaphore.Release();
            }
        }


        private List<SelectListItem> GetAlmacenes()
        {
            // Lee la sección; si no existe, usa lista vacía
            var lista = _configuration.GetSection("Warehouses")
                                      .Get<List<WarehouseOption>>() ?? new List<WarehouseOption>();

            // Filtra nulos/vacíos para evitar sorpresas
            return lista
                .Where(w => !string.IsNullOrWhiteSpace(w.Id) && !string.IsNullOrWhiteSpace(w.Name))
                .Select(w => new SelectListItem { Value = w.Id, Text = w.Name })
                .ToList();
        }

        private async Task<List<string>> ObtenerIdsAlmacenesPermitidosParaUsusarioAcualAsync()
        {
            if (UsuarioPuedeVerTodosLosAlmacenes())
            {
                return GetAlmacenes()
                    .Select(a => a.Value)
                    .ToList();
            }

            var login = (User?.Identity?.Name ?? "").Trim();

            var usuario = await _context.UsuarioSQL
                    .FirstOrDefaultAsync(u =>
                    u.Usuario == login ||
                    u.Nombre == login);

            if (usuario == null)
            {
                return new List<string>();
            }

            return JsonSerializer.Deserialize<List<string>>(
                usuario.AlmacenesPermitidos ?? "[]"
                ) ?? new List<string>();
        }

        private bool UsuarioPuedeVerTodosLosAlmacenes()
        {
            return User.IsInRole("Administrador") || User.IsInRole("Sistemas");
        }


        public async Task<IActionResult> comercial(CancellationToken ct)
        {
            var clientes = new List<ClienteViewModel>();

            var seriesList = await ObtenerSeriesPermitidasActualAsync(ct);

            var presentacionesList = _config.Presentaciones?
                .Distinct()
                .Select(p => new SelectListItem { Value = p, Text = p })
                .ToList() ?? new List<SelectListItem>();

            int ultimoId = await _context.OrdenVenta.AnyAsync(ct)
                ? await _context.OrdenVenta.MaxAsync(o => o.Id, ct)
                : 0;

            string consecutivo = $"OV-{(ultimoId + 1).ToString("D8")}";

            var model = new PedidoViewModel
            {
                Consecutivo = consecutivo,
                Serie = "",
                FechaEntrega = DateTime.Today,
                FechaEmbarque = DateTime.Today.AddDays(2),
                Presentacion = "",
                Cliente = clientes.FirstOrDefault()?.CardCode ?? "",
                Clientes = clientes,
                Productos = new List<PedidoProductoViewModel>(),
                Series = seriesList,
                Presentaciones = presentacionesList
            };

            return View("~/Views/Comercial/OrdenVenta.cshtml", model);
        }


        // Buscar clientes para autocomplete
        [HttpGet]
        public async Task<IActionResult> BuscarClientes(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(new List<object>());

            var clientes = await _sap.ObtenerTodosClientesAsync();

            var resultado = clientes
                .Where(c => c.CardName.Contains(term, StringComparison.OrdinalIgnoreCase)
                         || c.CardCode.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(c => new
                {
                    id = c.CardCode,
                    //text = c.CardName
                    text = c.CardName + " " + (c.CardFName ?? "") // ← Concatenación
                })
                .ToList();

            return Json(resultado);
        }

        // Autocomplete avanzado de clientes
        [HttpGet]
        public async Task<IActionResult> BuscarClientesAutocomplete(string term)
        {
            var clientes = await _sap.BuscarClientesPorNombreAsync(term);

            // Preparar tareas para obtener nombres de vendedores
            var tasks = clientes.Select(async c =>
            {
                string nombreVendedor = "No asignado";

                if (!string.IsNullOrEmpty(c.Vendedor))
                {
                    // Obtener nombre del vendedor
                    if (int.TryParse(c.Vendedor, out int vendedorId))
                    {
                        nombreVendedor = await _sap.ObtenerNombreVendedorAsync(vendedorId);
                    }
                }

                return new
                {
                    label = c.CardName + " " + (c.CardFName ?? ""),
                    value = c.CardCode,
                    credito = c.CreditLimit,
                    saldo = c.CurrentAccountBalance,
                    sumpedidos = c.TotalPendiente,
                    saldoVencido = c.SaldoVencido,
                    vendedor = nombreVendedor
                };
            });

            var result = await Task.WhenAll(tasks);

            return Json(result);
        }


        // Autocomplete de productos
        [HttpGet]
        public async Task<IActionResult> BuscarProductosAutocomplete(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(new List<object>());

            var productos = await _sap.BuscarProductosAsync(term);

            var resultado = productos.Select(p => new
            {
                label = $"{p.ItemCode} ({p.ItemName})",
                value = p.ItemCode
            });

            return Json(resultado);
        }


        [HttpGet]
        public async Task<IActionResult> ObtenerPropiedadesCliente(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return Json(new List<object>());

            var props = await _sap.ObtenerPropiedadesClienteAsync(cardCode);

            var resultado = props.Select(p => new
            {
                nombre = p.Nombre,
                valor = p.Valor
            });

            return Json(resultado);
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerVendedorCliente(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return BadRequest("Se requiere un CardCode");

            try
            {
                // 1️⃣ Obtener ID del vendedor
                var vendedorId = await _sap.ObtenerVendedorClienteAsync(cardCode); // int?

                // 2️⃣ Obtener nombre solo si hay ID
                string vendedorNombre = "No asignado";
                if (vendedorId.HasValue && vendedorId.Value > 0)
                {
                    vendedorNombre = await _sap.ObtenerNombreVendedorAsync(vendedorId.Value); // string
                }

                // 3️⃣ Devolver objeto JSON con ID y nombre
                return Ok(new
                {
                    CardCode = cardCode,
                    VendedorId = vendedorId,
                    VendedorNombre = vendedorNombre
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }



        // Acción para obtener las direcciones de un cliente
        [HttpGet]
        public async Task<IActionResult> ObtenerDireccionesCliente(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return BadRequest("El código de cliente es obligatorio.");

            var direcciones = await _sap.ObtenerDireccionesClienteAsync(cardCode);

            var listaDirecciones = direcciones
                .Select(d => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = d,
                    Text = d
                }).ToList();

            return Json(listaDirecciones);
        }


        ////======================================
        //// TRAE PRECIO POR CADA ARTICULO DEL CLIENTE DESDE SAP COMENTADO POR EL MOMENTO
        ////======================================
        //[HttpGet]
        //public async Task<IActionResult> ObtenerPrecioArticuloCliente(string cardCode, string itemCode)
        //{
        //    if (string.IsNullOrWhiteSpace(cardCode) || string.IsNullOrWhiteSpace(itemCode))
        //        return BadRequest("Cliente y artículo son requeridos.");

        //    try
        //    {
        //        var producto = await _sap.ObtenerPrecioArticuloPorClienteAsync(cardCode, itemCode);

        //        if (producto == null)
        //            return NotFound("No se encontró el artículo o el cliente.");

        //        // Devuelve JSON con ItemCode, ItemName, Precio y KilosCaja
        //        return Json(producto);
        //    }
        //    catch (HttpRequestException ex)
        //    {
        //        return StatusCode(500, $"Error al consultar SAP: {ex.Message}");
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, $"Error inesperado: {ex.Message}");
        //    }
        //}

        //======================================
        // TRAE PRECIO POR CADA ARTICULO DEL CLIENTE DESDE DB LOCAL
        //======================================

        // GET: /Comercial/ObtenerPrecioArticuloCliente?cardCode=C0001&itemCode=SKU123

        [HttpGet("Comercial/ObtenerPrecioArticuloCliente")]
        public async Task<IActionResult> ObtenerPrecioArticuloCliente(string cardCode, string itemCode)
        {
            if (string.IsNullOrWhiteSpace(cardCode) || string.IsNullOrWhiteSpace(itemCode))
                return BadRequest(new { error = "Parámetros inválidos" });

            var cli = cardCode.Trim().ToUpper();
            var sku = itemCode.Trim().ToUpper();

            // 1) Artículo (nombre y kilos/caja) desde local
            var art = await _context.ArticuloSap.AsNoTracking()
                .Where(a => a.ProductoCodigo == sku)
                .Select(a => new
                {
                    a.ProductoCodigo,
                    ItemName = a.ProductoNombre,
                    KilosCaja = (decimal?)a.U_KilosCaja  // ajusta el nombre real del campo si difiere
                })
                .FirstOrDefaultAsync();

            // 2) Último precio por cliente+producto en local
            var precioRow = await _context.CatalogoPrecioSap.AsNoTracking()
                .Where(x => x.Cliente == cli && x.ProductoCodigo == sku)
                .OrderByDescending(x => x.FechaModificacion)
                .Select(x => new { x.Precio })
                .FirstOrDefaultAsync();

            var precio = precioRow?.Precio ?? 0m;

            // Respuesta compatible con tu JS actual
            return Json(new
            {
                itemCode = sku,
                itemName = art?.ItemName ?? "",
                precio = precio,
                kilosCaja = art?.KilosCaja ?? 1m
            });
        }

        //======================================
        // GUARDAR PEDIDO (simple, fail-open sin SAP)
        //======================================
        //[HttpPost]
        //public async Task<IActionResult> GuardarPedido(PedidoViewModel model, string accion)
        //{
        //    if (!model.FechaEntrega.HasValue)
        //    {
        //        TempData["Error"] = "Debes seleccionar una fecha de entrega válida.";
        //        return View("OrdenVenta", model);
        //    }

        //    int mes = model.FechaEntrega.Value.Month;
        //    int anio = model.FechaEntrega.Value.Year;

        //    // ✅ MODO presupuesto (se guarda en OV)
        //    var modoPresupuesto = GetModoPresupuestoActual();   // "VENDEDOR" | "CLIENTE"
        //    bool usarVendedor = (modoPresupuesto == "VENDEDOR");

        //    string clienteUp = (model.Cliente ?? "").Trim().ToUpperInvariant();

        //    // Rango del mes (recomendado)
        //    var inicioMes = new DateTime(anio, mes, 1);
        //    var finMesExcl = inicioMes.AddMonths(1);

        //    // Totales del pedido
        //    decimal totalPedido = model.Productos?.Sum(p => p.Peso * p.Precio) ?? 0m;

        //    // Info crédito (estos valores suelen venir de SAP)
        //    decimal credito = model.Credito;
        //    decimal saldo = model.Saldo;
        //    decimal otrosPedidos = model.OtrosPedidos;
        //    decimal totalDisponible = credito - saldo - otrosPedidos;

        //    const decimal TOL = 0.01m;

        //    bool requiereAutorizacionPrecio = false;
        //    bool requiereAutorizacionPresupuesto = false;
        //    bool sapDisponible = true;

        //    // =========================
        //    // SKUs del pedido
        //    // =========================
        //    var skusPedido = (model.Productos ?? new List<PedidoProductoViewModel>())
        //                     .Select(p => (p.ProductoCodigo ?? "").Trim().ToUpperInvariant())
        //                     .Where(c => !string.IsNullOrWhiteSpace(c))
        //                     .Distinct()
        //                     .ToList();

        //    // =========================
        //    // Datos cliente / canal / vendedorId (desde ClienteSap)
        //    // =========================
        //    var cliSap = await _context.ClienteSap
        //        .AsNoTracking()
        //        .Where(c => (c.Cliente ?? "").ToUpper() == clienteUp)
        //        .Select(c => new
        //        {
        //            Canal = c.U_CANAL,
        //            VendedorId = (int?)c.VendedorId
        //        })
        //        .FirstOrDefaultAsync();

        //    string canalClienteUp = (cliSap?.Canal ?? "").Trim().ToUpperInvariant();
        //    bool esCanalCedis = canalClienteUp.StartsWith("CEDIS");

        //    int? vendedorId = (cliSap?.VendedorId.HasValue == true && cliSap.VendedorId.Value > 0)
        //        ? cliSap.VendedorId.Value
        //        : (int?)null;

        //    bool tieneVendedorId = vendedorId.HasValue;

        //    // Sucursal de la serie (para regla CEDIS/MATRIZ)
        //    var serieInfo = await _context.Series
        //        .AsNoTracking()
        //        .Where(s => s.NombreSerie == model.Serie)
        //        .Select(s => new { s.Sucursal })
        //        .FirstOrDefaultAsync();

        //    string sucursalSerieUp = (serieInfo?.Sucursal ?? "").Trim().ToUpperInvariant();
        //    bool esSerieMatriz = (sucursalSerieUp == "MATRIZ");

        //    // =========================
        //    // Presupuesto CLIENTE (Presupuestos)
        //    // =========================
        //    var normalDict = await _context.Presupuestos
        //        .AsNoTracking()
        //        .Where(pr => (pr.ClienteId ?? "").ToUpper() == clienteUp
        //                  && pr.Mes == mes
        //                  && pr.Año == anio
        //                  && skusPedido.Contains((pr.ProductoCodigo ?? "").ToUpper()))
        //        .GroupBy(pr => (pr.ProductoCodigo ?? "").ToUpper())
        //        .ToDictionaryAsync(
        //            g => g.Key,
        //            g => g.Sum(x => (decimal?)x.PresupuestoAsignado ?? 0m)
        //        );

        //    // =========================
        //    // Presupuesto CEDIS (PresupuestoCedis)
        //    // =========================
        //    var cedisDict = new Dictionary<string, decimal>();
        //    if (!string.IsNullOrWhiteSpace(canalClienteUp))
        //    {
        //        cedisDict = await _context.PresupuestoCedis
        //            .AsNoTracking()
        //            .Where(pc => (pc.Canal ?? "").ToUpper() == canalClienteUp
        //                      && pc.Mes == mes
        //                      && pc.Anio == anio
        //                      && skusPedido.Contains((pc.ProductoCodigo ?? "").ToUpper()))
        //            .GroupBy(pc => (pc.ProductoCodigo ?? "").ToUpper())
        //            .ToDictionaryAsync(
        //                g => g.Key,
        //                g => g.Sum(x => (decimal?)x.PresupuestoAsignado ?? 0m)
        //            );
        //    }

        //    // =========================
        //    // Presupuesto VENDEDOR (PresupuestoVendedor)
        //    // (por VendedorId que viene de ClienteSap)
        //    // =========================
        //    var vendDict = new Dictionary<string, decimal>();
        //    if (usarVendedor)
        //    {
        //        if (tieneVendedorId)
        //        {
        //            int vid = vendedorId!.Value;

        //            vendDict = await _context.PresupuestoVendedor
        //                .AsNoTracking()
        //                .Where(pv => pv.VendedorId == vid
        //                          && pv.Mes == mes
        //                          && pv.Anio == anio
        //                          && skusPedido.Contains((pv.ProductoCodigo ?? "").ToUpper()))
        //                .GroupBy(pv => (pv.ProductoCodigo ?? "").ToUpper())
        //                .ToDictionaryAsync(
        //                    g => g.Key,
        //                    g => g.Sum(x => (decimal?)x.PresupuestoAsignado ?? 0m)
        //                );
        //        }
        //        else
        //        {
        //            // Estás en modo vendedor pero no pudimos resolver VendedorId
        //            // => lo más seguro es mandar a autorización
        //            requiereAutorizacionPresupuesto = true;
        //        }
        //    }

        //    try
        //    {
        //        foreach (var p in model.Productos ?? new List<PedidoProductoViewModel>())
        //        {
        //            var sku = (p.ProductoCodigo ?? "").Trim().ToUpperInvariant();
        //            if (string.IsNullOrWhiteSpace(sku)) continue;

        //            // ===== Precio lista SAP =====
        //            var productoSap = await _sap.ObtenerPrecioArticuloPorClienteAsync(model.Cliente, sku);
        //            if (productoSap != null)
        //            {
        //                var precioOV = decimal.Round(p.Precio, 2, MidpointRounding.AwayFromZero);
        //                var precioLista = decimal.Round(productoSap.Precio, 2, MidpointRounding.AwayFromZero);
        //                if (precioOV < precioLista - TOL)
        //                    requiereAutorizacionPrecio = true;
        //            }

        //            // Regla: si cliente es CEDIS y serie NO es MATRIZ => NO validar presupuesto
        //            bool aplicarValidacionPresupuesto = true;
        //            if (esCanalCedis && !esSerieMatriz)
        //            {
        //                aplicarValidacionPresupuesto = false;
        //            }
        //            if (!aplicarValidacionPresupuesto) continue;

        //            // =============================
        //            // Presupuesto según prioridad:
        //            // 1) CEDIS (si existe)
        //            // 2) si modo VENDEDOR -> VENDEDOR
        //            // 3) si modo CLIENTE  -> CLIENTE
        //            // =============================
        //            bool usaCedis = cedisDict.TryGetValue(sku, out var presupuestoCedis);

        //            decimal presupuestoAsignado = 0m;
        //            string fuente = "";

        //            if (usaCedis)
        //            {
        //                presupuestoAsignado = presupuestoCedis;
        //                fuente = "CEDIS";
        //            }
        //            else if (usarVendedor)
        //            {
        //                if (vendDict.TryGetValue(sku, out var presupuestoVend))
        //                {
        //                    presupuestoAsignado = presupuestoVend;
        //                    fuente = "VENDEDOR";
        //                }
        //                else
        //                {
        //                    presupuestoAsignado = 0m;
        //                    fuente = "SIN PRESUPUESTO VENDEDOR";
        //                }
        //            }
        //            else
        //            {
        //                if (normalDict.TryGetValue(sku, out var presupuestoNormal))
        //                {
        //                    presupuestoAsignado = presupuestoNormal;
        //                    fuente = "CLIENTE";
        //                }
        //                else
        //                {
        //                    presupuestoAsignado = 0m;
        //                    fuente = "SIN PRESUPUESTO CLIENTE";
        //                }
        //            }

        //            if (presupuestoAsignado <= 0m)
        //            {
        //                requiereAutorizacionPresupuesto = true;
        //                continue;
        //            }

        //            // =============================
        //            // Kilos acumulados del mes según fuente
        //            // =============================
        //            decimal kilosAcumulados = 0m;

        //            if (fuente == "CEDIS")
        //            {
        //                // CEDIS: consumo global por SKU del canal en el mes
        //                kilosAcumulados = await (
        //                    from o in _context.OrdenVenta.AsNoTracking()
        //                    join cli in _context.ClienteSap.AsNoTracking() on o.Cliente equals cli.Cliente
        //                    join op in _context.OrdenVentaProducto.AsNoTracking() on o.Id equals op.PedidoId
        //                    where o.Estatus != 0
        //                       && o.FechaEntrega >= inicioMes && o.FechaEntrega < finMesExcl
        //                       && (cli.U_CANAL ?? "").ToUpper() == canalClienteUp
        //                       && (op.Eliminado == null || op.Eliminado == false)
        //                       && ((op.ProductoCodigo ?? "").ToUpper() == sku)
        //                    select (decimal?)op.Peso
        //                ).SumAsync() ?? 0m;
        //            }
        //            else if (fuente == "VENDEDOR")
        //            {
        //                // VENDEDOR: consumo por VendedorId (join ClienteSap)
        //                if (!tieneVendedorId)
        //                {
        //                    requiereAutorizacionPresupuesto = true;
        //                    continue;
        //                }

        //                int vid = vendedorId!.Value;

        //                kilosAcumulados = await (
        //                    from o in _context.OrdenVenta.AsNoTracking()
        //                    join cli in _context.ClienteSap.AsNoTracking() on o.Cliente equals cli.Cliente
        //                    join op in _context.OrdenVentaProducto.AsNoTracking() on o.Id equals op.PedidoId
        //                    where o.Estatus != 0
        //                       && o.FechaEntrega >= inicioMes && o.FechaEntrega < finMesExcl
        //                       && cli.VendedorId == vid
        //                       && (op.Eliminado == null || op.Eliminado == false)
        //                       && ((op.ProductoCodigo ?? "").ToUpper() == sku)
        //                    select (decimal?)op.Peso
        //                ).SumAsync() ?? 0m;
        //            }
        //            else
        //            {
        //                // CLIENTE: consumo por cliente
        //                kilosAcumulados = await (
        //                    from o in _context.OrdenVenta.AsNoTracking()
        //                    join op in _context.OrdenVentaProducto.AsNoTracking() on o.Id equals op.PedidoId
        //                    where o.Estatus != 0
        //                       && o.FechaEntrega >= inicioMes && o.FechaEntrega < finMesExcl
        //                       && ((o.Cliente ?? "").ToUpper() == clienteUp)
        //                       && (op.Eliminado == null || op.Eliminado == false)
        //                       && ((op.ProductoCodigo ?? "").ToUpper() == sku)
        //                    select (decimal?)op.Peso
        //                ).SumAsync() ?? 0m;
        //            }

        //            if ((kilosAcumulados + p.Peso) > presupuestoAsignado)
        //                requiereAutorizacionPresupuesto = true;
        //        }
        //    }
        //    catch
        //    {
        //        // Fail-open (incluye fallas de SAP)
        //        sapDisponible = false;
        //    }

        //    bool requiereAutorizacionCredito = sapDisponible && (totalPedido > totalDisponible);
        //    int estatusPedido = (requiereAutorizacionPrecio || requiereAutorizacionPresupuesto || requiereAutorizacionCredito) ? 2 : 1;

        //    // Propiedades/documentación del cliente (si SAP disponible)
        //    string documentacionConcatenada = string.Empty;
        //    if (sapDisponible)
        //    {
        //        try
        //        {
        //            var props = await _sap.ObtenerPropiedadesClienteAsync(model.Cliente);
        //            documentacionConcatenada = string.Join(" | ",
        //                props.Where(p => p.Valor != null && p.Valor.ToString().ToLower() == "true")
        //                     .Select(p => p.Nombre));
        //        }
        //        catch { sapDisponible = false; }
        //    }

        //    // Consecutivo temporal único
        //    string consecutivoTemporal = $"TMP-{Guid.NewGuid():N}";
        //    await using var tx = await _context.Database.BeginTransactionAsync();

        //    // 1) Cabecera de OV
        //    var pedido = new OrdenVenta
        //    {
        //        Consecutivo = consecutivoTemporal,
        //        Serie = model.Serie,
        //        FechaEntrega = model.FechaEntrega.Value,
        //        FechaEmbarque = model.FechaEmbarque,
        //        HoraEmbarque = model.HoraEmbarque,
        //        Cliente = model.Cliente,
        //        Vendedor = model.Vendedor, // tu campo actual (nombre)
        //        VendedorId = vendedorId,
        //        Ruta = string.IsNullOrWhiteSpace(model.Ruta) ? "Sin Dirección" : model.Ruta,
        //        Presentacion = model.Presentacion,
        //        Observacion = model.Observacion,
        //        // ✅ guarda el modo real del pedido
        //        ModoPresupuesto = modoPresupuesto,
        //        // Si no hay SAP, deja en 0 (fail-open)
        //        Saldo = sapDisponible ? model.Saldo : 0m,
        //        OtrosPedidos = sapDisponible ? model.OtrosPedidos : 0m,
        //        Credito = sapDisponible ? model.Credito : 0m,

        //        Estatus = estatusPedido,
        //        FechaRegistro = DateTime.Now,
        //        Documentacion = sapDisponible ? documentacionConcatenada : string.Empty,

        //        AutorizacionCredito = !requiereAutorizacionCredito,
        //        AutorizacionPresupuesto = !requiereAutorizacionPresupuesto,
        //        AutorizacionPrecio = !requiereAutorizacionPrecio
        //    };

        //    _context.OrdenVenta.Add(pedido);
        //    await _context.SaveChangesAsync(); // ya tenemos pedido.Id

        //    // 2) Consecutivo definitivo
        //    pedido.Consecutivo = $"OV-{pedido.Id:D8}";
        //    const int maxIntentos = 2;
        //    for (int intento = 1; intento <= maxIntentos; intento++)
        //    {
        //        try
        //        {
        //            _context.OrdenVenta.Update(pedido);
        //            await _context.SaveChangesAsync();
        //            break;
        //        }
        //        catch (DbUpdateException ex) when (EsDuplicadoConsecutivo(ex))
        //        {
        //            pedido.Consecutivo = $"OV-{pedido.Id:D8}-{intento}";
        //            if (intento == maxIntentos) throw;
        //        }
        //    }

        //    // 3) Detalle (líneas)
        //    if (model.Productos != null && model.Productos.Any())
        //    {
        //        foreach (var p in model.Productos)
        //        {
        //            var det = new OrdenVentaProducto
        //            {
        //                PedidoId = pedido.Id,
        //                ProductoCodigo = p.ProductoCodigo,
        //                ProductoNombre = p.ProductoNombre,
        //                Peso = p.Peso,
        //                Precio = p.Precio,
        //                Cajas = p.Cajas,

        //            };
        //            _context.OrdenVentaProducto.Add(det);
        //        }
        //        await _context.SaveChangesAsync();
        //    }

        //    await tx.CommitAsync();

        //    TempData["Success"] = sapDisponible
        //        ? $"Pedido guardado. Consecutivo: {pedido.Consecutivo}"
        //        : $"Pedido guardado (sin datos de SAP). Consecutivo: {pedido.Consecutivo}";

        //    if (accion == "salir") return RedirectToAction("Inicio", "Home");
        //    if (accion == "nuevo") return RedirectToAction("OrdenVenta", "Comercial");
        //    return RedirectToAction("Index", "Pedidos");
        //}

        private async Task<IReadOnlyList<PresupuestoConsumoDto>> ObtenerPresupuestoDetalleAsync(
            string sucursal,              // si luego lo ocupas, lo metemos (ahorita queda igual que el reporte: MATRIZ)
            string sku,
            DateTime fechaSolicitud,
            int? vendedorId,
            bool esCanalCedis,
            string? canalCliente,
            CancellationToken ct)
        {
            const string sql = @"
DECLARE 
    @Mes   INT = MONTH(@FechaSolicitud),
    @Anio  INT = YEAR(@FechaSolicitud),
    @SkuN  NVARCHAR(50) = UPPER(LTRIM(RTRIM(@Sku))),
    @VendedorId INT = @VendedorIdParam,
    @EsCanalCedis INT = @EsCanalCedisParam,
    @Canal NVARCHAR(100) = NULL;

IF (@EsCanalCedis = 1)
    SET @Canal = UPPER(LTRIM(RTRIM(@CanalParam)));

WITH
-- =====================================================
-- CATÁLOGOS
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
-- PRESUPUESTO CLIENTE
-- =====================================================
presupuestos_normales AS (
    SELECT
        Cliente = UPPER(LTRIM(RTRIM(p.ClienteId))),
        SKU     = UPPER(LTRIM(RTRIM(p.ProductoCodigo))),
        Mes     = p.Mes,
        Anio    = p.Año,
        Presupuesto = SUM(p.Presupuesto)
    FROM dbo.Presupuestos p
    GROUP BY
        UPPER(LTRIM(RTRIM(p.ClienteId))),
        UPPER(LTRIM(RTRIM(p.ProductoCodigo))),
        p.Mes, p.Año
),

-- =====================================================
-- ORDEN DE VENTA (MATRIZ) - INCLUYE ESTATUS 5
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

-- =====================================================
-- OV CON SURTIDO (existe Subpedido -> SurtidoEncabezado)
-- =====================================================
ov_con_surtido AS (
    SELECT DISTINCT
        o.Id
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp         ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
),

-- =====================================================
-- PESO PEDIDO POR OV+SKU
-- =====================================================
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

-- =====================================================
-- SURTIDO VALIDADO POR OV+SKU
-- =====================================================
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

-- =====================================================
-- OV PENDIENTE POR OV+SKU (pedido - surtido validado)
-- + excepción estatus=5 con surtido relacionado => pendiente=0
-- =====================================================
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
-- CONSUMO CLIENTE (pendiente)
-- =====================================================
consumo_cliente AS (
    SELECT
        Cliente = UPPER(ovp.Cliente),
        SKU     = ovp.SKU,
        Mes     = MONTH(ovp.FechaDate),
        Anio    = YEAR(ovp.FechaDate),
        Kg      = SUM(ovp.KgPendiente)
    FROM ov_pendiente_sku ovp
    GROUP BY
        UPPER(ovp.Cliente),
        ovp.SKU,
        MONTH(ovp.FechaDate),
        YEAR(ovp.FechaDate)
),

-- =====================================================
-- UNION CLIENTE (presupuesto + solo consumo)
-- =====================================================
todo_normal AS (
    SELECT
        'CLIENTE' AS Origen,
        pn.Mes,
        pn.Anio,
        pn.Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        CAST(NULL AS INT) AS VendedorId,
        pn.SKU,
        pn.Presupuesto,
        ISNULL(cc.Kg,0) AS Kg
    FROM presupuestos_normales pn
    LEFT JOIN consumo_cliente cc
        ON cc.Cliente = pn.Cliente
       AND cc.SKU     = pn.SKU
       AND cc.Mes     = pn.Mes
       AND cc.Anio    = pn.Anio

    UNION ALL

    SELECT
        'CLIENTE',
        cc.Mes,
        cc.Anio,
        cc.Cliente,
        CAST(NULL AS NVARCHAR(100)),
        CAST(NULL AS INT),
        cc.SKU,
        0,
        cc.Kg
    FROM consumo_cliente cc
    LEFT JOIN presupuestos_normales pn
        ON pn.Cliente = cc.Cliente
       AND pn.SKU     = cc.SKU
       AND pn.Mes     = cc.Mes
       AND pn.Anio    = cc.Anio
    WHERE pn.Cliente IS NULL
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
-- SURTIDO POR TRANSFERENCIA (para pendiente)
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
-- CONSUMO CEDIS BASE (pendiente)
-- =====================================================
consumo_cedis_base AS (
    SELECT Canal, SKU, Mes, Anio, SUM(Kg) Kg
    FROM (
        -- OV CEDIS (pendiente)
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
    LEFT JOIN consumo_cedis cc
        ON cc.Canal = pc.Canal
       AND cc.SKU   = pc.SKU
       AND cc.Mes   = pc.Mes
       AND cc.Anio  = pc.Anio

    UNION ALL

    SELECT
        'CEDIS' AS Origen,
        cc.Mes,
        cc.Anio,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        cc.Canal,
        CAST(NULL AS INT) AS VendedorId,
        cc.SKU,
        CAST(0 AS DECIMAL(18,4)) AS Presupuesto,
        cc.Kg
    FROM consumo_cedis cc
    WHERE NOT EXISTS (
        SELECT 1
        FROM presupuestos_cedis pc
        WHERE pc.Canal = cc.Canal
          AND pc.SKU   = cc.SKU
          AND pc.Mes   = cc.Mes
          AND pc.Anio  = cc.Anio
    )
),

-- =====================================================
-- PRESUPUESTO VENDEDOR
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

consumo_vendedor_normal AS (
    SELECT
        ovp.VendedorId,
        SKU  = ovp.SKU,
        Mes  = MONTH(ovp.FechaDate),
        Anio = YEAR(ovp.FechaDate),
        Kg   = SUM(ovp.KgPendiente)
    FROM ov_pendiente_sku ovp
    JOIN dbo.ClienteSap c ON c.Cliente = ovp.Cliente
                         AND ISNULL(UPPER(LTRIM(RTRIM(c.U_CANAL))),'') NOT LIKE 'CEDIS%'
    WHERE ovp.VendedorId IS NOT NULL
    GROUP BY
        ovp.VendedorId,
        ovp.SKU,
        MONTH(ovp.FechaDate),
        YEAR(ovp.FechaDate)
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
       AND cv.SKU  = pv.SKU
       AND cv.Mes  = pv.Mes
       AND cv.Anio = pv.Anio
),

-- =====================================================
-- SURTIDO REAL: CLIENTE / CEDIS / VENDEDOR (solo VALIDADO)
-- =====================================================
surtido_cliente AS (
    SELECT
        Cliente = UPPER(o.Cliente),
        SKU     = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Mes     = MONTH(se.FechaValidacion),
        Anio    = YEAR(se.FechaValidacion),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp         ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    JOIN dbo.ClienteSap cli       ON cli.Cliente = o.Cliente
    WHERE o.Estatus <> 0
      AND se.FechaValidacion IS NOT NULL
      AND ISNULL(UPPER(cli.U_CANAL),'') NOT LIKE 'CEDIS%'
    GROUP BY
        UPPER(o.Cliente),
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        MONTH(se.FechaValidacion),
        YEAR(se.FechaValidacion)
),
surtido_ov_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(cli.U_CANAL))),
        SKU   = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Mes   = MONTH(se.FechaValidacion),
        Anio  = YEAR(se.FechaValidacion),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp         ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    JOIN dbo.ClienteSap cli       ON cli.Cliente = o.Cliente
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
surtido_vendedor_normal AS (
    SELECT
        cl.VendedorId,
        sc.SKU,
        sc.Mes,
        sc.Anio,
        KgSurtido = SUM(sc.KgSurtido)
    FROM surtido_cliente sc
    JOIN clientes cl ON cl.Cliente = sc.Cliente
    WHERE cl.VendedorId IS NOT NULL
    GROUP BY
        cl.VendedorId,
        sc.SKU,
        sc.Mes,
        sc.Anio
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
        SELECT * FROM surtido_vendedor_normal
        UNION ALL
        SELECT * FROM surtido_vendedor_desde_cedis
    ) x
    GROUP BY VendedorId, SKU, Mes, Anio
),
surtido_real AS (
    -- CLIENTE
    SELECT
        'CLIENTE' AS Origen,
        Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        CAST(NULL AS INT) AS VendedorId,
        SKU, Mes, Anio,
        SUM(KgSurtido) AS KgSurtido
    FROM surtido_cliente
    GROUP BY Cliente, SKU, Mes, Anio

    UNION ALL

    -- CEDIS
    SELECT
        'CEDIS',
        CAST(NULL AS NVARCHAR(50)),
        Canal,
        CAST(NULL AS INT),
        SKU, Mes, Anio,
        SUM(KgSurtido)
    FROM surtido_cedis_base
    GROUP BY Canal, SKU, Mes, Anio

    UNION ALL

    -- VENDEDOR
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
    t.Mes  AS MesConsulta,
    t.Anio AS AnioConsulta,
    t.VendedorId,
    t.Canal,
    t.Cliente AS ClienteCodigo,
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
    SELECT * FROM todo_cedis
    UNION ALL
    SELECT * FROM todo_vendedor
    UNION ALL
    SELECT * FROM todo_normal
) t
LEFT JOIN productos prd ON prd.SKU = t.SKU
LEFT JOIN surtido_real sr
    ON sr.Origen = t.Origen
   AND sr.SKU    = t.SKU
   AND sr.Mes    = t.Mes
   AND sr.Anio   = t.Anio
   AND (
        (t.Origen = 'CLIENTE'  AND sr.Cliente    = t.Cliente)
     OR (t.Origen = 'CEDIS'    AND sr.Canal      = t.Canal)
     OR (t.Origen = 'VENDEDOR' AND sr.VendedorId = t.VendedorId)
   )
WHERE
    t.Mes = @Mes
    AND t.Anio = @Anio
    AND t.SKU = @SkuN
    -- filtros opcionales según parámetros del método:
    AND (@EsCanalCedis = 0 OR (@EsCanalCedis = 1 AND t.Origen = 'CEDIS' AND t.Canal = @Canal))
    AND (@EsCanalCedis = 1 OR (@EsCanalCedis = 0)) -- no limita si no es cedis
    AND (@VendedorId IS NULL OR t.VendedorId = @VendedorId OR t.Origen <> 'VENDEDOR')
ORDER BY
    t.Origen,
    t.Anio,
    t.Mes,
    ISNULL(t.Cliente,''),
    ISNULL(t.Canal,''),
    t.SKU;
";

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
                        EsCanalCedisParam = esCanalCedis ? 1 : 0,
                        CanalParam = canalCliente ?? ""
                    },
                    cancellationToken: ct
                ))).ToList();

            return rows;
        }



        private async Task<decimal?> ObtenerDisponibleRealAsync(
            string sucursal,
            string sku,
            DateTime fechaSolicitud,
            int? vendedorId,
            bool esCanalCedis,
            string? canalCliente,
            CancellationToken ct)
        {
            var rows = await ObtenerPresupuestoDetalleAsync(
                sucursal, sku, fechaSolicitud, vendedorId, esCanalCedis, canalCliente, ct);

            if (rows == null || rows.Count == 0) return null;

            if (esCanalCedis)
            {
                var canal = (canalCliente ?? "").Trim().ToUpperInvariant();
                return rows.FirstOrDefault(r => r.Origen == "CEDIS" &&
                                                (r.Canal ?? "").Trim().ToUpperInvariant() == canal)
                           ?.DisponibleVenta;
            }

            if (vendedorId.HasValue)
                return rows.FirstOrDefault(r => r.Origen == "VENDEDOR" && r.VendedorId == vendedorId.Value)
                           ?.DisponibleVenta;

            return rows.FirstOrDefault(r => r.Origen == "VENDEDOR")?.DisponibleVenta;
        }






        [HttpPost]
        public async Task<IActionResult> GuardarPedido(PedidoViewModel model, string accion, bool esMuestra = false, CancellationToken ct = default)
        {
            if (!model.FechaEntrega.HasValue)
            {
                TempData["Error"] = "Debes seleccionar una fecha de entrega válida.";
                return View("OrdenVenta", model);
            }

            static string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();

            // Lee ClienteCodigo o Cliente si existen en el DTO (para no depender del nombre exacto)
            static string GetClienteFromDto(PresupuestoConsumoDto dto)
            {
                var t = dto.GetType();
                var p = t.GetProperty("ClienteCodigo") ?? t.GetProperty("Cliente");
                var v = p?.GetValue(dto)?.ToString();
                return Norm(v);
            }

            var modoPresupuesto = GetModoPresupuestoActual();   // "VENDEDOR" | "CLIENTE"
            string clienteUp = Norm(model.Cliente);

            decimal totalPedido = model.Productos?.Sum(p => p.Peso * p.Precio) ?? 0m;

            // Info crédito (viene de SAP normalmente)
            decimal credito = model.Credito;
            decimal saldo = model.Saldo;
            decimal otrosPedidos = model.OtrosPedidos;
            decimal totalDisponible = credito - saldo - otrosPedidos;

            const decimal TOL = 0.01m;

            bool requiereAutorizacionPrecio = false;
            bool requiereAutorizacionPresupuesto = false;
            bool sapDisponible = true;

            // =========================
            // SKUs del pedido + kilos por SKU
            // =========================
            var kilosPedidoPorSku = (model.Productos ?? new List<PedidoProductoViewModel>())
                .Where(p => !string.IsNullOrWhiteSpace(p.ProductoCodigo))
                .GroupBy(p => Norm(p.ProductoCodigo))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Peso));

            var skusPedido = kilosPedidoPorSku.Keys.ToList();

            // =========================
            // Datos cliente / canal / vendedorId (desde ClienteSap)
            // =========================
            var cliSap = await _context.ClienteSap
                .AsNoTracking()
                .Where(c => ((c.Cliente ?? "").Trim().ToUpper()) == clienteUp)   // ✅ FIX EF (sin Norm() en SQL)
                .Select(c => new
                {
                    Canal = c.U_CANAL,
                    VendedorId = (int?)c.VendedorId
                })
                .FirstOrDefaultAsync(ct);

            string canalClienteUp = Norm(cliSap?.Canal);
            bool esCanalCedis = canalClienteUp.StartsWith("CEDIS");

            int? vendedorId = (cliSap?.VendedorId.HasValue == true && cliSap.VendedorId.Value > 0)
                ? cliSap.VendedorId.Value
                : (int?)null;

            // Sucursal de la serie (para regla CEDIS/MATRIZ)
            var serieInfo = await _context.Series
                .AsNoTracking()
                .Where(s => s.NombreSerie == model.Serie)
                .Select(s => new { s.Sucursal })
                .FirstOrDefaultAsync(ct);

            string sucursalSerieUp = Norm(serieInfo?.Sucursal);
            bool esSerieMatriz = (sucursalSerieUp == "MATRIZ");

            // Regla: si cliente es CEDIS y serie NO es MATRIZ => NO validar presupuesto
            bool aplicarValidacionPresupuesto = !(esCanalCedis && !esSerieMatriz);

            // =========================================================
            // 1) VALIDACIÓN PRESUPUESTO (SQL)  -> SU PROPIO TRY/CATCH
            // =========================================================
            if (aplicarValidacionPresupuesto && skusPedido.Count > 0)
            {
                try
                {
                    var sucursalParam = model.Serie; // o tu lógica de sucursal
                    var modoUp = Norm(modoPresupuesto);

                    foreach (var sku in skusPedido)
                    {
                        var kilosPedidoSku = kilosPedidoPorSku.TryGetValue(sku, out var kp) ? kp : 0m;
                        if (kilosPedidoSku <= 0m) continue;

                        // 🔥 Ahora regresa LISTA
                        var rows = await ObtenerPresupuestoDetalleAsync(
                            sucursal: sucursalParam,
                            sku: sku,
                            fechaSolicitud: model.FechaEntrega.Value,
                            vendedorId: vendedorId,
                            esCanalCedis: esCanalCedis,
                            canalCliente: canalClienteUp,
                            ct: ct
                        );

                        PresupuestoConsumoDto? detSel = null;

                        if (rows != null && rows.Count > 0)
                        {
                            if (esCanalCedis)
                            {
                                // CEDIS: por canal exacto
                                detSel = rows.FirstOrDefault(r =>
                                    Norm(r.Origen) == "CEDIS" &&
                                    Norm(r.Canal) == canalClienteUp);
                            }
                            else
                            {
                                if (modoUp == "CLIENTE")
                                {
                                    // CLIENTE: por cliente exacto (ClienteCodigo o Cliente)
                                    detSel = rows.FirstOrDefault(r =>
                                        Norm(r.Origen) == "CLIENTE" &&
                                        GetClienteFromDto(r) == clienteUp);
                                }
                                else // VENDEDOR
                                {
                                    if (vendedorId.HasValue)
                                        detSel = rows.FirstOrDefault(r => Norm(r.Origen) == "VENDEDOR" && r.VendedorId == vendedorId.Value);
                                    else
                                        detSel = rows.FirstOrDefault(r => Norm(r.Origen) == "VENDEDOR");
                                }
                            }
                        }

                        if (detSel == null || detSel.DisponibleVenta == null)
                        {
                            // sin presupuesto -> fail-open
                            continue;
                        }

                        var disp = detSel.DisponibleVenta.Value;

                        if (disp <= 0m || kilosPedidoSku > (disp + TOL))
                        {
                            requiereAutorizacionPresupuesto = true;

                            _logger.LogWarning(
                                "PRESUP AUT => SKU={Sku} PedidoKg={PedidoKg} Disp={Disp} Pres={Pres} KgPed={KgPed} KgSur={KgSur} Origen={Origen} Canal={Canal} VendId={VendId}",
                                sku, kilosPedidoSku, disp,
                                detSel.PresupuestoAsignado, detSel.KgPedidosMes, detSel.KgSurtidoReal,
                                detSel.Origen, detSel.Canal, detSel.VendedorId
                            );

                            break; // ya con uno que exceda, listo
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Si falla el SQL de presupuesto, aquí decides tu política:
                    // FAIL-OPEN real => NO marcar presupuesto.
                    // Conservador => sí marcar.
                    //requiereAutorizacionPresupuesto = true;
                    requiereAutorizacionPresupuesto = false;
                    _logger.LogError(ex, "Error validando presupuesto (SQL). Cliente={Cliente} Serie={Serie}", model.Cliente, model.Serie);
                }
            }

            // =========================================================
            // 2) VALIDACIÓN PRECIO (SAP)  -> SU PROPIO TRY/CATCH
            // =========================================================
            try
            {
                foreach (var p in model.Productos ?? new List<PedidoProductoViewModel>())
                {
                    var sku = Norm(p.ProductoCodigo);
                    if (string.IsNullOrWhiteSpace(sku)) continue;

                    var productoSap = await _sap.ObtenerPrecioArticuloPorClienteAsync(model.Cliente, sku);
                    if (productoSap != null)
                    {
                        var precioOV = decimal.Round(p.Precio, 2, MidpointRounding.AwayFromZero);
                        var precioLista = decimal.Round(productoSap.Precio, 2, MidpointRounding.AwayFromZero);

                        if (precioOV < precioLista - TOL)
                            requiereAutorizacionPrecio = true;
                    }
                }
            }
            catch (Exception ex)
            {
                sapDisponible = false;
                _logger.LogError(ex, "Error consultando precios SAP. Cliente={Cliente}", model.Cliente);
                // opcional:
                // requiereAutorizacionPrecio = true;
            }

            bool requiereAutorizacionCredito = sapDisponible && (totalPedido > totalDisponible);

            int estatusPedido =
                (requiereAutorizacionPrecio || requiereAutorizacionPresupuesto || requiereAutorizacionCredito)
                    ? 2
                    : 1;

            // =========================================================
            // Propiedades/documentación del cliente (SAP) -> TRY/CATCH APARTE
            // =========================================================
            string documentacionConcatenada = string.Empty;
            if (sapDisponible)
            {
                try
                {
                    var props = await _sap.ObtenerPropiedadesClienteAsync(model.Cliente);
                    documentacionConcatenada = string.Join(" | ",
                        props.Where(p => p.Valor != null && p.Valor.ToString().ToLower() == "true")
                             .Select(p => p.Nombre));
                }
                catch
                {
                    sapDisponible = false;
                }
            }

            // Consecutivo temporal único
            string consecutivoTemporal = $"TMP-{Guid.NewGuid():N}";
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            // 1) Cabecera OV
            var pedido = new OrdenVenta
            {
                Consecutivo = consecutivoTemporal,
                Serie = model.Serie,
                FechaEntrega = model.FechaEntrega.Value,
                FechaEmbarque = model.FechaEmbarque,
                HoraEmbarque = model.HoraEmbarque,
                Cliente = model.Cliente,
                Vendedor = model.Vendedor,
                VendedorId = vendedorId,
                Ruta = string.IsNullOrWhiteSpace(model.Ruta) ? "Sin Dirección" : model.Ruta,
                Presentacion = model.Presentacion,
                Observacion = model.Observacion,
                ModoPresupuesto = modoPresupuesto,

                Saldo = sapDisponible ? model.Saldo : 0m,
                OtrosPedidos = sapDisponible ? model.OtrosPedidos : 0m,
                Credito = sapDisponible ? model.Credito : 0m,

                Estatus = estatusPedido,
                FechaRegistro = DateTime.Now,
                Documentacion = sapDisponible ? documentacionConcatenada : string.Empty,

                AutorizacionCredito = !requiereAutorizacionCredito,
                //AutorizacionPresupuesto = !requiereAutorizacionPresupuesto,
                AutorizacionPresupuesto = true,
                AutorizacionPrecio = !requiereAutorizacionPrecio
            };

            _context.OrdenVenta.Add(pedido);
            await _context.SaveChangesAsync(ct);

            // 2) Consecutivo definitivo (retry anti-duplicado)
            pedido.Consecutivo = $"OV-{pedido.Id:D8}";
            const int maxIntentos = 2;
            for (int intento = 1; intento <= maxIntentos; intento++)
            {
                try
                {
                    _context.OrdenVenta.Update(pedido);
                    await _context.SaveChangesAsync(ct);
                    break;
                }
                catch (DbUpdateException ex) when (EsDuplicadoConsecutivo(ex))
                {
                    pedido.Consecutivo = $"OV-{pedido.Id:D8}-{intento}";
                    if (intento == maxIntentos) throw;
                }
            }

            // 3) Detalle
            if (model.Productos != null && model.Productos.Any())
            {
                foreach (var p in model.Productos)
                {
                    var det = new OrdenVentaProducto
                    {
                        PedidoId = pedido.Id,
                        ProductoCodigo = p.ProductoCodigo,
                        ProductoNombre = p.ProductoNombre,
                        Peso = p.Peso,
                        Precio = p.Precio,
                        Cajas = p.Cajas
                    };
                    _context.OrdenVentaProducto.Add(det);
                }
                await _context.SaveChangesAsync(ct);
            }

            // 4) Si es muestra, crear registro en tabla puente 
            if (esMuestra)
            {
                var ovMuestra = new OrdenVentaMuestra
                {
                    OrdenVentaId = pedido.Id,
                    EsMuestra = true,
                    FechaCreacion = DateTime.Now
                };
                _context.OrdenVentaMuestra.Add(ovMuestra);
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            TempData["Success"] = sapDisponible
                ? $"Pedido guardado. Consecutivo: {pedido.Consecutivo}"
                : $"Pedido guardado (sin datos de SAP). Consecutivo: {pedido.Consecutivo}";

            if (accion == "salir") return RedirectToAction("Inicio", "Home");
            if (accion == "nuevo") return RedirectToAction("OrdenVenta", "Comercial");
            return RedirectToAction("Index", "Pedidos");
        }

        [HttpPost("Comercial/CrearSolicitudDesdeOV")]
        public async Task<IActionResult> CrearSolicitudDesdeOV(int ordenVentaId)
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                // 1. Obtener datos de la OV
                var ov = await conn.QueryFirstOrDefaultAsync(
                    "SELECT * FROM OrdenVenta WHERE Id = @Id",
                    new { Id = ordenVentaId });

                if (ov == null)
                    return Json(new { ok = false, mensaje = "OV no encontrada" });

                // 2. Obtener productos de la OV
                var productos = await conn.QueryAsync(
                    "SELECT * FROM OrdenVentaProducto WHERE PedidoId = @PedidoId AND Eliminado = 0",
                    new { PedidoId = ordenVentaId });

                // 3. Calcular folio SM
                var anioActual = DateTime.Now.Year.ToString();
                var sqlFolio = @"SELECT ISNULL(MAX(CAST(SUBSTRING(Id, 9, 4) AS INT)), 0) + 1 
                         FROM SolicitudMuestras WHERE Id LIKE 'SM-' + @Anio + '-%'";
                var siguienteNum = await conn.ExecuteScalarAsync<int>(sqlFolio, new { Anio = anioActual });
                var folioDefinitivo = $"SM-{anioActual}-{siguienteNum:D4}";

                // 4. Crear solicitud de muestra con TODA la info de la OV
                var sqlSolicitud = @"
            INSERT INTO SolicitudMuestras 
                (Id, CreatedAt, CreatedBy, Seller, Client, Species, RequestedDate, 
                 Route, Destination, Priority, Notes, Stage, Location)
            VALUES 
                (@Id, @CreatedAt, @CreatedBy, @Seller, @Client, @Species, @RequestedDate,
                 @Route, @Destination, @Priority, @Notes, @Stage, @Location)";

                await conn.ExecuteAsync(sqlSolicitud, new
                {
                    Id = folioDefinitivo,
                    CreatedAt = DateTime.Now,
                    CreatedBy = ov.Vendedor ?? "Sistema",
                    Seller = ov.Vendedor ?? "",
                    Client = ov.Cliente ?? "",
                    Species = ov.Presentacion ?? "",
                    RequestedDate = ov.FechaEntrega ?? DateTime.Now.AddDays(3),
                    Route = ov.Ruta ?? "",
                    Destination = ov.Ruta ?? "",
                    Priority = "Normal",
                    Notes = ov.Observacion ?? $"Generada desde OV: {ov.Consecutivo}",
                    Stage = "Planeación pendiente",
                    Location = "Comercial"
                });

                // 5. Insertar items/productos
                var sqlItem = @"
            INSERT INTO SolicitudMuestras_Items 
                (Uid, SolicitudId, Sku, WorkSku, Product, Spec, Boxes, Temp)
            VALUES 
                (@Uid, @SolicitudId, @Sku, @WorkSku, @Product, @Spec, @Boxes, @Temp)";

                foreach (var p in productos)
                {
                    await conn.ExecuteAsync(sqlItem, new
                    {
                        Uid = Guid.NewGuid().ToString("N").Substring(0, 10),
                        SolicitudId = folioDefinitivo,
                        Sku = p.ProductoCodigo ?? "SD",
                        WorkSku = (string)null,
                        Product = p.ProductoNombre ?? "SD",
                        Spec = "",
                        Boxes = p.Cajas > 0 ? p.Cajas : 1,
                        Temp = "Refrigerado"
                    });
                }

                // 6. Actualizar tabla puente con el folio
                await conn.ExecuteAsync(
                    "UPDATE OrdenVentaMuestra SET SolicitudMuestraId = @SolicitudId WHERE OrdenVentaId = @OrdenVentaId",
                    new { SolicitudId = folioDefinitivo, OrdenVentaId = ordenVentaId });

                return Json(new { ok = true, mensaje = $"Solicitud {folioDefinitivo} creada correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }



        private static bool EsDuplicadoConsecutivo(DbUpdateException ex)
        {
            return ex.InnerException is SqlException sqlEx
                && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
        }





        // GET: /Comercial/ValidarFacturas?cardCode=C000337
        [HttpGet]
        public async Task<IActionResult> ValidarFacturas(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return BadRequest("Se requiere el cardCode.");

            try
            {
                var facturas = await _sap.GetInvoicesAll(cardCode);

                // Retorna JSON para inspección
                return Json(facturas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener facturas: {ex.Message}");
            }
        }



        ////======================================
        //// EJECUTA EL PRESUPUESTO PARA LA VISTA DE PRESUPUESTO.CSHTML
        ////======================================

        //[HttpGet]
        //public async Task<IActionResult> ObtenerPresupuesto(string cardCode)
        //{
        //    if (string.IsNullOrWhiteSpace(cardCode))
        //        return BadRequest("El código de cliente es obligatorio.");

        //    try
        //    {
        //        // 🔹 Obtener todas las facturas del cliente (sin filtrar por fecha)
        //        var facturas = await _sap.GetInvoicesAll(cardCode);

        //        if (facturas == null || !facturas.Any())
        //            return Json(new List<DocumentoVentaViewModel>());

        //        // 🔹 Mapear a PresupuestoArticuloViewModel
        //        var resultado = facturas.Select(f => new PresupuestoArticuloViewModel
        //        {
        //            SKU = f.SKU ?? string.Empty,
        //            TotalKilos = Math.Round(f.Kilos, 2, MidpointRounding.AwayFromZero),
        //            Fecha = f.DocDate
        //        }).ToList();

        //        return Json(resultado);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, $"Error inesperado: {ex.Message}");
        //    }
        //}

        // ===============================================
        // PRESUPUESTO LOCAL (últimos N meses) por cliente
        // Fuente: dbo.sap_invoice_lines + ArticuloSap (U_MASTER)
        // GET: /Comercial/ObtenerPresupuesto?cardCode=C000176&meses=12
        // ===============================================
        [HttpGet("Comercial/ObtenerPresupuesto")]
        public async Task<IActionResult> ObtenerPresupuesto(string cardCode, int meses = 12)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return BadRequest("El código de cliente es obligatorio.");

            if (meses <= 0) meses = 12;

            var hoy = DateTime.Today;
            var desdeBase = hoy.AddMonths(-(meses - 1));
            var desde = new DateTime(desdeBase.Year, desdeBase.Month, 1); // primer día del mes inicial

            var cs = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, "No se encontró la cadena de conexión 'DefaultConnection'.");

            // Unimos a ArticuloSap para traer U_MASTER por SKU
            var sql = @"
                     SELECT
                         l.sku                                       AS sku,
                         ISNULL(a.U_MASTER, 'SIN_MASTER')            AS master,
                         SUM(l.Kilos)                                AS totalKilos,
                         CAST(DATEFROMPARTS(YEAR(l.doc_date), MONTH(l.doc_date), 1) AS date) AS fecha
                     FROM dbo.sap_invoice_lines AS l WITH (NOLOCK)
                     LEFT JOIN dbo.ArticuloSap   AS a WITH (NOLOCK)
                            ON a.ProductoCodigo = l.sku
                     WHERE l.card_code = @card
                       AND l.doc_date >= @desde
                     GROUP BY
                         l.sku,
                         a.U_MASTER,
                         YEAR(l.doc_date),
                         MONTH(l.doc_date)
                     ORDER BY fecha, l.sku;";

            var lista = new List<object>();

            try
            {
                using var con = new SqlConnection(cs);
                await con.OpenAsync();

                using var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@card", cardCode.Trim());
                cmd.Parameters.AddWithValue("@desde", desde);

                using var rd = await cmd.ExecuteReaderAsync();
                // Usamos los ordinales por seguridad
                int iSku = rd.GetOrdinal("sku");
                int iMaster = rd.GetOrdinal("master");
                int iKilos = rd.GetOrdinal("totalKilos");
                int iFecha = rd.GetOrdinal("fecha");

                while (await rd.ReadAsync())
                {
                    var sku = rd.IsDBNull(iSku) ? "" : rd.GetString(iSku);
                    var master = rd.IsDBNull(iMaster) ? "" : rd.GetString(iMaster);
                    var kilos = rd.IsDBNull(iKilos) ? 0m : rd.GetDecimal(iKilos);
                    var fecha = rd.GetDateTime(iFecha);

                    // Claves en camelCase para que el JS (mapearVentas) funcione directo
                    lista.Add(new
                    {
                        sku = sku,
                        master = master, // <- ¡importante!
                        totalKilos = Math.Round(kilos, 2, MidpointRounding.AwayFromZero),
                        fecha = fecha
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al consultar ventas locales", detalle = ex.Message });
            }

            return Json(lista);
        }






        //=================================================
        // SINCRONIZADOR DE FACTURAS POR CLIENTE ESPECIFICO A SQL LOCAL
        //=================================================
        [HttpPost("Comercial/SyncCliente")]
        public async Task<IActionResult> SyncCliente(string cardCode)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return BadRequest("cardCode requerido");

            try
            {
                // Lee la cadena de conexión desde appsettings.json -> "ConnectionStrings:Default"
                var cs = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(cs))
                    return StatusCode(500, "No se encontró la cadena de conexión 'Default'.");

                var updated = await _sync.SincronizarInvoicesClienteAsync(cardCode, cs);
                return Ok(new { cardCode, updated, ts = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        //=================================================
        // SINCRONIZADOR DE FACTURAS DE TODOS LOS CLIENTES A SQL LOCAL 
        //=================================================
        //https://localhost:7171/Comercial/SyncFacturasTodas

        [HttpPost("Comercial/SyncFacturasTodas")]
        public async Task<IActionResult> SyncFacturasTodas()
        {
            try
            {
                var cs = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(cs))
                    return StatusCode(500, "No se encontró la cadena de conexión 'DefaultConnection'.");

                // ✅ Aquí mandas la cadena de conexión
                var total = await _sync.SincronizarInvoicesDeTodosLosClientesAsync(cs);

                return Ok(new { ok = true, total, ts = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ok = false, error = ex.Message });
            }
        }






        // ==================================================
        // OBTENER PRESUPUESTO (últimos 12 meses) DESDE LOCAL
        // Fuente: dbo.sap_invoice_lines
        // ==================================================
        [HttpGet("Comercial/ObtenerPresupuesto2")]
        public async Task<IActionResult> ObtenerPresupuesto2(string cardCode)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return BadRequest("El código de cliente es obligatorio.");

            // Primer día del mes de hace 11 meses (incluye el mes actual -> 12 meses)
            var hoy = DateTime.Today;
            var desde = new DateTime(hoy.AddMonths(-11).Year, hoy.AddMonths(-11).Month, 1);

            // Cadena de conexión
            var cs = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, "No se encontró la cadena de conexión 'DefaultConnection'.");

            // SQL: agrega por SKU y mes (DocDate) para el cliente
            var sql = @"
        SELECT
            sku,
            SUM(kilos) AS Kilos,
            CAST(DATEFROMPARTS(YEAR(doc_date), MONTH(doc_date), 1) AS date) AS Fecha
        FROM dbo.sap_invoice_lines WITH (NOLOCK)
        WHERE card_code = @card
          AND doc_date >= @desde
        GROUP BY YEAR(doc_date), MONTH(doc_date), sku
        ORDER BY Fecha, sku;";

            var resultado = new List<PresupuestoArticuloViewModel>();

            try
            {
                using var con = new SqlConnection(cs);
                await con.OpenAsync();

                using var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@card", cardCode.Trim());
                cmd.Parameters.AddWithValue("@desde", desde);

                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var sku = rd.IsDBNull(0) ? "" : rd.GetString(0);
                    var kilos = rd.IsDBNull(1) ? 0m : rd.GetDecimal(1);
                    var fecha = rd.GetDateTime(2);

                    resultado.Add(new PresupuestoArticuloViewModel
                    {
                        SKU = sku,
                        TotalKilos = Math.Round(kilos, 2, MidpointRounding.AwayFromZero),
                        Fecha = fecha
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al consultar ventas locales", detalle = ex.Message });
            }

            return Json(resultado);
        }



        // Constantes de compilación (válidas en atributos)
        private const long MAX_LONG = 9223372036854775807L; // long.MaxValue
        private const int MAX_INT = 2147483647;           // int.MaxValue

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MAX_LONG)]
        [RequestFormLimits(
            ValueCountLimit = MAX_INT,
            ValueLengthLimit = MAX_INT,
            MultipartBodyLengthLimit = MAX_LONG)]
        public async Task<IActionResult> GuardarPresupuesto(
            string Cliente, int Mes, int Anio, List<PresupuestoItem> items)
        {
            // --- Validaciones básicas ---
            if (items == null || !items.Any())
                return BadRequest("Datos inválidos.");
            if (Mes < 1 || Mes > 12 || Anio <= 0)
                return BadRequest("Mes o año inválidos.");

            // Normalizadores
            static string N(string s) => (s ?? string.Empty).Trim();
            static string U(string s) => N(s).ToUpperInvariant();

            // 1) Normalizar filas, validar numéricos y deduplicar por clave (cliente+sku)
            var filas = items
                .Select(i => new
                {
                    ClienteId = U(string.IsNullOrWhiteSpace(i.Cliente) ? (Cliente ?? "") : i.Cliente),
                    ProductoCodigo = U(i.ProductoCodigo),
                    Objetivo = i.Objetivo,
                    Presupuesto = i.Presupuesto,
                    Comentario = N(i.Comentario)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.ClienteId) &&
                            !string.IsNullOrWhiteSpace(x.ProductoCodigo))
                // Validación numérica (ajusta límites a tus reglas)
                .Where(x =>
                           x.Objetivo >= 0 && x.Objetivo <= 2_000_000_000 &&   // Objetivo puede ser 0
                           x.Presupuesto > 0 && x.Presupuesto <= 2_000_000_000 // Presupuesto tiene que ser > 0
                       )
                .GroupBy(x => new { x.ClienteId, x.ProductoCodigo }) // evita duplicados en el POST
                .Select(g => g.First())
                .ToList();

            if (!filas.Any())
                return BadRequest("No hay filas válidas para guardar.");

            // 2) Armar set de claves y consultar existentes para el Mes/Año (sin tracking)
            var claves = filas.Select(x => new { x.ClienteId, x.ProductoCodigo }).ToList();
            var claveSet = new HashSet<string>(claves.Select(k => $"{k.ClienteId}||{k.ProductoCodigo}"));

            // OJO: la propiedad C# debe ser 'Anio' si la columna SQL es 'Año'
            var existentes = await _context.Presupuestos
                .AsNoTracking()
                .Where(p => p.Mes == Mes && p.Año == Anio)
                .Select(p => new { p.ClienteId, p.ProductoCodigo })
                .ToListAsync();

            // Normaliza a UPPER también lo que viene de DB por si tuvieras basura histórica
            var yaGuardados = new HashSet<string>(
                existentes.Select(p => $"{U(p.ClienteId)}||{U(p.ProductoCodigo)}"));

            var duplicados = claveSet.Where(k => yaGuardados.Contains(k)).ToList();
            if (duplicados.Any())
            {
                var lista = string.Join(", ", duplicados.Take(10));
                TempData["Error"] = $"Ya existe presupuesto para {lista} en {Mes}/{Anio}. Revísalo en el Balance Master.";
                return RedirectToAction("Presupuestos");
            }

            // 3) Insertar (transacción + UTC + normalización)
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var r in filas)
                {
                    var entidad = new Presupuesto
                    {
                        ClienteId = r.ClienteId,      // ya viene en UPPER
                        ProductoCodigo = r.ProductoCodigo, // ya viene en UPPER
                        Objetivo = r.Objetivo,
                        PresupuestoAsignado = r.Presupuesto,    // o tu propiedad 'Presupuesto'
                        Mes = Mes,
                        Año = Anio,             // <- propiedad .NET (mapea a columna "Año" si aplica)
                        Comentario = r.Comentario,
                        FechaRegistro = DateTime.UtcNow,
                        Usuario = User?.Identity?.Name
                    };
                    _context.Presupuestos.Add(entidad);
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["Ok"] = $"Presupuesto guardado para {filas.Count} renglón(es) en {Mes}/{Anio}.";
                return RedirectToAction("Presupuestos");
            }
            catch (DbUpdateException ex)
            {
                // Captura de UX único (ajusta al nombre real de tu índice)
                if (ex.InnerException?.Message?.Contains("UX_Presupuesto_Cliente_Sku_Mes_Anio",
                     StringComparison.OrdinalIgnoreCase) == true)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Algunos renglones ya habían sido presupuestados en ese mes/año. Revísalo en el Balance Master.";
                    return RedirectToAction("Presupuestos");
                }

                await tx.RollbackAsync();
                throw;
            }
        }



        //======================================
        // MUESTRA PRESUPUESTOS POR MES ASIGNADO
        //======================================


        [HttpGet]
        public async Task<IActionResult> ObtenerPresupuestoCliente(string cardCode, string productoCodigo, int mes, int anio)
        {
            if (string.IsNullOrEmpty(cardCode) || string.IsNullOrEmpty(productoCodigo))
                return BadRequest("Se requiere cliente y producto.");

            try
            {
                var presupuesto = await _context.Presupuestos
                    .Where(p => p.ClienteId.ToUpper() == cardCode.ToUpper()
                             && p.ProductoCodigo.ToUpper() == productoCodigo.ToUpper()
                             && p.Mes == mes
                             && p.Año == anio)
                    .Select(p => new
                    {
                        p.ProductoCodigo,
                        PresupuestoAsignado = p.PresupuestoAsignado
                    })
                    .ToListAsync();

                // Devuelve al menos un elemento con 0 si no hay registro
                //if (!presupuesto.Any())
                //    presupuesto.Add(new { ProductoCodigo = productoCodigo, PresupuestoAsignado = 0m });

                if (!presupuesto.Any())
                    presupuesto.Add(new { ProductoCodigo = productoCodigo, PresupuestoAsignado = 0m });


                return Json(presupuesto);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener presupuesto: {ex.Message}");
            }
        }


        //https://localhost:7171/Comercial/ObtenerPresupuestoProducto?cardCode=C000176&productoCodigo=V101&mes=9&anio=2025

        [HttpGet]
        public async Task<IActionResult> ObtenerPresupuestoProducto(string cardCode, string productoCodigo, int mes, int anio, string serie)
        {
            if (string.IsNullOrWhiteSpace(cardCode) || string.IsNullOrWhiteSpace(productoCodigo))
                return BadRequest("Se requiere cliente y producto.");

            if (await NoAplicaPresupuestoPorSerieAsync(cardCode, serie))
                return Json(new { ProductoCodigo = productoCodigo.ToUpper().Trim(), PresupuestoAsignado = 0m, enPresupuesto = false });

            var card = cardCode.Trim().ToUpper();
            var item = productoCodigo.Trim().ToUpper();

            // Canal del cliente
            var canalClienteRaw = await _context.ClienteSap
                .Where(c => c.Cliente.ToUpper() == card)
                .Select(c => c.U_CANAL)
                .FirstOrDefaultAsync();

            var canalCliente = (canalClienteRaw ?? "").Trim().ToUpper();

            // Serie seleccionada (para canal aplicable)
            var serieInfo = await _context.Series
                .Where(s => s.NombreSerie == serie)
                .Select(s => new { s.Sucursal, s.Canal })
                .FirstOrDefaultAsync();

            var suc = (serieInfo?.Sucursal ?? "").Trim().ToUpper();
            var canalSerie = (serieInfo?.Canal ?? "").Trim().ToUpper();

            // ✅ Canal aplicable como tu SQL
            var canalAplicable = (suc == "MATRIZ") ? canalCliente : canalSerie;

            // ✅ Si quieres que CEDIS SOLO aplique en MATRIZ, fuerza esta regla:
            // if (suc != "MATRIZ") canalAplicable = "";  // <- deja sin CEDIS y cae a normal

            if (!string.IsNullOrWhiteSpace(canalAplicable))
            {
                var cedis = await _context.PresupuestoCedis
                    .Where(pc => pc.Canal.ToUpper() == canalAplicable
                              && pc.ProductoCodigo.ToUpper() == item
                              && pc.Mes == mes
                              && pc.Anio == anio)
                    .Select(pc => new { ProductoCodigo = item, PresupuestoAsignado = pc.PresupuestoAsignado })
                    .FirstOrDefaultAsync();

                if (cedis != null) return Json(cedis);
            }

            var normal = await _context.Presupuestos
                .Where(p => p.ClienteId.ToUpper() == card
                         && p.ProductoCodigo.ToUpper() == item
                         && p.Mes == mes
                         && p.Año == anio)
                .Select(p => new { ProductoCodigo = item, PresupuestoAsignado = p.PresupuestoAsignado })
                .FirstOrDefaultAsync();

            return Json(normal ?? new { ProductoCodigo = item, PresupuestoAsignado = 0m });
        }




        //======================================
        // OBTIENES PRESUPUESTO POR UNA FECHA EN CONCRETO
        //======================================

        [HttpGet]
        public async Task<IActionResult> ObtenerPresupuestoPorFecha(string clienteId, string productoCodigo, DateTime fechaEntrega)
        {
            if (string.IsNullOrEmpty(clienteId) || string.IsNullOrEmpty(productoCodigo))
                return BadRequest("Cliente y producto son requeridos.");

            int mes = fechaEntrega.Month;
            int anio = fechaEntrega.Year;

            var presupuesto = await _context.Presupuestos
                .Where(p => p.ClienteId.ToUpper() == clienteId.ToUpper()
                         && p.ProductoCodigo.ToUpper() == productoCodigo.ToUpper()
                         && p.Mes == mes
                         && p.Año == anio)
                .Select(p => p.PresupuestoAsignado)
                .FirstOrDefaultAsync();

            // Devuelve 0 si no hay presupuesto
            return Json(new { Presupuesto = presupuesto });
        }

        //======================================
        // TRAE PRODUCTOS DESDE SAP Y COMBINA CON PRESUUPUESTO CLIENTE
        //======================================

        [HttpGet]
        public async Task<JsonResult> ObtenerProductosConPresupuesto(string cardCode, int mes, int anio)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return Json(new List<PedidoProductoViewModel>());

            // 1️⃣ Traer todos los productos desde SAP
            var productosSAP = await _sap.BuscarProductosAsync(""); // traer todos o filtrar según necesidad

            // 2️⃣ Traer presupuestos del cliente para ese mes/año
            var presupuestosCliente = await _context.Presupuestos
                .Where(p => p.ClienteId.ToUpper() == cardCode.ToUpper()
                         && p.Mes == mes
                         && p.Año == anio)
                .ToListAsync();

            // 3️⃣ Combinar ambos para crear PedidoProductoViewModel
            var productosConPresupuesto = productosSAP.Select(p =>
            {
                var presupuesto = presupuestosCliente
                    .FirstOrDefault(pr => pr.ProductoCodigo.ToUpper() == p.ItemCode.ToUpper());

                return new PedidoProductoViewModel
                {
                    ProductoCodigo = p.ItemCode,
                    ProductoNombre = p.ItemName,
                    Precio = p.Precio,
                    Peso = 0,
                    Cajas = 0,
                    Presupuesto = presupuesto?.PresupuestoAsignado ?? 0m,
                    VariacionPresupuesto = 0
                };
            }).ToList();

            // 4️⃣ Si no hay productos, devolver al menos uno vacío
            if (!productosConPresupuesto.Any())
            {
                productosConPresupuesto.Add(new PedidoProductoViewModel
                {
                    ProductoCodigo = string.Empty,
                    ProductoNombre = string.Empty,
                    Precio = 0,
                    Peso = 0,
                    Cajas = 0,
                    Presupuesto = 0,
                    VariacionPresupuesto = 0
                });
            }

            return Json(productosConPresupuesto);
        }


        //======================================
        // OBTENER PRODUCTOS POR CLIENTE EN MES
        //======================================

        [HttpGet]
        public async Task<JsonResult> ObtenerProductosPorClienteFecha(string cardCode, DateTime fechaEntrega)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return Json(new List<PedidoProductoViewModel>());

            int mes = fechaEntrega.Month;
            int anio = fechaEntrega.Year;

            // 1) Canal del cliente (ajusta nombres de entidad/campos si difieren)
            //    Supone tabla Clientes con Codigo (o CardCode) y Canal (string)
            var canalCliente = await _context.ClienteSap
                .Where(c => c.Cliente.ToUpper() == cardCode.ToUpper())
                .Select(c => c.U_CANAL)               // <- ajusta si tu campo se llama diferente
                .FirstOrDefaultAsync();

            // 2) Presupuesto "normal" (por cliente+producto+mes/año)
            var normalList = await _context.Presupuestos
                .Where(p => !string.IsNullOrEmpty(p.ClienteId)
                         && p.ClienteId.ToUpper() == cardCode.ToUpper()
                         && p.Mes == mes
                         && p.Año == anio)
                .Select(p => new
                {
                    ProductoCodigo = p.ProductoCodigo,
                    PresupuestoAsignado = p.PresupuestoAsignado
                })
                .AsNoTracking()
                .ToListAsync();

            // 3) Presupuesto CEDIS (por canal+producto+mes/año) – solo si el cliente tiene canal
            var cedisList = new List<(string ProductoCodigo, decimal PresupuestoAsignado)>();
            if (!string.IsNullOrWhiteSpace(canalCliente))
            {
                var tmp = await _context.PresupuestoCedis
                    .Where(pc => pc.Canal.ToUpper() == canalCliente.ToUpper()
                              && pc.Mes == mes
                              && pc.Anio == anio)
                    .Select(pc => new
                    {
                        ProductoCodigo = pc.ProductoCodigo,
                        PresupuestoAsignado = pc.PresupuestoAsignado
                    })
                    .AsNoTracking()
                    .ToListAsync();

                cedisList = tmp
                    .Select(x => (ProductoCodigo: (x.ProductoCodigo ?? string.Empty).Trim().ToUpper(),
                                  PresupuestoAsignado: x.PresupuestoAsignado))
                    .ToList();
            }

            // 4) Diccionarios de consulta rápida (UPPER para llaves)
            var normalDict = normalList
                .GroupBy(x => (x.ProductoCodigo ?? string.Empty).Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.Sum(v => v.PresupuestoAsignado));

            var cedisDict = cedisList
                .GroupBy(x => x.ProductoCodigo)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.PresupuestoAsignado));

            // Si no hay ni normal ni cedis, no devolvemos nada
            if (!normalDict.Any() && !cedisDict.Any())
                return Json(new List<PedidoProductoViewModel>());

            // 5) Catálogo de códigos a evaluar (UNION de normal y cedis)
            var codigos = normalDict.Keys
                .Union(cedisDict.Keys)
                .Distinct()
                .ToList();

            // 6) Catálogo local ArticuloSap para nombre y kilos (ajusta nombres si difieren)
            var codigosUpper = codigos.ToHashSet(); // ya vienen en UPPER
            var articulos = await _context.ArticuloSap
                .Where(a => codigosUpper.Contains((a.ProductoCodigo ?? string.Empty).Trim().ToUpper()))
                .Select(a => new
                {
                    Code = a.ProductoCodigo,
                    Name = a.ProductoNombre,
                    KilosCaja = (decimal?)(a.U_KilosCaja) ?? 0m   // si U_KilosCaja es double/float/campo nullable, casteamos a decimal
                })
                .AsNoTracking()
                .ToListAsync();

            var artDict = articulos.ToDictionary(
                k => (k.Code ?? string.Empty).Trim().ToUpper(),
                v => new { v.Name, v.KilosCaja });

            // 7) Construcción final
            var lista = new List<PedidoProductoViewModel>();

            foreach (var code in codigos)
            {
                // Presupuesto a usar: si hay CEDIS para el canal, toma CEDIS; si no, el normal; si no, 0
                decimal presupuestoAsignado =
                    (cedisDict.TryGetValue(code, out var pCedis) ? pCedis :
                     normalDict.TryGetValue(code, out var pNorm) ? pNorm : 0m);

                // Datos locales
                artDict.TryGetValue(code, out var infoLocal);

                decimal precio = 0m;
                decimal kilosCaja = infoLocal?.KilosCaja ?? 0m;
                string nombre = !string.IsNullOrWhiteSpace(infoLocal?.Name)
                                    ? $"{infoLocal!.Name} ({code})"
                                    : code;

                // Intentar mejorar con SAP (precio + kilos + nombre)
                try
                {
                    var sap = await _sap.ObtenerPrecioArticuloPorClienteAsync(cardCode, code);
                    if (sap != null)
                    {
                        precio = sap.Precio;

                        if (sap.KilosCaja > 0)            // prioriza kilos SAP válidos
                            kilosCaja = sap.KilosCaja;

                        if (!string.IsNullOrWhiteSpace(sap.ItemName))
                            nombre = $"{sap.ItemName} ({sap.ItemCode})";
                    }
                }
                catch
                {
                    // Si SAP falla, dejamos precio=0 y usamos kilos/nombre locales si ya los tenemos
                }

                lista.Add(new PedidoProductoViewModel
                {
                    ProductoCodigo = code,
                    ProductoNombre = nombre,
                    Precio = precio,
                    KilosCaja = kilosCaja,
                    Peso = 0m,  // el usuario lo ajustará
                    Cajas = 0,  // se recalcula según Peso*KilosCaja en el front
                    Presupuesto = presupuestoAsignado,
                    VariacionPresupuesto = 0m
                });
            }

            return Json(lista);
        }



        //======================================
        // ABRIR VISTA DE PEDIDOS LIBERADOS
        //======================================
        public async Task<IActionResult> admin_ventas()
        {
            var almacenesPermitidos = await ObtenerIdsAlmacenesPermitidosParaUsusarioAcualAsync();

            var almacenes = GetAlmacenes()
                .Where(a => almacenesPermitidos.Contains(a.Value))
                .ToList();

            if (!almacenes.Any())
            {
                almacenes.Add(new SelectListItem
                {
                    Value = "",
                    Text = "No tienes almacenes asignador. Contacta a Sistemas.",
                    Selected = true
                });

            }


            var vm = new AlmacenViewModel
            {
                SelectedAlmacenId = null,
                Almacenes = almacenes// <- llena desde appsettings
            };



            return View("~/Views/Comercial/admin_ventas.cshtml", vm);
        }





        //======================================
        // VALIDACION POR MES DE PRESUPUESTO Y PEDIDOS
        //======================================
        //https://localhost:7171/Comercial/ObtenerProductosConPresupuestoDisponible?cardCode=C000176&fechaEntrega=2025-10-29

        [HttpGet]
        public async Task<IActionResult> ObtenerProductosConPresupuestoDisponible(
      string cardCode,
      DateTime fechaEntrega,
      char Serie)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return BadRequest("Cliente no especificado.");

            int mes = fechaEntrega.Month;
            int anio = fechaEntrega.Year;
            string card = cardCode.Trim().ToUpper();

            // =====================================================
            // CLIENTE → DEFINIR ORIGEN
            // =====================================================
            var cliente = await _context.ClienteSap
                .Where(c => c.Cliente.ToUpper() == card)
                .Select(c => new
                {
                    c.VendedorId,
                    c.U_CANAL
                })
                .FirstOrDefaultAsync();

            if (cliente == null)
                return BadRequest("Cliente no encontrado.");

            bool esCedis = !string.IsNullOrWhiteSpace(cliente.U_CANAL)
                           && cliente.U_CANAL.ToUpper().StartsWith("CEDIS");

            // Si NO es CEDIS y no hay vendedor, devuelve vacío como hoy
            if (!esCedis && !cliente.VendedorId.HasValue)
                return Json(new List<object>());

            string canal = esCedis ? cliente.U_CANAL.Trim().ToUpper() : null;
            int? vendedorId = !esCedis ? cliente.VendedorId.Value : (int?)null;

            // =====================================================
            // SQL (MISMA LÓGICA DEL REPORTE) + FILTRO AL FINAL
            // =====================================================
            var sql = @"
WITH
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
presupuestos_normales AS (
    SELECT
        Cliente = UPPER(LTRIM(RTRIM(p.ClienteId))),
        SKU     = UPPER(LTRIM(RTRIM(p.ProductoCodigo))),
        Mes     = p.Mes,
        Anio    = p.Año,
        Presupuesto = SUM(p.Presupuesto)
    FROM dbo.Presupuestos p
    GROUP BY
        UPPER(LTRIM(RTRIM(p.ClienteId))),
        UPPER(LTRIM(RTRIM(p.ProductoCodigo))),
        p.Mes, p.Año
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
      AND o.Estatus BETWEEN 1 AND 5
      AND ser.Sucursal = 'MATRIZ'
),
ov_con_surtido AS (
    SELECT DISTINCT o.Id
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp         ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
),
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
consumo_cliente AS (
    SELECT
        Cliente = UPPER(ovp.Cliente),
        SKU     = ovp.SKU,
        Mes     = MONTH(ovp.FechaDate),
        Anio    = YEAR(ovp.FechaDate),
        Kg      = SUM(ovp.KgPendiente)
    FROM ov_pendiente_sku ovp
    GROUP BY
        UPPER(ovp.Cliente),
        ovp.SKU,
        MONTH(ovp.FechaDate),
        YEAR(ovp.FechaDate)
),
todo_normal AS (
    SELECT
        'CLIENTE' AS Origen,
        pn.Mes,
        pn.Anio,
        pn.Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        CAST(NULL AS INT) AS VendedorId,
        pn.SKU,
        pn.Presupuesto,
        ISNULL(cc.Kg,0) AS Kg
    FROM presupuestos_normales pn
    LEFT JOIN consumo_cliente cc
        ON cc.Cliente = pn.Cliente
       AND cc.SKU     = pn.SKU
       AND cc.Mes     = pn.Mes
       AND cc.Anio    = pn.Anio

    UNION ALL

    SELECT
        'CLIENTE',
        cc.Mes,
        cc.Anio,
        cc.Cliente,
        CAST(NULL AS NVARCHAR(100)),
        CAST(NULL AS INT),
        cc.SKU,
        0,
        cc.Kg
    FROM consumo_cliente cc
    LEFT JOIN presupuestos_normales pn
        ON pn.Cliente = cc.Cliente
       AND pn.SKU     = cc.SKU
       AND pn.Mes     = cc.Mes
       AND pn.Anio    = cc.Anio
    WHERE pn.Cliente IS NULL
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
consumo_cedis_base AS (
    SELECT Canal, SKU, Mes, Anio, SUM(Kg) Kg
    FROM (
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
consumo_cedis AS (
    SELECT * FROM consumo_cedis_base
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
    LEFT JOIN consumo_cedis cc
        ON cc.Canal = pc.Canal
       AND cc.SKU   = pc.SKU
       AND cc.Mes   = pc.Mes
       AND cc.Anio  = pc.Anio
),
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
consumo_vendedor_normal AS (
    SELECT
        ovp.VendedorId,
        SKU  = ovp.SKU,
        Mes  = MONTH(ovp.FechaDate),
        Anio = YEAR(ovp.FechaDate),
        Kg   = SUM(ovp.KgPendiente)
    FROM ov_pendiente_sku ovp
    JOIN dbo.ClienteSap c ON c.Cliente = ovp.Cliente
                         AND ISNULL(UPPER(LTRIM(RTRIM(c.U_CANAL))),'') NOT LIKE 'CEDIS%'
    WHERE ovp.VendedorId IS NOT NULL
    GROUP BY
        ovp.VendedorId,
        ovp.SKU,
        MONTH(ovp.FechaDate),
        YEAR(ovp.FechaDate)
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
       AND cv.SKU  = pv.SKU
       AND cv.Mes  = pv.Mes
       AND cv.Anio = pv.Anio
),
surtido_cliente AS (
    SELECT
        Cliente = UPPER(LTRIM(RTRIM(o.Cliente))),
        SKU     = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Mes     = MONTH(se.FechaValidacion),
        Anio    = YEAR(se.FechaValidacion),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Series ser
        ON ser.NombreSerie = o.Serie
    JOIN dbo.Subpedido sp
        ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se
        ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    JOIN clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.Estatus <> 0
      AND se.FechaValidacion IS NOT NULL
      AND ser.Sucursal = 'MATRIZ'
      AND ISNULL(UPPER(LTRIM(RTRIM(cli.U_CANAL))),'') NOT LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(o.Cliente))),
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        MONTH(se.FechaValidacion),
        YEAR(se.FechaValidacion)
),
surtido_ov_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(cli.U_CANAL))),
        SKU   = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Mes   = MONTH(se.FechaValidacion),
        Anio  = YEAR(se.FechaValidacion),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Series ser
        ON ser.NombreSerie = o.Serie
    JOIN dbo.Subpedido sp
        ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se
        ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    JOIN clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.Estatus <> 0
      AND se.FechaValidacion IS NOT NULL
      AND ser.Sucursal = 'MATRIZ'
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
surtido_vendedor_normal AS (
    SELECT
        cl.VendedorId,
        sc.SKU,
        sc.Mes,
        sc.Anio,
        KgSurtido = SUM(sc.KgSurtido)
    FROM surtido_cliente sc
    JOIN clientes cl ON cl.Cliente = sc.Cliente
    WHERE cl.VendedorId IS NOT NULL
    GROUP BY
        cl.VendedorId,
        sc.SKU,
        sc.Mes,
        sc.Anio
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
        SELECT * FROM surtido_vendedor_normal
        UNION ALL
        SELECT * FROM surtido_vendedor_desde_cedis
    ) x
    GROUP BY VendedorId, SKU, Mes, Anio
),
surtido_real AS (
    SELECT
        'CLIENTE' AS Origen,
        Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        CAST(NULL AS INT) AS VendedorId,
        SKU, Mes, Anio,
        SUM(KgSurtido) AS KgSurtido
    FROM surtido_cliente
    GROUP BY Cliente, SKU, Mes, Anio

    UNION ALL

    SELECT
        'CEDIS',
        CAST(NULL AS NVARCHAR(50)),
        Canal,
        CAST(NULL AS INT),
        SKU, Mes, Anio,
        SUM(KgSurtido)
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
,

-- =====================================================
-- DEVOLUCIONES
-- Se suman al disponible:
-- Disponible = Presupuesto - Pendiente - SurtidoReal + Devoluciones
-- =====================================================
devoluciones_cliente AS (
    SELECT
        Cliente = UPPER(LTRIM(RTRIM(d.CodigoSap))),
        SKU     = UPPER(LTRIM(RTRIM(d.Articulo))),
        Mes     = MONTH(d.FechaDevolucion),
        Anio    = YEAR(d.FechaDevolucion),
        KgDevolucion = SUM(CAST(ISNULL(d.Peso, 0) AS DECIMAL(18,4)))
    FROM dbo.DevolucionMeat d
    JOIN clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(d.CodigoSap)))
    WHERE d.FechaDevolucion IS NOT NULL
      AND ISNULL(UPPER(LTRIM(RTRIM(cli.U_CANAL))),'') NOT LIKE 'CEDIS%'
      AND EXISTS
      (
          SELECT 1
          FROM dbo.Subpedido sp
          JOIN dbo.OrdenVenta o
              ON o.Id = sp.OrdenVentaId
          JOIN dbo.Series ser
              ON ser.NombreSerie = o.Serie
          WHERE sp.U_DocMeat = d.SolicitudSurtidoId
            AND ser.Sucursal = 'MATRIZ'
      )
    GROUP BY
        UPPER(LTRIM(RTRIM(d.CodigoSap))),
        UPPER(LTRIM(RTRIM(d.Articulo))),
        MONTH(d.FechaDevolucion),
        YEAR(d.FechaDevolucion)
),
devoluciones_cedis_base AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(cli.U_CANAL))),
        SKU   = UPPER(LTRIM(RTRIM(d.Articulo))),
        Mes   = MONTH(d.FechaDevolucion),
        Anio  = YEAR(d.FechaDevolucion),
        KgDevolucion = SUM(CAST(ISNULL(d.Peso, 0) AS DECIMAL(18,4)))
    FROM dbo.DevolucionMeat d
    JOIN clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(d.CodigoSap)))
    WHERE d.FechaDevolucion IS NOT NULL
      AND UPPER(LTRIM(RTRIM(cli.U_CANAL))) LIKE 'CEDIS%'
      AND EXISTS
      (
          SELECT 1
          FROM dbo.Subpedido sp
          JOIN dbo.OrdenVenta o
              ON o.Id = sp.OrdenVentaId
          JOIN dbo.Series ser
              ON ser.NombreSerie = o.Serie
          WHERE sp.U_DocMeat = d.SolicitudSurtidoId
            AND ser.Sucursal = 'MATRIZ'
      )
    GROUP BY
        UPPER(LTRIM(RTRIM(cli.U_CANAL))),
        UPPER(LTRIM(RTRIM(d.Articulo))),
        MONTH(d.FechaDevolucion),
        YEAR(d.FechaDevolucion)
),
devoluciones_vendedor_normal AS (
    SELECT
        cl.VendedorId,
        dc.SKU,
        dc.Mes,
        dc.Anio,
        KgDevolucion = SUM(dc.KgDevolucion)
    FROM devoluciones_cliente dc
    JOIN clientes cl
        ON cl.Cliente = dc.Cliente
    WHERE cl.VendedorId IS NOT NULL
    GROUP BY
        cl.VendedorId,
        dc.SKU,
        dc.Mes,
        dc.Anio
),
devoluciones_vendedor_desde_cedis AS (
    SELECT
        VendedorId = pv.VendedorId,
        SKU        = pv.SKU,
        Mes        = pv.Mes,
        Anio       = pv.Anio,
        KgDevolucion = SUM(
            CASE
                WHEN ISNULL(pxc.PresTotalCanal,0) <= 0 THEN 0
                ELSE (dc.KgDevolucion * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal))
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
    JOIN devoluciones_cedis_base dc
        ON dc.Canal = cv.Canal
       AND dc.SKU   = pv.SKU
       AND dc.Mes   = pv.Mes
       AND dc.Anio  = pv.Anio
    GROUP BY
        pv.VendedorId,
        pv.SKU,
        pv.Mes,
        pv.Anio
),
devoluciones_vendedor_total AS (
    SELECT VendedorId, SKU, Mes, Anio, SUM(KgDevolucion) AS KgDevolucion
    FROM (
        SELECT * FROM devoluciones_vendedor_normal
        UNION ALL
        SELECT * FROM devoluciones_vendedor_desde_cedis
    ) x
    GROUP BY VendedorId, SKU, Mes, Anio
),
devoluciones_real AS (
    SELECT
        'CLIENTE' AS Origen,
        Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        CAST(NULL AS INT) AS VendedorId,
        SKU,
        Mes,
        Anio,
        SUM(KgDevolucion) AS KgDevolucion
    FROM devoluciones_cliente
    GROUP BY Cliente, SKU, Mes, Anio

    UNION ALL

    SELECT
        'CEDIS',
        CAST(NULL AS NVARCHAR(50)),
        Canal,
        CAST(NULL AS INT),
        SKU,
        Mes,
        Anio,
        SUM(KgDevolucion)
    FROM devoluciones_cedis_base
    GROUP BY Canal, SKU, Mes, Anio

    UNION ALL

    SELECT
        'VENDEDOR',
        CAST(NULL AS NVARCHAR(50)),
        CAST(NULL AS NVARCHAR(100)),
        VendedorId,
        SKU,
        Mes,
        Anio,
        SUM(KgDevolucion)
    FROM devoluciones_vendedor_total
    GROUP BY VendedorId, SKU, Mes, Anio
)
SELECT
    productoCodigo         = t.SKU,
    presupuestoAsignado    = CAST(t.Presupuesto AS DECIMAL(18,4)),
    kgPedidosMes           = CAST(ISNULL(t.Kg,0) AS DECIMAL(18,4)),
    kgSurtidoReal          = CAST(ISNULL(sr.KgSurtido,0) AS DECIMAL(18,4)),
    kgDevoluciones         = CAST(ISNULL(dev.KgDevolucion,0) AS DECIMAL(18,4)),
    presupuestoDisponible  = CAST(
        (
            t.Presupuesto 
            - ISNULL(t.Kg,0) 
            - ISNULL(sr.KgSurtido,0)
            + ISNULL(dev.KgDevolucion,0)
        )
    AS DECIMAL(18,4))
FROM (
    SELECT * FROM todo_cedis
    UNION ALL
    SELECT * FROM todo_vendedor
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
LEFT JOIN devoluciones_real dev
    ON dev.Origen = t.Origen
   AND dev.SKU    = t.SKU
   AND dev.Mes    = t.Mes
   AND dev.Anio   = t.Anio
   AND (
        (t.Origen = 'CEDIS'    AND dev.Canal      = t.Canal)
     OR (t.Origen = 'VENDEDOR' AND dev.VendedorId = t.VendedorId)
   )
WHERE
    t.Mes = @mes
    AND t.Anio = @anio
    AND (
        (@esCedis = 1 AND t.Origen = 'CEDIS' AND UPPER(LTRIM(RTRIM(t.Canal))) = @canal)
        OR
        (@esCedis = 0 AND t.Origen = 'VENDEDOR' AND t.VendedorId = @vendedorId)
    )
ORDER BY t.SKU;
";

            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            // DTO opcional (puedes usar dynamic si prefieres)
            var data = await conn.QueryAsync(sql, new
            {
                mes,
                anio,
                esCedis = esCedis ? 1 : 0,
                canal,
                vendedorId,
                serie = Serie.ToString() // solo se usa si descomentas el filtro en ov
            });

            // Si quieres redondear igual que hoy:
            var respuesta = data.Select(x => new
            {
                productoCodigo = (string)x.productoCodigo,
                presupuestoAsignado = (decimal)x.presupuestoAsignado,
                kgPedidosMes = (decimal)x.kgPedidosMes,
                kgSurtidoReal = (decimal)x.kgSurtidoReal,
                presupuestoDisponible = Math.Round((decimal)x.presupuestoDisponible, 4)
            });

            return Json(respuesta);
        }





        private async Task<bool> NoAplicaPresupuestoPorSerieAsync(string cardCode, string serie)
        {
            if (string.IsNullOrWhiteSpace(cardCode) || string.IsNullOrWhiteSpace(serie))
                return false; // si no hay datos, no bloquees aquí (o decide bloquear por seguridad)

            string card = cardCode.Trim().ToUpper();

            // Cliente canal
            var canalRaw = await _context.ClienteSap
                .Where(c => c.Cliente.ToUpper() == card)
                .Select(c => c.U_CANAL)
                .FirstOrDefaultAsync();

            string canalUp = (canalRaw ?? "").Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(canalUp))
                return false; // no es CEDIS

            // Serie -> Sucursal
            string serieTrim = serie.Trim();
            var sucursalRaw = await _context.Series
                .Where(s => s.NombreSerie == serieTrim)
                .Select(s => s.Sucursal)
                .FirstOrDefaultAsync();

            bool serieEsMatriz = (sucursalRaw ?? "").Trim().ToUpper() == "MATRIZ";

            // ✅ regla: si cliente es CEDIS y serie NO es matriz => NO aplica
            return !serieEsMatriz;
        }





        // GET: /Comercial/ObtenerTransferenciasExcedidas
        [HttpGet]
        public IActionResult ObtenerTransferenciasExcedidas()
        {
            var cr = new CultureInfo("es-MX");

            // =============================
            // 0) BASE: líneas de TRANSFERENCIAS pendientes
            // =============================
            var baseLines = (
                from t in _context.Transferencias
                join td in _context.TransferenciaDetalles on t.Id equals td.TransferenciaId
                join se in _context.Series on t.Sucursal equals se.Sucursal
                where t.Estatus != 0
                && t.Estatus != 5
                && (td.AutorizacionPresupuestoLinea == null
               || td.AutorizacionPresupuestoLinea == false)
                let fecha = t.FechaSolicitud ?? DateTime.MinValue    // 👈 normalizamos a DateTime
                select new
                {
                    Id = t.Id,
                    Consecutivo = t.Consecutivo,

                    // usamos la sucursal como “cliente” lógico
                    Cliente = t.Sucursal,
                    ClienteNombre = t.Sucursal,

                    Canal = (se.Canal ?? "").Trim().ToUpper(),  // CEDIS-CDMX, CEDIS-MTY, etc.

                    Fecha = fecha,                               // 👈 ahora es DateTime
                    liId = td.Id,
                    ProductoCodigo = td.ProductoCodigo,
                    ProductoNombre = td.ProductoNombre,
                    KilosLinea = (decimal?)td.CantidadKg ?? 0m
                }
            ).ToList();

            if (baseLines.Count == 0)
                return Json(Array.Empty<object>());

            // Periodos y claves a evaluar
            var clavesPeriodo = baseLines
                .Select(x => new
                {
                    Canal = x.Canal,
                    Prod = (x.ProductoCodigo ?? "").ToUpper(),
                    Mes = x.Fecha.Month,       // 👈 ya es DateTime, no DateTime?
                    Anio = x.Fecha.Year
                })
                .Distinct()
                .ToList();

            var meses = clavesPeriodo.Select(k => k.Mes).Distinct().ToList();
            var anios = clavesPeriodo.Select(k => k.Anio).Distinct().ToList();

            // =============================
            // 1) CONSUMOS POR CANAL (solo transferencias)
            // =============================
            var consumoCanal = (
                from t in _context.Transferencias
                join td in _context.TransferenciaDetalles on t.Id equals td.TransferenciaId
                join se in _context.Series on t.Sucursal equals se.Sucursal
                let fecha = t.FechaSolicitud ?? DateTime.MinValue    // 👈 otra vez, DateTime
                where meses.Contains(fecha.Month)
                   && anios.Contains(fecha.Year)
                   && t.Estatus != 0
                let canal = (se.Canal ?? "").Trim().ToUpper()
                where canal.StartsWith("CEDIS")
                group td by new
                {
                    Canal = canal,
                    SKU = td.ProductoCodigo.ToUpper(),
                    Mes = fecha.Month,
                    Anio = fecha.Year
                } into g
                select new
                {
                    g.Key.Canal,
                    g.Key.SKU,
                    g.Key.Mes,
                    g.Key.Anio,
                    Kg = g.Sum(x => (decimal?)x.CantidadKg) ?? 0m
                }
            ).ToList();

            var consumoCanalDict = consumoCanal.ToDictionary(
                x => (x.Canal, x.SKU, x.Mes, x.Anio),   // (string, string, int, int)
                x => x.Kg
            );

            // =============================
            // 2) PRESUPUESTO CEDIS
            // =============================
            var presupCedis = (
                from p in _context.PresupuestoCedis
                where meses.Contains(p.Mes)
                   && anios.Contains(p.Anio)
                select new
                {
                    Canal = (p.Canal ?? "").Trim().ToUpper(),
                    SKU = p.ProductoCodigo.ToUpper(),
                    p.Mes,
                    Anio = p.Anio,
                    Kg = (decimal?)p.PresupuestoAsignado ?? 0m
                }
            ).ToList();

            var cedisDict = presupCedis
                .GroupBy(x => (x.Canal, x.SKU, x.Mes, x.Anio))
                .ToDictionary(g => g.Key, g => g.Sum(z => z.Kg));

            // =============================
            // 3) PROYECCIÓN: EXCEDIDOS / SIN PRESUPUESTO
            // =============================
            var result = new List<object>();

            foreach (var x in baseLines)
            {
                var canal = x.Canal;
                var prod = (x.ProductoCodigo ?? "").ToUpper();
                var mes = x.Fecha.Month;
                var anio = x.Fecha.Year;

                decimal presupuesto = 0m;
                decimal consumo = 0m;
                string fuente = "";

                if (cedisDict.TryGetValue((canal, prod, mes, anio), out var presCedis) && presCedis > 0m)
                {
                    presupuesto = presCedis;
                    consumo = consumoCanalDict.TryGetValue((canal, prod, mes, anio), out var kgCanal)
                        ? kgCanal : 0m;
                    fuente = "CEDIS";
                }
                else
                {
                    presupuesto = 0m;
                    consumo = consumoCanalDict.TryGetValue((canal, prod, mes, anio), out var kgCanal)
                        ? kgCanal : 0m;
                    fuente = "SIN PRESUPUESTO";
                }

                var diferencia = presupuesto - consumo;

                if (presupuesto == 0m || diferencia < 0m)
                {
                    var estatus = (presupuesto == 0m) ? "sin presupuesto" : "excedido";

                    var mesNumero = mes;
                    var anioNumero = anio;

                    result.Add(new
                    {
                        // Identificadores
                        pedidoProductoId = x.liId,
                        id = x.Id,
                        ordenVenta = x.Consecutivo,   // aquí es el Consecutivo de la transferencia

                        // “Cliente” = sucursal
                        cliente = x.Cliente,
                        nombreCliente = x.ClienteNombre,

                        // Periodo
                        mes = new DateTime(anio, mes, 1).ToString("MMMM 'de' yyyy", cr),
                        mesNumero,
                        anio = anioNumero,

                        // Artículo
                        productoCodigo = x.ProductoCodigo,
                        productoNombre = x.ProductoNombre,

                        // Números
                        kilosSolicitados = Math.Round(x.KilosLinea, 2),
                        presupuesto = Math.Round(presupuesto, 2),
                        totalKilosMes = Math.Round(consumo, 2),
                        diferencia = Math.Round(diferencia, 2),

                        kilosSolicitadosTexto = $"{x.KilosLinea.ToString("N2", cr)} kg",
                        presupuestoTexto = $"{presupuesto.ToString("N2", cr)} kg",
                        totalKilosMesTexto = $"{consumo.ToString("N2", cr)} kg",
                        diferenciaTexto = $"{diferencia.ToString("N2", cr)} kg",

                        estatus,
                        fuentePresupuesto = fuente,
                        canalEfectivo = canal
                    });
                }
            }

            result = result
                .OrderBy(r => ((dynamic)r).ordenVenta)
                .ThenBy(r => ((dynamic)r).productoCodigo)
                .ToList();

            return Json(result);
        }




        private static string Up(string? s) => (s ?? "").Trim().ToUpperInvariant();

        private sealed class ExcedidoDto
        {
            public int pedidoProductoId { get; set; }
            public int id { get; set; }
            public string ordenVenta { get; set; } = "";

            public string cliente { get; set; } = "";
            public string nombreCliente { get; set; } = "";

            public string mes { get; set; } = "";
            public int mesNumero { get; set; }
            public int anio { get; set; }

            public string productoCodigo { get; set; } = "";
            public string productoNombre { get; set; } = "";

            public decimal kilosSolicitados { get; set; }
            public decimal presupuesto { get; set; }
            public decimal totalKilosMes { get; set; }
            public decimal diferencia { get; set; }

            public string kilosSolicitadosTexto { get; set; } = "";
            public string presupuestoTexto { get; set; } = "";
            public string totalKilosMesTexto { get; set; } = "";
            public string diferenciaTexto { get; set; } = "";

            public string estatus { get; set; } = "";
            public string fuentePresupuesto { get; set; } = "";
            public string canalEfectivo { get; set; } = "";

            // ✅ NUEVO: para detalle consumo cuando fuente=VENDEDOR
            public string vendedorId { get; set; } = "";

            public string modoPresupuesto { get; set; } = "";
        }






        private string NormalizarModo(string? modo)
        {
            var m = (modo ?? "").Trim().ToUpperInvariant();
            if (m == "CLI" || m == "CLIENTE") return "CLIENTE";
            if (m == "VEND" || m == "VENDEDOR") return "VENDEDOR";
            return "CLIENTE"; // ✅ recomendado para evitar que todo se vaya a vendedor
        }


        private string GetModoPresupuestoGuardado()
        {
            // 1) Session (rápido)
            var modo = HttpContext.Session.GetString(SessionKeyModoPresupuesto);
            modo = NormalizarModo(modo);

            // 2) (Opcional) Si quieres fallback a BD por usuario, aquí lo cargas:
            // var userId = User?.Identity?.Name;
            // var dbModo = _context.ModoPresupuestoUsuario
            //     .Where(x => x.UserName == userId)
            //     .Select(x => x.Modo)
            //     .FirstOrDefault();
            // if (!string.IsNullOrWhiteSpace(dbModo)) modo = NormalizarModo(dbModo);

            return modo;
        }


        private const string SessionKeyModoPresupuesto = "PRESUPUESTO_MODO"; // legacy
        private const string SESSION_MODO_PRES = "MODO_PRESUPUESTO";         // actual

        private string NormalizarModoPresupuesto(string? modo)
        {
            var m = (modo ?? "").Trim().ToUpperInvariant();
            if (m == "CLI" || m == "CLIENTE") return "CLIENTE";
            if (m == "VEND" || m == "VENDEDOR") return "VENDEDOR";
            return "VENDEDOR";
        }

        private string GetModoPresupuestoActual()
        {
            var raw = HttpContext.Session.GetString(SESSION_MODO_PRES)
                   ?? HttpContext.Session.GetString(SessionKeyModoPresupuesto)
                   ?? "VENDEDOR";

            return NormalizarModoPresupuesto(raw);
        }






        // GET: /Comercial/ObtenerProductosExcedidos
        [HttpGet]
        public IActionResult ObtenerProductosExcedidos()
        {
            var cr = new CultureInfo("es-MX");
            static string Up(string? s) => (s ?? "").Trim().ToUpperInvariant();

            var modoSesion = Up(GetModoPresupuestoActual()); // "VENDEDOR" | "CLIENTE"
            string ModoFinal(string? modoOv)
            {
                var m = Up(modoOv);
                return (m == "CLIENTE" || m == "VENDEDOR") ? m : modoSesion;
            }

            // =============================
            // 0) BASE: líneas pendientes
            // =============================
            var baseRaw = (
                from ov in _context.OrdenVenta.AsNoTracking()
                join li in _context.OrdenVentaProducto.AsNoTracking() on ov.Id equals li.PedidoId
                join cli in _context.ClienteSap.AsNoTracking() on ov.Cliente equals cli.Cliente
                join se in _context.Series.AsNoTracking() on ov.Serie equals se.NombreSerie
                where ov.Estatus == 2
                   && ov.AutorizacionPresupuesto == false
                   && (li.Eliminado == null || li.Eliminado == false)
                   && li.AutorizacionPresupuestoLinea == false
                select new
                {
                    ov.Id,
                    ov.Consecutivo,
                    ov.Cliente,
                    ClienteNombre = cli.Nombrecliente,

                    ov.ModoPresupuesto,

                    VendedorId = (int?)cli.VendedorId,   // <<<<<< aquí usas el id REAL

                    CanalCliente = cli.U_CANAL,
                    SucursalSerie = se.Sucursal,
                    CanalSerie = se.Canal,

                    ov.FechaEntrega,

                    liId = li.Id,
                    li.ProductoCodigo,
                    li.ProductoNombre,
                    KilosLinea = (decimal?)li.Peso ?? 0m
                }
            ).ToList();

            var baseLines = baseRaw.Select(x => new
            {
                x.Id,
                x.Consecutivo,

                Cliente = x.Cliente ?? "",
                ClienteUp = Up(x.Cliente),
                ClienteNombre = x.ClienteNombre ?? "",

                Modo = ModoFinal(x.ModoPresupuesto),
                VendedorId = x.VendedorId, // int?

                CanalClienteUp = Up(x.CanalCliente),
                SucursalSerieUp = Up(x.SucursalSerie),
                CanalSerieUp = Up(x.CanalSerie),

                x.FechaEntrega,

                x.liId,
                ProductoCodigo = x.ProductoCodigo ?? "",
                ProductoCodigoUp = Up(x.ProductoCodigo),
                ProductoNombre = x.ProductoNombre ?? "",
                x.KilosLinea
            }).ToList();

            if (baseLines.Count == 0)
                return Json(Array.Empty<object>());

            // =============================
            // Periodos / sets
            // =============================
            var periodos = baseLines
                .Select(x => new { Mes = x.FechaEntrega.Month, Anio = x.FechaEntrega.Year })
                .Distinct()
                .ToList();

            var meses = periodos.Select(p => p.Mes).Distinct().ToList();
            var anios = periodos.Select(p => p.Anio).Distinct().ToList();

            var minFecha = periodos.Select(p => new DateTime(p.Anio, p.Mes, 1)).Min();
            var maxFechaExcl = periodos.Select(p => new DateTime(p.Anio, p.Mes, 1)).Max().AddMonths(1);

            bool PeriodoEsValido(DateTime f) => periodos.Any(p => p.Mes == f.Month && p.Anio == f.Year);

            var skusUp = baseLines.Select(x => x.ProductoCodigoUp).Distinct().ToList();
            var clientesNecesariosUp = baseLines.Select(x => x.ClienteUp).Distinct().ToList();

            var vendedoresIdsNecesarios = baseLines
                .Where(x => x.VendedorId.HasValue && x.VendedorId.Value > 0)
                .Select(x => x.VendedorId!.Value)
                .Distinct()
                .ToList();

            var canalesEfNecesarios = baseLines
                .Select(x => (x.SucursalSerieUp == "MATRIZ") ? x.CanalClienteUp : x.CanalSerieUp)
                .Where(c => c.StartsWith("CEDIS"))
                .Distinct()
                .ToList();

            // =============================
            // 1) PRESUPUESTOS
            // =============================
            // 1.1 CEDIS
            var cedisDict = _context.PresupuestoCedis.AsNoTracking()
                .Where(p => meses.Contains(p.Mes)
                         && anios.Contains(p.Anio)
                         && canalesEfNecesarios.Contains((p.Canal ?? "").Trim().ToUpper())
                         && skusUp.Contains((p.ProductoCodigo ?? "").Trim().ToUpper()))
                .Select(p => new
                {
                    CanalUp = (p.Canal ?? "").Trim().ToUpper(),
                    SkuUp = (p.ProductoCodigo ?? "").Trim().ToUpper(),
                    p.Mes,
                    p.Anio,
                    Kg = (decimal?)p.PresupuestoAsignado ?? 0m
                })
                .ToList()
                .GroupBy(x => (x.CanalUp, x.SkuUp, x.Mes, x.Anio))
                .ToDictionary(g => g.Key, g => g.Sum(z => z.Kg));

            // 1.2 CLIENTE  ✅ OJO: aquí tu tabla tiene columna Presupuesto (NO PresupuestoAsignado)
            var cliDict = _context.Presupuestos.AsNoTracking()
                .Where(p => meses.Contains(p.Mes)
                         && anios.Contains(p.Año)
                         && clientesNecesariosUp.Contains((p.ClienteId ?? "").Trim().ToUpper())
                         && skusUp.Contains((p.ProductoCodigo ?? "").Trim().ToUpper()))
                .Select(p => new
                {
                    ClienteUp = (p.ClienteId ?? "").Trim().ToUpper(),
                    SkuUp = (p.ProductoCodigo ?? "").Trim().ToUpper(),
                    p.Mes,
                    Anio = p.Año,
                    Kg = (decimal?)p.PresupuestoAsignado ?? 0m   // <<<<<<< ESTE era el error
                })
                .ToList()
                .GroupBy(x => (x.ClienteUp, x.SkuUp, x.Mes, x.Anio))
                .ToDictionary(g => g.Key, g => g.Sum(z => z.Kg));

            // 1.3 VENDEDOR
            var vendDict = new Dictionary<(int VendedorId, string SkuUp, int Mes, int Anio), decimal>();
            if (vendedoresIdsNecesarios.Count > 0)
            {
                vendDict = _context.PresupuestoVendedor.AsNoTracking()
                    .Where(p => meses.Contains(p.Mes)
                             && anios.Contains(p.Anio)
                             && vendedoresIdsNecesarios.Contains(p.VendedorId)
                             && skusUp.Contains((p.ProductoCodigo ?? "").Trim().ToUpper()))
                    .Select(p => new
                    {
                        p.VendedorId,
                        SkuUp = (p.ProductoCodigo ?? "").Trim().ToUpper(),
                        p.Mes,
                        p.Anio,
                        Kg = (decimal?)p.PresupuestoAsignado ?? 0m
                    })
                    .ToList()
                    .GroupBy(x => (x.VendedorId, x.SkuUp, x.Mes, x.Anio))
                    .ToDictionary(g => g.Key, g => g.Sum(z => z.Kg));
            }

            // =============================
            // 2) CONSUMOS (para que SIEMPRE salga consumido)
            // =============================
            // 2.1 CLIENTE (todas las OV del cliente, sin importar modo)
            var consumoClienteDict = (
                from o in _context.OrdenVenta.AsNoTracking()
                join op in _context.OrdenVentaProducto.AsNoTracking() on o.Id equals op.PedidoId
                where o.Estatus != 0
                   && o.FechaEntrega >= minFecha && o.FechaEntrega < maxFechaExcl
                   && (op.Eliminado == null || op.Eliminado == false)
                   && clientesNecesariosUp.Contains((o.Cliente ?? "").Trim().ToUpper())
                   && skusUp.Contains((op.ProductoCodigo ?? "").Trim().ToUpper())
                select new
                {
                    ClienteUp = (o.Cliente ?? "").Trim().ToUpper(),
                    SkuUp = (op.ProductoCodigo ?? "").Trim().ToUpper(),
                    o.FechaEntrega,
                    Kg = (decimal?)op.Peso ?? 0m
                }
            ).ToList()
             .Where(x => PeriodoEsValido(x.FechaEntrega))
             .GroupBy(x => (x.ClienteUp, x.SkuUp, Mes: x.FechaEntrega.Month, Anio: x.FechaEntrega.Year))
             .ToDictionary(g => g.Key, g => g.Sum(z => z.Kg));

            // 2.2 VENDEDOR (todas las OV de clientes del vendedorId)
            var consumoVendDict = new Dictionary<(int VendedorId, string SkuUp, int Mes, int Anio), decimal>();
            if (vendedoresIdsNecesarios.Count > 0)
            {
                consumoVendDict = (
                    from o in _context.OrdenVenta.AsNoTracking()
                    join cli in _context.ClienteSap.AsNoTracking() on o.Cliente equals cli.Cliente
                    join op in _context.OrdenVentaProducto.AsNoTracking() on o.Id equals op.PedidoId
                    where o.Estatus != 0
                       && o.FechaEntrega >= minFecha && o.FechaEntrega < maxFechaExcl
                       && (op.Eliminado == null || op.Eliminado == false)
                       && cli.VendedorId.HasValue
                       && vendedoresIdsNecesarios.Contains(cli.VendedorId.Value)
                       && skusUp.Contains((op.ProductoCodigo ?? "").Trim().ToUpper())
                    select new
                    {
                        VendedorId = cli.VendedorId.Value,
                        SkuUp = (op.ProductoCodigo ?? "").Trim().ToUpper(),
                        o.FechaEntrega,
                        Kg = (decimal?)op.Peso ?? 0m
                    }
                ).ToList()
                 .Where(x => PeriodoEsValido(x.FechaEntrega))
                 .GroupBy(x => (x.VendedorId, x.SkuUp, Mes: x.FechaEntrega.Month, Anio: x.FechaEntrega.Year))
                 .ToDictionary(g => g.Key, g => g.Sum(z => z.Kg));
            }

            // 2.3 CEDIS (canal efectivo)
            var consumoCanalDict = new Dictionary<(string CanalUp, string SkuUp, int Mes, int Anio), decimal>();
            if (canalesEfNecesarios.Count > 0)
            {
                var raw = (
                    from o in _context.OrdenVenta.AsNoTracking()
                    join cli in _context.ClienteSap.AsNoTracking() on o.Cliente equals cli.Cliente
                    join se in _context.Series.AsNoTracking() on o.Serie equals se.NombreSerie
                    join op in _context.OrdenVentaProducto.AsNoTracking() on o.Id equals op.PedidoId
                    where o.Estatus != 0
                       && o.FechaEntrega >= minFecha && o.FechaEntrega < maxFechaExcl
                       && (op.Eliminado == null || op.Eliminado == false)
                       && skusUp.Contains((op.ProductoCodigo ?? "").Trim().ToUpper())
                    select new
                    {
                        CanalEf = (((se.Sucursal ?? "").Trim().ToUpper() == "MATRIZ") ? (cli.U_CANAL ?? "") : (se.Canal ?? "")),
                        Sku = op.ProductoCodigo,
                        o.FechaEntrega,
                        Kg = (decimal?)op.Peso ?? 0m
                    }
                ).ToList();

                consumoCanalDict = raw
                    .Select(x => new { CanalUp = Up(x.CanalEf), SkuUp = Up(x.Sku), x.FechaEntrega, x.Kg })
                    .Where(x => x.CanalUp.StartsWith("CEDIS")
                             && canalesEfNecesarios.Contains(x.CanalUp)
                             && PeriodoEsValido(x.FechaEntrega))
                    .GroupBy(x => (x.CanalUp, x.SkuUp, Mes: x.FechaEntrega.Month, Anio: x.FechaEntrega.Year))
                    .ToDictionary(g => g.Key, g => g.Sum(z => z.Kg));
            }

            // =============================
            // 3) RESULT (fuente según CEDIS / modo con fallback)
            // =============================
            var result = new List<ExcedidoDto>();

            foreach (var x in baseLines)
            {
                var mes = x.FechaEntrega.Month;
                var anio = x.FechaEntrega.Year;

                bool canalEsCedis = x.CanalClienteUp.StartsWith("CEDIS");
                bool esMatriz = x.SucursalSerieUp == "MATRIZ";
                string canalEfLinea = esMatriz ? x.CanalClienteUp : x.CanalSerieUp;

                // regla: CEDIS + serie NO MATRIZ -> no validar
                if (canalEsCedis && !esMatriz)
                    continue;

                decimal presupuesto = 0m;
                decimal consumo = 0m;
                string fuente = "";

                // 1) CEDIS prioridad (si existe)
                if (canalEsCedis && esMatriz
                    && cedisDict.TryGetValue((canalEfLinea, x.ProductoCodigoUp, mes, anio), out var presCedis)
                    && presCedis > 0m)
                {
                    presupuesto = presCedis;
                    consumo = consumoCanalDict.TryGetValue((canalEfLinea, x.ProductoCodigoUp, mes, anio), out var kgCanal) ? kgCanal : 0m;
                    fuente = "CEDIS";
                }
                else
                {
                    var vid = x.VendedorId ?? 0;

                    // ✅ según modo, pero con fallback si el principal no existe
                    if (x.Modo == "VENDEDOR")
                    {
                        if (vid > 0 && vendDict.TryGetValue((vid, x.ProductoCodigoUp, mes, anio), out var presVend) && presVend > 0m)
                        {
                            presupuesto = presVend;
                            consumo = consumoVendDict.TryGetValue((vid, x.ProductoCodigoUp, mes, anio), out var kgVend) ? kgVend : 0m;
                            fuente = "VENDEDOR";
                        }
                        else if (cliDict.TryGetValue((x.ClienteUp, x.ProductoCodigoUp, mes, anio), out var presCli2) && presCli2 > 0m)
                        {
                            presupuesto = presCli2;
                            consumo = consumoClienteDict.TryGetValue((x.ClienteUp, x.ProductoCodigoUp, mes, anio), out var kgCli2) ? kgCli2 : 0m;
                            fuente = "CLIENTE";
                        }
                        else
                        {
                            presupuesto = 0m;
                            consumo = (vid > 0 && consumoVendDict.TryGetValue((vid, x.ProductoCodigoUp, mes, anio), out var kgV) ? kgV : 0m);
                            fuente = "SIN PRESUPUESTO VENDEDOR";
                        }
                    }
                    else // CLIENTE
                    {
                        if (cliDict.TryGetValue((x.ClienteUp, x.ProductoCodigoUp, mes, anio), out var presCli) && presCli > 0m)
                        {
                            presupuesto = presCli;
                            consumo = consumoClienteDict.TryGetValue((x.ClienteUp, x.ProductoCodigoUp, mes, anio), out var kgCli) ? kgCli : 0m;
                            fuente = "CLIENTE";
                        }
                        else if (vid > 0 && vendDict.TryGetValue((vid, x.ProductoCodigoUp, mes, anio), out var presVend2) && presVend2 > 0m)
                        {
                            presupuesto = presVend2;
                            consumo = consumoVendDict.TryGetValue((vid, x.ProductoCodigoUp, mes, anio), out var kgVend2) ? kgVend2 : 0m;
                            fuente = "VENDEDOR";
                        }
                        else
                        {
                            presupuesto = 0m;
                            consumo = consumoClienteDict.TryGetValue((x.ClienteUp, x.ProductoCodigoUp, mes, anio), out var kgC) ? kgC : 0m;
                            fuente = "SIN PRESUPUESTO CLIENTE";
                        }
                    }
                }

                var diferencia = presupuesto - consumo;

                if (presupuesto == 0m || diferencia < 0m)
                {
                    var estatus = (presupuesto == 0m) ? "sin presupuesto" : "excedido";

                    result.Add(new ExcedidoDto
                    {
                        pedidoProductoId = x.liId,
                        id = x.Id,
                        ordenVenta = (x.Consecutivo ?? "").ToString()!,

                        cliente = x.Cliente,
                        nombreCliente = x.ClienteNombre,

                        mes = new DateTime(anio, mes, 1).ToString("MMMM 'de' yyyy", cr),
                        mesNumero = mes,
                        anio = anio,

                        productoCodigo = x.ProductoCodigo,
                        productoNombre = x.ProductoNombre,

                        kilosSolicitados = Math.Round(x.KilosLinea, 2),
                        presupuesto = Math.Round(presupuesto, 2),
                        totalKilosMes = Math.Round(consumo, 2),
                        diferencia = Math.Round(diferencia, 2),

                        kilosSolicitadosTexto = $"{x.KilosLinea.ToString("N2", cr)} kg",
                        presupuestoTexto = $"{presupuesto.ToString("N2", cr)} kg",
                        totalKilosMesTexto = $"{consumo.ToString("N2", cr)} kg",
                        diferenciaTexto = $"{diferencia.ToString("N2", cr)} kg",

                        estatus = estatus,
                        fuentePresupuesto = fuente,
                        canalEfectivo = canalEfLinea,

                        vendedorId = (x.VendedorId?.ToString() ?? ""),
                        modoPresupuesto = x.Modo
                    });
                }
            }

            result = result.OrderBy(r => r.ordenVenta).ThenBy(r => r.productoCodigo).ToList();
            return Json(result);
        }














        private async Task<bool> HayLineasPendientesPresupuestoAsync(int ovId)
        {
            var cr = new CultureInfo("es-MX");

            // =============================
            // 0) BASE: líneas candidatas SOLO de esa OV
            // =============================
            var baseLines = await (
                from ov in _context.OrdenVenta
                join li in _context.OrdenVentaProducto on ov.Id equals li.PedidoId
                join cli in _context.ClienteSap on ov.Cliente equals cli.Cliente
                join se in _context.Series on ov.Serie equals se.NombreSerie
                where ov.Id == ovId
                   && ov.Estatus != 0           // excluye canceladas
                   && ov.Estatus == 2           // en proceso/autorización
                   && ov.AutorizacionPresupuesto == false
                   && (li.Eliminado == null || li.Eliminado == false)
                   && li.AutorizacionPresupuestoLinea == false   // ⬅️ SOLO líneas pendientes (según flag)
                select new
                {
                    ov.Id,
                    ov.Consecutivo,
                    ov.Cliente,
                    ClienteNombre = cli.Nombrecliente,

                    CanalCliente = (cli.U_CANAL ?? "").Trim().ToUpper(),
                    SucursalSerie = (se.Sucursal ?? "").Trim().ToUpper(),
                    CanalSerie = (se.Canal ?? "").Trim().ToUpper(),

                    ov.FechaEntrega,
                    liId = li.Id,
                    li.ProductoCodigo,
                    li.ProductoNombre,
                    KilosLinea = (decimal?)li.Peso ?? 0m
                }
            ).ToListAsync();

            // Si ya no hay ni siquiera líneas con flag 0 -> no hay pendientes
            if (baseLines.Count == 0)
                return false;

            // =============================
            // Periodos
            // =============================
            var clavesPeriodo = baseLines
                .Select(x => new
                {
                    Cliente = x.Cliente.ToUpper(),
                    Prod = (x.ProductoCodigo ?? "").ToUpper(),
                    Mes = x.FechaEntrega.Month,
                    Anio = x.FechaEntrega.Year
                })
                .Distinct()
                .ToList();

            var meses = clavesPeriodo.Select(k => k.Mes).Distinct().ToList();
            var anios = clavesPeriodo.Select(k => k.Anio).Distinct().ToList();

            // =============================
            // 1) CONSUMOS CLIENTE
            // =============================
            var consumoCliente = await (
                from o in _context.OrdenVenta
                where meses.Contains(o.FechaEntrega.Month)
                   && anios.Contains(o.FechaEntrega.Year)
                   && o.Estatus != 0
                join op in _context.OrdenVentaProducto on o.Id equals op.PedidoId
                where (op.Eliminado == null || op.Eliminado == false)
                group op by new
                {
                    Cliente = o.Cliente.ToUpper(),
                    SKU = op.ProductoCodigo.ToUpper(),
                    Mes = o.FechaEntrega.Month,
                    Anio = o.FechaEntrega.Year
                } into g
                select new
                {
                    g.Key.Cliente,
                    g.Key.SKU,
                    g.Key.Mes,
                    g.Key.Anio,
                    Kg = g.Sum(x => (decimal?)x.Peso) ?? 0m
                }
            ).ToListAsync();

            var consumoClienteDict = consumoCliente.ToDictionary(
                x => (x.Cliente, x.SKU, x.Mes, x.Anio),
                x => x.Kg
            );

            // =============================
            // 1b) CONSUMOS CANAL (CEDIS)
            // =============================
            var consumoCanal = await (
                from o in _context.OrdenVenta
                join cli in _context.ClienteSap on o.Cliente equals cli.Cliente
                join se in _context.Series on o.Serie equals se.NombreSerie
                where meses.Contains(o.FechaEntrega.Month)
                   && anios.Contains(o.FechaEntrega.Year)
                   && o.Estatus != 0
                join op in _context.OrdenVentaProducto on o.Id equals op.PedidoId
                where (op.Eliminado == null || op.Eliminado == false)
                let canalCliente = (cli.U_CANAL ?? "").Trim().ToUpper()
                let sucursalSerie = (se.Sucursal ?? "").Trim().ToUpper()
                let canalSerie = (se.Canal ?? "").Trim().ToUpper()
                let canalEf = (sucursalSerie == "MATRIZ" ? canalCliente : canalSerie)
                where canalEf.StartsWith("CEDIS")
                group new { op, canalEf } by new
                {
                    Canal = canalEf,
                    SKU = op.ProductoCodigo.ToUpper(),
                    Mes = o.FechaEntrega.Month,
                    Anio = o.FechaEntrega.Year
                } into g
                select new
                {
                    g.Key.Canal,
                    g.Key.SKU,
                    g.Key.Mes,
                    g.Key.Anio,
                    Kg = g.Sum(x => (decimal?)x.op.Peso) ?? 0m
                }
            ).ToListAsync();

            var consumoCanalDict = consumoCanal.ToDictionary(
                x => (x.Canal, x.SKU, x.Mes, x.Anio),
                x => x.Kg
            );

            // =============================
            // 2) PRESUPUESTO CEDIS
            // =============================
            var presupCedis = await (
                from p in _context.PresupuestoCedis
                where meses.Contains(p.Mes)
                   && anios.Contains(p.Anio)
                select new
                {
                    Canal = (p.Canal ?? "").Trim().ToUpper(),
                    SKU = p.ProductoCodigo.ToUpper(),
                    p.Mes,
                    Anio = p.Anio,
                    Kg = (decimal?)p.PresupuestoAsignado ?? 0m
                }
            ).ToListAsync();

            var cedisDict = presupCedis
                .GroupBy(x => (x.Canal, x.SKU, x.Mes, x.Anio))
                .ToDictionary(g => g.Key, g => g.Sum(z => z.Kg));

            // =============================
            // 3) PRESUPUESTO CLIENTE
            // =============================
            var presupCliente = await (
                from p in _context.Presupuestos
                where meses.Contains(p.Mes)
                   && anios.Contains(p.Año)
                select new
                {
                    Cliente = (p.ClienteId ?? "").Trim().ToUpper(),
                    SKU = p.ProductoCodigo.ToUpper(),
                    Mes = p.Mes,
                    Anio = p.Año,
                    Kg = (decimal?)p.PresupuestoAsignado ?? 0m
                }
            ).ToListAsync();

            var cliDict = presupCliente
                .GroupBy(x => (x.Cliente, x.SKU, x.Mes, x.Anio))
                .ToDictionary(g => g.Key, g => g.Sum(z => z.Kg));

            // =============================
            // 4) MISMA LÓGICA QUE ObtenerProductosExcedidos
            // =============================
            foreach (var x in baseLines)
            {
                var cliente = x.Cliente.ToUpper();
                var prod = (x.ProductoCodigo ?? "").ToUpper();
                var mes = x.FechaEntrega.Month;
                var anio = x.FechaEntrega.Year;

                var canalCliente = x.CanalCliente;
                var sucursal = x.SucursalSerie;
                var canalSerie = x.CanalSerie;

                bool canalEsCedis = canalCliente.StartsWith("CEDIS");
                bool esMatriz = sucursal == "MATRIZ";

                string canalEfLinea = esMatriz ? canalCliente : canalSerie;

                // cliente CEDIS + NO MATRIZ → no participa en presupuesto
                if (canalEsCedis && !esMatriz)
                    continue;

                decimal presupuesto = 0m;
                decimal consumo = 0m;

                // 🔧 AQUÍ EL ARREGLO DE presCedisVal
                decimal presCedisVal = 0m;
                bool tieneCedis = false;

                if (canalEsCedis
                    && esMatriz
                    && !string.IsNullOrWhiteSpace(canalEfLinea)
                    && cedisDict.TryGetValue((canalEfLinea, prod, mes, anio), out presCedisVal)
                    && presCedisVal > 0m)
                {
                    tieneCedis = true;
                }

                if (tieneCedis)
                {
                    presupuesto = presCedisVal;
                    consumo = consumoCanalDict.TryGetValue((canalEfLinea, prod, mes, anio), out var kgCanal)
                        ? kgCanal : 0m;
                }
                else
                {
                    if (cliDict.TryGetValue((cliente, prod, mes, anio), out var presCli) && presCli > 0m)
                    {
                        presupuesto = presCli;
                        consumo = consumoClienteDict.TryGetValue((cliente, prod, mes, anio), out var kgCli)
                            ? kgCli : 0m;
                    }
                    else
                    {
                        presupuesto = 0m;
                        consumo = consumoClienteDict.TryGetValue((cliente, prod, mes, anio), out var kgCli2)
                            ? kgCli2 : 0m;
                    }
                }

                var diferencia = presupuesto - consumo;

                // mismas condiciones que en tu vista:
                //   - presupuesto == 0  -> "sin presupuesto"
                //   - diferencia < 0    -> "excedido"
                if (presupuesto == 0m || diferencia < 0m)
                {
                    // Hay al menos UNA línea que seguiría apareciendo en la vista
                    return true;
                }
            }

            // Si ninguna línea entró como "sin presupuesto" o "excedido" → ya no hay pendientes
            return false;
        }





        // =============================
        // DETALLE DE PRESUPUESTO (OV + TRANSFERENCIAS)
        // =============================

        // GET: /Comercial/DetalleConsumoPresupuesto


        [HttpGet]
        public IActionResult DetalleConsumoPresupuesto(string tipo, string id, string sku, int mes, int anio)
        {
            tipo = (tipo ?? "").Trim().ToUpperInvariant();    // "CLIENTE" | "CANAL" | "VENDEDOR"
            id = (id ?? "").Trim().ToUpperInvariant();
            sku = (sku ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(sku) || mes <= 0 || anio <= 0)
                return Json(Array.Empty<object>());

            var inicioMes = new DateTime(anio, mes, 1);
            var finMesExcl = inicioMes.AddMonths(1);

            var dataTotal = new List<ConsumoDetalleDto>();

            // =============================
            // 1) BASE: ORDENES DE VENTA
            // =============================
            var qBaseOV =
                from o in _context.OrdenVenta.AsNoTracking()
                join cli in _context.ClienteSap.AsNoTracking() on o.Cliente equals cli.Cliente
                join se in _context.Series.AsNoTracking() on o.Serie equals se.NombreSerie
                join op in _context.OrdenVentaProducto.AsNoTracking() on o.Id equals op.PedidoId
                where o.Estatus != 0
                   && (op.Eliminado == null || op.Eliminado == false)
                   && o.FechaEntrega >= inicioMes && o.FechaEntrega < finMesExcl
                   && ((op.ProductoCodigo ?? "").Trim().ToUpper() == sku)
                let clienteUp = ((o.Cliente ?? "").Trim().ToUpper())
                let canalClienteUp = ((cli.U_CANAL ?? "").Trim().ToUpper())
                let sucursalSerieUp = ((se.Sucursal ?? "").Trim().ToUpper())
                let canalSerieUp = ((se.Canal ?? "").Trim().ToUpper())
                let canalEfUp = (sucursalSerieUp == "MATRIZ" ? canalClienteUp : canalSerieUp)
                let vendedorIdInt = (cli.VendedorId ?? 0)   // ✅ INT REAL
                select new
                {
                    o.Id,
                    o.Consecutivo,
                    o.FechaEntrega,
                    ClienteId = clienteUp,
                    ClienteNombre = cli.Nombrecliente,
                    CanalEfectivo = canalEfUp,
                    VendedorId = vendedorIdInt,             // ✅ INT
                    Kg = (decimal?)op.Peso ?? 0m
                };

            if (tipo == "CLIENTE")
            {
                qBaseOV = qBaseOV.Where(x => x.ClienteId == id);
            }
            else if (tipo == "CANAL")
            {
                qBaseOV = qBaseOV.Where(x => x.CanalEfectivo == id);
            }
            else if (tipo == "VENDEDOR")
            {
                if (!int.TryParse(id, out var vid) || vid <= 0)
                    return Json(Array.Empty<object>());

                qBaseOV = qBaseOV.Where(x => x.VendedorId == vid); // ✅ ya compara int con int
            }
            else
            {
                return Json(Array.Empty<object>());
            }

            var dataOV = qBaseOV
                .GroupBy(x => new { x.Id, x.Consecutivo, x.FechaEntrega, x.ClienteId, x.ClienteNombre })
                .Select(g => new ConsumoDetalleDto
                {
                    origen = "OV",
                    idDocumento = g.Key.Id,
                    folio = g.Key.Consecutivo,
                    fechaEntrega = g.Key.FechaEntrega,
                    cliente = g.Key.ClienteId,
                    nombreCliente = g.Key.ClienteNombre,
                    kilos = g.Sum(z => z.Kg)
                })
                .ToList();

            dataTotal.AddRange(dataOV);

            // =============================
            // 2) TRANSFERENCIAS (solo CANAL)
            // =============================
            if (tipo == "CANAL")
            {
                var qBaseTr =
                    from t in _context.Transferencias.AsNoTracking()
                    join td in _context.TransferenciaDetalles.AsNoTracking() on t.Id equals td.TransferenciaId
                    join se in _context.Series.AsNoTracking() on t.Sucursal equals se.Sucursal
                    where t.Estatus != 0
                       && t.FechaSolicitud.HasValue
                       && t.FechaSolicitud.Value >= inicioMes && t.FechaSolicitud.Value < finMesExcl
                       && ((td.ProductoCodigo ?? "").Trim().ToUpper() == sku)
                    let canalUp = ((se.Canal ?? "").Trim().ToUpper())
                    where canalUp.StartsWith("CEDIS") && canalUp == id
                    group td by new
                    {
                        t.Id,
                        t.Consecutivo,
                        Fecha = t.FechaSolicitud.Value,
                        ClienteId = (t.Sucursal ?? "").Trim().ToUpper(),
                        ClienteNombre = t.Sucursal
                    } into g
                    select new ConsumoDetalleDto
                    {
                        origen = "TR",
                        idDocumento = g.Key.Id,
                        folio = g.Key.Consecutivo,
                        fechaEntrega = g.Key.Fecha,
                        cliente = g.Key.ClienteId,
                        nombreCliente = g.Key.ClienteNombre,
                        kilos = g.Sum(z => (decimal?)z.CantidadKg) ?? 0m
                    };

                dataTotal.AddRange(qBaseTr.ToList());
            }

            return Json(dataTotal.OrderByDescending(x => x.fechaEntrega).ToList());
        }

        private sealed class ConsumoDetalleDto
        {
            public string origen { get; set; } = "";
            public int idDocumento { get; set; }
            public string folio { get; set; } = "";
            public DateTime fechaEntrega { get; set; }
            public string cliente { get; set; } = "";
            public string nombreCliente { get; set; } = "";
            public decimal kilos { get; set; }
        }


























        //// GET: /Comercial/ObtenerProductosPrecioExcedido?fechaEntrega=2025-10-29
        ////Pedidos con excedido en el precio y autorizacion
        //[HttpGet]
        //public async Task<IActionResult> ObtenerProductosPrecioExcedido()
        //{


        //    // 👉 Trae ovId y lineaId explícitos
        //    var datos = await (
        //        from ov in _context.OrdenVenta
        //        join ovp in _context.OrdenVentaProducto on ov.Id equals ovp.PedidoId
        //        where ov.Estatus == 2
        //              && ov.AutorizacionPrecio == false
        //        select new
        //        {
        //            ovId = ov.Id,                     // cabecera
        //            lineaId = ovp.Id,                    // 👈 ESTE es el id de la línea (OrdenVentaProducto.Id)
        //            ordenVenta = ov.Consecutivo ?? "-",
        //            cliente = ov.Cliente ?? "-",
        //            fechaEntrega = ov.FechaEntrega,
        //            productoCod = ovp.ProductoCodigo,
        //            productoNom = ovp.ProductoNombre ?? "-",
        //            precioOV = ovp.Precio
        //        }
        //    ).ToListAsync();

        //    var resultado = new List<object>();

        //    // (Opcional) cache para no llamar SAP repetido por (cliente, producto)
        //    var cache = new Dictionary<(string cli, string prod), decimal>();

        //    foreach (var item in datos)
        //    {
        //        decimal precioLista = 0;
        //        var k = (item.cliente, item.productoCod);

        //        if (!cache.TryGetValue(k, out precioLista))
        //        {
        //            try
        //            {
        //                var precioSAP = await _sap.ObtenerPrecioArticuloPorClienteAsync(item.cliente, item.productoCod);
        //                precioLista = precioSAP?.Precio ?? 0m;
        //            }
        //            catch
        //            {
        //                precioLista = 0m;
        //            }
        //            cache[k] = precioLista;
        //        }

        //        if (item.precioOV < precioLista)
        //        {
        //            resultado.Add(new
        //            {
        //                lineaId = item.lineaId,                  // 👈 id de la línea (para el botón Autorizar)
        //                ovId = item.ovId,                     //     id de cabecera (por si lo necesitas)
        //                ordenVenta = item.ordenVenta,
        //                cliente = item.cliente,
        //                producto = item.productoNom,
        //                precioLista = precioLista,
        //                precioOV = item.precioOV,
        //                diferencia = precioLista - item.precioOV,
        //                estadoPrecio = "Excedido",
        //                fechaCompleta = item.fechaEntrega.ToString("yyyy-MM-dd")
        //            });
        //        }
        //    }

        //    // Ordena por OV y luego por línea si quieres
        //    return Json(resultado
        //        .OrderBy(x => ((dynamic)x).ovId)
        //        .ThenBy(x => ((dynamic)x).lineaId));
        //}



        //======================================
        // AUTORIZACION DE PRECIO DESDE SQL LOCAL
        // Incluye casos: precio de lista = 0 o inexistente -> Excedido
        // Incluye Serie y Vendedor
        // Filtra por series asignadas al usuario
        //======================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ObtenerProductosPrecioExcedido()
        {
            const decimal TOL = 0.01m;

            // =====================================================
            // SERIES ASIGNADAS AL USUARIO
            // Usa los mismos métodos que ya utilizas en orden_venta.
            // Si tu usuario es admin o puede ver todas, no aplica filtro.
            // =====================================================
            var verTodasSeries = UsuarioPuedeVerTodasLasSeries();

            var seriesPermitidas = new List<string>();

            if (!verTodasSeries)
            {
                var idsSeries = await ObtenerSeriesIdsUsuarioActualAsync();

                if (idsSeries == null || !idsSeries.Any())
                    return Json(Array.Empty<object>());

                seriesPermitidas = await _context.Series
                    .AsNoTracking()
                    .Where(s => idsSeries.Contains(s.Id))
                    .Select(s => s.NombreSerie)
                    .ToListAsync();

                seriesPermitidas = seriesPermitidas
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpper())
                    .Distinct()
                    .ToList();

                if (!seriesPermitidas.Any())
                    return Json(Array.Empty<object>());
            }

            var query =
                from ov in _context.OrdenVenta.AsNoTracking()
                join ovp in _context.OrdenVentaProducto.AsNoTracking()
                    on ov.Id equals ovp.PedidoId
                join cli in _context.ClienteSap.AsNoTracking()
                    on ov.Cliente equals cli.Cliente into cliJoin
                from c in cliJoin.DefaultIfEmpty()
                where ov.Estatus == 2
                      && ov.AutorizacionPrecio == false
                select new
                {
                    ovId = ov.Id,
                    lineaId = ovp.Id,

                    ordenVenta = ov.Consecutivo ?? "-",

                    // NUEVO
                    serie = ov.Serie ?? "-",
                    vendedor = ov.Vendedor ?? "-",

                    clienteId = ov.Cliente ?? "-",
                    clienteNombre = c != null ? (c.Nombrecliente ?? ov.Cliente ?? "-") : (ov.Cliente ?? "-"),
                    fechaEntrega = ov.FechaEntrega,
                    productoCod = ovp.ProductoCodigo,
                    productoNom = ovp.ProductoNombre ?? "-",
                    precioOV = (decimal?)ovp.Precio ?? 0m,

                    // CAMBIA ESTE CAMPO por el real en tu modelo
                    kilosOV = (decimal?)ovp.Peso ?? 0m
                };

            // =====================================================
            // FILTRO POR SERIE ASIGNADA AL USUARIO
            // =====================================================
            if (!verTodasSeries)
            {
                query = query.Where(x =>
                    x.serie != null &&
                    seriesPermitidas.Contains(x.serie.Trim().ToUpper())
                );
            }

            var datos = await query.ToListAsync();

            if (datos.Count == 0)
                return Json(Array.Empty<object>());

            var clientes = datos
                .Select(x => (x.clienteId ?? "").Trim().ToUpper())
                .Distinct()
                .ToList();

            var productos = datos
                .Select(x => (x.productoCod ?? "").Trim().ToUpper())
                .Distinct()
                .ToList();

            var ultimos = await _context.CatalogoPrecioSap.AsNoTracking()
                .Where(c => clientes.Contains((c.Cliente ?? "").ToUpper())
                         && productos.Contains((c.ProductoCodigo ?? "").ToUpper()))
                .GroupBy(c => new
                {
                    Cli = (c.Cliente ?? "").ToUpper(),
                    Prod = (c.ProductoCodigo ?? "").ToUpper()
                })
                .Select(g => g
                    .OrderByDescending(x => x.FechaModificacion)
                    .Select(x => new
                    {
                        Cliente = (x.Cliente ?? "").ToUpper(),
                        ProductoCodigo = (x.ProductoCodigo ?? "").ToUpper(),
                        x.Precio,
                        x.PriceListName
                    })
                    .FirstOrDefault()
                )
                .ToListAsync();

            static string Key(string cli, string prod)
                => $"{(cli ?? "").Trim().ToUpper()}|{(prod ?? "").Trim().ToUpper()}";

            var mapPrecio = ultimos
                .Where(x => x != null)
                .GroupBy(x => Key(x.Cliente, x.ProductoCodigo))
                .ToDictionary(g => g.Key, g => g.First());

            var resultado = datos.Select(l =>
            {
                var k = Key(l.clienteId, l.productoCod);
                var pr = mapPrecio.TryGetValue(k, out var v) ? v : null;

                var precioLista = pr?.Precio ?? 0m;
                var nombreLista = pr?.PriceListName ?? string.Empty;
                var ovPrice = l.precioOV;

                bool sinPrecioLista = precioLista <= 0m;
                bool menorALista = !sinPrecioLista && (ovPrice + TOL) < precioLista;

                string motivo;
                if (sinPrecioLista) motivo = "Sin precio de lista";
                else if (menorALista) motivo = "Precio OV menor a lista";
                else motivo = "Dentro de lista";

                bool excedido = sinPrecioLista || menorALista;
                var dif = precioLista - ovPrice;

                return new
                {
                    lineaId = l.lineaId,
                    ovId = l.ovId,

                    ordenVenta = l.ordenVenta,

                    // NUEVO
                    serie = l.serie,
                    vendedor = string.IsNullOrWhiteSpace(l.vendedor) ? "-" : l.vendedor,

                    clienteId = l.clienteId,
                    clienteNombre = l.clienteNombre,
                    productoCodigo = l.productoCod,
                    producto = l.productoNom,
                    listaNombre = nombreLista,

                    precioLista = precioLista,
                    precioOV = ovPrice,
                    diferencia = dif,
                    estadoPrecio = excedido ? "Excedido" : "Dentro",
                    motivo = motivo,

                    kilosOV = l.kilosOV,

                    fechaCompleta = l.fechaEntrega != null
                        ? l.fechaEntrega.ToString("yyyy-MM-dd")
                        : ""
                };
            })
            .Where(x => x.estadoPrecio == "Excedido")
            .OrderBy(x => x.serie)
            .ThenBy(x => x.ordenVenta)
            .ThenBy(x => x.lineaId)
            .ToList();

            return Json(resultado);
        }































        // GET: /Comercial/ObtenerOrdenesConCredito
        // Muestra TODAS las OV con crédito pendiente (Estatus=2 y AutorizacionCredito=0)
        [HttpGet]
        public async Task<IActionResult> ObtenerOrdenesConCredito()
        {
            var datos = await (
                from ov in _context.OrdenVenta.AsNoTracking()
                join ovp in _context.OrdenVentaProducto.AsNoTracking()
                    on ov.Id equals ovp.PedidoId
                join c in _context.ClienteSap.AsNoTracking()
                    on ov.Cliente equals c.Cliente into jcli
                from c in jcli.DefaultIfEmpty()
                where ov.Estatus == 2
                      && (ov.AutorizacionCredito == false || ov.AutorizacionCredito == null)
                      && (ovp.Eliminado == false || ovp.Eliminado == null)   // opcional, por si manejas eliminados lógicos
                select new
                {
                    ov.Id,
                    Consecutivo = ov.Consecutivo ?? "-",
                    Cliente = ov.Cliente ?? "-",
                    ClienteNombre = c != null ? c.Nombrecliente : "-",
                    FechaEntrega = ov.FechaEntrega,

                    ProductoCodigo = ovp.ProductoCodigo,
                    ProductoNombre = ovp.ProductoNombre ?? "-",

                    PrecioOV = (decimal?)ovp.Precio,
                    Kg = (decimal?)ovp.Peso,
                    ImporteLinea = (decimal?)ovp.Importe
                }
            ).ToListAsync();

            if (datos.Count == 0)
                return Json(Array.Empty<object>());

            var ordenesAgrupadas = datos
                .GroupBy(d => new
                {
                    d.Id,
                    d.Consecutivo,
                    d.Cliente,
                    d.ClienteNombre,
                    d.FechaEntrega
                });

            var resultado = new List<object>();

            foreach (var grupo in ordenesAgrupadas)
            {
                decimal kgOv = grupo.Sum(x => x.Kg ?? 0m);

                decimal importePedido = grupo.Sum(x =>
                    x.ImporteLinea ?? ((x.PrecioOV ?? 0m) * (x.Kg ?? 0m))
                );

                decimal limiteCredito = 0m;
                decimal saldoActual = 0m;
                decimal otrosPedidos = 0m;

                try
                {
                    var cli = (grupo.Key.Cliente ?? "-").Trim();
                    var clienteSAP = await _sap.ObtenerClientePorCodigoAsync(cli);

                    if (clienteSAP != null)
                    {
                        limiteCredito = clienteSAP.CreditLimit;
                        otrosPedidos = clienteSAP.TotalPendiente;
                        saldoActual = clienteSAP.CurrentAccountBalance;
                    }
                }
                catch
                {
                    // Si SAP falla, se quedan valores en 0
                }

                decimal disponible = limiteCredito - (saldoActual + otrosPedidos);
                decimal excede = importePedido - disponible;
                decimal montoExcedido = excede > 0 ? excede : 0m;

                resultado.Add(new
                {
                    OrdenVenta = grupo.Key.Consecutivo,
                    Cliente = grupo.Key.Cliente,
                    ClienteNombre = grupo.Key.ClienteNombre,
                    FechaEntrega = grupo.Key.FechaEntrega.ToString("yyyy-MM-dd"),

                    KgOv = kgOv,                      // <-- AQUÍ VAN LOS KG TOTALES DE LA OV
                    ImportePedido = importePedido,
                    LimiteCredito = limiteCredito,
                    SaldoActual = saldoActual,
                    OtrosPedidos = otrosPedidos,
                    Disponible = disponible,
                    Excede = excede,
                    MontoExcedido = montoExcedido
                });
            }

            return Json(resultado.OrderBy(x => ((dynamic)x).OrdenVenta));
        }



        private async Task<bool> HayPendientesEnOvAsync(int ovId)
        {
            var ov = await _context.OrdenVenta.FindAsync(ovId);
            if (ov == null) return false;

            int mes = ov.FechaEntrega.Month;
            int anio = ov.FechaEntrega.Year;

            string cliente = (ov.Cliente ?? "").Trim().ToUpper();

            // ============================
            // 1) Canal del cliente (SAP)
            // ============================
            var canalRaw = await _context.ClienteSap
                .Where(c => (c.Cliente ?? "").Trim().ToUpper() == cliente)
                .Select(c => c.U_CANAL)
                .FirstOrDefaultAsync();

            string canalUp = (canalRaw ?? "").Trim().ToUpper();

            // ============================
            // 2) Sucursal de la serie de ESTA OV
            //    (para saber si es MATRIZ o no)
            // ============================
            var serieInfo = await _context.Series
                .Where(s => s.NombreSerie == ov.Serie)
                .Select(s => new
                {
                    Sucursal = (s.Sucursal ?? "").Trim().ToUpper(),
                    CanalSerie = (s.Canal ?? "").Trim().ToUpper()
                })
                .FirstOrDefaultAsync();

            string sucursalSerie = serieInfo?.Sucursal ?? "";
            // string canalSerie   = serieInfo?.CanalSerie ?? ""; // por ahora no lo usamos

            bool esCanalCedis = canalUp.StartsWith("CEDIS");  // cliente pertenece a canal CEDIS...
            bool esSerieMatriz = sucursalSerie == "MATRIZ";

            // ============================
            // 3) Regla de negocio:
            //    - Cliente CEDIS + Serie NO MATRIZ => NO se valida presupuesto
            //    - Cliente CEDIS + Serie MATRIZ    => usa PresupuestoCedis
            //    - Cliente NO CEDIS                => usa Presupuesto normal por cliente
            // ============================
            if (esCanalCedis && !esSerieMatriz)
            {
                // Es un cliente de canal CEDIS, pero la OV NO es de sucursal MATRIZ:
                // por regla tuya, no entra al juego de presupuesto → no hay pendientes.
                return false;
            }

            // ¿Hay presupuesto CEDIS para este canal/periodo?
            bool hayPresCedisPeriodo = esCanalCedis && esSerieMatriz &&
                await _context.PresupuestoCedis.AnyAsync(pc =>
                    ((pc.Canal ?? "").Trim().ToUpper() == canalUp) &&
                    pc.Mes == mes && pc.Anio == anio);

            // ============================
            // 4) Líneas de ESTA OV
            // ============================
            var lineas = await _context.OrdenVentaProducto
                .Where(l => l.PedidoId == ov.Id && (l.Eliminado == null || l.Eliminado == false))
                .Select(l => new
                {
                    SKU = (l.ProductoCodigo ?? "").Trim().ToUpper(),
                    Kg = l.Peso,
                    Aut = l.AutorizacionPresupuestoLinea
                })
                .ToListAsync();

            var skusPend = lineas
                .Where(x => !x.Aut && x.Kg > 0m && !string.IsNullOrWhiteSpace(x.SKU))
                .Select(x => x.SKU)
                .Distinct()
                .ToList();

            if (skusPend.Count == 0) return false;

            // ============================
            // 5) Presupuesto por SKU
            // ============================
            Dictionary<string, decimal> presupDict;
            if (hayPresCedisPeriodo)
            {
                // CEDIS + MATRIZ → PresupuestoCedis por canal
                presupDict = await _context.PresupuestoCedis
                    .Where(pc => ((pc.Canal ?? "").Trim().ToUpper() == canalUp)
                              && pc.Mes == mes && pc.Anio == anio
                              && skusPend.Contains((pc.ProductoCodigo ?? "").Trim().ToUpper()))
                    .GroupBy(pc => (pc.ProductoCodigo ?? "").Trim().ToUpper())
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Sum(v => v.PresupuestoAsignado)
                    );
            }
            else
            {
                // Resto de casos → Presupuesto normal por CLIENTE
                presupDict = await _context.Presupuestos
                    .Where(p => ((p.ClienteId ?? "").Trim().ToUpper() == cliente)
                             && p.Mes == mes && p.Año == anio
                             && skusPend.Contains((p.ProductoCodigo ?? "").Trim().ToUpper()))
                    .GroupBy(p => (p.ProductoCodigo ?? "").Trim().ToUpper())
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Sum(v => v.PresupuestoAsignado)
                    );
            }

            // ============================
            // 6) Consumo del mes SOLO de otras OV y SOLO líneas YA autorizadas
            // ============================
            Dictionary<string, decimal> consumoMesDict;
            if (hayPresCedisPeriodo)
            {
                // Consumo por CANAL (CEDIS + MATRIZ)
                consumoMesDict = await
                    (from o in _context.OrdenVenta
                     join cli in _context.ClienteSap on o.Cliente equals cli.Cliente
                     join se in _context.Series on o.Serie equals se.NombreSerie into seJoin
                     from se in seJoin.DefaultIfEmpty()
                     where o.Id != ov.Id
                        && o.FechaEntrega.Month == mes
                        && o.FechaEntrega.Year == anio
                        && o.Estatus != 0
                     let canalCli = (cli.U_CANAL ?? "").Trim().ToUpper()
                     let sucSerie = (se.Sucursal ?? "").Trim().ToUpper()
                     let canalEf = (canalCli.StartsWith("CEDIS") && sucSerie == "MATRIZ") ? canalCli : ""
                     where canalEf == canalUp      // mismo canal efectivo CEDIS de esta OV
                     join op in _context.OrdenVentaProducto on o.Id equals op.PedidoId
                     where (op.Eliminado == null || op.Eliminado == false)
                        && op.AutorizacionPresupuestoLinea == true
                     let sku = (op.ProductoCodigo ?? "").Trim().ToUpper()
                     where skusPend.Contains(sku)
                     group op by sku into g
                     select new { SKU = g.Key, Kg = g.Sum(x => (decimal?)x.Peso) ?? 0m })
                    .ToDictionaryAsync(x => x.SKU, x => x.Kg);
            }
            else
            {
                // Consumo por CLIENTE (casos no CEDIS o sin MATRIZ)
                consumoMesDict = await
                    (from o in _context.OrdenVenta
                     where o.Id != ov.Id
                        && ((o.Cliente ?? "").Trim().ToUpper() == cliente)
                        && o.FechaEntrega.Month == mes
                        && o.FechaEntrega.Year == anio
                        && o.Estatus != 0
                     join op in _context.OrdenVentaProducto on o.Id equals op.PedidoId
                     where (op.Eliminado == null || op.Eliminado == false)
                        && op.AutorizacionPresupuestoLinea == true
                     let sku = (op.ProductoCodigo ?? "").Trim().ToUpper()
                     where skusPend.Contains(sku)
                     group op by sku into g
                     select new { SKU = g.Key, Kg = g.Sum(x => (decimal?)x.Peso) ?? 0m })
                    .ToDictionaryAsync(x => x.SKU, x => x.Kg);
            }

            // ============================
            // 7) Kilos de ESTA OV (líneas aún NO autorizadas)
            // ============================
            var kgNoAutEstaOV = lineas
                .Where(x => !x.Aut && x.Kg > 0 && skusPend.Contains(x.SKU))
                .GroupBy(x => x.SKU)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Kg));

            // ============================
            // 8) Revisión por SKU
            // ============================
            foreach (var sku in skusPend)
            {
                // Si no hay presupuesto para este SKU, no limitamos
                if (!presupDict.TryGetValue(sku, out var presupuesto) || presupuesto <= 0m)
                    continue;

                var consumoMes = consumoMesDict.TryGetValue(sku, out var kgMes) ? kgMes : 0m;
                var kgNoAut = kgNoAutEstaOV.TryGetValue(sku, out var kgNA) ? kgNA : 0m;

                if (consumoMes + kgNoAut > presupuesto)
                    return true;  // hay pendientes/exceso
            }

            return false;
        }



        private void CerrarOvSiCorresponde(OrdenVenta ov)
        {
            // Si llegamos aquí es porque YA no hay líneas pendientes
            ov.AutorizacionPresupuesto = true;

            // Si ya están las tres autorizaciones, sube a 3; si no, se queda en 2
            ov.Estatus = (ov.AutorizacionPresupuesto &&
                          ov.AutorizacionPrecio &&
                          ov.AutorizacionCredito)
                         ? 3   // listo para surtido
                         : 2;  // sigue en proceso

            _context.Entry(ov).Property(x => x.AutorizacionPresupuesto).IsModified = true;
            _context.Entry(ov).Property(x => x.Estatus).IsModified = true;
        }



        // true  = todavía hay líneas sin AutorizacionPresupuestoLinea
        // false = ya TODAS las líneas activas están autorizadas
        private async Task<bool> HayLineasPendientesAsync(int ovId)
        {
            return await _context.OrdenVentaProducto.AnyAsync(li =>
                li.PedidoId == ovId &&
                (li.Eliminado == null || li.Eliminado == false) &&
                li.AutorizacionPresupuestoLinea == false
            );
        }




        // ============================================================================
        // HELPERS — Lógica ÚNICA para cálculos de autorizaciones y estatus
        // ============================================================================

        private List<OrdenVentaProducto> ObtenerLineasActivas(OrdenVenta ov)
        {
            return ov.Productos
                .Where(p => p.Eliminado == null || p.Eliminado == false)
                .ToList();
        }

        /// <summary>
        /// Recalcula las banderas globales de autorización y el estatus final de la OV
        /// ESTA ES LA ÚNICA FUNCIÓN que decide si la OV queda autorizada o no
        /// cuando se quiere recalcular de forma completa.
        /// </summary>
        private void RecalcularAutorizacionesYEstado(OrdenVenta ov)
        {
            var lineasActivas = ObtenerLineasActivas(ov);

            // 1) PRESUPUESTO GLOBAL (todas las líneas de presupuesto deben estar autorizadas)
            bool todasPresupuesto = lineasActivas.All(p => p.AutorizacionPresupuestoLinea);
            ov.AutorizacionPresupuesto = todasPresupuesto;

            // 2) PRECIO / CRÉDITO (si manejas autorizaciones por línea, replicarías aquí)
            // bool todasPrecio = lineasActivas.All(p => p.AutorizacionPrecioLinea);
            // bool todasCredito = lineasActivas.All(p => p.AutorizacionCreditoLinea);
            // ov.AutorizacionPrecio = todasPrecio;
            // ov.AutorizacionCredito = todasCredito;

            // 3) ESTATUS FINAL
            // Si TODO (presupuesto + precio + crédito) está autorizado => Estatus 3
            if (ov.AutorizacionPresupuesto && ov.AutorizacionPrecio && ov.AutorizacionCredito)
            {
                ov.Estatus = 3;
            }
            else
            {
                // opcional: asignar estatus intermedio
                // ov.Estatus = 2;
            }
        }


        // ============================================================================
        // AUTORIZAR PRESUPUESTO — CABECERA
        // ============================================================================
        // OJO: Esta acción se llama SOLO cuando el back ya dijo "todasAutorizadas = true".
        // Aquí ya NO volvemos a bloquear por pendientes; marcamos cabecera en true
        // y si también tiene precio + crédito, pasamos estatus a 3.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarPresupuesto(int id)
        {
            if (id <= 0)
                return BadRequest(new { mensaje = "Id inválido." });

            var ov = await _context.OrdenVenta.FirstOrDefaultAsync(o => o.Id == id);
            if (ov == null)
                return NotFound(new { mensaje = $"OV {id} no encontrada." });

            // Por si quieres verificar de nuevo:
            var hayPendientes = await HayLineasPendientesPresupuestoAsync(ov.Id);
            if (hayPendientes)
            {
                return Ok(new
                {
                    mensaje = $"La OV {ov.Consecutivo} aún tiene partidas con presupuesto excedido o sin presupuesto.",
                    ovId = ov.Id,
                    consecutivo = ov.Consecutivo,
                    estatus = ov.Estatus,
                    presupuestoGlobal = ov.AutorizacionPresupuesto
                });
            }

            ov.AutorizacionPresupuesto = true;

            if (ov.AutorizacionPrecio && ov.AutorizacionCredito)
            {
                ov.Estatus = 3;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = $"Presupuesto de la OV {ov.Consecutivo} quedó autorizado. Estatus = {ov.Estatus}.",
                ovId = ov.Id,
                consecutivo = ov.Consecutivo,
                estatus = ov.Estatus,
                presupuestoGlobal = ov.AutorizacionPresupuesto
            });
        }



        // ============================================================================
        // AUTORIZAR PRESUPUESTO — LÍNEA (OV / TR)
        // ============================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarPresupuestoLinea(int idLinea, string tipoDocumento)
        {
            if (idLinea <= 0)
                return BadRequest(new { mensaje = "Id de línea inválido." });

            // Normalizamos tipoDocumento (por defecto OV)
            tipoDocumento = (tipoDocumento ?? "OV").Trim().ToUpperInvariant();

            await using var tx = await _context.Database.BeginTransactionAsync();

            // =====================================================================
            // CASO 1: ORDEN DE VENTA (OV)  👉 tu lógica original
            // =====================================================================
            if (tipoDocumento == "OV")
            {
                var linea = await _context.OrdenVentaProducto.FindAsync(idLinea);
                if (linea == null)
                    return NotFound(new { mensaje = $"Línea {idLinea} no encontrada en OV." });

                var ov = await _context.OrdenVenta
                    .FirstOrDefaultAsync(o => o.Id == linea.PedidoId);

                if (ov == null)
                    return NotFound(new { mensaje = $"OV {linea.PedidoId} no encontrada." });

                var clienteSap = await _context.ClienteSap
                    .FirstOrDefaultAsync(c => c.Cliente == ov.Cliente);

                var kilosLinea = (decimal?)(linea.Peso) ?? 0m;

                // 1) Autoriza esta línea
                if (!linea.AutorizacionPresupuestoLinea)
                {
                    linea.AutorizacionPresupuestoLinea = true;
                    _context.Entry(linea).Property(x => x.AutorizacionPresupuestoLinea).IsModified = true;
                }

                // 2) Bitácora
                var fechaEnt = ov.FechaEntrega;
                int mes = fechaEnt != null ? fechaEnt.Month : DateTime.Now.Month;
                int anio = fechaEnt != null ? fechaEnt.Year : DateTime.Now.Year;

                var historico = new PresupuestoLineaHistorico
                {
                    FechaRegistro = DateTime.Now,
                    OrdenVentaId = ov.Id,
                    OrdenVentaConsecutivo = ov.Consecutivo,
                    ClienteId = ov.Cliente,
                    ClienteNombre = clienteSap?.Nombrecliente ?? string.Empty,
                    ProductoCodigo = linea.ProductoCodigo,
                    ProductoNombre = linea.ProductoNombre,
                    Mes = mes,
                    Anio = anio,
                    KilosPresupuestoMes = 0m,
                    KilosConsumidosAntes = 0m,
                    KilosSolicitadosLinea = kilosLinea,
                    KilosAutorizados = kilosLinea,
                    FuentePresupuesto = "AUTORIZACION_PRESUPUESTO",
                    Usuario = User?.Identity?.Name ?? "SYSTEM"
                };

                _context.PresupuestoLineasHistorico.Add(historico);

                await _context.SaveChangesAsync();

                // 3) Revisar si TODAVÍA hay líneas pendientes (misma lógica que antes)
                bool hayPendientes = await HayLineasPendientesPresupuestoAsync(ov.Id);
                bool todasAutorizadas = !hayPendientes;

                if (todasAutorizadas)
                {
                    ov.AutorizacionPresupuesto = true;

                    if (ov.AutorizacionPrecio && ov.AutorizacionCredito)
                    {
                        ov.Estatus = 3;
                    }

                    _context.Entry(ov).Property(x => x.AutorizacionPresupuesto).IsModified = true;
                    _context.Entry(ov).Property(x => x.Estatus).IsModified = true;

                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();

                string msg =
                    todasAutorizadas
                    ? (
                        ov.AutorizacionPrecio && ov.AutorizacionCredito
                        ? $"Línea {idLinea} autorizada. Todas las partidas quedaron sin excedentes; OV {ov.Consecutivo} pasó a Estatus = {ov.Estatus}."
                        : $"Línea {idLinea} autorizada. Todas las partidas quedaron sin excedentes; falta Precio y/o Crédito para cambiar estatus."
                    )
                    : $"Línea {idLinea} autorizada. Aún hay partidas con presupuesto excedido o sin presupuesto en la OV {ov.Consecutivo}.";

                return Ok(new
                {
                    mensaje = msg,
                    pendientes = hayPendientes ? 1 : 0,
                    total = 0,
                    todasAutorizadas,
                    ovId = ov.Id,
                    consecutivo = ov.Consecutivo,
                    estatus = ov.Estatus,
                    presupuestoGlobal = ov.AutorizacionPresupuesto,
                    tipoDocumento = "OV"
                });
            }

            // =====================================================================
            // CASO 2: TRANSFERENCIA (TR)
            // =====================================================================
            if (tipoDocumento == "TR")
            {
                var det = await _context.TransferenciaDetalles.FindAsync(idLinea);
                if (det == null)
                    return NotFound(new { mensaje = $"Línea {idLinea} no encontrada en Transferencias." });

                var tr = await _context.Transferencias
                    .FirstOrDefaultAsync(t => t.Id == det.TransferenciaId);

                if (tr == null)
                    return NotFound(new { mensaje = $"Transferencia {det.TransferenciaId} no encontrada." });

                // 1) Autoriza esta línea de transferencia
                if (!det.AutorizacionPresupuestoLinea)
                {
                    det.AutorizacionPresupuestoLinea = true;
                    _context.Entry(det).Property(x => x.AutorizacionPresupuestoLinea).IsModified = true;
                }

                await _context.SaveChangesAsync();

                // 2) Verificar si aún hay líneas pendientes en ESA transferencia
                bool hayPendientes = await _context.TransferenciaDetalles
                    .AnyAsync(d => d.TransferenciaId == tr.Id &&
                                   (d.AutorizacionPresupuestoLinea == null || d.AutorizacionPresupuestoLinea == false));

                bool todasAutorizadas = !hayPendientes;

                // 3) Si ya no hay pendientes → regresamos la transferencia a estatus 1 (normal)
                if (todasAutorizadas)
                {
                    // Si usas un campo AutorizacionPresupuesto en Transferencia, márcalo aquí:
                    // tr.AutorizacionPresupuesto = true;

                    tr.Estatus = 1; // 1 = normal, 2 = requiere autorización (como en Guardar)

                    _context.Entry(tr).Property(x => x.Estatus).IsModified = true;
                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();

                string msgTr =
                    todasAutorizadas
                    ? $"Línea {idLinea} autorizada. Todas las partidas quedaron sin excedentes en la transferencia {tr.Consecutivo}."
                    : $"Línea {idLinea} autorizada. Aún hay partidas con presupuesto excedido o sin presupuesto en la transferencia {tr.Consecutivo}.";

                // Ojo: para no romper el front, seguimos usando los mismos nombres (ovId, etc.)
                return Ok(new
                {
                    mensaje = msgTr,
                    pendientes = hayPendientes ? 1 : 0,
                    total = 0,
                    todasAutorizadas,
                    ovId = tr.Id,               // 👈 es la Transferencia.Id
                    consecutivo = tr.Consecutivo,
                    estatus = tr.Estatus,
                    presupuestoGlobal = (bool?)null,
                    tipoDocumento = "TR"
                });
            }

            // Tipo de documento desconocido
            await tx.RollbackAsync();
            return BadRequest(new { mensaje = $"Tipo de documento no soportado: {tipoDocumento}" });
        }












        // ======================================
        // AUTORIZACION DE PRECIO (CABECERA POR OV)
        // — Solo sube AutorizacionPrecio = 1 si NO quedan líneas pendientes.
        // — NO toca Estatus (no cierra la OV).
        // ======================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarPrecioPorId([FromForm] int id)
        {
            if (id <= 0) return BadRequest(new { mensaje = "Id inválido." });

            await using var tx = await _context.Database.BeginTransactionAsync();

            var ov = await _context.OrdenVenta.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (ov == null) return NotFound(new { mensaje = $"OV (Id={id}) no encontrada." });

            if (!ov.AutorizacionPresupuesto)
                return BadRequest(new { mensaje = "Primero debe autorizar el PRESUPUESTO antes del PRECIO." });

            // 1) Obtener líneas de la OV
            var lineas = await _context.OrdenVentaProducto
                .Where(x => x.PedidoId == ov.Id)
                .ToListAsync();

            // 2) Marcar SOLO las excedidas (precioOV < precioLista)
            var cache = new Dictionary<(string cli, string prod), decimal>();
            foreach (var l in lineas.Where(x => !x.AutorizacionPrecioLinea))
            {
                decimal precioLista;
                var key = (ov.Cliente ?? "-", l.ProductoCodigo ?? "-");
                if (!cache.TryGetValue(key, out precioLista))
                {
                    try
                    {
                        var precioSAP = await _sap.ObtenerPrecioArticuloPorClienteAsync(ov.Cliente, l.ProductoCodigo);
                        precioLista = precioSAP?.Precio ?? 0m;
                    }
                    catch { precioLista = 0m; }
                    cache[key] = precioLista;
                }

                if (l.Precio < precioLista)
                {
                    l.AutorizacionPrecioLinea = true;
                    _context.Entry(l).Property(x => x.AutorizacionPrecioLinea).IsModified = true;
                }
            }
            await _context.SaveChangesAsync();

            // 3) Ver si aún quedan EXCEDIDAS sin autorizar
            bool quedanPendientes = false;
            foreach (var l in lineas)
            {
                if (l.AutorizacionPrecioLinea) continue;

                decimal precioLista;
                var key = (ov.Cliente ?? "-", l.ProductoCodigo ?? "-");
                if (!cache.TryGetValue(key, out precioLista))
                {
                    try
                    {
                        var precioSAP = await _sap.ObtenerPrecioArticuloPorClienteAsync(ov.Cliente, l.ProductoCodigo);
                        precioLista = precioSAP?.Precio ?? 0m;
                    }
                    catch { precioLista = 0m; }
                    cache[key] = precioLista;
                }

                if (l.Precio < precioLista) { quedanPendientes = true; break; }
            }

            if (!quedanPendientes)
            {
                // 4) Cabecera: ya no hay excedidas pendientes → subir precio y estatus
                ov.AutorizacionPrecio = true;
                ov.Estatus = (ov.AutorizacionPresupuesto && ov.AutorizacionCredito) ? 3 : 2;

                var e = _context.Entry(ov);
                e.Property(o => o.AutorizacionPrecio).IsModified = true;
                e.Property(o => o.Estatus).IsModified = true;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { mensaje = $"OV {ov.Consecutivo}: precio autorizado. Estatus={ov.Estatus}." });
            }

            await tx.CommitAsync();
            return BadRequest(new { mensaje = $"La OV {ov.Consecutivo} aún tiene líneas excedidas sin autorizar." });
        }






        // ======================================
        // AUTORIZACION DE PRECIO (POR LÍNEA)
        // — Autoriza la línea; si ya no quedan pendientes, marca cabecera y cierra OV si aplica —
        // Regla solicitada: al completar PRECIO de la OV, si (Presupuesto && Crédito) => Estatus = 3; si no => Estatus = 2
        // ======================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarPrecioLinea([FromForm] int idLinea, [FromForm] string motivo)
        {
            if (idLinea <= 0)
                return BadRequest(new { mensaje = "Id de línea inválido." });

            if (string.IsNullOrWhiteSpace(motivo))
                return BadRequest(new { mensaje = "Debe capturar un motivo de autorización." });

            motivo = motivo.Trim();

            await using var tx = await _context.Database.BeginTransactionAsync();

            var linea = await _context.OrdenVentaProducto
                .AsTracking()
                .FirstOrDefaultAsync(x => x.Id == idLinea);

            if (linea == null)
                return NotFound(new { mensaje = "Línea no encontrada." });

            var ov = await _context.OrdenVenta
                .AsTracking()
                .FirstOrDefaultAsync(x => x.Id == linea.PedidoId);

            if (ov == null)
                return NotFound(new { mensaje = "OV de la línea no encontrada." });

            if (!ov.AutorizacionPresupuesto)
                return BadRequest(new { mensaje = "Primero debe autorizar el PRESUPUESTO antes del PRECIO." });

            var usuarioActual = User?.Identity?.IsAuthenticated == true
            ? User.Identity.Name
            : null;

            if (string.IsNullOrWhiteSpace(usuarioActual))
                return Unauthorized(new { mensaje = "No se pudo identificar al usuario actual." });

            var cliente = await _context.ClienteSap
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Cliente == ov.Cliente);

            var clienteNombre = cliente?.Nombrecliente ?? ov.Cliente ?? "-";

            decimal precioLista = 0m;
            string motivoPrecio = "Sin precio de lista";

            try
            {
                var precioSAP = await _sap.ObtenerPrecioArticuloPorClienteAsync(ov.Cliente, linea.ProductoCodigo);
                if (precioSAP != null)
                {
                    precioLista = precioSAP.Precio;
                    motivoPrecio = linea.Precio < precioLista
                        ? "Precio OV menor a lista"
                        : "Dentro de lista";
                }
            }
            catch
            {
                precioLista = 0m;
                motivoPrecio = "Sin precio de lista";
            }

            var precioOVAntes = linea.Precio;
            var precioAutorizado = linea.Precio;
            var diferencia = precioLista - precioAutorizado;

            if (!linea.AutorizacionPrecioLinea)
            {
                linea.AutorizacionPrecioLinea = true;
                _context.Entry(linea).Property(x => x.AutorizacionPrecioLinea).IsModified = true;

                var historico = new Plataforma_CG.Models.PrecioLineasHistorico
                {
                    FechaRegistro = DateTime.Now,
                    OrdenVentaId = ov.Id,
                    OrdenVentaConsecutivo = ov.Consecutivo ?? "-",
                    LineaId = linea.Id,
                    ClienteId = ov.Cliente ?? "-",
                    ClienteNombre = clienteNombre,
                    ProductoCodigo = linea.ProductoCodigo ?? "-",
                    ProductoNombre = linea.ProductoNombre ?? "-",
                    PrecioLista = precioLista,
                    PrecioOVAntes = precioOVAntes,
                    PrecioAutorizado = precioAutorizado,
                    Diferencia = diferencia,
                    Usuario = usuarioActual,
                    Fuente = "AUTORIZACION_PRECIO",
                    Motivo = motivo
                };

                _context.PrecioLineasHistorico.Add(historico);

                await _context.SaveChangesAsync();
            }

            var lineasOv = await _context.OrdenVentaProducto
                .Where(x => x.PedidoId == ov.Id)
                .ToListAsync();

            var cache = new Dictionary<(string cli, string prod), decimal>();
            bool quedanPendientes = false;

            foreach (var l in lineasOv)
            {
                if (l.AutorizacionPrecioLinea) continue;

                decimal precioListaLinea;
                var key = (ov.Cliente ?? "-", l.ProductoCodigo ?? "-");

                if (!cache.TryGetValue(key, out precioListaLinea))
                {
                    try
                    {
                        var precioSAP = await _sap.ObtenerPrecioArticuloPorClienteAsync(ov.Cliente, l.ProductoCodigo);
                        precioListaLinea = precioSAP?.Precio ?? 0m;
                    }
                    catch
                    {
                        precioListaLinea = 0m;
                    }

                    cache[key] = precioListaLinea;
                }

                if (precioListaLinea <= 0m || l.Precio < precioListaLinea)
                {
                    quedanPendientes = true;
                    break;
                }
            }

            if (!quedanPendientes)
            {
                var ovActual = await _context.OrdenVenta
                    .AsTracking()
                    .FirstOrDefaultAsync(x => x.Id == ov.Id);

                if (ovActual == null)
                    return NotFound(new { mensaje = "OV no encontrada al validar estatus." });

                if (!ovActual.AutorizacionPrecio)
                {
                    ovActual.AutorizacionPrecio = true;
                    _context.Entry(ovActual).Property(o => o.AutorizacionPrecio).IsModified = true;
                }

                var cerrar = ovActual.AutorizacionPresupuesto && ovActual.AutorizacionCredito;
                ovActual.Estatus = cerrar ? 3 : 2;
                _context.Entry(ovActual).Property(o => o.Estatus).IsModified = true;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new
                {
                    mensaje = cerrar
                        ? $"Se autorizó la línea {idLinea}. No quedan líneas excedidas por autorizar. Presupuesto y Crédito autorizados. OV cerrada (Estatus = 3)."
                        : $"Se autorizó la línea {idLinea}. No quedan líneas excedidas por autorizar. Falta autorización de Crédito y/o de Presupuesto para cerrar. OV en proceso (Estatus = 2).",
                    lineaId = idLinea,
                    ovId = ovActual.Id,
                    ovCompletada = cerrar
                });
            }

            await tx.CommitAsync();

            return Ok(new
            {
                mensaje = $"Se autorizó la línea {idLinea}. Aún quedan líneas excedidas por autorizar.",
                lineaId = idLinea,
                ovId = ov.Id,
                ovCompletada = false
            });
        }








        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarCredito([FromForm] string idOrdenVenta)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(idOrdenVenta))
                    return BadRequest(new { mensaje = "El parámetro 'idOrdenVenta' es requerido." });

                var consecutivo = idOrdenVenta.Trim();

                var orden = await _context.OrdenVenta
                    .AsTracking()
                    .FirstOrDefaultAsync(o => o.Consecutivo == consecutivo);

                if (orden == null)
                    return NotFound(new { mensaje = $"Orden con consecutivo '{consecutivo}' no encontrada." });

                // Recargar por seguridad para tener valores frescos de BD
                await _context.Entry(orden).ReloadAsync();

                if (!orden.AutorizacionPrecio || !orden.AutorizacionPresupuesto)
                    return BadRequest(new
                    {
                        mensaje = "Primero debe autorizar el PRECIO y el PRESUPUESTO antes de autorizar el CRÉDITO."
                    });

                bool huboCambios = false;

                if (!orden.AutorizacionCredito)
                {
                    orden.AutorizacionCredito = true;
                    _context.Entry(orden).Property(x => x.AutorizacionCredito).IsModified = true;
                    huboCambios = true;
                }

                // Volver a evaluar ya con crédito en true
                var cerrar = orden.AutorizacionPrecio && orden.AutorizacionPresupuesto && true;

                var nuevoEstatus = cerrar ? 3 : 2;

                if (orden.Estatus != nuevoEstatus)
                {
                    orden.Estatus = nuevoEstatus;
                    _context.Entry(orden).Property(x => x.Estatus).IsModified = true;
                    huboCambios = true;
                }

                if (huboCambios)
                    await _context.SaveChangesAsync();

                return Ok(new
                {
                    mensaje = cerrar
                        ? "Crédito autorizado correctamente. La OV quedó liberada."
                        : "Crédito autorizado correctamente.",
                    ordenId = orden.Id,
                    estatus = orden.Estatus,
                    autorizacionCredito = orden.AutorizacionCredito,
                    autorizacionPrecio = orden.AutorizacionPrecio,
                    autorizacionPresupuesto = orden.AutorizacionPresupuesto
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }






        // ======================================
        // CATALOGO DE ARTICULOS DESDE SAP 
        // ======================================
        // GET: /Comercial/TodosProductos


        [HttpGet]
        [Route("Comercial/TodosProductos")]
        public async Task<IActionResult> ObtenerTodosProductos()
        {
            try
            {
                var productos = await _sap.ObtenerTodosProductosAsync();
                return Ok(productos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener productos: {ex.Message}");
            }
        }


        // ======================================
        // SINCRONIZADOR PARA ENVIAR ALA BASE DE DATOS ARTICULOSAP LOCAL
        // ======================================

        public async Task SincronizarItemsAsync()
        {
            // 1. Traer datos desde SAP
            var itemsSap = await _sap.ObtenerTodosProductosAsync(); // CatalogoSkuSapViewmodel

            // ✅ A) Deduplicar por ItemCode (case-insensitive)
            itemsSap = itemsSap
                .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
                .GroupBy(x => x.ItemCode!.Trim().ToUpper())
                .Select(g => g.Last())
                .ToList();

            foreach (var item in itemsSap)
            {
                var code = item.ItemCode?.Trim().ToUpper() ?? "";
                if (string.IsNullOrWhiteSpace(code)) continue;

                // ✅ B) Buscar primero en el ChangeTracker (Local) y luego en BD
                var existente =
                    _context.ArticuloSap.Local.FirstOrDefault(x => x.ProductoCodigo == code)
                    ?? await _context.ArticuloSap.FirstOrDefaultAsync(x => x.ProductoCodigo == code);

                if (existente == null)
                {
                    // Nuevo -> Insertar
                    var nuevo = new ArticuloSap
                    {
                        ProductoCodigo = code,
                        ProductoNombre = item.ItemName ?? "",
                        U_MASTER = item.U_MASTER ?? "",
                        U_TipoporSKU = item.U_TipoporSKU ?? "",
                        Rotacion = 1,
                        U_KilosCaja = item.U_KilosCaja,

                        // ✅ NUEVOS CAMPOS INT
                        U_Clas_Prod = item.U_Clas_Prod,
                        U_PRESENT = item.U_PRESENT,
                        U_PorcInye = item.U_PorcInye,

                        FechaModificacion = DateTime.Now
                    };

                    _context.ArticuloSap.Add(nuevo);
                }
                else
                {
                    bool actualizo = false;

                    if (existente.ProductoNombre != (item.ItemName ?? ""))
                    {
                        existente.ProductoNombre = item.ItemName ?? "";
                        actualizo = true;
                    }

                    if ((existente.U_MASTER ?? "") != (item.U_MASTER ?? ""))
                    {
                        existente.U_MASTER = item.U_MASTER ?? "";
                        actualizo = true;
                    }

                    if ((existente.U_TipoporSKU ?? "") != (item.U_TipoporSKU ?? ""))
                    {
                        existente.U_TipoporSKU = item.U_TipoporSKU ?? "";
                        actualizo = true;
                    }

                    //if (existente.U_KilosCaja != item.U_KilosCaja)
                    //{
                    //    existente.U_KilosCaja = item.U_KilosCaja;
                    //    actualizo = true;
                    //}

                    // ✅ NUEVOS CAMPOS INT (UPDATE)
                    if (existente.U_Clas_Prod != item.U_Clas_Prod)
                    {
                        existente.U_Clas_Prod = item.U_Clas_Prod;
                        actualizo = true;
                    }

                    if (existente.U_PRESENT != item.U_PRESENT)
                    {
                        existente.U_PRESENT = item.U_PRESENT;
                        actualizo = true;
                    }

                    if (existente.U_PorcInye != item.U_PorcInye)
                    {
                        existente.U_PorcInye = item.U_PorcInye;
                        actualizo = true;
                    }

                    // 🔥 Siempre dejar rotación en 1
                    if (existente.Rotacion != 1)
                    {
                        existente.Rotacion = 1;
                        actualizo = true;
                    }

                    if (actualizo)
                        existente.FechaModificacion = DateTime.Now;
                }
            }

            await _context.SaveChangesAsync();
        }





        // ======================================
        // SINCRONIZADOR ENVIO A TABLA ARTICULOSAP DBO LOCAL
        // ======================================

        ////https://localhost:7171/Comercial/SincronizarItems con esto puedo ejecutar el procedimiento

        [HttpGet("SincronizarItems")]
        public async Task<IActionResult> SincronizarItems()
        {
            try
            {
                await _sap.SincronizarItemsAsync();
                return Ok("Sincronización completada correctamente");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }


        // ======================================
        // VISTA DEL CATALOGO DE ARTICULOS
        // ======================================


        [HttpGet("GetArticulosJson")]
        [Route("Comercial/GetArticulosJson")]
        public async Task<IActionResult> GetArticulosJson()
        {
            var articulos = await _context.ArticuloSap.ToListAsync();
            return Json(articulos); // devuelve JSON
        }



        // ======================================
        // CATALOGO DE ARTICULOSAP POR PAGINACION
        // ======================================


        [HttpGet("GetArticulosJsonPaged")]
        [Route("Comercial/GetArticulosJsonPaged")]
        public async Task<IActionResult> GetArticulosJsonPaged(int page = 1, int pageSize = 50, string search = "")
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200; // evita páginas gigantes

            IQueryable<ArticuloSap> query = _context.ArticuloSap.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(a =>
                    (a.ProductoCodigo != null && a.ProductoCodigo.Contains(term)) ||
                    (a.ProductoNombre != null && a.ProductoNombre.Contains(term))
                );
                // Si prefieres LIKE case-insensitive (según collation): 
                // query = query.Where(a =>
                //     EF.Functions.Like(a.ProductoCodigo ?? "", $"%{term}%") ||
                //     EF.Functions.Like(a.ProductoNombre ?? "", $"%{term}%"));
            }

            var total = await query.CountAsync();

            var articulos = await query
                .OrderBy(a => a.ProductoCodigo)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                // Proyección a camelCase y nuevos campos
                .Select(a => new
                {
                    productoCodigo = a.ProductoCodigo,
                    productoNombre = a.ProductoNombre,
                    u_MASTER = a.U_MASTER,
                    u_TipoporSKU = a.U_TipoporSKU,
                    u_KilosCaja = a.U_KilosCaja,   // ⬅️ nuevo
                    rotacion = a.Rotacion,
                    fechaModificacion = a.FechaModificacion
                })
                .ToListAsync();

            return Json(new { total, page, pageSize, articulos });
        }




        //======================================
        // SINCRONIZADOR DE CLIENTES SAP → BASE LOCAL
        //======================================
        public async Task SincronizarClientesAsync()
        {
            // 1. Traer datos desde SAP
            var itemsSap = await _sap.ObtenerCatTodosClientesAsync(); // Método que obtiene el catálogo desde SAP

            foreach (var item in itemsSap)
            {
                // 2. Buscar en la base SQL Server si ya existe el cliente
                var existente = await _context.ClienteSap
                    .FirstOrDefaultAsync(x => x.Cliente == item.CardCode);

                if (existente == null)
                {
                    // 3. Nuevo cliente → Insertar
                    var nuevo = new ClienteSap
                    {
                        Cliente = item.CardCode ?? "",
                        Nombrecliente = item.CardName ?? "",
                        U_MT_Clasificacion = item.U_MT_Clasificacion ?? "",
                        U_CANAL = item.U_CANAL ?? "",
                        VendedorId = item.SlpCode,                 // 👈 nuevo campo
                        VendedorNombre = item.SalesPersonName ?? "", // 👈 nuevo campo
                        FechaModificacion = DateTime.Now
                    };
                    _context.ClienteSap.Add(nuevo);
                }
                else
                {
                    // 4. Cliente existente → Actualizar solo si cambió algo
                    bool actualizo = false;

                    if (existente.Nombrecliente != item.CardName)
                    {
                        existente.Nombrecliente = item.CardName ?? "";
                        actualizo = true;
                    }

                    if (existente.U_MT_Clasificacion != item.U_MT_Clasificacion)
                    {
                        existente.U_MT_Clasificacion = item.U_MT_Clasificacion ?? "";
                        actualizo = true;
                    }

                    if (existente.U_CANAL != item.U_CANAL)
                    {
                        existente.U_CANAL = item.U_CANAL ?? "";
                        actualizo = true;
                    }

                    if (existente.VendedorId != item.SlpCode)
                    {
                        existente.VendedorId = item.SlpCode;
                        actualizo = true;
                    }

                    if (existente.VendedorNombre != item.SalesPersonName)
                    {
                        existente.VendedorNombre = item.SalesPersonName ?? "";
                        actualizo = true;
                    }

                    if (actualizo)
                    {
                        existente.FechaModificacion = DateTime.Now;
                    }
                }
            }

            // 5. Guardar todos los cambios
            await _context.SaveChangesAsync();
        }



        //https://localhost:7171/Comercial/SincronizarClient con esto puedo ejecutar el procedimiento
        //======================================
        // Sincronizar envio ala tabla de CLIENTESSAP
        //======================================       

        [HttpGet("/Comercial/SincronizarClient")]
        public async Task<IActionResult> SincronizarClient()
        {
            try
            {
                await _sap.SincronizarClientesAsync();
                return Ok("Sincronización completada correctamente");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }




        // ======================================
        // CATALOGO DE CLIENTES DESDE SAP LOCAL
        // ======================================

        [HttpGet]
        [Route("Comercial/TodosClientes")]
        public async Task<IActionResult> ObtenerTodosClientes()
        {
            try
            {
                var Clientes = await _sap.ObtenerCatTodosClientesAsync();
                return Ok(Clientes);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener productos: {ex.Message}");
            }
        }




        //======================================
        // VISTA CATALOGO CLIENTES
        //======================================
        [HttpGet("GetClientesJson")]
        [Route("Comercial/GetClientesJson")]
        public async Task<IActionResult> GetClientesJson()
        {
            var Clientes = await _context.ClienteSap.ToListAsync();
            return Json(Clientes); // devuelve JSON
        }


        // ========================
        // VENDEDORES (desde ClienteSap)
        // ========================
        [HttpGet("Comercial/GetVendedores")]
        public async Task<IActionResult> GetVendedores()
        {
            // Agrupa por vendedor disponible en tu tabla local ClienteSap
            var vendedores = await _context.ClienteSap
                .Where(c => c.VendedorId != null && c.VendedorId > 0)
                .GroupBy(c => new { c.VendedorId, c.VendedorNombre })
                .Select(g => new
                {
                    id = g.Key.VendedorId ?? 0,
                    nombre = g.Key.VendedorNombre ?? "(Sin nombre)",
                    clientes = g.Count()
                })
                .OrderBy(v => v.nombre)
                .ToListAsync();

            return Json(vendedores);
        }

        // ========================
        // CLIENTES POR VENDEDOR
        // ========================
        // GET: /Comercial/GetClientesPorVendedor?vendedorId=12&search=x
        [HttpGet("Comercial/GetClientesPorVendedor")]
        public async Task<IActionResult> GetClientesPorVendedor(
             [FromQuery] int vendedorId,
             [FromQuery] string? search = "",
             [FromQuery] string? clasif = "")
        {
            if (vendedorId <= 0)
                return Json(Array.Empty<object>());

            var q = _context.ClienteSap.AsNoTracking()
                .Where(c => c.VendedorId == vendedorId);

            // Filtro texto
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                q = q.Where(c =>
                    (c.Cliente ?? "").ToLower().Contains(s) ||
                    (c.Nombrecliente ?? "").ToLower().Contains(s));
            }

            // Filtro por una o varias clasificaciones
            if (!string.IsNullOrWhiteSpace(clasif))
            {
                var set = clasif
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.ToLower())
                    .ToHashSet();

                if (set.Count > 0)
                    q = q.Where(c => c.U_MT_Clasificacion != null &&
                                     set.Contains(c.U_MT_Clasificacion.ToLower()));
            }

            var clientes = await q
                .OrderBy(c => c.Nombrecliente)
                .Select(c => new
                {
                    id = c.Cliente,
                    text = $"{c.Nombrecliente} ({c.Cliente})"
                })
                .ToListAsync();

            return Json(clientes);
        }


        // ========================
        // CLASIFICACIONES (distintas)
        // ========================
        [HttpGet("Comercial/GetClasificacionesClientes")]
        public async Task<IActionResult> GetClasificacionesClientes()
        {
            var clasifs = await _context.ClienteSap.AsNoTracking()
                .Where(c => c.U_MT_Clasificacion != null && c.U_MT_Clasificacion != "")
                .Select(c => c.U_MT_Clasificacion!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return Json(clasifs);
        }


        // ========================
        // CANALES (U_Canal) distintos
        // ========================
        [HttpGet("Comercial/GetCanales")]
        public async Task<IActionResult> GetCanales()
        {
            var canales = await _context.ClienteSap
                .AsNoTracking()
                .Where(c => !string.IsNullOrEmpty(c.U_CANAL) &&
                            !EF.Functions.Like(c.U_CANAL, "%CEDIS%"))
                .Select(c => c.U_CANAL!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return Json(canales);
        }

        // ===================================================================
        // CLIENTES por canal, múltiples vendedores y múltiples clasificaciones
        // GET: /Comercial/GetClientes?vendedores=1,2&canal=RETAIL&clasif=A,B&search=texto
        // Devuelve: [{ id, text }]
        // ===================================================================
        [HttpGet("Comercial/GetClientes")]
        public async Task<IActionResult> GetClientes(
            [FromQuery] string? vendedores = "",
            [FromQuery] string? canal = "",
            [FromQuery] string? clasif = "",
            [FromQuery] string? search = "")
        {
            IQueryable<ClienteSap> q = _context.ClienteSap.AsNoTracking();

            // Canal (U_Canal)
            if (!string.IsNullOrWhiteSpace(canal))
            {
                var cVal = canal.Trim();
                q = q.Where(x => x.U_CANAL != null && x.U_CANAL == cVal);
            }

            // Vendedores múltiples (csv)
            if (!string.IsNullOrWhiteSpace(vendedores))
            {
                var vendSet = vendedores
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                    .Where(n => n.HasValue)
                    .Select(n => n!.Value)
                    .ToHashSet();

                if (vendSet.Count > 0)
                    q = q.Where(c => c.VendedorId != null && vendSet.Contains(c.VendedorId.Value));
            }

            // Clasificaciones múltiples (csv)
            if (!string.IsNullOrWhiteSpace(clasif))
            {
                var claSet = clasif
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.ToLower())
                    .ToHashSet();

                if (claSet.Count > 0)
                    q = q.Where(c => c.U_MT_Clasificacion != null &&
                                     claSet.Contains(c.U_MT_Clasificacion.ToLower()));
            }

            // Búsqueda libre
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                q = q.Where(c =>
                    (!string.IsNullOrEmpty(c.Cliente) && c.Cliente.ToLower().Contains(s)) ||
                    (!string.IsNullOrEmpty(c.Nombrecliente) && c.Nombrecliente.ToLower().Contains(s))
                );
            }

            var clientes = await q
                .OrderBy(c => c.Nombrecliente)
                .Select(c => new
                {
                    id = c.Cliente,
                    text = (c.Nombrecliente ?? "(Sin nombre)") + " (" + (c.Cliente ?? "") + ")"
                })
                .ToListAsync();

            return Json(clientes);
        }
        //=============================================
        // CATÁLOGO DE CLIENTES SAP LOCAL (paginado)
        //=============================================
        [HttpGet("GetClientesJsonPaged")]
        [Route("Comercial/GetClientesJsonPaged")]
        public async Task<IActionResult> GetClientesJsonPaged(int page = 1, int pageSize = 50, string search = "")
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            IQueryable<ClienteSap> query = _context.ClienteSap.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();

                query = query.Where(c =>
                    (c.Cliente != null && c.Cliente.ToLower().Contains(term)) ||
                    (c.Nombrecliente != null && c.Nombrecliente.ToLower().Contains(term)) ||
                    (c.U_MT_Clasificacion != null && c.U_MT_Clasificacion.ToLower().Contains(term)) ||
                    (c.U_CANAL != null && c.U_CANAL.ToLower().Contains(term)) ||
                    (c.VendedorNombre != null && c.VendedorNombre.ToLower().Contains(term)) ||
                    (c.VendedorId != null && EF.Functions.Like(c.VendedorId.ToString(), $"%{term}%")) ||

                    // Filtros opcionales por presupuesto
                    (term == "con presupuesto" && c.AplicaPresupuesto == true) ||
                    (term == "sin presupuesto" && c.AplicaPresupuesto == false) ||
                    (term == "1" && c.AplicaPresupuesto == true) ||
                    (term == "0" && c.AplicaPresupuesto == false)
                );
            }

            var total = await query.CountAsync();

            var clientes = await query
                .OrderBy(c => c.Cliente)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    cliente = c.Cliente,
                    nombrecliente = c.Nombrecliente,
                    u_MT_Clasificacion = c.U_MT_Clasificacion,
                    u_CANAL = c.U_CANAL,
                    vendedorId = c.VendedorId,
                    vendedorNombre = c.VendedorNombre,
                    fechaModificacion = c.FechaModificacion,
                    priceListNum = c.PriceListNum,
                    priceListName = c.PriceListName,

                    // NUEVO CAMPO PARA LA VISTA
                    aplicaPresupuesto = c.AplicaPresupuesto ? 1 : 0
                })
                .ToListAsync();

            return Json(new
            {
                total,
                page,
                pageSize,
                clientes
            });
        }

        public class ActualizarAplicaPresupuestoDto
        {
            public string ClienteId { get; set; } = "";
            public int AplicaPresupuesto { get; set; }
        }

        [HttpPost]
        [Route("Comercial/ActualizarAplicaPresupuesto")]
        public async Task<IActionResult> ActualizarAplicaPresupuesto([FromBody] ActualizarAplicaPresupuestoDto request)
        {
            if (request == null)
                return BadRequest("Solicitud no válida.");

            if (string.IsNullOrWhiteSpace(request.ClienteId))
                return BadRequest("Cliente no válido.");

            if (request.AplicaPresupuesto != 0 && request.AplicaPresupuesto != 1)
                return BadRequest("Valor no válido. Debe ser 1 o 0.");

            var cliente = await _context.ClienteSap
                .FirstOrDefaultAsync(c => c.Cliente == request.ClienteId);

            if (cliente == null)
                return NotFound("Cliente no encontrado.");

            cliente.AplicaPresupuesto = request.AplicaPresupuesto == 1;
            cliente.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            return Json(new
            {
                ok = true,
                cliente = cliente.Cliente,
                aplicaPresupuesto = cliente.AplicaPresupuesto ? 1 : 0
            });
        }



        //======================================
        // REPORTE BALANCE MASTER
        //======================================

        [HttpGet("Comercial/ObtenerInventarioAgrupado")]
        public async Task<IActionResult> ObtenerInventarioAgrupado(int mes, int año)
        {
            try
            {
                var query = @"
WITH BaseArt AS (
    SELECT
        a.ProductoCodigo,
        a.U_MASTER,
        a.Rotacion,
        ClasificacionId = ISNULL(TRY_CAST(a.U_Clas_Prod AS int), 99),
        ClasificacionNombre = ISNULL(cp.Nombre, 'POR DEFINIR')
    FROM dbo.ArticuloSap a WITH (NOLOCK)
    LEFT JOIN dbo.ClasificacionProduccion cp WITH (NOLOCK)
        ON TRY_CAST(a.U_Clas_Prod AS int) = cp.ClasificacionId
    --WHERE a.U_TipoporSKU IN (1,2)
    WHERE NULLIF(LTRIM(RTRIM(ISNULL(a.U_MASTER,''))), '') IS NOT NULL
),
InvP AS (
    SELECT i.ProductoCodigo, SUM(i.Kg) AS Inventario
    FROM dbo.InventarioSigo i WITH (NOLOCK)
    GROUP BY i.ProductoCodigo
),
PlanP AS (             
    SELECT e.ProductoCodigo, SUM(e.Peso) AS PlanProduccion
    FROM dbo.PlanDetalle e WITH (NOLOCK)
    INNER JOIN dbo.PlanProduccion f WITH (NOLOCK) ON f.Id = e.fk_Plan
    WHERE f.Mes = {0} AND f.Anio = {1}
    GROUP BY e.ProductoCodigo
),
PresuTOTAL AS (
    SELECT x.ProductoCodigo, SUM(x.Presupuesto) AS Presupuesto
    FROM (
        SELECT c.ProductoCodigo, SUM(c.Presupuesto) AS Presupuesto
        FROM dbo.Presupuestos c WITH (NOLOCK)
        WHERE c.Mes = {0} AND c.Año = {1}
        GROUP BY c.ProductoCodigo

        UNION ALL

        SELECT pc.ProductoCodigo, SUM(pc.PresupuestoAsignado) AS Presupuesto
        FROM dbo.PresupuestoCedis pc WITH (NOLOCK)
        WHERE pc.Mes = {0} AND pc.Anio = {1}
        GROUP BY pc.ProductoCodigo

        UNION ALL

        SELECT pv.ProductoCodigo, SUM(pv.PresupuestoAsignado) AS Presupuesto
        FROM dbo.PresupuestoVendedor pv WITH (NOLOCK)
        WHERE pv.Mes = {0} AND pv.Anio = {1}
        GROUP BY pv.ProductoCodigo

    ) x
    GROUP BY x.ProductoCodigo
),
Agg AS (               
    SELECT
        ba.U_MASTER,
        MAX(ba.Rotacion)                   AS Rotacion,
        MAX(ba.ClasificacionId)            AS ClasificacionId,
        MAX(ba.ClasificacionNombre)        AS ClasificacionNombre,
        MAX(fac.Factor)                    AS Factor,
        SUM(ISNULL(inv.Inventario, 0))     AS Inventario,
        SUM(ISNULL(pln.PlanProduccion, 0)) AS PlanProduccion,
        SUM(ISNULL(pre.Presupuesto, 0))    AS Presupuesto
    FROM BaseArt ba
    LEFT JOIN InvP       inv ON inv.ProductoCodigo = ba.ProductoCodigo
    LEFT JOIN PlanP      pln ON pln.ProductoCodigo = ba.ProductoCodigo
    LEFT JOIN PresuTOTAL pre ON pre.ProductoCodigo = ba.ProductoCodigo
    CROSS JOIN (SELECT MAX(valor) AS Factor FROM dbo.Factor WITH (NOLOCK)) AS fac
    GROUP BY ba.U_MASTER
),
Calc AS (
    SELECT 
        U_MASTER,
        CAST(Rotacion AS int)          AS Rotacion,
        CAST(ClasificacionId AS int)   AS ClasificacionId,
        ClasificacionNombre,
        CAST(PlanProduccion AS int)    AS PlanProduccion,
        CAST(Inventario AS int)        AS Inventario,
        CAST(ROUND(Presupuesto / 30.4 * (Rotacion * Factor), 0) AS int) AS InvIdeal,
        CAST(PlanProduccion + Inventario - ROUND(Presupuesto / 30.4 * (Rotacion * Factor), 0) AS int) AS Disponible,
        CAST(Presupuesto AS int)       AS Presupuesto,
        CAST(PlanProduccion + Inventario - ROUND(Presupuesto / 30.4 * (Rotacion * Factor), 0) - Presupuesto AS int) AS GAP,
        CAST(
            CASE WHEN Presupuesto = 0 THEN 0
                 ELSE ROUND(((PlanProduccion + Inventario - ROUND(Presupuesto / 30.4 * (Rotacion * Factor), 0)) * 1.0 / Presupuesto) * 100, 0)
            END AS int
        ) AS Porcentaje
    FROM Agg
)
SELECT *
FROM Calc
WHERE (PlanProduccion + Inventario + InvIdeal + Disponible + Presupuesto + GAP + Porcentaje) > 0
ORDER BY U_MASTER;
";

                var resultado = await _context.Set<BalanceMasterView>()
                    .FromSqlRaw(query, mes, año)
                    .ToListAsync();

                return Json(resultado);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }



        // GET: /Comercial/ObtenerFactor       
        [HttpGet("Comercial/ObtenerFactor")]
        public async Task<IActionResult> ObtenerFactor()
        {
            // Trae el primer registro de la tabla FACTOR
            var factor = await _context.Factor.FirstOrDefaultAsync();

            if (factor == null)
                return NotFound("No se encontró ningún factor");

            return Ok(factor);
        }



        //actualizar factor para balance master

        [HttpPost("Comercial/ActualizarFactor")]
        public async Task<IActionResult> ActualizarFactor([FromBody] UpdateFactor model)
        {
            if (model == null)
                return BadRequest(new { mensaje = "Body nulo o inválido" });

            // Actualizamos directamente en DB
            var sql = "UPDATE Factor SET valor = {0}";
            var filas = await _context.Database.ExecuteSqlRawAsync(sql, model.valor);

            return Ok(new { mensaje = "Factor actualizado correctamente.", filas });
        }


        //======================================
        // REPORTE PRESUPUESTOS POR MASTER
        //======================================
        [HttpGet("Comercial/ObtenerPresupuestosPorMaster")]
        public async Task<IActionResult> ObtenerPresupuestosPorMaster(string U_MASTER, int mes, int anio)
        {
            try
            {
                var resultado = await _context.Set<BalancePresupuestoView>()
                    .FromSqlInterpolated($@"
SELECT 
    x.Fuente,
    x.Id,
    x.U_MASTER,
    x.CanalVta,
    x.Estatus,
    x.RazonSocial,
    x.ProductoCodigo,
    x.ProductoNombre,
    x.ClasificacionId,
    x.ClasificacionNombre,
    x.Presupuesto
FROM (
    /* -------- Presupuesto GENERAL (por cliente) -------- */
    SELECT 
        'GENERAL' AS Fuente,
        a.Id,
        d.U_MASTER,
        c.U_Canal                       AS CanalVta,
        c.U_MT_Clasificacion            AS Estatus,
        c.Nombrecliente                 AS RazonSocial,
        a.ProductoCodigo,
        d.ProductoNombre,
        ISNULL(TRY_CAST(d.U_Clas_Prod AS int), 99)      AS ClasificacionId,
        ISNULL(cp.Nombre, 'POR DEFINIR')                AS ClasificacionNombre,
        CAST(ROUND(a.Presupuesto, 0) AS int)            AS Presupuesto
    FROM dbo.Presupuestos a
    INNER JOIN dbo.ArticuloSap d 
        ON a.ProductoCodigo = d.ProductoCodigo
    INNER JOIN dbo.ClienteSap c  
        ON a.ClienteId = c.cliente
    LEFT JOIN dbo.ClasificacionProduccion cp
        ON TRY_CAST(d.U_Clas_Prod AS int) = cp.ClasificacionId
    WHERE a.Mes = {mes}
      AND a.Año = {anio}
      AND d.U_MASTER = {U_MASTER}
      AND d.U_TipoporSKU IN (1,2,5)

    UNION ALL

    /* -------- Presupuesto (por VENDEDOR) AGRUPADO POR SKU -------- */
    SELECT 
        'VENDEDOR' AS Fuente,
        MIN(a.Id) AS Id,
        d.U_MASTER,
        'VENDEDOR' AS CanalVta,
        'VENDEDOR' AS Estatus,
        v.VendedorNombre AS RazonSocial,
        a.ProductoCodigo,
        d.ProductoNombre,
        ISNULL(TRY_CAST(d.U_Clas_Prod AS int), 99)      AS ClasificacionId,
        ISNULL(cp.Nombre, 'POR DEFINIR')                AS ClasificacionNombre,
        CAST(ROUND(SUM(a.PresupuestoAsignado), 0) AS int) AS Presupuesto
    FROM dbo.PresupuestoVendedor a
    INNER JOIN dbo.ArticuloSap d 
        ON a.ProductoCodigo = d.ProductoCodigo
    INNER JOIN (
        SELECT VendedorId, MAX(VendedorNombre) AS VendedorNombre
        FROM dbo.ClienteSap
        WHERE VendedorId IS NOT NULL
        GROUP BY VendedorId
    ) v 
        ON a.VendedorId = v.VendedorId
    LEFT JOIN dbo.ClasificacionProduccion cp
        ON TRY_CAST(d.U_Clas_Prod AS int) = cp.ClasificacionId
    WHERE a.Mes = {mes}
      AND a.Anio = {anio}
      AND d.U_MASTER = {U_MASTER}
      AND d.U_TipoporSKU IN (1,2,5)
    GROUP BY
        d.U_MASTER,
        v.VendedorNombre,
        a.ProductoCodigo,
        d.ProductoNombre,
        d.U_Clas_Prod,
        cp.Nombre

    UNION ALL

    /* -------- Presupuesto por CEDIS -------- */
    SELECT 
        'CEDIS' AS Fuente,
        pc.Id,
        d.U_MASTER,
        pc.Canal                                        AS CanalVta,
        'CEDIS'                                         AS Estatus,
        CONCAT('CEDIS ', pc.Canal)                      AS RazonSocial,
        pc.ProductoCodigo,
        d.ProductoNombre,
        ISNULL(TRY_CAST(d.U_Clas_Prod AS int), 99)      AS ClasificacionId,
        ISNULL(cp.Nombre, 'POR DEFINIR')                AS ClasificacionNombre,
        CAST(ROUND(pc.PresupuestoAsignado, 0) AS int)   AS Presupuesto
    FROM dbo.PresupuestoCedis pc
    INNER JOIN dbo.ArticuloSap d 
        ON pc.ProductoCodigo = d.ProductoCodigo
    LEFT JOIN dbo.ClasificacionProduccion cp
        ON TRY_CAST(d.U_Clas_Prod AS int) = cp.ClasificacionId
    WHERE pc.Mes  = {mes}
      AND pc.Anio = {anio}
      AND d.U_MASTER = {U_MASTER}
      AND d.U_TipoporSKU IN (1,2,5)
) x
ORDER BY x.U_MASTER, x.ProductoCodigo, x.Fuente, x.RazonSocial;





            ")
                    .ToListAsync();

                return Json(resultado);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        //======================================
        // REPORTE PLAN PRODUCCION POR MASTER
        //======================================

        [HttpGet("Comercial/ObtenerPlanProduccionPorMaster")]
        public async Task<IActionResult> ObtenerPlanProduccionPorMaster(string U_MASTER, int mes, int anio)
        {
            try
            {
                var resultado = await _context.Set<BalancePlanProduccionView>() // Debes tener un ViewModel o DTO para mapear
                    .FromSqlInterpolated($@"
                SELECT
    a.Id,
    c.U_MASTER,
    a.ProductoCodigo,
    c.ProductoNombre,
    ISNULL(TRY_CAST(c.U_Clas_Prod AS int), 99) AS ClasificacionId,
    ISNULL(cp.Nombre, 'POR DEFINIR') AS ClasificacionNombre,
    a.Peso AS [Plan]
FROM PlanDetalle a
INNER JOIN PlanProduccion b 
    ON a.fk_Plan = b.Id
INNER JOIN ArticuloSap c 
    ON a.ProductoCodigo = c.ProductoCodigo
LEFT JOIN dbo.ClasificacionProduccion cp
    ON TRY_CAST(c.U_Clas_Prod AS int) = cp.ClasificacionId
WHERE MONTH(b.Fecha) = {mes}
  AND YEAR(b.Fecha) = {anio}
  AND c.U_MASTER = {U_MASTER}
  AND c.U_TipoporSKU in (1,2)
ORDER BY c.U_MASTER, a.ProductoCodigo
            ")
                    .ToListAsync();

                return Json(resultado);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }


        //======================================
        // REPORTE PLAN PRODUCCION REAL POR MASTER
        //======================================

        [HttpGet("Comercial/ObtenerPlanProduccionRealPorMaster")]
        public async Task<IActionResult> ObtenerPlanProduccionRealPorMaster(string U_MASTER, int mes, int anio)
        {
            try
            {
                var resultado = await _context.Set<PlanProduccionRealRow>()
                    .FromSqlInterpolated($@"
                SELECT 
    b.U_MASTER AS U_MASTER,
    a.articuloCodigo AS ArticuloCodigo,
    a.producto AS Producto,
    ISNULL(TRY_CAST(b.U_Clas_Prod AS int), 99) AS ClasificacionId,
    ISNULL(cp.Nombre, 'POR DEFINIR') AS ClasificacionNombre,
    SUM(a.kgProducidos) AS KgProducidos,
    a.dia AS Dia,
    a.mes AS Mes,
    a.anio AS Anio
FROM dbo.produccionsigo a
INNER JOIN dbo.ArticuloSap b 
    ON a.articuloCodigo = b.ProductoCodigo
LEFT JOIN dbo.ClasificacionProduccion cp
    ON TRY_CAST(b.U_Clas_Prod AS int) = cp.ClasificacionId
WHERE a.mes = {mes}
  AND a.anio = {anio}
  AND b.U_MASTER = {U_MASTER}
  AND b.U_TipoporSKU in (1,2)
GROUP BY 
    b.U_MASTER,
    a.articuloCodigo,
    a.producto,
    b.U_Clas_Prod,
    cp.Nombre,
    a.dia,
    a.mes,
    a.anio
            ")
                    .AsNoTracking()
                    .ToListAsync();

                return Json(resultado);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }



        //======================================
        // REPORTE VENTA REAL (VENTAS + TRANSFERENCIAS) POR MASTER
        //======================================
        [HttpGet("Comercial/ObtenerVentaRealPorMaster")]
        public async Task<IActionResult> ObtenerVentaRealPorMaster(string U_MASTER, int mes, int anio)
        {
            try
            {
                var resultado = await _context.Set<VentaRealRow>()
                    .FromSqlInterpolated($@"
         SELECT
    a_sub.U_MASTER AS U_MASTER,
    a_sub.ArticuloCodigo AS ArticuloCodigo,
    a_sub.Producto AS Producto,
    a_sub.ClasificacionId AS ClasificacionId,
    a_sub.ClasificacionNombre AS ClasificacionNombre,
    SUM(a_sub.Kg) AS KgVendidos,
    a_sub.Dia AS Dia,
    a_sub.Mes AS Mes,
    a_sub.Anio AS Anio,
    a_sub.VendedorId AS VendedorId,
    a_sub.U_CANAL AS U_CANAL,
    a_sub.VendedorNombre AS VendedorNombre
FROM (
    SELECT
        c.U_MASTER,
        b.Articulo AS ArticuloCodigo,
        c.ProductoNombre AS Producto,
        ISNULL(TRY_CAST(c.U_Clas_Prod AS int), 99) AS ClasificacionId,
        ISNULL(cp.Nombre, 'POR DEFINIR') AS ClasificacionNombre,
        SUM(b.kg) AS Kg,
        DAY(a.FechaValidacion) AS Dia,
        MONTH(a.FechaValidacion) AS Mes,
        YEAR(a.FechaValidacion) AS Anio,
        cs.VendedorId AS VendedorId,
        cs.U_CANAL AS U_CANAL,
        cs.VendedorNombre AS VendedorNombre
    FROM SurtidoEncabezado a
    INNER JOIN SurtidoDetalle b 
        ON a.SolicitudSurtidoId = b.SolicitudSurtidoId
    INNER JOIN ArticuloSap c 
        ON b.Articulo = c.ProductoCodigo
    LEFT JOIN dbo.ClasificacionProduccion cp
        ON TRY_CAST(c.U_Clas_Prod AS int) = cp.ClasificacionId
    LEFT JOIN ClienteSap cs 
        ON cs.Cliente = a.CodigoSap
    WHERE MONTH(a.FechaValidacion) = {mes}
      AND YEAR(a.FechaValidacion) = {anio}
      AND c.U_MASTER = {U_MASTER}
      AND c.U_TipoporSKU IN (1, 2)
    GROUP BY
        c.U_MASTER,
        b.Articulo,
        c.ProductoNombre,
        c.U_Clas_Prod,
        cp.Nombre,
        DAY(a.FechaValidacion),
        MONTH(a.FechaValidacion),
        YEAR(a.FechaValidacion),
        cs.VendedorId,
        cs.U_CANAL,
        cs.VendedorNombre
) a_sub
GROUP BY
    a_sub.U_MASTER,
    a_sub.ArticuloCodigo,
    a_sub.Producto,
    a_sub.ClasificacionId,
    a_sub.ClasificacionNombre,
    a_sub.Dia,
    a_sub.Mes,
    a_sub.Anio,
    a_sub.VendedorId,
    a_sub.U_CANAL,
    a_sub.VendedorNombre
            ")
                    .AsNoTracking()
                    .ToListAsync();

                return Json(resultado);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }



        [HttpGet("Comercial/ObtenerPermisosBalanceMaster")]
        public async Task<IActionResult> ObtenerPermisosBalanceMaster()
        {
            var permisoPresupuesto = await ObtenerPermisoModuloAsync("EDIT_PRESUPUESTO");
            var permisoProduccion = await ObtenerPermisoModuloAsync("EDIT_PRODUCCION");

            return Json(new
            {
                presupuesto = new
                {
                    puedeLeer = permisoPresupuesto.puedeLeer,
                    puedeEscribir = permisoPresupuesto.puedeEscribir,
                    puedeEliminar = permisoPresupuesto.puedeEliminar
                },
                produccion = new
                {
                    puedeLeer = permisoProduccion.puedeLeer,
                    puedeEscribir = permisoProduccion.puedeEscribir,
                    puedeEliminar = permisoProduccion.puedeEliminar
                }
            });
        }


        //======================================
        // EDITAR PLAN DE PRODUCCION
        //======================================
        [HttpPost("Comercial/ActualizarPlanProduccion")]
        [RevisarPermiso("EDIT_PRODUCCION", "ESCRIBIR")]
        public async Task<IActionResult> ActualizarPlanProduccion([FromBody] BalancePlanProduccionView model)
        {
            // Validacion de permisos delegada al atributo global

            if (model == null || model.Id <= 0)
                return BadRequest(new { success = false, mensaje = "Datos inválidos" });

            try
            {
                var filas = await _context.Database.ExecuteSqlInterpolatedAsync($@"
      UPDATE dbo.PlanDetalle
         SET Peso = {model.Plan}
       WHERE Id = {model.Id}");

                if (filas == 0)
                {
                    return NotFound(new
                    {
                        success = false,
                        mensaje = "Plan de producción no encontrado"
                    });
                }

                return Ok(new
                {
                    success = true,
                    mensaje = "Plan de producción actualizado correctamente.",
                    filas
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    mensaje = "Error al actualizar plan",
                    detalle = ex.Message
                });
            }
        }


        //======================================
        // EDITAR PRESUPUESTOS (GENERAL / CEDIS / VENDEDOR)
        //======================================
        [HttpPost("Comercial/ActualizarPresupuesto")]
        [RevisarPermiso("EDIT_PRESUPUESTO", "ESCRIBIR")]
        public async Task<IActionResult> ActualizarPresupuesto([FromBody] BalancePresupuestoView model)
        {
            // Validacion de permisos delegada al filtro global

            if (model == null || model.Id <= 0)
                return BadRequest(new { success = false, mensaje = "Datos inválidos" });

            var fuente = (model.Fuente ?? "GENERAL").Trim().ToUpperInvariant();

            try
            {
                int filas = 0;

                if (fuente == "CEDIS")
                {
                    filas = await _context.Database.ExecuteSqlInterpolatedAsync($@"
          UPDATE dbo.PresupuestoCedis
             SET PresupuestoAsignado = {model.Presupuesto}
           WHERE Id = {model.Id}");
                }
                else if (fuente == "VENDEDOR")
                {
                    filas = await _context.Database.ExecuteSqlInterpolatedAsync($@"
          UPDATE dbo.PresupuestoVendedor
             SET PresupuestoAsignado = {model.Presupuesto}
           WHERE Id = {model.Id}");
                }
                else
                {
                    filas = await _context.Database.ExecuteSqlInterpolatedAsync($@"
          UPDATE dbo.Presupuestos
             SET Presupuesto = {model.Presupuesto}
           WHERE Id = {model.Id}");
                }

                if (filas == 0)
                {
                    return NotFound(new
                    {
                        success = false,
                        mensaje = "Registro no encontrado",
                        fuente
                    });
                }

                return Ok(new
                {
                    success = true,
                    mensaje = "Presupuesto actualizado correctamente.",
                    fuente,
                    filas
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    mensaje = "Error al actualizar presupuesto",
                    fuente,
                    detalle = ex.Message
                });
            }
        }




        //===================================================================
        // EDITAR KILOS SOLICITADOS (OV / TR)
        //===================================================================
        [HttpPost("Comercial/ActualizarKilosSolicitados")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarKilosSolicitados(
            [FromForm] int idLinea,
            [FromForm] decimal kilosSolicitados,
            [FromForm] string tipoDocumento   // OV | TR
        )
        {
            if (idLinea <= 0)
                return BadRequest(new { mensaje = "Id de línea inválido." });

            if (kilosSolicitados < 0)
                return BadRequest(new { mensaje = "Los kilos no pueden ser negativos." });

            tipoDocumento = (tipoDocumento ?? "OV").Trim().ToUpperInvariant();

            try
            {
                int filas = 0;

                // =========================================================
                // ORDEN DE VENTA
                // =========================================================
                if (tipoDocumento == "OV")
                {
                    var sql = @"
                UPDATE OrdenVentaProducto
                SET Peso = {0}
                WHERE Id = {1}";

                    filas = await _context.Database.ExecuteSqlRawAsync(
                        sql,
                        kilosSolicitados,
                        idLinea
                    );
                }
                // =========================================================
                // TRANSFERENCIA
                // =========================================================
                else if (tipoDocumento == "TR")
                {
                    var sql = @"
                UPDATE TransferenciaDetalles
                SET CantidadKg = {0}
                WHERE Id = {1}";

                    filas = await _context.Database.ExecuteSqlRawAsync(
                        sql,
                        kilosSolicitados,
                        idLinea
                    );
                }
                else
                {
                    return BadRequest(new { mensaje = $"Tipo de documento no soportado: {tipoDocumento}" });
                }

                if (filas == 0)
                    return NotFound(new { mensaje = "Línea no encontrada." });

                return Ok(new
                {
                    mensaje = "Kilos solicitados actualizados.",
                    idLinea,
                    kilosSolicitados,
                    tipoDocumento
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error al actualizar kilos solicitados",
                    detalle = ex.Message
                });
            }
        }



        //======================================================
        // ACTUALIZAR PRECIO OV EN UNA LÍNEA DE ORDEN DE VENTA
        //======================================================
        [HttpPost("Comercial/ActualizarPrecioLinea")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarPrecioLineaSql([FromForm] int idLinea, [FromForm] decimal precioOV)
        {
            if (idLinea <= 0) return BadRequest(new { mensaje = "Id de línea inválido." });
            if (precioOV < 0) return BadRequest(new { mensaje = "El precio no puede ser negativo." });

            try
            {
                // ⚠️ Cambia nombre de tabla/columna si aplica:
                var sql = "UPDATE OrdenVentaProducto SET Precio = {0} WHERE Id = {1}";
                var filas = await _context.Database.ExecuteSqlRawAsync(sql, precioOV, idLinea);

                if (filas == 0) return NotFound(new { mensaje = "Línea no encontrada." });
                return Ok(new { mensaje = "Precio actualizado correctamente.", idLinea, precioOV });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = "Error al actualizar el precio", detalle = ex.Message });
            }
        }




        //======================================
        // EDITAR ROTACION POR U_MASTER
        //======================================
        [HttpPost]
        public async Task<IActionResult> ActualizarRotacion([FromBody] RotacionUpdateModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.U_MASTER))
                return BadRequest("Modelo inválido");

            // UPDATE directo en SQL
            var query = "UPDATE ArticuloSap SET Rotacion = {0} WHERE U_MASTER = {1}";
            var filasAfectadas = await _context.Database.ExecuteSqlRawAsync(query, model.Rotacion, model.U_MASTER);

            if (filasAfectadas == 0)
                return NotFound("No se encontraron registros con ese U_MASTER");

            return Ok(new { mensaje = $"ROT actualizado correctamente para {filasAfectadas} artículos." });
        }



        //======================================
        // CONSULTA PARA MOSTRAR INVENTARIOS EN ORDEN VENTA
        //======================================
        [HttpGet("Comercial/ObtenerInventario")]
        public async Task<IActionResult> ObtenerInventario(string productoCodigo)
        {
            if (string.IsNullOrWhiteSpace(productoCodigo))
                return BadRequest(new { mensaje = "Código de producto no proporcionado." });

            // Normaliza parámetro en C# (opcional, por claridad)
            var codigo = productoCodigo.Trim();

            string sql = @"
        SELECT 
            AlmacenId,
            ProductoCodigo,
            Kg,
            Cajas,
            Almacen,
            Sucursal
        FROM inventariosigo WITH(NOLOCK)
        WHERE UPPER(LTRIM(RTRIM(ProductoCodigo))) = UPPER(LTRIM(RTRIM(@codigo))) AND colonia like '%VENTA%'";

            var parametro = new SqlParameter("@codigo", codigo);

            var inventario = await _context.Set<InventarioSigoView>()
                .FromSqlRaw(sql, parametro)
                .ToListAsync();

            if (inventario == null || inventario.Count == 0)
                return NotFound(new { mensaje = "No se encontró inventario para este producto." });

            return Json(inventario);
        }




        //======================================
        // CATALOGO DE ARTICULO CON PRECIO y KG PROMEDIO
        //======================================

        //https://localhost:7171/Comercial/ObtenerCatalogoCompleto
        [HttpGet("Comercial/ObtenerCatalogoCompleto")]
        public async Task<IActionResult> ObtenerCatalogoCompleto()
        {
            try
            {
                var catalogo = await _sap.CATPrecioArticuloClienteAsync(); // ✅ sin parámetros

                if (catalogo == null || catalogo.Count == 0)
                    return NotFound(new { mensaje = "No se encontraron artículos en el catálogo." });

                return Ok(catalogo);
            }
            catch (HttpRequestException httpEx)
            {
                return StatusCode(502, new
                {
                    error = "Error de comunicación con SAP",
                    detalle = httpEx.Message,
                    sugerencia = "Verifica la URL, filtros y sesión activa en SAP"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Error al consultar SAP",
                    detalle = ex.Message
                });
            }
        }



        //https://localhost:7171/Comercial/SincronizarPrecioCliente con esto puedo ejecutar el procedimiento
        //======================================
        // Sincronizar envio ala tabla de CatalogoPrecioSap
        //======================================       

        [HttpGet("/Comercial/SincronizarPrecioCliente")]
        public async Task<IActionResult> SincronizarPrecioCliente()
        {
            try
            {
                await _sap.SincronizarCatalogoPrecioAsync();
                return Ok("Sincronización completada correctamente");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }


        //======================================
        // PAGINADO ala tabla de CatalogoPrecioSap (con nombres)
        //======================================
        [HttpGet("Comercial/GetCatalogoPrecioJsonPaged")]
        public async Task<IActionResult> GetCatalogoPrecioJsonPaged(int page = 1, int pageSize = 50, string search = "")
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize <= 0 ? 50 : pageSize;

            // Base query con joins para traer nombres
            var q = from p in _context.CatalogoPrecioSap.AsNoTracking()
                    join a in _context.ArticuloSap.AsNoTracking()
                        on p.ProductoCodigo equals a.ProductoCodigo into aj
                    from a in aj.DefaultIfEmpty()
                    join c in _context.ClienteSap.AsNoTracking()
                        on p.Cliente equals c.Cliente into cj
                    from c in cj.DefaultIfEmpty()
                    select new
                    {
                        // claves
                        ProductoCodigo = p.ProductoCodigo,
                        Cliente = p.Cliente,

                        // ✅ nombres
                        ProductoNombre = a != null ? a.ProductoNombre : null,
                        ClienteNombre = c != null ? c.Nombrecliente : null,

                        // otros campos
                        PriceListNum = p.PriceListNum,
                        PriceListName = p.PriceListName,
                        Precio = p.Precio,
                        FechaModificacion = p.FechaModificacion
                    };

            // Filtro de búsqueda
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();

                q = q.Where(x =>
                    (x.ProductoCodigo ?? "").ToLower().Contains(search) ||
                    (x.Cliente ?? "").ToLower().Contains(search) ||
                    (x.PriceListName ?? "").ToLower().Contains(search) ||
                    (x.ProductoNombre ?? "").ToLower().Contains(search) ||   // ✅ nombre producto
                    (x.ClienteNombre ?? "").ToLower().Contains(search)       // ✅ nombre cliente
                );
            }

            var total = await q.CountAsync();

            var precios = await q
                .OrderBy(x => x.ProductoCodigo).ThenBy(x => x.Cliente)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Json(new
            {
                total,
                page,
                pageSize,
                precios
            });
        }



        //======================================
        // REPORTE DE PRESUPUESTO POR MES
        //======================================

        //https://localhost:7171/Comercial/ObtenerPresupuestos?mes=9&a%C3%B1o=2025

        [HttpGet("Comercial/ObtenerPresupuestos")]
        public async Task<IActionResult> ObtenerPresupuestos(int mes, int año)
        {
            try
            {
                if (mes <= 0 || mes > 12)
                    return BadRequest(new { mensaje = "Mes inválido. Debe estar entre 1 y 12." });

                if (año <= 2000)
                    return BadRequest(new { mensaje = "Año inválido." });

                var data = await _context.Set<ReportePresupuestoViewModel>()
                    .FromSqlInterpolated($@"
                SELECT
                    a.Id,
                    a.ClienteId,
                    ISNULL(b.Nombrecliente, '')    AS NombreCliente,   -- 🔴 alias al nombre de la propiedad
                    a.ProductoCodigo,
                    ISNULL(c.ProductoNombre, '')   AS ProductoNombre,  -- 🔴 alias + sin null
                    ISNULL(a.Presupuesto, 0)       AS Presupuesto,     -- 🔴 sin null
                    a.Mes,
                    a.Año
                FROM Presupuestos a
                LEFT JOIN ClienteSap  b ON a.ClienteId = b.Cliente
                LEFT JOIN ArticuloSap c ON a.ProductoCodigo = c.ProductoCodigo
                WHERE a.Mes = {mes} AND a.Año = {año}
                ORDER BY a.Mes
            ")
                    .ToListAsync();

                if (data == null || data.Count == 0)
                    return NotFound(new { mensaje = "No hay registros para el mes y año especificados." });

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Error al consultar base de datos",
                    detalle = ex.Message,
                    stack = ex.StackTrace
                });
            }
        }

        //======================================
        // OBTENER PEDIDOS LIBERADOS DE AUTORIZACION
        // - Si el usuario TIENE VendedorId(s) -> solo sus clientes
        // - Si NO tiene VendedorId(s)         -> todos los pedidos
        //======================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ObtenerPedidosLiberados(CancellationToken ct = default)
        {
            // =====================================================
            // SEGURIDAD POR SERIE DEL USUARIO
            // UsuarioSerie -> Series.NombreSerie -> OrdenVenta.Serie
            // =====================================================
            var verTodasSeries = UsuarioPuedeVerTodasLasSeries();

            var seriesPermitidas = new List<string>();

            if (!verTodasSeries)
            {
                var idsSeries = await ObtenerSeriesIdsUsuarioActualAsync(ct);

                // Si el usuario no tiene series asignadas, no ve nada.
                if (idsSeries == null || !idsSeries.Any())
                    return Json(new List<object>());

                seriesPermitidas = await _context.Series
                    .AsNoTracking()
                    .Where(s => idsSeries.Contains(s.Id))
                    .Select(s => s.NombreSerie)
                    .ToListAsync(ct);

                seriesPermitidas = seriesPermitidas
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpper())
                    .Distinct()
                    .ToList();

                if (!seriesPermitidas.Any())
                    return Json(new List<object>());
            }

            var query =
                from p in _context.OrdenVenta.AsNoTracking()
                join c0 in _context.ClienteSap.AsNoTracking()
                    on p.Cliente equals c0.Cliente into cj
                from c in cj.DefaultIfEmpty()
                where p.Estatus == 3 || p.Estatus == 1
                select new
                {
                    Pedido = p,
                    ClienteSap = c
                };

            if (!verTodasSeries)
            {
                query = query.Where(x =>
                    x.Pedido.Serie != null &&
                    seriesPermitidas.Contains(x.Pedido.Serie.Trim().ToUpper())
                );
            }

            var pedidos = await query
                .OrderByDescending(x => x.Pedido.FechaRegistro)
                .Select(x => new
                {
                    Id = x.Pedido.Id,
                    OrdenDeVenta = x.Pedido.Consecutivo ?? "",
                    Serie = x.Pedido.Serie ?? "",
                    ClienteId = x.Pedido.Cliente ?? "",
                    ClienteNombre = x.ClienteSap != null ? (x.ClienteSap.Nombrecliente ?? "") : "",
                    Vendedor = x.Pedido.Vendedor ?? "",
                    FechaEntrega = x.Pedido.FechaEntrega,
                    FechaRegistro = x.Pedido.FechaRegistro,
                    Presentacion = x.Pedido.Presentacion ?? "",
                    Observacion = x.Pedido.Observacion ?? "",
                    Ruta = x.Pedido.Ruta ?? "",
                    Estatus = x.Pedido.Estatus
                })
                .ToListAsync(ct);

            return Json(pedidos);
        }



        //======================================
        // OBTENER PEDIDOS COMPLETADOS (ESTATUS = 4)
        // - Si el usuario TIENE VendedorId(s) -> solo pedidos de sus clientes
        // - Si NO tiene VendedorId(s)         -> todos los pedidos
        //======================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ObtenerPedidosCompletados(CancellationToken ct = default)
        {
            // =====================================================
            // SEGURIDAD POR SERIE DEL USUARIO
            // UsuarioSerie -> Series.NombreSerie -> OrdenVenta.Serie
            // =====================================================
            var verTodasSeries = UsuarioPuedeVerTodasLasSeries();

            var seriesPermitidas = new List<string>();

            if (!verTodasSeries)
            {
                var idsSeries = await ObtenerSeriesIdsUsuarioActualAsync(ct);

                // Si el usuario no tiene series asignadas, no ve nada.
                if (idsSeries == null || !idsSeries.Any())
                    return Json(new List<object>());

                seriesPermitidas = await _context.Series
                    .AsNoTracking()
                    .Where(s => idsSeries.Contains(s.Id))
                    .Select(s => s.NombreSerie)
                    .ToListAsync(ct);

                seriesPermitidas = seriesPermitidas
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpper())
                    .Distinct()
                    .ToList();

                if (!seriesPermitidas.Any())
                    return Json(new List<object>());
            }

            var query =
                from a in _context.OrdenVenta.AsNoTracking()
                join c0 in _context.ClienteSap.AsNoTracking()
                    on a.Cliente equals c0.Cliente into cj
                from c in cj.DefaultIfEmpty()
                where a.Estatus == 4
                select new
                {
                    Pedido = a,
                    ClienteSap = c
                };

            if (!verTodasSeries)
            {
                query = query.Where(x =>
                    x.Pedido.Serie != null &&
                    seriesPermitidas.Contains(x.Pedido.Serie.Trim().ToUpper())
                );
            }

            var pedidos = await query
                .OrderByDescending(x => x.Pedido.FechaRegistro)
                .Select(x => new
                {
                    Id = x.Pedido.Id,
                    OrdenDeVenta = x.Pedido.Consecutivo ?? "",
                    Serie = x.Pedido.Serie ?? "",
                    ClienteId = x.Pedido.Cliente ?? "",
                    ClienteNombre = x.ClienteSap != null ? (x.ClienteSap.Nombrecliente ?? "") : "",
                    Vendedor = x.Pedido.Vendedor ?? "",
                    FechaEntrega = x.Pedido.FechaEntrega,
                    FechaRegistro = x.Pedido.FechaRegistro,
                    Presentacion = x.Pedido.Presentacion ?? "",
                    Observacion = x.Pedido.Observacion ?? "",
                    Ruta = x.Pedido.Ruta ?? "",
                    Estatus = x.Pedido.Estatus
                })
                .ToListAsync(ct);

            return Json(pedidos);
        }


        //======================================
        // OBTENER PEDIDOS CERRADOS EN PEDIDOS DE VENTA
        //======================================
        [HttpGet]
        public IActionResult ProductosSurtidos(int id) // 👈 cambia Guid → int
        {
            var items = (from d in _context.PedidoVentaProducto
                         where d.PedidoVentaId == id   // 👈 usa la FK real
                         orderby d.Id
                         select new
                         {
                             ProductoCodigo = d.ProductoCodigo,
                             ProductoNombre = d.ProductoNombre,
                             KilosCaja = d.KilosCaja,
                             Precio = d.Precio,
                             Cajas = d.Cajas
                         }).ToList();

            return Json(items);
        }






        //======================================
        // OBTENER DATOS DE LA ORDEN DE VENTA (JSON) + PRODUCTOS
        // Ruta: /Comercial/OrdenVentaJson?id={id}
        //======================================
        [HttpGet("Comercial/OrdenVentaJson")]
        public async Task<IActionResult> OrdenVentaJson(int id)
        {
            // 1) Encabezado (OrdenVenta + ClienteSap)
            var head = await (
                from p in _context.OrdenVenta.AsNoTracking()
                where p.Id == id
                join c in _context.ClienteSap.AsNoTracking()
                    on p.Cliente equals c.Cliente into gj
                from c in gj.DefaultIfEmpty()
                select new
                {
                    p.Id,
                    p.Consecutivo,
                    p.Serie,
                    FechaEntrega = (DateTime?)p.FechaEntrega,
                    p.FechaRegistro,
                    Cliente = p.Cliente,
                    ClienteNombre = c != null ? c.Nombrecliente : p.Cliente,
                    p.Vendedor,
                    p.Ruta,
                    p.Presentacion,
                    p.Observacion,
                    p.Saldo,
                    p.OtrosPedidos,
                    p.Credito,
                    p.Estatus
                }
            ).FirstOrDefaultAsync();

            if (head == null)
                return NotFound();

            // 2) Última gestión de PedidoVenta de esta OV
            var lastPV = await _context.PedidoVenta
                .AsNoTracking()
                .Where(pv => pv.OrdenVentaId == id)
                .OrderByDescending(pv => pv.FechaGestion)
                .Select(pv => new
                {
                    pv.Id,
                    pv.FechaEmbarque,
                    pv.AlmacenSurtir
                })
                .FirstOrDefaultAsync();

            // 3) Productos: si hay PedidoVenta → tomar de PedidoVentaProducto
            List<OrdenVentaDetalleDto> productos;

            if (lastPV != null)
            {
                productos = await _context.PedidoVentaProducto
                    .AsNoTracking()
                    .Where(d => d.PedidoVentaId == lastPV.Id)
                    .OrderBy(d => d.Id)
                    .Select(d => new OrdenVentaDetalleDto
                    {
                        DetalleId = d.Id,
                        ProductoCodigo = d.ProductoCodigo,
                        ProductoNombre = d.ProductoNombre,
                        KilosCaja = d.KilosCaja,
                        Precio = d.Precio,
                        Cajas = d.Cajas,
                        Importe = d.Precio * d.KilosCaja,
                        Almacen = d.Almacen ?? string.Empty,
                        Presupuesto = 0m,
                        VariacionPresupuesto = 0m
                    })
                    .ToListAsync();
            }
            else
            {
                // Fallback: detalle original de la OV
                productos = await _context.OrdenVenta
                    .AsNoTracking()
                    .Where(p => p.Id == id)
                    .SelectMany(p => p.Productos)
                    .OrderBy(d => d.Id)
                    .Select(d => new OrdenVentaDetalleDto
                    {
                        DetalleId = d.Id,
                        ProductoCodigo = d.ProductoCodigo,
                        ProductoNombre = d.ProductoNombre,
                        Cajas = d.Cajas,
                        KilosCaja = d.Cajas > 0 ? (decimal?)(d.Peso / d.Cajas) : d.Peso,
                        Precio = d.Precio,
                        Importe = d.Peso * d.Precio,
                        Almacen = string.Empty,
                        Presupuesto = 0m,
                        VariacionPresupuesto = 0m
                    })
                    .ToListAsync();
            }

            // 4) DTO final
            var dto = new OrdenVentaDto
            {
                Id = head.Id,
                PedidoVentaId = lastPV?.Id,
                Consecutivo = head.Consecutivo,
                Serie = head.Serie,
                FechaEntrega = head.FechaEntrega,
                FechaRegistro = head.FechaRegistro,
                Cliente = head.Cliente,
                ClienteNombre = head.ClienteNombre,
                Vendedor = head.Vendedor,
                Ruta = head.Ruta,
                Presentacion = head.Presentacion,
                Observacion = head.Observacion,
                Saldo = head.Saldo,
                OtrosPedidos = head.OtrosPedidos,
                Credito = head.Credito,
                Estatus = head.Estatus,
                FechaEmbarque = lastPV?.FechaEmbarque,
                AlmacenSurtir = lastPV?.AlmacenSurtir,
                Productos = productos
            };

            return Json(dto);
        }




        //======================================
        // GUARDAR PEDIDOS YA LIBERADOS Y LISTOS PARA GENERAR PDF
        //======================================

        // POST: /Comercial/GestionarPedido
        [HttpPost]
        public async Task<IActionResult> GestionarPedido([FromBody] Plataforma_CG.ViewModels.GestionarPedidoRequest req)
        {
            if (req == null || req.OrdenId <= 0)
                return BadRequest("Solicitud inválida.");

            var reqProductos = (req.Productos ?? new List<Plataforma_CG.ViewModels.GestionarPedidoRequest.GestionarProductoItem>()).ToList();
            var activos = reqProductos.Where(p => !p.Eliminado && p.Cajas > 0).ToList();

            await using var tx = await _context.Database.BeginTransactionAsync();

            // 1) OV
            var ov = await _context.OrdenVenta
                .Include(x => x.Productos)
                .FirstOrDefaultAsync(x => x.Id == req.OrdenId);

            if (ov == null)
                return NotFound("Orden de venta no encontrada.");


            // 3) Totales (solo activos)
            var totalImporte = activos.Sum(x => x.Precio * x.KilosCaja * x.Cajas);
            var totalPeso = ov.Productos.Where(l => !l.Eliminado).Sum(l => l.Peso);

            static string NormalizarAlmacen(string? linea, string? global)
                => (linea ?? global ?? string.Empty).Trim().ToUpperInvariant();

            // 4) PV (reemplazar detalle; admite duplicados)
            var pv = await _context.PedidoVenta
                .Include(pv => pv.Productos)
                .FirstOrDefaultAsync(pv => pv.OrdenVentaId == ov.Id);

            if (pv == null)
            {
                pv = new PedidoVenta
                {
                    OrdenVentaId = ov.Id,
                    OrdenVentaConsecutivo = ov.Consecutivo ?? string.Empty,
                    Cliente = ov.Cliente ?? string.Empty,
                    Vendedor = ov.Vendedor ?? string.Empty,
                    FechaEntrega = ov.FechaEntrega,
                    FechaEmbarque = req.FechaEmbarque,
                    AlmacenSurtir = (req.AlmacenSurtir ?? string.Empty).ToUpperInvariant(),
                    FechaGestion = DateTime.UtcNow,
                    TotalImporte = totalImporte,
                    TotalPeso = totalPeso,
                    Productos = activos.Select(x => new PedidoVentaProducto
                    {
                        ProductoCodigo = x.ProductoCodigo ?? string.Empty,
                        ProductoNombre = x.ProductoNombre ?? string.Empty,
                        // Guardamos KG TOTALES del renglón en el campo KilosCaja del PV (para tu PDF)
                        KilosCaja = x.KilosCaja * x.Cajas,
                        Precio = x.Precio,
                        Cajas = x.Cajas,
                        Almacen = NormalizarAlmacen(x.Almacen, req.AlmacenSurtir)
                    }).ToList()
                };
                _context.PedidoVenta.Add(pv);
            }
            else
            {
                pv.FechaEmbarque = req.FechaEmbarque;
                pv.AlmacenSurtir = (req.AlmacenSurtir ?? string.Empty).ToUpperInvariant();
                pv.FechaGestion = DateTime.UtcNow;
                pv.TotalImporte = totalImporte;
                pv.TotalPeso = totalPeso;

                _context.PedidoVentaProducto.RemoveRange(pv.Productos);
                pv.Productos = activos.Select(x => new PedidoVentaProducto
                {
                    ProductoCodigo = x.ProductoCodigo ?? string.Empty,
                    ProductoNombre = x.ProductoNombre ?? string.Empty,
                    KilosCaja = x.KilosCaja * x.Cajas, // KG TOTALES
                    Precio = x.Precio,
                    Cajas = x.Cajas,
                    Almacen = NormalizarAlmacen(x.Almacen, req.AlmacenSurtir)
                }).ToList();

                _context.PedidoVenta.Update(pv);
            }

            // 5) Completar OV
            ov.Estatus = 4;
            ov.FechaEmbarque = req.FechaEmbarque;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { ok = true, pedidoVentaId = pv.Id });
        }





        // GET (opcional): /Comercial/GestionPedido?idOv=123
        // Devuelve el gestionado si existe (para precargar)
        [HttpGet]
        public async Task<IActionResult> GestionPedido(int idOv)
        {
            var pv = await _context.PedidoVenta
                .AsNoTracking()
                .Include(p => p.Productos)
                .FirstOrDefaultAsync(p => p.OrdenVentaId == idOv);

            if (pv == null) return NotFound();

            return Json(new
            {
                pv.Id,
                pv.OrdenVentaId,
                pv.OrdenVentaConsecutivo,
                pv.FechaEmbarque,
                pv.AlmacenSurtir,
                pv.TotalImporte,
                pv.TotalPeso,
                Productos = pv.Productos.Select(d => new
                {
                    d.ProductoCodigo,
                    d.ProductoNombre,
                    d.KilosCaja,
                    d.Precio,
                    d.Cajas
                }).ToList()
            });
        }

        private async Task<string?> ResolverCadenaMeatPorSerieAsync(string? serie, CancellationToken ct = default)
        {
            var serieUp = (serie ?? "").Trim().ToUpper();

            var infoSerie = await _context.Series
                .AsNoTracking()
                .Where(s => s.NombreSerie == serie)
                .Select(s => new
                {
                    s.NombreSerie,
                    s.Sucursal,
                    s.Canal
                })
                .FirstOrDefaultAsync(ct);

            var nombreSerie = (infoSerie?.NombreSerie ?? serieUp).Trim().ToUpper();
            var sucursal = (infoSerie?.Sucursal ?? "").Trim().ToUpper();
            var canal = (infoSerie?.Canal ?? "").Trim().ToUpper();

            var esTif =
                nombreSerie.Contains("TIF") ||
                sucursal.Contains("TIF") ||
                canal.Contains("TIF");

            var nombreCadena = esTif ? "CadenaMeatTIF" : "CadenaMeatP1";

            var cadena = _configuration.GetConnectionString(nombreCadena);

            if (string.IsNullOrWhiteSpace(cadena))
            {
                _logger.LogWarning("No está configurada la cadena MEAT: {Cadena}", nombreCadena);
                return null;
            }

            return cadena;
        }

        private async Task<bool> IntentarCancelarSolicitudMeatAsync(
    string? serie,
    string? documentoMeat,
    CancellationToken ct = default)
        {
            var doc = (documentoMeat ?? "").Trim();

            if (string.IsNullOrWhiteSpace(doc))
                return false;

            if (!int.TryParse(doc, out var solicitudSurtidoId))
            {
                _logger.LogWarning("Documento MEAT inválido: {DocumentoMeat}", doc);
                return false;
            }

            var cadena = await ResolverCadenaMeatPorSerieAsync(serie, ct);

            if (string.IsNullOrWhiteSpace(cadena))
                return false;

            try
            {
                await using var cn = new SqlConnection(cadena);
                await cn.OpenAsync(ct);

                const string sql = @"
UPDATE SolicitudSurtido
SET EstatusId = 4
WHERE SolicitudSurtidoId = @SolicitudSurtidoId;
";

                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.Add("@SolicitudSurtidoId", SqlDbType.Int).Value = solicitudSurtidoId;

                var rows = await cmd.ExecuteNonQueryAsync(ct);

                if (rows == 0)
                {
                    _logger.LogWarning(
                        "No se encontró SolicitudSurtidoId {SolicitudSurtidoId} en MEAT.",
                        solicitudSurtidoId);

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al cancelar SolicitudSurtidoId {SolicitudSurtidoId} en MEAT.",
                    solicitudSurtidoId);

                return false;
            }
        }

        //======================================
        // CANCELAR ORDEN DE VENTA (con password de appsettings)
        // POST: /Comercial/CancelarOrdenVenta
        //======================================
        [HttpPost("Comercial/CancelarOrdenVenta")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarOrdenVenta([FromBody] CancelarOvDto dto, CancellationToken ct = default)
        {
            if (dto == null || dto.OrdenId <= 0)
                return BadRequest("Solicitud inválida.");

            var cancelPassword = _configuration["SIGO:CancelPassword"];

            if (string.IsNullOrWhiteSpace(cancelPassword))
                return StatusCode(500, "No se ha configurado la contraseña de cancelación.");

            if (string.IsNullOrWhiteSpace(dto.Password) || !string.Equals(dto.Password, cancelPassword))
                return Unauthorized("Contraseña inválida.");

            var ov = await _context.OrdenVenta
                .FirstOrDefaultAsync(x => x.Id == dto.OrdenId, ct);

            if (ov == null)
                return NotFound("Orden de venta no encontrada.");

            if (ov.Estatus == 0)
                return Ok(new { ok = true, yaCancelada = true });

            var subpedidos = await _context.Subpedidos
                .AsNoTracking()
                .Where(s => s.OrdenVentaId == ov.Id)
                .Select(s => new
                {
                    s.Id,
                    s.SubFolio,
                    s.U_DocMeat
                })
                .ToListAsync(ct);

            var docsMeatCancelados = 0;
            var docsMeatNoCancelados = 0;

            foreach (var sp in subpedidos)
            {
                var documentoMeat = (sp.U_DocMeat ?? "").Trim();

                if (string.IsNullOrWhiteSpace(documentoMeat))
                    continue;

                var canceladoMeat = await IntentarCancelarSolicitudMeatAsync(
                    ov.Serie,
                    documentoMeat,
                    ct);

                if (canceladoMeat)
                    docsMeatCancelados++;
                else
                    docsMeatNoCancelados++;
            }

            ov.Estatus = 0;

            // Si tienes estos campos en tu modelo, puedes activarlos:
            // ov.FechaCancelacion = DateTime.Now;
            // ov.UsuarioCancelacion = User?.Identity?.Name;

            await _context.SaveChangesAsync(ct);

            return Ok(new
            {
                ok = true,
                msg = "Orden de venta cancelada correctamente.",
                docsMeatCancelados,
                docsMeatNoCancelados
            });
        }


        //======================================
        // CLIENTE MIXTO LOCAL
        // - Si el usuario TIENE VendedorId(s) -> filtra por esos vendedores
        // - Si NO tiene VendedorId(s)         -> muestra TODOS los clientes
        // - NO consulta SAP aquí
        //======================================
        [Authorize]
        [HttpGet("Comercial/BuscarClientesMixto")]
        [Produces("application/json")]
        public async Task<IActionResult> BuscarClientesMixto(
            [FromQuery] string term = "",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 25;
            if (pageSize > 200) pageSize = 200;

            var vendedorIds = new List<int>();

            var vClaim = User.FindFirst("VendedorId")?.Value?.Trim();

            if (!string.IsNullOrWhiteSpace(vClaim))
            {
                if (vClaim.Contains(","))
                {
                    vendedorIds = vClaim
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();
                }
                else
                {
                    var clean = new string(vClaim.Where(char.IsDigit).ToArray());

                    vendedorIds = Enumerable
                        .Range(0, clean.Length / 2)
                        .Select(i => clean.Substring(i * 2, 2))
                        .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();
                }
            }

            if (vendedorIds.Count == 0)
            {
                var raw = (User?.Identity?.Name ?? "").Trim();
                var username = raw.Contains('\\') ? raw.Split('\\').Last() : raw;
                var usernameEmail = username.Contains('@') ? username : $"{username}@carnesg.net";

                try
                {
                    int? vendAD = null;

                    if (_uDb?.UsuariosAD != null)
                    {
                        vendAD = await _uDb.UsuariosAD.AsNoTracking()
                            .Where(x =>
                                x.UsuarioAd == raw ||
                                x.UsuarioAd == username ||
                                x.UsuarioAd == usernameEmail)
                            .Select(x => (int?)x.VendedorId)
                            .Where(v => v.HasValue && v.Value > 0)
                            .FirstOrDefaultAsync();
                    }

                    int? vendApp = null;

                    if (!(vendAD.HasValue && vendAD.Value > 0) && _uDb?.Usuarios != null)
                    {
                        vendApp = await _uDb.Usuarios.AsNoTracking()
                            .Where(x =>
                                x.Usuario == raw ||
                                x.Usuario == username ||
                                x.Usuario == usernameEmail)
                            .Select(x => (int?)x.VendedorId)
                            .Where(v => v.HasValue && v.Value > 0)
                            .FirstOrDefaultAsync();
                    }

                    var vend = vendAD ?? vendApp;

                    if (vend.HasValue && vend.Value > 0)
                        vendedorIds.Add(vend.Value);
                }
                catch
                {
                    vendedorIds.Clear();
                }
            }

            bool verTodos = vendedorIds.Count == 0;

            var q = (term ?? string.Empty).Trim();
            var pattern = $"%{q}%";

            var baseQuery = _context.ClienteSap.AsNoTracking()
                .Where(c =>
                    (verTodos || (c.VendedorId.HasValue && vendedorIds.Contains(c.VendedorId.Value)))
                    &&
                    (
                        q == "" ||
                        EF.Functions.Like(c.Cliente, pattern) ||
                        EF.Functions.Like(c.Nombrecliente, pattern)
                    )
                );

            var total = await baseQuery.CountAsync();

            var clientesLocal = await baseQuery
                .OrderBy(c => c.Nombrecliente)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    cardCode = c.Cliente,
                    cardName = c.Nombrecliente,
                    vendedorId = c.VendedorId,
                    vendedorNombre = c.VendedorNombre,
                    esCanalCedis = c.U_CANAL != null && c.U_CANAL.StartsWith("CEDIS"),

                    // ✅ NUEVO CAMPO IMPORTANTE
                    aplicaPresupuesto = c.AplicaPresupuesto ? 1 : 0
                })
                .ToListAsync();

            var items = clientesLocal.Select(loc => new
            {
                label = $"{(loc.cardName ?? string.Empty).ToUpperInvariant()} ({loc.cardCode})",
                value = loc.cardCode,
                cardCode = loc.cardCode,
                cardName = loc.cardName,
                vendedorId = loc.vendedorId,
                vendedorNombre = string.IsNullOrWhiteSpace(loc.vendedorNombre) ? "No asignado" : loc.vendedorNombre,
                esCanalCedis = loc.esCanalCedis,

                // ✅ NUEVO CAMPO IMPORTANTE
                aplicaPresupuesto = loc.aplicaPresupuesto,

                credito = 0m,
                saldo = 0m,
                sumpedidos = 0m,
                saldoVencido = 0m
            }).ToList();

            return Json(new
            {
                total,
                items
            });
        }


        [Authorize]
        [HttpGet("Comercial/ObtenerComplementoCliente")]
        [Produces("application/json")]
        public async Task<IActionResult> ObtenerComplementoCliente([FromQuery] string cardCode)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return BadRequest(new { mensaje = "cardCode es requerido" });

            decimal ToDec(object? v)
                => v != null && decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

            if (_sap == null)
            {
                return Json(new
                {
                    cardCode,
                    credito = 0m,
                    saldo = 0m,
                    sumpedidos = 0m,
                    saldoVencido = 0m,
                    success = false
                });
            }

            try
            {
                var s = await _sap.ObtenerClientePorCodigoAsync(cardCode);
                if (s == null)
                {
                    return Json(new
                    {
                        cardCode,
                        credito = 0m,
                        saldo = 0m,
                        sumpedidos = 0m,
                        saldoVencido = 0m,
                        success = false
                    });
                }

                var t = s.GetType();

                var credito = ToDec(
                    t.GetProperty("CreditLimit")?.GetValue(s) ??
                    t.GetProperty("CreditLine")?.GetValue(s)
                );

                var saldo = ToDec(
                    t.GetProperty("CurrentAccountBalance")?.GetValue(s) ??
                    t.GetProperty("Balance")?.GetValue(s)
                );

                var sumpedidos = ToDec(
                    t.GetProperty("TotalPendiente")?.GetValue(s) ??
                    t.GetProperty("OpenOrders")?.GetValue(s)
                );

                var saldoVencido = ToDec(
                    t.GetProperty("SaldoVencido")?.GetValue(s) ??
                    t.GetProperty("OverdueBalance")?.GetValue(s)
                );

                return Json(new
                {
                    cardCode,
                    credito,
                    saldo,
                    sumpedidos,
                    saldoVencido,
                    success = true
                });
            }
            catch
            {
                return Json(new
                {
                    cardCode,
                    credito = 0m,
                    saldo = 0m,
                    sumpedidos = 0m,
                    saldoVencido = 0m,
                    success = false
                });
            }
        }





        // ===============================================
        // PRESUPUESTO LOCAL (últimos N meses) por CEDIS
        // Fuente: dbo.sap_invoice_lines + ArticuloSap (U_MASTER) + ClienteSap (U_CANAL)
        // GET: /Comercial/ObtenerPresupuestoPorCedis?canalLike=%25CEDIS%25&meses=12
        // (Opcional) Si tienes columna del CEDIS en ClienteSap, descomenta y usa ?cedisId=...
        //https://localhost:7171/Comercial/ObtenerPresupuestoPorCedis2?canal=CEDIS-MDA
        // ===============================================
        [HttpGet("Comercial/ObtenerPresupuestoPorCedis2")]
        public async Task<IActionResult> ObtenerPresupuestoPorCedis2(
    string canal,          // p.ej. "CEDIS" / "Detalle" / "Autoservicio"
    string cedisId = null, // si tu tabla ClienteSap tiene U_CEDIS o CedisId
    int meses = 12)
        {
            if (meses <= 0) meses = 12;
            if (string.IsNullOrWhiteSpace(canal))
                return BadRequest("El canal es obligatorio.");

            var hoy = DateTime.Today;
            var desdeBase = hoy.AddMonths(-(meses - 1));
            var desde = new DateTime(desdeBase.Year, desdeBase.Month, 1);

            var cs = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, "No se encontró la cadena de conexión 'DefaultConnection'.");

            // IMPORTANTE: usa igualdad para que el índice de U_CANAL se aproveche.
            // Si SOLO tienes el texto "contiene CEDIS", usa LIKE 'CEDIS%' (prefijo), no '%CEDIS%'.
            var sql = @"
        SELECT
            l.sku                                        AS sku,
            ISNULL(a.U_MASTER, 'SIN_MASTER')             AS master,
            SUM(l.Kilos)                                 AS totalKilos,
            CAST(DATEFROMPARTS(YEAR(l.doc_date), MONTH(l.doc_date), 1) AS date) AS fecha
        FROM dbo.sap_invoice_lines AS l WITH (NOLOCK)
        INNER JOIN dbo.ClienteSap   AS c WITH (NOLOCK) ON c.Cliente = l.card_code
        LEFT  JOIN dbo.ArticuloSap  AS a WITH (NOLOCK) ON a.ProductoCodigo = l.sku
        WHERE c.U_CANAL = @canal            -- <- sargable
          AND l.doc_date >= @desde
          /**/ AND (@cedisId IS NULL OR c.U_Canal = @cedisId) /**/ -- opcional si existe la columna
        GROUP BY
            l.sku,
            a.U_MASTER,
            YEAR(l.doc_date),
            MONTH(l.doc_date)
        ORDER BY fecha, l.sku
        OPTION (RECOMPILE); -- evita planes malos en distintos parámetros
    ";

            var lista = new List<object>();
            try
            {
                using var con = new SqlConnection(cs);
                await con.OpenAsync();

                using var cmd = new SqlCommand(sql, con);
                cmd.CommandTimeout = 120; // sube timeout (seg)
                cmd.Parameters.AddWithValue("@canal", canal.Trim());
                cmd.Parameters.AddWithValue("@desde", desde);
                cmd.Parameters.AddWithValue("@cedisId", (object?)cedisId ?? DBNull.Value);

                using var rd = await cmd.ExecuteReaderAsync();
                int iSku = rd.GetOrdinal("sku");
                int iMaster = rd.GetOrdinal("master");
                int iKilos = rd.GetOrdinal("totalKilos");
                int iFecha = rd.GetOrdinal("fecha");

                while (await rd.ReadAsync())
                {
                    var sku = rd.IsDBNull(iSku) ? "" : rd.GetString(iSku);
                    var master = rd.IsDBNull(iMaster) ? "" : rd.GetString(iMaster);
                    var kilos = rd.IsDBNull(iKilos) ? 0m : rd.GetDecimal(iKilos);
                    var fecha = rd.GetDateTime(iFecha);

                    lista.Add(new
                    {
                        sku,
                        master = string.IsNullOrWhiteSpace(master) ? "SIN_MASTER" : master,
                        totalKilos = Math.Round(kilos, 2, MidpointRounding.AwayFromZero),
                        fecha
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al consultar ventas por CEDIS", detalle = ex.Message });
            }

            return Json(lista);
        }



        // ===============================================
        // OBTENER NOMBRE DEL CANAL DE CADA UNO DE LOS CEDIS
        // ===============================================

        [HttpGet("Comercial/ObtenerCanalesCedis")]
        public IActionResult ObtenerCanalesCedis()
        {
            try
            {
                var canales = _context.ClienteSap
                    .Where(c => c.U_CANAL.StartsWith("CEDIS"))
                    .Select(c => c.U_CANAL)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                return Json(canales);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al consultar canales", detalle = ex.Message });
            }
        }


        // ===============================================
        // GUARDAR PRESUPUESTO DE LOS CEDIS
        // ===============================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarPresupuestoCedis([FromForm] PresupuestoCedisSaveVM model)
        {
            // Detectar si viene de fetch (AJAX)
            bool isAjax = string.Equals(
                Request.Headers["X-Requested-With"].ToString(),
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase
            );

            if (string.IsNullOrWhiteSpace(model.Canal))
                model.Canal = Request.Form["selCanal"].ToString();

            // Rescate por si el binder dejó 0 (submit por Enter, etc.)
            if (model.Mes == 0 && int.TryParse(Request.Form["Mes"], out var mesPost)) model.Mes = mesPost;
            if (model.Anio == 0 && int.TryParse(Request.Form["Anio"], out var anioPost)) model.Anio = anioPost;

            // ==== VALIDACIONES INICIALES ====
            if (string.IsNullOrWhiteSpace(model.Canal))
            {
                const string msg = "Debes seleccionar un canal (U_CANAL).";
                if (isAjax) return BadRequest(new { ok = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("PresupuestoCedis");
            }

            if (model.Mes < 1 || model.Mes > 12 || model.Anio <= 0)
            {
                string msg = $"Periodo inválido. (Mes={model.Mes}, Anio={model.Anio})";
                if (isAjax) return BadRequest(new { ok = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("PresupuestoCedis");
            }

            if (model.Items == null || model.Items.Count == 0)
            {
                const string msg = "No hay renglones para guardar.";
                if (isAjax) return BadRequest(new { ok = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("PresupuestoCedis");
            }

            if (model.Items.Any(i => i.Presupuesto <= 0))
            {
                const string msg = "Hay presupuestos en 0. Corrige e intenta de nuevo.";
                if (isAjax) return BadRequest(new { ok = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("PresupuestoCedis");
            }

            // Normalizar items por SKU
            var items = model.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.ProductoCodigo))
                .GroupBy(i => i.ProductoCodigo.Trim().ToUpper())
                .Select(g =>
                {
                    var first = g.First();
                    return new PresupuestoCedisItemVM
                    {
                        ProductoCodigo = g.Key,
                        Master = (first.Master ?? "SIN_MASTER").Trim(),
                        Objetivo = first.Objetivo,       // decimal
                        Presupuesto = first.Presupuesto, // decimal
                        Comentario = first.Comentario
                    };
                })
                .ToList();

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var canal = model.Canal.Trim();
                var mes = model.Mes;
                var anio = model.Anio;   // <- sin ñ en la propiedad C#

                var skus = items.Select(i => i.ProductoCodigo).ToList();

                var existentes = await _context.PresupuestoCedis
                    .Where(p => p.Canal == canal
                                && p.Anio == anio      // propiedad C# sin ñ, mapeada a DB
                                && p.Mes == mes
                                && skus.Contains(p.ProductoCodigo))
                    .Select(p => p.ProductoCodigo)
                    .ToListAsync();

                if (existentes.Any())
                {
                    var lista = string.Join(", ", existentes);
                    var msg = $"Ya existen SKUs en {canal}/{mes:00}-{anio}: {lista}";

                    if (isAjax)
                        return BadRequest(new { ok = false, message = msg });

                    TempData["Error"] = msg;
                    return RedirectToAction("PresupuestoCedis");
                }

                var ahora = DateTime.UtcNow;
                var usuario = User?.Identity?.Name ?? "web";

                var rows = items.Select(i => new PresupuestoCedis
                {
                    Canal = canal,
                    Anio = anio,
                    Mes = mes,
                    ProductoCodigo = i.ProductoCodigo,
                    Master = i.Master,
                    Objetivo = i.Objetivo,
                    PresupuestoAsignado = i.Presupuesto,
                    Comentario = i.Comentario,
                    CreadoPor = usuario,
                    CreadoEn = ahora
                }).ToList();

                _context.PresupuestoCedis.AddRange(rows);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                var okMsg = $"Se guardaron {rows.Count} renglones para {canal} {mes:00}-{anio}.";

                if (isAjax)
                    return Ok(new { ok = true, message = okMsg, rows = rows.Count });

                TempData["Ok"] = okMsg;
                return RedirectToAction("PresupuestoCedis");
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                var msg = "Error al guardar presupuesto (DB): " + (ex.GetBaseException()?.Message ?? ex.Message);

                if (isAjax)
                    return StatusCode(500, new { ok = false, message = msg });

                TempData["Error"] = msg;
                return RedirectToAction("PresupuestoCedis");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                var msg = "Error inesperado: " + ex.Message;

                if (isAjax)
                    return StatusCode(500, new { ok = false, message = msg });

                TempData["Error"] = msg;
                return RedirectToAction("PresupuestoCedis");
            }
        }





        //======================================
        // REPORTE PRESUPUESTOS POR CEDIS
        //======================================
        //https://localhost:7171/Comercial/ObtenerPresupuestoReporteCedis?anio=2025&mes=11

        [HttpGet("Comercial/ObtenerPresupuestoReporteCedis")]
        public async Task<IActionResult> ObtenerPresupuestoReporteCedis(
       int anio,
       int mes,
       string? canal = null,
       string? uMaster = null)
        {
            try
            {
                // === Validaciones estilo "Presupuestos" ===
                if (mes < 1 || mes > 12)
                    return BadRequest(new { mensaje = "Mes inválido. Debe estar entre 1 y 12." });

                if (anio <= 2000)
                    return BadRequest(new { mensaje = "Año inválido." });

                // Nota:
                // - COALESCE en presupuesto para evitar nulls
                // - Alias consistentes con ViewModels (ProductoNombre, Cedis, Presupuesto)
                // - Filtro por canal robusto (UPPER + TRIM) y uMaster opcional
                var filas = await _context.Set<PresupuestoCedisView>()
                    .FromSqlInterpolated($@"
                SELECT
                    a.Id,
                    d.U_MASTER,
                    a.ProductoCodigo,
                    ISNULL(d.ProductoNombre, '')                     AS ProductoNombre,    -- alias + sin null
                    a.Canal                                          AS Cedis,            -- alias consistente
                    CAST(ROUND(COALESCE(a.PresupuestoAsignado,
                                        a.Presupuesto, 0), 0) AS int) AS Presupuesto,     -- sin null, entero
                    a.Mes,
                    a.Anio
                FROM presupuestoCedis a
                INNER JOIN ArticuloSap d ON a.ProductoCodigo = d.ProductoCodigo
                -- Si necesitas U_MASTER desde ArticuloSap b (otro alias), puedes agregarlo:
                -- INNER JOIN ArticuloSap b ON a.ProductoCodigo = b.ProductoCodigo
                WHERE a.Mes  = {mes}
                  AND a.Anio = {anio}
                  AND (
                        {uMaster} IS NULL 
                        OR d.U_MASTER = {uMaster}
                      )
                  AND (
                        {canal} IS NULL
                        OR LTRIM(RTRIM(UPPER(a.Canal))) = LTRIM(RTRIM(UPPER({canal})))
                      )
                ORDER BY d.U_MASTER, a.ProductoCodigo
            ")
                    .ToListAsync();

                if (filas == null || filas.Count == 0)
                    return NotFound(new { mensaje = "No hay registros para el mes/año (y CEDIS) especificados." });

                // Igual que tu endpoint de Presupuestos: devolvemos la lista "plana"
                return Ok(filas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Error al consultar base de datos.",
                    detalle = ex.Message,
                    stack = ex.StackTrace
                });
            }
        }

        //=======================================================
        //  CONVERTIR ALMACEN EN ID PARA GUARDAR EN BASE DE DATOS
        //=======================================================

        [HttpGet]
        public IActionResult Crear()
        {
            var vm = new AlmacenViewModel
            {
                Almacenes = GetAlmacenes()
            };
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> Crear(AlmacenViewModel vm)
        {
            // Asegura que la lista exista si hay que re-renderizar la vista
            vm.Almacenes ??= GetAlmacenes();

            // 1) Si el form ya mandó el ID (caso recomendado), úsalo.
            var id = (vm.SelectedAlmacenId ?? "").Trim();

            // 2) Si por alguna razón el form mandó el NOMBRE (por ej. name="ovAlmacen"),
            //    mapeamos nombre -> Id antes de validar/guardar.
            if (string.IsNullOrWhiteSpace(id))
            {
                var nombre = (Request.Form["SelectedAlmacenNombre"].FirstOrDefault()
                              ?? Request.Form["ovAlmacen"].FirstOrDefault()
                              ?? "").Trim();

                if (!string.IsNullOrEmpty(nombre))
                {
                    var lista = _configuration.GetSection("Warehouses").Get<List<WarehouseOption>>() ?? new();
                    id = lista.FirstOrDefault(w => string.Equals(w.Name, nombre, StringComparison.OrdinalIgnoreCase))?.Id ?? "";
                    vm.SelectedAlmacenId = id;
                }
            }

            // Validación final
            if (string.IsNullOrWhiteSpace(id))
            {
                ModelState.AddModelError(nameof(vm.SelectedAlmacenId), "Selecciona un almacén.");
                return View(vm);
            }

            var almacenesPermitidos = await ObtenerIdsAlmacenesPermitidosParaUsusarioAcualAsync();

            if (!almacenesPermitidos.Contains(id))
            {
                return Unauthorized();
            }

            // Guardar ID en BD (p.ej. 3)
            // _repo.GuardarAlmacen(int.Parse(id));

            return RedirectToAction("Index");
        }


        //=======================================================
        //  OBTENER EL NOMBRE DEL ALMACEN DESDE APPSETTINGS.JSON
        //=======================================================
        [HttpGet]
        public IActionResult ObtenerAlmacenes()
        {
            var almacenes = _configuration.GetSection("Warehouses")
                .Get<List<WarehouseOption>>() ?? new List<WarehouseOption>();

            return Json(almacenes);
        }



        //=======================================================
        //  OBTENER DATOS PARA MAPA DE CARGA
        //=======================================================

        [HttpGet("Comercial/ObtenerPedidosMapaCarga")]
        public async Task<IActionResult> ObtenerPedidosMapaCarga(
         int estatus = 4,
         string? vendedor = null,
         string? cliente = null,
         int? consecutivo = null,
         int top = 500,
         int cajasPorPallet = 60)
        {
            try
            {
                if (estatus < 0) return BadRequest(new { mensaje = "Estatus inválido." });
                if (top <= 0 || top > 5000) top = 500;
                if (cajasPorPallet <= 0) cajasPorPallet = 60;

                var sql = @"
WITH Base AS (
    SELECT TOP(@top)
           ISNULL(TRY_CONVERT(varchar, a.Consecutivo), 0)              AS Consecutivo,
           ISNULL(c.Nombrecliente, '')                             AS NombreCliente,
           ISNULL(a.Ruta, '')                                      AS Direccion,
           ISNULL(a.Vendedor, '')                                  AS Vendedor,
           ISNULL(d.ProductoCodigo, '')                            AS ProductoCodigo,
           ISNULL(e.ProductoNombre, '')                            AS ProductoNombre,
           ISNULL(TRY_CONVERT(decimal(18,3), d.KilosCaja), 0.0)    AS KilosCaja,
           ISNULL(TRY_CONVERT(int, d.Cajas), 0)                    AS CajasTotalesSku
    FROM OrdenVenta a
    INNER JOIN PedidoVenta b         ON a.Consecutivo = b.OrdenVentaConsecutivo
    INNER JOIN ClienteSap c          ON a.Cliente     = c.Cliente
    INNER JOIN PedidoVentaProducto d ON b.Id          = d.PedidoVentaId
    INNER JOIN ArticuloSap e         ON d.ProductoCodigo = e.ProductoCodigo
    WHERE a.Estatus = @estatus
      AND (@vendedor IS NULL OR LTRIM(RTRIM(a.Vendedor)) = LTRIM(RTRIM(@vendedor)))
      AND (@cliente  IS NULL OR c.Nombrecliente LIKE LTRIM(RTRIM(@cliente)) + '%')
      AND (@consec   IS NULL OR a.Consecutivo = @consec)
),
Nums AS ( -- 1..10000
    SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS n
    FROM sys.all_objects
),
Explode AS (
    SELECT
        b.*,
        CEILING(b.CajasTotalesSku * 1.0 / @cajasPorPallet) AS PalletsNecesarios
    FROM Base b
),
Pallets AS (
    SELECT
        e.Consecutivo,
        e.NombreCliente,
        e.Direccion,
        e.Vendedor,
        e.ProductoCodigo,
        e.ProductoNombre,
        e.KilosCaja,
        e.CajasTotalesSku,
        n.n AS PalletNo,
        CAST(
            CASE 
              WHEN n.n < CEILING(e.CajasTotalesSku * 1.0 / @cajasPorPallet)
                   THEN @cajasPorPallet
              ELSE e.CajasTotalesSku - (@cajasPorPallet * (CEILING(e.CajasTotalesSku * 1.0 / @cajasPorPallet) - 1))
            END
        AS int) AS CajasEnPallet
    FROM Explode e
    JOIN Nums n ON n.n <= CEILING(e.CajasTotalesSku * 1.0 / @cajasPorPallet)
),
Ordenado AS (
    SELECT
        p.*,
        UPPER(LTRIM(RTRIM(p.Direccion))) + '|' + UPPER(LTRIM(RTRIM(p.NombreCliente))) AS OrdenEntregaKey
    FROM Pallets p
)
SELECT
    Consecutivo,
    NombreCliente,
    Direccion,
    Vendedor,
    ProductoCodigo,
    ProductoNombre,
    KilosCaja,
    CajasTotalesSku,
    PalletNo,
    CajasEnPallet,
    CAST(COALESCE(CajasEnPallet,0) * COALESCE(KilosCaja,0) AS decimal(18,3)) AS KilosEnPallet,
    CAST(DENSE_RANK() OVER (ORDER BY OrdenEntregaKey) AS int) AS OrdenEntrega,
    OrdenEntregaKey
FROM Ordenado
WHERE COALESCE(CajasEnPallet,0) > 0
ORDER BY OrdenEntrega, Consecutivo, NombreCliente, ProductoCodigo, PalletNo
OPTION (RECOMPILE);";

                var data = await _context.Set<PalletParaMapaDto>()
                    .FromSqlRaw(sql,
                        new SqlParameter("@top", top),
                        new SqlParameter("@estatus", estatus),
                        new SqlParameter("@vendedor", (object?)vendedor ?? DBNull.Value),
                        new SqlParameter("@cliente", (object?)cliente ?? DBNull.Value),
                        new SqlParameter("@consec", (object?)consecutivo ?? DBNull.Value),
                        new SqlParameter("@cajasPorPallet", cajasPorPallet))
                    .AsNoTracking()
                    .ToListAsync();

                if (data.Count == 0)
                    return NotFound(new { mensaje = "No hay pedidos para los filtros especificados." });

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Error al consultar base de datos.",
                    detalle = ex.Message,
                    stack = ex.StackTrace
                });
            }
        }


        //=======================================================
        //  VISTA PARA SUBPEDIDOS (robusta con Id/DocSAP)
        //=======================================================
        [HttpGet("Comercial/SubpedidosPreview")]
        public async Task<IActionResult> SubpedidosPreview(int ordenId)
        {
            // Normalizadores
            static string KeyAlmFromString(string? s)
                => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim().ToUpper();
            static string KeyAlmFromInt(int? n)
                => n.HasValue ? n.Value.ToString().Trim().ToUpper() : "—";

            // 1) Encabezado OV
            var head = await _context.OrdenVenta
                .AsNoTracking()
                .Where(o => o.Id == ordenId)
                .Select(o => new { o.Id, o.Consecutivo })
                .FirstOrDefaultAsync();

            if (head is null) return NotFound("OV no encontrada.");

            // 2) Última gestión con productos
            var pv = await _context.PedidoVenta
                .AsNoTracking()
                .Include(x => x.Productos)
                .Where(x => x.OrdenVentaId == ordenId)
                .OrderByDescending(x => x.FechaGestion)
                .FirstOrDefaultAsync();

            if (pv is null) return BadRequest("La OV no tiene gestión previa.");

            // 3) Subpedidos ya creados (si los hay)
            //    OJO: si s.Almacen es int en tu modelo, cámbialo aquí a 'almKey = KeyAlmFromInt(s.Almacen)'
            var subsRaw = await _context.Subpedidos
                .AsNoTracking()
                .Where(s => s.OrdenVentaId == ordenId)
                .Select(s => new
                {
                    subpedidoId = s.Id,
                    subFolio = s.SubFolio,
                    almKey = s.Almacen is string
                             ? (s.Almacen)                           // string
                             : s.Almacen ?? "", // int?
                    totalPeso = s.TotalPeso,
                    totalImporte = s.TotalImporte,
                    documentoSAP = s.DocumentoSAP,
                    documentoMeat = s.U_DocMeat
                })
                .ToListAsync();

            // normaliza clave
            var subsByAlm = subsRaw
                .GroupBy(s => KeyAlmFromString(s.almKey))
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.subpedidoId).First());

            // 4) Materializa líneas “sanitizadas” (evita nulls y tipos mixtos)
            var lines = (pv.Productos ?? new List<PedidoVentaProducto>())
                .Select(l => new
                {
                    AlmKey = KeyAlmFromString(l.Almacen),
                    KgCaja = l.KilosCaja,
                    Precio = l.Precio,
                    Cajas = l.Cajas,
                    l.ProductoCodigo,
                    l.ProductoNombre
                })
                // si manejas bandera de eliminado en las líneas, filtra aquí:
                // .Where(x => !x.Eliminado)
                .ToList();

            // Si no hay líneas, respondemos vacío de una vez
            if (lines.Count == 0)
                return Ok(new { ordenVentaId = head.Id, consecutivo = head.Consecutivo, grupos = Array.Empty<object>() });

            // 5) Agrupar por almacén (ya limpio) y calcular totales
            var gruposBase = lines
                .GroupBy(x => string.IsNullOrWhiteSpace(x.AlmKey) ? "—" : x.AlmKey)
                .OrderBy(g => g.Key)
                .Select((g, idx) => new
                {
                    subNum = idx + 1,
                    almacen = g.Key,
                    totalKg = g.Sum(x => x.KgCaja),                        // kg/caja * cajas
                    importe = g.Sum(x => x.Precio * x.KgCaja),          // precio(kg) * kg
                    items = g.Select(x => new
                    {
                        x.ProductoCodigo,
                        x.ProductoNombre,
                        KilosCaja = x.KgCaja,
                        Precio = x.Precio,
                        Cajas = x.Cajas,
                        almacen = x.AlmKey
                    }).ToList()
                })
                .ToList();

            // 6) Proyección final + merge con subpedido existente por almacén
            var preview = gruposBase.Select(g =>
            {
                subsByAlm.TryGetValue(g.almacen, out var sub); // puede ser null
                var subFolioCalc = $"{head.Consecutivo}-{(g.subNum <= 99 ? g.subNum.ToString("00") : g.subNum.ToString())}";
                return new
                {
                    subpedidoId = sub?.subpedidoId,                 // numérico o null
                    subFolio = sub?.subFolio ?? subFolioCalc,
                    almacen = g.almacen,
                    totalKg = g.totalKg,
                    importe = g.importe,
                    items = g.items,
                    documentoSAP = sub?.documentoSAP,
                    documentoMeat = sub?.documentoMeat
                };
            });

            return Ok(new
            {
                ordenVentaId = head.Id,
                consecutivo = head.Consecutivo,
                grupos = preview
            });
        }




        //=======================================================
        //  GENERAR SUBPEDIDOS PARA SEPARARLOS POR ALMACEN
        //=======================================================

        [HttpPost("Comercial/GenerarSubpedidosPorAlmacen")]
        public async Task<IActionResult> GenerarSubpedidosPorAlmacen([FromBody] GenerarSubpedidosRequest req)
        {
            if (req is null || req.OrdenId <= 0)
                return BadRequest("Solicitud inválida.");

            static decimal ToDec(decimal? v) => v ?? 0m;
            static int ToInt(int? v) => v ?? 0;
            static string Clean(string? s) => (s ?? "").Trim();
            static string NAlm(string? s) => Clean(s).ToUpperInvariant();
            static string TrimMax(string? s, int max) { var t = Clean(s); return t.Length > max ? t[..max] : t; }

            var orden = await _context.OrdenVenta
                .AsNoTracking()
                .Where(o => o.Id == req.OrdenId)
                .Select(o => new { o.Id, o.Consecutivo, o.FechaEntrega, o.Cliente, o.Vendedor })
                .FirstOrDefaultAsync();

            if (orden is null)
                return NotFound("OV no encontrada.");

            // ¿Ya hay subpedidos para esta OV?
            var yaHay = await _context.Subpedidos
                .AsNoTracking()
                .AnyAsync(s => s.OrdenVentaId == req.OrdenId);

            if (yaHay && !req.ForzarRegeneracion)
                return StatusCode(StatusCodes.Status409Conflict, "Ya existen subpedidos para esta OV.");

            if (yaHay && req.ForzarRegeneracion)
            {
                var existentes = await _context.Subpedidos
                    .Include(s => s.Productos)
                    .Where(s => s.OrdenVentaId == req.OrdenId)
                    .ToListAsync();

                _context.SubpedidoProductos.RemoveRange(existentes.SelectMany(s => s.Productos));
                _context.Subpedidos.RemoveRange(existentes);
                await _context.SaveChangesAsync();
            }

            // Base para agrupar (no modificamos PedidoVenta)
            var pvBase = await _context.PedidoVenta
                .AsNoTracking()
                .Include(p => p.Productos)
                .Where(p => p.OrdenVentaId == req.OrdenId)
                .OrderByDescending(p => p.FechaGestion)
                .FirstOrDefaultAsync();

            if (pvBase is null)
                return BadRequest("Sin gestión previa para dividir.");

            var grupos = pvBase.Productos
                .GroupBy(l => string.IsNullOrWhiteSpace(NAlm(l.Almacen)) ? "—" : NAlm(l.Almacen))
                .OrderBy(g => g.Key)
                .ToList();

            var creados = new List<object>();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // ✅ SIN usar '??' entre DateTime (no-nullable):
                // Si fueran DateTime?, usa las líneas comentadas como alternativa.
                var fechaEntregaSegura =
                    (orden.FechaEntrega == default) ? DateTime.Today : orden.FechaEntrega;
                // Si orden.FechaEntrega fuera DateTime?: 
                // var fechaEntregaSegura = orden.FechaEntrega.HasValue ? orden.FechaEntrega.Value : DateTime.Today;

                var fechaEmbarqueSegura =
                    (pvBase.FechaEmbarque == default) ? fechaEntregaSegura : pvBase.FechaEmbarque;
                // Si pvBase.FechaEmbarque fuera DateTime?:
                // var fechaEmbarqueSegura = pvBase.FechaEmbarque.HasValue ? pvBase.FechaEmbarque.Value : fechaEntregaSegura;

                int idx = 0;
                foreach (var g in grupos)
                {
                    idx++;
                    var subFolio = $"{orden.Consecutivo}-{(idx <= 99 ? idx.ToString("00") : idx.ToString())}";

                    // ✅ KilosCaja = KG TOTALES (no volver a multiplicar por Cajas)
                    var totalKg = g.Sum(x => ToDec(x.KilosCaja));
                    var totalIm = g.Sum(x => ToDec(x.Precio) * ToDec(x.KilosCaja));

                    var s = new Subpedido
                    {
                        OrdenVentaId = orden.Id,
                        ConsecutivoOV = TrimMax(orden.Consecutivo, 50),
                        SubFolio = TrimMax(subFolio, 70),
                        Almacen = TrimMax(g.Key, 50),

                        TotalPeso = totalKg,
                        TotalImporte = totalIm,

                        FechaCreacion = DateTime.Now,
                        FechaEntrega = fechaEntregaSegura,
                        FechaEmbarque = (DateTime)fechaEmbarqueSegura,

                        Cliente = TrimMax(orden.Cliente, 200),
                        Vendedor = TrimMax(orden.Vendedor, 200)
                    };

                    _context.Subpedidos.Add(s);
                    await _context.SaveChangesAsync(); // se necesita s.Id

                    var items = g.Select(x => new SubpedidoProducto
                    {
                        SubpedidoId = s.Id,
                        ProductoCodigo = TrimMax(x.ProductoCodigo, 50),
                        ProductoNombre = TrimMax(x.ProductoNombre, 250),
                        KilosCaja = ToDec(x.KilosCaja),
                        Precio = ToDec(x.Precio),
                        Cajas = ToInt(x.Cajas),
                        Almacen = TrimMax(NAlm(x.Almacen), 50)
                    }).ToList();

                    _context.SubpedidoProductos.AddRange(items);
                    await _context.SaveChangesAsync();

                    creados.Add(new
                    {
                        Id = s.Id,
                        SubFolio = s.SubFolio,
                        AlmacenSurtir = s.Almacen,
                        TotalPeso = s.TotalPeso,
                        TotalImporte = s.TotalImporte
                    });
                }

                await tx.CommitAsync();
                return Ok(new { ok = true, subpedidos = creados });
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, "Error al guardar: " + (ex.GetBaseException()?.Message ?? ex.Message));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, "Error inesperado: " + ex.Message);
            }
        }


        //=======================================================
        //  SUBPEDIDOS POR ORDENES DE VENTA
        //=======================================================

        [HttpGet("Comercial/SubpedidosPorOV")]
        public async Task<IActionResult> SubpedidosPorOV(int ordenId)
        {
            var data = await _context.Subpedidos
                .AsNoTracking()
                .Where(s => s.OrdenVentaId == ordenId)
                .OrderBy(s => s.SubFolio)
                .Select(s => new
                {
                    s.Id,
                    s.SubFolio,
                    s.Almacen,
                    s.TotalPeso,
                    s.TotalImporte,
                    Productos = s.Productos.Select(p => new
                    {
                        p.ProductoCodigo,
                        p.ProductoNombre,
                        p.KilosCaja,
                        p.Precio,
                        p.Cajas,
                        p.Almacen
                    })
                })
                .ToListAsync();

            return Ok(data);
        }

        // GET /Comercial/OrdenesPorVendedor
        [HttpGet("Comercial/OrdenesPorVendedor")]
        public async Task<IActionResult> OrdenesPorVendedor([FromQuery] OrdenesOVFiltro? filtro, CancellationToken ct)
        {
            filtro ??= new OrdenesOVFiltro();
            //filtro.Desde ??= DateTime.Today.AddMonths(-6);
            filtro.Desde ??= DateTime.Today.AddDays(-1);
            filtro.Hasta ??= DateTime.Today;

            // Llenado combo de vendedores
            filtro.Vendedores = await _context.VOrdenesVentaPorVendedor
                .AsNoTracking()
                .Where(x => x.Vendedor != null && x.Vendedor != "")
                .Select(x => x.Vendedor!)
                .Distinct()
                .OrderBy(v => v)
                .ToListAsync();

            var query = await BaseQueryVista(filtro, ct);

            // ==========================================
            //  FILTROS EXTRA PARA MULTI-SELECT
            // ==========================================

            // --- Vendedores múltiples ---
            //if (filtro.VendedoresSeleccionados != null && filtro.VendedoresSeleccionados.Any())
            //{
            //    query = query.Where(x => filtro.VendedoresSeleccionados.Contains(x.Vendedor));
            //}

            // --- Estatus múltiples ---
            if (filtro.EstatusSeleccionados != null && filtro.EstatusSeleccionados.Any())
            {
                // Si usas el "Pendiente" (1) que agrupa 1 y 3 en BD:
                var estatusFiltro = new List<int>();

                foreach (var e in filtro.EstatusSeleccionados)
                {
                    if (e == 1)
                    {
                        // opción "Pendiente" del combo => incluye 1 y 3 en la consulta
                        estatusFiltro.Add(1);
                        estatusFiltro.Add(3);
                    }
                    else
                    {
                        estatusFiltro.Add(e);
                    }
                }

                estatusFiltro = estatusFiltro.Distinct().ToList();

                query = query.Where(x => estatusFiltro.Contains(x.Estatus));
            }

            // ==========================================
            //  EXPORTS / RESULTADOS
            // ==========================================

            if (string.Equals(filtro.Export, "xlsx", StringComparison.OrdinalIgnoreCase))
                return ExportExcel(await query.OrderByDescending(x => x.FechaRegistro).ToListAsync());

            if (string.Equals(filtro.Export, "csv", StringComparison.OrdinalIgnoreCase))
                return ExportCsv(await query.OrderByDescending(x => x.FechaRegistro).ToListAsync());

            filtro.Resultados = await query
                .OrderByDescending(x => x.FechaRegistro)
                .Take(1000)
                .ToListAsync();

            return View(filtro);
        }




        //=======================================================
        //   VISTA PARA ORDENES POR VENDEDOR
        //=======================================================

        // GET /Comercial/OrdenesPorVendedorApiVista?desde=...&hasta=...&...
        [HttpGet("Comercial/OrdenesPorVendedorApiVista")]
        public async Task<IActionResult> OrdenesPorVendedorApiVista(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] string? vendedor,
            [FromQuery] int? estatus,
            [FromQuery] string? buscar)
        {
            var f = new OrdenesOVFiltro
            {
                Desde = desde,
                Hasta = hasta,
                Vendedor = vendedor,
                Estatus = estatus,
                Buscar = buscar
            };

            var query = await BaseQueryVista(f, HttpContext.RequestAborted);

            var data = await query
                .OrderByDescending(x => x.FechaRegistro)
                .ToListAsync();

            return Ok(data);
        }

        private static (string raw, string username, string usernameEmail) NormalizeLogin(string? identityName)
        {
            var raw = (identityName ?? string.Empty).Trim();
            var username = raw.Contains('\\') ? raw.Split('\\').Last() : raw;
            var usernameEmail = username.Contains('@') ? username : $"{username}@carnesg.net";
            return (raw, username, usernameEmail);
        }

        //=======================================================
        //   VENDEDORES ACTUALES EN EL LOGIN (a prueba de AD/Local)
        //=======================================================

        private int? GetIdVendedorActual()
        {
            // 1) Claim directo
            var claim = User?.Claims?.FirstOrDefault(c => c.Type == "IdVendedor" || c.Type == "VendedorId")?.Value;
            if (!string.IsNullOrWhiteSpace(claim) && int.TryParse(claim, out var idClaim) && idClaim > 0)
                return idClaim;

            // 2) Normalizar login
            var (raw, username, usernameEmail) = NormalizeLogin(User?.Identity?.Name);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // 3) Intentar en AD (si el contexto y el DbSet existen)
            try
            {
                if (_uDb != null && _uDb.UsuariosAD != null)
                {
                    var idFromAD = _uDb.UsuariosAD
                        .AsNoTracking()
                        .Where(u => u.UsuarioAd == raw || u.UsuarioAd == username || u.UsuarioAd == usernameEmail)
                        .Select(u => (int?)u.VendedorId)
                        .FirstOrDefault();

                    if (idFromAD.HasValue && idFromAD.Value > 0)
                        return idFromAD.Value;
                }
                // else: no hay contexto o DbSet, brincamos a local
            }
            catch (Exception /*ex*/)
            {
                // _logger?.LogWarning(ex, "GetIdVendedorActual: consulta a UsuariosAD falló.");
                // continuar con el lookup local
            }

            // 4) Intentar en tabla local (si existe)
            try
            {
                if (_uDb != null && _uDb.Usuarios != null)
                {
                    var idFromLocal = _uDb.Usuarios
                        .AsNoTracking()
                        .Where(u => u.Usuario == raw || u.Usuario == username || u.Usuario == usernameEmail)
                        .Select(u => (int?)u.VendedorId)
                        .FirstOrDefault();

                    if (idFromLocal.HasValue && idFromLocal.Value > 0)
                        return idFromLocal.Value;
                }
            }
            catch (Exception /*ex*/)
            {
                // _logger?.LogWarning(ex, "GetIdVendedorActual: consulta a Usuarios (local) falló.");
            }

            // 5) Sin VendedorId (no romper)
            return null;
        }


        //=======================================================
        // BASE QUERY VISTA (MULTI-VENDEDOR + AD/Local)
        //=======================================================
        private async Task<IQueryable<OrdenVentaRow>> BaseQueryVista(OrdenesOVFiltro f, CancellationToken ct = default)
        {
            var q = _context.VOrdenesVentaPorVendedor
                .AsNoTracking()
                .AsQueryable();

            // ===================================================
            // FILTRO POR SERIES CONFIGURADAS AL USUARIO
            // Regla:
            // - Si el usuario tiene series configuradas: solo ve esas series.
            // - Si no tiene ninguna serie configurada: ve todo.
            // ===================================================
            var idsSeriesPermitidas = await ObtenerSeriesIdsUsuarioActualAsync(ct);

            if (idsSeriesPermitidas.Any())
            {
                var nombresSeriesPermitidas = await _context.Series
                    .AsNoTracking()
                    .Where(s => idsSeriesPermitidas.Contains(s.Id))
                    .Select(s => s.NombreSerie)
                    .ToListAsync(ct);

                q = q.Where(x => nombresSeriesPermitidas.Contains(x.Serie));
            }

            // ===================================================
            // FILTROS DE PANTALLA
            // ===================================================

            if (f.Desde.HasValue)
            {
                var desde = f.Desde.Value.Date;
                q = q.Where(x =>
                    x.FechaEntrega.HasValue &&
                    x.FechaEntrega.Value >= desde);
            }

            if (f.Hasta.HasValue)
            {
                var hastaExcl = f.Hasta.Value.Date.AddDays(1);
                q = q.Where(x =>
                    x.FechaEntrega.HasValue &&
                    x.FechaEntrega.Value < hastaExcl);
            }

            if (f.Estatus.HasValue)
            {
                if (f.Estatus.Value == 1)
                    q = q.Where(x => x.Estatus == 1 || x.Estatus == 3);
                else
                    q = q.Where(x => x.Estatus == f.Estatus.Value);
            }

            if (!string.IsNullOrWhiteSpace(f.Buscar))
            {
                var term = f.Buscar!;
                q = q.Where(x =>
                    (x.Consecutivo ?? "").Contains(term) ||
                    (x.Cliente ?? "").Contains(term) ||
                    (x.ClienteNombre ?? "").Contains(term) ||
                    (x.Serie ?? "").Contains(term) ||
                    (x.Vendedor ?? "").Contains(term));
            }

            return q.Select(x => new OrdenVentaRow
            {
                Id = x.Id,
                Consecutivo = x.Consecutivo ?? "",
                Serie = x.Serie ?? "",
                FechaRegistro = x.FechaRegistro,
                FechaEntrega = x.FechaEntrega,
                Cliente = x.Cliente ?? "",
                ClienteNombre = x.ClienteNombre ?? "",
                Vendedor = x.Vendedor ?? "",
                VendedorId = x.VendedorId,
                Estatus = x.Estatus,
                KgTotales = x.KgTotales,
                Importe = x.Importe,
                AutorizacionPendiente = x.AutorizacionPendiente
            });
        }


        //=======================================================
        //   EXPORTAR EXCEL (Agrega hoja de DETALLE)
        //=======================================================
        private FileResult ExportExcel(List<OrdenVentaRow> data)
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();

            // -----------------------
            // Hoja 1: Encabezados
            // -----------------------
            var ws = wb.Worksheets.Add("OV por Vendedor");

            ws.Cell(1, 1).Value = "Consecutivo";
            ws.Cell(1, 2).Value = "Fecha Registro";
            ws.Cell(1, 3).Value = "Fecha Embarque";
            ws.Cell(1, 4).Value = "Cliente";
            ws.Cell(1, 5).Value = "Nombre Cliente";
            ws.Cell(1, 6).Value = "Vendedor";
            ws.Cell(1, 7).Value = "Estatus";
            ws.Cell(1, 8).Value = "Kg Totales";
            ws.Cell(1, 9).Value = "Importe";
            ws.Cell(1, 10).Value = "Autorización Pendiente";

            int r = 2;
            foreach (var x in data)
            {
                ws.Cell(r, 1).Value = x.Consecutivo;
                ws.Cell(r, 2).Value = x.FechaRegistro;
                ws.Cell(r, 2).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
                ws.Cell(r, 3).Value = x.FechaEntrega;
                ws.Cell(r, 3).Style.DateFormat.Format = "yyyy-MM-dd";
                ws.Cell(r, 4).Value = x.Cliente;
                ws.Cell(r, 5).Value = x.ClienteNombre;
                ws.Cell(r, 6).Value = x.Vendedor;
                ws.Cell(r, 7).Value = x.Estatus; // si prefieres texto, mapea aquí
                ws.Cell(r, 8).Value = x.KgTotales;
                ws.Cell(r, 8).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(r, 9).Value = x.Importe;
                ws.Cell(r, 9).Style.NumberFormat.Format = "$ #,##0.00";
                ws.Cell(r, 10).Value = x.AutorizacionPendiente ?? "";
                r++;
            }
            ws.Columns().AdjustToContents();

            // -----------------------
            // Hoja 2: Detalle por línea
            // -----------------------
            var ids = data.Select(d => d.Id).ToList(); // Asegúrate de que OrdenVentaRow tenga Id (del Pedido)
            var consPorId = data.ToDictionary(d => d.Id, d => d.Consecutivo ?? "");

            var lineas = _context.OrdenVentaProducto
                .AsNoTracking()
                .Where(l => ids.Contains(l.PedidoId))
                .Select(l => new
                {
                    l.PedidoId,
                    l.ProductoCodigo,
                    l.ProductoNombre,
                    l.Peso,     // decimal?
                    l.Cajas,    // int? o decimal?
                    l.Precio    // decimal?
                })
                .OrderBy(l => l.PedidoId)
                .ThenBy(l => l.ProductoCodigo)
                .ToList();

            var wsDet = wb.Worksheets.Add("Detalle OV");
            wsDet.Cell(1, 1).Value = "Consecutivo";
            wsDet.Cell(1, 2).Value = "PedidoId";
            wsDet.Cell(1, 3).Value = "Artículo";
            wsDet.Cell(1, 4).Value = "Descripción";
            wsDet.Cell(1, 5).Value = "Peso";
            wsDet.Cell(1, 6).Value = "Cajas";
            wsDet.Cell(1, 7).Value = "Kg";
            wsDet.Cell(1, 8).Value = "Precio";
            wsDet.Cell(1, 9).Value = "Importe";

            int dr = 2;
            foreach (var l in lineas)
            {
                // Campos base (no-nullables)
                decimal peso = l.Peso;           // decimal
                int cajasRaw = l.Cajas;          // int
                decimal precio = l.Precio;         // decimal

                // Si Cajas viene en 0, cuenta como 1
                int cajasUsadas = (cajasRaw == 0) ? 1 : cajasRaw;

                decimal kg = peso * cajasUsadas;
                decimal importe = precio * kg;

                wsDet.Cell(dr, 1).Value = consPorId.TryGetValue(l.PedidoId, out var cons) ? cons : "";
                wsDet.Cell(dr, 2).Value = l.PedidoId;
                wsDet.Cell(dr, 3).Value = l.ProductoCodigo ?? "";
                wsDet.Cell(dr, 4).Value = l.ProductoNombre ?? "";
                wsDet.Cell(dr, 5).Value = peso;
                wsDet.Cell(dr, 6).Value = cajasUsadas;
                wsDet.Cell(dr, 7).Value = kg;
                wsDet.Cell(dr, 8).Value = precio;
                wsDet.Cell(dr, 9).Value = importe;

                wsDet.Cell(dr, 5).Style.NumberFormat.Format = "#,##0.00";
                wsDet.Cell(dr, 6).Style.NumberFormat.Format = "#,##0";
                wsDet.Cell(dr, 7).Style.NumberFormat.Format = "#,##0.00";
                wsDet.Cell(dr, 8).Style.NumberFormat.Format = "$ #,##0.00";
                wsDet.Cell(dr, 9).Style.NumberFormat.Format = "$ #,##0.00";
                dr++;
            }
            wsDet.Columns().AdjustToContents();

            // -----------------------
            // Devolver archivo
            // -----------------------
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"OV_PorVendedor_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }


        //=======================================================
        //   EXPORTAR CSV (Detalle por línea, usando consulta agrupada)
        //=======================================================
        private FileResult ExportCsv(List<OrdenVentaRow> data)
        {
            var ids = data.Select(d => d.Id).ToList();

            var query =
                from a in _context.OrdenVenta.AsNoTracking()
                join b in _context.OrdenVentaProducto.AsNoTracking()
                    on a.Id equals b.PedidoId
                join c in _context.ClienteSap.AsNoTracking()
                    on a.Cliente equals c.Cliente
                join d in _context.ArticuloSap.AsNoTracking()
                    on b.ProductoCodigo equals d.ProductoCodigo
                where ids.Contains(a.Id) && a.Estatus != 0
                group new { a, b, c, d } by new
                {
                    a.FechaRegistro,
                    a.Consecutivo,
                    c.VendedorNombre,
                    c.Nombrecliente,
                    d.ProductoCodigo,
                    d.ProductoNombre,
                    d.U_MASTER,          // ✅ NUEVO
                    b.Precio,
                    a.FechaEntrega,
                    a.Ruta
                }
                into g
                orderby g.Key.FechaRegistro, g.Key.Consecutivo, g.Key.ProductoCodigo
                select new
                {
                    FechaDocumento = g.Key.FechaRegistro,
                    Pedido = g.Key.Consecutivo,
                    RealizadoPor = g.Key.VendedorNombre,
                    Cliente = g.Key.Nombrecliente,
                    Sku = g.Key.ProductoCodigo,
                    Producto = g.Key.ProductoNombre,
                    Master = g.Key.U_MASTER,    // ✅ NUEVO
                    CajasSolicitadas = g.Sum(x => x.b.Cajas),
                    KgSolicitados = g.Sum(x => x.b.Peso),
                    Precio = g.Key.Precio,
                    FechaEntrega = g.Key.FechaEntrega,
                    Ruta = g.Key.Ruta,
                    Comentario = g.Max(x => x.a.Observacion)
                };

            var lineas = query.ToList();

            var sb = new System.Text.StringBuilder();

            // ✅ Encabezados CSV
            sb.AppendLine("FechaDocumento,Pedido,Realizado Por,Cliente,Sku,Producto,Master,CajasSolicitadas,KgSolicitados,Precio,FechaEmbarcar,Ruta,Comentario");

            var ci = CultureInfo.InvariantCulture;

            foreach (var l in lineas)
            {
                string realizadoPor = (l.RealizadoPor ?? "").Replace("\"", "\"\"");
                string cliente = (l.Cliente ?? "").Replace("\"", "\"\"");
                string sku = (l.Sku ?? "").Replace("\"", "\"\"");
                string producto = (l.Producto ?? "").Replace("\"", "\"\"");
                string master = (l.Master ?? "").Replace("\"", "\"\"");   // ✅ NUEVO
                string ruta = (l.Ruta ?? "").Replace("\"", "\"\"");
                string comentario = (l.Comentario ?? "").Replace("\"", "\"\"");

                string fechaDoc = string.Format(ci, "{0:yyyy-MM-dd HH:mm:ss}", l.FechaDocumento);
                string fechaEnt = string.Format(ci, "{0:yyyy-MM-dd}", l.FechaEntrega);

                var linea = string.Join(",",
                    $"\"{fechaDoc}\"",
                    $"\"{l.Pedido}\"",
                    $"\"{realizadoPor}\"",
                    $"\"{cliente}\"",
                    $"\"{sku}\"",
                    $"\"{producto}\"",
                    $"\"{master}\"",                     // ✅ NUEVO
                    l.CajasSolicitadas.ToString("0", ci),
                    l.KgSolicitados.ToString("0.##", ci),
                    l.Precio.ToString("0.##", ci),
                    $"\"{fechaEnt}\"",
                    $"\"{ruta}\"",
                    $"\"{comentario}\""
                );

                sb.AppendLine(linea);
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv",
                $"OV_PorVendedor_Detalle_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }





        //=======================================================
        //   DETALLES DE LOS PEDIDOS POR VENDEDOR
        //=======================================================

        [HttpGet("Comercial/OrdenDetalleApi")]
        public async Task<IActionResult> OrdenDetalleApi(int id)
        {
            var header = await _context.VOrdenesVentaPorVendedor
                .AsNoTracking()
                .Where(x => x.Id == id)
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
                    x.KgTotales,
                    x.Importe,
                    x.AutorizacionPendiente,
                    x.Observacion          // ✅ NUEVO
                })
                .FirstOrDefaultAsync();

            if (header == null)
                return NotFound();

            // Traer 1 subpedido asociado (el más reciente, por ejemplo)
            var sub = await _context.Subpedidos
                .AsNoTracking()
                .Where(s => s.Id == id)
                .OrderByDescending(s => s.Id)
                .Select(s => new
                {
                    s.Id,
                    s.DocumentoSAP
                })
                .FirstOrDefaultAsync();

            // Líneas
            var lineas = await _context.OrdenVentaProducto
                .AsNoTracking()
                .Where(l => l.PedidoId == id)
                .Select(l => new
                {
                    l.Id,
                    l.PedidoId,
                    l.ProductoCodigo,
                    l.ProductoNombre,
                    l.Peso,
                    l.Cajas,
                    l.Precio,
                    Kg = (decimal?)l.Peso, // SOLO PESO
                    Importe = (decimal?)l.Precio * (decimal?)l.Peso
                })
                .OrderBy(l => l.Id)
                .ToListAsync();

            return Ok(new
            {
                header = new
                {
                    header.Id,
                    header.Consecutivo,
                    header.FechaRegistro,
                    header.FechaEntrega,
                    header.Cliente,
                    header.ClienteNombre,
                    header.Vendedor,
                    header.Serie,
                    header.Estatus,
                    header.KgTotales,
                    header.Importe,
                    header.AutorizacionPendiente,
                    header.Observacion,      // ✅ NUEVO
                    SubpedidoId = sub?.Id,
                    DocumentoSAP = sub?.DocumentoSAP
                },
                lineas
            });
        }











        //=======================================================
        //  Webhook opcional: SAP te empuja U_DocMeat
        //=======================================================
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [HttpPost("sap/webhook/u-docmeat")]
        public async Task<IActionResult> SapWebhookUDocMeat([FromBody] SapDocMeatPayload payload, CancellationToken ct)
        {
            const int MAX_LEN = 100;
            if (payload is null) return BadRequest("Payload vacío.");

            var valor = payload.U_DocMeat?.Trim();
            if (string.IsNullOrEmpty(valor))
                return BadRequest("U_DocMeat requerido y no vacío.");
            if (valor.Length > MAX_LEN) valor = valor[..MAX_LEN];

            Subpedido? sub = null;

            if (payload.SubpedidoId.HasValue && payload.SubpedidoId.Value > 0)
            {
                sub = await _context.Subpedidos
                    .FirstOrDefaultAsync(s => s.Id == payload.SubpedidoId.Value, ct);
            }
            else if (!string.IsNullOrWhiteSpace(payload.DocumentoSAP))
            {
                var doc = payload.DocumentoSAP.Trim();
                string? docNorm = null;
                if (long.TryParse(doc, out var n) && n >= 0) docNorm = n.ToString(); // “001234” -> “1234”

                sub = await _context.Subpedidos
                    .FirstOrDefaultAsync(s => s.DocumentoSAP == doc || (docNorm != null && s.DocumentoSAP == docNorm), ct);
            }

            if (sub is null)
                return NotFound("No se encontró el subpedido por SubpedidoId/DocumentoSAP.");

            if (string.Equals(sub.U_DocMeat, valor, StringComparison.Ordinal))
                return Ok(new { subpedidoId = sub.Id, uDocMeat = sub.U_DocMeat, msg = "Sin cambios" });

            sub.U_DocMeat = valor;
            await _context.SaveChangesAsync(ct);

            // TODO: SignalR para avisar al front
            var ok = await UpsertUDocMeatAsync(_context, sub.Id, valor, ct);
            return Ok(new { subpedidoId = sub.Id, uDocMeat = valor, persisted = ok });
        }


        //=======================================================
        //   INTENTOS PARA TRAER EL U_DOCMEAT
        //=======================================================
        private async Task<string?> TryConsultarUDocMeatConReintentosAsync(string documentoSAP, CancellationToken ct)
        {
            var esperas = new[] { 2, 5, 10 }; // segundos
            foreach (var s in esperas)
            {
                var valor = await ConsultarUDocMeatEnSapAsync(documentoSAP, "Orders", ct);
                if (!string.IsNullOrWhiteSpace(valor)) return valor!.Trim();

                try { await Task.Delay(TimeSpan.FromSeconds(s), ct); }
                catch (TaskCanceledException) { break; }
            }
            return null;
        }


        //=======================================================
        //  CONSULTA U_DocMeat en la entidad especificada
        //  (usa URLs RELATIVAS contra _httpClient.BaseAddress)
        //=======================================================
        private async Task<string?> ConsultarUDocMeatEnSapAsync(string documentoSAP, string entity, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(entity)) return null;
            var raw = (documentoSAP ?? string.Empty).Trim();
            if (raw.Length == 0) return null;

            await EnsureSapSessionAsync(ct);

            // 1) Si parece DocEntry (int) intenta por key: /{entity}({docEntry})?$select=U_DocMeat
            if (int.TryParse(raw, out var maybeDocEntry) && maybeDocEntry > 0)
            {
                var byKey = $"{entity}({maybeDocEntry})?$select=U_DocMeat"; // ← RELATIVA
                var respKey = await SlGetWithReLoginAsync(byKey, ct);

                if (respKey.IsSuccessStatusCode)
                {
                    var json = await respKey.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);

                    if (TryGetPropIgnoreCase(doc.RootElement, "U_DocMeat", out var v) && !string.IsNullOrWhiteSpace(v))
                        return v!.Trim();
                    // si venía null o vacío, caemos a DocNum
                }
                // si 404/401/etc, continuamos con DocNum
            }

            // 2) SIEMPRE intenta por DocNum (lo que te manda SAP)
            if (!int.TryParse(raw, out var docNum) || docNum <= 0) return null;

            // Igual que Postman: Orders?$select=DocEntry,DocNum,U_DocMeat&$filter=DocNum eq 10009164&$top=1
            var url = $"{entity}?$select=DocEntry,DocNum,U_DocMeat&$filter=DocNum eq {docNum}&$top=1";
            var resp = await SlGetWithReLoginAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var jsonF = await resp.Content.ReadAsStringAsync(ct);
            using var docF = JsonDocument.Parse(jsonF);

            if (docF.RootElement.TryGetProperty("value", out var arr) &&
                arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                var first = arr[0];

                // Header U_DocMeat
                if (TryGetPropIgnoreCase(first, "U_DocMeat", out var v1) && !string.IsNullOrWhiteSpace(v1))
                    return v1!.Trim();

                // Fallback: busca en líneas si el header está vacío
                if (TryGetInt(first, "DocEntry", out var de) && de > 0)
                {
                    var linesUrl = $"{entity}({de})?$select=DocEntry&$expand=DocumentLines($select=U_DocMeat,LineNum)";
                    var respLines = await SlGetWithReLoginAsync(linesUrl, ct);
                    if (respLines.IsSuccessStatusCode)
                    {
                        var jsonL = await respLines.Content.ReadAsStringAsync(ct);
                        using var docL = JsonDocument.Parse(jsonL);

                        if (docL.RootElement.TryGetProperty("DocumentLines", out var lines) &&
                            lines.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var line in lines.EnumerateArray())
                            {
                                if (TryGetPropIgnoreCase(line, "U_DocMeat", out var vl) && !string.IsNullOrWhiteSpace(vl))
                                    return vl!.Trim();
                            }
                        }
                    }
                }
            }
            return null;
        }


        //=======================================================
        //  POLL + PERSIST: U_DocMeat (hasta ~1 min)
        //=======================================================
        private async Task PollAndPersistUDocMeatAsync(int subpedidoId, string documentoSAP, CancellationToken ct)
        {
            // Normaliza: DocNum suele ser entero; quita ceros a la izquierda
            var raw = (documentoSAP ?? "").Trim();
            _logger.LogInformation("Inicio poll U_DocMeat. Sub={Id}, DocSAP={Doc}", subpedidoId, raw);

            var esperas = new[] { 2, 4, 8, 16, 32 }; // ~1 min
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            foreach (var seg in esperas)
            {
                ct.ThrowIfCancellationRequested();

                string? valor = null;
                try
                {
                    valor = await ConsultarUDocMeatEnSapAsync(raw, "Orders", ct);
                    _logger.LogInformation("Consulta U_DocMeat. Sub={Id}, Val={Val}", subpedidoId, valor);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fallo consultando U_DocMeat. Sub={Id}", subpedidoId);
                }

                if (!string.IsNullOrWhiteSpace(valor))
                {
                    var v = valor.Trim();
                    if (v.Length > 100) v = v[..100]; // AJUSTA a la longitud real de tu columna

                    try
                    {
                        var ok = await UpsertUDocMeatAsync(db, subpedidoId, v, ct);
                        if (ok) return; // listo
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Fallo persistiendo U_DocMeat. Sub={Id}", subpedidoId);
                    }
                }

                try { await Task.Delay(TimeSpan.FromSeconds(seg), ct); }
                catch (TaskCanceledException) { return; }
            }

            _logger.LogWarning("Fin poll: no se obtuvo U_DocMeat a tiempo. Sub={Id}, DocSAP={Doc}", subpedidoId, raw);
        }



        // ================= Helpers JSON =================
        private static bool TryGetPropIgnoreCase(JsonElement el, string name, out string? value)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString();
                    return true;
                }
            }
            value = null;
            return false;
        }
        private static bool TryGetInt(JsonElement el, string name, out int value)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    p.Value.ValueKind == JsonValueKind.Number &&
                    p.Value.TryGetInt32(out value))
                    return true;
            }
            value = 0;
            return false;
        }


        // ✅ ÚNICO helper GET con re-login (acepta URL relativa o absoluta)
        private async Task<HttpResponseMessage> SlGetWithReLoginAsync(string relativeOrAbsolute, CancellationToken ct)
        {
            var uri = Uri.IsWellFormedUriString(relativeOrAbsolute, UriKind.Absolute)
                ? new Uri(relativeOrAbsolute, UriKind.Absolute)
                : new Uri(_httpClient.BaseAddress!, relativeOrAbsolute);

            var resp = await _httpClient.GetAsync(uri, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await EnsureSapSessionAsync(ct);
                resp = await _httpClient.GetAsync(uri, ct);
            }
            return resp;
        }


        // ✅ Login/refresh usando IConfiguration (no SeriesSettings.GetSection)
        private async Task EnsureSapSessionAsync(CancellationToken ct)
        {
            // Si ya hay cookie, asumimos sesión viva; si SL expira, la reponemos en el GET
            if (_httpClient.DefaultRequestHeaders.Contains("Cookie")) return;

            var user = _configuration["SapServiceLayer:UserName"] ?? "";
            var pass = _configuration["SapServiceLayer:Password"] ?? "";
            var comp = _configuration["SapServiceLayer:CompanyDB"] ?? "";

            var payload = new { UserName = user, Password = pass, CompanyDB = comp };

            // Usa BaseAddress -> endpoint relativo "Login"
            var loginUri = new Uri(_httpClient.BaseAddress!, "Login");
            using var req = new HttpRequestMessage(HttpMethod.Post, loginUri)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            var resp = await _httpClient.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                var sessionCookie = cookies.FirstOrDefault(c => c.StartsWith("B1SESSION", StringComparison.OrdinalIgnoreCase));
                var routeCookie = cookies.FirstOrDefault(c => c.StartsWith("ROUTEID", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(sessionCookie))
                {
                    var cookieHeader = sessionCookie.Split(';')[0];
                    if (!string.IsNullOrEmpty(routeCookie)) cookieHeader += "; " + routeCookie.Split(';')[0];

                    _httpClient.DefaultRequestHeaders.Remove("Cookie");
                    _httpClient.DefaultRequestHeaders.Add("Cookie", cookieHeader);
                }
            }
        }

        private async Task<bool> UpsertUDocMeatAsync(AppDbContext db, int subpedidoId, string valor, CancellationToken ct)
        {
            // intenta por EF
            try
            {
                var sub = await db.Subpedidos.FirstOrDefaultAsync(s => s.Id == subpedidoId, ct);
                if (sub == null) return false;

                if (!string.Equals(sub.U_DocMeat, valor, StringComparison.Ordinal))
                {
                    sub.U_DocMeat = valor;
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("U_DocMeat guardado por EF. Sub={Id}, Val={Val}", subpedidoId, valor);
                }
                else
                {
                    _logger.LogInformation("U_DocMeat sin cambios. Sub={Id}, Val={Val}", subpedidoId, valor);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EF SaveChanges falló; intentaremos UPDATE SQL directo. Sub={Id}", subpedidoId);
            }

            // fallback SQL directo (por si hay tracking/longitudes/concurrency)
            // Ajusta el tamaño NVARCHAR si tu columna es menor.
            var filas = await db.Database.ExecuteSqlRawAsync(@"
        UPDATE Subpedido
           SET U_DocMeat = @p0
         WHERE Id = @p1
           AND (U_DocMeat IS NULL OR U_DocMeat <> @p0);
    ", parameters: new object[] { valor, subpedidoId }, cancellationToken: ct);

            _logger.LogInformation("U_DocMeat guardado por SQL. Sub={Id}, Afectadas={N}", subpedidoId, filas);
            return filas > 0;
        }



        [HttpPost("Comercial/u-docmeat")]

        public async Task<IActionResult> DebugDocMeat(int subId, int docNum, CancellationToken ct)
        {
            var valor = await ConsultarUDocMeatEnSapAsync(docNum.ToString(), "Orders", ct);
            if (string.IsNullOrWhiteSpace(valor)) return Ok(new { encontrado = false });

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ok = await UpsertUDocMeatAsync(db, subId, valor.Trim(), ct);
            return Ok(new { encontrado = true, valor, guardado = ok });
        }









        //https://localhost:7171/Comercial/SubpedidoSAPJson?id=1
        //=======================================================
        //   GENERADOR DE JSON PARA ENVIO A SAP 
        //=======================================================

        [HttpGet("Comercial/SubpedidoSAPJson")]
        public async Task<IActionResult> SubpedidoSAPJson(int id)
        {
            var json = await BuildSapOvJsonAsync(id);
            return Content(json, "application/json");
        }

        //=======================================================
        //   ENVIAR SUBPEDIDO A SAP  (marca OV=5 cuando todos enviados)
        //=======================================================
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [HttpPost("Comercial/SubpedidoEnviarASAP")]
        public async Task<IActionResult> SubpedidoEnviarASAP(int id, CancellationToken ct)
        {
            await using var trx = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                // 1) Evitar reenvío si ya tiene Doc SAP
                var yaTiene = await _context.Subpedidos
                    .AsNoTracking()
                    .Where(s => s.Id == id)
                    .Select(s => s.DocumentoSAP)
                    .FirstOrDefaultAsync(ct);

                if (!string.IsNullOrWhiteSpace(yaTiene))
                    return StatusCode(StatusCodes.Status409Conflict,
                        $"Este subpedido ya fue enviado a SAP. DocumentoSAP: {yaTiene}");

                // 2) Enviar a SAP (implementa tu lógica real aquí)
                var docSapRaw = await EnviarASapAsync(id, ct);

                // 3) Normalizar DocNum/DocEntry
                var docSap = NormalizarDocumentoSap(docSapRaw);

                const int MAX_LEN = 100;
                if (string.IsNullOrWhiteSpace(docSap))
                    return StatusCode(500, $"DocumentoSAP inválido. Valor crudo: '{Trunc(docSapRaw, 120)}'");
                if (docSap.Length > MAX_LEN)
                    return StatusCode(500, $"DocumentoSAP demasiado largo ({docSap.Length}>{MAX_LEN}). Valor: '{Trunc(docSap, 120)}'");

                // 4) Persistir DocumentoSAP
                var sub = await _context.Subpedidos.FirstOrDefaultAsync(s => s.Id == id, ct);
                if (sub is null) return NotFound("Subpedido no existe.");

                sub.DocumentoSAP = docSap;
                await _context.SaveChangesAsync(ct);

                // 5) Si todos los subpedidos ya tienen DocumentoSAP => OV.Estatus = 5
                await MarcarOVComoEnviadaSiCorresponde_Atomico(sub.OrdenVentaId, ct);

                // 6) Commit
                await trx.CommitAsync(ct);

                // 7) 🔥 BACKGROUND: buscar U_DocMeat hasta ~8 min y persistir
                _ = Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(8)); // tope total
                    try
                    {
                        await PollAndPersistUDocMeatAsync(
                            subpedidoId: id,
                            documentoSAP: docSap, // DocEntry o DocNum (ver nota abajo)
                            ct: cts.Token
                        );
                    }
                    catch
                    {
                        // TODO: log
                    }
                });

                // 8) Responder sin bloquear al usuario
                return Ok(new { subpedidoId = id, documentoSAP = docSap });
            }
            catch (DbUpdateException ex)
            {
                await trx.RollbackAsync(ct);
                var detail = ex.InnerException?.Message ?? ex.Message;
                if (detail.Contains("String or binary data would be truncated", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(500, "DB error: Valor para DocumentoSAP excede tamaño de columna. Ajusta NormalizarDocumentoSap y/o columna.");

                return StatusCode(500, $"DB error: {detail}");
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync(ct);
                return StatusCode(500, ex.Message);
            }
        }


        ////=======================================================
        /// Recibe lo que devuelva EnviarASapAsync (JSON o string) y regresa SOLO el DocNum/DocEntry como texto corto.
        //=======================================================

        private static string NormalizarDocumentoSap(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            raw = raw.Trim();

            // 1) JSON común
            if (raw.StartsWith("{") || raw.StartsWith("["))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("DocNum", out var dn) &&
                            (dn.ValueKind is JsonValueKind.Number or JsonValueKind.String))
                            return dn.ToString().Trim();

                        if (root.TryGetProperty("DocEntry", out var de) &&
                            (de.ValueKind is JsonValueKind.Number or JsonValueKind.String))
                            return de.ToString().Trim();

                        if (root.TryGetProperty("value", out var val) &&
                            val.ValueKind == JsonValueKind.Object)
                        {
                            if (val.TryGetProperty("DocNum", out var dn2))
                                return dn2.ToString().Trim();
                            if (val.TryGetProperty("DocEntry", out var de2))
                                return de2.ToString().Trim();
                        }
                    }
                }
                catch { /* no JSON válido */ }
            }

            // 2) No JSON: intenta números (DocNum) o token alfanumérico (DocEntry)
            var onlyDigits = Regex.Match(raw, @"\d+").Value;
            if (!string.IsNullOrWhiteSpace(onlyDigits))
                return onlyDigits;

            var token = Regex.Match(raw, @"[A-Za-z0-9\-_/]{1,100}").Value;
            return token?.Trim() ?? string.Empty;
        }

        private static string Trunc(string? s, int max)
            => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");


        //=======================================================
        /// Marca la OV con Estatus=5 solo si TODOS sus subpedidos ya tienen DocumentoSAP.
        /// Se hace en un solo UPDATE con NOT EXISTS para evitar condiciones de carrera.
        //=======================================================

        private async Task MarcarOVComoEnviadaSiCorresponde_Atomico(int ordenVentaId, CancellationToken ct)
        {
            // Ajusta nombres reales de tablas si difieren (p.ej. OrdenesVenta/Subpedidos)
            var sql = @$"
          UPDATE ordenventa
             SET Estatus = 5
           WHERE Id = @p0
             AND Estatus <> 5
             AND NOT EXISTS (
                  SELECT 1
                    FROM subpedido s
                   WHERE s.OrdenVentaId = @p0
                     AND (s.DocumentoSAP IS NULL OR LTRIM(RTRIM(s.DocumentoSAP)) = '')
             );";

            await _context.Database.ExecuteSqlRawAsync(sql, new object[] { ordenVentaId }, ct);
        }



        //=======================================================
        //   Login a SAP SL y devuelve el header Cookie listo
        //=======================================================
        private async Task<string> GetSapSessionCookieAsync(
            string baseUrlRoot, string companyDb, string userName, string password, CancellationToken ct = default)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                // ⚠️ Solo para pruebas
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrlRoot.TrimEnd('/') + "/") };

            var loginPayload = new { CompanyDB = companyDb, UserName = userName, Password = password };
            using var content = new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json");

            // POST https://host:port/b1s/v1/Login
            using var resp = await http.PostAsync("Login", content, ct);
            var loginBody = await resp.Content.ReadAsStringAsync(ct);
            resp.EnsureSuccessStatusCode(); // si falla, revisa loginBody para el detalle

            var cookies = handler.CookieContainer.GetCookies(http.BaseAddress);
            var b1 = cookies["B1SESSION"]?.Value;
            var route = cookies["ROUTEID"]?.Value;

            if (string.IsNullOrEmpty(b1))
                throw new InvalidOperationException("No se obtuvo cookie B1SESSION del Service Layer.");

            return $"B1SESSION={b1}" + (string.IsNullOrEmpty(route) ? "" : $"; ROUTEID={route}");
        }


        //=======================================================
        //   ENVÍO A SAP EL JSON (PEDIDOS DE VENTAS)
        //=======================================================
        public async Task<string> EnviarASapAsync(int subpedidoId, CancellationToken ct = default)
        {
            // ⛔ Doble candado: impedir reenvío si ya tiene documento
            var docExistente = await _context.Subpedidos
                .AsNoTracking()
                .Where(s => s.Id == subpedidoId)
                .Select(s => s.DocumentoSAP)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(docExistente))
                throw new InvalidOperationException($"Subpedido {subpedidoId} ya tiene DocumentoSAP: {docExistente}. No se puede reenviar.");

            var json = await BuildSapOvJsonAsync(subpedidoId);

            string baseUrlRoot = "https://172.120.80.3:50000/b1s/v1";
            string company = "PROD_CARNESG";
            string user = "manager";
            string pass = "Sap.2023";

            var cookie = await GetSapSessionCookieAsync(baseUrlRoot, company, user, pass, ct);

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
            var url = $"{baseUrlRoot.TrimEnd('/')}/Orders";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("Cookie", cookie);
            req.Headers.Accept.ParseAdd("application/json");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"SL Error {(int)resp.StatusCode}: {body}");

            // ✅ Parsear y guardar el folio/Doc de SAP
            var sap = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
            var docNum = sap != null && sap.TryGetValue("DocNum", out var v1) ? v1?.ToString() : null;
            var docEntry = sap != null && sap.TryGetValue("DocEntry", out var v2) ? v2?.ToString() : null;

            var sub = await _context.Subpedidos.FirstOrDefaultAsync(s => s.Id == subpedidoId, ct);
            if (sub != null)
            {
                // ⛔ Verificación de carrera (por si 2 hilos llegaron a la vez)
                if (!string.IsNullOrWhiteSpace(sub.DocumentoSAP))
                    throw new InvalidOperationException($"Subpedido {subpedidoId} ya tenía DocumentoSAP al guardar: {sub.DocumentoSAP}.");

                sub.DocumentoSAP = docNum ?? docEntry;
                await _context.SaveChangesAsync(ct);
            }

            // (Opcional) Logout
            try
            {
                using var logout = new HttpRequestMessage(HttpMethod.Post, $"{baseUrlRoot.TrimEnd('/')}/Logout");
                logout.Headers.Add("Cookie", cookie);
                await http.SendAsync(logout, ct);
            }
            catch { }

            return body;
        }

        private static string LimpiarTextoSap(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return string.Empty;

            return System.Text.RegularExpressions.Regex
                .Replace(texto.Trim(), @"\s+", " ");
        }

        private static string CortarTextoSap(string texto, int max = 254)
        {
            texto = LimpiarTextoSap(texto);

            if (texto.Length <= max)
                return texto;

            return texto.Substring(0, max - 3) + "...";
        }


        //aqui construimos el json para las ordenes de venta
        private static string ConstruirComentarioSap(
     string? consecutivoOv,
     string? subFolio,
     DateTime fechaEmbarque,
     DateTime fechaEntrega,
     string? observacionOv,
     string? comentarioExtra)
        {
            var observacion = LimpiarTextoSap(observacionOv);

            // Si no hay observación en la OV, usamos comentarioExtra si viene algo
            if (string.IsNullOrWhiteSpace(observacion) && !string.IsNullOrWhiteSpace(comentarioExtra))
                observacion = LimpiarTextoSap(comentarioExtra);

            if (string.IsNullOrWhiteSpace(observacion))
                observacion = "N/A";

            // Limpia comas o separadores sobrantes al final
            observacion = observacion.Trim().Trim(',', '|', '-', ';', ':').Trim();

            var comentarioSap = $" {observacion}";

            return CortarTextoSap(comentarioSap, 254);
        }


        //=======================================================
        //   CREACION DEL JSON
        //=======================================================
        public async Task<string> BuildSapOvJsonAsync(int subpedidoId, string? comentarioExtra = null)
        {
            // 1) Header con campos necesarios
            var sp = await _context.Subpedidos
                .AsNoTracking()
                .Where(s => s.Id == subpedidoId)
                .Select(s => new
                {
                    s.Id,
                    s.Cliente,
                    s.FechaEmbarque,
                    s.FechaEntrega,
                    s.ConsecutivoOV,
                    s.SubFolio
                })
                .FirstOrDefaultAsync();

            if (sp == null)
                throw new InvalidOperationException($"Subpedido {subpedidoId} no existe.");

            // 2) Traer información de la OV + SerieId SAP
            var ovInfo = await _context.OrdenVenta
                .AsNoTracking()
                .Where(o => o.Consecutivo == sp.ConsecutivoOV)
                .Join(
                    _context.Series,
                    ov => ov.Serie,
                    serie => serie.NombreSerie,
                    (ov, serie) => new
                    {
                        SerieId = serie.SerieId,

                        // Si tu campo se llama diferente, cambia aquí:
                        // ov.Observaciones
                        // ov.Comentario
                        // ov.Comentarios
                        Observacion = ov.Observacion
                    }
                )
                .FirstOrDefaultAsync();

            if (ovInfo == null || ovInfo.SerieId <= 0)
            {
                throw new InvalidOperationException(
                    $"No se encontró SerieId para la OV con Consecutivo {sp.ConsecutivoOV}. " +
                    "Verifica que 'series.NombreSerie' coincida con 'OrdenVenta.Serie'."
                );
            }

            var serieId = ovInfo.SerieId;

            // 3) SalesPersonCode desde ClienteSap
            var spCode = await _context.ClienteSap
                .AsNoTracking()
                .Where(c => c.Cliente == sp.Cliente)
                .Select(c => c.VendedorId)
                .FirstOrDefaultAsync();

            if (spCode <= 0)
                throw new InvalidOperationException($"Cliente {sp.Cliente} no tiene VendedorId (SalesPersonCode) válido.");

            // 4) Líneas del documento
            var lineas = await _context.SubpedidoProductos
                .AsNoTracking()
                .Where(p => p.SubpedidoId == subpedidoId)
                .OrderBy(p => p.Id)
                .Select(p => new
                {
                    ItemCode = p.ProductoCodigo,

                    // Mantengo tu lógica actual:
                    // Si para SAP Quantity debe ser cajas, cambia esto por: Quantity = (double)p.Cajas
                    Quantity = (double)p.KilosCaja,

                    U_Cajas = (int)p.Cajas,
                    UnitPrice = (decimal)p.Precio,
                    WarehouseCode = p.Almacen.ToString()
                })
                .ToListAsync();

            if (lineas.Count == 0)
                throw new InvalidOperationException("El subpedido no tiene líneas.");

            // 5) Fechas formateadas
            var docDate = sp.FechaEmbarque.Date;

            // Como FechaEntrega puede ser nullable, usamos FechaEmbarque como respaldo
            var dueDate = sp.FechaEntrega.Date;

            // 6) Comentario limpio para SAP
            var comentarioSap = ConstruirComentarioSap(
                consecutivoOv: sp.ConsecutivoOV,
                subFolio: sp.SubFolio,
                fechaEmbarque: sp.FechaEmbarque,
                fechaEntrega: sp.FechaEntrega,
                observacionOv: ovInfo.Observacion,
                comentarioExtra: comentarioExtra
            );

            // 7) Documento JSON SAP
            var doc = new
            {
                CardCode = sp.Cliente,
                DocDate = docDate.ToString("yyyy-MM-dd"),
                TaxDate = docDate.ToString("yyyy-MM-dd"),
                DocDueDate = dueDate.ToString("yyyy-MM-dd"),

                Comments = comentarioSap,

                SalesPersonCode = spCode,
                Series = serieId,

                DocumentLines = lineas
            };

            return JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }


        //=======================================================
        //   SUBPEDIDOS DETALLE (acepta id | subpedidoId | subFolio)
        //=======================================================
        [HttpGet("Comercial/SubpedidoDetalleJson")]
        public async Task<IActionResult> SubpedidoDetalleJson(
            [FromQuery(Name = "id")] int? id,          // algunos llamados usan ?id=...
            [FromQuery] int? subpedidoId,              // otros usan ?subpedidoId=...
            [FromQuery] string? subFolio,              // o por subFolio
            CancellationToken ct = default)
        {
            // 1) Resolver el id real del subpedido
            var sid = id ?? subpedidoId;

            if ((sid is null || sid <= 0) && !string.IsNullOrWhiteSpace(subFolio))
            {
                sid = await _context.Subpedidos
                    .AsNoTracking()
                    .Where(s => s.SubFolio == subFolio)
                    .Select(s => (int?)s.Id)
                    .FirstOrDefaultAsync(ct);
            }

            if (sid is null || sid <= 0)
                return BadRequest("Debe especificar id, subpedidoId o subFolio.");

            // 2) Header
            var header = await _context.Subpedidos
                .AsNoTracking()
                .Where(s => s.Id == sid.Value)
                .Select(s => new
                {
                    s.Id,
                    s.SubFolio,
                    s.Cliente,
                    s.FechaEntrega,
                    s.FechaCreacion,
                    Almacen = s.Almacen,      // nombre exacto de tu columna
                    s.DocumentoSAP
                })
                .FirstOrDefaultAsync(ct);

            if (header is null)
                return NotFound($"El subpedido {sid} no existe.");

            // 3) Detalle
            var detalle = await _context.SubpedidoProductos
                .AsNoTracking()
                .Where(p => p.SubpedidoId == sid.Value)
                .OrderBy(p => p.Id)
                .Select(p => new
                {
                    p.ProductoCodigo,
                    p.ProductoNombre,
                    p.KilosCaja,
                    p.Precio,
                    p.Cajas,
                    Almacen = p.Almacen       // nombre exacto en detalle
                })
                .ToListAsync(ct);

            return Ok(new { header, detalle });
        }



        //=======================================================
        //   Actualizar FECHA ENTREGA
        //=======================================================


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ActualizarFechaEntrega(int id, DateTime nuevaFechaEntrega)
        {
            // 1) Verifica que exista SIN traer todas las columnas
            var existe = _context.OrdenVenta.AsNoTracking().Any(x => x.Id == id);
            if (!existe)
                return NotFound(new { ok = false, msg = "Orden no encontrada." });

            // (Opcional) reglas de negocio
            // if (nuevaFechaEntrega.Date < DateTime.Today)
            //     return BadRequest(new { ok = false, msg = "Fecha inválida." });

            // 2) Stub entity: adjunta solo la PK, cambia solo el campo requerido
            var ov = new OrdenVenta { Id = id };
            _context.Attach(ov);
            ov.FechaEntrega = nuevaFechaEntrega;
            _context.Entry(ov).Property(p => p.FechaEntrega).IsModified = true;

            _context.SaveChanges();

            return Json(new
            {
                ok = true,
                fechaEntrega = nuevaFechaEntrega.ToString("yyyy-MM-dd")
            });
        }



        //=======================================================
        //  CANCELAR PEDIDOS VENDEDORES (solo si Estatus <= 2)
        //=======================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarOV(int id, CancellationToken ct = default)
        {
            var header = await _context.OrdenVenta
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.Estatus,
                    x.Serie
                })
                .SingleOrDefaultAsync(ct);

            if (header == null)
                return NotFound(new { ok = false, msg = "Orden no encontrada." });

            if (header.Estatus == 0)
                return Json(new
                {
                    ok = true,
                    estatus = "Cancelado",
                    estatusId = 0,
                    msg = "La orden ya estaba cancelada."
                });

            var subpedidos = await _context.Subpedidos
                .AsNoTracking()
                .Where(s => s.OrdenVentaId == header.Id)
                .Select(s => new
                {
                    s.Id,
                    s.SubFolio,
                    s.DocumentoSAP,
                    s.U_DocMeat
                })
                .ToListAsync(ct);

            var docsSapCancelados = 0;
            var docsSapNoCancelados = 0;

            var docsMeatCancelados = 0;
            var docsMeatNoCancelados = 0;

            foreach (var sp in subpedidos)
            {
                var documentoSap = (sp.DocumentoSAP ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(documentoSap))
                {
                    var canceladoSap = await IntentarCancelarOrdenSapAsync(documentoSap, ct);

                    if (canceladoSap)
                        docsSapCancelados++;
                    else
                        docsSapNoCancelados++;
                }

                var documentoMeat = (sp.U_DocMeat ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(documentoMeat))
                {
                    var canceladoMeat = await IntentarCancelarSolicitudMeatAsync(
                        header.Serie,
                        documentoMeat,
                        ct
                    );

                    if (canceladoMeat)
                        docsMeatCancelados++;
                    else
                        docsMeatNoCancelados++;
                }
            }

            var ov = new OrdenVenta
            {
                Id = header.Id,
                Estatus = 0
            };

            _context.Attach(ov);
            _context.Entry(ov).Property(p => p.Estatus).IsModified = true;

            await _context.SaveChangesAsync(ct);

            return Json(new
            {
                ok = true,
                estatus = "Cancelado",
                estatusId = 0,

                docsSapCancelados,
                docsSapNoCancelados,

                docsMeatCancelados,
                docsMeatNoCancelados,

                msg = "Pedido cancelado. Se intentó cancelar también en SAP y MEAT cuando había documentos relacionados."
            });
        }


        private async Task<bool> IntentarCancelarOrdenSapAsync(string? documentoSap, CancellationToken ct = default)
        {
            var doc = (documentoSap ?? "").Trim();

            if (string.IsNullOrWhiteSpace(doc))
                return false;

            if (!int.TryParse(doc, out var docNumOrEntry))
            {
                _logger.LogWarning("Documento SAP inválido: {DocumentoSAP}", doc);
                return false;
            }

            try
            {
                string baseUrlRoot = "https://172.120.80.3:50000/b1s/v1";
                string company = "PROD_CARNESG";
                string user = "manager";
                string pass = "Sap.2023";

                var cookie = await GetSapSessionCookieAsync(baseUrlRoot, company, user, pass, ct);

                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                using var http = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(90)
                };

                http.DefaultRequestHeaders.Add("Cookie", cookie);
                http.DefaultRequestHeaders.Add("Accept", "application/json");

                // Primero intentamos resolver DocEntry.
                var docEntry = await ResolverDocEntryOrdenSapAsync(
                    http,
                    baseUrlRoot,
                    docNumOrEntry,
                    ct
                );

                if (docEntry <= 0)
                {
                    _logger.LogWarning(
                        "No se encontró DocEntry para DocumentoSAP {DocumentoSAP}",
                        documentoSap
                    );

                    return false;
                }

                var urlCancel = $"{baseUrlRoot.TrimEnd('/')}/Orders({docEntry})/Cancel";

                using var content = new StringContent("{}", Encoding.UTF8, "application/json");

                using var resp = await http.PostAsync(urlCancel, content, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "SAP no permitió cancelar DocumentoSAP {DocumentoSAP}. DocEntry={DocEntry}. HTTP={Status}. Body={Body}",
                        documentoSap,
                        docEntry,
                        (int)resp.StatusCode,
                        body
                    );

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al cancelar DocumentoSAP {DocumentoSAP} en SAP.",
                    documentoSap
                );

                return false;
            }
        }


        private async Task<int> ResolverDocEntryOrdenSapAsync(
    HttpClient http,
    string baseUrlRoot,
    int docNumOrEntry,
    CancellationToken ct = default)
        {
            var root = baseUrlRoot.TrimEnd('/');

            // 1) Intentar como DocEntry directo
            try
            {
                var urlDirecto = $"{root}/Orders({docNumOrEntry})?$select=DocEntry,DocNum,Cancelled,DocumentStatus";

                using var respDirecto = await http.GetAsync(urlDirecto, ct);
                var bodyDirecto = await respDirecto.Content.ReadAsStringAsync(ct);

                if (respDirecto.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(bodyDirecto);
                    var rootJson = json.RootElement;

                    if (rootJson.TryGetProperty("DocEntry", out var de))
                        return de.GetInt32();
                }
            }
            catch
            {
                // Si no fue DocEntry, intentamos como DocNum abajo.
            }

            // 2) Intentar como DocNum
            var urlPorDocNum =
                $"{root}/Orders?$select=DocEntry,DocNum,Cancelled,DocumentStatus&$filter=DocNum eq {docNumOrEntry}";

            using var resp = await http.GetAsync(urlPorDocNum, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return 0;

            using var doc = JsonDocument.Parse(body);
            var jsonRoot = doc.RootElement;

            if (!jsonRoot.TryGetProperty("value", out var value))
                return 0;

            var first = value.EnumerateArray().FirstOrDefault();

            if (first.ValueKind != JsonValueKind.Object)
                return 0;

            if (first.TryGetProperty("DocEntry", out var docEntry))
                return docEntry.GetInt32();

            return 0;
        }





        //=======================================================
        //  Devuelve TODO lo existente en BD: presupuestos normales (cliente) y CEDIS (canal) con sus consumos
        //  + Producción: Plan, Producido, TendenciaProd (sin domingos)
        //=======================================================
        [HttpGet("Comercial/ReportePresupuestosMES")]
        public async Task<IActionResult> ReportePresupuestosMES(CancellationToken ct)
        {
            //var vendedorIds = await GetVendedorIdsActualesAsync(ct);
            //var vendedorIdsCsv = string.Join(",", vendedorIds);

            var vendedorIds = await GetVendedorIdsActualesAsync(ct);
            var canalesCedis = await GetCanalesCedisPorVendedorIdsAsync(vendedorIds, ct);

            var vendedorIdsCsv = string.Join(",", vendedorIds);
            var canalesCsv = string.Join(",", canalesCedis);



            const string sql = @"
SET NOCOUNT ON;

-- =========================================================
-- LIMPIEZA
-- =========================================================
DROP TABLE IF EXISTS #productos;
DROP TABLE IF EXISTS #clientes;
DROP TABLE IF EXISTS #vendedores;
DROP TABLE IF EXISTS #canal_vendedores;
DROP TABLE IF EXISTS #plan_prod;
DROP TABLE IF EXISTS #producido_real;
DROP TABLE IF EXISTS #presupuestos_normales;
DROP TABLE IF EXISTS #ov;
DROP TABLE IF EXISTS #ov_con_surtido;
DROP TABLE IF EXISTS #ov_peso_agg;
DROP TABLE IF EXISTS #ov_surtido_agg;
DROP TABLE IF EXISTS #ov_pendiente_sku;
DROP TABLE IF EXISTS #consumo_cliente;
DROP TABLE IF EXISTS #todo_normal;
DROP TABLE IF EXISTS #presupuestos_cedis;
DROP TABLE IF EXISTS #tr_surtido_agg;
DROP TABLE IF EXISTS #consumo_cedis_base;
DROP TABLE IF EXISTS #todo_cedis;
DROP TABLE IF EXISTS #presupuestos_vendedor;
DROP TABLE IF EXISTS #pres_vendedor_x_canal;
DROP TABLE IF EXISTS #consumo_vendedor_normal;
DROP TABLE IF EXISTS #consumo_vendedor_desde_cedis;
DROP TABLE IF EXISTS #consumo_vendedor_total;
DROP TABLE IF EXISTS #todo_vendedor;
DROP TABLE IF EXISTS #venta_real_base;
DROP TABLE IF EXISTS #todo_vendedor_estrategico_extra;
DROP TABLE IF EXISTS #surtido_cliente;
DROP TABLE IF EXISTS #surtido_ov_cedis;
DROP TABLE IF EXISTS #surtido_transferencias_cedis;
DROP TABLE IF EXISTS #surtido_cedis_base;
DROP TABLE IF EXISTS #surtido_vendedor_normal;
DROP TABLE IF EXISTS #surtido_vendedor_desde_cedis;
DROP TABLE IF EXISTS #surtido_vendedor_total;
DROP TABLE IF EXISTS #surtido_real_cliente;
DROP TABLE IF EXISTS #surtido_real_cedis;
DROP TABLE IF EXISTS #surtido_real_vendedor;
DROP TABLE IF EXISTS #todo_cedis_venta_real_extra;
DROP TABLE IF EXISTS #t_base;
DROP TABLE IF EXISTS #meses_distintos;
DROP TABLE IF EXISTS #dias_laborables;
DROP TABLE IF EXISTS #filtro_vendedores;

-- =========================================================
-- FILTRO DE VENDEDOR DEL USUARIO LOGUEADO
-- Si viene vacío, no filtra.
-- Si viene 28, solo deja vendedor 28.
-- =========================================================
SELECT DISTINCT
    VendedorId = TRY_CONVERT(INT, value)
INTO #filtro_vendedores
FROM STRING_SPLIT(ISNULL(@VendedorIdsCsv, ''), ',')
WHERE TRY_CONVERT(INT, value) IS NOT NULL
  AND TRY_CONVERT(INT, value) > 0;

CREATE CLUSTERED INDEX IX_tmp_filtro_vendedores
ON #filtro_vendedores (VendedorId);

-- =========================================================
-- 1) CATÁLOGOS
-- =========================================================
SELECT
    SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
    ProductoNombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), ''), a.ProductoCodigo),
    U_MASTER = UPPER(LTRIM(RTRIM(a.U_MASTER))),
    ClasificacionId = ISNULL(TRY_CONVERT(INT, a.U_Clas_Prod), 99),
    ClasificacionNombre = ISNULL(cp.Nombre, 'POR DEFINIR'),
    IdTipoSKU = ISNULL(TRY_CONVERT(INT, a.U_TipoporSKU), 0),
    TipoSKUDescripcion = ISNULL(ts.Descripcion, 'POR DEFINIR')
INTO #productos
FROM dbo.ArticuloSap a
LEFT JOIN dbo.ClasificacionProduccion cp
    ON TRY_CONVERT(INT, a.U_Clas_Prod) = cp.ClasificacionId
LEFT JOIN dbo.CatTipoSKU ts
    ON TRY_CONVERT(INT, a.U_TipoporSKU) = ts.IdTipoSKU;

CREATE UNIQUE CLUSTERED INDEX IX_tmp_productos
ON #productos (SKU);

SELECT
    Cliente        = UPPER(LTRIM(RTRIM(cs.Cliente))),
    NombreCliente  = COALESCE(NULLIF(LTRIM(RTRIM(cs.NombreCliente)), ''), cs.Cliente),
    VendedorId     = cs.VendedorId,
    VendedorNombre = LTRIM(RTRIM(cs.VendedorNombre)),
    U_CANAL        = UPPER(LTRIM(RTRIM(cs.U_CANAL)))
INTO #clientes
FROM dbo.ClienteSap cs;

CREATE CLUSTERED INDEX IX_tmp_clientes_cliente
ON #clientes (Cliente);

CREATE INDEX IX_tmp_clientes_vendedor
ON #clientes (VendedorId);

CREATE INDEX IX_tmp_clientes_canal
ON #clientes (U_CANAL);

SELECT DISTINCT
    VendedorId,
    VendedorNombre
INTO #vendedores
FROM #clientes
WHERE VendedorId IS NOT NULL;

CREATE CLUSTERED INDEX IX_tmp_vendedores
ON #vendedores (VendedorId);

SELECT DISTINCT
    Canal      = UPPER(LTRIM(RTRIM(c.U_CANAL))),
    VendedorId = c.VendedorId
INTO #canal_vendedores
FROM dbo.ClienteSap c
WHERE c.VendedorId IS NOT NULL
  AND UPPER(LTRIM(RTRIM(c.U_CANAL))) LIKE 'CEDIS%';

CREATE INDEX IX_tmp_canal_vendedores
ON #canal_vendedores (Canal, VendedorId);

-- =========================================================
-- 2) PLAN PRODUCCIÓN / PRODUCIDO
-- =========================================================
SELECT
    SKU  = UPPER(LTRIM(RTRIM(pd.ProductoCodigo))),
    Mes  = pp.Mes,
    Anio = pp.Anio,
    PlanProduccion = SUM(CAST(pd.Peso AS DECIMAL(18,4)))
INTO #plan_prod
FROM dbo.PlanDetalle pd WITH (NOLOCK)
INNER JOIN dbo.PlanProduccion pp WITH (NOLOCK)
    ON pp.Id = pd.fk_Plan
GROUP BY
    UPPER(LTRIM(RTRIM(pd.ProductoCodigo))),
    pp.Mes,
    pp.Anio;

CREATE CLUSTERED INDEX IX_tmp_plan_prod
ON #plan_prod (SKU, Mes, Anio);

SELECT
    SKU  = UPPER(LTRIM(RTRIM(p.ArticuloCodigo))),
    Mes  = MONTH(p.FechaProduccion),
    Anio = YEAR(p.FechaProduccion),
    Producido = SUM(CAST(p.KgProducidos AS DECIMAL(18,4)))
INTO #producido_real
FROM dbo.ProduccionSigo p WITH (NOLOCK)
WHERE p.FechaProduccion IS NOT NULL
GROUP BY
    UPPER(LTRIM(RTRIM(p.ArticuloCodigo))),
    MONTH(p.FechaProduccion),
    YEAR(p.FechaProduccion);

CREATE CLUSTERED INDEX IX_tmp_producido_real
ON #producido_real (SKU, Mes, Anio);

-- =========================================================
-- 3) PRESUPUESTOS CLIENTE
-- =========================================================
SELECT
    Cliente = UPPER(LTRIM(RTRIM(p.ClienteId))),
    SKU     = UPPER(LTRIM(RTRIM(p.ProductoCodigo))),
    Mes     = p.Mes,
    Anio    = p.Año,
    Presupuesto = SUM(p.Presupuesto)
INTO #presupuestos_normales
FROM dbo.Presupuestos p
GROUP BY
    UPPER(LTRIM(RTRIM(p.ClienteId))),
    UPPER(LTRIM(RTRIM(p.ProductoCodigo))),
    p.Mes,
    p.Año;

CREATE CLUSTERED INDEX IX_tmp_presupuestos_normales
ON #presupuestos_normales (Cliente, SKU, Mes, Anio);

-- =========================================================
-- 4) ORDENES DE VENTA / PENDIENTE
-- =========================================================
SELECT
    o.Id,
    Cliente    = UPPER(LTRIM(RTRIM(o.Cliente))),
    o.VendedorId,
    o.Estatus,
    o.Serie,
    FechaDate = TRY_CONVERT(date, o.FechaEntrega)
INTO #ov
FROM dbo.OrdenVenta o
INNER JOIN dbo.Series ser
    ON o.Serie = ser.NombreSerie
WHERE o.FechaEntrega IS NOT NULL
  AND o.Estatus BETWEEN 1 AND 5
  AND ser.Sucursal = 'MATRIZ';

CREATE CLUSTERED INDEX IX_tmp_ov
ON #ov (Id);

CREATE INDEX IX_tmp_ov_cliente_fecha
ON #ov (Cliente, FechaDate);

CREATE INDEX IX_tmp_ov_vendedor_fecha
ON #ov (VendedorId, FechaDate);

SELECT DISTINCT
    o.Id
INTO #ov_con_surtido
FROM dbo.OrdenVenta o
JOIN dbo.Subpedido sp
    ON sp.OrdenVentaId = o.Id
JOIN dbo.SurtidoEncabezado se
    ON se.SolicitudSurtidoId = sp.U_DocMeat;

CREATE UNIQUE CLUSTERED INDEX IX_tmp_ov_con_surtido
ON #ov_con_surtido (Id);

SELECT
    PedidoId = op.PedidoId,
    SKU      = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
    KgPedido = SUM(CAST(op.Peso AS DECIMAL(18,4)))
INTO #ov_peso_agg
FROM dbo.OrdenVentaProducto op
GROUP BY
    op.PedidoId,
    UPPER(LTRIM(RTRIM(op.ProductoCodigo)));

CREATE CLUSTERED INDEX IX_tmp_ov_peso_agg
ON #ov_peso_agg (PedidoId, SKU);

SELECT
    PedidoId = o.Id,
    SKU      = UPPER(LTRIM(RTRIM(sd.Articulo))),
    KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
INTO #ov_surtido_agg
FROM dbo.OrdenVenta o
JOIN dbo.Subpedido sp
    ON sp.OrdenVentaId = o.Id
JOIN dbo.SurtidoEncabezado se
    ON se.SolicitudSurtidoId = sp.U_DocMeat
JOIN dbo.SurtidoDetalle sd
    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
WHERE se.FechaValidacion IS NOT NULL
GROUP BY
    o.Id,
    UPPER(LTRIM(RTRIM(sd.Articulo)));

CREATE CLUSTERED INDEX IX_tmp_ov_surtido_agg
ON #ov_surtido_agg (PedidoId, SKU);

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
                ELSE CASE
                    WHEN (p.KgPedido - ISNULL(sa.KgSurtido, 0)) < 0 THEN 0
                    ELSE (p.KgPedido - ISNULL(sa.KgSurtido, 0))
                END
            END
        AS DECIMAL(18,4))
INTO #ov_pendiente_sku
FROM #ov ov
JOIN #ov_peso_agg p
    ON p.PedidoId = ov.Id
LEFT JOIN #ov_surtido_agg sa
    ON sa.PedidoId = ov.Id
   AND sa.SKU = p.SKU
LEFT JOIN #ov_con_surtido os
    ON os.Id = ov.Id;

CREATE INDEX IX_tmp_ov_pendiente_cliente
ON #ov_pendiente_sku (Cliente, SKU, FechaDate);

CREATE INDEX IX_tmp_ov_pendiente_vendedor
ON #ov_pendiente_sku (VendedorId, SKU, FechaDate);

-- =========================================================
-- 5) CONSUMO CLIENTE / TODO_NORMAL
-- =========================================================
SELECT
    Cliente = ovp.Cliente,
    SKU     = ovp.SKU,
    Mes     = MONTH(ovp.FechaDate),
    Anio    = YEAR(ovp.FechaDate),
    Kg      = SUM(ovp.KgPendiente)
INTO #consumo_cliente
FROM #ov_pendiente_sku ovp
GROUP BY
    ovp.Cliente,
    ovp.SKU,
    MONTH(ovp.FechaDate),
    YEAR(ovp.FechaDate);

CREATE CLUSTERED INDEX IX_tmp_consumo_cliente
ON #consumo_cliente (Cliente, SKU, Mes, Anio);

SELECT
    'CLIENTE' AS Origen,
    pn.Mes,
    pn.Anio,
    pn.Cliente,
    CAST(NULL AS NVARCHAR(100)) AS Canal,
    CAST(NULL AS INT) AS VendedorId,
    pn.SKU,
    pn.Presupuesto,
    ISNULL(cc.Kg, 0) AS Kg
INTO #todo_normal
FROM #presupuestos_normales pn
LEFT JOIN #consumo_cliente cc
    ON cc.Cliente = pn.Cliente
   AND cc.SKU     = pn.SKU
   AND cc.Mes     = pn.Mes
   AND cc.Anio    = pn.Anio;

INSERT INTO #todo_normal
(
    Origen, Mes, Anio, Cliente, Canal, VendedorId, SKU, Presupuesto, Kg
)
SELECT
    'CLIENTE',
    cc.Mes,
    cc.Anio,
    cc.Cliente,
    CAST(NULL AS NVARCHAR(100)),
    CAST(NULL AS INT),
    cc.SKU,
    CAST(0 AS DECIMAL(18,4)),
    cc.Kg
FROM #consumo_cliente cc
LEFT JOIN #presupuestos_normales pn
    ON pn.Cliente = cc.Cliente
   AND pn.SKU     = cc.SKU
   AND pn.Mes     = cc.Mes
   AND pn.Anio    = cc.Anio
WHERE pn.Cliente IS NULL;

CREATE INDEX IX_tmp_todo_normal
ON #todo_normal (Mes, Anio, Cliente, SKU);

-- =========================================================
-- 6) PRESUPUESTO / CONSUMO CEDIS
-- =========================================================
SELECT
    Canal = UPPER(LTRIM(RTRIM(pc.Canal))),
    SKU   = UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
    Mes   = pc.Mes,
    Anio  = pc.Anio,
    Presupuesto = SUM(pc.PresupuestoAsignado)
INTO #presupuestos_cedis
FROM dbo.PresupuestoCedis pc
GROUP BY
    UPPER(LTRIM(RTRIM(pc.Canal))),
    UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
    pc.Mes,
    pc.Anio;

CREATE CLUSTERED INDEX IX_tmp_presupuestos_cedis
ON #presupuestos_cedis (Canal, SKU, Mes, Anio);

SELECT
    ts.TransferenciaId,
    SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
    KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
INTO #tr_surtido_agg
FROM dbo.TransferenciaSurtido ts
GROUP BY
    ts.TransferenciaId,
    UPPER(LTRIM(RTRIM(ts.Sku)));

CREATE CLUSTERED INDEX IX_tmp_tr_surtido_agg
ON #tr_surtido_agg (TransferenciaId, SKU);

SELECT
    Canal,
    SKU,
    Mes,
    Anio,
    Kg = SUM(Kg)
INTO #consumo_cedis_base
FROM
(
    SELECT
        Canal = cli.U_CANAL,
        SKU   = ovp.SKU,
        Mes   = MONTH(ovp.FechaDate),
        Anio  = YEAR(ovp.FechaDate),
        Kg    = SUM(ovp.KgPendiente)
    FROM #ov_pendiente_sku ovp
    JOIN #clientes cli
        ON cli.Cliente = ovp.Cliente
    WHERE cli.U_CANAL LIKE 'CEDIS%'
    GROUP BY
        cli.U_CANAL,
        ovp.SKU,
        MONTH(ovp.FechaDate),
        YEAR(ovp.FechaDate)

    UNION ALL

    SELECT
        Canal = UPPER(LTRIM(RTRIM(s.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
        Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
        Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
        Kg    = SUM(
                    CASE
                        WHEN (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0)) < 0 THEN 0
                        ELSE (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0))
                    END
               )
    FROM dbo.Transferencias t
    JOIN dbo.TransferenciaDetalles td
        ON td.TransferenciaId = t.Id
    JOIN dbo.Series s
        ON s.Sucursal = t.Sucursal
    LEFT JOIN #tr_surtido_agg tsa
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
GROUP BY
    Canal, SKU, Mes, Anio;

CREATE CLUSTERED INDEX IX_tmp_consumo_cedis_base
ON #consumo_cedis_base (Canal, SKU, Mes, Anio);

SELECT
    'CEDIS' AS Origen,
    pc.Mes,
    pc.Anio,
    CAST(NULL AS NVARCHAR(50)) AS Cliente,
    pc.Canal,
    CAST(NULL AS INT) AS VendedorId,
    pc.SKU,
    pc.Presupuesto,
    ISNULL(cc.Kg, 0) AS Kg
INTO #todo_cedis
FROM #presupuestos_cedis pc
LEFT JOIN #consumo_cedis_base cc
    ON cc.Canal = pc.Canal
   AND cc.SKU   = pc.SKU
   AND cc.Mes   = pc.Mes
   AND cc.Anio  = pc.Anio;

CREATE INDEX IX_tmp_todo_cedis
ON #todo_cedis (Mes, Anio, Canal, SKU);

-- =========================================================
-- 7) PRESUPUESTO / CONSUMO VENDEDOR
-- =========================================================
SELECT
    VendedorId,
    SKU = UPPER(LTRIM(RTRIM(pv.ProductoCodigo))),
    Mes = pv.Mes,
    Anio = pv.Anio,
    Presupuesto = SUM(pv.PresupuestoAsignado)
INTO #presupuestos_vendedor
FROM dbo.PresupuestoVendedor pv
GROUP BY
    pv.VendedorId,
    UPPER(LTRIM(RTRIM(pv.ProductoCodigo))),
    pv.Mes,
    pv.Anio;

CREATE CLUSTERED INDEX IX_tmp_presupuestos_vendedor
ON #presupuestos_vendedor (VendedorId, SKU, Mes, Anio);

SELECT
    cv.Canal,
    pv.SKU,
    pv.Mes,
    pv.Anio,
    PresTotalCanal = SUM(CAST(pv.Presupuesto AS DECIMAL(18,4)))
INTO #pres_vendedor_x_canal
FROM #presupuestos_vendedor pv
JOIN #canal_vendedores cv
    ON cv.VendedorId = pv.VendedorId
GROUP BY
    cv.Canal, pv.SKU, pv.Mes, pv.Anio;

CREATE CLUSTERED INDEX IX_tmp_pres_vendedor_x_canal
ON #pres_vendedor_x_canal (Canal, SKU, Mes, Anio);

SELECT
    ovp.VendedorId,
    SKU  = ovp.SKU,
    Mes  = MONTH(ovp.FechaDate),
    Anio = YEAR(ovp.FechaDate),
    Kg   = SUM(ovp.KgPendiente)
INTO #consumo_vendedor_normal
FROM #ov_pendiente_sku ovp
JOIN #clientes c
    ON c.Cliente = ovp.Cliente
   AND ISNULL(c.U_CANAL, '') NOT LIKE 'CEDIS%'
WHERE ovp.VendedorId IS NOT NULL
GROUP BY
    ovp.VendedorId,
    ovp.SKU,
    MONTH(ovp.FechaDate),
    YEAR(ovp.FechaDate);

CREATE CLUSTERED INDEX IX_tmp_consumo_vendedor_normal
ON #consumo_vendedor_normal (VendedorId, SKU, Mes, Anio);

SELECT
    VendedorId = pv.VendedorId,
    SKU        = pv.SKU,
    Mes        = pv.Mes,
    Anio       = pv.Anio,
    Kg = SUM(
            CASE
                WHEN ISNULL(pxc.PresTotalCanal, 0) <= 0 THEN 0
                ELSE (cb.Kg * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal))
            END
        )
INTO #consumo_vendedor_desde_cedis
FROM #presupuestos_vendedor pv
JOIN #canal_vendedores cv
    ON cv.VendedorId = pv.VendedorId
JOIN #pres_vendedor_x_canal pxc
    ON pxc.Canal = cv.Canal
   AND pxc.SKU   = pv.SKU
   AND pxc.Mes   = pv.Mes
   AND pxc.Anio  = pv.Anio
JOIN #consumo_cedis_base cb
    ON cb.Canal = cv.Canal
   AND cb.SKU   = pv.SKU
   AND cb.Mes   = pv.Mes
   AND cb.Anio  = pv.Anio
GROUP BY
    pv.VendedorId, pv.SKU, pv.Mes, pv.Anio;

CREATE CLUSTERED INDEX IX_tmp_consumo_vendedor_desde_cedis
ON #consumo_vendedor_desde_cedis (VendedorId, SKU, Mes, Anio);

SELECT
    VendedorId,
    SKU,
    Mes,
    Anio,
    Kg = SUM(Kg)
INTO #consumo_vendedor_total
FROM
(
    SELECT * FROM #consumo_vendedor_normal
    UNION ALL
    SELECT * FROM #consumo_vendedor_desde_cedis
) x
GROUP BY
    VendedorId, SKU, Mes, Anio;

CREATE CLUSTERED INDEX IX_tmp_consumo_vendedor_total
ON #consumo_vendedor_total (VendedorId, SKU, Mes, Anio);

SELECT
    'VENDEDOR' AS Origen,
    pv.Mes,
    pv.Anio,
    CAST(NULL AS NVARCHAR(50)) AS Cliente,
    CAST(NULL AS NVARCHAR(100)) AS Canal,
    pv.VendedorId,
    pv.SKU,
    pv.Presupuesto,
    ISNULL(cv.Kg, 0) AS Kg
INTO #todo_vendedor
FROM #presupuestos_vendedor pv
LEFT JOIN #consumo_vendedor_total cv
    ON cv.VendedorId = pv.VendedorId
   AND cv.SKU        = pv.SKU
   AND cv.Mes        = pv.Mes
   AND cv.Anio       = pv.Anio;

CREATE INDEX IX_tmp_todo_vendedor
ON #todo_vendedor (Mes, Anio, VendedorId, SKU);

-- =========================================================
-- 8) VENTA REAL BASE / EXTRAS
-- =========================================================
SELECT
    ArticuloCodigo = UPPER(LTRIM(RTRIM(b.Articulo))),
    Mes            = MONTH(a.FechaValidacion),
    Anio           = YEAR(a.FechaValidacion),
    VendedorId     = cs.VendedorId,
    Vendedor       = UPPER(LTRIM(RTRIM(cs.VendedorNombre))),
    U_CANAL        = UPPER(LTRIM(RTRIM(cs.U_CANAL))),
    KgVendidos     = SUM(CAST(b.Kg AS DECIMAL(18,4)))
INTO #venta_real_base
FROM dbo.SurtidoEncabezado a
INNER JOIN dbo.SurtidoDetalle b
    ON a.SolicitudSurtidoId = b.SolicitudSurtidoId
LEFT JOIN dbo.ClienteSap cs
    ON cs.Cliente = a.CodigoSap
WHERE a.FechaValidacion IS NOT NULL
GROUP BY
    UPPER(LTRIM(RTRIM(b.Articulo))),
    MONTH(a.FechaValidacion),
    YEAR(a.FechaValidacion),
    cs.VendedorId,
    UPPER(LTRIM(RTRIM(cs.VendedorNombre))),
    UPPER(LTRIM(RTRIM(cs.U_CANAL)));

CREATE INDEX IX_tmp_venta_real_base_vendedor
ON #venta_real_base (VendedorId, ArticuloCodigo, Mes, Anio);

CREATE INDEX IX_tmp_venta_real_base_canal
ON #venta_real_base (U_CANAL, ArticuloCodigo, Mes, Anio);

SELECT
    'VENDEDOR' AS Origen,
    vr.Mes,
    vr.Anio,
    CAST(NULL AS NVARCHAR(50)) AS Cliente,
    CAST(NULL AS NVARCHAR(100)) AS Canal,
    vr.VendedorId,
    vr.ArticuloCodigo AS SKU,
    CAST(0 AS DECIMAL(18,4)) AS Presupuesto,
    CAST(0 AS DECIMAL(18,4)) AS Kg
INTO #todo_vendedor_estrategico_extra
FROM #venta_real_base vr
WHERE vr.VendedorId IS NOT NULL
  AND ISNULL(vr.U_CANAL, '') = 'ESTRATEGICO'
  AND NOT EXISTS
  (
      SELECT 1
      FROM #todo_vendedor tv
      WHERE tv.VendedorId = vr.VendedorId
        AND tv.SKU        = vr.ArticuloCodigo
        AND tv.Mes        = vr.Mes
        AND tv.Anio       = vr.Anio
  );

CREATE INDEX IX_tmp_todo_vendedor_estrategico_extra
ON #todo_vendedor_estrategico_extra (Mes, Anio, VendedorId, SKU);

-- =========================================================
-- 9) SURTIDO REAL SEPARADO POR ORIGEN
-- =========================================================
SELECT
    Cliente = UPPER(LTRIM(RTRIM(o.Cliente))),
    SKU     = UPPER(LTRIM(RTRIM(sd.Articulo))),
    Mes     = MONTH(se.FechaValidacion),
    Anio    = YEAR(se.FechaValidacion),
    KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
INTO #surtido_cliente
FROM dbo.OrdenVenta o
JOIN dbo.Series ser
    ON ser.NombreSerie = o.Serie
JOIN dbo.Subpedido sp
    ON sp.OrdenVentaId = o.Id
JOIN dbo.SurtidoEncabezado se
    ON se.SolicitudSurtidoId = sp.U_DocMeat
JOIN dbo.SurtidoDetalle sd
    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
JOIN #clientes cli
    ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
WHERE o.Estatus <> 0
  AND se.FechaValidacion IS NOT NULL
  AND ser.Sucursal = 'MATRIZ'
  AND ISNULL(cli.U_CANAL, '') NOT LIKE 'CEDIS%'
GROUP BY
    UPPER(LTRIM(RTRIM(o.Cliente))),
    UPPER(LTRIM(RTRIM(sd.Articulo))),
    MONTH(se.FechaValidacion),
    YEAR(se.FechaValidacion);

CREATE CLUSTERED INDEX IX_tmp_surtido_cliente
ON #surtido_cliente (Cliente, SKU, Mes, Anio);

SELECT
    Canal = cli.U_CANAL,
    SKU   = UPPER(LTRIM(RTRIM(sd.Articulo))),
    Mes   = MONTH(se.FechaValidacion),
    Anio  = YEAR(se.FechaValidacion),
    KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
INTO #surtido_ov_cedis
FROM dbo.OrdenVenta o
JOIN dbo.Series ser
    ON ser.NombreSerie = o.Serie
JOIN dbo.Subpedido sp
    ON sp.OrdenVentaId = o.Id
JOIN dbo.SurtidoEncabezado se
    ON se.SolicitudSurtidoId = sp.U_DocMeat
JOIN dbo.SurtidoDetalle sd
    ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
JOIN #clientes cli
    ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
WHERE o.Estatus <> 0
  AND se.FechaValidacion IS NOT NULL
  AND ser.Sucursal = 'MATRIZ'
  AND cli.U_CANAL LIKE 'CEDIS%'
GROUP BY
    cli.U_CANAL,
    UPPER(LTRIM(RTRIM(sd.Articulo))),
    MONTH(se.FechaValidacion),
    YEAR(se.FechaValidacion);

CREATE CLUSTERED INDEX IX_tmp_surtido_ov_cedis
ON #surtido_ov_cedis (Canal, SKU, Mes, Anio);

SELECT
    Canal = UPPER(LTRIM(RTRIM(s.Canal))),
    SKU   = UPPER(LTRIM(RTRIM(ts.Sku))),
    Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
    Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
    KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
INTO #surtido_transferencias_cedis
FROM dbo.TransferenciaSurtido ts
JOIN dbo.Transferencias t
    ON t.Id = ts.TransferenciaId
JOIN dbo.Series s
    ON s.Sucursal = t.Sucursal
WHERE t.FechaSolicitud IS NOT NULL
  AND t.Estatus >= 5
  AND ts.KgSurtido > 0
  AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
GROUP BY
    UPPER(LTRIM(RTRIM(s.Canal))),
    UPPER(LTRIM(RTRIM(ts.Sku))),
    MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
    YEAR(TRY_CONVERT(date, t.FechaSolicitud));

CREATE CLUSTERED INDEX IX_tmp_surtido_transferencias_cedis
ON #surtido_transferencias_cedis (Canal, SKU, Mes, Anio);

SELECT
    Canal,
    SKU,
    Mes,
    Anio,
    KgSurtido = SUM(KgSurtido)
INTO #surtido_cedis_base
FROM
(
    SELECT * FROM #surtido_ov_cedis
    UNION ALL
    SELECT * FROM #surtido_transferencias_cedis
) x
GROUP BY
    Canal, SKU, Mes, Anio;

CREATE CLUSTERED INDEX IX_tmp_surtido_cedis_base
ON #surtido_cedis_base (Canal, SKU, Mes, Anio);

SELECT
    cl.VendedorId,
    sc.SKU,
    sc.Mes,
    sc.Anio,
    KgSurtido = SUM(sc.KgSurtido)
INTO #surtido_vendedor_normal
FROM #surtido_cliente sc
JOIN #clientes cl
    ON cl.Cliente = sc.Cliente
WHERE cl.VendedorId IS NOT NULL
GROUP BY
    cl.VendedorId, sc.SKU, sc.Mes, sc.Anio;

CREATE CLUSTERED INDEX IX_tmp_surtido_vendedor_normal
ON #surtido_vendedor_normal (VendedorId, SKU, Mes, Anio);

SELECT
    VendedorId = pv.VendedorId,
    SKU        = pv.SKU,
    Mes        = pv.Mes,
    Anio       = pv.Anio,
    KgSurtido  = SUM(
                    CASE
                        WHEN ISNULL(pxc.PresTotalCanal, 0) <= 0 THEN 0
                        ELSE (sb.KgSurtido * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal))
                    END
                 )
INTO #surtido_vendedor_desde_cedis
FROM #presupuestos_vendedor pv
JOIN #canal_vendedores cv
    ON cv.VendedorId = pv.VendedorId
JOIN #pres_vendedor_x_canal pxc
    ON pxc.Canal = cv.Canal
   AND pxc.SKU   = pv.SKU
   AND pxc.Mes   = pv.Mes
   AND pxc.Anio  = pv.Anio
JOIN #surtido_cedis_base sb
    ON sb.Canal = cv.Canal
   AND sb.SKU   = pv.SKU
   AND sb.Mes   = pv.Mes
   AND sb.Anio  = pv.Anio
GROUP BY
    pv.VendedorId, pv.SKU, pv.Mes, pv.Anio;

CREATE CLUSTERED INDEX IX_tmp_surtido_vendedor_desde_cedis
ON #surtido_vendedor_desde_cedis (VendedorId, SKU, Mes, Anio);

SELECT
    VendedorId,
    SKU,
    Mes,
    Anio,
    KgSurtido = SUM(KgSurtido)
INTO #surtido_vendedor_total
FROM
(
    SELECT * FROM #surtido_vendedor_normal
    UNION ALL
    SELECT * FROM #surtido_vendedor_desde_cedis
) x
GROUP BY
    VendedorId, SKU, Mes, Anio;

CREATE CLUSTERED INDEX IX_tmp_surtido_vendedor_total
ON #surtido_vendedor_total (VendedorId, SKU, Mes, Anio);

-- Tablas finales separadas por origen, para evitar JOIN con OR
SELECT
    Cliente,
    SKU,
    Mes,
    Anio,
    KgSurtido
INTO #surtido_real_cliente
FROM #surtido_cliente;

CREATE CLUSTERED INDEX IX_tmp_surtido_real_cliente
ON #surtido_real_cliente (Cliente, SKU, Mes, Anio);

SELECT
    Canal,
    SKU,
    Mes,
    Anio,
    KgSurtido
INTO #surtido_real_cedis
FROM #surtido_cedis_base;

CREATE CLUSTERED INDEX IX_tmp_surtido_real_cedis
ON #surtido_real_cedis (Canal, SKU, Mes, Anio);

SELECT
    VendedorId,
    SKU,
    Mes,
    Anio,
    KgSurtido
INTO #surtido_real_vendedor
FROM #surtido_vendedor_total;

CREATE CLUSTERED INDEX IX_tmp_surtido_real_vendedor
ON #surtido_real_vendedor (VendedorId, SKU, Mes, Anio);

-- =========================================================
-- 10) EXTRA CEDIS DESDE VENTA REAL
-- =========================================================
SELECT
    'CEDIS' AS Origen,
    vr.Mes,
    vr.Anio,
    CAST(NULL AS NVARCHAR(50)) AS Cliente,
    vr.U_CANAL AS Canal,
    CAST(NULL AS INT) AS VendedorId,
    vr.ArticuloCodigo AS SKU,
    CAST(0 AS DECIMAL(18,4)) AS Presupuesto,
    CAST(0 AS DECIMAL(18,4)) AS Kg
INTO #todo_cedis_venta_real_extra
FROM #venta_real_base vr
WHERE ISNULL(vr.U_CANAL, '') LIKE 'CEDIS%'
  AND NOT EXISTS
  (
      SELECT 1
      FROM #todo_cedis tc
      WHERE tc.Canal = vr.U_CANAL
        AND tc.SKU   = vr.ArticuloCodigo
        AND tc.Mes   = vr.Mes
        AND tc.Anio  = vr.Anio
  );

CREATE INDEX IX_tmp_todo_cedis_venta_real_extra
ON #todo_cedis_venta_real_extra (Mes, Anio, Canal, SKU);

-- =========================================================
-- 11) BASE FINAL
-- =========================================================
SELECT *
INTO #t_base
FROM
(
    SELECT * FROM #todo_cedis
    UNION ALL
    SELECT * FROM #todo_cedis_venta_real_extra
    UNION ALL
    SELECT * FROM #todo_vendedor
) t;

CREATE INDEX IX_tmp_t_base
ON #t_base (Origen, Anio, Mes, SKU, Cliente, Canal, VendedorId);

-- =========================================================
-- 12) DÍAS LABORABLES
-- =========================================================
SELECT DISTINCT
    Mes,
    Anio
INTO #meses_distintos
FROM #t_base;

CREATE CLUSTERED INDEX IX_tmp_meses_distintos
ON #meses_distintos (Mes, Anio);

SELECT
    m.Mes,
    m.Anio,
    DiasMesLaborables = SUM(
        CASE
            WHEN (DATEDIFF(day, '19000101', cal.D) % 7) = 6 THEN 0
            ELSE 1
        END
    ),
    DiasLaborados = SUM(
        CASE
            WHEN cutoff.CutoffDate IS NULL THEN 0
            WHEN cal.D <= cutoff.CutoffDate
             AND (DATEDIFF(day, '19000101', cal.D) % 7) <> 6
            THEN 1
            ELSE 0
        END
    )
INTO #dias_laborables
FROM #meses_distintos m
CROSS APPLY
(
    SELECT
        StartDate = DATEFROMPARTS(m.Anio, m.Mes, 1),
        EndDate   = EOMONTH(DATEFROMPARTS(m.Anio, m.Mes, 1))
) rng
CROSS APPLY
(
    SELECT
        CutoffDate =
            CASE
                WHEN CONVERT(date, GETDATE()) < rng.StartDate THEN NULL
                WHEN CONVERT(date, GETDATE()) > rng.EndDate   THEN rng.EndDate
                ELSE CONVERT(date, GETDATE())
            END
) cutoff
CROSS APPLY
(
    SELECT TOP (DATEDIFF(day, rng.StartDate, rng.EndDate) + 1)
           n = ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1
    FROM sys.all_objects
) nums
CROSS APPLY
(
    SELECT D = DATEADD(day, nums.n, rng.StartDate)
) cal
GROUP BY
    m.Mes,
    m.Anio;

CREATE CLUSTERED INDEX IX_tmp_dias_laborables
ON #dias_laborables (Mes, Anio);

-- =========================================================
-- 13) SELECT FINAL
--     YA SIN JOIN CON OR PARA SURTIDO
-- =========================================================
SELECT
    t.Origen,
    t.Mes  AS MesConsulta,
    t.Anio AS AnioConsulta,
    ISNULL(t.Cliente, '-') AS ClienteCodigo,
    ISNULL(cl.NombreCliente, '-') AS NombreCliente,
    ISNULL(t.Canal, '-') AS Canal,
    ISNULL(t.VendedorId, 0) AS VendedorId,
    ISNULL(COALESCE(vend.VendedorNombre, cl.VendedorNombre), '-') AS VendedorNombre,
    t.SKU AS ProductoCodigo,
    prd.ProductoNombre,
    prd.U_MASTER AS U_MASTER,
    ISNULL(prd.ClasificacionId, 99) AS ClasificacionId,
    ISNULL(prd.ClasificacionNombre, 'POR DEFINIR') AS ClasificacionNombre,
    ISNULL(prd.IdTipoSKU, 0) AS IdTipoSKU,
    ISNULL(prd.TipoSKUDescripcion, 'POR DEFINIR') AS TipoSKUDescripcion,
    CAST(t.Presupuesto AS DECIMAL(18,4)) AS PresupuestoAsignado,
    CAST(t.Kg AS DECIMAL(18,4)) AS KgPedidosMes,
    CAST(
        CASE
            WHEN t.Origen = 'CLIENTE'  THEN ISNULL(src.KgSurtido, 0)
            WHEN t.Origen = 'CEDIS'    THEN ISNULL(srd.KgSurtido, 0)
            WHEN t.Origen = 'VENDEDOR' THEN ISNULL(srv.KgSurtido, 0)
            ELSE 0
        END
    AS DECIMAL(18,4)) AS KgSurtidoReal,
    CAST(
        CASE
            WHEN (
                t.Presupuesto
                - ISNULL(t.Kg, 0)
                - CASE
                    WHEN t.Origen = 'CLIENTE'  THEN ISNULL(src.KgSurtido, 0)
                    WHEN t.Origen = 'CEDIS'    THEN ISNULL(srd.KgSurtido, 0)
                    WHEN t.Origen = 'VENDEDOR' THEN ISNULL(srv.KgSurtido, 0)
                    ELSE 0
                  END
            ) < 0 THEN 0
            ELSE (
                t.Presupuesto
                - ISNULL(t.Kg, 0)
                - CASE
                    WHEN t.Origen = 'CLIENTE'  THEN ISNULL(src.KgSurtido, 0)
                    WHEN t.Origen = 'CEDIS'    THEN ISNULL(srd.KgSurtido, 0)
                    WHEN t.Origen = 'VENDEDOR' THEN ISNULL(srv.KgSurtido, 0)
                    ELSE 0
                  END
            )
        END
    AS DECIMAL(18,4)) AS DisponibleVenta,
    CAST(ISNULL(pp.PlanProduccion, 0) AS DECIMAL(18,4)) AS PlanProduccion,
    CAST(ISNULL(pr.Producido, 0) AS DECIMAL(18,4)) AS Producido,
    CAST(
        CASE
            WHEN ISNULL(dl.DiasLaborados, 0) <= 0 THEN 0
            ELSE (ISNULL(pr.Producido, 0) / NULLIF(CAST(dl.DiasLaborados AS DECIMAL(18,4)), 0))
                 * CAST(ISNULL(dl.DiasMesLaborables, 0) AS DECIMAL(18,4))
        END
    AS DECIMAL(18,4)) AS TendenciaProduccion
FROM #t_base t
LEFT JOIN #productos prd
    ON prd.SKU = t.SKU
LEFT JOIN #clientes cl
    ON cl.Cliente = t.Cliente
LEFT JOIN #vendedores vend
    ON vend.VendedorId = t.VendedorId
LEFT JOIN #surtido_real_cliente src
    ON t.Origen = 'CLIENTE'
   AND src.Cliente = t.Cliente
   AND src.SKU     = t.SKU
   AND src.Mes     = t.Mes
   AND src.Anio    = t.Anio
LEFT JOIN #surtido_real_cedis srd
    ON t.Origen = 'CEDIS'
   AND srd.Canal = t.Canal
   AND srd.SKU   = t.SKU
   AND srd.Mes   = t.Mes
   AND srd.Anio  = t.Anio
LEFT JOIN #surtido_real_vendedor srv
    ON t.Origen = 'VENDEDOR'
   AND srv.VendedorId = t.VendedorId
   AND srv.SKU        = t.SKU
   AND srv.Mes        = t.Mes
   AND srv.Anio       = t.Anio
LEFT JOIN #plan_prod pp
    ON pp.SKU  = t.SKU
   AND pp.Mes  = t.Mes
   AND pp.Anio = t.Anio
LEFT JOIN #producido_real pr
    ON pr.SKU  = t.SKU
   AND pr.Mes  = t.Mes
   AND pr.Anio = t.Anio
LEFT JOIN #dias_laborables dl
    ON dl.Mes  = t.Mes
   AND dl.Anio = t.Anio
WHERE
    -- Si NO trae canal CEDIS, ve todo
    NOT EXISTS
    (
        SELECT 1
        FROM STRING_SPLIT(ISNULL(@CanalesCsv, ''), ',') c
        WHERE ISNULL(LTRIM(RTRIM(c.value)), '') <> ''
          AND UPPER(LTRIM(RTRIM(c.value))) LIKE 'CEDIS%'
    )

    OR

    -- Si SÍ trae canal CEDIS, se limita al canal correspondiente
    EXISTS
    (
        SELECT 1
        FROM STRING_SPLIT(ISNULL(@CanalesCsv, ''), ',') c
        WHERE ISNULL(LTRIM(RTRIM(c.value)), '') <> ''
          AND UPPER(LTRIM(RTRIM(c.value))) LIKE 'CEDIS%'
          AND
          (
              UPPER(LTRIM(RTRIM(ISNULL(t.Canal, '')))) =
              UPPER(LTRIM(RTRIM(c.value)))

              OR 'CEDIS-' + UPPER(LTRIM(RTRIM(ISNULL(t.Canal, '')))) =
              UPPER(LTRIM(RTRIM(c.value)))

              OR REPLACE(UPPER(LTRIM(RTRIM(ISNULL(t.Canal, '')))), 'CEDIS-', '') =
                 REPLACE(UPPER(LTRIM(RTRIM(c.value))), 'CEDIS-', '')
          )
    )
ORDER BY
    t.Origen,
    t.Anio,
    t.Mes,
    ISNULL(t.Cliente, ''),
    ISNULL(t.Canal, ''),
    t.SKU;
";

            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);
            var rows = await conn.QueryAsync<PresupuestoConsumoDto>(
     new CommandDefinition(
         sql,
         new
         {
             VendedorIdsCsv = vendedorIdsCsv,
             CanalesCsv = canalesCsv
         },
         cancellationToken: ct
     )
 );

            return Ok(rows);
        }

        private static string Norm(string? s)
            => (s ?? "").Trim().ToUpperInvariant();

        private static bool EsCanalCedis(string? canal)
            => !string.IsNullOrWhiteSpace(canal) && Norm(canal).StartsWith("CEDIS-");




        //=============================================================
        // PRESUPUESTO POR VENDEDOR (prioriza CEDIS si el cliente es canal CEDIS)
        // GET: /Comercial/ObtenerPresupuestoProductoVendedor?vendedorId=1&cardCode=C000176&productoCodigo=SKU01&mes=10&anio=2025
        //=============================================================
        [HttpGet]
        public async Task<IActionResult> ObtenerPresupuestoProductoVendedor(
            int vendedorId,
            string? cardCode,
            string productoCodigo,
            int mes,
            int anio)
        {
            if (vendedorId <= 0) return BadRequest("Se requiere vendedorId.");
            if (string.IsNullOrWhiteSpace(productoCodigo)) return BadRequest("Se requiere producto.");

            var item = Norm(productoCodigo);
            var card = Norm(cardCode);

            // 1) LEFT JOIN ClienteSap -> PresupuestoCedis por canal (solo si existe)
            //    OJO: Si el cliente NO es CEDIS, aunque haya canal, NO usamos CEDIS.
            var joinCedis = await (
                from cli in _context.ClienteSap.AsNoTracking()
                where card != "" && cli.Cliente == card
                join pc in _context.PresupuestoCedis.AsNoTracking()
                        .Where(x => x.Mes == mes && x.Anio == anio && x.ProductoCodigo == item)
                    on cli.U_CANAL equals pc.Canal into gj
                from pc in gj.DefaultIfEmpty() // LEFT
                select new
                {
                    Canal = cli.U_CANAL,
                    PresupuestoCedis = (decimal?)pc.PresupuestoAsignado
                }
            ).FirstOrDefaultAsync();

            // 2) Si el cliente es canal CEDIS y hay presupuesto en PresupuestoCedis para ese SKU, úsalo
            if (joinCedis != null && EsCanalCedis(joinCedis.Canal) && joinCedis.PresupuestoCedis.HasValue)
            {
                var p = joinCedis.PresupuestoCedis.Value;
                return Json(new
                {
                    productoCodigo = item,
                    presupuestoAsignado = p,
                    tienePresupuesto = p > 0m,
                    enPresupuesto = p > 0m,
                    origen = "CEDIS",
                    canal = Norm(joinCedis.Canal)
                });
            }

            // 3) Fallback: presupuesto NORMAL por vendedor
            var normal = await _context.PresupuestoVendedor.AsNoTracking()
                .Where(p => p.VendedorId == vendedorId
                         && p.Mes == mes
                         && p.Anio == anio
                         && p.ProductoCodigo == item)
                .Select(p => (decimal?)p.PresupuestoAsignado)
                .FirstOrDefaultAsync();

            var pn = normal ?? 0m;

            return Json(new
            {
                productoCodigo = item,
                presupuestoAsignado = pn,
                tienePresupuesto = pn > 0m,
                enPresupuesto = pn > 0m,
                origen = "VENDEDOR",
                canal = joinCedis?.Canal != null ? Norm(joinCedis.Canal) : ""
            });
        }


        //=============================================================
        // DISPONIBLE POR VENDEDOR (prioriza CEDIS si el cliente es canal CEDIS)
        // GET: /Comercial/ObtenerProductosConPresupuestoDisponibleVendedor?vendedorId=1&cardCode=C000176&fechaEntrega=2025-10-29
        //=============================================================
        [HttpGet]
        public async Task<IActionResult> ObtenerProductosConPresupuestoDisponibleVendedor(
            int vendedorId,
            string? cardCode,
            DateTime fechaEntrega)
        {
            if (vendedorId <= 0) return BadRequest("vendedorId no especificado.");

            int mes = fechaEntrega.Month;
            int anio = fechaEntrega.Year;

            var card = Norm(cardCode);

            // 1) Canal del cliente (puede ser MAYOREO, CEDIS-MDA, etc.)
            string? canalRaw = null;
            if (!string.IsNullOrWhiteSpace(card))
            {
                canalRaw = await _context.ClienteSap.AsNoTracking()
                    .Where(c => c.Cliente == card)
                    .Select(c => c.U_CANAL)
                    .FirstOrDefaultAsync();
            }
            var canalUp = Norm(canalRaw);
            var usarCedis = EsCanalCedis(canalUp);

            // 2) Presupuesto vendedor (siempre lo cargamos, sirve de fallback)
            var vendList = await _context.PresupuestoVendedor.AsNoTracking()
                .Where(p => p.VendedorId == vendedorId && p.Mes == mes && p.Anio == anio)
                .GroupBy(p => p.ProductoCodigo)
                .Select(g => new
                {
                    SKU = Norm(g.Key),
                    Presupuesto = g.Sum(x => (decimal?)x.PresupuestoAsignado) ?? 0m
                })
                .ToListAsync();

            var vendDict = vendList
                .Where(x => x.SKU != "")
                .ToDictionary(x => x.SKU, x => x.Presupuesto);

            // 3) Presupuesto CEDIS por canal (solo si aplica)
            Dictionary<string, decimal> cedisDict = new();
            if (usarCedis)
            {
                var cedisList = await _context.PresupuestoCedis.AsNoTracking()
                    .Where(pc => pc.Canal == canalUp && pc.Mes == mes && pc.Anio == anio)
                    .GroupBy(pc => pc.ProductoCodigo)
                    .Select(g => new
                    {
                        SKU = Norm(g.Key),
                        Presupuesto = g.Sum(x => (decimal?)x.PresupuestoAsignado) ?? 0m
                    })
                    .ToListAsync();

                cedisDict = cedisList
                    .Where(x => x.SKU != "")
                    .ToDictionary(x => x.SKU, x => x.Presupuesto);
            }

            // 4) Consumo por VENDEDOR (siempre, por si se usa fallback)

            var consumoVend = await (
                from o in _context.OrdenVenta.AsNoTracking()
                join cli in _context.ClienteSap.AsNoTracking() on o.Cliente equals cli.Cliente
                join op in _context.OrdenVentaProducto.AsNoTracking() on o.Id equals op.PedidoId
                join ser in _context.Series.AsNoTracking() on o.Serie equals ser.NombreSerie
                where cli.VendedorId == vendedorId
                      && o.FechaEntrega.Month == mes
                      && o.FechaEntrega.Year == anio
                      && o.Estatus != 0
                      && (op.Eliminado == null || op.Eliminado == false)
                      && ser.Sucursal == "MATRIZ"
                group op by op.ProductoCodigo into g
                select new
                {
                    SKU = Norm(g.Key),
                    Kg = g.Sum(x => (decimal?)x.Peso) ?? 0m
                }
            ).ToListAsync();

            var consumoVendDict = consumoVend
                .Where(x => x.SKU != "")
                .ToDictionary(x => x.SKU, x => x.Kg);

            // 5) Consumo por CANAL (solo si CEDIS)
            Dictionary<string, decimal> consumoCanalDict = new();
            if (usarCedis)
            {
                // OV por canal (regla MATRIZ usa ClienteSap.U_CANAL; sucursales usan Series.Canal)
                var consumoOVCanal = await (
                    from o in _context.OrdenVenta.AsNoTracking()
                    join cli in _context.ClienteSap.AsNoTracking() on o.Cliente equals cli.Cliente
                    join s in _context.Series.AsNoTracking() on o.Serie equals s.NombreSerie
                    join op in _context.OrdenVentaProducto.AsNoTracking() on o.Id equals op.PedidoId
                    join ser in _context.Series.AsNoTracking() on o.Serie equals ser.NombreSerie
                    where o.FechaEntrega.Month == mes
                          && o.FechaEntrega.Year == anio
                          && o.Estatus != 0
                          && (op.Eliminado == null || op.Eliminado == false)
                          && (
                                ((s.Sucursal ?? "") == "MATRIZ" && (cli.U_CANAL ?? "") == canalUp)
                                || ((s.Sucursal ?? "") != "MATRIZ" && (s.Canal ?? "") == canalUp)
                             )
                             && ser.Sucursal == "MATRIZ"
                    group op by op.ProductoCodigo into g
                    select new
                    {
                        SKU = Norm(g.Key),
                        Kg = g.Sum(x => (decimal?)x.Peso) ?? 0m
                    }
                ).ToListAsync();

                // Transferencias por canal
                var consumoTrCanal = await (
                    from t in _context.Transferencias.AsNoTracking()
                    where t.FechaSolicitud.HasValue
                          && t.FechaSolicitud.Value.Month == mes
                          && t.FechaSolicitud.Value.Year == anio
                    join td in _context.TransferenciaDetalles.AsNoTracking() on t.Id equals td.TransferenciaId
                    join s in _context.Series.AsNoTracking() on t.Sucursal equals s.Sucursal
                    where (s.Canal ?? "") == canalUp
                    && t.Estatus != 0
                    group td by td.ProductoCodigo into g
                    select new
                    {
                        SKU = Norm(g.Key),
                        Kg = g.Sum(x => (decimal?)x.CantidadKg) ?? 0m
                    }
                ).ToListAsync();

                consumoCanalDict = consumoOVCanal
                    .Concat(consumoTrCanal)
                    .Where(x => x.SKU != "")
                    .GroupBy(x => x.SKU)
                    .ToDictionary(g => g.Key, g => g.Sum(v => v.Kg));
            }

            // 6) SKUs a regresar (LEFT “lógico”: CEDIS + los NO CEDIS/fallback vendedor)
            var skus = new HashSet<string>(vendDict.Keys);
            if (usarCedis) skus.UnionWith(cedisDict.Keys);

            var respuesta = skus
                .OrderBy(x => x)
                .Select(sku =>
                {
                    bool esSkuCedis = usarCedis && cedisDict.ContainsKey(sku);

                    decimal presupuestoAsignado = esSkuCedis
                        ? cedisDict[sku]
                        : (vendDict.TryGetValue(sku, out var pv) ? pv : 0m);

                    decimal kgPedidosMes = esSkuCedis
                        ? (consumoCanalDict.TryGetValue(sku, out var kgC) ? kgC : 0m)
                        : (consumoVendDict.TryGetValue(sku, out var kgV) ? kgV : 0m);

                    return new
                    {
                        productoCodigo = sku,
                        presupuestoAsignado,
                        kgPedidosMes,
                        presupuestoDisponible = presupuestoAsignado - kgPedidosMes,
                        origen = esSkuCedis ? "CEDIS" : "VENDEDOR",
                        canal = canalUp
                    };
                })
                .ToList();

            return Json(respuesta);
        }


        // Obtiene el modo y los permisos para la vista
        [HttpGet]
        [RevisarPermiso("MODO_PRESUPUESTO", "LEER")]
        public async Task<IActionResult> ObtenerModoPresupuesto()
        {
            var login = (User?.Identity?.Name ?? "").Trim();

            var permiso = await (
                from u in _context.UsuarioSQL
                join p in _context.Perfiles on u.PerfilId equals p.Id
                join ppm in _context.PerfilPermisoModulo on p.Id equals ppm.PerfilId
                join m in _context.ModulosSistema on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == "MODO_PRESUPUESTO"
                      && ppm.Activo
                      && m.Activo
                select new { ppm.PuedeLeer, ppm.PuedeEscribir, ppm.PuedeEliminar }
            ).FirstOrDefaultAsync();

            return Json(new
            {
                login = login,
                modo = GetModoPresupuestoActual(),
                puedeLeer = permiso?.PuedeLeer ?? false,
                puedeEscribir = permiso?.PuedeEscribir ?? false,
                puedeEliminar = permiso?.PuedeEliminar ?? false
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RevisarPermiso("MODO_PRESUPUESTO", "ESCRIBIR")]
        public IActionResult GuardarModoPresupuesto(string modo)
        {
            var m = NormalizarModo(modo);

            HttpContext.Session.SetString(SESSION_MODO_PRES, m);
            HttpContext.Session.SetString(SessionKeyModoPresupuesto, m);

            return Json(new { ok = true, modo = m });
        }






































        // ==================================================
        // OBTENER FECHA DE SOLICITUD DE UNA TRANSFERENCIA
        // Tabla: dbo.Transferencias
        // ==================================================
        [HttpGet("Transferencias/ObtenerFechaSolicitudTransf")]
        public async Task<IActionResult> ObtenerFechaSolicitudTransf(string folio)
        {
            if (string.IsNullOrWhiteSpace(folio))
                return BadRequest("El folio de la transferencia es obligatorio.");

            var cs = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, "No se encontró la cadena de conexión 'DefaultConnection'.");

            var sql = @"
SELECT TOP (1)
    FechaSolicitud
FROM dbo.Transferencias WITH (NOLOCK)
WHERE Consecutivo = @folio;
";

            try
            {
                using var con = new SqlConnection(cs);
                await con.OpenAsync();

                using var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@folio", folio.Trim());

                var escalar = await cmd.ExecuteScalarAsync();

                if (escalar == null || escalar == DBNull.Value)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "No se encontró fecha de solicitud para la transferencia."
                    });
                }

                var fecha = (DateTime)escalar;

                return Json(new
                {
                    ok = true,
                    fecha = fecha
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    error = "Error al consultar la fecha de solicitud",
                    detalle = ex.Message
                });
            }
        }



        // ==================================================
        // OBTENER FECHA DE ENTREGA DE UNA OV DESDE LOCAL
        // Tabla: dbo.OrdenVenta  (ajusta al nombre real)
        // ==================================================
        [HttpGet("Comercial/ObtenerFechaEntregaOV")]
        public async Task<IActionResult> ObtenerFechaEntregaOV(string folio)
        {
            if (string.IsNullOrWhiteSpace(folio))
                return BadRequest("El folio de la orden es obligatorio.");

            // Cadena de conexión
            var cs = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, "No se encontró la cadena de conexión 'DefaultConnection'.");

            // ⚠️ Ajusta nombres de tabla y columnas:
            //  - dbo.OrdenVenta         -> tu tabla real
            //  - Folio                  -> columna del folio / OV
            //  - FechaEntrega           -> columna de fecha de entrega
            var sql = @"
        SELECT TOP (1)
            FechaEntrega
        FROM dbo.OrdenVenta WITH (NOLOCK)
        WHERE Consecutivo = @folio;";

            try
            {
                using var con = new SqlConnection(cs);
                await con.OpenAsync();

                using var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@folio", folio.Trim());

                var escalar = await cmd.ExecuteScalarAsync();

                if (escalar == null || escalar == DBNull.Value)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "No se encontró la fecha de entrega para la orden."
                    });
                }

                var fecha = (DateTime)escalar;

                // Devuelves la fecha, el JS ya la formatea
                return Json(new
                {
                    ok = true,
                    fechaEntrega = fecha
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    error = "Error al consultar la fecha de entrega",
                    detalle = ex.Message
                });
            }
        }


        // ==============================
        // SUBPEDIDOS POR OV (GET)
        // ==============================
        //https://localhost:7171/Comercial/SubpedidosPorOV2?consecutivoOV=OV-00001015

        [HttpGet("Comercial/SubpedidosPorOV2")]
        public async Task<IActionResult> SubpedidosPorOV2(string consecutivoOV)
        {
            if (string.IsNullOrWhiteSpace(consecutivoOV))
                return Json(Array.Empty<object>());

            consecutivoOV = consecutivoOV.Trim();

            var lista = await _context.Subpedidos
                .Where(s => s.ConsecutivoOV != null &&
                            s.ConsecutivoOV.Trim() == consecutivoOV)
                .Select(s => new
                {
                    s.Id,
                    s.ConsecutivoOV,
                    s.SubFolio,
                    s.Almacen,
                    s.TotalPeso,
                    s.TotalImporte,
                    s.DocumentoSAP,
                    s.U_DocMeat
                })
                .OrderBy(s => s.SubFolio)
                .ToListAsync();

            return Json(lista);
        }


        // ==============================
        // ACTUALIZAR U_DocMeat (POST)
        // ==============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubpedidoActualizarDocMeat(
            [FromBody] SubpedidoDocMeatUpdateVm model)
        {
            if (model == null || model.SubpedidoId <= 0)
                return BadRequest(new { ok = false, msg = "Datos inválidos." });

            var sub = await _context.Subpedidos
                .FirstOrDefaultAsync(s => s.Id == model.SubpedidoId);

            if (sub == null)
                return NotFound(new { ok = false, msg = "Subpedido no encontrado." });

            // asignar nuevo U_DocMeat
            sub.U_DocMeat = string.IsNullOrWhiteSpace(model.U_DocMeat)
                ? null
                : model.U_DocMeat!.Trim();

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception)
            {
                return StatusCode(500, new { ok = false, msg = "Error al guardar en base de datos." });
            }

            return Json(new
            {
                ok = true,
                u_DocMeat = sub.U_DocMeat
            });
        }





































        // ============================================================
        // LISTA DE VENDEDORES (SIN CEDIS)
        // GET: /Comercial/ObtenerVendedores
        // ============================================================
        [HttpGet("Comercial/ObtenerVendedores")]
        public IActionResult ObtenerVendedores()
        {
            try
            {
                var vendedores = _context.ClienteSap
                    .Where(c => c.VendedorId != null && c.VendedorId > 0)
                    .GroupBy(c => new { c.VendedorId, c.VendedorNombre })
                    .Select(g => new
                    {
                        id = (int)g.Key.VendedorId,          // <- int
                        nombre = g.Key.VendedorNombre
                    })
                    .OrderBy(x => x.id)                     // <- ordenar por número
                                                            //.ThenBy(x => x.nombre)                // opcional
                    .ToList();

                return Json(vendedores);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al consultar vendedores", detalle = ex.Message });
            }
        }



        // ============================================================
        // 2) VENTAS AGRUPADAS POR SKU PARA UN VENDEDOR (últimos N meses)
        // GET: /Comercial/ObtenerPresupuestoPorVendedor?canal=CEDIS-MDA&vendedorId=12&meses=12
        // ============================================================
        [HttpGet("Comercial/ObtenerPresupuestoPorVendedor")]
        public async Task<IActionResult> ObtenerPresupuestoPorVendedor(int vendedorId, int meses = 12)
        {
            if (meses <= 0) meses = 12;
            if (vendedorId <= 0) return BadRequest("El vendedorId es obligatorio.");

            var hoy = DateTime.Today;
            var desdeBase = hoy.AddMonths(-(meses - 1));
            var desde = new DateTime(desdeBase.Year, desdeBase.Month, 1);

            var cs = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, "No se encontró la cadena de conexión 'DefaultConnection'.");

            var sql = @"
                   SELECT
                       l.sku                                        AS sku,
                       ISNULL(a.U_MASTER, 'SIN_MASTER')             AS master,
                       SUM(l.Kilos)                                 AS totalKilos,
                       CAST(DATEFROMPARTS(YEAR(l.doc_date), MONTH(l.doc_date), 1) AS date) AS fecha
                   FROM dbo.sap_invoice_lines AS l WITH (NOLOCK)
                   INNER JOIN dbo.ClienteSap   AS c WITH (NOLOCK) ON c.Cliente = l.card_code
                   LEFT  JOIN dbo.ArticuloSap  AS a WITH (NOLOCK) ON a.ProductoCodigo = l.sku
                   WHERE c.VendedorId = @vendedorId
                     AND l.doc_date >= @desde
                   GROUP BY
                       l.sku,
                       a.U_MASTER,
                       YEAR(l.doc_date),
                       MONTH(l.doc_date)
                   ORDER BY fecha, l.sku
                   OPTION (RECOMPILE);";

            var lista = new List<object>();
            try
            {
                using var con = new SqlConnection(cs);
                await con.OpenAsync();

                using var cmd = new SqlCommand(sql, con);
                cmd.CommandTimeout = 120;
                cmd.Parameters.AddWithValue("@vendedorId", vendedorId);
                cmd.Parameters.AddWithValue("@desde", desde);

                using var rd = await cmd.ExecuteReaderAsync();
                int iSku = rd.GetOrdinal("sku");
                int iMaster = rd.GetOrdinal("master");
                int iKilos = rd.GetOrdinal("totalKilos");
                int iFecha = rd.GetOrdinal("fecha");

                while (await rd.ReadAsync())
                {
                    var sku = rd.IsDBNull(iSku) ? "" : rd.GetString(iSku);
                    var master = rd.IsDBNull(iMaster) ? "" : rd.GetString(iMaster);
                    var kilos = rd.IsDBNull(iKilos) ? 0m : rd.GetDecimal(iKilos);
                    var fecha = rd.GetDateTime(iFecha);

                    lista.Add(new
                    {
                        sku,
                        master = string.IsNullOrWhiteSpace(master) ? "SIN_MASTER" : master,
                        totalKilos = Math.Round(kilos, 2, MidpointRounding.AwayFromZero),
                        fecha
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al consultar ventas por Vendedor", detalle = ex.Message });
            }

            return Json(lista);
        }



        // ============================================================
        // 3) GUARDAR PRESUPUESTO POR VENDEDOR
        // POST: /Comercial/GuardarPresupuestoVendedor
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarPresupuestoVendedor([FromForm] PresupuestoVendedorSaveVM model)
        {
            bool isAjax = string.Equals(
                Request.Headers["X-Requested-With"].ToString(),
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase
            );

            // VendedorId (int) - rescate desde form si viene 0
            if (model.VendedorId <= 0)
            {
                var vendStr = Request.Form["VendedorId"].ToString();
                if (string.IsNullOrWhiteSpace(vendStr))
                    vendStr = Request.Form["Vendedor"].ToString();

                if (!int.TryParse(vendStr, out var vendParsed))
                    vendParsed = 0;

                model.VendedorId = vendParsed;
            }

            // Periodo
            if (model.Mes == 0 && int.TryParse(Request.Form["Mes"], out var mesPost)) model.Mes = mesPost;
            if (model.Anio == 0 && int.TryParse(Request.Form["Anio"], out var anioPost)) model.Anio = anioPost;

            // ===== VALIDACIONES =====
            if (model.VendedorId <= 0)
                return isAjax ? BadRequest(new { ok = false, message = "Debes seleccionar un vendedor." })
                              : BadRequest("Debes seleccionar un vendedor.");

            if (model.Mes < 1 || model.Mes > 12 || model.Anio <= 0)
                return isAjax ? BadRequest(new { ok = false, message = $"Periodo inválido. (Mes={model.Mes}, Anio={model.Anio})" })
                              : BadRequest("Periodo inválido.");

            if (model.Items == null || model.Items.Count == 0)
                return isAjax ? BadRequest(new { ok = false, message = "No hay renglones para guardar." })
                              : BadRequest("No hay renglones para guardar.");

            if (model.Items.Any(i => i.Presupuesto <= 0))
                return isAjax ? BadRequest(new { ok = false, message = "Hay presupuestos en 0. Corrige e intenta de nuevo." })
                              : BadRequest("Hay presupuestos en 0.");

            // Normalizar items por SKU
            var items = model.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.ProductoCodigo))
                .GroupBy(i => i.ProductoCodigo.Trim().ToUpper())
                .Select(g =>
                {
                    var first = g.First();
                    return new PresupuestoVendedorItemVM
                    {
                        ProductoCodigo = g.Key,
                        Master = (first.Master ?? "SIN_MASTER").Trim(),
                        Objetivo = first.Objetivo,
                        Presupuesto = first.Presupuesto,
                        Comentario = first.Comentario
                    };
                })
                .ToList();

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var vendedorId = model.VendedorId;
                var mes = model.Mes;
                var anio = model.Anio;

                var skus = items.Select(i => i.ProductoCodigo).ToList();

                // Evitar duplicados (sin Canal)
                var existentes = await _context.PresupuestoVendedor
                    .Where(p => p.VendedorId == vendedorId
                             && p.Anio == anio
                             && p.Mes == mes
                             && skus.Contains(p.ProductoCodigo))
                    .Select(p => p.ProductoCodigo)
                    .ToListAsync();

                if (existentes.Any())
                {
                    var msg = $"Ya existen SKUs para Vendedor {vendedorId} / {mes:00}-{anio}: {string.Join(", ", existentes)}";
                    return isAjax ? BadRequest(new { ok = false, message = msg }) : BadRequest(msg);
                }

                var ahora = DateTime.UtcNow;
                var usuario = User?.Identity?.Name ?? "web";

                var rows = items.Select(i => new PresupuestoVendedor
                {
                    VendedorId = vendedorId,
                    Anio = anio,
                    Mes = mes,
                    ProductoCodigo = i.ProductoCodigo,
                    Master = i.Master ?? "SIN_MASTER",
                    Objetivo = i.Objetivo,
                    PresupuestoAsignado = i.Presupuesto,
                    Comentario = i.Comentario,
                    CreadoPor = usuario,
                    CreadoEn = ahora
                }).ToList();

                _context.PresupuestoVendedor.AddRange(rows);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { ok = true, message = $"Se guardaron {rows.Count} renglones.", rows = rows.Count });
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                var msg = "Error al guardar presupuesto (DB): " + (ex.GetBaseException()?.Message ?? ex.Message);
                return StatusCode(500, new { ok = false, message = msg });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { ok = false, message = "Error inesperado: " + ex.Message });
            }
        }


        // ============================================================
        // DEMANDA vs INVENTARIO (RESUMEN POR SKU)
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> DemandaInventario(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] List<string>? almacenesId
        )
        {
            var d1 = desde.Date;
            var d2 = hasta.Date;
            if (d2 < d1) return BadRequest("El rango de fechas es inválido.");

            var almacenesList = (almacenesId ?? new List<string>())
                .Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sinFiltro = almacenesList.Count == 0;
            var almacenesCsv = string.Join(",", almacenesList);

            Console.WriteLine($"[DemandaInventario] almacenesId={almacenesList.Count} => '{almacenesCsv}'");

            var sql = @"
;WITH
/* =========================================================
   1) DEMANDA CONFIRMADA HOY (AFECTA INVENTARIO)
      -> PedidoVenta
      -> PedidosTransferencia
   ========================================================= */
DemandaConfirmada AS (

    -- PEDIDOS DE VENTA CONFIRMADOS HOY
    SELECT
        pvp.ProductoCodigo,
        SUM(CAST(pvp.Cajas AS decimal(18,2)))     AS Cajas,
        SUM(CAST(pvp.KilosCaja AS decimal(18,3))) AS Kg
    FROM PedidoVenta pv
    INNER JOIN PedidoVentaProducto pvp
        ON pv.Id = pvp.PedidoVentaId
    WHERE CAST(pv.FechaGestion AS date) = CAST(GETDATE() AS date)
    GROUP BY pvp.ProductoCodigo

    UNION ALL

    -- TRANSFERENCIAS CONFIRMADAS HOY
    SELECT
        ptd.ProductoCodigo,
        SUM(CAST(ptd.Cajas AS decimal(18,2)))      AS Cajas,
        SUM(CAST(ptd.CantidadKg AS decimal(18,3))) AS Kg
    FROM PedidosTransferencia pt
    INNER JOIN PedidosTransferenciaDetalle ptd
        ON pt.Id = ptd.PedidoTransferenciaId
    WHERE CAST(pt.FechaSolicitud AS date) = CAST(GETDATE() AS date)
    GROUP BY ptd.ProductoCodigo
),

DemandaConfirmadaAgr AS (
    SELECT
        ProductoCodigo,
        SUM(Cajas) AS CajasComprometidas,
        SUM(Kg)    AS KgComprometidos
    FROM DemandaConfirmada
    GROUP BY ProductoCodigo
),

/* =========================================================
   2) DEMANDA FUTURA (SOLICITADO / PLANEACIÓN)
      -> OrdenVenta
      -> Transferencias
      (estatus != 0)
   ========================================================= */
DemandaSolicitada AS (

    -- ORDENES DE VENTA
    SELECT
        b.ProductoCodigo,
        b.ProductoNombre,
        CAST(b.Cajas AS decimal(18,2)) AS Cajas,
        CAST(b.Peso  AS decimal(18,3)) AS Kg
    FROM OrdenVenta a
    INNER JOIN OrdenVentaProducto b
        ON a.Id = b.PedidoId
    WHERE a.Estatus <> 0
      AND b.Eliminado = 0
      AND CAST(a.FechaEntrega AS date)
          BETWEEN @Desde AND @Hasta

    UNION ALL

    -- TRANSFERENCIAS SOLICITADAS
    SELECT
        c.ProductoCodigo,
        c.ProductoNombre,
        CAST(b.Cajas      AS decimal(18,2)) AS Cajas,
        CAST(b.CantidadKg AS decimal(18,3)) AS Kg
    FROM Transferencias a
    INNER JOIN TransferenciaDetalles b
        ON a.Id = b.TransferenciaId
    INNER JOIN ArticuloSap c
        ON b.ProductoCodigo = c.ProductoCodigo
    WHERE a.Estatus <> 0
      AND CAST(a.FechaSolicitud AS date)
          BETWEEN @Desde AND @Hasta
),

DemandaSolicitadaAgr AS (
    SELECT
        ProductoCodigo,
        MAX(ProductoNombre) AS ProductoNombre,
        SUM(Cajas) AS CajasSolicitadas,
        SUM(Kg)    AS KgSolicitados
    FROM DemandaSolicitada
    GROUP BY ProductoCodigo
),

/* =========================================================
   3) INVENTARIO (POR ALMACÉN)
   ========================================================= */
AlmacenesSel AS (
    SELECT DISTINCT LTRIM(RTRIM([value])) AS AlmacenId
    FROM STRING_SPLIT(@Almacenes, ',')
    WHERE LTRIM(RTRIM([value])) <> ''
),

InventarioPorSKU AS (
    SELECT
        inv.ProductoCodigo,
        SUM(CAST(inv.Cajas AS decimal(18,2))) AS CajasInventario,
        SUM(CAST(inv.Kg    AS decimal(18,3))) AS KgInventario
    FROM InventarioSigo inv
    WHERE @SinFiltro = 1
       OR EXISTS (
            SELECT 1
            FROM AlmacenesSel s
            WHERE s.AlmacenId COLLATE Latin1_General_CI_AI
                = LTRIM(RTRIM(CONVERT(NVARCHAR(50), inv.AlmacenId)))
                  COLLATE Latin1_General_CI_AI
        )
    GROUP BY inv.ProductoCodigo
)

/* =========================================================
   4) RESULTADO FINAL
   ========================================================= */
SELECT
    s.ProductoCodigo AS SKU,
    s.ProductoNombre AS Producto,

    -- SOLICITADO
    s.CajasSolicitadas,
     s.KgSolicitados AS KilosSolicitados,

    -- DISPONIBLE REAL (YA DESCONTANDO HOY)
    ISNULL(i.CajasInventario,0) - ISNULL(c.CajasComprometidas,0) AS CajasDisponibles,
    ISNULL(i.KgInventario,0)    - ISNULL(c.KgComprometidos,0)    AS KilosDisponibles,

    -- BALANCE CONTRA LO SOLICITADO
    (ISNULL(i.CajasInventario,0) - ISNULL(c.CajasComprometidas,0))
        - s.CajasSolicitadas AS CajasConfirmadas,

    (ISNULL(i.KgInventario,0) - ISNULL(c.KgComprometidos,0))
        - s.KgSolicitados AS KilosConfirmados

FROM DemandaSolicitadaAgr s
LEFT JOIN InventarioPorSKU i
    ON i.ProductoCodigo = s.ProductoCodigo
LEFT JOIN DemandaConfirmadaAgr c
    ON c.ProductoCodigo = s.ProductoCodigo
ORDER BY CajasConfirmadas ASC, SKU;
";

            await using var cn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = d1;
            cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = d2;
            cmd.Parameters.Add("@SinFiltro", SqlDbType.Bit).Value = sinFiltro;
            cmd.Parameters.Add("@Almacenes", SqlDbType.NVarChar, -1).Value = almacenesCsv;

            var list = new List<DemandaInventarioDto>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                decimal GetDec(string col) => rd[col] == DBNull.Value ? 0m : Convert.ToDecimal(rd[col]);

                list.Add(new DemandaInventarioDto
                {
                    SKU = rd["SKU"]?.ToString() ?? "",
                    Producto = rd["Producto"]?.ToString() ?? "",
                    CajasSolicitadas = GetDec("CajasSolicitadas"),
                    KilosSolicitados = GetDec("KilosSolicitados"),
                    CajasDisponibles = GetDec("CajasDisponibles"),
                    KilosDisponibles = GetDec("KilosDisponibles"),
                    CajasConfirmadas = GetDec("CajasConfirmadas"),
                    KilosConfirmados = GetDec("KilosConfirmados"),
                });
            }

            return Ok(list);
        }





        // ============================================================
        // DEMANDA DETALLE (OV + TRANSFERENCIAS) -> JSON PARA LA VISTA
        // Endpoint que tu JS llama: /Comercial/DemandaDetalle?desde=...&hasta=...
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> DemandaDetalle([FromQuery] DateTime desde, [FromQuery] DateTime hasta)
        {
            var d1 = desde.Date;
            var d2 = hasta.Date;
            if (d2 < d1) return BadRequest("El rango de fechas es inválido.");

            var list = new List<DemandaDetalleDto>();

            var sql = @"
SELECT 'OV' AS Origen,
       d.Nombrecliente  AS Cliente,
       a.Ruta,
       b.ProductoCodigo,
       b.ProductoNombre,
       CAST(b.Cajas AS decimal(18,2)) AS Cajas,
       CAST(b.Peso  AS decimal(18,3)) AS Kg,
       CAST(a.FechaEntrega AS date)   AS FechaEmbarcar
FROM OrdenVenta a
INNER JOIN OrdenVentaProducto b ON a.Id = b.PedidoId
INNER JOIN ArticuloSap c        ON b.ProductoCodigo = c.ProductoCodigo
INNER JOIN ClienteSap d         ON a.Cliente = d.Cliente
WHERE CAST(a.FechaEntrega AS date) >= @Desde and a.estatus  in (1,2,3)
  AND CAST(a.FechaEntrega AS date) <  DATEADD(DAY,1,@Hasta)

UNION ALL

SELECT 'TR' AS Origen,
       a.Sucursal       AS Cliente,
        a.Sucursal as Ruta,
       c.ProductoCodigo,
       c.ProductoNombre,
       CAST(b.Cajas      AS decimal(18,2)) AS Cajas,
       CAST(b.CantidadKg AS decimal(18,3)) AS Kg,
       CAST(a.FechaSolicitud AS date)      AS FechaEmbarcar
FROM Transferencias a
INNER JOIN TransferenciaDetalles b ON a.Id = b.TransferenciaId
INNER JOIN ArticuloSap c           ON b.ProductoCodigo = c.ProductoCodigo
WHERE CAST(a.FechaSolicitud AS date) >= @Desde and a.estatus  in (1,2,3)
  AND CAST(a.FechaSolicitud AS date) <  DATEADD(DAY,1,@Hasta)

ORDER BY FechaEmbarcar, Cliente, ProductoCodigo;";

            await using var cn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = d1;
            cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = d2;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                decimal GetDec(string col)
                {
                    var o = rd[col];
                    return (o == DBNull.Value) ? 0m : Convert.ToDecimal(o);
                }
                DateTime GetDate(string col)
                {
                    var o = rd[col];
                    return (o == DBNull.Value) ? DateTime.MinValue : Convert.ToDateTime(o);
                }

                list.Add(new DemandaDetalleDto
                {
                    Origen = rd["Origen"]?.ToString() ?? "",
                    Cliente = rd["Cliente"]?.ToString() ?? "",
                    Ruta = rd["Ruta"]?.ToString() ?? "",              // ✅ AQUÍ
                    ProductoCodigo = rd["ProductoCodigo"]?.ToString() ?? "",
                    ProductoNombre = rd["ProductoNombre"]?.ToString() ?? "",
                    Cajas = GetDec("Cajas"),
                    Kg = GetDec("Kg"),
                    FechaEmbarcar = GetDate("FechaEmbarcar")
                });
            }

            return Ok(list);
        }


        // ============================================================
        // EXPORT CSV (OV + TRANSFERENCIAS)
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> ExportDemandaDetalle([FromQuery] DateTime desde, [FromQuery] DateTime hasta)
        {
            var d1 = desde.Date;
            var d2 = hasta.Date;
            if (d2 < d1) return BadRequest("Rango inválido.");

            var sql = @"
SELECT 'OV' AS Origen,
       d.Nombrecliente  AS Cliente,
       a.Ruta,
       b.ProductoCodigo,
       b.ProductoNombre,
       CAST(b.Cajas AS decimal(18,2)) AS Cajas,
       CAST(b.Peso  AS decimal(18,3)) AS Kg,
       CAST(a.FechaEntrega AS date)   AS FechaEmbarcar
FROM OrdenVenta a
INNER JOIN OrdenVentaProducto b ON a.Id = b.PedidoId
INNER JOIN ArticuloSap c        ON b.ProductoCodigo = c.ProductoCodigo
INNER JOIN ClienteSap d         ON a.Cliente = d.Cliente
WHERE CAST(a.FechaEntrega AS date) >= @Desde
  AND CAST(a.FechaEntrega AS date) <  DATEADD(DAY,1,@Hasta)

UNION ALL

SELECT 'TR' AS Origen,
       a.Sucursal       AS Cliente,
       a.Sucursal       AS Ruta,
       c.ProductoCodigo,
       c.ProductoNombre,
       CAST(b.Cajas      AS decimal(18,2)) AS Cajas,
       CAST(b.CantidadKg AS decimal(18,3)) AS Kg,
       CAST(a.FechaSolicitud AS date)      AS FechaEmbarcar
FROM Transferencias a
INNER JOIN TransferenciaDetalles b ON a.Id = b.TransferenciaId
INNER JOIN ArticuloSap c           ON b.ProductoCodigo = c.ProductoCodigo
WHERE CAST(a.FechaSolicitud AS date) >= @Desde
  AND CAST(a.FechaSolicitud AS date) <  DATEADD(DAY,1,@Hasta)

ORDER BY FechaEmbarcar, Cliente, ProductoCodigo;";

            var sb = new StringBuilder();
            sb.AppendLine("Origen,Cliente,Ruta,ProductoCodigo,ProductoNombre,Cajas,Kg,FechaEmbarcar");

            await using var cn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = d1;
            cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = d2;

            await using var rd = await cmd.ExecuteReaderAsync();

            string Q(string s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";

            decimal GetDec(string col)
            {
                var o = rd[col];
                return (o == DBNull.Value) ? 0m : Convert.ToDecimal(o);
            }

            DateTime GetDate(string col)
            {
                var o = rd[col];
                return (o == DBNull.Value) ? DateTime.MinValue : Convert.ToDateTime(o);
            }

            while (await rd.ReadAsync())
            {
                var origen = rd["Origen"]?.ToString() ?? "";
                var cliente = rd["Cliente"]?.ToString() ?? "";
                var ruta = rd["Ruta"]?.ToString() ?? "";                 // ✅ NUEVO
                var codigo = rd["ProductoCodigo"]?.ToString() ?? "";
                var nombre = rd["ProductoNombre"]?.ToString() ?? "";

                var cajas = GetDec("Cajas").ToString(System.Globalization.CultureInfo.InvariantCulture);
                var kg = GetDec("Kg").ToString(System.Globalization.CultureInfo.InvariantCulture);

                var fechaDt = GetDate("FechaEmbarcar");
                var fecha = (fechaDt == DateTime.MinValue) ? "" : fechaDt.ToString("yyyy-MM-dd");

                // ✅ Ojo: aquí ya va Ruta después de Cliente
                sb.AppendLine($"{Q(origen)},{Q(cliente)},{Q(ruta)},{Q(codigo)},{Q(nombre)},{cajas},{kg},{Q(fecha)}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"Demanda_Detalle_{d1:yyyyMMdd}_{d2:yyyyMMdd}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }



        // ============================================================
        // EXPORT CSV (DEMANDA vs INVENTARIO) - RESUMEN POR SKU
        // Endpoint: /Comercial/ExportDemandaInventario?desde=...&hasta=...
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> ExportDemandaInventario([FromQuery] DateTime desde, [FromQuery] DateTime hasta)
        {
            var d1 = desde.Date;
            var d2 = hasta.Date;
            if (d2 < d1) return BadRequest("Rango inválido.");

            var sql = @"
;WITH Demanda AS (
    SELECT
        d.Nombrecliente AS Cliente,
        b.ProductoCodigo,
        b.ProductoNombre,
        CAST(b.Cajas AS decimal(18,2)) AS CajasSolicitadas,
        CAST(b.Peso  AS decimal(18,3)) AS KgSolicitados,
        CAST(a.FechaEntrega AS date)   AS FechaEmbarcar
    FROM OrdenVenta a
    INNER JOIN OrdenVentaProducto b ON a.Id = b.PedidoId
    INNER JOIN ArticuloSap c        ON b.ProductoCodigo = c.ProductoCodigo
    INNER JOIN ClienteSap d         ON a.Cliente = d.Cliente

    UNION ALL

    SELECT
        a.Sucursal AS Cliente,
        c.ProductoCodigo,
        c.ProductoNombre,
        CAST(b.Cajas      AS decimal(18,2)) AS CajasSolicitadas,
        CAST(b.CantidadKg AS decimal(18,3)) AS KgSolicitados,
        CAST(a.FechaSolicitud AS date)      AS FechaEmbarcar
    FROM Transferencias a
    INNER JOIN TransferenciaDetalles b ON a.Id = b.TransferenciaId
    INNER JOIN ArticuloSap c           ON b.ProductoCodigo = c.ProductoCodigo
),
DemandaPorSKU AS (
    SELECT
        ProductoCodigo,
        MAX(ProductoNombre)   AS ProductoNombre,
        SUM(CajasSolicitadas) AS CajasSolicitadas,
        SUM(KgSolicitados)    AS KilosSolicitados
    FROM Demanda
    WHERE FechaEmbarcar >= @Desde
      AND FechaEmbarcar <  DATEADD(DAY,1,@Hasta)
    GROUP BY ProductoCodigo
),
InventarioPorSKU AS (
    SELECT
        ProductoCodigo,
        SUM(CAST(Cajas AS decimal(18,2))) AS CajasDisponibles,
        SUM(CAST(Kg    AS decimal(18,3))) AS KilosDisponibles
    FROM InventarioSigo
    GROUP BY ProductoCodigo
)
SELECT
    d.ProductoCodigo AS SKU,
    d.ProductoNombre AS Producto,
    d.CajasSolicitadas,
    d.KilosSolicitados,
    ISNULL(i.CajasDisponibles, 0) AS CajasDisponibles,
    ISNULL(i.KilosDisponibles, 0) AS KilosDisponibles,
    (ISNULL(i.CajasDisponibles, 0) - d.CajasSolicitadas) AS CajasConfirmadas,
    (ISNULL(i.KilosDisponibles, 0) - d.KilosSolicitados) AS KilosConfirmados
FROM DemandaPorSKU d
LEFT JOIN InventarioPorSKU i ON i.ProductoCodigo = d.ProductoCodigo
ORDER BY CajasConfirmadas ASC, d.ProductoCodigo;";

            var sb = new StringBuilder();
            sb.AppendLine("SKU,Producto,CajasSolicitadas,KilosSolicitados,CajasDisponibles,KilosDisponibles,CajasConfirmadas,KilosConfirmados");

            await using var cn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = d1;
            cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = d2;

            await using var rd = await cmd.ExecuteReaderAsync();

            string Q(string s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
            string D(object o) => ((o == DBNull.Value) ? 0m : Convert.ToDecimal(o))
                                  .ToString(System.Globalization.CultureInfo.InvariantCulture);

            while (await rd.ReadAsync())
            {
                var sku = rd["SKU"]?.ToString() ?? "";
                var prod = rd["Producto"]?.ToString() ?? "";

                var cajasSol = D(rd["CajasSolicitadas"]);
                var kgSol = D(rd["KilosSolicitados"]);
                var cajasDis = D(rd["CajasDisponibles"]);
                var kgDis = D(rd["KilosDisponibles"]);
                var cajasCon = D(rd["CajasConfirmadas"]);
                var kgCon = D(rd["KilosConfirmados"]);

                sb.AppendLine($"{Q(sku)},{Q(prod)},{cajasSol},{kgSol},{cajasDis},{kgDis},{cajasCon},{kgCon}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"Demanda_Inventario_{d1:yyyyMMdd}_{d2:yyyyMMdd}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }


        [HttpGet]
        public async Task<IActionResult> AlmacenesInventario()
        {
            var list = new List<AlmacenDto>();

            var sql = @"          

SELECT DISTINCT
       AlmacenId,
       Almacen
FROM InventarioSigo
WHERE NULLIF(LTRIM(RTRIM(AlmacenId)), '') IS NOT NULL
  AND AlmacenId NOT IN (
    '4','TIFCAN','TIFPIE','TIFCA1','TIFCA2','TIFCA3','TIFCA4','TIFCA5','TIFCA6',
    'TIFVC','TM','MT','PTSUB','FCDMX','FM','FMX','FMM','FTJ','1','TIFCAL','VR',
    'TIFPRO','TIFPC','8','9','TRES','CINCO','SEIS','SIETE','TIFPRV','CE','TE','TTN','TMX','FSLW'
  )
ORDER BY Almacen;
";

            await using var cn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new AlmacenDto
                {
                    AlmacenId = rd["AlmacenId"]?.ToString() ?? "",
                    Almacen = rd["Almacen"]?.ToString() ?? ""
                });
            }

            return Ok(list);
        }




        [HttpGet]
        public async Task<IActionResult> AdminListPresupuestos(
       string tipo, int mes, int anio,
       string sku = null, string canal = null, int? vendedorId = null, string cliente = null)
        {
            if (string.IsNullOrWhiteSpace(tipo)) return Json(new { ok = false, message = "Falta tipo" });
            if (mes < 1 || mes > 12) return Json(new { ok = false, message = "Mes inválido" });
            if (anio < 2020) return Json(new { ok = false, message = "Año inválido" });

            // ✅ Normaliza: "" => null (para que NO filtre)
            tipo = tipo.Trim().ToUpperInvariant();
            sku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
            canal = string.IsNullOrWhiteSpace(canal) ? null : canal.Trim();
            cliente = string.IsNullOrWhiteSpace(cliente) ? null : cliente.Trim();

            // ✅ log rápido (si no tienes ILogger, usa Console)
            Console.WriteLine($"[ADMINLIST] tipo={tipo} mes={mes} anio={anio} sku={(sku ?? "NULL")} canal={(canal ?? "NULL")} vendedorId={(vendedorId?.ToString() ?? "NULL")} cliente={(cliente ?? "NULL")}");

            var rows = await _presAdmin.Listar(tipo, mes, anio, sku, canal, vendedorId, cliente);

            Console.WriteLine($"[ADMINLIST] rows={rows?.Count() ?? 0}");

            return Json(new { ok = true, rows });
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminDeleteByIds(AdminDeleteByIdsRequest req)
        {
            if (req?.RowIds == null || req.RowIds.Count == 0)
                return Json(new { ok = false, message = "No hay filas para eliminar." });

            var user = User?.Identity?.Name ?? "unknown";
            var deleted = await _presAdmin.EliminarPorIds(req.Tipo, req.RowIds, user, req.Reason);
            return Json(new { ok = true, deleted });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminDeleteByFilter(AdminDeleteByFilterRequest req)
        {
            if (req == null) return Json(new { ok = false, message = "Request inválido" });

            var user = User?.Identity?.Name ?? "unknown";
            var deleted = await _presAdmin.EliminarPorFiltro(req, user);
            return Json(new { ok = true, deleted });
        }

        [HttpPost]
        public async Task<IActionResult> SincronizarDireccionesClientes()
        {
            try
            {
                var totalCambios =
                    await _direccionesSync.SincronizarDireccionesClientesDesdeSapAsync();

                return Json(new
                {
                    ok = true,
                    cambios = totalCambios
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }


        [HttpGet("Comercial/ObtenerDireccionesCliente")]
        public async Task<IActionResult> ObtenerDireccionesLocal(string cardCode)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return Json(new List<object>());

            var direcciones = await _context.DireccionesCliente
                .Where(d => d.Cliente == cardCode)   // ✅ FILTRO POR CLIENTE
                .Select(d => new
                {
                    value = d.AliasDireccion,         // lo que se guarda
                    text = d.AliasDireccion          // lo que se muestra
                })
                .OrderBy(d => d.text)
                .ToListAsync();

            return Json(direcciones);
        }

        [HttpGet]
        public async Task<IActionResult> SeriesCatalogo(CancellationToken ct = default)
        {
            try
            {
                var query = _context.Series
                    .AsNoTracking()
                    .AsQueryable();

                if (!UsuarioPuedeVerTodasLasSeries())
                {
                    var idsPermitidos = await ObtenerSeriesIdsUsuarioActualAsync(ct);

                    if (idsPermitidos == null || !idsPermitidos.Any())
                        return Json(new List<object>());

                    query = query.Where(s => idsPermitidos.Contains(s.Id));
                }

                var series = await query
                    .OrderBy(s => s.NombreSerie)
                    .Select(s => new
                    {
                        id = s.Id,
                        nombre = s.NombreSerie
                    })
                    .ToListAsync(ct);

                return Json(series);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    msg = ex.GetBaseException().Message
                });
            }
        }

        private int ObtenerUsuarioIdActual(SqlConnection cn)
        {
            var usuario = User.Identity?.Name ?? "";

            if (int.TryParse(usuario, out var idDirecto))
                return idDirecto;

            using var cmd = new SqlCommand(@"
        SELECT TOP 1 Id
        FROM Usuarios
        WHERE UserName = @Usuario
           OR Email = @Usuario
    ", cn);

            cmd.Parameters.AddWithValue("@Usuario", usuario);

            var result = cmd.ExecuteScalar();

            if (result == null || result == DBNull.Value)
                return 0;

            return Convert.ToInt32(result);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarSerieOV(int ordenVentaId, int serieId, CancellationToken ct = default)
        {
            try
            {
                if (ordenVentaId <= 0 || serieId <= 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = "Datos inválidos."
                    });
                }

                if (!UsuarioPuedeVerTodasLasSeries())
                {
                    var idsPermitidos = await ObtenerSeriesIdsUsuarioActualAsync(ct);

                    if (idsPermitidos == null || !idsPermitidos.Contains(serieId))
                    {
                        return StatusCode(403, new
                        {
                            ok = false,
                            msg = "No tienes permiso para asignar esta serie."
                        });
                    }
                }

                var serieNombre = await _context.Series
                    .AsNoTracking()
                    .Where(s => s.Id == serieId)
                    .Select(s => s.NombreSerie)
                    .FirstOrDefaultAsync(ct);

                if (string.IsNullOrWhiteSpace(serieNombre))
                {
                    return NotFound(new
                    {
                        ok = false,
                        msg = "La serie seleccionada no existe."
                    });
                }

                var orden = await _context.OrdenVenta
                    .FirstOrDefaultAsync(o => o.Id == ordenVentaId, ct);

                if (orden == null)
                {
                    return NotFound(new
                    {
                        ok = false,
                        msg = "No se encontró la orden de venta."
                    });
                }

                orden.Serie = serieNombre.Trim();

                await _context.SaveChangesAsync(ct);

                return Json(new
                {
                    ok = true,
                    serieNombre = orden.Serie
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    msg = ex.GetBaseException().Message
                });
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReporteVentasPresupuestoSPDetalle([FromBody] ReporteVentasPresupuestoRequest req)
        {
            if (req == null || req.Anio <= 0)
                return BadRequest(new { ok = false, message = "Request inválido" });

            var mesesValidos = (req.Meses ?? new List<int>())
                .Where(m => m >= 1 && m <= 12)
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            if (mesesValidos.Count == 0)
                mesesValidos = new List<int> { DateTime.Today.Month };

            var mesesCsv = string.Join(",", mesesValidos);

            var vendedores = (req.Vendedores ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            var clientes = (req.Clientes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            var rows = new List<ReporteVentasPresupuestoDetalleRow>();

            try
            {
                var cs = _context.Database.GetDbConnection().ConnectionString;

                await using var cn = new SqlConnection(cs);
                await cn.OpenAsync();

                // ✅ TU SP REAL
                await using var cmd = new SqlCommand("dbo.Reporte_Ventas_Presupuesto", cn);
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.Add("@Anio", SqlDbType.Int).Value = req.Anio;
                cmd.Parameters.Add("@Meses", SqlDbType.VarChar, 100).Value = mesesCsv;

                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    rows.Add(new ReporteVentasPresupuestoDetalleRow
                    {
                        ClienteId = rd["ClienteID"] == DBNull.Value ? "" : rd["ClienteID"].ToString(),
                        RazonSocial = rd["RazonSocial"] == DBNull.Value ? "" : rd["RazonSocial"].ToString(),
                        VendedorNombre = rd["VendedorNombre"] == DBNull.Value ? "" : rd["VendedorNombre"].ToString(),
                        Mes = rd["Mes"] == DBNull.Value ? "" : Convert.ToDateTime(rd["Mes"]).ToString("yyyy-MM"),
                        Venta = rd["Venta"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["Venta"]),
                        Presupuesto = rd["Presupuesto"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["Presupuesto"]),
                        Cump = rd["Cump"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["Cump"]),
                        Tend = rd["Tend"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["Tend"])
                    });
                }

                // ✅ filtros backend opcionales
                if (clientes.Count > 0)
                {
                    rows = rows.Where(r =>
                        clientes.Any(t =>
                            (r.ClienteId ?? "").ToUpperInvariant().Contains(t) ||
                            (r.RazonSocial ?? "").ToUpperInvariant().Contains(t)
                        )
                    ).ToList();
                }

                if (vendedores.Count > 0)
                {
                    rows = rows.Where(r =>
                        vendedores.Any(t =>
                            (r.VendedorNombre ?? "").ToUpperInvariant().Contains(t)
                        )
                    ).ToList();
                }

                return Json(rows);
            }
            catch (SqlException ex)
            {
                // Para que NO te reviente con 500 y veas el error real
                return BadRequest(new { ok = false, sqlNumber = ex.Number, message = ex.Message });
            }
        }



        [HttpGet]
        public IActionResult InventarioInicialAyer(int mes, int anio)
        {
            using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
            cn.Open();

            var sql = @"
SELECT 
    UPPER(LTRIM(RTRIM(Sku))) AS Sku,
    SUM(PesoNeto) AS InvInicial
FROM dbo.InventarioAlmacenado_Meat
WHERE FechaInventario = EOMONTH(DATEFROMPARTS(@Anio, @Mes, 1), -1)
  AND CodigoEtiqueta NOT LIKE '%SACT%'
GROUP BY UPPER(LTRIM(RTRIM(Sku)))
ORDER BY UPPER(LTRIM(RTRIM(Sku)));
";

            using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Mes", SqlDbType.Int).Value = mes;
            cmd.Parameters.Add("@Anio", SqlDbType.Int).Value = anio;

            var list = new List<object>();

            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(new
                {
                    sku = dr.IsDBNull(0) ? "" : dr.GetString(0),
                    invInicial = dr.IsDBNull(1) ? 0m : Convert.ToDecimal(dr.GetValue(1))
                });
            }

            return Json(list);
        }


        //no muestra lo que son de rafaga ya que pertenecen a VENTAS 2
        [HttpGet]
        public IActionResult InventarioActual()
        {
            using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
            cn.Open();

            var cmd = new SqlCommand(@"
        SELECT 
            UPPER(LTRIM(RTRIM(ProductoCodigo))) AS sku,
            SUM(kg) AS invActual
        FROM InventarioSigo
     where colonia = 'VENTAS' or colonia = 'VENTAS 1' or colonia = 'VENTAS ESP'
        GROUP BY UPPER(LTRIM(RTRIM(ProductoCodigo)));
    ", cn);

            var list = new List<object>();
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(new
                {
                    sku = dr.GetString(0),
                    invActual = dr.IsDBNull(1) ? 0m : dr.GetDecimal(1) // si kg es decimal
                });
            }

            return Json(list);
        }

        [HttpGet]
        public IActionResult PedidosDetalle(
            int anio,
            int? mes,
            int? trimestre,
            string sku,
            string origen = "TODOS",
            string cliente = null,
            string canal = null,
            int? vendedorId = null
        )
        {
            if (string.IsNullOrWhiteSpace(sku))
                return BadRequest("SKU es requerido.");

            // Si no viene mes pero sí trimestre, se arma el rango por trimestre
            // Si no viene ninguno, error
            if (!mes.HasValue && !trimestre.HasValue)
                return BadRequest("Debes enviar mes o trimestre.");

            // Validación básica
            if (mes.HasValue && (mes < 1 || mes > 12))
                return BadRequest("Mes inválido.");

            if (trimestre.HasValue && (trimestre < 1 || trimestre > 4))
                return BadRequest("Trimestre inválido.");

            DateTime desde;
            DateTime hasta;

            if (trimestre.HasValue)
            {
                // Q1 = ene-mar, Q2 = abr-jun, Q3 = jul-sep, Q4 = oct-dic
                var mesInicio = ((trimestre.Value - 1) * 3) + 1;
                desde = new DateTime(anio, mesInicio, 1);
                hasta = desde.AddMonths(3); // rango exclusivo
            }
            else
            {
                desde = new DateTime(anio, mes!.Value, 1);
                hasta = desde.AddMonths(1); // rango exclusivo
            }

            using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
            cn.Open();

            var sql = @"
DECLARE @SKU  varchar(50) = @pSku;
DECLARE @Origen varchar(20) = UPPER(LTRIM(RTRIM(@pOrigen)));

DECLARE @Cliente        varchar(50)  = @pCliente;
DECLARE @Canal          varchar(100) = @pCanal;
DECLARE @VendedorId int = @pVendedorId;

DECLARE @Desde date = @pDesde;
DECLARE @Hasta date = @pHasta;

;WITH
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
      AND TRY_CONVERT(date, o.FechaEntrega) >= @Desde
      AND TRY_CONVERT(date, o.FechaEntrega) <  @Hasta
),
ov_con_surtido AS (
    SELECT DISTINCT o.Id
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp         ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
),
ov_peso_agg AS (
    SELECT
        PedidoId = op.PedidoId,
        SKU      = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        KgPedido = SUM(CAST(op.Peso AS DECIMAL(18,4)))
    FROM dbo.OrdenVentaProducto op
    GROUP BY op.PedidoId, UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
),
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
    GROUP BY o.Id, UPPER(LTRIM(RTRIM(sd.Articulo)))
),
ov_pendiente_sku AS (
    SELECT
        ov.Id,
        ov.Cliente,
        ov.VendedorId,
        ov.Estatus,
        ov.Serie,
        ov.FechaDate,
        p.SKU,
        KgPedido   = p.KgPedido,
        KgSurtido  = ISNULL(sa.KgSurtido,0),
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
tr_surtido_agg AS (
    SELECT
        ts.TransferenciaId,
        SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
        KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    GROUP BY ts.TransferenciaId, UPPER(LTRIM(RTRIM(ts.Sku)))
),
tr_pendiente AS (
    SELECT
        Tipo = 'TRANSFERENCIA',
        DocumentoId = t.Id,
        Fecha = TRY_CONVERT(date, t.FechaSolicitud),
        Canal = UPPER(LTRIM(RTRIM(s.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
        KgPedido   = SUM(CAST(td.CantidadKg AS DECIMAL(18,4))),
        KgSurtido  = SUM(ISNULL(tsa.KgSurtido,0)),
        KgPendiente = SUM(
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
      AND TRY_CONVERT(date, t.FechaSolicitud) >= @Desde
      AND TRY_CONVERT(date, t.FechaSolicitud) <  @Hasta
    GROUP BY
        t.Id,
        TRY_CONVERT(date, t.FechaSolicitud),
        UPPER(LTRIM(RTRIM(s.Canal))),
        UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
)

SELECT
    Tipo        = 'OV',
    DocumentoId = ovp.Id,
    Serie       = ovp.Serie,
    Fecha       = ovp.FechaDate,
    Estatus     = ovp.Estatus,
    Cliente     = ovp.Cliente,
    RazonSocial = cs.NombreCliente,
    Canal       = UPPER(LTRIM(RTRIM(cs.U_CANAL))),
    VendedorId  = ovp.VendedorId,
    Vendedor    = cs.VendedorNombre,
    SKU         = ovp.SKU,
    KgPedido    = ovp.KgPedido,
    KgSurtido   = ovp.KgSurtido,
    KgPendiente = ovp.KgPendiente
FROM ov_pendiente_sku ovp
LEFT JOIN dbo.ClienteSap cs ON cs.Cliente = ovp.Cliente
WHERE
    ovp.SKU COLLATE DATABASE_DEFAULT = UPPER(LTRIM(RTRIM(@SKU))) COLLATE DATABASE_DEFAULT
    AND ovp.KgPendiente > 0
    AND (
         @Origen = 'TODOS'
      OR (@Origen = 'CLIENTE'  AND @Cliente IS NOT NULL AND UPPER(ovp.Cliente) = UPPER(@Cliente))
      OR (@Origen = 'VENDEDOR' AND @VendedorId IS NOT NULL AND ovp.VendedorId = @VendedorId)
      OR (@Origen = 'CEDIS'    AND @Canal IS NOT NULL AND UPPER(LTRIM(RTRIM(cs.U_CANAL))) = UPPER(LTRIM(RTRIM(@Canal))))
    )

UNION ALL

SELECT
    Tipo        = tp.Tipo,
    DocumentoId = tp.DocumentoId,
    Serie       = NULL,
    Fecha       = tp.Fecha,
    Estatus     = NULL,
    Cliente     = NULL,
    RazonSocial = NULL,
    Canal       = tp.Canal,
    VendedorId  = NULL,
    Vendedor    = NULL,
    SKU         = tp.SKU,
    KgPedido    = tp.KgPedido,
    KgSurtido   = tp.KgSurtido,
    KgPendiente = tp.KgPendiente
FROM tr_pendiente tp
WHERE
    (@Origen IN ('TODOS','CEDIS'))
    AND tp.SKU COLLATE DATABASE_DEFAULT = UPPER(LTRIM(RTRIM(@SKU))) COLLATE DATABASE_DEFAULT
    AND tp.KgPendiente > 0
    AND (
          @Origen = 'TODOS'
       OR (@Canal IS NOT NULL AND UPPER(LTRIM(RTRIM(tp.Canal))) = UPPER(LTRIM(RTRIM(@Canal))))
    )

ORDER BY Fecha DESC, Tipo, DocumentoId DESC;
";

            using var cmd = new SqlCommand(sql, cn);
            cmd.CommandType = CommandType.Text;

            cmd.Parameters.AddWithValue("@pSku", sku.Trim());
            cmd.Parameters.AddWithValue("@pOrigen", (object?)origen ?? "TODOS");
            cmd.Parameters.AddWithValue("@pCliente", (object?)cliente ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pCanal", (object?)canal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pVendedorId", (object?)vendedorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pDesde", desde.Date);
            cmd.Parameters.AddWithValue("@pHasta", hasta.Date);

            var list = new List<object>();

            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(new
                {
                    tipo = dr.IsDBNull(0) ? "" : dr.GetString(0),
                    documentoId = dr.IsDBNull(1) ? 0 : Convert.ToInt32(dr.GetValue(1)),
                    serie = dr.IsDBNull(2) ? null : dr.GetString(2),
                    fecha = dr.IsDBNull(3) ? (DateTime?)null : dr.GetDateTime(3),
                    estatus = dr.IsDBNull(4) ? (int?)null : Convert.ToInt32(dr.GetValue(4)),
                    cliente = dr.IsDBNull(5) ? null : dr.GetString(5),
                    razonSocial = dr.IsDBNull(6) ? null : dr.GetString(6),
                    canal = dr.IsDBNull(7) ? null : dr.GetString(7),
                    vendedorId = dr.IsDBNull(8) ? (int?)null : Convert.ToInt32(dr.GetValue(8)),
                    vendedor = dr.IsDBNull(9) ? null : dr.GetString(9),
                    sku = dr.IsDBNull(10) ? "" : dr.GetString(10),
                    kgPedido = dr.IsDBNull(11) ? 0m : Convert.ToDecimal(dr.GetValue(11)),
                    kgSurtido = dr.IsDBNull(12) ? 0m : Convert.ToDecimal(dr.GetValue(12)),
                    kgPendiente = dr.IsDBNull(13) ? 0m : Convert.ToDecimal(dr.GetValue(13)),
                });
            }

            return Json(list);
        }


        [Authorize]
        [HttpGet]
        public async Task<IActionResult> VentaRealResumen(CancellationToken ct)
        {
            try
            {
                var vendedorIds = await GetVendedorIdsActualesAsync(ct);
                var canalesCedis = await GetCanalesCedisPorVendedorIdsAsync(vendedorIds, ct);

                var vendedorIdsCsv = string.Join(",", vendedorIds);
                var canalesCsv = string.Join(",", canalesCedis);

                using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
                await cn.OpenAsync(ct);

                var sql = @"
SELECT
    UPPER(LTRIM(RTRIM(b.Articulo))) AS ArticuloCodigo,
    MONTH(a.FechaValidacion) AS Mes,
    YEAR(a.FechaValidacion) AS Anio,
    cs.VendedorId AS VendedorId,
    UPPER(LTRIM(RTRIM(cs.VendedorNombre))) AS Vendedor,
    UPPER(LTRIM(RTRIM(cs.U_CANAL))) AS U_CANAL,
    SUM(CAST(b.Kg AS decimal(18,4))) AS KgVendidos
FROM dbo.SurtidoEncabezado a
INNER JOIN dbo.SurtidoDetalle b
    ON a.SolicitudSurtidoId = b.SolicitudSurtidoId
LEFT JOIN dbo.ClienteSap cs
    ON cs.Cliente = a.CodigoSap
WHERE a.FechaValidacion IS NOT NULL
  AND
  (
      -- Si NO trae canal CEDIS, ve todo
      NOT EXISTS
      (
          SELECT 1
          FROM STRING_SPLIT(ISNULL(@CanalesCsv, ''), ',') cc
          WHERE ISNULL(LTRIM(RTRIM(cc.value)), '') <> ''
            AND UPPER(LTRIM(RTRIM(cc.value))) LIKE 'CEDIS%'
      )

      OR

      -- Si SÍ trae canal CEDIS, se limita al canal correspondiente
      EXISTS
      (
          SELECT 1
          FROM STRING_SPLIT(ISNULL(@CanalesCsv, ''), ',') cc
          WHERE ISNULL(LTRIM(RTRIM(cc.value)), '') <> ''
            AND UPPER(LTRIM(RTRIM(cc.value))) LIKE 'CEDIS%'
            AND
            (
                UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) =
                UPPER(LTRIM(RTRIM(cc.value)))

                OR 'CEDIS-' + UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))) =
                UPPER(LTRIM(RTRIM(cc.value)))

                OR REPLACE(UPPER(LTRIM(RTRIM(ISNULL(cs.U_CANAL, '')))), 'CEDIS-', '') =
                   REPLACE(UPPER(LTRIM(RTRIM(cc.value))), 'CEDIS-', '')
            )
      )
  )
GROUP BY
    UPPER(LTRIM(RTRIM(b.Articulo))),
    MONTH(a.FechaValidacion),
    YEAR(a.FechaValidacion),
    cs.VendedorId,
    UPPER(LTRIM(RTRIM(cs.VendedorNombre))),
    UPPER(LTRIM(RTRIM(cs.U_CANAL)))
ORDER BY
    Anio, Mes, ArticuloCodigo;
";

                using var cmd = new SqlCommand(sql, cn);
                //cmd.Parameters.Add("@VendedorIdsCsv", System.Data.SqlDbType.VarChar, 200).Value = vendedorIdsCsv;
                cmd.Parameters.Add("@VendedorIdsCsv", SqlDbType.VarChar, 200).Value = vendedorIdsCsv;
                cmd.Parameters.Add("@CanalesCsv", SqlDbType.VarChar, 500).Value = canalesCsv;

                using var dr = await cmd.ExecuteReaderAsync(ct);

                var list = new List<object>();

                while (await dr.ReadAsync(ct))
                {
                    list.Add(new
                    {
                        articuloCodigo = dr.IsDBNull(0) ? "" : dr.GetString(0),
                        mes = dr.IsDBNull(1) ? 0 : Convert.ToInt32(dr.GetValue(1)),
                        anio = dr.IsDBNull(2) ? 0 : Convert.ToInt32(dr.GetValue(2)),
                        vendedorId = dr.IsDBNull(3) ? (int?)null : Convert.ToInt32(dr.GetValue(3)),
                        vendedor = dr.IsDBNull(4) ? "" : dr.GetString(4),
                        u_CANAL = dr.IsDBNull(5) ? "" : dr.GetString(5),
                        kgVendidos = dr.IsDBNull(6) ? 0m : Convert.ToDecimal(dr.GetValue(6))
                    });
                }

                return Json(list);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    error = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> UsuarioVendedorActual(CancellationToken ct)
        {
            var vendedorIds = await GetVendedorIdsActualesAsync(ct);
            var canales = await GetCanalesCedisPorVendedorIdsAsync(vendedorIds, ct);

            return Json(new
            {
                esVendedor = vendedorIds.Count > 0,
                vendedorIds = vendedorIds,
                canales = canales
            });
        }


        private List<int> GetVendedorIdsActualesSync()
        {
            var vendedorIds = new List<int>();

            //===================================================
            // 1) Resolver VendedorId(s) desde CLAIM
            //===================================================
            var vClaim = User.FindFirst("VendedorId")?.Value?.Trim();

            if (!string.IsNullOrWhiteSpace(vClaim))
            {
                if (vClaim.Contains(","))
                {
                    // Ejemplo: "10,28"
                    vendedorIds = vClaim
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0)
                        .Where(v => v > 0)
                        .Distinct()
                        .ToList();
                }
                else
                {
                    // Ejemplo: "1028" => 10, 28
                    var clean = new string(vClaim.Where(char.IsDigit).ToArray());

                    for (int i = 0; i + 1 < clean.Length; i += 2)
                    {
                        if (int.TryParse(clean.Substring(i, 2), out var v) && v > 0)
                            vendedorIds.Add(v);
                    }
                }
            }

            //===================================================
            // 2) Fallback AD / Usuarios si no vino CLAIM
            //===================================================
            if (vendedorIds.Count == 0)
            {
                var raw = (User?.Identity?.Name ?? "").Trim();
                var username = raw.Contains('\\') ? raw.Split('\\').Last() : raw;
                var usernameEmail = username.Contains('@') ? username : $"{username}@carnesg.net";

                try
                {
                    int? vendAD = null;

                    if (_uDb?.UsuariosAD != null)
                    {
                        vendAD = _uDb.UsuariosAD.AsNoTracking()
                            .Where(x =>
                                x.UsuarioAd == raw ||
                                x.UsuarioAd == username ||
                                x.UsuarioAd == usernameEmail)
                            .Select(x => (int?)x.VendedorId)
                            .FirstOrDefault();
                    }

                    int? vendApp = null;

                    if (!(vendAD.HasValue && vendAD.Value > 0) && _uDb?.Usuarios != null)
                    {
                        vendApp = _uDb.Usuarios.AsNoTracking()
                            .Where(x =>
                                x.Usuario == raw ||
                                x.Usuario == username ||
                                x.Usuario == usernameEmail)
                            .Select(x => (int?)x.VendedorId)
                            .FirstOrDefault();
                    }

                    var vend = vendAD ?? vendApp;

                    if (vend.HasValue && vend.Value > 0)
                        vendedorIds.Add(vend.Value);
                }
                catch
                {
                    vendedorIds.Clear();
                }
            }

            return vendedorIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();
        }

        [HttpGet]
        public IActionResult DescargarArticulosExcel(string search = "")
        {
            var query = _context.ArticuloSap.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(a =>
                    (a.ProductoCodigo != null && a.ProductoCodigo.Contains(search)) ||
                    (a.ProductoNombre != null && a.ProductoNombre.Contains(search))
                );
            }

            var articulos = query
                .OrderBy(a => a.ProductoCodigo)
                .ToList();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("ArticulosSAP");

            // Encabezados
            ws.Cell(1, 1).Value = "Sku";
            ws.Cell(1, 2).Value = "Producto";
            ws.Cell(1, 3).Value = "Master";
            ws.Cell(1, 4).Value = "Tipo SKU";
            ws.Cell(1, 5).Value = "Rotación";
            ws.Cell(1, 6).Value = "Kg Promedio";
            ws.Cell(1, 7).Value = "Última Modificación";

            int row = 2;

            foreach (var art in articulos)
            {
                ws.Cell(row, 1).Value = art.ProductoCodigo ?? "";
                ws.Cell(row, 2).Value = art.ProductoNombre ?? "";
                ws.Cell(row, 3).Value = art.U_MASTER ?? "";
                ws.Cell(row, 4).Value = art.U_TipoporSKU ?? "";
                ws.Cell(row, 5).Value = art.Rotacion ?? 0;
                ws.Cell(row, 6).Value = art.U_KilosCaja ?? 0;

                ws.Cell(row, 7).Value = art.FechaModificacion;
                row++;
            }

            // Estilo encabezado
            var headerRange = ws.Range(1, 1, 1, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.DarkRed;
            headerRange.Style.Font.FontColor = XLColor.White;

            // Formatos
            ws.Column(6).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(7).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

            // Autoajuste
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"Catalogo_Articulos_SAP_{DateTime.Now:yyyy-MM-dd}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }


        [HttpGet]
        public IActionResult DescargarClientesExcel(string search = "")
        {
            var query = _context.ClienteSap.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(c =>
                    (c.Cliente != null && c.Cliente.Contains(search)) ||
                    (c.Nombrecliente != null && c.Nombrecliente.Contains(search)) ||
                    (c.VendedorNombre != null && c.VendedorNombre.Contains(search)) ||
                    (c.VendedorId.HasValue && c.VendedorId.Value.ToString().Contains(search))
                );
            }

            var clientes = query
                .OrderBy(c => c.Cliente)
                .ToList();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("ClientesSAP");

            // Encabezados
            ws.Cell(1, 1).Value = "ClienteId";
            ws.Cell(1, 2).Value = "Cliente";
            ws.Cell(1, 3).Value = "Clasificación";
            ws.Cell(1, 4).Value = "Canal";
            ws.Cell(1, 5).Value = "Id";
            ws.Cell(1, 6).Value = "Vendedor";
            ws.Cell(1, 7).Value = "Última Modificación";

            int row = 2;

            foreach (var cliente in clientes)
            {
                ws.Cell(row, 1).Value = cliente.Cliente ?? "";
                ws.Cell(row, 2).Value = cliente.Nombrecliente ?? "";
                ws.Cell(row, 3).Value = cliente.U_MT_Clasificacion ?? "";
                ws.Cell(row, 4).Value = cliente.U_CANAL ?? "";
                ws.Cell(row, 5).Value = cliente.VendedorId.HasValue ? cliente.VendedorId.Value.ToString() : "";
                ws.Cell(row, 6).Value = cliente.VendedorNombre ?? "";
                ws.Cell(row, 7).Value = cliente.FechaModificacion;

                row++;
            }

            // Estilo encabezado
            var headerRange = ws.Range(1, 1, 1, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.DarkRed;
            headerRange.Style.Font.FontColor = XLColor.White;

            // Formatos
            ws.Column(7).Style.NumberFormat.Format = "dd/MM/yyyy HH:mm";

            // Ajuste de columnas
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"Catalogo_Clientes_SAP_{DateTime.Now:yyyy-MM-dd}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }

        [HttpGet]
        public IActionResult DescargarCatalogoPreciosExcel(string search = "")
        {
            var query = _context.CatalogoPrecioSap.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(p =>
                    (p.ProductoCodigo != null && p.ProductoCodigo.Contains(search)) ||
                    (p.Cliente != null && p.Cliente.Contains(search)) ||
                    (p.PriceListName != null && p.PriceListName.Contains(search)) ||
                    p.PriceListNum.ToString().Contains(search)
                );
            }

            var precios = query
                .OrderBy(p => p.Cliente)
                .ThenBy(p => p.ProductoCodigo)
                .ToList();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("PreciosClienteSAP");

            // Encabezados
            ws.Cell(1, 1).Value = "Sku";
            ws.Cell(1, 2).Value = "ClienteId";
            ws.Cell(1, 3).Value = "IdPrecio";
            ws.Cell(1, 4).Value = "Lista";
            ws.Cell(1, 5).Value = "Precio";
            ws.Cell(1, 6).Value = "Última Modificación";

            int row = 2;

            foreach (var p in precios)
            {
                ws.Cell(row, 1).Value = p.ProductoCodigo ?? "";
                ws.Cell(row, 2).Value = p.Cliente ?? "";
                ws.Cell(row, 3).Value = p.PriceListNum;
                ws.Cell(row, 4).Value = p.PriceListName ?? "";
                ws.Cell(row, 5).Value = p.Precio;
                ws.Cell(row, 6).Value = p.FechaModificacion;
                row++;
            }

            // Estilo encabezado
            var headerRange = ws.Range(1, 1, 1, 6);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.DarkRed;
            headerRange.Style.Font.FontColor = XLColor.White;

            // Formatos
            ws.Column(5).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(6).Style.NumberFormat.Format = "dd/MM/yyyy HH:mm";

            // Ajuste de columnas
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"Catalogo_Precios_Cliente_{DateTime.Now:yyyy-MM-dd}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }



        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ObtenerHistoricoAutorizacionPrecio(string fechaInicio, string fechaFin)
        {
            DateTime? inicio = null;
            DateTime? fin = null;

            if (!string.IsNullOrWhiteSpace(fechaInicio) && DateTime.TryParse(fechaInicio, out var fi))
                inicio = fi.Date;

            if (!string.IsNullOrWhiteSpace(fechaFin) && DateTime.TryParse(fechaFin, out var ff))
                fin = ff.Date.AddDays(1).AddTicks(-1);

            // =====================================================
            // SERIES ASIGNADAS AL USUARIO
            // =====================================================
            var verTodasSeries = UsuarioPuedeVerTodasLasSeries();

            var seriesPermitidas = new List<string>();

            if (!verTodasSeries)
            {
                var idsSeries = await ObtenerSeriesIdsUsuarioActualAsync();

                if (idsSeries == null || !idsSeries.Any())
                    return Json(Array.Empty<object>());

                seriesPermitidas = await _context.Series
                    .AsNoTracking()
                    .Where(s => idsSeries.Contains(s.Id))
                    .Select(s => s.NombreSerie)
                    .ToListAsync();

                seriesPermitidas = seriesPermitidas
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpper())
                    .Distinct()
                    .ToList();

                if (!seriesPermitidas.Any())
                    return Json(Array.Empty<object>());
            }

            // =====================================================
            // HISTÓRICO + ORDEN DE VENTA PARA VALIDAR SERIE
            // =====================================================
            var query =
                from h in _context.PrecioLineasHistorico.AsNoTracking()
                from ov in _context.OrdenVenta.AsNoTracking()
                    .Where(ov => ov.Id == h.OrdenVentaId)
                    .DefaultIfEmpty()
                select new
                {
                    Historico = h,
                    Orden = ov
                };

            if (inicio.HasValue)
                query = query.Where(x => x.Historico.FechaRegistro >= inicio.Value);

            if (fin.HasValue)
                query = query.Where(x => x.Historico.FechaRegistro <= fin.Value);

            // =====================================================
            // FILTRO POR SERIE ASIGNADA
            // =====================================================
            if (!verTodasSeries)
            {
                query = query.Where(x =>
                    x.Orden != null &&
                    x.Orden.Serie != null &&
                    seriesPermitidas.Contains(x.Orden.Serie.Trim().ToUpper())
                );
            }

            var data = await query
                .OrderByDescending(x => x.Historico.FechaRegistro)
                .Select(x => new
                {
                    x.Historico.OrdenVentaConsecutivo,

                    // Lo mando por si después quieres mostrar la columna Serie en el modal
                    Serie = x.Orden != null ? x.Orden.Serie : "",

                    x.Historico.ClienteNombre,
                    x.Historico.ProductoCodigo,
                    x.Historico.ProductoNombre,
                    x.Historico.PrecioLista,
                    x.Historico.PrecioOVAntes,
                    x.Historico.PrecioAutorizado,
                    x.Historico.Diferencia,
                    x.Historico.Usuario,
                    x.Historico.Motivo,
                    FechaRegistro = x.Historico.FechaRegistro
                })
                .ToListAsync();

            return Json(data);
        }


        public record ActualizarAlmacenesCompletadoReq(
        int ordenId,
        List<ActualizarAlmacenDetalleReq> productos
    );

        public record ActualizarAlmacenDetalleReq(
            int detalleId,
            string almacen
        );

        [HttpPost]
        public async Task<IActionResult> ActualizarAlmacenesCompletado([FromBody] ActualizarAlmacenesCompletadoReq req)
        {
            if (req == null || req.ordenId <= 0)
                return BadRequest("Orden inválida.");

            if (req.productos == null || req.productos.Count == 0)
                return BadRequest("No se recibieron productos.");

            var actualizadas = 0;

            foreach (var p in req.productos)
            {
                if (p.detalleId <= 0)
                    continue;

                var almacen = (p.almacen ?? "").Trim();

                var rows = await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.PedidoVentaProducto
               SET Almacen = {almacen}
             WHERE Id = {p.detalleId};
        ");

                actualizadas += rows;
            }

            return Ok(new
            {
                ok = actualizadas > 0,
                msg = actualizadas > 0
                    ? "Almacenes actualizados correctamente."
                    : "No se actualizó ningún almacén.",
                actualizadas
            });
        }
        [HttpGet]
        public async Task<IActionResult> DescargarHistoricoAutorizacionPrecioExcel(string fechaInicio, string fechaFin)
        {
            try
            {
                DateTime? inicio = null;
                DateTime? fin = null;

                if (!string.IsNullOrWhiteSpace(fechaInicio) && DateTime.TryParse(fechaInicio, out var fi))
                    inicio = fi.Date;

                if (!string.IsNullOrWhiteSpace(fechaFin) && DateTime.TryParse(fechaFin, out var ff))
                    fin = ff.Date.AddDays(1).AddTicks(-1);

                var query = _context.PrecioLineasHistorico
                    .AsNoTracking()
                    .AsQueryable();

                if (inicio.HasValue)
                    query = query.Where(x => x.FechaRegistro >= inicio.Value);

                if (fin.HasValue)
                    query = query.Where(x => x.FechaRegistro <= fin.Value);

                var data = await query
                    .OrderByDescending(x => x.FechaRegistro)
                    .Select(x => new
                    {
                        x.OrdenVentaConsecutivo,
                        x.ClienteNombre,
                        x.ProductoCodigo,
                        x.ProductoNombre,
                        x.PrecioLista,
                        x.PrecioOVAntes,
                        x.PrecioAutorizado,
                        x.Diferencia,
                        x.Usuario,
                        x.Motivo,
                        x.FechaRegistro
                    })
                    .ToListAsync();

                using var workbook = new ClosedXML.Excel.XLWorkbook();
                var ws = workbook.Worksheets.Add("Historico Precio");

                ws.Cell(1, 1).Value = "Orden de venta";
                ws.Cell(1, 2).Value = "Cliente";
                ws.Cell(1, 3).Value = "SKU";
                ws.Cell(1, 4).Value = "Producto";
                ws.Cell(1, 5).Value = "Precio Lista";
                ws.Cell(1, 6).Value = "Precio Solicitado";
                ws.Cell(1, 7).Value = "Precio Autorizado";
                ws.Cell(1, 8).Value = "Diferencia";
                ws.Cell(1, 9).Value = "Autorizó";
                ws.Cell(1, 10).Value = "Motivo";
                ws.Cell(1, 11).Value = "Fecha Registro";

                int row = 2;

                foreach (var x in data)
                {
                    ws.Cell(row, 1).Value = x.OrdenVentaConsecutivo ?? "";
                    ws.Cell(row, 2).Value = x.ClienteNombre ?? "";
                    ws.Cell(row, 3).Value = x.ProductoCodigo ?? "";
                    ws.Cell(row, 4).Value = x.ProductoNombre ?? "";
                    ws.Cell(row, 5).Value = x.PrecioLista;
                    ws.Cell(row, 6).Value = x.PrecioOVAntes;
                    ws.Cell(row, 7).Value = x.PrecioAutorizado;
                    ws.Cell(row, 8).Value = x.Diferencia;
                    ws.Cell(row, 9).Value = x.Usuario ?? "";
                    ws.Cell(row, 10).Value = x.Motivo ?? "";
                    ws.Cell(row, 11).Value = x.FechaRegistro;
                    row++;
                }

                ws.Range(1, 1, 1, 11).Style.Font.Bold = true;
                ws.Range(1, 1, 1, 11).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.DarkRed;
                ws.Range(1, 1, 1, 11).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

                ws.Column(5).Style.NumberFormat.Format = "$#,##0.00";
                ws.Column(6).Style.NumberFormat.Format = "$#,##0.00";
                ws.Column(7).Style.NumberFormat.Format = "$#,##0.00";
                ws.Column(8).Style.NumberFormat.Format = "$#,##0.00";
                ws.Column(11).Style.DateFormat.Format = "dd/MM/yyyy HH:mm:ss";

                ws.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var nombreArchivo = $"HistoricoAutorizacionPrecio_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(
                    stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    nombreArchivo
                );
            }
            catch (Exception ex)
            {
                return BadRequest($"Error al generar Excel: {ex.Message}");
            }
        }


        [Authorize]
        [HttpGet]
        public async Task<IActionResult> DevolucionesMeat(int mes, int anio, CancellationToken ct)
        {
            if (mes < 1 || mes > 12)
                return BadRequest("Mes inválido.");

            if (anio < 2000 || anio > 2100)
                return BadRequest("Año inválido.");

            var vendedorIds = await GetVendedorIdsActualesAsync(ct);
            var canalesCedis = await GetCanalesCedisPorVendedorIdsAsync(vendedorIds, ct);

            var canalesCsv = string.Join(",", canalesCedis);

            using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
            await cn.OpenAsync(ct);

            var fechaInicio = new DateTime(anio, mes, 1);
            var fechaFin = fechaInicio.AddMonths(1);

            var sql = @"
SELECT
    YEAR(b.FechaDevolucion) AS Anio,
    MONTH(b.FechaDevolucion) AS Mes,
    UPPER(LTRIM(RTRIM(b.Articulo))) AS Sku,
    SUM(CAST(ISNULL(b.Peso, 0) AS DECIMAL(18,4))) AS Kg,
    b.CodigoSap AS ClienteId,
    b.Cliente,
    c.VendedorId,
    c.VendedorNombre,
    ISNULL(c.U_CANAL, '') AS Canal
FROM dbo.DevolucionMeat b
INNER JOIN dbo.ClienteSap c 
    ON UPPER(LTRIM(RTRIM(b.CodigoSap))) = UPPER(LTRIM(RTRIM(c.Cliente)))
WHERE b.FechaDevolucion IS NOT NULL
  AND b.FechaDevolucion >= @FechaInicio
  AND b.FechaDevolucion < @FechaFin

  AND EXISTS
  (
      SELECT 1
      FROM dbo.Subpedido a
      WHERE a.U_DocMeat = b.SolicitudSurtidoId
  )

  AND
  (
      NOT EXISTS
      (
          SELECT 1
          FROM STRING_SPLIT(ISNULL(@CanalesCsv, ''), ',') cc
          WHERE ISNULL(LTRIM(RTRIM(cc.value)), '') <> ''
            AND UPPER(LTRIM(RTRIM(cc.value))) LIKE 'CEDIS%'
      )

      OR

      EXISTS
      (
          SELECT 1
          FROM STRING_SPLIT(ISNULL(@CanalesCsv, ''), ',') cc
          WHERE ISNULL(LTRIM(RTRIM(cc.value)), '') <> ''
            AND UPPER(LTRIM(RTRIM(cc.value))) LIKE 'CEDIS%'
            AND
            (
                UPPER(LTRIM(RTRIM(ISNULL(c.U_CANAL, '')))) =
                UPPER(LTRIM(RTRIM(cc.value)))

                OR 'CEDIS-' + UPPER(LTRIM(RTRIM(ISNULL(c.U_CANAL, '')))) =
                UPPER(LTRIM(RTRIM(cc.value)))

                OR REPLACE(UPPER(LTRIM(RTRIM(ISNULL(c.U_CANAL, '')))), 'CEDIS-', '') =
                   REPLACE(UPPER(LTRIM(RTRIM(cc.value))), 'CEDIS-', '')
            )
      )
  )
GROUP BY
    YEAR(b.FechaDevolucion),
    MONTH(b.FechaDevolucion),
    UPPER(LTRIM(RTRIM(b.Articulo))),
    b.CodigoSap,
    b.Cliente,
    c.VendedorId,
    c.VendedorNombre,
    ISNULL(c.U_CANAL, '')
ORDER BY
    YEAR(b.FechaDevolucion),
    MONTH(b.FechaDevolucion),
    UPPER(LTRIM(RTRIM(b.Articulo)));
";

            using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = fechaInicio;
            cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = fechaFin;
            cmd.Parameters.Add("@CanalesCsv", SqlDbType.VarChar, 500).Value = canalesCsv;

            var list = new List<object>();

            using var dr = await cmd.ExecuteReaderAsync(ct);

            while (await dr.ReadAsync(ct))
            {
                list.Add(new
                {
                    anio = dr.IsDBNull(0) ? 0 : Convert.ToInt32(dr.GetValue(0)),
                    mes = dr.IsDBNull(1) ? 0 : Convert.ToInt32(dr.GetValue(1)),
                    sku = dr.IsDBNull(2) ? "" : dr.GetString(2),
                    kg = dr.IsDBNull(3) ? 0m : Convert.ToDecimal(dr.GetValue(3)),
                    clienteId = dr.IsDBNull(4) ? "" : dr.GetString(4),
                    clienteNombre = dr.IsDBNull(5) ? "" : dr.GetString(5),
                    vendedorId = dr.IsDBNull(6) ? (int?)null : Convert.ToInt32(dr.GetValue(6)),
                    vendedorNombre = dr.IsDBNull(7) ? "" : dr.GetString(7),
                    canal = dr.IsDBNull(8) ? "" : dr.GetString(8)
                });
            }

            return Json(list);
        }


        [HttpGet]
        public async Task<IActionResult> ObtenerSurtidosMeatResumen(
       string cliente,
       string folio,
       string serie,
       string fechaInicio,
       string fechaFin,
       string tipo = "",
       CancellationToken ct = default)
        {
            try
            {
                var seriesUsuarioCsv = await ObtenerSeriesPermitidasCsvActualAsync(ct);

                using var cn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                cn.Open();

                DateTime? fi = null;
                DateTime? ff = null;

                if (!string.IsNullOrWhiteSpace(fechaInicio))
                    fi = Convert.ToDateTime(fechaInicio);

                if (!string.IsNullOrWhiteSpace(fechaFin))
                    ff = Convert.ToDateTime(fechaFin);

                var sql = @"
WITH PedidosOVBase AS (
    SELECT 
        a.Id,
        a.ConsecutivoOV,
        a.OrdenVentaId,
        a.Cliente,
        a.U_DocMeat,
        SUM(b.KilosCaja) AS KgPedidos,
        SUM(b.Cajas) AS CajasPedidas
    FROM Subpedido a
    INNER JOIN SubpedidoProductos b 
        ON a.Id = b.SubpedidoId
    GROUP BY 
        a.Id,
        a.ConsecutivoOV,
        a.OrdenVentaId,
        a.Cliente,
        a.U_DocMeat
),
SurtidoOVBase AS (
    SELECT
        e.SolicitudSurtidoId,
        SUM(ISNULL(e.Kg, 0)) AS KgSurtidos,
        SUM(ISNULL(e.Cajas, 0)) AS CajasSurtidas
    FROM SurtidoDetalle e
    GROUP BY e.SolicitudSurtidoId
),
PedidosOV AS (
    SELECT
        p.ConsecutivoOV,
        p.OrdenVentaId,
        p.Cliente,
        SUM(p.KgPedidos) AS KgPedidos,
        SUM(p.CajasPedidas) AS CajasPedidas,
        SUM(ISNULL(s.KgSurtidos, 0)) AS KgSurtidos,
        SUM(ISNULL(s.CajasSurtidas, 0)) AS CajasSurtidas
    FROM PedidosOVBase p
    LEFT JOIN SurtidoOVBase s
        ON TRY_CAST(p.U_DocMeat AS INT) = s.SolicitudSurtidoId
    GROUP BY
        p.ConsecutivoOV,
        p.OrdenVentaId,
        p.Cliente
),
PedidosTR AS (
    SELECT
        a.TransferenciaId,
        a.Destino,
        SUM(b.CantidadKg) AS KgPedidos,
        SUM(b.Cajas) AS CajasPedidas
    FROM PedidosTransferencia a
    INNER JOIN PedidosTransferenciaDetalle b
        ON a.Id = b.PedidoTransferenciaId
    GROUP BY
        a.TransferenciaId,
        a.Destino
),
SurtidoTR AS (
    SELECT
        d.TransferenciaId,
        SUM(ISNULL(d.KgSurtido, 0)) AS KgSurtidos,
        SUM(ISNULL(d.CajasSurtidas, 0)) AS CajasSurtidas
    FROM TransferenciaSurtido d
    GROUP BY d.TransferenciaId
),
ResumenFinal AS (
    SELECT
        p.ConsecutivoOV AS Folio,
        LTRIM(RTRIM(ISNULL(c.Serie, ''))) AS Serie,
        d.Nombrecliente AS Cliente,
        p.KgPedidos,
        p.CajasPedidas,
        p.KgSurtidos,
        p.CajasSurtidas,
        ROUND((ISNULL(p.KgSurtidos, 0) * 100.0) / NULLIF(p.KgPedidos, 0), 2) AS GAPKg,
        ROUND((ISNULL(p.CajasSurtidas, 0) * 100.0) / NULLIF(p.CajasPedidas, 0), 2) AS GAPCajas,
        c.FechaEmbarque AS Fecha,
        'OV' AS Tipo
    FROM PedidosOV p
    INNER JOIN OrdenVenta c 
        ON p.OrdenVentaId = c.Id
    INNER JOIN ClienteSap d 
        ON p.Cliente = d.Cliente
AND ISNULL(d.AplicaPresupuesto, 0) = 1
    WHERE c.Estatus <> 0

    UNION ALL

    SELECT
        c.Consecutivo AS Folio,
        '' AS Serie,
        p.Destino AS Cliente,
        p.KgPedidos,
        p.CajasPedidas,
        ISNULL(s.KgSurtidos, 0) AS KgSurtidos,
        ISNULL(s.CajasSurtidas, 0) AS CajasSurtidas,
        ROUND((ISNULL(s.KgSurtidos, 0) * 100.0) / NULLIF(p.KgPedidos, 0), 2) AS GAPKg,
        ROUND((ISNULL(s.CajasSurtidas, 0) * 100.0) / NULLIF(p.CajasPedidas, 0), 2) AS GAPCajas,
        c.FechaSolicitud AS Fecha,
        'TR' AS Tipo
    FROM Transferencias c
    INNER JOIN PedidosTR p
        ON c.Id = p.TransferenciaId
    LEFT JOIN SurtidoTR s
        ON c.Id = s.TransferenciaId
    WHERE c.Estatus <> 0
)
SELECT *
FROM ResumenFinal
WHERE (@Cliente IS NULL OR @Cliente = '' OR Cliente LIKE '%' + @Cliente + '%')
  AND (@Folio IS NULL OR @Folio = '' OR Folio LIKE '%' + @Folio + '%')
  AND (@Tipo IS NULL OR @Tipo = '' OR Tipo = @Tipo)
  AND (@FechaInicio IS NULL OR CONVERT(date, Fecha) >= CONVERT(date, @FechaInicio))
  AND (@FechaFin IS NULL OR CONVERT(date, Fecha) <= CONVERT(date, @FechaFin))

  -- Filtro seleccionado en pantalla:
  -- OV sí filtra por serie seleccionada.
  -- TR siempre pasa.
  AND (
        @Serie IS NULL
        OR @Serie = ''
        OR Tipo = 'TR'
        OR (
            Tipo = 'OV'
            AND EXISTS (
                SELECT 1
                FROM STRING_SPLIT(@Serie, ',') S
                WHERE UPPER(LTRIM(RTRIM(S.value))) = UPPER(LTRIM(RTRIM(Serie)))
            )
        )
  )

  -- Seguridad por series configuradas al usuario:
  -- OV sí filtra por UsuarioSerie.
  -- TR siempre pasa.
  AND (
        @SeriesUsuario IS NULL
        OR @SeriesUsuario = ''
        OR Tipo = 'TR'
        OR (
            Tipo = 'OV'
            AND EXISTS (
                SELECT 1
                FROM STRING_SPLIT(@SeriesUsuario, ',') SU
                WHERE UPPER(LTRIM(RTRIM(SU.value))) = UPPER(LTRIM(RTRIM(Serie)))
            )
        )
  )
ORDER BY Fecha DESC, Folio;
";

                using var cmd = new SqlCommand(sql, cn);

                cmd.Parameters.Add("@Cliente", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(cliente) ? DBNull.Value : cliente.Trim();

                cmd.Parameters.Add("@Folio", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(folio) ? DBNull.Value : folio.Trim();

                cmd.Parameters.Add("@Serie", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(serie) ? DBNull.Value : serie.Trim();

                cmd.Parameters.Add("@SeriesUsuario", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(seriesUsuarioCsv) ? DBNull.Value : seriesUsuarioCsv;

                cmd.Parameters.Add("@Tipo", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(tipo) ? DBNull.Value : tipo.Trim();

                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value =
                    fi.HasValue ? fi.Value : DBNull.Value;

                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                    ff.HasValue ? ff.Value : DBNull.Value;

                var list = new List<object>();

                using var dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        folio = dr.IsDBNull(0) ? "" : dr.GetString(0),
                        serie = dr.IsDBNull(1) ? "" : dr.GetString(1),
                        cliente = dr.IsDBNull(2) ? "" : dr.GetString(2),
                        kgPedidos = dr.IsDBNull(3) ? 0m : Convert.ToDecimal(dr.GetValue(3)),
                        cajasPedidas = dr.IsDBNull(4) ? 0m : Convert.ToDecimal(dr.GetValue(4)),
                        kgSurtidos = dr.IsDBNull(5) ? 0m : Convert.ToDecimal(dr.GetValue(5)),
                        cajasSurtidas = dr.IsDBNull(6) ? 0m : Convert.ToDecimal(dr.GetValue(6)),
                        gapKg = dr.IsDBNull(7) ? 0m : Convert.ToDecimal(dr.GetValue(7)),
                        gapCajas = dr.IsDBNull(8) ? 0m : Convert.ToDecimal(dr.GetValue(8)),
                        fecha = dr.IsDBNull(9) ? (DateTime?)null : Convert.ToDateTime(dr.GetValue(9)),
                        tipo = dr.IsDBNull(10) ? "" : dr.GetString(10)
                    });
                }

                return Json(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    error = ex.GetBaseException().Message
                });
            }
        }


        [HttpGet]
        public async Task<IActionResult> ObtenerSurtidosMeatDetalle(
            string folio,
            string tipo,
            string serie = "",
            CancellationToken ct = default)
        {
            try
            {
                var seriesUsuarioCsv = await ObtenerSeriesPermitidasCsvActualAsync(ct);

                using var cn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                cn.Open();

                string sql = "";

                if ((tipo ?? "").Trim().ToUpper() == "OV")
                {
                    sql = @"
WITH PedidoDetOV AS (
    SELECT
        a.U_DocMeat AS DocumentoSurtidoId,
        a.ConsecutivoOV AS Folio,
        b.ProductoCodigo AS SKU,
        b.ProductoNombre AS Producto,
        SUM(b.KilosCaja) AS KgPedidos,
        SUM(b.Cajas) AS CajasPedidas
    FROM Subpedido a
    INNER JOIN SubpedidoProductos b
        ON a.Id = b.SubpedidoId
    INNER JOIN OrdenVenta ov
        ON a.OrdenVentaId = ov.Id 
    WHERE a.ConsecutivoOV = @Folio
      AND ov.Estatus <> 0
      AND (
            @Serie IS NULL
            OR @Serie = ''
            OR UPPER(LTRIM(RTRIM(ov.Serie))) = UPPER(LTRIM(RTRIM(@Serie)))
      )
      AND (
            @SeriesUsuario IS NULL
            OR @SeriesUsuario = ''
            OR EXISTS (
                SELECT 1
                FROM STRING_SPLIT(@SeriesUsuario, ',') SU
                WHERE UPPER(LTRIM(RTRIM(SU.value))) = UPPER(LTRIM(RTRIM(ov.Serie)))
            )
      )
    GROUP BY
        a.U_DocMeat,
        a.ConsecutivoOV,
        b.ProductoCodigo,
        b.ProductoNombre
),
SurtidoDetOV AS (
    SELECT
        e.SolicitudSurtidoId AS DocumentoSurtidoId,
        e.Articulo AS SKU,
        SUM(ISNULL(e.Kg, 0)) AS KgSurtidos,
        SUM(ISNULL(e.Cajas, 0)) AS CajasSurtidas
    FROM SurtidoDetalle e
    GROUP BY
        e.SolicitudSurtidoId,
        e.Articulo
)
SELECT
    p.Folio,
    p.SKU,
    p.Producto,
    p.KgPedidos,
    p.CajasPedidas,
    ISNULL(s.KgSurtidos, 0) AS KgSurtidos,
    ISNULL(s.CajasSurtidas, 0) AS CajasSurtidas,
    ROUND((ISNULL(s.KgSurtidos, 0) * 100.0) / NULLIF(p.KgPedidos, 0), 0) AS GAPKg,
    ROUND((ISNULL(s.CajasSurtidas, 0) * 100.0) / NULLIF(p.CajasPedidas, 0), 0) AS GAPCaja,
    'OV' AS Tipo
FROM PedidoDetOV p
LEFT JOIN SurtidoDetOV s
    ON TRY_CAST(p.DocumentoSurtidoId AS INT) = s.DocumentoSurtidoId
   AND p.SKU = s.SKU
ORDER BY p.SKU;";
                }
                else
                {
                    // Transferencias: NO se filtran por UsuarioSerie ni por Serie.
                    sql = @"
WITH PedidoDetTR AS (
    SELECT
        a.TransferenciaId,
        c.Consecutivo AS Folio,
        b.ProductoCodigo AS SKU,
        d.ProductoNombre AS Producto,
        SUM(b.CantidadKg) AS KgPedidos,
        SUM(b.Cajas) AS CajasPedidas
    FROM PedidosTransferencia a
    INNER JOIN PedidosTransferenciaDetalle b
        ON a.Id = b.PedidoTransferenciaId
    INNER JOIN Transferencias c
        ON a.TransferenciaId = c.Id
    INNER JOIN ArticuloSap d
        ON b.ProductoCodigo = d.ProductoCodigo
    WHERE c.Consecutivo = @Folio
    GROUP BY
        a.TransferenciaId,
        c.Consecutivo,
        b.ProductoCodigo,
        d.ProductoNombre
),
SurtidoDetTR AS (
    SELECT
        d.TransferenciaId,
        d.Sku AS SKU,
        SUM(ISNULL(d.KgSurtido, 0)) AS KgSurtidos,
        SUM(ISNULL(d.CajasSurtidas, 0)) AS CajasSurtidas
    FROM TransferenciaSurtido d
    GROUP BY
        d.TransferenciaId,
        d.Sku
)
SELECT
    p.Folio,
    p.SKU,
    p.Producto,
    p.KgPedidos,
    p.CajasPedidas,
    ISNULL(s.KgSurtidos, 0) AS KgSurtidos,
    ISNULL(s.CajasSurtidas, 0) AS CajasSurtidas,
    ROUND((ISNULL(s.KgSurtidos, 0) * 100.0) / NULLIF(p.KgPedidos, 0), 0) AS GAPKg,
    ROUND((ISNULL(s.CajasSurtidas, 0) * 100.0) / NULLIF(p.CajasPedidas, 0), 0) AS GAPCaja,
    'TR' AS Tipo
FROM PedidoDetTR p
LEFT JOIN SurtidoDetTR s
    ON p.TransferenciaId = s.TransferenciaId
   AND p.SKU = s.SKU
ORDER BY p.SKU;";
                }

                using var cmd = new SqlCommand(sql, cn);

                cmd.Parameters.Add("@Folio", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(folio) ? DBNull.Value : folio.Trim();

                cmd.Parameters.Add("@Serie", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(serie) ? DBNull.Value : serie.Trim();

                cmd.Parameters.Add("@SeriesUsuario", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(seriesUsuarioCsv) ? DBNull.Value : seriesUsuarioCsv;

                var list = new List<object>();

                using var dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        folio = dr.IsDBNull(0) ? "" : dr.GetString(0),
                        sku = dr.IsDBNull(1) ? "" : dr.GetString(1),
                        producto = dr.IsDBNull(2) ? "" : dr.GetString(2),
                        kgPedidos = dr.IsDBNull(3) ? 0m : Convert.ToDecimal(dr.GetValue(3)),
                        cajasPedidas = dr.IsDBNull(4) ? 0m : Convert.ToDecimal(dr.GetValue(4)),
                        kgSurtidos = dr.IsDBNull(5) ? 0m : Convert.ToDecimal(dr.GetValue(5)),
                        cajasSurtidas = dr.IsDBNull(6) ? 0m : Convert.ToDecimal(dr.GetValue(6)),
                        gapKg = dr.IsDBNull(7) ? 0m : Convert.ToDecimal(dr.GetValue(7)),
                        gapCaja = dr.IsDBNull(8) ? 0m : Convert.ToDecimal(dr.GetValue(8)),
                        tipo = dr.IsDBNull(9) ? "" : dr.GetString(9)
                    });
                }

                return Json(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    error = ex.GetBaseException().Message
                });
            }
        }


        [HttpGet]
        public async Task<IActionResult> ObtenerSurtidosMeatDetalleExcel(
            string cliente,
            string folio,
            string serie,
            string fechaInicio,
            string fechaFin,
            string tipo = "",
            CancellationToken ct = default)
        {
            try
            {
                var seriesUsuarioCsv = await ObtenerSeriesPermitidasCsvActualAsync(ct);

                using var cn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                cn.Open();

                DateTime? fi = null;
                DateTime? ff = null;

                if (!string.IsNullOrWhiteSpace(fechaInicio))
                    fi = Convert.ToDateTime(fechaInicio);

                if (!string.IsNullOrWhiteSpace(fechaFin))
                    ff = Convert.ToDateTime(fechaFin);

                var sql = @"
WITH BaseOV AS (
    SELECT
        a.ConsecutivoOV AS Folio,
        LTRIM(RTRIM(ISNULL(c.Serie, ''))) AS Serie,
        d.Nombrecliente AS Cliente,
        c.FechaEmbarque AS Fecha,
        'OV' AS Tipo,
        a.U_DocMeat AS DocumentoSurtidoId,
        b.ProductoCodigo AS SKU,
        b.ProductoNombre AS Producto,
        SUM(b.KilosCaja) AS KgPedidos,
        SUM(b.Cajas) AS CajasPedidas
    FROM Subpedido a
    INNER JOIN SubpedidoProductos b
        ON a.Id = b.SubpedidoId
    INNER JOIN OrdenVenta c
        ON a.OrdenVentaId = c.Id
    INNER JOIN ClienteSap d
        ON a.Cliente = d.Cliente
 AND ISNULL(d.AplicaPresupuesto, 0) = 1
    WHERE c.Estatus <> 0
    GROUP BY
        a.ConsecutivoOV,
        c.Serie,
        d.Nombrecliente,
        c.FechaEmbarque,
        a.U_DocMeat,
        b.ProductoCodigo,
        b.ProductoNombre
),
SurtidoOV AS (
    SELECT
        e.SolicitudSurtidoId AS DocumentoSurtidoId,
        e.Articulo AS SKU,
        SUM(ISNULL(e.Kg, 0)) AS KgSurtidos,
        SUM(ISNULL(e.Cajas, 0)) AS CajasSurtidas
    FROM SurtidoDetalle e
    GROUP BY
        e.SolicitudSurtidoId,
        e.Articulo
),
DetalleOV AS (
    SELECT
        p.Folio,
        p.Serie,
        p.Cliente,
        p.Fecha,
        p.Tipo,
        p.SKU,
        p.Producto,
        p.KgPedidos,
        p.CajasPedidas,
        ISNULL(s.KgSurtidos, 0) AS KgSurtidos,
        ISNULL(s.CajasSurtidas, 0) AS CajasSurtidas,
        ROUND((ISNULL(s.KgSurtidos, 0) * 100.0) / NULLIF(p.KgPedidos, 0), 0) AS GAPKg,
        ROUND((ISNULL(s.CajasSurtidas, 0) * 100.0) / NULLIF(p.CajasPedidas, 0), 0) AS GAPCaja
    FROM BaseOV p
    LEFT JOIN SurtidoOV s
        ON TRY_CAST(p.DocumentoSurtidoId AS INT) = s.DocumentoSurtidoId
       AND p.SKU = s.SKU
),
BaseTR AS (
    SELECT
        c.Consecutivo AS Folio,
        '' AS Serie,
        a.Destino AS Cliente,
        c.FechaSolicitud AS Fecha,
        'TR' AS Tipo,
        a.TransferenciaId,
        b.ProductoCodigo AS SKU,
        d.ProductoNombre AS Producto,
        SUM(b.CantidadKg) AS KgPedidos,
        SUM(b.Cajas) AS CajasPedidas
    FROM PedidosTransferencia a
    INNER JOIN PedidosTransferenciaDetalle b
        ON a.Id = b.PedidoTransferenciaId
    INNER JOIN Transferencias c
        ON a.TransferenciaId = c.Id
    INNER JOIN ArticuloSap d
        ON b.ProductoCodigo = d.ProductoCodigo
    WHERE c.Estatus <> 0
    GROUP BY
        c.Consecutivo,
        a.Destino,
        c.FechaSolicitud,
        a.TransferenciaId,
        b.ProductoCodigo,
        d.ProductoNombre
),
SurtidoTR AS (
    SELECT
        d.TransferenciaId,
        d.Sku AS SKU,
        SUM(ISNULL(d.KgSurtido, 0)) AS KgSurtidos,
        SUM(ISNULL(d.CajasSurtidas, 0)) AS CajasSurtidas
    FROM TransferenciaSurtido d
    GROUP BY
        d.TransferenciaId,
        d.Sku
),
DetalleTR AS (
    SELECT
        p.Folio,
        p.Serie,
        p.Cliente,
        p.Fecha,
        p.Tipo,
        p.SKU,
        p.Producto,
        p.KgPedidos,
        p.CajasPedidas,
        ISNULL(s.KgSurtidos, 0) AS KgSurtidos,
        ISNULL(s.CajasSurtidas, 0) AS CajasSurtidas,
        ROUND((ISNULL(s.KgSurtidos, 0) * 100.0) / NULLIF(p.KgPedidos, 0), 0) AS GAPKg,
        ROUND((ISNULL(s.CajasSurtidas, 0) * 100.0) / NULLIF(p.CajasPedidas, 0), 0) AS GAPCaja
    FROM BaseTR p
    LEFT JOIN SurtidoTR s
        ON p.TransferenciaId = s.TransferenciaId
       AND p.SKU = s.SKU
),
FinalDetalle AS (
    SELECT * FROM DetalleOV
    UNION ALL
    SELECT * FROM DetalleTR
)
SELECT *
FROM FinalDetalle
WHERE (@Cliente IS NULL OR @Cliente = '' OR Cliente LIKE '%' + @Cliente + '%')
  AND (@Folio IS NULL OR @Folio = '' OR Folio LIKE '%' + @Folio + '%')
  AND (@Tipo IS NULL OR @Tipo = '' OR Tipo = @Tipo)
  AND (@FechaInicio IS NULL OR CONVERT(date, Fecha) >= CONVERT(date, @FechaInicio))
  AND (@FechaFin IS NULL OR CONVERT(date, Fecha) <= CONVERT(date, @FechaFin))

  -- Filtro seleccionado en pantalla:
  -- OV sí filtra por serie seleccionada.
  -- TR siempre pasa.
  AND (
        @Serie IS NULL
        OR @Serie = ''
        OR Tipo = 'TR'
        OR (
            Tipo = 'OV'
            AND EXISTS (
                SELECT 1
                FROM STRING_SPLIT(@Serie, ',') S
                WHERE UPPER(LTRIM(RTRIM(S.value))) = UPPER(LTRIM(RTRIM(Serie)))
            )
        )
  )

  -- Seguridad por series configuradas al usuario:
  -- OV sí filtra por UsuarioSerie.
  -- TR siempre pasa.
  AND (
        @SeriesUsuario IS NULL
        OR @SeriesUsuario = ''
        OR Tipo = 'TR'
        OR (
            Tipo = 'OV'
            AND EXISTS (
                SELECT 1
                FROM STRING_SPLIT(@SeriesUsuario, ',') SU
                WHERE UPPER(LTRIM(RTRIM(SU.value))) = UPPER(LTRIM(RTRIM(Serie)))
            )
        )
  )
ORDER BY Fecha DESC, Folio, SKU;
";

                using var cmd = new SqlCommand(sql, cn);

                cmd.Parameters.Add("@Cliente", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(cliente) ? DBNull.Value : cliente.Trim();

                cmd.Parameters.Add("@Folio", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(folio) ? DBNull.Value : folio.Trim();

                cmd.Parameters.Add("@Serie", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(serie) ? DBNull.Value : serie.Trim();

                cmd.Parameters.Add("@SeriesUsuario", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(seriesUsuarioCsv) ? DBNull.Value : seriesUsuarioCsv;

                cmd.Parameters.Add("@Tipo", SqlDbType.VarChar).Value =
                    string.IsNullOrWhiteSpace(tipo) ? DBNull.Value : tipo.Trim();

                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value =
                    fi.HasValue ? fi.Value : DBNull.Value;

                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                    ff.HasValue ? ff.Value : DBNull.Value;

                var list = new List<object>();

                using var dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    list.Add(new
                    {
                        folio = dr.IsDBNull(0) ? "" : dr.GetString(0),
                        serie = dr.IsDBNull(1) ? "" : dr.GetString(1),
                        cliente = dr.IsDBNull(2) ? "" : dr.GetString(2),
                        fecha = dr.IsDBNull(3) ? (DateTime?)null : Convert.ToDateTime(dr.GetValue(3)),
                        tipo = dr.IsDBNull(4) ? "" : dr.GetString(4),
                        sku = dr.IsDBNull(5) ? "" : dr.GetString(5),
                        producto = dr.IsDBNull(6) ? "" : dr.GetString(6),
                        kgPedidos = dr.IsDBNull(7) ? 0m : Convert.ToDecimal(dr.GetValue(7)),
                        cajasPedidas = dr.IsDBNull(8) ? 0m : Convert.ToDecimal(dr.GetValue(8)),
                        kgSurtidos = dr.IsDBNull(9) ? 0m : Convert.ToDecimal(dr.GetValue(9)),
                        cajasSurtidas = dr.IsDBNull(10) ? 0m : Convert.ToDecimal(dr.GetValue(10)),
                        gapKg = dr.IsDBNull(11) ? 0m : Convert.ToDecimal(dr.GetValue(11)),
                        gapCaja = dr.IsDBNull(12) ? 0m : Convert.ToDecimal(dr.GetValue(12))
                    });
                }

                return Json(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    error = ex.GetBaseException().Message
                });
            }
        }














        /// <summary>
        /// AQUI EMPIEZA EL METODO PARA GUARDAR IMAGENES DE LA COMPETENCIA
        /// </summary>
        /// <param ></param>
        /// <returns></returns>




        [HttpPost]
        public async Task<IActionResult> GuardarPrecioCompetencia([FromBody] PrecioCompetenciaDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Sku))
                return BadRequest("SKU requerido.");

            var sku = dto.Sku.Trim().ToUpperInvariant();
            DateTime? fechaCorte = dto.FechaCorte?.Date;

            var existente = await _context.PrecioCompetenciaSemana
                .FirstOrDefaultAsync(x => x.Sku == sku && x.FechaCorte == fechaCorte);

            if (existente == null)
            {
                existente = new PrecioCompetenciaSemana
                {
                    FechaRegistro = DateTime.Now,
                    FechaCorte = fechaCorte,
                    Sku = sku,
                    Denes = dto.Denes,
                    Tc = dto.Tc,
                    Freasa = dto.Freasa,
                    Comentarios = dto.Comentarios,
                    UsuarioRegistro = User?.Identity?.Name ?? "SYSTEM"
                };

                _context.PrecioCompetenciaSemana.Add(existente);
            }
            else
            {
                existente.Denes = dto.Denes;
                existente.Tc = dto.Tc;
                existente.Freasa = dto.Freasa;
                existente.Comentarios = dto.Comentarios;
                existente.UsuarioRegistro = User?.Identity?.Name ?? "SYSTEM";
                existente.FechaRegistro = DateTime.Now;
            }

            await _context.SaveChangesAsync();

            return Json(new { ok = true, mensaje = "Competencia guardada correctamente." });
        }


        [HttpPost]
        public IActionResult ObtenerAnalisisSemanal([FromBody] AnalisisSemanalRequest request)
        {
            try
            {
                using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
                cn.Open();

                if (request == null)
                    return BadRequest("Solicitud inválida.");

                DateTime fc = request.FechaCorte?.Date ?? DateTime.Today;

                var skus = request.Skus?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToList() ?? new List<string>();

                var skusCsv = string.Join(",", skus);
                var master = request.Master;

                var fechaInvActual = fc.Date;
                var fechaInvAnterior = fc.Date.AddDays(-7);

                var colInvAnterior = $"inventario {fechaInvAnterior:dd/MM/yyyy}";
                var colInvActual = $"inventario {fechaInvActual:dd/MM/yyyy}";

                var nomSem1 = $"Semana {ISOWeek.GetWeekOfYear(fc.AddDays(-21))}";
                var nomSem2 = $"Semana {ISOWeek.GetWeekOfYear(fc.AddDays(-14))}";
                var nomSem3 = $"Semana {ISOWeek.GetWeekOfYear(fc.AddDays(-7))}";

                var sql = @"
SET NOCOUNT ON;

DECLARE @FechaCorte DATE = @pFechaCorte;

-- Inventario lunes a lunes
DECLARE @FechaInvActual DATE   = @FechaCorte;
DECLARE @FechaInvAnterior DATE = DATEADD(DAY, -7, @FechaCorte);

-- Ventas
DECLARE @DesdeSemana7 DATE  = DATEADD(DAY, -6, @FechaCorte);
DECLARE @DesdeSemana14 DATE = DATEADD(DAY, -13, @FechaCorte);
DECLARE @HastaSemana14 DATE = DATEADD(DAY, -7, @FechaCorte);
DECLARE @DesdeSemana15 DATE = DATEADD(DAY, -20, @FechaCorte);
DECLARE @HastaSemana15 DATE = DATEADD(DAY, -14, @FechaCorte);

DECLARE @ColInvAnterior SYSNAME = N'inventario ' + CONVERT(VARCHAR(10), @FechaInvAnterior, 103);
DECLARE @ColInvActual   SYSNAME = N'inventario ' + CONVERT(VARCHAR(10), @FechaInvActual, 103);

DECLARE @sql NVARCHAR(MAX) = N'
WITH Productos AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
        ProductoNombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), ''''), a.ProductoCodigo),
        U_MASTER = UPPER(LTRIM(RTRIM(ISNULL(a.U_MASTER, '''')))),
        ClasificacionNombre = ISNULL(cp.Nombre, ''POR DEFINIR'')
    FROM dbo.ArticuloSap a
    LEFT JOIN dbo.ClasificacionProduccion cp
        ON a.U_Clas_Prod = cp.ClasificacionId
),
InventarioFechas AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(iam.Sku))),
        InventarioAnterior = SUM(CASE WHEN iam.FechaInventario = @FechaInvAnterior THEN CAST(ISNULL(iam.PesoNeto, 0) AS DECIMAL(18,4)) ELSE 0 END),
        InventarioActual   = SUM(CASE WHEN iam.FechaInventario = @FechaInvActual   THEN CAST(ISNULL(iam.PesoNeto, 0) AS DECIMAL(18,4)) ELSE 0 END)
    FROM dbo.InventarioAlmacenado_Meat iam
    WHERE ISNULL(iam.Sku, '''') <> ''''
      AND iam.FechaInventario IN (@FechaInvAnterior, @FechaInvActual)
    GROUP BY UPPER(LTRIM(RTRIM(iam.Sku)))
),
PrecioActual AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(c.ProductoCodigo))),
        PrecioLista = MAX(CAST(ISNULL(c.Precio, 0) AS DECIMAL(18,4)))
    FROM dbo.CatalogoPrecioSap c
    WHERE ISNULL(c.ProductoCodigo, '''') <> ''''
    GROUP BY UPPER(LTRIM(RTRIM(c.ProductoCodigo)))
),

/* =========================
   KG REALES VENDIDOS
   ========================= */
VentaOV AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
        FechaMov = CAST(se.FechaValidacion AS DATE),
        Kg = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
    FROM dbo.SurtidoEncabezado se
    INNER JOIN dbo.SurtidoDetalle sd
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    WHERE se.FechaValidacion IS NOT NULL
    GROUP BY
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        CAST(se.FechaValidacion AS DATE)
),
VentaTR AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
        FechaMov = CAST(t.FechaSolicitud AS DATE),
        Kg = SUM(CAST(ISNULL(ts.KgSurtido, 0) AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    INNER JOIN dbo.Transferencias t
        ON t.Id = ts.TransferenciaId
    WHERE t.FechaSolicitud IS NOT NULL
      AND ts.KgSurtido > 0
    GROUP BY
        UPPER(LTRIM(RTRIM(ts.Sku))),
        CAST(t.FechaSolicitud AS DATE)
),
VentaReal AS (
    SELECT SKU, FechaMov, Kg FROM VentaOV
    UNION ALL
    SELECT SKU, FechaMov, Kg FROM VentaTR
),
SemanasKg AS (
    SELECT
        vr.SKU,
        KgSemana7  = SUM(CASE WHEN vr.FechaMov >= @DesdeSemana7  AND vr.FechaMov <= @FechaCorte    THEN vr.Kg ELSE 0 END),
        KgSemana14 = SUM(CASE WHEN vr.FechaMov >= @DesdeSemana14 AND vr.FechaMov <= @HastaSemana14 THEN vr.Kg ELSE 0 END),
        KgSemana15 = SUM(CASE WHEN vr.FechaMov >= @DesdeSemana15 AND vr.FechaMov <= @HastaSemana15 THEN vr.Kg ELSE 0 END)
    FROM VentaReal vr
    GROUP BY vr.SKU
),

/* =========================
   PRECIOS REALES DE VENTA
   ========================= */
VentasPrecioOV AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        FechaVenta = CAST(o.FechaEntrega AS DATE),
        PrecioVenta = CAST(ISNULL(op.Precio, 0) AS DECIMAL(18,4))
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.OrdenVentaProducto op
        ON op.PedidoId = o.Id
    WHERE ISNULL(op.ProductoCodigo, '''') <> ''''
      AND o.FechaEntrega IS NOT NULL
      AND ISNULL(op.Precio, 0) > 0
),
SemanasPrecio AS (
    SELECT
        vp.SKU,
        Semana7  = CAST(AVG(CASE WHEN vp.FechaVenta >= @DesdeSemana7  AND vp.FechaVenta <= @FechaCorte    THEN vp.PrecioVenta END) AS DECIMAL(18,4)),
        Semana14 = CAST(AVG(CASE WHEN vp.FechaVenta >= @DesdeSemana14 AND vp.FechaVenta <= @HastaSemana14 THEN vp.PrecioVenta END) AS DECIMAL(18,4)),
        Semana15 = CAST(AVG(CASE WHEN vp.FechaVenta >= @DesdeSemana15 AND vp.FechaVenta <= @HastaSemana15 THEN vp.PrecioVenta END) AS DECIMAL(18,4))
    FROM VentasPrecioOV vp
    GROUP BY vp.SKU
),
UltimoPrecioVenta AS (
    SELECT
        q.SKU,
        q.PrecioVenta AS PpVentaReal
    FROM (
        SELECT
            vp.SKU,
            vp.PrecioVenta,
            vp.FechaVenta,
            rn = ROW_NUMBER() OVER (
                PARTITION BY vp.SKU
                ORDER BY vp.FechaVenta DESC, vp.PrecioVenta DESC
            )
        FROM VentasPrecioOV vp
        WHERE vp.FechaVenta <= @FechaCorte
    ) q
    WHERE q.rn = 1
),

PedidosOV AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        KgPendiente = SUM(
            CAST(
                CASE
                    WHEN o.Estatus = 5 AND os.Id IS NOT NULL THEN 0
                    ELSE CASE
                        WHEN (CAST(op.Peso AS DECIMAL(18,4)) - ISNULL(sa.KgSurtido, 0)) < 0 THEN 0
                        ELSE (CAST(op.Peso AS DECIMAL(18,4)) - ISNULL(sa.KgSurtido, 0))
                    END
                END
            AS DECIMAL(18,4))
        )
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.Series ser
        ON o.Serie = ser.NombreSerie
    INNER JOIN dbo.OrdenVentaProducto op
        ON op.PedidoId = o.Id
    LEFT JOIN (
        SELECT DISTINCT o2.Id
        FROM dbo.OrdenVenta o2
        JOIN dbo.Subpedido sp2
            ON sp2.OrdenVentaId = o2.Id
        JOIN dbo.SurtidoEncabezado se2
            ON se2.SolicitudSurtidoId = sp2.U_DocMeat
    ) os
        ON os.Id = o.Id
    LEFT JOIN (
        SELECT
            PedidoId = o3.Id,
            SKU = UPPER(LTRIM(RTRIM(sd3.Articulo))),
            KgSurtido = SUM(CAST(sd3.Kg AS DECIMAL(18,4)))
        FROM dbo.OrdenVenta o3
        JOIN dbo.Subpedido sp3
            ON sp3.OrdenVentaId = o3.Id
        JOIN dbo.SurtidoEncabezado se3
            ON se3.SolicitudSurtidoId = sp3.U_DocMeat
        JOIN dbo.SurtidoDetalle sd3
            ON sd3.SolicitudSurtidoId = se3.SolicitudSurtidoId
        WHERE se3.FechaValidacion IS NOT NULL
        GROUP BY
            o3.Id,
            UPPER(LTRIM(RTRIM(sd3.Articulo)))
    ) sa
        ON sa.PedidoId = o.Id
       AND sa.SKU = UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
    WHERE o.FechaEntrega IS NOT NULL
      AND TRY_CONVERT(DATE, o.FechaEntrega) >= @FechaCorte
      AND o.Estatus BETWEEN 1 AND 5
      AND ser.Sucursal = ''MATRIZ''
    GROUP BY
        UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
),
PedidosTR AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
        KgPendiente = SUM(
            CAST(
                CASE
                    WHEN (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0)) < 0 THEN 0
                    ELSE (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0))
                END
            AS DECIMAL(18,4))
        )
    FROM dbo.Transferencias t
    JOIN dbo.TransferenciaDetalles td
        ON td.TransferenciaId = t.Id
    LEFT JOIN (
        SELECT
            ts.TransferenciaId,
            SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
            KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
        FROM dbo.TransferenciaSurtido ts
        GROUP BY
            ts.TransferenciaId,
            UPPER(LTRIM(RTRIM(ts.Sku)))
    ) tsa
        ON tsa.TransferenciaId = t.Id
       AND tsa.SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
    WHERE t.FechaSolicitud IS NOT NULL
      AND TRY_CONVERT(DATE, t.FechaSolicitud) >= @FechaCorte
      AND t.Estatus BETWEEN 1 AND 4
    GROUP BY
        UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
),
PedidosFuturo AS (
    SELECT
        SKU,
        Pedidos = SUM(KgPendiente)
    FROM (
        SELECT SKU, KgPendiente FROM PedidosOV
        UNION ALL
        SELECT SKU, KgPendiente FROM PedidosTR
    ) x
    GROUP BY SKU
),

/* =========================
   COMPETENCIA - NUEVA TABLA
   dbo.CompetidorPrecio
   dbo.PrecioCompetenciaDetalle
   ========================= */
CompetenciaPorCompetidor AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(d.SkuPropio))),
        CompetidorId = d.CompetidorId,
        Competidor = UPPER(LTRIM(RTRIM(c.Nombre))),
        CompetidorNombre = MAX(c.Nombre),
        PrecioCompetidor = CAST(AVG(CAST(d.PrecioCompetencia AS DECIMAL(18,4))) AS DECIMAL(18,4))
    FROM dbo.PrecioCompetenciaDetalle d
    INNER JOIN dbo.CompetidorPrecio c
        ON c.CompetidorId = d.CompetidorId
    WHERE d.FechaCorte = @pFechaCorte
      AND ISNULL(d.SkuPropio, '''') <> ''''
      AND ISNULL(d.PrecioCompetencia, 0) > 0
    GROUP BY
        UPPER(LTRIM(RTRIM(d.SkuPropio))),
        d.CompetidorId,
        UPPER(LTRIM(RTRIM(c.Nombre)))
),
Competencia AS (
    SELECT
        SKU,

        PromCompetencia = CAST(AVG(NULLIF(PrecioCompetidor, 0)) AS DECIMAL(18,4)),

        NumCompetidores = COUNT(DISTINCT CompetidorId),

        Competidores = STRING_AGG(
            CONCAT(
                CompetidorNombre,
                '': $'',
                CONVERT(VARCHAR(30), CAST(PrecioCompetidor AS DECIMAL(18,2)))
            ),
            '' | ''
        ),

        Comentarios = STRING_AGG(
            CONCAT(
                CompetidorNombre,
                '': $'',
                CONVERT(VARCHAR(30), CAST(PrecioCompetidor AS DECIMAL(18,2)))
            ),
            '' | ''
        )
    FROM CompetenciaPorCompetidor
    GROUP BY SKU
),
Base AS (
    SELECT
        SKU = COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU, sp.SKU, upv.SKU, c.SKU, pf.SKU),
        ProductoNombre = ISNULL(p.ProductoNombre, ''''),
        U_MASTER = ISNULL(p.U_MASTER, ''''),
        ClasificacionNombre = ISNULL(p.ClasificacionNombre, ''POR DEFINIR''),
        InventarioAnterior = ISNULL(inv.InventarioAnterior, 0),
        InventarioActual = ISNULL(inv.InventarioActual, 0),
        PrecioLista = ISNULL(pa.PrecioLista, 0),
        KgSemana7 = ISNULL(sk.KgSemana7, 0),
        KgSemana14 = ISNULL(sk.KgSemana14, 0),
        KgSemana15 = ISNULL(sk.KgSemana15, 0),
        PrecioSemana7 = ISNULL(sp.Semana7, 0),
        PrecioSemana14 = ISNULL(sp.Semana14, 0),
        PrecioSemana15 = ISNULL(sp.Semana15, 0),
        PpVentaReal = ISNULL(upv.PpVentaReal, 0),
        Pedidos = ISNULL(pf.Pedidos, 0),
       PromCompetencia = ISNULL(c.PromCompetencia, 0),
NumCompetidores = ISNULL(c.NumCompetidores, 0),
Competidores = ISNULL(c.Competidores, ''''),
Comentarios = ISNULL(c.Comentarios, '''')
    FROM Productos p
    FULL OUTER JOIN InventarioFechas inv
        ON p.SKU = inv.SKU
    FULL OUTER JOIN PrecioActual pa
        ON COALESCE(p.SKU, inv.SKU) = pa.SKU
    FULL OUTER JOIN SemanasKg sk
        ON COALESCE(p.SKU, inv.SKU, pa.SKU) = sk.SKU
    FULL OUTER JOIN SemanasPrecio sp
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU) = sp.SKU
    FULL OUTER JOIN UltimoPrecioVenta upv
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU, sp.SKU) = upv.SKU
    FULL OUTER JOIN Competencia c
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU, sp.SKU, upv.SKU) = c.SKU
    FULL OUTER JOIN PedidosFuturo pf
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU, sp.SKU, upv.SKU, c.SKU) = pf.SKU
),
Calc AS (
    SELECT
        [CLASIFICACION] = ClasificacionNombre,
        [master] = CASE WHEN NULLIF(U_MASTER, '''') IS NOT NULL THEN U_MASTER ELSE ''GENERAL'' END,
        [SKU] = SKU,
        [SKU Prod] = ProductoNombre,
        [Inv. Inicial Refer.] = CAST(
            InventarioActual + KgSemana7 + KgSemana14 + KgSemana15 + Pedidos
        AS DECIMAL(18,2)),
        InvAnteriorValor = CAST(InventarioAnterior AS DECIMAL(18,2)),
        InvActualValor = CAST(InventarioActual AS DECIMAL(18,2)),
        [Inventario Ideal] = CAST(
            CASE
                WHEN (KgSemana7 + KgSemana14 + KgSemana15) > 0
                    THEN ((KgSemana7 + KgSemana14 + KgSemana15) / 23.0) * 14.0
                ELSE 0
            END
        AS DECIMAL(18,2)),
        [PEDIDOS] = CAST(Pedidos AS DECIMAL(18,2)),
        KgNetosVenta = CAST(
            KgSemana7 + KgSemana14 + KgSemana15
        AS DECIMAL(18,2)),
        [DÍAS DE INVENTARIO] = CAST(
            CASE
                WHEN ((KgSemana7 + KgSemana14 + KgSemana15) / 23.0) > 0
                    THEN InventarioActual / ((KgSemana7 + KgSemana14 + KgSemana15) / 23.0)
                ELSE 0
            END
        AS DECIMAL(18,4)),
        [Semana 7] = CAST(PrecioSemana7 AS DECIMAL(18,2)),
        [Semana 14] = CAST(PrecioSemana14 AS DECIMAL(18,2)),
        [Semana 15] = CAST(PrecioSemana15 AS DECIMAL(18,2)),
        [pp-venta real] = CAST(
            CASE
                WHEN PpVentaReal > 0 THEN PpVentaReal
                ELSE PrecioLista
            END
        AS DECIMAL(18,2)),
       [PROM] = CAST(ISNULL(PromCompetencia, 0) AS DECIMAL(18,2)),
[COMPETIDORES] =
    CASE
        WHEN NumCompetidores > 0
            THEN CONCAT(''Competidores: '', NumCompetidores, '' | '', Competidores)
        ELSE ''''
    END,
[COMENTARIOS] = Comentarios
    FROM Base
)
SELECT
    [CLASIFICACION],
    [master],
    [SKU],
    [SKU Prod],
    [Inv. Inicial Refer.],
    InvAnteriorValor AS ' + QUOTENAME(@ColInvAnterior) + N',
    InvActualValor   AS ' + QUOTENAME(@ColInvActual) + N',
    [Inventario Ideal],
    [PEDIDOS],
    KgNetosVenta AS [Kg Netos venta],
    [DÍAS DE INVENTARIO],
    [Semana 7]  AS [Semana1Valor],
    [Semana 14] AS [Semana2Valor],
    [Semana 15] AS [Semana3Valor],
    [pp-venta real],
    [DIF INV] = CAST(InvActualValor - [Inventario Ideal] AS DECIMAL(18,2)),
  [PROM],
[COMPETIDORES],
[DIF PRECIO VS COMP] = CAST(
        CASE
            WHEN [PROM] > 0 THEN ([pp-venta real] / [PROM]) - 1
            ELSE 0
        END
    AS DECIMAL(6,4)),
    [RECOMENDACIÓN] =
        CASE
            WHEN [PEDIDOS] >= InvActualValor THEN ''SUBIR''
            WHEN [PEDIDOS] > (InvActualValor * 0.8) THEN ''MANTENER''
            WHEN [DÍAS DE INVENTARIO] > 30
              OR InvActualValor > ([Inventario Ideal] * 2) THEN
                CASE
                    WHEN [PEDIDOS] < (InvActualValor * 0.1)
                      OR KgNetosVenta < (InvActualValor * 0.3) THEN ''BAJAR''
                    ELSE ''MANTENER''
                END
            WHEN (
                CASE
                    WHEN [PROM] > 0 THEN ([pp-venta real] / [PROM]) - 1
                    ELSE 0
                END
            ) > 0.01 THEN ''BAJAR''
            ELSE ''MANTENER''
        END,
    [COMENTARIOS]
FROM Calc
WHERE (
        @pSkusCsv IS NULL
        OR @pSkusCsv = ''''
        OR EXISTS (
            SELECT 1
            FROM STRING_SPLIT(@pSkusCsv, '','') s
            WHERE UPPER(LTRIM(RTRIM(s.value))) = [SKU]
        )
      )
AND (@pMaster IS NULL OR @pMaster = '''' OR UPPER([master]) LIKE ''%'' + UPPER(@pMaster) + ''%'')
ORDER BY [master], [SKU];';

EXEC sp_executesql
    @sql,
    N'@pFechaCorte DATE,
      @FechaCorte DATE,
      @FechaInvActual DATE,
      @FechaInvAnterior DATE,
      @DesdeSemana7 DATE,
      @DesdeSemana14 DATE,
      @HastaSemana14 DATE,
      @DesdeSemana15 DATE,
      @HastaSemana15 DATE,
      @pSkusCsv NVARCHAR(MAX),
      @pMaster VARCHAR(200)',
    @pFechaCorte = @pFechaCorte,
    @FechaCorte = @FechaCorte,
    @FechaInvActual = @FechaInvActual,
    @FechaInvAnterior = @FechaInvAnterior,
    @DesdeSemana7 = @DesdeSemana7,
    @DesdeSemana14 = @DesdeSemana14,
    @HastaSemana14 = @HastaSemana14,
    @DesdeSemana15 = @DesdeSemana15,
    @HastaSemana15 = @HastaSemana15,
    @pSkusCsv = @pSkusCsv,
    @pMaster = @pMaster;
    
";

                using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.Add("@pFechaCorte", SqlDbType.Date).Value = fc;
                cmd.Parameters.Add("@pSkusCsv", SqlDbType.NVarChar).Value =
                string.IsNullOrWhiteSpace(skusCsv) ? DBNull.Value : skusCsv;

                cmd.Parameters.Add("@pMaster", SqlDbType.VarChar).Value =
       string.IsNullOrWhiteSpace(master) ? DBNull.Value : master.Trim().ToUpperInvariant();

                var list = new List<AnalisisSemanalPrecioDto>();

                using var dr = cmd.ExecuteReader();
                while (dr.Read())
                {
                    list.Add(new AnalisisSemanalPrecioDto
                    {
                        Clasificacion = dr["CLASIFICACION"] == DBNull.Value ? "" : Convert.ToString(dr["CLASIFICACION"]) ?? "",
                        Master = dr["master"] == DBNull.Value ? "" : Convert.ToString(dr["master"]) ?? "",
                        Sku = dr["SKU"] == DBNull.Value ? "" : Convert.ToString(dr["SKU"]) ?? "",
                        SkuProd = dr["SKU Prod"] == DBNull.Value ? "" : Convert.ToString(dr["SKU Prod"]) ?? "",

                        InvInicialRefer = dr["Inv. Inicial Refer."] == DBNull.Value ? 0m : Convert.ToDecimal(dr["Inv. Inicial Refer."]),

                        InventarioAnterior = dr[colInvAnterior] == DBNull.Value ? 0m : Convert.ToDecimal(dr[colInvAnterior]),
                        InventarioActual = dr[colInvActual] == DBNull.Value ? 0m : Convert.ToDecimal(dr[colInvActual]),

                        InventarioIdeal = dr["Inventario Ideal"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["Inventario Ideal"]),
                        Pedidos = dr["PEDIDOS"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["PEDIDOS"]),
                        KgVenta = dr["Kg Netos venta"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["Kg Netos venta"]),
                        DiasInventario = dr["DÍAS DE INVENTARIO"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["DÍAS DE INVENTARIO"]),

                        Semana1 = dr["Semana1Valor"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["Semana1Valor"]),
                        Semana2 = dr["Semana2Valor"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["Semana2Valor"]),
                        Semana3 = dr["Semana3Valor"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["Semana3Valor"]),

                        PpVentaReal = dr["pp-venta real"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["pp-venta real"]),
                        Prom = dr["PROM"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["PROM"]),
                        Competidores = dr["COMPETIDORES"] == DBNull.Value ? "" : Convert.ToString(dr["COMPETIDORES"]) ?? "",
                        DifPct = dr["DIF PRECIO VS COMP"] == DBNull.Value ? 0m : Convert.ToDecimal(dr["DIF PRECIO VS COMP"]),
                        Recomendacion = dr["RECOMENDACIÓN"] == DBNull.Value ? "" : Convert.ToString(dr["RECOMENDACIÓN"]) ?? "",
                        Comentarios = dr["COMENTARIOS"] == DBNull.Value ? "" : Convert.ToString(dr["COMENTARIOS"]) ?? ""
                    });
                }

                return Json(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error en análisis semanal: " + ex.Message);
            }
        }





        private string ObtenerClasificacionAnalisis(string nombre)
        {
            var n = (nombre ?? "").Trim().ToUpperInvariant();

            if (n.Contains("SUPREMO")) return "B.PEDIDO";
            return "LINEA";
        }

        private string ObtenerMasterAnalisis(string nombre)
        {
            var n = (nombre ?? "").Trim().ToUpperInvariant();

            if (n.Contains("ARRACHERA")) return "ARRACHERA";
            if (n.Contains("RIB EYE")) return "RIB EYE";
            if (n.Contains("NEW YORK")) return "NEW YORK";
            if (n.Contains("TOP SIRLOIN")) return "TOP SIRLOIN";
            if (n.Contains("PULPA")) return "PULPA";
            if (n.Contains("DIEZMILLO")) return "DIEZMILLO";
            if (n.Contains("CHULETON")) return "CHULETON";

            return "GENERAL";
        }


        // ============================================================
        // DTOs
        // ============================================================

        public class CompetenciaLoteDto
        {
            public string Competidor { get; set; } = "";
            public string? Fecha { get; set; }
            public string? FechaCorte { get; set; }
            public List<CompetenciaRegistroDto> Registros { get; set; } = new();
        }

        public class CompetenciaRegistroDto
        {
            public string SkuPropio { get; set; } = string.Empty;

            public string CodigoCompetencia { get; set; } = string.Empty;
            public string NombreCompetencia { get; set; } = string.Empty;

            public decimal PrecioCompetencia { get; set; }
            public decimal? NuestroPrecio { get; set; }
        }

        public class CompetenciaProductoExtraidoDto
        {
            public string CodigoCompetencia { get; set; } = "";
            public string Nombre { get; set; } = "";
            public decimal Precio { get; set; }
            public string SkuPropio { get; set; } = "";
        }



        public class CompetenciaExtraidaDto
        {
            public string Competidor { get; set; } = string.Empty;
            public string Fecha { get; set; } = string.Empty;
            public List<CompetenciaProductoExtraidoDto> Productos { get; set; } = new();
        }

        [HttpPost]
        public async Task<IActionResult> GuardarPrecioPublicoDesdeAnalisis(
    [FromBody] GuardarPrecioPublicoDesdeAnalisisRequest request)
        {
            if (request == null)
                return BadRequest(new { ok = false, mensaje = "Solicitud inválida." });

            if (request.Registros == null || !request.Registros.Any())
                return BadRequest(new { ok = false, mensaje = "No se recibieron SKUs." });

            var registrosValidos = request.Registros
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductoCodigo) && x.Precio > 0)
                .GroupBy(x => x.ProductoCodigo.Trim().ToUpper())
                .Select(g => g.First())
                .ToList();

            var dtSkus = new DataTable();
            dtSkus.Columns.Add("ProductoCodigo", typeof(string));
            dtSkus.Columns.Add("Precio", typeof(decimal));
            dtSkus.Columns.Add("Master", typeof(string));
            dtSkus.Columns.Add("ProductoNombre", typeof(string));
            dtSkus.Columns.Add("Clasificacion", typeof(string));

            foreach (var r in registrosValidos)
            {
                dtSkus.Rows.Add(
                    r.ProductoCodigo.Trim().ToUpper(),
                    r.Precio,
                    r.Master ?? "",
                    r.ProductoNombre ?? "",
                    r.Clasificacion ?? ""
                );
            }

            var dtClientes = new DataTable();
            dtClientes.Columns.Add("Cliente", typeof(string));

            foreach (var cliente in request.Clientes.Distinct())
            {
                if (!string.IsNullOrWhiteSpace(cliente))
                {
                    dtClientes.Rows.Add(cliente.Trim());
                }
            }

            var usuario = User?.Identity?.Name ?? "Sistema";

            await using var cn = new SqlConnection(
                _configuration.GetConnectionString("DefaultConnection")
            );

            await using var cmd = new SqlCommand("dbo.sp_GuardarPrecioPublicoDesdeAnalisis", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 300;

            cmd.Parameters.AddWithValue("@PriceListNum", request.PriceListNum);
            cmd.Parameters.AddWithValue("@FechaCorte", request.FechaCorte.Date);
            cmd.Parameters.AddWithValue("@FechaUso", request.FechaUso.Date);
            cmd.Parameters.AddWithValue("@Usuario", usuario);
            cmd.Parameters.AddWithValue("@AlcanceClientes", request.AlcanceClientes ?? "ACTIVOS");
            cmd.Parameters.AddWithValue("@Canal", string.IsNullOrWhiteSpace(request.Canal) ? DBNull.Value : request.Canal.Trim());

            var pSkus = cmd.Parameters.AddWithValue("@Skus", dtSkus);
            pSkus.SqlDbType = SqlDbType.Structured;
            pSkus.TypeName = "dbo.TVP_PrecioPublicoSku";

            var pClientes = cmd.Parameters.AddWithValue("@Clientes", dtClientes);
            pClientes.SqlDbType = SqlDbType.Structured;
            pClientes.TypeName = "dbo.TVP_ClientePrecio";

            await cn.OpenAsync();

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return BadRequest(new { ok = false, mensaje = "El procedimiento no regresó resultado." });

            return Ok(new
            {
                ok = true,
                loteId = rd["LoteId"]?.ToString(),
                priceListNum = Convert.ToInt32(rd["PriceListNum"]),
                priceListName = rd["PriceListName"]?.ToString(),
                fechaCorte = Convert.ToDateTime(rd["FechaCorte"]).ToString("yyyy-MM-dd"),
                fechaUso = Convert.ToDateTime(rd["FechaUso"]).ToString("yyyy-MM-dd"),
                insertados = Convert.ToInt32(rd["Insertados"]),
                actualizados = Convert.ToInt32(rd["Actualizados"]),
                sinCambio = Convert.ToInt32(rd["SinCambio"]),
                totalHistorico = Convert.ToInt32(rd["TotalHistorico"])
            });
        }

        [HttpGet]
        public IActionResult ObtenerMasters()
        {
            using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
            cn.Open();
            var cmd = new SqlCommand(@"
        SELECT DISTINCT LTRIM(RTRIM(U_MASTER))
        FROM dbo.ArticuloSap
        WHERE NULLIF(LTRIM(RTRIM(U_MASTER)), '') IS NOT NULL
        ORDER BY 1", cn);
            var lista = new List<string>();
            using var dr = cmd.ExecuteReader();
            while (dr.Read()) lista.Add(dr.GetString(0));
            return Json(lista);
        }

        // ============================================================
        // GET /Comercial/ObtenerSkusParaMapeo
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> ObtenerSkusParaMapeo()
        {
            try
            {
                // Precio público actual por SKU.
                // PriceListNum = 1 corresponde a "Precio Publico".
                var preciosPublicosRaw = await _context.CatalogoPrecioSap
                    .Where(p => p.PriceListNum == 1)
                    .Select(p => new
                    {
                        ProductoCodigo = p.ProductoCodigo,
                        Precio = p.Precio,
                        FechaModificacion = p.FechaModificacion,
                        Id = p.Id
                    })
                    .ToListAsync();

                var preciosPublicos = preciosPublicosRaw
                    .Where(x => !string.IsNullOrWhiteSpace(x.ProductoCodigo))
                    .GroupBy(x => x.ProductoCodigo.Trim().ToUpper())
                    .ToDictionary(
                        g => g.Key,
                        g => g
                            .OrderByDescending(x => x.FechaModificacion)
                            .ThenByDescending(x => x.Id)
                            .First()
                            .Precio
                    );

                var articulos = await _context.ArticuloSap
                    .OrderBy(a => a.ProductoNombre)
                    .Select(a => new
                    {
                        sku = a.ProductoCodigo,
                        nombre = a.ProductoNombre,
                        master = a.U_MASTER,
                        tipo = a.U_TipoporSKU
                    })
                    .ToListAsync();

                var skus = articulos.Select(a =>
                {
                    var skuKey = (a.sku ?? "").Trim().ToUpper();

                    var precioPublico = preciosPublicos.ContainsKey(skuKey)
                        ? preciosPublicos[skuKey]
                        : 0m;

                    return new
                    {
                        sku = a.sku,
                        nombre = a.nombre,

                        // Tu JS actual usa "precio", por eso aquí mandamos el precio público.
                        precio = precioPublico,

                        // También mando este campo por claridad.
                        precioPublico = precioPublico,

                        master = a.master,
                        tipo = a.tipo
                    };
                }).ToList();

                return Json(new
                {
                    ok = true,
                    total = skus.Count,
                    data = skus
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al obtener SKUs para mapeo.",
                    error = ex.Message
                });
            }
        }

        // ============================================================
        // POST /Comercial/GuardarCompetenciaLote
        // Guarda por columnas: Denes / Tc / Freasa
        // según el competidor recibido
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> GuardarCompetenciaLote([FromBody] CompetenciaLoteDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "No se recibió información."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.Competidor))
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "El nombre del competidor es obligatorio."
                    });
                }

                if (dto.Registros == null || !dto.Registros.Any())
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "No se recibieron registros."
                    });
                }

                DateTime fecha;
                if (!DateTime.TryParseExact(
                    dto.Fecha,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out fecha))
                {
                    fecha = DateTime.Today;
                }

                var competidorNombre = dto.Competidor.Trim();

                if (competidorNombre.Equals("Competencia", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "Captura el nombre real del competidor."
                    });
                }

                using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
                await cn.OpenAsync();

                using var tx = cn.BeginTransaction();

                try
                {
                    int competidorId;

                    var sqlCompetidor = @"
SELECT CompetidorId
FROM dbo.CompetidorPrecio
WHERE UPPER(LTRIM(RTRIM(Nombre))) = UPPER(LTRIM(RTRIM(@Nombre)));
";

                    using (var cmd = new SqlCommand(sqlCompetidor, cn, tx))
                    {
                        cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 150).Value = competidorNombre;

                        var result = await cmd.ExecuteScalarAsync();

                        if (result == null || result == DBNull.Value)
                        {
                            var sqlInsertCompetidor = @"
INSERT INTO dbo.CompetidorPrecio (Nombre)
VALUES (@Nombre);

SELECT SCOPE_IDENTITY();
";

                            using var cmdIns = new SqlCommand(sqlInsertCompetidor, cn, tx);
                            cmdIns.Parameters.Add("@Nombre", SqlDbType.NVarChar, 150).Value = competidorNombre;

                            competidorId = Convert.ToInt32(await cmdIns.ExecuteScalarAsync());
                        }
                        else
                        {
                            competidorId = Convert.ToInt32(result);
                        }
                    }

                    var registrosValidos = dto.Registros
                        .Where(r =>
                            !string.IsNullOrWhiteSpace(r.SkuPropio) &&
                            r.PrecioCompetencia > 0)
                        .Select(r => new CompetenciaRegistroDto
                        {
                            SkuPropio = r.SkuPropio.Trim().ToUpperInvariant(),
                            CodigoCompetencia = (r.CodigoCompetencia ?? "").Trim(),
                            NombreCompetencia = (r.NombreCompetencia ?? "").Trim(),
                            PrecioCompetencia = r.PrecioCompetencia,
                            NuestroPrecio = r.NuestroPrecio
                        })
                        .ToList();

                    if (!registrosValidos.Any())
                    {
                        tx.Rollback();

                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "No hay registros válidos para guardar."
                        });
                    }

                    foreach (var r in registrosValidos)
                    {
                        /*
                           Borramos el registro previo del mismo:
                           FechaCorte + Competidor + SKU propio + producto competencia.

                           Así si vuelves a importar el mismo competidor para la misma fecha,
                           se actualiza el precio.
                        */
                        var sqlDelete = @"
DELETE d
FROM dbo.PrecioCompetenciaDetalle d
WHERE d.FechaCorte = @FechaCorte
  AND d.CompetidorId = @CompetidorId
  AND UPPER(LTRIM(RTRIM(d.SkuPropio))) = UPPER(LTRIM(RTRIM(@SkuPropio)))
  AND ISNULL(UPPER(LTRIM(RTRIM(d.CodigoCompetencia))), '') = ISNULL(UPPER(LTRIM(RTRIM(@CodigoCompetencia))), '')
  AND UPPER(LTRIM(RTRIM(d.NombreCompetencia))) = UPPER(LTRIM(RTRIM(@NombreCompetencia)));
";

                        using (var cmdDel = new SqlCommand(sqlDelete, cn, tx))
                        {
                            cmdDel.Parameters.Add("@FechaCorte", SqlDbType.Date).Value = fecha.Date;
                            cmdDel.Parameters.Add("@CompetidorId", SqlDbType.Int).Value = competidorId;
                            cmdDel.Parameters.Add("@SkuPropio", SqlDbType.NVarChar, 50).Value = r.SkuPropio;
                            cmdDel.Parameters.Add("@CodigoCompetencia", SqlDbType.NVarChar, 80).Value =
                                string.IsNullOrWhiteSpace(r.CodigoCompetencia) ? DBNull.Value : r.CodigoCompetencia;
                            cmdDel.Parameters.Add("@NombreCompetencia", SqlDbType.NVarChar, 300).Value = r.NombreCompetencia;

                            await cmdDel.ExecuteNonQueryAsync();
                        }

                        var sqlInsert = @"
INSERT INTO dbo.PrecioCompetenciaDetalle
(
    FechaRegistro,
    FechaCorte,
    CompetidorId,
    SkuPropio,
    CodigoCompetencia,
    NombreCompetencia,
    PrecioCompetencia,
    NuestroPrecio,
    UsuarioRegistro
)
VALUES
(
    SYSDATETIME(),
    @FechaCorte,
    @CompetidorId,
    @SkuPropio,
    @CodigoCompetencia,
    @NombreCompetencia,
    @PrecioCompetencia,
    @NuestroPrecio,
    @UsuarioRegistro
);
";

                        using var cmdIns = new SqlCommand(sqlInsert, cn, tx);

                        cmdIns.Parameters.Add("@FechaCorte", SqlDbType.Date).Value = fecha.Date;
                        cmdIns.Parameters.Add("@CompetidorId", SqlDbType.Int).Value = competidorId;
                        cmdIns.Parameters.Add("@SkuPropio", SqlDbType.NVarChar, 50).Value = r.SkuPropio;

                        cmdIns.Parameters.Add("@CodigoCompetencia", SqlDbType.NVarChar, 80).Value =
                            string.IsNullOrWhiteSpace(r.CodigoCompetencia)
                                ? DBNull.Value
                                : r.CodigoCompetencia;

                        cmdIns.Parameters.Add("@NombreCompetencia", SqlDbType.NVarChar, 300).Value =
                            string.IsNullOrWhiteSpace(r.NombreCompetencia)
                                ? "SIN NOMBRE"
                                : r.NombreCompetencia;

                        cmdIns.Parameters.Add("@PrecioCompetencia", SqlDbType.Decimal).Value = r.PrecioCompetencia;
                        cmdIns.Parameters["@PrecioCompetencia"].Precision = 18;
                        cmdIns.Parameters["@PrecioCompetencia"].Scale = 4;

                        cmdIns.Parameters.Add("@NuestroPrecio", SqlDbType.Decimal).Value =
                            r.NuestroPrecio.HasValue
                                ? r.NuestroPrecio.Value
                                : DBNull.Value;

                        cmdIns.Parameters["@NuestroPrecio"].Precision = 18;
                        cmdIns.Parameters["@NuestroPrecio"].Scale = 4;

                        cmdIns.Parameters.Add("@UsuarioRegistro", SqlDbType.NVarChar, 150).Value =
                            User?.Identity?.Name ?? "sistema";

                        await cmdIns.ExecuteNonQueryAsync();
                    }

                    tx.Commit();

                    return Json(new
                    {
                        ok = true,
                        mensaje = "Registros guardados correctamente.",
                        guardados = registrosValidos.Count,
                        competidor = competidorNombre,
                        fechaCorte = fecha.ToString("yyyy-MM-dd")
                    });
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al guardar lote de competencia.",
                    error = ex.Message
                });
            }
        }

        [HttpPost]
        [RequestSizeLimit(30_000_000)]
        public async Task<IActionResult> ExtraerCompetenciaArchivo(
    IFormFile archivo,
    string? competidor,
    string? fecha)
        {
            try
            {
                if (archivo == null || archivo.Length == 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "No se recibió archivo."
                    });
                }

                var ext = Path.GetExtension(archivo.FileName)?.ToLowerInvariant();

                // ✅ AHORA INCLUYE EXCEL Y CSV
                var permitidas = new[]
                {
            ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".xlsx", ".csv"
        };

                if (!permitidas.Contains(ext))
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "Formato no soportado. Usa PDF, JPG, PNG, WEBP, Excel o CSV."
                    });
                }

                byte[] bytes;

                using (var ms = new MemoryStream())
                {
                    await archivo.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }

                // ✅ ========= EXCEL =========
                if (ext == ".xlsx")
                {
                    var productos = ExtraerExcel(bytes);

                    if (!productos.Any())
                    {
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "El Excel no tiene datos válidos."
                        });
                    }

                    return Json(new
                    {
                        ok = true,
                        competidor = string.IsNullOrWhiteSpace(competidor) ? "Competencia" : competidor.Trim(),
                        fecha = string.IsNullOrWhiteSpace(fecha) ? DateTime.Today.ToString("yyyy-MM-dd") : fecha,
                        productos
                    });
                }

                // ✅ ========= CSV =========
                if (ext == ".csv")
                {
                    var productos = ExtraerCsv(bytes);

                    if (!productos.Any())
                    {
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "El CSV no tiene datos válidos."
                        });
                    }

                    return Json(new
                    {
                        ok = true,
                        competidor = string.IsNullOrWhiteSpace(competidor) ? "Competencia" : competidor.Trim(),
                        fecha = string.IsNullOrWhiteSpace(fecha) ? DateTime.Today.ToString("yyyy-MM-dd") : fecha,
                        productos
                    });
                }

                // ✅ ========= PDF / IMAGEN =========

                string textoExtraido = "";

                if (ext == ".pdf")
                {
                    textoExtraido = ExtraerTextoPdf(bytes);

                    var productosPdf = ParsearTextoCompetenciaFlexible(textoExtraido);

                    // Si PDF no trae buen texto → OCR
                    if (string.IsNullOrWhiteSpace(textoExtraido) || productosPdf.Count < 25)
                    {
                        var textoOcrPdf = await ExtraerTextoOcrDesdePdfAsync(bytes);

                        if (!string.IsNullOrWhiteSpace(textoOcrPdf))
                        {
                            textoExtraido = string.Join("\n", textoExtraido, textoOcrPdf);
                        }
                    }
                }
                else
                {
                    // Imagen
                    textoExtraido = await ExtraerTextoOcrDesdeImagenAsync(bytes);
                }

                if (string.IsNullOrWhiteSpace(textoExtraido))
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "No se pudo extraer texto del archivo."
                    });
                }

                var productosFinal = ParsearTextoCompetenciaFlexible(textoExtraido);

                if (!productosFinal.Any())
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "Se leyó texto pero no se detectaron productos.",
                        previewTexto = textoExtraido.Length > 2000
                            ? textoExtraido.Substring(0, 2000)
                            : textoExtraido
                    });
                }

                return Json(new
                {
                    ok = true,
                    competidor = string.IsNullOrWhiteSpace(competidor) ? "Competencia" : competidor.Trim(),
                    fecha = string.IsNullOrWhiteSpace(fecha) ? DateTime.Today.ToString("yyyy-MM-dd") : fecha,
                    totalCaracteres = textoExtraido.Length,
                    previewTexto = textoExtraido.Length > 5000
                        ? textoExtraido.Substring(0, 5000)
                        : textoExtraido,
                    productos = productosFinal
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al extraer datos.",
                    error = ex.Message
                });
            }
        }


        private List<CompetenciaProductoExtraidoDto> ExtraerExcel(byte[] bytes)
        {
            var lista = new List<CompetenciaProductoExtraidoDto>();

            using var ms = new MemoryStream(bytes);
            using var wb = new ClosedXML.Excel.XLWorkbook(ms);
            var ws = wb.Worksheet(1);

            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var nombre = row.Cell(2).GetString();

                if (string.IsNullOrWhiteSpace(nombre)) continue;

                decimal.TryParse(row.Cell(3).GetString().Replace("$", ""), out decimal precio);

                lista.Add(new CompetenciaProductoExtraidoDto
                {
                    CodigoCompetencia = row.Cell(1).GetString(),
                    Nombre = nombre,
                    Precio = precio,
                    SkuPropio = row.Cell(4).GetString()
                });
            }

            return lista;
        }

        //TABLA PARA CARGAR UN EXCEL 
        //Codigo | Nombre | Precio | Sku
        //1001   | Maiz   | 25.5   | N001
        //1002   | Frijol | 30.0   | N002

        private List<CompetenciaProductoExtraidoDto> ExtraerCsv(byte[] bytes)
        {
            var lista = new List<CompetenciaProductoExtraidoDto>();

            var texto = Encoding.UTF8.GetString(bytes);
            var lineas = texto.Split('\n');

            foreach (var linea in lineas.Skip(1))
            {
                var cols = linea.Split(',');

                if (cols.Length < 3) continue;

                decimal.TryParse(cols[2].Replace("$", ""), out decimal precio);

                lista.Add(new CompetenciaProductoExtraidoDto
                {
                    CodigoCompetencia = cols[0],
                    Nombre = cols[1],
                    Precio = precio,
                    SkuPropio = cols.Length > 3 ? cols[3] : ""
                });
            }

            return lista;
        }

        private string ExtraerTextoPdf(byte[] pdfBytes)
        {
            try
            {
                using var ms = new MemoryStream(pdfBytes);
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(ms);

                var paginas = pdf.GetPages()
                    .Select(p => p.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t));

                return string.Join("\n", paginas);
            }
            catch
            {
                return "";
            }
        }

        private async Task<string> ExtraerTextoOcrDesdeImagenAsync(byte[] imageBytes)
        {
            return await Task.Run(() =>
            {
                var imagenNormalizada = PrepararImagenParaOcr(imageBytes);

                var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

                using var engine = new TesseractEngine(
                    tessDataPath,
                    "spa+eng",
                    EngineMode.Default
                );

                engine.SetVariable("preserve_interword_spaces", "1");

                using var pix = Pix.LoadFromMemory(imagenNormalizada);

                var textos = new List<string>();

                // Primer pase: automático
                using (var pageAuto = engine.Process(pix, PageSegMode.Auto))
                {
                    textos.Add(pageAuto.GetText() ?? "");
                    textos.Add(ExtraerTextoOrdenadoPorCoordenadas(pageAuto));
                }

                // Segundo pase: texto disperso, útil para tablas escaneadas
                using (var pageSparse = engine.Process(pix, PageSegMode.SparseText))
                {
                    textos.Add(pageSparse.GetText() ?? "");
                    textos.Add(ExtraerTextoOrdenadoPorCoordenadas(pageSparse));
                }

                return string.Join("\n", textos.Where(x => !string.IsNullOrWhiteSpace(x)));
            });
        }
        private sealed class OcrWordBox
        {
            public string Text { get; set; } = "";
            public int X { get; set; }
            public int Y { get; set; }
            public int H { get; set; }
        }

        private string ExtraerTextoOrdenadoPorCoordenadas(Page page)
        {
            var words = new List<OcrWordBox>();

            using var iter = page.GetIterator();

            iter.Begin();

            do
            {
                var text = iter.GetText(PageIteratorLevel.Word);

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                    continue;

                words.Add(new OcrWordBox
                {
                    Text = text.Trim(),
                    X = rect.X1,
                    Y = rect.Y1,
                    H = Math.Max(1, rect.Y2 - rect.Y1)
                });

            } while (iter.Next(PageIteratorLevel.Word));

            if (!words.Any())
                return "";

            var sorted = words
                .OrderBy(w => w.Y)
                .ThenBy(w => w.X)
                .ToList();

            var rows = new List<List<OcrWordBox>>();

            foreach (var word in sorted)
            {
                var tolerancia = Math.Max(10, word.H);

                var row = rows.FirstOrDefault(r =>
                {
                    var avgY = r.Average(x => x.Y);
                    return Math.Abs(avgY - word.Y) <= tolerancia;
                });

                if (row == null)
                {
                    row = new List<OcrWordBox>();
                    rows.Add(row);
                }

                row.Add(word);
            }

            var lineas = rows
                .OrderBy(r => r.Average(w => w.Y))
                .Select(r => string.Join(" ", r.OrderBy(w => w.X).Select(w => w.Text)))
                .Select(NormalizarLineaCompetencia)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            return string.Join("\n", lineas);
        }

        private async Task<string> ExtraerTextoOcrDesdePdfAsync(byte[] pdfBytes)
        {
            return await Task.Run(() =>
            {
                var textos = new List<string>();

                var settings = new MagickReadSettings
                {
                    Density = new Density(300, 300)
                };

                using var paginas = new MagickImageCollection();
                paginas.Read(pdfBytes, settings);

                var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

                using var engine = new TesseractEngine(
                    tessDataPath,
                    "spa+eng",
                    EngineMode.Default
                );

                foreach (var pagina in paginas)
                {
                    pagina.BackgroundColor = MagickColors.White;
                    pagina.Alpha(AlphaOption.Remove);
                    pagina.ColorType = ColorType.Grayscale;
                    pagina.Format = MagickFormat.Png;

                    using var ms = new MemoryStream();
                    pagina.Write(ms);

                    var imagenNormalizada = PrepararImagenParaOcr(ms.ToArray());

                    using var pix = Pix.LoadFromMemory(imagenNormalizada);
                    using var page = engine.Process(pix, PageSegMode.Auto);

                    textos.Add(page.GetText() ?? "");
                    textos.Add(ExtraerTextoOrdenadoPorCoordenadas(page));
                }

                return string.Join("\n", textos);
            });
        }

        private byte[] PrepararImagenParaOcr(byte[] imageBytes)
        {
            using var image = new MagickImage(imageBytes);

            image.AutoOrient();
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            image.ColorType = ColorType.Grayscale;

            // Para tablas con letras pequeñas, 1600 es poco.
            // Subimos la imagen a mínimo 3500 px de ancho.
            if (image.Width < 3500)
            {
                var porcentaje = (int)Math.Ceiling((3500.0 / image.Width) * 100.0);
                image.Resize(new Percentage(porcentaje));
            }

            image.Sharpen();
            image.Format = MagickFormat.Png;

            using var ms = new MemoryStream();
            image.Write(ms);

            return ms.ToArray();
        }

        private void ExtraerRegistrosConCodigoPorLinea(string rawText, List<CompetenciaProductoExtraidoDto> productos)
        {
            var lineas = (rawText ?? "")
                .Replace("\r", "")
                .Split('\n')
                .Select(NormalizarLineaCompetencia)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            foreach (var lineaOriginal in lineas)
            {
                var linea = NormalizarLineaCompetencia(lineaOriginal);

                if (EsLineaIgnorableCompetencia(linea))
                    continue;

                // Formato esperado:
                // 563 PULPA NEGRA $179 $180 $181
                // 0005 PECHO DESHUESADO $149 $150 $151
                // 0633 SHORT LOIN (T-BONE) (MEJORADO) $196 $197 $198
                var m = Regex.Match(
                    linea,
                    @"^\s*(?<codigo>[A-Z]{0,3}\d{1,6})\s+(?<resto>.+)$",
                    RegexOptions.IgnoreCase
                );

                if (!m.Success)
                    continue;

                var codigo = m.Groups["codigo"].Value.Trim().ToUpperInvariant();
                var resto = NormalizarLineaCompetencia(m.Groups["resto"].Value);

                if (EsCodigoBasura(codigo))
                    continue;

                if (string.IsNullOrWhiteSpace(resto))
                    continue;

                var precios = BuscarPrecios(resto);

                if (!precios.Any())
                    continue;

                // Tomamos el primer precio de escala.
                // Ejemplo: $179 $180 $181 => usa 179.
                var primerPrecio = precios.First();
                var precio = ParsePrecioCompetencia(primerPrecio.Groups["precio"].Value);

                if (precio < 5 || precio > 1000)
                    continue;

                var nombre = resto.Substring(0, primerPrecio.Index).Trim();
                nombre = LimpiarNombreProductoCompetencia(nombre);

                if (!EsNombreProductoValidoCompetencia(nombre))
                    continue;

                if (EsTituloOCategoria(nombre))
                    continue;

                if (EsLineaIgnorableCompetencia(nombre))
                    continue;

                productos.Add(new CompetenciaProductoExtraidoDto
                {
                    CodigoCompetencia = codigo,
                    Nombre = nombre,
                    Precio = precio
                });
            }
        }



        private List<CompetenciaProductoExtraidoDto> ParsearTextoCompetenciaFlexible(string rawText)
        {
            var productos = new List<CompetenciaProductoExtraidoDto>();

            if (string.IsNullOrWhiteSpace(rawText))
                return productos;

            var texto = NormalizarTextoCompetencia(rawText);

            // 1) Primero intentar por línea: ideal para tablas con código + producto + varios precios.
            ExtraerRegistrosConCodigoPorLinea(rawText, productos);

            // 2) Después intentar sobre texto plano.
            ExtraerRegistrosConCodigo(texto, productos);

            // 3) Si ya encontró productos con código, no procesar sin código.
            // Esto evita meter encabezados, títulos y basura OCR.
            if (productos.Count >= 5)
            {
                return productos
                    .Where(p => !string.IsNullOrWhiteSpace(p.Nombre))
                    .Where(p => p.Precio >= 5 && p.Precio <= 1000)
                    .GroupBy(x => new
                    {
                        Codigo = (x.CodigoCompetencia ?? "").Trim().ToUpperInvariant(),
                        Nombre = (x.Nombre ?? "").Trim().ToUpperInvariant(),
                        x.Precio
                    })
                    .Select(g => g.First())
                    .OrderBy(x => x.CodigoCompetencia)
                    .ThenBy(x => x.Nombre)
                    .ToList();
            }

            // 4) Solo si no hay códigos suficientes, usar parser sin código.
            ExtraerRegistrosSinCodigoPorLinea(rawText, productos);

            return productos
                .Where(p => !string.IsNullOrWhiteSpace(p.Nombre))
                .Where(p => p.Precio >= 5 && p.Precio <= 1000)
                .GroupBy(x => new
                {
                    Codigo = (x.CodigoCompetencia ?? "").Trim().ToUpperInvariant(),
                    Nombre = (x.Nombre ?? "").Trim().ToUpperInvariant(),
                    x.Precio
                })
                .Select(g => g.First())
                .OrderBy(x => x.CodigoCompetencia)
                .ThenBy(x => x.Nombre)
                .ToList();
        }

        private string NormalizarTextoCompetencia(string raw)
        {
            var x = raw ?? "";

            x = x.Replace("\r", " ").Replace("\n", " ");
            x = x.Replace("|", " ");
            x = x.Replace("—", " ");
            x = x.Replace("_", " ");
            x = x.Replace("•", " ");
            x = x.Replace("\t", " ");

            // 221.00$NE11026 -> 221.00 NE11026
            x = Regex.Replace(
                x,
                @"(\d{1,4}(?:[.,]\d{1,2})?)\s*\$\s*([A-Z]{1,5}\d{1,12})",
                "$1 $2",
                RegexOptions.IgnoreCase
            );

            // NA00105Aguja -> NA00105 Aguja
            x = Regex.Replace(
                x,
                @"\b(?<sku>[A-Z]{1,5}\d{1,12})(?<nom>[A-ZÁÉÍÓÚÑ][A-ZÁÉÍÓÚÑa-záéíóúñ])",
                "${sku} ${nom}",
                RegexOptions.IgnoreCase
            );

            x = Regex.Replace(x, @"\s+", " ").Trim();

            return x;
        }

        private void ExtraerRegistrosConCodigo(string texto, List<CompetenciaProductoExtraidoDto> productos)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return;

            texto = NormalizarTextoCompetencia(texto);

            var codigoPattern = @"(?:[A-Z]{1,3}\d{1,6}|\d{1,6})";

            var regex = new Regex(
                $@"(?<codigo>\b{codigoPattern}\b)\s+
           (?<nombre>[A-ZÁÉÍÓÚÑ0-9][A-ZÁÉÍÓÚÑ0-9a-záéíóúñ\s\/\.\-\(\)]+?)
           \s+\$?\s*(?<precio>\d{{1,4}}(?:[.,]\d{{1,2}})?)",
                RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
            );

            var matches = regex.Matches(texto);

            foreach (Match m in matches)
            {
                if (!m.Success)
                    continue;

                var codigo = m.Groups["codigo"].Value.Trim().ToUpperInvariant();
                var nombre = LimpiarNombreProductoCompetencia(m.Groups["nombre"].Value);
                var precio = ParsePrecioCompetencia(m.Groups["precio"].Value);

                if (EsCodigoBasura(codigo))
                    continue;

                if (precio < 5 || precio > 1000)
                    continue;

                if (!EsNombreProductoValidoCompetencia(nombre))
                    continue;

                if (EsTituloOCategoria(nombre))
                    continue;

                if (EsLineaIgnorableCompetencia(nombre))
                    continue;

                productos.Add(new CompetenciaProductoExtraidoDto
                {
                    CodigoCompetencia = codigo,
                    Nombre = nombre,
                    Precio = precio
                });
            }
        }

        private void ExtraerRegistrosSinCodigoPorLinea(string rawText, List<CompetenciaProductoExtraidoDto> productos)
        {
            var lineas = (rawText ?? "")
                .Replace("\r", "")
                .Split('\n')
                .Select(NormalizarLineaCompetencia)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var pendientes = new Queue<string>();

            foreach (var lineaOriginal in lineas)
            {
                var linea = NormalizarLineaCompetencia(lineaOriginal);

                if (EsLineaIgnorableCompetencia(linea))
                    continue;

                // Si ya trae código/SKU, NO se procesa aquí.
                // Ejemplo: NE000118 Arrachera Concha 221.00
                if (Regex.IsMatch(linea, @"^\s*(?:[A-Z]{1,5}\d{1,12}|\d{1,8})\s+", RegexOptions.IgnoreCase))
                    continue;

                var precios = BuscarPrecios(linea);

                // CASO 1: línea sin precio = posible producto pendiente
                if (!precios.Any())
                {
                    var posibleNombre = LimpiarNombreProductoCompetencia(linea);

                    if (EsNombreProductoValidoCompetencia(posibleNombre)
                        && !EsTituloOCategoria(posibleNombre)
                        && EsProductoCarnico(posibleNombre))
                    {
                        pendientes.Enqueue(posibleNombre);
                    }

                    continue;
                }

                // CASO 2: línea con producto + precio en la misma línea
                // Ejemplo: PESCUEZO S/H $110
                var primerPrecio = precios.First();
                var precio = ParsePrecioCompetencia(primerPrecio.Groups["precio"].Value);

                if (precio < 5 || precio > 1000)
                    continue;

                var textoAntesPrecio = linea.Substring(0, primerPrecio.Index).Trim();

                if (!string.IsNullOrWhiteSpace(textoAntesPrecio))
                {
                    var nombre = LimpiarNombreProductoCompetencia(textoAntesPrecio);

                    if (EsNombreProductoValidoCompetencia(nombre)
                        && !EsTituloOCategoria(nombre)
                        && EsProductoCarnico(nombre))
                    {
                        productos.Add(new CompetenciaProductoExtraidoDto
                        {
                            CodigoCompetencia = "",
                            Nombre = nombre,
                            Precio = precio
                        });

                        continue;
                    }
                }

                // CASO 3: línea solo con uno o varios precios.
                // Ejemplo:
                // $110
                // $135
                // o "$62 $60 $58 $53"
                foreach (var precioMatch in precios)
                {
                    if (pendientes.Count == 0)
                        break;

                    var precioPendiente = ParsePrecioCompetencia(precioMatch.Groups["precio"].Value);

                    if (precioPendiente < 5 || precioPendiente > 1000)
                        continue;

                    var nombrePendiente = pendientes.Dequeue();

                    if (!EsNombreProductoValidoCompetencia(nombrePendiente)
                        || !EsProductoCarnico(nombrePendiente))
                        continue;

                    productos.Add(new CompetenciaProductoExtraidoDto
                    {
                        CodigoCompetencia = "",
                        Nombre = nombrePendiente,
                        Precio = precioPendiente
                    });
                }
            }
        }

        private Match? BuscarPrimerPrecio(string texto)
        {
            return BuscarPrecios(texto).FirstOrDefault();
        }

        private List<Match> BuscarPrecios(string texto)
        {
            return Regex.Matches(
                    texto ?? "",
                    @"(?<![A-Z0-9])(?:\$?\s*)(?<precio>\d{1,4}(?:[.,]\d{1,2})?)(?:\s*\$?)(?![A-Z0-9])",
                    RegexOptions.IgnoreCase
                )
                .Cast<Match>()
                .Where(m =>
                {
                    var precio = ParsePrecioCompetencia(m.Groups["precio"].Value);
                    return precio >= 5 && precio <= 1000;
                })
                .ToList();
        }

        private string NormalizarLineaCompetencia(string linea)
        {
            var x = linea ?? "";

            x = x
                .Replace("|", " ")
                .Replace("—", " ")
                .Replace("_", " ")
                .Replace("•", " ")
                .Replace(":", " ")
                .Replace(";", " ")
                .Replace("\t", " ")
                .Replace("º", " ")
                .Replace("°", " ");

            x = Regex.Replace(x, @"\s+", " ");

            return x.Trim();
        }

        private bool EsLineaIgnorableCompetencia(string linea)
        {
            var x = (linea ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(x))
                return true;

            if (x.Length < 2)
                return true;

            var patronesIgnorar = new[]
            {
        "LISTA DE PRECIOS",
        "LLIISSTTAA",
        "VERSIÓN",
        "VERSION",
        "ZONA",
        "FECHA",
        "VIGENCIA",
        "PRODUCTO PRECIO",
        "PRECIO DE LISTA",
        "PRECIO VENTA",
        "PRECIO POR KILO",
        "PRECIO KG",
        "DESCRIPCIÓN DE PRODUCTO",
        "DESCRIPCION DE PRODUCTO",
        "SKU DESCRIPCIÓN",
        "SKU DESCRIPCION",
        "CODIGO PRODUCTO",
        "CÓDIGO PRODUCTO",
        "CLIENTE",
        "EMPRESA",
        "RFC",
        "DIRECCION",
        "DIRECCIÓN",
        "CORREO",
        "EMAIL",
        "TELEFONO",
        "TELÉFONO",
        "SUBTOTAL",
        "TOTAL",
        "IVA",
        "IMPORTE",
        "SUJETA A CAMBIOS",
        "SIN PREVIO AVISO",
        "PRODUCTOS SUJETOS",
        "PROGRAMACIÓN PREVIA",
        "PROGRAMACION PREVIA",
        "RANCHO GANADERO",
        "EMPACADORA",
        "NUEVO LEON",
        "NUEVO LEÓN",
        "NO CUENTA CON",
        "C. TIF",
        "C TIF",
        "SAGARPA",
        "HACCP",
        "VENTAS",
        "AV.",
        "COL.",
        "BODEGA",
        "CENTRAL DE ABASTOS",
        "PRECIOS SUJETOS",
        "SUJETO A CAMBIO",
        "CAMBIO SIN PREVIO",
        "NO SE ACEPTAN",
        "NOTA",
        "OF.",
        "CEL.",
        "@",
        ".COM",
        "THE BEEF EXPERTS",
        "FREE SOFTWARE",
        "LICENCIA",
        "CLIENTE PREMIUM",
        "REDBUCHEF",
"THE BEEF EXPERTS",
"TRANSFORMACION CARNICA",
"TRANSFORMACIÓN CÁRNICA",
"SPR DE RL DE CV",
"REGIOPARQUE",
"GUADALUPE NUEVO LEON",
"GUADALUPE NUEVO LEÓN",
"TIF",
"SAGARPA",
"INSPECCIONADO",
"INSPECCIONADO Y APROBADO",
"CERTIFICACION",
"CERTIFICACIÓN",
"EMPACADOS AL ALTO VACIO",
"EMPACADOS AL ALTO VACÍO",
"VIGENCIA DE PRECIOS",
"L.A.B MONTERREY",
"VENTAS",
"EMAIL",
"TEL",
"CELULAR",
"PRECIO",
"CODIGO",
"CÓDIGO",
"PRECIOS SUJETO",
"PRECIOS SUJETOS",
"CAMBIOS SIN PREVIO AVISO",
"HORARIO DE PEDIDOS",
"ENTREGAR AL DIA SIGUIENTE",
"ENTREGAR AL DÍA SIGUIENTE",
"A 30KG",
"30KG",
"PIEZA BASICA",
"PIEZA BÁSICA",
"CALIDAD EN CARNES",
"EN PIEZA BASICA",
"EN PIEZA BÁSICA",
"LISTA DE PRECIOS",
    "LLIISSTTAA",
    "VERSIÓN",
    "VERSION",
    "ZONA",
    "FECHA",
    "VIGENCIA",

    "A 30KG",
    "30KG",
    "PIEZA BASICA",
    "PIEZA BÁSICA",
    "CALIDAD EN CARNES",
    "EN PIEZA BASICA",
    "EN PIEZA BÁSICA",

    "PRODUCTO PRECIO",
    "PRECIO DE LISTA",
    "PRECIO VENTA",
    "PRECIO POR KILO",
    "PRECIO KG",
    "SUJETA A CAMBIOS",
    "SIN PREVIO AVISO",
    "COTIZACION",
"COTIZACIÓN",
"SUCURSAL",
"EMPRESA",
"CLIENTE",
"AGUASCALIENTES",
"CORREO",
"DIRECCION",
"DIRECCIÓN",
"TIPO DE PAGO",
"RFC",
"VIGENCIA",
"PRESENTACION",
"PRESENTACIÓN",
"PRECIO POR KILO",
"CLUB 35",
"MENUDEO",
"NOMBRE Y FIRMA",
"EJECUTIVO QUE AUTORIZA",
"ADRIANA CONTRERAS",
"SUPERVISORA DE VENTAS",
"FORMATO OFICIAL",
"PRADERAS HUASTECA"

    };

            return patronesIgnorar.Any(p => x.Contains(p));
        }

        private bool EsTituloOCategoria(string texto)
        {
            var x = (texto ?? "").Trim().ToUpperInvariant();

            var categoriasExactas = new HashSet<string>
    {
        "NATURAL NOVILLO EUROPEO",
        "MEJORADO NOVILLO EUROPEO",
        "CORTES FINOS CALIDAD CHOICE",
        "VALOR AGREGADO",
        "CANALES",
        "VACA GORDA",
        "VACA GORDA S/H",
        "VACA GORDA C/H",
        "CARNUDA",
        "CARNUDA S/H",
        "CARNUDA C/H",
        "NOVILLO",
        "NOVILLO SUPREMO",
        "VISCERAS",
        "VÍSCERAS",
        "CERDO",
        "RECORTES",
        "IMPORTACION",
        "IMPORTACIÓN",
        "PRODUCTO BRASILEÑO",
        "PIEZA BASE",
        "VACA GORDA",
        "NOVILLO",
        "VACA GORDA NATURAL",
        "NOVILLO NATURAL",
        "VACA REGULAR",
        "IMPORTADOS RES, CERDO Y POLLO",
        "IMPORTADOS",
        "RES",
        "CERDO",
        "POLLO",
        "PRECIO",
        "CODIGO",
        "CÓDIGO",
        "PRODUCTO",
        "VIGENCIA DE PRECIOS",
        "HORARIO DE PEDIDOS",
        "TRANSFORMACION CARNICA",
        "TRANSFORMACIÓN CÁRNICA",
        "REDBUCHEF",
        "THE BEEF EXPERTS"
    };

            return categoriasExactas.Contains(x);
        }

        private bool EsCodigoBasura(string codigo)
        {
            var c = (codigo ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(c))
                return true;

            // Evita años o textos de encabezado interpretados como código.
            if (c == "2026" || c == "2025" || c == "2024")
                return true;

            return false;
        }

        private decimal ParsePrecioCompetencia(string raw)
        {
            raw = (raw ?? "")
                .Replace("$", "")
                .Replace(",", ".")
                .Trim();

            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var precio))
                return precio;

            return 0;
        }

        private string LimpiarNombreProductoCompetencia(string texto)
        {
            var nombre = texto ?? "";

            nombre = Regex.Replace(nombre, @"\bSKU\b", " ", RegexOptions.IgnoreCase);
            nombre = Regex.Replace(nombre, @"\bCODIGO\b", " ", RegexOptions.IgnoreCase);
            nombre = Regex.Replace(nombre, @"\bCÓDIGO\b", " ", RegexOptions.IgnoreCase);
            nombre = Regex.Replace(nombre, @"\bPRODUCTO\b", " ", RegexOptions.IgnoreCase);
            nombre = Regex.Replace(nombre, @"\bDESCRIPCION\b", " ", RegexOptions.IgnoreCase);
            nombre = Regex.Replace(nombre, @"\bDESCRIPCIÓN\b", " ", RegexOptions.IgnoreCase);
            nombre = Regex.Replace(nombre, @"\bPRECIO\b", " ", RegexOptions.IgnoreCase);
            nombre = Regex.Replace(nombre, @"\bKG\b", " ", RegexOptions.IgnoreCase);

            // Quitar $ sobrantes.
            nombre = nombre.Replace("$", " ");

            nombre = Regex.Replace(nombre, @"\s+", " ");

            return nombre.Trim();
        }

        private bool EsNombreProductoValidoCompetencia(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                return false;

            nombre = nombre.Trim();

            if (nombre.Length < 4)
                return false;

            var upper = nombre.ToUpperInvariant();

            // Basura típica del OCR
            if (upper.Contains("¿") || upper.Contains("«") || upper.Contains("»"))
                return false;

            if (upper.Contains("NO CUENTA"))
                return false;

            if (upper.Contains("CLIENTE"))
                return false;

            if (upper.Contains("PREMIUM") && upper.Length < 20)
                return false;

            if (upper.Contains("GANADERO"))
                return false;

            if (upper.Contains("EMPACADORA"))
                return false;

            if (upper.Contains("SAGARPA"))
                return false;

            if (upper.Contains("TIF"))
                return false;

            if (upper.Contains("LISTA"))
                return false;

            if (upper.Contains("PRECIO"))
                return false;

            if (upper.Contains("FECHA"))
                return false;

            if (upper.Contains("VENTAS"))
                return false;

            if (upper.Contains("NOTA"))
                return false;

            if (upper.Contains("SUJETO"))
                return false;

            if (upper.Contains("A 30KG"))
                return false;

            if (upper.Contains("30KG"))
                return false;

            if (upper.Contains("PIEZA BASICA") || upper.Contains("PIEZA BÁSICA"))
                return false;

            if (upper.Contains("CALIDAD EN CARNES"))
                return false;

            if (upper.Contains("EN PIEZA BASICA") || upper.Contains("EN PIEZA BÁSICA"))
                return false;

            if (EsTituloOCategoria(nombre))
                return false;

            // Debe tener letras
            if (!Regex.IsMatch(nombre, @"[A-ZÁÉÍÓÚÑ]", RegexOptions.IgnoreCase))
                return false;

            // No aceptar puro número/símbolo
            if (Regex.IsMatch(nombre, @"^[\d\s\.\,\-\$]+$"))
                return false;

            // Evitar textos demasiado raros tipo "a Á", "a li"
            var letras = Regex.Replace(nombre, @"[^A-ZÁÉÍÓÚÑa-záéíóúñ]", "");
            if (letras.Length < 4)
                return false;

            // Debe tener al menos una vocal real
            if (!Regex.IsMatch(nombre, @"[AEIOUÁÉÍÓÚaeiouáéíóú]"))
                return false;

            return true;
        }

        private bool EsProductoCarnico(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                return false;

            var x = nombre.ToUpperInvariant();

            var palabrasProducto = new[]
            {
        "AGUJA",
        "ARRACHERA",
        "BISTEC",
        "BISTECK",
        "BOFE",
        "BRISKET",
        "CABEZA",
        "CARNE",
        "CHAMBERETE",
        "CHAMORRO",
        "CHULETA",
        "CHULETON",
        "CHULETÓN",
        "CLOD",
        "COLA",
        "COLAS",
        "CONCHA",
        "CORAZON",
        "CORAZÓN",
        "COSTILLA",
        "COSTILLAR",
        "CUERO",
        "CUAJO",
        "DESHEBRADA",
        "DIEZMILLO",
        "EMPUJE",
        "FALDA",
        "FAJITA",
        "FILETE",
        "GIBA",
        "GRASA",
        "HIGADO",
        "HÍGADO",
        "HUESO",
        "LABIO",
        "LENGUA",
        "LIBRO",
        "LOMO",
        "MARINADA",
        "MENUDO",
        "MOLIDA",
        "NEW YORK",
        "PALETA",
        "PANZA",
        "PATA",
        "PECHO",
        "PESCUEZO",
        "PICAÑA",
        "PIERNA",
        "PULPA",
        "RECORTE",
        "RIB EYE",
        "RIBEYE",
        "SEBO",
        "SHORT RIB",
        "SIRLOIN",
        "SUADERO",
        "T-BONE",
        "TBONE",
        "TOMAHAWK",
        "TOP SIRLOIN",
        "TRIPA",
        "TUETANERO",
        "UBRE"
    };

            return palabrasProducto.Any(p => x.Contains(p));
        }

        private string NormalizarLineaPdf(string linea)
        {
            if (string.IsNullOrWhiteSpace(linea))
                return string.Empty;

            return Regex.Replace(linea.Trim(), @"\s+", " ");
        }

        private bool EsLineaBasuraPdf(string linea)
        {
            if (string.IsNullOrWhiteSpace(linea))
                return true;

            var l = linea.Trim().ToUpperInvariant();

            string[] basura =
            {
        "CARNES G SA DE CV",
        "CAMINO",
        "SAN JUAN",
        "TEL:",
        "CELULAR:",
        "VENTAS -",
        "EMAIL",
        "CERTIFICACIÓN",
        "PRODUCTO EMPACADO",
        "VIGENCIA DE PRECIOS",
        "CODIGO",
        "PRECIO",
        "CÓDIGO",
        "GENERAL",
        "NOVILLO",
        "RES",
        "VACA GORDA",
        "VACA GORDA NATURAL",
        "VACA REGULAR",
        "CERDO",
        "POLLO",
        "IMPORTADOS",
        "HORARIO",
        "ENTREGAR"
    };

            if (basura.Any(x => l.Contains(x)))
                return true;

            if (l.Length < 3)
                return true;

            return false;
        }

        private List<string> UnirLineasDeProducto(List<string> lineas)
        {
            var resultado = new List<string>();
            string actual = string.Empty;

            foreach (var linea in lineas)
            {
                bool iniciaConCodigo = Regex.IsMatch(linea, @"^[A-Z]\d{3}\b", RegexOptions.IgnoreCase);
                bool contienePrecio = Regex.IsMatch(linea, @"(?:\$?\s*)\d{1,4}\.\d{2}\b");

                if (iniciaConCodigo)
                {
                    if (!string.IsNullOrWhiteSpace(actual))
                    {
                        resultado.Add(actual.Trim());
                    }

                    actual = linea;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(actual))
                        actual = linea;
                    else
                        actual += " " + linea;
                }

                if (!string.IsNullOrWhiteSpace(actual) && contienePrecio)
                {
                    resultado.Add(actual.Trim());
                    actual = string.Empty;
                }
            }

            if (!string.IsNullOrWhiteSpace(actual))
            {
                resultado.Add(actual.Trim());
            }

            return resultado
                .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private CompetenciaProductoExtraidoDto? IntentarParsearBloqueProducto(string bloque)
        {
            if (string.IsNullOrWhiteSpace(bloque))
                return null;

            var texto = Regex.Replace(bloque, @"\s+", " ").Trim();

            var patrones = new[]
            {
        new Regex(@"^(?<codigo>[A-Z]\d{3})\s+(?<nombre>.+?)\s+\$?\s*(?<precio>\d{1,4}(?:\.\d{2})?)$", RegexOptions.IgnoreCase),
        new Regex(@"(?<codigo>[A-Z]\d{3})\s+(?<nombre>.+?)\s+\$?\s*(?<precio>\d{1,4}(?:\.\d{2})?)$", RegexOptions.IgnoreCase),
        new Regex(@"^(?<nombre>.+?)\s+\$?\s*(?<precio>\d{1,4}(?:\.\d{2})?)$", RegexOptions.IgnoreCase)
    };

            foreach (var rx in patrones)
            {
                var m = rx.Match(texto);
                if (!m.Success) continue;

                var codigo = m.Groups["codigo"]?.Value ?? "";
                var nombre = m.Groups["nombre"]?.Value ?? "";
                var precio = m.Groups["precio"]?.Value ?? "";

                var prod = CrearProducto(codigo, nombre, precio);
                if (prod != null) return prod;
            }

            return null;
        }




        private bool PrecioPlausible(decimal precio)
        {
            return precio >= 10m && precio <= 1000m;
        }

        private CompetenciaProductoExtraidoDto? CrearProducto(string codigo, string nombre, string precioTxt)
        {
            nombre = LimpiarNombreProducto(nombre);

            if (string.IsNullOrWhiteSpace(nombre))
                return null;

            var upper = nombre.ToUpperInvariant();

            if (upper.Contains("CODIGO") || upper.Contains("CÓDIGO") || upper.Contains("PRECIO"))
                return null;

            if (!decimal.TryParse(precioTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out var precio))
                return null;

            if (!PrecioPlausible(precio))
                return null;

            return new CompetenciaProductoExtraidoDto
            {
                CodigoCompetencia = (codigo ?? "").Trim(),
                Nombre = nombre,
                Precio = precio
            };
        }

        private string LimpiarNombreProducto(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                return string.Empty;

            var limpio = nombre.Trim();

            limpio = Regex.Replace(limpio, @"\s+", " ");
            limpio = limpio.Replace(" ,", ",");
            limpio = limpio.Replace(" .", ".");
            limpio = limpio.Replace("$", "");
            limpio = limpio.Trim('-', ':', ';', ' ');

            return limpio;
        }



        private async Task<List<int>> GetVendedorIdsActualesAsync(CancellationToken ct = default)
        {
            var vendedorIds = new List<int>();

            // 1) CLAIM VendedorId
            var vClaim = User.FindFirst("VendedorId")?.Value?.Trim();

            if (!string.IsNullOrWhiteSpace(vClaim))
            {
                if (vClaim.Contains(","))
                {
                    vendedorIds = vClaim
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0)
                        .Where(v => v > 0)
                        .Distinct()
                        .ToList();
                }
                else
                {
                    var clean = new string(vClaim.Where(char.IsDigit).ToArray());

                    // Si viene "28"
                    if (clean.Length <= 2)
                    {
                        if (int.TryParse(clean, out var v) && v > 0)
                            vendedorIds.Add(v);
                    }
                    else
                    {
                        // Si viene "1028" => 10, 28
                        for (int i = 0; i + 1 < clean.Length; i += 2)
                        {
                            if (int.TryParse(clean.Substring(i, 2), out var v) && v > 0)
                                vendedorIds.Add(v);
                        }
                    }
                }
            }

            // 2) Fallback UsuariosAD / Usuarios
            if (vendedorIds.Count == 0)
            {
                var raw = (User?.Identity?.Name ?? "").Trim();
                var username = raw.Contains('\\') ? raw.Split('\\').Last() : raw;
                var usernameEmail = username.Contains('@') ? username : $"{username}@carnesg.net";

                try
                {
                    int? vendAD = null;

                    if (_uDb?.UsuariosAD != null)
                    {
                        vendAD = await _uDb.UsuariosAD.AsNoTracking()
                            .Where(x =>
                                x.UsuarioAd == raw ||
                                x.UsuarioAd == username ||
                                x.UsuarioAd == usernameEmail)
                            .Select(x => (int?)x.VendedorId)
                            .FirstOrDefaultAsync(ct);
                    }

                    int? vendApp = null;

                    if (!(vendAD.HasValue && vendAD.Value > 0) && _uDb?.Usuarios != null)
                    {
                        vendApp = await _uDb.Usuarios.AsNoTracking()
                            .Where(x =>
                                x.Usuario == raw ||
                                x.Usuario == username ||
                                x.Usuario == usernameEmail)
                            .Select(x => (int?)x.VendedorId)
                            .FirstOrDefaultAsync(ct);
                    }

                    var vend = vendAD ?? vendApp;

                    if (vend.HasValue && vend.Value > 0)
                        vendedorIds.Add(vend.Value);
                }
                catch
                {
                    vendedorIds.Clear();
                }
            }

            return vendedorIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();
        }


        private async Task<List<string>> GetCanalesCedisPorVendedorIdsAsync(List<int> vendedorIds, CancellationToken ct = default)
        {
            var canales = new List<string>();

            if (vendedorIds == null || vendedorIds.Count == 0)
                return canales;

            var vendedorIdsCsv = string.Join(",", vendedorIds);

            using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
            await cn.OpenAsync(ct);

            var sql = @"
SELECT DISTINCT
    Canal = UPPER(LTRIM(RTRIM(ISNULL(U_CANAL, ''))))
FROM dbo.ClienteSap
WHERE VendedorId IN
(
    SELECT TRY_CONVERT(INT, value)
    FROM STRING_SPLIT(@VendedorIdsCsv, ',')
    WHERE TRY_CONVERT(INT, value) IS NOT NULL
)
AND ISNULL(LTRIM(RTRIM(U_CANAL)), '') <> '';
";

            using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@VendedorIdsCsv", SqlDbType.VarChar, 200).Value = vendedorIdsCsv;

            using var dr = await cmd.ExecuteReaderAsync(ct);

            while (await dr.ReadAsync(ct))
            {
                if (!dr.IsDBNull(0))
                    canales.Add(dr.GetString(0));
            }

            return canales
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
        }


        private async Task<IEnumerable<dynamic>> CalcularAnalisisSemanalAsync(
            DateTime? fechaCorte,
            List<string> skus,
            string? producto,
            string? clasificacion,
            List<int> vendedorIds,
            CancellationToken ct = default)
        {
            using var cn = new SqlConnection(_context.Database.GetDbConnection().ConnectionString);
            await cn.OpenAsync(ct);

            var fc = fechaCorte?.Date ?? DateTime.Today;

            var skusJson = JsonSerializer.Serialize(
                skus?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpper())
                    .Distinct()
                    .ToList() ?? new List<string>()
            );

            /*
                AQUÍ debes pegar el SQL que ya tienes dentro de tu método GET ObtenerAnalisisSemanal.

                Solo hay que cambiar el filtro de SKU:
                Antes:
                WHERE (@pSku IS NULL OR @pSku = '' OR [SKU] LIKE '%' + @pSku + '%')

                Ahora:
                WHERE (
                    NOT EXISTS (SELECT 1 FROM #SkuFiltro)
                    OR UPPER(LTRIM(RTRIM([SKU]))) IN (SELECT Sku FROM #SkuFiltro)
                )
            */

            var sql = @"
SET NOCOUNT ON;

DECLARE @FechaCorte DATE = @pFechaCorte;

IF OBJECT_ID('tempdb..#SkuFiltro') IS NOT NULL
    DROP TABLE #SkuFiltro;

CREATE TABLE #SkuFiltro
(
    Sku NVARCHAR(50) NOT NULL PRIMARY KEY
);

INSERT INTO #SkuFiltro (Sku)
SELECT DISTINCT UPPER(LTRIM(RTRIM([value])))
FROM OPENJSON(@pSkusJson)
WHERE NULLIF(LTRIM(RTRIM([value])), '') IS NOT NULL;

/*
    PEGA AQUÍ TU SQL ACTUAL COMPLETO DEL ANÁLISIS SEMANAL.
    Respeta tus CTE: Productos, InventarioFechas, PrecioActual, Base, Calc, etc.

    Solo reemplaza el WHERE final.
*/
";

            var rows = await cn.QueryAsync<dynamic>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        pFechaCorte = fc,
                        pSkusJson = skusJson,
                        pProducto = producto ?? "",
                        pClasificacion = clasificacion ?? ""
                    },
                    cancellationToken: ct
                )
            );

            return rows;
        }


        [HttpGet]
        public async Task<IActionResult> ObtenerCanalesPrecio()
        {
            var canales = new List<string>();

            await using var cn = new SqlConnection(
                _configuration.GetConnectionString("DefaultConnection")
            );

            await using var cmd = new SqlCommand(@"
        SELECT DISTINCT 
            LTRIM(RTRIM(U_CANAL)) AS Canal
        FROM dbo.ClienteSap
        WHERE U_MT_Clasificacion = 'ACTIVO'
          AND ISNULL(LTRIM(RTRIM(U_CANAL)), '') <> ''
        ORDER BY LTRIM(RTRIM(U_CANAL));
    ", cn);

            await cn.OpenAsync();

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                canales.Add(rd["Canal"]?.ToString() ?? "");
            }

            return Json(canales);
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerClientesPrecio(string? canal = null)
        {
            var clientes = new List<object>();

            await using var cn = new SqlConnection(
                _configuration.GetConnectionString("DefaultConnection")
            );

            await using var cmd = new SqlCommand(@"
        SELECT TOP (5000)
            Cliente,
            Nombrecliente AS NombreCliente,
            U_CANAL AS Canal
        FROM dbo.ClienteSap
        WHERE U_MT_Clasificacion = 'ACTIVO'
          AND (
                @Canal IS NULL 
                OR @Canal = '' 
                OR U_CANAL = @Canal
          )
        ORDER BY Nombrecliente;
    ", cn);

            cmd.Parameters.AddWithValue("@Canal", string.IsNullOrWhiteSpace(canal) ? DBNull.Value : canal.Trim());

            await cn.OpenAsync();

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                clientes.Add(new
                {
                    cliente = rd["Cliente"]?.ToString(),
                    nombreCliente = rd["NombreCliente"]?.ToString(),
                    canal = rd["Canal"]?.ToString()
                });
            }

            return Json(clientes);
        }


        [HttpGet]
        public async Task<IActionResult> ObtenerListasPrecioParaGuardar()
        {
            try
            {
                var listas = await _context.ListaPreciosSap
                    .Where(x => x.Activo == true
                        && (
                            x.PriceListNum == 1
                            || x.Factor == 0
                        ))
                    .OrderBy(x => x.PriceListNum)
                    .Select(x => new
                    {
                        priceListNum = x.PriceListNum,
                        priceListName = x.PriceListName,
                        factor = x.Factor
                    })
                    .ToListAsync();

                return Json(new
                {
                    ok = true,
                    data = listas
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al obtener listas de precios.",
                    error = ex.Message
                });
            }
        }


        private bool UsuarioPuedeVerTodasLasSeries()
        {
            return User.IsInRole("Administrador") || User.IsInRole("Sistemas");
        }

        private async Task<List<int>> ObtenerSeriesIdsUsuarioActualAsync(CancellationToken ct = default)
        {
            var raw = (User?.Identity?.Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(raw))
                return new List<int>();

            var username = raw.Contains("\\")
                ? raw.Split("\\").Last()
                : raw;

            var usernameEmail = username.Contains("@")
                ? username
                : $"{username}@carnesg.net";

            var ids = await (
                from u in _context.UsuarioSQL.AsNoTracking()
                join us in _context.UsuarioSeries.AsNoTracking()
                    on u.Id equals us.UsuarioId
                where u.Activo
                   && (
                        u.Usuario == raw ||
                        u.Usuario == username ||
                        u.Usuario == usernameEmail ||
                        u.Nombre == raw ||
                        u.Nombre == username
                      )
                select us.SerieId
            )
            .Distinct()
            .ToListAsync(ct);

            return ids;
        }

        private async Task<string> ObtenerSeriesPermitidasCsvActualAsync(CancellationToken ct = default)
        {
            var idsSeries = await ObtenerSeriesIdsUsuarioActualAsync(ct);

            // Si no tiene series configuradas, regresa vacío.
            // Vacío significa: ver todo.
            if (idsSeries == null || !idsSeries.Any())
                return "";

            var nombresSeries = await _context.Series
                .AsNoTracking()
                .Where(s => idsSeries.Contains(s.Id))
                .Select(s => s.NombreSerie)
                .ToListAsync(ct);

            return string.Join(",", nombresSeries);
        }


        private async Task<List<SelectListItem>> ObtenerSeriesPermitidasActualAsync(CancellationToken ct = default)
        {
            var query = _context.Series.AsNoTracking();

            if (!UsuarioPuedeVerTodasLasSeries())
            {
                var idsPermitidos = await ObtenerSeriesIdsUsuarioActualAsync(ct);
                query = query.Where(s => idsPermitidos.Contains(s.Id));
            }

            return await query
                .OrderBy(s => s.NombreSerie)
                .Select(s => new SelectListItem
                {
                    Value = s.NombreSerie,
                    Text = s.NombreSerie
                })
                .ToListAsync(ct);
        }

        private async Task<bool> UsuarioTieneAccesoSerieAsync(string? nombreSerie, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(nombreSerie))
                return false;

            if (UsuarioPuedeVerTodasLasSeries())
                return true;

            var idsPermitidos = await ObtenerSeriesIdsUsuarioActualAsync(ct);

            return await _context.Series.AsNoTracking()
                .AnyAsync(s => s.NombreSerie == nombreSerie && idsPermitidos.Contains(s.Id), ct);
        }


        [HttpGet]
        public async Task<IActionResult> ObtenerSeriesSurtidosMeat(CancellationToken ct = default)
        {
            try
            {
                var seriesPermitidasCsv = await ObtenerSeriesPermitidasCsvActualAsync(ct);

                var query = _context.OrdenVenta
                    .AsNoTracking()
                    .Where(o =>
                        o.Estatus != 0 &&
                        o.Serie != null &&
                        o.Serie.Trim() != "");

                // Si el usuario tiene series configuradas, limita.
                // Si no tiene series configuradas, ve todas.
                if (!string.IsNullOrWhiteSpace(seriesPermitidasCsv))
                {
                    var seriesPermitidas = seriesPermitidasCsv
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .ToList();

                    query = query.Where(o => seriesPermitidas.Contains(o.Serie.Trim()));
                }

                var series = await query
                    .Select(o => o.Serie.Trim())
                    .Distinct()
                    .OrderBy(s => s)
                    .Select(s => new
                    {
                        serie = s
                    })
                    .ToListAsync(ct);

                return Json(series);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    error = ex.GetBaseException().Message
                });
            }
        }

        //-------------solicitud muestra----------------------------------------------
        // =========================================================================================

        private static readonly string[] ModulosMuestras = {
           "SOLICITUD_MUESTRAS_VENDEDOR",
           "SOLICITUD_MUESTRAS_PLANEACION",
           "SOLICITUD_MUESTRAS_PRODUCCION",
           "SOLICITUD_MUESTRAS_TRACKING"
       };

        [HttpGet("Comercial/AsegurarModulosMuestras")]
        public async Task<IActionResult> AsegurarModulosMuestras()
        {
            try
            {
                foreach (var clave in ModulosMuestras)
                {
                    var existe = await _context.ModulosSistema.AnyAsync(m => m.Clave == clave);
                    if (!existe)
                    {
                        _context.ModulosSistema.Add(new ModulosSistema
                        {
                            Clave = clave,
                            Nombre = clave.Replace("_", " "),
                            Activo = true,
                            FechaCreacion = DateTime.Now
                        });
                    }
                }
                await _context.SaveChangesAsync();
                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet("Comercial/ObtenerPermisosVistaSolicitudMuestras")]
        public async Task<IActionResult> ObtenerPermisosVistaSolicitudMuestras(string perfil = null)
        {
            if (string.Equals(perfil, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                var allTrue = new Dictionary<string, bool?>
                {
                    ["vendedor"] = true,
                    ["planeacion"] = true,
                    ["produccion"] = true,
                    ["tracking"] = true
                };
                return Json(allTrue);
            }

            var login = (User?.Identity?.Name ?? "").Trim();

            var result = new Dictionary<string, bool?>
            {
                ["vendedor"] = null,
                ["planeacion"] = null,
                ["produccion"] = null,
                ["tracking"] = null
            };

            var clavesPermisos = new (string tab, string clave)[]
            {
               ("vendedor",   "SOLICITUD_MUESTRAS_VENDEDOR"),
               ("planeacion", "SOLICITUD_MUESTRAS_PLANEACION"),
               ("produccion", "SOLICITUD_MUESTRAS_PRODUCCION"),
               ("tracking",   "SOLICITUD_MUESTRAS_TRACKING")
            };

            foreach (var (tab, clave) in clavesPermisos)
            {
                var query = from ppm in _context.PerfilPermisoModulo
                            join m in _context.ModulosSistema on ppm.ModuloId equals m.Id
                            where ppm.Activo && m.Clave == clave
                            select ppm;

                if (!string.IsNullOrEmpty(perfil))
                {
                    query = from ppm in query
                            join p in _context.Perfiles on ppm.PerfilId equals p.Id
                            where p.Nombre == perfil
                            select ppm;
                }
                else
                {
                    query = from ppm in query
                            join u in _context.UsuarioSQL on ppm.PerfilId equals u.PerfilId
                            where (u.Usuario == login || u.Nombre == login)
                            select ppm;
                }

                var permiso = await query
                    .Select(ppm => (bool?)ppm.PuedeLeer)
                    .FirstOrDefaultAsync();

                result[tab] = tab == "tracking" ? (permiso ?? true) : permiso;
            }

            return Json(result);
        }

        [HttpGet("Comercial/ObtenerPerfilesMuestras")]
        public async Task<IActionResult> ObtenerPerfilesMuestras()
        {
            try
            {
                var perfiles = await _context.Perfiles
                    .Where(p => p.Activo)
                    .Select(p => new
                    {
                        p.Id,
                        p.Nombre
                    })
                    .OrderBy(p => p.Nombre)
                    .ToListAsync();

                var modulos = await _context.ModulosSistema
                    .Where(m => ModulosMuestras.Contains(m.Clave) && m.Activo)
                    .ToListAsync();

                var permisos = await _context.PerfilPermisoModulo
                    .Where(ppm => ppm.Activo && modulos.Select(m => m.Id).Contains(ppm.ModuloId))
                    .ToListAsync();

                var data = perfiles.Select(p => new
                {
                    p.Id,
                    p.Nombre,
                    vendedor = permisos.Any(x => x.PerfilId == p.Id && x.ModuloId == modulos.FirstOrDefault(m => m.Clave == "SOLICITUD_MUESTRAS_VENDEDOR")?.Id && x.PuedeLeer),
                    planeacion = permisos.Any(x => x.PerfilId == p.Id && x.ModuloId == modulos.FirstOrDefault(m => m.Clave == "SOLICITUD_MUESTRAS_PLANEACION")?.Id && x.PuedeLeer),
                    produccion = permisos.Any(x => x.PerfilId == p.Id && x.ModuloId == modulos.FirstOrDefault(m => m.Clave == "SOLICITUD_MUESTRAS_PRODUCCION")?.Id && x.PuedeLeer),
                    tracking = permisos.Any(x => x.PerfilId == p.Id && x.ModuloId == modulos.FirstOrDefault(m => m.Clave == "SOLICITUD_MUESTRAS_TRACKING")?.Id && x.PuedeLeer)
                }).ToList();

                return Json(new { ok = true, data });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        public class GuardarPermisoMuestrasRequest
        {
            public int PerfilId { get; set; }
            public string ClaveModulo { get; set; }
            public bool Activo { get; set; }
        }

        [HttpPost("Comercial/GuardarPermisoMuestras")]
        public async Task<IActionResult> GuardarPermisoMuestras([FromBody] GuardarPermisoMuestrasRequest request)
        {
            try
            {
                var modulo = await _context.ModulosSistema.FirstOrDefaultAsync(m => m.Clave == request.ClaveModulo && m.Activo);
                if (modulo == null)
                    return Json(new { ok = false, mensaje = "Módulo no encontrado." });

                var permiso = await _context.PerfilPermisoModulo
                    .FirstOrDefaultAsync(ppm => ppm.PerfilId == request.PerfilId && ppm.ModuloId == modulo.Id);

                if (permiso == null)
                {
                    _context.PerfilPermisoModulo.Add(new PerfilPermisoModulo
                    {
                        PerfilId = request.PerfilId,
                        ModuloId = modulo.Id,
                        PuedeLeer = request.Activo,
                        PuedeEscribir = request.Activo,
                        Activo = true,
                        FechaCreacion = DateTime.Now
                    });
                }
                else
                {
                    permiso.PuedeLeer = request.Activo;
                    permiso.PuedeEscribir = request.Activo;
                    permiso.Activo = true;
                }

                await _context.SaveChangesAsync();
                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        // 1. Le agregamos el parametro opcional (string simular = "")
        [HttpGet("Comercial/SolicitudMuestras")]
        public async Task<IActionResult> SolicitudMuestras(string simular = "")
        {
            var nombreUsuario = User.Identity?.Name ?? "";
            string perfilCalculado = "Vendedor";

            if (!string.IsNullOrEmpty(nombreUsuario))
            {
                try
                {
                    using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                    await conn.OpenAsync();

                    var sql = @"
               SELECT TOP 1 
                   CASE 
                       WHEN u.Rol = 'Admin' OR u.PerfilId = 1 THEN 'Admin'
                       ELSE ISNULL(p.Nombre, 'Vendedor')
                   END
               FROM UsuarioSQL u
               LEFT JOIN Perfiles p ON u.PerfilId = p.Id
               WHERE u.Usuario = @User OR u.Nombre = @User";

                    var resultado = await conn.ExecuteScalarAsync<string>(sql, new { User = nombreUsuario });

                    if (!string.IsNullOrEmpty(resultado))
                    {
                        perfilCalculado = resultado;
                    }
                }
                catch { }
            }

            // --- EL TRUCO  ADMIN ---
            // Si  usuario  es Admin, y escribiste "?simular=Algo" en la URL, te disfraza:
            if (perfilCalculado == "Admin" && !string.IsNullOrEmpty(simular))
            {
                perfilCalculado = simular;
            }

            ViewData["Perfil"] = perfilCalculado;
            return View();
        }
        [HttpGet("Comercial/ObtenerOVMuestras")]
        public async Task<IActionResult> ObtenerOVMuestras()
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                var esAdmin = User.IsInRole("Administrador") || User.IsInRole("Sistemas");
                int? vendedorId = null;

                if (!esAdmin)
                {
                    var login = User.Identity?.Name ?? "";
                    vendedorId = await conn.ExecuteScalarAsync<int?>(@"
                SELECT u.VendedorId 
                FROM UsuarioSQL u 
                WHERE u.Usuario = @Login", new { Login = login });

                    if (!vendedorId.HasValue)
                        return Json(Array.Empty<object>());
                }

                var sql = @"
    SELECT 
        ov.Id AS id, ov.Consecutivo AS consecutivo, ov.Cliente AS cliente, 
        cs.Nombrecliente AS clienteNombre,
        ov.Vendedor AS vendedor, ov.Ruta AS ruta, 
        ov.Presentacion AS presentacion, ov.FechaEntrega AS fechaEntrega, 
        ov.Observacion AS observacion,
        ovm.SolicitudMuestraId AS solicitudMuestraId
    FROM OrdenVenta ov
    INNER JOIN OrdenVentaMuestra ovm ON ov.Id = ovm.OrdenVentaId
    LEFT JOIN ClienteSap cs ON ov.Cliente = cs.Cliente
    WHERE ovm.EsMuestra = 1 
      AND ovm.SolicitudMuestraId IS NULL
      AND (@VendedorId IS NULL OR ov.VendedorId = @VendedorId)
    ORDER BY ov.FechaRegistro DESC";

                var ovMuestras = await conn.QueryAsync(sql, new { VendedorId = vendedorId });

                return Json(ovMuestras);
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }
        [HttpGet("Comercial/ObtenerProductosOV")]
        public async Task<IActionResult> ObtenerProductosOV(int ovId)
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                var sql = "SELECT ProductoCodigo AS productoCodigo, ProductoNombre AS productoNombre, Cajas AS cajas, Peso AS peso, Precio AS precio FROM OrdenVentaProducto WHERE PedidoId = @PedidoId AND Eliminado = 0";
                var productos = await conn.QueryAsync(sql, new { PedidoId = ovId });

                return Json(productos);
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }


        [HttpPost("Comercial/ActualizarStageSolicitud")]
        public async Task<IActionResult> ActualizarStageSolicitud(string solicitudId, string stage)
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();
                await conn.ExecuteAsync(
                    "UPDATE SolicitudMuestras SET Stage = @Stage WHERE Id = @Id",
                    new { Stage = stage, Id = solicitudId });
                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }


        [HttpGet("Comercial/ObtenerSolicitudes")]
        public async Task<IActionResult> ObtenerSolicitudes(string vendedor = null)

        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                var esAdmin = User.IsInRole("Administrador") || User.IsInRole("Sistemas");

                int? vendedorId = null;
                if (!esAdmin)
                {
                    var login = !string.IsNullOrWhiteSpace(vendedor) ? vendedor : (User.Identity?.Name ?? "");
                    vendedorId = await conn.ExecuteScalarAsync<int?>(@"
                        SELECT u.VendedorId 
                        FROM UsuarioSQL u 
                        WHERE u.Usuario = @Login", new { Login = login });

                    if (!vendedorId.HasValue)
                        return Json(Array.Empty<object>());
                }

                var sql = @"
SELECT 
    s.Id, s.CreatedAt, s.CreatedBy, s.Seller,
    COALESCE(NULLIF(LTRIM(RTRIM(cs.Nombrecliente)), ''), s.Client) AS Client,
    s.Species,
    s.RequestedDate, s.[Route], s.Destination, s.Priority, s.Notes,
    s.Stage, s.Location,
    s.Plan_ProcessDate AS ProcessDate, s.Plan_Shift AS Shift,
    s.Plan_Line AS Line, s.Plan_Planner AS Planner, s.Plan_ReleasedAt AS ReleasedAt
FROM SolicitudMuestras s
LEFT JOIN OrdenVentaMuestra ovm ON s.Id = ovm.SolicitudMuestraId
LEFT JOIN OrdenVenta ov ON ovm.OrdenVentaId = ov.Id
LEFT JOIN ClienteSap cs ON s.Client = cs.Cliente
WHERE s.Activo = 1 AND s.CreatedAt >= DATEADD(day, -60, GETDATE())
AND (@VendedorId IS NULL OR ov.VendedorId = @VendedorId)
ORDER BY s.CreatedAt DESC";

                var solicitudes = await conn.QueryAsync(sql, new { VendedorId = vendedorId });

                var lista = new List<Plataforma_CG.Models.SolicitudMuestraVM>();
                foreach (var s in solicitudes)
                {
                    var vm = new Plataforma_CG.Models.SolicitudMuestraVM
                    {
                        Id = s.Id,
                        CreatedAt = s.CreatedAt,
                        CreatedBy = s.CreatedBy ?? "",
                        Seller = s.Seller ?? "",
                        Client = s.Client ?? "",
                        Species = s.Species ?? "",
                        RequestedDate = s.RequestedDate,
                        Route = s.Route ?? "",
                        Destination = s.Destination ?? "",
                        Priority = s.Priority ?? "Media",
                        Notes = s.Notes ?? "",
                        Stage = s.Stage ?? "Pendiente",
                        Location = s.Location ?? "",
                        Planning = s.ProcessDate != null ? new Plataforma_CG.Models.PlaneacionVM
                        {
                            ProcessDate = s.ProcessDate,
                            Shift = s.Shift,
                            Line = s.Line,
                            Planner = s.Planner,
                            ReleasedAt = s.ReleasedAt
                        } : null
                    };

                    var itemsSql = "SELECT * FROM SolicitudMuestras_Items WHERE SolicitudId = @Id";
                    var items = await conn.QueryAsync(itemsSql, new { Id = s.Id });

                    foreach (var i in items)
                    {
                        var itemVM = new Plataforma_CG.Models.ItemMuestraVM
                        {
                            Uid = i.Uid,
                            Sku = i.Sku ?? "",
                            WorkSku = i.WorkSku,
                            Product = i.Product ?? "",
                            Spec = i.Spec ?? "",
                            Boxes = i.Boxes,
                            Temp = i.Temp ?? "Refrigerado"
                        };

                        var labelsSql = "SELECT * FROM SolicitudMuestras_Etiquetas WHERE ItemUid = @Uid";
                        var labels = await conn.QueryAsync(labelsSql, new { Uid = i.Uid });
                        itemVM.Labels = labels.Select(l => new Plataforma_CG.Models.EtiquetaVM
                        {
                            Code = l.Code,
                            ExternalChain = l.ExternalChain,
                            Operator = l.Operator ?? "",
                            ProcessedAt = l.ProcessedAt,
                            Location = l.Location ?? ""
                        }).ToList();

                        vm.Items.Add(itemVM);
                    }

                    lista.Add(vm);
                }

                return Json(lista);
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet("Comercial/ObtenerClientesVendedor")]
        public async Task<IActionResult> ObtenerClientesVendedor(string usuario)
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                // Cruzamos las tablas por VendedorId y filtramos por el nombre de usuario
                var sql = @"
        SELECT c.Cliente AS Id, c.Nombrecliente AS Nombre
        FROM ClienteSap c
        INNER JOIN UsuarioSQL u ON c.VendedorId = u.VendedorId
        WHERE (u.Usuario = @Usuario OR u.Nombre = @Usuario)
          AND c.U_MT_Clasificacion = 'ACTIVO'
        ORDER BY c.Nombrecliente";

                // Usamos Dapper para ejecutar la consulta
                var clientes = await conn.QueryAsync(sql, new { Usuario = usuario });
                return Json(clientes);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }


        [HttpGet("Comercial/ObtenerArticulosSap")]
        public async Task<IActionResult> ObtenerArticulosSap()
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                var sql = "SELECT ProductoCodigo AS Codigo, ProductoNombre AS Nombre FROM ArticuloSap ORDER BY ProductoCodigo";
                var articulos = (await conn.QueryAsync(sql))
                    .Select(a => new { Codigo = (string)a.Codigo, Nombre = (string)a.Nombre })
                    .ToList();
                return Json(articulos);
            }
            catch
            {
                return Json(Array.Empty<object>());
            }
        }

        [HttpPost("Comercial/CrearSolicitud")]
        [RevisarPermiso("SOLICITUD_MUESTRAS_VENDEDOR", "ESCRIBIR")]
        public async Task<IActionResult> CrearSolicitud([FromBody] Plataforma_CG.Models.SolicitudMuestraVM nuevaSolicitud)
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                // Calculo de folio en servidor
                var anioActual = DateTime.Now.Year.ToString();
                var sqlFolio = @"
                   SELECT ISNULL(MAX(CAST(SUBSTRING(Id, 9, 4) AS INT)), 0) + 1 
                   FROM SolicitudMuestras 
                   WHERE Id LIKE 'SM-' + @Anio + '-%'";

                var siguienteNum = await conn.ExecuteScalarAsync<int>(sqlFolio, new { Anio = anioActual });
                var folioDefinitivo = $"SM-{anioActual}-{siguienteNum:D4}";

                // Asignacion de folio oficial
                nuevaSolicitud.Id = folioDefinitivo;

                var sqlSolicitud = @"
       INSERT INTO SolicitudMuestras 
           (Id, CreatedAt, CreatedBy, Seller, Client, Species, RequestedDate, 
            Route, Destination, Priority, Notes, Stage, Location)
       VALUES 
           (@Id, @CreatedAt, @CreatedBy, @Seller, @Client, @Species, @RequestedDate,
            @Route, @Destination, @Priority, @Notes, @Stage, @Location)";

                await conn.ExecuteAsync(sqlSolicitud, new
                {
                    nuevaSolicitud.Id,
                    CreatedAt = DateTime.Now,
                    CreatedBy = nuevaSolicitud.CreatedBy ?? "Sistema",
                    nuevaSolicitud.Seller,
                    nuevaSolicitud.Client,
                    nuevaSolicitud.Species,
                    nuevaSolicitud.RequestedDate,
                    nuevaSolicitud.Route,
                    nuevaSolicitud.Destination,
                    nuevaSolicitud.Priority,
                    nuevaSolicitud.Notes,
                    Stage = "Planeación pendiente",
                    Location = "Comercial"
                });

                if (nuevaSolicitud.Items != null && nuevaSolicitud.Items.Any())
                {
                    var sqlItem = @"
           INSERT INTO SolicitudMuestras_Items 
               (Uid, SolicitudId, Sku, WorkSku, Product, Spec, Boxes, Temp)
           VALUES 
               (@Uid, @SolicitudId, @Sku, @WorkSku, @Product, @Spec, @Boxes, @Temp)";

                    foreach (var item in nuevaSolicitud.Items)
                    {
                        await conn.ExecuteAsync(sqlItem, new
                        {
                            Uid = item.Uid ?? Guid.NewGuid().ToString("N").Substring(0, 10),
                            SolicitudId = nuevaSolicitud.Id,
                            Sku = item.Sku ?? "SD",
                            WorkSku = item.WorkSku ?? (object)DBNull.Value,
                            Product = item.Product ?? "SD",
                            Spec = item.Spec ?? "",
                            Boxes = item.Boxes <= 0 ? 1 : item.Boxes,
                            Temp = item.Temp ?? "Refrigerado"
                        });
                    }
                }

                if (nuevaSolicitud.OrdenVentaId.HasValue)
                {
                    await conn.ExecuteAsync(
                        "UPDATE OrdenVentaMuestra SET SolicitudMuestraId = @SolicitudId WHERE OrdenVentaId = @OrdenVentaId",
                        new { SolicitudId = nuevaSolicitud.Id, OrdenVentaId = nuevaSolicitud.OrdenVentaId.Value });
                }

                return Json(new { ok = true, mensaje = "Solicitud creada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        //[HttpPost("Comercial/GuardarPlaneacion")]
        //[RevisarPermiso("SOLICITUD_MUESTRAS_PLANEACION", "ESCRIBIR")]
        //public async Task<IActionResult> GuardarPlaneacion([FromBody] Plataforma_CG.Models.SolicitudMuestraVM solicitudActualizada)
        //{
        //    try
        //    {
        //        using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        //        await conn.OpenAsync();

        //        var sql = @"
        //UPDATE SolicitudMuestras 
        //SET Stage = @Stage,
        //    Location = @Location,
        //    Plan_ProcessDate = @ProcessDate,
        //    Plan_Shift = @Shift,
        //    Plan_Line = @Line,
        //    Plan_Planner = @Planner,
        //    Plan_ReleasedAt = @ReleasedAt
        //WHERE Id = @Id";

        //        await conn.ExecuteAsync(sql, new
        //        {
        //            solicitudActualizada.Id,
        //            Stage = "Liberado a producción",
        //            Location = "Producción",
        //            ProcessDate = solicitudActualizada.Planning?.ProcessDate,
        //            Shift = solicitudActualizada.Planning?.Shift,
        //            Line = solicitudActualizada.Planning?.Line,
        //            Planner = solicitudActualizada.Planning?.Planner,
        //            ReleasedAt = DateTime.Now
        //        });

        //        if (solicitudActualizada.Items != null)
        //        {
        //            foreach (var item in solicitudActualizada.Items)
        //            {
        //                await conn.ExecuteAsync(
        //                    "UPDATE SolicitudMuestras_Items SET WorkSku = @WorkSku WHERE Uid = @Uid",
        //                    new { item.Uid, WorkSku = item.WorkSku ?? (object)DBNull.Value });
        //            }
        //        }

        //        return Json(new { ok = true, mensaje = "Liberado a producción." });
        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new { ok = false, mensaje = ex.Message });
        //    }
        //}

        //[HttpPost("Comercial/LigarEtiqueta")]
        //[RevisarPermiso("SOLICITUD_MUESTRAS_PRODUCCION", "ESCRIBIR")]
        //public async Task<IActionResult> LigarEtiqueta([FromBody] Plataforma_CG.Models.EtiquetaVM nuevaEtiqueta, string solicitudId, string itemUid)
        //{
        //    try
        //    {
        //        using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        //        await conn.OpenAsync();

        //        var existe = await conn.ExecuteScalarAsync<int>(
        //            "SELECT COUNT(1) FROM SolicitudMuestras_Etiquetas WHERE ExternalChain = @Chain",
        //            new { Chain = nuevaEtiqueta.ExternalChain });

        //        if (existe > 0)
        //            return Json(new { ok = false, mensaje = "La cadena de conexión externa ya está registrada." });

        //        var sql = @"
        //INSERT INTO SolicitudMuestras_Etiquetas (Code, ItemUid, ExternalChain, Operator, ProcessedAt, Location, PesoReal)
        //VALUES (@Code, @ItemUid, @ExternalChain, @Operator, @ProcessedAt, @Location, @PesoReal)";

        //        await conn.ExecuteAsync(sql, new
        //        {
        //            nuevaEtiqueta.Code,
        //            ItemUid = itemUid,
        //            nuevaEtiqueta.ExternalChain,
        //            Operator = nuevaEtiqueta.Operator ?? "Sistema",
        //            ProcessedAt = DateTime.Now,
        //            Location = "Producción",
        //            PesoReal = (decimal?)null
        //        });

        //        return Json(new { ok = true, mensaje = "Etiqueta ligada." });
        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new { ok = false, mensaje = ex.Message });
        //    }
        //}

        [HttpPost("Comercial/ActualizarUbicacion")]
        [RevisarPermiso("SOLICITUD_MUESTRAS_PRODUCCION", "ESCRIBIR")]
        public async Task<IActionResult> ActualizarUbicacion(string solicitudId, string ubicacion)
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                var sql = "UPDATE SolicitudMuestras SET Location = @Ubicacion, Stage = @Stage WHERE Id = @Id";
                await conn.ExecuteAsync(sql, new
                {
                    Id = solicitudId,
                    Ubicacion = ubicacion,
                    Stage = ubicacion == "Surtido al cliente" ? "Surtido al cliente" : "En almacén"
                });

                return Json(new { ok = true, mensaje = "Ubicación actualizada." });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpPost("Comercial/CancelarSolicitud")]
        public async Task<IActionResult> CancelarSolicitud(string id, string motivo)
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                var sql = "UPDATE SolicitudMuestras SET Activo = 0, Stage = 'Cancelada', CancelReason = @Motivo WHERE Id = @Id";
                await conn.ExecuteAsync(sql, new { Id = id, Motivo = motivo });

                return Json(new { ok = true, mensaje = "Solicitud cancelada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }


        ///////////precios por sku ///////////////////////
        [HttpGet]
        public IActionResult AjustePrecios()
        {
            return View();
        }
        [HttpGet("Comercial/ObtenerPreciosGuadalajara")]
        public async Task<IActionResult> ObtenerPreciosGuadalajara(string planta = "TIF")
        {
            try
            {
                // Validar planta y asignar cadena
                planta = string.IsNullOrWhiteSpace(planta) ? "TIF" : planta.ToUpper();
                string nombreCadena = (planta == "P1") ? "CadenaMeatP1" : "CadenaMeatTIF";
                string dbCatalogo = (planta == "P1") ? "CommerciaNet" : "TIF_CommerciaNet";

                using var connMeat = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString(nombreCadena));
                await connMeat.OpenAsync();

                // Consulta usando el catalogo dinamico
                var sqlMeat = $@"
        SELECT 
            p.ArticuloId AS Sku,
            ISNULL(a.Nombre, 'PRODUCTO SIN DESCRIPCION') AS Nombre,
            p.Publico AS Precio
        FROM [{dbCatalogo}].[dbo].[Precio] p
        LEFT JOIN [{dbCatalogo}].[dbo].[Articulo] a ON p.ArticuloId = a.ArticuloId
        WHERE p.EmpresaId = 'CARNG' AND p.ZonaId = 'GUA' AND p.Criterio = 'NV1'";

                var precios = await connMeat.QueryAsync(sqlMeat);

                // Conexion independiente para SIGO
                using var connSigo = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connSigo.OpenAsync();

                var sqlSigo = "SELECT ProductoCodigo AS Sku, U_MASTER AS CategoriaMaster FROM ArticuloSap";
                var articulosSap = await connSigo.QueryAsync(sqlSigo);

                var listaFinal = precios.Select(p => {
                    var sapData = articulosSap.FirstOrDefault(s => s.Sku == p.Sku);
                    return new
                    {
                        Sku = p.Sku,
                        Nombre = p.Nombre,
                        CategoriaMaster = sapData != null ? sapData.CategoriaMaster : "(SIN MASTER)",
                        Precio = p.Precio
                    };
                });

                return Json(listaFinal);
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet("Comercial/ObtenerLogGeneralPrecios")]
        public async Task<IActionResult> ObtenerLogGeneralPrecios(string planta = "TODAS")
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                var sql = @"
        SELECT TOP 300
            ArticuloId AS Sku, PrecioAnterior, PrecioNuevo, Usuario, FechaHora, ISNULL(Planta, 'N/A') AS Planta
        FROM LogAjustePrecios 
        WHERE 1=1 ";

                if (planta != "TODAS") sql += " AND Planta = @Planta ";
                sql += " ORDER BY FechaHora DESC";

                var historial = await conn.QueryAsync(sql, new { Planta = planta });
                return Json(historial);
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpGet("Comercial/ObtenerHistorialProfundoSku")]
        public async Task<IActionResult> ObtenerHistorialProfundoSku(string sku, int? mes, int? anio, string planta = "TODAS")
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                var sql = @"
        SELECT ArticuloId AS Sku, PrecioAnterior, PrecioNuevo, Usuario, FechaHora, ISNULL(Planta, 'N/A') AS Planta 
        FROM LogAjustePrecios 
        WHERE ArticuloId = @Sku ";

                if (mes.HasValue && anio.HasValue) sql += " AND MONTH(FechaHora) = @Mes AND YEAR(FechaHora) = @Anio ";
                if (planta != "TODAS") sql += " AND Planta = @Planta ";

                sql += " ORDER BY FechaHora DESC";

                var historial = await conn.QueryAsync(sql, new { Sku = sku, Mes = mes, Anio = anio, Planta = planta });
                return Json(historial);
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpPost("Comercial/GuardarPreciosMasivo")]
        public async Task<IActionResult> GuardarPreciosMasivo([FromBody] List<DtoAjustePrecio> paquete, [FromQuery] string planta = "TIF")
        {
            if (paquete == null || !paquete.Any())
                return Json(new { ok = false, mensaje = "Sin datos para actualizar." });

            var nombreUsuario = User.Identity?.Name ?? "Sistema";

            // Validar planta para guardado
            planta = string.IsNullOrWhiteSpace(planta) ? "TIF" : planta.ToUpper();
            string nombreCadena = (planta == "P1") ? "CadenaMeatP1" : "CadenaMeatTIF";
            string dbCatalogo = (planta == "P1") ? "CommerciaNet" : "TIF_CommerciaNet";

            using var connMeat = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString(nombreCadena));
            using var connSigo = new Microsoft.Data.SqlClient.SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            await connMeat.OpenAsync();
            await connSigo.OpenAsync();

            using var txMeat = connMeat.BeginTransaction();
            using var txSigo = connSigo.BeginTransaction();

            try
            {
                var sqlSelectBase = $@"
        SELECT Publico FROM [{dbCatalogo}].[dbo].[Precio] 
        WHERE ArticuloId = @Sku AND EmpresaId = 'CARNG' AND ZonaId = 'GUA' AND Criterio = 'NV1'";

                var sqlUpdateMeat = $@"
        UPDATE [{dbCatalogo}].[dbo].[Precio] 
        SET Publico = @Precio, FechaHora = GETDATE()
        WHERE ArticuloId = @Sku AND EmpresaId = 'CARNG' AND ZonaId = 'GUA' AND Criterio = 'NV1'";

                var sqlLogSigo = @"
INSERT INTO LogAjustePrecios (ArticuloId, PrecioAnterior, PrecioNuevo, Usuario, FechaHora, Planta)
VALUES (@Sku, @PrecioAnterior, @PrecioNuevo, @Usuario, GETDATE(), @Planta)";
                foreach (var item in paquete)
                {
                    var precioAnterior = await connMeat.QueryFirstOrDefaultAsync<decimal>(sqlSelectBase, new { Sku = item.Sku }, txMeat);

                    await connMeat.ExecuteAsync(sqlUpdateMeat, item, txMeat);

                    await connSigo.ExecuteAsync(sqlLogSigo, new
                    {
                        Sku = item.Sku,
                        PrecioAnterior = precioAnterior,
                        PrecioNuevo = item.Precio,
                        Usuario = nombreUsuario,
                        Planta = planta
                    }, txSigo);
                }

                txMeat.Commit();
                txSigo.Commit();
                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                txMeat.Rollback();
                txSigo.Rollback();
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        public class DtoAjustePrecio
        {
            public string Sku { get; set; } = "";
            public decimal Precio { get; set; }
        }


        // ============================================================================
        // PEGAR DENTRO DE ComercialController
        // Reemplaza tus métodos actuales GuardarPlaneacion y LigarEtiqueta por este bloque completo.
        // Requiere los using que tu controlador ya tiene:
        // using Dapper;
        // using Microsoft.Data.SqlClient;
        // using System.Data;
        // using System.Text.RegularExpressions;
        // ============================================================================

        private sealed class SolicitudItemEtiquetaValidacionRow
        {
            public string Uid { get; set; } = "";
            public string SolicitudId { get; set; } = "";
            public string Sku { get; set; } = "";
            public string? WorkSku { get; set; }
            public int Boxes { get; set; }
            public bool Activo { get; set; }
            public string Stage { get; set; } = "";
            public DateTime? PlanReleasedAt { get; set; }
            public int EtiquetasLigadas { get; set; }
        }

        private sealed class ProduccionEtiquetaMeatRow
        {
            public int ProduccionId { get; set; }
            public long? LoteId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
            public string Articulo { get; set; } = "";
            public int Estatus { get; set; }
            public decimal? PesoNeto { get; set; }
            public string? Almacen { get; set; }
            public DateTime? FechaProduccion { get; set; }
            public DateTime? FechaHora { get; set; }
        }


        // ============================================================================
        // PLANEACIÓN: obliga a asignar un artículo válido a CADA renglón.
        // La liberación es fail-closed: si falta un SKU o SQL falla, no se libera.
        // ============================================================================
        [HttpPost("Comercial/GuardarPlaneacion")]
        [RevisarPermiso("SOLICITUD_MUESTRAS_PLANEACION", "ESCRIBIR")]
        public async Task<IActionResult> GuardarPlaneacion(
            [FromBody] Plataforma_CG.Models.SolicitudMuestraVM solicitudActualizada)
        {
            static string N(string? valor) =>
                (valor ?? string.Empty).Trim().ToUpperInvariant();

            if (solicitudActualizada == null)
                return Json(new { ok = false, mensaje = "La planeación recibida es inválida." });

            var solicitudId = (solicitudActualizada.Id ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(solicitudId))
                return Json(new { ok = false, mensaje = "La solicitud es obligatoria." });

            if (solicitudActualizada.Planning == null)
                return Json(new { ok = false, mensaje = "Capture los datos de Planeación." });

            var processDateText = Convert.ToString(
                solicitudActualizada.Planning.ProcessDate,
                System.Globalization.CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(processDateText))
                return Json(new { ok = false, mensaje = "La fecha de proceso es obligatoria." });

            var shift = (solicitudActualizada.Planning.Shift ?? string.Empty).Trim();
            var line = (solicitudActualizada.Planning.Line ?? string.Empty).Trim();
            var planner = (solicitudActualizada.Planning.Planner ?? string.Empty).Trim();
            var especificacion = (solicitudActualizada.Planning.Especificacion ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(shift))
                return Json(new { ok = false, mensaje = "El turno es obligatorio." });

            if (string.IsNullOrWhiteSpace(line))
                return Json(new { ok = false, mensaje = "La línea es obligatoria." });

            if (string.IsNullOrWhiteSpace(planner))
                planner = (User?.Identity?.Name ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(planner))
                return Json(new { ok = false, mensaje = "No se pudo identificar al planeador." });

            var itemsRecibidos = (solicitudActualizada.Items ?? new List<Plataforma_CG.Models.ItemMuestraVM>())
                .Where(i => i != null)
                .Select(i => new
                {
                    Uid = (i.Uid ?? string.Empty).Trim(),
                    SkuSolicitado = N(i.Sku),
                    WorkSku = N(i.WorkSku)
                })
                .Where(i => !string.IsNullOrWhiteSpace(i.Uid))
                .GroupBy(i => i.Uid, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();

            if (itemsRecibidos.Count == 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "La solicitud no contiene artículos para planear."
                });
            }

            var sinArticulo = itemsRecibidos
                .Where(i => string.IsNullOrWhiteSpace(i.WorkSku))
                .Select(i => string.IsNullOrWhiteSpace(i.SkuSolicitado) ? i.Uid : i.SkuSolicitado)
                .ToList();

            if (sinArticulo.Count > 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje =
                        "No se puede liberar a producción. Asigne un artículo a todos los renglones. " +
                        $"Pendientes: {string.Join(", ", sinArticulo.Take(10))}."
                });
            }

            var cadena = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(cadena))
            {
                _logger.LogError("No se encontró la cadena DefaultConnection.");
                return Json(new
                {
                    ok = false,
                    mensaje = "No está configurada la conexión de SIGO."
                });
            }

            try
            {
                using var conn = new SqlConnection(cadena);
                await conn.OpenAsync();

                using var tx = conn.BeginTransaction(IsolationLevel.Serializable);

                try
                {
                    var solicitudActiva = await conn.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(1)
                  FROM dbo.SolicitudMuestras WITH (UPDLOCK, HOLDLOCK)
                  WHERE Id = @Id
                    AND ISNULL(Activo, 0) = 1;",
                        new { Id = solicitudId },
                        tx);

                    if (solicitudActiva != 1)
                    {
                        tx.Rollback();
                        return Json(new
                        {
                            ok = false,
                            mensaje = "La solicitud no existe, está cancelada o ya no está activa."
                        });
                    }

                    var uidsBase = (await conn.QueryAsync<string>(
                        @"SELECT Uid
                  FROM dbo.SolicitudMuestras_Items WITH (UPDLOCK, HOLDLOCK)
                  WHERE SolicitudId = @SolicitudId;",
                        new { SolicitudId = solicitudId },
                        tx))
                        .Select(x => (x ?? string.Empty).Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (uidsBase.Count == 0)
                    {
                        tx.Rollback();
                        return Json(new
                        {
                            ok = false,
                            mensaje = "La solicitud no tiene artículos registrados en la base de datos."
                        });
                    }

                    var recibidosPorUid = itemsRecibidos.ToDictionary(
                        x => x.Uid,
                        x => x,
                        StringComparer.OrdinalIgnoreCase);

                    var faltantesBase = uidsBase
                        .Where(uid =>
                            !recibidosPorUid.TryGetValue(uid, out var item) ||
                            string.IsNullOrWhiteSpace(item.WorkSku))
                        .ToList();

                    if (faltantesBase.Count > 0)
                    {
                        tx.Rollback();
                        return Json(new
                        {
                            ok = false,
                            mensaje =
                                "No se puede liberar. Existen renglones sin artículo asignado " +
                                $"({faltantesBase.Count}). Recargue la pantalla y complete todos los SKU."
                        });
                    }

                    var extras = itemsRecibidos
                        .Where(i => !uidsBase.Contains(i.Uid, StringComparer.OrdinalIgnoreCase))
                        .Select(i => i.Uid)
                        .ToList();

                    if (extras.Count > 0)
                    {
                        tx.Rollback();
                        return Json(new
                        {
                            ok = false,
                            mensaje = "La petición contiene artículos que no pertenecen a la solicitud."
                        });
                    }

                    // Blindaje adicional: el WorkSku debe existir realmente en ArticuloSap.
                    var skusAsignados = itemsRecibidos
                        .Select(i => i.WorkSku)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    var skusCatalogo = (await conn.QueryAsync<string>(
                        @"SELECT DISTINCT UPPER(LTRIM(RTRIM(ProductoCodigo)))
                  FROM dbo.ArticuloSap
                  WHERE UPPER(LTRIM(RTRIM(ProductoCodigo))) IN @Skus;",
                        new { Skus = skusAsignados },
                        tx))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var skusInvalidos = skusAsignados
                        .Where(sku => !skusCatalogo.Contains(sku))
                        .ToList();

                    if (skusInvalidos.Count > 0)
                    {
                        tx.Rollback();
                        return Json(new
                        {
                            ok = false,
                            mensaje =
                                "No se puede liberar. Los siguientes artículos no existen en ArticuloSap: " +
                                string.Join(", ", skusInvalidos.Take(10))
                        });
                    }

                    foreach (var uid in uidsBase)
                    {
                        var item = recibidosPorUid[uid];

                        var actualizados = await conn.ExecuteAsync(
                            @"UPDATE dbo.SolicitudMuestras_Items
                      SET WorkSku = @WorkSku
                      WHERE Uid = @Uid
                        AND SolicitudId = @SolicitudId;",
                            new
                            {
                                WorkSku = item.WorkSku,
                                Uid = uid,
                                SolicitudId = solicitudId
                            },
                            tx);

                        if (actualizados != 1)
                            throw new InvalidOperationException($"No se pudo actualizar el renglón {uid}.");
                    }

                    var solicitudActualizadaCount = await conn.ExecuteAsync(
                        @"UPDATE dbo.SolicitudMuestras
                  SET Stage = N'Liberado a producción',
                      Location = N'Producción',
                      Plan_ProcessDate = @ProcessDate,
                      Plan_Shift = @Shift,
                      Plan_Line = @Line,
                      Plan_Planner = @Planner,
                      Plan_ReleasedAt = @ReleasedAt
                  WHERE Id = @Id
                    AND ISNULL(Activo, 0) = 1;",
                        new
                        {
                            Id = solicitudId,
                            ProcessDate = solicitudActualizada.Planning.ProcessDate,
                            Shift = shift,
                            Line = line,
                            Planner = planner,
                            ReleasedAt = DateTime.Now
                        },
                        tx);

                    if (solicitudActualizadaCount != 1)
                        throw new InvalidOperationException("No se pudo liberar la solicitud a producción.");

                    tx.Commit();

                    return Json(new
                    {
                        ok = true,
                        mensaje =
                            $"Solicitud {solicitudId} liberada a producción con " +
                            $"{uidsBase.Count} artículo(s) asignado(s)."
                    });
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Error SQL guardando la planeación de {SolicitudId}.",
                    solicitudId);

                return Json(new
                {
                    ok = false,
                    mensaje = "No fue posible guardar la planeación. No se liberó a producción."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error guardando la planeación de {SolicitudId}.",
                    solicitudId);

                return Json(new
                {
                    ok = false,
                    mensaje = ex.GetBaseException().Message
                });
            }
        }

        [HttpPost("Comercial/LigarEtiqueta")]
        [RevisarPermiso("SOLICITUD_MUESTRAS_PRODUCCION", "ESCRIBIR")]
        public async Task<IActionResult> LigarEtiqueta(
            [FromBody] Plataforma_CG.Models.EtiquetaVM nuevaEtiqueta,
            string solicitudId,
            string itemUid,
            string planta)
        {
            static string Normalizar(string? valor) =>
                (valor ?? string.Empty).Trim().ToUpperInvariant();

            var etiqueta = Normalizar(
                !string.IsNullOrWhiteSpace(nuevaEtiqueta?.ExternalChain)
                    ? nuevaEtiqueta.ExternalChain
                    : nuevaEtiqueta?.Code);

            solicitudId = (solicitudId ?? string.Empty).Trim();
            itemUid = (itemUid ?? string.Empty).Trim();
            planta = Normalizar(planta);

            if (string.IsNullOrWhiteSpace(solicitudId))
                return Json(new { ok = false, mensaje = "La solicitud es obligatoria." });

            if (string.IsNullOrWhiteSpace(itemUid))
                return Json(new { ok = false, mensaje = "El artículo de la solicitud es obligatorio." });

            if (string.IsNullOrWhiteSpace(etiqueta))
                return Json(new { ok = false, mensaje = "La etiqueta DEST o RETT es obligatoria." });

            if (etiqueta.Length > 100)
                return Json(new { ok = false, mensaje = "La etiqueta excede la longitud permitida." });

            if (!Regex.IsMatch(etiqueta, @"^(?:DEST|RETT)[0-9A-Z_-]+$", RegexOptions.IgnoreCase))
            {
                return Json(new
                {
                    ok = false,
                    mensaje = $"La etiqueta {etiqueta} no tiene un formato DEST o RETT válido."
                });
            }

            var tipoEtiqueta = etiqueta.StartsWith("RETT", StringComparison.OrdinalIgnoreCase)
                ? "RETT"
                : "DEST";

            if (planta != "P1" && planta != "TIF")
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "Seleccione la planta de origen de la etiqueta: P1 o TIF."
                });
            }

            var nombreCadena = planta == "P1"
                ? "CadenaMeatP1"
                : "CadenaMeatTIF";

            var cadenaSigo = _configuration.GetConnectionString("DefaultConnection");
            var cadenaMeat = _configuration.GetConnectionString(nombreCadena);

            if (string.IsNullOrWhiteSpace(cadenaSigo))
            {
                _logger.LogError("No se encontró la cadena DefaultConnection.");
                return Json(new
                {
                    ok = false,
                    mensaje = "No está configurada la conexión de SIGO. La etiqueta no fue ligada."
                });
            }

            if (string.IsNullOrWhiteSpace(cadenaMeat))
            {
                _logger.LogError("No se encontró la cadena {CadenaMeat}.", nombreCadena);
                return Json(new
                {
                    ok = false,
                    mensaje = $"No está configurada la conexión {nombreCadena}. La etiqueta no fue ligada."
                });
            }

            try
            {
                // --------------------------------------------------------------------
                // 1. Obtener la solicitud, el SKU planeado y el avance actual desde SIGO
                // --------------------------------------------------------------------
                using var connSigo = new SqlConnection(cadenaSigo);
                await connSigo.OpenAsync();

                const string sqlItem = @"
SELECT TOP (1)
    i.Uid,
    i.SolicitudId,
    Sku = ISNULL(i.Sku, ''),
    i.WorkSku,
    Boxes = ISNULL(i.Boxes, 0),
    Activo = CAST(ISNULL(s.Activo, 0) AS bit),
    Stage = ISNULL(s.Stage, ''),
    PlanReleasedAt = s.Plan_ReleasedAt,
    EtiquetasLigadas =
    (
        SELECT COUNT(1)
        FROM dbo.SolicitudMuestras_Etiquetas e
        WHERE e.ItemUid = i.Uid
    )
FROM dbo.SolicitudMuestras_Items i
INNER JOIN dbo.SolicitudMuestras s
    ON s.Id = i.SolicitudId
WHERE i.Uid = @ItemUid
  AND i.SolicitudId = @SolicitudId;";

                var item = await connSigo.QuerySingleOrDefaultAsync<SolicitudItemEtiquetaValidacionRow>(
                    sqlItem,
                    new
                    {
                        ItemUid = itemUid,
                        SolicitudId = solicitudId
                    });

                if (item == null)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "No se encontró el SKU dentro de la solicitud seleccionada."
                    });
                }

                if (!item.Activo)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "La solicitud está cancelada o inactiva. No se pueden ligar etiquetas."
                    });
                }

                if (!item.PlanReleasedAt.HasValue)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "La solicitud todavía no ha sido liberada por Planeación."
                    });
                }

                var skuPlaneado = Normalizar(item.WorkSku);

                if (string.IsNullOrWhiteSpace(skuPlaneado))
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "El artículo no tiene un SKU de trabajo definido en Planeación."
                    });
                }

                if (item.Boxes <= 0)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "El artículo no tiene una cantidad válida de cajas."
                    });
                }

                if (item.EtiquetasLigadas >= item.Boxes)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = $"El SKU {skuPlaneado} ya tiene todas sus cajas ligadas."
                    });
                }

                var yaExiste = await connSigo.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
              FROM dbo.SolicitudMuestras_Etiquetas
              WHERE UPPER(LTRIM(RTRIM(ISNULL(ExternalChain, '')))) = @Etiqueta
                 OR UPPER(LTRIM(RTRIM(ISNULL(Code, '')))) = @Etiqueta;",
                    new { Etiqueta = etiqueta });

                if (yaExiste > 0)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = $"La etiqueta {etiqueta} ya fue ligada anteriormente."
                    });
                }

                // --------------------------------------------------------------------
                // 2. Consultar la etiqueta exacta en Produccion de la planta seleccionada
                // --------------------------------------------------------------------
                using var connMeat = new SqlConnection(cadenaMeat);
                await connMeat.OpenAsync();

                const string sqlMeat = @"
SELECT TOP (2)
    ProduccionId,
    LoteId,
    CodigoEtiqueta = ISNULL(CodigoEtiqueta, ''),
    Articulo = ISNULL(Articulo, ''),
    Estatus = ISNULL(Estatus, 0),
    PesoNeto,
    Almacen,
    FechaProduccion,
    FechaHora
FROM dbo.Produccion
WHERE UPPER(LTRIM(RTRIM(ISNULL(CodigoEtiqueta, '')))) = @Etiqueta
ORDER BY FechaHora DESC, ProduccionId DESC;";

                var coincidencias = (await connMeat.QueryAsync<ProduccionEtiquetaMeatRow>(
                    sqlMeat,
                    new { Etiqueta = etiqueta }))
                    .ToList();

                if (coincidencias.Count == 0)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje =
                            $"La etiqueta {etiqueta} no existe en {nombreCadena}. " +
                            "Verifique la planta seleccionada y vuelva a escanear."
                    });
                }

                if (coincidencias.Count > 1)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje =
                            $"La etiqueta {etiqueta} tiene más de un registro en {nombreCadena}. " +
                            "Debe revisarse antes de ligarla."
                    });
                }

                var produccion = coincidencias[0];
                var skuMeat = Normalizar(produccion.Articulo);

                if (produccion.Estatus != 1)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje =
                            $"La etiqueta {etiqueta} existe en {nombreCadena}, " +
                            $"pero no está activa. Estatus encontrado: {produccion.Estatus}."
                    });
                }

                if (string.IsNullOrWhiteSpace(skuMeat))
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje =
                            $"La etiqueta {etiqueta} no tiene un artículo válido registrado en MEAT."
                    });
                }

                if (!string.Equals(skuMeat, skuPlaneado, StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje =
                            $"Etiqueta rechazada. La etiqueta {etiqueta} pertenece al SKU " +
                            $"{skuMeat}, pero Planeación indicó {skuPlaneado}.",
                        etiqueta,
                        skuPlaneado,
                        skuMeat,
                        estatus = produccion.Estatus,
                        planta,
                        cadena = nombreCadena
                    });
                }

                // --------------------------------------------------------------------
                // 3. Insertar en SIGO con una transacción serializable.
                //    Se repiten las validaciones críticas para evitar doble escaneo.
                // --------------------------------------------------------------------
                using var tx = connSigo.BeginTransaction(IsolationLevel.Serializable);

                try
                {
                    var duplicadoBloqueado = await connSigo.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(1)
                  FROM dbo.SolicitudMuestras_Etiquetas WITH (UPDLOCK, HOLDLOCK)
                  WHERE UPPER(LTRIM(RTRIM(ISNULL(ExternalChain, '')))) = @Etiqueta
                     OR UPPER(LTRIM(RTRIM(ISNULL(Code, '')))) = @Etiqueta;",
                        new { Etiqueta = etiqueta },
                        tx);

                    if (duplicadoBloqueado > 0)
                    {
                        tx.Rollback();

                        return Json(new
                        {
                            ok = false,
                            mensaje = $"La etiqueta {etiqueta} ya fue ligada por otro usuario."
                        });
                    }

                    var ligadasActuales = await connSigo.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(1)
                  FROM dbo.SolicitudMuestras_Etiquetas WITH (UPDLOCK, HOLDLOCK)
                  WHERE ItemUid = @ItemUid;",
                        new { ItemUid = itemUid },
                        tx);

                    if (ligadasActuales >= item.Boxes)
                    {
                        tx.Rollback();

                        return Json(new
                        {
                            ok = false,
                            mensaje = $"El SKU {skuPlaneado} ya tiene todas sus cajas ligadas."
                        });
                    }

                    const string sqlInsert = @"
INSERT INTO dbo.SolicitudMuestras_Etiquetas
(
    Code,
    ItemUid,
    ExternalChain,
    Operator,
    ProcessedAt,
    Location,
    PesoReal
)
VALUES
(
    @Code,
    @ItemUid,
    @ExternalChain,
    @Operator,
    @ProcessedAt,
    @Location,
    @PesoReal
);";

                    var operador = (User?.Identity?.Name ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(operador))
                        operador = (nuevaEtiqueta?.Operator ?? "Sistema").Trim();

                    await connSigo.ExecuteAsync(
                        sqlInsert,
                        new
                        {
                            Code = etiqueta,
                            ItemUid = itemUid,
                            ExternalChain = etiqueta,
                            Operator = operador,
                            ProcessedAt = DateTime.Now,
                            Location = $"Producción {planta}",
                            PesoReal = produccion.PesoNeto
                        },
                        tx);

                    const string sqlActualizarSolicitud = @"
UPDATE s
SET
    s.Location = N'Producción',
    s.Stage =
        CASE
            WHEN
                (
                    SELECT COUNT(1)
                    FROM dbo.SolicitudMuestras_Etiquetas e
                    INNER JOIN dbo.SolicitudMuestras_Items i2
                        ON i2.Uid = e.ItemUid
                    WHERE i2.SolicitudId = s.Id
                )
                >=
                (
                    SELECT ISNULL(SUM(ISNULL(i3.Boxes, 0)), 0)
                    FROM dbo.SolicitudMuestras_Items i3
                    WHERE i3.SolicitudId = s.Id
                )
                AND
                (
                    SELECT ISNULL(SUM(ISNULL(i4.Boxes, 0)), 0)
                    FROM dbo.SolicitudMuestras_Items i4
                    WHERE i4.SolicitudId = s.Id
                ) > 0
            THEN N'Cumplido producción'
            ELSE N'Producción parcial'
        END
FROM dbo.SolicitudMuestras s
WHERE s.Id = @SolicitudId;";

                    await connSigo.ExecuteAsync(
                        sqlActualizarSolicitud,
                        new { SolicitudId = solicitudId },
                        tx);

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }

                return Json(new
                {
                    ok = true,
                    mensaje =
                        $"Etiqueta {etiqueta} validada y ligada correctamente. " +
                        $"SKU {skuMeat}, estatus activo, origen {nombreCadena}.",
                    etiqueta,
                    tipoEtiqueta,
                    skuPlaneado,
                    skuMeat,
                    estatus = produccion.Estatus,
                    pesoNeto = produccion.PesoNeto,
                    produccionId = produccion.ProduccionId,
                    loteId = produccion.LoteId,
                    almacen = produccion.Almacen,
                    planta,
                    cadena = nombreCadena
                });
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                _logger.LogWarning(
                    ex,
                    "Intento de ligar una etiqueta MEAT DEST/RETT duplicada: {Etiqueta}.",
                    etiqueta);

                return Json(new
                {
                    ok = false,
                    mensaje = $"La etiqueta {etiqueta} ya fue ligada anteriormente."
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "Error SQL al validar la etiqueta {Etiqueta} en la planta {Planta}.",
                    etiqueta,
                    planta);

                return Json(new
                {
                    ok = false,
                    mensaje =
                        $"No fue posible validar la etiqueta en {nombreCadena}. " +
                        "La etiqueta NO fue ligada."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error inesperado al ligar la etiqueta {Etiqueta}.",
                    etiqueta);

                return Json(new
                {
                    ok = false,
                    mensaje = "Ocurrió un error inesperado. La etiqueta NO fue ligada."
                });
            }
        }


        // ============================================================================
        // REEMPLAZO ROBUSTO DEL TRACKING MEAT
        // Pegar dentro de ComercialController.
        // Reemplaza:
        //   TrackingEtiquetasMeatRequest
        //   TrackingEtiquetaMeatRow
        //   ConsultarTrackingEtiquetasEnPlantaAsync
        //
        // El endpoint ObtenerTrackingEtiquetasMeat puede conservarse.
        // ============================================================================

        public sealed class TrackingEtiquetasMeatRequest
        {
            public List<string> Etiquetas { get; set; } = new();
        }

        private sealed class TrackingEtiquetaMeatRow
        {
            public string CodigoEtiqueta { get; set; } = string.Empty;
            public int ProduccionId { get; set; }
            public string? Articulo { get; set; }
            public decimal? PesoNeto { get; set; }
            public int Estatus { get; set; }

            // Código que guarda Produccion.Almacen.
            public string? AlmacenCodigo { get; set; }

            // Nombre enriquecido desde CommerciaNET/TIF_CommerciaNET.
            public string? Almacen { get; set; }

            public int TieneVenta { get; set; }
            public long? SolicitudSurtidoId { get; set; }
            public string? Cliente { get; set; }
            public string? Folio { get; set; }
            public string Planta { get; set; } = string.Empty;
        }

        private sealed class TrackingAlmacenMeatRow
        {
            public string AlmacenId { get; set; } = string.Empty;
            public string? Nombre { get; set; }
        }

        private sealed class TrackingVentaMeatRow
        {
            public int ProduccionId { get; set; }
            public long SolicitudSurtidoId { get; set; }
            public string? Cliente { get; set; }
            public string? Folio { get; set; }
        }

        private async Task<List<TrackingEtiquetaMeatRow>> ConsultarTrackingEtiquetasEnPlantaAsync(
            string connectionStringName,
            string planta,
            string catalogoAlmacenes,
            IReadOnlyCollection<string> etiquetas,
            CancellationToken ct)
        {
            static string Normalizar(string? valor) =>
                (valor ?? string.Empty)
                    .Replace("\0", string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            var cadena = _configuration.GetConnectionString(connectionStringName);

            if (string.IsNullOrWhiteSpace(cadena))
            {
                throw new InvalidOperationException(
                    $"No existe la cadena de conexión {connectionStringName}.");
            }

            var catalogoSeguro = catalogoAlmacenes switch
            {
                "CommerciaNET" => "CommerciaNET",
                "TIF_CommerciaNET" => "TIF_CommerciaNET",
                _ => throw new InvalidOperationException(
                    "Catálogo de almacenes no permitido.")
            };

            var etiquetasNormalizadas = etiquetas
                .Select(Normalizar)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (etiquetasNormalizadas.Length == 0)
                return new List<TrackingEtiquetaMeatRow>();

            await using var conn = new SqlConnection(cadena);
            await conn.OpenAsync(ct);

            // ------------------------------------------------------------------------
            // 1. BUSCAR PRODUCCIÓN PRIMERO.
            //
            // Esta consulta no depende de Almacen, SalidaEmbarque ni SurtidoReferencia.
            // Aunque falle algún catálogo adicional, la etiqueta seguirá apareciendo.
            // ------------------------------------------------------------------------
            const string sqlProduccion = @"
SELECT
    CodigoEtiqueta =
        UPPER(LTRIM(RTRIM(ISNULL(p.CodigoEtiqueta, '')))),
    p.ProduccionId,
    Articulo = ISNULL(p.Articulo, ''),
    p.PesoNeto,
    Estatus = ISNULL(p.Estatus, 0),
    AlmacenCodigo =
        LTRIM(RTRIM(CONVERT(NVARCHAR(100), ISNULL(p.Almacen, '')))),
    Almacen =
        LTRIM(RTRIM(CONVERT(NVARCHAR(100), ISNULL(p.Almacen, '')))),
    TieneVenta = CAST(0 AS INT),
    SolicitudSurtidoId = CAST(NULL AS BIGINT),
    Cliente = CAST(NULL AS NVARCHAR(500)),
    Folio = CAST(NULL AS NVARCHAR(500))
FROM dbo.Produccion p
WHERE
    UPPER(LTRIM(RTRIM(ISNULL(p.CodigoEtiqueta, ''))))
        IN @Etiquetas;";

            var rows = (await conn.QueryAsync<TrackingEtiquetaMeatRow>(
                new CommandDefinition(
                    sqlProduccion,
                    new { Etiquetas = etiquetasNormalizadas },
                    commandTimeout: 45,
                    cancellationToken: ct)))
                .ToList();

            foreach (var row in rows)
            {
                row.CodigoEtiqueta = Normalizar(row.CodigoEtiqueta);
                row.AlmacenCodigo = (row.AlmacenCodigo ?? string.Empty).Trim();
                row.Almacen = string.IsNullOrWhiteSpace(row.Almacen)
                    ? row.AlmacenCodigo
                    : row.Almacen.Trim();
                row.Planta = planta;
            }

            if (rows.Count == 0)
                return rows;

            // ------------------------------------------------------------------------
            // 2. ENRIQUECER EL NOMBRE DEL ALMACÉN.
            //
            // Si el usuario SQL no tiene permiso al catálogo CommerciaNET, no se
            // descarta la etiqueta: se conserva el código de Produccion.Almacen.
            // ------------------------------------------------------------------------
            var almacenesIds = rows
                .Select(x => (x.AlmacenCodigo ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (almacenesIds.Length > 0)
            {
                try
                {
                    var sqlAlmacenes = $@"
SELECT
    AlmacenId =
        LTRIM(RTRIM(CONVERT(NVARCHAR(100), AlmacenId))),
    Nombre
FROM [{catalogoSeguro}].dbo.Almacen
WHERE
    LTRIM(RTRIM(CONVERT(NVARCHAR(100), AlmacenId)))
        IN @Almacenes;";

                    var almacenes = (await conn.QueryAsync<TrackingAlmacenMeatRow>(
                        new CommandDefinition(
                            sqlAlmacenes,
                            new { Almacenes = almacenesIds },
                            commandTimeout: 30,
                            cancellationToken: ct)))
                        .ToDictionary(
                            x => (x.AlmacenId ?? string.Empty).Trim(),
                            x => (x.Nombre ?? string.Empty).Trim(),
                            StringComparer.OrdinalIgnoreCase);

                    foreach (var row in rows)
                    {
                        var codigo = (row.AlmacenCodigo ?? string.Empty).Trim();

                        if (almacenes.TryGetValue(codigo, out var nombre)
                            && !string.IsNullOrWhiteSpace(nombre))
                        {
                            row.Almacen = nombre;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "No se pudo enriquecer el almacén de tracking en {Catalogo}. " +
                        "Se mostrará Produccion.Almacen.",
                        catalogoSeguro);
                }
            }

            // ------------------------------------------------------------------------
            // 3. CONSULTAR VENTA.
            //
            // La ausencia o falla de venta tampoco elimina el registro de Produccion.
            // ------------------------------------------------------------------------
            var produccionIds = rows
                .Select(x => x.ProduccionId)
                .Distinct()
                .ToArray();

            try
            {
                const string sqlVentas = @"
;WITH VentaAgrupada AS
(
    SELECT
        se.ProduccionId,
        SolicitudSurtidoId =
            CONVERT(BIGINT, se.SolicitudSurtidoId),
        Cliente =
            MAX(CASE
                    WHEN sr.TipoReferenciaId = 6
                    THEN LTRIM(RTRIM(sr.Referencia))
                END),
        Folio =
            MAX(CASE
                    WHEN sr.TipoReferenciaId = 9
                    THEN LTRIM(RTRIM(sr.Referencia))
                END)
    FROM dbo.SalidaEmbarque se
    LEFT JOIN dbo.SurtidoReferencia sr
        ON sr.SolicitudSurtidoId = se.SolicitudSurtidoId
       AND sr.TipoReferenciaId IN (6, 9)
    WHERE se.ProduccionId IN @ProduccionIds
    GROUP BY
        se.ProduccionId,
        se.SolicitudSurtidoId
),
VentaElegida AS
(
    SELECT
        *,
        rn = ROW_NUMBER() OVER
        (
            PARTITION BY ProduccionId
            ORDER BY SolicitudSurtidoId DESC
        )
    FROM VentaAgrupada
)
SELECT
    ProduccionId,
    SolicitudSurtidoId,
    Cliente,
    Folio
FROM VentaElegida
WHERE rn = 1;";

                var ventas = (await conn.QueryAsync<TrackingVentaMeatRow>(
                    new CommandDefinition(
                        sqlVentas,
                        new { ProduccionIds = produccionIds },
                        commandTimeout: 45,
                        cancellationToken: ct)))
                    .ToDictionary(x => x.ProduccionId);

                foreach (var row in rows)
                {
                    if (!ventas.TryGetValue(row.ProduccionId, out var venta))
                        continue;

                    row.TieneVenta = 1;
                    row.SolicitudSurtidoId = venta.SolicitudSurtidoId;
                    row.Cliente = (venta.Cliente ?? string.Empty).Trim();
                    row.Folio = (venta.Folio ?? string.Empty).Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "No se pudo consultar SalidaEmbarque/SurtidoReferencia para " +
                    "tracking en {Planta}. Se conservará la ubicación de almacén.",
                    planta);
            }

            return rows;
        }

        [HttpGet("~/Comercial/ObtenerTrackingEtiquetasMeat")]
        //[RevisarPermiso("SOLICITUD_MUESTRAS_TRACKING", "LEER")]
        public async Task<IActionResult> ObtenerTrackingEtiquetasMeat(
    [FromQuery] List<string> etiquetas,
    CancellationToken ct = default)
        {
            static string Normalizar(string? valor) =>
                (valor ?? string.Empty)
                    .Replace("\0", string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            etiquetas = (etiquetas ?? new List<string>())
                .Select(Normalizar)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x =>
                    x.StartsWith("DEST", StringComparison.OrdinalIgnoreCase) ||
                    x.StartsWith("RETT", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList();

            if (etiquetas.Count == 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "Debe proporcionar al menos una etiqueta DEST o RETT."
                });
            }

            var filas = new List<TrackingEtiquetaMeatRow>();
            var advertencias = new List<string>();

            try
            {
                var tif = await ConsultarTrackingEtiquetasEnPlantaAsync(
                    connectionStringName: "CadenaMeatTIF",
                    planta: "TIF",
                    catalogoAlmacenes: "TIF_CommerciaNET",
                    etiquetas: etiquetas,
                    ct: ct);

                filas.AddRange(tif);
            }
            catch (Exception ex)
            {
                advertencias.Add(
                    $"Error consultando TIF: {ex.GetBaseException().Message}");

                _logger.LogError(
                    ex,
                    "Error consultando tracking MEAT TIF.");
            }

            try
            {
                var p1 = await ConsultarTrackingEtiquetasEnPlantaAsync(
                    connectionStringName: "CadenaMeatP1",
                    planta: "P1",
                    catalogoAlmacenes: "CommerciaNET",
                    etiquetas: etiquetas,
                    ct: ct);

                filas.AddRange(p1);
            }
            catch (Exception ex)
            {
                advertencias.Add(
                    $"Error consultando P1: {ex.GetBaseException().Message}");

                _logger.LogError(
                    ex,
                    "Error consultando tracking MEAT P1.");
            }

            if (advertencias.Count == 2)
            {
                return StatusCode(503, new
                {
                    ok = false,
                    mensaje = "No fue posible consultar TIF ni P1.",
                    advertencias
                });
            }

            var filasPorEtiqueta = filas
                .GroupBy(x => Normalizar(x.CodigoEtiqueta))
                .ToDictionary(
                    x => x.Key,
                    x => x.ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var resultado = new List<object>();

            foreach (var etiqueta in etiquetas)
            {
                if (!filasPorEtiqueta.TryGetValue(etiqueta, out var coincidencias) ||
                    coincidencias.Count == 0)
                {
                    resultado.Add(new
                    {
                        etiqueta,
                        encontrado = false,
                        estado = "NO_ENCONTRADA",
                        resumen = "No existe coincidencia en TIF ni en P1.",
                        planta = "",
                        almacen = "",
                        cliente = "",
                        folio = "",
                        articulo = "",
                        pesoNeto = (decimal?)null,
                        estatus = (int?)null,
                        transferida = false,
                        encontradaEnTif = false,
                        encontradaEnP1 = false
                    });

                    continue;
                }

                bool encontradaEnTif =
                    coincidencias.Any(x => x.Planta == "TIF");

                bool encontradaEnP1 =
                    coincidencias.Any(x => x.Planta == "P1");

                bool transferida =
                    encontradaEnTif && encontradaEnP1;

                /*
                 * Prioridad:
                 * 1. Venta comprobada.
                 * 2. Registro activo.
                 * 3. P1 sobre TIF cuando ambos están activos.
                 * 4. ProduccionId más reciente.
                 */
                var elegida = coincidencias
                    .OrderByDescending(x => x.TieneVenta == 1)
                    .ThenByDescending(x => x.Estatus == 1)
                    .ThenByDescending(x => x.Planta == "P1")
                    .ThenByDescending(x => x.ProduccionId)
                    .First();

                string estado;
                string resumen;

                if (elegida.TieneVenta == 1)
                {
                    estado = "VENDIDA";

                    var cliente = string.IsNullOrWhiteSpace(elegida.Cliente)
                        ? "Cliente no identificado"
                        : elegida.Cliente.Trim();

                    var folio = string.IsNullOrWhiteSpace(elegida.Folio)
                        ? "Folio no identificado"
                        : elegida.Folio.Trim();

                    resumen =
                        $"Vendida en {elegida.Planta} · " +
                        $"{cliente} · {folio}";

                    if (transferida)
                        resumen += " · aparece en TIF y P1";
                }
                else if (elegida.Estatus == 1)
                {
                    estado = "EN_ALMACEN";

                    var almacen = string.IsNullOrWhiteSpace(elegida.Almacen)
                        ? elegida.AlmacenCodigo ?? "Almacén no identificado"
                        : elegida.Almacen.Trim();

                    resumen =
                        $"En almacén {almacen} · Planta {elegida.Planta}";

                    if (transferida)
                        resumen += " · transferida entre plantas";
                }
                else
                {
                    estado = "INACTIVA_SIN_VENTA";

                    var almacen = string.IsNullOrWhiteSpace(elegida.Almacen)
                        ? elegida.AlmacenCodigo ?? "Sin almacén identificado"
                        : elegida.Almacen.Trim();

                    resumen =
                        $"Registro inactivo en {elegida.Planta} · " +
                        $"{almacen} · sin venta comprobada";
                }

                resultado.Add(new
                {
                    etiqueta,
                    encontrado = true,
                    estado,
                    resumen,
                    planta = elegida.Planta,
                    almacen = elegida.Almacen ?? elegida.AlmacenCodigo ?? "",
                    cliente = elegida.Cliente ?? "",
                    folio = elegida.Folio ?? "",
                    articulo = elegida.Articulo ?? "",
                    pesoNeto = elegida.PesoNeto,
                    estatus = elegida.Estatus,
                    produccionId = elegida.ProduccionId,
                    solicitudSurtidoId = elegida.SolicitudSurtidoId,
                    transferida,
                    encontradaEnTif,
                    encontradaEnP1
                });
            }

            return Json(new
            {
                ok = true,
                etiquetas = resultado,
                advertencias
            });
        }

        [HttpPost("Comercial/ImprimirEtiquetaMuestra")]
        [RevisarPermiso("SOLICITUD_MUESTRAS_PRODUCCION", "ESCRIBIR")]
        public async Task<IActionResult> ImprimirEtiquetaMuestra(
            [FromBody] List<Plataforma_CG.Models.EtiquetaMuestraPrintModel> etiquetas,
            string ip)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ip))
                    return Json(new { ok = false, mensaje = "La IP de la impresora es obligatoria." });

                if (etiquetas == null || !etiquetas.Any())
                    return Json(new { ok = false, mensaje = "No hay etiquetas para imprimir." });

                var con = new Plataforma_CG.Controllers.Comercial.Muestras.Conexiones();
                var errores = new List<string>();
                var exitosas = 0;

                foreach (var etiq in etiquetas)
                {
                    var resultado = con.Impresion(etiq, ip);
                    if (resultado.ok)
                        exitosas++;
                    else
                        errores.Add($"{etiq.Lote}: {resultado.mensaje}");
                }

                if (errores.Any())
                {
                    return Json(new
                    {
                        ok = exitosas > 0,
                        mensaje = $"Impresas: {exitosas}. Errores: {string.Join("; ", errores)}"
                    });
                }

                return Json(new { ok = true, mensaje = $"{exitosas} etiqueta(s) enviada(s) correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = $"Error: {ex.Message}" });
            }
        }

    }

}



















