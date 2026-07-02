using Plataforma_CG.Models.Reportes.Filtros;

namespace Plataforma_CG.Models.Reportes.QueryExtensions
{
    public static class TransferenciasDetallesQueryExtension
    {
        public static IQueryable<TransferenciasDetallesCabecera> AplicarFiltros(
           this IQueryable<TransferenciasDetallesCabecera> query,
           TransferenciasDetallesFiltroViewModel filtros)
        {
            if (!string.IsNullOrWhiteSpace(filtros.Consecutivo))
            {
                query = query.Where(x =>
                    x.Consecutivo!.Contains(filtros.Consecutivo));
            }

            if (!string.IsNullOrWhiteSpace(filtros.Sucursal))
            {
                query = query.Where(x =>
                    x.Sucursal!.Contains(filtros.Sucursal));
            }

            if (filtros.FechaInicioSolicitud.HasValue)
            {
                query = query.Where(x =>
                    x.FechaSolicitud >= filtros.FechaInicioSolicitud);
            }

            if (filtros.FechaFinSolicitud.HasValue)
            {
                query = query.Where(x =>
                    x.FechaSolicitud <= filtros.FechaFinSolicitud);
            }

            if (filtros.Anio.HasValue)
            {
                query = query.Where(x =>
                    x.anio == filtros.Anio);
            }

            return query;
        }
    }
}
