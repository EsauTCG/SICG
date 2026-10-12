using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Comercial.Planeacion;
using Plataforma_CG.AccesoDatos.Operaciones.Planeacion;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Comercial.Planeacion;
using Plataforma_CG.Models.Operaciones.Planeacion;
using Plataforma_CG.Models.Operaciones.Planeacion.Diaria;
using Plataforma_CG.Models.Operaciones.Planeacion.Extra;
using System.Numerics;
using static QuestPDF.Helpers.Colors;
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
        //[HttpGet] 
        //[Route("Participacion")]
        //public IActionResult Participacion(int clasi)
        //{
        //    List<ParticipacionModel> model = new List<ParticipacionModel>();
        //    model = new AccesoParticipacion().Consultar(clasi);
        //    return Json(model);
        //}
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
        AccesoPlanExtra ape = new AccesoPlanExtra();
        [HttpGet("CargarDetalleClasificacionMensual")]
        public IActionResult CargarDetalleClasificacionMensual([FromQuery]int clasificacionId, [FromQuery]string fecha)
        {
            var lista = ape.ListarParticipacion(clasificacionId, fecha);
            int llen = 0;
            foreach (var item in lista)
            {
                if (item.KgInyeccion>0)
                {
                    llen = 1;
                    break;
                }
            }
            AccesoClasificacionMensual acm = new AccesoClasificacionMensual();
            //return View("~/Views/Operaciones/PlaneadorProduccion.cshtml", vm);
            List<SubClasMensualModel> model = new List<SubClasMensualModel>();
            var fec = Convert.ToDateTime(fecha);
            var anio = fec.ToString("yyyy");
            var mes = fec.ToString("MM");
            var clas = acm.ListarSub(anio, mes).Where(i => i.Id == clasificacionId).FirstOrDefault();
            var can = acm.ListarPlaneacionMensual(anio, mes, clas.SkuClasificacion);
            TotalDiarioModel mod=new TotalDiarioModel();
            try
            {


                mod = new TotalDiarioModel
                {
                    Fecha = fecha,
                    CanMen = can,
                    Participaciones = lista,
                    TipoPlan = clas.Nombre,
                    Llenado = llen
                };

            }
            catch (Exception)
            {

            }
            return PartialView("~/Views/Operaciones/Planeacion/_PlaneacionMensualProduccion.cshtml",mod);
        }
        [HttpPost("GuardarPlanMensual")]
        public IActionResult GuardarPlanMensual(
     [FromBody]GuardarPlaneacionMensualModel model)
        {
            if (model == null)
                return BadRequest("Modelo inválido.");

            try
            {
                foreach (var item in model.Productos)
                {
                    ape.InsertarPlanDetalle(model.Fecha,item.ProductoCodigo,item.KgInyeccion);
                    ape.InsertarSubClasMes(model.Fecha.ToString("yyyy-MM-dd"),item.ProductoCodigo,item.SubClasificaciones);
                    
                }
                return Ok(new
                {
                    ok = true,
                    mensaje = "Planeación mensual guardada correctamente."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = ex.Message
                });
            }
        }
    }
}

