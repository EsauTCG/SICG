using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Plataforma_CG.Filters;
using Plataforma_CG.Services;
using Plataforma_CG.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    public sealed class IntegracionesSapController : Controller
    {
        private readonly IIntegracionSapService _service;
        private readonly IntegracionSapAutomaticoState _automaticoState;
        private readonly ILogger<IntegracionesSapController> _logger;

        public IntegracionesSapController(
            IIntegracionSapService service,
            IntegracionSapAutomaticoState automaticoState,
            ILogger<IntegracionesSapController> logger)
        {
            _service = service;
            _automaticoState = automaticoState;
            _logger = logger;
        }

        // =========================================================
        // VISTA PRINCIPAL
        // GET:
        // /IntegracionesSAP
        // /IntegracionesSAP/integracionsap
        // =========================================================

        [HttpGet]
        [Route("~/IntegracionesSAP")]
        [Route("~/IntegracionesSAP/integracionsap")]
        [RevisarPermiso("INTEGRACION_SAP", "LEER")]
        public async Task<IActionResult> Index(
            string source = "P1",
            string tipo = "ENTRADA",
            DateTime? desde = null,
            DateTime? hasta = null,
            int? estatus = null,
            int? folio = null)
        {
            try
            {
                var hoy = DateTime.Today;

                source = string.IsNullOrWhiteSpace(source)
                    ? "P1"
                    : source.Trim().ToUpperInvariant();

                tipo = string.IsNullOrWhiteSpace(tipo)
                    ? "ENTRADA"
                    : tipo.Trim().ToUpperInvariant();

                // Las transferencias de entrada se registran en NEXT/P1.
                if (tipo == "TRANSFERENCIA_ENTRADA")
                    source = "P1";

                var vm = await _service.ListarAsync(
                    new IntegracionSapFiltroVM
                    {
                        Source = source,
                        Tipo = tipo,
                        Desde = (desde ?? hoy).Date,
                        Hasta = (hasta ?? hoy).Date,
                        Estatus = estatus,
                        Folio = folio,
                        Top = 500
                    });

                return View(
                    "~/Views/IntegracionesSAP/integracionsap.cshtml",
                    vm
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al abrir Integraciones SAP. Source={Source}, Tipo={Tipo}",
                    source,
                    tipo
                );

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "No se pudo cargar la pantalla de integraciones SAP.",
                    error = ex.GetBaseException().Message
                });
            }
        }

        // =========================================================
        // OBTENER JSON
        // GET /IntegracionesSAP/Json
        // =========================================================

        [HttpGet]
        [Route("~/IntegracionesSAP/Json")]
        [RevisarPermiso("INTEGRACION_SAP", "LEER")]
        public async Task<IActionResult> Json(
            [FromQuery] int integracionId,
            [FromQuery] string source = "P1",
            [FromQuery] string tipo = "ENTRADA")
        {
            try
            {
                if (integracionId <= 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = "El identificador de la integración no es válido."
                    });
                }

                source = string.IsNullOrWhiteSpace(source)
                    ? "P1"
                    : source.Trim().ToUpperInvariant();

                tipo = string.IsNullOrWhiteSpace(tipo)
                    ? "ENTRADA"
                    : tipo.Trim().ToUpperInvariant();

                if (tipo == "TRANSFERENCIA_ENTRADA")
                    source = "P1";

                var row = await _service.ObtenerAsync(
                    integracionId,
                    source,
                    tipo
                );

                if (row == null)
                {
                    return NotFound(new
                    {
                        ok = false,
                        msg = "Integración no encontrada."
                    });
                }

                return Content(
                    string.IsNullOrWhiteSpace(row.JsonSap)
                        ? "{}"
                        : row.JsonSap,
                    "application/json"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al obtener JSON de integración {IntegracionId}. Source={Source}, Tipo={Tipo}",
                    integracionId,
                    source,
                    tipo
                );

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "No se pudo obtener el JSON de la integración.",
                    error = ex.GetBaseException().Message
                });
            }
        }

        // =========================================================
        // ESTADO DEL ENVÍO AUTOMÁTICO
        // GET /IntegracionesSAP/AutomaticoEstado
        // =========================================================

        [HttpGet]
        [Route("~/IntegracionesSAP/AutomaticoEstado")]
        [RevisarPermiso("INTEGRACION_SAP", "LEER")]
        public IActionResult AutomaticoEstado()
        {
            return Ok(new
            {
                ok = true,
                estado = _automaticoState.ObtenerEstado()
            });
        }

        // =========================================================
        // ENCENDER / APAGAR EL ENVÍO AUTOMÁTICO
        // POST /IntegracionesSAP/AutomaticoCambiar
        // =========================================================

        [HttpPost]
        [Route("~/IntegracionesSAP/AutomaticoCambiar")]
        [ValidateAntiForgeryToken]
        [RevisarPermiso("INTEGRACION_SAP", "ESCRIBIR")]
        public IActionResult AutomaticoCambiar(
            [FromBody] CambiarAutomaticoSapRequest request)
        {
            if (request == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    msg = "No se recibió el estado solicitado."
                });
            }

            var usuario = User?.Identity?.Name ?? "SISTEMA";

            var estado = _automaticoState.CambiarEstado(
                request.Activo,
                usuario);

            _logger.LogInformation(
                "El usuario {Usuario} cambió el automático SAP a {Activo}.",
                usuario,
                request.Activo);

            return Ok(new
            {
                ok = true,
                msg = request.Activo
                    ? "Envío automático SAP encendido. El worker revisará pendientes inmediatamente."
                    : "Envío automático SAP apagado. No se iniciarán nuevos envíos.",
                estado
            });
        }

        // =========================================================
        // ENVIAR UNA INTEGRACIÓN
        // POST /IntegracionesSAP/Enviar
        // =========================================================

        [HttpPost]
        [Route("~/IntegracionesSAP/Enviar")]
        [ValidateAntiForgeryToken]
        [RevisarPermiso("INTEGRACION_SAP", "ESCRIBIR")]
        public async Task<IActionResult> Enviar(
            [FromBody] EnviarIntegracionSapRequest request)
        {
            try
            {
                if (request == null || request.IntegracionId <= 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = "Integración inválida."
                    });
                }

                request.Source = string.IsNullOrWhiteSpace(request.Source)
                    ? "P1"
                    : request.Source.Trim().ToUpperInvariant();

                request.Tipo = string.IsNullOrWhiteSpace(request.Tipo)
                    ? "ENTRADA"
                    : request.Tipo.Trim().ToUpperInvariant();

                if (request.Tipo == "TRANSFERENCIA_ENTRADA")
                    request.Source = "P1";

                _logger.LogInformation(
                    "Inicio de envío manual SAP. IntegracionId={IntegracionId}, Source={Source}, Tipo={Tipo}, Usuario={Usuario}",
                    request.IntegracionId,
                    request.Source,
                    request.Tipo,
                    User?.Identity?.Name ?? "SISTEMA"
                );

                var result = await _service.EnviarAsync(
                    request.IntegracionId,
                    request.Source,
                    request.Tipo,
                    User?.Identity?.Name ?? "SISTEMA",
                    request.Forzar
                );

                if (!result.Ok)
                {
                    _logger.LogWarning(
                        "Envío manual SAP rechazado. IntegracionId={IntegracionId}, Source={Source}, Tipo={Tipo}, Mensaje={Mensaje}",
                        request.IntegracionId,
                        request.Source,
                        request.Tipo,
                        result.Mensaje
                    );

                    return BadRequest(result);
                }

                _logger.LogInformation(
                    "Envío manual SAP correcto. IntegracionId={IntegracionId}, DocEntry={DocEntry}, DocNum={DocNum}",
                    request.IntegracionId,
                    result.DocEntry,
                    result.DocNum
                );

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error interno durante envío manual SAP. IntegracionId={IntegracionId}",
                    request?.IntegracionId
                );

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error interno al enviar la integración.",
                    error = ex.GetBaseException().Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        // =========================================================
        // ENVIAR VARIAS INTEGRACIONES
        // POST /IntegracionesSAP/EnviarLote
        // =========================================================

        [HttpPost]
        [Route("~/IntegracionesSAP/EnviarLote")]
        [ValidateAntiForgeryToken]
        [RevisarPermiso("INTEGRACION_SAP", "ESCRIBIR")]
        public async Task<IActionResult> EnviarLote(
            [FromBody] EnviarIntegracionesSapRequest request)
        {
            try
            {
                if (request?.IntegracionIds == null ||
                    request.IntegracionIds.Count == 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = "Selecciona al menos una integración."
                    });
                }

                request.Source = string.IsNullOrWhiteSpace(request.Source)
                    ? "P1"
                    : request.Source.Trim().ToUpperInvariant();

                request.Tipo = string.IsNullOrWhiteSpace(request.Tipo)
                    ? "ENTRADA"
                    : request.Tipo.Trim().ToUpperInvariant();

                if (request.Tipo == "TRANSFERENCIA_ENTRADA")
                    request.Source = "P1";

                var idsValidos = request.IntegracionIds
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

                if (idsValidos.Count == 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = "No se recibieron identificadores válidos."
                    });
                }

                var results = await _service.EnviarLoteAsync(
                    idsValidos,
                    request.Source,
                    request.Tipo,
                    User?.Identity?.Name ?? "SISTEMA",
                    request.Forzar
                );

                var fallidos = results
                    .Count(x => !x.Ok && !x.YaEnviado);

                return Ok(new
                {
                    ok = true,
                    total = results.Count,
                    enviados = results.Count(x => x.Ok && !x.YaEnviado),
                    yaEnviados = results.Count(x => x.YaEnviado),
                    fallidos,
                    results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error interno durante envío múltiple SAP. Source={Source}, Tipo={Tipo}",
                    request?.Source,
                    request?.Tipo
                );

                return StatusCode(500, new
                {
                    ok = false,
                    msg = "Error interno al enviar las integraciones seleccionadas.",
                    error = ex.GetBaseException().Message,
                    inner = ex.InnerException?.Message
                });
            }
        }
    }

    public sealed class CambiarAutomaticoSapRequest
    {
        public bool Activo { get; set; }
    }
}