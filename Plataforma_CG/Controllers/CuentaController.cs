using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    [AllowAnonymous]

    // ============================================================
    // Aunque el Controller se llama CuentaController,
    // públicamente usamos la ruta /Acceso/...
    // ============================================================
    [Route("Acceso")]
    public class CuentaController : Controller
    {
        private readonly Data.AppDbContextUsuarios _db;

        public CuentaController(Data.AppDbContextUsuarios db)
        {
            _db = db;
        }


        // ============================================================
        // REGISTRAR - GET
        //
        // URL:
        // https://localhost:44346/Acceso/Registrar
        //
        // VISTA:
        // Views/Acceso/Registrar.cshtml
        // ============================================================
        [HttpGet("Registrar")]
        public IActionResult Registrar()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View(
                "~/Views/Acceso/Registrar.cshtml",
                new RegistroUsuarioViewModel()
            );
        }


        // ============================================================
        // REGISTRAR - POST
        //
        // POST:
        // /Acceso/Registrar
        // ============================================================
        [HttpPost("Registrar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Registrar(
            RegistroUsuarioViewModel model)
        {
            // ========================================================
            // NORMALIZAR
            // ========================================================
            model.Nombre = model.Nombre?.Trim() ?? string.Empty;
            model.Usuario = model.Usuario?.Trim() ?? string.Empty;


            // ========================================================
            // VALIDAR MODELO
            // ========================================================
            if (!ModelState.IsValid)
            {
                return View(
                    "~/Views/Acceso/Registrar.cshtml",
                    model
                );
            }


            try
            {
                // ====================================================
                // VALIDAR USUARIO DUPLICADO
                // ====================================================
                string usuarioNormalizado =
                    model.Usuario.ToUpperInvariant();

                bool usuarioExiste = await _db.Usuarios
                    .AsNoTracking()
                    .AnyAsync(u =>
                        u.Usuario != null &&
                        u.Usuario.ToUpper() == usuarioNormalizado
                    );


                if (usuarioExiste)
                {
                    ModelState.AddModelError(
                        nameof(model.Usuario),
                        "Este nombre de usuario ya se encuentra registrado."
                    );

                    return View(
                        "~/Views/Acceso/Registrar.cshtml",
                        model
                    );
                }


                // ====================================================
                // BUSCAR PERFIL PENDIENTE
                // ====================================================
                var perfilPendiente = await _db.Perfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p =>
                        p.Nombre == "Pendiente"
                    );


                if (perfilPendiente == null)
                {
                    ModelState.AddModelError(
                        "",
                        "No se encuentra configurado el perfil Pendiente. " +
                        "Contacta al administrador del sistema."
                    );

                    return View(
                        "~/Views/Acceso/Registrar.cshtml",
                        model
                    );
                }


                // ====================================================
                // GENERAR HASH
                //
                // Se conserva SHA256 porque tu sistema actual
                // ya trabaja con este mismo formato.
                // ====================================================
                string passwordHash =
                    GenerarHashPassword(model.Password);


                // ====================================================
                // CREAR USUARIO
                // ====================================================
                var nuevoUsuario = new UsuarioSQL
                {
                    Nombre = model.Nombre,

                    Usuario = model.Usuario,

                    Password = passwordHash,


                    // ================================================
                    // USUARIO PENDIENTE
                    // ================================================
                    Activo = false,

                    PerfilId = perfilPendiente.Id,

                    EsVendedor = false,

                    IgnoraFiltroSerieTransferencias = false,


                    // ================================================
                    // SIN ALMACENES INICIALMENTE
                    // ================================================
                    AlmacenesPermitidos =
                        JsonSerializer.Serialize(
                            new List<string>()
                        ),


                    FechaModificacion = DateTime.Now
                };


                // ====================================================
                // GUARDAR
                // ====================================================
                _db.Usuarios.Add(nuevoUsuario);

                await _db.SaveChangesAsync();


                // ====================================================
                // MENSAJE
                // ====================================================
                TempData["RegistroExitoso"] =
                    "Tu cuenta fue registrada correctamente. " +
                    "Está pendiente de autorización por un administrador.";


                // ====================================================
                // REDIRECCIONAR
                // ====================================================
                return RedirectToAction(
                    nameof(RegistroExitoso)
                );
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine(ex);

                ModelState.AddModelError(
                    "",
                    "No fue posible registrar la cuenta. " +
                    "Verifica que el usuario no esté registrado."
                );

                return View(
                    "~/Views/Acceso/Registrar.cshtml",
                    model
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                ModelState.AddModelError(
                    "",
                    "Ocurrió un error al registrar tu cuenta. " +
                    "Intenta nuevamente."
                );

                return View(
                    "~/Views/Acceso/Registrar.cshtml",
                    model
                );
            }
        }


        // ============================================================
        // REGISTRO EXITOSO
        //
        // URL:
        // /Acceso/RegistroExitoso
        // ============================================================
        [HttpGet("RegistroExitoso")]
        public IActionResult RegistroExitoso()
        {
            if (TempData["RegistroExitoso"] == null)
            {
                return RedirectToAction(
                    nameof(Registrar)
                );
            }


            ViewBag.Mensaje =
                TempData["RegistroExitoso"];


            return View(
                "~/Views/Acceso/RegistroExitoso.cshtml"
            );
        }


        // ============================================================
        // HASH PASSWORD
        // ============================================================
        private static string GenerarHashPassword(
            string password)
        {
            using var sha = SHA256.Create();

            return Convert.ToBase64String(
                sha.ComputeHash(
                    Encoding.UTF8.GetBytes(password)
                )
            );
        }
    }


    // ================================================================
    // VIEWMODEL DE REGISTRO
    // ================================================================
    public class RegistroUsuarioViewModel
    {
        [Required(
            ErrorMessage = "El nombre es obligatorio."
        )]
        [StringLength(
            150,
            ErrorMessage =
                "El nombre no puede exceder 150 caracteres."
        )]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; }
            = string.Empty;



        [Required(
            ErrorMessage = "El usuario es obligatorio."
        )]
        [StringLength(
            50,
            MinimumLength = 4,
            ErrorMessage =
                "El usuario debe contener entre 4 y 50 caracteres."
        )]
        [Display(Name = "Usuario")]
        public string Usuario { get; set; }
            = string.Empty;



        [Required(
            ErrorMessage = "La contraseña es obligatoria."
        )]
        [StringLength(
            100,
            MinimumLength = 8,
            ErrorMessage =
                "La contraseña debe contener al menos 8 caracteres."
        )]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; }
            = string.Empty;



        [Required(
            ErrorMessage = "Confirma tu contraseña."
        )]
        [DataType(DataType.Password)]
        [Compare(
            nameof(Password),
            ErrorMessage =
                "Las contraseñas no coinciden."
        )]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmarPassword { get; set; }
            = string.Empty;
    }
}