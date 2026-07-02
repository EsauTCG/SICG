using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using System.Data;
using System.Text;

namespace Plataforma_CG.Controllers
{
    //https://api.callmebot.com/whatsapp.php?phone=5213951146202&text=Hola%20prueba%20desde%20navegador&apikey=7739413
    // POST https://localhost:7171/WhatsApp/EnviarReporte?telefono=5213951146202&tipoReporte=CEDIS&canal=CEDIS-MDA&mes=3&anio=2026
    // POST https://localhost:7171/WhatsApp/EnviarReporte?telefono=5213951146202&tipoReporte=VENDEDOR&vendedorId=28&mes=3&anio=2026
    // POST https://localhost:7171/WhatsApp/EnviarTodos?mes=3&anio=2026

    [ApiController]
    [Route("[controller]")]
    public class WhatsAppController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly CallMeBotService _callMeBotService;

        public WhatsAppController(AppDbContext db, CallMeBotService callMeBotService)
        {
            _db = db;
            _callMeBotService = callMeBotService;
        }

        [HttpPost("EnviarTodos")]
        public async Task<IActionResult> EnviarTodos(
    [FromQuery] int mes,
    [FromQuery] int anio,
    [FromServices] WhatsAppReporteService service)
        {
            await service.EnviarTodosAsync(mes, anio);
            return Ok(new { ok = true });
        }

        [HttpPost("EnviarReporte")]
        public async Task<IActionResult> EnviarReporte(
            [FromQuery] string telefono,
            [FromQuery] string tipoReporte,
            [FromQuery] int mes,
            [FromQuery] int anio,
            [FromQuery] string? canal = null,
            [FromQuery] int? vendedorId = null)
        {
            string encabezado = "";
            decimal objetivo = 0;
            decimal vendido = 0;
            decimal avance = 0;
            var skus = new List<string>();

            await _db.Database.OpenConnectionAsync();
            try
            {
                var conn = _db.Database.GetDbConnection();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "dbo.sp_WhatsAppReporte";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 180;

                cmd.Parameters.Add(new SqlParameter("@TipoReporte", tipoReporte));
                cmd.Parameters.Add(new SqlParameter("@Canal", (object?)canal ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@VendedorId", (object?)vendedorId ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@Mes", mes));
                cmd.Parameters.Add(new SqlParameter("@Anio", anio));

                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    if (tipoReporte.Equals("CEDIS", StringComparison.OrdinalIgnoreCase))
                        encabezado = $"Resumen {reader["Canal"]}";
                    else
                        encabezado = $"Resumen {reader["VendedorNombre"]}";

                    objetivo = reader["Objetivo"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Objetivo"]);
                    vendido = reader["Vendido"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Vendido"]);
                    avance = reader["AvancePct"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["AvancePct"]);
                }

                if (await reader.NextResultAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var sku = reader["ProductoCodigo"]?.ToString() ?? "";
                        var producto = reader["ProductoNombre"]?.ToString() ?? "";
                        skus.Add($"- {sku} | {producto}");
                    }
                }
            }
            finally
            {
                await _db.Database.CloseConnectionAsync();
            }

            var mensaje = ConstruirMensaje(encabezado, objetivo, vendido, avance, skus);

            var ok = await _callMeBotService.EnviarPorEstatusAsync(
                telefono,
                $"REPORTE_{tipoReporte}_{canal}_{vendedorId}",
                mensaje);

            return Ok(new
            {
                ok,
                mensaje
            });
        }

        private static string ConstruirMensaje(
            string encabezado,
            decimal objetivo,
            decimal vendido,
            decimal avance,
            List<string> skus)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Buen dia.");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(encabezado))
            {
                sb.AppendLine(encabezado);
                sb.AppendLine();
            }

            sb.AppendLine("Resumen de ventas:");
            sb.AppendLine($"- Objetivo: {objetivo:N2} kg");
            sb.AppendLine($"- Vendido: {vendido:N2} kg");
            sb.AppendLine($"- Avance: {avance:N1}%");

            if (skus.Any())
            {
                sb.AppendLine();
                sb.AppendLine("SKU que estan dejando de vender:");
                foreach (var item in skus.Take(10))
                    sb.AppendLine(item);
            }

            sb.AppendLine();
            sb.AppendLine("Favor de revisar.");

            return sb.ToString();
        }
    }
}
