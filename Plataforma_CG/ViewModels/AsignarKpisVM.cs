using Plataforma_CG.Models;

namespace Plataforma_CG.ViewModels
{
    public class AsignarKpisPerfilVM
    {
        public int PerfilId { get; set; }
        public string DisplayName { get; set; } = "";

        public List<KpiCatalogo> Asignados { get; set; } = new();
        public List<KpiCatalogo> Disponibles { get; set; } = new();
    }
}
