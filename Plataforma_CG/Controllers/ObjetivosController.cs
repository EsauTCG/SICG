using Dapper;
using DocumentFormat.OpenXml.Office2010.Excel;
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
            string connectionString = _configuration.GetConnectionString("CadenaSQLSIGO");

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                ViewBag.Perfiles = await conn.QueryAsync(
                    "SELECT Id, Nombre FROM dbo.Perfiles"
                );

                var tipoObjetivo = await conn.QueryAsync<TipoObjetivoViewModel>(
                    "SELECT ID, Nombre, Descripcion, Activo FROM dbo.Tipo_Objetivo WHERE Activo = 1"
                );

                ViewBag.TiposObjetivo = tipoObjetivo;

                ViewBag.Clientes = await conn.QueryAsync(
                    "SELECT Cliente, Nombrecliente FROM dbo.ClienteSap ORDER BY Nombrecliente"
                );

                ViewBag.Vendedores = await conn.QueryAsync(
                    "SELECT Id, Nombre FROM dbo.UsuarioSQL where EsVendedor = 1 ORDER  By Nombre"
                );

                ViewBag.Articulos = await conn.QueryAsync(
                    "SELECT ProductoCodigo, ProductoNombre FROM dbo.ArticuloSap ORDER BY ProductoCodigo");
            }

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
                string connectionString =
                    _configuration.GetConnectionString("CadenaSQLSIGO");

                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    string sqlObjetivo = @"
                        INSERT INTO dbo.Objetivos
                            (
	                            ID_Perfil,
	                            ID_Tipo_Objetivo,
	                            Tipo_Periodo_Cumplimiento,
	                            Fecha_Desde,
	                            Fecha_Hasta,
	                            Proveedor,
	                            ID_Cliente,
	                            UsuarioID_Vendedor,
	                            SKU,
	                            CC,
	                            LINEA,
	                            Descripcion_Objetivo,
	                            Estado,
	                            UsuarioID_Creacion,
	                            Fecha_Creacion
                            )
                            VALUES
                            (
	                            @ID_Perfil,
                                @ID_Tipo_Objetivo,
                                @Tipo_Periodo_Cumplimiento,
                                @Fecha_Desde,
                                @Fecha_Hasta,
                                @Proveedor,
                                @ID_Cliente,
                                @UsuarioID_Vendedor,
                                @SKU,
                                @CC,
                                @LINEA,
                                @Descripcion_Objetivo,
                                'Activo',
                                @UsuarioID_Creacion,
                                GETDATE()

                            );

                            SELECT CAST(SCOPE_IDENTITY() AS INT);";
                    var parametrosObjetivos = new
                    {
                        modelo.ID_Perfil,
                        modelo.ID_Tipo_Objetivo,
                        modelo.Tipo_Periodo_Cumplimiento,
                        modelo.Fecha_Desde,
                        modelo.Fecha_Hasta,

                        modelo.Proveedor,

                        modelo.ID_Cliente,
                        modelo.UsuarioID_Vendedor,
                        modelo.SKU,
                        modelo.CC,

                        LINEA = modelo.Linea,

                        modelo.Descripcion_Objetivo,

                        //Temporalmente usamos mi usuario luego se cambia al autenticado automatico

                        UsuarioID_Creacion = 2060
                    };

                    int nuevoObjetivoID =
                        await conn.QuerySingleAsync<int>(
                            sqlObjetivo,
                            parametrosObjetivos
                        );
                    if (modelo.ValoresConfigurados != null &&
                        modelo.ValoresConfigurados.Any())
                    {
                        string sqlValor = @"
                            INSERT INTO dbo.Objetivo_Valor
                            (
                                ID_Objetivo,
                                Tipo_Valor,
                                Unidad_Medida,
                                Valor_Minimo,
                                Valor_Maximo,
                                Valor_Objetivo
                            )
                            VALUES
                            (
                                @ID_Objetivo,
                                @Tipo_Valor,
                                @Unidad_Medida,
                                @Valor_Minimo,
                                @Valor_Maximo,
                                @Valor_Objetivo
                            )";

                        foreach (var valor in modelo.ValoresConfigurados)
                        {
                            var parametrosValor = new
                            {
                                ID_Objetivo = nuevoObjetivoID,
                                valor.Tipo_Valor,
                                valor.Unidad_Medida,
                                valor.Valor_Minimo,
                                valor.Valor_Maximo,
                                valor.Valor_Objetivo
                            };

                            await conn.ExecuteAsync(sqlValor, parametrosValor);
                        }
                    }

                    return Json(new
                    {
                        mensaje = "Objetivo y valores creados correctamente",
                        id = nuevoObjetivoID,
                        cantidadValores = modelo.ValoresConfigurados?.Count ?? 0
                    });
                }
            }

            catch (Exception ex)
            {
                ModelState.AddModelError(
                    "",
                    "Ocurrió un error al guardar: " + ex.Message
                );

                return View("Nuevo", modelo);
            }
        }
    }
}