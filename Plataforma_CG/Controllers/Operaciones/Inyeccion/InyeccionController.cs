
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        Receta r = new Receta();
        Conexiones co = new Conexiones();
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
            string res = await r.InsertarEntrada(model);
            Console.WriteLine("Respuesta raw InsertarEntrada: " + res);
            return Ok(res);
        }

        [HttpPost("Imprimir")]
        public async Task<IActionResult> Imprimir(string ip, string lote, string prod, [FromBody] EntradaModel model)
        {
            try
            {
                var resultado = co.Impresion(1, model, ip, lote, prod);

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
                    message = resultado.mensaje
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
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