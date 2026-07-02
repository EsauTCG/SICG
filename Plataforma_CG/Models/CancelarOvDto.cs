namespace Plataforma_CG.Models
{
    public class CancelarOvDto
    {
        public int OrdenId { get; set; }
        public string? Motivo { get; set; } // opcional, por si luego quieres guardarlo

        public string? Password { get; set; }   // <- contraseña que viene del front
    }
}
