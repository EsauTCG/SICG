using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
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

            var permiso = await (
                from u in _db.UsuarioSQL
                join p in _db.Perfiles on u.PerfilId equals p.Id
                join ppm in _db.PerfilPermisoModulo on p.Id equals ppm.PerfilId
                join m in _db.ModulosSistema on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == _claveModulo
                      && ppm.Activo
                      && m.Activo
                select new
                {
                    ppm.PuedeLeer,
                    ppm.PuedeEscribir,
                    ppm.PuedeEliminar
                }
            ).FirstOrDefaultAsync();

            bool tieneAcceso = false;

            if (permiso != null)
            {
                // Validacion dinamica segun el permiso solicitado
                tieneAcceso = _tipoPermiso.ToUpper() switch
                {
                    "LEER" => permiso.PuedeLeer,
                    "ESCRIBIR" => permiso.PuedeEscribir,
                    "ELIMINAR" => permiso.PuedeEliminar,
                    _ => false
                };
            }

            if (!tieneAcceso)
            {
                // Verifica si la peticion es AJAX/Fetch o carga de vista normal
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
                return;
            }

            // Permite que el codigo original del controlador se ejecute
            await next();
        }
    }
}