namespace Plataforma_CG.Models.Reportes.Core
{
    public interface IReportRegistry
    {
        IReportDefinition Get(string reportKey);
    }
}
