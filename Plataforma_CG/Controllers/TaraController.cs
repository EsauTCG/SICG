using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.Models;
using System.Net.Http.Json;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaraController : ControllerBase
    {
        private readonly HttpClient _http;

        public TaraController(IHttpClientFactory httpClientFactory)
        {
            _http = httpClientFactory.CreateClient();
        }

        [HttpGet("Listar")]
        public async Task<ActionResult<IEnumerable<Tara>>> Listar()
        {
            try
            {
                var url = "http://10.1.1.2:252/Receta/ListarTara";
                var taras = await _http.GetFromJsonAsync<List<Tara>>(url);

                if (taras == null || !taras.Any())
                    return NotFound("No se encontraron taras");

                return Ok(taras);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al obtener taras: {ex.Message}");
            }
        }
    }
}

