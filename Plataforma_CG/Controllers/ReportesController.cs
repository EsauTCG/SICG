using System.Collections;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Plataforma_CG.Data;
using Plataforma_CG.Models.Reportes;
using Plataforma_CG.Models.Reportes.Configuracion;
using Plataforma_CG.Models.Reportes.Core;
using Plataforma_CG.Models.Reportes.Datasets;
using Plataforma_CG.Models.Reportes.Exporting.Interfaces;
using Plataforma_CG.Models.Reportes.Filtros;
using Plataforma_CG.Models.Reportes.QueryExtensions;
using Plataforma_CG.Models.Reportes.ViewModels;
using ColumnaReporteVM = Plataforma_CG.Models.Reportes.ViewModels.ColumnaReporteViewModel;

namespace Plataforma_CG.Controllers
{
    public class ReportesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IReportRegistry _reportRegistry;
        private readonly IReportFilterFactory _filterFactory;
        private readonly IExcelExporter _excelExporter;
        private readonly IPdfExporter _pdfExporter;

        public ReportesController(
            AppDbContext context,
            IReportRegistry reportRegistry,
            IReportFilterFactory filterFactory,
            IExcelExporter excelExporter,
            IPdfExporter pdfExporter)
        {
            _context = context;
            _reportRegistry = reportRegistry;
            _filterFactory = filterFactory;
            _excelExporter = excelExporter;
            _pdfExporter = pdfExporter;
        }

        public class ReporteFiltroDinamicoViewModel
        {
            public string? Key { get; set; }

            public string? Operador { get; set; }

            public string? Valor { get; set; }

            public string? Valor2 { get; set; }
        }

        private class SqlViewColumnInfo
        {
            public string ColumnName { get; set; } = string.Empty;

            public string DataType { get; set; } = string.Empty;
        }

        public async Task<IActionResult> Visor(
            string reportKey,
            List<string>? visible = null,
            int page = 1,
            int pageSize = 50,
            List<ReporteFiltroDinamicoViewModel>? filtros = null,
            string? ordenarPor = null,
            string direccion = "asc",
            string? nombreReporte = null,
            string? descripcionReporte = null,
            bool compartirReporte = false,
            List<ColumnaCalculadaViewModel>? calculadas = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = Math.Clamp(pageSize, 25, 5000);

            if (string.IsNullOrWhiteSpace(reportKey))
            {
                return RedirectToAction(nameof(Portal));
            }

            if (reportKey.StartsWith("sqlview:", StringComparison.OrdinalIgnoreCase))
            {
                return await VisorSqlView(
                    reportKey,
                    visible,
                    page,
                    pageSize,
                    filtros,
                    ordenarPor,
                    direccion,
                    nombreReporte,
                    descripcionReporte,
                    compartirReporte,
                    calculadas);
            }

            var report = _reportRegistry.Get(reportKey);

            var filtrosFactory = _filterFactory.Create(
                report,
                Request.Query);

            var query = report.BuildQuery(
                _context,
                filtrosFactory);

            var columnas = report.GetColumns();

            var rowsBase = await MaterializarQueryAsync(query);

            var rowsDiccionario = ConvertirFilasADiccionario(
                rowsBase,
                columnas);

            var columnasCalculadas = PrepararColumnasCalculadas(
                calculadas);

            AplicarColumnasCalculadas(
                rowsDiccionario,
                columnasCalculadas);

            PrepararColumnasParaDiccionario(
                columnas);

            if (visible?.Any() == true)
            {
                foreach (var columna in columnas)
                {
                    columna.Visible = visible.Contains(
                        columna.Key,
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            AgregarColumnasCalculadasAConfiguracion(
                columnas,
                columnasCalculadas);

            var datosFiltrados = AplicarFiltrosDinamicos(
                    rowsDiccionario.Cast<object>(),
                    filtros)
                .ToList();

            var datosOrdenados = AplicarOrdenDinamico(
                    datosFiltrados,
                    ordenarPor,
                    direccion)
                .ToList();

            var totalRegistros = datosOrdenados.Count;

            var totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(totalRegistros / (double)pageSize);

            if (totalPaginas > 0 && page > totalPaginas)
            {
                page = totalPaginas;
            }

            var resultados = datosOrdenados
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var columnasVisibles = columnas
                .Where(x => x.Visible)
                .Select(x => x.Key)
                .ToList();

            var vm = new ReporteVisorViewModel
            {
                Filtros = filtrosFactory,

                Resultados = resultados,

                ColumnasVisibles = columnasVisibles,

                ColumnasCalculadas = columnasCalculadas,

                Configuracion = new ReporteConfiguracionViewModel
                {
                    Titulo = report.ReportName,

                    Datasetkey = report.ReportKey,

                    Columnas = columnas,

                    Filtros = report.BuildFilters(filtrosFactory),

                    PermiteExportarExcel = true,

                    PermiteExportarPdf = true,

                    PermiteGuardarPlantilla = true
                },

                Paginacion = new()
                {
                    PaginaActual = page,
                    TamanoPagina = pageSize,
                    TotalPaginas = totalPaginas,
                    TotalRegistros = totalRegistros
                }
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(
            string reportKey,
            List<string>? visible = null,
            List<ReporteFiltroDinamicoViewModel>? filtros = null,
            string? ordenarPor = null,
            string direccion = "asc",
            List<ColumnaCalculadaViewModel>? calculadas = null)
        {
            if (string.IsNullOrWhiteSpace(reportKey))
            {
                return RedirectToAction(nameof(Portal));
            }

            if (reportKey.StartsWith("sqlview:", StringComparison.OrdinalIgnoreCase))
            {
                return await ExportExcelSqlView(
                    reportKey,
                    visible,
                    filtros,
                    ordenarPor,
                    direccion,
                    calculadas);
            }

            var report = _reportRegistry.Get(reportKey);

            var filtrosFactory = _filterFactory.Create(
                report,
                Request.Query);

            var query = report.BuildQuery(
                _context,
                filtrosFactory);

            var columns = report.GetColumns();

            var rowsBase = await MaterializarQueryAsync(query);

            var rowsDiccionario = ConvertirFilasADiccionario(
                rowsBase,
                columns);

            var columnasCalculadas = PrepararColumnasCalculadas(
                calculadas);

            AplicarColumnasCalculadas(
                rowsDiccionario,
                columnasCalculadas);

            PrepararColumnasParaDiccionario(
                columns);

            if (visible?.Any() == true)
            {
                foreach (var columna in columns)
                {
                    columna.Visible = visible.Contains(
                        columna.Key,
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            AgregarColumnasCalculadasAConfiguracion(
                columns,
                columnasCalculadas);

            var rowsFiltrados = AplicarFiltrosDinamicos(
                    rowsDiccionario.Cast<object>(),
                    filtros)
                .ToList();

            var rowsOrdenados = AplicarOrdenDinamico(
                    rowsFiltrados,
                    ordenarPor,
                    direccion)
                .ToList();

            var file = _excelExporter.Export(
                rowsOrdenados,
                columns,
                report.ReportName);

            return File(
                file,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{report.ReportKey}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> ExportPdf(
            string reportKey,
            List<string>? visible = null,
            List<ReporteFiltroDinamicoViewModel>? filtros = null,
            string? ordenarPor = null,
            string direccion = "asc",
            List<ColumnaCalculadaViewModel>? calculadas = null)
        {
            if (string.IsNullOrWhiteSpace(reportKey))
            {
                return RedirectToAction(nameof(Portal));
            }

            if (reportKey.StartsWith("sqlview:", StringComparison.OrdinalIgnoreCase))
            {
                return await ExportPdfSqlView(
                    reportKey,
                    visible,
                    filtros,
                    ordenarPor,
                    direccion,
                    calculadas);
            }

            var report = _reportRegistry.Get(reportKey);

            var filtrosFactory = _filterFactory.Create(
                report,
                Request.Query);

            var query = report.BuildQuery(
                _context,
                filtrosFactory);

            var columns = report.GetColumns();

            var rowsBase = await MaterializarQueryAsync(query);

            var rowsDiccionario = ConvertirFilasADiccionario(
                rowsBase,
                columns);

            var columnasCalculadas = PrepararColumnasCalculadas(
                calculadas);

            AplicarColumnasCalculadas(
                rowsDiccionario,
                columnasCalculadas);

            PrepararColumnasParaDiccionario(
                columns);

            if (visible?.Any() == true)
            {
                foreach (var columna in columns)
                {
                    columna.Visible = visible.Contains(
                        columna.Key,
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            AgregarColumnasCalculadasAConfiguracion(
                columns,
                columnasCalculadas);

            var rowsFiltrados = AplicarFiltrosDinamicos(
                    rowsDiccionario.Cast<object>(),
                    filtros)
                .ToList();

            var rowsOrdenados = AplicarOrdenDinamico(
                    rowsFiltrados,
                    ordenarPor,
                    direccion)
                .ToList();

            var file = _pdfExporter.Export(
                rowsOrdenados,
                columns,
                report.ReportName);

            return File(
                file,
                "application/pdf",
                $"{report.ReportKey}.pdf");
        }

        public async Task<IActionResult> Portal()
        {
            var reportes = await ObtenerReportesDesdeSqlAsync();

            return View(reportes);
        }

        public IActionResult Guardados()
        {
            return View();
        }

        [HttpPost]
        public IActionResult GuardarPlantilla(
            string reportKey,
            string? nombreReporte = null,
            string? descripcionReporte = null,
            bool compartirReporte = false)
        {
            TempData["Mensaje"] = "La función de guardar plantilla está pendiente de implementar.";

            if (string.IsNullOrWhiteSpace(reportKey))
            {
                return RedirectToAction(nameof(Portal));
            }

            return RedirectToAction(nameof(Visor), new
            {
                reportKey
            });
        }

        private async Task<IActionResult> VisorSqlView(
            string reportKey,
            List<string>? visible = null,
            int page = 1,
            int pageSize = 50,
            List<ReporteFiltroDinamicoViewModel>? filtros = null,
            string? ordenarPor = null,
            string direccion = "asc",
            string? nombreReporte = null,
            string? descripcionReporte = null,
            bool compartirReporte = false,
            List<ColumnaCalculadaViewModel>? calculadas = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = Math.Clamp(pageSize, 25, 5000);

            if (!TryParseSqlViewReportKey(
                    reportKey,
                    out var schema,
                    out var viewName))
            {
                throw new ArgumentException(
                    $"ReportKey SQL View inválido: {reportKey}");
            }

            if (!string.Equals(schema, "rpt", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Solo se permiten vistas del schema rpt. Vista recibida: {schema}.{viewName}");
            }

            var existeVista = await ExisteSqlViewAsync(
                schema,
                viewName);

            if (!existeVista)
            {
                throw new ArgumentException(
                    $"La vista SQL {schema}.{viewName} no existe.");
            }

            var columnasSql = await ObtenerColumnasSqlViewAsync(
                schema,
                viewName);

            var columnas = CrearColumnasSqlView(
                columnasSql);

            var rowsDiccionario = await ObtenerDatosSqlViewAsync(
                schema,
                viewName,
                columnasSql);

            var columnasCalculadas = PrepararColumnasCalculadas(
                calculadas);

            AplicarColumnasCalculadas(
                rowsDiccionario,
                columnasCalculadas);

            if (visible?.Any() == true)
            {
                foreach (var columna in columnas)
                {
                    columna.Visible = visible.Contains(
                        columna.Key,
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            AgregarColumnasCalculadasSqlView(
                columnas,
                columnasCalculadas);

            var datosFiltrados = AplicarFiltrosDinamicos(
                    rowsDiccionario.Cast<object>(),
                    filtros)
                .ToList();

            var datosOrdenados = AplicarOrdenDinamico(
                    datosFiltrados,
                    ordenarPor,
                    direccion)
                .ToList();

            var totalRegistros = datosOrdenados.Count;

            var totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(totalRegistros / (double)pageSize);

            if (totalPaginas > 0 && page > totalPaginas)
            {
                page = totalPaginas;
            }

            var resultados = datosOrdenados
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var columnasVisibles = columnas
                .Where(x => x.Visible)
                .Select(x => x.Key)
                .ToList();

            var titulo = HumanizarNombreReporte(viewName);

            var vm = new ReporteVisorViewModel
            {
                Resultados = resultados,

                ColumnasVisibles = columnasVisibles,

                ColumnasCalculadas = columnasCalculadas,

                Configuracion = new ReporteConfiguracionViewModel
                {
                    Titulo = titulo,

                    Datasetkey = reportKey,

                    Columnas = columnas,

                    PermiteExportarExcel = true,

                    PermiteExportarPdf = true,

                    PermiteGuardarPlantilla = true
                },

                Paginacion = new()
                {
                    PaginaActual = page,
                    TamanoPagina = pageSize,
                    TotalPaginas = totalPaginas,
                    TotalRegistros = totalRegistros
                }
            };

            return View("Visor", vm);
        }

        private async Task<IActionResult> ExportExcelSqlView(
            string reportKey,
            List<string>? visible = null,
            List<ReporteFiltroDinamicoViewModel>? filtros = null,
            string? ordenarPor = null,
            string direccion = "asc",
            List<ColumnaCalculadaViewModel>? calculadas = null)
        {
            if (!TryParseSqlViewReportKey(
                    reportKey,
                    out var schema,
                    out var viewName))
            {
                throw new ArgumentException(
                    $"ReportKey SQL View inválido: {reportKey}");
            }

            if (!string.Equals(schema, "rpt", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Solo se permiten vistas del schema rpt. Vista recibida: {schema}.{viewName}");
            }

            var existeVista = await ExisteSqlViewAsync(
                schema,
                viewName);

            if (!existeVista)
            {
                throw new ArgumentException(
                    $"La vista SQL {schema}.{viewName} no existe.");
            }

            var columnasSql = await ObtenerColumnasSqlViewAsync(
                schema,
                viewName);

            var columnas = CrearColumnasSqlView(
                columnasSql);

            var rowsDiccionario = await ObtenerDatosSqlViewAsync(
                schema,
                viewName,
                columnasSql);

            var columnasCalculadas = PrepararColumnasCalculadas(
                calculadas);

            AplicarColumnasCalculadas(
                rowsDiccionario,
                columnasCalculadas);

            if (visible?.Any() == true)
            {
                foreach (var columna in columnas)
                {
                    columna.Visible = visible.Contains(
                        columna.Key,
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            AgregarColumnasCalculadasSqlView(
                columnas,
                columnasCalculadas);

            var rowsFiltrados = AplicarFiltrosDinamicos(
                    rowsDiccionario.Cast<object>(),
                    filtros)
                .ToList();

            var rowsOrdenados = AplicarOrdenDinamico(
                    rowsFiltrados,
                    ordenarPor,
                    direccion)
                .ToList();

            var titulo = HumanizarNombreReporte(viewName);

            var file = _excelExporter.Export(
                rowsOrdenados,
                columnas,
                titulo);

            return File(
                file,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{viewName}.xlsx");
        }

        private async Task<IActionResult> ExportPdfSqlView(
            string reportKey,
            List<string>? visible = null,
            List<ReporteFiltroDinamicoViewModel>? filtros = null,
            string? ordenarPor = null,
            string direccion = "asc",
            List<ColumnaCalculadaViewModel>? calculadas = null)
        {
            if (!TryParseSqlViewReportKey(
                    reportKey,
                    out var schema,
                    out var viewName))
            {
                throw new ArgumentException(
                    $"ReportKey SQL View inválido: {reportKey}");
            }

            if (!string.Equals(schema, "rpt", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Solo se permiten vistas del schema rpt. Vista recibida: {schema}.{viewName}");
            }

            var existeVista = await ExisteSqlViewAsync(
                schema,
                viewName);

            if (!existeVista)
            {
                throw new ArgumentException(
                    $"La vista SQL {schema}.{viewName} no existe.");
            }

            var columnasSql = await ObtenerColumnasSqlViewAsync(
                schema,
                viewName);

            var columnas = CrearColumnasSqlView(
                columnasSql);

            var rowsDiccionario = await ObtenerDatosSqlViewAsync(
                schema,
                viewName,
                columnasSql);

            var columnasCalculadas = PrepararColumnasCalculadas(
                calculadas);

            AplicarColumnasCalculadas(
                rowsDiccionario,
                columnasCalculadas);

            if (visible?.Any() == true)
            {
                foreach (var columna in columnas)
                {
                    columna.Visible = visible.Contains(
                        columna.Key,
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            AgregarColumnasCalculadasSqlView(
                columnas,
                columnasCalculadas);

            var rowsFiltrados = AplicarFiltrosDinamicos(
                    rowsDiccionario.Cast<object>(),
                    filtros)
                .ToList();

            var rowsOrdenados = AplicarOrdenDinamico(
                    rowsFiltrados,
                    ordenarPor,
                    direccion)
                .ToList();

            var titulo = HumanizarNombreReporte(viewName);

            var file = _pdfExporter.Export(
                rowsOrdenados,
                columnas,
                titulo);

            return File(
                file,
                "application/pdf",
                $"{viewName}.pdf");
        }

        private async Task<List<object>> MaterializarQueryAsync(IQueryable query)
        {
            if (query.Provider is IAsyncQueryProvider)
            {
                return await query
                    .Cast<object>()
                    .ToListAsync();
            }

            return query
                .Cast<object>()
                .ToList();
        }

        private List<Dictionary<string, object?>> ConvertirFilasADiccionario(
            List<object> rows,
            dynamic columnas)
        {
            var resultado = new List<Dictionary<string, object?>>();

            foreach (var row in rows)
            {
                var dict = new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var columna in columnas)
                {
                    string key = columna.Key;

                    object? valor = null;

                    try
                    {
                        if (columna.ValueSelector != null)
                        {
                            valor = columna.ValueSelector(row);
                        }
                        else
                        {
                            valor = ObtenerValorPropiedad(row, key);
                        }
                    }
                    catch
                    {
                        valor = ObtenerValorPropiedad(row, key);
                    }

                    dict[key] = valor;
                }

                resultado.Add(dict);
            }

            return resultado;
        }

        private void PrepararColumnasParaDiccionario(dynamic columnas)
        {
            foreach (var columna in columnas)
            {
                try
                {
                    string key = columna.Key;

                    columna.ValueSelector = new Func<object, object?>(item =>
                    {
                        return ObtenerValorPropiedad(
                            item,
                            key);
                    });
                }
                catch
                {
                    // Si no se puede reemplazar el ValueSelector,
                    // se mantiene el existente.
                }
            }
        }

        private void AgregarColumnasCalculadasAConfiguracion(
            dynamic columnas,
            List<ColumnaCalculadaViewModel> calculadas)
        {
            if (calculadas == null || !calculadas.Any())
            {
                return;
            }

            if (columnas is not IList listaColumnas)
            {
                return;
            }

            if (listaColumnas.Count == 0)
            {
                return;
            }

            var tipoColumna = listaColumnas[0]?.GetType();

            if (tipoColumna == null)
            {
                return;
            }

            foreach (var calculada in calculadas)
            {
                if (string.IsNullOrWhiteSpace(calculada.Nombre))
                {
                    continue;
                }

                var existe = listaColumnas
                    .Cast<object>()
                    .Any(x =>
                    {
                        var key = ObtenerPropiedadComoTexto(
                            x,
                            "Key");

                        return string.Equals(
                            key,
                            calculada.Nombre,
                            StringComparison.OrdinalIgnoreCase);
                    });

                if (existe)
                {
                    continue;
                }

                var nuevaColumna = Activator.CreateInstance(
                    tipoColumna);

                if (nuevaColumna == null)
                {
                    continue;
                }

                var keyColumna = calculada.Nombre.Trim();

                SetearPropiedadSiExiste(
                    nuevaColumna,
                    "Key",
                    keyColumna);

                SetearPropiedadSiExiste(
                    nuevaColumna,
                    "Titulo",
                    keyColumna);

                SetearPropiedadSiExiste(
                    nuevaColumna,
                    "Visible",
                    true);

                SetearPropiedadSiExiste(
                    nuevaColumna,
                    "CssClass",
                    "text-end");

                SetearPropiedadSiExiste(
                    nuevaColumna,
                    "Width",
                    "140px");

                SetearPropiedadSiExiste(
                    nuevaColumna,
                    "ValueSelector",
                    new Func<object, object?>(item =>
                    {
                        var valor = ObtenerValorPropiedad(
                            item,
                            keyColumna);

                        return FormatearValorCalculado(
                            valor,
                            calculada.Formato);
                    }));

                listaColumnas.Add(nuevaColumna);
            }
        }

        private string? ObtenerPropiedadComoTexto(
            object item,
            string propiedad)
        {
            var prop = item.GetType()
                .GetProperties()
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Name,
                        propiedad,
                        StringComparison.OrdinalIgnoreCase));

            return prop?.GetValue(item)?.ToString();
        }

        private void SetearPropiedadSiExiste(
            object item,
            string propiedad,
            object? valor)
        {
            var prop = item.GetType()
                .GetProperties()
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Name,
                        propiedad,
                        StringComparison.OrdinalIgnoreCase));

            if (prop == null || !prop.CanWrite)
            {
                return;
            }

            try
            {
                if (valor == null)
                {
                    prop.SetValue(item, null);
                    return;
                }

                if (prop.PropertyType.IsAssignableFrom(valor.GetType()))
                {
                    prop.SetValue(item, valor);
                    return;
                }

                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(item, valor.ToString());
                    return;
                }

                if (prop.PropertyType == typeof(bool) &&
                    bool.TryParse(valor.ToString(), out var boolValor))
                {
                    prop.SetValue(item, boolValor);
                    return;
                }

                prop.SetValue(item, valor);
            }
            catch
            {
                // Se ignora para evitar romper el reporte.
            }
        }

        private List<ColumnaCalculadaViewModel> PrepararColumnasCalculadas(
            List<ColumnaCalculadaViewModel>? calculadas)
        {
            if (calculadas == null)
            {
                return new List<ColumnaCalculadaViewModel>();
            }

            return calculadas
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Nombre) &&
                    !string.IsNullOrWhiteSpace(x.Formula))
                .Select(x => new ColumnaCalculadaViewModel
                {
                    Nombre = x.Nombre!.Trim(),
                    Formula = x.Formula!.Trim(),
                    Formato = string.IsNullOrWhiteSpace(x.Formato)
                        ? "Numero"
                        : x.Formato.Trim()
                })
                .ToList();
        }

        private void AplicarColumnasCalculadas(
            List<Dictionary<string, object?>> filas,
            List<ColumnaCalculadaViewModel> calculadas)
        {
            if (filas == null || !filas.Any())
            {
                return;
            }

            if (calculadas == null || !calculadas.Any())
            {
                return;
            }

            foreach (var fila in filas)
            {
                foreach (var columna in calculadas)
                {
                    if (string.IsNullOrWhiteSpace(columna.Nombre) ||
                        string.IsNullOrWhiteSpace(columna.Formula))
                    {
                        continue;
                    }

                    var resultado = EvaluarFormula(
                        columna.Formula,
                        fila);

                    fila[columna.Nombre] = resultado;
                }
            }
        }

        private decimal? EvaluarFormula(
            string formula,
            IDictionary<string, object?> fila)
        {
            if (string.IsNullOrWhiteSpace(formula))
            {
                return null;
            }

            var expresion = formula;

            foreach (var columna in fila.OrderByDescending(x => x.Key.Length))
            {
                var valorTexto = columna.Value?.ToString() ?? "0";

                if (!TryParseDecimal(valorTexto, out var numero))
                {
                    numero = 0;
                }

                expresion = expresion.Replace(
                    columna.Key,
                    numero.ToString(CultureInfo.InvariantCulture),
                    StringComparison.OrdinalIgnoreCase);
            }

            if (!EsFormulaSegura(expresion))
            {
                return null;
            }

            try
            {
                var resultado = new System.Data.DataTable()
                    .Compute(expresion, null);

                if (resultado == null)
                {
                    return null;
                }

                if (TryParseDecimal(resultado.ToString(), out var decimalResultado))
                {
                    return decimalResultado;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private bool EsFormulaSegura(string expresion)
        {
            if (string.IsNullOrWhiteSpace(expresion))
            {
                return false;
            }

            var caracteresPermitidos = "0123456789.+-*/() ";

            return expresion.All(c =>
                caracteresPermitidos.Contains(c));
        }

        private string FormatearValorCalculado(
            object? valor,
            string? formato)
        {
            if (valor == null)
            {
                return string.Empty;
            }

            if (!TryParseDecimal(valor.ToString(), out var numero))
            {
                return valor.ToString() ?? string.Empty;
            }

            return formato switch
            {
                "Moneda" => numero.ToString("C2"),
                "Porcentaje" => numero.ToString("N2") + " %",
                _ => numero.ToString("N2")
            };
        }

        private IEnumerable<object> AplicarFiltrosDinamicos(
            IEnumerable<object> datos,
            List<ReporteFiltroDinamicoViewModel>? filtros)
        {
            if (filtros == null || !filtros.Any())
            {
                return datos;
            }

            var filtrosValidos = filtros
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToList();

            foreach (var filtro in filtrosValidos)
            {
                datos = datos.Where(item =>
                {
                    var valorCampo = ObtenerValorPropiedad(
                        item,
                        filtro.Key!);

                    return CumpleFiltro(
                        valorCampo,
                        filtro.Operador,
                        filtro.Valor,
                        filtro.Valor2);
                });
            }

            return datos;
        }

        private IEnumerable<object> AplicarOrdenDinamico(
            IEnumerable<object> datos,
            string? ordenarPor,
            string? direccion)
        {
            if (string.IsNullOrWhiteSpace(ordenarPor))
            {
                return datos;
            }

            bool descendente = string.Equals(
                direccion,
                "desc",
                StringComparison.OrdinalIgnoreCase);

            return descendente
                ? datos.OrderByDescending(x => ObtenerValorOrdenable(x, ordenarPor))
                : datos.OrderBy(x => ObtenerValorOrdenable(x, ordenarPor));
        }

        private object? ObtenerValorPropiedad(
            object item,
            string propiedad)
        {
            if (item == null || string.IsNullOrWhiteSpace(propiedad))
            {
                return null;
            }

            if (item is IDictionary<string, object?> dictionaryNullable)
            {
                var key = dictionaryNullable.Keys.FirstOrDefault(x =>
                    string.Equals(
                        x,
                        propiedad,
                        StringComparison.OrdinalIgnoreCase));

                if (key != null)
                {
                    return dictionaryNullable[key];
                }

                var keyNormalizada = dictionaryNullable.Keys.FirstOrDefault(x =>
                    NormalizarNombre(x) == NormalizarNombre(propiedad));

                if (keyNormalizada != null)
                {
                    return dictionaryNullable[keyNormalizada];
                }

                return null;
            }

            if (item is IDictionary<string, object> dictionary)
            {
                var key = dictionary.Keys.FirstOrDefault(x =>
                    string.Equals(
                        x,
                        propiedad,
                        StringComparison.OrdinalIgnoreCase));

                if (key != null)
                {
                    return dictionary[key];
                }

                var keyNormalizada = dictionary.Keys.FirstOrDefault(x =>
                    NormalizarNombre(x) == NormalizarNombre(propiedad));

                if (keyNormalizada != null)
                {
                    return dictionary[keyNormalizada];
                }

                return null;
            }

            var propertyInfo = item.GetType()
                .GetProperties()
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Name,
                        propiedad,
                        StringComparison.OrdinalIgnoreCase));

            if (propertyInfo != null)
            {
                return propertyInfo.GetValue(item);
            }

            propertyInfo = item.GetType()
                .GetProperties()
                .FirstOrDefault(x =>
                    NormalizarNombre(x.Name) == NormalizarNombre(propiedad));

            return propertyInfo?.GetValue(item);
        }

        private IComparable? ObtenerValorOrdenable(
            object item,
            string propiedad)
        {
            var valor = ObtenerValorPropiedad(
                item,
                propiedad);

            if (valor == null)
            {
                return null;
            }

            if (valor is DateTime fecha)
            {
                return fecha;
            }

            if (valor is decimal dec)
            {
                return dec;
            }

            if (valor is int entero)
            {
                return entero;
            }

            if (valor is long largo)
            {
                return largo;
            }

            if (valor is double dbl)
            {
                return dbl;
            }

            if (valor is float flt)
            {
                return flt;
            }

            var texto = valor.ToString();

            if (TryParseDecimal(texto, out var numero))
            {
                return numero;
            }

            if (TryParseDateTime(texto, out var fechaTexto))
            {
                return fechaTexto;
            }

            return texto;
        }

        private bool CumpleFiltro(
            object? valorCampo,
            string? operador,
            string? valor,
            string? valor2)
        {
            operador = string.IsNullOrWhiteSpace(operador)
                ? "eq"
                : operador.Trim().ToLowerInvariant();

            var textoCampo = valorCampo?.ToString()?.Trim() ?? string.Empty;
            var textoValor = valor?.Trim() ?? string.Empty;
            var textoValor2 = valor2?.Trim() ?? string.Empty;

            switch (operador)
            {
                case "empty":
                    return string.IsNullOrWhiteSpace(textoCampo);

                case "notempty":
                    return !string.IsNullOrWhiteSpace(textoCampo);
            }

            if (string.IsNullOrWhiteSpace(textoValor))
            {
                return true;
            }

            switch (operador)
            {
                case "eq":
                    return CompararIgualDiferente(
                        valorCampo,
                        textoValor,
                        esDiferente: false);

                case "ne":
                    return CompararIgualDiferente(
                        valorCampo,
                        textoValor,
                        esDiferente: true);

                case "contains":
                    return textoCampo.Contains(
                        textoValor,
                        StringComparison.OrdinalIgnoreCase);

                case "starts":
                    return textoCampo.StartsWith(
                        textoValor,
                        StringComparison.OrdinalIgnoreCase);

                case "gt":
                    return CompararMayorMenor(
                        valorCampo,
                        textoValor,
                        ">");

                case "gte":
                    return CompararMayorMenor(
                        valorCampo,
                        textoValor,
                        ">=");

                case "lt":
                    return CompararMayorMenor(
                        valorCampo,
                        textoValor,
                        "<");

                case "lte":
                    return CompararMayorMenor(
                        valorCampo,
                        textoValor,
                        "<=");

                case "between":
                    return CompararEntre(
                        valorCampo,
                        textoValor,
                        textoValor2);

                default:
                    return true;
            }
        }

        private bool CompararIgualDiferente(
            object? valorCampo,
            string valorFiltro,
            bool esDiferente)
        {
            var textoCampo = valorCampo?.ToString()?.Trim() ?? string.Empty;
            var textoValor = valorFiltro?.Trim() ?? string.Empty;

            bool iguales;

            if (TryParseDateTime(textoCampo, out var fechaCampo) &&
                TryParseDateTime(textoValor, out var fechaFiltro))
            {
                // Para filtros de calendario se compara solo la fecha,
                // ignorando la hora que pueda venir desde SQL Server.
                iguales = fechaCampo.Date == fechaFiltro.Date;

                return esDiferente
                    ? !iguales
                    : iguales;
            }

            if (TryParseDecimal(textoCampo, out var numeroCampo) &&
                TryParseDecimal(textoValor, out var numeroFiltro))
            {
                iguales = numeroCampo == numeroFiltro;

                return esDiferente
                    ? !iguales
                    : iguales;
            }

            iguales = string.Equals(
                textoCampo,
                textoValor,
                StringComparison.OrdinalIgnoreCase);

            return esDiferente
                ? !iguales
                : iguales;
        }

        private bool CompararMayorMenor(
            object? valorCampo,
            string valorFiltro,
            string operador)
        {
            if (valorCampo == null)
            {
                return false;
            }

            var textoCampo = valorCampo.ToString();

            if (TryParseDecimal(textoCampo, out var numeroCampo) &&
                TryParseDecimal(valorFiltro, out var numeroFiltro))
            {
                return operador switch
                {
                    ">" => numeroCampo > numeroFiltro,
                    ">=" => numeroCampo >= numeroFiltro,
                    "<" => numeroCampo < numeroFiltro,
                    "<=" => numeroCampo <= numeroFiltro,
                    _ => false
                };
            }

            if (TryParseDateTime(textoCampo, out var fechaCampo) &&
                TryParseDateTime(valorFiltro, out var fechaFiltro))
            {
                // Comparación por día para filtros de calendario.
                var fechaCampoSoloDia = fechaCampo.Date;
                var fechaFiltroSoloDia = fechaFiltro.Date;

                return operador switch
                {
                    ">" => fechaCampoSoloDia > fechaFiltroSoloDia,
                    ">=" => fechaCampoSoloDia >= fechaFiltroSoloDia,
                    "<" => fechaCampoSoloDia < fechaFiltroSoloDia,
                    "<=" => fechaCampoSoloDia <= fechaFiltroSoloDia,
                    _ => false
                };
            }

            return false;
        }

        private bool CompararEntre(
            object? valorCampo,
            string valorInicial,
            string valorFinal)
        {
            if (valorCampo == null ||
                string.IsNullOrWhiteSpace(valorInicial) ||
                string.IsNullOrWhiteSpace(valorFinal))
            {
                return false;
            }

            var textoCampo = valorCampo.ToString();

            if (TryParseDecimal(textoCampo, out var numeroCampo) &&
                TryParseDecimal(valorInicial, out var numeroInicial) &&
                TryParseDecimal(valorFinal, out var numeroFinal))
            {
                return numeroCampo >= numeroInicial &&
                       numeroCampo <= numeroFinal;
            }

            if (TryParseDateTime(textoCampo, out var fechaCampo) &&
                TryParseDateTime(valorInicial, out var fechaInicial) &&
                TryParseDateTime(valorFinal, out var fechaFinal))
            {
                // Para rangos seleccionados desde calendario se compara por día.
                return fechaCampo.Date >= fechaInicial.Date &&
                       fechaCampo.Date <= fechaFinal.Date;
            }

            return false;
        }

        private bool TryParseDecimal(
            string? valor,
            out decimal resultado)
        {
            resultado = 0;

            if (string.IsNullOrWhiteSpace(valor))
            {
                return false;
            }

            var culturas = new[]
            {
                CultureInfo.CurrentCulture,
                CultureInfo.GetCultureInfo("es-MX"),
                CultureInfo.InvariantCulture
            };

            foreach (var cultura in culturas)
            {
                if (decimal.TryParse(
                    valor,
                    NumberStyles.Any,
                    cultura,
                    out resultado))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryParseDateTime(
            string? valor,
            out DateTime resultado)
        {
            resultado = default;

            if (string.IsNullOrWhiteSpace(valor))
            {
                return false;
            }

            valor = valor.Trim();

            var formatosFecha = new[]
            {
                "yyyy-MM-dd",
                "yyyy/MM/dd",
                "dd/MM/yyyy",
                "d/M/yyyy",
                "dd-MM-yyyy",
                "d-M-yyyy",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm",
                "yyyy-MM-ddTHH:mm:ss"
            };

            var culturas = new[]
            {
                CultureInfo.CurrentCulture,
                CultureInfo.GetCultureInfo("es-MX"),
                CultureInfo.InvariantCulture
            };

            foreach (var cultura in culturas)
            {
                if (DateTime.TryParseExact(
                    valor,
                    formatosFecha,
                    cultura,
                    DateTimeStyles.None,
                    out resultado))
                {
                    return true;
                }
            }

            foreach (var cultura in culturas)
            {
                if (DateTime.TryParse(
                    valor,
                    cultura,
                    DateTimeStyles.None,
                    out resultado))
                {
                    return true;
                }
            }

            return false;
        }

        private string NormalizarNombre(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }

            return new string(
                    texto
                        .Where(char.IsLetterOrDigit)
                        .ToArray())
                .ToUpperInvariant();
        }

        private async Task<List<ReportePortalItemViewModel>> ObtenerReportesDesdeSqlAsync()
        {
            var reportes = new List<ReportePortalItemViewModel>();

            var connection = _context.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();

            command.CommandText = @"
                SELECT 
                    s.name AS SchemaName,
                    v.name AS ViewName
                FROM sys.views v
                INNER JOIN sys.schemas s 
                    ON v.schema_id = s.schema_id
                WHERE s.name = 'rpt'
                ORDER BY v.name;
            ";

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var schema = reader["SchemaName"]?.ToString() ?? "";
                var viewName = reader["ViewName"]?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(schema) ||
                    string.IsNullOrWhiteSpace(viewName))
                {
                    continue;
                }

                var titulo = HumanizarNombreReporte(viewName);

                reportes.Add(new ReportePortalItemViewModel
                {
                    Titulo = titulo,
                    Descripcion = $"Consulta dinámica de {titulo.ToLower()}.",
                    Icono = ObtenerIconoReporte(viewName),
                    ReportKey = $"sqlview:{schema}.{viewName}",
                    Schema = schema,
                    ViewName = viewName
                });
            }

            return reportes;
        }

        private string HumanizarNombreReporte(string nombreVista)
        {
            if (string.IsNullOrWhiteSpace(nombreVista))
            {
                return "Reporte";
            }

            var nombre = nombreVista
                .Replace("vw_", "", StringComparison.OrdinalIgnoreCase)
                .Replace("vw", "", StringComparison.OrdinalIgnoreCase)
                .Replace("_", " ");

            nombre = Regex.Replace(
                nombre,
                "([a-z])([A-Z])",
                "$1 $2");

            nombre = Regex.Replace(
                nombre,
                @"\s+",
                " ");

            nombre = nombre.Trim();

            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                nombre.ToLower());
        }

        private string ObtenerIconoReporte(string viewName)
        {
            var nombre = viewName.ToLower();

            if (nombre.Contains("cliente"))
            {
                return "fas fa-users";
            }

            if (nombre.Contains("venta") || nombre.Contains("orden"))
            {
                return "fas fa-file-invoice-dollar";
            }

            if (nombre.Contains("transfer"))
            {
                return "fas fa-exchange-alt";
            }

            if (nombre.Contains("inventario") || nombre.Contains("stock"))
            {
                return "fas fa-boxes";
            }

            if (nombre.Contains("devolucion") || nombre.Contains("devolución"))
            {
                return "fas fa-undo-alt";
            }

            if (nombre.Contains("producto") || nombre.Contains("articulo") || nombre.Contains("artículo"))
            {
                return "fas fa-box";
            }

            return "fas fa-table";
        }

        private bool TryParseSqlViewReportKey(
            string reportKey,
            out string schema,
            out string viewName)
        {
            schema = string.Empty;
            viewName = string.Empty;

            if (string.IsNullOrWhiteSpace(reportKey))
            {
                return false;
            }

            if (!reportKey.StartsWith("sqlview:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var raw = reportKey
                .Replace("sqlview:", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            var parts = raw.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length != 2)
            {
                return false;
            }

            schema = parts[0];
            viewName = parts[1];

            return !string.IsNullOrWhiteSpace(schema) &&
                   !string.IsNullOrWhiteSpace(viewName);
        }

        private async Task<bool> ExisteSqlViewAsync(
            string schema,
            string viewName)
        {
            var connection = _context.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();

            command.CommandText = @"
                SELECT COUNT(1)
                FROM sys.views v
                INNER JOIN sys.schemas s
                    ON v.schema_id = s.schema_id
                WHERE s.name = @schema
                  AND v.name = @viewName;
            ";

            var parameterSchema = command.CreateParameter();
            parameterSchema.ParameterName = "@schema";
            parameterSchema.Value = schema;
            command.Parameters.Add(parameterSchema);

            var parameterView = command.CreateParameter();
            parameterView.ParameterName = "@viewName";
            parameterView.Value = viewName;
            command.Parameters.Add(parameterView);

            var result = await command.ExecuteScalarAsync();

            return Convert.ToInt32(result) > 0;
        }

        private async Task<List<SqlViewColumnInfo>> ObtenerColumnasSqlViewAsync(
            string schema,
            string viewName)
        {
            var columnas = new List<SqlViewColumnInfo>();

            var connection = _context.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();

            command.CommandText = @"
                SELECT
                    c.name AS ColumnName,
                    t.name AS DataType
                FROM sys.columns c
                INNER JOIN sys.types t
                    ON c.user_type_id = t.user_type_id
                INNER JOIN sys.views v
                    ON c.object_id = v.object_id
                INNER JOIN sys.schemas s
                    ON v.schema_id = s.schema_id
                WHERE s.name = @schema
                  AND v.name = @viewName
                ORDER BY c.column_id;
            ";

            var parameterSchema = command.CreateParameter();
            parameterSchema.ParameterName = "@schema";
            parameterSchema.Value = schema;
            command.Parameters.Add(parameterSchema);

            var parameterView = command.CreateParameter();
            parameterView.ParameterName = "@viewName";
            parameterView.Value = viewName;
            command.Parameters.Add(parameterView);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                columnas.Add(new SqlViewColumnInfo
                {
                    ColumnName = reader["ColumnName"]?.ToString() ?? string.Empty,
                    DataType = reader["DataType"]?.ToString() ?? string.Empty
                });
            }

            return columnas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerDatosSqlViewAsync(
            string schema,
            string viewName,
            List<SqlViewColumnInfo> columnas)
        {
            var rows = new List<Dictionary<string, object?>>();

            var connection = _context.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();

            command.CommandText = $@"
                SELECT *
                FROM {QuoteSqlName(schema)}.{QuoteSqlName(viewName)};
            ";

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var fila = new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var columna in columnas)
                {
                    var valor = reader[columna.ColumnName];

                    fila[columna.ColumnName] = valor == DBNull.Value
                        ? null
                        : valor;
                }

                rows.Add(fila);
            }

            return rows;
        }

        private List<ColumnaReporteViewModel> CrearColumnasSqlView(
       List<SqlViewColumnInfo> columnasSql)
        {
            var columnas = new List<ColumnaReporteViewModel>();

            foreach (var columnaSql in columnasSql)
            {
                var key = columnaSql.ColumnName;

                columnas.Add(new ColumnaReporteViewModel
                {
                    Key = key,

                    Titulo = HumanizarNombreReporte(key),

                    Visible = true,

                    CssClass = ObtenerCssClassSqlView(
                        columnaSql.DataType),

                    Width = "140px",

                    ValueSelector = item =>
                    {
                        return ObtenerValorPropiedad(
                            item,
                            key);
                    }
                });
            }

            return columnas;
        }

        private void AgregarColumnasCalculadasSqlView(
      List<ColumnaReporteViewModel> columnas,
      List<ColumnaCalculadaViewModel> calculadas)
        {
            if (calculadas == null || !calculadas.Any())
            {
                return;
            }

            foreach (var calculada in calculadas)
            {
                if (string.IsNullOrWhiteSpace(calculada.Nombre))
                {
                    continue;
                }

                var key = calculada.Nombre.Trim();

                if (columnas.Any(x =>
                        string.Equals(
                            x.Key,
                            key,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                columnas.Add(new ColumnaReporteViewModel
                {
                    Key = key,

                    Titulo = key,

                    Visible = true,

                    CssClass = "text-end",

                    Width = "140px",

                    ValueSelector = item =>
                    {
                        var valor = ObtenerValorPropiedad(
                            item,
                            key);

                        return FormatearValorCalculado(
                            valor,
                            calculada.Formato);
                    }
                });
            }
        }
        private string ObtenerCssClassSqlView(string dataType)
        {
            dataType = dataType?.ToLower() ?? string.Empty;

            if (dataType.Contains("int") ||
                dataType.Contains("decimal") ||
                dataType.Contains("numeric") ||
                dataType.Contains("money") ||
                dataType.Contains("float") ||
                dataType.Contains("real"))
            {
                return "text-end";
            }

            if (dataType.Contains("date") ||
                dataType.Contains("time"))
            {
                return "text-center";
            }

            return "";
        }

        private string QuoteSqlName(string name)
        {
            return "[" + name.Replace("]", "]]") + "]";
        }
    }
}