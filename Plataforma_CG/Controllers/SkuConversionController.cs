using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Linq;

namespace Plataforma_CG.Controllers
{
    public class SkuConversionController : Controller
    {
        private readonly AppDbContext _context;

        public SkuConversionController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult SkuConversion(int? grupoId, string? sku)
        {
            // 1️⃣ Grupos - AHORA INCLUYE INACTIVOS
            var grupos = _context.SkuGrupo
                .OrderByDescending(g => g.Activo) // Activos primero
                .ThenBy(g => g.MasterSku)
                .ToList();

            if (!grupos.Any())
            {
                ViewBag.Grupos = grupos;
                ViewBag.Items = Enumerable.Empty<SkuGrupoItem>();
                ViewBag.Conversiones = Enumerable.Empty<SkuConversion>();
                return View();
            }

            // 2️⃣ Grupo seleccionado
            var grupoSeleccionado = grupoId.HasValue
                ? grupos.FirstOrDefault(g => g.GrupoId == grupoId)
                : grupos.FirstOrDefault(g => g.Activo); // Primero busca uno activo

            if (grupoSeleccionado == null)
                grupoSeleccionado = grupos.First(); // Si no hay activos, toma el primero

            // 3️⃣ Items del grupo - AHORA INCLUYE INACTIVOS
            var items = _context.SkuGrupoItem
                .Where(i => i.GrupoId == grupoSeleccionado.GrupoId)
                .OrderByDescending(i => i.Activo) // Activos primero
                .ThenBy(i => i.Nivel)
                .ThenBy(i => i.Orden)
                .ToList();

            // 4️⃣ SKU seleccionado
            var skuSeleccionado = !string.IsNullOrEmpty(sku)
                ? sku
                : grupoSeleccionado.MasterSku;

            // 5️⃣ Conversiones - AHORA INCLUYE INACTIVAS
            var conversiones = _context.SkuConversion
                .Where(c => c.SkuOrigen == skuSeleccionado)
                .OrderByDescending(c => c.Activo) // Activas primero
                .ThenBy(c => c.Prioridad)
                .ToList();

            // 6️⃣ ViewBag
            ViewBag.Grupos = grupos;
            ViewBag.GrupoSeleccionado = grupoSeleccionado;
            ViewBag.Items = items;
            ViewBag.SkuSeleccionado = skuSeleccionado;
            ViewBag.Conversiones = conversiones;

            return View();
        }

        // ====================================
        // MÉTODOS AJAX - CONSULTA
        // ====================================

        [HttpGet]
        public IActionResult GetGrupoItems(int grupoId)
        {
            var items = _context.SkuGrupoItem
                .Where(i => i.GrupoId == grupoId)
                .OrderByDescending(i => i.Activo)
                .ThenBy(i => i.Nivel)
                .ThenBy(i => i.Orden)
                .Select(i => new
                {
                    i.Sku,
                    i.TipoRelacion,
                    i.Nivel,
                    i.Activo
                })
                .ToList();

            var grupo = _context.SkuGrupo
                .FirstOrDefault(g => g.GrupoId == grupoId);

            return Json(new
            {
                items = items,
                masterSku = grupo?.MasterSku
            });
        }

        [HttpGet]
        public IActionResult GetConversiones(string sku)
        {
            var conversiones = _context.SkuConversion
                .Where(c => c.SkuOrigen == sku)
                .OrderByDescending(c => c.Activo)
                .ThenBy(c => c.Prioridad)
                .Select(c => new
                {
                    c.Id,
                    c.Prioridad,
                    c.SkuOrigen,
                    c.SkuDestino,
                    c.Factor,
                    c.Activo
                })
                .ToList();

            return Json(conversiones);
        }

        // ====================================
        //  OBTENER DATOS PARA EDICIÓN
        // ====================================

        [HttpGet]
        public IActionResult GetGrupo(int grupoId)
        {
            var grupo = _context.SkuGrupo
                .Where(g => g.GrupoId == grupoId)
                .Select(g => new
                {
                    g.GrupoId,
                    g.MasterSku,
                    g.NombreGrupo,
                    g.Activo
                })
                .FirstOrDefault();

            if (grupo == null)
            {
                return Json(new { success = false, message = "Grupo no encontrado" });
            }

            return Json(new { success = true, grupo = grupo });
        }

        [HttpGet]
        public IActionResult GetSkuItem(int grupoId, string sku)
        {
            var item = _context.SkuGrupoItem
                .Where(i => i.GrupoId == grupoId && i.Sku == sku)
                .Select(i => new
                {
                    i.GrupoId,
                    i.Sku,
                    i.TipoRelacion,
                    i.Nivel,
                    i.Orden,
                    i.Activo
                })
                .FirstOrDefault();

            if (item == null)
            {
                return Json(new { success = false, message = "SKU no encontrado" });
            }

            return Json(new { success = true, item = item });
        }

        [HttpGet]
        public IActionResult GetConversion(int id)
        {
            var conversion = _context.SkuConversion
                .Where(c => c.Id == id)
                .Select(c => new
                {
                    c.Id,
                    c.SkuOrigen,
                    c.SkuDestino,
                    c.Prioridad,
                    c.Activo
                })
                .FirstOrDefault();

            if (conversion == null)
            {
                return Json(new { success = false, message = "Conversión no encontrada" });
            }

            return Json(new { success = true, conversion = conversion });
        }

        // ====================================
        // CREAR GRUPO SKU
        // ====================================

        [HttpPost]
        public IActionResult CrearGrupo([FromBody] SkuGrupo grupo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(grupo.MasterSku))
                {
                    return Json(new { success = false, message = "El Master SKU es requerido" });
                }

                if (string.IsNullOrWhiteSpace(grupo.NombreGrupo))
                {
                    return Json(new { success = false, message = "El Nombre del Grupo es requerido" });
                }

                var existe = _context.SkuGrupo.Any(g => g.MasterSku == grupo.MasterSku && g.Activo);
                if (existe)
                {
                    return Json(new { success = false, message = "Ya existe un grupo activo con ese Master SKU" });
                }

                grupo.Activo = true;
                grupo.CreatedAt = DateTime.Now;
                grupo.UpdatedAt = DateTime.Now;

                _context.SkuGrupo.Add(grupo);
                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Grupo creado exitosamente",
                    grupo = new
                    {
                        grupo.GrupoId,
                        grupo.MasterSku,
                        grupo.NombreGrupo,
                        grupo.Activo
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al crear el grupo: " + ex.Message });
            }
        }

        // ====================================
        //  EDITAR GRUPO SKU
        // ====================================

        [HttpPost]
        public IActionResult EditarGrupo([FromBody] SkuGrupo grupoEditado)
        {
            try
            {
                var grupo = _context.SkuGrupo.FirstOrDefault(g => g.GrupoId == grupoEditado.GrupoId);
                if (grupo == null)
                {
                    return Json(new { success = false, message = "El grupo no existe" });
                }

                if (string.IsNullOrWhiteSpace(grupoEditado.NombreGrupo))
                {
                    return Json(new { success = false, message = "El Nombre del Grupo es requerido" });
                }

                // Actualizar solo campos editables (NO el MasterSku)
                grupo.NombreGrupo = grupoEditado.NombreGrupo;
                grupo.UpdatedAt = DateTime.Now;

                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Grupo actualizado exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al editar el grupo: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult CrearSkuItem([FromBody] SkuGrupoItem item)
        {
            try
            {
                if (item.GrupoId <= 0)
                {
                    return Json(new { success = false, message = "Debe seleccionar un grupo" });
                }

                if (string.IsNullOrWhiteSpace(item.Sku))
                {
                    return Json(new { success = false, message = "El SKU es requerido" });
                }

                //  VALIDAR TIPO DE RELACIÓN
                if (item.TipoRelacion != "Master" && item.TipoRelacion != "Derivado")
                {
                    return Json(new { success = false, message = "Tipo de Relación debe ser 'Master' o 'Derivado'" });
                }

                //  VALIDAR NIVEL SEGÚN TIPO
                if (item.TipoRelacion == "Master" && item.Nivel != 0)
                {
                    return Json(new { success = false, message = "El Master SKU debe tener Nivel 0" });
                }

                if (item.TipoRelacion == "Derivado" && item.Nivel == 0)
                {
                    return Json(new { success = false, message = "Un SKU Derivado debe tener Nivel mayor a 0" });
                }

                var grupo = _context.SkuGrupo.FirstOrDefault(g => g.GrupoId == item.GrupoId && g.Activo);
                if (grupo == null)
                {
                    return Json(new { success = false, message = "El grupo seleccionado no existe o está inactivo" });
                }

                //  ASIGNAR PARENT SKU AUTOMÁTICAMENTE
                if (item.TipoRelacion == "Derivado")
                {
                    item.ParentSku = grupo.MasterSku; // El padre siempre es el Master del grupo
                }
                else
                {
                    item.ParentSku = null; // Master no tiene padre
                }

                var existe = _context.SkuGrupoItem.Any(i =>
                    i.GrupoId == item.GrupoId &&
                    i.Sku == item.Sku &&
                    i.Activo);
                if (existe)
                {
                    return Json(new { success = false, message = "El SKU ya existe activo en este grupo" });
                }

                item.Activo = true;
                item.CreatedAt = DateTime.Now;
                item.UpdatedAt = DateTime.Now;

                var itemsEnNivel = _context.SkuGrupoItem
                    .Where(i => i.GrupoId == item.GrupoId && i.Nivel == item.Nivel && i.Activo)
                    .ToList();

                if (item.Orden == 0 && itemsEnNivel.Any())
                {
                    var maxOrden = itemsEnNivel.Max(i => i.Orden);
                    item.Orden = maxOrden + 1;
                }

                _context.SkuGrupoItem.Add(item);
                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "SKU agregado exitosamente",
                    item = new
                    {
                        item.Sku,
                        item.TipoRelacion,
                        item.ParentSku,
                        item.Nivel,
                        item.Orden
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al crear el SKU: " + ex.Message });
            }
        }

        // ====================================
        //  EDITAR SKU ITEM
        // ====================================

        [HttpPost]
        public IActionResult EditarSkuItem([FromBody] EditSkuItemRequest request)
        {
            try
            {
                var item = _context.SkuGrupoItem.FirstOrDefault(i =>
                    i.GrupoId == request.GrupoId &&
                    i.Sku == request.SkuOriginal);

                if (item == null)
                {
                    return Json(new { success = false, message = "El SKU no existe" });
                }

                //  VALIDAR TIPO DE RELACIÓN
                if (request.TipoRelacion != "Master" && request.TipoRelacion != "Derivado")
                {
                    return Json(new { success = false, message = "Tipo de Relación debe ser 'Master' o 'Derivado'" });
                }

                //  VALIDAR NIVEL SEGÚN TIPO
                if (request.TipoRelacion == "Master" && request.Nivel != 0)
                {
                    return Json(new { success = false, message = "El Master SKU debe tener Nivel 0" });
                }

                if (request.TipoRelacion == "Derivado" && request.Nivel == 0)
                {
                    return Json(new { success = false, message = "Un SKU Derivado debe tener Nivel mayor a 0" });
                }

                var grupo = _context.SkuGrupo.FirstOrDefault(g => g.GrupoId == request.GrupoId && g.Activo);
                if (grupo == null)
                {
                    return Json(new { success = false, message = "El grupo no existe o está inactivo" });
                }

                //  ASIGNAR PARENT SKU AUTOMÁTICAMENTE SEGÚN TIPO
                if (request.TipoRelacion == "Derivado")
                {
                    item.ParentSku = grupo.MasterSku;
                }
                else
                {
                    item.ParentSku = null;
                }

                // Actualizar campos editables
                item.TipoRelacion = request.TipoRelacion;
                item.Nivel = request.Nivel;
                item.Orden = request.Orden;
                item.UpdatedAt = DateTime.Now;

                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "SKU actualizado exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al editar el SKU: " + ex.Message });
            }
        }

        // ====================================
        // CREAR CONVERSIÓN
        // ====================================

        [HttpPost]
        public IActionResult CrearConversion([FromBody] SkuConversion conversion)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(conversion.SkuOrigen))
                {
                    return Json(new { success = false, message = "El SKU Origen es requerido" });
                }

                if (string.IsNullOrWhiteSpace(conversion.SkuDestino))
                {
                    return Json(new { success = false, message = "El SKU Destino es requerido" });
                }

                var existe = _context.SkuConversion.Any(c =>
                    c.SkuOrigen == conversion.SkuOrigen &&
                    c.SkuDestino == conversion.SkuDestino &&
                    c.Activo);
                if (existe)
                {
                    return Json(new { success = false, message = "Esta conversión ya existe activa" });
                }

                conversion.Activo = true;
                conversion.CreatedAt = DateTime.Now;
                conversion.UpdatedAt = DateTime.Now;

                // 🔥 LÓGICA CORRECTA: 0 (automático) asigna null para la primera, luego 1, 2, 3...
                if (!conversion.Prioridad.HasValue || conversion.Prioridad == 0)
                {
                    // Verificar si ya existe alguna conversión para este SKU
                    var conversionesExistentes = _context.SkuConversion
                        .Where(c => c.SkuOrigen == conversion.SkuOrigen && c.Activo)
                        .ToList();

                    if (!conversionesExistentes.Any())
                    {
                        // 🔥 Primera conversión = null (se mostrará como "Base")
                        conversion.Prioridad = null;
                    }
                    else
                    {
                        // 🔥 Ya existen conversiones, buscar el máximo y sumar 1
                        // Si todas son null, empezar en 1
                        var prioridadesConValor = conversionesExistentes
                            .Where(c => c.Prioridad.HasValue)
                            .Select(c => c.Prioridad.Value)
                            .ToList();

                        if (!prioridadesConValor.Any())
                        {
                            // Solo existe la BASE (null), la siguiente es 1
                            conversion.Prioridad = 1;
                        }
                        else
                        {
                            // Ya hay conversiones con prioridad, tomar el máximo + 1
                            conversion.Prioridad = prioridadesConValor.Max() + 1;
                        }
                    }
                }
                // Si el usuario puso un número específico (mayor a 0), respetarlo
                // conversion.Prioridad ya tiene el valor que el usuario eligió

                _context.SkuConversion.Add(conversion);
                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Conversión creada exitosamente",
                    conversion = new
                    {
                        conversion.SkuOrigen,
                        conversion.SkuDestino,
                        conversion.Prioridad,
                        conversion.Activo
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al crear la conversión: " + ex.Message });
            }
        }

        // ====================================
        //  EDITAR CONVERSIÓN
        // ====================================

        [HttpPost]
        public IActionResult EditarConversion([FromBody] SkuConversion conversionEditada)
        {
            try
            {
                var conversion = _context.SkuConversion.FirstOrDefault(c => c.Id == conversionEditada.Id);
                if (conversion == null)
                {
                    return Json(new { success = false, message = "La conversión no existe" });
                }

                if (string.IsNullOrWhiteSpace(conversionEditada.SkuDestino))
                {
                    return Json(new { success = false, message = "El SKU Destino es requerido" });
                }

                // Actualizar campos editables
                conversion.SkuDestino = conversionEditada.SkuDestino;
                conversion.Prioridad = conversionEditada.Prioridad;
                conversion.Factor = conversionEditada.Factor;
                conversion.UpdatedAt = DateTime.Now;

                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Conversión actualizada exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al editar la conversión: " + ex.Message });
            }
        }

        // ====================================
        // DESACTIVAR GRUPO SKU (SOFT DELETE)
        // ====================================

        [HttpPost]
        public IActionResult DesactivarGrupo([FromBody] int grupoId)
        {
            try
            {
                var grupo = _context.SkuGrupo.FirstOrDefault(g => g.GrupoId == grupoId && g.Activo);
                if (grupo == null)
                {
                    return Json(new { success = false, message = "El grupo no existe o ya fue desactivado" });
                }

                var skusDelGrupo = _context.SkuGrupoItem
                    .Where(i => i.GrupoId == grupoId && i.Activo)
                    .Select(i => i.Sku)
                    .ToList();

                var conversiones = _context.SkuConversion
                    .Where(c => skusDelGrupo.Contains(c.SkuOrigen) || skusDelGrupo.Contains(c.SkuDestino))
                    .Where(c => c.Activo)
                    .ToList();

                foreach (var conv in conversiones)
                {
                    conv.Activo = false;
                    conv.UpdatedAt = DateTime.Now;
                }

                var items = _context.SkuGrupoItem
                    .Where(i => i.GrupoId == grupoId && i.Activo)
                    .ToList();

                foreach (var item in items)
                {
                    item.Activo = false;
                    item.UpdatedAt = DateTime.Now;
                }

                grupo.Activo = false;
                grupo.UpdatedAt = DateTime.Now;

                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Grupo, items y conversiones asociadas desactivados exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al desactivar el grupo: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult ReactivarGrupo([FromBody] int grupoId)
        {
            try
            {
                var grupo = _context.SkuGrupo.FirstOrDefault(g => g.GrupoId == grupoId && !g.Activo);
                if (grupo == null)
                {
                    return Json(new { success = false, message = "El grupo no existe o ya está activo" });
                }

                grupo.Activo = true;
                grupo.UpdatedAt = DateTime.Now;

                var items = _context.SkuGrupoItem
                    .Where(i => i.GrupoId == grupoId && !i.Activo)
                    .ToList();

                foreach (var item in items)
                {
                    item.Activo = true;
                    item.UpdatedAt = DateTime.Now;
                }

                var skusDelGrupo = _context.SkuGrupoItem
                    .Where(i => i.GrupoId == grupoId)
                    .Select(i => i.Sku)
                    .ToList();

                var conversiones = _context.SkuConversion
                    .Where(c => skusDelGrupo.Contains(c.SkuOrigen) || skusDelGrupo.Contains(c.SkuDestino))
                    .Where(c => !c.Activo)
                    .ToList();

                foreach (var conv in conversiones)
                {
                    conv.Activo = true;
                    conv.UpdatedAt = DateTime.Now;
                }

                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Grupo, items y conversiones reactivados exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al reactivar el grupo: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult EliminarGrupo([FromBody] int grupoId)
        {
            try
            {
                var grupo = _context.SkuGrupo.FirstOrDefault(g => g.GrupoId == grupoId);
                if (grupo == null)
                {
                    return Json(new { success = false, message = "El grupo no existe" });
                }

                var skusDelGrupo = _context.SkuGrupoItem
                    .Where(i => i.GrupoId == grupoId)
                    .Select(i => i.Sku)
                    .ToList();

                var conversiones = _context.SkuConversion
                    .Where(c => skusDelGrupo.Contains(c.SkuOrigen) || skusDelGrupo.Contains(c.SkuDestino))
                    .ToList();

                if (conversiones.Any())
                {
                    _context.SkuConversion.RemoveRange(conversiones);
                }

                var items = _context.SkuGrupoItem
                    .Where(i => i.GrupoId == grupoId)
                    .ToList();

                if (items.Any())
                {
                    _context.SkuGrupoItem.RemoveRange(items);
                }

                _context.SkuGrupo.Remove(grupo);

                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Grupo, items y conversiones eliminados permanentemente de la base de datos"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al eliminar el grupo: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult DesactivarSkuItem([FromBody] DeleteSkuItemRequest request)
        {
            try
            {
                var item = _context.SkuGrupoItem.FirstOrDefault(i =>
                    i.GrupoId == request.GrupoId &&
                    i.Sku == request.Sku &&
                    i.Activo);

                if (item == null)
                {
                    return Json(new { success = false, message = "El SKU no existe o ya fue desactivado" });
                }

                if (item.TipoRelacion == "Master")
                {
                    var skusDelGrupo = _context.SkuGrupoItem
                        .Where(i => i.GrupoId == request.GrupoId && i.Activo)
                        .Select(i => i.Sku)
                        .ToList();

                    var conversiones = _context.SkuConversion
                        .Where(c => skusDelGrupo.Contains(c.SkuOrigen) || skusDelGrupo.Contains(c.SkuDestino))
                        .Where(c => c.Activo)
                        .ToList();

                    foreach (var conv in conversiones)
                    {
                        conv.Activo = false;
                        conv.UpdatedAt = DateTime.Now;
                    }

                    var todosLosItems = _context.SkuGrupoItem
                        .Where(i => i.GrupoId == request.GrupoId && i.Activo)
                        .ToList();

                    foreach (var itm in todosLosItems)
                    {
                        itm.Activo = false;
                        itm.UpdatedAt = DateTime.Now;
                    }

                    var grupo = _context.SkuGrupo.FirstOrDefault(g => g.GrupoId == request.GrupoId && g.Activo);
                    if (grupo != null)
                    {
                        grupo.Activo = false;
                        grupo.UpdatedAt = DateTime.Now;
                    }

                    _context.SaveChanges();

                    return Json(new
                    {
                        success = true,
                        message = "Master SKU, grupo y conversiones asociadas desactivados exitosamente",
                        grupoEliminado = true
                    });
                }

                var conversionesDelSku = _context.SkuConversion
                    .Where(c => (c.SkuOrigen == request.Sku || c.SkuDestino == request.Sku) && c.Activo)
                    .ToList();

                foreach (var conv in conversionesDelSku)
                {
                    conv.Activo = false;
                    conv.UpdatedAt = DateTime.Now;
                }

                item.Activo = false;
                item.UpdatedAt = DateTime.Now;
                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "SKU y conversiones asociadas desactivados exitosamente",
                    grupoEliminado = false
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al desactivar el SKU: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult ReactivarSkuItem([FromBody] DeleteSkuItemRequest request)
        {
            try
            {
                var item = _context.SkuGrupoItem.FirstOrDefault(i =>
                    i.GrupoId == request.GrupoId &&
                    i.Sku == request.Sku &&
                    !i.Activo);

                if (item == null)
                {
                    return Json(new { success = false, message = "El SKU no existe o ya está activo" });
                }

                var grupo = _context.SkuGrupo.FirstOrDefault(g => g.GrupoId == request.GrupoId);
                if (grupo == null || !grupo.Activo)
                {
                    return Json(new { success = false, message = "El grupo está inactivo. Active el grupo primero." });
                }

                item.Activo = true;
                item.UpdatedAt = DateTime.Now;

                var conversionesDelSku = _context.SkuConversion
                    .Where(c => (c.SkuOrigen == request.Sku || c.SkuDestino == request.Sku) && !c.Activo)
                    .ToList();

                foreach (var conv in conversionesDelSku)
                {
                    conv.Activo = true;
                    conv.UpdatedAt = DateTime.Now;
                }

                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "SKU y conversiones asociadas reactivados exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al reactivar el SKU: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult EliminarSkuItem([FromBody] DeleteSkuItemRequest request)
        {
            try
            {
                var item = _context.SkuGrupoItem.FirstOrDefault(i =>
                    i.GrupoId == request.GrupoId &&
                    i.Sku == request.Sku);

                if (item == null)
                {
                    return Json(new { success = false, message = "El SKU no existe" });
                }

                if (item.TipoRelacion == "Master")
                {
                    var skusDelGrupo = _context.SkuGrupoItem
                        .Where(i => i.GrupoId == request.GrupoId)
                        .Select(i => i.Sku)
                        .ToList();

                    var conversiones = _context.SkuConversion
                        .Where(c => skusDelGrupo.Contains(c.SkuOrigen) || skusDelGrupo.Contains(c.SkuDestino))
                        .ToList();

                    if (conversiones.Any())
                    {
                        _context.SkuConversion.RemoveRange(conversiones);
                    }

                    var todosLosItems = _context.SkuGrupoItem
                        .Where(i => i.GrupoId == request.GrupoId)
                        .ToList();

                    if (todosLosItems.Any())
                    {
                        _context.SkuGrupoItem.RemoveRange(todosLosItems);
                    }

                    var grupo = _context.SkuGrupo.FirstOrDefault(g => g.GrupoId == request.GrupoId);
                    if (grupo != null)
                    {
                        _context.SkuGrupo.Remove(grupo);
                    }

                    _context.SaveChanges();

                    return Json(new
                    {
                        success = true,
                        message = "Master SKU, grupo y conversiones eliminados permanentemente de la base de datos",
                        grupoEliminado = true
                    });
                }

                var conversionesDelSku = _context.SkuConversion
                    .Where(c => c.SkuOrigen == request.Sku || c.SkuDestino == request.Sku)
                    .ToList();

                if (conversionesDelSku.Any())
                {
                    _context.SkuConversion.RemoveRange(conversionesDelSku);
                }

                _context.SkuGrupoItem.Remove(item);
                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "SKU y conversiones eliminados permanentemente de la base de datos",
                    grupoEliminado = false
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al eliminar el SKU: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult DesactivarConversion([FromBody] int id)
        {
            try
            {
                var conversion = _context.SkuConversion.FirstOrDefault(c =>
                    c.Id == id && c.Activo);

                if (conversion == null)
                {
                    return Json(new { success = false, message = "La conversión no existe o ya fue desactivada" });
                }

                conversion.Activo = false;
                conversion.UpdatedAt = DateTime.Now;
                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Conversión desactivada exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al desactivar la conversión: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult ReactivarConversion([FromBody] int id)
        {
            try
            {
                var conversion = _context.SkuConversion.FirstOrDefault(c =>
                    c.Id == id && !c.Activo);

                if (conversion == null)
                {
                    return Json(new { success = false, message = "La conversión no existe o ya está activa" });
                }

                conversion.Activo = true;
                conversion.UpdatedAt = DateTime.Now;
                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Conversión reactivada exitosamente"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al reactivar la conversión: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult EliminarConversion([FromBody] int id)
        {
            try
            {
                var conversion = _context.SkuConversion.FirstOrDefault(c => c.Id == id);

                if (conversion == null)
                {
                    return Json(new { success = false, message = "La conversión no existe" });
                }

                _context.SkuConversion.Remove(conversion);
                _context.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Conversión eliminada permanentemente de la base de datos"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al eliminar la conversión: " + ex.Message });
            }
        }

        // ====================================
        // CLASES AUXILIARES PARA REQUESTS
        // ====================================

        public class DeleteSkuItemRequest
        {
            public int GrupoId { get; set; }
            public string Sku { get; set; }
        }

        public class EditSkuItemRequest
        {
            public int GrupoId { get; set; }
            public string SkuOriginal { get; set; }
            public string TipoRelacion { get; set; }
            public int Nivel { get; set; }
            public int Orden { get; set; }
        }
    }
}