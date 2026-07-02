using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.Models;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly HttpClient _httpClient;

        public ProxyController(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
            // Configurar timeout
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        [HttpGet("productos")]
        public async Task<IActionResult> GetProductos()
        {
            var url = "http://10.1.1.2:252/Receta/ListarProducto";

            try
            {
                var response = await _httpClient.GetStringAsync(url);

                // Configurar opciones de deserialización
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var productos = JsonSerializer.Deserialize<List<Producto>>(response, options);

                return Ok(productos);
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(503, new { error = $"Error conectando con API externa: {ex.Message}" });
            }
            catch (JsonException ex)
            {
                return StatusCode(502, new { error = $"Error deserializando respuesta: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error interno: {ex.Message}" });
            }
        }

        // Endpoint adicional para probar conectividad
        [HttpGet("test-connection")]
        public async Task<IActionResult> TestConnection()
        {
            var url = "http://10.1.1.2:252/Receta/ListarProducto";

            try
            {
                var response = await _httpClient.GetAsync(url);
                return Ok(new
                {
                    status = response.StatusCode,
                    message = response.IsSuccessStatusCode ? "Conexión exitosa" : "Error en respuesta",
                    url = url
                });
            }
            catch (Exception ex)
            {
                return Ok(new { status = "Error", message = ex.Message, url = url });
            }
        }
    }
}