using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Plataforma_CG.Data;
using Plataforma_CG.Data.Entities;
using Plataforma_CG.Models;
using Plataforma_CG.Services;
using Plataforma_CG.ViewModels;
using QRCoder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    [Authorize]
    public class InventarioRefaccionesController : Controller
    {
        private const int GrupoRefacciones = 102;

        private readonly AppDbContext _context;
        private readonly AppDbContextUsuarios _dbUsuarios;
        private readonly IWebHostEnvironment _env;
        private readonly RefaccionSapSalidaService _sapSalidas;

        public InventarioRefaccionesController(AppDbContext context, AppDbContextUsuarios dbUsuarios, IWebHostEnvironment env, RefaccionSapSalidaService sapSalidas)
        {
            _context = context;
            _dbUsuarios = dbUsuarios;
            _env = env;
            _sapSalidas = sapSalidas;
        }

        // =========================================================
        //  VISTA PRINCIPAL
        // =========================================================
        [HttpGet("/Operaciones/InventarioRefacciones")]
        public async Task<IActionResult> Index()
        {
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";

            var (nombre, login) = await ObtenerUsuarioSesionAsync();

            ViewBag.UsuarioNombre = nombre;
            ViewBag.UsuarioLogin = login;

            return View("~/Views/Operaciones/InventarioRefacciones.cshtml");
        }

        // =========================================================
        //  CARGA DE DATOS (refacciones + catalogos + movimientos)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Cargar()
        {
            var refacciones = await _context.Refacciones
                .AsNoTracking()
                .Where(r => r.Activo)
                .OrderBy(r => r.Descripcion)
                .Select(r => new
                {
                    id = r.Id,
                    sku = r.Sku,
                    desc = r.Descripcion,
                    unidadMedidaInventario = _context.ArticuloConsumiblesSap
                        .Where(a => a.ProductoCodigo == r.Sku)
                        .Select(a => a.UnidadMedidaInventario)
                        .FirstOrDefault(),
                    catId = r.CategoriaId,
                    cat = r.Categoria != null ? r.Categoria.Nombre : "",
                    ubiId = r.UbicacionId,
                    loc = r.Ubicacion != null ? r.Ubicacion.Nombre : "",
                    stock = r.Stock,
                    min = r.StockMinimo,
                    max = r.StockMaximo ?? 0,
                    costo = r.Costo,
                    codigoEtiqueta = r.CodigoEtiqueta,
                    foto = r.FotoUrl,
                    solicitudStockActiva = r.SolicitudStockActiva,
                    fechaSolicitudStock = r.FechaSolicitudStock,
                    usuarioSolicitudStock = r.UsuarioSolicitudStock
                })
                .ToListAsync();

            var categorias = await _context.RefaccionCategorias
                .AsNoTracking()
                .Where(c => c.Activo)
                .OrderBy(c => c.Nombre)
                .Select(c => new { id = c.Id, nombre = c.Nombre })
                .ToListAsync();

            var ubicaciones = await _context.RefaccionUbicaciones
                .AsNoTracking()
                .Where(u => u.Activo)
                .OrderBy(u => u.Nombre)
                .Select(u => new { id = u.Id, nombre = u.Nombre })
                .ToListAsync();

            var maquinas = await _context.RefaccionMaquinas
                .AsNoTracking()
                .Where(m => m.Activo)
                .OrderBy(m => m.Nombre)
                .Select(m => new { id = m.Id, nombre = m.Nombre })
                .ToListAsync();

            var usoCat = await _context.Refacciones
                .AsNoTracking()
                .Where(r => r.Activo && r.CategoriaId != null)
                .GroupBy(r => r.CategoriaId!.Value)
                .Select(g => new { id = g.Key, uso = g.Count() })
                .ToListAsync();

            var usoUbi = await _context.Refacciones
                .AsNoTracking()
                .Where(r => r.Activo && r.UbicacionId != null)
                .GroupBy(r => r.UbicacionId!.Value)
                .Select(g => new { id = g.Key, uso = g.Count() })
                .ToListAsync();

            var dictUsoCat = usoCat.ToDictionary(x => x.id, x => x.uso);
            var dictUsoUbi = usoUbi.ToDictionary(x => x.id, x => x.uso);

            var usoMaq = await _context.RefaccionMovimientos
                .AsNoTracking()
                .Where(m => m.MaquinaDestino != null && m.MaquinaDestino != "")
                .GroupBy(m => m.MaquinaDestino!)
                .Select(g => new { nombre = g.Key, uso = g.Count() })
                .ToListAsync();

            var dictUsoMaq = usoMaq.ToDictionary(x => x.nombre, x => x.uso, StringComparer.OrdinalIgnoreCase);

            var movimientos = await _context.RefaccionMovimientos
                .AsNoTracking()
                .OrderByDescending(m => m.Fecha)
                .ThenByDescending(m => m.Id)
                .Take(100)
                .Select(m => new
                {
                    tipo = m.Tipo,
                    desc = m.Refaccion != null ? m.Refaccion.Descripcion : "",
                    sku = m.Refaccion != null ? m.Refaccion.Sku : "",
                    referencia = m.Referencia,
                    cantidad = m.Cantidad,
                    stockResultante = m.StockResultante,
                    folio = m.Folio,
                    maquina = m.MaquinaDestino,
                    usuario = m.Usuario,
                    fecha = m.Fecha
                })
                .ToListAsync();

            var vista = await ObtenerVistaAsync();
            var cuentasContablesSap = await _context.CuentasContablesSap
                .AsNoTracking()
                .Where(x => x.Activa)
                .OrderBy(x => x.CodigoCuenta)
                .ToListAsync();

            return Json(new
            {
                ok = true,
                vista,
                refacciones,
                categorias = categorias.Select(c => new { c.id, c.nombre, uso = dictUsoCat.ContainsKey(c.id) ? dictUsoCat[c.id] : 0 }),
                ubicaciones = ubicaciones.Select(u => new { u.id, u.nombre, uso = dictUsoUbi.ContainsKey(u.id) ? dictUsoUbi[u.id] : 0 }),
                maquinas = maquinas.Select(m => new { m.id, m.nombre, uso = dictUsoMaq.ContainsKey(m.nombre) ? dictUsoMaq[m.nombre] : 0 }),
                movimientos,
                sap = _sapSalidas.ObtenerConfiguracion(cuentasContablesSap)
            });
        }

        [HttpPost]
        public async Task<IActionResult> MarcarSolicitudStock([FromBody] RefaccionSolicitudStockRequest req)
        {
            if (req == null || req.Id <= 0)
                return Json(new { ok = false, mensaje = "Refacción inválida." });

            var refaccion = await _context.Refacciones.FirstOrDefaultAsync(r => r.Id == req.Id && r.Activo);
            if (refaccion == null)
                return Json(new { ok = false, mensaje = "No se encontró la refacción." });

            refaccion.SolicitudStockActiva = req.Solicitada;
            refaccion.FechaSolicitudStock = req.Solicitada ? DateTime.Now : null;
            refaccion.UsuarioSolicitudStock = req.Solicitada ? UsuarioActual() : null;
            refaccion.FechaModificacion = DateTime.Now;
            refaccion.UsuarioModificacion = UsuarioActual();

            await _context.SaveChangesAsync();
            return Json(new { ok = true, solicitada = refaccion.SolicitudStockActiva });
        }

        [HttpGet]
        public async Task<IActionResult> ExportarPendientesExcel()
        {
            var articulos = await _context.Refacciones
                .AsNoTracking()
                .Where(r => r.Activo)
                .Select(r => new
                {
                    r.Sku,
                    r.Descripcion,
                    Unidad = _context.ArticuloConsumiblesSap
                        .Where(a => a.ProductoCodigo == r.Sku)
                        .Select(a => a.UnidadMedidaInventario)
                        .FirstOrDefault(),
                    Ubicacion = r.Ubicacion != null ? r.Ubicacion.Nombre : "",
                    r.Stock,
                    r.StockMinimo,
                    StockMaximo = r.StockMaximo ?? 0,
                    r.SolicitudStockActiva,
                    r.FechaSolicitudStock,
                    r.UsuarioSolicitudStock
                })
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Pendientes refacciones");
            var headers = new[] { "Estado solicitud", "Prioridad", "SKU", "Descripción", "Unidad inventario", "Ubicación", "Stock", "Mínimo", "Máximo", "Cantidad sugerida", "Fecha solicitud", "Solicitado por" };
            for (var i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var row = 2;
            foreach (var item in articulos)
            {
                var nivel = item.Stock == 0 ? "AGOTADO"
                    : item.StockMinimo > 0 && item.Stock <= item.StockMinimo ? "BAJO MÍNIMO"
                    : item.StockMaximo > 0 && item.Stock > item.StockMaximo ? "SOBRE MÁXIMO"
                    : null;
                if (nivel == null) continue;

                var objetivo = item.StockMaximo > 0 ? item.StockMaximo : Math.Max(item.StockMinimo * 2, item.StockMinimo + 1);
                var sugerido = nivel == "SOBRE MÁXIMO"
                    ? item.Stock - item.StockMaximo
                    : Math.Max(objetivo - item.Stock, 1);

                ws.Cell(row, 1).Value = item.SolicitudStockActiva ? "YA SOLICITADO" : "PENDIENTE";
                ws.Cell(row, 2).Value = nivel;
                ws.Cell(row, 3).Value = item.Sku;
                ws.Cell(row, 4).Value = item.Descripcion;
                ws.Cell(row, 5).Value = item.Unidad ?? "";
                ws.Cell(row, 6).Value = item.Ubicacion;
                ws.Cell(row, 7).Value = item.Stock;
                ws.Cell(row, 8).Value = item.StockMinimo;
                ws.Cell(row, 9).Value = item.StockMaximo;
                ws.Cell(row, 10).Value = sugerido;
                if (item.FechaSolicitudStock.HasValue)
                    ws.Cell(row, 11).Value = item.FechaSolicitudStock.Value;
                ws.Cell(row, 12).Value = item.UsuarioSolicitudStock ?? "";
                row++;
            }

            var encabezado = ws.Range(1, 1, 1, headers.Length);
            encabezado.Style.Font.Bold = true;
            encabezado.Style.Font.FontColor = XLColor.White;
            encabezado.Style.Fill.BackgroundColor = XLColor.DarkRed;
            ws.SheetView.FreezeRows(1);
            if (row > 2) ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
            ws.Columns().AdjustToContents();
            ws.Column(4).Width = Math.Min(ws.Column(4).Width, 55);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var nombre = $"Pendientes_Refacciones_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre);
        }

        // =========================================================
        //  PREFERENCIA DE VISTA (tarjetas / tabla) POR USUARIO
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> GuardarVista([FromBody] VistaModoRequest req)
        {
            var modo = (req?.Vista ?? "").Trim().ToLowerInvariant();
            if (modo != "grid" && modo != "table")
                return Json(new { ok = false, mensaje = "Modo de vista inválido." });

            await GuardarVistaAsync(modo);
            return Json(new { ok = true, mensaje = "Vista guardada." });
        }

        // =========================================================
        //  REFACCIONES: ALTA / EDICIÓN
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> GuardarRefaccion([FromBody] RefaccionGuardarRequest req)
        {
            if (req == null)
                return Json(new { ok = false, mensaje = "Datos inválidos." });

            var login = UsuarioActual();

            var sku = (req.Sku ?? "").Trim();
            var desc = (req.Desc ?? "").Trim();

            if (string.IsNullOrWhiteSpace(sku))
                return Json(new { ok = false, mensaje = "El Código/SKU es obligatorio." });
            if (string.IsNullOrWhiteSpace(desc))
                return Json(new { ok = false, mensaje = "La descripción es obligatoria." });

            var dup = await _context.Refacciones
                .AnyAsync(r => r.Sku == sku && r.Id != req.Id);
            if (dup)
                return Json(new { ok = false, mensaje = $"El SKU \"{sku}\" ya está registrado." });

            var catId = await ResolverCategoriaAsync(req.Cat, login);
            var ubiId = await ResolverUbicacionAsync(req.Loc, login);

            if (req.Id > 0)
            {
                var r = await _context.Refacciones.FirstOrDefaultAsync(x => x.Id == req.Id);
                if (r == null)
                    return Json(new { ok = false, mensaje = "Refacción no encontrada." });

                r.Sku = sku;
                r.Descripcion = desc;
                r.CategoriaId = catId;
                r.UbicacionId = ubiId;
                r.StockMinimo = req.Min < 0 ? 0 : req.Min;
                r.StockMaximo = req.Max < 0 ? 0 : req.Max;
                r.Costo = req.Costo < 0 ? 0 : req.Costo;
                r.CodigoEtiqueta = "REF|" + sku;
                r.FechaModificacion = DateTime.Now;
                r.UsuarioModificacion = login;

                // Si cambian el stock desde la ficha, se registra como AJUSTE para no romper el kardex
                var nuevoStock = req.Stock < 0 ? 0 : req.Stock;
                var delta = nuevoStock - r.Stock;
                if (delta != 0)
                {
                    r.Stock = nuevoStock;
                    _context.RefaccionMovimientos.Add(new RefaccionMovimiento
                    {
                        RefaccionId = r.Id,
                        Tipo = "AJUSTE",
                        Cantidad = delta,
                        StockResultante = nuevoStock,
                        Referencia = "Ajuste por edición",
                        Usuario = login,
                        Fecha = DateTime.Now
                    });
                }
            }
            else
            {
                var stockInicial = req.Stock < 0 ? 0 : req.Stock;
                var r = new Refaccion
                {
                    Sku = sku,
                    Descripcion = desc,
                    CategoriaId = catId,
                    UbicacionId = ubiId,
                    Stock = stockInicial,
                    StockMinimo = req.Min < 0 ? 0 : req.Min,
                    StockMaximo = req.Max < 0 ? 0 : req.Max,
                    Costo = req.Costo < 0 ? 0 : req.Costo,
                    CodigoEtiqueta = "REF|" + sku,
                    Activo = true,
                    FechaCreacion = DateTime.Now,
                    UsuarioCreacion = login
                };
                _context.Refacciones.Add(r);
                await _context.SaveChangesAsync();

                if (stockInicial > 0)
                {
                    _context.RefaccionMovimientos.Add(new RefaccionMovimiento
                    {
                        RefaccionId = r.Id,
                        Tipo = "ENTRADA",
                        Cantidad = stockInicial,
                        StockResultante = stockInicial,
                        Referencia = "Alta inicial",
                        Usuario = login,
                        Fecha = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { ok = true, mensaje = "Refacción guardada." });
        }

        [HttpPost]
        public async Task<IActionResult> EliminarRefaccion([FromBody] CatalogoEliminarRequest req)
        {
            var r = await _context.Refacciones.FirstOrDefaultAsync(x => x.Id == req.Id);
            if (r == null)
                return Json(new { ok = false, mensaje = "Refacción no encontrada." });

            r.Activo = false;
            r.FechaModificacion = DateTime.Now;
            r.UsuarioModificacion = UsuarioActual();
            await _context.SaveChangesAsync();

            return Json(new { ok = true, mensaje = "Refacción eliminada." });
        }

        // =========================================================
        //  MOVIMIENTOS: ENTRADA / SALIDA / AJUSTE
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> RegistrarMovimiento([FromBody] MovimientoRequest req)
        {
            if (req == null)
                return Json(new { ok = false, mensaje = "Datos inválidos." });

            var r = await _context.Refacciones.FirstOrDefaultAsync(x => x.Id == req.Id);
            if (r == null)
                return Json(new { ok = false, mensaje = "Refacción no encontrada." });

            var tipo = (req.Tipo ?? "").Trim().ToUpperInvariant();
            int delta;

            if (tipo == "SALIDA")
            {
                if (string.IsNullOrWhiteSpace(req.Maquina))
                    return Json(new { ok = false, mensaje = "Selecciona la máquina o equipo de destino." });
                var errorSap = await _sapSalidas.ValidarSeleccionAsync(req.Sap);
                if (errorSap != null)
                    return Json(new { ok = false, mensaje = errorSap });
                delta = -Math.Abs(req.Cantidad);
            }
            else if (tipo == "ENTRADA")
            {
                delta = Math.Abs(req.Cantidad);
            }
            else
            {
                tipo = "AJUSTE";
                delta = req.Cantidad;
            }

            if (delta == 0)
                return Json(new { ok = false, mensaje = "Ingresa una cantidad válida." });

            if (r.Stock + delta < 0)
                return Json(new { ok = false, mensaje = $"Stock insuficiente. Disponible: {r.Stock}." });

            r.Stock += delta;

            var folioSalida = tipo == "SALIDA"
                ? "SAL-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]
                : null;

            _context.RefaccionMovimientos.Add(new RefaccionMovimiento
            {
                RefaccionId = r.Id,
                Tipo = tipo,
                Cantidad = delta,
                StockResultante = r.Stock,
                Folio = folioSalida,
                Referencia = string.IsNullOrWhiteSpace(req.Referencia) ? "Sin documento" : req.Referencia.Trim(),
                MaquinaDestino = (tipo == "SALIDA") ? (req.Maquina ?? "").Trim() : null,
                Comentario = req.Comentario,
                Usuario = UsuarioActual(),
                Fecha = DateTime.Now
            });

            if (folioSalida != null)
            {
                var referencia = string.IsNullOrWhiteSpace(req.Referencia) ? "Sin documento" : req.Referencia.Trim();
                var comentario = ConstruirComentarioSap(req.Comentario, new[] { req.Maquina });
                _context.RefaccionSapSalidas.Add(new RefaccionSapSalida
                {
                    Folio = folioSalida,
                    Estado = "PENDIENTE",
                    FechaCreacion = DateTime.Now,
                    Usuario = UsuarioActual(),
                    Referencia = referencia,
                    Comentario = comentario,
                    CantidadLineas = 1,
                    JsonSap = _sapSalidas.CrearDocumentoJson(
                        folioSalida,
                        DateTime.Today,
                        referencia,
                        comentario,
                        new[] { (r, Math.Abs(delta)) },
                        req.Sap)
                });
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                ok = true,
                mensaje = folioSalida == null
                    ? "Movimiento registrado."
                    : $"Salida registrada en Sigo y pendiente de envío a SAP. Folio: {folioSalida}.",
                stock = r.Stock,
                folio = folioSalida,
                sapPendiente = folioSalida != null
            });
        }

        // =========================================================
        //  PUNTO DE VENTA (salida por escaneo)
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> RegistrarSalidaPos([FromBody] SalidaPosRequest req)
        {
            if (req == null || req.Lineas == null || req.Lineas.Count == 0)
                return Json(new { ok = false, mensaje = "No hay artículos escaneados." });

            var maquinaGlobal = (req.Maquina ?? "").Trim();

            var ids = req.Lineas.Select(l => l.Id).Distinct().ToList();
            var items = await _context.Refacciones
                .Where(r => ids.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id);
            var cuentasSapPermitidas = (await _context.CuentasContablesSap
                    .AsNoTracking()
                    .Where(x => x.Activa)
                    .Select(x => x.CodigoCuenta)
                    .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Validación previa (no aplica nada si algo falla)
            foreach (var linea in req.Lineas)
            {
                if (linea.Cantidad <= 0)
                    return Json(new { ok = false, mensaje = "Hay una línea con cantidad inválida." });

                if (!items.TryGetValue(linea.Id, out var r))
                    return Json(new { ok = false, mensaje = "Uno de los artículos ya no existe." });

                if (r.Stock < linea.Cantidad)
                    return Json(new { ok = false, mensaje = $"Stock insuficiente para \"{r.Descripcion}\" (disponible {r.Stock})." });

                var maquinaLinea = string.IsNullOrWhiteSpace(linea.Maquina) ? maquinaGlobal : linea.Maquina.Trim();
                if (string.IsNullOrWhiteSpace(maquinaLinea))
                    return Json(new { ok = false, mensaje = $"Asigna la máquina o equipo de destino para \"{r.Descripcion}\"." });

                var errorSap = await _sapSalidas.ValidarSeleccionAsync(linea.Sap ?? req.Sap, cuentasSapPermitidas);
                if (errorSap != null)
                    return Json(new { ok = false, mensaje = $"{r.Descripcion}: {errorSap}" });
            }

            var login = UsuarioActual();
            var folio = "SAL-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];

            foreach (var linea in req.Lineas)
            {
                var r = items[linea.Id];
                r.Stock -= linea.Cantidad;

                var maquinaLinea = string.IsNullOrWhiteSpace(linea.Maquina) ? maquinaGlobal : linea.Maquina.Trim();

                _context.RefaccionMovimientos.Add(new RefaccionMovimiento
                {
                    RefaccionId = r.Id,
                    Tipo = "SALIDA",
                    Cantidad = -Math.Abs(linea.Cantidad),
                    StockResultante = r.Stock,
                    Folio = folio,
                    Referencia = string.IsNullOrWhiteSpace(req.Referencia) ? "Ticket POS" : req.Referencia.Trim(),
                    MaquinaDestino = maquinaLinea,
                    Comentario = req.Comentario,
                    Usuario = login,
                    Fecha = DateTime.Now
                });
            }

            var referenciaPos = string.IsNullOrWhiteSpace(req.Referencia) ? "Ticket POS" : req.Referencia.Trim();
            var comentarioPos = ConstruirComentarioSap(
                req.Comentario,
                req.Lineas.Select(linea => string.IsNullOrWhiteSpace(linea.Maquina) ? maquinaGlobal : linea.Maquina));
            _context.RefaccionSapSalidas.Add(new RefaccionSapSalida
            {
                Folio = folio,
                Estado = "PENDIENTE",
                FechaCreacion = DateTime.Now,
                Usuario = login,
                Referencia = referenciaPos,
                Comentario = comentarioPos,
                CantidadLineas = req.Lineas.Count,
                JsonSap = _sapSalidas.CrearDocumentoJsonPorLinea(
                    folio,
                    DateTime.Today,
                    referenciaPos,
                    comentarioPos,
                    req.Lineas.Select(linea => (items[linea.Id], linea.Cantidad, linea.Sap ?? req.Sap)))
            });

            await _context.SaveChangesAsync();
            return Json(new
            {
                ok = true,
                mensaje = $"Salida registrada en Sigo y pendiente de envío a SAP. Folio: {folio}.",
                folio,
                sapPendiente = true
            });
        }

        [HttpGet]
        public async Task<IActionResult> SalidasSapPendientes()
        {
            var items = await _context.RefaccionSapSalidas
                .AsNoTracking()
                .Where(x => x.Estado != "ENVIADO")
                .OrderByDescending(x => x.FechaCreacion)
                .Select(x => new
                {
                    folio = x.Folio,
                    estado = x.Estado,
                    fecha = x.FechaCreacion,
                    usuario = x.Usuario,
                    referencia = x.Referencia,
                    cantidadLineas = x.CantidadLineas,
                    error = x.Error
                })
                .ToListAsync();

            return Json(new { ok = true, items, sap = _sapSalidas.ObtenerConfiguracion() });
        }

        [HttpGet]
        public async Task<IActionResult> SalidasSapEnviadas()
        {
            var salidas = await _context.RefaccionSapSalidas
                .AsNoTracking()
                .Where(x => x.Estado == "ENVIADO")
                .OrderByDescending(x => x.FechaEnvio ?? x.FechaCreacion)
                .Take(30)
                .Select(x => new
                {
                    x.Folio,
                    x.SapDocNum,
                    x.FechaEnvio,
                    x.FechaCreacion,
                    x.Usuario,
                    x.Referencia,
                    x.CantidadLineas,
                    x.JsonSap
                })
                .ToListAsync();

            var items = salidas.Select(x => new
            {
                folio = x.Folio,
                documentoSap = x.SapDocNum,
                fecha = x.FechaEnvio ?? x.FechaCreacion,
                usuario = x.Usuario,
                referencia = x.Referencia,
                cantidadLineas = x.CantidadLineas,
                articulos = ExtraerLineasSalidaSap(x.JsonSap)
            });

            return Json(new { ok = true, items });
        }

        [HttpPost]
        public async Task<IActionResult> EnviarSalidaSap([FromBody] RefaccionSapEnviarRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Folio))
                return Json(new { ok = false, mensaje = "Folio de salida inválido." });

            var resultado = await _sapSalidas.EnviarAsync(req.Folio.Trim());
            return Json(new
            {
                ok = resultado.Ok,
                yaEnviada = resultado.YaEnviada,
                mensaje = resultado.Mensaje,
                docEntry = resultado.DocEntry,
                docNum = resultado.DocNum
            });
        }

        // =========================================================
        //  CATÁLOGOS: CATEGORÍAS / UBICACIONES / MÁQUINAS
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> GuardarCategoria([FromBody] CatalogoGuardarRequest req)
        {
            var nombre = (req?.Nombre ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return Json(new { ok = false, mensaje = "Escribe un nombre." });

            var existe = await _context.RefaccionCategorias
                .AnyAsync(c => c.Nombre == nombre && c.Id != req.Id);
            if (existe)
                return Json(new { ok = false, mensaje = "Ya existe una categoría con ese nombre." });

            if (req.Id > 0)
            {
                var c = await _context.RefaccionCategorias.FirstOrDefaultAsync(x => x.Id == req.Id);
                if (c == null)
                    return Json(new { ok = false, mensaje = "Categoría no encontrada." });
                c.Nombre = nombre;
            }
            else
            {
                _context.RefaccionCategorias.Add(new RefaccionCategoria
                {
                    Nombre = nombre,
                    Activo = true,
                    FechaCreacion = DateTime.Now,
                    UsuarioCreacion = UsuarioActual()
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { ok = true, mensaje = "Categoría guardada." });
        }

        [HttpPost]
        public async Task<IActionResult> EliminarCategoria([FromBody] CatalogoEliminarRequest req)
        {
            var c = await _context.RefaccionCategorias.FirstOrDefaultAsync(x => x.Id == req.Id);
            if (c == null)
                return Json(new { ok = false, mensaje = "Categoría no encontrada." });

            var uso = await _context.Refacciones.CountAsync(r => r.Activo && r.CategoriaId == c.Id);
            if (uso > 0)
                return Json(new { ok = false, mensaje = $"No se puede eliminar: está en uso en {uso} refacción(es)." });

            c.Activo = false;
            await _context.SaveChangesAsync();
            return Json(new { ok = true, mensaje = "Categoría eliminada." });
        }

        [HttpPost]
        public async Task<IActionResult> GuardarUbicacion([FromBody] CatalogoGuardarRequest req)
        {
            var nombre = (req?.Nombre ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return Json(new { ok = false, mensaje = "Escribe un nombre." });

            var existe = await _context.RefaccionUbicaciones
                .AnyAsync(u => u.Nombre == nombre && u.Id != req.Id);
            if (existe)
                return Json(new { ok = false, mensaje = "Ya existe una ubicación con ese nombre." });

            if (req.Id > 0)
            {
                var u = await _context.RefaccionUbicaciones.FirstOrDefaultAsync(x => x.Id == req.Id);
                if (u == null)
                    return Json(new { ok = false, mensaje = "Ubicación no encontrada." });
                u.Nombre = nombre;
            }
            else
            {
                _context.RefaccionUbicaciones.Add(new RefaccionUbicacion
                {
                    Nombre = nombre,
                    Activo = true,
                    FechaCreacion = DateTime.Now,
                    UsuarioCreacion = UsuarioActual()
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { ok = true, mensaje = "Ubicación guardada." });
        }

        [HttpPost]
        public async Task<IActionResult> EliminarUbicacion([FromBody] CatalogoEliminarRequest req)
        {
            var u = await _context.RefaccionUbicaciones.FirstOrDefaultAsync(x => x.Id == req.Id);
            if (u == null)
                return Json(new { ok = false, mensaje = "Ubicación no encontrada." });

            var uso = await _context.Refacciones.CountAsync(r => r.Activo && r.UbicacionId == u.Id);
            if (uso > 0)
                return Json(new { ok = false, mensaje = $"No se puede eliminar: está en uso en {uso} refacción(es)." });

            u.Activo = false;
            await _context.SaveChangesAsync();
            return Json(new { ok = true, mensaje = "Ubicación eliminada." });
        }

        [HttpPost]
        public async Task<IActionResult> GuardarMaquina([FromBody] CatalogoGuardarRequest req)
        {
            var nombre = (req?.Nombre ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return Json(new { ok = false, mensaje = "Escribe un nombre." });

            var existe = await _context.RefaccionMaquinas
                .AnyAsync(m => m.Nombre == nombre && m.Id != req.Id);
            if (existe)
                return Json(new { ok = false, mensaje = "Ya existe una máquina con ese nombre." });

            if (req.Id > 0)
            {
                var m = await _context.RefaccionMaquinas.FirstOrDefaultAsync(x => x.Id == req.Id);
                if (m == null)
                    return Json(new { ok = false, mensaje = "Máquina no encontrada." });
                m.Nombre = nombre;
            }
            else
            {
                _context.RefaccionMaquinas.Add(new RefaccionMaquina
                {
                    Nombre = nombre,
                    Activo = true,
                    FechaCreacion = DateTime.Now,
                    UsuarioCreacion = UsuarioActual()
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { ok = true, mensaje = "Máquina guardada." });
        }

        [HttpPost]
        public async Task<IActionResult> EliminarMaquina([FromBody] CatalogoEliminarRequest req)
        {
            var m = await _context.RefaccionMaquinas.FirstOrDefaultAsync(x => x.Id == req.Id);
            if (m == null)
                return Json(new { ok = false, mensaje = "Máquina no encontrada." });

            // Se puede eliminar aunque tenga movimientos históricos; el texto ya quedó registrado.
            m.Activo = false;
            await _context.SaveChangesAsync();
            return Json(new { ok = true, mensaje = "Máquina eliminada." });
        }

        // =========================================================
        //  SINCRONIZACIÓN CON SAP (refacciones / consumibles)
        //  Upsert por Sku: inserta nuevas, actualiza Descripcion y Costo.
        //  Nunca borra ni modifica Stock/Min/Max/Categoria/Ubicacion.
        // =========================================================

        // Compara descripciones ignorando caracteres que una columna varchar no puede
        // representar (Ω, ⌀, etc.): si al normalizarlos quedan iguales, no hay cambio real.
        private static bool DescripcionCambio(string? local, string? sap)
        {
            static string Normalizar(string? s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                var sb = new System.Text.StringBuilder(s.Length);
                foreach (var ch in s)
                {
                    switch (ch)
                    {
                        case 'Ω': sb.Append('O'); break;  // ohm
                        case '⌀': sb.Append("?"); break;   // diámetro (literal de la columna varchar)
                        default:
                            // Cualquier carácter fuera de Latin-1 se trata como '?', que es
                            // lo que hace SQL Server al guardar en varchar.
                            sb.Append(ch <= 0xFF ? ch : '?');
                            break;
                    }
                }
                return sb.ToString().Trim();
            }

            return !string.Equals(Normalizar(local), Normalizar(sap), StringComparison.Ordinal);
        }

        private async Task<List<CatalogoSkuSapViewmodel>> ObtenerConsumiblesLocalesAsync()
        {
            var query = _context.ArticuloConsumiblesSap
                .AsNoTracking()
                .Where(x => x.Activo && x.ItemsGroupCode == GrupoRefacciones);

            return await query
                .Select(x => new CatalogoSkuSapViewmodel
                {
                    ItemCode = x.ProductoCodigo,
                    ItemName = x.ProductoNombre,
                    InventoryUOM = x.UnidadMedidaInventario,
                    U_MASTER = x.U_MASTER,
                    U_TipoporSKU = x.U_TipoporSKU,
                    U_KilosCaja = x.U_KilosCaja,
                    U_Clas_Prod = x.U_Clas_Prod,
                    U_PRESENT = x.U_PRESENT,
                    U_PorcInye = x.U_PorcInye,
                    ItemsGroupCode = x.ItemsGroupCode,
                    AvgPrice = x.Costo,
                    QuantityOnStock = x.Stock,
                    QuantityAvailable = x.Disponible
                })
                .ToListAsync();
        }

        // GET: vista previa de consumibles locales. No escribe en la BD.
        [HttpGet]
        public async Task<IActionResult> PreviaSap()
        {
            try
            {
                var itemsSap = await ObtenerConsumiblesLocalesAsync();

                var limpios = itemsSap
                    .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
                    .GroupBy(x => x.ItemCode!.Trim().ToUpperInvariant())
                    .Select(g => g.Last())
                    .ToList();

                // Refacciones que YA existen localmente, indexadas por SKU (case-insensitive)
                var locales = await _context.Refacciones
                    .AsNoTracking()
                    .ToListAsync();
                var dicLocales = locales
                    .GroupBy(r => (r.Sku ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

                // Mismas reglas que SincronizarSap: solo importa cambios reales.
                static bool DecCambio(decimal a, decimal b) =>
                    Math.Round(a, 2, MidpointRounding.AwayFromZero) !=
                    Math.Round(b, 2, MidpointRounding.AwayFromZero);

                var nuevas = new List<object>();
                var actualizar = new List<object>();

                foreach (var item in limpios)
                {
                    var sku = item.ItemCode!.Trim();
                    var desc = (item.ItemName ?? "").Trim();
                    var costo = item.AvgPrice;

                    if (!dicLocales.TryGetValue(sku, out var local))
                    {
                        nuevas.Add(new
                        {
                            sku,
                            nombre = item.ItemName,
                            unidadMedidaInventario = item.InventoryUOM,
                            grupo = item.ItemsGroupCode,
                            costo,
                            stock = item.QuantityOnStock,
                            disponible = item.QuantityAvailable,
                            accion = "Nuevo",
                            cambios = new List<object>()
                        });
                        continue;
                    }

                    var cambios = new List<object>();

                    if (desc.Length > 0 && DescripcionCambio(local.Descripcion, desc))
                        cambios.Add(new { campo = "Descripción", antes = local.Descripcion, despues = desc });

                    if (costo.HasValue && DecCambio(local.Costo, costo.Value))
                        cambios.Add(new { campo = "Costo", antes = local.Costo.ToString("0.##"), despues = costo.Value.ToString("0.##") });

                    if (cambios.Count > 0)
                    {
                        actualizar.Add(new
                        {
                            sku,
                            nombre = item.ItemName,
                            unidadMedidaInventario = item.InventoryUOM,
                            grupo = item.ItemsGroupCode,
                            costo,
                            stock = item.QuantityOnStock,
                            disponible = item.QuantityAvailable,
                            accion = "Actualizar",
                            cambios
                        });
                    }
                }

                var pendientes = nuevas.Concat(actualizar).ToList();

                return Json(new
                {
                    ok = true,
                    total = limpios.Count,
                    nuevos = nuevas.Count,
                    actualizados = actualizar.Count,
                    yaExistentes = limpios.Count - nuevas.Count,
                    nuevas = nuevas.Count,
                    items = pendientes
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = MensajeErrorSap(ex) });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SincronizarSap()
        {
            try
            {
                var itemsSap = await ObtenerConsumiblesLocalesAsync();

                // Deduplicar por Sku (case-insensitive)
                itemsSap = itemsSap
                    .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
                    .GroupBy(x => x.ItemCode!.Trim().ToUpperInvariant())
                    .Select(g => g.Last())
                    .ToList();

                var login = UsuarioActual();

                int insertadas = 0;
                int actualizadas = 0;
                int sinCambio = 0;

                foreach (var item in itemsSap)
                {
                    var sku = item.ItemCode!.Trim();
                    var skuKey = sku.ToUpperInvariant();
                    var desc = (item.ItemName ?? "").Trim();
                    var costo = item.AvgPrice;   // puede ser null si SAP no lo trae
                    var stockInicial = (int)Math.Round(item.QuantityOnStock ?? 0m, MidpointRounding.ToZero);

                    var existente =
                        _context.Refacciones.Local.FirstOrDefault(x => x.Sku.ToUpper() == skuKey)
                        ?? await _context.Refacciones.FirstOrDefaultAsync(x => x.Sku.ToUpper() == skuKey);

                    if (existente == null)
                    {
                        _context.Refacciones.Add(new Refaccion
                        {
                            Sku = sku,
                            Descripcion = desc,
                            Stock = stockInicial,
                            StockMinimo = 0,
                            StockMaximo = 0,
                            Costo = costo ?? 0m,
                            Activo = true,
                            FechaCreacion = DateTime.Now,
                            UsuarioCreacion = login
                        });
                        insertadas++;
                    }
                    else
                    {
                        // Sólo se sobrescribe la descripción si SAP trae texto realmente distinto
                        // (ignora diferencias que la columna varchar no puede representar, ej. Ω, ⌀).
                        var descCambio = desc.Length > 0
                            && DescripcionCambio(existente.Descripcion, desc);

                        // Sólo se sobrescribe el costo si SAP trae un valor distinto (a 2 decimales)
                        var costoCambio = costo.HasValue &&
                            Math.Round(existente.Costo, 2, MidpointRounding.AwayFromZero) !=
                            Math.Round(costo.Value, 2, MidpointRounding.AwayFromZero);

                        if (descCambio || costoCambio)
                        {
                            if (descCambio) existente.Descripcion = desc;
                            if (costoCambio) existente.Costo = costo!.Value;
                            existente.FechaModificacion = DateTime.Now;
                            existente.UsuarioModificacion = login;
                            actualizadas++;
                        }
                        else
                        {
                            sinCambio++;
                        }
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = $"Sincronización completa. {insertadas} nuevas, {actualizadas} actualizadas, {sinCambio} sin cambios.",
                    total = itemsSap.Count,
                    insertadas,
                    actualizadas,
                    sinCambio
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = MensajeErrorSap(ex) });
            }
        }

        // =========================================================
        //  MENSAJE DE ERROR AMIGABLE PARA FALLAS DE SAP
        // =========================================================
        private static string MensajeErrorSap(Exception ex)
        {
            var msg = ex.GetBaseException().Message;

            if (msg.Contains("Invalid login credential", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("\"code\":206", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("\"code\": 206", StringComparison.OrdinalIgnoreCase))
            {
                return "SAP rechazó las credenciales o la cuenta está bloqueada temporalmente "
                     + "por intentos de acceso. Espera unos minutos e inténtalo de nuevo. "
                     + "Detalle: " + msg;
            }

            if (msg.Contains("No se pudo asignar", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                return "No hay conexión con SAP Service Layer. Verifica la VPN y que el servidor "
                     + "172.120.80.3:50000 esté alcanzable. Detalle: " + msg;
            }

            return "Error al sincronizar con SAP: " + msg;
        }

        // =========================================================
        //  FIN BLOQUE SAP
        // =========================================================

        // =========================================================
        //  FOTO DE LA REFACCIÓN
        // =========================================================
        private static readonly string[] FotoExtensionesValidas = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        [HttpPost]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> SubirFoto(IFormFile? archivo, int id)
        {
            if (id <= 0)
                return Json(new { ok = false, mensaje = "Refacción inválida." });

            var r = await _context.Refacciones.FirstOrDefaultAsync(x => x.Id == id);
            if (r == null)
                return Json(new { ok = false, mensaje = "Refacción no encontrada." });

            if (archivo == null || archivo.Length == 0)
                return Json(new { ok = false, mensaje = "Selecciona una imagen." });

            var extension = (Path.GetExtension(archivo.FileName) ?? "").ToLowerInvariant();
            if (!FotoExtensionesValidas.Contains(extension))
                return Json(new { ok = false, mensaje = "Formato no permitido. Usa JPG, PNG, WEBP o GIF." });

            var carpeta = Path.Combine(_env.WebRootPath, "uploads", "refacciones");
            Directory.CreateDirectory(carpeta);

            var nombreArchivo = $"ref_{id}_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
            var rutaFisica = Path.Combine(carpeta, nombreArchivo);

            await using (var stream = new FileStream(rutaFisica, FileMode.Create))
            {
                await archivo.CopyToAsync(stream);
            }

            // Eliminar la foto anterior si existía
            EliminarArchivoFoto(r.FotoUrl);

            r.FotoUrl = $"/uploads/refacciones/{nombreArchivo}";
            r.FechaModificacion = DateTime.Now;
            r.UsuarioModificacion = UsuarioActual();
            await _context.SaveChangesAsync();

            return Json(new { ok = true, mensaje = "Foto guardada.", foto = r.FotoUrl });
        }

        [HttpPost]
        public async Task<IActionResult> EliminarFoto([FromBody] CatalogoEliminarRequest req)
        {
            var r = await _context.Refacciones.FirstOrDefaultAsync(x => x.Id == req.Id);
            if (r == null)
                return Json(new { ok = false, mensaje = "Refacción no encontrada." });

            EliminarArchivoFoto(r.FotoUrl);

            r.FotoUrl = null;
            r.FechaModificacion = DateTime.Now;
            r.UsuarioModificacion = UsuarioActual();
            await _context.SaveChangesAsync();

            return Json(new { ok = true, mensaje = "Foto eliminada." });
        }

        private void EliminarArchivoFoto(string? fotoUrl)
        {
            if (string.IsNullOrWhiteSpace(fotoUrl))
                return;

            try
            {
                var relativa = fotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var ruta = Path.Combine(_env.WebRootPath, relativa);
                if (System.IO.File.Exists(ruta))
                    System.IO.File.Delete(ruta);
            }
            catch
            {
                // Si no se puede borrar el archivo, no bloqueamos la operación.
            }
        }

        // =========================================================
        //  ETIQUETAS QR
        // =========================================================
        [HttpGet]
        public IActionResult GenerarQR(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku))
                return BadRequest();

            var texto = "REF|" + sku.Trim();

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(texto, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            return File(qrCode.GetGraphic(10), "image/png");
        }

        [HttpGet]
        public async Task<IActionResult> Etiquetas(string? sku = null)
        {
            var q = _context.Refacciones.Where(r => r.Activo);

            if (!string.IsNullOrWhiteSpace(sku))
            {
                var s = sku.Trim();
                q = q.Where(r => r.Sku == s);
            }

            var items = await q.OrderBy(r => r.Descripcion).ToListAsync();
            return View("~/Views/Operaciones/RefaccionesEtiquetas.cshtml", items);
        }

        // =========================================================
        //  HELPERS
        // =========================================================
        private static string? ConstruirComentarioSap(string? comentario, IEnumerable<string?> maquinas)
        {
            var partes = new List<string>();
            if (!string.IsNullOrWhiteSpace(comentario))
                partes.Add(comentario.Trim());

            var equipos = maquinas
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (equipos.Count > 0)
                partes.Add("Equipo(s) destino: " + string.Join(", ", equipos));

            var texto = string.Join(" | ", partes);
            if (texto.Length == 0) return null;
            return texto.Length <= 254 ? texto : texto[..254];
        }

        private string UsuarioActual() => (User?.Identity?.Name ?? "").Trim();

        private static List<Dictionary<string, object?>> ExtraerLineasSalidaSap(string? json)
        {
            var resultado = new List<Dictionary<string, object?>>();
            if (string.IsNullOrWhiteSpace(json)) return resultado;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("DocumentLines", out var lineas) ||
                    lineas.ValueKind != JsonValueKind.Array)
                    return resultado;

                foreach (var linea in lineas.EnumerateArray())
                {
                    var resumen = new Dictionary<string, object?>
                    {
                        ["itemCode"] = LeerTextoJson(linea, "ItemCode"),
                        ["quantity"] = LeerDecimalJson(linea, "Quantity"),
                        ["warehouseCode"] = LeerTextoJson(linea, "WarehouseCode"),
                        ["accountCode"] = LeerTextoJson(linea, "AccountCode"),
                        ["planta"] = LeerTextoJson(linea, "CostingCode"),
                        ["unidadNegocio"] = LeerTextoJson(linea, "CostingCode2"),
                        ["departamento"] = LeerTextoJson(linea, "CostingCode3")
                    };
                    resultado.Add(resumen);
                }
            }
            catch (JsonException)
            {
                // Mantiene visible la salida enviada aunque el detalle histórico no pueda interpretarse.
            }

            return resultado;
        }

        private static string? LeerTextoJson(JsonElement elemento, string propiedad) =>
            elemento.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String
                ? valor.GetString()
                : null;

        private static decimal? LeerDecimalJson(JsonElement elemento, string propiedad) =>
            elemento.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out var numero)
                ? numero
                : null;

        // Clave en AppSettings para la vista preferida de cada usuario
        private string ClaveVista() => "RefVista:" + UsuarioActual();

        private async Task<string> ObtenerVistaAsync()
        {
            var key = ClaveVista();
            var row = await _context.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == key);

            return (row != null && row.Value == "table") ? "table" : "grid";
        }

        private async Task GuardarVistaAsync(string modo)
        {
            var key = ClaveVista();
            var row = await _context.AppSettings.FirstOrDefaultAsync(x => x.Key == key);

            if (row == null)
            {
                row = new AppSetting { Key = key };
                _context.AppSettings.Add(row);
            }

            row.Value = modo;
            row.UpdatedBy = UsuarioActual();
            row.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        private async Task<int?> ResolverCategoriaAsync(string? nombre, string login)
        {
            nombre = (nombre ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return null;

            var cat = await _context.RefaccionCategorias
                .FirstOrDefaultAsync(c => c.Nombre == nombre);

            if (cat != null)
                return cat.Id;

            var nueva = new RefaccionCategoria
            {
                Nombre = nombre,
                Activo = true,
                FechaCreacion = DateTime.Now,
                UsuarioCreacion = login
            };
            _context.RefaccionCategorias.Add(nueva);
            await _context.SaveChangesAsync();
            return nueva.Id;
        }

        private async Task<int?> ResolverUbicacionAsync(string? nombre, string login)
        {
            nombre = (nombre ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return null;

            var ubi = await _context.RefaccionUbicaciones
                .FirstOrDefaultAsync(u => u.Nombre == nombre);

            if (ubi != null)
                return ubi.Id;

            var nueva = new RefaccionUbicacion
            {
                Nombre = nombre,
                Activo = true,
                FechaCreacion = DateTime.Now,
                UsuarioCreacion = login
            };
            _context.RefaccionUbicaciones.Add(nueva);
            await _context.SaveChangesAsync();
            return nueva.Id;
        }

        private async Task<(string Nombre, string Login)> ObtenerUsuarioSesionAsync()
        {
            var login = (User?.Identity?.Name ?? "").Trim();

            if (string.IsNullOrEmpty(login))
                return ("Invitado", "");

            var usuario = await _dbUsuarios.Usuarios
                .Where(u => u.Usuario == login)
                .Select(u => new { u.Nombre, u.Activo })
                .FirstOrDefaultAsync();

            if (usuario == null || !usuario.Activo)
                return (login, login);

            var nombre = (usuario.Nombre ?? "").Trim();
            return (string.IsNullOrEmpty(nombre) ? login : nombre, login);
        }
    }
}
