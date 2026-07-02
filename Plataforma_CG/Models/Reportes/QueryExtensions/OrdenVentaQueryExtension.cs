using Plataforma_CG.Models.Reportes.Filtros;

namespace Plataforma_CG.Models.Reportes.QueryExtensions
{
    public static class OrdenVentaQueryExtension
    {

        public static IQueryable<OrdenVentaCabecera> AplicarFiltros(
            this IQueryable<OrdenVentaCabecera> query,
            OrdenVentaFiltroViewModel filtros)
        {
            if (filtros.FechaInicio.HasValue)
            {
                query = query.Where(x =>
                    x.FechaRegistro >= filtros.FechaInicio);
            }

            if (filtros.FechaFin.HasValue)
            {
                query = query.Where(x =>
                    x.FechaRegistro <= filtros.FechaFin);
            }

            if (filtros.Estado.HasValue)
            {
                query = query.Where(x =>
                    x.Estado == filtros.Estado);
            }

            if (!string.IsNullOrWhiteSpace(filtros.Cliente))
            {
                query = query.Where(x =>
                    x.NombreCliente != null &&
                    x.NombreCliente.Contains(filtros.Cliente));
            }

            return query;
        }

    }
}
