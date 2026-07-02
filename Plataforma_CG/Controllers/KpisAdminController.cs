using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.Models.ViewModels; // <-- para KpiCatalogoFormVM
using Plataforma_CG.ViewModels;
using System.IO;

namespace Plataforma_CG.Controllers
{
    public class KpisAdminController : Controller
    {
        private readonly AppDbContextUsuarios _db;
        public KpisAdminController(AppDbContextUsuarios db) { _db = db; }

        // =========================
        //  A) ASIGNAR KPIs A USUARIO
        // =========================

        // /KpisAdmin/Asignar?usuarioKey=SQL:JPEREZ
        public async Task<IActionResult> Asignar(string usuarioKey)
        {
            if (string.IsNullOrWhiteSpace(usuarioKey)) return BadRequest();
            usuarioKey = usuarioKey.ToUpperInvariant();

            var asignadosIds = await _db.UsuarioKpiPermiso
                .Where(x => x.UsuarioKey == usuarioKey)
                .Select(x => x.KpiCatalogoId)
                .ToListAsync();

            var todos = await _db.KpiCatalogo.Where(k => k.Activo)
                .OrderBy(k => k.Categoria).ThenBy(k => k.Titulo)
                .ToListAsync();

            var vm = new AsignarKpisVM
            {
                UsuarioKey = usuarioKey,
                DisplayName = usuarioKey,
                Asignados = todos.Where(k => asignadosIds.Contains(k.Id)).ToList(),
                Disponibles = todos.Where(k => !asignadosIds.Contains(k.Id)).ToList()
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Agregar(string usuarioKey, int kpiId)
        {
            usuarioKey = (usuarioKey ?? "").ToUpperInvariant();

            var existe = await _db.UsuarioKpiPermiso
                .AnyAsync(x => x.UsuarioKey == usuarioKey && x.KpiCatalogoId == kpiId);

            if (!existe)
            {
                _db.UsuarioKpiPermiso.Add(new UsuarioKpiPermiso
                {
                    UsuarioKey = usuarioKey,
                    KpiCatalogoId = kpiId
                });
                await _db.SaveChangesAsync();
            }

            return RedirectToAction("Asignar", new { usuarioKey });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Quitar(string usuarioKey, int kpiId)
        {
            usuarioKey = (usuarioKey ?? "").ToUpperInvariant();

            var item = await _db.UsuarioKpiPermiso
                .FirstOrDefaultAsync(x => x.UsuarioKey == usuarioKey && x.KpiCatalogoId == kpiId);

            if (item != null)
            {
                _db.UsuarioKpiPermiso.Remove(item);
                await _db.SaveChangesAsync();
            }

            return RedirectToAction("Asignar", new { usuarioKey });
        }


        // =========================
        //  B) ADMINISTRAR CATÁLOGO
        // =========================

        // /KpisAdmin
        // /KpisAdmin/Index
        [HttpGet]
        public async Task<IActionResult> InicioKpi()
        {
            var kpis = await _db.KpiCatalogo
                .OrderByDescending(x => x.Activo)
                .ThenBy(x => x.Categoria)
                .ThenBy(x => x.Titulo)
                .ToListAsync();

            return View(kpis); // Views/KpisAdmin/InicioKpi.cshtml
        }

        // GET: /KpisAdmin/Crear
        [HttpGet]
        public IActionResult Crear()
        {
            return View(new KpiCatalogoFormVM { Activo = true }); // Views/KpisAdmin/Crear.cshtml
        }

        // POST: /KpisAdmin/Crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(KpiCatalogoFormVM vm)
        {
            if (!ModelState.IsValid) return View(vm);

            try
            {
                var imgUrl = await GuardarImagenKpi(vm.Imagen);

                var nuevo = new KpiCatalogo
                {
                    Titulo = (vm.Titulo ?? "").Trim(),
                    Categoria = (vm.Categoria ?? "").Trim(),
                    Descripcion = (vm.Descripcion ?? "").Trim(),
                    EmbedUrl = (vm.EmbedUrl ?? "").Trim(),
                    Activo = vm.Activo,
                    FechaAlta = DateTime.Now,
                    ImagenUrl = imgUrl
                };

                _db.KpiCatalogo.Add(nuevo);
                await _db.SaveChangesAsync();

                TempData["Exito"] = "KPI creado correctamente.";
                return RedirectToAction("InicioKpi");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                return View(vm);
            }
        }

        // GET: /KpisAdmin/Editar/5
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            var kpi = await _db.KpiCatalogo.FindAsync(id);
            if (kpi == null) return NotFound();

            var vm = new KpiCatalogoFormVM
            {
                Id = kpi.Id,
                Titulo = kpi.Titulo,
                Categoria = kpi.Categoria,
                Descripcion = kpi.Descripcion,
                EmbedUrl = kpi.EmbedUrl,
                Activo = kpi.Activo,
                ImagenUrlActual = kpi.ImagenUrl
            };

            return View(vm); // Views/KpisAdmin/Editar.cshtml
        }

        // POST: /KpisAdmin/Editar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(KpiCatalogoFormVM vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var kpi = await _db.KpiCatalogo.FindAsync(vm.Id);
            if (kpi == null) return NotFound();

            try
            {
                kpi.Titulo = (vm.Titulo ?? "").Trim();
                kpi.Categoria = (vm.Categoria ?? "").Trim();
                kpi.Descripcion = (vm.Descripcion ?? "").Trim();
                kpi.EmbedUrl = (vm.EmbedUrl ?? "").Trim();
                kpi.Activo = vm.Activo;

                // Quitar imagen actual
                if (vm.QuitarImagen && !string.IsNullOrWhiteSpace(kpi.ImagenUrl))
                {
                    EliminarArchivoImagen(kpi.ImagenUrl);
                    kpi.ImagenUrl = null;
                }

                // Reemplazar imagen si sube una nueva
                if (vm.Imagen != null && vm.Imagen.Length > 0)
                {
                    if (!string.IsNullOrWhiteSpace(kpi.ImagenUrl))
                        EliminarArchivoImagen(kpi.ImagenUrl);

                    kpi.ImagenUrl = await GuardarImagenKpi(vm.Imagen);
                }

                await _db.SaveChangesAsync();

                TempData["Exito"] = "KPI actualizado correctamente.";
                return RedirectToAction("InicioKpi");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                vm.ImagenUrlActual = kpi.ImagenUrl; // para que no se pierda en la vista
                return View(vm);
            }
        }

        // POST: /KpisAdmin/Eliminar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id)
        {
            var kpi = await _db.KpiCatalogo.FindAsync(id);
            if (kpi == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(kpi.ImagenUrl))
                EliminarArchivoImagen(kpi.ImagenUrl);

            _db.KpiCatalogo.Remove(kpi);
            await _db.SaveChangesAsync();

            TempData["Exito"] = "KPI eliminado.";
            return RedirectToAction("InicioKpi");
        }


        // =========================
        //  Helpers imagen (wwwroot/img/kpi)
        // =========================

        private async Task<string?> GuardarImagenKpi(IFormFile? imagen)
        {
            if (imagen == null || imagen.Length == 0) return null;
            if (!imagen.ContentType.StartsWith("image/"))
                throw new Exception("Solo se permiten imágenes.");
            if (imagen.Length > 5 * 1024 * 1024)
                throw new Exception("La imagen no debe superar 5MB.");

            var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var carpeta = Path.Combine(webRootPath, "img", "kpi");

            if (!Directory.Exists(carpeta))
                Directory.CreateDirectory(carpeta);

            var extension = Path.GetExtension(imagen.FileName).ToLowerInvariant();
            var nombreArchivo = $"{Guid.NewGuid()}{extension}";
            var rutaCompleta = Path.Combine(carpeta, nombreArchivo);

            using (var stream = new FileStream(rutaCompleta, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await imagen.CopyToAsync(stream);
                await stream.FlushAsync();
            }

            return $"/img/kpi/{nombreArchivo}";
        }

        private void EliminarArchivoImagen(string? imagenUrl)
        {
            if (string.IsNullOrWhiteSpace(imagenUrl)) return;

            var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var ruta = Path.Combine(webRootPath, "img", "kpi", Path.GetFileName(imagenUrl));
            if (System.IO.File.Exists(ruta))
                System.IO.File.Delete(ruta);
        }

        // =========================
        //  A) ASIGNAR KPIs A PERFIL
        // =========================

        // /KpisAdmin/AsignarPerfil?perfilId=1
        public async Task<IActionResult> AsignarPerfil(int perfilId)
        {
            var perfil = await _db.Perfiles.FirstOrDefaultAsync(p => p.Id == perfilId);
            if (perfil == null) return NotFound();

            var asignadosIds = await _db.PerfilKpiPermiso
                .Where(x => x.PerfilId == perfilId)
                .Select(x => x.KpiCatalogoId)
                .ToListAsync();

            var todos = await _db.KpiCatalogo
                .Where(k => k.Activo)
                .OrderBy(k => k.Categoria).ThenBy(k => k.Titulo)
                .ToListAsync();

            var vm = new AsignarKpisPerfilVM
            {
                PerfilId = perfilId,
                DisplayName = perfil.Nombre,
                Asignados = todos.Where(k => asignadosIds.Contains(k.Id)).ToList(),
                Disponibles = todos.Where(k => !asignadosIds.Contains(k.Id)).ToList()
            };

            return View(vm); // Views/KpisAdmin/AsignarPerfil.cshtml
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AgregarPerfil(int perfilId, int kpiId)
        {
            var existe = await _db.PerfilKpiPermiso
                .AnyAsync(x => x.PerfilId == perfilId && x.KpiCatalogoId == kpiId);

            if (!existe)
            {
                _db.PerfilKpiPermiso.Add(new PerfilKpiPermiso
                {
                    PerfilId = perfilId,
                    KpiCatalogoId = kpiId
                });
                await _db.SaveChangesAsync();
            }

            return RedirectToAction("AsignarPerfil", new { perfilId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuitarPerfil(int perfilId, int kpiId)
        {
            var item = await _db.PerfilKpiPermiso
                .FirstOrDefaultAsync(x => x.PerfilId == perfilId && x.KpiCatalogoId == kpiId);

            if (item != null)
            {
                _db.PerfilKpiPermiso.Remove(item);
                await _db.SaveChangesAsync();
            }

            return RedirectToAction("AsignarPerfil", new { perfilId });
        }

    }
}
