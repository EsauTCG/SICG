using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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
        public async Task<IActionResult> Tablero()
        {
            var listaObjetivos = new List<ObjetivoViewModel>();
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Consulta principal con JOIN
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
                        ORDER BY o.Fecha_Creacion DESC";

                    var objetivosRaw = await conn.QueryAsync<ObjetivoViewModel>(sqlObjetivos);
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
                    ViewBag.Perfiles = await conn.QueryAsync("SELECT Id, Nombre FROM dbo.Perfiles");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar los objetivos: " + ex.Message);
            }
           
            return View(listaObjetivos);
        }
        [HttpGet]
        public async Task<IActionResult> Nuevo()
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Cargamos los catálogos para los menús desplegables (Dropdowns)
                    // Nota: Si la tabla Perfiles se llama diferente, ajusta la consulta
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
        public async Task<IActionResult> GuardarNuevo(ObjetivoViewModel modelo)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");

                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // 1. Insertamos el Objetivo (Cabecera) y recuperamos el ID generado
                    string sqlObjetivo = @"
                        INSERT INTO dbo.Objetivos (
                            ID_Perfil, ID_Tipo_Objetivo, Tipo_Periodo_Cumplimiento, 
                            Fecha_Desde, Fecha_Hasta, Descripcion_Objetivo, 
                            Estado, UsuarioID_Creacion, Fecha_Creacion
                        ) 
                        VALUES (
                            @ID_Perfil, @ID_Tipo_Objetivo, @Tipo_Periodo_Cumplimiento, 
                            @Fecha_Desde, @Fecha_Hasta, @Descripcion_Objetivo, 
                            'Activo', @UsuarioCreacion, GETDATE()
                        );
                        
                        -- Recupera el ID recién insertado
                        SELECT CAST(SCOPE_IDENTITY() as int);";

                    var parametrosObjetivo = new
                    {
                        modelo.ID_Perfil,
                        modelo.ID_Tipo_Objetivo,
                        modelo.Tipo_Periodo_Cumplimiento,
                        modelo.Fecha_Desde,
                        modelo.Fecha_Hasta,
                        modelo.Descripcion_Objetivo,

                        // NOTA: Aquí puse 1 por defecto, pero puedes cambiarlo por el ID de sesión de tu usuario
                        UsuarioCreacion = 1
                    };

                    // Ejecutamos la consulta y guardamos el nuevo ID en una variable
                    int nuevoObjetivoId = await conn.QuerySingleAsync<int>(sqlObjetivo, parametrosObjetivo);

                    // 2. Insertamos la Meta (Detalle en Objetivo_Valor)
                    if (modelo.ValoresConfigurados != null && modelo.ValoresConfigurados.Any())
                    {
                        var valorMeta = modelo.ValoresConfigurados.First(); // Tomamos la meta que viene del formulario

                        string sqlValor = @"
                            INSERT INTO dbo.Objetivo_Valor (
                                ID_Objetivo, Tipo_Valor, Unidad_Medida, Valor_Objetivo
                            ) VALUES (
                                @ID_Objetivo, @Tipo_Valor, @Unidad_Medida, @Valor_Objetivo
                            )";

                        var parametrosValor = new
                        {
                            ID_Objetivo = nuevoObjetivoId, // Usamos el ID que acabamos de recuperar
                            valorMeta.Tipo_Valor,
                            valorMeta.Unidad_Medida,
                            valorMeta.Valor_Objetivo
                        };

                        await conn.ExecuteAsync(sqlValor, parametrosValor);
                    }
                }

                // 3. Si todo salió bien, redirigimos de vuelta a la tabla
                return RedirectToAction("Tablero");
            }
            catch (Exception ex)
            {
                // Si algo falla, recargamos los catálogos y devolvemos la vista con el error
                ModelState.AddModelError("", "Ocurrió un error al guardar: " + ex.Message);

                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    ViewBag.Perfiles = conn.Query("SELECT Id, Nombre FROM dbo.Perfiles");
                    try { ViewBag.TiposObjetivo = conn.Query("SELECT ID, Nombre FROM dbo.Tipo_Objetivo WHERE Activo = 1"); }
                    catch { ViewBag.TiposObjetivo = new List<dynamic>(); }
                }

                return View("Nuevo", modelo);
            }
        }
    }
}