using Plataforma_CG.Models.Reportes.Core;

namespace Plataforma_CG.Models.Reportes.Filtros
{
    public class DetallesClientesFiltroViewModel : IReportFilter
    {
        public string? Cliente { get; set; }

        public string? MT_Clasificacion { get; set; }

        public string? Vendedor { get; set; }

        public DateTime? FechaInicio { get; set; }

        public DateTime? FechaFin { get; set; }

        public string? CodigoPostal { get; set; }
    }
}
