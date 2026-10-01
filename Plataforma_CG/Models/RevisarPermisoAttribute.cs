using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Linq;
using System.Threading.Tasks;

namespace Plataforma_CG.Filters
{
    // Etiqueta personalizada para usar en los controladores
    public class RevisarPermisoAttribute : TypeFilterAttribute
    {
        public RevisarPermisoAttribute(string claveModulo, string tipoPermiso)
            : base(typeof(RevisarPermisoFilter))
        {
            Arguments = new object[] { claveModulo, tipoPermiso };
        }
    }

    // Logica interna del filtro
    public class RevisarPermisoFilter : IAsyncActionFilter
    {
        private readonly AppDbContextUsuarios _db;
        private readonly string _claveModulo;
        private readonly string _tipoPermiso;

        public RevisarPermisoFilter(AppDbContextUsuarios db, string claveModulo, string tipoPermiso)
        {
            _db = db;
            _claveModulo = claveModulo;
            _tipoPermiso = tipoPermiso;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var login = (context.HttpContext.User?.Identity?.Name ?? "").Trim();

            var (puedeLeer, puedeEscribir, puedeEliminar) =
                await PermisosHelper.ObtenerPermisoEfectivoAsync(_db, login, _claveModulo);

            bool tieneAcceso = _tipoPermiso.ToUpper() switch
            {
                "LEER" => puedeLeer,
                "ESCRIBIR" => puedeEscribir,
                "ELIMINAR" => puedeEliminar,
                _ => false
            };

            if (!tieneAcceso)
            {
                returnForbidden(context);
                return;
            }

            await next();
        }

        private static void returnForbidden(ActionExecutingContext context)
        {
            var isAjax = context.HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         context.HttpContext.Request.Headers["Accept"].ToString().Contains("application/json");

            if (isAjax)
            {
                context.Result = new JsonResult(new { ok = false, mensaje = "Acceso denegado. Permisos insuficientes." })
                {
                    StatusCode = 403
                };
            }
            else
            {
                context.Result = new ForbidResult();
            }
        }
    }
}