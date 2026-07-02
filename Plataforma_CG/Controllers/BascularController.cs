using Microsoft.AspNetCore.Mvc;
using System.Net.Sockets;
using System.Text;

namespace Plataforma_CG.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BascularController : ControllerBase
    {
        private readonly ILogger<BascularController> _logger;

        public BascularController(ILogger<BascularController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Lee el peso actual de la báscula vía TCP/IP
        /// </summary>
        [HttpGet("LeerPeso")]
        public async Task<IActionResult> LeerPeso([FromQuery] string ip, [FromQuery] int puerto = 5000)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                return BadRequest(new { error = "IP de báscula no proporcionada" });
            }

            try
            {
                _logger.LogInformation($"Intentando conectar con báscula: {ip}:{puerto}");

                using var client = new TcpClient();

                // Timeout de 5 segundos para conexión
                var connectTask = client.ConnectAsync(ip, puerto);
                if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                {
                    return StatusCode(408, new { error = "Timeout al conectar con la báscula" });
                }

                await connectTask; // Asegurar que la tarea se complete

                using var stream = client.GetStream();
                stream.ReadTimeout = 3000; // 3 segundos para leer
                stream.WriteTimeout = 3000;

                // Comando para solicitar peso (puede variar según el fabricante)
                // Comandos comunes: "W\r\n", "P\r\n", "READ\r\n"
                byte[] comandoBytes = Encoding.ASCII.GetBytes("W\r\n");
                await stream.WriteAsync(comandoBytes, 0, comandoBytes.Length);

                // Leer respuesta
                byte[] buffer = new byte[256];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                {
                    return StatusCode(500, new { error = "No se recibió respuesta de la báscula" });
                }

                string respuesta = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                _logger.LogInformation($"Respuesta de báscula: {respuesta}");

                // Parsear el peso de la respuesta
                decimal peso = ParsearPesoDeRespuesta(respuesta);

                return Ok(new
                {
                    peso = peso,
                    respuestaCompleta = respuesta,
                    timestamp = DateTime.Now
                });
            }
            catch (SocketException ex)
            {
                _logger.LogError(ex, $"Error de socket al conectar con {ip}:{puerto}");
                return StatusCode(503, new { error = "No se pudo conectar con la báscula", detalle = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general al leer peso");
                return StatusCode(500, new { error = "Error al leer peso", detalle = ex.Message });
            }
        }

        /// <summary>
        /// Parsea el peso de la respuesta de la báscula
        /// Ajustar según el formato de tu báscula específica
        /// </summary>
        private decimal ParsearPesoDeRespuesta(string respuesta)
        {
            try
            {
                // Formato común: "ST,GS,    12.50 kg"
                // O: "12.50"
                // O: "W 12.50 kg"

                // Eliminar caracteres no deseados
                respuesta = respuesta.Replace("kg", "")
                                   .Replace("lb", "")
                                   .Replace("ST", "")
                                   .Replace("GS", "")
                                   .Replace("W", "")
                                   .Replace(",", "")
                                   .Trim();

                // Buscar el primer número decimal
                var match = System.Text.RegularExpressions.Regex.Match(respuesta, @"-?\d+\.?\d*");

                if (match.Success && decimal.TryParse(match.Value, out decimal peso))
                {
                    return peso;
                }

                // Si no se puede parsear, intentar conversión directa
                if (decimal.TryParse(respuesta, out peso))
                {
                    return peso;
                }

                _logger.LogWarning($"No se pudo parsear peso de: '{respuesta}'");
                return 0m;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error parseando peso de: '{respuesta}'");
                return 0m;
            }
        }

        /// <summary>
        /// Prueba la conectividad con la báscula
        /// </summary>
        [HttpGet("Probar")]
        public async Task<IActionResult> ProbarConexion([FromQuery] string ip, [FromQuery] int puerto = 9761)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, puerto);

                if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                {
                    return Ok(new { conectada = false, mensaje = "Timeout" });
                }

                await connectTask;
                return Ok(new { conectada = true, mensaje = "Conexión exitosa" });
            }
            catch (Exception ex)
            {
                return Ok(new { conectada = false, mensaje = ex.Message });
            }
        }
    }
}