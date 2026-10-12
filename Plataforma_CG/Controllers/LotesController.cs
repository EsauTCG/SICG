using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Operaciones;
using System.Net.Http;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LotesController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly InyeccionAPI _api;

        public LotesController(IHttpClientFactory httpClientFactory, InyeccionAPI api)
        {
            _httpClientFactory = httpClientFactory;
            _api = api;
        }

        [HttpGet("Listar")]
        public async Task<IActionResult> Listar()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync($"{_api.BaseUrl}Lote/ListarLotePlaneacion");

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

