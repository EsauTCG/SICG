using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models.Reportes.Core;
using Plataforma_CG.Models.Reportes.Enums;
using Plataforma_CG.Models.Reportes.Filtros;
using Plataforma_CG.Models.Reportes.ViewModels;
using Plataforma_CG.Models.Reportes.QueryExtensions;

namespace Plataforma_CG.Models.Reportes.Datasets
{
    public class TransferenciasDetallesDataset
        : ReportDefinitionBase<TransferenciasDetallesFiltroViewModel>
    {

        public override string ReportKey => "transferencia-detalles";

        public override string ReportName => "Detalles de Transferencias";

        public static List<ColumnaReporteViewModel> Columnas =>
            new()
            {
                new()
                {
                    Key = "Id",
                    Titulo = "Id",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).Id
                },
                new()
                {
                    Key = "consecutivo",
                    Titulo = "Consecutivo",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).Consecutivo
                },
                new()
                {
                    Key = "sucursal",
                    Titulo = "Sucursal",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).Sucursal
                },
                new()
                {
                    Key = "fechaSolicitud",
                    Titulo = "Fecha de Solicitud",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).FechaSolicitud
                },
                new()
                {
                    Key = "mes",
                    Titulo = "Mes",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).Mes
                },
                new()
                {
                    Key = "anio",
                    Titulo = "Año",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).anio
                },
                new()
                {
                    Key = "observaciones",
                    Titulo = "Observaciones",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).Observacion
                },
                new()
                {
                    Key = "estatus",
                    Titulo = "Estatus",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).Estatus
                },
                new()
                {
                    Key = "usuarioSolicita",
                    Titulo = "Usuario que solicita",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).UsuarioSolicita
                },
                new()
                {
                    Key = "fechaCreacion",
                    Titulo = "Fecha de Creacion",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).FechaCreacion
                },
                new()
                {
                    Key = "productoCodigo",
                    Titulo = "Codigo del producto",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).ProductoCodigo
                },
                new()
                {
                    Key = "productoNombre",
                    Titulo = "Nombre del Producto",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).ProductoNombre
                },
                new()
                {
                    Key = "cantidadKg",
                    Titulo = "Cantidad en KG",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).CantidadKg
                },
                new()
                {
                    Key = "nota",
                    Titulo = "Nota",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).Nota
                },
                new()
                {
                    Key = "cajas",
                    Titulo = "Cajas",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).Cajas
                },
                new()
                {
                    Key = "autorizacionPresupuestoLinea",
                    Titulo = "Autorizacion Presupuesto Linea",
                    Visible = true,
                    ValueSelector = x =>
                        ((TransferenciasDetallesCabecera)x).AutorizacionPresupuestoLinea
                }
            };

        

        public static List<FiltroReporteViewModel> Filtros =>
            new()
            {
                 new()
        {
            Key = "Consecutivo",
            Label = "Consecutivo",
            Tipo = TipoFiltroReporte.Texto
        },

        new()
        {
            Key = "Sucursal",
            Label = "Sucursal",
            Tipo = TipoFiltroReporte.Texto
        },

        new()
        {
            Key = "FechaInicioSolicitud",
            Label = "Fecha Inicio Solicitud",
            Tipo = TipoFiltroReporte.Fecha
        },

        new()
        {
            Key = "FechaFinSolicitud",
            Label = "Fecha Fin Solicitud",
            Tipo = TipoFiltroReporte.Fecha
        },

        new()
        {
            Key = "Anio",
            Label = "Año",
            Tipo = TipoFiltroReporte.Numero
        }
      };

        public static List<FiltroReporteViewModel> CrearFiltros(
    TransferenciasDetallesFiltroViewModel filtros)
        {
            var lista = Filtros;

            lista.First(x => x.Key == "Consecutivo")
                .Valor = filtros.Consecutivo;

            lista.First(x => x.Key == "Sucursal")
                .Valor = filtros.Sucursal;

            lista.First(x => x.Key == "FechaInicioSolicitud")
                .Valor = filtros.FechaInicioSolicitud?.ToString("yyyy-MM-dd");

            lista.First(x => x.Key == "FechaFinSolicitud")
                .Valor = filtros.FechaFinSolicitud?.ToString("yyyy-MM-dd");

            lista.First(x => x.Key == "Anio")
                .Valor = filtros.Anio?.ToString();

            return lista;
        }

        public override List<ColumnaReporteViewModel> GetColumns()
        {
            return Columnas;
        }

        public override List<FiltroReporteViewModel> BuildFilters(
     TransferenciasDetallesFiltroViewModel filtros)
        {
            return CrearFiltros(filtros);
        }


        public override IQueryable BuildQuery(
    AppDbContext context,
    TransferenciasDetallesFiltroViewModel filtros)
        {
            var query = context.TransferenciasDetallesCabeceras
                .AsNoTracking()
                .AsQueryable();

            query = query.AplicarFiltros(filtros);

            query = query.OrderByDescending(
                x => x.FechaCreacion);

            return query;
        }

        public override async Task<ReportExecutionResult> ExecuteAsync(
    AppDbContext context,
    TransferenciasDetallesFiltroViewModel filtros,
    int page,
    int pageSize)
        {
            var query = BuildQuery(
                context,
                filtros);

            var typedQuery =
                (IQueryable<TransferenciasDetallesCabecera>)query;

            var totalRecords =
                await typedQuery.CountAsync();

            var rows = await typedQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new ReportExecutionResult
            {
                Rows = rows.Cast<object>(),
                TotalRecords = totalRecords
            };
        }

    }
}
