using Plataforma_CG.Models.Reportes.Core;

namespace Plataforma_CG.Models.Reportes.Filtros
{
    public class OrdenVentaFiltroViewModel : IReportFilter
    {
        public DateTime? FechaInicio { get; set; }

        public DateTime? FechaFin { get; set; }

        public int? Estado { get; set; }

        public String? Cliente { get; set; }
    }
}
