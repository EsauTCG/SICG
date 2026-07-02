using Plataforma_CG.Models.Operaciones.Inyeccion;
using System.Text.Json;
namespace Plataforma_CG.AccesoDatos.Operaciones.Inyeccion
{
    public class AccesoLote
    {
        HttpClient conn = new Conexion().ConAPI();
        JsonSerializerOptions jsonopt= new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        public async Task<List<LotePlantillaModel>> ConsultarLotes()
        {
            var response = await conn.GetAsync("Lote/ListarLotePlaneacion");
            response.EnsureSuccessStatusCode();

            var lista = await JsonSerializer.DeserializeAsync<List<LotePlantillaModel>>(await response.Content.ReadAsStreamAsync(),jsonopt);
            return lista;
        }
    }
}
