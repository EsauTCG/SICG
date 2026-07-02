using Plataforma_CG.Data;
using Plataforma_CG.Models.Reportes.ViewModels;

namespace Plataforma_CG.Models.Reportes.Core
{
    public abstract class ReportDefinitionBase<TFilter>
        : IReportDefinition
        where TFilter : class, IReportFilter
    {
        public abstract string ReportKey { get; }

        public abstract string ReportName { get; }

        public Type FilterType => typeof(TFilter);

        public abstract List<ColumnaReporteViewModel> GetColumns();

        public abstract List<FiltroReporteViewModel> BuildFilters(
            TFilter filtros);

        public abstract IQueryable BuildQuery(
            AppDbContext context,
            TFilter filtros);

        public abstract Task<ReportExecutionResult> ExecuteAsync(
            AppDbContext context,
            TFilter filtros,
            int page,
            int pageSize);

        List<FiltroReporteViewModel> IReportDefinition.BuildFilters(
            IReportFilter filtros)
        {
            return BuildFilters((TFilter)filtros);
        }

        IQueryable IReportDefinition.BuildQuery(
            AppDbContext context,
            IReportFilter filtros)
        {
            return BuildQuery(context, (TFilter)filtros);
        }

        Task<ReportExecutionResult> IReportDefinition.ExecuteAsync(
            AppDbContext context, 
            IReportFilter filtros, 
            int page, 
            int pageSize)
        {
            return ExecuteAsync(
                context,
                (TFilter)filtros,
                page,
                pageSize);
        }
    }
}
