
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Plataforma_CG.AccesoDatos.Operaciones.Inyeccion;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Operaciones.Inyeccion;
using Plataforma_CG.Services;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers.Operaciones.Inyeccion
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class InyeccionController : ControllerBase
    {
        Lotes l = new Lotes();
        private readonly Receta r;
        Conexiones co = new Conexiones();
        AccesoPermisos permisos = new AccesoPermisos();
        private readonly ImagenProductoService _imgservice;
        private readonly BasculaService _basc;
        private readonly ILogger<InyeccionController> _logger;

        public InyeccionController(
            ImagenProductoService imgservice,
            AccesoRecetas accesoRecetas,
            ILogger<InyeccionController> logger)
        {
            _imgservice = imgservice;
            _basc = new BasculaService();
            _logger = logger;
            l = new Lotes();
            r = new Receta(accesoRecetas);
            permisos = new AccesoPermisos();
        }
        [HttpGet("ObtenerLotes")]
        //[RevisarPermiso("INYECCION", "ESCRIBIR")]
        public async Task<IActionResult> ObtenerLotes()
        {
            var lista = await l.ConsultarLotes();
            return Ok(lista);
        }
        [HttpGet("ListarProductos")]
        public async Task<IActionResult> ObtenerProductos(string plan)
        {
            var lista = await r.ListarProductos(plan);
            return Ok(lista);
        }
        [HttpGet("ObtenerReceta")]
        public async Task<IActionResult> ObtenerReceta(string sku)
        {
            var dato = await r.ObtenerReceta(sku);
            return Ok(dato);
        }
        [HttpGet("ObtenerImagen")]
        public IActionResult ObtenerImagen(string nombre, string sku)
        {
            var ruta = _imgservice.ObtenerRutaImagen(nombre, sku);
            return PhysicalFile(ruta, "image/png");
        }
        [HttpGet("ObtenerPeso")]
        public async Task<string> Peso(string ip, string comando = "P")
        {
            var peso = await _basc.Bascula(ip, comando);
            return peso;
        }
        [HttpGet("ObtenerTaras")]
        public async Task<IActionResult> Taras()
        {
            var taras = await r.ObtenerTaras();
            return Ok(taras);
        }
        [HttpPost("CapturarEntrada")]
        public async Task<IActionResult> InsertarEntrada([FromBody] EntradaModel model)
        {
            try
            {
                EntradaModel entrada = await r.InsertarEntrada(model);
                return Ok(new
                {
                    success = true,
                    id = entrada.Id,
                    folio = entrada.Folio
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                _logger.LogError(ex, "No existe dbo.Entradas para guardar la captura de Inyecciones");
                return StatusCode(500, new
                {
                    success = false,
                    message = "No existe dbo.Entradas en la base configurada. Ejecute el script SQL de instalación del módulo de Inyecciones."
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error de SQL Server al guardar una captura de Inyecciones");
                return StatusCode(503, new
                {
                    success = false,
                    message = "No fue posible guardar la captura directamente en SQL Server. Verifique la conexión de InyeccionesSql."
                });
            }
        }

        [HttpPost("Imprimir")]
        public IActionResult Imprimir(
            string ip,
            string lote,
            string prod,
            [FromBody] EntradaModel model)
        {
            try
            {
                if (model == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "No se recibieron los datos de la etiqueta."
                    });
                }

                if (string.IsNullOrWhiteSpace(ip))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "No se recibió la IP de la impresora."
                    });
                }

                if (model.Id <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "La etiqueta no contiene un Id de entrada válido."
                    });
                }

                if (string.IsNullOrWhiteSpace(model.SKU))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "La etiqueta no contiene SKU."
                    });
                }

                if (string.IsNullOrWhiteSpace(prod))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "La etiqueta no contiene el nombre del producto."
                    });
                }

                if (string.IsNullOrWhiteSpace(lote))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "La etiqueta no contiene el lote."
                    });
                }

                // Se imprime exactamente la fotografía enviada por el front.
                // No se vuelve a consultar la entrada ni el catálogo de productos.
                var resultado = co.Impresion(
                    1,
                    model,
                    ip.Trim(),
                    lote.Trim(),
                    prod.Trim()
                );

                if (!resultado.ok)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = resultado.mensaje
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = resultado.mensaje,
                    id = model.Id,
                    sku = model.SKU,
                    producto = prod.Trim()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Error al imprimir la entrada {model?.Id}: {ex.Message}"
                });
            }
        }
        [HttpGet("ValidarModoManual")]
        public async Task<IActionResult> ValidarModoManual(int usrid, string nip)
        {
            try
            {
                var resultado = await permisos.Manual(usrid, nip);

                // Si fk_Permiso o usuarioId es 0, no tiene permisos
                if (resultado.fk_Permiso == 0 || resultado.usuarioId == 0)
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Usuario o NIP incorrectos"
                    });
                }

                return Ok(new
                {
                    success = true,
                    usuario = resultado.nombre,
                    permiso = resultado.descripcion
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    message = "Error al validar permisos: " + ex.Message
                });
            }
        }
        [HttpGet("ConsultarEntrada")]
        public async Task<IActionResult> ConsultarEntrada(int id)
        {
            var dato = await r.ConsultarEntrada(id);
            if (dato == null)
                return NotFound(new { success = false, message = "No se encontró la entrada solicitada." });

            return Ok(dato);
        }
    }
}
