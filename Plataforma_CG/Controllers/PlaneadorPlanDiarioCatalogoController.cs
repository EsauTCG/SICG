using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;
using Plataforma_CG.Services;

namespace Plataforma_CG.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Operaciones/PlanDiarioCatalogo")]
public sealed class PlaneadorPlanDiarioCatalogoController(PlaneadorPlanDiarioCatalogoService service,
    ILogger<PlaneadorPlanDiarioCatalogoController> logger) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View("~/Views/Operaciones/Planeacion/PlanDiarioCatalogo.cshtml",
        new ParticipacionCatalogoPagina { UsuarioActual = User.Identity?.Name ?? "" });

    [HttpGet("Listado")]
    public Task<IActionResult> Listado(string? busqueda, int? clasificacionId, int pagina = 1, CancellationToken ct = default)
        => EjecutarAsync(async () => Json(await service.ListarAsync(busqueda, clasificacionId, pagina, ct)));

    [HttpGet("Articulos")]
    public Task<IActionResult> Articulos(string? busqueda, CancellationToken ct)
        => EjecutarAsync(async () => Json(await service.ArticulosAsync(busqueda, ct)));

    [HttpPost("Guardar")]
    public Task<IActionResult> Guardar([FromBody] PlanDiarioCatalogoGuardar? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid) return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Revisa el SKU, porcentaje, línea y distribuciones del catálogo." }));
        return EjecutarAsync(async () =>
        {
            var planes = await service.GuardarAsync(input, User.Identity?.Name ?? "", ct);
            return Json(new { mensaje = "Catálogo global guardado correctamente.", planesAfectados = planes });
        });
    }

    [HttpPost("Previsualizar")]
    public Task<IActionResult> Previsualizar([FromBody] PlanDiarioCatalogoGuardar? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid) return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Revisa las distribuciones del catálogo." }));
        return EjecutarAsync(async () => Json(await service.PrevisualizarAsync(input, null, ct)));
    }

    [HttpPost("PrevisualizarEliminar")]
    public Task<IActionResult> PrevisualizarEliminar([FromBody] PlanDiarioCatalogoEliminar? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid) return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Actualiza el catálogo antes de eliminar." }));
        return EjecutarAsync(async () => Json(await service.PrevisualizarAsync(null, input, ct)));
    }

    [HttpPost("Eliminar")]
    public Task<IActionResult> Eliminar([FromBody] PlanDiarioCatalogoEliminar? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid) return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Selecciona un registro válido y actualizado." }));
        return EjecutarAsync(async () =>
        {
            var planes = await service.EliminarAsync(input, User.Identity?.Name ?? "", ct);
            return Json(new { mensaje = "SKU eliminado del catálogo global y de los planes de ese tipo.", planesAfectados = planes });
        });
    }

    [HttpGet("Historial")]
    public Task<IActionResult> Historial(string? sku, int? clasificacionId, int pagina = 1, CancellationToken ct = default)
        => EjecutarAsync(async () => Json(await service.HistorialAsync(sku, clasificacionId, pagina, ct)));

    private async Task<IActionResult> EjecutarAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (PlanDiarioCatalogoValidacionException ex) { return BadRequest(new { mensaje = ex.Message }); }
        catch (PlanDiarioCatalogoConflictoException ex) { return Conflict(new { mensaje = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { mensaje = ex.Message }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error en el catálogo global del plan diario.");
            return StatusCode(503, new { mensaje = "No se pudo completar la operación. Revisa la conexión SIGO y la instalación de PlanDiarioCatalogoHistorial." });
        }
    }
}
