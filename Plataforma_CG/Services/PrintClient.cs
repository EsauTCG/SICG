using Plataforma_CG.Services;
using System.Text;
using System.Text.Json;

public class PrintClient : IPrintClient
{
    private readonly HttpClient _http;
    private readonly ILogger<PrintClient> _logger;

    public PrintClient(HttpClient http, ILogger<PrintClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PrintRestResponse> PrintAsync(string baseSvcUrl, string innerJsonRequest)
    {
        // Endpoint WCF REST
        var url = $"{baseSvcUrl.TrimEnd('/')}/Print";

        // Wrapper EXACTO que espera el servicio: { "jsonRequest": "<json interno>" }
        var bodyObj = new { jsonRequest = innerJsonRequest };

        var json = JsonSerializer.Serialize(bodyObj);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.PostAsync(url, content);
        var text = await resp.Content.ReadAsStringAsync();

        _logger.LogInformation("PrintAsync HTTP {Status}. Body={Body} Resp={Resp}",
            (int)resp.StatusCode, json, text);

        return PrintRestResponse.FromRaw((int)resp.StatusCode, text);
    }
}
