using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Data;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using Plataforma_CG.Models.ActivacionSkus;
using Plataforma_CG.Services;

namespace Plataforma_CG.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[Route("ActivacionSkus")]
public sealed class ActivacionSkusController(ActivacionSkusService service, AppDbContextUsuarios db,
    ILogger<ActivacionSkusController> logger) : Controller
{
    [HttpGet("")]
    [HttpGet("Index")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "LEER")]
    public async Task<IActionResult> Index()
    {
        var permisos = await PermisosHelper.ObtenerPermisoEfectivoAsync(db, User.Identity?.Name ?? "", ActivacionSkusService.ClaveModulo);
        return View(new ActivacionSkusPagina(permisos.PuedeEscribir, permisos.PuedeEliminar, ActivacionSkusService.PlantasPermitidas));
    }

    [HttpGet("Programaciones")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "LEER")]
    public Task<IActionResult> Programaciones(string? planta, CancellationToken ct)
        => EjecutarAsync(planta, async () => Json(new { programaciones = await service.ProgramacionesAsync(planta, ct), tipos = ActivacionSkusService.TiposPermitidos }));

    [HttpGet("Listado")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "LEER")]
    public Task<IActionResult> Listado(string? planta, string? origen, string? busqueda, string? tipo, int pagina = 1, CancellationToken ct = default)
        => EjecutarAsync(planta, async () => Json(await service.ListarAsync(planta, origen, busqueda, tipo, pagina, ct)));

    [HttpGet("Disponibles")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "ESCRIBIR")]
    public Task<IActionResult> Disponibles(string? planta, string? origen, string? busqueda, int pagina = 1, CancellationToken ct = default)
        => EjecutarAsync(planta, async () => Json(await service.DisponiblesAsync(planta, origen, busqueda, pagina, ct)));

    [HttpPost("Agregar")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "ESCRIBIR")]
    public Task<IActionResult> Agregar([FromBody] AgregarSkuProgramacion? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Selecciona la planta, programación, SKU del catálogo y tipo." }));
        return EjecutarAsync(input.Planta, async () => Json(new { mensaje = "SKU agregado a la programación.",
            registro = await service.AgregarAsync(input, User.Identity?.Name ?? "", ct) }));
    }

    [HttpGet("Subclasificaciones")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "LEER")]
    public Task<IActionResult> Subclasificaciones(string? planta, string? busqueda, string? estado,
        int pagina = 1, CancellationToken ct = default)
    {
        if (planta == "P1")
            return Task.FromResult<IActionResult>(StatusCode(403, new { mensaje = "Subclasificación está disponible únicamente para Planta TIF." }));
        return EjecutarAsync(planta, async () => Json(await service.ListarSubclasificacionesAsync(planta, busqueda, estado, pagina, ct)));
    }

    [HttpPost("GuardarSubclasificacion")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "ESCRIBIR")]
    public Task<IActionResult> GuardarSubclasificacion([FromBody] GuardarSubclasificacionSkus? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Selecciona una planta y de 1 a 500 SKU indicando si deseas agregar o quitar N." }));
        if (input.Planta == "P1")
            return Task.FromResult<IActionResult>(StatusCode(403, new { mensaje = "Subclasificación está disponible únicamente para Planta TIF." }));
        return EjecutarAsync(input.Planta, async () => Json(new { mensaje = "Subclasificación guardada correctamente.",
            resultado = await service.GuardarSubclasificacionAsync(input, User.Identity?.Name ?? "", ct) }));
    }

    [HttpPost("Eliminar")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "ELIMINAR")]
    public Task<IActionResult> Eliminar([FromBody] EliminarSkuProgramacion? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Revisa la planta y el vínculo que deseas eliminar." }));
        return EjecutarAsync(input.Planta, async () => Json(new { mensaje = "SKU desvinculado de la programación.",
            registro = await service.EliminarAsync(input, User.Identity?.Name ?? "", ct) }));
    }

    [HttpPost("CambiarTipoMasivo")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "ESCRIBIR")]
    public Task<IActionResult> CambiarTipoMasivo([FromBody] CambiarTipoSkusMasivo? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Selecciona una planta, de 1 a 500 vínculos y un tipo válido (P, F, C o S)." }));
        return EjecutarAsync(input.Planta, async () => Json(new { mensaje = "Cambio masivo completado.",
            resultado = await service.CambiarTipoMasivoAsync(input, User.Identity?.Name ?? "", ct) }));
    }

    [HttpPost("AgregarMasivo")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "ESCRIBIR")]
    public Task<IActionResult> AgregarMasivo([FromBody] AgregarSkusMasivo? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Selecciona una planta, programación, de 1 a 500 SKU del catálogo y un tipo válido." }));
        return EjecutarAsync(input.Planta, async () => Json(new { mensaje = "Alta masiva completada.",
            resultado = await service.AgregarMasivoAsync(input, User.Identity?.Name ?? "", ct) }));
    }

    [HttpPost("CambiarTipo")]
    [RevisarPermiso(ActivacionSkusService.ClaveModulo, "ESCRIBIR")]
    public Task<IActionResult> CambiarTipo([FromBody] CambiarTipoSku? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Revisa la planta, la llave del registro y el tipo nuevo (P, F, C o S)." }));
        return EjecutarAsync(input.Planta, async () => Json(new { mensaje = "Vínculo de SKU actualizado correctamente.",
            registro = await service.CambiarTipoAsync(input, User.Identity?.Name ?? "", ct) }));
    }

    private async Task<IActionResult> EjecutarAsync(string? planta, Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (ActivacionSkusValidacionException ex) { return BadRequest(new { mensaje = ex.Message }); }
        catch (ActivacionSkusConflictoException ex) { return Conflict(new { mensaje = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { mensaje = ex.Message }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            logger.LogError(ex, "Error al consultar o actualizar Activación de SKUS en {Planta}.", planta);
            return StatusCode(503, new { mensaje = "No se pudo completar la operación. Revisa la conexión de la planta seleccionada e intenta nuevamente." });
        }
    }
}
