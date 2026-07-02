// =====================
// OPCIÓN 1: AGREGAR LA RUTA QUE ESPERA EL JAVASCRIPT
// =====================

using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecetasController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public RecetasController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        // ===========================
        // NUEVA RUTA: /api/recetas/productos/{planId}
        // Para coincidir con el JavaScript
        // ===========================
        [HttpGet("productos/{planId:int}")]
        public async Task<IActionResult> ObtenerProductosPorPlan(int planId)
        {
            try
            {
                // Validar planId
                if (planId <= 0)
                {
                    return BadRequest(new { error = "Plan ID debe ser mayor a 0", planId });
                }

                var client = _httpClientFactory.CreateClient();
                var url = $"http://10.1.1.2:252/Receta/ListarPlantilla?busq=&plan={planId}";

                Console.WriteLine($"🔄 Llamando a API externa: {url}");

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Error API externa: {response.StatusCode}");
                    return StatusCode((int)response.StatusCode, new
                    {
                        error = "Error al obtener productos del servidor externo",
                        status = response.StatusCode,
                        planId
                    });
                }

                var json = await response.Content.ReadAsStringAsync();

                // Validar que hay contenido
                if (string.IsNullOrWhiteSpace(json) || json == "[]" || json == "{}")
                {
                    Console.WriteLine($"⚠️ Sin productos para plan {planId}");
                    return Ok(new object[0]); // Array vacío
                }

                var productos = JsonSerializer.Deserialize<object>(json);

                Console.WriteLine($"✅ Productos obtenidos para plan {planId}");
                return Ok(productos);
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"❌ Error de red: {httpEx.Message}");
                return StatusCode(503, new
                {
                    error = "Servidor de productos no disponible",
                    message = httpEx.Message,
                    planId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error general: {ex.Message}");
                return StatusCode(500, new
                {
                    error = "Error al consumir API de productos",
                    message = ex.Message,
                    planId
                });
            }
        }

        // ===========================
        // RUTA EXISTENTE - MANTENIDA PARA COMPATIBILIDAD
        // ===========================
        [HttpGet("ListarProductos")]
        public async Task<IActionResult> ListarProductos([FromQuery] int plan)
        {
            // Redirigir a la nueva función
            return await ObtenerProductosPorPlan(plan);
        }

        // ===========================
        // NUEVA RUTA: Listar todas las recetas (para método alternativo)
        // ===========================
        [HttpGet("Listar")]
        public async Task<IActionResult> ListarRecetas()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = "http://10.1.1.2:252/Receta/Listar"; // Ajusta la URL según tu API

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, new
                    {
                        error = "Error al obtener lista de recetas"
                    });
                }

                var json = await response.Content.ReadAsStringAsync();
                var recetas = JsonSerializer.Deserialize<object>(json);

                return Ok(recetas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Error al consumir API de recetas",
                    message = ex.Message
                });
            }
        }

        // ===========================
        // Consultar receta por SKU - EXISTENTE
        // ===========================
        [HttpGet("ConsultarReceta")]
        public async Task<IActionResult> ConsultarReceta([FromQuery] string sku)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sku))
                {
                    return BadRequest(new { error = "SKU es requerido" });
                }

                var client = _httpClientFactory.CreateClient();
                var url = $"http://10.1.1.2:252/Receta/ConsultarReceta?sku={sku}";

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, new
                    {
                        error = "Error al obtener receta",
                        sku
                    });
                }

                var json = await response.Content.ReadAsStringAsync();
                var receta = JsonSerializer.Deserialize<object>(json);

                return Ok(receta);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Error al consumir API de receta",
                    message = ex.Message,
                    sku
                });
            }
        }

        // ===========================
        // ENDPOINT DE PRUEBA - Para debugging
        // ===========================
        [HttpGet("test/{planId:int}")]
        public IActionResult TestPlan(int planId)
        {
            return Ok(new
            {
                message = "Endpoint funcionando",
                planId,
                timestamp = DateTime.Now
            });
        }
    }
}