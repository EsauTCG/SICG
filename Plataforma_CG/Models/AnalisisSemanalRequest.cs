namespace Plataforma_CG.Models
{
    public class AnalisisSemanalRequest
    {
        public DateTime? FechaCorte { get; set; }
        public List<string> Skus { get; set; } = new();
        public string? Master { get; set; }
    }
}
