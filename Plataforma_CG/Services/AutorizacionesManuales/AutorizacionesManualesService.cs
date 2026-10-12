using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Plataforma_CG.Services.AutorizacionesManuales;

public sealed record AutorizadorManualDto(
    int Id,
    string Nombre,
    string Usuario,
    bool Activo,
    int IntentosFallidos,
    DateTime? BloqueadoHastaUtc,
    DateTime FechaCreacionUtc,
    DateTime? UltimoAccesoUtc);

public sealed record AutorizadorManualResultado(
    bool Exito,
    int CodigoHttp,
    string Mensaje,
    AutorizadorManualDto? Autorizador = null);

public sealed class AutorizacionesManualesService
{
    private const int IteracionesPbkdf2 = 210_000;
    private const int LongitudSalt = 16;
    private const int LongitudHash = 32;
    private readonly string? _cadenaConexion;

    public AutorizacionesManualesService(IConfiguration configuration)
    {
        var nombreCadena = configuration["FactorCriticoSql:ConnectionStringName"];
        if (string.IsNullOrWhiteSpace(nombreCadena))
            nombreCadena = "FactorCritico";

        _cadenaConexion = configuration.GetConnectionString(nombreCadena);
    }

    public async Task<IReadOnlyList<AutorizadorManualDto>> ListarAsync(
        CancellationToken cancellationToken)
    {
        await using var conexion = CrearConexion();
        await conexion.OpenAsync(cancellationToken);

        const string sql = """
            SELECT Id, Nombre, Usuario, Activo, IntentosFallidos,
                   BloqueadoHastaUtc, FechaCreacionUtc, UltimoAccesoUtc
            FROM dbo.AutorizadorModoPeso
            ORDER BY Activo DESC, Nombre, Usuario;
            """;

        await using var command = new SqlCommand(sql, conexion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var resultado = new List<AutorizadorManualDto>();

        while (await reader.ReadAsync(cancellationToken))
            resultado.Add(Mapear(reader));

        return resultado;
    }

    public async Task<AutorizadorManualResultado> CrearAsync(
        string nombre,
        string usuario,
        string clave,
        CancellationToken cancellationToken)
    {
        var validacion = ValidarDatos(nombre, usuario, clave, claveRequerida: true);
        if (validacion is not null)
            return validacion;

        nombre = nombre.Trim();
        usuario = usuario.Trim();
        var salt = RandomNumberGenerator.GetBytes(LongitudSalt);
        var hash = GenerarHash(clave, salt);

        await using var conexion = CrearConexion();
        await conexion.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO dbo.AutorizadorModoPeso
                (Nombre, Usuario, ClaveHash, ClaveSalt, Iteraciones, Activo)
            OUTPUT INSERTED.Id, INSERTED.Nombre, INSERTED.Usuario, INSERTED.Activo,
                   INSERTED.IntentosFallidos, INSERTED.BloqueadoHastaUtc,
                   INSERTED.FechaCreacionUtc, INSERTED.UltimoAccesoUtc
            VALUES (@Nombre, @Usuario, @ClaveHash, @ClaveSalt, @Iteraciones, 1);
            """;

        await using var command = new SqlCommand(sql, conexion);
        AgregarNVarChar(command, "@Nombre", 80, nombre);
        AgregarNVarChar(command, "@Usuario", 50, usuario);
        command.Parameters.Add("@ClaveHash", SqlDbType.VarBinary, LongitudHash).Value = hash;
        command.Parameters.Add("@ClaveSalt", SqlDbType.VarBinary, LongitudSalt).Value = salt;
        command.Parameters.Add("@Iteraciones", SqlDbType.Int).Value = IteracionesPbkdf2;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var creado = Mapear(reader);
            return new AutorizadorManualResultado(
                true,
                StatusCodes.Status201Created,
                $"El usuario {creado.Usuario} quedó listo para autorizar peso manual.",
                creado);
        }
        catch (SqlException ex) when (EsUsuarioDuplicado(ex))
        {
            return new AutorizadorManualResultado(
                false,
                StatusCodes.Status409Conflict,
                "Ya existe un autorizador con ese usuario.");
        }
    }

    public async Task<AutorizadorManualResultado> ActualizarAsync(
        int id,
        string nombre,
        string usuario,
        string? nuevaClave,
        bool activo,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
            return SolicitudInvalida("El identificador del autorizador no es válido.");

        var validacion = ValidarDatos(nombre, usuario, nuevaClave, claveRequerida: false);
        if (validacion is not null)
            return validacion;

        nombre = nombre.Trim();
        usuario = usuario.Trim();
        var cambiaClave = !string.IsNullOrEmpty(nuevaClave);
        var salt = cambiaClave ? RandomNumberGenerator.GetBytes(LongitudSalt) : null;
        var hash = cambiaClave ? GenerarHash(nuevaClave!, salt!) : null;

        await using var conexion = CrearConexion();
        await conexion.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE dbo.AutorizadorModoPeso
            SET Nombre = @Nombre,
                Usuario = @Usuario,
                Activo = @Activo,
                ClaveHash = CASE WHEN @CambiarClave = 1 THEN @ClaveHash ELSE ClaveHash END,
                ClaveSalt = CASE WHEN @CambiarClave = 1 THEN @ClaveSalt ELSE ClaveSalt END,
                Iteraciones = CASE WHEN @CambiarClave = 1 THEN @Iteraciones ELSE Iteraciones END,
                IntentosFallidos = CASE WHEN @CambiarClave = 1 THEN 0 ELSE IntentosFallidos END,
                BloqueadoHastaUtc = CASE WHEN @CambiarClave = 1 THEN NULL ELSE BloqueadoHastaUtc END
            OUTPUT INSERTED.Id, INSERTED.Nombre, INSERTED.Usuario, INSERTED.Activo,
                   INSERTED.IntentosFallidos, INSERTED.BloqueadoHastaUtc,
                   INSERTED.FechaCreacionUtc, INSERTED.UltimoAccesoUtc
            WHERE Id = @Id;
            """;

        await using var command = new SqlCommand(sql, conexion);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        AgregarNVarChar(command, "@Nombre", 80, nombre);
        AgregarNVarChar(command, "@Usuario", 50, usuario);
        command.Parameters.Add("@Activo", SqlDbType.Bit).Value = activo;
        command.Parameters.Add("@CambiarClave", SqlDbType.Bit).Value = cambiaClave;
        command.Parameters.Add("@ClaveHash", SqlDbType.VarBinary, LongitudHash).Value = (object?)hash ?? DBNull.Value;
        command.Parameters.Add("@ClaveSalt", SqlDbType.VarBinary, LongitudSalt).Value = (object?)salt ?? DBNull.Value;
        command.Parameters.Add("@Iteraciones", SqlDbType.Int).Value = IteracionesPbkdf2;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return NoEncontrado();

            var actualizado = Mapear(reader);
            return new AutorizadorManualResultado(
                true,
                StatusCodes.Status200OK,
                cambiaClave
                    ? "Los datos y la contraseña se actualizaron correctamente."
                    : "Los datos del autorizador se actualizaron correctamente.",
                actualizado);
        }
        catch (SqlException ex) when (EsUsuarioDuplicado(ex))
        {
            return new AutorizadorManualResultado(
                false,
                StatusCodes.Status409Conflict,
                "Ya existe otro autorizador con ese usuario.");
        }
    }

    public async Task<AutorizadorManualResultado> DesbloquearAsync(
        int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
            return SolicitudInvalida("El identificador del autorizador no es válido.");

        await using var conexion = CrearConexion();
        await conexion.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE dbo.AutorizadorModoPeso
            SET IntentosFallidos = 0,
                BloqueadoHastaUtc = NULL
            OUTPUT INSERTED.Id, INSERTED.Nombre, INSERTED.Usuario, INSERTED.Activo,
                   INSERTED.IntentosFallidos, INSERTED.BloqueadoHastaUtc,
                   INSERTED.FechaCreacionUtc, INSERTED.UltimoAccesoUtc
            WHERE Id = @Id;
            """;

        await using var command = new SqlCommand(sql, conexion);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return NoEncontrado();

        return new AutorizadorManualResultado(
            true,
            StatusCodes.Status200OK,
            "El autorizador fue desbloqueado.",
            Mapear(reader));
    }

    public async Task<AutorizadorManualResultado> EliminarAsync(
        int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
            return SolicitudInvalida("El identificador del autorizador no es válido.");

        await using var conexion = CrearConexion();
        await conexion.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await conexion.BeginTransactionAsync(cancellationToken);

        const string consultarSql = """
            SELECT Usuario
            FROM dbo.AutorizadorModoPeso WITH (UPDLOCK, HOLDLOCK)
            WHERE Id = @Id;
            """;

        string? usuario;
        await using (var consultar = new SqlCommand(consultarSql, conexion, transaction))
        {
            consultar.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            usuario = (string?)await consultar.ExecuteScalarAsync(cancellationToken);
        }

        if (usuario is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NoEncontrado();
        }

        // NombreAutorizador permanece en la bitácora; solo se libera la FK.
        const string eliminarSql = """
            UPDATE dbo.BitacoraModoPeso
            SET fk_Autorizador = NULL
            WHERE fk_Autorizador = @Id;

            DELETE FROM dbo.AutorizadorModoPeso
            WHERE Id = @Id;
            """;

        await using (var eliminar = new SqlCommand(eliminarSql, conexion, transaction))
        {
            eliminar.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            await eliminar.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new AutorizadorManualResultado(
            true,
            StatusCodes.Status200OK,
            $"El usuario {usuario} fue eliminado. Su historial de autorizaciones se conservó.");
    }

    private SqlConnection CrearConexion()
    {
        if (string.IsNullOrWhiteSpace(_cadenaConexion))
            throw new InvalidOperationException("No está configurada la conexión de autorizaciones manuales.");

        return new SqlConnection(_cadenaConexion);
    }

    private static byte[] GenerarHash(string clave, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            clave,
            salt,
            IteracionesPbkdf2,
            HashAlgorithmName.SHA256,
            LongitudHash);

    private static AutorizadorManualResultado? ValidarDatos(
        string? nombre,
        string? usuario,
        string? clave,
        bool claveRequerida)
    {
        var nombreLimpio = nombre?.Trim() ?? string.Empty;
        var usuarioLimpio = usuario?.Trim() ?? string.Empty;

        if (nombreLimpio.Length is < 2 or > 80)
            return SolicitudInvalida("El nombre debe contener entre 2 y 80 caracteres.");

        if (usuarioLimpio.Length is < 2 or > 50)
            return SolicitudInvalida("El usuario debe contener entre 2 y 50 caracteres.");

        if (usuarioLimpio.Any(char.IsWhiteSpace))
            return SolicitudInvalida("El usuario no puede contener espacios.");

        if (usuarioLimpio.Any(c => !char.IsLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
            return SolicitudInvalida("El usuario solo puede contener letras, números, punto, guion o guion bajo.");

        if (claveRequerida && string.IsNullOrEmpty(clave))
            return SolicitudInvalida("La contraseña es obligatoria.");

        if (!string.IsNullOrEmpty(clave) && clave.Length is < 8 or > 128)
            return SolicitudInvalida("La contraseña debe contener entre 8 y 128 caracteres.");

        return null;
    }

    private static AutorizadorManualDto Mapear(SqlDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetBoolean(3),
        reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetDateTime(5),
        reader.GetDateTime(6),
        reader.IsDBNull(7) ? null : reader.GetDateTime(7));

    private static void AgregarNVarChar(
        SqlCommand command,
        string nombre,
        int longitud,
        string valor) =>
        command.Parameters.Add(nombre, SqlDbType.NVarChar, longitud).Value = valor;

    private static bool EsUsuarioDuplicado(SqlException exception) =>
        exception.Number is 2601 or 2627;

    private static AutorizadorManualResultado SolicitudInvalida(string mensaje) =>
        new(false, StatusCodes.Status400BadRequest, mensaje);

    private static AutorizadorManualResultado NoEncontrado() =>
        new(false, StatusCodes.Status404NotFound, "El autorizador ya no existe.");
}
