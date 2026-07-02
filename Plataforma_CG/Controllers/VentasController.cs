using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.ViewModels;

namespace Plataforma_CG.Controllers
{
    [Route("Comercial")] // base de la ruta
    public class VentasController : Controller
    {
        [Route("Prospectos")] // ruta completa: /Comercial/Ventas
        public IActionResult Prospectos()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/Prospectos.cshtml");
        }


        [Route("OrdenVenta")]
        public IActionResult OrdenVentas()
        {
            return RedirectToAction("comercial", "Comercial");
        }


        [Route("Presupuestos")]
        public IActionResult Presupuestos()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/Presupuestos.cshtml");
        }


        //esta era la de cedis
        [Route("PresupuestosGenerales")]
        public IActionResult PresupuestosGenerales()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/PresupuestosGenerales.cshtml");
        }



        [Route("Cat_Articulo")]
        public IActionResult Cat_Articulo()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/Cat_Articulo.cshtml");
        }


        [Route("Cat_Clientes")]
        public IActionResult Cat_Clientes()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/Cat_Clientes.cshtml");
        }

        [Route("Cat_Precio")]
        public IActionResult Cat_Precio()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/Cat_Precio.cshtml");
        }


        [Route("Balance_Master")]
        public IActionResult Balance_Master()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/Balance_Master.cshtml");
        }


        [Route("mapaCarga")]
        public IActionResult mapaCarga()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/mapaCarga.cshtml");
        }


        [Route("OrdenesPorVendedor")]
        public IActionResult OrdenesPorVendedor()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/OrdenesPorVendedor.cshtml");
        }


        [Route("PresupuestosPorMes")]
        public IActionResult PresupuestosPorMes()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/PresupuestosPorMes.cshtml");
        }


        [Route("TableroOV")]
        public IActionResult TableroOV()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/TableroOV.cshtml");
        }


        [Route("ControlCenter")]
        public IActionResult ControlCenter()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/ControlCenter.cshtml");
        }


        [Route("TrackingViaje")]
        public IActionResult TrackingViaje()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/TrackingViaje.cshtml");
        }


        [Route("Inventarios")]
        public IActionResult Inventarios()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/Inventarios.cshtml");
        }


        [Route("ConfirmadoVsEmbarcado")]
        public IActionResult ConfirmadoVsEmbarcado()
        {
            // Aquí se indica la vista, usando ruta completa si está en otra carpeta
            return View("~/Views/Comercial/ConfirmadoVsEmbarcado.cshtml");
        }




    }
}
