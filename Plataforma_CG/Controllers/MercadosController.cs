using Microsoft.AspNetCore.Authorization;
using Plataforma_CG.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    public class MercadosController : Controller
    {
        private readonly MercadoService _service = new MercadoService();


       
        public async Task<IActionResult> MateriaPrima()
        {
            var mercados = await _service.ObtenerDatosCommodities();
            return View(mercados); // Se buscará Views/Mercados/MateriaPrima.cshtml
        }

        
        
    }
}
