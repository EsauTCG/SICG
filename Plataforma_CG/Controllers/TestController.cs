using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;

namespace Plataforma_CG.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestProduccionController : ControllerBase
    {
        private readonly IConfiguration _config;

        public TestProduccionController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet("Preview")]
        public async Task<IActionResult> GetPreview()
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("CadenaSQLTIF"));


                var sql = @"
                SELECT *
                FROM Produccion
                WHERE CAST(FechaProduccion AS DATE) = CAST(GETDATE() AS DATE)
                ORDER BY FechaProduccion DESC";


                var rows = await conn.QueryAsync(sql);

                return Ok(rows); // JSON con los últimos 10 registros
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "No se pudo leer Produccion", message = ex.Message });
            }
        }

    }
}
