using System.Text.Json;
using System.Security.Cryptography;
using Plataforma_CG.Models.Operaciones.Planeacion.Catalogos;

namespace Plataforma_CG.Services;

/// <summary>Cálculo puro compartido por el guardado diario y el catálogo; no abre conexiones.</summary>
public static class PlaneadorAfectaciones
{
    private static readonly Lazy<AfectacionesDefinicion> Definicion = new(() =>
    {
        using var stream = typeof(PlaneadorAfectaciones).Assembly.GetManifestResourceStream("Plataforma_CG.Plantillas.PlanDiarioAfectaciones.json")
            ?? throw new InvalidOperationException("Falta la definición de afectaciones del plan diario.");
        return JsonSerializer.Deserialize<AfectacionesDefinicion>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    });
    public static AfectacionesDefinicion Reglas => Definicion.Value;
    private static readonly Lazy<string> VersionDefinicion = new(() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Reglas))));
    public static string Version => VersionDefinicion.Value;
    public static string Tipo(string tipo) => tipo.Trim().ToUpperInvariant() is "BG" ? "VG" : tipo.Trim().ToUpperInvariant();
    public static string Sub(string nombre) => nombre.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace(".", "") switch
    {
        "PREMIUN" or "PREMIUM" => "PREMIUM", "LINEA" or "NOVLINEA" => "NOV-LINEA", "SELECTO" => "SELECTO",
        "VGBIG" or "BIG" => "VG-BIG", "VGLINEA" => "VG-LINEA", "REG" or "REGULAR" or "VR" => "VR",
        "TERAMA" or "TAMARILLA" or "TERAMARILLA" => "T.Amarilla", var value => value
    };
    private static string Sku(string sku) => sku.Trim().ToUpperInvariant();
    public static bool Excluido(string tipo, string sku) => Reglas.Planes.TryGetValue(Tipo(tipo), out var p) && p.Bloqueados.Any(b => b.Excluir && b.Sku == Sku(sku));

    public static AfectacionesResultado Simular(string tipo, IReadOnlyList<AfectacionesSubclasificacion> subclasificaciones,
        IReadOnlyList<AfectacionesValor> originales, IReadOnlyList<AfectacionesValor> cambios, bool normalizar = false, bool permitirPendientes = false)
    {
        if (!Reglas.Planes.TryGetValue(Tipo(tipo), out var reglas)) throw new PlanDiarioCatalogoValidacionException("El plan de afectaciones debe ser NOV o VG.");
        if (originales.Count > 10000 || cambios.Count > 10000) throw new PlanDiarioCatalogoValidacionException("Demasiados productos para calcular las afectaciones.");
        var subs = subclasificaciones.ToDictionary(s => s.Id);
        foreach (var v in originales.Concat(cambios))
        {
            if (string.IsNullOrWhiteSpace(v.Sku) || v.Sku.Length > 50)
                throw new PlanDiarioCatalogoValidacionException("Selecciona un SKU válido para editar su distribución.");
            if (!subs.ContainsKey(v.SubClasificacionId))
                throw new PlanDiarioCatalogoValidacionException($"La subclasificación de {v.Sku} no pertenece al plan. Vuelve a abrir el día.");
        }
        // La precisión de capturas previas (incluido float) no invalida una nueva captura válida.
        // Las entradas nuevas sí respetan centésimas. Tolerar sólo ruido binario insignificante.
        foreach (var v in cambios)
        {
            if (v.PartSub is < 0 or > 1)
                throw new PlanDiarioCatalogoValidacionException($"{v.Sku}: la distribución debe estar entre 0.00 y 1.00; por ejemplo, 0.98 equivale al 98%.");
            if (Math.Abs(v.PartSub - decimal.Round(v.PartSub, 2, MidpointRounding.AwayFromZero)) > 0.000000001m)
                throw new PlanDiarioCatalogoValidacionException($"{v.Sku}: captura de 0.00 a 1.00 con hasta dos decimales, por ejemplo 0.98 (98%).");
        }
        originales = originales.Select(v => v with { PartSub = decimal.Round(v.PartSub, 2, MidpointRounding.AwayFromZero) }).ToList();
        cambios = cambios.Select(v => v with { PartSub = decimal.Round(v.PartSub, 2, MidpointRounding.AwayFromZero) }).ToList();
        if (originales.GroupBy(v => (Sku(v.Sku), v.SubClasificacionId)).Any(g => g.Count() > 1) || cambios.GroupBy(v => (Sku(v.Sku), v.SubClasificacionId)).Any(g => g.Count() > 1))
            throw new PlanDiarioCatalogoValidacionException("Hay distribuciones repetidas para el mismo SKU y subclasificación.");
        var salida = originales.ToDictionary(v => (Sku(v.Sku), v.SubClasificacionId), v => v with { Sku = Sku(v.Sku) });
        var resultados = new List<AfectacionesCambio>();
        var afectados = new HashSet<(string, int)>();
        foreach (var sub in subclasificaciones)
        {
            var nombre = Sub(sub.Nombre);
            var baseValores = originales.Where(v => v.SubClasificacionId == sub.Id).ToDictionary(v => Sku(v.Sku), v => v.PartSub);
            var editados = cambios.Where(v => v.SubClasificacionId == sub.Id).ToDictionary(v => Sku(v.Sku), v => v);
            var presentes = baseValores.Keys.Concat(editados.Keys).ToHashSet();
            var sumas = reglas.Sumas.Where(r => r.Subclasificaciones.Contains(nombre)).ToDictionary(r => r.Destino);
            var descuentos = reglas.Descuentos.Where(r => r.Subclasificaciones.Contains(nombre)).Select(r => (r.Origen, r.Destino)).ToList();
            var modos = descuentos.Distinct().ToDictionary(d => d, _ => (Antes: true, Despues: true));
            // Conservar el comportamiento de líneas para SKU que no aparecen en la plantilla suministrada.
            foreach (var v in originales.Where(v => v.SubClasificacionId == sub.Id && !reglas.Conocidos.Contains(Sku(v.Sku))))
            {
                var anterior = string.IsNullOrWhiteSpace(v.LineaCodigo) ? null : Sku(v.LineaCodigo);
                var siguiente = editados.TryGetValue(Sku(v.Sku), out var editado) ? editado.LineaCodigo : v.LineaCodigo;
                siguiente = string.IsNullOrWhiteSpace(siguiente) ? null : Sku(siguiente);
                foreach (var padre in new[] { anterior, siguiente }.Where(p => p is not null).Distinct())
                {
                    var arista = (Origen: Sku(v.Sku), Destino: padre!);
                    descuentos.Add(arista); modos[arista] = (padre == anterior, padre == siguiente);
                }
            }
            descuentos = descuentos.Distinct().ToList();
            var nodos = presentes.Concat(descuentos.SelectMany(d => new[] { d.Origen, d.Destino }))
                .Concat(sumas.Values.SelectMany(s => s.Fuentes.Concat(s.Restar).Append(s.Destino))).ToHashSet();
            var aristas = descuentos.Concat(sumas.Values.SelectMany(s => s.Fuentes.Concat(s.Restar).Select(f => (Origen: f, Destino: s.Destino)))).Distinct().ToList();
            var grados = nodos.ToDictionary(n => n, n => aristas.Count(a => a.Destino == n));
            var cola = new Queue<string>(nodos.Where(n => grados[n] == 0));
            var orden = new List<string>();
            while (cola.TryDequeue(out var n)) { orden.Add(n); foreach (var a in aristas.Where(a => a.Origen == n)) if (--grados[a.Destino] == 0) cola.Enqueue(a.Destino); }
            if (orden.Count != nodos.Count) throw new PlanDiarioCatalogoValidacionException("Las afectaciones contienen un ciclo. Revisa las líneas de los productos.");
            var tocados = editados.Keys.ToHashSet();
            if (normalizar) tocados.UnionWith(sumas.Keys.Concat(reglas.Bloqueados.Select(b => b.Sku)));
            var actuales = new Dictionary<string,decimal>(baseValores);
            foreach (var v in editados) actuales[v.Key] = v.Value.PartSub;
            decimal Antes(string sku) => baseValores.GetValueOrDefault(sku);
            decimal Actual(string sku) => actuales.GetValueOrDefault(sku);
            decimal Delta((string Origen, string Destino) d) => (modos[d].Despues ? Actual(d.Origen) : 0) - (modos[d].Antes ? Antes(d.Origen) : 0);
            foreach (var sku in orden)
            {
                if (!tocados.Contains(sku)) continue;
                var bloqueo = reglas.Bloqueados.FirstOrDefault(b => b.Sku == sku);
                var motivo = "Captura manual";
                var valor = Actual(sku);
                if (bloqueo is not null)
                {
                    if (editados.TryGetValue(sku, out var editado) && editado.PartSub != 0)
                        throw new PlanDiarioCatalogoValidacionException($"{sku}: {bloqueo.Motivo} en {Tipo(tipo)}.");
                    valor = 0; motivo = bloqueo.Motivo;
                }
                else if (sumas.TryGetValue(sku, out var suma))
                {
                    valor = suma.Fuentes.Sum(Actual) - suma.Restar.Sum(Actual);
                    motivo = "Suma de " + string.Join(" + ", suma.Fuentes) + (suma.Restar.Count == 0 ? "" : " menos " + string.Join(" + ", suma.Restar));
                }
                else
                {
                    var entradas = descuentos.Where(d => d.Destino == sku).ToList();
                    var delta = entradas.Sum(Delta);
                    valor -= delta;
                    if (delta != 0) motivo = "Descuento/restitución por " + string.Join(", ", entradas.Where(d => Delta(d) != 0).Select(d => d.Origen));
                }
                if (!presentes.Contains(sku))
                {
                    if ((!sumas.ContainsKey(sku) && valor != 0 && descuentos.Any(d => d.Destino == sku && Delta(d) != 0)) ||
                        (sumas.TryGetValue(sku, out var ausente) && ausente.Restar.Any(f => Actual(f) != Antes(f))))
                        throw new PlanDiarioCatalogoValidacionException($"Falta {sku} en {sub.Nombre}; agrégalo al plan antes de mover el producto que lo afecta.");
                    continue;
                }
                actuales[sku] = valor;
                afectados.Add((sku, sub.Id));
                var previo = salida.GetValueOrDefault((sku, sub.Id));
                salida[(sku, sub.Id)] = new(sku, sub.Id, valor, editados.TryGetValue(sku, out var cambio) ? cambio.LineaCodigo : previo?.LineaCodigo);
                if (valor != Antes(sku)) resultados.Add(new(sku, sub.Id, sub.Nombre, Antes(sku), valor, motivo));
                // También recalcular un agregado cuya captura previa sea inconsistente aunque el origen conserve su valor.
                foreach (var a in aristas.Where(a => a.Origen == sku)) tocados.Add(a.Destino);
            }
        }
        // Calcular primero permite reparar una captura histórica fuera de rango. En edición
        // del día se conserva como pendiente; ningún guardado utiliza ese modo tolerante.
        var pendientes = salida.Values.Where(v => v.PartSub is < 0 or > 1).Select(v =>
        {
            var sub = subs[v.SubClasificacionId];
            var suma = reglas.Sumas.FirstOrDefault(s => s.Destino == v.Sku && s.Subclasificaciones.Contains(Sub(sub.Nombre)));
            return new AfectacionesPendiente(v.Sku, sub.Id, sub.Nombre, v.PartSub, suma?.Fuentes ?? []);
        }).ToList();
        // El catálogo sólo persiste los SKU afectados. Un pendiente histórico de otro
        // grupo no debe impedir corregir éste; el guardado diario sí valida todo el día.
        var invalidos = pendientes.Where(p => normalizar || afectados.Contains((p.Sku, p.SubClasificacionId))).ToList();
        if (!permitirPendientes && invalidos.Count > 0)
            throw new PlanDiarioCatalogoValidacionException("No se puede guardar: " + string.Join("; ", invalidos.Select(p =>
                $"{p.Sku} en {p.Valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} para {p.Subclasificacion}")) +
                ". Debe quedar entre 0 y 1. Puedes seguir editando los productos de origen para corregirlo.");
        return new(salida.Values.ToList(), resultados, pendientes);
    }
}
