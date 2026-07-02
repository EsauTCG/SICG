using Plataforma_CG.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace Plataforma_CG.Services.ControlPrecios
{
    public class ListaPreciosCarnesgPdf : IDocument
    {
        private readonly ListaPreciosPdfDto _model;

        public ListaPreciosCarnesgPdf(ListaPreciosPdfDto model)
        {
            _model = model;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(18);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9).FontColor(Colors.Black));

                page.Content().Column(col =>
                {
                    col.Item().Element(ComposeHeader);
                    col.Item().PaddingTop(4).Element(ComposeVigencia);
                    col.Item().PaddingTop(2).Element(ComposeBody);
                });
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Border(1).Padding(8).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.ConstantItem(150).Height(78).Border(1).AlignMiddle().AlignCenter().Element(c =>
                    {
                        if (_model.Empresa.LogoBytes is { Length: > 0 })
                            c.Image(_model.Empresa.LogoBytes).FitArea();
                        else
                            c.Background(Colors.Grey.Lighten3)
                             .AlignMiddle()
                             .AlignCenter()
                             .Text("LOGO CARNES G")
                             .Bold();
                    });

                    row.RelativeItem().PaddingHorizontal(10).Column(mid =>
                    {
                        mid.Item().AlignCenter().Text(_model.Empresa.RazonSocial).Bold().FontSize(14);
                        mid.Item().PaddingTop(2).AlignCenter().Text(_model.Empresa.Direccion1).FontSize(9);
                        mid.Item().AlignCenter().Text(_model.Empresa.Direccion2).FontSize(9);
                        mid.Item().PaddingTop(4).AlignCenter().Text($"{_model.Empresa.Telefonos}    {_model.Empresa.Celular}").FontSize(9);
                        mid.Item().AlignCenter().Text(_model.Empresa.VentasTexto).Italic().SemiBold().FontSize(9);
                        mid.Item().AlignCenter().Text($"Email- {_model.Empresa.EmailVentas}").Underline().SemiBold().FontSize(9);
                    });

                    row.ConstantItem(120).Height(78).Border(1).AlignMiddle().AlignCenter().Element(c =>
                    {
                        if (_model.Empresa.SelloBytes is { Length: > 0 })
                            c.Image(_model.Empresa.SelloBytes).FitArea();
                        else
                            c.AlignCenter().Column(x =>
                            {
                                x.Item().Text("SELLO").Bold();
                                x.Item().Text("TIF / APROBADO").FontSize(8);
                            });
                    });
                });

                col.Item()
                   .PaddingTop(4)
                   .AlignCenter()
                   .Text(_model.Empresa.CertificacionTexto)
                   .Italic()
                   .SemiBold()
                   .FontSize(8);
            });
        }

        private void ComposeVigencia(IContainer container)
        {
            container.Border(1)
                     .PaddingVertical(4)
                     .AlignCenter()
                     .Text($"{_model.VigenciaTexto}    {_model.PlantaTexto}")
                     .Bold()
                     .Italic()
                     .FontSize(11);
        }

        private void ComposeBody(IContainer container)
        {
            var pares = ArmarPares(_model.Grupos);

            container.Border(1).Column(col =>
            {
                foreach (var par in pares)
                {
                    col.Item().Element(x => ComposePairSection(x, par.left, par.right));
                }
            });
        }

        private void ComposePairSection(IContainer container, GrupoPreciosPdfDto? left, GrupoPreciosPdfDto? right)
        {
            var maxRows = Math.Max(left?.Items?.Count ?? 0, right?.Items?.Count ?? 0);

            container.Row(row =>
            {
                row.RelativeItem().Element(c => ComposeHalfTable(c, left, maxRows, drawRightBorder: true));
                row.RelativeItem().Element(c => ComposeHalfTable(c, right, maxRows, drawRightBorder: false));
            });
        }

        private void ComposeHalfTable(IContainer container, GrupoPreciosPdfDto? grupo, int maxRows, bool drawRightBorder)
        {
            container.BorderRight(drawRightBorder ? 1 : 0).Column(col =>
            {
                col.Item()
                   .BorderBottom(1)
                   .MinHeight(28)
                   .AlignMiddle()
                   .AlignCenter()
                   .Text(grupo?.Titulo ?? string.Empty)
                   .Bold()
                   .FontSize(11);

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(50);
                        columns.RelativeColumn();
                        columns.ConstantColumn(70);
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "CODIGO");
                        HeaderCell(header.Cell(), "");
                        HeaderCell(header.Cell(), "PRECIO");
                    });

                    for (int i = 0; i < maxRows; i++)
                    {
                        var item = grupo != null && i < grupo.Items.Count
                            ? grupo.Items[i]
                            : null;

                        BodyCell(table, item?.Codigo, "center");
                        BodyCell(table, item?.Producto, "left");
                        BodyCell(table, item?.Precio.HasValue == true
                            ? FormatearPrecio(item.Precio.Value)
                            : string.Empty, "right");
                    }
                });
            });
        }

        private static void HeaderCell(IContainer cell, string text)
        {
            cell.Border(1)
                .PaddingVertical(4)
                .PaddingHorizontal(3)
                .AlignMiddle()
                .AlignCenter()
                .Text(text)
                .Bold()
                .FontSize(8.5f);
        }

        private static void BodyCell(TableDescriptor table, string? text, string align = "left")
        {
            var cell = table.Cell()
                .Border(1)
                .MinHeight(20)
                .PaddingVertical(2)
                .PaddingHorizontal(4)
                .AlignMiddle();

            if (align == "center")
                cell = cell.AlignCenter();
            else if (align == "right")
                cell = cell.AlignRight();

            cell.Text(text ?? string.Empty)
                .FontSize(8.5f);
        }

        private static string FormatearPrecio(decimal value)
        {
            return "$   " + value.ToString("N2", CultureInfo.InvariantCulture);
        }

        private static List<(GrupoPreciosPdfDto? left, GrupoPreciosPdfDto? right)> ArmarPares(List<GrupoPreciosPdfDto> grupos)
        {
            var list = new List<(GrupoPreciosPdfDto? left, GrupoPreciosPdfDto? right)>();

            for (int i = 0; i < grupos.Count; i += 2)
            {
                var left = grupos[i];
                GrupoPreciosPdfDto? right = i + 1 < grupos.Count ? grupos[i + 1] : null;
                list.Add((left, right));
            }

            return list;
        }
    }
}