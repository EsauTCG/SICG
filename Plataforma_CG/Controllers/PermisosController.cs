using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Models;
using Plataforma_CG.Filters;


namespace Plataforma_CG.Controllers
{
    public class PermisosController : Controller
    {
        private readonly Data.AppDbContextUsuarios _db;
        public PermisosController(Data.AppDbContextUsuarios db) => _db = db;

        public async Task<IActionResult> PermisosConfiguracion()
        {
            var perfiles = await _db.Perfiles.ToListAsync();
            var vistas = await _db.Vistas.ToListAsync();
            var permisos = await _db.Permisos.ToListAsync();

            var modelo = new PermisosViewModel
            {
                Perfiles = perfiles,
                Vistas = vistas,
                Permisos = permisos
            };

            return View(modelo);
        }

        [HttpPost]
        public async Task<IActionResult> Asignar(int perfilId, int vistaId)
        {
            var permisoExistente = await _db.Permisos
                .FirstOrDefaultAsync(p => p.PerfilId == perfilId && p.VistaId == vistaId);

            if (permisoExistente == null)
            {
                var nuevoPermiso = new Permiso { PerfilId = perfilId, VistaId = vistaId };
                _db.Permisos.Add(nuevoPermiso);
                await _db.SaveChangesAsync();
                return Json(new { id = nuevoPermiso.Id });
            }

            return Json(new { id = permisoExistente.Id });
        }


        [HttpPost]
        public async Task<IActionResult> Revocar(int id)
        {
            var permiso = await _db.Permisos.FindAsync(id);
            if (permiso == null)
                return NotFound();

            _db.Permisos.Remove(permiso);
            await _db.SaveChangesAsync();

            return Ok();
        }


        // GET: Crear Perfil
        [HttpGet]
        public IActionResult CrearPerfil()
        {
            return View();
        }

        // POST: Crear Perfil
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearPerfil(Perfil perfil)
        {
            if (ModelState.IsValid)
            {
                _db.Perfiles.Add(perfil);
                await _db.SaveChangesAsync();
                return RedirectToAction("PermisosConfiguracion");
            }
            return View(perfil);
        }

        [HttpGet]
        public async Task<IActionResult> EliminarPerfil()
        {
            var perfiles = await _db.Perfiles.ToListAsync();
            return View(perfiles);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarPerfil(int id)
        {
            var perfil = await _db.Perfiles.FindAsync(id);

            if (perfil == null)
                return NotFound();

            var permisosAsociados = await _db.Permisos
                .Where(p => p.PerfilId == id)
                .ToListAsync();

            if (permisosAsociados.Any())
            {
                _db.Permisos.RemoveRange(permisosAsociados);
            }

            _db.Perfiles.Remove(perfil);
            await _db.SaveChangesAsync();

            return RedirectToAction("PermisosConfiguracion");
        }

        // GET: Crear Vista
        [HttpGet]
        public IActionResult CrearVista()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearVista(Vista vista)
        {
            System.Diagnostics.Debug.WriteLine("Entró al POST de CrearVista");

            if (ModelState.IsValid)
            {
                _db.Vistas.Add(vista);
                await _db.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine("Vista guardada correctamente");
                return RedirectToAction("PermisosConfiguracion");
            }

            System.Diagnostics.Debug.WriteLine("ModelState inválido");
            return View(vista);
        }

        // GET: Lista de vistas para editar
        [HttpGet]
        public async Task<IActionResult> EditarVista()
        {
            var vistas = await _db.Vistas.ToListAsync();
            return View(vistas);
        }

        // GET: Formulario de edición con el ID
        [HttpGet]
        public async Task<IActionResult> EditarVistaForm(int id)
        {
            var vista = await _db.Vistas.FindAsync(id);
            if (vista == null)
                return NotFound();

            return View(vista);
        }

        // POST: Guardar cambios
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarVistaForm(Vista vista)
        {
            if (ModelState.IsValid)
            {
                _db.Vistas.Update(vista);
                await _db.SaveChangesAsync();
                return RedirectToAction("PermisosConfiguracion");
            }
            return View(vista);
        }

        // GET: Eliminar Vista
        [HttpGet]
        public async Task<IActionResult> EliminarVista()
        {
            var vistas = await _db.Vistas.ToListAsync();
            return View(vistas);
        }

        // POST: Eliminar Vista
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarVista(int id)
        {
            var vista = await _db.Vistas.FindAsync(id);

            if (vista == null)
                return NotFound();

            // Eliminar permisos asociados primero
            var permisosAsociados = await _db.Permisos
                .Where(p => p.VistaId == id)
                .ToListAsync();

            if (permisosAsociados.Any())
            {
                _db.Permisos.RemoveRange(permisosAsociados);
            }

            _db.Vistas.Remove(vista);
            await _db.SaveChangesAsync();

            return RedirectToAction("PermisosConfiguracion");
        }

        [HttpGet]
        public async Task<IActionResult> CarouselConfiguracion()
        {
            var perfiles = await _db.Perfiles.ToListAsync();
            var carousel = await _db.CarouselPerfil
                .OrderBy(x => x.Orden)
                .ToListAsync();

            ViewBag.Perfiles = perfiles;
            return View(carousel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarCarousel(CarouselPerfil model)
        {
            var item = await _db.CarouselPerfil.FindAsync(model.Id);
            if (item == null)
                return NotFound();

            item.PerfilId = model.PerfilId;
            item.Activo = model.Activo;
            item.Orden = model.Orden;

            await _db.SaveChangesAsync();
            return RedirectToAction("CarouselConfiguracion");
        }

        [HttpGet]
        public IActionResult CrearCarousel()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearCarousel(IFormFile imagen)
        {
            System.Diagnostics.Debug.WriteLine("=== INICIO CrearCarousel ===");

            // 1. Validar imagen
            if (imagen == null || imagen.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("ERROR: No se recibió imagen");
                TempData["Error"] = "Debe seleccionar una imagen.";
                return View();
            }

            System.Diagnostics.Debug.WriteLine($"Imagen recibida: {imagen.FileName}, Tamaño: {imagen.Length} bytes");

            // 2. Validar tipo
            if (!imagen.ContentType.StartsWith("image/"))
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: Tipo inválido - {imagen.ContentType}");
                TempData["Error"] = "Solo se permiten imágenes.";
                return View();
            }

            // 3. Validar tamaño (5MB)
            if (imagen.Length > 5 * 1024 * 1024)
            {
                System.Diagnostics.Debug.WriteLine("ERROR: Imagen demasiado grande");
                TempData["Error"] = "La imagen no debe superar los 5 MB.";
                return View();
            }

            try
            {
                // 4. Preparar rutas (CAMBIADO A WWWROOT)
                var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var carpetaCarousel = Path.Combine(webRootPath, "img", "carousel");

                System.Diagnostics.Debug.WriteLine($"WebRoot: {webRootPath}");
                System.Diagnostics.Debug.WriteLine($"Carpeta destino: {carpetaCarousel}");

                // 5. Crear directorio si no existe
                if (!Directory.Exists(carpetaCarousel))
                {
                    System.Diagnostics.Debug.WriteLine("Creando directorio...");
                    Directory.CreateDirectory(carpetaCarousel);
                    System.Diagnostics.Debug.WriteLine("Directorio creado");
                }

                // 6. Generar nombre único
                var extension = Path.GetExtension(imagen.FileName).ToLowerInvariant();
                var nombreArchivo = $"{Guid.NewGuid()}{extension}";
                var rutaCompleta = Path.Combine(carpetaCarousel, nombreArchivo);

                System.Diagnostics.Debug.WriteLine($"Guardando en: {rutaCompleta}");

                // 7. Guardar archivo
                using (var stream = new FileStream(rutaCompleta, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await imagen.CopyToAsync(stream);
                    await stream.FlushAsync();
                }

                // 8. Verificar guardado
                if (!System.IO.File.Exists(rutaCompleta))
                {
                    throw new Exception("El archivo no se guardó correctamente");
                }

                var tamanoGuardado = new FileInfo(rutaCompleta).Length;
                System.Diagnostics.Debug.WriteLine($"Archivo guardado exitosamente. Tamaño: {tamanoGuardado} bytes");

                // 9. Guardar en BD (RUTA RELATIVA CAMBIADA)
                var nuevoCarousel = new CarouselPerfil
                {
                    ImagenUrl = $"/img/carousel/{nombreArchivo}", // ← CAMBIO AQUÍ
                    PerfilId = 0,
                    Activo = true,
                    Orden = await _db.CarouselPerfil.CountAsync() + 1
                };

                _db.CarouselPerfil.Add(nuevoCarousel);
                var filasAfectadas = await _db.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"Registro guardado en BD. ID: {nuevoCarousel.Id}, Filas: {filasAfectadas}");

                TempData["Exito"] = "Imagen subida correctamente";
                return RedirectToAction("CarouselConfiguracion");
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR DE PERMISOS: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                TempData["Error"] = "Error de permisos. Contacte al administrador del servidor.";
                return View();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR GENERAL: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Mensaje: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                TempData["Error"] = $"Error: {ex.Message}";
                return View();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarCarouselAjax([FromBody] CarouselPerfil model)
        {
            var item = await _db.CarouselPerfil.FindAsync(model.Id);
            if (item == null)
                return NotFound();

            item.PerfilId = model.PerfilId;
            item.Activo = model.Activo;
            item.Orden = model.Orden;

            await _db.SaveChangesAsync();
            return Ok();
        }


        [HttpPost]
        [Route("Permisos/EliminarCarouselAjax")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarCarouselAjax([FromBody] EliminarCarouselDto dto)
        {
            var item = await _db.CarouselPerfil.FindAsync(dto.Id);
            if (item == null)
                return NotFound();

            // Eliminar archivo físico
            if (!string.IsNullOrEmpty(item.ImagenUrl))
            {
                try
                {
                    // Si usas Opción 1 (AppData):
                    var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var rutaArchivo = Path.Combine(
                        webRootPath,
                        "img",
                        "carousel",
                        Path.GetFileName(item.ImagenUrl)
                    );
                    if (System.IO.File.Exists(rutaArchivo))
                        System.IO.File.Delete(rutaArchivo);

                }
                catch (Exception ex)
                {
                    // Log pero no falla la operación
                    System.Diagnostics.Debug.WriteLine($"Error al eliminar archivo: {ex.Message}");
                }
            }

            _db.CarouselPerfil.Remove(item);
            await _db.SaveChangesAsync();

            return Ok();
        }

        // =======================================================
        // MÉTODOS PARA PESTAÑA DE MÓDULOS (LEER / ESCRIBIR / ELIMINAR)
        // =======================================================
        [HttpPost]
        public async Task<IActionResult> CrearModuloSistema([FromBody] ModulosSistema modelo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelo.Clave) || string.IsNullOrWhiteSpace(modelo.Nombre))
                    return Json(new { ok = false, mensaje = "La clave y el nombre son obligatorios." });

                // Normalizar clave
                modelo.Clave = modelo.Clave.Trim().ToUpper().Replace(" ", "_");

                // Validar duplicados
                bool existe = await _db.ModulosSistema.AnyAsync(m => m.Clave == modelo.Clave);
                if (existe)
                    return Json(new { ok = false, mensaje = "Ya existe un módulo con esa clave exacta." });

                var nuevoModulo = new ModulosSistema
                {
                    Clave = modelo.Clave,
                    Nombre = modelo.Nombre.Trim(),
                    Activo = true,
                    FechaCreacion = DateTime.Now
                };

                _db.ModulosSistema.Add(nuevoModulo);
                await _db.SaveChangesAsync();

                return Json(new { ok = true, clave = nuevoModulo.Clave });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }
        [HttpGet]
        public async Task<IActionResult> ObtenerPermisosPorPerfil(int perfilId)
        {
            try
            {
                // Obtenemos todos los módulos activos del sistema (Reglas Comerciales, etc.)
                var modulos = await _db.ModulosSistema.Where(m => m.Activo).ToListAsync();

                // Obtenemos los permisos que ya tiene configurados este perfil en la BD
                var permisosGuardados = await _db.PerfilPermisoModulo
                                                 .Where(p => p.PerfilId == perfilId && p.Activo)
                                                 .ToListAsync();

                // Cruzamos la información: Módulos vs Permisos Guardados
                var matrizPermisos = modulos.Select(m => {
                    var permisoBD = permisosGuardados.FirstOrDefault(p => p.ModuloId == m.Id);
                    return new PermisoModuloDto
                    {
                        ModuloId = m.Id,
                        NombreModulo = m.Nombre,
                        ClaveModulo = m.Clave,
                        PuedeLeer = permisoBD?.PuedeLeer ?? false,
                        PuedeEscribir = permisoBD?.PuedeEscribir ?? false,
                        PuedeEliminar = permisoBD?.PuedeEliminar ?? false
                    };
                }).ToList();

                return Json(matrizPermisos);
            }
            catch (Exception ex)
            {
                // En caso de error, devolvemos un 500 o mensaje controlado
                return StatusCode(500, new { mensaje = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GuardarPermisosModulos([FromBody] GuardarPermisosModulosDto datos)
        {
            if (datos == null || datos.Permisos == null)
                return Json(new { ok = false, mensaje = "Datos inválidos o vacíos." });

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var permisosActuales = await _db.PerfilPermisoModulo
                                                .Where(p => p.PerfilId == datos.PerfilId)
                                                .ToListAsync();

                foreach (var item in datos.Permisos)
                {
                    var permisoBD = permisosActuales.FirstOrDefault(p => p.ModuloId == item.ModuloId);

                    if (permisoBD != null)
                    {
                        // Si ya existe, lo actualizamos
                        permisoBD.PuedeLeer = item.PuedeLeer;
                        permisoBD.PuedeEscribir = item.PuedeEscribir;
                        permisoBD.PuedeEliminar = item.PuedeEliminar;
                        permisoBD.FechaModificacion = DateTime.Now;
                    }
                    else
                    {
                        // Si no existe, insertamos el nuevo registro
                        _db.PerfilPermisoModulo.Add(new PerfilPermisoModulo
                        {
                            PerfilId = datos.PerfilId,
                            ModuloId = item.ModuloId,
                            PuedeLeer = item.PuedeLeer,
                            PuedeEscribir = item.PuedeEscribir,
                            PuedeEliminar = item.PuedeEliminar,
                            Activo = true,
                            FechaCreacion = DateTime.Now
                        });
                    }
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { ok = true, mensaje = "Permisos guardados correctamente." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { ok = false, mensaje = "Error al guardar: " + ex.Message });
            }
        }


    }

    // 🔹 Nuevo ViewModel auxiliar
    public class PermisosViewModel
    {
        public List<Perfil> Perfiles { get; set; }
        public List<Vista> Vistas { get; set; }
        public List<Permiso> Permisos { get; set; }
    }
    // 🔹 DTOs para la matriz de permisos de Módulos
    public class PermisoModuloDto
    {
        public int ModuloId { get; set; }
        public string NombreModulo { get; set; }
        public string ClaveModulo { get; set; }
        public bool PuedeLeer { get; set; }
        public bool PuedeEscribir { get; set; }
        public bool PuedeEliminar { get; set; }
    }

    public class GuardarPermisosModulosDto
    {
        public int PerfilId { get; set; }
        public List<PermisoModuloDto> Permisos { get; set; }
    }
}