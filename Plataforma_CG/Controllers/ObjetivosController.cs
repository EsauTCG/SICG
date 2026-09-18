using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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

        public ObjetivosController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Carga la pantalla principal de configuracion de objetivos
        [HttpGet]
        [RevisarPermiso("OBJETIVOS", "LEER")]

        public async Task<IActionResult> Tablero()
        {
            var listaObjetivos = new List<ObjetivoViewModel>();
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            // Admin ve todo; el resto solo los objetivos de su propia área/perfil
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

                    // Consulta principal con JOIN (filtrada por perfil del usuario si no es admin)
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

                    // Consulta de valores vinculados
                    if (listaObjetivos.Any())
                    {
                        var idsObjetivos = listaObjetivos.Select(x => x.ID).ToList();

                        string sqlValores = @"
                            SELECT * 
                            FROM dbo.Objetivo_Valor 
                            WHERE ID_Objetivo IN @Ids";

                        var valoresRaw = await conn.QueryAsync<ObjetivoValorViewModel>(sqlValores, new { Ids = idsObjetivos });

                        // Asignar valores a su respectivo objetivo
                        foreach (var obj in listaObjetivos)
                        {
                            obj.ValoresConfigurados = valoresRaw.Where(v => v.ID_Objetivo == obj.ID).ToList();
                        }
                    }

                    // El filtro de área solo muestra la propia para usuarios no admin
                    if (esAdmin)
                    {
                        ViewBag.Perfiles = await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles");
                    }
                    else
                    {
                        ViewBag.Perfiles = await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles WHERE Id = @PerfilId",
                            new { PerfilId = perfilIdUsuario });
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar los objetivos: " + ex.Message);
            }
           
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

                    // Cargamos los catálogos para los menús desplegables (Dropdowns)
                    ViewBag.Perfiles = await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles");

                    // Usamos TRY CATCH interno por si la tabla Tipo_Objetivo aun está vacía o no existe
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

            // Inicializamos el modelo con la fecha de hoy por defecto
            var modeloNuevo = new ObjetivoViewModel
            {
                Fecha_Desde = DateTime.Today
            };

            return View(modeloNuevo);
        }
        [HttpPost]
        [RevisarPermiso("OBJETIVOS", "ESCRIBIR")]
        public async Task<IActionResult> GuardarNuevo(ObjetivoViewModel modelo)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // 1. Guardar Objetivo Principal (Incluyendo nuevos campos comerciales)
                    string sqlObjetivo = @"
                        INSERT INTO dbo.Objetivos (
                            ID_Perfil, ID_Tipo_Objetivo, Tipo_Periodo_Cumplimiento, 
                            Fecha_Desde, Fecha_Hasta, Descripcion_Objetivo, 
                            ID_Cliente, UsuarioID_Vendedor, ID_Proveedor, SKU, CC, LINEA,
                            Estado, UsuarioID_Creacion, Fecha_Creacion
                        ) 
                        VALUES (
                            @ID_Perfil, @ID_Tipo_Objetivo, @Tipo_Periodo_Cumplimiento, 
                            @Fecha_Desde, @Fecha_Hasta, @Descripcion_Objetivo, 
                            @ID_Cliente, @UsuarioID_Vendedor, @ID_Proveedor, @SKU, @CC, @LINEA,
                            'Activo', @UsuarioCreacion, GETDATE()
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
                        modelo.ID_Proveedor,
                        modelo.SKU,
                        modelo.CC,
                        modelo.LINEA,
                        UsuarioCreacion = 1
                    };

                    int nuevoObjetivoId = await conn.QuerySingleAsync<int>(sqlObjetivo, parametrosObjetivo);

                    // 2. Guardar Metas Dinamicas (Soporta multiples filas)
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
                            });
                        }
                    }
                }

                return RedirectToAction("Tablero");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ocurrió un error al guardar: " + ex.Message);

                // Recargar catalogos en caso de error
                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    ViewBag.Perfiles = conn.Query("SELECT Id, Nombre FROM dbo.Perfiles");
                    try { ViewBag.TiposObjetivo = conn.Query("SELECT ID, Nombre FROM dbo.Tipo_Objetivo WHERE Activo = 1"); }
                    catch { ViewBag.TiposObjetivo = new List<dynamic>(); }
                }

                return View("Nuevo", modelo);
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

                    // Actualizamos el estado a 'Cancelado'
                    string sql = "UPDATE dbo.Objetivos SET Estado = 'Cancelado' WHERE ID = @Id";
                    await conn.ExecuteAsync(sql, new { Id = id });
                }
                // Si todo sale bien, recargamos el tablero
                return RedirectToAction("Tablero");
            }
            catch (Exception ex)
            {
                // Manejo básico de error
                ModelState.AddModelError("", "Error al cancelar el objetivo: " + ex.Message);
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

                    // Si el perfil es 1 (Administrador), obtenemos todos los KPIs activos
                    if (idPerfil == 1)
                    {
                        sql = @"SELECT ID as Valor, Nombre as Texto 
                                FROM dbo.Tipo_Objetivo 
                                WHERE Activo = 1";
                        var kpisTodos = await conn.QueryAsync(sql);
                        return Json(kpisTodos);
                    }
                    else
                    {
                        // Si es otra area, filtramos por su ID
                        sql = @"SELECT ID as Valor, Nombre as Texto 
                                FROM dbo.Tipo_Objetivo 
                                WHERE Activo = 1 AND ID_Perfil = @IdPerfil";
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
        [RevisarPermiso("OBJETIVOS", "ESCRIBIR")]

        public async Task<IActionResult> Editar(ObjetivoViewModel modelo)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Actualizar datos del objetivo principal
                    string sqlObjetivo = @"
                        UPDATE dbo.Objetivos 
                        SET Fecha_Hasta = @FechaHasta,
                            Descripcion_Objetivo = @Descripcion,
                            UsuarioID_Modificacion = 1, 
                            Fecha_Modificacion = GETDATE()
                        WHERE ID = @ID";

                    await conn.ExecuteAsync(sqlObjetivo, new
                    {
                        FechaHasta = modelo.Fecha_Hasta,
                        Descripcion = modelo.Descripcion_Objetivo,
                        ID = modelo.ID
                    });

                    // Actualizar las metas dinamicas si existen
                    if (modelo.ValoresConfigurados != null && modelo.ValoresConfigurados.Any())
                    {
                        string sqlValor = @"
                            UPDATE dbo.Objetivo_Valor 
                            SET Valor_Minimo = @Minimo,
                                Valor_Maximo = @Maximo,
                                Valor_Objetivo = @Objetivo
                            WHERE ID = @IDValor";

                        foreach (var val in modelo.ValoresConfigurados)
                        {
                            await conn.ExecuteAsync(sqlValor, new
                            {
                                Minimo = val.Valor_Minimo,
                                Maximo = val.Valor_Maximo,
                                Objetivo = val.Valor_Objetivo,
                                IDValor = val.ID 
                            });
                        }
                    }
                }

                return RedirectToAction("Tablero");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al editar: " + ex.Message);
                return RedirectToAction("Tablero");
            }
        }


        [HttpPost]
        public async Task<IActionResult> CrearTipoObjetivo(int idPerfil, string nombre, string descripcion)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionString))
                {
                    // Inserta el nuevo KPI y recupera inmediatamente el ID generado
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