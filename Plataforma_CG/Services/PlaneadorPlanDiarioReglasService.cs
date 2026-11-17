using Dapper;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;
using System.Text.Json;
using System.Transactions;

namespace Plataforma_CG.Services;

public sealed partial class PlaneadorPlanDiarioCatalogoService
{
    private sealed record ConfiguracionReglas(List<AfectacionRegla> Reglas, string Version, bool Instalada, AfectacionesDefinicion Definicion);
    private static async Task<ConfiguracionReglas> LeerReglasAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
    {
        var instalada = await cn.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN OBJECT_ID(N'dbo.PlanDiarioAfectacion',N'U') IS NULL THEN 0 ELSE 1 END;", null, tx, ct)) == 1;
        if (!instalada) return new(PlaneadorAfectacionesConfiguracion.Iniciales().ToList(), "plantilla", false, PlaneadorAfectaciones.Reglas);
        var datos = await cn.QueryAsync<string>(Cmd("SELECT DatosJson FROM dbo.PlanDiarioAfectacion ORDER BY Id;", null, tx, ct));
        var reglas = datos.Select(d => JsonSerializer.Deserialize<AfectacionRegla>(d, JsonOptions)
            ?? throw new InvalidOperationException("Hay una afectación sin definición." )).ToList();
        var version = Version(reglas);
        var definicion = PlaneadorAfectacionesConfiguracion.Compilar(reglas);
        definicion.Version = version;
        return new(reglas, version, true, definicion);
    }

    public async Task<object> ListarReglasAsync(CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var config = await LeerReglasAsync(cn, null, ct);
        return new { config.Reglas, config.Version, config.Instalada, subclasificaciones = PlaneadorAfectacionesConfiguracion.Subclasificaciones };
    }

    private static async Task<AfectacionRegla> PrepararReglaAsync(SqlConnection cn, SqlTransaction? tx, ConfiguracionReglas config, AfectacionGuardar input, CancellationToken ct)
    {
        if (!config.Instalada) throw new PlanDiarioCatalogoValidacionException("Primero instala manualmente PlanDiarioAfectaciones-Instalacion.sql en la BD de pruebas.");
        if (input is null || input.Version != config.Version)
            throw new PlanDiarioCatalogoConflictoException("Las afectaciones cambiaron. Actualiza la lista y revisa de nuevo la regla.");
        var regla = PlaneadorAfectacionesConfiguracion.Normalizar(input.Regla);
        if (regla.Id == Guid.Empty) regla.Id = Guid.NewGuid();
        var anteriores = config.Reglas.SingleOrDefault(r => r.Id == regla.Id);
        if (anteriores is not null && anteriores.TipoPlan != regla.TipoPlan)
            throw new PlanDiarioCatalogoValidacionException("Para cambiar el tipo de plan, desactiva esta regla y crea otra en el plan correspondiente.");
        var existentes = config.Reglas.Where(r => r.Id != regla.Id).Append(regla).ToList();
        PlaneadorAfectacionesConfiguracion.Compilar(existentes);
        if (regla.Activa)
        {
            var codigos = regla.Origenes.Concat(regla.Destinos).Concat(regla.Restar).Distinct().ToArray();
            var disponibles = (await cn.QueryAsync<string>(Cmd("""
                SELECT DISTINCT a.ProductoCodigo FROM dbo.ArticuloSap a JOIN dbo.Participacion p ON p.ProductoCodigo=a.ProductoCodigo
                JOIN dbo.Clasificacion c ON c.Id=p.fk_Clasificacion WHERE c.SKU=@Tipo AND a.ProductoCodigo IN @Codigos;
                """, new { Tipo = regla.TipoPlan, Codigos = codigos }, tx, ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var faltantes = codigos.Where(s => !disponibles.Contains(s)).ToList();
            if (faltantes.Count > 0) throw new PlanDiarioCatalogoValidacionException("Agrega primero estos SKU al catálogo global de " + regla.TipoPlan + ": " + string.Join(", ", faltantes) + ". Deben existir también en ArticuloSAP de SIGO.");
            var subs = (await cn.QueryAsync<string>(Cmd("SELECT s.Nombre FROM dbo.SubClasif s WHERE s.fk_Clasificacion=@Tipo;", new { Tipo = regla.TipoPlan }, tx, ct)))
                .Select(PlaneadorAfectaciones.Sub).ToHashSet();
            if (regla.Subclasificaciones.Any(s => !subs.Contains(s)))
                throw new PlanDiarioCatalogoValidacionException("Alguna subclasificación ya no existe en ese plan.");
        }
        return regla;
    }

    public async Task<object> PrevisualizarReglaAsync(AfectacionGuardar input, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var config = await LeerReglasAsync(cn, null, ct);
        var regla = await PrepararReglaAsync(cn, null, config, input, ct);
        return new { regla, config.Version, alcance = "La regla se aplicará en NOV/VG al abrir y editar sus planes y en el catálogo global. Las capturas se conservan al guardar cada plan; publicar la regla no reescribe sus distribuciones." };
    }

    public async Task GuardarReglaAsync(AfectacionGuardar input, string usuario, CancellationToken ct)
    {
        usuario = usuario.Trim(); ValidarUsuario(usuario);
        await using var cn = await AbrirAsync(ct);
        await using var tx = Transaction.Current is null ? (SqlTransaction)await cn.BeginTransactionAsync(ct) : null;
        await BloquearAsync(cn, tx, true, ct);
        var config = await LeerReglasAsync(cn, tx, ct);
        var regla = await PrepararReglaAsync(cn, tx, config, input, ct);
        var antes = config.Reglas.SingleOrDefault(r => r.Id == regla.Id);
        var datos = Json(regla);
        if (antes is null)
            await cn.ExecuteAsync(Cmd("INSERT dbo.PlanDiarioAfectacion(Id,TipoPlan,DatosJson,Activa,Usuario) VALUES(@Id,@TipoPlan,@Datos,@Activa,@Usuario);",
                new { regla.Id, regla.TipoPlan, Datos = datos, regla.Activa, Usuario = usuario }, tx, ct));
        else
            await cn.ExecuteAsync(Cmd("UPDATE dbo.PlanDiarioAfectacion SET DatosJson=@Datos,Activa=@Activa,Usuario=@Usuario,Fecha=SYSDATETIME() WHERE Id=@Id;",
                new { regla.Id, Datos = datos, regla.Activa, Usuario = usuario }, tx, ct));
        var accion = antes is null ? "Alta" : antes.Activa && !regla.Activa ? "Desactivación" : !antes.Activa && regla.Activa ? "Reactivación" : "Edición";
        await cn.ExecuteAsync(Cmd("""
            INSERT dbo.PlanDiarioAfectacionHistorial(ReglaId,TipoPlan,Accion,Usuario,DatosAntes,DatosDespues)
            VALUES(@Id,@TipoPlan,@Accion,@Usuario,@Antes,@Despues);
            """, new { regla.Id, regla.TipoPlan, Accion = accion, Usuario = usuario, Antes = antes is null ? null : Json(antes), Despues = datos }, tx, ct));
        if (tx is not null) await tx.CommitAsync(ct);
    }

    public async Task<object> HistorialReglasAsync(string? tipoPlan, int pagina, CancellationToken ct)
    {
        var tipo = string.IsNullOrWhiteSpace(tipoPlan) ? null : PlaneadorAfectaciones.Tipo(tipoPlan);
        if (tipo is not null && !PlaneadorAfectacionesConfiguracion.Subclasificaciones.ContainsKey(tipo))
            throw new PlanDiarioCatalogoValidacionException("Selecciona NOV o VG.");
        await using var cn = await AbrirAsync(ct);
        var config = await LeerReglasAsync(cn, null, ct);
        if (!config.Instalada) return Array.Empty<object>();
        return (await cn.QueryAsync(Cmd("""
            SELECT Id,ReglaId,TipoPlan,Accion,Usuario,Fecha,DatosAntes,DatosDespues FROM dbo.PlanDiarioAfectacionHistorial
            WHERE @Tipo IS NULL OR TipoPlan=@Tipo ORDER BY Id DESC OFFSET @Offset ROWS FETCH NEXT 25 ROWS ONLY;
            """, new { Tipo = tipo, Offset = (Math.Clamp(pagina, 1, 100000) - 1) * 25 }, null, ct))).ToList();
    }

    public async Task<AfectacionesContexto> ObtenerContextoAsync(CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var tx = Transaction.Current is null ? (SqlTransaction)await cn.BeginTransactionAsync(ct) : null;
        await BloquearAsync(cn, tx, false, ct);
        var config = await LeerReglasAsync(cn, tx, ct);
        var version = await VersionCatalogoAsync(cn, tx, ct, config.Definicion);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(version, config.Definicion);
    }
}
