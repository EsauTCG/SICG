using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models.Sidebar
{
    public class SidebarCategoria
    {
        public int Id { get; set; }

        [Required]
        public string Nombre { get; set; }

        public string Icono { get; set; }

        public List<SidebarModulo> Modulos { get; set; } = new List<SidebarModulo>();
    }
}
