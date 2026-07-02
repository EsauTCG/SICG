namespace Plataforma_CG.Models
{
    public class SkuConversion
    {
        public int Id { get; set; }
        public string SkuOrigen { get; set; } = "";
        public string SkuDestino { get; set; } = "";
        public decimal? Factor { get; set; }
        public int? Prioridad { get; set; }
        public string? Motivo { get; set; }
        public bool Activo { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
