using Dapper;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;
using Plataforma_CG.Models.Operaciones.Planeacion.Diaria;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Transactions;

namespace Plataforma_CG.Services;

public sealed class PlanDiarioCatalogoValidacionException(string message) : Exception(message);
public sealed class PlanDiarioCatalogoConflictoException(string message) : Exception(message);

/// <summary>Plantilla global de SKU por tipo de plan y sus capturas diarias, en SIGO.</summary>
public sealed partial class PlaneadorPlanDiarioCatalogoService(IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Columnas = "p.Id,p.ProductoCodigo Sku,p.fk_Clasificacion ClasificacionId,p.Porcentaje,p.fk_SubClas SubClasificacionId,p.LineaCodigo,p.PartSub";
    private static CommandDefinition Cmd(string sql, object? args, SqlTransaction? tx, CancellationToken ct)
        => new(sql, args, tx, commandTimeout: 30, cancellationToken: ct);
    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);
    private static string Version(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json(value))));
    private static string Buscar(string q) => "%" + q.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var cadena = configuration.GetConnectionString("CadenaSQLSIGO");
        PlaneadorParticipacionService.ValidarDestino(cadena);
        var cn = new SqlConnection(new SqlConnectionStringBuilder(cadena) { ConnectTimeout = 10, ApplicationName = "SIGO Planeador - Catalogo diario" }.ConnectionString);
        try { await cn.OpenAsync(ct); return cn; }
        catch { await cn.DisposeAsync(); throw; }
    }

    // Una edición global y el guardado de un día no pueden intercalarse.
    internal static async Task BloquearAsync(SqlConnection cn, SqlTransaction? tx, bool exclusivo, CancellationToken ct)
    {
        var code = await cn.ExecuteScalarAsync<int>(Cmd("""
            DECLARE @resultado int;
            EXEC @resultado=sys.sp_getapplock @Resource=N'Planeador.PlanDiario.Catalogo',
                @LockMode=@Modo,@LockOwner='Transaction',@LockTimeout=10000;
            SELECT @resultado;
            """, new { Modo = exclusivo ? "Exclusive" : "Shared" }, tx, ct));
        if (code < 0) throw new PlanDiarioCatalogoConflictoException("El planeador está guardando otro cambio. Intenta nuevamente.");
    }

    private static async Task<List<PlanDiarioCatalogoFila>> FilasAsync(SqlConnection cn, SqlTransaction? tx, string sku, int clasificacionId, CancellationToken ct)
        => (await cn.QueryAsync<PlanDiarioCatalogoFila>(Cmd($"SELECT {Columnas} FROM dbo.Participacion p WHERE p.ProductoCodigo=@Sku AND p.fk_Clasificacion=@ClasificacionId ORDER BY p.Id;",
            new { Sku = sku, ClasificacionId = clasificacionId }, tx, ct))).ToList();

    public async Task<object> ListarAsync(string? busqueda, int? clasificacionId, int pagina, CancellationToken ct)
    {
        var q = busqueda?.Trim() ?? "";
        if (q.Length > 100 || clasificacionId <= 0) throw new PlanDiarioCatalogoValidacionException("Revisa los filtros de búsqueda.");
        pagina = Math.Clamp(pagina, 1, 100000);
        const int tamano = 25;
        const string grupos = "SELECT DISTINCT ProductoCodigo,fk_Clasificacion FROM dbo.Participacion";
        const string joins = "LEFT JOIN dbo.ArticuloSap a ON a.ProductoCodigo=g.ProductoCodigo JOIN dbo.Clasificacion c ON c.Id=g.fk_Clasificacion";
        const string filtro = "WHERE (@ClasificacionId IS NULL OR g.fk_Clasificacion=@ClasificacionId) AND (@Vacio=1 OR g.ProductoCodigo LIKE @Q ESCAPE '~' OR a.ProductoNombre LIKE @Q ESCAPE '~' OR a.U_MASTER LIKE @Q ESCAPE '~')";
        await using var cn = await AbrirAsync(ct);
        using var result = await cn.QueryMultipleAsync(Cmd($"""
            SELECT COUNT(*) FROM ({grupos}) g {joins} {filtro};
            SELECT g.ProductoCodigo Sku,g.fk_Clasificacion ClasificacionId,c.Nombre Clasificacion,
                a.ProductoNombre Nombre,a.U_MASTER Master,h.Usuario UsuarioUltimoCambio,h.Fecha FechaUltimoCambio,
                (SELECT {Columnas} FROM dbo.Participacion p WHERE p.ProductoCodigo=g.ProductoCodigo AND p.fk_Clasificacion=g.fk_Clasificacion ORDER BY p.Id FOR JSON PATH,INCLUDE_NULL_VALUES) EstadoJson
            FROM ({grupos}) g {joins}
            OUTER APPLY (SELECT TOP(1) Usuario,Fecha FROM dbo.PlanDiarioCatalogoHistorial
                WHERE (Sku=g.ProductoCodigo AND ClasificacionId=g.fk_Clasificacion)
                   OR (SkuAnterior=g.ProductoCodigo AND ClasificacionAnteriorId=g.fk_Clasificacion) ORDER BY Id DESC) h
            {filtro} ORDER BY c.Nombre,g.ProductoCodigo OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;
            SELECT Id,Nombre,SKU Tipo FROM dbo.Clasificacion ORDER BY Nombre;
            SELECT s.Id,s.Nombre,c.Id ClasificacionId FROM dbo.SubClasif s JOIN dbo.Clasificacion c ON c.SKU=s.fk_Clasificacion ORDER BY c.Nombre,s.Nombre;
            """, new { ClasificacionId = clasificacionId, Vacio = q.Length == 0, Q = Buscar(q), Offset = (pagina - 1) * tamano, Tamano = tamano }, null, ct));
        var total = await result.ReadSingleAsync<int>();
        var registros = (await result.ReadAsync<PlanDiarioCatalogoRegistro>()).ToList();
        foreach (var r in registros)
        {
            r.Filas = JsonSerializer.Deserialize<List<PlanDiarioCatalogoFila>>(r.EstadoJson, JsonOptions)!;
            r.Version = Version(r.Filas);
        }
        var clasificaciones = (await result.ReadAsync<PlanDiarioCatalogoOpcion>()).ToList();
        var subclasificaciones = (await result.ReadAsync<PlanDiarioCatalogoOpcion>()).ToList();
        return new { registros, totalFiltrado = total, pagina, tamanoPagina = tamano, clasificaciones, subclasificaciones,
            versionCatalogo = await VersionCatalogoAsync(cn, null, ct), reglas = PlaneadorAfectaciones.Reglas };
    }

    public async Task<IReadOnlyList<PlanDiarioCatalogoArticulo>> ArticulosAsync(string? busqueda, CancellationToken ct)
    {
        var q = busqueda?.Trim() ?? "";
        if (q.Length < 2) return [];
        if (q.Length > 100) throw new PlanDiarioCatalogoValidacionException("La búsqueda admite hasta 100 caracteres.");
        await using var cn = await AbrirAsync(ct);
        // Se consulta todo ArticuloSap, sin depender de Masters/MasterProd ni de SKU ya planeados.
        return (await cn.QueryAsync<PlanDiarioCatalogoArticulo>(Cmd("""
            SELECT TOP(30) ProductoCodigo Sku,ProductoNombre Nombre,U_MASTER Master FROM dbo.ArticuloSap
            WHERE ProductoCodigo LIKE @Q ESCAPE '~' OR ProductoNombre LIKE @Q ESCAPE '~'
            ORDER BY CASE WHEN ProductoCodigo=@Exacto THEN 0 ELSE 1 END,ProductoCodigo;
            """, new { Q = Buscar(q), Exacto = q }, null, ct))).ToList();
    }

    private static void ValidarUsuario(string usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario) || usuario.Length > 80)
            throw new PlanDiarioCatalogoValidacionException("Se requiere un usuario de sesión válido.");
    }

    private static void VerificarVersion(List<PlanDiarioCatalogoFila> filas, string? version)
    {
        if (filas.Count == 0) throw new KeyNotFoundException("Este SKU ya no está en el tipo de plan seleccionado.");
        if (!string.Equals(Version(filas), version, StringComparison.Ordinal))
            throw new PlanDiarioCatalogoConflictoException("El catálogo cambió mientras lo editabas. Actualiza el listado y vuelve a abrir el SKU.");
    }

    private static async Task<object> EstadoAsync(SqlConnection cn, SqlTransaction? tx, string sku, int clasificacionId, CancellationToken ct, string? origenCambio = null)
    {
        var articulo = await cn.QuerySingleOrDefaultAsync<PlanDiarioCatalogoArticulo>(Cmd(
            "SELECT ProductoCodigo Sku,ProductoNombre Nombre,U_MASTER Master FROM dbo.ArticuloSap WHERE ProductoCodigo=@Sku;", new { Sku = sku }, tx, ct));
        return new { sku, clasificacionId, articulo?.Nombre, articulo?.Master, origenCambio,
            versionAfectaciones = PlaneadorAfectaciones.Reglas.Version, hashAfectaciones = PlaneadorAfectaciones.Version,
            filas = await FilasAsync(cn, tx, sku, clasificacionId, ct) };
    }

    private static Task<string> CapturasAsync(SqlConnection cn, SqlTransaction? tx, string sku, int clasificacionId, string? skuAnterior, int? clasificacionAnteriorId, CancellationToken ct)
        => cn.QuerySingleAsync<string>(Cmd("""
            SELECT (
                SELECT JSON_QUERY((SELECT d.* FROM dbo.PlanDiario d JOIN dbo.PlaneacionProduccion pp ON pp.PlaneacionId=d.PlaneacionId
                    JOIN dbo.Clasificacion c ON c.SKU=pp.TipoPlan
                    WHERE (c.Id=@ClasificacionId AND d.ProductoCodigo=@Sku) OR (c.Id=@ClasificacionAnteriorId AND d.ProductoCodigo=@SkuAnterior)
                    ORDER BY d.PlaneacionId FOR JSON PATH,INCLUDE_NULL_VALUES)) Diario,
                JSON_QUERY((SELECT ps.* FROM dbo.PlanSubClas ps JOIN dbo.PlaneacionProduccion pp ON pp.PlaneacionId=ps.PlanId
                    JOIN dbo.Clasificacion c ON c.SKU=pp.TipoPlan
                    WHERE (c.Id=@ClasificacionId AND ps.ProductoCodigo=@Sku) OR (c.Id=@ClasificacionAnteriorId AND ps.ProductoCodigo=@SkuAnterior)
                    ORDER BY ps.PlanId,ps.fk_SubClas FOR JSON PATH,INCLUDE_NULL_VALUES)) Distribuciones
                FOR JSON PATH,WITHOUT_ARRAY_WRAPPER
            );
            """, new { Sku = sku, ClasificacionId = clasificacionId, SkuAnterior = skuAnterior, ClasificacionAnteriorId = clasificacionAnteriorId }, tx, ct));

    private static Task LimpiarCapturasAsync(SqlConnection cn, SqlTransaction? tx, string sku, int clasificacionId, bool eliminarDiario, CancellationToken ct)
        => cn.ExecuteAsync(Cmd("""
            DELETE ps FROM dbo.PlanSubClas ps JOIN dbo.PlaneacionProduccion pp ON pp.PlaneacionId=ps.PlanId
            JOIN dbo.Clasificacion c ON c.SKU=pp.TipoPlan WHERE c.Id=@ClasificacionId AND ps.ProductoCodigo=@Sku;
            IF @EliminarDiario=1
                DELETE d FROM dbo.PlanDiario d JOIN dbo.PlaneacionProduccion pp ON pp.PlaneacionId=d.PlaneacionId
                JOIN dbo.Clasificacion c ON c.SKU=pp.TipoPlan WHERE c.Id=@ClasificacionId AND d.ProductoCodigo=@Sku;
            """, new { Sku = sku, ClasificacionId = clasificacionId, EliminarDiario = eliminarDiario }, tx, ct));

    private static async Task<int> SincronizarAsync(SqlConnection cn, SqlTransaction? tx, string sku, int clasificacionId, CancellationToken ct)
        => await cn.ExecuteScalarAsync<int>(Cmd("""
            -- Recalcular con los canales y pesos propios de cada fecha; conservar su conversión e inyección.
            SELECT pp.PlaneacionId,CAST(ROUND(COALESCE(SUM(p.Porcentaje/100.0*p.PartSub*cp.KgCanalCompleta*cp.NoCanalCompleta),0),2) AS decimal(18,2)) Kg,
                CAST(ROUND(COALESCE(SUM(p.PartSub*cp.NoCanalCompleta),0),0) AS int) Canales,MAX(p.Porcentaje) Porcentaje
            INTO #TotalesPlanDiarioCatalogo FROM dbo.PlaneacionProduccion pp
            JOIN dbo.Clasificacion c ON c.SKU=pp.TipoPlan
            JOIN dbo.Participacion p ON p.fk_Clasificacion=c.Id AND p.ProductoCodigo=@Sku
            LEFT JOIN dbo.CanalPlaneacion cp WITH (HOLDLOCK) ON cp.PlaneacionId=pp.PlaneacionId AND cp.fk_SubClas=p.fk_SubClas
                AND cp.NoCanalCompleta>0 AND cp.KgCanalCompleta>0
            WHERE c.Id=@ClasificacionId GROUP BY pp.PlaneacionId;
            UPDATE d SET Porcentaje=t.Porcentaje,KgLote=t.Kg,Canales=t.Canales,
                KgInyeccion=ROUND(t.Kg*(1+COALESCE(d.PorcentajeInyeccion,0)/100.0),2)
            FROM dbo.PlanDiario d JOIN #TotalesPlanDiarioCatalogo t ON t.PlaneacionId=d.PlaneacionId WHERE d.ProductoCodigo=@Sku;
            INSERT INTO dbo.PlanDiario(PlaneacionId,ProductoCodigo,Porcentaje,KgLote,Canales,ProductoCodigoConvertido,PorcentajeInyeccion,KgInyeccion)
            SELECT t.PlaneacionId,@Sku,t.Porcentaje,t.Kg,t.Canales,@Sku,0,t.Kg FROM #TotalesPlanDiarioCatalogo t
            WHERE NOT EXISTS(SELECT 1 FROM dbo.PlanDiario d WITH (UPDLOCK,HOLDLOCK) WHERE d.PlaneacionId=t.PlaneacionId AND d.ProductoCodigo=@Sku);
            SELECT COUNT(*) FROM #TotalesPlanDiarioCatalogo;
            DROP TABLE #TotalesPlanDiarioCatalogo;
            """, new { Sku = sku, ClasificacionId = clasificacionId }, tx, ct));

    private static Task RegistrarAsync(SqlConnection cn, SqlTransaction? tx, string sku, int clasificacionId, string? skuAnterior,
        int? clasificacionAnteriorId, string accion, string usuario, object? antes, object? despues,
        string capturasAntes, string capturasDespues, int planes, CancellationToken ct)
        => cn.ExecuteAsync(Cmd("""
            INSERT INTO dbo.PlanDiarioCatalogoHistorial(Sku,ClasificacionId,SkuAnterior,ClasificacionAnteriorId,Accion,Usuario,
                DatosAntes,DatosDespues,CapturasAntes,CapturasDespues,PlanesAfectados)
            VALUES(@Sku,@ClasificacionId,@SkuAnterior,@ClasificacionAnteriorId,@Accion,@Usuario,@Antes,@Despues,@CapturasAntes,@CapturasDespues,@Planes);
            """, new { Sku = sku, ClasificacionId = clasificacionId, SkuAnterior = skuAnterior, ClasificacionAnteriorId = clasificacionAnteriorId,
                Accion = accion, Usuario = usuario, Antes = antes is null ? null : Json(antes), Despues = despues is null ? null : Json(despues),
                CapturasAntes = capturasAntes, CapturasDespues = capturasDespues, Planes = planes }, tx, ct));

    public async Task<int> GuardarAsync(PlanDiarioCatalogoGuardar input, string usuario, CancellationToken ct)
    {
        usuario = usuario.Trim(); ValidarUsuario(usuario);
        input.Sku = input.Sku?.Trim() ?? "";
        input.SkuOriginal = input.SkuOriginal?.Trim();
        input.LineaCodigo = string.IsNullOrWhiteSpace(input.LineaCodigo) ? null : input.LineaCodigo.Trim();
        var edicion = !string.IsNullOrEmpty(input.SkuOriginal);
        if (input.Sku.Length is < 1 or > 50 || input.ClasificacionId <= 0 || input.LineaCodigo?.Length > 50 ||
            (edicion && (input.SkuOriginal!.Length > 50 || input.ClasificacionOriginalId is null or <= 0)) ||
            (!edicion && (input.ClasificacionOriginalId is not null || !string.IsNullOrEmpty(input.Version))))
            throw new PlanDiarioCatalogoValidacionException("Indica un SKU, tipo de plan y línea válidos. El catálogo de participación admite SKU de hasta 50 caracteres.");
        if (input.Porcentaje is null or < 0 or > 100 || decimal.Round(input.Porcentaje.Value, 2) != input.Porcentaje)
            throw new PlanDiarioCatalogoValidacionException("La participación debe estar entre 0 y 100, con hasta 2 decimales.");
        if (input.Distribuciones is null || input.Distribuciones.Count is < 1 or > 100 ||
            input.Distribuciones.Any(d => d.SubClasificacionId <= 0 || d.PartSub is null or < 0 or > 1 || decimal.Round(d.PartSub.Value, 2) != d.PartSub) ||
            input.Distribuciones.Select(d => d.SubClasificacionId).Distinct().Count() != input.Distribuciones.Count)
            throw new PlanDiarioCatalogoValidacionException("Selecciona subclasificaciones sin repetir y captura su distribución de 0 a 1, con hasta 2 decimales.");

        await using var cn = await AbrirAsync(ct);
        await using var tx = Transaction.Current is null ? (SqlTransaction)await cn.BeginTransactionAsync(ct) : null;
        await BloquearAsync(cn, tx, true, ct);
        var anteriores = edicion ? await FilasAsync(cn, tx, input.SkuOriginal!, input.ClasificacionOriginalId!.Value, ct) : [];
        if (edicion) VerificarVersion(anteriores, input.Version);
        var articulo = await cn.QuerySingleOrDefaultAsync<PlanDiarioCatalogoArticulo>(Cmd("""
            SELECT ProductoCodigo Sku,ProductoNombre Nombre,U_MASTER Master FROM dbo.ArticuloSap WITH (HOLDLOCK) WHERE ProductoCodigo=@Sku;
            """, input, tx, ct)) ?? throw new PlanDiarioCatalogoValidacionException("El SKU no existe en ArticuloSAP de SIGO.");
        input.Sku = articulo.Sku;
        var mismoGrupo = edicion && string.Equals(input.Sku, input.SkuOriginal, StringComparison.OrdinalIgnoreCase) && input.ClasificacionId == input.ClasificacionOriginalId;
        if (!mismoGrupo && (await FilasAsync(cn, tx, input.Sku, input.ClasificacionId, ct)).Count > 0)
            throw new PlanDiarioCatalogoValidacionException("Este SKU ya existe en ese tipo de plan. Abre su registro para editarlo.");
        var subs = (await cn.QueryAsync<int>(Cmd("""
            SELECT s.Id FROM dbo.SubClasif s JOIN dbo.Clasificacion c ON c.SKU=s.fk_Clasificacion WHERE c.Id=@ClasificacionId;
            """, input, tx, ct))).ToHashSet();
        if (input.Distribuciones.Any(d => !subs.Contains(d.SubClasificacionId)))
            throw new PlanDiarioCatalogoValidacionException("Las subclasificaciones deben pertenecer al tipo de plan seleccionado.");
        if (input.LineaCodigo is not null)
        {
            input.LineaCodigo = await cn.QuerySingleOrDefaultAsync<string>(Cmd("SELECT ProductoCodigo FROM dbo.ArticuloSap WHERE ProductoCodigo=@LineaCodigo;", input, tx, ct))
                ?? throw new PlanDiarioCatalogoValidacionException("El SKU de la línea no existe en ArticuloSAP de SIGO.");
            if (string.Equals(input.LineaCodigo, input.Sku, StringComparison.OrdinalIgnoreCase))
                throw new PlanDiarioCatalogoValidacionException("La línea debe ser un SKU distinto del producto; déjala vacía para un producto principal.");
        }
        var afectaciones = await PrepararAfectacionesAsync(cn, tx, input, null, ct);
        // No borrar distribuciones por fecha si no cambió ningún valor del catálogo.
        if (mismoGrupo && anteriores.Count == input.Distribuciones.Count && anteriores.All(f =>
            f.Porcentaje == input.Porcentaje && (f.LineaCodigo ?? "") == (input.LineaCodigo ?? "") &&
            input.Distribuciones.Any(d => d.SubClasificacionId == f.SubClasificacionId && d.PartSub == f.PartSub)) && afectaciones.Relacionados.Count == 0) return 0;

        var antes = edicion ? await EstadoAsync(cn, tx, input.SkuOriginal!, input.ClasificacionOriginalId!.Value, ct) : null;
        var capturasAntes = await CapturasAsync(cn, tx, input.Sku, input.ClasificacionId, input.SkuOriginal, input.ClasificacionOriginalId, ct);
        // Mantener los ID existentes de las subclasificaciones que continúan en el catálogo.
        var conservados = new HashSet<int>();
        foreach (var d in input.Distribuciones)
        {
            var previa = anteriores.FirstOrDefault(f => f.SubClasificacionId == d.SubClasificacionId);
            var args = new { input.Sku, input.ClasificacionId, input.Porcentaje, input.LineaCodigo, d.SubClasificacionId, d.PartSub, Id = previa?.Id };
            if (previa is null)
                await cn.ExecuteAsync(Cmd("INSERT INTO dbo.Participacion(ProductoCodigo,fk_Clasificacion,Porcentaje,fk_SubClas,LineaCodigo,PartSub) VALUES(@Sku,@ClasificacionId,@Porcentaje,@SubClasificacionId,@LineaCodigo,@PartSub);", args, tx, ct));
            else
            {
                conservados.Add(previa.Id);
                await cn.ExecuteAsync(Cmd("UPDATE dbo.Participacion SET ProductoCodigo=@Sku,fk_Clasificacion=@ClasificacionId,Porcentaje=@Porcentaje,fk_SubClas=@SubClasificacionId,LineaCodigo=@LineaCodigo,PartSub=@PartSub WHERE Id=@Id;", args, tx, ct));
            }
        }
        var eliminados = anteriores.Where(f => !conservados.Contains(f.Id)).Select(f => f.Id).ToArray();
        if (eliminados.Length > 0) await cn.ExecuteAsync(Cmd("DELETE FROM dbo.Participacion WHERE Id IN @Ids;", new { Ids = eliminados }, tx, ct));
        var planes = 0;
        if (edicion && !mismoGrupo)
        {
            planes = await ContarPlanesAsync(cn, tx, input.ClasificacionOriginalId!.Value, ct);
            await LimpiarCapturasAsync(cn, tx, input.SkuOriginal!, input.ClasificacionOriginalId.Value, true, ct);
        }
        await LimpiarCapturasAsync(cn, tx, input.Sku, input.ClasificacionId, false, ct);
        var destinos = await SincronizarAsync(cn, tx, input.Sku, input.ClasificacionId, ct);
        planes = edicion && input.ClasificacionOriginalId == input.ClasificacionId ? destinos : planes + destinos;
        await RegistrarAsync(cn, tx, input.Sku, input.ClasificacionId, input.SkuOriginal, input.ClasificacionOriginalId,
            edicion ? "Edición" : "Alta", usuario, antes, await EstadoAsync(cn, tx, input.Sku, input.ClasificacionId, ct), capturasAntes,
            await CapturasAsync(cn, tx, input.Sku, input.ClasificacionId, input.SkuOriginal, input.ClasificacionOriginalId, ct), planes, ct);
        await AplicarRelacionadosAsync(cn, tx, afectaciones, input.Sku, usuario, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return planes;
    }

    private static Task<int> ContarPlanesAsync(SqlConnection cn, SqlTransaction? tx, int clasificacionId, CancellationToken ct)
        => cn.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.PlaneacionProduccion pp JOIN dbo.Clasificacion c ON c.SKU=pp.TipoPlan WHERE c.Id=@ClasificacionId;", new { ClasificacionId = clasificacionId }, tx, ct));

    public async Task<int> EliminarAsync(PlanDiarioCatalogoEliminar input, string usuario, CancellationToken ct)
    {
        usuario = usuario.Trim(); ValidarUsuario(usuario);
        if (string.IsNullOrWhiteSpace(input.Sku) || input.Sku.Length > 50 || input.ClasificacionId <= 0)
            throw new PlanDiarioCatalogoValidacionException("Selecciona un SKU y tipo de plan válidos.");
        await using var cn = await AbrirAsync(ct);
        await using var tx = Transaction.Current is null ? (SqlTransaction)await cn.BeginTransactionAsync(ct) : null;
        await BloquearAsync(cn, tx, true, ct);
        VerificarVersion(await FilasAsync(cn, tx, input.Sku, input.ClasificacionId, ct), input.Version);
        var afectaciones = await PrepararAfectacionesAsync(cn, tx, null, input, ct);
        var antes = await EstadoAsync(cn, tx, input.Sku, input.ClasificacionId, ct);
        var capturas = await CapturasAsync(cn, tx, input.Sku, input.ClasificacionId, null, null, ct);
        await cn.ExecuteAsync(Cmd("DELETE FROM dbo.Participacion WHERE ProductoCodigo=@Sku AND fk_Clasificacion=@ClasificacionId;", input, tx, ct));
        await LimpiarCapturasAsync(cn, tx, input.Sku, input.ClasificacionId, true, ct);
        var planes = await ContarPlanesAsync(cn, tx, input.ClasificacionId, ct);
        await RegistrarAsync(cn, tx, input.Sku, input.ClasificacionId, input.Sku, input.ClasificacionId, "Eliminación", usuario, antes, null,
            capturas, await CapturasAsync(cn, tx, input.Sku, input.ClasificacionId, null, null, ct), planes, ct);
        await AplicarRelacionadosAsync(cn, tx, afectaciones, input.Sku, usuario, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return planes;
    }

    public async Task<IReadOnlyList<PlanDiarioCatalogoCambio>> HistorialAsync(string? sku, int? clasificacionId, int pagina, CancellationToken ct)
    {
        sku = sku?.Trim();
        if (sku?.Length > 50 || clasificacionId <= 0) throw new PlanDiarioCatalogoValidacionException("Revisa los filtros del historial.");
        await using var cn = await AbrirAsync(ct);
        return (await cn.QueryAsync<PlanDiarioCatalogoCambio>(Cmd("""
            SELECT Id,Sku,SkuAnterior,ClasificacionId,ClasificacionAnteriorId,Accion,Usuario,Fecha,PlanesAfectados,DatosAntes,DatosDespues
            FROM dbo.PlanDiarioCatalogoHistorial
            WHERE ((@Sku IS NULL OR @Sku='' OR Sku=@Sku) AND (@ClasificacionId IS NULL OR ClasificacionId=@ClasificacionId))
               OR ((@Sku IS NULL OR @Sku='' OR SkuAnterior=@Sku) AND (@ClasificacionId IS NULL OR ClasificacionAnteriorId=@ClasificacionId))
            ORDER BY Id DESC OFFSET @Offset ROWS FETCH NEXT 25 ROWS ONLY;
            """, new { Sku = sku, ClasificacionId = clasificacionId, Offset = (Math.Clamp(pagina, 1, 100000) - 1) * 25 }, null, ct))).ToList();
    }

    private static async Task<string> VersionCatalogoAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => Version(new { Reglas = PlaneadorAfectaciones.Version, Filas = (await cn.QueryAsync<PlanDiarioCatalogoFila>(Cmd($"SELECT {Columnas} FROM dbo.Participacion p ORDER BY p.Id;", null, tx, ct))).ToList() });

    public async Task<string> ObtenerVersionAsync(CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        return await VersionCatalogoAsync(cn, null, ct);
    }

    public async Task GuardarDiaAsync(List<PlanDiarioModel> lista, string? version, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(version)) throw new PlanDiarioCatalogoConflictoException("Vuelve a abrir el día antes de guardar; falta la versión del catálogo global.");
        if (lista.Count == 0 || lista.Any(d => d is null) || lista[0].PlaneacionId <= 0 || lista.Any(d => d.PlaneacionId != 0 && d.PlaneacionId != lista[0].PlaneacionId) ||
            lista.Select(d => d.ProductoCodigo).Distinct(StringComparer.OrdinalIgnoreCase).Count() != lista.Count ||
            lista.Any(d => string.IsNullOrWhiteSpace(d.ProductoCodigo) || d.ProductoCodigo.Length > 50 || d.ProductoCodigoConvertido?.Length > 50 ||
                !double.IsFinite(d.Porcentaje) || d.Porcentaje is < 0 or > 100 || !double.IsFinite(d.KgLote) || d.KgLote < 0 || d.Canales < 0 || d.KgInyeccion < 0 || d.PorcentajeInyeccion < 0 ||
                d.Participaciones is null || d.Participaciones.Any(p => p.PlanId != 0 && p.PlanId != lista[0].PlaneacionId ||
                    p.ProductoCodigo != d.ProductoCodigo || !double.IsFinite(p.PartSub) || p.PartSub is < 0 or > 1) ||
                d.Participaciones.Select(p => p.fk_SubClas).Distinct().Count() != d.Participaciones.Count))
            throw new PlanDiarioCatalogoValidacionException("Revisa los productos, cantidades y distribuciones del plan diario.");
        await using var cn = await AbrirAsync(ct);
        await using var tx = Transaction.Current is null ? (SqlTransaction)await cn.BeginTransactionAsync(ct) : null;
        await BloquearAsync(cn, tx, false, ct);
        if (!string.Equals(await VersionCatalogoAsync(cn, tx, ct), version, StringComparison.Ordinal))
            throw new PlanDiarioCatalogoConflictoException("El catálogo global cambió mientras este día estaba abierto. Vuelve a abrir el día para cargar los cambios antes de guardar.");
        var planId = lista[0].PlaneacionId;
        if (await cn.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM dbo.PlaneacionProduccion WITH (UPDLOCK,HOLDLOCK) WHERE PlaneacionId=@PlanId;", new { PlanId = planId }, tx, ct)) != 1)
            throw new KeyNotFoundException("El plan diario ya no existe.");
        var productos = (await cn.QueryAsync<string>(Cmd("SELECT DISTINCT ProductoCodigo FROM dbo.Participacion WHERE ProductoCodigo IN @Skus;",
            new { Skus = lista.Select(d => d.ProductoCodigo).ToArray() }, tx, ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subclasificaciones = (await cn.QueryAsync<int>(Cmd("""
            SELECT s.Id FROM dbo.SubClasif s JOIN dbo.PlaneacionProduccion pp ON pp.TipoPlan=s.fk_Clasificacion WHERE pp.PlaneacionId=@PlanId;
            """, new { PlanId = planId }, tx, ct))).ToHashSet();
        if (lista.Any(d => !productos.Contains(d.ProductoCodigo) || d.Participaciones.Any(p => !subclasificaciones.Contains(p.fk_SubClas))))
            throw new PlanDiarioCatalogoValidacionException("Hay productos o subclasificaciones que ya no están disponibles. Vuelve a abrir el día.");
        await ValidarAfectacionesDiaAsync(cn, tx, lista, planId, ct);
        await cn.ExecuteAsync(Cmd("DELETE FROM dbo.PlanDiario WHERE PlaneacionId=@PlanId; DELETE FROM dbo.PlanSubClas WHERE PlanId=@PlanId;", new { PlanId = planId }, tx, ct));
        foreach (var d in lista)
        {
            await cn.ExecuteAsync(Cmd("""
                INSERT INTO dbo.PlanDiario(PlaneacionId,ProductoCodigo,Porcentaje,KgLote,Canales,ProductoCodigoConvertido,PorcentajeInyeccion,KgInyeccion)
                VALUES(@PlanId,@ProductoCodigo,@Porcentaje,@KgLote,@Canales,@ProductoCodigoConvertido,@PorcentajeInyeccion,@KgInyeccion);
                """, new { PlanId = planId, d.ProductoCodigo, d.Porcentaje, d.KgLote, d.Canales, d.ProductoCodigoConvertido, d.PorcentajeInyeccion, d.KgInyeccion }, tx, ct));
            foreach (var p in d.Participaciones)
                await cn.ExecuteAsync(Cmd("INSERT INTO dbo.PlanSubClas(PlanId,fk_SubClas,ProductoCodigo,PartSub) VALUES(@PlanId,@fk_SubClas,@ProductoCodigo,@PartSub);",
                    new { PlanId = planId, p.fk_SubClas, d.ProductoCodigo, p.PartSub }, tx, ct));
        }
        if (tx is not null) await tx.CommitAsync(ct);
    }
}
