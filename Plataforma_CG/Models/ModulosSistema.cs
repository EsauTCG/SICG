namespace Plataforma_CG.Models
{
    public class ModulosSistema
    {
        public int Id { get; set; }
        public string Clave { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
    }
}
