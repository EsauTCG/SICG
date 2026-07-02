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
    public record InvReporteReq(string almacen,string desde, string hasta);


    private static string Norm(string s) => (s ?? "").Trim().ToUpperInvariant();

    // ====== Meat: buscar etiqueta normal (trae también almacén desde CommerciaNet) ======
    private async Task<(string Sku, decimal Kg, string AlmacenNombre)?> BuscarEtiquetaAsync(
     string cs, string etiqueta, string dbAlmacen)
    {
        if (string.IsNullOrWhiteSpace(cs)) return null;

        // ✅ Whitelist (para evitar inyección en nombre de BD)
        dbAlmacen = (dbAlmacen ?? "").Trim();
        if (dbAlmacen != "CommerciaNet" && dbAlmacen != "TIF_CommerciaNet")
            throw new Exception("dbAlmacen inválida.");

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();

        var sql = $@"
SELECT TOP 1
    Sku           = UPPER(LTRIM(RTRIM(a.Articulo))),
    Kg            = CAST(a.PesoNeto AS DECIMAL(18,4)),
    AlmacenNombre = ISNULL(b.Nombre,'')
FROM dbo.Produccion a
INNER JOIN [{dbAlmacen}].dbo.Almacen b
    ON a.Almacen = b.AlmacenId
WHERE UPPER(LTRIM(RTRIM(a.CodigoEtiqueta))) = @etiqueta
  AND a.Estatus = 1;";

        var info = await cn.QueryFirstOrDefaultAsync<(string Sku, decimal Kg, string AlmacenNombre)>(
            sql, new { etiqueta });

        if (string.IsNullOrWhiteSpace(info.Sku)) return null;
        return info;
    }


    // ====== Meat: buscar tarima -> lista de etiquetas ======
    private async Task<List<string>> BuscarTarimaEtiquetasAsync(string cs, string tarimaCodigo)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(cs)) return list;

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();

        const string sql = @"
SELECT p.CodigoEtiqueta
FROM Tarima t
JOIN TarimaDetalle td ON td.TarimaId = t.TarimaId
JOIN Produccion p     ON p.ProduccionId = td.ProduccionId
WHERE t.Estatus = 1
  AND p.Estatus = 1
  AND UPPER(LTRIM(RTRIM(t.Nombre))) = @TarimaCodigo;";

        var rows = await cn.QueryAsync<string>(sql, new { TarimaCodigo = tarimaCodigo });
        foreach (var r in rows)
        {
            var etq = Norm(r);
            if (!string.IsNullOrWhiteSpace(etq)) list.Add(etq);
        }

        return list;
    }

    // ====== Guardar en BD principal + actualizar FechaInventario en Meat ======
    private async Task<(bool ok, bool dup, string msg)> GuardarScanAsync(
        string almacen, string codigo, string sku, decimal kg, string origen, string usuario, string csMeat)
    {
        try
        {
            var now = DateTime.Now;

            var row = new InventarioScanEtiqueta
            {
                Almacen = almacen,
                CodigoEtiqueta = codigo,
                Sku = sku,
                Kg = kg,
                Origen = origen,
                Usuario = usuario ?? "",
                Fecha = now
            };

            _context.InventarioScanEtiquetas.Add(row);
            await _context.SaveChangesAsync();

            // ✅ Si guardó OK en tu BD principal, marca FechaInventario en Meat
            var marked = await MarcarFechaInventarioAsync(csMeat, codigo, now);

            return (true, false, $"OK [{origen}] {sku} {kg:N2} kg · {almacen} · MeatUpd={(marked ? "SI" : "NO")}");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx
                                          && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
        {
            return (false, true, $"Duplicado: {codigo}");
        }
        catch (Exception ex)
        {
            return (false, false, $"Error al guardar: {ex.Message}");
        }
    }



    // ====== Procesa 1 código: etiqueta o tarima ======
    private async Task<List<object>> ProcesarCodigoAsync(string almacenRequest, string codigo)
    {
        var results = new List<object>();

        var csP1 = _cfg.GetConnectionString("CadenaMeatP1");
        var csTif = _cfg.GetConnectionString("CadenaMeatTIF");

        // 1) Buscar etiqueta normal (P1 -> TIF)
        (string Sku, decimal Kg, string AlmacenNombre)? info = null;
        string origen = "";
        string csMeat = ""; // ✅ esta es la que usaremos para actualizar FechaInventario

        info = await BuscarEtiquetaAsync(csP1, codigo, "CommerciaNet");
        if (info != null)
        {
            origen = "P1";
            csMeat = csP1;
        }
        else
        {
            info = await BuscarEtiquetaAsync(csTif, codigo, "TIF_CommerciaNet");
            if (info != null)
            {
                origen = "TIF";
                csMeat = csTif;
            }
        }

        // Si fue etiqueta, guardar en BD principal + actualizar FechaInventario en Meat
        if (info != null)
        {
            var sku = Norm(info.Value.Sku);
            var kg = info.Value.Kg;

            // ✅ almacén real sale del join de CommerciaNet / TIF_CommerciaNet
            var almacenReal = Norm(info.Value.AlmacenNombre);
            if (string.IsNullOrWhiteSpace(almacenReal))
                almacenReal = Norm(almacenRequest);

            var usuario = User?.Identity?.Name ?? "";

            var (ok, dup, msg) = await GuardarScanAsync(
                almacenReal,
                codigo,
                sku,
                kg,
                origen,
                usuario,
                csMeat // ✅ IMPORTANTE: para marcar FechaInventario en la BD correcta
            );

            results.Add(new
            {
                kind = ok ? "ok" : (dup ? "dup" : "err"),
                codigo = codigo,
                sku,
                kg,
                origen,
                almacen = almacenReal,
                msg
            });

            return results;
        }

        // 2) Si no fue etiqueta, intentar TARIMA (P1 -> TIF)
        var etqs = await BuscarTarimaEtiquetasAsync(csP1, codigo);
        origen = etqs.Count > 0 ? "P1" : "";

        if (etqs.Count == 0)
        {
            etqs = await BuscarTarimaEtiquetasAsync(csTif, codigo);
            if (etqs.Count > 0) origen = "TIF";
        }

        if (etqs.Count == 0)
        {
            results.Add(new
            {
                kind = "err",
                codigo = codigo,
                sku = "",
                kg = 0m,
                origen = "",
                almacen = Norm(almacenRequest),
                msg = $"No se encontró etiqueta ni tarima: {codigo}"
            });
            return results;
        }

        // Procesa etiquetas de tarima (cada etiqueta se valida y guarda individualmente)
        foreach (var e in etqs.Distinct())
        {
            var sub = await ProcesarCodigoAsync(almacenRequest, e);
            results.AddRange(sub);
        }

        return results;
    }







    // ✅ Batch: POST /Inventario/ScanBatch { almacen, codigos }
    [HttpPost("Inventario/ScanBatch")]
    public async Task<IActionResult> ScanBatch([FromBody] InvScanBatchReq req)
    {
        var almacen = Norm(req.almacen);
        if (string.IsNullOrWhiteSpace(almacen)) almacen = "ALL";

        var codigos = (req.codigos ?? new List<string>())
            .Select(Norm)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (codigos.Count == 0)
            return BadRequest(new { ok = false, msg = "No hay códigos." });

        var results = new List<object>();
        foreach (var c in codigos)
        {
            var r = await ProcesarCodigoAsync(almacen, c);
            results.AddRange(r);
        }

        var okCount = results.Count(x => ((dynamic)x).kind == "ok");
        var dupCount = results.Count(x => ((dynamic)x).kind == "dup");
        var errCount = results.Count(x => ((dynamic)x).kind == "err");

        return Ok(new
        {
            ok = true,
            almacen,
            total = results.Count,
            okCount,
            dupCount,
            errCount,
            results
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
            "logoPDF.png"
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
