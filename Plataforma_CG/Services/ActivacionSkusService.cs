using Dapper;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Models.ActivacionSkus;
using System.Data;
using System.Transactions;
using System.Text.Json;

namespace Plataforma_CG.Services;

public sealed class ActivacionSkusValidacionException(string message) : Exception(message);
public sealed class ActivacionSkusConflictoException(string message) : Exception(message);

public sealed class ActivacionSkusService(IConfiguration configuration, ILogger<ActivacionSkusService> logger)
{
    public const string ClaveModulo = "ACTIVACION_SKUS";
    public const int MaximoLote = 500;
    public static IReadOnlyList<PlantaActivacionSku> PlantasPermitidas { get; } = Array.AsReadOnly(new[]
    {
        new PlantaActivacionSku("TIF", "Planta TIF"), new PlantaActivacionSku("P1", "Planta 1")
    });
    public static IReadOnlyList<string> TiposPermitidos { get; } = Array.AsReadOnly(new[] { "P", "F", "C", "S" });
    private const string Relacion = "FROM dbo.Codigo c INNER JOIN dbo.Proveedor p ON p.ProveedorId=c.Origen";
    private const string Columnas = "c.Origen,c.AliasExterno,c.ArticuloId,c.Tipo,p.Nombre Programacion";
    private const string Filtro = """
        WHERE p.Nombre LIKE @Prefijo
          AND (@Origen IS NULL OR c.Origen=@Origen)
          AND (@Vacio=1 OR c.ArticuloId LIKE @Busqueda ESCAPE '~' OR c.AliasExterno LIKE @Busqueda ESCAPE '~')
        """;

    // La petición elige una planta conocida; nunca recibe una cadena o nombre de BD del navegador.
    private static (string Conexion, string BaseDatos) Destino(string? planta) => planta switch
    {
        "TIF" => ("CadenaTIFCommerciaNET", "TIF_CommerciaNET"),
        "P1" => ("CadenaCommerciaNET", "CommerciaNet"),
        _ => throw new ActivacionSkusValidacionException("Selecciona Planta TIF o Planta 1.")
    };

    public static void ValidarDestino(string? cadena, string? planta)
    {
        var destino = Destino(planta);
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException($"Falta ConnectionStrings:{destino.Conexion}.");
        var builder = new SqlConnectionStringBuilder(cadena);
        if (!string.Equals(builder.InitialCatalog, destino.BaseDatos, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"La conexión de la planta seleccionada debe apuntar a {destino.BaseDatos}.");
    }

    private async Task<SqlConnection> AbrirAsync(string? planta, CancellationToken ct)
    {
        var destino = Destino(planta);
        var cadena = configuration.GetConnectionString(destino.Conexion);
        ValidarDestino(cadena, planta);
        var builder = new SqlConnectionStringBuilder(cadena)
        {
            ConnectTimeout = 8,
            ApplicationName = $"SIGO - Activacion de SKUS - {planta}"
        };
        var cn = new SqlConnection(builder.ConnectionString);
        try { await cn.OpenAsync(ct); return cn; }
        catch { await cn.DisposeAsync(); throw; }
    }

    public static void ValidarCatalogo(string? cadena)
    {
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException("Falta ConnectionStrings:CadenaSQLSIGO.");
        if (!string.Equals(new SqlConnectionStringBuilder(cadena).InitialCatalog, "SIGO", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El catálogo de SKU requiere la base SIGO.");
    }

    private async Task<SqlConnection> AbrirCatalogoAsync(CancellationToken ct)
    {
        var cadena = configuration.GetConnectionString("CadenaSQLSIGO");
        ValidarCatalogo(cadena);
        // El catálogo es de lectura; no enlistarlo en las transacciones de la planta ni requerir DTC.
        var builder = new SqlConnectionStringBuilder(cadena)
        {
            ConnectTimeout = 8, ApplicationName = "SIGO - Activacion SKUS - Catalogo", Enlist = false
        };
        var cn = new SqlConnection(builder.ConnectionString);
        try { await cn.OpenAsync(ct); return cn; }
        catch { await cn.DisposeAsync(); throw; }
    }

    private static CommandDefinition Cmd(string sql, object? args, CancellationToken ct, SqlTransaction? tx = null)
        => new(sql, args, tx, commandTimeout: 30, cancellationToken: ct);
    private static string EscaparBusqueda(string texto) => "%" + texto.Replace("~", "~~")
        .Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    // Dapper DbString preserva varchar y evita conversiones de las llaves indexadas a nvarchar.
    private static DbString Clave(string? valor) => new() { Value = valor, IsAnsi = true, Length = 15 };
    private static DbString Letra(string? valor) => new() { Value = valor, IsAnsi = true, Length = 1 };
    private static void ValidarProgramacion(string? planta, string? origen)
    {
        _ = Destino(planta);
        if (string.IsNullOrWhiteSpace(origen) || origen.Length > 15)
            throw new ActivacionSkusValidacionException("Selecciona una programación válida.");
    }

    private static async Task<ProgramacionSku> ObtenerProgramacionAsync(SqlConnection cn, string origen,
        CancellationToken ct, SqlTransaction? tx = null, bool bloquear = false)
        => await cn.QuerySingleOrDefaultAsync<ProgramacionSku>(Cmd($"""
            SELECT p.ProveedorId,p.Nombre,0 Registros FROM dbo.Proveedor p {(bloquear ? "WITH (UPDLOCK,HOLDLOCK)" : "")}
            WHERE p.ProveedorId=@Origen AND p.Nombre LIKE @Prefijo;
            """, new { Origen = Clave(origen), Prefijo = "PROGRAMACION%" }, ct, tx))
            ?? throw new ActivacionSkusValidacionException("El proveedor seleccionado no es una programación válida.");

    public async Task<ActivacionSkusDisponibles> DisponiblesAsync(string? planta, string? origen,
        string? busqueda, int pagina, CancellationToken ct)
    {
        ValidarProgramacion(planta, origen);
        var texto = busqueda?.Trim() ?? "";
        if (texto.Length > 100 || pagina is < 1 or > 100000)
            throw new ActivacionSkusValidacionException("Revisa la búsqueda (máximo 100 caracteres) y la página.");
        await using var destino = await AbrirAsync(planta, ct);
        await ObtenerProgramacionAsync(destino, origen!, ct);
        await using var catalogo = await AbrirCatalogoAsync(ct);
        var articulos = (await catalogo.QueryAsync<ArticuloCatalogoSku>(Cmd("""
            SELECT ProductoCodigo ArticuloId,ProductoNombre Descripcion FROM dbo.ArticuloSap
            WHERE NULLIF(LTRIM(RTRIM(ProductoCodigo)),N'') IS NOT NULL AND DATALENGTH(ProductoCodigo)<=30
              AND (@Vacio=1 OR ProductoCodigo LIKE @Busqueda ESCAPE '~' OR ProductoNombre LIKE @Busqueda ESCAPE '~')
            ORDER BY ProductoCodigo;
            """, new { Vacio = texto.Length == 0, Busqueda = EscaparBusqueda(texto) }, ct))).ToList();
        // Cada planta aplica su propio cotejamiento para detectar vínculos existentes.
        // JSON evita servidores vinculados y el límite de 2100 parámetros de SQL Server.
        const int tamano = 20;
        using var result = await destino.QueryMultipleAsync(Cmd("""
            DECLARE @Disponibles TABLE (ArticuloId nvarchar(50),Descripcion nvarchar(200));
            INSERT @Disponibles(ArticuloId,Descripcion)
            SELECT a.ArticuloId,a.Descripcion FROM OPENJSON(@Catalogo)
                WITH (ArticuloId nvarchar(50) '$.ArticuloId',Descripcion nvarchar(200) '$.Descripcion') a
            WHERE a.ArticuloId COLLATE Latin1_General_100_BIN2 =
                  CONVERT(nvarchar(50),CONVERT(varchar(15),a.ArticuloId)) COLLATE Latin1_General_100_BIN2
              AND NOT EXISTS (SELECT 1 FROM dbo.Codigo c WHERE c.Origen=@Origen
                  AND c.ArticuloId=a.ArticuloId COLLATE DATABASE_DEFAULT);
            SELECT COUNT(*) FROM @Disponibles;
            SELECT ArticuloId,Descripcion FROM @Disponibles ORDER BY ArticuloId
            OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;
            """, new { Catalogo = JsonSerializer.Serialize(articulos), Origen = Clave(origen),
                Offset = (pagina - 1) * tamano, Tamano = tamano }, ct));
        var total = await result.ReadSingleAsync<int>();
        return new((await result.ReadAsync<ArticuloCatalogoSku>()).ToList(), total, pagina, tamano);
    }

    private static async Task<CodigoSku> BloquearRegistroAsync(SqlConnection cn, SqlTransaction? tx,
        string origen, string alias, string articuloId, CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<CodigoSku>(Cmd($"""
            SELECT {Columnas} FROM dbo.Codigo c WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Proveedor p WITH (HOLDLOCK) ON p.ProveedorId=c.Origen
            WHERE c.Origen=@Origen AND c.AliasExterno=@AliasExterno AND c.ArticuloId=@ArticuloId
                AND p.Nombre LIKE @Prefijo;
            """, new { Origen = Clave(origen), AliasExterno = Clave(alias), ArticuloId = Clave(articuloId),
                Prefijo = "PROGRAMACION%" }, ct, tx))
            ?? throw new KeyNotFoundException("El registro ya no existe o no pertenece a una programación.");

    private static void ValidarLlave(string? planta, string? origen, string? alias, string? articuloId)
    {
        ValidarProgramacion(planta, origen);
        if (alias is null || alias.Length > 15 || string.IsNullOrWhiteSpace(articuloId) || articuloId.Length > 15)
            throw new ActivacionSkusValidacionException("La llave del registro está incompleta o excede 15 caracteres.");
    }

    private static async Task ExigirSkuNoAsignadoAsync(SqlConnection cn, SqlTransaction? tx,
        string origen, string articuloId, CancellationToken ct)
    {
        if (await cn.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM dbo.Codigo WITH (UPDLOCK,HOLDLOCK)
            WHERE Origen=@Origen AND ArticuloId=@ArticuloId;
            """, new { Origen = Clave(origen), ArticuloId = Clave(articuloId) }, ct, tx)) > 0)
            throw new ActivacionSkusConflictoException("Este SKU ya está asignado a esa programación. Actualiza el listado.");
    }

    public static void ValidarAlta(AgregarSkuProgramacion input)
    {
        ValidarLlave(input.Planta, input.Origen, input.ArticuloId, input.ArticuloId);
        if (!TiposPermitidos.Contains(input.Tipo))
            throw new ActivacionSkusValidacionException("El tipo debe ser P, F, C o S.");
    }

    public async Task<CodigoSku> AgregarAsync(AgregarSkuProgramacion input, string usuario, CancellationToken ct)
    {
        ValidarAlta(input);
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ActivacionSkusValidacionException("Se requiere una sesión de usuario válida.");
        ArticuloCatalogoSku articulo;
        await using (var catalogo = await AbrirCatalogoAsync(ct))
        {
            articulo = await catalogo.QuerySingleOrDefaultAsync<ArticuloCatalogoSku>(Cmd("""
                SELECT ProductoCodigo ArticuloId,ProductoNombre Descripcion FROM dbo.ArticuloSap
                WHERE ProductoCodigo=@ArticuloId;
                """, new { ArticuloId = new DbString { Value = input.ArticuloId, Length = 50 } }, ct))
                ?? throw new ActivacionSkusValidacionException("El SKU seleccionado ya no existe en el catálogo de SIGO.");
        }
        if (string.IsNullOrWhiteSpace(articulo.ArticuloId) || articulo.ArticuloId.Length > 15)
            throw new ActivacionSkusValidacionException("El código del catálogo no es compatible con Codigo (máximo 15 caracteres).");
        await using var cn = await AbrirAsync(input.Planta, ct);
        await using var tx = Transaction.Current is null
            ? (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var programacion = await ObtenerProgramacionAsync(cn, input.Origen, ct, tx, bloquear: true);
        var compatible = await cn.ExecuteScalarAsync<int>(Cmd("""
            SELECT CASE WHEN CONVERT(nvarchar(50),@ArticuloAnsi) COLLATE Latin1_General_100_BIN2 =
                @ArticuloUnicode COLLATE Latin1_General_100_BIN2 THEN 1 ELSE 0 END;
            """, new { ArticuloAnsi = Clave(articulo.ArticuloId), ArticuloUnicode = articulo.ArticuloId }, ct, tx));
        if (compatible != 1)
            throw new ActivacionSkusValidacionException("El código del catálogo contiene caracteres incompatibles con la planta.");
        await ExigirSkuNoAsignadoAsync(cn, tx, programacion.ProveedorId, articulo.ArticuloId, ct);
        try
        {
            var modificados = await cn.ExecuteAsync(Cmd("""
                INSERT dbo.Codigo(Origen,AliasExterno,ArticuloId,Tipo,Precio,Unidad,Estatus,
                    UltimaModificacion,FechaHora,FechaHoraServer)
                VALUES(@Origen,@ArticuloId,@ArticuloId,@Tipo,0,0,1,GETDATE(),GETDATE(),GETDATE());
                """, new { Origen = Clave(programacion.ProveedorId), ArticuloId = Clave(articulo.ArticuloId),
                    Tipo = Letra(input.Tipo) }, ct, tx));
            if (modificados != 1)
                throw new ActivacionSkusConflictoException("No se pudo agregar un único vínculo.");
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new ActivacionSkusConflictoException("El SKU fue agregado por otro usuario. Actualiza el listado.");
        }
        logger.LogInformation("Activacion SKUS: {Usuario} agregó {ArticuloId} a {Origen}, tipo {Tipo}, en {Planta} ({BaseDatos}).",
            usuario, articulo.ArticuloId, programacion.ProveedorId, input.Tipo, input.Planta, Destino(input.Planta).BaseDatos);
        return new CodigoSku { Origen = programacion.ProveedorId, AliasExterno = articulo.ArticuloId,
            ArticuloId = articulo.ArticuloId, Tipo = input.Tipo, Programacion = programacion.Nombre };
    }

    public static void ValidarCambioMasivo(CambiarTipoSkusMasivo input)
    {
        _ = Destino(input.Planta);
        if (!TiposPermitidos.Contains(input.TipoNuevo) || input.Registros is null || input.Registros.Count is < 1 or > MaximoLote)
            throw new ActivacionSkusValidacionException("Selecciona entre 1 y 500 vínculos y un tipo válido (P, F, C o S).");
        var llaves = new HashSet<(string, string, string)>();
        foreach (var registro in input.Registros)
        {
            if (registro is null) throw new ActivacionSkusValidacionException("El lote contiene un vínculo vacío.");
            ValidarLlave(input.Planta, registro.Origen, registro.AliasExterno, registro.ArticuloId);
            if (registro.TipoAnterior?.Length > 1 || !llaves.Add((registro.Origen, registro.AliasExterno, registro.ArticuloId)))
                throw new ActivacionSkusValidacionException("Revisa los tipos anteriores y los vínculos repetidos en la selección.");
        }
    }

    public static void ValidarAltaMasiva(AgregarSkusMasivo input)
    {
        ValidarProgramacion(input.Planta, input.Origen);
        if (!TiposPermitidos.Contains(input.Tipo) || input.Articulos is null || input.Articulos.Count is < 1 or > MaximoLote
            || input.Articulos.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 15)
            || input.Articulos.Distinct(StringComparer.Ordinal).Count() != input.Articulos.Count)
            throw new ActivacionSkusValidacionException("Selecciona de 1 a 500 SKU diferentes, de máximo 15 caracteres, y un tipo válido.");
    }

    private async Task<ResultadoLoteSkus> EjecutarLoteAsync(string planta, IEnumerable<string> origenes,
        string sql, object args, CancellationToken ct)
    {
        try
        {
            ResultadoLoteSkus resultado;
            // Una excepción aborta el lote completo, incluso si el llamador usa una transacción ambiente.
            using (var scope = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled))
            {
                await using var cn = await AbrirAsync(planta, ct);
                foreach (var origen in origenes.Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal))
                    await ObtenerProgramacionAsync(cn, origen, ct, bloquear: true);
                resultado = await cn.QuerySingleAsync<ResultadoLoteSkus>(Cmd(sql, args, ct));
                scope.Complete();
            }
            return resultado;
        }
        catch (SqlException ex) when (ex.Number is 50001 or 50002)
        {
            throw new ActivacionSkusValidacionException(ex.Message);
        }
        catch (SqlException ex) when (ex.Number is 50003 or 50004 or 50005 or 50006)
        {
            throw new ActivacionSkusConflictoException(ex.Message);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            throw new ActivacionSkusConflictoException("Otro usuario modificó los SKU del lote. No se guardó el lote; actualiza el listado y vuelve a intentarlo.");
        }
    }

    public async Task<ResultadoLoteSkus> CambiarTipoMasivoAsync(CambiarTipoSkusMasivo input, string usuario, CancellationToken ct)
    {
        ValidarCambioMasivo(input);
        if (string.IsNullOrWhiteSpace(usuario)) throw new ActivacionSkusValidacionException("Se requiere una sesión de usuario válida.");
        var detalle = JsonSerializer.Serialize(input.Registros);
        var resultado = await EjecutarLoteAsync(input.Planta, input.Registros.Select(r => r.Origen), """
            DECLARE @Seleccion TABLE (N int PRIMARY KEY,Origen nvarchar(15) COLLATE DATABASE_DEFAULT,
                AliasExterno nvarchar(15) COLLATE DATABASE_DEFAULT,ArticuloId nvarchar(15) COLLATE DATABASE_DEFAULT,
                TipoAnterior nvarchar(1));
            INSERT @Seleccion
            SELECT CONVERT(int,j.[key]),s.Origen,s.AliasExterno,s.ArticuloId,s.TipoAnterior
            FROM OPENJSON(@Registros) j CROSS APPLY OPENJSON(j.value) WITH (
                Origen nvarchar(15) '$.Origen',AliasExterno nvarchar(15) '$.AliasExterno',
                ArticuloId nvarchar(15) '$.ArticuloId',TipoAnterior nvarchar(1) '$.TipoAnterior') s;
            IF EXISTS (SELECT 1 FROM @Seleccion WHERE
                Origen COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(15),CONVERT(varchar(15),Origen)) COLLATE Latin1_General_100_BIN2 OR
                AliasExterno COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(15),CONVERT(varchar(15),AliasExterno)) COLLATE Latin1_General_100_BIN2 OR
                ArticuloId COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(15),CONVERT(varchar(15),ArticuloId)) COLLATE Latin1_General_100_BIN2)
                THROW 50002,'El lote contiene códigos incompatibles con la planta.',1;
            IF EXISTS (SELECT 1 FROM @Seleccion GROUP BY Origen,AliasExterno,ArticuloId HAVING COUNT(*)>1)
                THROW 50001,'El lote contiene vínculos repetidos según el cotejamiento de la planta.',1;
            DECLARE @Actual TABLE (N int PRIMARY KEY,Origen varchar(15) COLLATE DATABASE_DEFAULT,
                AliasExterno varchar(15) COLLATE DATABASE_DEFAULT,ArticuloId varchar(15) COLLATE DATABASE_DEFAULT,Tipo varchar(1));
            INSERT @Actual
            SELECT s.N,c.Origen,c.AliasExterno,c.ArticuloId,c.Tipo FROM @Seleccion s
            INNER JOIN dbo.Codigo c WITH (UPDLOCK,HOLDLOCK) ON c.Origen=CONVERT(varchar(15),s.Origen)
                AND c.AliasExterno=CONVERT(varchar(15),s.AliasExterno) AND c.ArticuloId=CONVERT(varchar(15),s.ArticuloId);
            IF (SELECT COUNT(*) FROM @Actual)<>@Cantidad
                THROW 50003,'Un vínculo ya no existe. No se cambió ningún SKU; limpia la selección y consulta nuevamente.',1;
            IF EXISTS (SELECT 1 FROM @Actual a INNER JOIN @Seleccion s ON s.N=a.N
                WHERE EXISTS (SELECT CONVERT(nvarchar(1),a.Tipo) COLLATE Latin1_General_100_BIN2,DATALENGTH(CONVERT(nvarchar(1),a.Tipo))
                    EXCEPT SELECT s.TipoAnterior COLLATE Latin1_General_100_BIN2,DATALENGTH(s.TipoAnterior)))
                THROW 50004,'Un tipo cambió desde la consulta. No se cambió ningún SKU; limpia la selección y consulta nuevamente.',1;
            DECLARE @Esperados int=(SELECT COUNT(*) FROM @Actual WHERE Tipo IS NULL OR Tipo COLLATE Latin1_General_100_BIN2<>@TipoNuevo COLLATE Latin1_General_100_BIN2);
            UPDATE c SET Tipo=@TipoNuevo FROM dbo.Codigo c INNER JOIN @Actual a
                ON c.Origen=a.Origen AND c.AliasExterno=a.AliasExterno AND c.ArticuloId=a.ArticuloId
            WHERE a.Tipo IS NULL OR a.Tipo COLLATE Latin1_General_100_BIN2<>@TipoNuevo COLLATE Latin1_General_100_BIN2;
            DECLARE @Modificados int=@@ROWCOUNT;
            IF @Modificados<>@Esperados THROW 50006,'No se pudo completar el lote; consulta nuevamente.',1;
            SELECT @Cantidad Seleccionados,@Modificados Modificados,@Cantidad-@Modificados SinCambios;
            """, new { Registros = detalle, Cantidad = input.Registros.Count, TipoNuevo = Letra(input.TipoNuevo) }, ct);
        logger.LogInformation("Activacion SKUS: {Usuario} cambió un lote de {Cantidad} vínculos a {Tipo}, {Modificados} modificados, en {Planta} ({BaseDatos}). Selección: {Registros}",
            usuario, resultado.Seleccionados, input.TipoNuevo, resultado.Modificados, input.Planta, Destino(input.Planta).BaseDatos, detalle);
        return resultado;
    }

    private sealed record ArticuloLote(int Orden, string ArticuloId);

    public async Task<ResultadoLoteSkus> AgregarMasivoAsync(AgregarSkusMasivo input, string usuario, CancellationToken ct)
    {
        ValidarAltaMasiva(input);
        if (string.IsNullOrWhiteSpace(usuario)) throw new ActivacionSkusValidacionException("Se requiere una sesión de usuario válida.");
        List<ArticuloLote> articulos;
        await using (var catalogo = await AbrirCatalogoAsync(ct))
        {
            articulos = (await catalogo.QueryAsync<ArticuloLote>(Cmd("""
                SELECT CONVERT(int,j.[key]) Orden,a.ProductoCodigo ArticuloId FROM OPENJSON(@Articulos) j
                INNER JOIN dbo.ArticuloSap a ON a.ProductoCodigo=j.value COLLATE DATABASE_DEFAULT;
                """, new { Articulos = JsonSerializer.Serialize(input.Articulos) }, ct))).ToList();
        }
        if (articulos.Count != input.Articulos.Count || articulos.Select(a => a.Orden).Distinct().Count() != input.Articulos.Count)
            throw new ActivacionSkusValidacionException("Uno de los SKU ya no existe en el catálogo de SIGO. Revisa la selección antes de agregar.");
        if (articulos.Any(a => string.IsNullOrWhiteSpace(a.ArticuloId) || a.ArticuloId.Length > 15))
            throw new ActivacionSkusValidacionException("Un SKU del catálogo excede los 15 caracteres permitidos en Codigo.");
        var detalle = JsonSerializer.Serialize(articulos.OrderBy(a => a.Orden).Select(a => a.ArticuloId));
        var resultado = await EjecutarLoteAsync(input.Planta, new[] { input.Origen }, """
            DECLARE @Articulos TABLE (ArticuloId nvarchar(50) COLLATE DATABASE_DEFAULT);
            INSERT @Articulos SELECT value FROM OPENJSON(@ArticulosJson);
            IF EXISTS (SELECT 1 FROM @Articulos WHERE ArticuloId COLLATE Latin1_General_100_BIN2 <>
                CONVERT(nvarchar(50),CONVERT(varchar(15),ArticuloId)) COLLATE Latin1_General_100_BIN2)
                THROW 50002,'Un SKU contiene caracteres incompatibles con la planta.',1;
            IF EXISTS (SELECT 1 FROM @Articulos GROUP BY ArticuloId HAVING COUNT(*)>1)
                THROW 50001,'La selección contiene SKU repetidos según el cotejamiento de la planta.',1;
            IF EXISTS (SELECT 1 FROM @Articulos a INNER JOIN dbo.Codigo c WITH (UPDLOCK,HOLDLOCK)
                ON c.Origen=@Origen AND c.ArticuloId=CONVERT(varchar(15),a.ArticuloId))
                THROW 50005,'Uno de los SKU ya está ligado a esta programación. No se agregó ninguno; actualiza el catálogo y la selección.',1;
            INSERT dbo.Codigo(Origen,AliasExterno,ArticuloId,Tipo,Precio,Unidad,Estatus,UltimaModificacion,FechaHora,FechaHoraServer)
            SELECT @Origen,CONVERT(varchar(15),ArticuloId),CONVERT(varchar(15),ArticuloId),@Tipo,0,0,1,GETDATE(),GETDATE(),GETDATE()
            FROM @Articulos;
            DECLARE @Modificados int=@@ROWCOUNT;
            IF @Modificados<>@Cantidad THROW 50006,'No se pudo completar el alta del lote; consulta nuevamente.',1;
            SELECT @Cantidad Seleccionados,@Modificados Modificados,0 SinCambios;
            """, new { ArticulosJson = detalle, Cantidad = input.Articulos.Count, Origen = Clave(input.Origen), Tipo = Letra(input.Tipo) }, ct);
        logger.LogInformation("Activacion SKUS: {Usuario} agregó un lote de {Cantidad} SKU a {Origen}, tipo {Tipo}, en {Planta} ({BaseDatos}). Artículos: {Articulos}",
            usuario, resultado.Modificados, input.Origen, input.Tipo, input.Planta, Destino(input.Planta).BaseDatos, detalle);
        return resultado;
    }

    public async Task<SubclasificacionesSkusListado> ListarSubclasificacionesAsync(string? planta, string? busqueda,
        string? estado, int pagina, CancellationToken ct)
    {
        ValidarPlantaSubclasificacion(planta);
        var texto = busqueda?.Trim() ?? "";
        estado ??= "";
        if (texto.Length > 100 || pagina is < 1 or > 100000 || estado is not ("" or "N" or "VACIO" or "OTROS"))
            throw new ActivacionSkusValidacionException("Revisa la búsqueda, el estado de subclasificación y la página.");
        const int tamano = 20;
        const string filtro = """
            WHERE (@Vacio=1 OR a.ArticuloId LIKE @Busqueda ESCAPE '~' OR a.Nombre LIKE @Busqueda ESCAPE '~')
              AND (@Estado='' OR (@Estado='N' AND LTRIM(RTRIM(a.Clasifica2)) COLLATE Latin1_General_100_BIN2='N')
                OR (@Estado='VACIO' AND (a.Clasifica2 IS NULL OR LTRIM(RTRIM(a.Clasifica2))=''))
                OR (@Estado='OTROS' AND NULLIF(LTRIM(RTRIM(a.Clasifica2)),'') IS NOT NULL
                    AND LTRIM(RTRIM(a.Clasifica2)) COLLATE Latin1_General_100_BIN2<>'N'))
            """;
        await using var cn = await AbrirAsync(planta, ct);
        using var result = await cn.QueryMultipleAsync(Cmd($"""
            SELECT COUNT(*) FROM dbo.Articulo a {filtro};
            SELECT a.ArticuloId,a.Nombre,a.Clasifica2 FROM dbo.Articulo a {filtro}
            ORDER BY a.ArticuloId OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;
            """, new { Vacio = texto.Length == 0, Busqueda = EscaparBusqueda(texto), Estado = estado,
                Offset = (pagina - 1) * tamano, Tamano = tamano }, ct));
        var total = await result.ReadSingleAsync<int>();
        return new((await result.ReadAsync<ArticuloSubclasificacionSku>()).ToList(), total, pagina, tamano, false);
    }

    private static void ValidarPlantaSubclasificacion(string? planta)
    {
        _ = Destino(planta);
        if (planta != "TIF")
            throw new ActivacionSkusValidacionException("Subclasificación está disponible únicamente para Planta TIF.");
    }

    public static void ValidarSubclasificacion(GuardarSubclasificacionSkus input)
    {
        ValidarPlantaSubclasificacion(input.Planta);
        if (input.Cambios is null || input.Cambios.Count is < 1 or > MaximoLote)
            throw new ActivacionSkusValidacionException("Selecciona de 1 a 500 cambios de subclasificación.");
        var articulos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cambio in input.Cambios)
        {
            if (cambio is null || string.IsNullOrWhiteSpace(cambio.ArticuloId) || cambio.ArticuloId.Length > 15
                || cambio.Clasifica2Anterior?.Length > 50 || cambio.AsignarN is null || !articulos.Add(cambio.ArticuloId))
                throw new ActivacionSkusValidacionException("Revisa los SKU, sus valores anteriores y la acción de agregar o quitar N.");
            if (!new ArticuloSubclasificacionSku(cambio.ArticuloId, null, cambio.Clasifica2Anterior).PuedeCambiar)
                throw new ActivacionSkusValidacionException("Solo se puede cambiar una subclasificación vacía o con N. Los demás valores se conservan.");
        }
    }

    public async Task<ResultadoLoteSkus> GuardarSubclasificacionAsync(GuardarSubclasificacionSkus input,
        string usuario, CancellationToken ct)
    {
        ValidarSubclasificacion(input);
        if (string.IsNullOrWhiteSpace(usuario)) throw new ActivacionSkusValidacionException("Se requiere una sesión de usuario válida.");
        var detalle = JsonSerializer.Serialize(input.Cambios.OrderBy(c => c.ArticuloId, StringComparer.Ordinal));
        ResultadoLoteSkus resultado;
        try
        {
            using var scope = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            await using var cn = await AbrirAsync(input.Planta, ct);
            // Una automatización existente puede escribir en otras BD aun cuando el UPDATE solo incluya Clasifica2.
            if (await cn.ExecuteScalarAsync<int>(Cmd("""
                SELECT COUNT(*) FROM sys.triggers t WHERE t.parent_id=OBJECT_ID('dbo.Articulo') AND t.is_disabled=0
                    AND (OBJECT_DEFINITION(t.object_id) IS NULL
                        OR CHARINDEX(N'SIGO_SUBCLASIFICACION_GUARD_V1',OBJECT_DEFINITION(t.object_id))=0);
                """, null, ct)) > 0)
                throw new ActivacionSkusValidacionException("Esta planta tiene un trigger de Articulo pendiente de ajustar. Solo se permite consultar hasta proteger las actualizaciones exclusivas de Clasifica2.");
            resultado = await cn.QuerySingleAsync<ResultadoLoteSkus>(Cmd("""
                DECLARE @Cambios TABLE (ArticuloId nvarchar(15) COLLATE DATABASE_DEFAULT,
                    Clasifica2Anterior nvarchar(50),AsignarN bit);
                INSERT @Cambios SELECT ArticuloId,Clasifica2Anterior,AsignarN FROM OPENJSON(@CambiosJson)
                    WITH (ArticuloId nvarchar(15) '$.ArticuloId',Clasifica2Anterior nvarchar(50) '$.Clasifica2Anterior',AsignarN bit '$.AsignarN');
                IF EXISTS (SELECT 1 FROM @Cambios GROUP BY ArticuloId HAVING COUNT(*)>1)
                    THROW 50011,'Un SKU aparece más de una vez en los cambios de subclasificación.',1;
                IF EXISTS (SELECT 1 FROM @Cambios WHERE ArticuloId COLLATE Latin1_General_100_BIN2<>
                    CONVERT(nvarchar(15),CONVERT(varchar(15),ArticuloId)) COLLATE Latin1_General_100_BIN2)
                    THROW 50012,'Un SKU contiene caracteres incompatibles con la planta.',1;
                DECLARE @Actual TABLE (ArticuloId varchar(15) COLLATE DATABASE_DEFAULT PRIMARY KEY,
                    Clasifica2 varchar(50),TieneN bit);
                INSERT @Actual SELECT a.ArticuloId,a.Clasifica2,
                    CASE WHEN LTRIM(RTRIM(a.Clasifica2)) COLLATE Latin1_General_100_BIN2='N' THEN 1 ELSE 0 END
                FROM dbo.Articulo a WITH (UPDLOCK,HOLDLOCK) INNER JOIN @Cambios c ON a.ArticuloId=CONVERT(varchar(15),c.ArticuloId);
                IF (SELECT COUNT(*) FROM @Actual)<>@Cantidad
                    THROW 50013,'Un SKU ya no existe. No se guardó ningún cambio; descarta los pendientes y consulta nuevamente.',1;
                IF EXISTS (SELECT 1 FROM @Actual a INNER JOIN @Cambios c ON a.ArticuloId=CONVERT(varchar(15),c.ArticuloId)
                    WHERE EXISTS (SELECT CONVERT(nvarchar(50),a.Clasifica2) COLLATE Latin1_General_100_BIN2,DATALENGTH(CONVERT(nvarchar(50),a.Clasifica2))
                        EXCEPT SELECT c.Clasifica2Anterior COLLATE Latin1_General_100_BIN2,DATALENGTH(c.Clasifica2Anterior)))
                    THROW 50014,'La subclasificación cambió desde la consulta. No se guardó el lote; descarta los pendientes y consulta nuevamente.',1;
                IF EXISTS (SELECT 1 FROM @Actual WHERE TieneN=0 AND NULLIF(LTRIM(RTRIM(Clasifica2)),'') IS NOT NULL)
                    THROW 50015,'Un SKU tiene otra subclasificación que debe conservarse. No se guardó ningún cambio.',1;
                DECLARE @Esperados int=(SELECT COUNT(*) FROM @Actual a INNER JOIN @Cambios c
                    ON a.ArticuloId=CONVERT(varchar(15),c.ArticuloId) WHERE a.TieneN<>c.AsignarN);
                DECLARE @Modificados int=0;
                IF @Esperados>0
                BEGIN
                    UPDATE a SET Clasifica2=CASE WHEN c.AsignarN=1 THEN 'N' ELSE '' END
                    FROM dbo.Articulo a INNER JOIN @Cambios c ON a.ArticuloId=CONVERT(varchar(15),c.ArticuloId)
                    INNER JOIN @Actual previo ON previo.ArticuloId=a.ArticuloId WHERE previo.TieneN<>c.AsignarN;
                    SET @Modificados=@@ROWCOUNT;
                END;
                IF @Modificados<>@Esperados THROW 50016,'No se pudo completar la subclasificación. No se guardó el lote.',1;
                SELECT @Cantidad Seleccionados,@Modificados Modificados,@Cantidad-@Modificados SinCambios;
                """, new { CambiosJson = detalle, Cantidad = input.Cambios.Count }, ct));
            scope.Complete();
        }
        catch (SqlException ex) when (ex.Number is 50011 or 50012)
        {
            throw new ActivacionSkusValidacionException(ex.Message);
        }
        catch (SqlException ex) when (ex.Number is >= 50013 and <= 50016)
        {
            throw new ActivacionSkusConflictoException(ex.Message);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            throw new ActivacionSkusConflictoException("Otro usuario está modificando los artículos. No se guardó el lote; consulta nuevamente.");
        }
        logger.LogInformation("Subclasificacion SKUS: {Usuario} actualizó exclusivamente Articulo.Clasifica2 en {Planta} ({BaseDatos}); {Modificados} cambios. Detalle: {Cambios}",
            usuario, input.Planta, Destino(input.Planta).BaseDatos, resultado.Modificados, detalle);
        return resultado;
    }

    public async Task<IReadOnlyList<ProgramacionSku>> ProgramacionesAsync(string? planta, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(planta, ct);
        return (await cn.QueryAsync<ProgramacionSku>(Cmd("""
            SELECT p.ProveedorId,p.Nombre,COUNT(c.ArticuloId) Registros
            FROM dbo.Proveedor p LEFT JOIN dbo.Codigo c ON c.Origen=p.ProveedorId
            WHERE p.Nombre LIKE @Prefijo
            GROUP BY p.ProveedorId,p.Nombre ORDER BY p.Nombre,p.ProveedorId;
            """, new { Prefijo = "PROGRAMACION%" }, ct))).ToList();
    }

    public async Task<ActivacionSkusListado> ListarAsync(string? planta, string? origen, string? busqueda,
        string? tipo, int pagina, CancellationToken ct)
    {
        // No recortar Origen: puede contener espacios significativos en la llave.
        origen = string.IsNullOrEmpty(origen) ? null : origen;
        var texto = busqueda?.Trim() ?? "";
        tipo = string.IsNullOrEmpty(tipo) ? null : tipo;
        if (origen?.Length > 15 || texto.Length > 100)
            throw new ActivacionSkusValidacionException("Revisa la programación y la búsqueda (máximo 100 caracteres).");
        if (tipo is not null && !TiposPermitidos.Contains(tipo))
            throw new ActivacionSkusValidacionException("El tipo debe ser P, F, C o S.");
        if (pagina is < 1 or > 100000)
            throw new ActivacionSkusValidacionException("Selecciona una página válida.");
        const int tamano = 25;
        var filtroTipo = " AND (@Tipo IS NULL OR c.Tipo=@Tipo)";
        await using var cn = await AbrirAsync(planta, ct);
        using var result = await cn.QueryMultipleAsync(Cmd($"""
            SELECT c.Tipo,COUNT(*) Registros {Relacion} {Filtro} GROUP BY c.Tipo;
            SELECT COUNT(*) {Relacion} {Filtro} {filtroTipo};
            SELECT {Columnas} {Relacion} {Filtro} {filtroTipo}
            ORDER BY c.ArticuloId,c.Origen,c.AliasExterno
            OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;
            """, new { Prefijo = "PROGRAMACION%", Origen = Clave(origen),
                Vacio = texto.Length == 0, Busqueda = EscaparBusqueda(texto), Tipo = Letra(tipo),
                Offset = (pagina - 1) * tamano, Tamano = tamano }, ct));
        var conteos = (await result.ReadAsync<ConteoTipoSku>()).ToList();
        var total = await result.ReadSingleAsync<int>();
        var registros = (await result.ReadAsync<CodigoSku>()).ToList();
        return new(registros, total, pagina, tamano, conteos);
    }

    public static void ValidarCambio(CambiarTipoSku input)
    {
        ValidarLlave(input.Planta, input.Origen, input.AliasExterno, input.ArticuloId);
        if (input.OrigenNuevo is not null) ValidarProgramacion(input.Planta, input.OrigenNuevo);
        if (input.TipoAnterior?.Length > 1 || !TiposPermitidos.Contains(input.TipoNuevo))
            throw new ActivacionSkusValidacionException("El tipo nuevo debe ser P, F, C o S.");
        if (string.Equals(input.TipoAnterior, input.TipoNuevo, StringComparison.Ordinal)
            && string.Equals(input.Origen, input.OrigenNuevo ?? input.Origen, StringComparison.Ordinal))
            throw new ActivacionSkusValidacionException("Cambia el tipo o la programación para guardar.");
    }

    public async Task<CodigoSku> CambiarTipoAsync(CambiarTipoSku input, string usuario, CancellationToken ct)
    {
        ValidarCambio(input);
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ActivacionSkusValidacionException("Se requiere una sesión de usuario válida.");
        await using var cn = await AbrirAsync(input.Planta, ct);
        // Permite que un llamador (incluidas las pruebas con rollback) controle una transacción ambiente.
        await using var tx = Transaction.Current is null
            ? (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var origenNuevo = input.OrigenNuevo ?? input.Origen;
        // Mismo orden de bloqueo para altas, bajas y movimientos, incluso entre dos programaciones.
        var programaciones = new Dictionary<string, ProgramacionSku>(StringComparer.Ordinal);
        foreach (var origen in new[] { input.Origen, origenNuevo }.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            programaciones[origen] = await ObtenerProgramacionAsync(cn, origen, ct, tx, bloquear: true);
        var destino = programaciones[origenNuevo];
        var args = new { Origen = Clave(input.Origen), AliasExterno = Clave(input.AliasExterno),
            ArticuloId = Clave(input.ArticuloId), Prefijo = "PROGRAMACION%" };
        // La llave completa evita modificar otros alias o programaciones del mismo SKU.
        var actual = await BloquearRegistroAsync(cn, tx, input.Origen, input.AliasExterno, input.ArticuloId, ct);
        if (!string.Equals(actual.Tipo, input.TipoAnterior, StringComparison.Ordinal))
            throw new ActivacionSkusConflictoException("El tipo cambió desde que consultaste el SKU. Actualiza el listado y vuelve a intentarlo.");

        var mover = await cn.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN @Origen=@OrigenNuevo THEN 0 ELSE 1 END;",
            new { args.Origen, OrigenNuevo = Clave(destino.ProveedorId) }, ct, tx)) == 1;
        if (mover) await ExigirSkuNoAsignadoAsync(cn, tx, destino.ProveedorId, actual.ArticuloId, ct);

        var modificados = await cn.ExecuteAsync(Cmd("""
            UPDATE dbo.Codigo SET Tipo=@TipoNuevo,Origen=@OrigenNuevo
            WHERE Origen=@Origen AND AliasExterno=@AliasExterno AND ArticuloId=@ArticuloId;
            """, new { args.Origen, args.AliasExterno, args.ArticuloId, TipoNuevo = Letra(input.TipoNuevo),
                OrigenNuevo = Clave(destino.ProveedorId) }, ct, tx));
        if (modificados != 1)
            throw new ActivacionSkusConflictoException("No se pudo identificar un único registro. Actualiza el listado.");
        if (tx is not null) await tx.CommitAsync(ct);
        logger.LogInformation("Activacion SKUS: {Usuario} cambió Codigo ({Origen}, {AliasExterno}, {ArticuloId}) de {TipoAnterior} a {TipoNuevo}, programación destino {OrigenNuevo}, en {Planta} ({BaseDatos}).",
            usuario, actual.Origen, actual.AliasExterno, actual.ArticuloId, actual.Tipo, input.TipoNuevo,
            destino.ProveedorId, input.Planta, Destino(input.Planta).BaseDatos);
        actual.Tipo = input.TipoNuevo;
        actual.Origen = destino.ProveedorId;
        actual.Programacion = destino.Nombre;
        return actual;
    }

    public static void ValidarEliminacion(EliminarSkuProgramacion input)
    {
        ValidarLlave(input.Planta, input.Origen, input.AliasExterno, input.ArticuloId);
        if (input.TipoAnterior?.Length > 1)
            throw new ActivacionSkusValidacionException("El tipo anterior no es válido.");
    }

    public async Task<CodigoSku> EliminarAsync(EliminarSkuProgramacion input, string usuario, CancellationToken ct)
    {
        ValidarEliminacion(input);
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ActivacionSkusValidacionException("Se requiere una sesión de usuario válida.");
        await using var cn = await AbrirAsync(input.Planta, ct);
        await using var tx = Transaction.Current is null
            ? (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await ObtenerProgramacionAsync(cn, input.Origen, ct, tx, bloquear: true);
        var actual = await BloquearRegistroAsync(cn, tx, input.Origen, input.AliasExterno, input.ArticuloId, ct);
        if (!string.Equals(actual.Tipo, input.TipoAnterior, StringComparison.Ordinal))
            throw new ActivacionSkusConflictoException("El registro cambió desde que lo consultaste. Actualiza el listado antes de eliminarlo.");
        try
        {
            var modificados = await cn.ExecuteAsync(Cmd("""
                DELETE FROM dbo.Codigo
                WHERE Origen=@Origen AND AliasExterno=@AliasExterno AND ArticuloId=@ArticuloId;
                """, new { Origen = Clave(actual.Origen), AliasExterno = Clave(actual.AliasExterno),
                    ArticuloId = Clave(actual.ArticuloId) }, ct, tx));
            if (modificados != 1)
                throw new ActivacionSkusConflictoException("No se pudo identificar un único vínculo. Actualiza el listado.");
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw new ActivacionSkusConflictoException("Este vínculo está en uso por otros registros y no se puede eliminar.");
        }
        logger.LogInformation("Activacion SKUS: {Usuario} eliminó Codigo ({Origen}, {AliasExterno}, {ArticuloId}) en {Planta} ({BaseDatos}).",
            usuario, actual.Origen, actual.AliasExterno, actual.ArticuloId, input.Planta, Destino(input.Planta).BaseDatos);
        return actual;
    }
}
