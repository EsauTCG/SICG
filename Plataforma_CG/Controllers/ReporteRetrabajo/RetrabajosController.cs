using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.Services.ReporteRetrabajo;


namespace Plataforma_CG.Controllers.ReporteRetrabajo
{
    public class RetrabajosController : Controller
    {
        private readonly RetrabajosService _service;

        public RetrabajosController(RetrabajosService service)
        {
            _service = service;
        }

        [HttpGet]
        public IActionResult DemoVista()
        {
            return View(
                "~/Views/ReporteRetrabajo/DemoVista.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> ConsultarResumen(
            DateTime fechaInicio,
            DateTime fechaFin,
            string? skuEntrada = null)
        {
            if (fechaInicio == default ||
                fechaFin == default ||
                fechaFin < fechaInicio)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Rango de fechas inválido."
                });
            }

            var datos = await _service.ConsultarResumenAsync(
                fechaInicio,
                fechaFin,
                skuEntrada);

            return Json(new
            {
                success = true,
                data = datos
            });
        }

        [HttpGet]
        public async Task<IActionResult> ConsultarDetalle(
            DateTime fechaInicio,
            DateTime fechaFin,
            string? skuEntrada,
            int solicitudId,
            string tipoMovimiento,
            string sku,
            DateTime? fechaSalida = null)
        {

            if (fechaInicio == default ||
                fechaFin == default ||
                fechaFin < fechaInicio)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Rango de fechas inválido."
                });
            }

            var datos = await _service.ConsultarDetalleAsync(
                fechaInicio,
                fechaFin,
                skuEntrada,
                solicitudId,
                tipoMovimiento,
                sku,
                fechaSalida);

            return Json(new
            {
                success = true,
                data = datos
            });
        }

        [HttpGet]
        public async Task<IActionResult> ConsultarIndicadoresSalida(
            DateTime fechaInicio,
            DateTime fechaFin,
            string? skuEntrada = null)
        {
            if (fechaInicio == default ||
                fechaFin == default ||
                fechaFin < fechaInicio)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Rango de fechas invalido."
                });
            }

            var datos = await _service.ConsultarIndicadoresSalidaAsync(
                fechaInicio,
                fechaFin,
                skuEntrada);

            return Json(new
            {
                success = true,
                data = datos
            });
        }


        [HttpGet]
        public async Task<IActionResult> ConsultarEntradasAsociadas(
            DateTime fechaInicio,
            DateTime fechaFin,
            string? skuEntrada = null)
        {
            if (fechaInicio == default ||
                fechaFin == default ||
                fechaFin < fechaInicio)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Rango de fechas inválido."
                });
            }

            var datos = await _service.ConsultarEntradasAsociadasAsync(
                fechaInicio,
                fechaFin,
                skuEntrada);

            return Json(new
            {
                success = true,
                data = datos
            });
        }

        [HttpGet]
        public async Task<IActionResult> ConsultarReporteVisceras(
            DateTime fechaInicio,
            DateTime fechaFin,
            string? skuEntrada = null)
        {
            if (fechaInicio == default ||
        fechaFin == default ||
        fechaFin < fechaInicio ||
        (fechaFin.Date - fechaInicio.Date).TotalDays > 366)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Rango de fechas inválido."
                });
            }

            var resultado = await _service.ConsultarReporteViscerasAsync(
                fechaInicio,
                fechaFin,
                skuEntrada
            );

            return Json(new
            {
                success = true,
                data = resultado
            });
        }



    }
}
