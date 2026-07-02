using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models.Reportes.Core;
using Plataforma_CG.Models.Reportes.Enums;
using Plataforma_CG.Models.Reportes.Filtros;
using Plataforma_CG.Models.Reportes.QueryExtensions;
using Plataforma_CG.Models.Reportes.ViewModels;

namespace Plataforma_CG.Models.Reportes.Datasets
{
    public class DetallesClientesDataset :
        ReportDefinitionBase<DetallesClientesFiltroViewModel>
    {
        public override string ReportKey => 
            "detalles-clientes";

        public override string ReportName => 
            "Detalles de Clientes";

        // Aqui van las columnas que apareceran en el reporte
        public static List<ColumnaReporteViewModel> Columnas =>
            new()
            {
                new()
                {
                    Key = "codigocliente",
                    Titulo = "Código Cliente",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).CodigoCliente
                },
                new()
                {
                    Key = "nombrecliente",
                    Titulo = "Nombre Cliente",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).NombreCliente
                },
                new()
                {
                    Key = "MTClasificacion",
                    Titulo = "MTClasificacion",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).MTClasificacion
                },
                new()
                {
                    Key = "canal",
                    Titulo = "Canal",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).Canal
                },
                new()
                {
                    Key = "vendedor",
                    Titulo = "Vendedor",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).NombreVendedor
                },
                new()
                {
                    Key = "origen",
                    Titulo = "Origen",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).Origen
                },
                new()
                {
                    Key = "aliasDireccion",
                    Titulo = "Alias de direccion",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).AliasDireccion
                },
                new()
                {
                    Key = "nombreCalle",
                    Titulo = "Nombre de la Calle",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).NombreCalle
                },
                new()
                {
                    Key = "nombreColonia",
                    Titulo = "Nombre de la colonia",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).NombreColonia
                },
                new()
                {
                    Key = "ciudad",
                    Titulo = "Ciudad",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).Ciudad
                },
                new()
                {
                    Key = "nombreDelEstado",
                    Titulo = "Nombre del Estado",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).NombreDelEstado
                },
                new()
                {
                    Key = "codigopostal",
                    Titulo = "C. P.",
                    Visible = true,
                    ValueSelector = x => 
                        ((DetallesClientesCabecera)x).CodigoPostal
                },
                new()
                {
                    Key = "pais",
                    Titulo = "País",
                    Visible = true,
                    ValueSelector = x => 
                        ((DetallesClientesCabecera)x).Pais
                },
                new()
                {
                    Key = "sapAddressCode",
                    Titulo = "SapAddressCode",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).SapAddressCode
                },
                new()
                {
                    Key = "fechaDeAlta",
                    Titulo = "Fecha de alta",
                    Visible = true,
                    ValueSelector = x =>
                        ((DetallesClientesCabecera)x).FechaDeAlta
                }
            };
        public override List<ColumnaReporteViewModel> GetColumns()
        {
            return Columnas;
        }


        //Filtros, estos los consulta en DetallesClientesQueryExtensions? AUN EN DUDA SI RELAMENTE FUNCIONA ASI
        public override List<FiltroReporteViewModel> BuildFilters(
            DetallesClientesFiltroViewModel filtros)
        {
            return new()
            {
                new()
                {
                    Key = "Cliente",
                    Label = "Cliente",
                    Tipo = TipoFiltroReporte.Texto,
                    Valor = filtros.Cliente
                },
                new()
                {
                    Key = "MT_Clasificacion",
                    Label = "Clasificacion",
                    Tipo = TipoFiltroReporte.Texto,
                    Valor = filtros.MT_Clasificacion
                },
                new()
                {
                    Key = "Vendedor",
                    Label = "Vendedor",
                    Tipo = TipoFiltroReporte.Texto,
                    Valor = filtros.Vendedor
                },
                new()
                {
                    Key = "FechaInicio",
                    Label = "Fecha de Inicio",
                    Tipo = TipoFiltroReporte.Fecha
                },
                new()
                {
                    Key = "FechaFin",
                    Label = "Fecha Fin ",
                    Tipo = TipoFiltroReporte.Fecha
                }
            };
        }

            public override IQueryable BuildQuery(
                AppDbContext context,
                DetallesClientesFiltroViewModel filtros)
        {
            var query = context.DetallesClientes
                .AsNoTracking()
                .AsQueryable();

            //Se cargan los filtros que tenemos en el render razor
            query = query.AplicarFiltros(filtros);

            //Le agregamos un orden para ordenar el más reciente primero segun la Fecha que se dío de alta
            query = query.OrderByDescending(
                x => x.FechaDeAlta);

            //Se regresa el Query listo para ser consultado
            return query;
        }

        public override async Task<ReportExecutionResult> ExecuteAsync(
            AppDbContext context,
            DetallesClientesFiltroViewModel filtros,
            int page,
            int pageSize)
        {
            var query = BuildQuery(
                context,
                filtros);

            var typedQuery =
                (IQueryable<DetallesClientesCabecera>)query;

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
