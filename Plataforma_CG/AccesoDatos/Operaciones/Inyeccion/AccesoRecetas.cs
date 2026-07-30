using Plataforma_CG.Models.Operaciones.Inyeccion;
using Plataforma_CG.ViewModels;
using System.Text;
using System.Text.Json;
namespace Plataforma_CG.AccesoDatos.Operaciones.Inyeccion
{
    public class AccesoRecetas
    {
        HttpClient connRead = new InyeccionAPI().Client();
        HttpClient connWrite = new InyeccionAPI().ClientWrite();
        JsonSerializerOptions jsonopt= new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        public async Task<List<ProductoModel>> ListarProductos(string plan)
        {
            var response = await connRead.GetAsync($"Receta/ListarPlantilla?plan={plan}");
            response.EnsureSuccessStatusCode();
            var lista = await JsonSerializer.DeserializeAsync<List<ProductoModel>>(await response.Content.ReadAsStreamAsync(), jsonopt);
            return lista;
        }
        public async Task<RecetaModel> Receta(string sku)
        {
            RecetaModel rec = new RecetaModel();
            try
            {
                var response = await connRead.GetAsync($"Receta/ConsultarReceta?sku={sku}");
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
            var response = await connRead.GetAsync("Receta/ListarTara");
            response.EnsureSuccessStatusCode();
            var lista = await JsonSerializer.DeserializeAsync<List<TaraModel>>(await response.Content.ReadAsStreamAsync(), jsonopt);
            return lista;
        }
        public async Task<string> InsertarEntrada(EntradaModel model)
        {
            var json = JsonSerializer.Serialize(model,new JsonSerializerOptions { PropertyNamingPolicy= JsonNamingPolicy.CamelCase});
            var body = new StringContent(json,Encoding.UTF8,"application/json");
            var response = await connWrite.PostAsync($"Entrada/InsertarSIGO",body);
            string dato = await response.Content.ReadAsStringAsync();
            return dato;
        }
        public async Task<EntradaModel> ConsultarEntrada(int id)
        {
            var response = await connWrite.GetAsync($"Entrada/Consultar?id={id}");

            var rawBody = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[ConsultarEntrada] Status: {(int)response.StatusCode}");
            Console.WriteLine($"[ConsultarEntrada] Raw response (first 2000 chars):");
            Console.WriteLine(rawBody?.Substring(0, Math.Min(2000, rawBody?.Length ?? 0)));

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ConsultarEntrada] ❌ HTTP {(int)response.StatusCode} - response: {rawBody}");
            }

            response.EnsureSuccessStatusCode();

            var dato = JsonSerializer.Deserialize<EntradaModel>(rawBody, jsonopt);

            return dato;
        }

    }
}
