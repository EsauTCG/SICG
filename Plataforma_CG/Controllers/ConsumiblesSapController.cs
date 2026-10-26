using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Services;

namespace Plataforma_CG.Controllers
{
    [Authorize]
    public class ConsumiblesSapController : Controller
    {
        private readonly AppDbContext _context;
        private readonly SapServiceLayerClient _sap;

        public ConsumiblesSapController(AppDbContext context, SapServiceLayerClient sap)
        {
            _context = context;
            _sap = sap;
        }

        [HttpGet("/Operaciones/ConsumiblesSap")]
        public IActionResult Index()
        {
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            return View("~/Views/Operaciones/ConsumiblesSap.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> Cargar(int page = 1, int pageSize = 50, string? q = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize <= 0 ? 50 : (pageSize > 200 ? 200 : pageSize);

            var baseQuery = _context.ArticuloConsumiblesSap.AsNoTracking();

            var filtro = baseQuery;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                filtro = filtro.Where(x =>
                    x.ProductoCodigo.Contains(term) ||
                    x.ProductoNombre.Contains(term));
            }

            var totalFiltrados = await filtro.CountAsync();
            var totalPaginas = totalFiltrados == 0
                ? 1
                : (int)Math.Ceiling(totalFiltrados / (double)pageSize);
            if (page > totalPaginas) page = totalPaginas;

            var items = await filtro
                .OrderBy(x => x.ProductoNombre)
                .ThenBy(x => x.ProductoCodigo)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    sku = x.ProductoCodigo,
                    nombre = x.ProductoNombre,
                    grupo = x.ItemsGroupCode,
                    master = x.U_MASTER,
                    tipo = x.U_TipoporSKU,
                    clas = x.U_Clas_Prod,
                    costo = x.Costo,
                    stock = x.Stock,
                    disponible = x.Disponible,
                    activo = x.Activo,
                    fecha = x.FechaModificacion
                })
                .ToListAsync();

            var total = await baseQuery.CountAsync();
            var activos = await baseQuery.CountAsync(x => x.Activo);
            var ultima = await baseQuery.MaxAsync(x => (DateTime?)x.FechaModificacion);

            return Json(new
            {
                ok = true,
                total,
                activos,
                ultima,
                page,
                pageSize,
                totalFiltrados,
                totalPaginas,
                items
            });
        }

        [HttpGet]
        public async Task<IActionResult> Previa(bool forzar = false)
        {
            try
            {
                if (forzar) _sap.InvalidarCacheRefacciones();

                var previa = await _sap.PreviaConsumiblesSapAsync();

                return Json(new
                {
                    ok = true,
                    total = previa.total,
                    nuevos = previa.nuevos,
                    actualizados = previa.actualizados,
                    sinCambio = previa.sinCambio,
                    items = previa.items.Select(x => new
                    {
                        sku = x.Sku,
                        nombre = x.Nombre,
                        grupo = x.ItemsGroupCode,
                        master = x.U_MASTER,
                        tipo = x.U_TipoporSKU,
                        clas = x.U_Clas_Prod,
                        costo = x.Costo,
                        stock = x.Stock,
                        disponible = x.Disponible,
                        accion = x.Accion,
                        cambios = x.Cambios.Select(c => new { campo = c.Campo, antes = c.Antes, despues = c.Despues }).ToList()
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = MensajeErrorSap(ex) });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Sincronizar()
        {
            try
            {
                var r = await _sap.SincronizarConsumiblesSapAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = $"Sincronización completa. {r.insertados} nuevos, {r.actualizados} actualizados, {r.desactivados} desactivados.",
                    insertados = r.insertados,
                    actualizados = r.actualizados,
                    desactivados = r.desactivados,
                    total = r.total
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = MensajeErrorSap(ex) });
            }
        }

        private static string MensajeErrorSap(Exception ex)
        {
            var msg = ex.GetBaseException().Message;

            if (msg.Contains("SAP no devolvió consumibles", StringComparison.OrdinalIgnoreCase))
                return msg;

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

            return "Error al sincronizar consumibles con SAP: " + msg;
        }
    }
}
