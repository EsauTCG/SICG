namespace Plataforma_CG.Models
{
    public class AutoSapSettings
    {
        public bool Enabled { get; set; }
        public string Source { get; set; } = "P1"; // P1 o TIF
        public int IntervalMs { get; set; } = 5000;
    }
}
