

namespace Plataforma_CG.Models
{
    using System.ComponentModel.DataAnnotations;

    public class Vista
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre de la vista es obligatorio")]
        public string? Nombre { get; set; }

        [Required(ErrorMessage = "El controlador es obligatorio")]
        public string? Controlador { get; set; }

        [Required(ErrorMessage = "La acción es obligatoria")]
        public string? Accion { get; set; }

        public ICollection<Permiso>? Permisos { get; set; }
    }

}