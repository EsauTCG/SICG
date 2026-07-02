namespace Plataforma_CG.Models.ViewModels
{
    public class SidebarViewModel
    {
        public List<CategoriaViewModel> Categorias { get; set; } = new();
    }

    public class CategoriaViewModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Icono { get; set; } = "";
        public List<ModuloViewModel> Modulos { get; set; } = new();
    }

        public class ModuloViewModel
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = "";
            public string Icono { get; set; } = "";
            public string Url { get; set; } = "";
            public int? PadreId { get; set; }
            public int? CategoriaId { get; set; }
            public int Orden { get; set; } = 0;
            public List<ModuloViewModel> SubModulos { get; set; } = new();
        }

}
