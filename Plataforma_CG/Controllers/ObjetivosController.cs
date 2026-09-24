using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Plataforma_CG.Data;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
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

                    // Consulta de cabeceras
                    string whereClause = esAdmin ? "" : "WHERE o.ID_Perfil = @PerfilId ";
                    string sqlObjetivos = @"
                        SELECT 
                            o.*, 
                            p.Nombre AS NombrePerfil, 
                            t.Nombre AS NombreTipoObjetivo,
                            a.ProductoNombre AS NombreArticulo
                        FROM dbo.Objetivos o
                        INNER JOIN dbo.Perfiles p ON o.ID_Perfil = p.Id
                        INNER JOIN dbo.Tipo_Objetivo t ON o.ID_Tipo_Objetivo = t.ID
                        LEFT JOIN dbo.ArticuloSap a ON o.SKU = a.ProductoCodigo
                        " + whereClause + @"
                        ORDER BY o.Fecha_Creacion DESC";

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
                    int usuarioCreacionId = await ObtenerIdUsuarioAsync(conn) ?? 1;

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
                                        ID_Objetivo, Tipo_Valor, Unidad_Medida, Valor_Minimo, Valor_Maximo, Valor_Objetivo
                                    ) VALUES (
                                        @IdObjetivo, @TipoValor, @Unidad, @Minimo, @Maximo, @Meta
                                    )";

                                foreach (var val in modelo.ValoresConfigurados)
                                {
                                    await conn.ExecuteAsync(sqlValor, new
                                    {
                                        IdObjetivo = nuevoObjetivoId,
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
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    int usuarioModId = await ObtenerIdUsuarioAsync(conn) ?? 1;

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
                                    SET Tipo_Valor = @TipoValor,
                                        Unidad_Medida = @Unidad,
                                        Valor_Minimo = @Minimo,
                                        Valor_Maximo = @Maximo,
                                        Valor_Objetivo = @Objetivo
                                    WHERE ID = @IDValor";

                                string sqlNuevo = @"
                                    INSERT INTO dbo.Objetivo_Valor 
                                        (ID_Objetivo, Tipo_Valor, Unidad_Medida, Valor_Minimo, Valor_Maximo, Valor_Objetivo)
                                    VALUES
                                        (@IDObjetivo, @TipoValor, @Unidad, @Minimo, @Maximo, @Objetivo)";

                                foreach (var val in modelo.ValoresConfigurados)
                                {
                                    if (val.ID > 0)
                                    {
                                        idsEnviados.Add(val.ID);
                                        await conn.ExecuteAsync(sqlValor, new
                                        {
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
    }
}