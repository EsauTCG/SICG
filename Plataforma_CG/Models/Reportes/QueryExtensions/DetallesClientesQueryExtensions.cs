using Plataforma_CG.Models.Reportes.Filtros;

namespace Plataforma_CG.Models.Reportes.QueryExtensions
{
    public static class DetallesClientesQueryExtensions
    {
        public static IQueryable<DetallesClientesCabecera> AplicarFiltros(
            this IQueryable<DetallesClientesCabecera> query,
            DetallesClientesFiltroViewModel filtros)
        {
            if (!string.IsNullOrWhiteSpace(filtros.Cliente))
            {
                query = query.Where(x =>
                    x.NombreCliente!.Contains(filtros.Cliente));
            }

            if (!string.IsNullOrWhiteSpace(filtros.MT_Clasificacion))
            {
                query = query.Where(x =>
                    x.MTClasificacion == filtros.MT_Clasificacion);
            }

            if (!string.IsNullOrWhiteSpace(filtros.Vendedor))
            {
                query = query.Where(x =>
                    x.NombreVendedor!.Contains(filtros.Vendedor));
            }

            if (!string.IsNullOrWhiteSpace(filtros.CodigoPostal))
            {
                query = query.Where(x =>
                    x.CodigoPostal == filtros.CodigoPostal);
            }

            if (filtros.FechaInicio.HasValue)
            {
                query = query.Where(x =>
                    x.FechaDeAlta >= filtros.FechaInicio);
            }

            if (filtros.FechaFin.HasValue)
            {
                query = query.Where(x =>
                    x.FechaDeAlta <= filtros.FechaFin);
            }

            return query;

        }
    }
}
