using Dapper;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Transactions;

namespace Plataforma_CG.Services;

public sealed class SkuConversionValidacionException(string message) : Exception(message);
public sealed class SkuConversionConflictoException(string message) : Exception(message);

/// <summary>Catálogo del Planeador, limitado al SIGO local de Pruebas Esau.</summary>
public sealed class PlaneadorSkuConversionService(IConfiguration configuration)
{
    public const string ServidorPruebas = @"DESKTOP-UB7IMQF\SQL2026";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Columnas = "s.Id,s.SkuOrigen,s.SkuDestino,s.Factor,s.Prioridad,s.Motivo,s.Activo,s.FechaAlta,s.UsuarioAlta,s.CreatedAt,s.UpdatedAt";
    private const string JoinsArticulos = "LEFT JOIN dbo.ArticuloSap o ON o.ProductoCodigo=s.SkuOrigen LEFT JOIN dbo.ArticuloSap d ON d.ProductoCodigo=s.SkuDestino";

    public static void ValidarDestino(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Falta configurar CadenaSQLSIGO para Pruebas Esau.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.DataSource, ServidorPruebas, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(builder.InitialCatalog, "SIGO", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sku Conversión está habilitado únicamente para el SIGO local de Pruebas Esau. Revisa CadenaSQLSIGO.");
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var cadena = configuration.GetConnectionString("CadenaSQLSIGO");
        ValidarDestino(cadena);
        var builder = new SqlConnectionStringBuilder(cadena)
        {
            ConnectTimeout = 10,
            ApplicationName = "SIGO Planeador - SkuConversion"
        };
        var connection = new SqlConnection(builder.ConnectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static CommandDefinition Cmd(string sql, object? args, SqlTransaction? tx, CancellationToken ct)
        => new(sql, args, tx, commandTimeout: 30, cancellationToken: ct);

    private static string? Limpiar(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Buscar(string value) => "%" + value.Replace("~", "~~").Replace("%", "~%")
        .Replace("_", "~_").Replace("[", "~[") + "%";

    public async Task<SkuConversionListado> ListarAsync(string? busqueda, string? estado, int pagina, CancellationToken ct)
    {
        var q = Limpiar(busqueda) ?? "";
        if (q.Length > 100) throw new SkuConversionValidacionException("La búsqueda admite hasta 100 caracteres.");
        if (estado is not (null or "" or "todos" or "activos" or "inactivos"))
            throw new SkuConversionValidacionException("Selecciona un estado válido.");
        pagina = Math.Clamp(pagina, 1, 100000);
        const int tamano = 25;
        bool? activo = estado == "activos" ? true : estado == "inactivos" ? false : null;
        var filtro = " WHERE (@Activo IS NULL OR s.Activo=@Activo) AND (@Vacio=1 OR s.SkuOrigen LIKE @Q ESCAPE '~' OR s.SkuDestino LIKE @Q ESCAPE '~' OR o.ProductoNombre LIKE @Q ESCAPE '~' OR d.ProductoNombre LIKE @Q ESCAPE '~' OR s.Motivo LIKE @Q ESCAPE '~')";
        await using var cn = await AbrirAsync(ct);
        using var result = await cn.QueryMultipleAsync(Cmd($"""
            SELECT COUNT(*) Total, COALESCE(SUM(CASE WHEN Activo=1 THEN 1 ELSE 0 END),0) Activas,
                COALESCE(SUM(CASE WHEN Activo=0 THEN 1 ELSE 0 END),0) Inactivas FROM dbo.SkuConversion;
            SELECT COUNT(*) FROM dbo.SkuConversion s {JoinsArticulos} {filtro};
            SELECT {Columnas},o.ProductoNombre NombreOrigen,d.ProductoNombre NombreDestino,h.Usuario UsuarioUltimoCambio
            FROM dbo.SkuConversion s {JoinsArticulos}
            OUTER APPLY (SELECT TOP(1) Usuario FROM dbo.SkuConversionHistorial WHERE SkuConversionId=s.Id ORDER BY Id DESC) h
            {filtro} ORDER BY s.SkuOrigen,s.Prioridad,s.SkuDestino,s.Id OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;
            """, new { Activo = activo, Vacio = q.Length == 0, Q = Buscar(q), Offset = (pagina - 1) * tamano, Tamano = tamano }, null, ct));
        var conteos = await result.ReadSingleAsync<SkuConversionConteos>();
        var total = await result.ReadSingleAsync<int>();
        var rows = (await result.ReadAsync<SkuConversionRegistro>()).ToList();
        rows.ForEach(r => r.Version = Version(r));
        return new(rows, total, pagina, tamano, conteos);
    }

    public async Task<IReadOnlyList<SkuConversionArticulo>> ArticulosAsync(string? busqueda, CancellationToken ct)
    {
        var q = Limpiar(busqueda) ?? "";
        if (q.Length < 2) return [];
        if (q.Length > 100) throw new SkuConversionValidacionException("La búsqueda admite hasta 100 caracteres.");
        await using var cn = await AbrirAsync(ct);
        return (await cn.QueryAsync<SkuConversionArticulo>(Cmd("""
            SELECT TOP(30) ProductoCodigo Sku,ProductoNombre Nombre FROM dbo.ArticuloSap
            WHERE ProductoCodigo LIKE @Q ESCAPE '~' OR ProductoNombre LIKE @Q ESCAPE '~'
            ORDER BY CASE WHEN ProductoCodigo=@Exacto THEN 0 ELSE 1 END,ProductoCodigo;
            """, new { Q = Buscar(q), Exacto = q }, null, ct))).ToList();
    }

    private static void Validar(SkuConversionGuardar input, string usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario) || usuario.Length > 80)
            throw new SkuConversionValidacionException("Se requiere un usuario de sesión válido de hasta 80 caracteres.");
        input.SkuOrigen = input.SkuOrigen?.Trim() ?? "";
        input.SkuDestino = input.SkuDestino?.Trim() ?? "";
        input.Motivo = Limpiar(input.Motivo);
        if (input.Id < 0 || input.SkuOrigen.Length is < 1 or > 50 || input.SkuDestino.Length is < 1 or > 50)
            throw new SkuConversionValidacionException("Indica los SKU de origen y destino, de hasta 50 caracteres.");
        if (input.Motivo?.Length > 150) throw new SkuConversionValidacionException("El motivo admite hasta 150 caracteres.");
        if (input.Prioridad < 0) throw new SkuConversionValidacionException("La prioridad debe ser cero o mayor; déjala vacía para Base.");
        if (input.Factor.HasValue && (input.Factor <= 0 || input.Factor > 999999999999.999999m || decimal.Round(input.Factor.Value, 6) != input.Factor))
            throw new SkuConversionValidacionException("El factor debe ser positivo, con hasta 12 enteros y 6 decimales, o quedar vacío.");
    }

    private static string Snapshot(SkuConversionRegistro r) => JsonSerializer.Serialize(new
    {
        r.Id,r.SkuOrigen,r.SkuDestino,r.Factor,r.Prioridad,r.Motivo,r.Activo,
        r.FechaAlta,r.UsuarioAlta,r.CreatedAt,r.UpdatedAt
    }, JsonOptions);

    private static string Version(SkuConversionRegistro r)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Snapshot(r))));

    private static void VerificarVersion(SkuConversionRegistro actual, string? version)
    {
        if (!string.Equals(Version(actual), version, StringComparison.Ordinal))
            throw new SkuConversionConflictoException("Otra persona modificó esta conversión. Actualiza el listado y vuelve a abrirla antes de guardar.");
    }

    private static async Task<SkuConversionRegistro> ObtenerAsync(SqlConnection cn, int id, SqlTransaction? tx, bool bloquear, CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<SkuConversionRegistro>(Cmd(
            $"SELECT {Columnas} FROM dbo.SkuConversion s {(bloquear ? "WITH (UPDLOCK,HOLDLOCK)" : "")} WHERE s.Id=@Id;", new { Id = id }, tx, ct))
            ?? throw new KeyNotFoundException("La conversión ya no existe.");

    private static Task RegistrarAsync(SqlConnection cn, SqlTransaction? tx, string accion, string usuario,
        SkuConversionRegistro? antes, SkuConversionRegistro despues, CancellationToken ct)
        => cn.ExecuteAsync(Cmd("""
            INSERT INTO dbo.SkuConversionHistorial(SkuConversionId,Accion,Usuario,DatosAntes,DatosDespues)
            VALUES (@Id,@Accion,@Usuario,@Antes,@Despues);
            """, new { despues.Id, Accion = accion, Usuario = usuario, Antes = antes is null ? null : Snapshot(antes), Despues = Snapshot(despues) }, tx, ct));

    public async Task<SkuConversionRegistro> GuardarAsync(SkuConversionGuardar input, string usuario, CancellationToken ct)
    {
        usuario = usuario.Trim();
        if (input.Id == 0)
        {
            // Las altas del Planeador siempre usan estos valores, independientemente del cliente.
            input.Prioridad = 1;
            input.Factor = null;
        }
        Validar(input, usuario);
        await using var cn = await AbrirAsync(ct);
        // Respetar una transacción de quien invoque el servicio; en HTTP se crea una propia.
        await using var tx = Transaction.Current is null ? (SqlTransaction)await cn.BeginTransactionAsync(ct) : null;
        SkuConversionRegistro? antes = null;
        if (input.Id > 0)
        {
            antes = await ObtenerAsync(cn, input.Id, tx, true, ct);
            VerificarVersion(antes, input.Version);
        }
        var articulos = await cn.QueryAsync<string>(Cmd("SELECT ProductoCodigo FROM dbo.ArticuloSap WHERE ProductoCodigo IN (@Origen,@Destino);",
            new { Origen = input.SkuOrigen, Destino = input.SkuDestino }, tx, ct));
        var codes = articulos.ToList();
        // Conservar el código exacto del catálogo, incluso si se escribió con otra combinación de mayúsculas.
        input.SkuOrigen = codes.FirstOrDefault(c => string.Equals(c, input.SkuOrigen, StringComparison.OrdinalIgnoreCase))
            ?? throw new SkuConversionValidacionException("El SKU de origen no existe en el catálogo de artículos de SIGO.");
        input.SkuDestino = codes.FirstOrDefault(c => string.Equals(c, input.SkuDestino, StringComparison.OrdinalIgnoreCase))
            ?? throw new SkuConversionValidacionException("El SKU de destino no existe en el catálogo de artículos de SIGO.");
        if (await cn.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM dbo.SkuConversion WITH (UPDLOCK,HOLDLOCK)
            WHERE SkuOrigen=@SkuOrigen AND SkuDestino=@SkuDestino AND Id<>@Id;
            """, input, tx, ct)) > 0)
            throw new SkuConversionValidacionException("Esta pareja de SKU ya existe, incluso si está inactiva. Edita o reactiva la conversión existente.");
        var args = new { input.Id,input.SkuOrigen,input.SkuDestino,input.Factor,input.Prioridad,input.Motivo,input.Activo, Usuario = usuario };
        var id = input.Id;
        try
        {
            if (id == 0)
                id = await cn.QuerySingleAsync<int>(Cmd("""
                    INSERT INTO dbo.SkuConversion(SkuOrigen,SkuDestino,Factor,Prioridad,Motivo,Activo,UsuarioAlta)
                    OUTPUT INSERTED.Id VALUES (@SkuOrigen,@SkuDestino,@Factor,@Prioridad,@Motivo,@Activo,@Usuario);
                    """, args, tx, ct));
            else
                await cn.ExecuteAsync(Cmd("""
                    UPDATE dbo.SkuConversion SET SkuOrigen=@SkuOrigen,SkuDestino=@SkuDestino,Factor=@Factor,
                        Prioridad=@Prioridad,Motivo=@Motivo,Activo=@Activo,UpdatedAt=GETDATE() WHERE Id=@Id;
                    """, args, tx, ct));
            var despues = await ObtenerAsync(cn, id, tx, false, ct);
            await RegistrarAsync(cn, tx, antes is null ? "Alta" : "Edición", usuario, antes, despues, ct);
            if (tx is not null) await tx.CommitAsync(ct);
            despues.Version = Version(despues);
            return despues;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        { throw new SkuConversionValidacionException("Esta pareja de SKU ya existe. Actualiza el listado y edita la conversión existente."); }
    }

    public async Task CambiarEstadoAsync(SkuConversionEstado input, string usuario, CancellationToken ct)
    {
        if (input.Id <= 0 || string.IsNullOrWhiteSpace(usuario) || usuario.Trim().Length > 80)
            throw new SkuConversionValidacionException("Se requiere una conversión y un usuario de sesión válidos.");
        await using var cn = await AbrirAsync(ct);
        await using var tx = Transaction.Current is null ? (SqlTransaction)await cn.BeginTransactionAsync(ct) : null;
        var antes = await ObtenerAsync(cn, input.Id, tx, true, ct);
        VerificarVersion(antes, input.Version);
        if (antes.Activo == input.Activo) return;
        await cn.ExecuteAsync(Cmd("UPDATE dbo.SkuConversion SET Activo=@Activo,UpdatedAt=GETDATE() WHERE Id=@Id;", input, tx, ct));
        var despues = await ObtenerAsync(cn, input.Id, tx, false, ct);
        await RegistrarAsync(cn, tx, input.Activo ? "Activación" : "Desactivación", usuario.Trim(), antes, despues, ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<SkuConversionCambio>> HistorialAsync(int id, CancellationToken ct)
    {
        if (id <= 0) throw new SkuConversionValidacionException("Selecciona una conversión válida.");
        await using var cn = await AbrirAsync(ct);
        return (await cn.QueryAsync<SkuConversionCambio>(Cmd("""
            SELECT Id,Accion,Usuario,Fecha,DatosAntes,DatosDespues FROM dbo.SkuConversionHistorial
            WHERE SkuConversionId=@Id ORDER BY Id DESC;
            """, new { Id = id }, null, ct))).ToList();
    }
}
