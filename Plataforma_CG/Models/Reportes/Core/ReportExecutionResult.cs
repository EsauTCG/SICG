namespace Plataforma_CG.Models.Reportes.Core
{
    public class ReportExecutionResult
    {
        public IEnumerable<object> Rows { get; set; }
            = Enumerable.Empty<object>();

        public int TotalRecords { get; set; }
    }
}
