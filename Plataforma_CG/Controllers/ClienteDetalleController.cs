using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.ViewModels;
using System.Data;

namespace Plataforma_CG.Controllers
{
    /// <summary>
    /// Ficha de cliente en el catálogo de SIGO.
    /// Solo lectura. No crea ni modifica información.
    /// Pedidos se agregó en la Fase 1.5.
    /// </summary>
    [Authorize]
    [Produces("application/json")]
    public class ClienteDetalleController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ClienteDetalleController> _logger;

        public ClienteDetalleController(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<ClienteDetalleController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Cabecera de la ficha + direcciones activas.
        /// GET /ClienteDetalle/Resumen?cardCode=C000176
        /// </summary>
        [HttpGet("ClienteDetalle/Resumen")]
        public async Task<IActionResult> Resumen(
            [FromQuery] string cardCode,
            CancellationToken ct = default)
        {
            cardCode = (cardCode ?? "").Trim();

            if (string.IsNullOrWhiteSpace(cardCode))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "cardCode es requerido."
                });
            }

            var cliente = await _context.ClienteSap
                .AsNoTracking()
                .Where(c => c.Cliente == cardCode)
                .Select(c => new ClienteDetalleResumenVm
                {
                    Cliente = c.Cliente,
                    Nombrecliente = c.Nombrecliente,
                    U_MT_Clasificacion = c.U_MT_Clasificacion,
                    U_CANAL = c.U_CANAL,
                    PriceListNum = c.PriceListNum,
                    PriceListName = c.PriceListName,
                    VendedorId = c.VendedorId,
                    VendedorNombre = c.VendedorNombre,
                    AplicaPresupuesto = c.AplicaPresupuesto,
                    FechaModificacion = c.FechaModificacion
                })
                .FirstOrDefaultAsync(ct);

            if (cliente == null)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje = "El cliente no está en el catálogo de SIGO."
                });
            }

            var direcciones = await _context.DireccionesCliente
                .AsNoTracking()
                .Where(d => d.Cliente == cardCode && d.Activa)
                .OrderByDescending(d => d.EsPrincipal)
                .ThenBy(d => d.AliasDireccion)
                .Select(d => new DireccionClienteDetalleVm
                {
                    AliasDireccion = d.AliasDireccion,
                    EsPrincipal = d.EsPrincipal,
                    Cedis = d.Cedis,
                    Ruta = d.Ruta,
                    Calle = d.Calle,
                    Colonia = d.Colonia,
                    Ciudad = d.Ciudad,
                    Estado = d.Estado,
                    CodigoPostal = d.CodigoPostal,
                    Pais = d.Pais,
                    SapAddressType = d.SapAddressType,
                    SapAddressCode = d.SapAddressCode
                })
                .ToListAsync(ct);

            return Json(new
            {
                ok = true,
                cliente,
                direcciones
            });
        }

        /// <summary>
        /// Última compra, consumo promedio y SKUs facturados.
        /// GET /ClienteDetalle/Productos?cardCode=C000176&amp;meses=12
        /// </summary>
        [HttpGet("ClienteDetalle/Productos")]
        public async Task<IActionResult> Productos(
            [FromQuery] string cardCode,
            [FromQuery] int meses = 12,
            CancellationToken ct = default)
        {
            cardCode = (cardCode ?? "").Trim();

            if (string.IsNullOrWhiteSpace(cardCode))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "cardCode es requerido."
                });
            }

            if (meses <= 0 || meses > 60)
            {
                meses = 12;
            }

            // Mismo criterio que ComercialController/ObtenerPresupuesto:
            // desde el primer día del mes inicial de la ventana.
            var hoy = DateTime.Today;
            var desdeBase = hoy.AddMonths(-(meses - 1));
            var desde = new DateTime(desdeBase.Year, desdeBase.Month, 1);

            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "No se encontró la cadena de conexión 'DefaultConnection'."
                });
            }

            const string sql = @"
                SELECT
                    l.sku                                       AS sku,
                    ISNULL(a.U_MASTER, 'SIN_MASTER')            AS master,
                    ISNULL(NULLIF(a.ProductoNombre, ''), l.sku) AS producto,
                    SUM(l.Kilos)                                AS kilos,
                    MAX(l.doc_date)                             AS ultimaCompra
                FROM dbo.sap_invoice_lines AS l WITH (NOLOCK)
                LEFT JOIN dbo.ArticuloSap   AS a WITH (NOLOCK)
                       ON a.ProductoCodigo = l.sku
                WHERE l.card_code = @card
                  AND l.doc_date >= @desde
                GROUP BY
                    l.sku,
                    a.U_MASTER,
                    a.ProductoNombre
                ORDER BY kilos DESC;";

            var productos = new List<ProductoClienteVm>();

            try
            {
                await using var con = new SqlConnection(connectionString);
                await con.OpenAsync(ct);

                await using var cmd = new SqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@card", cardCode);
                cmd.Parameters.AddWithValue("@desde", desde);

                await using var rd = await cmd.ExecuteReaderAsync(ct);

                var iSku = rd.GetOrdinal("sku");
                var iMaster = rd.GetOrdinal("master");
                var iProducto = rd.GetOrdinal("producto");
                var iKilos = rd.GetOrdinal("kilos");
                var iUltima = rd.GetOrdinal("ultimaCompra");

                while (await rd.ReadAsync(ct))
                {
                    productos.Add(new ProductoClienteVm
                    {
                        Sku = rd.IsDBNull(iSku) ? "" : rd.GetString(iSku),
                        Master = rd.IsDBNull(iMaster) ? "" : rd.GetString(iMaster),
                        Producto = rd.IsDBNull(iProducto) ? "" : rd.GetString(iProducto),
                        Kilos = rd.IsDBNull(iKilos) ? 0m : rd.GetDecimal(iKilos),
                        UltimaCompra = rd.IsDBNull(iUltima) ? null : rd.GetDateTime(iUltima)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error consultando productos facturados. Cliente={Cliente}",
                    cardCode);

                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "No fue posible consultar los productos facturados del cliente.",
                    error = ex.GetBaseException().Message
                });
            }

            var kilosTotales = productos.Sum(p => p.Kilos);

            return Json(new
            {
                ok = true,
                cardCode,
                meses,
                desde,
                ultimaCompra = productos
                    .Where(p => p.UltimaCompra.HasValue)
                    .Select(p => p.UltimaCompra!.Value)
                    .DefaultIfEmpty()
                    .Max(),
                kilosTotales,
                promedioMensual = kilosTotales / meses,
                productos
            });
        }

        /// <summary>
        /// Etiqueta local de los estatus de OrdenVenta. Mismos valores que
        /// OrdenesPorVendedor.cshtml; 7 se usa cuando se actualizan documentos.
        /// </summary>
        private static string EstadoPedidoTexto(int? estado)
        {
            return estado switch
            {
                0 => "Cancelado",
                1 => "Pendiente",
                2 => "Autorización",
                3 => "Pendiente",
                4 => "Validado",
                5 => "Enviado a SAP",
                6 => "Logística",
                7 => "Documentado",
                _ => estado.HasValue ? $"Estado {estado.Value}" : "Sin estado"
            };
        }

        /// <summary>
        /// Pedidos de venta recientes del cliente (Fase 1.5).
        /// GET /ClienteDetalle/Pedidos?cardCode=C000176&amp;meses=12
        /// </summary>
        [HttpGet("ClienteDetalle/Pedidos")]
        public async Task<IActionResult> Pedidos(
            [FromQuery] string cardCode,
            [FromQuery] int meses = 12,
            CancellationToken ct = default)
        {
            cardCode = (cardCode ?? "").Trim();

            if (string.IsNullOrWhiteSpace(cardCode))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "cardCode es requerido."
                });
            }

            if (meses <= 0 || meses > 60)
            {
                meses = 12;
            }

            const int limite = 300;

            var hoy = DateTime.Today;
            var desdeBase = hoy.AddMonths(-(meses - 1));
            var desde = new DateTime(desdeBase.Year, desdeBase.Month, 1);

            try
            {
                // La tabla real OrdenVenta es la cabecera (vw_OrdenVentaCabecera
                // no existe en la BD configurada). PedidoVenta guarda el snapshot
                // de gestión (almacén, embarque, totales).
                var ordenes = await _context.OrdenVenta
                    .AsNoTracking()
                    .Where(o => o.Cliente != null
                             && o.Cliente.Trim() == cardCode
                             && (o.FechaRegistro >= desde
                                 || (o.FechaRegistro == null && o.FechaEntrega >= desde)))
                    .OrderByDescending(o => o.FechaRegistro ?? o.FechaEntrega)
                    .Take(limite)
                    .ToListAsync(ct);

                var ids = ordenes
                    .Select(o => o.Id)
                    .Distinct()
                    .ToList();

                var consecutivos = ordenes
                    .Where(o => !string.IsNullOrWhiteSpace(o.Consecutivo))
                    .Select(o => o.Consecutivo!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var pedidosVenta = new List<PedidoVenta>();

                if (ids.Count > 0 || consecutivos.Count > 0)
                {
                    pedidosVenta = await _context.PedidoVenta
                        .AsNoTracking()
                        .Where(p =>
                            (ids.Count > 0 && ids.Contains(p.OrdenVentaId)) ||
                            (consecutivos.Count > 0
                             && p.OrdenVentaConsecutivo != null
                             && consecutivos.Contains(p.OrdenVentaConsecutivo.Trim())))
                        .ToListAsync(ct);
                }

                var porId = pedidosVenta
                    .Where(p => p.OrdenVentaId > 0)
                    .GroupBy(p => p.OrdenVentaId)
                    .ToDictionary(g => g.Key, g => g.First());

                var porConsecutivo = pedidosVenta
                    .Where(p => !string.IsNullOrWhiteSpace(p.OrdenVentaConsecutivo))
                    .GroupBy(p => p.OrdenVentaConsecutivo.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var pedidoIds = pedidosVenta.Select(p => p.Id).ToList();

                var lineasPorPedido = new Dictionary<int, List<ProductoPedidoClienteDetalleVm>>();

                if (pedidoIds.Count > 0)
                {
                    var lineasDb = await _context.PedidoVentaProducto
                        .AsNoTracking()
                        .Where(d => pedidoIds.Contains(d.PedidoVentaId))
                        .Select(d => new
                        {
                            d.PedidoVentaId,
                            d.ProductoCodigo,
                            d.ProductoNombre,
                            d.Cajas,
                            d.Precio,
                            d.KilosCaja,
                            d.Almacen
                        })
                        .ToListAsync(ct);

                    lineasPorPedido = lineasDb
                        .GroupBy(d => d.PedidoVentaId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(d => new ProductoPedidoClienteDetalleVm
                            {
                                Sku = d.ProductoCodigo ?? "",
                                Producto = d.ProductoNombre ?? "",
                                Cajas = d.Cajas,
                                Precio = d.Precio,
                                Kilos = d.KilosCaja,
                                Almacen = d.Almacen ?? ""
                            }).ToList());
                }

                var pedidos = new List<PedidoClienteDetalleVm>(ordenes.Count);

                foreach (var ov in ordenes)
                {
                    PedidoVenta? pv = null;

                    if (porId.TryGetValue(ov.Id, out var encontradoPorId))
                    {
                        pv = encontradoPorId;
                    }

                    if (pv == null
                        && !string.IsNullOrWhiteSpace(ov.Consecutivo)
                        && porConsecutivo.TryGetValue(ov.Consecutivo.Trim(), out var encontradoPorConsecutivo))
                    {
                        pv = encontradoPorConsecutivo;
                    }

                    var productosDelPedido = new List<ProductoPedidoClienteDetalleVm>();
                    if (pv != null && lineasPorPedido.TryGetValue(pv.Id, out var lineasDelPedido))
                    {
                        productosDelPedido = lineasDelPedido;
                    }

                    pedidos.Add(new PedidoClienteDetalleVm
                    {
                        IdOrdenVenta = ov.Id,
                        PedidoVentaId = pv?.Id ?? 0,
                        Consecutivo = ov.Consecutivo ?? "",
                        Serie = ov.Serie,
                        FechaRegistro = ov.FechaRegistro,
                        FechaEntrega = ov.FechaEntrega,
                        FechaEmbarque = pv?.FechaEmbarque ?? ov.FechaEmbarque,
                        Estado = ov.Estatus,
                        EstadoTexto = EstadoPedidoTexto(ov.Estatus),
                        Vendedor = !string.IsNullOrWhiteSpace(ov.Vendedor)
                            ? ov.Vendedor!
                            : (pv?.Vendedor ?? ""),
                        AlmacenSurtir = pv?.AlmacenSurtir ?? "",
                        ObservacionGestion = pv?.ObservacionGestion,
                        Saldo = ov.Saldo,
                        Credito = ov.Credito,
                        TotalImporte = pv?.TotalImporte ?? 0m,
                        TotalPeso = pv?.TotalPeso ?? 0m,
                        Productos = productosDelPedido
                    });
                }

                return Json(new
                {
                    ok = true,
                    cardCode,
                    meses,
                    desde,
                    limite,
                    truncado = ordenes.Count == limite,
                    totalPedidos = pedidos.Count,
                    importeTotal = pedidos.Sum(p => p.TotalImporte),
                    pesoTotal = pedidos.Sum(p => p.TotalPeso),
                    pedidos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error consultando pedidos del cliente. Cliente={Cliente}",
                    cardCode);

                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "No fue posible consultar los pedidos del cliente.",
                    error = ex.GetBaseException().Message
                });
            }
        }
    }
}
