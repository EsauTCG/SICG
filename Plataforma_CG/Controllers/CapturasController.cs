using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using Plataforma_CG.Models;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CapturasController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<CapturasController> _logger;

        public CapturasController(IConfiguration config, ILogger<CapturasController> logger)
        {
            _config = config;
            _logger = logger;
        }

        //POST
        [HttpPost]
        public async Task<IActionResult> PostRaw()
        {
            string rawJson;
            using (var sr = new StreamReader(Request.Body))
            {
                rawJson = await sr.ReadToEndAsync();
            }

            _logger.LogInformation("Raw JSON recibido: {raw}", rawJson);

            Captura? captura;
            try
            {
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                captura = JsonSerializer.Deserialize<Captura>(rawJson, opts);
                if (captura == null)
                    return BadRequest(new { error = "Deserialización devolvió null", raw = rawJson });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al deserializar JSON");
                return BadRequest(new { error = "Error de deserialización", message = ex.Message, raw = rawJson });
            }

            // Capturar el correo del usuario logueado
            captura.UsuarioCorreo = User.Identity?.Name;

            // ⭐ VALIDACIÓN: Si no viene ModoCaptura, usar "Automático" por defecto
            if (string.IsNullOrWhiteSpace(captura.ModoCaptura))
            {
                captura.ModoCaptura = "Automático";
                _logger.LogWarning("ModoCaptura no especificado, usando valor por defecto: Automático");
            }

            // Validar que solo sea "Manual" o "Automático"
            if (captura.ModoCaptura != "Manual" && captura.ModoCaptura != "Automático")
            {
                _logger.LogWarning("ModoCaptura inválido recibido: {modo}, usando Automático", captura.ModoCaptura);
                captura.ModoCaptura = "Automático";
            }

            var sql = @"
                INSERT INTO Capturas (
                    LoteId, Lote, Producto, ProductoSeleccionado, Programacion, SKU, Porcentaje, Velocidad, 
                    Modo, Presion, Altura, Avance, Tara, IpBascula, ComandoBascula, IpImpresora,
                    PesoActual, PorcentajeActual, VelocidadActual, ModoCaptura, FechaCaptura, UsuarioCorreo
                )
                VALUES (
                    @LoteId, @Lote, @Producto, @ProductoSeleccionado, @Programacion, @SKU, @Porcentaje, @Velocidad,
                    @Modo, @Presion, @Altura, @Avance, @Tara, @IpBascula, @ComandoBascula, @IpImpresora,
                    @PesoActual, @PorcentajeActual, @VelocidadActual, @ModoCaptura, GETDATE(), @UsuarioCorreo
                );
                SELECT CAST(SCOPE_IDENTITY() as int);
            ";

            try
            {
                var connStr = _config.GetConnectionString("CadenaSQLSIGO");
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var id = await conn.ExecuteScalarAsync<int>(sql, captura);
                captura.IdCaptura = id;

                _logger.LogInformation("Captura guardada exitosamente con ID: {id}, Modo: {modo}",
                    id, captura.ModoCaptura);

                return CreatedAtAction(nameof(GetById), new { id = id }, new
                {
                    id = id,
                    modoCaptura = captura.ModoCaptura
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al insertar en DB");
                return StatusCode(500, new { error = "Error DB", message = ex.Message });
            }
        }


        // GET api/capturas/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var sql = "SELECT * FROM Capturas WHERE IdCaptura = @Id";
            try
            {
                var connStr = _config.GetConnectionString("CadenaSQLSIGO");
                using var conn = new SqlConnection(connStr);
                var item = await conn.QuerySingleOrDefaultAsync<Captura>(sql, new { Id = id });
                if (item == null) return NotFound();
                return Ok(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error GetById");
                return StatusCode(500, new { error = "Error DB", message = ex.Message });
            }
        }

        // GET api/capturas
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var sql = "SELECT TOP (200) * FROM Capturas ORDER BY FechaCaptura DESC";
            try
            {
                var connStr = _config.GetConnectionString("CadenaSQLSIGO");
                using var conn = new SqlConnection(connStr);
                var items = await conn.QueryAsync<Captura>(sql);
                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error GetAll");
                return StatusCode(500, new { error = "Error DB", message = ex.Message });
            }
        }

        // ⭐ NUEVO ENDPOINT: Obtener estadísticas por modo
        [HttpGet("estadisticas-modo")]
        public async Task<IActionResult> GetEstadisticasPorModo(
            [FromQuery] DateTime? fechaInicio = null,
            [FromQuery] DateTime? fechaFin = null)
        {
            var sql = @"
                SELECT 
                    ModoCaptura,
                    COUNT(*) as TotalCapturas,
                    AVG(CAST(PesoActual AS FLOAT)) as PesoPromedio,
                    MIN(FechaCaptura) as PrimeraCaptura,
                    MAX(FechaCaptura) as UltimaCaptura
                FROM Capturas
                WHERE (@FechaInicio IS NULL OR FechaCaptura >= @FechaInicio)
                  AND (@FechaFin IS NULL OR FechaCaptura <= @FechaFin)
                GROUP BY ModoCaptura
                ORDER BY ModoCaptura";

            try
            {
                var connStr = _config.GetConnectionString("CadenaSQLSIGO");
                using var conn = new SqlConnection(connStr);

                var stats = await conn.QueryAsync(sql, new
                {
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin
                });

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error GetEstadisticasPorModo");
                return StatusCode(500, new { error = "Error DB", message = ex.Message });
            }
        }

        // ⭐ NUEVO ENDPOINT: Filtrar por modo
        [HttpGet("por-modo/{modo}")]
        public async Task<IActionResult> GetByModo(string modo, [FromQuery] int top = 100)
        {
            // Validar modo
            if (modo != "Manual" && modo != "Automatico" && modo != "Automático")
            {
                return BadRequest(new { error = "Modo inválido. Use 'Manual' o 'Automatico'" });
            }

            var sql = @"
                SELECT TOP (@Top) * 
                FROM Capturas 
                WHERE ModoCaptura = @Modo 
                ORDER BY FechaCaptura DESC";

            try
            {
                var connStr = _config.GetConnectionString("CadenaSQLSIGO");
                using var conn = new SqlConnection(connStr);

                var items = await conn.QueryAsync<Captura>(sql, new
                {
                    Modo = modo == "Automatico" ? "Automático" : modo,
                    Top = top
                });

                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error GetByModo");
                return StatusCode(500, new { error = "Error DB", message = ex.Message });
            }
        }
    }
}