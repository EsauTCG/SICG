using Plataforma_CG.Models.Reportes.ViewModels;

namespace Plataforma_CG.Models.Reportes.Exporting.Interfaces
{
    public interface IPdfExporter
    {
        byte[] Export(
            IEnumerable<object> rows,
            List<ColumnaReporteViewModel> columns,
            string reportName);
    }
}