using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Plataforma_CG.Data;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    [Authorize]
    public sealed class InyeccionPorcentajeController : Controller
    {
        private readonly IConfiguration _configuracion;
        private readonly AppDbContextUsuarios _dbUsuarios;

        public InyeccionPorcentajeController(IConfiguration configuracion, AppDbContextUsuarios dbUsuarios)
        {
            _configuracion = configuracion;
            _dbUsuarios = dbUsuarios;
        }

        private string UsuarioActual => (User?.Identity?.Name ?? "").Trim();

        [HttpGet("/Operaciones/Inyecciones%%")]
        [RevisarPermiso("INYECCIONES%%", "LEER")]
        public async Task<IActionResult> Index()
        {
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";

            var (_, puedeEscribir, puedeEliminar) =
                await PermisosHelper.ObtenerPermisoEfectivoAsync(_dbUsuarios, UsuarioActual, "INYECCIONES%%");

            ViewBag.PuedeEscribir = puedeEscribir;
            ViewBag.PuedeEliminar = puedeEliminar;

            var (listaInyecciones, error) = await ListarAsync();

            if (error != null)
            {
                ModelState.AddModelError("", error);
            }

            return View("~/Views/Operaciones/InyeccionesPorcentaje.cshtml", listaInyecciones);
        }

        [HttpPost("/Operaciones/Inyecciones%%/Actualizar")]
        [RevisarPermiso("INYECCIONES%%", "ESCRIBIR")]
        public async Task<IActionResult> ActualizarPorcentaje(int id, int porcentaje)
        {
            if (id <= 0)
                return BadRequest(new { ok = false, mensaje = "Identificador invalido." });

            if (porcentaje < 0 || porcentaje > 100)
                return BadRequest(new { ok = false, mensaje = "El porcentaje debe estar entre 0 y 100." });

            using (var conn = CrearConexionPiny(out var errorConexion))
            {
                if (conn == null)
                    return StatusCode(500, new { ok = false, mensaje = errorConexion });

                InyeccionSkuViewModel? antes;
                InyeccionSkuViewModel despues;

                try
                {
                    await conn.OpenAsync();

                    antes = await conn.QueryFirstOrDefaultAsync<InyeccionSkuViewModel>(SqlSeleccion, new { Id = id });

                    if (antes == null)
                        return NotFound(new { ok = false, mensaje = "El SKU ya no existe." });

                    const string sql = "UPDATE dbo.Recetas SET Porcentaje = @Porcentaje WHERE Id = @Id;";

                    await conn.ExecuteAsync(sql, new { Id = id, Porcentaje = porcentaje });

                    despues = new InyeccionSkuViewModel
                    {
                        Id = antes.Id,
                        SKU = antes.SKU,
                        fk_Inyectora = antes.fk_Inyectora,
                        Porcentaje = porcentaje,
                        ModoInyeccion = antes.ModoInyeccion,
                        Presion = antes.Presion,
                        Velocidad = antes.Velocidad,
                        Altura = antes.Altura,
                        Avance = antes.Avance
                    };
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { ok = false, mensaje = "Error al actualizar: " + ex.Message });
                }

                var mensaje = await RegistrarLogAsync("MODIFICAR", id, antes.SKU, antes, despues);

                return Ok(new { ok = true, mensaje = mensaje });
            }
        }

        [HttpPost("/Operaciones/Inyecciones%%/Nuevo")]
        [RevisarPermiso("INYECCIONES%%", "ESCRIBIR")]
        public async Task<IActionResult> NuevoSku(
            string sku,
            int? porcentaje,
            int? fkInyectora,
            int? modoInyeccion,
            string presion,
            int? velocidad,
            int? altura,
            string avance)
        {
            sku = (sku ?? "").Trim();
            avance = (avance ?? "").Trim();

            if (string.IsNullOrWhiteSpace(sku))
                return BadRequest(new { ok = false, mensaje = "El SKU es obligatorio." });

            if (sku.Length > 20)
                return BadRequest(new { ok = false, mensaje = "El SKU no puede superar los 20 caracteres." });

            if (!porcentaje.HasValue || porcentaje.Value < 0 || porcentaje.Value > 100)
                return BadRequest(new { ok = false, mensaje = "El porcentaje debe estar entre 0 y 100." });

            if (avance.Length > 10)
                return BadRequest(new { ok = false, mensaje = "Avance no puede superar los 10 caracteres." });

            decimal? presionValor = null;
            if (!string.IsNullOrWhiteSpace(presion))
            {
                var texto = presion.Trim().Replace(',', '.');

                if (!decimal.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var valorPresion))
                    return BadRequest(new { ok = false, mensaje = "La presion no es un numero valido." });

                presionValor = valorPresion;
            }

            InyeccionSkuViewModel despues;

            using (var conn = CrearConexionPiny(out var errorConexion))
            {
                if (conn == null)
                    return StatusCode(500, new { ok = false, mensaje = errorConexion });

                try
                {
                    await conn.OpenAsync();

                    const string sql = @"
                        INSERT INTO dbo.Recetas (SKU, fk_Inyectora, Porcentaje, ModoInyeccion, Presion, Velocidad, Altura, Avance)
                        VALUES (@SKU, @fkInyectora, @Porcentaje, @ModoInyeccion, @Presion, @Velocidad, @Altura, @Avance);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

                    var identity = await conn.ExecuteScalarAsync<object>(sql, new
                    {
                        SKU = sku,
                        fkInyectora = fkInyectora,
                        Porcentaje = porcentaje.Value,
                        ModoInyeccion = modoInyeccion,
                        Presion = presionValor,
                        Velocidad = velocidad,
                        Altura = altura,
                        Avance = string.IsNullOrWhiteSpace(avance) ? null : avance
                    });

                    despues = new InyeccionSkuViewModel
                    {
                        Id = identity == null ? 0 : Convert.ToInt32(identity, CultureInfo.InvariantCulture),
                        SKU = sku,
                        fk_Inyectora = fkInyectora,
                        Porcentaje = porcentaje.Value,
                        ModoInyeccion = modoInyeccion,
                        Presion = presionValor,
                        Velocidad = velocidad,
                        Altura = altura,
                        Avance = string.IsNullOrWhiteSpace(avance) ? null : avance
                    };
                }
                catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                {
                    return Conflict(new { ok = false, mensaje = "Ya existe un registro con ese SKU." });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { ok = false, mensaje = "Error al crear el SKU: " + ex.Message });
                }
            }

            var mensajeOk = await RegistrarLogAsync("CREAR", despues.Id == 0 ? null : despues.Id, sku, null, despues);

            return Ok(new { ok = true, mensaje = mensajeOk });
        }

        [HttpPost("/Operaciones/Inyecciones%%/Eliminar")]
        [RevisarPermiso("INYECCIONES%%", "ELIMINAR")]
        public async Task<IActionResult> Eliminar(int id)
        {
            if (id <= 0)
                return BadRequest(new { ok = false, mensaje = "Identificador invalido." });

            InyeccionSkuViewModel? antes;

            using (var conn = CrearConexionPiny(out var errorConexion))
            {
                if (conn == null)
                    return StatusCode(500, new { ok = false, mensaje = errorConexion });

                try
                {
                    await conn.OpenAsync();

                    antes = await conn.QueryFirstOrDefaultAsync<InyeccionSkuViewModel>(SqlSeleccion, new { Id = id });

                    if (antes == null)
                        return NotFound(new { ok = false, mensaje = "El SKU ya no existe." });

                    const string sql = "DELETE FROM dbo.Recetas WHERE Id = @Id;";

                    await conn.ExecuteAsync(sql, new { Id = id });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { ok = false, mensaje = "Error al eliminar: " + ex.Message });
                }
            }

            var mensaje = await RegistrarLogAsync("ELIMINAR", id, antes.SKU, antes, null);

            return Ok(new { ok = true, mensaje = mensaje });
        }

        [HttpGet("/Operaciones/Inyecciones%%/Bitacora")]
        [RevisarPermiso("INYECCIONES%%", "LEER")]
        public async Task<IActionResult> Bitacora(string? sku, string? accion)
        {
            using (var conn = CrearConexionSigo(out var errorConexion))
            {
                if (conn == null)
                    return StatusCode(500, new { ok = false, mensaje = errorConexion });

                try
                {
                    await conn.OpenAsync();

                    const string sql = @"
                        SELECT TOP 300
                            Id,
                            Fecha,
                            Usuario,
                            Accion,
                            IdReceta,
                            SKU,
                            DatosAntes,
                            DatosDespues
                        FROM dbo.Recetas_Log
                        WHERE (@Sku IS NULL OR SKU LIKE '%' + @Sku + '%')
                          AND (@Accion IS NULL OR Accion = @Accion)
                        ORDER BY Id DESC;";

                    var filtroSku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
                    var filtroAccion = string.IsNullOrWhiteSpace(accion) ? null : accion.Trim().ToUpperInvariant();

                    var registros = await conn.QueryAsync<InyeccionBitacoraViewModel>(
                        sql, new { Sku = filtroSku, Accion = filtroAccion });

                    var items = new List<object>();

                    foreach (var r in registros)
                    {
                        items.Add(new
                        {
                            id = r.Id,
                            fecha = r.Fecha,
                            usuario = r.Usuario,
                            accion = r.Accion,
                            idReceta = r.IdReceta,
                            sku = r.SKU,
                            antes = r.DatosAntes,
                            despues = r.DatosDespues
                        });
                    }

                    return Ok(new { ok = true, items });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { ok = false, mensaje = "Error al leer la bitacora: " + ex.Message });
                }
            }
        }

        private const string SqlSeleccion = @"
            SELECT
                Id,
                SKU,
                fk_Inyectora,
                Porcentaje,
                ModoInyeccion,
                Presion,
                Velocidad,
                Altura,
                Avance
            FROM dbo.Recetas
            WHERE Id = @Id;";

        private async Task<string> RegistrarLogAsync(string accion, int? idReceta, string? sku, object? antes, object? despues)
        {
            var exito = "SKU actualizado correctamente.";

            if (accion == "CREAR") exito = "SKU creado correctamente.";
            if (accion == "ELIMINAR") exito = "SKU eliminado correctamente.";

            using (var conn = CrearConexionSigo(out _))
            {
                if (conn == null)
                    return exito + " (aviso: no se pudo abrir la bitacora).";

                try
                {
                    await conn.OpenAsync();

                    const string sql = @"
                        INSERT INTO dbo.Recetas_Log (Usuario, Accion, IdReceta, SKU, DatosAntes, DatosDespues)
                        VALUES (@Usuario, @Accion, @IdReceta, @SKU, @DatosAntes, @DatosDespues);";

                    await conn.ExecuteAsync(sql, new
                    {
                        Usuario = string.IsNullOrEmpty(UsuarioActual) ? "desconocido" : UsuarioActual,
                        Accion = accion,
                        IdReceta = idReceta,
                        SKU = sku,
                        DatosAntes = antes == null ? null : JsonSerializer.Serialize(antes),
                        DatosDespues = despues == null ? null : JsonSerializer.Serialize(despues)
                    });
                }
                catch (Exception)
                {
                    return exito + " (aviso: no se pudo registrar en la bitacora).";
                }
            }

            return exito;
        }

        private async Task<(List<InyeccionSkuViewModel> lista, string? error)> ListarAsync()
        {
            var lista = new List<InyeccionSkuViewModel>();

            using (var conn = CrearConexionPiny(out var errorConexion))
            {
                if (conn == null)
                {
                    return (lista, errorConexion);
                }

                try
                {
                    await conn.OpenAsync();

                    const string sql = @"
                        SELECT
                            Id,
                            SKU,
                            fk_Inyectora,
                            Porcentaje,
                            ModoInyeccion,
                            Presion,
                            Velocidad,
                            Altura,
                            Avance
                        FROM dbo.Recetas
                        ORDER BY SKU;";

                    var filas = await conn.QueryAsync<InyeccionSkuViewModel>(sql);
                    lista = new List<InyeccionSkuViewModel>(filas);
                }
                catch (Exception ex)
                {
                    return (lista, "Error al obtener los datos de inyeccion: " + ex.Message);
                }
            }

            return (lista, null);
        }

        private SqlConnection? CrearConexionPiny(out string error)
        {
            return CrearConexion("PINY", out error);
        }

        private SqlConnection? CrearConexionSigo(out string error)
        {
            return CrearConexion("DefaultConnection", out error);
        }

        private SqlConnection? CrearConexion(string nombre, out string error)
        {
            error = string.Empty;

            var cadena = _configuracion.GetConnectionString(nombre);

            if (string.IsNullOrWhiteSpace(cadena))
            {
                error = "No se encontro la cadena de conexion " + nombre + " en appsettings.json.";
                return null;
            }

            return new SqlConnection(cadena);
        }
    }
}
