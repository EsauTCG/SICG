using Plataforma_CG.Models.Operaciones.Inyeccion;
using Plataforma_CG.ViewModels;
using System.Text;
using System.Text.Json;
namespace Plataforma_CG.AccesoDatos.Operaciones.Inyeccion
{
    public class AccesoRecetas
    {
        HttpClient conn = new Conexion().ConAPI();
        JsonSerializerOptions jsonopt= new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        public async Task<List<ProductoModel>> ListarProductos(string plan)
        {
            var response = await conn.GetAsync($"Receta/ListarPlantilla?plan={plan}");
            response.EnsureSuccessStatusCode();
            var lista = await JsonSerializer.DeserializeAsync<List<ProductoModel>>(await response.Content.ReadAsStreamAsync(), jsonopt);
            return lista;
        }
        public async Task<RecetaModel> Receta(string sku)
        {
            RecetaModel rec = new RecetaModel();
            try
            {
                var response = await conn.GetAsync($"Receta/ConsultarReceta?sku={sku}");
                response.EnsureSuccessStatusCode();
                string responsejson = await response.Content.ReadAsStringAsync();
                rec = JsonSerializer.Deserialize<RecetaModel>(responsejson, jsonopt);
            }
            catch (Exception)
            {
            }
            return rec;
        }
        public async Task<List<TaraModel>> Taras()
        {
            var response = await conn.GetAsync("Receta/ListarTara");
            response.EnsureSuccessStatusCode();
            var lista = await JsonSerializer.DeserializeAsync<List<TaraModel>>(await response.Content.ReadAsStreamAsync(), jsonopt);
            return lista;
        }
        public async Task<string> InsertarEntrada(EntradaModel model)
        {
            var json = JsonSerializer.Serialize(model,new JsonSerializerOptions { PropertyNamingPolicy= JsonNamingPolicy.CamelCase});
            var body = new StringContent(json,Encoding.UTF8,"application/json");
            var response = await conn.PostAsync($"Entrada/InsertarSIGO",body);
            string dato = await response.Content.ReadAsStringAsync();
            return dato;
        }
        public async Task<EntradaModel> ConsultarEntrada(int id)
        {
            var response = await conn.GetAsync($"Entrada/Consultar?id={id}");
            response.EnsureSuccessStatusCode();

            var dato = await JsonSerializer.DeserializeAsync<EntradaModel>(
                await response.Content.ReadAsStreamAsync(),
                jsonopt
            );

            return dato;
        }

    }
}
