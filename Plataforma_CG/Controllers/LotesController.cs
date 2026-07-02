using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LotesController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public LotesController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("Listar")]
        public async Task<IActionResult> Listar()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync("http://10.1.1.2:252/Lote/ListarLotePlaneacion");

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, "Error al obtener lotes");
                }

                var json = await response.Content.ReadAsStringAsync();

                var lotes = JsonSerializer.Deserialize<object>(json);

                return Ok(lotes);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al consumir servicio externo", message = ex.Message });
            }
        }
    }
}

