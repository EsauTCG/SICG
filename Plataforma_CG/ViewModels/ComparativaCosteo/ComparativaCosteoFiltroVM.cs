using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Plataforma_CG.ViewModels.ComparativaCosteo
{
    public class ComparativaCosteoFiltroVM
    {
        public string TipoPeriodo { get; set; } = "MES";

        public DateTime FechaBase { get; set; } = DateTime.Today;

        public decimal Tolerancia { get; set; } = 1;

        public string EstadoCosto { get; set; }

        public string CodigoEtiqueta { get; set; }

        public string Lote { get; set; }

        public string SKU { get; set; }

        public string Producto { get; set; } 

        //Rango de fecha

        public DateTime FechaInicio { get; set; }

        public DateTime FechaFin { get; set; }

    }
}
