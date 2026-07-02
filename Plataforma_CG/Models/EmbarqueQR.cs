namespace Plataforma_CG.Models
{
    public class EmbarqueQR
    {
        public int Id { get; set; }

        public int EmbarqueId { get; set; }
        public Embarque Embarque { get; set; }

        public string Token { get; set; } = "";
        public string UrlQR { get; set; } = "";

        public DateTime FechaGeneracion { get; set; }
        public DateTime? FechaValidacion { get; set; }

        public int Estado { get; set; }

        public string? UsuarioGenera { get; set; }
        public string? UsuarioValida { get; set; }
    }

}
