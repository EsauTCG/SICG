using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Data;
using System.Text;

public class WhatsAppReporteService
{
    private readonly AppDbContext _db;
    private readonly CallMeBotService _callMeBotService;

    public WhatsAppReporteService(AppDbContext db, CallMeBotService callMeBotService)
    {
        _db = db;
        _callMeBotService = callMeBotService;
    }

    public async Task EnviarReportesProgramadosAsync(int mes, int anio, CancellationToken cancellationToken = default)
    {
        var ahora = DateTime.Now;
        var diaActual = ObtenerDiaSemana(ahora.DayOfWeek);
        var horaActual = new TimeSpan(ahora.Hour, ahora.Minute, 0);

        Console.WriteLine($"[WA] Ahora: {ahora:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"[WA] Día actual: {diaActual}");
        Console.WriteLine($"[WA] Hora actual: {horaActual}");

        var candidatos = await _db.WhatsAppDestino
            .AsNoTracking()
            .Where(x => x.Activo)
            .Where(x => x.DiaEnvio == null || x.DiaEnvio == diaActual)
            .ToListAsync(cancellationToken);

        Console.WriteLine($"[WA] Destinos activos del día: {candidatos.Count}");

        foreach (var destino in candidatos)
        {
            try
            {
                Console.WriteLine($"[WA] Revisando destino {destino.Id} - {destino.Nombre} - Hora BD: {destino.HoraEnvio}");

                if (destino.HoraEnvio.HasValue)
                {
                    var horaDestino = new TimeSpan(destino.HoraEnvio.Value.Hours, destino.HoraEnvio.Value.Minutes, 0);
                    var difMin = Math.Abs((horaDestino - horaActual).TotalMinutes);

                    Console.WriteLine($"[WA] Diferencia minutos: {difMin}");

                    if (difMin > 1)
                        continue;
                }

                var mensaje = await GenerarMensajeAsync(destino, mes, anio);
                if (string.IsNullOrWhiteSpace(mensaje))
                {
                    Console.WriteLine($"[WA] Mensaje vacío para {destino.Nombre}");
                    continue;
                }

                var estatus = destino.TipoDestino.Equals("CEDIS", StringComparison.OrdinalIgnoreCase)
                    ? $"REPORTE_CEDIS_{destino.Canal}"
                    : $"REPORTE_VENDEDOR_{destino.VendedorId}";

                var ok = await _callMeBotService.EnviarPorEstatusAsync(
                    destino.Telefono,
                    estatus,
                    mensaje,
                    cancellationToken);

                Console.WriteLine($"[WA] Resultado envío {destino.Nombre}: {ok}");

                await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WA] Error enviando reporte a {destino.Nombre}: {ex}");
            }
        }
    }

    public async Task EnviarTodosAsync(int mes, int anio, CancellationToken cancellationToken = default)
    {
        var destinos = await _db.WhatsAppDestino
            .AsNoTracking()
            .Where(x => x.Activo)
            .ToListAsync(cancellationToken);

        foreach (var destino in destinos)
        {
            try
            {
                var mensaje = await GenerarMensajeAsync(destino, mes, anio);
                if (string.IsNullOrWhiteSpace(mensaje))
                    continue;

                var estatus = destino.TipoDestino.Equals("CEDIS", StringComparison.OrdinalIgnoreCase)
                    ? $"REPORTE_CEDIS_{destino.Canal}"
                    : $"REPORTE_VENDEDOR_{destino.VendedorId}";

                await _callMeBotService.EnviarPorEstatusAsync(
                    destino.Telefono,
                    estatus,
                    mensaje);

                await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enviando reporte a {destino.Nombre}: {ex.Message}");
            }
        }
    }


    private async Task<string> GenerarMensajeAsync(WhatsAppDestino destino, int mes, int anio)
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

            cmd.Parameters.Add(new SqlParameter("@TipoReporte", destino.TipoDestino));
            cmd.Parameters.Add(new SqlParameter("@Canal", (object?)destino.Canal ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@VendedorId", (object?)destino.VendedorId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Mes", mes));
            cmd.Parameters.Add(new SqlParameter("@Anio", anio));

            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                if (destino.TipoDestino.Equals("CEDIS", StringComparison.OrdinalIgnoreCase))
                    encabezado = $"Resumen {reader["Canal"]}";
                else
                    encabezado = $"Resumen {reader["VendedorNombre"]}";

                objetivo = reader["Objetivo"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Objetivo"]);
                vendido = reader["Vendido"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Vendido"]);
                avance = reader["AvancePct"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["AvancePct"]);
            }
            else
            {
                return string.Empty;
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    var sku = reader["ProductoCodigo"]?.ToString() ?? "";
                    var producto = reader["ProductoNombre"]?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(sku))
                        skus.Add($"- {sku} | {producto}");
                }
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }

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

    private static string ObtenerDiaSemana(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => "LUNES",
            DayOfWeek.Tuesday => "MARTES",
            DayOfWeek.Wednesday => "MIERCOLES",
            DayOfWeek.Thursday => "JUEVES",
            DayOfWeek.Friday => "VIERNES",
            DayOfWeek.Saturday => "SABADO",
            _ => "DOMINGO"
        };
    }
}
