using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.Models.ViewModels;

namespace Plataforma_CG.Controllers
{
    public class KpisPermisosController : Controller
    {
        private readonly AppDbContextUsuarios _db;
        public KpisPermisosController(AppDbContextUsuarios db) => _db = db;

        // GET: /KpisPermisos/KpisPermisosConfiguracion
        public async Task<IActionResult> KpisPermisosConfiguracion()
        {
            var perfiles = await _db.Perfiles.ToListAsync();
            var kpis = await _db.KpiCatalogo
                .Where(x => x.Activo)
                .OrderBy(x => x.Categoria)
                .ThenBy(x => x.Titulo)
                .ToListAsync();

            var permisosKpi = await _db.PerfilKpiPermiso.ToListAsync();

            var vm = new KpisPermisosViewModel
            {
                Perfiles = perfiles,
                Kpis = kpis,
                PermisosKpi = permisosKpi
            };

            return View(vm);
        }

        // POST: /KpisPermisos/Asignar
        [HttpPost]
        public async Task<IActionResult> Asignar(int perfilId, int kpiId)
        {
            var existente = await _db.PerfilKpiPermiso
                .FirstOrDefaultAsync(x => x.PerfilId == perfilId && x.KpiCatalogoId == kpiId);

            if (existente == null)
            {
                var nuevo = new PerfilKpiPermiso { PerfilId = perfilId, KpiCatalogoId = kpiId };
                _db.PerfilKpiPermiso.Add(nuevo);
                await _db.SaveChangesAsync();
                return Json(new { id = nuevo.Id });
            }

            return Json(new { id = existente.Id });
        }

        // POST: /KpisPermisos/Revocar
        [HttpPost]
        public async Task<IActionResult> Revocar(int id)
        {
            var item = await _db.PerfilKpiPermiso.FindAsync(id);
            if (item == null) return NotFound();

            _db.PerfilKpiPermiso.Remove(item);
            await _db.SaveChangesAsync();
            return Ok();
        }
    }
}