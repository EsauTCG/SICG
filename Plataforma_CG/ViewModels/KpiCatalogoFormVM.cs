using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models.ViewModels
{
    public class KpiCatalogoFormVM
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Título es obligatorio")]
        public string Titulo { get; set; } = "";

        [Required(ErrorMessage = "Categoría es obligatoria")]
        public string Categoria { get; set; } = "";

        [Required(ErrorMessage = "Descripción es obligatoria")]
        public string Descripcion { get; set; } = "";

        [Required(ErrorMessage = "EmbedUrl es obligatorio")]
        public string EmbedUrl { get; set; } = "";

        public bool Activo { get; set; } = true;

        // Para mostrar preview si ya existe
        public string? ImagenUrlActual { get; set; }

        // Upload
        public IFormFile? Imagen { get; set; }

        // Si quieres eliminar la imagen actual desde edición
        public bool QuitarImagen { get; set; } = false;
    }
}
