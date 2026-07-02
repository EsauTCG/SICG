namespace Plataforma_CG.Models
{
    public class WhatsAppAPI
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;
        public int OrdenRotacion { get; set; } = 1;
        public DateTime FechaAlta { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }
}
