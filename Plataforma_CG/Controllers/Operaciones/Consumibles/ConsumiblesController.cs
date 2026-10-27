using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Operaciones.Consumibles;
using Plataforma_CG.Models.Operaciones.Consumibles;

namespace Plataforma_CG.Controllers.Operaciones.Consumibles
{
    public class ConsumiblesController : Controller
    {
        AccesoConsumibles ac = new AccesoConsumibles();
        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public async Task<IActionResult> ObtenerConsumibles(
    string tipo,
    DateTime fechaInicio,
    DateTime fechaFin)
        {
            var lista = new List<ConsumiblesModel>();

            lista = ac.ConsultarConsumibles(tipo, fechaInicio.ToString("yyyy-MM-dd"), fechaFin.ToString("yyyy-MM-dd"));

            return Ok(lista);
        }
        [HttpGet]
        public IActionResult ConsultarNombreArticulo(string articulo)
        {
            var nombre = ac.ConsultarNombre(articulo);

            return Ok(nombre);
        }

            [HttpGet]
            public IActionResult ConsultarPlan(string articulo, string fechain, string fechafin)
            {
                var plan = ac.ConsultarPlanIndi(articulo,fechain,fechafin);

                return Ok(plan);
            }

            [HttpGet]
            public IActionResult ConsultarInye(string articulo, string fechain, string fechafin)
            {
                var inye = ac.ConsultarInyeccion(articulo, fechain, fechafin);
            if (inye.Porcentaje>0)
            {
                Console.WriteLine(inye.Porcentaje);
            }
            if (inye.Peso>0)
            {
                Console.WriteLine(inye.Porcentaje);
            }
            return new JsonResult(new
            {
                Peso = inye.Peso,
                Porcentaje = inye.Porcentaje
            });
        }

    }
}
