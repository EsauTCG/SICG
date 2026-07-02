using Plataforma_CG.Data;
using Plataforma_CG.Models.Reportes.ViewModels;

namespace Plataforma_CG.Models.Reportes.Core
{
    public interface IReportDefinition
    {
        //Metadatos
        string ReportKey { get; }

        string ReportName {  get; }

        Type FilterType { get; }

        //IReportFilter CreateFilter();

        //Definir Columnas
        List<ColumnaReporteViewModel> GetColumns();

        //Definir filtros
        List<FiltroReporteViewModel> BuildFilters(IReportFilter filtros);

        //Construir Consultas
        IQueryable BuildQuery(
            AppDbContext context,
            IReportFilter filtros);

        //Ejecutar reportes
        Task<ReportExecutionResult> ExecuteAsync(
            AppDbContext context,
            IReportFilter filtros,
            int page,
            int pageSize);
    }
}
