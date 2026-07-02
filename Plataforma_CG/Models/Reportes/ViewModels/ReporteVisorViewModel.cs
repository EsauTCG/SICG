using Plataforma_CG.Models.Reportes.Configuracion;
using Plataforma_CG.Models.Reportes.Core;
using Plataforma_CG.Models.Reportes.Filtros;

namespace Plataforma_CG.Models.Reportes.ViewModels
{
    public class ReporteVisorViewModel
    {
        public IReportFilter? Filtros { get; set; } 

        public IEnumerable<object> Resultados { get; set; }
            = Enumerable.Empty<object>();

        public ReporteConfiguracionViewModel Configuracion { get; set; } = new();

        public PaginacionViewModel Paginacion { get; set; }

        public List<string> ColumnasVisibles { get; set; }
            = new();

        public List<ColumnaCalculadaViewModel> ColumnasCalculadas { get; set; } = new();
    }
}
