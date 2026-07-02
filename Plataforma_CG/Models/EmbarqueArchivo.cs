namespace Plataforma_CG.Models
{
    public class EmbarqueArchivo
    {
        public int Id { get; set; }
        public int EmbarqueId { get; set; }

        public string Tipo { get; set; } = null!;
        public string RutaArchivo { get; set; } = null!;

        public DateTime FechaRegistro { get; set; }
        public string? UsuarioRegistro { get; set; }

        public Embarque Embarque { get; set; } = null!;
    }
}