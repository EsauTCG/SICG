namespace Plataforma_CG.Models
{
    public class ConfirmadovsEmbarcadoFiltroVm
    {

        public string Cliente { get; set; }
        public string Folio { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string Tipo { get; set; } // "", "OV", "TR"
    }
}
