using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;

namespace Plataforma_CG.Controllers
{
    public class KpisController : Controller
    {
        private readonly AppDbContextUsuarios _db;
        public KpisController(AppDbContextUsuarios db) { _db = db; }

        // ✅ AQUÍ VA LO DEL PASO 3 (helper)
        // ✅ Helper: obtiene el PerfilId del usuario logueado desde tu tabla Usuarios
        private int? GetPerfilIdActual()
        {
            var perfilIdStr = User.FindFirst("PerfilId")?.Value;
            if (int.TryParse(perfilIdStr, out var perfilId) && perfilId > 0)
                return perfilId;

            return null;
        }

        // ✅ 1) Catálogo Netflix
        public async Task<IActionResult> Catalogo()
        {
            var perfilId = GetPerfilIdActual();
            if (perfilId == null) return Forbid();

            var kpis = await _db.PerfilKpiPermiso
                .Where(x => x.PerfilId == perfilId.Value)
                .Select(x => x.KpiCatalogo)
                .Where(k => k.Activo)
                .OrderBy(k => k.Categoria).ThenBy(k => k.Titulo)
                .ToListAsync();

            var grouped = kpis
                .GroupBy(k => k.Categoria)
                .ToDictionary(g => g.Key, g => g.ToList());

            return View(grouped);
        }

        // ✅ 2) Sinopsis (partial para modal)
        public async Task<IActionResult> Detalle(int id)
        {
            var perfilId = GetPerfilIdActual();
            if (perfilId == null) return Forbid();

            var permitido = await _db.PerfilKpiPermiso
                .AnyAsync(x => x.PerfilId == perfilId.Value && x.KpiCatalogoId == id);

            if (!permitido) return Forbid();

            var kpi = await _db.KpiCatalogo.FirstOrDefaultAsync(x => x.Id == id && x.Activo);
            if (kpi == null) return NotFound();

            return PartialView("_DetalleKpi", kpi);
        }

        public async Task<IActionResult> Ver(int id)
        {
            var perfilId = GetPerfilIdActual();
            if (perfilId == null) return Forbid();

            var permitido = await _db.PerfilKpiPermiso
                .AnyAsync(x => x.PerfilId == perfilId.Value && x.KpiCatalogoId == id);

            if (!permitido) return Forbid();

            var kpi = await _db.KpiCatalogo.FirstOrDefaultAsync(x => x.Id == id && x.Activo);
            if (kpi == null) return NotFound();

            return View(kpi);
        }

        [HttpGet]
        public async Task<IActionResult> GetEmbedUrl(int id)
        {
            if (Request.Headers["X-Requested-With"] != "XMLHttpRequest")
                return BadRequest();

            Response.Headers["Cache-Control"] = "no-store";

            var perfilId = GetPerfilIdActual();
            if (perfilId == null) return Forbid();

            var permitido = await _db.PerfilKpiPermiso
                .AnyAsync(x => x.PerfilId == perfilId.Value && x.KpiCatalogoId == id);

            if (!permitido) return Forbid();

            var kpi = await _db.KpiCatalogo.FirstOrDefaultAsync(x => x.Id == id && x.Activo);
            if (kpi == null) return NotFound();

            return Json(new { url = kpi.EmbedUrl });
        }

    }
}
