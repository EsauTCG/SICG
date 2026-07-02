using Microsoft.AspNetCore.Http;
using Plataforma_CG.Middleware;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Plataforma_CG.Middleware
{
    public class PermisosMiddleware
    {
        private readonly RequestDelegate _next;

        public PermisosMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Solo proteger si el usuario está autenticado
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var path = context.Request.Path.Value?.ToLower();

                /*
                // Ejemplo de reglas
                if (path != null)
                {
                    if (path.Contains("/configuracion") && !context.User.IsInRole("Administrador"))
                    {
                    context.Response.Redirect("/Acceso/NoAutorizado");
                        return;
                    }

                    if (path.Contains("/operaciones") && !(context.User.IsInRole("Operador") || context.User.IsInRole("Administrador")))
                    {
                    context.Response.Redirect("/Acceso/NoAutorizado");
                        return;
                    }


                    if (path.Contains("/Comercial") && !(context.User.IsInRole("Compras") || context.User.IsInRole("Ventas") || context.User.IsInRole("Administrador")))
                    {
                        context.Response.Redirect("/Acceso/NoAutorizado");
                        return;
                    }

                    if (path.Contains("/Autorizaciones") && !(context.User.IsInRole("Compras") || context.User.IsInRole("Ventas") || context.User.IsInRole("Administrador")))
                    {
                        context.Response.Redirect("/Acceso/NoAutorizado");
                        return;
                    }
                }*/
            }

            await _next(context);
        }
    }
}

