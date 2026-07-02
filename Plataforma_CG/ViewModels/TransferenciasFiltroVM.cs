namespace Plataforma_CG.ViewModels
{
    public class TransferenciasFiltroVM
    {

        // Filtros
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public string? Sucursal { get; set; }
        public int? Estatus { get; set; }
        public string? Buscar { get; set; }

        // nuevo para el multi
        public List<string> SucursalesSeleccionadas { get; set; } = new();
        public List<int> EstatusSeleccionados { get; set; } = new();

        // Catálogos para filtros
        public IEnumerable<SucursalVM>? Sucursales { get; set; }

        // Resultados
        public IEnumerable<TransferenciaListadoVM>? Resultados { get; set; }
    }
}
