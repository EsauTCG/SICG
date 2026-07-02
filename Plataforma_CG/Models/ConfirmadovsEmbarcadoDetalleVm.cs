namespace Plataforma_CG.Models
{
    public class ConfirmadovsEmbarcadoDetalleVm
    {
        public string Folio { get; set; }
        public string SKU { get; set; }
        public string Producto { get; set; }
        public decimal KgPedidos { get; set; }
        public decimal CajasPedidas { get; set; }
        public decimal KgSurtidos { get; set; }
        public decimal CajasSurtidas { get; set; }
        public decimal GAPKg { get; set; }
        public decimal GAPCaja { get; set; }
        public string Tipo { get; set; }
    }
}
