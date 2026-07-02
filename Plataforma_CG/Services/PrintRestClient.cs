// Services/PrintRestClient.cs
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public class PrintRestClient
    {
        private readonly HttpClient _http;

        public PrintRestClient(HttpClient http)
        {
            _http = http;
        }

        public record PrintResponse(int Estado, string Mensaje);

        public async Task<string[]> InstalledPrintersAsync(string baseUrl)
        {
            var url = $"{baseUrl.TrimEnd('/')}/InstalledPrinters";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var res = await _http.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"InstalledPrinters HTTP {(int)res.StatusCode}: {raw}");

            // Si llega HTML, lo detectas aquí y te da pista inmediata
            var trimmed = (raw ?? "").TrimStart();
            if (trimmed.StartsWith("<"))
                throw new Exception("InstalledPrinters regresó HTML (URL incorrecta o servicio no-JSON): " +
                                    (raw.Length > 250 ? raw.Substring(0, 250) : raw));

            // Caso 1: viene como array JSON normal
            try
            {
                return JsonSerializer.Deserialize<string[]>(raw) ?? Array.Empty<string>();
            }
            catch
            {
                // Caso 2: viene como string que contiene JSON
                var inner = JsonSerializer.Deserialize<string>(raw);
                return inner is null
                    ? Array.Empty<string>()
                    : (JsonSerializer.Deserialize<string[]>(inner) ?? Array.Empty<string>());
            }
        }

        public async Task<PrintResponse> PrintAsync(string baseUrl, string jsonRequest)
        {
            var url = $"{baseUrl.TrimEnd('/')}/Print";

            // OJO: aquí jsonRequest ya debe ser el JSON interno (no wrapper)
            var payload = JsonSerializer.Serialize(new { jsonRequest });

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                return new PrintResponse(-1, $"HTTP {(int)res.StatusCode}: {raw}");

            // raw típico: "\"{\\\"Estado\\\":0,\\\"Mensaje\\\":\\\"OK\\\"}\""
            var innerJson = JsonSerializer.Deserialize<string>(raw) ?? raw;

            try
            {
                return JsonSerializer.Deserialize<PrintResponse>(innerJson)
                       ?? new PrintResponse(-999, "Respuesta inválida");
            }
            catch
            {
                return new PrintResponse(-999, "No se pudo parsear respuesta: " + innerJson);
            }
        }
    }
}
