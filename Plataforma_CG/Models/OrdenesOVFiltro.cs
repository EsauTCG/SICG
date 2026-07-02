namespace Plataforma_CG.Models
{
    public class OrdenesOVFiltro
    {

        // Filtros (coinciden con names del form)
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public string? Vendedor { get; set; }
        public int? Estatus { get; set; }
        public string? Buscar { get; set; }
        public string? Export { get; set; } // "xlsx" | "csv" | null

        // NUEVOS para multi
        public List<string> VendedoresSeleccionados { get; set; } = new();
        public List<int> EstatusSeleccionados { get; set; } = new();

        // Datos auxiliares para la vista
        public List<string>? Vendedores { get; set; }

        public string Serie { get; set; } = "";

        // Resultados
        public List<OrdenVentaRow>? Resultados { get; set; }
    }
}
