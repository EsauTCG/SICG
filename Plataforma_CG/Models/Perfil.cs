using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models
{
    public class Perfil
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string? Nombre { get; set; }

        public string? Descripcion { get; set; }

        public bool Activo { get; set; } = true;
    }
}
