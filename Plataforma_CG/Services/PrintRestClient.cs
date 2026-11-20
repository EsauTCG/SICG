
using System;
using System.Collections.Generic;
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

        // =====================================================
        // OBTENER IMPRESORAS INSTALADAS
        // =====================================================
        public async Task<string[]> InstalledPrintersAsync(
            string baseUrl)
        {
            var url =
                $"{baseUrl.TrimEnd('/')}/InstalledPrinters";

            using var req = new HttpRequestMessage(
                HttpMethod.Get, url);

            using var res = await _http.SendAsync(req);

            var raw = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"InstalledPrinters HTTP " +
                    $"{(int)res.StatusCode}: {raw}");
            }

            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<string>();

            if (raw.TrimStart().StartsWith("<"))
            {
                throw new Exception(
                    "InstalledPrinters regresó HTML: " +
                    Recortar(raw));
            }

            try
            {
                using var doc = JsonDocument.Parse(raw);

                var root = DesempaquetarJson(doc.RootElement);

                // Respuesta: ["Zebra", "ZD AUDITORIA"]
                if (root.ValueKind == JsonValueKind.Array)
                {
                    var impresoras = new List<string>();

                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var nombre = item.GetString();

                            if (!string.IsNullOrWhiteSpace(nombre))
                                impresoras.Add(nombre);
                        }
                    }

                    return impresoras.ToArray();
                }

                // Respuesta tipo objeto:
                // {"InstalledPrintersResult":["Zebra"]}
                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Value.ValueKind !=
                            JsonValueKind.Array)
                            continue;

                        var impresoras = new List<string>();

                        foreach (var item in
                                 prop.Value.EnumerateArray())
                        {
                            if (item.ValueKind ==
                                JsonValueKind.String)
                            {
                                var nombre = item.GetString();

                                if (!string.IsNullOrWhiteSpace(nombre))
                                    impresoras.Add(nombre);
                            }
                        }

                        if (impresoras.Count > 0)
                            return impresoras.ToArray();
                    }
                }

                throw new Exception(
                    "Formato inesperado de impresoras: " +
                    Recortar(raw));
            }
            catch (JsonException ex)
            {
                throw new Exception(
                    "Respuesta JSON inválida en " +
                    "InstalledPrinters: " + Recortar(raw), ex);
            }
        }

        // =====================================================
        // IMPRIMIR ETIQUETA
        // =====================================================
        public async Task<PrintResponse> PrintAsync(
            string baseUrl,
            string jsonRequest)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new PrintResponse(
                    -1, "URL del servicio de impresión vacía.");
            }

            if (string.IsNullOrWhiteSpace(jsonRequest))
            {
                return new PrintResponse(
                    -1, "JSON de impresión vacío.");
            }

            var url = $"{baseUrl.TrimEnd('/')}/Print";

            // Mantener formato compatible con WCF:
            // {"jsonRequest":"{...}"}
            var payload = JsonSerializer.Serialize(
                new { jsonRequest });

            try
            {
                using var req = new HttpRequestMessage(
                    HttpMethod.Post, url);

                req.Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json");

                using var res = await _http.SendAsync(req);

                var raw = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    return new PrintResponse(
                        -1,
                        $"HTTP {(int)res.StatusCode}: " +
                        Recortar(raw));
                }

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return new PrintResponse(
                        -999,
                        "El servicio devolvió una " +
                        "respuesta vacía. Impresión no confirmada.");
                }

                if (raw.TrimStart().StartsWith("<"))
                {
                    return new PrintResponse(
                        -999,
                        "El servicio devolvió HTML: " +
                        Recortar(raw));
                }

                // =================================================
                // CORRECCIÓN PRINCIPAL:
                // Aceptar respuesta JSON como objeto o string.
                // =================================================
                using var doc = JsonDocument.Parse(raw);

                var root = DesempaquetarJson(doc.RootElement);

                // WCF puede envolver el resultado:
                // {"PrintResult":{"Estado":0,"Mensaje":"OK"}}
                if (root.ValueKind == JsonValueKind.Object &&
                    !TienePropiedad(root, "Estado") &&
                    TryObtenerPropiedad(
                        root, "PrintResult", out var resultado))
                {
                    root = DesempaquetarJson(resultado);
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new PrintResponse(
                        -999,
                        "Formato inesperado del servicio: " +
                        Recortar(root.GetRawText()));
                }

                if (!TryObtenerPropiedad(
                    root, "Estado", out var estadoJson))
                {
                    return new PrintResponse(
                        -999,
                        "La respuesta no contiene Estado: " +
                        Recortar(root.GetRawText()));
                }

                int estado;

                if (estadoJson.ValueKind ==
                    JsonValueKind.Number)
                {
                    if (!estadoJson.TryGetInt32(out estado))
                    {
                        return new PrintResponse(
                            -999,
                            "Estado numérico inválido.");
                    }
                }
                else if (
                    estadoJson.ValueKind ==
                    JsonValueKind.String)
                {
                    if (!int.TryParse(
                        estadoJson.GetString(), out estado))
                    {
                        return new PrintResponse(
                            -999,
                            "Estado no numérico: " +
                            estadoJson.GetString());
                    }
                }
                else
                {
                    return new PrintResponse(
                        -999,
                        "Tipo de Estado inválido: " +
                        estadoJson.ValueKind);
                }

                string mensaje = "";

                if (TryObtenerPropiedad(
                    root, "Mensaje", out var mensajeJson))
                {
                    mensaje = LeerTexto(mensajeJson);
                }

                if (string.IsNullOrWhiteSpace(mensaje))
                {
                    mensaje = estado == 0
                        ? "Servicio de impresión confirmó OK."
                        : "El servicio reportó error " + estado;
                }

                return new PrintResponse(estado, mensaje);
            }
            catch (JsonException ex)
            {
                return new PrintResponse(
                    -999,
                    "Respuesta JSON inválida: " + ex.Message);
            }
            catch (HttpRequestException ex)
            {
                return new PrintResponse(
                    -1,
                    "Error de conexión con PrintRestService. " +
                    "Resultado de impresión no confirmado: " +
                    ex.Message);
            }
            catch (TaskCanceledException ex)
            {
                return new PrintResponse(
                    -1,
                    "Tiempo de espera agotado. " +
                    "La impresión pudo haberse enviado. " +
                    ex.Message);
            }
            catch (Exception ex)
            {
                return new PrintResponse(
                    -999,
                    "Error procesando respuesta de impresión: " +
                    ex.Message);
            }
        }

        // =====================================================
        // EXTRAER OBJETO JSON O JSON COMO TEXTO
        // =====================================================
        private static JsonElement DesempaquetarJson(
            JsonElement elemento)
        {
            // Máximo 5 niveles para evitar ciclos
            // o respuestas anidadas inesperadas.
            for (int i = 0; i < 5; i++)
            {
                if (elemento.ValueKind !=
                    JsonValueKind.String)
                {
                    break;
                }

                var texto = elemento.GetString();

                if (string.IsNullOrWhiteSpace(texto))
                    break;

                var trimmed = texto.TrimStart();

                if (!trimmed.StartsWith("{") &&
                    !trimmed.StartsWith("[") &&
                    !trimmed.StartsWith("\""))
                {
                    break;
                }

                try
                {
                    using var doc = JsonDocument.Parse(texto);

                    elemento = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    break;
                }
            }

            return elemento;
        }

        // =====================================================
        // LECTURA SEGURA DE VALORES JSON
        // =====================================================
        private static string LeerTexto(JsonElement valor)
        {
            return valor.ValueKind switch
            {
                JsonValueKind.String =>
                    valor.GetString() ?? "",

                JsonValueKind.Null => "",

                JsonValueKind.Undefined => "",

                JsonValueKind.Object =>
                    valor.GetRawText(),

                JsonValueKind.Array =>
                    valor.GetRawText(),

                _ => valor.ToString()
            };
        }

        // =====================================================
        // BUSCAR PROPIEDADES SIN DIFERENCIAR MAYÚSCULAS
        // =====================================================
        private static bool TryObtenerPropiedad(
            JsonElement objeto,
            string nombre,
            out JsonElement valor)
        {
            valor = default;

            if (objeto.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var prop in objeto.EnumerateObject())
            {
                if (string.Equals(
                    prop.Name,
                    nombre,
                    StringComparison.OrdinalIgnoreCase))
                {
                    valor = prop.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TienePropiedad(
            JsonElement objeto,
            string nombre)
        {
            return TryObtenerPropiedad(
                objeto, nombre, out _);
        }

        // =====================================================
        // LIMITAR TEXTO DE ERRORES
        // =====================================================
        private static string Recortar(
            string texto,
            int max = 600)
        {
            if (string.IsNullOrEmpty(texto))
                return "";

            return texto.Length <= max
                ? texto
                : texto.Substring(0, max) + "...";
        }
    }
}
