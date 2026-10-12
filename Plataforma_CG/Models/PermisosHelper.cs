using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plataforma_CG.Models
{
    // Permiso efectivo de un módulo: usuario sobrescribe al perfil.
    // Reutilizado por el [RevisarPermiso] y por los endpoints que consultan.
    public static class PermisosHelper
    {
        public static Task<(bool PuedeLeer, bool PuedeEscribir, bool PuedeEliminar)>
            ObtenerPermisoEfectivoAsync(DbContext db, string login, string claveModulo)
            => ObtenerPermisoEfectivoAsync(db, new[] { login }, claveModulo);

        /// <summary>
        /// Igual que la sobrecarga de un solo login, pero acepta varias formas del
        /// mismo usuario (con/sin dominio, con/sin correo) para no perder las
        /// normalizaciones que hacia cada controller.
        /// </summary>
        public static async Task<(bool PuedeLeer, bool PuedeEscribir, bool PuedeEliminar)>
            ObtenerPermisoEfectivoAsync(DbContext db, IEnumerable<string> logins, string claveModulo)
        {
            var candidatos = (logins ?? Enumerable.Empty<string>())
                .Select(x => (x ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (candidatos.Count == 0)
                return (false, false, false);

            var modulos = db.Set<ModulosSistema>();
            var perfiles = db.Set<Perfil>();
            var perfilPermisos = db.Set<PerfilPermisoModulo>();
            var usuarioPermisos = db.Set<UsuarioPermisoModulo>();
            var usuariosSql = db.Set<UsuarioSQL>();

            // El login SQL guarda ClaimTypes.Name = Nombre (nombre completo), no Usuario,
            // mientras que UsuarioKey siempre se guarda como Usuario. Se resuelve la fila
            // de UsuarioSQL para agregar la clave canonica a los candidatos y usar su Id
            // en la consulta de perfil, evitando depender de coincidencias por nombre.
            var usuarioSql = await (
                from u in usuariosSql
                where candidatos.Contains(u.Usuario) || candidatos.Contains(u.Nombre)
                select new { u.Id, u.Usuario, u.Nombre }
            ).FirstOrDefaultAsync();

            if (usuarioSql != null)
            {
                foreach (var clave in new[] { usuarioSql.Usuario, usuarioSql.Nombre })
                {
                    var limpia = (clave ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(limpia) && !candidatos.Contains(limpia))
                        candidatos.Add(limpia);
                }
            }

            // 1) Permiso individual por usuario (sobrescribe al perfil)
            var permisoUsuario = await (
                from upm in usuarioPermisos
                join m in modulos on upm.ModuloId equals m.Id
                where candidatos.Contains(upm.UsuarioKey)
                      && m.Clave == claveModulo
                      && upm.Activo
                      && m.Activo
                select new { upm.PuedeLeer, upm.PuedeEscribir, upm.PuedeEliminar }
            ).FirstOrDefaultAsync();

            if (permisoUsuario != null)
                return (permisoUsuario.PuedeLeer, permisoUsuario.PuedeEscribir, permisoUsuario.PuedeEliminar);

            // 2) Sin permiso individual → permiso del perfil (comportamiento original)
            var consultaPerfil =
                from u in usuariosSql
                join p in perfiles on u.PerfilId equals p.Id
                join ppm in perfilPermisos on p.Id equals ppm.PerfilId
                join m in modulos on ppm.ModuloId equals m.Id
                where (usuarioSql != null ? u.Id == usuarioSql.Id : (candidatos.Contains(u.Usuario) || candidatos.Contains(u.Nombre)))
                      && m.Clave == claveModulo
                      && ppm.Activo
                      && m.Activo
                select new
                {
                    ppm.PuedeLeer,
                    ppm.PuedeEscribir,
                    ppm.PuedeEliminar
                };

            var permisoPerfil = await consultaPerfil.FirstOrDefaultAsync();

            if (permisoPerfil == null)
                return (false, false, false);

            return (permisoPerfil.PuedeLeer, permisoPerfil.PuedeEscribir, permisoPerfil.PuedeEliminar);
        }
    }
}