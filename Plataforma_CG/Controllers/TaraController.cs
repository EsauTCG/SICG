using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Operaciones;
using Plataforma_CG.Models;
using System.Net.Http.Json;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaraController : ControllerBase
    {
        private readonly HttpClient _http;
        private readonly InyeccionAPI _api;

        public TaraController(IHttpClientFactory httpClientFactory, InyeccionAPI api)
        {
            _http = httpClientFactory.CreateClient();
            _api = api;
        }

        [HttpGet("Listar")]
        public async Task<ActionResult<IEnumerable<Tara>>> Listar()
        {
            try
            {
                var url = $"{_api.BaseUrlWrite}Receta/ListarTara";
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

