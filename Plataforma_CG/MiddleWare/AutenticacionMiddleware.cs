/*
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace Plataforma_CG.Middleware
{
    public class AutenticacionMiddleware
    {
        private readonly RequestDelegate _next;

        public AutenticacionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, Data.AppDbContextUsuarios db)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";

            // 🔓 Permitir rutas públicas
            if (path.Contains("/acceso/login") ||
                path.Contains("/acceso/noautorizado") ||
                path.Contains("/acceso/errorlogin") ||
                path.Contains("/acceso/logout") ||
                path.Contains("/acceso/modolimitado") || // ✅ NUEVA RUTA PERMITIDA
                path.Contains("/embarques/caseta") ||
                path.Contains("/api/") ||
                path.StartsWith("/css") ||
                path.StartsWith("/js") ||
                path.StartsWith("/images") ||
                path.StartsWith("/assets") ||
                path.StartsWith("assets") ||
                path == "/")
            {
                await _next(context);
                return;
            }

            // 🔐 Validar autenticación
            if (!context.User.Identity?.IsAuthenticated ?? true)
            {
                if (context.Request.Cookies[".AspNetCore.CookieAuth"] != null)
                    context.Response.Cookies.Delete(".AspNetCore.CookieAuth");

                context.Response.Redirect("/Acceso/ErrorLogin");
                return;
            }

            // 👤 Obtener datos del usuario actual
            var perfilIdClaim = context.User.Claims.FirstOrDefault(c => c.Type == "PerfilId")?.Value;
            if (string.IsNullOrEmpty(perfilIdClaim))
            {
                context.Response.Redirect("/Acceso/NoAutorizado");
                return;
            }

            int perfilId = int.Parse(perfilIdClaim);

            // ⚠️ MODO LIMITADO: Si el usuario está en modo limitado, redirigir a la vista especial
            var modoLimitadoClaim = context.User.Claims.FirstOrDefault(c => c.Type == "ModoLimitado")?.Value;
            if (modoLimitadoClaim == "true")
            {
                // Permitir solo logout y la página de modo limitado
                if (!path.Contains("/acceso/logout") && !path.Contains("/acceso/modolimitado"))
                {
                    context.Response.Redirect("/Acceso/ModoLimitado");
                    return;
                }

                await _next(context);
                return;
            }

            // 🧭 Determinar controlador y acción solicitada
            var routeData = context.GetRouteData();
            string controlador = routeData.Values["controller"]?.ToString() ?? "";
            string accion = routeData.Values["action"]?.ToString() ?? "";

            // 🔍 Verificar disponibilidad de SQL antes de consultar permisos
            bool sqlDisponible = await VerificarSQLDisponible(db);

            if (!sqlDisponible)
            {
                // Si SQL no está disponible, redirigir a modo limitado
                context.Response.Redirect("/Acceso/ModoLimitado");
                return;
            }

            // 🔎 Buscar permiso exacto o general del controlador
            bool tienePermiso = await db.Permisos
                .Include(p => p.Vista)
                .AnyAsync(p =>
                    p.PerfilId == perfilId &&
                    p.Vista.Controlador.ToLower() == controlador.ToLower() &&
                    (
                        string.IsNullOrEmpty(p.Vista.Accion) ||
                        p.Vista.Accion == "*" ||
                        p.Vista.Accion.ToLower() == accion.ToLower()
                    )
                );

            if (!tienePermiso)
            {
                context.Response.Redirect("/Acceso/NoAutorizado");
                return;
            }

            await _next(context);
        }

        private async Task<bool> VerificarSQLDisponible(Data.AppDbContextUsuarios db)
        {
            try
            {
                using (var command = db.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "SELECT 1";
                    command.CommandTimeout = 2; // 2 segundos timeout

                    await db.Database.OpenConnectionAsync();
                    await command.ExecuteScalarAsync();
                    await db.Database.CloseConnectionAsync();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
} */


using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace Plataforma_CG.Middleware
{
    public class AutenticacionMiddleware
    {
        private readonly RequestDelegate _next;

        public AutenticacionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        // ✅ NO BLOQUEA NADA: deja pasar todo
        public async Task InvokeAsync(HttpContext context, Data.AppDbContextUsuarios db)
        {
            await _next(context);
        }
    }
}


