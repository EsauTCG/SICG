// ViewModels/ReimpresionEtiquetaVM.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.ViewModels
{
    public class ReimpresionEtiquetaVM
    {
        public string Source { get; set; } = "P1";

        [Required]
        public string EmpresaEysId { get; set; } = "CARNG";

        // TipoImpresion lo dejé fijo en 3 porque así lo estás usando
        public int TipoImpresion { get; set; } = 3;

        [Required]
        public string PrinterName { get; set; } = "ZD JAIME_P1";

        [Required]
        public string ClaveReporte { get; set; } = "3"; // tipo de etiquetación

        public List<ReimpresionItemVM> Items { get; set; } = new();
    }

    public class ReimpresionItemVM
    {
        public string CodigoEtiqueta { get; set; } = "";
        public int Cantidad { get; set; } = 1;

        public string? ClaveReporte { get; set; }
    }

    // Para POST desde JS
    public class ReimpresionRequestVM
    {
        public string Source { get; set; } = "P1";
        public string EmpresaEysId { get; set; } = "CARNG";
        public int TipoImpresion { get; set; } = 3;
        public string PrinterName { get; set; } = "";
        public string ClaveReporte { get; set; } = "";
        public List<ReimpresionItemVM> Items { get; set; } = new();
    }

    public class ReimpresionRowResultVM
    {
        public string CodigoEtiqueta { get; set; } = "";
        public int Cantidad { get; set; }
        public bool Ok { get; set; }
        public string Msg { get; set; } = "";

        
    }
}
