using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Operaciones;
using System.Net.Http;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers.Operaciones.Inyeccion
{
    [ApiController]
    [Route("api/Reportes")]
    public class ReportesController : Controller
    {
        private readonly HttpClient _http;
        private readonly InyeccionAPI _api;

        public ReportesController(IHttpClientFactory factory, InyeccionAPI api)
        {
            _http = factory.CreateClient();
            _api = api;
        }

        [HttpGet("RendimientoFecha")]
        public async Task<IActionResult> ObtenerRendimientoPorFecha(DateTime? fechain,DateTime? fechafin)
        {

            var url = $"{_api.BaseUrl}Reporte/RendimientoFecha" +
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

            var url = $"{_api.BaseUrl}Reporte/Consultar" +
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
            var url = $"{_api.BaseUrl}Reporte/RendimientoActual?lote={lote}";

            var resp = await _http.GetAsync(url);

            if (!resp.IsSuccessStatusCode)
                return StatusCode(500, "Error consultando API Rendimiento Actual");

            var json = await resp.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }


    }


}
