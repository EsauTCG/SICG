namespace Plataforma_CG.Models.Reportes.Core
{
    public class ReportColumn<T>
    {
        public string Key { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public bool Visible { get; set;  }

        public Func<T, object?>? ValueSelector { get; set; }
    }
}
