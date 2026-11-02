using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Data;
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;

public class InventarioController : Controller
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _cfg;

    public InventarioController(AppDbContext context, IConfiguration cfg)
    {
        _context = context;
        _cfg = cfg;
    }

    // ✅ Requests que tu JS manda
    public record InvScanBatchReq(string almacen, List<string> codigos);
    public record InvReporteReq(string almacen, string desde, string hasta);


    private static string Norm(string s) => (s ?? "").Trim().ToUpperInvariant();

    // OPTIMIZACION: consultas en grupo, sin cambiar contrato del front ni reportes.
    private sealed class EtiquetaInfo
    {
        public string CodigoEtiqueta { get; set; } = "";
        public string Sku { get; set; } = "";
        public decimal Kg { get; set; }
        public string AlmacenNombre { get; set; } = "";
        public string Origen { get; set; } = "";
    }

    private sealed class ScanPendiente
    {
        public string Codigo { get; set; } = "";
        public string Sku { get; set; } = "";
        public decimal Kg { get; set; }
        public string Almacen { get; set; } = "";
        public string Origen { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; } = "";
    }

    private sealed class ScanResultado
    {
        public string kind { get; set; } = "err";
        public string codigo { get; set; } = "";
        public string sku { get; set; } = "";
        public decimal kg { get; set; }
        public string origen { get; set; } = "";
        public string almacen { get; set; } = "";
        public string msg { get; set; } = "";
    }

    private static ScanResultado Resultado(ScanPendiente p, string kind, string msg) => new()
    {
        kind = kind,
        codigo = p.Codigo,
        sku = p.Sku,
        kg = p.Kg,
        origen = p.Origen,
        almacen = p.Almacen,
        msg = msg
    };

    // Busqueda rapida indexada. El respaldo legado solo se ejecuta despues
    // de intentar en P1 y TIF, evitando barridos caros por cada tarima TIF.
    private async Task<Dictionary<string, EtiquetaInfo>> BuscarEtiquetasLoteAsync(
        string? cs, IEnumerable<string> codigos, string dbAlmacen, string origen, bool legado = false)
    {
        var encontrados = new Dictionary<string, EtiquetaInfo>(StringComparer.OrdinalIgnoreCase);
        var lista = codigos.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (lista.Length == 0 || string.IsNullOrWhiteSpace(cs)) return encontrados;
        if (dbAlmacen != "CommerciaNet" && dbAlmacen != "TIF_CommerciaNet")
            throw new ArgumentException("dbAlmacen invalida.");

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        const int lote = 150;
        for (int i = 0; i < lista.Length; i += lote)
        {
            var parte = lista.Skip(i).Take(lote).ToArray();
            string filtro = legado
                ? "UPPER(LTRIM(RTRIM(a.CodigoEtiqueta))) IN @codigos"
                : "a.CodigoEtiqueta IN @codigos";
            var sql = $@"SELECT a.CodigoEtiqueta,
       Sku=UPPER(LTRIM(RTRIM(a.Articulo))),
       Kg=CAST(a.PesoNeto AS DECIMAL(18,4)),
       AlmacenNombre=ISNULL(b.Nombre,'')
FROM dbo.Produccion a
INNER JOIN [{dbAlmacen}].dbo.Almacen b ON a.Almacen=b.AlmacenId
WHERE {filtro} AND a.Estatus=1;";
            var rows = await cn.QueryAsync<EtiquetaInfo>(sql, new { codigos = parte });
            foreach (var row in rows)
            {
                var clave = Norm(row.CodigoEtiqueta);
                if (!encontrados.ContainsKey(clave))
                {
                    row.Origen = origen;
                    encontrados[clave] = row;
                }
            }
        }
        return encontrados;
    }

    // Busca tarima primero mediante igualdad indexable. Conserva la busqueda
    // anterior como respaldo para los nombres con formato historico.
    private async Task<List<string>> BuscarTarimaEtiquetasAsync(string? cs, string codigo)
    {
        if (string.IsNullOrWhiteSpace(cs)) return new List<string>();
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        const string baseSql = @"SELECT p.CodigoEtiqueta
FROM dbo.Tarima t
JOIN dbo.TarimaDetalle td ON td.TarimaId=t.TarimaId
JOIN dbo.Produccion p ON p.ProduccionId=td.ProduccionId
WHERE t.Estatus=1 AND p.Estatus=1 AND ";
        var rows = (await cn.QueryAsync<string>(baseSql + "t.Nombre=@codigo;", new { codigo })).ToList();
        if (rows.Count == 0)
            rows = (await cn.QueryAsync<string>(baseSql +
                "UPPER(LTRIM(RTRIM(t.Nombre)))=@codigo;", new { codigo })).ToList();
        return rows.Select(Norm).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<Dictionary<string, EtiquetaInfo>> ObtenerMetadatosAsync(
        IEnumerable<string> codigos, string? csP1, string? csTif)
    {
        var lista = codigos.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var datos = await BuscarEtiquetasLoteAsync(csP1, lista, "CommerciaNet", "P1");
        var faltantes = lista.Where(x => !datos.ContainsKey(x)).ToArray();
        if (faltantes.Length > 0)
        {
            var tif = await BuscarEtiquetasLoteAsync(csTif, faltantes, "TIF_CommerciaNet", "TIF");
            foreach (var par in tif) datos.TryAdd(par.Key, par.Value);
        }
        // Preserva lectura de codigos antiguos, pero no realiza el costoso
        // barrido legado de P1 antes de buscar por indice en TIF.
        faltantes = lista.Where(x => !datos.ContainsKey(x)).ToArray();
        if (faltantes.Length > 0)
        {
            var p1Legado = await BuscarEtiquetasLoteAsync(csP1, faltantes,
                "CommerciaNet", "P1", legado: true);
            foreach (var par in p1Legado) datos.TryAdd(par.Key, par.Value);
            faltantes = lista.Where(x => !datos.ContainsKey(x)).ToArray();
            if (faltantes.Length > 0)
            {
                var tifLegado = await BuscarEtiquetasLoteAsync(csTif, faltantes,
                    "TIF_CommerciaNet", "TIF", legado: true);
                foreach (var par in tifLegado) datos.TryAdd(par.Key, par.Value);
            }
        }
        return datos;
    }

    // Conserva la restriccion unica UX_InventarioScanEtiqueta_Dia.
    // El filtro evita intentar insertar miles de duplicados; la restriccion sigue
    // garantizando seguridad frente a escaneos simultaneos.
    private async Task<Dictionary<string, ScanResultado>> GuardarLoteAsync(
        IReadOnlyList<ScanPendiente> entradas, string? csP1, string? csTif)
    {
        var salida = new Dictionary<string, ScanResultado>(StringComparer.OrdinalIgnoreCase);
        if (entradas.Count == 0) return salida;
        var fechaDia = DateTime.Today;
        var fechaSiguiente = fechaDia.AddDays(1);
        var codigos = entradas.Select(x => x.Codigo).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Se consulta lo ya guardado EN EL DIA, sin alterar la regla de duplicados.
        var yaRegistrados = await _context.InventarioScanEtiquetas.AsNoTracking()
            .Where(x => codigos.Contains(x.CodigoEtiqueta) && x.Fecha >= fechaDia && x.Fecha < fechaSiguiente)
            .Select(x => new { x.CodigoEtiqueta, x.Almacen }).ToListAsync();
        var existe = new HashSet<string>(yaRegistrados.Select(x => x.Almacen + "\u001f" + x.CodigoEtiqueta),
            StringComparer.OrdinalIgnoreCase);
        var candidatos = new List<ScanPendiente>();
        foreach (var e in entradas)
        {
            var key = e.Almacen + "\u001f" + e.Codigo;
            if (!existe.Add(key)) salida[e.Codigo] = Resultado(e, "dup", $"Duplicado: {e.Codigo}");
            else candidatos.Add(e);
        }

        // Un SaveChanges para todas las cajas nuevas, con fallback individual si
        // otra sesion inserto la misma caja simultaneamente.
        var guardadas = new List<ScanPendiente>();
        if (candidatos.Count > 0)
        {
            try
            {
                foreach (var e in candidatos) _context.InventarioScanEtiquetas.Add(NuevaEntidad(e));
                await _context.SaveChangesAsync();
                guardadas.AddRange(candidatos);
            }
            catch (DbUpdateException)
            {
                _context.ChangeTracker.Clear();
                foreach (var e in candidatos)
                {
                    try
                    {
                        _context.InventarioScanEtiquetas.Add(NuevaEntidad(e));
                        await _context.SaveChangesAsync();
                        guardadas.Add(e);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is SqlException sx && (sx.Number == 2601 || sx.Number == 2627))
                    {
                        salida[e.Codigo] = Resultado(e, "dup", $"Duplicado: {e.Codigo}");
                    }
                    catch (Exception ex)
                    {
                        salida[e.Codigo] = Resultado(e, "err", $"Error al guardar: {ex.Message}");
                    }
                    finally { _context.ChangeTracker.Clear(); }
                }
            }
            catch (Exception ex)
            {
                _context.ChangeTracker.Clear();
                foreach (var e in candidatos)
                    salida[e.Codigo] = Resultado(e, "err", $"Error al guardar: {ex.Message}");
            }
            finally { _context.ChangeTracker.Clear(); }
        }

        // Solo se marca FechaInventario para lo efectivamente insertado, agrupado por P1/TIF.
        foreach (var grupo in guardadas.GroupBy(e => e.Origen))
        {
            var cs = grupo.Key == "P1" ? csP1 : csTif;
            Dictionary<string, bool> marcadas;
            try { marcadas = await MarcarFechasLoteAsync(cs, grupo.ToList()); }
            catch (Exception ex)
            {
                foreach (var e in grupo)
                    salida[e.Codigo] = Resultado(e, "err", $"Error al guardar: {ex.Message}");
                continue;
            }
            foreach (var e in grupo)
            {
                var ok = marcadas.TryGetValue(e.Codigo, out var marcado) && marcado;
                salida[e.Codigo] = Resultado(e, "ok",
                    $"OK [{e.Origen}] {e.Sku} {e.Kg:N2} kg · {e.Almacen} · MeatUpd={(ok ? "SI" : "NO")}");
            }
        }
        return salida;
    }

    private static InventarioScanEtiqueta NuevaEntidad(ScanPendiente e) => new()
    {
        Almacen = e.Almacen,
        CodigoEtiqueta = e.Codigo,
        Sku = e.Sku,
        Kg = e.Kg,
        Origen = e.Origen,
        Usuario = e.Usuario,
        Fecha = e.Fecha
    };

    private async Task<Dictionary<string, bool>> MarcarFechasLoteAsync(
        string? cs, IReadOnlyList<ScanPendiente> entradas)
    {
        var resultado = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(cs)) return resultado;
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        // VALUES parametrizado: evita abrir una conexion y ejecutar un UPDATE por caja.
        // Se conserva la fecha de cada registro individual.
        const int tam = 150;
        for (int offset = 0; offset < entradas.Count; offset += tam)
        {
            var parte = entradas.Skip(offset).Take(tam).ToArray();
            var args = new DynamicParameters();
            var valores = new List<string>();
            for (int i = 0; i < parte.Length; i++)
            {
                valores.Add($"(@c{i},@f{i})");
                args.Add($"c{i}", parte[i].Codigo);
                args.Add($"f{i}", parte[i].Fecha);
            }
            var sql = $@"
DECLARE @Cambiadas TABLE (CodigoEtiqueta NVARCHAR(250));
UPDATE p SET FechaInventario=v.Fecha
OUTPUT inserted.CodigoEtiqueta INTO @Cambiadas(CodigoEtiqueta)
FROM dbo.Produccion p
JOIN (VALUES {string.Join(",", valores)}) v(Codigo,Fecha)
  ON p.CodigoEtiqueta=v.Codigo
WHERE p.Estatus=1;
SELECT CodigoEtiqueta FROM @Cambiadas;";
            var marcadas = await cn.QueryAsync<string>(sql, args);
            foreach (var codigo in marcadas) resultado[Norm(codigo)] = true;
            // Casos antiguos que dependen de TRIM/UPPER: usar la actualizacion original.
            foreach (var e in parte.Where(x => !resultado.ContainsKey(x.Codigo)))
            {
                var filas = await cn.ExecuteAsync(@"
UPDATE dbo.Produccion SET FechaInventario=@fecha
WHERE UPPER(LTRIM(RTRIM(CodigoEtiqueta)))=@codigo AND Estatus=1;",
                    new { fecha = e.Fecha, codigo = e.Codigo });
                resultado[e.Codigo] = filas > 0;
            }
        }
        return resultado;
    }

    // MISMO ENDPOINT Y MISMA ESTRUCTURA JSON que utiliza Inventarios.cshtml.
    [HttpPost("Inventario/ScanBatch")]
    public async Task<IActionResult> ScanBatch([FromBody] InvScanBatchReq req)
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var almacen = Norm(req.almacen);
        if (string.IsNullOrWhiteSpace(almacen)) almacen = "ALL";
        var codigos = (req.codigos ?? new List<string>()).Select(Norm)
            .Where(x => x.Length > 0).ToList();
        if (codigos.Count == 0) return BadRequest(new { ok = false, msg = "No hay códigos." });
        var csP1 = _cfg.GetConnectionString("CadenaMeatP1");
        var csTif = _cfg.GetConnectionString("CadenaMeatTIF");
        var contenidos = new List<object>();
        var resultados = new List<ScanResultado>();
        var usuario = User?.Identity?.Name ?? "";

        foreach (var c in codigos)
        {
            // Igual que antes: determinar primero si el codigo es tarima para
            // el resumen. Buscar P1 y luego TIF.
            var tiempoTarima = System.Diagnostics.Stopwatch.StartNew();
            var etiquetasTarima = await BuscarTarimaEtiquetasAsync(csP1, c);
            if (etiquetasTarima.Count == 0)
                etiquetasTarima = await BuscarTarimaEtiquetasAsync(csTif, c);
            tiempoTarima.Stop();
            var esTarima = etiquetasTarima.Count > 0;
            var lista = esTarima ? etiquetasTarima : new List<string> { c };
            var tiempoMetadatos = System.Diagnostics.Stopwatch.StartNew();
            var metadatos = await ObtenerMetadatosAsync(lista, csP1, csTif);
            tiempoMetadatos.Stop();
            var pendientes = new List<ScanPendiente>();
            var porCodigo = new Dictionary<string, ScanResultado>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in lista)
            {
                if (!metadatos.TryGetValue(e, out var d))
                {
                    porCodigo[e] = new ScanResultado
                    {
                        kind = "err",
                        codigo = e,
                        almacen = almacen,
                        msg = esTarima ? $"No se encontró etiqueta ni tarima: {e}"
                            : $"No se encontró etiqueta ni tarima: {c}"
                    };
                    continue;
                }
                var almacenReal = Norm(d.AlmacenNombre);
                if (almacenReal.Length == 0) almacenReal = almacen;
                pendientes.Add(new ScanPendiente
                {
                    Codigo = e,
                    Sku = Norm(d.Sku),
                    Kg = d.Kg,
                    Almacen = almacenReal,
                    Origen = d.Origen,
                    Usuario = usuario,
                    Fecha = DateTime.Now
                });
            }

            var tiempoGuardar = System.Diagnostics.Stopwatch.StartNew();
            if (pendientes.Count > 0)
            {
                var guardado = await GuardarLoteAsync(pendientes, csP1, csTif);
                foreach (var par in guardado) porCodigo[par.Key] = par.Value;
            }
            tiempoGuardar.Stop();
            System.Diagnostics.Trace.WriteLine(
                $"[ScanBatch] codigo={c} cajas={lista.Count} tarimaMs={tiempoTarima.ElapsedMilliseconds} " +
                $"metadatosMs={tiempoMetadatos.ElapsedMilliseconds} guardarYFechaMs={tiempoGuardar.ElapsedMilliseconds}");
            var bloque = lista.Select(e => porCodigo[e]).ToList();
            resultados.AddRange(bloque);
            if (esTarima)
            {
                contenidos.Add(new
                {
                    codigo = c,
                    tipo = "TARIMA",
                    cantidadCajas = lista.Count,
                    registradas = bloque.Count(x => x.kind == "ok"),
                    duplicadas = bloque.Count(x => x.kind == "dup"),
                    errores = bloque.Count(x => x.kind == "err")
                });
            }
            else if (bloque.Any(x => x.kind == "ok" || x.kind == "dup"))
                contenidos.Add(new { codigo = c, tipo = "ETIQUETA", cantidadCajas = 1 });
        }
        reloj.Stop();
        Response.Headers["Server-Timing"] = $"scan;dur={reloj.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}";
        // Tiempos visibles en logs del servidor, sin afectar las respuestas del front.
        System.Diagnostics.Trace.WriteLine($"[Inventario/ScanBatch] Codigos={codigos.Count} Resultados={resultados.Count} TotalMs={reloj.ElapsedMilliseconds}");
        return Ok(new
        {
            ok = true,
            almacen,
            total = resultados.Count,
            okCount = resultados.Count(x => x.kind == "ok"),
            dupCount = resultados.Count(x => x.kind == "dup"),
            errCount = resultados.Count(x => x.kind == "err"),
            contenidos,
            results = resultados
        });
    }

    // ✅ Reporte: POST /Inventario/Reporte { almacen }  (almacen = "ALL" para todo)

    [HttpPost("Inventario/Reporte")]
    public async Task<IActionResult> Reporte([FromBody] InvReporteReq req)
    {
        var almacen = Norm(req.almacen);
        if (string.IsNullOrWhiteSpace(almacen))
            return BadRequest(new { ok = false, msg = "almacen requerido." });

        var desde = string.IsNullOrWhiteSpace(req.desde)
            ? DateTime.Today
            : DateTime.ParseExact(req.desde, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var hastaExclusivo = string.IsNullOrWhiteSpace(req.hasta)
            ? desde.AddDays(1)
            : DateTime.ParseExact(req.hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1); // exclusivo

        var conn = _context.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        // ✅ Total de etiquetas
        var total = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.InventarioScanEtiqueta
WHERE (@almacen = 'ALL' OR Almacen = @almacen)
  AND Fecha >= @desde
  AND Fecha <  @hasta;", new { almacen, desde, hasta = hastaExclusivo });

        // ✅ NUEVO: lista de almacenes que existen en ese rango (para el select dinámico)
        var almacenes = (await conn.QueryAsync<string>(@"
SELECT DISTINCT Almacen
FROM dbo.InventarioScanEtiqueta
WHERE Fecha >= @desde
  AND Fecha <  @hasta
  AND ISNULL(LTRIM(RTRIM(Almacen)),'') <> ''
ORDER BY Almacen;", new { desde, hasta = hastaExclusivo })).ToList();

        // ✅ IMPORTANTE: ahora el reporte incluye s.Almacen
        var rows = (await conn.QueryAsync(@"
SELECT
    almacen = s.Almacen,
    sku     = s.Sku,
    name    = ISNULL(a.ProductoNombre, s.Sku),
    qty     = COUNT(1)
FROM dbo.InventarioScanEtiqueta s
LEFT JOIN dbo.ArticuloSap a
  ON UPPER(LTRIM(RTRIM(a.ProductoCodigo))) = UPPER(LTRIM(RTRIM(s.Sku)))
WHERE (@almacen = 'ALL' OR s.Almacen = @almacen)
  AND s.Fecha >= @desde
  AND s.Fecha <  @hasta
GROUP BY s.Almacen, s.Sku, a.ProductoNombre
ORDER BY COUNT(1) DESC;",
            new { almacen, desde, hasta = hastaExclusivo }
        )).ToList();

        return Ok(new
        {
            ok = true,
            almacen,
            desde,
            hasta = hastaExclusivo.AddDays(-1), // para que el front vea inclusive
            total,
            almacenes,   // ✅ NUEVO
            rows         // ✅ AHORA cada row trae "almacen"
        });
    }




    private async Task<bool> MarcarFechaInventarioAsync(string cs, string etiqueta, DateTime fecha)
    {
        if (string.IsNullOrWhiteSpace(cs)) return false;

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();

        const string sql = @"
UPDATE dbo.Produccion
SET FechaInventario = @fecha
WHERE UPPER(LTRIM(RTRIM(CodigoEtiqueta))) = @etiqueta
  AND Estatus = 1;";

        var rows = await cn.ExecuteAsync(sql, new { fecha, etiqueta });
        return rows > 0;
    }


    [HttpGet("Inventario/ReportePdf")]
    public async Task<IActionResult> ReportePdf(
     string almacen = "ALL",
     string? desde = null,
     string? hasta = null)
    {
        almacen = Norm(almacen);
        if (string.IsNullOrWhiteSpace(almacen))
            almacen = "ALL";

        var dDesde = string.IsNullOrWhiteSpace(desde)
            ? DateTime.Today
            : DateTime.ParseExact(desde, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var dHastaExclusivo = string.IsNullOrWhiteSpace(hasta)
            ? dDesde.AddDays(1)
            : DateTime.ParseExact(hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1);

        var conn = _context.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();

        // ================= DETALLE =================
        var detalle = (await conn.QueryAsync(@"
SELECT
    s.Fecha,
    s.Almacen,
    s.Sku,
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)),''), s.Sku),
    s.Kg,
    s.Origen,
    s.Usuario,
    s.CodigoEtiqueta
FROM dbo.InventarioScanEtiqueta s
LEFT JOIN dbo.ArticuloSap a
  ON UPPER(LTRIM(RTRIM(a.ProductoCodigo))) = UPPER(LTRIM(RTRIM(s.Sku)))
WHERE (@almacen = 'ALL' OR s.Almacen = @almacen)
  AND s.Fecha >= @desde
  AND s.Fecha <  @hasta
ORDER BY s.Fecha DESC;",
            new { almacen, desde = dDesde, hasta = dHastaExclusivo }))
            .ToList();

        // ================= RESUMEN =================
        var resumen = (await conn.QueryAsync(@"
SELECT
    s.Almacen,
    s.Sku,
    Nombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)),''), s.Sku),
    Cantidad = COUNT(1),
    KgTotal  = SUM(s.Kg)
FROM dbo.InventarioScanEtiqueta s
LEFT JOIN dbo.ArticuloSap a
  ON UPPER(LTRIM(RTRIM(a.ProductoCodigo))) = UPPER(LTRIM(RTRIM(s.Sku)))
WHERE (@almacen = 'ALL' OR s.Almacen = @almacen)
  AND s.Fecha >= @desde
  AND s.Fecha <  @hasta
GROUP BY s.Almacen, s.Sku, a.ProductoNombre
ORDER BY s.Almacen, COUNT(1) DESC;",
            new { almacen, desde = dDesde, hasta = dHastaExclusivo }))
            .ToList();

        var totalEtiquetas = detalle.Count;
        var totalKg = detalle.Sum(x => (decimal)x.Kg);

        // ================= LOGO =================
        QuestPDF.Settings.License = LicenseType.Community;

        var logoPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "images",
            "logoCubo.png"
        );

        byte[]? logoBytes = null;
        if (System.IO.File.Exists(logoPath))
            logoBytes = System.IO.File.ReadAllBytes(logoPath);

        // ================= PDF =================
        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(9));

                // ========= HEADER =========
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        if (logoBytes != null)
                        {
                            row.ConstantItem(90)
                               .Height(55)
                               .AlignLeft()
                               .Image(logoBytes, ImageScaling.FitArea);
                        }

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("REPORTE DE INVENTARIO").FontSize(16).Bold();
                            c.Item().Text($"Carnes G • {DateTime.Now:dd/MM/yyyy HH:mm}");
                            c.Item().Text($"Rango: {dDesde:dd/MM/yyyy} - {dHastaExclusivo.AddDays(-1):dd/MM/yyyy}");
                            c.Item().Text($"Almacén: {(almacen == "ALL" ? "TODOS" : almacen)}");
                        });

                        row.ConstantItem(150).AlignRight().Column(c =>
                        {
                            c.Item().Text($"Total etiquetas: {totalEtiquetas}").Bold();
                            c.Item().Text($"Kg total: {totalKg:N2}").Bold();
                        });
                    });

                    col.Item().PaddingTop(8).LineHorizontal(1);
                });

                // ========= CONTENT =========
                page.Content().Column(col =>
                {
                    // ===== RESUMEN =====
                    col.Item().PaddingTop(10).Text("Resumen por Almacén / Artículo").Bold().FontSize(12);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(65); // Almacén
                            columns.ConstantColumn(70); // SKU
                            columns.RelativeColumn();   // Nombre
                            columns.ConstantColumn(50); // Cant
                            columns.ConstantColumn(60); // Kg
                        });

                        table.Header(h =>
                        {
                            HeaderCell(h.Cell(), "Alm");
                            HeaderCell(h.Cell(), "SKU");
                            HeaderCell(h.Cell(), "Descripción");
                            HeaderCell(h.Cell(), "Cant", true);
                            HeaderCell(h.Cell(), "Kg", true);
                        });

                        foreach (var r in resumen)
                        {
                            BodyCell(table.Cell(), (string)r.Almacen);
                            BodyCell(table.Cell(), (string)r.Sku);
                            BodyCell(table.Cell(), (string)r.Nombre);
                            BodyCell(table.Cell(), ((int)r.Cantidad).ToString(), true);
                            BodyCell(table.Cell(), ((decimal)r.KgTotal).ToString("N2"), true);
                        }
                    });

                    // ===== DETALLE =====
                    col.Item().PaddingTop(14).Text("Detalle de Etiquetas").Bold().FontSize(12);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(70); // Fecha
                            columns.ConstantColumn(55); // Alm
                            columns.ConstantColumn(65); // SKU
                            columns.RelativeColumn();   // Nombre
                            columns.ConstantColumn(50); // Kg
                            columns.ConstantColumn(40); // Org
                        });

                        table.Header(h =>
                        {
                            HeaderCell(h.Cell(), "Fecha");
                            HeaderCell(h.Cell(), "Alm");
                            HeaderCell(h.Cell(), "SKU");
                            HeaderCell(h.Cell(), "Descripción");
                            HeaderCell(h.Cell(), "Kg", true);
                            HeaderCell(h.Cell(), "Org");
                        });

                        foreach (var d in detalle)
                        {
                            BodyCell(table.Cell(), ((DateTime)d.Fecha).ToString("dd/MM HH:mm"));
                            BodyCell(table.Cell(), (string)d.Almacen);
                            BodyCell(table.Cell(), (string)d.Sku);
                            BodyCell(table.Cell(), (string)d.Nombre);
                            BodyCell(table.Cell(), ((decimal)d.Kg).ToString("N2"), true);
                            BodyCell(table.Cell(), (string)d.Origen);
                        }
                    });
                });

                // ========= FOOTER =========
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Carnes G • Reporte automático • Página ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();

        var fileName =
            $"ReporteInventario_{(almacen == "ALL" ? "TODOS" : almacen)}_" +
            $"{dDesde:yyyyMMdd}_{dHastaExclusivo.AddDays(-1):yyyyMMdd}.pdf";

        return File(pdfBytes, "application/pdf", fileName);

        // ===== Helpers =====
        static void HeaderCell(IContainer c, string text, bool right = false)
        {
            var cell = c
                .Background(Colors.Grey.Lighten3)
                .BorderBottom(1)
                .BorderColor(Colors.Grey.Medium)
                .Padding(4)
                .DefaultTextStyle(x => x.SemiBold());

            if (right)
                cell = cell.AlignRight();   // ✅ IMPORTANTE: reasignar

            cell.Text(text);
        }

        static void BodyCell(IContainer c, string text, bool right = false)
        {
            var cell = c
                .BorderBottom(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Padding(4);

            if (right)
                cell = cell.AlignRight();   // ✅ IMPORTANTE: reasignar

            cell.Text(text);
        }

    }





}
