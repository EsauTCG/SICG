namespace Plataforma_CG.Models
{
    public sealed class MapaCargaItem
    {
        public int id { get; set; }
        public string nombre { get; set; } = "";
        public string sku { get; set; } = "";
        public string color { get; set; } = "#3b82f6";
        public int cajas { get; set; }
        public decimal kgPorCaja { get; set; }

        // Fallbacks si aún no traes dimensiones reales desde BD
        public decimal boxL { get; set; } = 0.40m;
        public decimal boxA { get; set; } = 0.30m;
        public decimal boxH { get; set; } = 0.25m;
    }
}
