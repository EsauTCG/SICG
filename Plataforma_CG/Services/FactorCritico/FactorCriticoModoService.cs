using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Plataforma_CG.Services.FactorCritico;

public sealed record CambioModoPesoSolicitud(
    int? PruebaId,
    string Lote,
    string Factor,
    string? ModoOrigen,
    string ModoDestino,
    string? UsuarioAutorizador,
    string? Clave,
    string UsuarioAplicacion,
    string? DireccionIp);

public sealed record CambioModoPesoResultado(
    bool Exito,
    int CodigoHttp,
    string Mensaje,
    string? AutorizadoPor = null,
    long? BitacoraId = null);

public sealed class FactorCriticoModoService
{
    private const int MaximoIntentosFallidos = 5;
    private const int MinutosBloqueo = 5;
    private readonly string? _cadenaConexion;

    public FactorCriticoModoService(IConfiguration configuration)
    {
        var nombreCadena = configuration["FactorCriticoSql:ConnectionStringName"];
        if (string.IsNullOrWhiteSpace(nombreCadena))
            nombreCadena = "FactorCritico";

        _cadenaConexion = configuration.GetConnectionString(nombreCadena);
    }

    public async Task<CambioModoPesoResultado> CambiarModoAsync(
        CambioModoPesoSolicitud solicitud,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_cadenaConexion))
        {
            return new CambioModoPesoResultado(
                false,
                StatusCodes.Status503ServiceUnavailable,
                "No está configurada la conexión de Factor Crítico.");
        }

        var factor = NormalizarFactor(solicitud.Factor);
        if (factor is null)
            return new CambioModoPesoResultado(false, StatusCodes.Status400BadRequest, "Factor inválido.");

        var modoDestino = NormalizarModo(solicitud.ModoDestino);
        var modoOrigen = NormalizarModo(solicitud.ModoOrigen);
        if (modoDestino is null)
            return new CambioModoPesoResultado(false, StatusCodes.Status400BadRequest, "Modo de captura inválido.");

        await using var conexion = new SqlConnection(_cadenaConexion);
        await conexion.OpenAsync(cancellationToken);

        if (modoDestino == "AUTOMATICO")
        {
            var bitacoraId = await InsertarBitacoraAsync(
                conexion,
                transaction: null,
                solicitud,
                factor,
                modoOrigen,
                modoDestino,
                autorizadorId: null,
                autorizadorNombre: null,
                cancellationToken);

            return new CambioModoPesoResultado(
                true,
                StatusCodes.Status200OK,
                "Modo automático activado.",
                BitacoraId: bitacoraId);
        }

        if (string.IsNullOrWhiteSpace(solicitud.UsuarioAutorizador) || string.IsNullOrEmpty(solicitud.Clave))
        {
            return new CambioModoPesoResultado(
                false,
                StatusCodes.Status400BadRequest,
                "Captura el usuario y la contraseña de autorización.");
        }

        if (solicitud.UsuarioAutorizador.Length > 50 || solicitud.Clave.Length > 128)
        {
            return new CambioModoPesoResultado(
                false,
                StatusCodes.Status400BadRequest,
                "Las credenciales de autorización no tienen un formato válido.");
        }

        await using var transaction = (SqlTransaction)await conexion.BeginTransactionAsync(cancellationToken);

        var autorizador = await ConsultarAutorizadorAsync(
            conexion,
            transaction,
            solicitud.UsuarioAutorizador.Trim(),
            cancellationToken);

        if (autorizador is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            await Task.Delay(150, cancellationToken);
            return CredencialesInvalidas();
        }

        if (autorizador.BloqueadoHastaUtc is DateTime bloqueadoHasta && bloqueadoHasta > DateTime.UtcNow)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CambioModoPesoResultado(
                false,
                StatusCodes.Status429TooManyRequests,
                $"La autorización está bloqueada temporalmente. Intenta después de {bloqueadoHasta.ToLocalTime():HH:mm}.");
        }

        var hashCapturado = Rfc2898DeriveBytes.Pbkdf2(
            solicitud.Clave,
            autorizador.Salt,
            autorizador.Iteraciones,
            HashAlgorithmName.SHA256,
            autorizador.Hash.Length);

        if (!CryptographicOperations.FixedTimeEquals(hashCapturado, autorizador.Hash))
        {
            await RegistrarIntentoFallidoAsync(conexion, transaction, autorizador.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CredencialesInvalidas();
        }

        await ReiniciarIntentosAsync(conexion, transaction, autorizador.Id, cancellationToken);

        var idBitacora = await InsertarBitacoraAsync(
            conexion,
            transaction,
            solicitud,
            factor,
            modoOrigen,
            modoDestino,
            autorizador.Id,
            autorizador.Nombre,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new CambioModoPesoResultado(
            true,
            StatusCodes.Status200OK,
            $"{autorizador.Nombre} autorizó el peso manual.",
            autorizador.Nombre,
            idBitacora);
    }

    private static CambioModoPesoResultado CredencialesInvalidas() =>
        new(false, StatusCodes.Status403Forbidden, "Usuario o contraseña de autorización incorrectos.");

    private static string? NormalizarFactor(string? factor) =>
        factor?.Trim().ToUpperInvariant() switch
        {
            "HUESO" => "HUESO",
            "RECORTE" => "RECORTE",
            "GRASA" => "GRASA",
            _ => null
        };

    private static string? NormalizarModo(string? modo) =>
        modo?.Trim().ToUpperInvariant() switch
        {
            "MANUAL" => "MANUAL",
            "AUTOMATICO" or "AUTOMÁTICO" => "AUTOMATICO",
            _ => null
        };

    private static async Task<Autorizador?> ConsultarAutorizadorAsync(
        SqlConnection conexion,
        SqlTransaction transaction,
        string usuario,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1)
                Id,
                Nombre,
                ClaveHash,
                ClaveSalt,
                Iteraciones,
                BloqueadoHastaUtc
            FROM dbo.AutorizadorModoPeso WITH (UPDLOCK, ROWLOCK)
            WHERE Usuario = @Usuario
              AND Activo = 1;
            """;

        await using var command = new SqlCommand(sql, conexion, transaction);
        command.Parameters.AddWithValue("@Usuario", usuario);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new Autorizador(
            reader.GetInt32(0),
            reader.GetString(1),
            (byte[])reader[2],
            (byte[])reader[3],
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5));
    }

    private static async Task RegistrarIntentoFallidoAsync(
        SqlConnection conexion,
        SqlTransaction transaction,
        int autorizadorId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.AutorizadorModoPeso
            SET IntentosFallidos = IntentosFallidos + 1,
                BloqueadoHastaUtc = CASE
                    WHEN IntentosFallidos + 1 >= @MaximoIntentos
                    THEN DATEADD(MINUTE, @MinutosBloqueo, SYSUTCDATETIME())
                    ELSE BloqueadoHastaUtc
                END
            WHERE Id = @Id;
            """;

        await using var command = new SqlCommand(sql, conexion, transaction);
        command.Parameters.AddWithValue("@MaximoIntentos", MaximoIntentosFallidos);
        command.Parameters.AddWithValue("@MinutosBloqueo", MinutosBloqueo);
        command.Parameters.AddWithValue("@Id", autorizadorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReiniciarIntentosAsync(
        SqlConnection conexion,
        SqlTransaction transaction,
        int autorizadorId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.AutorizadorModoPeso
            SET IntentosFallidos = 0,
                BloqueadoHastaUtc = NULL,
                UltimoAccesoUtc = SYSUTCDATETIME()
            WHERE Id = @Id;
            """;

        await using var command = new SqlCommand(sql, conexion, transaction);
        command.Parameters.AddWithValue("@Id", autorizadorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> InsertarBitacoraAsync(
        SqlConnection conexion,
        SqlTransaction? transaction,
        CambioModoPesoSolicitud solicitud,
        string factor,
        string? modoOrigen,
        string modoDestino,
        int? autorizadorId,
        string? autorizadorNombre,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.BitacoraModoPeso
            (
                fk_Prueba,
                Lote,
                Factor,
                ModoOrigen,
                ModoDestino,
                fk_Autorizador,
                NombreAutorizador,
                UsuarioAplicacion,
                DireccionIp
            )
            OUTPUT INSERTED.Id
            VALUES
            (
                @PruebaId,
                @Lote,
                @Factor,
                @ModoOrigen,
                @ModoDestino,
                @AutorizadorId,
                @AutorizadorNombre,
                @UsuarioAplicacion,
                @DireccionIp
            );
            """;

        await using var command = new SqlCommand(sql, conexion, transaction);
        command.Parameters.AddWithValue("@PruebaId", (object?)(solicitud.PruebaId is > 0 ? solicitud.PruebaId.Value : null) ?? DBNull.Value);
        command.Parameters.AddWithValue("@Lote", (object?)Limitar(solicitud.Lote, 60) ?? DBNull.Value);
        command.Parameters.AddWithValue("@Factor", factor);
        command.Parameters.AddWithValue("@ModoOrigen", (object?)modoOrigen ?? DBNull.Value);
        command.Parameters.AddWithValue("@ModoDestino", modoDestino);
        command.Parameters.AddWithValue("@AutorizadorId", (object?)autorizadorId ?? DBNull.Value);
        command.Parameters.AddWithValue("@AutorizadorNombre", (object?)autorizadorNombre ?? DBNull.Value);
        command.Parameters.AddWithValue("@UsuarioAplicacion", Limitar(solicitud.UsuarioAplicacion, 256) ?? "Sistema");
        command.Parameters.AddWithValue("@DireccionIp", (object?)Limitar(solicitud.DireccionIp, 45) ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    private static string? Limitar(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private sealed record Autorizador(
        int Id,
        string Nombre,
        byte[] Hash,
        byte[] Salt,
        int Iteraciones,
        DateTime? BloqueadoHastaUtc);
}
