using Microsoft.AspNetCore.Mvc.Rendering;
using Plataforma_CG.Models.Reportes.Enums;
using Plataforma_CG.Models.Reportes.Filtros;

namespace Plataforma_CG.Models.Reportes.ViewModels

{
    public class FiltroReporteViewModel
    {

        public string Key { get; set; }
        
        public string Label { get; set; }

        public TipoFiltroReporte Tipo { get; set; }

        public string? Valor { get; set; }

        public bool Visible { get; set; } = true;

        public List<SelectListItem> Opciones { get; set; }
            = new();

        
        
    }
}
