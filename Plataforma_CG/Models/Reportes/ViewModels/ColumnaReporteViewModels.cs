namespace Plataforma_CG.Models.Reportes.ViewModels
{
    public class ColumnaReporteViewModel
    {
        public string Key { get; set; } = string.Empty;

        public string Titulo { get; set; } = string.Empty;

        public bool Visible { get; set; } = true;

        public string CssClass { get; set; } = string.Empty;

        public string Width { get; set; } = string.Empty;

        public bool PermiteOrdenamiento { get; set; } = false;

        public Func<object, object?>? ValueSelector { get; set; }
    }
}