using Plataforma_CG.Models.SAP.JSON;
using System.Text;
using System.Text.Json;

namespace Plataforma_CG.AccesoDatos.JSON
{
    public class AccesoJSON
    {
        HttpClient api= new Conexion().ConAPI();
        public static string generarJsonSurtido(SolicitudSurtidoModel model)
        {
            var salida = new
            {
                model.ClienteId,
                model.NombreCliente,
                model.Comentario,
                model.FechaSurtido,
                model.Serie,
                model.ProcesoId,
                Multialmacen = model.Multialmacen
            .Select((item, index) => new
            {
                Linea = index,
                item.Articulo,
                item.Almacen,
                item.Cantidad,
                item.Precio
            })
            .ToList()
            };
            var opciones = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            return JsonSerializer.Serialize(salida, opciones);
        }
        public async Task<string> EnviarJSON(string json)
        {
            var body = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await api.PostAsync($"ConexionAPI/Insertar", body);
            response.EnsureSuccessStatusCode();

            // Leer el stream directamente y deserializar en un paso (mejor que leer como string)
            //var lista = await JsonSerializer.DeserializeAsync<string>(await response.Content.ReadAsStreamAsync());
            string res = await response.Content.ReadAsStringAsync();


            return res;
        }
    }
}
