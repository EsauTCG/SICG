namespace Plataforma_CG.ViewModels
{
    public class FacturaViewModel
    {
        public string SKU { get; set; }
        public decimal Kilos { get; set; }
        public DateTime DocDate { get; set; }
        public string CANCELED { get; set; } // 'C', 'Y', o ''
    }
}
