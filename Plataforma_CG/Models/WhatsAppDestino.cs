namespace Plataforma_CG.Models
{
    public class WhatsAppDestino
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string TipoDestino { get; set; } = string.Empty;
        public string? Canal { get; set; }
        public int? VendedorId { get; set; }
        public bool Activo { get; set; }
        public TimeSpan? HoraEnvio { get; set; }
        public string? DiaEnvio { get; set; }
        public DateTime FechaAlta { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }
}
