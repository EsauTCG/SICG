using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Comercial.Planeacion;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Comercial.Planeacion;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Plataforma_CG.Controllers.Comercial
{
    [Route("Planeacion")]
    public class PlaneacionController : Controller
    {
        AccesoPlaneacion ap= new AccesoPlaneacion();
        AccesoPlanDetalle apd= new AccesoPlanDetalle();
        [Route("Index")]
        public IActionResult Index()
        {   
            return PartialView("~/Views/Comercial/Planeacion/Meses.cshtml");
        }
        [HttpGet]
        [Route("Participacion")]
        public IActionResult Participacion(int clasi)
        {
            List<ParticipacionModel> model = new List<ParticipacionModel>();
            model = new AccesoParticipacion().Consultar(clasi);
            return Json(model);
        }
        [Route("Planeacion")] // ruta completa: /Comerc/Prospecto
        public IActionResult Planeacion(string fecha)
        {
            TodoPlanModel model = new TodoPlanModel();

            model._ListaClasificacion = new AccesoClasificacion().Consultar();
            model._PlanPro = new PlanProduccionModel();
            model._PlanPro.Fecha = fecha;
            return PartialView("~/Views/Comercial/Planeacion/Index.cshtml", model);
        }
        [Route("Planes")] // ruta completa: /Comerc/Prospecto
        public IActionResult Planes(string fecha)
        {
            TodoPlanModel model = new TodoPlanModel();
            model._ListaPlanPro = new List<PlanProduccionModel>();
            model._ListaClasificacion = new AccesoClasificacion().Consultar();

            model._PlanPro = new PlanProduccionModel();
            model._PlanPro.Fecha = fecha;
            model._ListaPlanPro = ap.ListarFecha(fecha);
            return PartialView("~/Views/Comercial/Planeacion/Planes.cshtml", model);
        }
        [Route("Detalle")] // ruta completa: /Comerc/Prospecto
        public IActionResult Detalle(int id)
        {
            TodoPlanModel model = new TodoPlanModel();
            model._ListaPlanPro = new List<PlanProduccionModel>();
            model._ListaClasificacion = new AccesoClasificacion().Consultar();

            model._PlanPro = new PlanProduccionModel();
            model._PlanPro = ap.ConsultarId(id);
            model._ListaPlanDetalle = apd.Consultar(model._PlanPro.Id);
            return PartialView("~/Views/Comercial/Planeacion/Detalle.cshtml", model);
        }
        [HttpPost]
        [Route("Guardar")]
        public IActionResult Guardar(TodoPlanModel model)
        {
            var run = model;
            model._PlanPro.Id = Convert.ToInt32(ap.Insertar(model._PlanPro));
            foreach (var item in model._ListaPlanDetalle)
            {
                item.fk_Plan = model._PlanPro.Id;
                apd.Insertar(item);
            }
            return Json(new { success = true });

            //return RedirectToAction("Planeacion","Comerc");
        }
        [HttpPost]
        [Route("Modificar")]
        public IActionResult Modificar(TodoPlanModel model)
        {
            var run = model;
            ap.Modificar(model._PlanPro);
            apd.Eliminar(model._PlanPro.Id);
            foreach (var item in model._ListaPlanDetalle)
            {
                item.fk_Plan = model._PlanPro.Id;
                apd.Insertar(item);
            }
            return Json(new { success = true });

//            return RedirectToAction("Planeacion", "Comerc");
        }
    }
}

