using DocumentFormat.OpenXml.Vml;
using DocumentFormat.OpenXml.Wordprocessing;
using Plataforma_CG.Models.Reportes.Datasets;

namespace Plataforma_CG.Models.Reportes.Core
{
    public class ReportRegistry : IReportRegistry
    {
        private readonly IEnumerable<IReportDefinition> _reports;

        public ReportRegistry(
            IEnumerable<IReportDefinition> reports)
        {
            _reports = reports;
        }

        public IReportDefinition Get(string reportKey)
        {
            var report = _reports.FirstOrDefault(x =>
                x.ReportKey.Equals(
                    reportKey,
                    StringComparison.OrdinalIgnoreCase));

            return report
                ?? throw new ArgumentException(
                    $"Reporte '{reportKey}' no encontrado");
        }
 

    }
}
