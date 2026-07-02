using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Comercial.Ventas;
using Plataforma_CG.Models;

namespace Plataforma_CG.Controllers.Comercial.Ventas
{
    public class OrdenVentaController : Controller
    {
        AccesoTodoVentas atv = new AccesoTodoVentas();
        TodoVentasModel tvm = new TodoVentasModel();
        public IActionResult Index()
        {
            var lista = atv._SAP.ClientesSap();
            //tvm._ListaCliente = atv._Clientes.Listar();
            tvm._ListaCliente = lista;
            tvm._ListaZonas = atv._Zonas.Listar();
            return PartialView("~/Views/Comercial/Clientes/Cliente/Index.cshtml", tvm);
        }
    }
}
