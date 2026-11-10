using Dapper;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;
using System.Transactions;

namespace Plataforma_CG.Services;

public sealed partial class PlaneadorPlanDiarioCatalogoService
{
    private sealed class ClasAfectacion { public int Id { get; set; } public string Tipo { get; set; } = ""; }
    private sealed class SubAfectacion { public int Id { get; set; } public string Nombre { get; set; } = ""; public int ClasificacionId { get; set; } }
    private sealed record CambioRelacionado(string Sku, int ClasificacionId, List<AfectacionesCambio> Cambios, PlanDiarioCatalogoFila Base, object Antes, string CapturasAntes);
    private sealed record PropuestaAfectaciones(List<AfectacionesCambio> Cambios, List<CambioRelacionado> Relacionados, string VersionCatalogo);

    private async Task<PropuestaAfectaciones> PrepararAfectacionesAsync(SqlConnection cn, SqlTransaction? tx,
        PlanDiarioCatalogoGuardar? guardar, PlanDiarioCatalogoEliminar? eliminar, CancellationToken ct)
    {
        var bloqueo = tx is not null || Transaction.Current is not null ? "WITH (UPDLOCK,HOLDLOCK)" : "";
        using var result = await cn.QueryMultipleAsync(Cmd($"""
            SELECT {Columnas} FROM dbo.Participacion p {bloqueo} ORDER BY p.Id;
            SELECT Id,SKU Tipo FROM dbo.Clasificacion;
            SELECT s.Id,s.Nombre,c.Id ClasificacionId FROM dbo.SubClasif s JOIN dbo.Clasificacion c ON c.SKU=s.fk_Clasificacion;
            """, null, tx, ct));
        var filas = (await result.ReadAsync<PlanDiarioCatalogoFila>()).ToList();
        var clases = (await result.ReadAsync<ClasAfectacion>()).ToDictionary(c => c.Id);
        var subs = (await result.ReadAsync<SubAfectacion>()).ToList();
        var version = Version(new { Reglas = PlaneadorAfectaciones.Version, Filas = filas });
        var esperada = guardar?.VersionCatalogo ?? eliminar?.VersionCatalogo;
        if (string.IsNullOrEmpty(esperada) || version != esperada)
            throw new PlanDiarioCatalogoConflictoException("Cambió el catálogo o sus afectaciones. Actualiza el listado y revisa nuevamente los cambios antes de guardar.");
        var skuAnterior = eliminar?.Sku ?? guardar?.SkuOriginal;
        var claseAnterior = eliminar?.ClasificacionId ?? guardar?.ClasificacionOriginalId;
        var ids = new[] { claseAnterior, guardar?.ClasificacionId }.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        var cambiosTotales = new List<AfectacionesCambio>();
        var relacionados = new List<CambioRelacionado>();
        foreach (var id in ids)
        {
            if (!clases.TryGetValue(id, out var clase)) throw new PlanDiarioCatalogoValidacionException("El tipo de plan ya no existe.");
            var subclases = subs.Where(s => s.ClasificacionId == id).Select(s => new AfectacionesSubclasificacion(s.Id, s.Nombre)).ToList();
            var grupos = filas.Where(f => f.ClasificacionId == id).GroupBy(f => f.Sku.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var originales = grupos.Values.SelectMany(g => subclases.Select(s => new AfectacionesValor(g[0].Sku, s.Id,
                g.FirstOrDefault(f => f.SubClasificacionId == s.Id)?.PartSub ?? 0, g[0].LineaCodigo))).ToList();
            var solicitados = new Dictionary<(string Sku, int Sub), AfectacionesValor>();
            var mueveIdentidad = guardar is not null && (!string.Equals(guardar.Sku, skuAnterior, StringComparison.OrdinalIgnoreCase) || guardar.ClasificacionId != claseAnterior);
            if (claseAnterior == id && !string.IsNullOrEmpty(skuAnterior) && (eliminar is not null || mueveIdentidad))
                foreach (var v in originales.Where(v => string.Equals(v.Sku, skuAnterior, StringComparison.OrdinalIgnoreCase))) solicitados[(v.Sku.ToUpperInvariant(), v.SubClasificacionId)] = v with { PartSub = 0 };
            if (guardar?.ClasificacionId == id)
            {
                if (guardar.Distribuciones.Any(d => subclases.All(s => s.Id != d.SubClasificacionId)))
                    throw new PlanDiarioCatalogoValidacionException("Las subclasificaciones deben pertenecer al tipo de plan seleccionado.");
                if (PlaneadorAfectaciones.Excluido(clase.Tipo, guardar.Sku))
                    throw new PlanDiarioCatalogoValidacionException($"{guardar.Sku} está excluido de la plantilla {clase.Tipo} según el Excel.");
                foreach (var s in subclases)
                {
                    var v = new AfectacionesValor(guardar.Sku, s.Id, guardar.Distribuciones.FirstOrDefault(d => d.SubClasificacionId == s.Id)?.PartSub ?? 0, guardar.LineaCodigo);
                    var original = originales.FirstOrDefault(o => string.Equals(o.Sku, v.Sku, StringComparison.OrdinalIgnoreCase) && o.SubClasificacionId == s.Id);
                    if (original is null) originales.Add(v with { PartSub = 0 });
                    solicitados[(v.Sku.ToUpperInvariant(), s.Id)] = v;
                }
            }
            var efectivos = solicitados.Values.ToList();
            var calculo = PlaneadorAfectaciones.Simular(clase.Tipo, subclases, originales, efectivos);
            cambiosTotales.AddRange(calculo.Cambios);
            if (guardar?.ClasificacionId == id)
            {
                foreach (var d in guardar.Distribuciones)
                    d.PartSub = calculo.Valores.Single(v => string.Equals(v.Sku, guardar.Sku, StringComparison.OrdinalIgnoreCase) && v.SubClasificacionId == d.SubClasificacionId).PartSub;
                foreach (var v in calculo.Valores.Where(v => string.Equals(v.Sku, guardar.Sku, StringComparison.OrdinalIgnoreCase) && v.PartSub != 0 && guardar.Distribuciones.All(d => d.SubClasificacionId != v.SubClasificacionId)))
                    guardar.Distribuciones.Add(new() { SubClasificacionId = v.SubClasificacionId, PartSub = v.PartSub });
            }
            var origen = guardar?.Sku ?? eliminar!.Sku;
            foreach (var grupo in calculo.Cambios.GroupBy(c => c.Sku))
            {
                if ((id == claseAnterior && string.Equals(grupo.Key, skuAnterior, StringComparison.OrdinalIgnoreCase)) ||
                    (id == guardar?.ClasificacionId && string.Equals(grupo.Key, guardar.Sku, StringComparison.OrdinalIgnoreCase))) continue;
                if (!grupos.TryGetValue(grupo.Key, out var actuales)) continue;
                relacionados.Add(new(grupo.Key, id, grupo.ToList(), actuales[0],
                    await EstadoAsync(cn, tx, grupo.Key, id, ct, origen),
                    await CapturasAsync(cn, tx, grupo.Key, id, null, null, ct)));
            }
        }
        return new(cambiosTotales, relacionados, version);
    }

    private static async Task AplicarRelacionadosAsync(SqlConnection cn, SqlTransaction? tx, PropuestaAfectaciones propuesta,
        string origen, string usuario, CancellationToken ct)
    {
        foreach (var grupo in propuesta.Relacionados)
        {
            foreach (var c in grupo.Cambios)
                await cn.ExecuteAsync(Cmd("""
                    UPDATE dbo.Participacion SET PartSub=@PartSub WHERE ProductoCodigo=@Sku AND fk_Clasificacion=@ClasificacionId AND fk_SubClas=@SubClasificacionId;
                    IF @@ROWCOUNT=0 INSERT dbo.Participacion(ProductoCodigo,fk_Clasificacion,Porcentaje,fk_SubClas,LineaCodigo,PartSub)
                    VALUES(@Sku,@ClasificacionId,@Porcentaje,@SubClasificacionId,@LineaCodigo,@PartSub);
                    """, new { grupo.Sku, grupo.ClasificacionId, c.SubClasificacionId, PartSub = c.Despues, grupo.Base.Porcentaje, grupo.Base.LineaCodigo }, tx, ct));
            await LimpiarCapturasAsync(cn, tx, grupo.Sku, grupo.ClasificacionId, false, ct);
            var planes = await SincronizarAsync(cn, tx, grupo.Sku, grupo.ClasificacionId, ct);
            await RegistrarAsync(cn, tx, grupo.Sku, grupo.ClasificacionId, grupo.Sku, grupo.ClasificacionId,
                "Edición", usuario, grupo.Antes, await EstadoAsync(cn, tx, grupo.Sku, grupo.ClasificacionId, ct, origen),
                grupo.CapturasAntes, await CapturasAsync(cn, tx, grupo.Sku, grupo.ClasificacionId, null, null, ct), planes, ct);
        }
    }

    public async Task<object> PrevisualizarAsync(PlanDiarioCatalogoGuardar? guardar, PlanDiarioCatalogoEliminar? eliminar, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var propuesta = await PrepararAfectacionesAsync(cn, null, guardar, eliminar, ct);
        return new { propuesta.Cambios, propuesta.VersionCatalogo, distribuciones = guardar?.Distribuciones };
    }

    private static async Task ValidarAfectacionesDiaAsync(SqlConnection cn, SqlTransaction? tx, List<Models.Operaciones.Planeacion.Diaria.PlanDiarioModel> lista, int planId, CancellationToken ct)
    {
        var tipo = await cn.QuerySingleAsync<string>(Cmd("SELECT TipoPlan FROM dbo.PlaneacionProduccion WHERE PlaneacionId=@PlanId;", new { PlanId = planId }, tx, ct));
        if (lista.Any(d => PlaneadorAfectaciones.Excluido(tipo, d.ProductoCodigo)))
            throw new PlanDiarioCatalogoValidacionException("El plan incluye un SKU excluido por la plantilla. Vuelve a abrir el día.");
        var subs = (await cn.QueryAsync<AfectacionesSubclasificacion>(Cmd("""
            SELECT s.Id,s.Nombre FROM dbo.SubClasif s JOIN dbo.CanalPlaneacion cp ON cp.fk_SubClas=s.Id
            WHERE cp.PlaneacionId=@PlanId AND cp.NoCanalCompleta>0 AND cp.KgCanalCompleta>0;
            """, new { PlanId = planId }, tx, ct))).ToList();
        var valores = lista.SelectMany(d => subs.Select(s => new AfectacionesValor(d.ProductoCodigo, s.Id,
            (decimal)(d.Participaciones.FirstOrDefault(p => p.fk_SubClas == s.Id)?.PartSub ?? 0)))).ToList();
        // Estos valores llegan de un guardado nuevo: validar también su precisión,
        // aunque la lectura del estado histórico admita capturas de mayor precisión.
        var calculo = PlaneadorAfectaciones.Simular(tipo, subs, valores, valores, true);
        if (calculo.Cambios.Count > 0)
            throw new PlanDiarioCatalogoValidacionException("Hay productos calculados que no coinciden con sus afectaciones. Revisa los avisos del plan antes de guardar.");
    }
}
