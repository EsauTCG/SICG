using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Models;

namespace Plataforma_CG.Controllers
{
    //[Authorize(Roles = "Administrador, Sistemas")]
    public class UsuariosADController : Controller
    {
        private readonly Data.AppDbContextUsuarios _db;

        public UsuariosADController(Data.AppDbContextUsuarios db)
        {
            _db = db;
        }

        // Listar usuarios AD
        public async Task<IActionResult> UsuariosADConfiguracion()
        {
            var usuariosAD = await _db.UsuariosAD
                .Include(u => u.Perfil)
                .ToListAsync();

            return View(usuariosAD);
        }

        // Editar perfil de usuario AD
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            var usuario = await _db.UsuariosAD.FindAsync(id);
            if (usuario == null)
                return NotFound();

            ViewBag.Perfiles = _db.Perfiles
                .Select(p => new { p.Id, p.Nombre })
                .ToList();

            return View(usuario);
        }

        [HttpPost]
        public async Task<IActionResult> Editar(UsuarioAD model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Perfiles = _db.Perfiles.ToList();
                TempData["Error"] = "El modelo no es válido. Revisa los errores mostrados.";
                return View(model);
            }

            var usuario = await _db.UsuariosAD.FindAsync(model.Id);
            if (usuario == null)
                return NotFound();

            usuario.Nombre = model.Nombre;
            usuario.Puesto = model.Puesto;
            usuario.PerfilId = model.PerfilId;

            // 🔹 Nuevos campos
            usuario.EsVendedor = model.EsVendedor;
            usuario.VendedorId = model.EsVendedor ? model.VendedorId : null;

            await _db.SaveChangesAsync();

            TempData["Exito"] = "Usuario AD actualizado correctamente.";
            return RedirectToAction("UsuariosADConfiguracion");
        }

    }
}
