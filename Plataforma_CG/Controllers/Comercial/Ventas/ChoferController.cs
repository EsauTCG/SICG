using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos;
using Plataforma_CG.AccesoDatos.Comercial.Ventas;
using Plataforma_CG.Models;
using System.Reflection;

namespace Plataforma_CG.Controllers.Comercial.Ventas
{
    public class ChoferController : Controller
    {
        AccesoTodoVentas atv = new AccesoTodoVentas();
        TodoVentasModel tvm = new TodoVentasModel();
        public IActionResult Index()
        {
            tvm._ListaChofer = atv._Choferes.Listar();
            return PartialView("~/Views/Comercial/Ventas/Chofer/Index.cshtml",tvm);
        }
        public IActionResult Nuevo()
        {
            tvm._ListaChofer = atv._Choferes.Listar();
            return PartialView("~/Views/Comercial/Ventas/Chofer/Index.cshtml", tvm);
        }
        public IActionResult Modificar(int id)
        {
            var model = atv._Choferes.Listar().Where(item=>item.Id==id).FirstOrDefault();
            return PartialView("~/Views/Comercial/Ventas/Chofer/Modificar.cshtml", model);
        }
        [HttpPost]
        public IActionResult Modificar(ChoferModel model)
        {
            if (ModelState.IsValid)
            {
                // 👉 Aquí va tu lógica para guardar en BD
                return Json(new { success = true });
            }

            // Si el modelo no es válido, enviamos los errores
            var errores = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            return Json(new { success = false, errors = errores });
        }
    }
}
