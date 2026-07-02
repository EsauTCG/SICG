using System;
using System.Collections.Generic;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static Plataforma_CG.ViewModels.PlaneadorPdfDto;

public static class PlaneadorPdfBuilder
{
    // Colores estilo SAP
    private const string SapBlue = "#0A6ED1";
    private const string Grid = "#C7C7C7";
    private const string HeadBg = "#F5F6F7";
    private const string TextDark = "#1F2937";

    public static byte[] Build(PlaneadorPdfRequest req, byte[]? logoBytes)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(8));

                page.Header().Element(h => HeaderSap(h, req, logoBytes));

                page.Content().Element(c =>
                {
                    c.Column(col =>
                    {
                        if (req.Mode == "ALL")
                        {
                            Section(col, "DESHUESE (con Empaque)", req.Rows, Mode.DES);
                            col.Item().PageBreak();

                            Section(col, "INYECCIÓN (con Empaque)", req.Rows, Mode.INY);
                            col.Item().PageBreak();

                            Section(col, "EMPAQUE Y EMBARQUE", req.Rows, Mode.EMP);
                        }
                        else if (req.Mode == "DES")
                            Section(col, "DESHUESE (con Empaque)", req.Rows, Mode.DES);
                        else if (req.Mode == "INY")
                            Section(col, "INYECCIÓN (con Empaque)", req.Rows, Mode.INY);
                        else
                            Section(col, "EMPAQUE Y EMBARQUE", req.Rows, Mode.EMP);
                    });
                });

                page.Footer().AlignRight().Text(x =>
                {
                    x.Span("Página ").FontSize(8);
                    x.CurrentPageNumber();
                    x.Span(" / ").FontSize(8);
                    x.TotalPages();
                });
            });
        });

        return doc.GeneratePdf();
    }

    private enum Mode { DES, INY, EMP }

    private static void HeaderSap(IContainer c, PlaneadorPdfRequest req, byte[]? logoBytes)
    {
        c.PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Background(SapBlue).Padding(10).Row(r =>
            {
                if (logoBytes != null)
                {
                    r.ConstantItem(52)
                     .Height(52)
                     .Background(Colors.White)
                     .Padding(4)
                     .AlignMiddle()
                     .AlignCenter()
                     .Image(logoBytes, ImageScaling.FitArea);

                    r.Spacing(10);
                }

                r.RelativeItem().AlignMiddle().Column(col =>
                {
                    col.Item().Text("PLAN DE PRODUCCIÓN")
                        .FontColor(Colors.White)
                        .FontSize(14)
                        .SemiBold();

                    col.Item().Text($"{req.PlanTexto} · Generado: {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .FontColor(Colors.White)
                        .FontSize(9);
                });

                r.ConstantItem(180).AlignMiddle().AlignRight()
                    .Text(req.PlanTexto)
                    .FontColor(Colors.White)
                    .FontSize(11)
                    .SemiBold();
            });
        });
    }

    private static void Section(ColumnDescriptor col, string title, List<PlaneadorPdfRow> rows, Mode mode)
    {
        SectionTitle(col, title);

        col.Item().Table(table =>
        {
            switch (mode)
            {
                case Mode.DES:
                    BuildTableDes(table, rows);
                    break;
                case Mode.INY:
                    BuildTableIny(table, rows);
                    break;
                default:
                    BuildTableEmp(table, rows);
                    break;
            }
        });
    }

    // -------------------------
    // TABLAS
    // -------------------------

    private static void BuildTableDes(TableDescriptor table, List<PlaneadorPdfRow> rows)
    {
        table.ColumnsDefinition(columns =>
        {
            columns.ConstantColumn(52);        // SKU
            columns.RelativeColumn(3.6f);      // PRODUCTO (más ancho)
            columns.ConstantColumn(30);        // C1
            columns.ConstantColumn(30);        // C2
            columns.ConstantColumn(30);        // C3
            columns.ConstantColumn(45);        // REND
            columns.ConstantColumn(62);        // KG LOTE
            columns.ConstantColumn(48);        // CANALES
            columns.ConstantColumn(62);        // ALMACÉN
            columns.RelativeColumn(1.4f);      // MANEJO
            columns.ConstantColumn(80);        // ETIQUETADO
            columns.RelativeColumn(1.8f);      // OBS
        });

        table.Header(h =>
        {
            H(h.Cell(), "SKU");
            H(h.Cell(), "PRODUCTO", left: true);
            H(h.Cell(), "C1");
            H(h.Cell(), "C2");
            H(h.Cell(), "C3");
            H(h.Cell(), "%");
            H(h.Cell(), "KG LOTE");
            H(h.Cell(), "CANALES");
            H(h.Cell(), "ALMACÉN");
            H(h.Cell(), "MANEJO");
            H(h.Cell(), "ETIQUETADO");
            H(h.Cell(), "OBS", left: true);
        });

        foreach (var r in rows)
        {
            B(table.Cell(), r.DesSku, lines: 1);
            B(table.Cell(), r.DesProducto, left: true, lines: 2);

            B(table.Cell(), r.Col1, right: true, lines: 1);
            B(table.Cell(), r.Col2, right: true, lines: 1);
            B(table.Cell(), r.Col3, right: true, lines: 1);

            B(table.Cell(), r.RendPct, right: true, lines: 1);
            B(table.Cell(), r.KgLote, right: true, lines: 1);
            B(table.Cell(), r.Canales, right: true, lines: 1);

            B(table.Cell(), r.Almacen, lines: 1);
            B(table.Cell(), r.Manejo, left: true, lines: 2);

            B(table.Cell(), r.Etiquetado, lines: 1);
            B(table.Cell(), r.Observaciones, left: true, lines: 2);
        }
    }

    private static void BuildTableIny(TableDescriptor table, List<PlaneadorPdfRow> rows)
    {
        table.ColumnsDefinition(columns =>
        {
            columns.ConstantColumn(52);        // SKU
            columns.RelativeColumn(3.8f);      // PRODUCTO
            columns.ConstantColumn(48);        // % INY
            columns.ConstantColumn(78);        // MODO
            columns.ConstantColumn(72);        // KG SUBTOTAL
            columns.ConstantColumn(50);        // PIEZAS
            columns.ConstantColumn(62);        // ALMACÉN
            columns.RelativeColumn(1.4f);      // MANEJO
            columns.ConstantColumn(80);        // ETIQUETADO
            columns.RelativeColumn(1.8f);      // OBS
        });

        table.Header(h =>
        {
            H(h.Cell(), "SKU");
            H(h.Cell(), "PRODUCTO", left: true);
            H(h.Cell(), "% INY");
            H(h.Cell(), "MODO");
            H(h.Cell(), "KG SUBTOTAL");
            H(h.Cell(), "PIEZAS");
            H(h.Cell(), "ALMACÉN");
            H(h.Cell(), "MANEJO");
            H(h.Cell(), "ETIQUETADO");
            H(h.Cell(), "OBS", left: true);
        });

        foreach (var r in rows)
        {
            B(table.Cell(), r.InySku, lines: 1);
            B(table.Cell(), r.InyProducto, left: true, lines: 2);

            B(table.Cell(), r.InyPct, right: true, lines: 1);
            B(table.Cell(), r.InyModo, lines: 1);

            B(table.Cell(), r.Subtotal, right: true, lines: 1);
            B(table.Cell(), r.Piezas, right: true, lines: 1);

            B(table.Cell(), r.Almacen, lines: 1);
            B(table.Cell(), r.Manejo, left: true, lines: 2);

            B(table.Cell(), r.Etiquetado, lines: 1);
            B(table.Cell(), r.Observaciones, left: true, lines: 2);
        }
    }

    private static void BuildTableEmp(TableDescriptor table, List<PlaneadorPdfRow> rows)
    {
        table.ColumnsDefinition(columns =>
        {
            columns.ConstantColumn(52);        // SKU
            columns.RelativeColumn(4.2f);      // PRODUCTO
            columns.ConstantColumn(78);        // KG SUBTOTAL
            columns.ConstantColumn(52);        // PIEZAS
            columns.ConstantColumn(62);        // ALMACÉN
            columns.RelativeColumn(1.6f);      // MANEJO
            columns.ConstantColumn(90);        // ETIQUETADO
            columns.RelativeColumn(2.0f);      // OBS
        });

        table.Header(h =>
        {
            H(h.Cell(), "SKU");
            H(h.Cell(), "PRODUCTO", left: true);
            H(h.Cell(), "KG SUBTOTAL");
            H(h.Cell(), "PIEZAS");
            H(h.Cell(), "ALMACÉN");
            H(h.Cell(), "MANEJO");
            H(h.Cell(), "ETIQUETADO");
            H(h.Cell(), "OBS", left: true);
        });

        foreach (var r in rows)
        {
            B(table.Cell(), r.InySku, lines: 1);
            B(table.Cell(), r.InyProducto, left: true, lines: 2);

            B(table.Cell(), r.Subtotal, right: true, lines: 1);
            B(table.Cell(), r.Piezas, right: true, lines: 1);

            B(table.Cell(), r.Almacen, lines: 1);
            B(table.Cell(), r.Manejo, left: true, lines: 2);

            B(table.Cell(), r.Etiquetado, lines: 1);
            B(table.Cell(), r.Observaciones, left: true, lines: 2);
        }
    }

    // -------------------------
    // HELPERS (estilo SAP)
    // -------------------------

    private static void SectionTitle(ColumnDescriptor col, string title)
    {
        col.Item().Column(x =>
        {
            x.Item()
             .PaddingBottom(4)
             .Text(title)
             .FontColor(SapBlue)
             .FontSize(11)
             .SemiBold();

            x.Item().Height(1).Background(SapBlue);
        });

        col.Item().PaddingBottom(6);
    }

    private static IContainer HCell(IContainer c) =>
        c.Background(HeadBg).Border(1).BorderColor(Grid).PaddingVertical(4).PaddingHorizontal(5).AlignMiddle();

    private static IContainer BCell(IContainer c) =>
        c.Border(1).BorderColor(Grid).PaddingVertical(3).PaddingHorizontal(5).AlignMiddle();

    private static void H(IContainer c, string text, bool left = false, bool right = false)
        => CellText(HCell(c), text, left: left, right: right, lines: 1, bold: true);

    private static void B(IContainer c, string? text, bool left = false, bool right = false, int lines = 1)
        => CellText(BCell(c), text ?? "", left: left, right: right, lines: lines, bold: false);

    private static void CellText(IContainer c, string value, bool left, bool right, int lines, bool bold)
    {
        if (left) c = c.AlignLeft();
        else if (right) c = c.AlignRight();
        else c = c.AlignCenter();

        var t = c.Text((value ?? "").Trim())
                 .FontSize(8)
                 .FontColor(TextDark)
                 .LineHeight(1);

        if (bold) t.SemiBold();

        // clave para que NO se “haga torre” el texto
        t.ClampLines(lines);
        
    }
}
