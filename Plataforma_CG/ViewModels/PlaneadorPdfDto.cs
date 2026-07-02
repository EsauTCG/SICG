namespace Plataforma_CG.ViewModels
{
    public class PlaneadorPdfDto
    {
        public class PlaneadorPdfRequest
        {
            public string PlanTexto { get; set; } = "";
            public string Mode { get; set; } = "ALL"; // DES | INY | EMP | ALL
            public List<PlaneadorPdfRow> Rows { get; set; } = new();
        }

        public class PlaneadorPdfRow
        {
            // DESHUESE
            public string DesSku { get; set; } = "";
            public string DesProducto { get; set; } = "";
            public string Col1 { get; set; } = "";
            public string Col2 { get; set; } = "";
            public string Col3 { get; set; } = "";
            public string RendPct { get; set; } = "";
            public string KgLote { get; set; } = "";
            public string Canales { get; set; } = "";

            // INYECCION
            public string InySku { get; set; } = "";
            public string InyProducto { get; set; } = "";
            public string InyPct { get; set; } = "";
            public string InyModo { get; set; } = "";
            public string Subtotal { get; set; } = "";
            public string Piezas { get; set; } = "";

            // EMPAQUE
            public string Almacen { get; set; } = "";
            public string Manejo { get; set; } = "";
            public string Etiquetado { get; set; } = "";
            public string Observaciones { get; set; } = "";
        }

    }
}
