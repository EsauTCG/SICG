using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Plataforma_CG.Controllers
{
    [Authorize]
    public sealed class InyeccionBitacoraController : Controller
    {
        [HttpGet("/Operaciones/BitacoraImpresionesInyeccion")]
        public IActionResult Index()
        {
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            return View("~/Views/Operaciones/BitacoraImpresionesInyeccion.cshtml");
        }
    }
}
