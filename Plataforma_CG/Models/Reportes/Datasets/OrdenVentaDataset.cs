using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models.Reportes.Core;
using Plataforma_CG.Models.Reportes.Enums;
using Plataforma_CG.Models.Reportes.Filtros;
using Plataforma_CG.Models.Reportes.QueryExtensions;
using Plataforma_CG.Models.Reportes.ViewModels;


namespace Plataforma_CG.Models.Reportes.Datasets
{
    public class OrdenVentaDataset
        : ReportDefinitionBase<OrdenVentaFiltroViewModel>
        
    {
        public override string ReportKey => "orden-venta";

        public override string ReportName => "Órdenes de Venta";


        public const string Key = "orden-venta";

        public static List<ColumnaReporteViewModel> Columnas =>
            new()
            {
                new()
                {
                    Key = "idordenventa",
                    Titulo = "IdOrdenVenta",
                    Visible = true,
                    PermiteOrdenamiento = false,
                    ValueSelector = x => 
                        ((OrdenVentaCabecera)x).IdOrdenVenta
                },

                new()
                {
                    Key = "consecutivo",
                    Titulo = "Consecutivo",
                    Visible = true,
                    PermiteOrdenamiento = true,
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).Consecutivo
                },

                new()
                {
                    Key = "fechaentrega",
                    Titulo = "FechaEntrega",
                    Visible = true,
                    PermiteOrdenamiento = true,
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).FechaEntrega
                },

                new()
                {
                    Key = "serie",
                    Titulo = "Serie",
                    Visible = true,
                    PermiteOrdenamiento = false,
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).Serie
                },

                new()
                {
                    Key = "codigocliente",
                    Titulo = "CodigoCliente",
                    Visible = true,
                    PermiteOrdenamiento = false,
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).CodigoCliente
                },

                new()
                {
                    Key = "nombrecliente",
                    Titulo = "NombreCliente",
                    Visible = true,
                    PermiteOrdenamiento = false,
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).NombreCliente
                },
                new()
                {
                    Key = "nombrevendedor",
                    Titulo = "NombreVendedor",
                    Visible = true,
                    PermiteOrdenamiento = false,
                    ValueSelector = x => 
                        ((OrdenVentaCabecera)x).NombreVendedor
                },
                new()
                {
                    Key = "codigovendedor",
                    Titulo = "CodigoVendedor",
                    Visible = true,
                    PermiteOrdenamiento = false,
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).CodigoCliente
                },
                new()
                {
                    Key = "saldo",
                    Titulo = "Saldo",
                    Visible = true,
                    CssClass = "text-bold",
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).Saldo
                },
                new()
                {
                    Key = "credito",
                    Titulo = "Credito",
                    Visible = true,
                    CssClass = "text-bold",
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).Credito
                },
                new()
                {
                    Key = "fecharegistro",
                    Titulo = "FechaRegistro",
                    Visible = true,
                    PermiteOrdenamiento = false,
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).FechaRegistro
                },
                new()
                {
                    Key = "estado",
                    Titulo = "Estado",
                    Visible = true,
                    PermiteOrdenamiento = false,
                    CssClass = "text-bg-info",
                    ValueSelector = x =>
                        ((OrdenVentaCabecera)x).Estado
                }

            };

        public static List<FiltroReporteViewModel> Filtros =>
            new()
            {
                new()
                {
                    Key = "Cliente",
                    Label = "Cliente",
                    Tipo = TipoFiltroReporte.Texto
                },

                new()
                {
                    Key = "Estado",
                    Label = "Estado",
                    Tipo = TipoFiltroReporte.Numero

                },

                new()
                {
                    Key = "FechaInicio",
                    Label = "Fecha Inicio",
                    Tipo = TipoFiltroReporte.Fecha
                },

                new()
                {
                    Key = "FechaFin",
                    Label = "Fecha Fin",
                    Tipo = TipoFiltroReporte.Fecha
                }
            };

        public static List<FiltroReporteViewModel> CrearFiltros(
            OrdenVentaFiltroViewModel filtros)
        {
            var lista = Filtros;

            lista.First(x => x.Key == "Cliente")
                .Valor = filtros.Cliente;

            lista.First(x => x.Key == "Estado")
                .Valor = filtros.Estado?.ToString();

            lista.First(x => x.Key == "FechaInicio")
                .Valor = filtros.FechaInicio?.ToString("yyyy-MM-dd");

            lista.First(x => x.Key == "FechaFin")
                 .Valor = filtros.FechaFin?.ToString("yyyy-MM-dd");

            return lista;
        }

        public override List<ColumnaReporteViewModel> GetColumns()
        {
            return Columnas;
        }
       
        public override List<FiltroReporteViewModel> BuildFilters (OrdenVentaFiltroViewModel filtros)
        {
            return CrearFiltros(filtros);
        }

        public override IQueryable BuildQuery(
            AppDbContext context,
            OrdenVentaFiltroViewModel filtros)
        {
            //var f = (OrdenVentaFiltroViewModel)filtros;

            var query = context.OrdenVentaCabecera
                .AsNoTracking()
                .AsQueryable();

            query = query.AplicarFiltros(filtros);

            query = query.OrderByDescending(
                x => x.FechaRegistro);

            return query;
        }

        public override async Task<ReportExecutionResult> ExecuteAsync(
            AppDbContext context,
            OrdenVentaFiltroViewModel filtros,
            int page,
            int pageSize)
        {
            var query = BuildQuery(
                context,
                filtros);

            var typedQuery =
                (IQueryable<OrdenVentaCabecera>)query;

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
