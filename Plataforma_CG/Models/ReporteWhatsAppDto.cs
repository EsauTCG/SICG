namespace Plataforma_CG.Models
{
    public class ReporteWhatsAppDto
    {
        public string DestinoNombre { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string TipoDestino { get; set; } = string.Empty;
        public string ClaveDestino { get; set; } = string.Empty;
        public decimal Objetivo { get; set; }
        public decimal Vendido { get; set; }
        public decimal Avance { get; set; }
        public List<string> SkuSinVenta { get; set; } = new();
    }
}
