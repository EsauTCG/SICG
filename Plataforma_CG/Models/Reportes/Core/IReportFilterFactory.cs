using Microsoft.AspNetCore.Http;

namespace Plataforma_CG.Models.Reportes.Core
{
    public interface IReportFilterFactory
    {
        IReportFilter Create(
            IReportDefinition report,
            IQueryCollection query);
    }
}
