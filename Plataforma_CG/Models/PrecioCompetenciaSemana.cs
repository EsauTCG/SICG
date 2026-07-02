namespace Plataforma_CG.Models
{
    public class PrecioCompetenciaSemana
    {
        public int Id { get; set; }
        public DateTime FechaRegistro { get; set; }
        public DateTime? FechaCorte { get; set; }
        public string Sku { get; set; } = string.Empty;
        public decimal? Denes { get; set; }
        public decimal? Tc { get; set; }
        public decimal? Freasa { get; set; }
        public string? Comentarios { get; set; }
        public string? UsuarioRegistro { get; set; }
    }
}
