using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Models.ViewModels;
using Plataforma_CG.Models.Sidebar;
using System.Linq;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    public class SidebarAdminController : Controller
    {
        private readonly Data.AppDbContextUsuarios _db;

        public SidebarAdminController(Data.AppDbContextUsuarios db)
        {
            _db = db;
        }

        public async Task<IActionResult> AdminSidebar()
        {
            var model = new SidebarAdminViewModel
            {
                Categorias = await _db.SidebarCategorias
                    .Include(c => c.Modulos)
                    .ThenInclude(m => m.Submodulos)
                    .ToListAsync(),

                Perfiles = await _db.Perfiles.ToListAsync(), // 🔹 NUEVO
                Permisos = await _db.SidebarPermisos.ToListAsync()
            };

            return View("~/Views/Sidebar/AdminSidebar.cshtml", model);

        }

        [HttpGet]
        public async Task<IActionResult> GetPermisosPerfil(int perfilId)
        {
            var permisos = await _db.SidebarPermisos
                .Where(p => p.PerfilId == perfilId)
                .Select(p => p.ModuloId)
                .ToListAsync();

            return Json(permisos);
        }

        [HttpPost]
        public async Task<IActionResult> TogglePermiso([FromBody] TogglePermisoDto dto)
        {
            var existente = await _db.SidebarPermisos
                .FirstOrDefaultAsync(p => p.PerfilId == dto.PerfilId && p.ModuloId == dto.ModuloId);

            if (dto.Activo && existente == null)
            {
                _db.SidebarPermisos.Add(new SidebarPermiso
                {
                    PerfilId = dto.PerfilId,
                    ModuloId = dto.ModuloId
                });
            }
            else if (!dto.Activo && existente != null)
            {
                _db.SidebarPermisos.Remove(existente);
            }

            await _db.SaveChangesAsync();
            return Ok();
        }
    }

    public class TogglePermisoDto
    {
        public int PerfilId { get; set; }   // 🔹 NUEVO
        public int ModuloId { get; set; }
        public bool Activo { get; set; }
    }
}
