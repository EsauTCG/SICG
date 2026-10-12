using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Services.FactorCritico;

namespace Plataforma_CG.Controllers;

[Authorize]
[Route("tif/historial")]
public sealed class FactorCriticoHistorialController : Controller
{
    private readonly FactorCriticoHistorialService _historial;
    private readonly ILogger<FactorCriticoHistorialController> _logger;

    public FactorCriticoHistorialController(
        FactorCriticoHistorialService historial,
        ILogger<FactorCriticoHistorialController> logger)
    {
        _historial = historial;
        _logger = logger;
    }

    [HttpGet]
    public Task<IActionResult> Listar(
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] string? buscar,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Task.FromResult<IActionResult>(BadRequest("Filtros de fecha inválidos."));

        if (desde > hasta)
            return Task.FromResult<IActionResult>(BadRequest("La fecha inicial debe ser anterior a la final."));

        if (buscar?.Length > 100)
            return Task.FromResult<IActionResult>(BadRequest("La búsqueda no puede superar 100 caracteres."));

        return Consultar(() => _historial.ListarAsync(desde, hasta, buscar, cancellationToken));
    }

    [HttpGet("{id:int}/hueso")]
    public Task<IActionResult> Huesos(int id, CancellationToken cancellationToken) =>
        id <= 0
            ? Task.FromResult<IActionResult>(BadRequest("Prueba inválida."))
            : Consultar(() => _historial.HuesosAsync(id, cancellationToken));

    [HttpGet("{id:int}/recorte")]
    public Task<IActionResult> Recortes(int id, CancellationToken cancellationToken) =>
        id <= 0
            ? Task.FromResult<IActionResult>(BadRequest("Prueba inválida."))
            : Consultar(() => _historial.RecortesAsync(id, cancellationToken));

    [HttpGet("{id:int}/grasa")]
    public Task<IActionResult> Grasas(int id, CancellationToken cancellationToken) =>
        id <= 0
            ? Task.FromResult<IActionResult>(BadRequest("Prueba inválida."))
            : Consultar(() => _historial.GrasasAsync(id, cancellationToken));

    private async Task<IActionResult> Consultar<T>(Func<Task<T>> consulta)
    {
        try
        {
            return Ok(await consulta());
        }
        catch (OperationCanceledException)
        {
            return StatusCode(408, "La consulta del historial excedió el tiempo de espera.");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            _logger.LogError(ex, "No fue posible consultar el historial de Factor Crítico.");
            return StatusCode(503, "No se pudo consultar el historial de Factor Crítico.");
        }
    }
}
