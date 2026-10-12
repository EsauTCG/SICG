namespace Plataforma_CG.ViewModels
{
    public class FacturaPendienteSapViewModel
    {
        public int DocEntry { get; set; }
        public int DocNum { get; set; }
        public DateTime? FechaFactura { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public string Moneda { get; set; } = "";
        public decimal Importe { get; set; }
        public decimal Pagado { get; set; }
        public decimal Pendiente { get; set; }
        public int DiasVencidos { get; set; }
    }
}
