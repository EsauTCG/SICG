using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Services.AutorizacionesManuales;

namespace Plataforma_CG.Controllers;

[Authorize(Roles = "Administrador,Sistemas")]
[Route("AutorizacionesManuales")]
public sealed class AutorizacionesManualesController : Controller
{
    private readonly AutorizacionesManualesService _service;
    private readonly ILogger<AutorizacionesManualesController> _logger;

    public AutorizacionesManualesController(
        AutorizacionesManualesService service,
        ILogger<AutorizacionesManualesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    public sealed record CrearAutorizadorRequest(
        string? Nombre,
        string? Usuario,
        string? Clave);

    public sealed record ActualizarAutorizadorRequest(
        string? Nombre,
        string? Usuario,
        string? NuevaClave,
        bool Activo);

    [HttpGet("")]
    public IActionResult Index() =>
        View("~/Views/AutorizacionesManuales/Index.cshtml");

    [HttpGet("lista")]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        try
        {
            var autorizadores = await _service.ListarAsync(cancellationToken);
            return Ok(new { success = true, autorizadores });
        }
        catch (Exception ex) when (EsErrorDeDatos(ex))
        {
            return ErrorDeDatos(ex, "consultar");
        }
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(
        [FromBody] CrearAutorizadorRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { success = false, message = "La solicitud está incompleta." });

        try
        {
            var resultado = await _service.CrearAsync(
                request.Nombre ?? string.Empty,
                request.Usuario ?? string.Empty,
                request.Clave ?? string.Empty,
                cancellationToken);

            return Responder(resultado);
        }
        catch (Exception ex) when (EsErrorDeDatos(ex))
        {
            return ErrorDeDatos(ex, "crear");
        }
    }

    [HttpPut("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Actualizar(
        int id,
        [FromBody] ActualizarAutorizadorRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { success = false, message = "La solicitud está incompleta." });

        try
        {
            var resultado = await _service.ActualizarAsync(
                id,
                request.Nombre ?? string.Empty,
                request.Usuario ?? string.Empty,
                request.NuevaClave,
                request.Activo,
                cancellationToken);

            return Responder(resultado);
        }
        catch (Exception ex) when (EsErrorDeDatos(ex))
        {
            return ErrorDeDatos(ex, "actualizar");
        }
    }

    [HttpPost("{id:int}/desbloquear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Desbloquear(
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            return Responder(await _service.DesbloquearAsync(id, cancellationToken));
        }
        catch (Exception ex) when (EsErrorDeDatos(ex))
        {
            return ErrorDeDatos(ex, "desbloquear");
        }
    }

    [HttpDelete("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            return Responder(await _service.EliminarAsync(id, cancellationToken));
        }
        catch (Exception ex) when (EsErrorDeDatos(ex))
        {
            return ErrorDeDatos(ex, "eliminar");
        }
    }

    private IActionResult Responder(AutorizadorManualResultado resultado) =>
        StatusCode(resultado.CodigoHttp, new
        {
            success = resultado.Exito,
            message = resultado.Mensaje,
            autorizador = resultado.Autorizador
        });

    private IActionResult ErrorDeDatos(Exception exception, string operacion)
    {
        _logger.LogError(
            exception,
            "No fue posible {Operacion} autorizadores manuales.",
            operacion);

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            success = false,
            message = "No fue posible comunicarse con la base de autorizaciones. Intenta de nuevo o revisa la configuración."
        });
    }

    private static bool EsErrorDeDatos(Exception exception) =>
        exception is SqlException or InvalidOperationException;
}
