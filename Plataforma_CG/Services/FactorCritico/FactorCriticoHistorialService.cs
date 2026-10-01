using Dapper;
using Microsoft.Data.SqlClient;

namespace Plataforma_CG.Services.FactorCritico;

public sealed class FactorCriticoHistorialService
{
    private readonly IConfiguration _configuration;

    public FactorCriticoHistorialService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private SqlConnection CrearConexion()
    {
        var cadena = _configuration.GetConnectionString("FactorCriticoHistorial");
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException("Falta configurar la conexión FactorCriticoHistorial.");

        var opciones = new SqlConnectionStringBuilder(cadena)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly
        };
        return new SqlConnection(opciones.ConnectionString);
    }

    public async Task<IReadOnlyList<PruebaHistorica>> ListarAsync(
        DateOnly? desde, DateOnly? hasta, string? buscar, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.id AS Id,
                   COALESCE(p.Lote, '') AS Lote,
                   CONVERT(char(10), p.Fecha, 23) AS Fecha,
                   CONVERT(char(8), p.HoraInicio, 108) AS Hora,
                   p.Realiza AS Realiza
            FROM dbo.PruebaFactor AS p
            WHERE (@desde IS NULL OR p.Fecha >= @desde)
              AND (@hasta IS NULL OR p.Fecha <= @hasta)
              AND (@buscar IS NULL OR CHARINDEX(@buscar, p.Lote) > 0
                   OR CHARINDEX(@buscar, p.Realiza) > 0)
            ORDER BY p.Fecha DESC, p.HoraInicio DESC, p.id DESC;
            """;

        await using var conexion = CrearConexion();
        var parametros = new
        {
            desde = desde?.ToDateTime(TimeOnly.MinValue),
            hasta = hasta?.ToDateTime(TimeOnly.MinValue),
            buscar = string.IsNullOrWhiteSpace(buscar) ? null : buscar.Trim()
        };
        var comando = new CommandDefinition(sql, parametros, commandTimeout: 30,
            cancellationToken: cancellationToken);
        return (await conexion.QueryAsync<PruebaHistorica>(comando)).AsList();
    }

    public async Task<IReadOnlyList<HuesoHistorico>> HuesosAsync(int pruebaId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT h.id AS Id, h.fk_Hueso AS Fk_Hueso,
                   COALESCE(c.Nombre, 'Hueso sin catálogo') AS NombreHueso,
                   h.PorcObjetivo, h.KgEntrada, h.KgSalida, h.PorcPartic, h.DifPorc
            FROM dbo.PruebaHueso AS h
            INNER JOIN dbo.PruebaFactor AS p ON p.id = h.fk_Prueba
            LEFT JOIN dbo.CatalogHueso AS c ON c.id = h.fk_Hueso
            WHERE p.id = @pruebaId
            ORDER BY h.id;
            """;
        await using var conexion = CrearConexion();
        var comando = new CommandDefinition(sql, new { pruebaId }, commandTimeout: 30,
            cancellationToken: cancellationToken);
        return (await conexion.QueryAsync<HuesoHistorico>(comando)).AsList();
    }

    public async Task<IReadOnlyList<PesoHistorico>> RecortesAsync(int pruebaId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.id AS Id, r.PorcObjetivo, r.KgEntrada, r.KgSalida,
                   r.PorcPartic, r.DifPorc
            FROM dbo.PruebaRecorte AS r
            INNER JOIN dbo.PruebaFactor AS p ON p.id = r.fk_Prueba
            WHERE p.id = @pruebaId
            ORDER BY r.id;
            """;
        await using var conexion = CrearConexion();
        var comando = new CommandDefinition(sql, new { pruebaId }, commandTimeout: 30,
            cancellationToken: cancellationToken);
        return (await conexion.QueryAsync<PesoHistorico>(comando)).AsList();
    }

    public async Task<IReadOnlyList<PesoHistorico>> GrasasAsync(int pruebaId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT g.id AS Id, g.PorcObjetivo, g.KgEntrada, g.KgSalida,
                   g.PorcPartic, g.DifPorc
            FROM dbo.PruebaGrasa AS g
            INNER JOIN dbo.PruebaFactor AS p ON p.id = g.fk_Prueba
            WHERE p.id = @pruebaId
            ORDER BY g.id;
            """;
        await using var conexion = CrearConexion();
        var comando = new CommandDefinition(sql, new { pruebaId }, commandTimeout: 30,
            cancellationToken: cancellationToken);
        return (await conexion.QueryAsync<PesoHistorico>(comando)).AsList();
    }
}

public sealed class PruebaHistorica
{
    public int Id { get; set; }
    public string Lote { get; set; } = "";
    public string? Fecha { get; set; }
    public string? Hora { get; set; }
    public string Realiza { get; set; } = "";
}

public sealed class HuesoHistorico
{
    public int Id { get; set; }
    public int? Fk_Hueso { get; set; }
    public string NombreHueso { get; set; } = "";
    public decimal? PorcObjetivo { get; set; }
    public decimal? KgEntrada { get; set; }
    public decimal? KgSalida { get; set; }
    public decimal? PorcPartic { get; set; }
    public decimal? DifPorc { get; set; }
}

public sealed class PesoHistorico
{
    public int Id { get; set; }
    public decimal? PorcObjetivo { get; set; }
    public decimal? KgEntrada { get; set; }
    public decimal? KgSalida { get; set; }
    public decimal? PorcPartic { get; set; }
    public decimal? DifPorc { get; set; }
}
