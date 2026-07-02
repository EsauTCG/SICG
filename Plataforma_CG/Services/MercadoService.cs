using Newtonsoft.Json.Linq;
using Plataforma_CG.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public class MercadoService
    {
        // API Key Quandl/Nasdaq Data Link
        private readonly string apiKey = "qR_mn9_7nBKHLosykzsE";

        public async Task<List<Mercado>> ObtenerDatosCommodities()
        {
            var mercados = new List<Mercado>();

            var urls = new Dictionary<string, string>
            {
                { "Maíz", "https://www.quandl.com/api/v3/datasets/CHRIS/CME_C1.json?api_key=" + apiKey },
                { "Trigo", "https://www.quandl.com/api/v3/datasets/CHRIS/CME_W1.json?api_key=" + apiKey },
                { "Soja", "https://www.quandl.com/api/v3/datasets/CHRIS/CME_S1.json?api_key=" + apiKey }
            };

            using HttpClient client = new HttpClient();

            foreach (var kv in urls)
            {
                try
                {
                    // Solicitud HTTP
                    string json = await client.GetStringAsync(kv.Value);
                    JObject data = JObject.Parse(json);

                    decimal ultimoPrecio = data["dataset"]["data"][0][4].Value<decimal>(); // Precio cierre
                    decimal anterior = data["dataset"]["data"][1][4].Value<decimal>();
                    decimal cambio = ((ultimoPrecio - anterior) / anterior) * 100;
                    string tendencia = cambio > 0 ? "up" : (cambio < 0 ? "down" : "none");

                    mercados.Add(new Mercado
                    {
                        Nombre = kv.Key,
                        Precio = ultimoPrecio,
                        Cambio = cambio,
                        Tendencia = tendencia
                    });
                }
                catch (HttpRequestException)
                {
                    // Si falla la API, usa datos simulados
                    mercados.Add(new Mercado
                    {
                        Nombre = kv.Key,
                        Precio = 0,
                        Cambio = 0,
                        Tendencia = "none"
                    });
                }
                catch (Exception ex)
                {
                    // Log de cualquier otro error
                    Console.WriteLine($"Error procesando {kv.Key}: {ex.Message}");
                    mercados.Add(new Mercado
                    {
                        Nombre = kv.Key,
                        Precio = 0,
                        Cambio = 0,
                        Tendencia = "none"
                    });
                }
            }

            return mercados;
        }
    }
}
