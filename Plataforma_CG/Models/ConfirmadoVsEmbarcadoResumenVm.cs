namespace Plataforma_CG.Models
{
    public class ConfirmadoVsEmbarcadoResumenVm
    {
        public string Folio { get; set; }
        public string Cliente { get; set; }
        public decimal KgPedidos { get; set; }
        public decimal CajasPedidas { get; set; }
        public decimal KgSurtidos { get; set; }
        public decimal CajasSurtidas { get; set; }
        public decimal GAPKg { get; set; }
        public decimal GAPCajas { get; set; }
        public DateTime? Fecha { get; set; }
        public string Tipo { get; set; }
    }
}
