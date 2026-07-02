using System.Collections.Generic;

namespace Plataforma_CG.Models.ViewModels
{
    public class SideAdminViewModel
    {
        public ModuloViewModel? ModuloActual { get; set; }
        public List<ModuloViewModel> ModulosExistentes { get; set; } = new();
        public List<CategoriaViewModel> CategoriasExistentes { get; set; } = new();
    }
}
