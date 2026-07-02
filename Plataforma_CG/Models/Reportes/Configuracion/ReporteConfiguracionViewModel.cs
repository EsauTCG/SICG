using Plataforma_CG.Models.Reportes.ViewModels;

namespace Plataforma_CG.Models.Reportes.Configuracion
{
    public class ReporteConfiguracionViewModel
    {
        public string Titulo { get; set; } = string.Empty;

        public bool PermiteExportarExcel { get; set; }

        public bool PermiteExportarPdf { get; set; }

        public bool PermiteGuardarPlantilla { get; set; }

        public string Datasetkey { get; set; }

        public List<ColumnaReporteViewModel> Columnas { get; set; } = new();
               

        public List<FiltroReporteViewModel> Filtros { get; set; } = new();
    }
}
