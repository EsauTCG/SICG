using System.Collections;
using System.Globalization;
using Plataforma_CG.Models.Reportes.Exporting.Interfaces;
using Plataforma_CG.Models.Reportes.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Plataforma_CG.Models.Reportes.Exporting.Services
{
    public class PdfExporter : IPdfExporter
    {
        public byte[] Export(
            IEnumerable<object> rows,
            List<ColumnaReporteViewModel> columns,
            string reportName)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var visibleColumns = columns
                .Where(x => x.Visible)
                .ToList();

            if (!visibleColumns.Any())
            {
                throw new InvalidOperationException(
                    "El reporte no contiene columnas visibles.");
            }

            var rowList = rows?.ToList() ?? new List<object>();

            var tituloReporte = string.IsNullOrWhiteSpace(reportName)
                ? "Reporte"
                : reportName.Trim();

            var isWideReport = visibleColumns.Count > 7;

            var fontSize = ObtenerTamanoFuente(visibleColumns.Count);
            var headerFontSize = fontSize;
            var cellPadding = visibleColumns.Count > 12 ? 2 : 3;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(
                        isWideReport
                            ? PageSizes.A4.Landscape()
                            : PageSizes.A4);

                    page.Margin(22);
                    page.PageColor(Colors.White);

                    page.DefaultTextStyle(x => x
                        .FontFamily("Arial")
                        .FontSize(fontSize)
                        .FontColor(Colors.Grey.Darken4));

                    page.Header()
                        .Element(header =>
                        {
                            CrearEncabezado(
                                header,
                                tituloReporte,
                                rowList.Count);
                        });

                    page.Content()
                        .PaddingTop(8)
                        .Element(content =>
                        {
                            if (!rowList.Any())
                            {
                                content
                                    .Border(1)
                                    .BorderColor(Colors.Grey.Lighten1)
                                    .Padding(24)
                                    .AlignCenter()
                                    .Text("No se encontraron registros con los filtros seleccionados.")
                                    .FontSize(10)
                                    .FontColor(Colors.Grey.Darken2);

                                return;
                            }

                            CrearTabla(
                                content,
                                rowList,
                                visibleColumns,
                                fontSize,
                                headerFontSize,
                                cellPadding);
                        });

                    page.Footer()
                        .Element(CrearFooter);
                });
            });

            return document.GeneratePdf();
        }

        private static void CrearEncabezado(
            IContainer container,
            string tituloReporte,
            int totalRegistros)
        {
            container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem(2).Column(left =>
                    {
                        left.Item()
                            .Text("SIGO")
                            .Bold()
                            .FontSize(24)
                            .FontColor("#8B0000");

                        left.Item()
                            .Text("Sistema Integral de Gestión Operativa")
                            .FontSize(7)
                            .FontColor(Colors.Grey.Darken2);
                    });

                    row.RelativeItem(5).AlignCenter().Column(center =>
                    {
                        center.Item()
                            .Text("CARNES G")
                            .SemiBold()
                            .FontSize(12)
                            .FontColor(Colors.Grey.Darken3);

                        center.Item()
                            .Text("Reporte operativo")
                            .FontSize(9)
                            .FontColor(Colors.Grey.Darken2);

                        center.Item()
                            .Text($"Total de registros: {totalRegistros:N0}")
                            .FontSize(8)
                            .FontColor(Colors.Grey.Darken2);
                    });

                    row.RelativeItem(2).AlignRight().Column(right =>
                    {
                        right.Item()
                            .Text(DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-MX")))
                            .FontSize(8)
                            .FontColor(Colors.Grey.Darken3);

                        right.Item()
                            .Text(text =>
                            {
                                text.Span("Página ").FontSize(8);
                                text.CurrentPageNumber().FontSize(8);
                            });
                    });
                });

                column.Item()
                    .PaddingTop(10)
                    .Border(1)
                    .BorderColor(Colors.Grey.Darken1)
                    .Column(titleBox =>
                    {
                        titleBox.Item()
                            .Background(Colors.White)
                            .PaddingVertical(6)
                            .AlignCenter()
                            .Text(tituloReporte)
                            .Bold()
                            .FontSize(15)
                            .FontColor(Colors.Grey.Darken4);
                    });
            });
        }

        private static void CrearTabla(
            IContainer container,
            List<object> rows,
            List<ColumnaReporteViewModel> columns,
            float fontSize,
            float headerFontSize,
            int cellPadding)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    foreach (var _ in columns)
                    {
                        definition.RelativeColumn();
                    }
                });

                table.Header(header =>
                {
                    foreach (var column in columns)
                    {
                        header.Cell()
                            .Background(Colors.Grey.Darken2)
                            .Border(0.5f)
                            .BorderColor(Colors.Grey.Darken3)
                            .PaddingVertical(4)
                            .PaddingHorizontal(cellPadding)
                            .AlignCenter()
                            .Text(column.Titulo)
                            .FontSize(headerFontSize)
                            .Bold()
                            .FontColor(Colors.White);
                    }
                });

                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = rows[rowIndex];

                    var background = rowIndex % 2 == 0
                        ? Colors.Grey.Lighten4
                        : Colors.White;

                    foreach (var column in columns)
                    {
                        var value = GetValue(column, row);
                        var text = FormatValue(value);

                        var isNumeric =
                            IsNumericValue(value) ||
                            (column.CssClass?.Contains("text-end", StringComparison.OrdinalIgnoreCase) ?? false);

                        var cell = table.Cell()
                        .Background(background)
                        .BorderBottom(0.5f)
                        .BorderColor(Colors.Grey.Lighten2)
                        .PaddingVertical(3)
                        .PaddingHorizontal(cellPadding);

                        if (isNumeric)
                        {
                            cell.AlignRight()
                                .Text(text)
                                .FontSize(fontSize)
                                .FontColor(Colors.Grey.Darken4);
                        }
                        else
                        {
                            cell.AlignLeft()
                                .Text(text)
                                .FontSize(fontSize)
                                .FontColor(Colors.Grey.Darken4);
                        }
                    }
                }
            });
        }

        private static void CrearFooter(IContainer container)
        {
            container.PaddingTop(8).Row(row =>
            {
                row.RelativeItem()
                    .Text("© 2026 - Carnes G - Todos los derechos reservados")
                    .FontSize(7)
                    .FontColor(Colors.Grey.Darken1);

                row.ConstantItem(130)
                    .AlignRight()
                    .Text(text =>
                    {
                        text.Span("Página ")
                            .FontSize(7)
                            .FontColor(Colors.Grey.Darken1);

                        text.CurrentPageNumber()
                            .FontSize(7)
                            .FontColor(Colors.Grey.Darken1);

                        text.Span(" de ")
                            .FontSize(7)
                            .FontColor(Colors.Grey.Darken1);

                        text.TotalPages()
                            .FontSize(7)
                            .FontColor(Colors.Grey.Darken1);
                    });
            });
        }

        private static string GetValue(
            ColumnaReporteViewModel column,
            object row)
        {
            object? value = null;

            try
            {
                value = column.ValueSelector?.Invoke(row);
            }
            catch
            {
                value = null;
            }

            if (value == null)
            {
                value = GetValueByKey(row, column.Key);
            }

            return FormatValue(value);
        }

        private static object? GetValueByKey(
            object row,
            string key)
        {
            if (row == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (row is IDictionary<string, object?> dictionaryNullable)
            {
                var realKey = dictionaryNullable.Keys.FirstOrDefault(x =>
                    string.Equals(
                        x,
                        key,
                        StringComparison.OrdinalIgnoreCase));

                return realKey == null
                    ? null
                    : dictionaryNullable[realKey];
            }

            if (row is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (string.Equals(
                            entry.Key?.ToString(),
                            key,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Value;
                    }
                }
            }

            var property = row.GetType()
                .GetProperties()
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Name,
                        key,
                        StringComparison.OrdinalIgnoreCase));

            return property?.GetValue(row);
        }

        private static string FormatValue(object? value)
        {
            if (value == null || value == DBNull.Value)
            {
                return string.Empty;
            }

            if (value is DateTime fecha)
            {
                return fecha.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-MX"));
            }

            if (value is DateTimeOffset fechaOffset)
            {
                return fechaOffset.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-MX"));
            }

            if (value is decimal decimalValue)
            {
                return decimalValue.ToString("N2", CultureInfo.GetCultureInfo("es-MX"));
            }

            if (value is double doubleValue)
            {
                return doubleValue.ToString("N2", CultureInfo.GetCultureInfo("es-MX"));
            }

            if (value is float floatValue)
            {
                return floatValue.ToString("N2", CultureInfo.GetCultureInfo("es-MX"));
            }

            if (value is int intValue)
            {
                return intValue.ToString("N0", CultureInfo.GetCultureInfo("es-MX"));
            }

            if (value is long longValue)
            {
                return longValue.ToString("N0", CultureInfo.GetCultureInfo("es-MX"));
            }

            if (value is bool boolValue)
            {
                return boolValue ? "Sí" : "No";
            }

            return value.ToString() ?? string.Empty;
        }

        private static bool IsNumericValue(object? value)
        {
            return value is byte
                or short
                or int
                or long
                or float
                or double
                or decimal;
        }

        private static float ObtenerTamanoFuente(int totalColumnas)
        {
            if (totalColumnas >= 18)
            {
                return 5.2f;
            }

            if (totalColumnas >= 14)
            {
                return 5.8f;
            }

            if (totalColumnas >= 10)
            {
                return 6.5f;
            }

            return 7.5f;
        }
    }
}