using ClosedXML.Excel;
using Plataforma_CG.Models.Operaciones.Planeacion.Diaria;
using System.Globalization;

namespace Plataforma_CG.Services;

/// <summary>Exporta una copia editable del plan visible. No consulta ni modifica bases de datos.</summary>
public static class PlanDiarioExcelService
{
    private const string TemplateResource = "Plataforma_CG.Plantillas.PlanDiarioReferencia.xlsx";
    private const string SinDato = "Sin dato";

    public static byte[] Generar(PlanDiarioExcelRequest plan)
    {
        Validar(plan);
        using var templateStream = typeof(PlanDiarioExcelService).Assembly.GetManifestResourceStream(TemplateResource)
            ?? throw new InvalidOperationException("No se encontró la plantilla del plan diario.");
        using var template = new XLWorkbook(templateStream);
        var source = template.Worksheets.First(s => s.Name.Trim() == plan.TipoPlan);
        var originalCount = plan.TipoPlan == "NOV" ? 3 : 4;
        var templateOrder = Enumerable.Range(4, originalCount)
            .Select(c => Normalizar(source.Cell(5, c).GetString())).ToList();
        var canales = plan.Canales.OrderBy(c =>
        {
            var index = templateOrder.IndexOf(Normalizar(c.Nombre));
            return index < 0 ? int.MaxValue : index;
        }).ThenBy(c => c.SubClasificacionId).ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(plan.TipoPlan);
        var catalogSheet = workbook.AddWorksheet("CONCENTRADO");
        var catalog = LeerCatalogo(template, source, plan.TipoPlan, originalCount);
        ActualizarCatalogo(catalog, plan.Productos);
        var catalogLastRow = EscribirCatalogo(catalogSheet, catalog);
        var lookup = $"'CONCENTRADO'!$A$3:$K${catalogLastRow}";

        var count = canales.Count;
        var porcentajeCol = count + 4;
        var kgCol = porcentajeCol + 1;
        var canalesCol = porcentajeCol + 2;
        var skuInyCol = porcentajeCol + 3;
        var nombreInyCol = skuInyCol + 1;
        var porcentajeInyCol = skuInyCol + 2;
        var tipoInyCol = skuInyCol + 3;
        var kgInyCol = skuInyCol + 4;
        var piezasCol = skuInyCol + 5;
        var etiquetaCol = skuInyCol + 6;
        var lastCol = etiquetaCol + 4;
        var lastRow = 5 + plan.Productos.Count;

        // Reutilizar los estilos de cada función de columna, aunque cambie el número de subclasificaciones.
        for (var col = 1; col <= lastCol; col++)
        {
            var originalCol = col <= 3 ? col : col < porcentajeCol
                ? Math.Min(col, 3 + originalCount) : col - count + originalCount;
            sheet.Column(col).Width = source.Column(originalCol).Width;
            sheet.Range(1, col, 3, col).Style = source.Cell(2, originalCol).Style;
            sheet.Cell(5, col).Style = source.Cell(5, originalCol).Style;
            sheet.Range(6, col, lastRow, col).Style = source.Cell(6, originalCol).Style;
        }
        sheet.Range(1, 1, lastRow, lastCol).Style.Font.FontName = "Calibri";
        sheet.Range(5, 1, lastRow, lastCol).Style.Font.FontSize = 11;
        sheet.Range(1, 1, lastRow, lastCol).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Range(5, 1, 5, lastCol).Style.Alignment.WrapText = true;
        sheet.Range(5, 1, 5, lastCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Row(5).Height = 32;
        sheet.Row(4).Height = 9;
        sheet.Rows(6, lastRow).Height = 27;
        sheet.Column(1).Width = 17;
        sheet.Column(2).Width = 14;
        sheet.Column(skuInyCol).Width = 16;
        sheet.Column(3).Width = 54;
        sheet.Column(nombreInyCol).Width = 45;
        sheet.Column(tipoInyCol).Width = 18;
        sheet.Column(etiquetaCol).Width = 36;
        sheet.Column(lastCol).Width = 44;
        sheet.Column(kgCol).Width = 14;
        sheet.Column(kgInyCol).Width = 14;
        sheet.Column(canalesCol).Width = 12;
        sheet.Column(piezasCol).Width = 12;

        Titulo(sheet, 1, 1, 2, 2, plan.TipoPlan == "NOV" ? "PLAN DE NOVILLO" : "PLAN DE VACA GORDA", "00B050");
        sheet.Range(3, 1, 3, 2).Merge().Style = sheet.Cell(1, 1).Style;
        sheet.Cell(3, 1).Value = DateTime.ParseExact(plan.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        sheet.Cell(3, 1).Style.DateFormat.Format = "dd/MM/yyyy";
        Titulo(sheet, 1, 3, 1, canalesCol, "DESHUESE", "948A54");
        Titulo(sheet, 1, skuInyCol, 3, piezasCol, "INYECCIÓN", "31869B");
        Titulo(sheet, 1, etiquetaCol, 3, lastCol, "EMPAQUE", "953735");
        sheet.Cell(2, 3).Value = "No. Canal completa";
        sheet.Cell(3, 3).Value = "Kg por canal completa";
        sheet.Range(2, 3, 3, canalesCol).Style.Fill.BackgroundColor = XLColor.FromHtml("#948A54");
        sheet.Range(2, 3, 3, canalesCol).Style.Font.FontColor = XLColor.White;
        sheet.Range(2, 3, 3, canalesCol).Style.Font.Bold = true;
        sheet.Cell(2, kgCol).Value = "TOTAL";
        sheet.Range(2, canalesCol, 3, canalesCol).Merge();
        sheet.Cell(2, canalesCol).FormulaA1 = $"SUM(D2:{Col(porcentajeCol - 1)}2)";
        sheet.Cell(2, canalesCol).Style.Font.FontSize = 18;
        sheet.Cell(2, canalesCol).Style.NumberFormat.Format = "#,##0";
        for (var i = 0; i < count; i++)
        {
            sheet.Cell(2, 4 + i).Value = canales[i].Canales;
            sheet.Cell(3, 4 + i).Value = canales[i].PesoPromedio;
            sheet.Cell(3, 4 + i).Style.NumberFormat.Format = "#,##0.00";
            sheet.Column(4 + i).Width = Math.Max(12, Math.Min(20, canales[i].Nombre.Length + 2));
        }
        var headers = new[] { "MÁSTER", "SKU", "Producto" }.Concat(canales.Select(c => c.Nombre))
            .Concat(new[] { "%", "KG", "CANALES", "SKU", "Producto", "% Iny", "Mejorado/Natural", "KG", "PZ", "Etiqueta", "Caducidad", "Almacén", "Manejo", "Observaciones" }).ToArray();
        for (var col = 1; col <= headers.Length; col++) sheet.Cell(5, col).Value = headers[col - 1];

        for (var i = 0; i < plan.Productos.Count; i++)
        {
            var row = i + 6;
            var product = plan.Productos[i];
            var selected = $"{Col(skuInyCol)}{row}";
            var porcentaje = $"{Col(porcentajeCol)}{row}";
            sheet.Range(row, 1, row, lastCol).Style.Fill.BackgroundColor = i % 2 == 0
                ? XLColor.FromHtml("#DCE6F1") : XLColor.White;
            sheet.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9D9D9");
            sheet.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFC000");
            sheet.Cell(row, skuInyCol).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFC000");
            sheet.Cell(row, 1).FormulaA1 = $"VLOOKUP({selected},{lookup},3,FALSE)";
            sheet.Cell(row, 2).Value = product.Sku;
            sheet.Cell(row, 3).Value = product.Nombre;
            var kgTerms = new List<string>();
            var canalTerms = new List<string>();
            for (var c = 0; c < count; c++)
            {
                var letter = Col(c + 4);
                sheet.Cell(row, c + 4).Value = product.Participaciones.FirstOrDefault(p => p.SubClasificacionId == canales[c].SubClasificacionId)?.Proporcion ?? 0;
                sheet.Cell(row, c + 4).Style.NumberFormat.Format = "General";
                kgTerms.Add($"{letter}{row}*{letter}$2*{letter}$3");
                canalTerms.Add($"{letter}{row}*{letter}$2");
            }
            // El rendimiento pertenece al SKU base; la conversión controla el producto y la inyección posteriores.
            sheet.Cell(row, porcentajeCol).FormulaA1 = $"VLOOKUP(B{row},{lookup},7,FALSE)";
            sheet.Cell(row, kgCol).FormulaA1 = $"({string.Join("+", kgTerms)})*{porcentaje}";
            sheet.Cell(row, canalesCol).FormulaA1 = string.Join("+", canalTerms);
            sheet.Cell(row, skuInyCol).Value = product.SkuConversion;
            sheet.Cell(row, nombreInyCol).FormulaA1 = $"VLOOKUP({selected},{lookup},2,FALSE)";
            sheet.Cell(row, porcentajeInyCol).FormulaA1 = $"VLOOKUP({selected},{lookup},8,FALSE)";
            sheet.Cell(row, tipoInyCol).FormulaA1 = $"IF({Col(porcentajeInyCol)}{row}>0,\"Mejorado\",\"Natural\")";
            sheet.Cell(row, kgInyCol).FormulaA1 = $"{Col(kgCol)}{row}*(1+{Col(porcentajeInyCol)}{row})";
            sheet.Cell(row, piezasCol).FormulaA1 = $"IF(ISNUMBER(VLOOKUP({selected},{lookup},6,FALSE)),VLOOKUP({selected},{lookup},6,FALSE)*{Col(canalesCol)}{row},\"{SinDato}\")";
            sheet.Cell(row, etiquetaCol).FormulaA1 = $"VLOOKUP({selected},{lookup},10,FALSE)";
            sheet.Cell(row, etiquetaCol + 1).FormulaA1 = $"VLOOKUP({selected},{lookup},11,FALSE)";
            sheet.Cell(row, etiquetaCol + 2).FormulaA1 = $"VLOOKUP({selected},{lookup},4,FALSE)";
            sheet.Cell(row, etiquetaCol + 3).FormulaA1 = $"VLOOKUP({selected},{lookup},5,FALSE)";
            sheet.Cell(row, lastCol).FormulaA1 = $"IF(VLOOKUP({selected},{lookup},9,FALSE)=0,\"\",VLOOKUP({selected},{lookup},9,FALSE))";
            foreach (var col in new[] { porcentajeCol, porcentajeInyCol }) sheet.Cell(row, col).Style.NumberFormat.Format = "0.00%";
            foreach (var col in new[] { kgCol, kgInyCol }) sheet.Cell(row, col).Style.NumberFormat.Format = "#,##0.0";
            sheet.Cell(row, canalesCol).Style.NumberFormat.Format = "#,##0.00";
            sheet.Cell(row, piezasCol).Style.NumberFormat.Format = "#,##0.00";
            sheet.Range(row, 3, row, 3).Style.Alignment.WrapText = true;
            sheet.Cell(row, nombreInyCol).Style.Alignment.WrapText = true;
            sheet.Cell(row, etiquetaCol).Style.Alignment.WrapText = true;
            sheet.Cell(row, lastCol).Style.Alignment.WrapText = true;
        }

        var participaciones = sheet.Range(6, 4, lastRow, porcentajeCol - 1);
        // Exportar también los factores existentes mayores a uno sin cambiarlos ni bloquear la descarga.
        participaciones.CreateDataValidation().Decimal.EqualOrGreaterThan(0);
        participaciones.AddConditionalFormat().WhenEquals(0).Fill.SetBackgroundColor(XLColor.FromHtml("#FF9900"));
        participaciones.AddConditionalFormat().WhenEquals(1).Fill.SetBackgroundColor(XLColor.FromHtml("#00B050"));
        participaciones.AddConditionalFormat().WhenIsTrue("AND(D6>0,D6<1)").Fill.SetBackgroundColor(XLColor.FromHtml("#00B0F0"));
        var tipos = sheet.Range(6, tipoInyCol, lastRow, tipoInyCol);
        tipos.AddConditionalFormat().WhenEquals("\"Mejorado\"").Fill.SetBackgroundColor(XLColor.FromHtml("#C6EFCE"));
        tipos.AddConditionalFormat().WhenEquals("\"Natural\"").Fill.SetBackgroundColor(XLColor.FromHtml("#FFEB9C"));
        sheet.Range(6, skuInyCol, lastRow, skuInyCol).CreateDataValidation().List(catalogSheet.Range(3, 1, catalogLastRow, 1), true);
        sheet.Range(2, 4, 2, porcentajeCol - 1).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        sheet.Range(3, 4, 3, porcentajeCol - 1).CreateDataValidation().Decimal.EqualOrGreaterThan(0);
        sheet.Range(5, 1, lastRow, lastCol).SetAutoFilter();
        sheet.SheetView.Freeze(5, 3);
        sheet.ShowGridLines = false;
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A3Paper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 5);
        sheet.PageSetup.PrintAreas.Add(1, 1, lastRow, lastCol);
        workbook.CalculateMode = XLCalculateMode.Auto;
        workbook.RecalculateAllFormulas();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream, new SaveOptions { EvaluateFormulasBeforeSaving = true, ValidatePackage = true });
        return stream.ToArray();
    }

    public static void Validar(PlanDiarioExcelRequest plan)
    {
        if (plan == null || !DateTime.TryParseExact(plan.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            (plan.TipoPlan != "NOV" && plan.TipoPlan != "VG"))
            throw new ArgumentException("La fecha o el tipo del plan no son válidos. Actualiza el plan diario.");
        if (plan.Canales == null || plan.Productos == null || plan.Canales.Count is < 1 or > 20 || plan.Productos.Count is < 1 or > 3000)
            throw new ArgumentException("El plan debe contener subclasificaciones y productos para exportar.");
        if (plan.Canales.Any(c => c == null || c.SubClasificacionId <= 0 || string.IsNullOrWhiteSpace(c.Nombre) || c.Canales < 0 || c.PesoPromedio < 0 || (c.Canales > 0 && c.PesoPromedio <= 0)) ||
            plan.Canales.GroupBy(c => c.SubClasificacionId).Any(g => g.Count() > 1))
            throw new ArgumentException("Revisa los canales y los pesos diarios antes de exportar.");
        var ids = plan.Canales.Select(c => c.SubClasificacionId).ToHashSet();
        for (var index = 0; index < plan.Productos.Count; index++)
        {
            var product = plan.Productos[index];
            if (product == null || string.IsNullOrWhiteSpace(product.Sku))
                throw new ArgumentException($"Falta el SKU del producto de la fila {index + 1} del plan diario.");
            var sku = product.Sku;
            if (string.IsNullOrWhiteSpace(product.Nombre))
                throw new ArgumentException($"SKU {sku}: falta el nombre del producto.");
            if (string.IsNullOrWhiteSpace(product.SkuConversion))
                throw new ArgumentException($"SKU {sku}: falta seleccionar el SKU de conversión.");
            if (product.Porcentaje is < 0 or > 100)
                throw new ArgumentException($"SKU {sku}: el rendimiento ({product.Porcentaje}) debe estar entre 0 y 100.");
            if (product.PorcentajeInyeccion is < 0 or > 100)
                throw new ArgumentException($"SKU {sku}: el porcentaje de inyección ({product.PorcentajeInyeccion}) debe estar entre 0 y 100.");
            if (product.Participaciones == null)
                throw new ArgumentException($"SKU {sku}: no se recibieron las participaciones del producto.");
            foreach (var participation in product.Participaciones)
            {
                if (participation == null || !ids.Contains(participation.SubClasificacionId))
                    throw new ArgumentException($"SKU {sku}: una participación no corresponde a las subclasificaciones del plan cargado. Actualiza el diario.");
                if (participation.Proporcion < 0)
                {
                    var nombre = plan.Canales.First(c => c.SubClasificacionId == participation.SubClasificacionId).Nombre;
                    throw new ArgumentException($"SKU {sku}, subclasificación {nombre}: la participación ({participation.Proporcion}) no puede ser negativa.");
                }
            }
            var repeated = product.Participaciones.GroupBy(x => x.SubClasificacionId).FirstOrDefault(g => g.Count() > 1);
            if (repeated != null)
            {
                var nombre = plan.Canales.First(c => c.SubClasificacionId == repeated.Key).Nombre;
                throw new ArgumentException($"SKU {sku}: la subclasificación {nombre} está repetida en el plan cargado. Actualiza el diario.");
            }
        }
        var duplicate = plan.Productos.GroupBy(p => p.Sku, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException($"El SKU {duplicate.Key} está repetido en el plan diario. Actualiza el diario.");
    }

    private sealed class Catalogo
    {
        public string Sku = "", Nombre = "", Master = "", Almacen = SinDato, Manejo = SinDato, Observaciones = "", Etiqueta = SinDato;
        public decimal? Piezas;
        public decimal Porcentaje, Inyeccion;
        public string Caducidad = SinDato;
    }

    private static Dictionary<string, Catalogo> LeerCatalogo(XLWorkbook template, IXLWorksheet source, string tipo, int count)
    {
        var result = new Dictionary<string, Catalogo>(StringComparer.OrdinalIgnoreCase);
        var table = template.Worksheet("CONCENTRADO").Table(tipo == "NOV" ? "Tabla1" : "Tabla25");
        foreach (var row in table.DataRange.Rows())
        {
            var sku = row.Cell(1).GetString().Trim();
            if (sku.Length == 0 || result.ContainsKey(sku)) continue;
            result.Add(sku, new Catalogo
            {
                Sku = sku, Nombre = row.Cell(2).GetString(), Master = row.Cell(3).GetString(),
                Almacen = Texto(row.Cell(4)), Manejo = Texto(row.Cell(5)),
                Piezas = row.Cell(6).TryGetValue<decimal>(out var piezas) ? piezas : null,
                Porcentaje = row.Cell(7).TryGetValue<decimal>(out var porcentaje) ? porcentaje : 0,
                Inyeccion = row.Cell(8).TryGetValue<decimal>(out var inyeccion) ? inyeccion : 0,
                Observaciones = row.Cell(9).GetString()
            });
        }
        // Las etiquetas y caducidades del ejemplo están en las hojas de plan, fuera de su catálogo.
        for (var row = 6; row <= source.LastRowUsed()!.RowNumber(); row++)
        {
            var sku = source.Cell(row, count + 7).GetString().Trim();
            if (!result.TryGetValue(sku, out var entry)) continue;
            entry.Etiqueta = Texto(source.Cell(row, count + 13));
            entry.Caducidad = Texto(source.Cell(row, count + 14));
        }
        return result;
    }

    private static void ActualizarCatalogo(Dictionary<string, Catalogo> catalog, List<ProductoPlanExcel> products)
    {
        Catalogo Obtener(string sku)
        {
            if (!catalog.TryGetValue(sku, out var entry)) catalog.Add(sku, entry = new Catalogo { Sku = sku });
            return entry;
        }
        foreach (var product in products)
        {
            var original = Obtener(product.Sku);
            original.Nombre = product.Nombre;
            original.Master = string.IsNullOrWhiteSpace(product.Master) ? "SIN MASTER" : product.Master;
            original.Porcentaje = product.Porcentaje / 100;
        }
        foreach (var product in products)
        {
            var converted = Obtener(product.SkuConversion);
            if (!string.IsNullOrWhiteSpace(product.NombreConversion)) converted.Nombre = product.NombreConversion;
            if (string.IsNullOrWhiteSpace(converted.Nombre)) converted.Nombre = SinDato;
            if (string.IsNullOrWhiteSpace(converted.Master)) converted.Master = product.Master;
            converted.Inyeccion = product.PorcentajeInyeccion / 100;
            if (!string.IsNullOrWhiteSpace(product.Etiqueta)) converted.Etiqueta = product.Etiqueta;
            if (!string.IsNullOrWhiteSpace(product.DiasCaducidad)) converted.Caducidad = product.DiasCaducidad;
        }
    }

    private static int EscribirCatalogo(IXLWorksheet sheet, Dictionary<string, Catalogo> catalog)
    {
        sheet.Cell(1, 1).Value = "Catálogo del plan diario";
        var headers = new[] { "SKU", "PRODUCTO", "MÁSTER", "ALMACÉN", "MANEJO", "PZ POR CANAL", "%", "INY", "OBSERVACIONES", "ETIQUETA", "CADUCIDAD" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(2, i + 1).Value = headers[i];
        var row = 3;
        foreach (var item in catalog.Values.OrderBy(c => c.Sku, StringComparer.OrdinalIgnoreCase))
        {
            sheet.Cell(row, 1).Value = item.Sku;
            sheet.Cell(row, 2).Value = item.Nombre;
            sheet.Cell(row, 3).Value = item.Master;
            sheet.Cell(row, 4).Value = item.Almacen;
            sheet.Cell(row, 5).Value = item.Manejo;
            sheet.Cell(row, 6).Value = item.Piezas.HasValue ? XLCellValue.FromObject(item.Piezas.Value) : SinDato;
            sheet.Cell(row, 7).Value = item.Porcentaje;
            sheet.Cell(row, 8).Value = item.Inyeccion;
            sheet.Cell(row, 9).Value = string.IsNullOrWhiteSpace(item.Observaciones) ? "" : item.Observaciones;
            sheet.Cell(row, 10).Value = item.Etiqueta;
            sheet.Cell(row, 11).Value = decimal.TryParse(item.Caducidad, NumberStyles.Number, CultureInfo.InvariantCulture, out var dias)
                ? XLCellValue.FromObject(dias) : item.Caducidad;
            row++;
        }
        sheet.Range(2, 1, row - 1, 11).CreateTable("CatalogoPlanDiario");
        sheet.Range(3, 7, row - 1, 8).Style.NumberFormat.Format = "0.00%";
        sheet.Columns(1, 11).Width = 19;
        sheet.Column(2).Width = 55;
        sheet.Column(9).Width = 42;
        sheet.Column(10).Width = 38;
        sheet.SheetView.Freeze(2, 1);
        sheet.ShowGridLines = false;
        return row - 1;
    }

    private static string Texto(IXLCell cell) => string.IsNullOrWhiteSpace(cell.GetString()) ? SinDato : cell.GetString();
    private static string Normalizar(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string Col(int number) => XLHelper.GetColumnLetterFromNumber(number);
    private static void Titulo(IXLWorksheet sheet, int r1, int c1, int r2, int c2, string title, string color)
    {
        var range = sheet.Range(r1, c1, r2, c2).Merge();
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + color);
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.FirstCell().Value = title;
    }
}
