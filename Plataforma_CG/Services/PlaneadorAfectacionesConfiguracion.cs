using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;

namespace Plataforma_CG.Services;

/// <summary>Traduce reglas editables al mismo motor de cálculo. No accede a SQL.</summary>
public static class PlaneadorAfectacionesConfiguracion
{
    public static readonly IReadOnlyDictionary<string, string[]> Subclasificaciones = new Dictionary<string, string[]>
    {
        ["NOV"] = ["PREMIUM", "NOV-LINEA", "SELECTO"],
        ["VG"] = ["VG-BIG", "VG-LINEA", "VR", "T.Amarilla"]
    };

    public static AfectacionRegla Normalizar(AfectacionRegla regla)
    {
        if (regla is null) throw new PlanDiarioCatalogoValidacionException("Selecciona una afectación.");
        // Copiar evita alterar una propuesta después de revisarla o modificar reglas compartidas.
        var r = JsonSerializer.Deserialize<AfectacionRegla>(JsonSerializer.Serialize(regla))!;
        r.TipoPlan = PlaneadorAfectaciones.Tipo(r.TipoPlan ?? "");
        r.Nombre = (r.Nombre ?? "").Trim();
        r.Subclasificaciones = (r.Subclasificaciones ?? []).Select(s => PlaneadorAfectaciones.Sub(s ?? "")).ToList();
        List<string> Codigos(List<string>? valores) => (valores ?? []).Select(s => (s ?? "").Trim().ToUpperInvariant()).ToList();
        r.Origenes = Codigos(r.Origenes); r.Destinos = Codigos(r.Destinos); r.Restar = Codigos(r.Restar);
        if (!Subclasificaciones.TryGetValue(r.TipoPlan, out var subs) || r.Nombre.Length is < 1 or > 150 ||
            r.Subclasificaciones.Count == 0 || r.Subclasificaciones.Any(s => !subs.Contains(s)) ||
            r.Subclasificaciones.Distinct().Count() != r.Subclasificaciones.Count)
            throw new PlanDiarioCatalogoValidacionException("Indica nombre, plan NOV/VG y subclasificaciones válidas sin repetir.");
        foreach (var lista in new[] { r.Origenes, r.Destinos, r.Restar })
            if (lista.Count > 100 || lista.Any(s => s.Length is < 1 or > 50) || lista.Distinct().Count() != lista.Count)
                throw new PlanDiarioCatalogoValidacionException("Selecciona hasta 100 SKU válidos por grupo, sin repetir.");
        if (r.Destinos.Count == 0 || r.Destinos.Intersect(r.Origenes.Concat(r.Restar)).Any() || r.Origenes.Intersect(r.Restar).Any())
            throw new PlanDiarioCatalogoValidacionException("Selecciona destinos distintos de sus fuentes; un SKU no puede depender de sí mismo ni sumarse y restarse a la vez.");
        var valida = r.TipoRegla switch
        {
            "Descuento" => r.Origenes.Count > 0 && r.Restar.Count == 0,
            "Reparto" => r.Origenes.Count > 0 && r.Destinos.Count == 1 && r.Restar.Count == 0,
            "Calculado" => r.Origenes.Count > 0,
            "Cero" => r.Origenes.Count == 0 && r.Restar.Count == 0,
            _ => false
        };
        if (!valida) throw new PlanDiarioCatalogoValidacionException("Revisa los SKU requeridos por el tipo de afectación. El reparto necesita un solo SKU que reciba el restante.");
        if (r.Excluir && (r.TipoRegla != "Cero" || r.Subclasificaciones.Count != subs.Length))
            throw new PlanDiarioCatalogoValidacionException("Ocultar del plan sólo aplica a una regla de cero para todas las subclasificaciones.");
        return r;
    }

    public static IReadOnlyList<AfectacionRegla> Iniciales()
    {
        var salida = new List<AfectacionRegla>();
        foreach (var (tipo, plan) in PlaneadorAfectaciones.Reglas.Planes)
        {
            void Agregar(AfectacionRegla r)
            {
                r.TipoPlan = tipo;
                var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(r)));
                r.Id = new Guid(bytes.AsSpan(0, 16)); salida.Add(r);
            }
            foreach (var d in plan.Descuentos)
                Agregar(new() { Nombre = $"Descuento {d.Origen} → {d.Destino}", TipoRegla = "Descuento", Origenes = [d.Origen], Destinos = [d.Destino], Subclasificaciones = d.Subclasificaciones });
            foreach (var s in plan.Sumas)
                Agregar(new() { Nombre = $"Calculado {s.Destino}", TipoRegla = "Calculado", Origenes = s.Fuentes, Restar = s.Restar, Destinos = [s.Destino], Subclasificaciones = s.Subclasificaciones });
            foreach (var b in plan.Bloqueados)
                Agregar(new() { Nombre = b.Motivo, TipoRegla = "Cero", Destinos = [b.Sku], Subclasificaciones = Subclasificaciones[tipo].ToList(), Excluir = b.Excluir });
        }
        return salida;
    }

    public static AfectacionesDefinicion Compilar(IEnumerable<AfectacionRegla> reglas)
    {
        var todas = reglas.Select(Normalizar).ToList();
        if (todas.Count > 2000 || todas.Select(r => r.Id).Distinct().Count() != todas.Count)
            throw new PlanDiarioCatalogoValidacionException("Hay demasiadas reglas o identificadores repetidos.");
        var definicion = new AfectacionesDefinicion { Version = "Administradas desde SIGO" };
        foreach (var tipo in Subclasificaciones.Keys)
        {
            var plan = new AfectacionesPlan(); definicion.Planes[tipo] = plan;
            // Conservar también códigos de reglas desactivadas impide que LineaCodigo
            // vuelva a activar implícitamente una relación que el usuario quitó.
            plan.Conocidos = PlaneadorAfectaciones.Reglas.Planes[tipo].Conocidos.ToList();
            plan.Administrados = todas.Where(r => r.TipoPlan == tipo).SelectMany(r =>
                r.Origenes.Concat(r.Destinos).Concat(r.Restar).Select(sku => (Sku: sku, Subs: r.Subclasificaciones)))
                .GroupBy(x => x.Sku).ToDictionary(g => g.Key, g => g.SelectMany(x => x.Subs).Distinct().ToList());
            foreach (var r in todas.Where(r => r.Activa && r.TipoPlan == tipo))
            {
                if (r.TipoRegla == "Descuento")
                    foreach (var origen in r.Origenes) foreach (var destino in r.Destinos)
                        plan.Descuentos.Add(new() { Origen = origen, Destino = destino, Subclasificaciones = r.Subclasificaciones });
                else if (r.TipoRegla == "Cero")
                    foreach (var destino in r.Destinos)
                        plan.Bloqueados.Add(new() { Sku = destino, Excluir = r.Excluir, Motivo = r.Nombre, Subclasificaciones = r.Subclasificaciones });
                else foreach (var destino in r.Destinos)
                        plan.Sumas.Add(new() { Destino = destino, Fuentes = r.TipoRegla == "Reparto" ? [] : r.Origenes,
                            Restar = r.TipoRegla == "Reparto" ? r.Origenes : r.Restar, Constante = r.TipoRegla == "Reparto" ? 1 : 0, Subclasificaciones = r.Subclasificaciones });
            }
            foreach (var sub in Subclasificaciones[tipo])
            {
                var descuentos = plan.Descuentos.Where(r => r.Subclasificaciones.Contains(sub)).ToList();
                var sumas = plan.Sumas.Where(r => r.Subclasificaciones.Contains(sub)).ToList();
                var ceros = plan.Bloqueados.Where(r => r.Subclasificaciones.Contains(sub)).ToList();
                if (descuentos.GroupBy(d => (d.Origen, d.Destino)).Any(g => g.Count() > 1) ||
                    sumas.Select(s => s.Destino).Concat(ceros.Select(c => c.Sku)).GroupBy(s => s).Any(g => g.Count() > 1))
                    throw new PlanDiarioCatalogoValidacionException($"Hay afectaciones duplicadas o dos cálculos para el mismo destino en {tipo} · {sub}. Edita o desactiva la regla existente.");
                if (descuentos.Any(d => ceros.Any(c => c.Sku == d.Destino) || sumas.Any(s => s.Destino == d.Destino && !s.Restar.Contains(d.Origen))))
                    throw new PlanDiarioCatalogoValidacionException($"Un destino de descuento ya tiene un cálculo automático incompatible en {tipo} · {sub}.");
            }
            // Validar ciclos también en reglas cuyos SKU todavía no están en un día concreto.
            PlaneadorAfectaciones.Simular(tipo, Subclasificaciones[tipo].Select((s, i) => new AfectacionesSubclasificacion(i + 1, s)).ToList(), [], [], definicion: definicion);
        }
        return definicion;
    }
}
