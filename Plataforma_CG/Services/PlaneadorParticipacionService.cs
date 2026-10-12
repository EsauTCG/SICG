using Dapper;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Transactions;

namespace Plataforma_CG.Services;

public sealed class ParticipacionValidacionException(string message) : Exception(message);
public sealed class ParticipacionConflictoException(string message) : Exception(message);

/// <summary>Edición del porcentaje del catálogo, únicamente en SIGO local de Pruebas Esau.</summary>
public sealed class PlaneadorParticipacionService(IConfiguration configuration)
{
    public const string ServidorPruebas = @"DESKTOP-UB7IMQF\SQL2026";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Columnas = "p.Id,p.ProductoCodigo Sku,p.fk_Clasificacion ClasificacionId,p.Porcentaje,p.fk_SubClas SubClasificacionId,p.LineaCodigo,p.PartSub";

    public static void ValidarDestino(string? cadena)
    {
        if (string.IsNullOrWhiteSpace(cadena)) throw new InvalidOperationException("Falta CadenaSQLSIGO de Pruebas Esau.");
        var target = new SqlConnectionStringBuilder(cadena);
        if (!string.Equals(target.DataSource, ServidorPruebas, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(target.InitialCatalog, "SIGO", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Participación sólo está habilitada en SIGO local de Pruebas Esau.");
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var cs = configuration.GetConnectionString("CadenaSQLSIGO");
        ValidarDestino(cs);
        var cn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 10, ApplicationName = "SIGO Planeador - Participacion" }.ConnectionString);
        try { await cn.OpenAsync(ct); return cn; }
        catch { await cn.DisposeAsync(); throw; }
    }

    private static CommandDefinition Cmd(string sql, object? args, SqlTransaction? tx, CancellationToken ct)
        => new(sql, args, tx, commandTimeout: 30, cancellationToken: ct);
    private static string Snapshot(IEnumerable<ParticipacionCatalogoFila> rows) => JsonSerializer.Serialize(rows, JsonOptions);
    private static string Version(IEnumerable<ParticipacionCatalogoFila> rows)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(rows))));
    private static string Buscar(string q) => "%" + q.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    public async Task<ParticipacionCatalogoListado> ListarAsync(string? busqueda, int? clasificacionId, int pagina, CancellationToken ct)
    {
        var q = busqueda?.Trim() ?? "";
        if (q.Length > 100 || clasificacionId <= 0) throw new ParticipacionValidacionException("Revisa los filtros de búsqueda.");
        pagina = Math.Clamp(pagina, 1, 100000);
        const int tamano = 25;
        const string grupos = "SELECT ProductoCodigo,fk_Clasificacion,MIN(Porcentaje) Minimo,MAX(Porcentaje) Maximo,COUNT(*) Registros FROM dbo.Participacion GROUP BY ProductoCodigo,fk_Clasificacion";
        const string joins = "LEFT JOIN dbo.ArticuloSap a ON a.ProductoCodigo=g.ProductoCodigo LEFT JOIN dbo.Clasificacion c ON c.Id=g.fk_Clasificacion";
        const string filtro = "WHERE (@ClasificacionId IS NULL OR g.fk_Clasificacion=@ClasificacionId) AND (@Vacio=1 OR g.ProductoCodigo LIKE @Q ESCAPE '~' OR a.ProductoNombre LIKE @Q ESCAPE '~')";
        await using var cn = await AbrirAsync(ct);
        using var result = await cn.QueryMultipleAsync(Cmd($"""
            SELECT COUNT(DISTINCT ProductoCodigo) Skus,COUNT(*) Registros,
                (SELECT COUNT(*) FROM ({grupos}) t) Grupos FROM dbo.Participacion;
            SELECT c.Id,c.Nombre FROM dbo.Clasificacion c WHERE EXISTS(SELECT 1 FROM dbo.Participacion p WHERE p.fk_Clasificacion=c.Id) ORDER BY c.Nombre;
            SELECT COUNT(*) FROM ({grupos}) g {joins} {filtro};
            SELECT g.ProductoCodigo Sku,g.fk_Clasificacion ClasificacionId,g.Minimo,g.Maximo,g.Registros,
                a.ProductoNombre Nombre,c.Nombre Clasificacion,mp.Masters,sc.SubClasificaciones,
                h.Usuario UsuarioUltimoCambio,h.Fecha FechaUltimoCambio,
                (SELECT {Columnas} FROM dbo.Participacion p WHERE p.ProductoCodigo=g.ProductoCodigo AND p.fk_Clasificacion=g.fk_Clasificacion ORDER BY p.Id FOR JSON PATH, INCLUDE_NULL_VALUES) EstadoJson
            FROM ({grupos}) g {joins}
            OUTER APPLY (SELECT STRING_AGG(n.Nombre,', ') Masters FROM
                (SELECT DISTINCT m.Nombre FROM dbo.MasterProd p JOIN dbo.Masters m ON m.Id=p.MasterID WHERE p.SKU=g.ProductoCodigo) n) mp
            OUTER APPLY (SELECT STRING_AGG(n.Nombre,', ') SubClasificaciones FROM
                (SELECT DISTINCT s.Nombre FROM dbo.Participacion p JOIN dbo.SubClasif s ON s.Id=p.fk_SubClas
                 WHERE p.ProductoCodigo=g.ProductoCodigo AND p.fk_Clasificacion=g.fk_Clasificacion) n) sc
            OUTER APPLY (SELECT TOP(1) Usuario,Fecha FROM dbo.ParticipacionHistorial
                WHERE ProductoCodigo=g.ProductoCodigo AND ClasificacionId=g.fk_Clasificacion ORDER BY Id DESC) h
            {filtro} ORDER BY c.Nombre,g.ProductoCodigo OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;
            """, new { ClasificacionId = clasificacionId, Vacio = q.Length == 0, Q = Buscar(q), Offset = (pagina - 1) * tamano, Tamano = tamano }, null, ct));
        var conteos = await result.ReadSingleAsync<ParticipacionCatalogoConteos>();
        var clasificaciones = (await result.ReadAsync<ParticipacionCatalogoClasificacion>()).ToList();
        var total = await result.ReadSingleAsync<int>();
        var registros = (await result.ReadAsync<ParticipacionCatalogoRegistro>()).ToList();
        foreach (var r in registros)
        {
            var filas = JsonSerializer.Deserialize<List<ParticipacionCatalogoFila>>(r.EstadoJson, JsonOptions)!;
            // Mostrar los mismos valores que protege la versión, incluso ante cambios simultáneos.
            r.Registros = filas.Count;
            if (filas.Count == 0) continue;
            r.Minimo = filas.Min(f => f.Porcentaje); r.Maximo = filas.Max(f => f.Porcentaje);
            r.Version = Version(filas);
        }
        registros.RemoveAll(r => r.Registros == 0);
        return new(registros, total, pagina, tamano, conteos, clasificaciones);
    }

    public async Task<int> GuardarAsync(ParticipacionCatalogoGuardar input, string usuario, CancellationToken ct)
    {
        usuario = usuario.Trim(); input.Sku = input.Sku?.Trim() ?? "";
        if (input.Sku.Length is < 1 or > 7 || input.ClasificacionId <= 0 || string.IsNullOrWhiteSpace(usuario) || usuario.Length > 80)
            throw new ParticipacionValidacionException("Se requiere un SKU, tipo de plan y usuario de sesión válidos.");
        if (!input.Porcentaje.HasValue || input.Porcentaje < 0 || input.Porcentaje > 100 || decimal.Round(input.Porcentaje.Value, 2) != input.Porcentaje)
            throw new ParticipacionValidacionException("El porcentaje debe estar entre 0 y 100, con hasta 2 decimales.");
        await using var cn = await AbrirAsync(ct);
        await using var tx = Transaction.Current is null ? (SqlTransaction)await cn.BeginTransactionAsync(ct) : null;
        var filas = (await cn.QueryAsync<ParticipacionCatalogoFila>(Cmd($"""
            SELECT {Columnas} FROM dbo.Participacion p WITH (UPDLOCK,HOLDLOCK)
            WHERE p.ProductoCodigo=@Sku AND p.fk_Clasificacion=@ClasificacionId ORDER BY p.Id;
            """, input, tx, ct))).ToList();
        if (filas.Count == 0) throw new KeyNotFoundException("Este SKU ya no tiene registros de participación en el tipo de plan seleccionado.");
        if (!string.Equals(Version(filas), input.Version, StringComparison.Ordinal))
            throw new ParticipacionConflictoException("La participación cambió mientras la editabas. Actualiza el catálogo y vuelve a abrirla.");
        if (filas.All(r => r.Porcentaje == input.Porcentaje.Value)) return 0;
        var antes = Snapshot(filas);
        decimal? porcentajeAntes = filas.Select(r => r.Porcentaje).Distinct().Count() == 1 ? filas[0].Porcentaje : null;
        // Sólo el porcentaje: conservar subclasificaciones, PartSub, líneas, SKU e IDs.
        var actualizados = await cn.ExecuteAsync(Cmd("UPDATE dbo.Participacion SET Porcentaje=@Porcentaje WHERE ProductoCodigo=@Sku AND fk_Clasificacion=@ClasificacionId;", input, tx, ct));
        filas.ForEach(r => r.Porcentaje = input.Porcentaje.Value);
        await cn.ExecuteAsync(Cmd("""
            INSERT INTO dbo.ParticipacionHistorial(ProductoCodigo,ClasificacionId,PorcentajeAntes,PorcentajeDespues,Registros,Usuario,DatosAntes,DatosDespues)
            VALUES(@Sku,@ClasificacionId,@PorcentajeAntes,@Porcentaje,@Registros,@Usuario,@Antes,@Despues);
            """, new { Sku = filas[0].Sku, input.ClasificacionId, PorcentajeAntes = porcentajeAntes, input.Porcentaje, Registros = actualizados, Usuario = usuario, Antes = antes, Despues = Snapshot(filas) }, tx, ct));
        if (tx is not null) await tx.CommitAsync(ct);
        return actualizados;
    }

    public async Task<IReadOnlyList<ParticipacionCatalogoCambio>> HistorialAsync(string sku, int clasificacionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sku) || sku.Length > 7 || clasificacionId <= 0) throw new ParticipacionValidacionException("Selecciona un SKU y tipo de plan válidos.");
        await using var cn = await AbrirAsync(ct);
        return (await cn.QueryAsync<ParticipacionCatalogoCambio>(Cmd("""
            SELECT Id,PorcentajeAntes,PorcentajeDespues,Registros,Usuario,Fecha FROM dbo.ParticipacionHistorial
            WHERE ProductoCodigo=@Sku AND ClasificacionId=@ClasificacionId ORDER BY Id DESC;
            """, new { Sku = sku.Trim(), ClasificacionId = clasificacionId }, null, ct))).ToList();
    }
}
