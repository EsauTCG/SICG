using Plataforma_CG.Models.Operaciones.Inyeccion;
using System.Text.Json;

namespace Plataforma_CG.AccesoDatos.Operaciones.Inyeccion
{
    public class AccesoPermisos
    {
        HttpClient conn = new Conexion().ConAPI();
        JsonSerializerOptions jsonopt = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<PermisosModel> Manual(int usrid, string nip)
        {
            try
            {
                var response = await conn.GetAsync($"Permisos/Manual?usrid={usrid}&nip={nip}");
                response.EnsureSuccessStatusCode();

                var lista = await JsonSerializer.DeserializeAsync<PermisosModel>(
                    await response.Content.ReadAsStreamAsync(),
                    jsonopt
                );

                return lista;
            }
            catch (Exception)
            {
                // Si hay error, retornar un objeto vacío con fk_Permiso = 0
                return new PermisosModel
                {
                    usuarioId = 0,
                    fk_Permiso = 0,
                    nombre = "",
                    descripcion = ""
                };
            }
        }
    }
}