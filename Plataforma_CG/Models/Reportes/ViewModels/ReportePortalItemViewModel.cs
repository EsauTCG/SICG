namespace Plataforma_CG.Models.Reportes.ViewModels
{
    public class ReportePortalItemViewModel
    {
        public string Titulo { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;

        public string Icono { get; set; } = "fas fa-table";

        public string ReportKey { get; set; } = string.Empty;

        public string Schema { get; set; } = string.Empty;

        public string ViewName { get; set; } = string.Empty;
    }
}