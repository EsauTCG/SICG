using Plataforma_CG.Services;
using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.ViewModels;

namespace Plataforma_CG.Controllers
{
    public class AutorizacionesController : Controller
    {

        public async Task<IActionResult> ControlPrecios()
        {
            return View("~/Views/Autorizaciones/ControlPrecios.cshtml");
        }

        public async Task<IActionResult> aut_credito()
        {
            return View("~/Views/Autorizaciones/aut_credito.cshtml");
        }


        public async Task<IActionResult> aut_precio()
        {
            return View("~/Views/Autorizaciones/aut_precio.cshtml");
        }


        public async Task<IActionResult> aut_presupuesto()
        {
            return View("~/Views/Autorizaciones/aut_presupuesto.cshtml");
        }
    }
}
