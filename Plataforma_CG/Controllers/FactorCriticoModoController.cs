using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Services.FactorCritico;

namespace Plataforma_CG.Controllers;

[Authorize]
[Route("tif/modo-peso")]
public sealed class FactorCriticoModoController : Controller
{
    private readonly FactorCriticoModoService _modoService;
    private readonly ILogger<FactorCriticoModoController> _logger;

    public FactorCriticoModoController(
        FactorCriticoModoService modoService,
        ILogger<FactorCriticoModoController> logger)
    {
        _modoService = modoService;
        _logger = logger;
    }

    public sealed record CambiarModoRequest(
        int? PruebaId,
        string? Lote,
        string? Factor,
        string? ModoOrigen,
        string? ModoDestino,
        string? UsuarioAutorizador,
        string? Clave);

    [HttpPost("cambiar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarModo(
        [FromBody] CambiarModoRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Factor) || string.IsNullOrWhiteSpace(request.ModoDestino))
            return BadRequest(new { success = false, message = "La solicitud de cambio de modo está incompleta." });

        try
        {
            var solicitud = new CambioModoPesoSolicitud(
                request.PruebaId,
                request.Lote ?? string.Empty,
                request.Factor,
                request.ModoOrigen,
                request.ModoDestino,
                request.UsuarioAutorizador,
                request.Clave,
                User.Identity?.Name ?? "Sistema",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            var resultado = await _modoService.CambiarModoAsync(solicitud, cancellationToken);

            return StatusCode(resultado.CodigoHttp, new
            {
                success = resultado.Exito,
                message = resultado.Mensaje,
                autorizadoPor = resultado.AutorizadoPor,
                bitacoraId = resultado.BitacoraId,
                modo = request.ModoDestino
            });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "No fue posible validar o registrar el modo de peso de Factor Crítico.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                message = "No fue posible consultar las autorizaciones de Factor Crítico. Verifica la conexión y que la migración esté instalada."
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al cambiar el modo de peso de Factor Crítico.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Ocurrió un error al cambiar el modo de captura."
            });
        }
    }
}
