using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Models;
using System.DirectoryServices.AccountManagement;
using System.Security.Claims;

namespace Plataforma_CG.Controllers
{
    public class AccesoController : Controller
    {
        private readonly Data.AppDbContextUsuarios _db;
        private readonly IConfiguration _config;

        public AccesoController(Data.AppDbContextUsuarios db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        [HttpGet]
        public IActionResult NoAutorizado() => View();

        [HttpGet]
        public IActionResult ErrorLogin() => View();

        [HttpGet]
        public IActionResult Login(string returnUrl = null, int expirada = 0)
        {
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.SesionExpirada = expirada == 1;
            return View();
        }

        [HttpGet]
        public IActionResult EstadoSesion()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                return Json(new
                {
                    activa = true,
                    usuario = User.Identity.Name
                });
            }

            var returnUrl = Request.Headers["X-SIGO-Current-Url"].ToString();

            if (string.IsNullOrWhiteSpace(returnUrl) ||
                !returnUrl.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
                returnUrl.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            {
                returnUrl = Url.Action("Inicio", "Home") ?? "/Home/Inicio";
            }

            return Unauthorized(new
            {
                activa = false,
                sessionExpired = true,
                message = "Tu sesión expiró. Vuelve a iniciar sesión.",
                redirectUrl = Url.Action("Index", "Home", new
                {
                    expirada = 1,
                    returnUrl
                })
            });
        }

        [HttpGet]
        public IActionResult ModoLimitado() => View();

        [HttpGet]
        public async Task<IActionResult> Logout(string? returnUrl = null, int expirada = 0)
        {
            await HttpContext.SignOutAsync("CookieAuth");

            if (Request.Cookies[".AspNetCore.CookieAuth"] != null)
                Response.Cookies.Delete(".AspNetCore.CookieAuth");

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return RedirectToAction("Index", "Home", new
                {
                    expirada = expirada == 1 ? 1 : 0,
                    returnUrl
                });
            }

            return RedirectToAction("Index", "Home", new
            {
                expirada = expirada == 1 ? 1 : 0
            });
        }

        [HttpPost]
        public async Task<IActionResult> Login([FromBody] LoginViewModel model)
        {
                if (!ModelState.IsValid)
                    return Json(new { success = false, message = "Por favor ingresa tus credenciales." });

                var returnUrl = model.ReturnUrl;

                if (!string.IsNullOrEmpty(returnUrl) && !Url.IsLocalUrl(returnUrl))
                {
                    returnUrl = null;
                }

                bool sqlDisponible = false;

                try
                {
                    sqlDisponible = await VerificarConexionSQL();

                    using (var context = new PrincipalContext(ContextType.Domain, "carnesg.net"))
                    {
                        bool validoAD = context.ValidateCredentials(model.Usuario, model.Password);

                        if (validoAD)
                        {
                            if (!sqlDisponible)
                            {
                                var claimsLimitado = new List<Claim>
                        {
                            new Claim(ClaimTypes.Name, model.Usuario),
                            new Claim("Correo", model.Usuario),
                            new Claim(ClaimTypes.Role, "ModoLimitado"),
                            new Claim("PerfilId", "0"),
                            new Claim("AuthType", "AD"),
                            new Claim("ModoLimitado", "true")
                        };

                                var identityLimitado = new ClaimsIdentity(claimsLimitado, "CookieAuth");
                                await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(identityLimitado),
                                    new AuthenticationProperties
                                    {
                                        IsPersistent = true,
                                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                                        AllowRefresh = true
                                    });

                                return Json(new
                                {
                                    success = true,
                                    redirectUrl = !string.IsNullOrEmpty(returnUrl)
                                        ? returnUrl
                                        : Url.Action("ModoLimitado", "Acceso"),
                                    modoLimitado = true
                                });
                            }

                            var userAd = await _db.UsuariosAD
                                .Include(u => u.Perfil)
                                .FirstOrDefaultAsync(u => u.UsuarioAd == model.Usuario);

                            if (userAd == null)
                                return Json(new { success = false, message = "Usuario AD no registrado en el sistema." });

                            var perfilNombre = userAd.Perfil?.Nombre ?? "SinPerfil";
                            var perfilId = userAd.PerfilId.ToString();

                            var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, model.Usuario),
                        new Claim("Correo", model.Usuario),
                        new Claim(ClaimTypes.Role, perfilNombre),
                        new Claim("PerfilId", perfilId),
                        new Claim("AuthType", "AD"),
                        new Claim("ModoLimitado", "false"),
                        new Claim("VendedorId", (userAd.VendedorId ?? 0).ToString())
                    };

                            var identity = new ClaimsIdentity(claims, "CookieAuth");
                            await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(identity),
                                new AuthenticationProperties
                                {
                                    IsPersistent = true,
                                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                                    AllowRefresh = true
                                });

                            return Json(new
                            {
                                success = true,
                                redirectUrl = !string.IsNullOrEmpty(returnUrl)
                                    ? returnUrl
                                    : Url.Action("Inicio", "Home")
                            });
                        }
                    }

                    if (!sqlDisponible)
                        return Json(new { success = false, message = "Servicio de autenticación no disponible. Intente más tarde." });

                    var user = await _db.Usuarios
                        .Include(u => u.Perfil)
                        .FirstOrDefaultAsync(u => u.Usuario == model.Usuario && u.Activo);

                    if (user == null)
                        return Json(new { success = false, message = "Usuario no encontrado o inactivo." });

                    using var sha = System.Security.Cryptography.SHA256.Create();
                    var hash = Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(model.Password)));

                    if (hash != user.Password)
                        return Json(new { success = false, message = "Contraseña incorrecta." });

                    var perfilSQL = user.Perfil?.Nombre ?? "Usuario";
                    var perfilIdSQL = user.PerfilId.ToString();

                    var claimsSql = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Nombre ?? user.Usuario),
                new Claim("Correo", user.Usuario),
                new Claim(ClaimTypes.Role, perfilSQL),
                new Claim("PerfilId", perfilIdSQL),
                new Claim("AuthType", "SQL"),
                new Claim("ModoLimitado", "false"),
                new Claim("VendedorId", (user.VendedorId ?? 0).ToString())
            };

                    var identitySql = new ClaimsIdentity(claimsSql, "CookieAuth");
                await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(identitySql),
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                        AllowRefresh = true
                    });

                return Json(new
                    {
                        success = true,
                        redirectUrl = !string.IsNullOrEmpty(returnUrl)
                            ? returnUrl
                            : Url.Action("Inicio", "Home")
                    });
                }
                catch (PrincipalServerDownException)
                {
                    if (!sqlDisponible)
                        return Json(new { success = false, message = "Todos los servicios de autenticación están temporalmente fuera de servicio." });

                    var user = await _db.Usuarios
                        .Include(u => u.Perfil)
                        .FirstOrDefaultAsync(u => u.Usuario == model.Usuario && u.Activo);

                    if (user == null)
                        return Json(new { success = false, message = "Usuario no encontrado o inactivo." });

                    using var sha = System.Security.Cryptography.SHA256.Create();
                    var hash = Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(model.Password)));

                    if (hash != user.Password)
                        return Json(new { success = false, message = "Contraseña incorrecta." });

                    var perfilSQL = user.Perfil?.Nombre ?? "Usuario";
                    var perfilIdSQL = user.PerfilId.ToString();

                    var claimsSql = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Nombre ?? user.Usuario),
                new Claim("Correo", user.Usuario),
                new Claim(ClaimTypes.Role, perfilSQL),
                new Claim("PerfilId", perfilIdSQL),
                new Claim("AuthType", "SQL"),
                new Claim("ModoLimitado", "false"),
                new Claim("VendedorId", (user.VendedorId ?? 0).ToString())
            };

                    var identitySql = new ClaimsIdentity(claimsSql, "CookieAuth");
                await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(identitySql),
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                        AllowRefresh = true
                    });

                return Json(new
                    {
                        success = true,
                        redirectUrl = !string.IsNullOrEmpty(returnUrl)
                            ? returnUrl
                            : Url.Action("Inicio", "Home")
                    });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Error en el inicio de sesión: " + ex.Message });
                }
        }

        private async Task<bool> VerificarConexionSQL()
        {
            try
            {
                // Intenta una operación simple en la BD con timeout corto
                using (var command = _db.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "SELECT 1";
                    command.CommandTimeout = 3; // 3 segundos timeout

                    await _db.Database.OpenConnectionAsync();
                    await command.ExecuteScalarAsync();
                    await _db.Database.CloseConnectionAsync();
                }
                return true;
            }
            catch (Exception ex)
            {
                // Log del error si tienes logger configurado
                // _logger.LogWarning($"SQL no disponible: {ex.Message}");
                return false;
            }
        }
    }
}