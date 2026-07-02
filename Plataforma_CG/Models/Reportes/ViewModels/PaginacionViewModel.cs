namespace Plataforma_CG.Models.Reportes.ViewModels
{
    public class PaginacionViewModel
    {
        public int PaginaActual { get; set; }

        public int TamanoPagina { get; set; }

        public int TotalPaginas { get; set; }

        public int TotalRegistros { get; set; }

        public bool TienePaginaAnterior => PaginaActual > 1;

        public bool TienePaginaSiguiente => PaginaActual < TotalPaginas;
    }
}
