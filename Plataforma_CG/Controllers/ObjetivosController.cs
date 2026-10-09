using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Plataforma_CG.Data;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    public class ObjetivosController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _db;

        public ObjetivosController(IConfiguration configuration, AppDbContext db)
        {
            _configuration = configuration;
            _db = db;
        }

        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "LEER")]
        public async Task<IActionResult> Tablero(int pagina = 1, int pageSize = 50,
            string qTexto = "", string qArea = "", string qEstado = "",
            string qTipo = "", string qPeriodo = "", string qVendedor = "",
            string qDesde = "", string qHasta = "", string qVigencia = "")
        {
            var listaObjetivos = new List<ObjetivoViewModel>();
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            // Validar perfil
            bool esAdmin = User.IsInRole("Administrador") || User.IsInRole("Sistemas");
            int perfilIdUsuario = 0;
            if (!esAdmin)
            {
                int.TryParse(User.FindFirst("PerfilId")?.Value, out perfilIdUsuario);
            }

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    string sqlObjetivos = @"
                        SELECT 
                            o.ID,
                            o.ID_Perfil,
                            o.ID_Tipo_Objetivo,
                            o.Tipo_Periodo_Cumplimiento,
                            o.Fecha_Desde,
                            o.Fecha_Hasta,
                            o.Proveedor,
                            o.ID_Cliente,
                            o.UsuarioID_Vendedor,
                            o.SKU,
                            o.CC,
                            o.LINEA AS Linea,
                            o.Descripcion_Objetivo,
                            o.Estado,
                            o.UsuarioID_Creacion,
                            o.Fecha_Creacion,
                            o.UsuarioID_Modificacion,
                            o.Fecha_Modificacion,
                            o.UsuarioID_Aprueba,
                            o.Fecha_Aprueba,
                            p.Nombre AS NombrePerfil, 
                            t.Nombre AS NombreTipoObjetivo,
                            a.ProductoNombre AS NombreArticulo,
                            uv.Usuario AS UsuarioVendedor,
                            uv.Nombre AS NombreVendedor
                        FROM dbo.Objetivos o
                        INNER JOIN dbo.Perfiles p ON o.ID_Perfil = p.Id
                        INNER JOIN dbo.Tipo_Objetivo t ON o.ID_Tipo_Objetivo = t.ID
                        LEFT JOIN dbo.ArticuloSap a ON o.SKU = a.ProductoCodigo
                        LEFT JOIN dbo.UsuarioSQL uv ON o.UsuarioID_Vendedor = uv.Id";

                    // Filtros del tablero resueltos en servidor: asi se buscan en TODOS los
                    // registros y no solo en la pagina visible, y se conservan al paginar.
                    var p = new DynamicParameters();
                    var filtros = new List<string>();

                    if (!esAdmin)
                    {
                        filtros.Add("o.ID_Perfil = @PerfilId");
                        p.Add("PerfilId", perfilIdUsuario);
                    }
                    else if (!string.IsNullOrWhiteSpace(qArea))
                    {
                        filtros.Add("p.Nombre = @Area");
                        p.Add("Area", qArea.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(qEstado))
                    {
                        filtros.Add("o.Estado = @Estado");
                        p.Add("Estado", qEstado.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(qTipo))
                    {
                        filtros.Add("t.Nombre = @Tipo");
                        p.Add("Tipo", qTipo.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(qPeriodo))
                    {
                        filtros.Add("o.Tipo_Periodo_Cumplimiento = @Periodo");
                        p.Add("Periodo", qPeriodo.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(qVendedor))
                    {
                        if (int.TryParse(qVendedor, out int vendedorSel))
                        {
                            filtros.Add("o.UsuarioID_Vendedor = @Vendedor");
                            p.Add("Vendedor", vendedorSel);
                        }
                        else
                        {
                            filtros.Add("ISNULL(uv.Nombre, '') LIKE @Vendedor");
                            p.Add("Vendedor", "%" + qVendedor.Trim() + "%");
                        }
                    }

                    // Rango de fechas sobre el periodo del objetivo (ambos extremos incluidos)
                    if (DateTime.TryParse(qDesde, out var dtDesde))
                    {
                        filtros.Add("o.Fecha_Hasta >= @Desde");
                        p.Add("Desde", dtDesde.Date);
                    }
                    if (DateTime.TryParse(qHasta, out var dtHasta))
                    {
                        filtros.Add("o.Fecha_Desde <= @Hasta");
                        p.Add("Hasta", dtHasta.Date);
                    }

                    // Vigencia relativa a la fecha de hoy
                    switch ((qVigencia ?? "").Trim().ToLowerInvariant())
                    {
                        case "vencido":
                            filtros.Add("o.Fecha_Hasta < CAST(GETDATE() AS date)");
                            break;
                        case "porvencer":
                            filtros.Add("o.Fecha_Hasta >= CAST(GETDATE() AS date) AND o.Fecha_Hasta <= DATEADD(DAY, 30, CAST(GETDATE() AS date))");
                            break;
                        case "vigente":
                            filtros.Add("o.Fecha_Hasta >= CAST(GETDATE() AS date)");
                            break;
                    }

                    if (!string.IsNullOrWhiteSpace(qTexto))
                    {
                        filtros.Add(@"(
                            ISNULL(t.Nombre, '') LIKE @Texto
                            OR ISNULL(p.Nombre, '') LIKE @Texto
                            OR ISNULL(o.Descripcion_Objetivo, '') LIKE @Texto
                            OR ISNULL(o.Proveedor, '') LIKE @Texto
                            OR ISNULL(o.SKU, '') LIKE @Texto
                            OR ISNULL(o.CC, '') LIKE @Texto
                            OR ISNULL(o.LINEA, '') LIKE @Texto
                            OR ISNULL(o.Estado, '') LIKE @Texto
                            OR ISNULL(a.ProductoNombre, '') LIKE @Texto
                            OR ISNULL(uv.Nombre, '') LIKE @Texto
                            OR ISNULL(uv.Usuario, '') LIKE @Texto)");
                        p.Add("Texto", "%" + qTexto.Trim() + "%");
                    }

                    // Nota: el espacio inicial es obligatorio: sqlObjetivos termina en "uv.Id" sin espacio
                    string whereClause = filtros.Count > 0 ? " WHERE " + string.Join(" AND ", filtros) + " " : "";

                    ViewBag.FiltroTexto = qTexto ?? "";
                    ViewBag.FiltroArea = qArea ?? "";
                    ViewBag.FiltroEstado = qEstado ?? "";
                    ViewBag.FiltroTipo = qTipo ?? "";
                    ViewBag.FiltroPeriodo = qPeriodo ?? "";
                    ViewBag.FiltroVendedor = qVendedor ?? "";
                    ViewBag.FiltroDesde = qDesde ?? "";
                    ViewBag.FiltroHasta = qHasta ?? "";
                    ViewBag.FiltroVigencia = qVigencia ?? "";

                    // Catalogos para los nuevos desplegables
                    ViewBag.TiposFiltro = await conn.QueryAsync(
                        "SELECT ID, Nombre FROM dbo.Tipo_Objetivo WHERE Activo = 1 ORDER BY Nombre");
                    ViewBag.PeriodosFiltro = await conn.QueryAsync<string>(
                        "SELECT DISTINCT Tipo_Periodo_Cumplimiento FROM dbo.Objetivos "
                        + "WHERE Tipo_Periodo_Cumplimiento IS NOT NULL AND Tipo_Periodo_Cumplimiento <> '' "
                        + "ORDER BY Tipo_Periodo_Cumplimiento");
                    ViewBag.VendedoresFiltro = await conn.QueryAsync(
                        "SELECT Id, Nombre FROM dbo.UsuarioSQL WHERE EsVendedor = 1 ORDER BY Nombre");

                    // Paginacion en servidor: evita traer miles de filas al navegador
                    if (pageSize < 10) pageSize = 10;
                    if (pageSize > 500) pageSize = 500;

                    int totalRegistros = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM dbo.Objetivos o " +
                        "INNER JOIN dbo.Perfiles p ON o.ID_Perfil = p.Id " +
                        "INNER JOIN dbo.Tipo_Objetivo t ON o.ID_Tipo_Objetivo = t.ID " +
                        "LEFT JOIN dbo.ArticuloSap a ON o.SKU = a.ProductoCodigo " +
                        "LEFT JOIN dbo.UsuarioSQL uv ON o.UsuarioID_Vendedor = uv.Id " + whereClause,
                        p);

                    int totalPaginas = totalRegistros == 0 ? 1 : (int)Math.Ceiling(totalRegistros / (double)pageSize);
                    if (pagina < 1) pagina = 1;
                    if (pagina > totalPaginas) pagina = totalPaginas;

                    // Orden ascendente por folio: el listado va del ID mas bajo al mas alto
                    string sqlPaginado = sqlObjetivos + whereClause +
                        " ORDER BY o.ID ASC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
                    p.Add("Offset", (pagina - 1) * pageSize);
                    p.Add("PageSize", pageSize);

                    var objetivosRaw = await conn.QueryAsync<ObjetivoViewModel>(sqlPaginado, p);
                    listaObjetivos = objetivosRaw.ToList();

                    ViewBag.TotalRegistros = totalRegistros;
                    ViewBag.Pagina = pagina;
                    ViewBag.TotalPaginas = totalPaginas;
                    ViewBag.PageSize = pageSize;

                    // Consulta de metas: se une por JOIN en lugar de "IN @Ids" para no
                    // exceder el limite de 2100 parametros de SQL Server
                    if (listaObjetivos.Any())
                    {
                        string sqlValores = @"
                            SELECT v.*
                            FROM dbo.Objetivo_Valor v
                            INNER JOIN dbo.Objetivos o2 ON o2.ID = v.ID_Objetivo";

                        if (!esAdmin) sqlValores += " WHERE o2.ID_Perfil = @PerfilId";

                        var valoresRaw = (await conn.QueryAsync<ObjetivoValorViewModel>(
                            sqlValores, esAdmin ? null : new { PerfilId = perfilIdUsuario })).ToList();

                        // Agrupar en diccionario: evita el Where() dentro del foreach (O(n*m))
                        var metasPorObjetivo = new Dictionary<int, List<ObjetivoValorViewModel>>();
                        foreach (var v in valoresRaw)
                        {
                            if (!metasPorObjetivo.TryGetValue(v.ID_Objetivo, out var lst))
                            {
                                lst = new List<ObjetivoValorViewModel>();
                                metasPorObjetivo[v.ID_Objetivo] = lst;
                            }
                            lst.Add(v);
                        }

                        foreach (var obj in listaObjetivos)
                        {
                            obj.ValoresConfigurados = metasPorObjetivo.TryGetValue(obj.ID, out var metas)
                                ? metas
                                : new List<ObjetivoValorViewModel>();
                        }
                    }

                    // Cargar perfiles para el filtro
                    if (esAdmin)
                        ViewBag.Perfiles = await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles");
                    else
                        ViewBag.Perfiles = await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles WHERE Id = @PerfilId", new { PerfilId = perfilIdUsuario });

                    // La bitácora ya no se carga aquí: el modal la pide filtrada y paginada
                    // a VistaParcialBitacora para no traer 300 filas en cada visita al tablero.
                    ViewBag.TiposObjetivoLog = await conn.QueryAsync(
                        "SELECT ID, Nombre FROM dbo.Tipo_Objetivo ORDER BY Nombre");

                    // Contador del aviso de vencimientos (independiente de los filtros del tablero)
                    var pVenc = new DynamicParameters();
                    string whereVenc = "";
                    if (!esAdmin)
                    {
                        whereVenc = " AND o.ID_Perfil = @PerfilId";
                        pVenc.Add("PerfilId", perfilIdUsuario);
                    }
                    pVenc.Add("Dias", DIAS_AVISO_VENCIMIENTO);

                    ViewBag.ObjetivosPorVencer = await conn.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.Objetivos o
                          WHERE o.Estado = 'Activo'
                            AND o.Fecha_Hasta IS NOT NULL
                            AND o.Fecha_Hasta <= DATEADD(DAY, @Dias, CAST(GETDATE() AS date))"
                        + whereVenc, pVenc);

                    // Catalogos para los combos del modal Editar (cliente, vendedor y SKU)
                    await CargarVendedoresYClientesAsync(conn);
                    ViewBag.Articulos = await conn.QueryAsync("SELECT ProductoCodigo, ProductoNombre FROM dbo.ArticuloSap ORDER BY ProductoCodigo");

                    ViewBag.CatalogoValores = await conn.QueryAsync(@"
                        SELECT
                            ID,
                            Nombre,
                            Unidad_Medida
                        FROM dbo.Catalogo_ValorObjetivo
                        WHERE Activo = 1
                        ORDER BY Nombre
                        ");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar los objetivos: " + ex.Message);
            }

            // Permiso efectivo (usuario sobrescribe perfil) para controlar los botones del Tablero
            var (_, _, puedeEliminar) = await PermisosHelper.ObtenerPermisoEfectivoAsync(_db, User.Identity.Name, "OBJETIVOS");
            ViewBag.PuedeEliminarObjetivos = puedeEliminar;

            return View(listaObjetivos);
        }

        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "LEER")]
        public async Task<IActionResult> VistaParcialBitacora(
            string texto = "", string estado = "", int? idPerfil = null, int? idTipo = null,
            string campoFecha = "creacion", DateTime? desde = null, DateTime? hasta = null,
            string actividad = "", int pagina = 1, int pageSize = 25)
        {
            string connection = _configuration.GetConnectionString("DefaultConnection");

            bool esAdmin = User.IsInRole("Administrador") || User.IsInRole("Sistemas");
            int perfilIdUsuario = 0;
            if (!esAdmin) int.TryParse(User.FindFirst("PerfilId")?.Value, out perfilIdUsuario);

            if (pageSize < 10) pageSize = 10;
            if (pageSize > 200) pageSize = 200;
            if (pagina < 1) pagina = 1;

            // Solo se acepta una columna de la lista blanca
            string columnaFecha;
            switch ((campoFecha ?? "").Trim().ToLowerInvariant())
            {
                case "modificacion": columnaFecha = "o.Fecha_Modificacion"; break;
                case "aprobacion": columnaFecha = "o.Fecha_Aprueba"; break;
                default: columnaFecha = "o.Fecha_Creacion"; break;
            }

            var p = new DynamicParameters();
            var filtros = new List<string>();

            if (!esAdmin)
            {
                filtros.Add("o.ID_Perfil = @PerfilId");
                p.Add("PerfilId", perfilIdUsuario);
            }
            else if (idPerfil.HasValue && idPerfil.Value > 0)
            {
                filtros.Add("o.ID_Perfil = @IdPerfil");
                p.Add("IdPerfil", idPerfil.Value);
            }

            if (idTipo.HasValue && idTipo.Value > 0)
            {
                filtros.Add("o.ID_Tipo_Objetivo = @IdTipo");
                p.Add("IdTipo", idTipo.Value);
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                filtros.Add("o.Estado = @Estado");
                p.Add("Estado", estado.Trim());
            }

            if (desde.HasValue)
            {
                filtros.Add(columnaFecha + " >= @Desde");
                p.Add("Desde", desde.Value.Date);
            }

            if (hasta.HasValue)
            {
                // Se incluye todo el dia seleccionado
                filtros.Add(columnaFecha + " < @Hasta");
                p.Add("Hasta", hasta.Value.Date.AddDays(1));
            }

            if (!string.IsNullOrWhiteSpace(texto))
            {
                filtros.Add(@"(
                        CONVERT(varchar(20), o.ID) LIKE @Texto
                        OR p.Nombre LIKE @Texto
                        OR t.Nombre LIKE @Texto
                        OR o.Estado LIKE @Texto
                        OR ISNULL(uc.Usuario, '') LIKE @Texto OR ISNULL(uc.Nombre, '') LIKE @Texto
                        OR ISNULL(um.Usuario, '') LIKE @Texto OR ISNULL(um.Nombre, '') LIKE @Texto
                        OR ISNULL(ua.Usuario, '') LIKE @Texto OR ISNULL(ua.Nombre, '') LIKE @Texto)");
                p.Add("Texto", "%" + texto.Trim() + "%");
            }

            switch ((actividad ?? "").Trim().ToLowerInvariant())
            {
                case "modificados": filtros.Add("o.Fecha_Modificacion IS NOT NULL"); break;
                case "sinmodificar": filtros.Add("o.Fecha_Modificacion IS NULL"); break;
                case "autorizados": filtros.Add("o.Fecha_Aprueba IS NOT NULL"); break;
                case "sinautorizar": filtros.Add("o.Fecha_Aprueba IS NULL"); break;
            }

            string whereLog = filtros.Count > 0 ? " WHERE " + string.Join(" AND ", filtros) : "";

            string fromLog = @"
                FROM dbo.Objetivos o
                INNER JOIN dbo.Perfiles p ON o.ID_Perfil = p.Id
                INNER JOIN dbo.Tipo_Objetivo t ON o.ID_Tipo_Objetivo = t.ID
                LEFT JOIN dbo.UsuarioSQL uc ON o.UsuarioID_Creacion = uc.Id
                LEFT JOIN dbo.UsuarioSQL um ON o.UsuarioID_Modificacion = um.Id
                LEFT JOIN dbo.UsuarioSQL ua ON o.UsuarioID_Aprueba = ua.Id"
                + whereLog;

            var registros = new List<ObjetivoLogViewModel>();
            int totalRegistros = 0;
            int totalPaginas = 1;

            try
            {
                using (var conn = new SqlConnection(connection))
                {
                    await conn.OpenAsync();

                    totalRegistros = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*)" + fromLog, p);

                    int totalPaginasLog = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)pageSize));
                    if (pagina > totalPaginasLog) pagina = totalPaginasLog;
                    totalPaginas = totalPaginasLog;

                    p.Add("Offset", (pagina - 1) * pageSize);
                    p.Add("PageSize", pageSize);

                    var sqlPaginado = @"
                        SELECT o.ID, o.Estado, o.ID_Perfil, o.ID_Tipo_Objetivo,
                               p.Nombre AS NombrePerfil,
                               t.Nombre AS NombreTipoObjetivo,
                               o.Fecha_Creacion,
                               uc.Usuario AS UsuarioCreacion, uc.Nombre AS NombreCreador,
                               o.Fecha_Modificacion,
                               um.Usuario AS UsuarioModificacion, um.Nombre AS NombreModificador,
                               o.Fecha_Aprueba,
                               ua.Usuario AS UsuarioAprueba, ua.Nombre AS NombreAutorizador"
                        + fromLog + @"
                        ORDER BY o.Fecha_Creacion DESC, o.ID DESC
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                    var filas = await conn.QueryAsync<ObjetivoLogViewModel>(sqlPaginado, p);
                    registros = filas.ToList();
                }

                ViewBag.TotalRegistros = totalRegistros;
                ViewBag.Pagina = pagina;
                ViewBag.TotalPaginas = totalPaginas;
                ViewBag.PageSize = pageSize;

                return PartialView("_BitacoraLog", registros);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar la bitácora: " + ex.Message);
                return PartialView("_BitacoraLog", registros);
            }
        }

        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "LEER")]
        public async Task<IActionResult> VistaParcialDetalle(int id)
        {
            var objetivo = await ObtenerObjetivoConValoresAsync(id);
            if (objetivo == null)
            {
                return NotFound("Objetivo no encontrado.");
            }

            // Reutilizamos el permiso para saber si mostramos los botones de Editar/Eliminar
            var (_, _, puedeEliminar) = await PermisosHelper.ObtenerPermisoEfectivoAsync(_db, User.Identity.Name, "OBJETIVOS");
            ViewBag.PuedeEliminarObjetivos = puedeEliminar;

            return PartialView("_ModalDetalleObjetivo", objetivo);
        }

        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "ESCRIBIR")]
        public async Task<IActionResult> VistaParcialEditar(int id)
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            var objetivo = await ObtenerObjetivoConValoresAsync(id);

            if (objetivo == null)
            {
                return NotFound("Objetivo no encontrado.");
            }

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                await CargarVendedoresYClientesAsync(conn);
                ViewBag.Articulos = await conn.QueryAsync("SELECT ProductoCodigo, ProductoNombre FROM dbo.ArticuloSap ORDER BY ProductoCodigo");
                ViewBag.CatalogoValores = await conn.QueryAsync("SELECT ID, Nombre, Unidad_Medida FROM dbo.Catalogo_ValorObjetivo WHERE Activo = 1 ORDER BY Nombre");

                ViewBag.CamposVisibles = await conn.ExecuteScalarAsync<string>(
                    "SELECT Campos_Visibles FROM dbo.Tipo_Objetivo WHERE ID = @IdTipo",
                    new { IdTipo = objetivo.ID_Tipo_Objetivo });
            }

            return PartialView("_ModalEditarObjetivo", objetivo);
        }

        // Método auxiliar para consultar un solo objetivo
        private async Task<ObjetivoViewModel> ObtenerObjetivoConValoresAsync(int id)
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            using (var conn = new SqlConnection(connectionString))
            {
                string sqlObjetivo = @"
                    SELECT 
                        o.ID, o.ID_Perfil, o.ID_Tipo_Objetivo, o.Tipo_Periodo_Cumplimiento,
                        o.Fecha_Desde, o.Fecha_Hasta, o.Proveedor, o.ID_Cliente,
                        o.UsuarioID_Vendedor, o.SKU, o.CC, o.LINEA AS Linea,
                        o.Descripcion_Objetivo, o.Estado, o.Fecha_Creacion,
                        o.UsuarioID_Creacion, o.UsuarioID_Modificacion, o.Fecha_Modificacion,
                        o.UsuarioID_Aprueba, o.Fecha_Aprueba,
                        p.Nombre AS NombrePerfil, t.Nombre AS NombreTipoObjetivo,
                        a.ProductoNombre AS NombreArticulo,
                        uv.Usuario AS UsuarioVendedor, uv.Nombre AS NombreVendedor,
                        uc.Usuario AS UsuarioCreacion, uc.Nombre AS NombreCreador,
                        um.Usuario AS UsuarioModificacion, um.Nombre AS NombreModificador,
                        ua.Usuario AS UsuarioAprueba, ua.Nombre AS NombreAutorizador
                    FROM dbo.Objetivos o
                    INNER JOIN dbo.Perfiles p ON o.ID_Perfil = p.Id
                    INNER JOIN dbo.Tipo_Objetivo t ON o.ID_Tipo_Objetivo = t.ID
                    LEFT JOIN dbo.ArticuloSap a ON o.SKU = a.ProductoCodigo
                    LEFT JOIN dbo.UsuarioSQL uv ON o.UsuarioID_Vendedor = uv.Id
                    LEFT JOIN dbo.UsuarioSQL uc ON o.UsuarioID_Creacion = uc.Id
                    LEFT JOIN dbo.UsuarioSQL um ON o.UsuarioID_Modificacion = um.Id
                    LEFT JOIN dbo.UsuarioSQL ua ON o.UsuarioID_Aprueba = ua.Id
                    WHERE o.ID = @Id";

                var obj = await conn.QueryFirstOrDefaultAsync<ObjetivoViewModel>(sqlObjetivo, new { Id = id });

                if (obj != null)
                {
                    string sqlValores = "SELECT * FROM dbo.Objetivo_Valor WHERE ID_Objetivo = @Id";
                    obj.ValoresConfigurados = (await conn.QueryAsync<ObjetivoValorViewModel>(sqlValores, new { Id = id })).ToList();
                }

                return obj;
            }
        }


        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "ESCRIBIR")]
        public async Task<IActionResult> Nuevo()
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Cargar catalogos base
                    ViewBag.Perfiles = await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles");

                    // Vendedor y Clientes filtrados segun el usuario (admin ve todo)
                    await CargarVendedoresYClientesAsync(conn);

                    // Cargar catalogos comerciales faltantes
                    ViewBag.Articulos = await conn.QueryAsync("SELECT ProductoCodigo, ProductoNombre FROM dbo.ArticuloSap ORDER BY ProductoCodigo");

                    ViewBag.CatalogoValores = await conn.QueryAsync(@"
                        SELECT
                            ID,
                            Nombre,
                            Unidad_Medida
                        FROM dbo.Catalogo_ValorObjetivo
                        WHERE Activo = 1
                        ORDER BY Nombre
                    ");

                    try
                    {
                        ViewBag.TiposObjetivo = await conn.QueryAsync("SELECT ID, Nombre FROM dbo.Tipo_Objetivo WHERE Activo = 1");
                    }
                    catch
                    {
                        ViewBag.TiposObjetivo = new List<dynamic>();
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar catálogos: " + ex.Message);
            }

            var modeloNuevo = new ObjetivoViewModel
            {
                Fecha_Desde = DateTime.Today
            };

            // Permiso efectivo (usuario sobrescribe perfil): solo con ELIMINAR se muestra 'Crear KPI'
            var (_, _, puedeEliminar) = await PermisosHelper.ObtenerPermisoEfectivoAsync(_db, User.Identity.Name, "OBJETIVOS");
            ViewBag.PuedeEliminarObjetivos = puedeEliminar;

            return View(modeloNuevo);
        }

        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ESCRIBIR")]
        public async Task<IActionResult> GuardarNuevo(ObjetivoViewModel modelo)
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Resolver el usuario de sesion ANTES de abrir la transaccion (Dapper exige que el
                    // comando use la transaccion si la conexion ya esta en una).
                    //Se agrego una validacion, si no se encuentra el ususario no se puede hacer INSERT

                    int? usuarioCreacionId = await ObtenerIdUsuarioAsync(conn);

                    if (!usuarioCreacionId.HasValue)
                    {
                        throw new Exception(
                            "No fue posible identificar al usuario actual en SIGO" +
                            "El objetivo no fue guardado."
                            );
                    }

                    using (var transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. Guardar Cabecera
                            string sqlObjetivo = @"
                                INSERT INTO dbo.Objetivos (
                                    ID_Perfil, ID_Tipo_Objetivo, Tipo_Periodo_Cumplimiento, 
                                    Fecha_Desde, Fecha_Hasta, Descripcion_Objetivo, 
                                    ID_Cliente, UsuarioID_Vendedor, Proveedor, SKU, CC, LINEA,
                                    Estado, UsuarioID_Creacion, Fecha_Creacion
                                ) 
                                VALUES (
                                    @ID_Perfil, @ID_Tipo_Objetivo, @Tipo_Periodo_Cumplimiento, 
                                    @Fecha_Desde, @Fecha_Hasta, @Descripcion_Objetivo, 
                                    @ID_Cliente, @UsuarioID_Vendedor, @Proveedor, @SKU, @CC, @LINEA,
                                    'Pendiente', @UsuarioCreacion, GETDATE()
                                );
                                
                                SELECT CAST(SCOPE_IDENTITY() as int);";

                            var parametrosObjetivo = new
                            {
                                modelo.ID_Perfil,
                                modelo.ID_Tipo_Objetivo,
                                modelo.Tipo_Periodo_Cumplimiento,
                                modelo.Fecha_Desde,
                                modelo.Fecha_Hasta,
                                modelo.Descripcion_Objetivo,
                                modelo.ID_Cliente,
                                modelo.UsuarioID_Vendedor,
                                modelo.Proveedor,
                                modelo.SKU,
                                modelo.CC,
                                modelo.Linea,
                                UsuarioCreacion = usuarioCreacionId
                            };

                            int nuevoObjetivoId = await conn.QuerySingleAsync<int>(sqlObjetivo, parametrosObjetivo, transaccion);

                            // 2. Guardar Detalles
                            if (modelo.ValoresConfigurados != null && modelo.ValoresConfigurados.Any())
                            {
                                string sqlValor = @"
                                    INSERT INTO dbo.Objetivo_Valor (
                                        ID_Objetivo,
                                        ID_Catalogo_ValorObjetivo,
                                        Tipo_Valor,
                                        Unidad_Medida,
                                        Valor_Minimo,
                                        Valor_Maximo,
                                        Valor_Objetivo
                                    ) VALUES (
                                        @IdObjetivo,
                                        @ID_Catalogo_ValorObjetivo,
                                        @TipoValor,
                                        @Unidad,
                                        @Minimo,
                                        @Maximo,
                                        @Meta
                                    )";

                                foreach (var val in modelo.ValoresConfigurados)
                                {
                                    await conn.ExecuteAsync(sqlValor, new
                                    {
                                        IdObjetivo = nuevoObjetivoId,
                                        ID_Catalogo_ValorObjetivo = val.ID_Catalogo_ValorObjetivo,
                                        TipoValor = val.Tipo_Valor,
                                        Unidad = val.Unidad_Medida,
                                        Minimo = val.Valor_Minimo,
                                        Maximo = val.Valor_Maximo,
                                        Meta = val.Valor_Objetivo
                                    }, transaccion);
                                }
                            }

                            // Confirmar operacion
                            transaccion.Commit();
                        }
                        catch
                        {
                            transaccion.Rollback();
                            throw;
                        }
                    }
                }

                return RedirectToAction("Tablero");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ocurrió un error al guardar: " + ex.Message);

                // Recargar catalogos en caso de error
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    ViewBag.Perfiles = conn.Query("SELECT Id, Nombre FROM dbo.Perfiles");
                    await CargarVendedoresYClientesAsync(conn);
                    ViewBag.Articulos = conn.Query("SELECT ProductoCodigo, ProductoNombre FROM dbo.ArticuloSap ORDER BY ProductoCodigo");

                    try { ViewBag.TiposObjetivo = conn.Query("SELECT ID, Nombre FROM dbo.Tipo_Objetivo WHERE Activo = 1"); }
                    catch { ViewBag.TiposObjetivo = new List<dynamic>(); }
                }

                return View("Nuevo", modelo);
            }
        }

        // Vendedores y Clientes segun el usuario: admin ve todo; no-admin solo su vendedor
        // y los clientes de su codigo de vendedor.
        private async Task CargarVendedoresYClientesAsync(SqlConnection conn)
        {
            bool esAdmin = User.IsInRole("Administrador") || User.IsInRole("Sistemas");

            if (esAdmin)
            {
                ViewBag.PuedeVerTodoCombo = true;
                ViewBag.MiUsuarioId = (int?)null;
                ViewBag.Vendedores = await conn.QueryAsync("SELECT Id, Nombre, VendedorId FROM dbo.UsuarioSQL WHERE EsVendedor = 1 ORDER BY Nombre");
                ViewBag.Clientes = await conn.QueryAsync("SELECT Cliente, Nombrecliente FROM dbo.ClienteSap ORDER BY Nombrecliente");
                return;
            }

            ViewBag.PuedeVerTodoCombo = false;
            var miUsuario = await conn.QueryFirstOrDefaultAsync(
                "SELECT Id, VendedorId FROM dbo.UsuarioSQL WHERE (Usuario = @Login OR Nombre = @Login) AND EsVendedor = 1",
                new { Login = (User.Identity.Name ?? "").Trim() });

            if (miUsuario == null)
            {
                ViewBag.MiUsuarioId = (int?)null;
                ViewBag.Vendedores = new List<dynamic>();
                ViewBag.Clientes = new List<dynamic>();
                return;
            }

            ViewBag.MiUsuarioId = (int)miUsuario.Id;
            ViewBag.Vendedores = await conn.QueryAsync("SELECT Id, Nombre, VendedorId FROM dbo.UsuarioSQL WHERE Id = @Id", new { Id = (int)miUsuario.Id });

            int? vendedorIdUsuario = miUsuario.VendedorId;
            var vendedorIds = DescomponerVendedorId(vendedorIdUsuario);
            if (vendedorIds.Any())
                ViewBag.Clientes = await conn.QueryAsync("SELECT Cliente, Nombrecliente FROM dbo.ClienteSap WHERE VendedorId IN @Ids ORDER BY Nombrecliente", new { Ids = vendedorIds });
            else
                ViewBag.Clientes = new List<dynamic>();
        }

        // Un usuario puede tener varios codigos de vendedor concatenados en bloques de 2 digitos
        // (ej. 1609 = 16 y 09; 190617 = 19, 06 y 17). Devuelve la lista de codigos individuales.
        // Resuelve el Id de UsuarioSQL del usuario logueado (login o nombre).
        // Devuelve null si la sesion no corresponde a un registro de UsuarioSQL.
        private async Task<int?> ObtenerIdUsuarioAsync(SqlConnection conn)
        {
            string login = (User.Identity.Name ?? "").Trim();
            if (string.IsNullOrEmpty(login)) return null;
            return await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT Id FROM dbo.UsuarioSQL WHERE Usuario = @Login OR Nombre = @Login",
                new { Login = login });
        }

        private static List<int> DescomponerVendedorId(int? vendedorId)
        {
            var lista = new List<int>();
            if (vendedorId == null) return lista;

            string txt = vendedorId.Value.ToString();
            if (txt.Length <= 2 || txt.Length % 2 != 0)
            {
                lista.Add(vendedorId.Value);
                return lista;
            }

            for (int i = 0; i < txt.Length; i += 2)
                lista.Add(int.Parse(txt.Substring(i, 2)));

            return lista;
        }

        [HttpGet]
        public async Task<JsonResult> ObtenerClientesPorVendedor(int idVendedor)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Si no hay vendedor seleccionado: admin ve todos los clientes
                    if (idVendedor <= 0)
                    {
                        bool esAdmin = User.IsInRole("Administrador") || User.IsInRole("Sistemas");
                        if (esAdmin)
                        {
                            var todos = await conn.QueryAsync("SELECT Cliente, Nombrecliente FROM dbo.ClienteSap ORDER BY Nombrecliente");
                            return Json(todos);
                        }
                        return Json(new List<dynamic>());
                    }

                    // Clientes del codigo de vendedor del UsuarioSQL seleccionado
                    var vendedorId = await conn.QueryFirstOrDefaultAsync<int?>(
                        "SELECT VendedorId FROM dbo.UsuarioSQL WHERE Id = @Id", new { Id = idVendedor });

                    var vendedorIds = DescomponerVendedorId(vendedorId);
                    if (!vendedorIds.Any())
                        return Json(new List<dynamic>());

                    var clientes = await conn.QueryAsync(
                        "SELECT Cliente, Nombrecliente FROM dbo.ClienteSap WHERE VendedorId IN @Ids ORDER BY Nombrecliente",
                        new { Ids = vendedorIds });

                    return Json(clientes);
                }
            }
            catch
            {
                return Json(new List<dynamic>());
            }
        }

        

        

        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
        public async Task<IActionResult> Cancelar(int id)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    string sql = "UPDATE dbo.Objetivos SET Estado = 'Cancelado' WHERE ID = @Id";
                    await conn.ExecuteAsync(sql, new { Id = id });
                }
                return RedirectToAction("Tablero");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cancelar el objetivo: " + ex.Message);
                return RedirectToAction("Tablero");
            }
        }

        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
        public async Task<IActionResult> Autorizar(int id)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    var aprId = await ObtenerIdUsuarioAsync(conn);
                    string sql = "UPDATE dbo.Objetivos SET Estado = 'Activo', UsuarioID_Aprueba = @UsuarioApr, Fecha_Aprueba = GETDATE() WHERE ID = @Id";
                    await conn.ExecuteAsync(sql, new { Id = id, UsuarioApr = aprId ?? 1 });
                }
                return RedirectToAction("Tablero");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al autorizar el objetivo: " + ex.Message);
                return RedirectToAction("Tablero");
            }
        }

        // =====================================================================
        // AVISO DE VENCIMIENTOS: objetivos Activos que llegan a su Fecha_Hasta
        // =====================================================================

        /// <summary>Dias de anticipacion con los que se anticipa el aviso de vencimiento.</summary>
        private const int DIAS_AVISO_VENCIMIENTO = 30;

        /// <summary>
        /// Lista de objetivos Activos que ya vencieron o que vencen dentro del plazo,
        /// para ofrecer renovarlos o pasarlos a Inactivo.
        /// </summary>
        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "LEER")]
        public async Task<IActionResult> VistaParcialVencimientos(int dias = DIAS_AVISO_VENCIMIENTO)
        {
            if (dias < 1) dias = DIAS_AVISO_VENCIMIENTO;
            if (dias > 365) dias = 365;

            var lista = new List<VencimientoViewModel>();
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            bool esAdmin = User.IsInRole("Administrador") || User.IsInRole("Sistemas");
            int perfilIdUsuario = 0;
            if (!esAdmin) int.TryParse(User.FindFirst("PerfilId")?.Value, out perfilIdUsuario);

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    var p = new DynamicParameters();
                    string wherePerfil = "";
                    if (!esAdmin)
                    {
                        wherePerfil = " AND o.ID_Perfil = @PerfilId";
                        p.Add("PerfilId", perfilIdUsuario);
                    }
                    p.Add("Dias", dias);

                    // DiasRestantes: negativo = ya vencido
                    lista = (await conn.QueryAsync<VencimientoViewModel>(@"
                        SELECT o.ID,
                               p.Nombre AS NombrePerfil,
                               t.Nombre AS NombreTipoObjetivo,
                               o.Descripcion_Objetivo,
                               o.Fecha_Desde,
                               o.Fecha_Hasta,
                               o.Estado,
                               DATEDIFF(DAY, CAST(GETDATE() AS date), o.Fecha_Hasta) AS DiasRestantes,
                               DATEDIFF(DAY, o.Fecha_Desde, ISNULL(o.Fecha_Hasta, o.Fecha_Desde)) AS DuracionDias,
                               ISNULL(uv.Nombre, '') AS NombreVendedor,
                               ISNULL(a.ProductoNombre, '') AS NombreArticulo,
                               (SELECT COUNT(*) FROM dbo.Objetivo_Valor v WHERE v.ID_Objetivo = o.ID) AS TotalMetas
                        FROM dbo.Objetivos o
                        INNER JOIN dbo.Perfiles p ON o.ID_Perfil = p.Id
                        INNER JOIN dbo.Tipo_Objetivo t ON o.ID_Tipo_Objetivo = t.ID
                        LEFT JOIN dbo.ArticuloSap a ON o.SKU = a.ProductoCodigo
                        LEFT JOIN dbo.UsuarioSQL uv ON o.UsuarioID_Vendedor = uv.Id
                        WHERE o.Estado = 'Activo'
                          AND o.Fecha_Hasta IS NOT NULL
                          AND o.Fecha_Hasta <= DATEADD(DAY, @Dias, CAST(GETDATE() AS date))
                          " + wherePerfil + @"
                        ORDER BY o.Fecha_Hasta ASC", p)).ToList();
                }

                ViewBag.DiasAviso = dias;
                return PartialView("_Vencimientos", lista);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar los vencimientos: " + ex.Message);
                return PartialView("_Vencimientos", lista);
            }
        }

        /// <summary>
        /// Crea un nuevo objetivo copiando el vigente pero con otras fechas y estado Pendiente.
        /// </summary>
        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> RenovarObjetivo(int id, DateTime? nuevaDesde, DateTime? nuevaHasta)
        {
            if (!nuevaDesde.HasValue || !nuevaHasta.HasValue)
                return Json(new { success = false, message = "Indica las fechas del nuevo periodo." });

            DateTime dDesde = nuevaDesde.Value.Date;
            DateTime dHasta = nuevaHasta.Value.Date;

            if (dHasta < dDesde)
                return Json(new { success = false, message = "La fecha final no puede ser anterior a la inicial." });

            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    var origen = await conn.QueryFirstOrDefaultAsync(@"
                        SELECT ID, ID_Perfil, ID_Tipo_Objetivo, Tipo_Periodo_Cumplimiento,
                               Proveedor, ID_Cliente, UsuarioID_Vendedor, SKU, CC, LINEA,
                               Descripcion_Objetivo, Estado
                        FROM dbo.Objetivos WHERE ID = @Id", new { Id = id });

                    if (origen == null)
                        return Json(new { success = false, message = "El objetivo ya no existe." });

                    if (!string.Equals(origen.Estado?.ToString(), "Activo", StringComparison.OrdinalIgnoreCase))
                        return Json(new { success = false, message = "Solo se pueden renovar objetivos en estado Activo." });

                    int usuarioId = await ObtenerIdUsuarioAsync(conn) ?? 1;

                    using (var trx = conn.BeginTransaction())
                    {
                        try
                        {
                            // Clave natural: se evalua con las fechas nuevas para no duplicar
                            string clave = string.Join("|",
                                origen.ID_Perfil.ToString(),
                                origen.ID_Tipo_Objetivo.ToString(),
                                (origen.Tipo_Periodo_Cumplimiento?.ToString() ?? "").Trim().ToUpperInvariant(),
                                dDesde.ToString("yyyy-MM-dd"),
                                dHasta.ToString("yyyy-MM-dd"),
                                (origen.Proveedor?.ToString() ?? "").Trim().ToUpperInvariant(),
                                (origen.ID_Cliente?.ToString() ?? "").Trim().ToUpperInvariant(),
                                (origen.UsuarioID_Vendedor?.ToString() ?? "0"),
                                (origen.SKU?.ToString() ?? "").Trim().ToUpperInvariant(),
                                (origen.CC?.ToString() ?? "").Trim().ToUpperInvariant(),
                                (origen.LINEA?.ToString() ?? "").Trim().ToUpperInvariant());

                            var posibles = await conn.QueryAsync(@"
                                SELECT ID, ID_Perfil, ID_Tipo_Objetivo, Tipo_Periodo_Cumplimiento,
                                       Fecha_Desde, Fecha_Hasta, Proveedor, ID_Cliente, UsuarioID_Vendedor,
                                       SKU, CC, LINEA
                                FROM dbo.Objetivos", transaction: trx);

                            foreach (var e in posibles)
                            {
                                string k = string.Join("|",
                                    e.ID_Perfil.ToString(),
                                    e.ID_Tipo_Objetivo.ToString(),
                                    (e.Tipo_Periodo_Cumplimiento?.ToString() ?? "").Trim().ToUpperInvariant(),
                                    (e.Fecha_Desde is DateTime fd ? fd.ToString("yyyy-MM-dd") : ""),
                                    (e.Fecha_Hasta is DateTime fh ? fh.ToString("yyyy-MM-dd") : "19000101"),
                                    (e.Proveedor?.ToString() ?? "").Trim().ToUpperInvariant(),
                                    (e.ID_Cliente?.ToString() ?? "").Trim().ToUpperInvariant(),
                                    (e.UsuarioID_Vendedor?.ToString() ?? "0"),
                                    (e.SKU?.ToString() ?? "").Trim().ToUpperInvariant(),
                                    (e.CC?.ToString() ?? "").Trim().ToUpperInvariant(),
                                    (e.LINEA?.ToString() ?? "").Trim().ToUpperInvariant());

                                if (string.Equals(k, clave, StringComparison.OrdinalIgnoreCase))
                                {
                                    trx.Rollback();
                                    return Json(new { success = false, message = $"Ya existe un objetivo con esas fechas (folio #{e.ID})." });
                                }
                            }

                            int nuevoId = await conn.ExecuteScalarAsync<int>(@"
                                INSERT INTO dbo.Objetivos (
                                    ID_Perfil, ID_Tipo_Objetivo, Tipo_Periodo_Cumplimiento,
                                    Fecha_Desde, Fecha_Hasta, Proveedor, ID_Cliente, UsuarioID_Vendedor,
                                    SKU, CC, LINEA, Descripcion_Objetivo, Estado,
                                    UsuarioID_Creacion, Fecha_Creacion)
                                VALUES (
                                    @Perfil, @Tipo, @Periodo,
                                    @Desde, @Hasta, @Proveedor, @Cliente, @Vendedor,
                                    @SKU, @CC, @Linea, @Descripcion, 'Pendiente',
                                    @Usuario, GETDATE());
                                SELECT CAST(SCOPE_IDENTITY() AS int);",
                                new
                                {
                                    Perfil = origen.ID_Perfil,
                                    Tipo = origen.ID_Tipo_Objetivo,
                                    Periodo = origen.Tipo_Periodo_Cumplimiento,
                                    Desde = dDesde,
                                    Hasta = dHasta,
                                    Proveedor = origen.Proveedor,
                                    Cliente = origen.ID_Cliente,
                                    Vendedor = origen.UsuarioID_Vendedor,
                                    SKU = origen.SKU,
                                    CC = origen.CC,
                                    Linea = origen.LINEA,
                                    Descripcion = origen.Descripcion_Objetivo,
                                    Usuario = usuarioId
                                }, trx);

                            // Se copian las metas tal cual; el usuario las ajusta si lo necesita
                            await conn.ExecuteAsync(@"
                                INSERT INTO dbo.Objetivo_Valor
                                    (ID_Objetivo, Tipo_Valor, Unidad_Medida, Valor_Minimo,
                                     Valor_Maximo, Valor_Objetivo, ID_Catalogo_ValorObjetivo)
                                SELECT @NuevoId, Tipo_Valor, Unidad_Medida, Valor_Minimo,
                                       Valor_Maximo, Valor_Objetivo, ID_Catalogo_ValorObjetivo
                                FROM dbo.Objetivo_Valor
                                WHERE ID_Objetivo = @IdOrigen",
                                new { NuevoId = nuevoId, IdOrigen = id }, trx);

                            trx.Commit();

                            return Json(new { success = true, nuevoId, message = $"Nuevo objetivo #{nuevoId} creado en estado Pendiente." });
                        }
                        catch (Exception ex)
                        {
                            trx.Rollback();
                            return Json(new { success = false, message = "No se pudo crear el nuevo objetivo: " + ex.Message });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al renovar: " + ex.Message });
            }
        }

        /// <summary>Marca un objetivo como Inactivo cuando su periodo ya concluyo.</summary>
        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> MarcarInactivo(int id)
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    int usuarioId = await ObtenerIdUsuarioAsync(conn) ?? 1;

                    int filas = await conn.ExecuteAsync(@"
                        UPDATE dbo.Objetivos
                        SET Estado = 'Inactivo',
                            UsuarioID_Aprueba = NULL,
                            Fecha_Aprueba = NULL,
                            UsuarioID_Modificacion = @Usuario,
                            Fecha_Modificacion = GETDATE()
                        WHERE ID = @Id AND Estado = 'Activo'",
                        new { Id = id, Usuario = usuarioId });

                    if (filas == 0)
                        return Json(new { success = false, message = "El objetivo no existe o no está Activo." });

                    return Json(new { success = true, message = "Objetivo marcado como Inactivo." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al marcar Inactivo: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> ObtenerKpisPorArea(int idPerfil)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    string sql;

                    // Validar si es admin
                    if (idPerfil == 1)
                    {
                        sql = "SELECT ID as Valor, Nombre as Texto, Campos_Visibles as Campos FROM dbo.Tipo_Objetivo WHERE Activo = 1";
                        var kpisTodos = await conn.QueryAsync(sql);
                        return Json(kpisTodos);
                    }
                    else
                    {
                        sql = "SELECT ID as Valor, Nombre as Texto, Campos_Visibles as Campos FROM dbo.Tipo_Objetivo WHERE Activo = 1 AND ID_Perfil = @IdPerfil";
                        var kpisFiltrados = await conn.QueryAsync(sql, new { IdPerfil = idPerfil });
                        return Json(kpisFiltrados);
                    }
                }
            }
            catch
            {
                return Json(new List<dynamic>());
            }
        }

        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
        public async Task<JsonResult> ObtenerTiposConfiguracion()
        {
            try
            {
                bool esAdmin = User.IsInRole("Administrador") || User.IsInRole("sistemas");
                int.TryParse(User.FindFirst("PerfilId")?.Value, out int miPerfilId);

                string sql = @"
                    SELECT t.ID, t.Nombre, t.Descripcion, t.Activo, t.ID_Perfil,
                        p.Nombre AS NombrePerfil,
                        t.Campos_Visibles AS Campos,
                        (SELECT COUNT(1) FROM dbo.ArticuloRelevanteParaObjetivo r
                         WHERE r.ID_TipoObjetivo = t.ID) AS SkusConfigurados
                    FROM dbo.Tipo_Objetivo t
                    LEFT JOIN dbo.Perfiles p ON p.Id = t.ID_Perfil";

                if (!esAdmin)
                    sql += " WHERE t.ID_Perfil = @IdPerfil";

                sql += " ORDER BY p.Nombre, t.Nombre";

                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    var tipos = await conn.QueryAsync(sql, new { IdPerfil = miPerfilId });
                    return Json(tipos);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }

        }

        //Controlador para guardar la configuración de campos visibles por tipo de objetivo
        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
        public async Task<JsonResult> GuardarConfiguracionTipo(
            int id,
            string nombre,
            string descripcion,
            bool activo,
            string campos)
        {
            try
            {
                nombre = (nombre ?? "").Trim();
                descripcion = (descripcion ?? "").Trim();

                if (id <= 0)
                    return Json(new { success = false, message = "No se identificó el tipo de objetivo." });
                if (nombre.Length == 0 || nombre.Length > 150)
                    return Json(new { success = false, message = "El nombre es obligatorio (máximo 150 caracteres)." });
                if (descripcion.Length > 500)
                    return Json(new { success = false, message = "La descripcion no puede pasar de 500 caracteres." });

                // Solo se aceptan los campos conocidos
                var permitidos = new[] { "Cliente", "Vendedor", "Proveedor", "SKU", "CC", "Linea" };
                var seleccion = (campos ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                var validos = permitidos.Where(p => seleccion.Contains(p)).ToList();

                //Todos marcados = sin configurar (NULL). Ninguno marcado = "NINGUNO"
                string camposGuardar =
                    validos.Count == permitidos.Length ? null :
                    validos.Count == 0 ? "NINGUNO" :
                    string.Join(",", validos);

                bool esAdmin = User.IsInRole("Administrador") || User.IsInRole("Sistemas");
                int.TryParse(User.FindFirst("PerfilID")?.Value, out int miPerfilId);

                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    var tipo = await conn.QueryFirstOrDefaultAsync(
                        "SELECT ID_Perfil FROM dbo.Tipo_Objetivo WHERE Id = @id",
                    new { Id = id });

                    if (tipo == null)
                        return Json(new { success = false, message = "El tipo de objetivo no existe." });

                    int? idPerfilTipo = tipo.ID_Perfil;

                    if (!esAdmin && idPerfilTipo != miPerfilId)
                        return Json(new { success = false, message = "No puedes modificar tipos de otro perfil." });

                    int duplicados = await conn.ExecuteScalarAsync<int>(@"
                        SELECT COUNT(1) FROM dbo.Tipo_Objetivo
                        WHERE Nombre = @Nombre
                            AND ISNULL(ID_Perfil, 0) = ISNULL(@IdPerfil, 0)
                            AND ID <> @Id",
                        new { Nombre = nombre, IdPerfil = idPerfilTipo, Id = id });

                    if (duplicados > 0)
                        return Json(new { success = false, message = "Ya existe un tipo con ese nombre en el mismo perfil" });

                    await conn.ExecuteAsync(@"
                        UPDATE dbo.Tipo_Objetivo
                        SET Nombre = @Nombre,
                            Descripcion = @Descripcion,
                            Activo = @Activo,
                            Campos_Visibles = @Campos
                        WHERE ID = @Id",
                        new
                        {
                            Nombre = nombre,
                            Descripcion = descripcion.Length == 0 ? null : descripcion,
                            Activo = activo,
                            Campos = camposGuardar,
                            Id = id
                        });

                    return Json(new { success = true, nombre = nombre, activo = activo, campos = camposGuardar });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Lee los SKU configurados como relevantes para un tipo de objetivo
        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "LEER")]
        public async Task<JsonResult> ObtenerArticulosRelevantes(int idTipo)
        {
            try
            {
                if (idTipo <= 0)
                    return Json(new { success = false, message = "No se identificó el tipo de objetivo." });

                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    var skus = (await conn.QueryAsync<string>(@"
                        SELECT ProductoCodigo
                        FROM dbo.ArticuloRelevanteParaObjetivo
                        WHERE ID_TipoObjetivo = @idTipo
                        ORDER BY ProductoCodigo",
                        new { idTipo })).ToList();

                    return Json(new { success = true, skus });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Guarda (reemplaza) la selección de SKU relevantes de un tipo de objetivo.
        // Lista vacía = sin restricción (se muestran todos los SKU).
        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
        public async Task<JsonResult> GuardarArticulosRelevantes(int idTipo, string skus)
        {
            try
            {
                if (idTipo <= 0)
                    return Json(new { success = false, message = "No se identificó el tipo de objetivo." });

                var lista = (skus ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                bool esAdmin = User.IsInRole("Administrador") || User.IsInRole("Sistemas");
                int.TryParse(User.FindFirst("PerfilID")?.Value, out int miPerfilId);

                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    var tipo = await conn.QueryFirstOrDefaultAsync(
                        "SELECT ID_Perfil FROM dbo.Tipo_Objetivo WHERE ID = @idTipo",
                        new { idTipo });

                    if (tipo == null)
                        return Json(new { success = false, message = "El tipo de objetivo no existe." });

                    int? idPerfilTipo = tipo.ID_Perfil;
                    if (!esAdmin && idPerfilTipo != miPerfilId)
                        return Json(new { success = false, message = "No puedes modificar tipos de otro perfil." });

                    using (var trx = conn.BeginTransaction())
                    {
                        await conn.ExecuteAsync(
                            "DELETE FROM dbo.ArticuloRelevanteParaObjetivo WHERE ID_TipoObjetivo = @idTipo",
                            new { idTipo }, trx);

                        int insertados = 0;
                        if (lista.Count > 0)
                        {
                            insertados = await conn.ExecuteAsync(@"
                                INSERT INTO dbo.ArticuloRelevanteParaObjetivo (ID_TipoObjetivo, ProductoCodigo)
                                SELECT @idTipo, a.ProductoCodigo
                                FROM dbo.ArticuloSap a
                                WHERE a.ProductoCodigo IN @skus",
                                new { idTipo, skus = lista }, trx);
                        }

                        trx.Commit();
                        return Json(new { success = true, total = insertados });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }




        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
       
        public async Task<IActionResult> Editar(ObjetivoViewModel modelo)
        {
            // Marca el paso actual para saber que operacion fallo si hay error
            string paso = "inicio";
            try
            {

                // Si algo no se enlazo bien (por ejemplo un numero de meta mal capturado)
                // se avisa claro en vez de dejar que reviente una sentencia SQL.
                if (!ModelState.IsValid)
                {
                    string primerError = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .FirstOrDefault() ?? "datos no validos";

                    TempData["ErrorObjetivos"] = "Error al validar el formulario: " + primerError;
                    return RedirigirALista(modelo);
                }

                if (modelo == null || modelo.ID <= 0)
                {
                    TempData["ErrorObjetivos"] = "No se identifico el objetivo a editar.";
                    return RedirigirALista(modelo);
                }

                paso = "abriendo conexion";
                string connectionString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    paso = "resolviendo usuario";
                    int usuarioModId = await ObtenerIdUsuarioAsync(conn) ?? 1;

            


                    if (modelo.ValoresConfigurados == null || !modelo.ValoresConfigurados.Any())
                    {
                        TempData["ErrorObjetivos"] = "El objetivo debe tener al menos un valor configurado.";
                        return RedirectToAction("Tablero");
                    }

                    var estadosValidos = new[] { "Pendiente", "Activo", "Inactivo", "Cancelado" };

                    string estadoActual = await conn.ExecuteScalarAsync<string>(
                        "SELECT Estado FROM dbo.Objetivos WHERE ID = @ID", new { ID = modelo.ID }) ?? "Pendiente";

                    string nuevoEstado = estadosValidos.Contains(modelo.Estado?.Trim() ?? "")
                        ? modelo.Estado.Trim()
                        : estadoActual;


                    // Tipo_Periodo_Cumplimiento es NOT NULL: si el select llega vacio se conserva el actual
                    string periodo = (modelo.Tipo_Periodo_Cumplimiento ?? "").Trim();

                    if (string.IsNullOrEmpty(periodo))
                    {
                        periodo = (await conn.ExecuteScalarAsync<string>(
                            "SELECT Tipo_Periodo_Cumplimiento FROM dbo.Objetivos WHERE ID = @ID",
                            new { ID = modelo.ID }))?.Trim() ?? "Mensual";
                    }


                    using (var transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            // Actualizar cabecera (todo excepto Perfil y Tipo de Objetivo)
                            paso = "actualizando cabecera";
                            string sqlObjetivo = @"
                                UPDATE dbo.Objetivos 
                                SET Fecha_Desde = @FechaDesde,
                                    Fecha_Hasta = @FechaHasta,
                                    Tipo_Periodo_Cumplimiento = @Periodo,
                                    Estado = @Estado,
                                    ID_Cliente = @ID_Cliente,
                                    UsuarioID_Vendedor = @UsuarioID_Vendedor,
                                    Proveedor = @Proveedor,
                                    SKU = @SKU,
                                    CC = @CC,
                                    LINEA = @Linea,
                                    Descripcion_Objetivo = @Descripcion,
                                    -- Al pasar a Activo se registra la aprobacion; al salir de Activo se limpia.
                                    -- Si ya estaba Activo se conservan usuario y fecha originales.
                                    UsuarioID_Aprueba = CASE
                                        WHEN @Estado <> 'Activo' THEN NULL
                                        WHEN Estado = 'Activo' AND UsuarioID_Aprueba IS NOT NULL THEN UsuarioID_Aprueba
                                        ELSE @UsuarioMod END,
                                    Fecha_Aprueba = CASE
                                        WHEN @Estado <> 'Activo' THEN NULL
                                        WHEN Estado = 'Activo' AND Fecha_Aprueba IS NOT NULL THEN Fecha_Aprueba
                                        ELSE GETDATE() END,
                                    UsuarioID_Modificacion = @UsuarioMod, 
                                    Fecha_Modificacion = GETDATE()
                                WHERE ID = @ID";

                            await conn.ExecuteAsync(sqlObjetivo, new
                            {
                                FechaDesde = modelo.Fecha_Desde,
                                FechaHasta = modelo.Fecha_Hasta,
                                Periodo = periodo,
                                Estado = nuevoEstado,
                                ID_Cliente = modelo.ID_Cliente,
                                UsuarioID_Vendedor = modelo.UsuarioID_Vendedor,
                                Proveedor = modelo.Proveedor,
                                SKU = modelo.SKU,
                                CC = modelo.CC,
                                Linea = modelo.Linea,
                                Descripcion = modelo.Descripcion_Objetivo,
                                UsuarioMod = usuarioModId,
                                ID = modelo.ID
                            }, transaccion);

                            // Detalles: insertar nuevos, actualizar existentes y eliminar los que ya no vienen
                            var idsEnviados = new List<int>();
                            if (modelo.ValoresConfigurados != null && modelo.ValoresConfigurados.Any())
                            {
                                // IDs que ya existen en BD para este objetivo
                                paso = "leyendo metas existentes";
                                var idsExistentes = (await conn.QueryAsync<int>(
                                    "SELECT ID FROM dbo.Objetivo_Valor WHERE ID_Objetivo = @IDObjetivo",
                                    new { IDObjetivo = modelo.ID }, transaccion)).ToList();

                                string sqlValor = @"
                                    UPDATE dbo.Objetivo_Valor 
                                    SET ID_Catalogo_ValorObjetivo = @ID_Catalogo_ValorObjetivo,
                                        Tipo_Valor = @TipoValor,
                                        Unidad_Medida = @Unidad,
                                        Valor_Minimo = @Minimo,
                                        Valor_Maximo = @Maximo,
                                        Valor_Objetivo = @Objetivo
                                    WHERE ID = @IDValor";

                                string sqlNuevo = @"
                                    INSERT INTO dbo.Objetivo_Valor 
                                        (ID_Objetivo, ID_Catalogo_ValorObjetivo, Tipo_Valor, Unidad_Medida, Valor_Minimo, Valor_Maximo, Valor_Objetivo)
                                    VALUES
                                        (@IDObjetivo, @ID_Catalogo_ValorObjetivo, @TipoValor, @Unidad, @Minimo, @Maximo, @Objetivo)";

                                foreach (var val in modelo.ValoresConfigurados)
                                {
                                    if (val.ID > 0)
                                    {
                                        paso = "actualizando meta " + val.ID;
                                        idsEnviados.Add(val.ID);
                                        await conn.ExecuteAsync(sqlValor, new
                                        {
                                            ID_Catalogo_ValorObjetivo = val.ID_Catalogo_ValorObjetivo,
                                            TipoValor = val.Tipo_Valor ?? "",
                                            Unidad = val.Unidad_Medida ?? "",
                                            Minimo = val.Valor_Minimo,
                                            Maximo = val.Valor_Maximo,
                                            Objetivo = val.Valor_Objetivo,
                                            IDValor = val.ID
                                        }, transaccion);
                                    }
                                    else
                                    {
                                        paso = "insertando meta nueva";
                                        await conn.ExecuteAsync(sqlNuevo, new
                                        {
                                            IDObjetivo = modelo.ID,
                                            ID_Catalogo_ValorObjetivo = val.ID_Catalogo_ValorObjetivo,
                                            TipoValor = val.Tipo_Valor ?? "",
                                            Unidad = val.Unidad_Medida ?? "",
                                            Minimo = val.Valor_Minimo,
                                            Maximo = val.Valor_Maximo,
                                            Objetivo = val.Valor_Objetivo
                                        }, transaccion);
                                    }
                                }

                                // Eliminar solo las metas que existian antes y ya no vienen en el formulario
                                var idsAEliminar = idsExistentes.Except(idsEnviados).ToList();
                                if (idsAEliminar.Any())
                                {
                                    paso = "eliminando metas sobrantes";
                                    await conn.ExecuteAsync("DELETE FROM dbo.Objetivo_Valor WHERE ID IN @Ids",
                                        new { Ids = idsAEliminar }, transaccion);
                                }
                            }
                            else
                            {
                                // No viene ninguna meta de la forma -> eliminar todas las existentes
                                paso = "eliminando todas las metas";
                                await conn.ExecuteAsync("DELETE FROM dbo.Objetivo_Valor WHERE ID_Objetivo = @IDObjetivo",
                                    new { IDObjetivo = modelo.ID }, transaccion);
                            }

                            paso = "confirmando transaccion";
                            transaccion.Commit();
                        }
                        catch
                        {
                            transaccion.Rollback();
                            throw;
                        }
                    }
                }

                TempData["OkObjetivos"] = "Objetivo actualizado correctamente.";
                return RedirigirALista(modelo);
            }
            catch (Exception ex)
            {
                string detalle = ex.Message;
                // Si es error de SQL se agrega el numero para localizarlo
                if (ex is SqlException sqlEx)
                    detalle = $"SQL {sqlEx.Number}: {sqlEx.Message}";

                TempData["ErrorObjetivos"] = $"Error al editar ({paso}): {detalle}";
                return RedirigirALista(modelo);
            }
        }

        /// <summary>
        /// Vuelve al listado conservando los filtros y la pagina desde la que se edito.
        /// Sin esto el guardado se perdia de vista y parecia que no se habia guardado.
        /// </summary>
        private IActionResult RedirigirALista(ObjetivoViewModel modelo)
        {
            string returnUrl = (modelo?.URLRetorno ?? "").Trim();

            // Solo se admite una URL local para evitar redirecciones abiertas
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Tablero");
        }

        // ================================================================
        //  CARGA MASIVA DE OBJETIVOS 
        // ================================================================

        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "LEER")]
        public async Task<IActionResult> DescargarPlantillaObjetivos()
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            var perfiles = new List<dynamic>();
            var tipos = new List<dynamic>();
            var vendedores = new List<dynamic>();
            var valores = new List<dynamic>();

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                perfiles = (await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles ORDER BY Nombre")).ToList();
                tipos = (await conn.QueryAsync("SELECT ID, Nombre FROM dbo.Tipo_Objetivo WHERE Activo = 1 ORDER BY Nombre")).ToList();
                vendedores = (await conn.QueryAsync("SELECT Id, Usuario, Nombre FROM dbo.UsuarioSQL WHERE EsVendedor = 1 ORDER BY Nombre")).ToList();
                valores = (await conn.QueryAsync("SELECT ID, Nombre, Unidad_Medida FROM dbo.Catalogo_ValorObjetivo WHERE Activo = 1 ORDER BY Nombre")).ToList();
            }

            string[] headers = {
                "Perfil", "Tipo de Objetivo", "Periodo",
                "Fecha Desde", "Fecha Hasta",
                "Vendedor", "Cliente", "SKU", "CC", "Linea", "Proveedor",
                "Descripcion",
                "Valor (ID_Catalogo_ValorObjetivo o nombre)", "Valor Minimo", "Valor Maximo", "Meta"
            };

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Plantilla");

            for (int c = 0; c < headers.Length; c++)
            {
                var cel = ws.Cell(1, c + 1);
                cel.Value = headers[c];
                cel.Style.Font.Bold = true;
                cel.Style.Fill.BackgroundColor = XLColor.FromHtml("#B30000");
                cel.Style.Font.FontColor = XLColor.White;
            }
            ws.SheetView.FreezeRows(1);

            // Hoja de catalogos para listas desplegables
            var cat = wb.Worksheets.Add("Catalogos");
            cat.Cell(1, 1).Value = "Perfiles";
            cat.Cell(1, 2).Value = "Tipo de Objetivo";
            cat.Cell(1, 3).Value = "Vendedores (Usuario - Nombre)";
            cat.Cell(1, 4).Value = "Valores";
            int filaCat = 2;
            foreach (var p in perfiles) cat.Cell(filaCat++, 1).Value = (string)p.Nombre;
            filaCat = 2;
            foreach (var t in tipos) cat.Cell(filaCat++, 2).Value = (string)t.Nombre;
            filaCat = 2;
            foreach (var v in vendedores) cat.Cell(filaCat++, 3).Value = ((string)v.Usuario) + " - " + ((string)v.Nombre);
            filaCat = 2;
            foreach (var val in valores) cat.Cell(filaCat++, 4).Value = ((string)val.Nombre) + " (" + ((string)val.Unidad_Medida) + ")";
            cat.Columns(1, 4).AdjustToContents();

            const int maxFila = 500;
            ws.Range("A2:A" + maxFila).SetDataValidation().List("'Catalogos'!$A$2:$A$500");
            ws.Range("B2:B" + maxFila).SetDataValidation().List("'Catalogos'!$B$2:$B$500");
            ws.Range("F2:F" + maxFila).SetDataValidation().List("'Catalogos'!$C$2:$C$500");
            ws.Range("M2:M" + maxFila).SetDataValidation().List("'Catalogos'!$D$2:$D$500");

            // Fila de ejemplo
            string[] ejemplo = {
                "Ventas", "Entregas a Tiempo (OTIF)", "Mensual",
                new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("dd/MM/yyyy"),
                new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month)).ToString("dd/MM/yyyy"),
                "jperez - Juan Perez", "1001", "SKU0001", "CC01", "Linea A", "Proveedor X",
                "Entregar a tiempo a los clientes",
                "Peso por Caja (KG)", "10", "20", "15"
            };
            for (int c = 0; c < ejemplo.Length; c++) ws.Cell(2, c + 1).Value = ejemplo[c];
            ws.Cell(2, 4).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(2, 5).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Columns(1, headers.Length).AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Plantilla_Objetivos.xlsx");
        }

        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ESCRIBIR")]
        public async Task<JsonResult> CargarMasivoObjetivos(IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
                return Json(new { ok = false, mensaje = "Selecciona un archivo Excel (.xlsx)." });

            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            var errores = new List<string>();
            int insertados = 0, actualizados = 0, omitidos = 0;

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Catalogos de resolucion
                    var perfiles = (await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles")).ToList();
                    var tipos = (await conn.QueryAsync("SELECT ID, Nombre FROM dbo.Tipo_Objetivo WHERE Activo = 1")).ToList();
                    var vendedores = (await conn.QueryAsync("SELECT Id, Usuario, Nombre FROM dbo.UsuarioSQL")).ToList();
                    var valores = (await conn.QueryAsync("SELECT ID, Nombre, Unidad_Medida FROM dbo.Catalogo_ValorObjetivo WHERE Activo = 1")).ToList();

                    // Catalogos para validar las FKs (ClienteSap / ArticuloSap): si el valor
                    // del Excel no existe, se guarda NULL en vez de romper el INSERT.
                    var dictClientesValidos = new HashSet<string>(
                        (await conn.QueryAsync<string>("SELECT Cliente FROM dbo.ClienteSap"))
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Select(c => c.Trim()),
                        StringComparer.OrdinalIgnoreCase);
                    var dictSkusValidos = new HashSet<string>(
                        (await conn.QueryAsync<string>("SELECT ProductoCodigo FROM dbo.ArticuloSap"))
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s.Trim()),
                        StringComparer.OrdinalIgnoreCase);

                    var dictPerfil = perfiles
                        .GroupBy(x => ((string)x.Nombre).Trim().ToUpperInvariant())
                        .ToDictionary(g => g.Key, g => (int)g.First().Id);
                    var dictTipo = tipos
                        .GroupBy(x => ((string)x.Nombre).Trim().ToUpperInvariant())
                        .ToDictionary(g => g.Key, g => (int)g.First().ID);
                    var dictVendedor = vendedores
                        .GroupBy(x => ((string)x.Usuario).Trim().ToUpperInvariant())
                        .ToDictionary(g => g.Key, g => (int)g.First().Id);
                    var dictVendedorNombre = vendedores
                        .Where(x => !string.IsNullOrEmpty((string)x.Nombre?.ToString()) && !string.IsNullOrWhiteSpace(x.Nombre?.ToString()))
                        .GroupBy(x => ((string)x.Nombre).Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => (int)g.First().Id, StringComparer.OrdinalIgnoreCase);
                    var dictValor = valores.ToDictionary(
                        x => ((string)x.Nombre).Trim() + " (" + ((string)x.Unidad_Medida) + ")",
                        x => new { ID = (int)x.ID, Nombre = (string)x.Nombre, Unidad = (string)x.Unidad_Medida });
                    var dictValorPorId = valores.ToDictionary(x => (int)x.ID, x => new { ID = (int)x.ID, Nombre = (string)x.Nombre, Unidad = (string)x.Unidad_Medida });

                    if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                        return Json(new { ok = false, mensaje = "Solo se aceptan archivos .xlsx." });

                    using var ms = new MemoryStream();
                    await archivo.CopyToAsync(ms);
                    ms.Position = 0;

                    using var wb = new XLWorkbook(ms);
                    var ws = wb.Worksheet(1);
                    var ultimaFilaUsada = ws.LastRowUsed()?.RowNumber() ?? 0;
                    if (ultimaFilaUsada < 2)
                        return Json(new { ok = false, mensaje = "El archivo no contiene filas de datos." });

                    // Mapeo de columnas por nombre de encabezado
                    var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    var filaEncabezado = ws.Row(1);
                    for (int c = 1; c <= ws.LastColumnUsed()?.ColumnNumber(); c++)
                    {
                        string nombre = (filaEncabezado.Cell(c).GetString() ?? "").Trim();
                        if (!string.IsNullOrEmpty(nombre) && !cols.ContainsKey(nombre))
                            cols[nombre] = c;
                    }
                    int Col(string nombre) => cols.TryGetValue(nombre, out var c) ? c : -1;

                    var colPerfil = Col("Perfil");
                    var colTipo = Col("Tipo de Objetivo");
                    var colPeriodo = Col("Periodo");
                    var colDesde = Col("Fecha Desde");
                    var colHasta = Col("Fecha Hasta");
                    var colVendedor = Col("Vendedor");
                    var colCliente = Col("Cliente");
                    var colSku = Col("SKU");
                    var colCc = Col("CC");
                    var colLinea = Col("Linea");
                    var colProveedor = Col("Proveedor");
                    var colDescripcion = Col("Descripcion");
                    var colValor = Col("Valor (ID_Catalogo_ValorObjetivo o nombre)");
                    var colMin = Col("Valor Minimo");
                    var colMax = Col("Valor Maximo");
                    var colMeta = Col("Meta");

                    if (colPerfil < 0 || colTipo < 0 || colPeriodo < 0 || colDesde < 0)
                        return Json(new { ok = false, mensaje = "La plantilla no tiene las columnas requeridas (Perfil, Tipo de Objetivo, Periodo, Fecha Desde)." });

                    int? idUsuario = await ObtenerIdUsuarioAsync(conn);

                    string Texto(int fila, int col) => col < 0 ? "" : ((ws.Cell(fila, col).GetString()) ?? "").Trim();
                    DateTime? Fecha(int fila, int col)
                    {
                        if (col < 0) return null;
                        var celda = ws.Cell(fila, col);
                        if (celda.IsEmpty()) return null;
                        if (celda.DataType == XLDataType.DateTime) return celda.GetDateTime();
                        string s = celda.GetString().Trim();
                        if (string.IsNullOrEmpty(s)) return null;
                        if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out var d)) return d;
                        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d2;
                        return null;
                    }
                    decimal? DecimalCelda(int fila, int col)
                    {
                        if (col < 0) return null;
                        var celda = ws.Cell(fila, col);
                        if (celda.IsEmpty()) return null;
                        if (celda.DataType == XLDataType.Number) return (decimal)celda.GetDouble();
                        string s = celda.GetString().Trim();
                        if (string.IsNullOrEmpty(s)) return null;
                        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("es-MX"), out var d)) return d;
                        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d2)) return d2;
                        return null;
                    }

                    // Agrupar por clave natural (todo excepto metas y estado)
                    var grupos = new Dictionary<string, List<ObjetivoCargaRow>>();
                    for (int fila = 2; fila <= ultimaFilaUsada; fila++)
                    {
                        string perfilN = Texto(fila, colPerfil);
                        string tipoN = Texto(fila, colTipo);
                        if (string.IsNullOrEmpty(perfilN) && string.IsNullOrEmpty(tipoN))
                            continue;

                        string clave = string.Join("|", new[] {
                            perfilN.ToUpperInvariant(), tipoN.ToUpperInvariant(), Texto(fila, colPeriodo).ToUpperInvariant(),
                            Fecha(fila, colDesde)?.ToString("yyyy-MM-dd") ?? "",
                            Fecha(fila, colHasta)?.ToString("yyyy-MM-dd") ?? "",
                            Texto(fila, colVendedor).Trim().ToUpperInvariant(),
                            Texto(fila, colCliente).Trim().ToUpperInvariant(),
                            Texto(fila, colSku).Trim().ToUpperInvariant(),
                            Texto(fila, colCc).Trim().ToUpperInvariant(),
                            Texto(fila, colLinea).Trim().ToUpperInvariant(),
                            Texto(fila, colProveedor).Trim().ToUpperInvariant()
                        });

                        var row = new ObjetivoCargaRow();
                        row.Fila = fila;
                        row.PerfilNombre = perfilN;
                        row.TipoNombre = tipoN;
                        row.Periodo = Texto(fila, colPeriodo);
                        row.FechaDesde = Fecha(fila, colDesde);
                        row.FechaHasta = Fecha(fila, colHasta);
                        row.VendedorTexto = Texto(fila, colVendedor);
                        row.Cliente = Texto(fila, colCliente);
                        row.Sku = Texto(fila, colSku);
                        row.Cc = Texto(fila, colCc);
                        row.Linea = Texto(fila, colLinea);
                        row.Proveedor = Texto(fila, colProveedor);
                        row.Descripcion = Texto(fila, colDescripcion);
                        row.ValorTexto = Texto(fila, colValor);
                        row.ValorMinimo = DecimalCelda(fila, colMin);
                        row.ValorMaximo = DecimalCelda(fila, colMax);
                        row.Meta = DecimalCelda(fila, colMeta);

                        if (!grupos.TryGetValue(clave, out var lista))
                        {
                            lista = new List<ObjetivoCargaRow>();
                            grupos[clave] = lista;
                        }
                        lista.Add(row);
                    }

                    // Cache en memoria de los objetivos existentes: evita una consulta a la BD
                    // por cada grupo (con miles de filas son miles de round-trips).
                    var cacheExistentes = new Dictionary<string, (int ID, string Estado)>(StringComparer.OrdinalIgnoreCase);
                    var existentesRaw = (await conn.QueryAsync(@"
                        SELECT ID, Estado, ID_Perfil, ID_Tipo_Objetivo, Tipo_Periodo_Cumplimiento,
                               Fecha_Desde, Fecha_Hasta, Proveedor, ID_Cliente, UsuarioID_Vendedor,
                               SKU, CC, LINEA
                        FROM dbo.Objetivos")).ToList();
                    foreach (var e in existentesRaw)
                    {
                        string k = string.Join("|",
                            e.ID_Perfil.ToString(),
                            e.ID_Tipo_Objetivo.ToString(),
                            (e.Tipo_Periodo_Cumplimiento?.ToString() ?? "").Trim().ToUpperInvariant(),
                            (e.Fecha_Desde is DateTime fd ? fd.ToString("yyyy-MM-dd") : ""),
                            (e.Fecha_Hasta is DateTime fh ? fh.ToString("yyyy-MM-dd") : "19000101"),
                            (e.Proveedor?.ToString() ?? "").Trim().ToUpperInvariant(),
                            (e.ID_Cliente?.ToString() ?? "").Trim().ToUpperInvariant(),
                            (e.UsuarioID_Vendedor?.ToString() ?? "0"),
                            (e.SKU?.ToString() ?? "").Trim().ToUpperInvariant(),
                            (e.CC?.ToString() ?? "").Trim().ToUpperInvariant(),
                            (e.LINEA?.ToString() ?? "").Trim().ToUpperInvariant());
                        if (!cacheExistentes.ContainsKey(k))
                            cacheExistentes[k] = ((int)e.ID, e.Estado?.ToString() ?? "");
                    }

                    using (var transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            // Se recorre en orden de fila del Excel para que los ID asignados
                            // por IDENTITY sigan la secuencia natural del archivo.
                            foreach (var grupo in grupos.Values.OrderBy(g => g.Min(x => x.Fila)))
                            {
                                var primera = grupo[0];

                                // Resolver PKs
                                int perfilId = dictPerfil.TryGetValue(primera.PerfilNombre.Trim().ToUpperInvariant(), out var pid) ? pid : -1;
                                int tipoId = dictTipo.TryGetValue(primera.TipoNombre.Trim().ToUpperInvariant(), out var tid) ? tid : -1;

                                if (perfilId <= 0) { errores.Add($"Fila {primera.Fila}: Perfil '{primera.PerfilNombre}' no encontrado."); continue; }
                                if (tipoId <= 0) { errores.Add($"Fila {primera.Fila}: Tipo de Objetivo '{primera.TipoNombre}' no encontrado."); continue; }
                                if (!primera.FechaDesde.HasValue) { errores.Add($"Fila {primera.Fila}: Fecha Desde inválida."); continue; }

                                // Validar Cliente y SKU contra sus tablas (FK): si no existen, NULL
                                string clienteFinal = null;
                                if (!string.IsNullOrWhiteSpace(primera.Cliente))
                                {
                                    string cli = primera.Cliente.Trim();
                                    if (dictClientesValidos.Contains(cli)) clienteFinal = cli;
                                    else errores.Add($"Fila {primera.Fila}: Cliente '{cli}' no existe en ClienteSap; se guardará vacío.");
                                }

                                string skuFinal = null;
                                if (!string.IsNullOrWhiteSpace(primera.Sku))
                                {
                                    string sku = primera.Sku.Trim();
                                    if (dictSkusValidos.Contains(sku)) skuFinal = sku;
                                    else errores.Add($"Fila {primera.Fila}: SKU '{sku}' no existe en ArticuloSap; se guardará vacío.");
                                }

                                int? vendedorId = null;
                                if (!string.IsNullOrEmpty(primera.VendedorTexto))
                                {
                                    string vendKey = primera.VendedorTexto.Contains('-')
                                        ? primera.VendedorTexto.Split('-')[0].Trim().ToUpperInvariant()
                                        : primera.VendedorTexto.Trim().ToUpperInvariant();
                                    if (dictVendedor.TryGetValue(vendKey, out var vid)) vendedorId = vid;
                                    else if (dictVendedorNombre.TryGetValue(vendKey, out var vidNombre)) vendedorId = vidNombre;
                                    else { errores.Add($"Fila {primera.Fila}: Vendedor '{primera.VendedorTexto}' no encontrado."); continue; }
                                }

                                // Buscar objetivo existente por clave natural (cache en memoria)
                                string claveBusqueda = string.Join("|",
                                    perfilId.ToString(),
                                    tipoId.ToString(),
                                    (primera.Periodo ?? "").Trim().ToUpperInvariant(),
                                    primera.FechaDesde.Value.ToString("yyyy-MM-dd"),
                                    primera.FechaHasta?.ToString("yyyy-MM-dd") ?? "19000101",
                                    (primera.Proveedor ?? "").Trim().ToUpperInvariant(),
                                    clienteFinal?.Trim().ToUpperInvariant() ?? "",
                                    (vendedorId ?? 0).ToString(),
                                    skuFinal?.Trim().ToUpperInvariant() ?? "",
                                    (primera.Cc ?? "").Trim().ToUpperInvariant(),
                                    (primera.Linea ?? "").Trim().ToUpperInvariant());

                                bool yaExiste = cacheExistentes.TryGetValue(claveBusqueda, out var existenteInfo);
                                string estadoExistente = yaExiste ? existenteInfo.Estado : null;

                                int objId;
                                if (!yaExiste)
                                {
                                    // INSERT
                                    string sqlInsert = @"
                                        INSERT INTO dbo.Objetivos (
                                            ID_Perfil, ID_Tipo_Objetivo, Tipo_Periodo_Cumplimiento,
                                            Fecha_Desde, Fecha_Hasta, Descripcion_Objetivo,
                                            ID_Cliente, UsuarioID_Vendedor, Proveedor, SKU, CC, LINEA,
                                            Estado, UsuarioID_Creacion, Fecha_Creacion
                                        ) VALUES (
                                            @ID_Perfil, @ID_Tipo_Objetivo, @Tipo_Periodo_Cumplimiento,
                                            @Fecha_Desde, @Fecha_Hasta, @Descripcion_Objetivo,
                                            @ID_Cliente, @UsuarioID_Vendedor, @Proveedor, @SKU, @CC, @LINEA,
                                            'Pendiente', @UsuarioCreacion, GETDATE()
                                        );
                                        SELECT CAST(SCOPE_IDENTITY() as int);";

                                    objId = await conn.QuerySingleAsync<int>(sqlInsert, new
                                    {
                                        ID_Perfil = perfilId,
                                        ID_Tipo_Objetivo = tipoId,
                                        Tipo_Periodo_Cumplimiento = (primera.Periodo ?? "").Trim(),
                                        Fecha_Desde = primera.FechaDesde.Value,
                                        Fecha_Hasta = primera.FechaHasta,
                                        Descripcion_Objetivo = primera.Descripcion ?? "",
                                        ID_Cliente = clienteFinal == null ? (object)DBNull.Value : clienteFinal,
                                        UsuarioID_Vendedor = (vendedorId.HasValue && vendedorId.Value > 0) ? (object)vendedorId.Value : (object)DBNull.Value,
                                        Proveedor = string.IsNullOrWhiteSpace(primera.Proveedor) ? (object)DBNull.Value : primera.Proveedor.Trim(),
                                        SKU = skuFinal == null ? (object)DBNull.Value : skuFinal,
                                        CC = string.IsNullOrWhiteSpace(primera.Cc) ? (object)DBNull.Value : primera.Cc.Trim(),
                                        LINEA = string.IsNullOrWhiteSpace(primera.Linea) ? (object)DBNull.Value : primera.Linea.Trim(),
                                        UsuarioCreacion = idUsuario ?? 1
                                    }, transaccion);
                                    insertados++;
                                }
                                else if (estadoExistente == "Pendiente")
                                {
                                    objId = existenteInfo.ID;
                                    string sqlUpdate = @"
                                        UPDATE dbo.Objetivos
                                        SET Descripcion_Objetivo = @Descripcion,
                                            UsuarioID_Modificacion = @UsuarioMod,
                                            Fecha_Modificacion = GETDATE()
                                        WHERE ID = @ID";
                                    await conn.ExecuteAsync(sqlUpdate, new
                                    {
                                        Descripcion = primera.Descripcion ?? "",
                                        UsuarioMod = idUsuario ?? 1,
                                        ID = objId
                                    }, transaccion);

                                    await conn.ExecuteAsync("DELETE FROM dbo.Objetivo_Valor WHERE ID_Objetivo = @ID", new { ID = objId }, transaccion);
                                    actualizados++;
                                }
                                else
                                {
                                    omitidos++; // ya autorizado/cumplido/otro estado: no se toca
                                    continue;
                                }

                                // Metas (una fila por meta)
                                var metasProcesadas = new HashSet<int>();
                                foreach (var filaMeta in grupo)
                                {
                                    if (string.IsNullOrEmpty(filaMeta.ValorTexto)) continue;

                                    int valorId = -1;
                                    string valorNombre = filaMeta.ValorTexto.Trim();
                                    string valorUnidad = "";
                                    if (int.TryParse(valorNombre, out int valorIdDirecto))
                                    {
                                        valorId = valorIdDirecto;
                                        if (dictValorPorId.TryGetValue(valorId, out var infoId))
                                        {
                                            valorNombre = infoId.Nombre;
                                            valorUnidad = infoId.Unidad;
                                        }
                                    }
                                    else
                                    {
                                        var match = dictValor.FirstOrDefault(x => string.Equals(x.Key, valorNombre, StringComparison.OrdinalIgnoreCase));
                                        if (match.Value == null)
                                        {
                                            var matchSimple = dictValor.FirstOrDefault(x => x.Key.Split('(')[0].Trim().Equals(valorNombre, StringComparison.OrdinalIgnoreCase));
                                            if (matchSimple.Value != null) { valorId = matchSimple.Value.ID; valorNombre = matchSimple.Value.Nombre; valorUnidad = matchSimple.Value.Unidad; }
                                            else { errores.Add($"Fila {filaMeta.Fila}: Valor '{valorNombre}' no encontrado en el catálogo."); continue; }
                                        }
                                        else { valorId = match.Value.ID; valorNombre = match.Value.Nombre; valorUnidad = match.Value.Unidad; }
                                    }

                                    if (valorId <= 0) { errores.Add($"Fila {filaMeta.Fila}: Valor inválido."); continue; }
                                    if (!metasProcesadas.Add(valorId))
                                    {
                                        errores.Add($"Fila {filaMeta.Fila}: El valor '{valorNombre}' ya viene duplicado para el mismo objetivo; solo se consideró su primera fila.");
                                        continue;
                                    }

                                    string sqlMeta = @"
                                        INSERT INTO dbo.Objetivo_Valor (
                                            ID_Objetivo, ID_Catalogo_ValorObjetivo, Tipo_Valor, Unidad_Medida,
                                            Valor_Minimo, Valor_Maximo, Valor_Objetivo
                                        ) VALUES (
                                            @ID_Objetivo, @ID_Catalogo, @TipoValor, @Unidad,
                                            @Minimo, @Maximo, @Meta
                                        )";
                                    await conn.ExecuteAsync(sqlMeta, new
                                    {
                                        ID_Objetivo = objId,
                                        ID_Catalogo = valorId,
                                        TipoValor = filaMeta.ValorTexto.Trim(),
                                        Unidad = valorUnidad,
                                        Minimo = filaMeta.ValorMinimo,
                                        Maximo = filaMeta.ValorMaximo,
                                        Meta = filaMeta.Meta
                                    }, transaccion);
                                }
                            }

                            transaccion.Commit();
                        }
                        catch
                        {
                            transaccion.Rollback();
                            throw;
                        }
                    }

                    string mensaje = $"Se insertaron {insertados}, se actualizaron {actualizados} y se omitieron {omitidos} objetivo(s).";
                    if (errores.Count > 0)
                        mensaje += " Con " + errores.Count + " fila(s) con error.";

                    return Json(new { ok = true, mensaje, insertados, actualizados, omitidos, errores });
                }
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error al procesar el archivo: " + ex.Message });
            }
        }

        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
        public async Task<IActionResult> CrearTipoObjetivo(int idPerfil, string nombre, string descripcion)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    string sql = @"
                        INSERT INTO dbo.Tipo_Objetivo (Nombre, Descripcion, Activo, ID_Perfil) 
                        OUTPUT INSERTED.ID 
                        VALUES (@Nombre, @Descripcion, 1, @IdPerfil)";

                    int nuevoId = await conn.QuerySingleAsync<int>(sql, new
                    {
                        Nombre = nombre,
                        Descripcion = descripcion,
                        IdPerfil = idPerfil
                    });

                    return Json(new { success = true, id = nuevoId, nombre = nombre });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CrearValorObjetivo(
            string nombre,
            string unidadMedida,
            string descripcion)
        {
            try
            {
                string connectionString =
                    _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new SqlConnection(connectionString)) 
                {
                    string sql = @"
                        INSERT INTO dbo.Catalogo_ValorObjetivo
                            (Nombre, Unidad_Medida, Descripcion, Activo)
                        OUTPUT INSERTED.ID
                        VALUES
                            (@Nombre, @UnidadMedida, @Descripcion, 1);";

                    int nuevoId = await conn.QuerySingleAsync<int>(
                        sql,
                        new
                        {
                            Nombre = nombre,
                            UnidadMedida = unidadMedida,
                            Descripcion = descripcion
                        });

                    return Json(new
                    {
                        success = true,
                        id = nuevoId,
                        nombre = nombre,
                        unidad = unidadMedida
                    });
                }
            }

            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        private class ObjetivoCargaRow
        {
            public int Fila { get; set; }
            public string PerfilNombre { get; set; }
            public string TipoNombre { get; set; }
            public string Periodo { get; set; }
            public DateTime? FechaDesde { get; set; }
            public DateTime? FechaHasta { get; set; }
            public string VendedorTexto { get; set; }
            public string Cliente { get; set; }
            public string Sku { get; set; }
            public string Cc { get; set; }
            public string Linea { get; set; }
            public string Proveedor { get; set; }
            public string Descripcion { get; set; }
            public string ValorTexto { get; set; }
            public decimal? ValorMinimo { get; set; }
            public decimal? ValorMaximo { get; set; }
            public decimal? Meta { get; set; }
        }
    }
}