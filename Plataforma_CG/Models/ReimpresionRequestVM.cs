namespace Plataforma_CG.Models
{
    public class ReimpresionRequestVM
    {
        public string Source { get; set; } = "P1"; // "P1" o "TIF"
        public string PrinterName { get; set; } = "";
        public string ClaveReporte { get; set; } = "";
        public string EmpresaEysId { get; set; } = "";
        public int TipoImpresion { get; set; } = 3;
        public List<ReimpresionItemVM> Items { get; set; } = new();
    }

    public class ReimpresionItemVM
    {
        public string CodigoEtiqueta { get; set; } = "";
        public int Cantidad { get; set; }

        public string? ClaveReporte { get; set; }


    }

    public class ReimpresionRowResultVM
    {
        public string CodigoEtiqueta { get; set; } = "";
        public int Cantidad { get; set; }
        public bool Ok { get; set; }
        public string Msg { get; set; } = "";
    }

}
