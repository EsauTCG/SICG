using ClosedXML.Excel;
using Plataforma_CG.Models.Reportes.Exporting.Interfaces;
using Plataforma_CG.Models.Reportes.ViewModels;

namespace Plataforma_CG.Models.Reportes.Exporting.Services
{
    public class ExcelExporter : IExcelExporter
    {
        public byte[] Export(
            IEnumerable<object> rows,
            List<ColumnaReporteViewModel> columns,
            string reportName)
        {
            using var workbook = new XLWorkbook();

            var sheetName = LimpiarNombreHoja(
                string.IsNullOrWhiteSpace(reportName)
                    ? "Reporte"
                    : reportName);

            var worksheet = workbook.Worksheets.Add(sheetName);

            worksheet.SheetView.FreezeRows(1);

            var visibleColumns = columns
                .Where(x => x.Visible)
                .ToList();

            int columnIndex = 1;

            foreach (var column in visibleColumns)
            {
                worksheet.Cell(1, columnIndex).Value = column.Titulo;
                columnIndex++;
            }

            int rowIndex = 2;

            foreach (var row in rows)
            {
                columnIndex = 1;

                foreach (var column in visibleColumns)
                {
                    var value = column.ValueSelector?.Invoke(row);

                    var cell = worksheet.Cell(
                        rowIndex,
                        columnIndex);

                    if (value == null)
                    {
                        cell.Value = string.Empty;
                    }
                    else if (value is DateTime fecha)
                    {
                        cell.Value = fecha;
                        cell.Style.DateFormat.Format = "dd/MM/yyyy";
                    }
                    else if (value is DateTimeOffset fechaOffset)
                    {
                        cell.Value = fechaOffset.DateTime;
                        cell.Style.DateFormat.Format = "dd/MM/yyyy";
                    }
                    else if (value is decimal decimalValue)
                    {
                        cell.Value = decimalValue;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                    }
                    else if (value is double doubleValue)
                    {
                        cell.Value = doubleValue;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                    }
                    else if (value is float floatValue)
                    {
                        cell.Value = floatValue;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                    }
                    else if (value is int intValue)
                    {
                        cell.Value = intValue;
                    }
                    else if (value is long longValue)
                    {
                        cell.Value = longValue;
                    }
                    else if (value is bool boolValue)
                    {
                        cell.Value = boolValue;
                    }
                    else
                    {
                        cell.Value = value.ToString() ?? string.Empty;
                    }

                    columnIndex++;
                }

                rowIndex++;
            }

            var headerRow = worksheet.Row(1);

            headerRow.Style.Font.Bold = true;
            headerRow.Style.Font.FontColor = XLColor.White;
            headerRow.Style.Fill.BackgroundColor = XLColor.CornflowerBlue;
            headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            if (worksheet.RangeUsed() != null)
            {
                worksheet.RangeUsed()!.SetAutoFilter();
                worksheet.Columns().AdjustToContents();
            }

            using var stream = new MemoryStream();

            workbook.SaveAs(stream);

            return stream.ToArray();
        }

        private static string LimpiarNombreHoja(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return "Reporte";
            }

            var caracteresInvalidos = new[]
            {
                ':',
                '\\',
                '/',
                '?',
                '*',
                '[',
                ']'
            };

            foreach (var caracter in caracteresInvalidos)
            {
                nombre = nombre.Replace(caracter.ToString(), string.Empty);
            }

            nombre = nombre.Trim();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                nombre = "Reporte";
            }

            if (nombre.Length > 31)
            {
                nombre = nombre.Substring(0, 31);
            }

            return nombre;
        }
    }
}