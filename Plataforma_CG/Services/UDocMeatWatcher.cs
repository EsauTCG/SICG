using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma_CG.Data;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Plataforma_CG.Services
{
    public class UDocMeatWatcher : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<UDocMeatWatcher> _logger;
        private readonly IConfiguration _config;

        public UDocMeatWatcher(
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpFactory,
            ILogger<UDocMeatWatcher> logger,
            IConfiguration config)
        {
            _scopeFactory = scopeFactory;
            _httpFactory = httpFactory;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Intervalo base (ajústalo). Ej: cada 30 segundos.
            var intervalo = TimeSpan.FromSeconds(30);
            // Concurrencia máxima a SAP
            var maxParallel = 3;
            var sem = new SemaphoreSlim(maxParallel);

            _logger.LogInformation("UDocMeatWatcher iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Toma un batch pequeño de pendientes
                    var pendientes = await db.Subpedidos
                        .AsNoTracking()
                        .Where(s =>
                            !string.IsNullOrWhiteSpace(s.DocumentoSAP) &&
                            (s.U_DocMeat == null || s.U_DocMeat == ""))
                        .OrderBy(s => s.Id)
                        .Take(30)
                        .Select(s => new { s.Id, s.DocumentoSAP })
                        .ToListAsync(stoppingToken);

                    if (pendientes.Count == 0)
                    {
                        // Nada que hacer; duerme
                        await Task.Delay(intervalo, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("UDocMeatWatcher: {Count} pendientes.", pendientes.Count);

                    var client = _httpFactory.CreateClient("SapServiceLayer");
                    // Aseguramos sesión SL antes del batch
                    await EnsureSapSessionAsync(client, stoppingToken);

                    var tasks = new List<Task>();

                    foreach (var p in pendientes)
                    {
                        await sem.WaitAsync(stoppingToken);
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                // Normaliza a DocNum
                                if (!int.TryParse(p.DocumentoSAP?.Trim(), out var docNum) || docNum <= 0)
                                    return;

                                var valor = await ConsultarUDocMeatPorDocNumAsync(client, docNum, stoppingToken);
                                if (string.IsNullOrWhiteSpace(valor))
                                    return;

                                // Guardar si cambió
                                using var inner = _scopeFactory.CreateScope();
                                var db2 = inner.ServiceProvider.GetRequiredService<AppDbContext>();

                                var sub = await db2.Subpedidos.FirstOrDefaultAsync(x => x.Id == p.Id, stoppingToken);
                                if (sub != null && sub.U_DocMeat != valor)
                                {
                                    sub.U_DocMeat = valor.Length > 100 ? valor[..100] : valor;
                                    await db2.SaveChangesAsync(stoppingToken);
                                    _logger.LogInformation("UDocMeatWatcher: sub {Id} actualizado a {Val}.", p.Id, sub.U_DocMeat);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "UDocMeatWatcher: error procesando sub {Id}.", p.Id);
                            }
                            finally
                            {
                                sem.Release();
                            }
                        }, stoppingToken));
                    }

                    await Task.WhenAll(tasks);

                    // Pequeña espera antes del siguiente ciclo (con jitter)
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(200, 900));
                    await Task.Delay(intervalo + jitter, stoppingToken);
                }
                catch (TaskCanceledException) { /* apagando */ }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "UDocMeatWatcher: error de ciclo. Reintentando en 10s.");
                    try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch { }
                }
            }

            _logger.LogInformation("UDocMeatWatcher detenido.");
        }

        // ===== Service Layer helpers (copias ligeras de lo que ya usas) =====

        private async Task EnsureSapSessionAsync(HttpClient client, CancellationToken ct)
        {
            // Si ya hay cookie, asumimos válida (tu Service Layer reintenta si 401).
            if (client.DefaultRequestHeaders.Contains("Cookie"))
                return;

            var baseUrl = _config["SapServiceLayer:BaseUrl"]!.TrimEnd('/');
            var payload = new
            {
                UserName = _config["SapServiceLayer:UserName"],
                Password = _config["SapServiceLayer:Password"],
                CompanyDB = _config["SapServiceLayer:CompanyDB"]
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/Login")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            var resp = await client.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                var sessionCookie = cookies.FirstOrDefault(c => c.StartsWith("B1SESSION", StringComparison.OrdinalIgnoreCase));
                var routeCookie = cookies.FirstOrDefault(c => c.StartsWith("ROUTEID", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(sessionCookie))
                {
                    var cookieHeader = sessionCookie.Split(';')[0];
                    if (!string.IsNullOrEmpty(routeCookie)) cookieHeader += "; " + routeCookie.Split(';')[0];

                    client.DefaultRequestHeaders.Remove("Cookie");
                    client.DefaultRequestHeaders.Add("Cookie", cookieHeader);
                }
            }
        }

        private async Task<string?> ConsultarUDocMeatPorDocNumAsync(HttpClient client, int docNum, CancellationToken ct)
        {
            // Igual que tu Postman:
            // Orders?$select=DocEntry,DocNum,U_DocMeat&$filter=DocNum eq 123&$top=1
            var url = $"Orders?$select=DocEntry,DocNum,U_DocMeat&$filter=DocNum eq {docNum}&$top=1";

            // 1er intento
            var resp = await client.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await EnsureSapSessionAsync(client, ct);
                resp = await client.GetAsync(url, ct);
            }
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.GetArrayLength() == 0)
                return null;

            var first = arr[0];

            // U_DocMeat en header
            foreach (var p in first.EnumerateObject())
            {
                if (string.Equals(p.Name, "U_DocMeat", StringComparison.OrdinalIgnoreCase))
                {
                    var v = p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }

            // Si no viene en header, intenta en líneas
            if (first.TryGetProperty("DocEntry", out var de) && de.ValueKind == JsonValueKind.Number)
            {
                var docEntry = de.GetInt32();
                var urlLines = $"Orders({docEntry})?$select=DocEntry&$expand=DocumentLines($select=U_DocMeat,LineNum)";
                var respLines = await client.GetAsync(urlLines, ct);
                if (!respLines.IsSuccessStatusCode) return null;

                var jsonL = await respLines.Content.ReadAsStringAsync(ct);
                using var docL = JsonDocument.Parse(jsonL);

                if (docL.RootElement.TryGetProperty("DocumentLines", out var lines) &&
                    lines.ValueKind == JsonValueKind.Array)
                {
                    foreach (var line in lines.EnumerateArray())
                    {
                        foreach (var p in line.EnumerateObject())
                        {
                            if (string.Equals(p.Name, "U_DocMeat", StringComparison.OrdinalIgnoreCase))
                            {
                                var v = p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString();
                                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                            }
                        }
                    }
                }
            }

            return null;
        }
    }
}
