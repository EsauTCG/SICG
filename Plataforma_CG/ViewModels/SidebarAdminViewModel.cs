using System.Collections.Generic;
using Plataforma_CG.Models.Sidebar;

namespace Plataforma_CG.Models.ViewModels
{
    public class SidebarAdminViewModel
    {
        public List<SidebarCategoria> Categorias { get; set; }
        public List<SidebarPermiso> Permisos { get; set; }
        public List<Perfil> Perfiles { get; set; } = new();

    }
}
