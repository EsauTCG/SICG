using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma_CG.Models;

namespace Plataforma_CG.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly Data.AppDbContextUsuarios _context;

        public HomeController(
            ILogger<HomeController> logger,
            Data.AppDbContextUsuarios context)
        {
            _logger = logger;
            _context = context;
        }

        [HttpGet]
        public IActionResult Index(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        public IActionResult Inicio()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [Authorize]
        [HttpGet]
        public IActionResult ObtenerCarouselInicio()
        {
            int perfilId = int.Parse(User.FindFirst("PerfilId")?.Value ?? "0");

            var banners = _context.CarouselPerfil
                .Where(x =>
                    x.Activo &&
                    (x.PerfilId == perfilId || x.PerfilId == 0)
                )
                .OrderBy(x => x.Orden)
                .Select(x => new
                {
                    imagen = x.ImagenUrl
                })
                .ToList();

            return Json(banners);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}
