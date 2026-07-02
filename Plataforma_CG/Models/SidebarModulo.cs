using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models.Sidebar
{
    public class SidebarModulo
    {
        public int Id { get; set; }

        [Required]
        public string? Nombre { get; set; }

        public string? Icono { get; set; }

        public string? Url { get; set; }

        public int? PadreId { get; set; }

        [ForeignKey("PadreId")]
        public SidebarModulo? Padre { get; set; }

        public List<SidebarModulo> Submodulos { get; set; } = new List<SidebarModulo>();

        public int Orden { get; set; } = 0;

        public bool Activo { get; set; } = true;

        public int? CategoriaId { get; set; }

        [ForeignKey("CategoriaId")]
        public SidebarCategoria? Categoria { get; set; }
    }
}
