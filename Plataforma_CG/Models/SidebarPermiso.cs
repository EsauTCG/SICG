namespace Plataforma_CG.Models.Sidebar
{
    public class SidebarPermiso
    {
        public int Id { get; set; }
        public int PerfilId { get; set; }
        public int ModuloId { get; set; }

        public SidebarModulo Modulo { get; set; }
    }
}
