
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Operaciones.Inyeccion;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Operaciones.Inyeccion;
using Plataforma_CG.Services;
using System.Threading.Tasks;
using System.Linq;

namespace Plataforma_CG.Controllers.Operaciones.Inyeccion
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class InyeccionController : ControllerBase
    {
        Lotes l= new Lotes();
        Receta r = new Receta();
        Conexiones co= new Conexiones();
        AccesoPermisos permisos = new AccesoPermisos();
        private readonly ImagenProductoService _imgservice;
        private readonly BasculaService _basc;

        public InyeccionController(ImagenProductoService imgservice)
        {
            _imgservice = imgservice;
            _basc = new BasculaService();
            l = new Lotes();
            r = new Receta();
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
            var ruta = _imgservice.ObtenerRutaImagen(nombre,sku);
            return PhysicalFile(ruta,"image/png");
        }
        [HttpGet("ObtenerPeso")]
        public async Task<string> Peso(string ip, string comando="P")
        {
            var peso = await _basc.Bascula(ip,comando);
            return peso;
        }
        [HttpGet("ObtenerTaras")]
        public async Task<IActionResult> Taras()
        {
            var taras = await r.ObtenerTaras();
            return Ok(taras);
        }
        [HttpPost("CapturarEntrada")]
        public async Task<IActionResult> InsertarEntrada([FromBody]EntradaModel model)
        {
            string res=await r.InsertarEntrada(model);
            Console.WriteLine("Respuesta raw InsertarEntrada: " + res);
            return Ok(res);
        }

        [HttpPost("Imprimir")]
        public async Task<IActionResult> Imprimir(int id, string ip, string lote)
        {
            try
            {
                if (id <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "El Id de la entrada no es válido."
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

                // Fuente de verdad: el registro guardado correctamente en BD.
                var entradaGuardada = await r.ConsultarEntrada(id);

                if (entradaGuardada == null || entradaGuardada.Id <= 0)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = $"No se encontró la entrada {id} para imprimir."
                    });
                }

                if (string.IsNullOrWhiteSpace(entradaGuardada.SKU))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"La entrada {id} no tiene SKU almacenado."
                    });
                }

                if (string.IsNullOrWhiteSpace(entradaGuardada.Plantilla))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"La entrada {id} no tiene plantilla almacenada."
                    });
                }

                /*
                 * Consultamos nuevamente los productos de la plantilla
                 * y buscamos el nombre usando el SKU guardado en BD.
                 */
                var productos = await r.ListarProductos(entradaGuardada.Plantilla);

                var productoCorrecto = productos?.FirstOrDefault(p =>
                    string.Equals(
                        p.SKU?.Trim(),
                        entradaGuardada.SKU.Trim(),
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (productoCorrecto == null ||
                    string.IsNullOrWhiteSpace(productoCorrecto.Nombre))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"No se encontró el nombre correspondiente al SKU " +
                            $"{entradaGuardada.SKU} en la plantilla " +
                            $"{entradaGuardada.Plantilla}."
                    });
                }

                string nombreProductoCorrecto = productoCorrecto.Nombre.Trim();

                var resultado = co.Impresion(
                    1,
                    entradaGuardada,
                    ip.Trim(),
                    lote?.Trim() ?? string.Empty,
                    nombreProductoCorrecto
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
                    id = entradaGuardada.Id,
                    sku = entradaGuardada.SKU,
                    producto = nombreProductoCorrecto
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Error al imprimir la entrada {id}: {ex.Message}"
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
            return Ok(dato);
        }
    }
}