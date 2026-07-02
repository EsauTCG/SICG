using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers.Operaciones.Inyeccion
{
    [ApiController]
    [Route("api/Reportes")]
    public class ReportesController : Controller
    {
        private readonly HttpClient _http;

        public ReportesController(IHttpClientFactory factory)
        {
            _http = factory.CreateClient();
        }

        [HttpGet("RendimientoFecha")]
        public async Task<IActionResult> ObtenerRendimientoPorFecha(DateTime? fechain,DateTime? fechafin)
        {

            var url = $"http://10.1.1.2:252/Reporte/RendimientoFecha" +
                      $"?fechain={fechain:yyyy-MM-dd}&fechafin={fechafin:yyyy-MM-dd}";

            var resp = await _http.GetAsync(url);

            if (!resp.IsSuccessStatusCode)
                return StatusCode(500, "Error consultando API Rendimiento");

            var json = await resp.Content.ReadAsStringAsync();

            return Content(json, "application/json");
        }

        [HttpGet("Detallado")]
        public async Task<IActionResult> ObtenerReporteDetallado(DateTime? fechain,DateTime? fechafin)
        {

            var url = $"http://10.1.1.2:252/Reporte/Consultar" +
                      $"?fechain={fechain:yyyy-MM-dd}&fechafin={fechafin:yyyy-MM-dd}";

            var resp = await _http.GetAsync(url);

            if (!resp.IsSuccessStatusCode)
                return StatusCode(500, "Error API detallado");

            var json = await resp.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }

        [HttpGet("RendimientoActual")]
        public async Task<IActionResult> ObtenerRendimientoActual(long lote)
        {
            var url = $"http://10.1.1.2:252/Reporte/RendimientoActual?lote={lote}";

            var resp = await _http.GetAsync(url);

            if (!resp.IsSuccessStatusCode)
                return StatusCode(500, "Error consultando API Rendimiento Actual");

            var json = await resp.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }


    }


}
