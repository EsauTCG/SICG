using System.Collections.Generic;

namespace Plataforma_CG.Models.ViewModels
{
    public class KpisPermisosViewModel
    {
        public List<Perfil> Perfiles { get; set; } = new();
        public List<KpiCatalogo> Kpis { get; set; } = new();
        public List<PerfilKpiPermiso> PermisosKpi { get; set; } = new();
    }
}