using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models.Sidebar;
using Plataforma_CG.Models.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    public class SidebarController : Controller
    {
        private readonly AppDbContextUsuarios _db;

        public SidebarController(AppDbContextUsuarios db)
        {
            _db = db;
        }

        public IActionResult PruebaSidebar() => View();

        public IActionResult _Sidebar() => View();

        // =====================================
        // CARGA DE MÓDULOS SEGÚN PERFIL Y ORDEN
        // =====================================
        [HttpGet]
        public async Task<IActionResult> CargarModulos()
        {
            var perfilIdClaim = User.FindFirst("PerfilId")?.Value;
            int perfilId;

            if (string.IsNullOrEmpty(perfilIdClaim))
            {
                var userName = User.Identity.Name;
                var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Usuario == userName);

                if (usuario == null)
                    return Json(new { error = "Usuario no encontrado" });

                perfilId = usuario.PerfilId;
            }
            else
            {
                perfilId = int.Parse(perfilIdClaim);
            }

            var permisos = await _db.SidebarPermisos
                .Where(p => p.PerfilId == perfilId)
                .Select(p => p.ModuloId)
                .ToListAsync();

            var categorias = await _db.SidebarCategorias
                .Select(c => new
                {
                    c.Id,
                    c.Nombre,
                    c.Icono
                })
                .ToDictionaryAsync(c => c.Id, c => c);

            string GetCategoriaNombre(int? categoriaId)
            {
                if (categoriaId.HasValue && categorias.TryGetValue(categoriaId.Value, out var categoria))
                    return categoria.Nombre;

                return "Principal";
            }

            string GetCategoriaIcono(int? categoriaId)
            {
                if (categoriaId.HasValue && categorias.TryGetValue(categoriaId.Value, out var categoria))
                    return categoria.Icono ?? "bi bi-folder";

                return "bi bi-house";
            }

            var modulos = await _db.SidebarModulos
                .Include(m => m.Submodulos)
                .Where(m => m.PadreId == null && permisos.Contains(m.Id))
                .OrderBy(m => m.CategoriaId)
                .ThenBy(m => m.Orden)
                .ToListAsync();

            var modulosVM = modulos.Select(m => new
            {
                id = m.Id,
                nombre = m.Nombre,
                icono = m.Icono,
                url = m.Url,
                padreId = m.PadreId,
                categoriaId = m.CategoriaId,
                categoriaNombre = GetCategoriaNombre(m.CategoriaId),
                categoriaIcono = GetCategoriaIcono(m.CategoriaId),

                subModulos = m.Submodulos
                    .Where(sm => permisos.Contains(sm.Id))
                    .OrderBy(sm => sm.Orden)
                    .Select(sm => new
                    {
                        id = sm.Id,
                        nombre = sm.Nombre,
                        icono = sm.Icono,
                        url = sm.Url,
                        padreId = sm.PadreId,
                        categoriaId = sm.CategoriaId ?? m.CategoriaId,
                        categoriaNombre = GetCategoriaNombre(sm.CategoriaId ?? m.CategoriaId),
                        categoriaIcono = GetCategoriaIcono(sm.CategoriaId ?? m.CategoriaId),
                        subModulos = new List<object>()
                    })
                    .ToList()
            }).ToList();

            return Json(modulosVM);
        }

        [HttpGet]
        public async Task<IActionResult> Administrar()
        {
            var modulos = await _db.SidebarModulos
                .OrderBy(m => m.Orden)
                .Select(m => new ModuloViewModel
                {
                    Id = m.Id,
                    Nombre = m.Nombre,
                    Icono = m.Icono,
                    Url = m.Url,
                    PadreId = m.PadreId,         // 👈 Agregado
                    CategoriaId = m.CategoriaId, // 👈 Agregado
                    Orden = m.Orden              // 👈 Opcional, útil para ordenar
                })
                .ToListAsync();

            var categorias = await _db.SidebarCategorias
                .Select(c => new CategoriaViewModel
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    Icono = c.Icono
                })
                .ToListAsync();

            var vm = new SideAdminViewModel
            {
                ModulosExistentes = modulos,
                CategoriasExistentes = categorias
            };

            return View(vm);
        }


        // =====================================
        // CRUD DE MÓDULOS
        // =====================================
        [HttpPost]
        public async Task<IActionResult> CrearModulo([FromBody] ModuloViewModel nuevo)
        {
            if (string.IsNullOrWhiteSpace(nuevo.Nombre))
                return BadRequest(new { mensaje = "El nombre es obligatorio." });

            // 🔹 Asigna automáticamente el último orden disponible
            int ultimoOrden = await _db.SidebarModulos.MaxAsync(m => (int?)m.Orden) ?? 0;

            var modulo = new SidebarModulo
            {
                Nombre = nuevo.Nombre,
                Icono = nuevo.Icono ?? "fas fa-circle",
                Url = nuevo.Url,
                PadreId = nuevo.PadreId,
                CategoriaId = nuevo.CategoriaId,
                Orden = ultimoOrden + 1 // 👈 Nuevo
            };

            _db.SidebarModulos.Add(modulo);
            await _db.SaveChangesAsync();

            return Ok(new { mensaje = "Módulo creado correctamente." });
        }

        [HttpPost]
        public async Task<IActionResult> EliminarModulo(int id)
        {
            var modulo = await _db.SidebarModulos.FindAsync(id);

            if (modulo == null)
                return NotFound(new { mensaje = "Módulo no encontrado." });

            _db.SidebarModulos.Remove(modulo);

            try
            {
                await _db.SaveChangesAsync();
                return Ok(new { mensaje = "Módulo eliminado correctamente." });
            }
            catch (DbUpdateException ex)
            {
                var mensajeBD = ex.InnerException?.Message ?? ex.Message;

                // 👇 Detecta si el bloqueo viene de permisos/perfiles
                if (mensajeBD.Contains("SidebarPermisos") ||
                    mensajeBD.Contains("FK_") ||
                    mensajeBD.Contains("REFERENCE"))
                {
                    return BadRequest(new
                    {
                        mensaje = "No se puede eliminar este módulo porque está asignado a uno o más perfiles. Primero debes quitarlo de los permisos."
                    });
                }

                // Cualquier otro error inesperado
                return StatusCode(500, new
                {
                    mensaje = "Ocurrió un error inesperado al eliminar el módulo."
                });
            }
        }


        [HttpGet]
        public async Task<IActionResult> EditarModulo(int id)
        {
            var modulo = await _db.SidebarModulos.FindAsync(id);
            if (modulo == null)
                return NotFound();

            var categorias = await _db.SidebarCategorias
                .Select(c => new CategoriaViewModel
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    Icono = c.Icono
                })
                .ToListAsync();

            var modulos = await _db.SidebarModulos
                .Where(m => m.Id != id)
                .Select(m => new ModuloViewModel
                {
                    Id = m.Id,
                    Nombre = m.Nombre
                })
                .ToListAsync();

            var vm = new SideAdminViewModel
            {
                ModuloActual = new ModuloViewModel
                {
                    Id = modulo.Id,
                    Nombre = modulo.Nombre,
                    Icono = modulo.Icono,
                    Url = modulo.Url,
                    CategoriaId = modulo.CategoriaId,
                    PadreId = modulo.PadreId
                },
                CategoriasExistentes = categorias,
                ModulosExistentes = modulos
            };

            return View("EditarModulo", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarModuloGuardar(ModuloViewModel model)
        {
            if (!ModelState.IsValid)
                return View("EditarModulo", model);

            var modulo = await _db.SidebarModulos.FindAsync(model.Id);
            if (modulo == null)
                return NotFound();

            modulo.Nombre = model.Nombre;
            modulo.Icono = model.Icono;
            modulo.Url = model.Url;
            modulo.CategoriaId = model.CategoriaId;
            modulo.PadreId = model.PadreId;

            await _db.SaveChangesAsync();

            TempData["Mensaje"] = "Módulo actualizado correctamente.";
            return RedirectToAction("Administrar");
        }

        // =====================================
        // CATEGORÍAS
        // =====================================
        [HttpGet]
        public async Task<IActionResult> Categorias()
        {
            var categorias = await _db.SidebarCategorias
                .Select(c => new CategoriaViewModel
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    Icono = c.Icono
                })
                .ToListAsync();

            return View(categorias);
        }

        [HttpPost]
        public async Task<IActionResult> CrearCategoria([FromBody] CategoriaViewModel nueva)
        {
            if (string.IsNullOrWhiteSpace(nueva.Nombre))
                return BadRequest(new { mensaje = "El nombre de la categoría es obligatorio." });

            var categoria = new SidebarCategoria
            {
                Nombre = nueva.Nombre,
                Icono = nueva.Icono ?? "bi bi-folder"
            };

            _db.SidebarCategorias.Add(categoria);
            await _db.SaveChangesAsync();

            return Ok(new { mensaje = "Categoría creada correctamente." });
        }

        [HttpPost]
        public async Task<IActionResult> EliminarCategoria(int id)
        {
            var categoria = await _db.SidebarCategorias.FindAsync(id);
            if (categoria == null)
                return NotFound(new { mensaje = "Categoría no encontrada." });

            _db.SidebarCategorias.Remove(categoria);
            await _db.SaveChangesAsync();

            return Ok(new { mensaje = "Categoría eliminada correctamente." });
        }

        // =====================================
        // ORDENAR (para drag & drop)
        // =====================================
        [HttpPost]
        public async Task<IActionResult> ActualizarOrden([FromBody] List<int> nuevoOrden)
        {
            if (nuevoOrden == null || !nuevoOrden.Any())
                return BadRequest(new { mensaje = "No se recibió un orden válido." });

            var modulos = await _db.SidebarModulos
                .Where(m => nuevoOrden.Contains(m.Id))
                .ToListAsync();

            for (int i = 0; i < nuevoOrden.Count; i++)
            {
                var modulo = modulos.FirstOrDefault(m => m.Id == nuevoOrden[i]);
                if (modulo != null)
                    modulo.Orden = i + 1;
            }

            await _db.SaveChangesAsync();
            return Ok(new { mensaje = "Orden actualizado correctamente." });
        }

    }
}
