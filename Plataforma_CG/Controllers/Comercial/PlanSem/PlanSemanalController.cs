using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.AccesoDatos.Comercial.Planeacion;
using Plataforma_CG.AccesoDatos.Comercial.Planeacion.Semanal;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Comercial.Planeacion;
using Plataforma_CG.Models.Comercial.Planeacion.Semanal;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Plataforma_CG.Controllers.Comercial.PlanSem
{
    [Route("PlanSemanal")]
    public class PlanSemanalController : Controller
    {
        TodoSemanalModel tod = new TodoSemanalModel();
        TodoPlanModel todp = new TodoPlanModel();
        AccesoTodoPlan atp = new AccesoTodoPlan();
        AccesoTodoSemanal at = new AccesoTodoSemanal();
        [Route("Index")]
        public IActionResult Index(int id, string fecha)
        {

            DateTime fec = Convert.ToDateTime(fecha);
            tod._ListaPlanSemanal = at._Semanal.ListarTodo(id)._ListaPlanSemanal;
            int mes = fec.Month, ano = fec.Year;
            tod._ListaSemanas = at._Semanal.Semanas(ano, mes);
            tod._PlanSemanal = new PlanSemanalModel();
            tod._PlanSemanal.fk_Plan = id;
            tod._ListaDetalleSemanal = at._DetalleSemanal.Listar(id)._ListaDetalleSemanal;
            return PartialView("~/Views/Comercial/Planeacion/Semanal/Index.cshtml", tod);
        }
        [Route("Detalle")]
        public IActionResult Detalle(int id, string fechain, string fechafin)
        {
            int conteo = 0;
            double pesoasig = 0.0;
            int canalasig = 0;

            todp._ListaPlanDetalle = atp._Detalle.Consultar(id);

            todp._PlanPro = atp._Planeacion.ConsultarId(id);
            todp._TodSemanal = at._Semanal.ListarTodo(id);
            tod = at._Semanal.Listar(id, fechain, fechafin);
            tod._ListaSemanas = at._Semanal.Semanas(Convert.ToDateTime(fechain).Year, Convert.ToDateTime(fechain).Month);
            int sem = Convert.ToInt32(tod._ListaSemanas.Count() - conteo);
            try
            {
                conteo = todp._TodSemanal._ListaPlanSemanal.Count();
                foreach (var item in todp._TodSemanal._ListaPlanSemanal)
                {
                    canalasig += item.Canales;
                    pesoasig += item.PesoTotal;
                }

            }
            catch (Exception)
            {
            }
            if (tod._PlanSemanal == null)
            {
                tod._PlanSemanal = new PlanSemanalModel();
                tod._PlanSemanal.FechaIn = Convert.ToDateTime(fechain).ToString("dd/MM/yyyyy");
                tod._PlanSemanal.FechaFin = Convert.ToDateTime(fechafin).ToString("dd/MM/yyyyy");
                int promcan = ((todp._PlanPro.Canales - canalasig) / sem);
                double promtot = (todp._PlanPro.PesoTotal - pesoasig) / sem;
                tod._PlanSemanal.Canales = promcan;
                tod._PlanSemanal.PesoTotal = promtot;
                tod._PlanSemanal.PesoPromedio = todp._PlanPro.PesoPromedio;
                todp._PlanPro.PesoTotal -= pesoasig;
                todp._PlanPro.Canales -= canalasig;
                //tod._ListaDetalleSemanal = new List<DetalleSemanal>();
            }
            else
            {
                todp._PlanPro.Canales += tod._PlanSemanal.Canales - canalasig;

            }

            todp._TodSemanal._PlanSemanal = tod._PlanSemanal;

            tod._ListaDetalleSemanal = at._DetalleSemanal.Listar(todp._TodSemanal._PlanSemanal.Id)._ListaDetalleSemanal;

            if (tod._ListaDetalleSemanal == null || tod._ListaDetalleSemanal.Count == 0)
            {
                tod._ListaDetalleSemanal = new List<DetalleSemanal>();

                foreach (var item in todp._ListaPlanDetalle)
                {
                    tod._ListaDetalleSemanal.Add(new DetalleSemanal
                    {
                        ProductoCodigo = item.ProductoCodigo,
                        Porcentaje = item.Porcentaje,
                    });
                }
            }
            todp._TodSemanal = tod;
            return PartialView("~/Views/Comercial/Planeacion/Semanal/Detalle.cshtml", todp);
        }
        [HttpPost]
        [Route("Guardar")]
        public IActionResult Guardar(TodoPlanModel model)
        {
            var res = true;
            var a = model._TodSemanal._ListaDetalleSemanal;
            var b = model._TodSemanal._PlanSemanal;
            b.FechaIn = Convert.ToDateTime(b.FechaIn).ToString("yyyyMMdd");
            b.FechaFin = Convert.ToDateTime(b.FechaFin).ToString("yyyyMMdd");
            int s = at._Semanal.Insertar(b);

            foreach (var item in a)
            {
                item.fk_Semana = s;
                bool d = at._DetalleSemanal.Insertar(item);
            }
            return Json(new { success = true });
        }
    }
}
