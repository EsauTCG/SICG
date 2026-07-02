namespace Plataforma_CG.Models
{
    public class AsignarKpisVM
    {
        public string UsuarioKey { get; set; } = "";
        public string DisplayName { get; set; } = "";

        public List<KpiCatalogo> Asignados { get; set; } = new();
        public List<KpiCatalogo> Disponibles { get; set; } = new();
    }
}
