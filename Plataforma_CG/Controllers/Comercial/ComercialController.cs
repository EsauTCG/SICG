using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Plataforma_CG.AccesoDatos.Comercial.Planeacion;
using Plataforma_CG.AccesoDatos.Comercial.Ventas;
using Plataforma_CG.AccesoDatos.JSON;
using Plataforma_CG.Models;
using Plataforma_CG.Models.SAP.JSON;

namespace Plataforma_CG.Controllers.Comercial.Ventas
{
    [Route("Comerc")] // base de la ruta
    public class ComercialController : Controller
    {
        
        [Route("Prospecto")] // ruta completa: /Comerc/Prospecto
        public IActionResult Prospectos()
        {
            TodoVentasModel TodVenMod = new TodoVentasModel();
            return View("~/Views/Comercial/Prosp.cshtml",TodVenMod);
        }
        
        [Route("Cliente")] // ruta completa: /Comercial/Clientes
        public IActionResult Cliente()
        {
            TodoVentasModel TodVenMod = new TodoVentasModel();
            TodVenMod._ListaChofer = new AccesoChoferes().Listar();
            TodVenMod._ConteoChofer = TodVenMod._ListaChofer.Count();
            TodVenMod._ListaCliente = new AccesoClientes().ListarActivo();
            TodVenMod._ConteoCLienteAct = TodVenMod._ListaCliente.Count();
            return View("~/Views/Comercial/Clien.cshtml", TodVenMod);
        }
        
        [Route("Planeacion")] // ruta completa: /Comerc/Prospecto
        public IActionResult Planeacion()
        {
            TodoPlanModel model = new TodoPlanModel();

            //TodVenMod._ListaClasificacion = new AccesoClasificacion().Consultar();
            //return View("~/Views/Comercial/Planeacion/Index.cshtml", TodVenMod);
            return View("~/Views/Comercial/Planes.cshtml", model);
        }

    }
}
