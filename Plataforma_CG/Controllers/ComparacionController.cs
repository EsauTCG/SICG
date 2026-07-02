using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ComparacionController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ComparacionController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public ComparacionController(IConfiguration config, ILogger<ComparacionController> logger, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] DateTime fechaIn, [FromQuery] DateTime fechaFin)
        {
            try
            {
                // 1. Datos de BD (antes de inyección)
                var sql = @"
                    SELECT LoteId, Lote, SKU, SUM(PesoActual) as PesoAntes
                    FROM Capturas
                    WHERE FechaCaptura BETWEEN @fechaIn AND DATEADD(DAY,1,@fechaFin)
                    GROUP BY LoteId, Lote, SKU";

                var connStr = _config.GetConnectionString("CadenaSQLSIGO");
                using var conn = new SqlConnection(connStr);
                var bdData = (await conn.QueryAsync(sql, new { fechaIn, fechaFin })).ToList();

                // 2. Datos de API externa (después de inyección)
                var http = _httpClientFactory.CreateClient();
                var url = $"http://10.1.1.2:252/Reporte/Consultar?fechaIn={fechaIn:yyyyMMdd}&fechaFin={fechaFin:yyyyMMdd}";
                var response = await http.GetStringAsync(url);

                var apiData = JsonSerializer.Deserialize<List<ApiRegistro>>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // 3. Agrupar API
                var apiAgrupado = apiData
                    .GroupBy(x => new { x.LoteId, x.Lote, x.SKU })
                    .Select(g => new
                    {
                        g.Key.LoteId,
                        g.Key.Lote,
                        g.Key.SKU,
                        Producto = g.First().Producto,
                        Porcentaje = g.First().Porcentaje,
                        PesoDespues = g.Sum(x => x.Peso)
                    })
                    .ToList();

                // 4. Comparar
                var resultados = new List<object>();
                foreach (var api in apiAgrupado)
                {
                    var bd = bdData.FirstOrDefault(b =>
                        b.LoteId == api.LoteId &&
                        b.Lote == api.Lote &&
                        b.SKU == api.SKU
                    );

                    if (bd != null)
                    {
                        decimal esperado = bd.PesoAntes * (1 + (decimal)api.Porcentaje / 100m);
                        decimal real = (decimal)api.PesoDespues;
                        decimal diff = (real - esperado) / esperado;
                        decimal margen = 0.020m; // 2.1%

                        resultados.Add(new
                        {
                            api.LoteId,
                            api.Lote,
                            api.SKU,
                            api.Producto,
                            api.Porcentaje,
                            PesoAntes = bd.PesoAntes,
                            PesoDespues = real,
                            Esperado = esperado,
                            Diferencia = $"{diff:P1}",
                            //Estado = Math.Abs(diff) <= margen ? "OK" : "FAIL"
                        });
                    }
                }

                return Ok(resultados);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en comparación");
                return StatusCode(500, new { error = "Error en comparación", message = ex.Message });
            }
        }

        // Clase auxiliar para el API externo
        public class ApiRegistro
        {
            public int LoteId { get; set; }
            public string Lote { get; set; }
            public string SKU { get; set; }
            public string Producto { get; set; }
            public int Porcentaje { get; set; }
            public decimal Peso { get; set; }
        }
    }
}
