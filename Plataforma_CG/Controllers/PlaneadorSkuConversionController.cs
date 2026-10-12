using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;
using Plataforma_CG.Services;

namespace Plataforma_CG.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[Route("Operaciones/SkuConversion")]
public sealed class PlaneadorSkuConversionController(PlaneadorSkuConversionService service,
    ILogger<PlaneadorSkuConversionController> logger) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View("~/Views/Operaciones/Planeacion/SkuConversion.cshtml",
        new SkuConversionPagina { UsuarioActual = User.Identity?.Name ?? "" });

    [HttpGet("Listado")]
    public Task<IActionResult> Listado(string? busqueda, string? estado = "todos", int pagina = 1, CancellationToken ct = default)
        => EjecutarAsync(async () => Json(await service.ListarAsync(busqueda, estado, pagina, ct)));

    [HttpGet("Articulos")]
    public Task<IActionResult> Articulos(string? busqueda, CancellationToken ct)
        => EjecutarAsync(async () => Json(await service.ArticulosAsync(busqueda, ct)));

    [HttpPost("Guardar")]
    public Task<IActionResult> Guardar([FromBody] SkuConversionGuardar? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid) return Task.FromResult<IActionResult>(DatosInvalidos());
        return EjecutarAsync(async () =>
        {
            var registro = await service.GuardarAsync(input, User.Identity?.Name ?? "", ct);
            return Json(new { mensaje = input.Id == 0 ? "Conversión creada correctamente." : "Conversión actualizada correctamente.", registro });
        });
    }

    [HttpPost("Estado")]
    public Task<IActionResult> Estado([FromBody] SkuConversionEstado? input, CancellationToken ct)
    {
        if (input is null || !ModelState.IsValid) return Task.FromResult<IActionResult>(DatosInvalidos());
        return EjecutarAsync(async () =>
        {
            await service.CambiarEstadoAsync(input, User.Identity?.Name ?? "", ct);
            return Json(new { mensaje = input.Activo ? "Conversión activada correctamente." : "Conversión desactivada correctamente." });
        });
    }

    [HttpGet("Historial")]
    public Task<IActionResult> Historial(int id, CancellationToken ct)
        => EjecutarAsync(async () => Json(await service.HistorialAsync(id, ct)));

    private IActionResult DatosInvalidos() => BadRequest(new { mensaje = "Revisa los datos de la conversión.", errores = ModelState.Values
        .SelectMany(v => v.Errors).Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Hay un campo con formato inválido." : e.ErrorMessage) });

    private async Task<IActionResult> EjecutarAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (SkuConversionValidacionException ex) { return BadRequest(new { mensaje = ex.Message }); }
        catch (SkuConversionConflictoException ex) { return Conflict(new { mensaje = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { mensaje = ex.Message }); }
        catch (InvalidOperationException ex) { logger.LogWarning(ex, "Configuración no disponible para Sku Conversión."); return StatusCode(503, new { mensaje = "El catálogo requiere una CadenaSQLSIGO válida y su tabla de historial en la base configurada." }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception ex) { logger.LogError(ex, "Error en el catálogo Sku Conversión del Planeador."); return StatusCode(503, new { mensaje = "No se pudo completar la operación en la base configurada en CadenaSQLSIGO. Intenta nuevamente; si continúa, revisa la conexión y la instalación del historial." }); }
    }
}
