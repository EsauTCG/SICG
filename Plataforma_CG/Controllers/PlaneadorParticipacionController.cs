using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;
using Plataforma_CG.Services;

namespace Plataforma_CG.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[Route("Operaciones/Participacion")]
public sealed class PlaneadorParticipacionController(PlaneadorParticipacionService service,
    ILogger<PlaneadorParticipacionController> logger) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View("~/Views/Operaciones/Planeacion/Participacion.cshtml",
        new ParticipacionCatalogoPagina { UsuarioActual = User.Identity?.Name ?? "" });

    [HttpGet("Listado")]
    public Task<IActionResult> Listado(string? busqueda, int? clasificacionId, int pagina = 1, CancellationToken ct = default)
        => EjecutarAsync(async () => Json(await service.ListarAsync(busqueda, clasificacionId, pagina, ct)));

    [HttpPost("Guardar")]
    public Task<IActionResult> Guardar([FromBody] ParticipacionCatalogoGuardar? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid) return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "Revisa el porcentaje y los datos de la participación." }));
        return EjecutarAsync(async () =>
        {
            var registros = await service.GuardarAsync(input, User.Identity?.Name ?? "", ct);
            return Json(new { mensaje = registros == 0 ? "El porcentaje ya tenía ese valor; no se realizaron cambios." : "Porcentaje de participación actualizado correctamente.", registros });
        });
    }

    [HttpGet("Historial")]
    public Task<IActionResult> Historial(string sku, int clasificacionId, CancellationToken ct)
        => EjecutarAsync(async () => Json(await service.HistorialAsync(sku, clasificacionId, ct)));

    private async Task<IActionResult> EjecutarAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (ParticipacionValidacionException ex) { return BadRequest(new { mensaje = ex.Message }); }
        catch (ParticipacionConflictoException ex) { return Conflict(new { mensaje = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { mensaje = ex.Message }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception ex) { logger.LogError(ex, "Error en el catálogo de participación del Planeador."); return StatusCode(503, new { mensaje = "No se pudo completar la operación. Revisa CadenaSQLSIGO de Pruebas Esau y la instalación del historial de participación." }); }
    }
}
