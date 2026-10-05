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
        public async Task<IActionResult> Tablero()
        {
            var listaObjetivos = new List<ObjetivoViewModel>();
            string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");

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

                    // Consulta de cabeceras
                    string whereClause = esAdmin ? "" : "WHERE o.ID_Perfil = @PerfilId ";
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

                    var parametros = esAdmin ? null : new { PerfilId = perfilIdUsuario };
                    var objetivosRaw = await conn.QueryAsync<ObjetivoViewModel>(sqlObjetivos, parametros);
                    listaObjetivos = objetivosRaw.ToList();

                    // Consulta de metas
                    if (listaObjetivos.Any())
                    {
                        var idsObjetivos = listaObjetivos.Select(x => x.ID).ToList();
                        string sqlValores = "SELECT * FROM dbo.Objetivo_Valor WHERE ID_Objetivo IN @Ids";
                        var valoresRaw = await conn.QueryAsync<ObjetivoValorViewModel>(sqlValores, new { Ids = idsObjetivos });

                        foreach (var obj in listaObjetivos)
                        {
                            obj.ValoresConfigurados = valoresRaw.Where(v => v.ID_Objetivo == obj.ID).ToList();
                        }
                    }

                    // Cargar perfiles para el filtro
                    if (esAdmin)
                        ViewBag.Perfiles = await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles");
                    else
                        ViewBag.Perfiles = await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles WHERE Id = @PerfilId", new { PerfilId = perfilIdUsuario });

                    // Bitácora (log) para el modal: admin ve todo, los demas solo su perfil
                    string sqlLog = @"
                        SELECT 
                            o.ID,
                            o.Estado,
                            p.Nombre AS NombrePerfil,
                            t.Nombre AS NombreTipoObjetivo,
                            o.Fecha_Creacion,
                            uc.Usuario AS UsuarioCreacion,
                            uc.Nombre AS NombreCreador,
                            o.Fecha_Modificacion,
                            um.Usuario AS UsuarioModificacion,
                            um.Nombre AS NombreModificador,
                            o.Fecha_Aprueba,
                            ua.Usuario AS UsuarioAprueba,
                            ua.Nombre AS NombreAutorizador
                        FROM dbo.Objetivos o
                        INNER JOIN dbo.Perfiles p ON o.ID_Perfil = p.Id
                        INNER JOIN dbo.Tipo_Objetivo t ON o.ID_Tipo_Objetivo = t.ID
                        LEFT JOIN dbo.UsuarioSQL uc ON o.UsuarioID_Creacion = uc.Id
                        LEFT JOIN dbo.UsuarioSQL um ON o.UsuarioID_Modificacion = um.Id
                        LEFT JOIN dbo.UsuarioSQL ua ON o.UsuarioID_Aprueba = ua.Id
                        " + whereClause + @"
                        ORDER BY o.Fecha_Creacion DESC";
                    var logRaw = await conn.QueryAsync<ObjetivoLogViewModel>(sqlLog, parametros);
                    ViewBag.LogObjetivos = logRaw.ToList();

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
        [RevisarPermiso("OBJETIVOS", "ESCRIBIR")]
        public async Task<IActionResult> Nuevo()
        {
            string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");

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
            string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");

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
                string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");
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
                string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");
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
                string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");
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

        [HttpGet]
        public async Task<JsonResult> ObtenerKpisPorArea(int idPerfil)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");
                using (var conn = new SqlConnection(connectionString))
                {
                    string sql;

                    // Validar si es admin
                    if (idPerfil == 1)
                    {
                        sql = "SELECT ID as Valor, Nombre as Texto FROM dbo.Tipo_Objetivo WHERE Activo = 1";
                        var kpisTodos = await conn.QueryAsync(sql);
                        return Json(kpisTodos);
                    }
                    else
                    {
                        sql = "SELECT ID as Valor, Nombre as Texto FROM dbo.Tipo_Objetivo WHERE Activo = 1 AND ID_Perfil = @IdPerfil";
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

        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ELIMINAR")]
        public async Task<IActionResult> Editar(ObjetivoViewModel modelo)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    int usuarioModId = await ObtenerIdUsuarioAsync(conn) ?? 1;

                    if (modelo.ValoresConfigurados == null || !modelo.ValoresConfigurados.Any())
                    {
                        TempData["ErrorObjetivos"] = "El objetivo debe tener al menos un valor configurado.";
                        return RedirectToAction("Tablero");
                    }


                    using (var transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            // Actualizar cabecera (todo excepto Perfil y Tipo de Objetivo)
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
                                    UsuarioID_Modificacion = @UsuarioMod, 
                                    Fecha_Modificacion = GETDATE()
                                WHERE ID = @ID";

                            await conn.ExecuteAsync(sqlObjetivo, new
                            {
                                FechaDesde = modelo.Fecha_Desde,
                                FechaHasta = modelo.Fecha_Hasta,
                                Periodo = modelo.Tipo_Periodo_Cumplimiento,
                                Estado = modelo.Estado ?? "Pendiente",
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
                                    await conn.ExecuteAsync("DELETE FROM dbo.Objetivo_Valor WHERE ID IN @Ids",
                                        new { Ids = idsAEliminar }, transaccion);
                            }
                            else
                            {
                                // No viene ninguna meta de la forma -> eliminar todas las existentes
                                await conn.ExecuteAsync("DELETE FROM dbo.Objetivo_Valor WHERE ID_Objetivo = @IDObjetivo",
                                    new { IDObjetivo = modelo.ID }, transaccion);
                            }

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
                return RedirectToAction("Tablero");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al editar: " + ex.Message);
                TempData["ErrorObjetivos"] = "Error al editar: " + ex.Message;
                return RedirectToAction("Tablero");
            }
        }

        // ================================================================
        //  CARGA MASIVA DE OBJETIVOS 
        // ================================================================

        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "LEER")]
        public async Task<IActionResult> DescargarPlantillaObjetivos()
        {
            string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");
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

            string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");
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

                    using (var transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var grupo in grupos.Values)
                            {
                                var primera = grupo[0];

                                // Resolver PKs
                                int perfilId = dictPerfil.TryGetValue(primera.PerfilNombre.Trim().ToUpperInvariant(), out var pid) ? pid : -1;
                                int tipoId = dictTipo.TryGetValue(primera.TipoNombre.Trim().ToUpperInvariant(), out var tid) ? tid : -1;

                                if (perfilId <= 0) { errores.Add($"Fila {primera.Fila}: Perfil '{primera.PerfilNombre}' no encontrado."); continue; }
                                if (tipoId <= 0) { errores.Add($"Fila {primera.Fila}: Tipo de Objetivo '{primera.TipoNombre}' no encontrado."); continue; }
                                if (!primera.FechaDesde.HasValue) { errores.Add($"Fila {primera.Fila}: Fecha Desde inválida."); continue; }

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

                                // Buscar objetivo existente por clave natural
                                var existente = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                                    SELECT TOP 1 ID, Estado FROM dbo.Objetivos
                                    WHERE ID_Perfil = @Perfil
                                      AND ID_Tipo_Objetivo = @Tipo
                                      AND UPPER(LTRIM(RTRIM(Tipo_Periodo_Cumplimiento))) = @Periodo
                                      AND Fecha_Desde = @Desde
                                      AND ISNULL(Fecha_Hasta, '19000101') = @Hasta
                                      AND ISNULL(UPPER(LTRIM(RTRIM(Proveedor))), '') = @Prov
                                      AND ISNULL(UPPER(LTRIM(RTRIM(ID_Cliente))), '') = @Cliente
                                      AND ISNULL(UsuarioID_Vendedor, 0) = @Vendedor
                                      AND ISNULL(UPPER(LTRIM(RTRIM(SKU))), '') = @Sku
                                      AND ISNULL(UPPER(LTRIM(RTRIM(CC))), '') = @Cc
                                      AND ISNULL(UPPER(LTRIM(RTRIM(LINEA))), '') = @Linea",
                                    new
                                    {
                                        Perfil = perfilId,
                                        Tipo = tipoId,
                                        Periodo = (primera.Periodo ?? "").Trim().ToUpperInvariant(),
                                        Desde = primera.FechaDesde.Value,
                                        Hasta = primera.FechaHasta?.ToString("yyyy-MM-dd") ?? "19000101",
                                        Prov = (primera.Proveedor ?? "").Trim().ToUpperInvariant(),
                                        Cliente = (primera.Cliente ?? "").Trim().ToUpperInvariant(),
                                        Vendedor = vendedorId ?? 0,
                                        Sku = (primera.Sku ?? "").Trim().ToUpperInvariant(),
                                        Cc = (primera.Cc ?? "").Trim().ToUpperInvariant(),
                                        Linea = (primera.Linea ?? "").Trim().ToUpperInvariant()
                                    }, transaccion);

                                int objId;
                                if (existente == null)
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
                                        ID_Cliente = string.IsNullOrWhiteSpace(primera.Cliente) ? null : primera.Cliente,
                                        UsuarioID_Vendedor = vendedorId,
                                        Proveedor = string.IsNullOrWhiteSpace(primera.Proveedor) ? null : primera.Proveedor,
                                        SKU = string.IsNullOrWhiteSpace(primera.Sku) ? null : primera.Sku,
                                        CC = string.IsNullOrWhiteSpace(primera.Cc) ? null : primera.Cc,
                                        LINEA = string.IsNullOrWhiteSpace(primera.Linea) ? null : primera.Linea,
                                        UsuarioCreacion = idUsuario ?? 1
                                    }, transaccion);
                                    insertados++;
                                }
                                else if ((string)existente.Estado == "Pendiente")
                                {
                                    objId = (int)existente.ID;
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
                string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");
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
                    _configuration.GetConnectionString("CadenaSQLSIGO");

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