using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using System.Threading.Tasks;

namespace Plataforma_CG.Models
{
    // Permiso efectivo de un módulo: usuario sobrescribe al perfil.
    // Reutilizado por el [RevisarPermiso] y por los endpoints que consultan.
    public static class PermisosHelper
    {
        public static async Task<(bool PuedeLeer, bool PuedeEscribir, bool PuedeEliminar)>
            ObtenerPermisoEfectivoAsync(DbContext db, string login, string claveModulo)
        {
            login = (login ?? "").Trim();
            if (string.IsNullOrWhiteSpace(login))
                return (false, false, false);

            var modulos = db.Set<ModulosSistema>();
            var perfiles = db.Set<Perfil>();
            var perfilPermisos = db.Set<PerfilPermisoModulo>();
            var usuarioPermisos = db.Set<UsuarioPermisoModulo>();
            var usuariosSql = db.Set<UsuarioSQL>();

            // 1) Permiso individual por usuario (sobrescribe al perfil)
            var permisoUsuario = await (
                from upm in usuarioPermisos
                join m in modulos on upm.ModuloId equals m.Id
                where upm.UsuarioKey == login
                      && m.Clave == claveModulo
                      && upm.Activo
                      && m.Activo
                select new { upm.PuedeLeer, upm.PuedeEscribir, upm.PuedeEliminar }
            ).FirstOrDefaultAsync();

            if (permisoUsuario != null)
                return (permisoUsuario.PuedeLeer, permisoUsuario.PuedeEscribir, permisoUsuario.PuedeEliminar);

            // 2) Sin permiso individual → permiso del perfil (comportamiento original)
            var permisoPerfil = await (
                from u in usuariosSql
                join p in perfiles on u.PerfilId equals p.Id
                join ppm in perfilPermisos on p.Id equals ppm.PerfilId
                join m in modulos on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == claveModulo
                      && ppm.Activo
                      && m.Activo
                select new
                {
                    ppm.PuedeLeer,
                    ppm.PuedeEscribir,
                    ppm.PuedeEliminar
                }
            ).FirstOrDefaultAsync();

            if (permisoPerfil == null)
                return (false, false, false);

            return (permisoPerfil.PuedeLeer, permisoPerfil.PuedeEscribir, permisoPerfil.PuedeEliminar);
        }
    }
}