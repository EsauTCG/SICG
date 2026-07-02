using Plataforma_CG.Models.Reportes.Core;

namespace Plataforma_CG.Models.Reportes.Filtros
{
    public class TransferenciasDetallesFiltroViewModel : IReportFilter
    {
        public string? Consecutivo { get; set; }

        public string? Sucursal { get; set; }

        public DateTime? FechaInicioSolicitud { get; set; }

        public DateTime? FechaFinSolicitud { get; set; }

        public int? Anio { get; set; }

        
    }
}
