using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Text.Json;

namespace Plataforma_CG.Services
{
    public class EnviarEntregaService : IEnviarEntregaService
    {
        private readonly IEntregasSapService _data;
        private readonly ISapServiceLayerClient _sap;
        private readonly AppDbContext _db;
        private readonly ILogger<EnviarEntregaService> _logger;

        public EnviarEntregaService(
            IEntregasSapService data,
            ISapServiceLayerClient sap,
            AppDbContext db,
            ILogger<EnviarEntregaService> logger)
        {
            _data = data;
            _sap = sap;
            _db = db;
            _logger = logger;
        }

        public async Task<(bool ok, string msg)> EnviarAsync(string referencia, string source, CancellationToken ct)
        {
            var sapEndpoint = "DeliveryNotes";

            try
            {
                var json = await _data.BuildJsonAsync(referencia, source);
                if (string.IsNullOrWhiteSpace(json))
                {
                    await UpsertEntregaSapLogAsync(referencia, source, false, "JSON vacío.");
                    return (false, "JSON vacío");
                }

                string? uDocMeat = null;
                string? numAtCard = null;

                try
                {
                    using var jd = JsonDocument.Parse(json);
                    var root = jd.RootElement;

                    if (root.TryGetProperty("U_DocMeat", out var p1) && p1.ValueKind == JsonValueKind.String)
                        uDocMeat = p1.GetString();

                    if (root.TryGetProperty("NumAtCard", out var p2) && p2.ValueKind == JsonValueKind.String)
                        numAtCard = p2.GetString();
                }
                catch { }

                var (found, docEntryExist, docNumExist) = await BuscarEntregaEnSapAsync(uDocMeat ?? "", numAtCard ?? "");
                if (found)
                {
                    await UpsertEntregaSapLogAsync(referencia, source, true, "Ya está en SAP.", docEntryExist, docNumExist);
                    return (true, "Ya está en SAP");
                }

                var r = await _sap.PostJsonAsync(sapEndpoint, json);

                if (!r.ok)
                {
                    await UpsertEntregaSapLogAsync(referencia, source, false, r.error ?? "No se pudo enviar a SAP.");
                    return (false, r.error ?? "No se pudo enviar a SAP");
                }

                int? docEntry = null;
                int? docNum = null;

                try
                {
                    using var doc = JsonDocument.Parse(r.response);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("DocEntry", out var a) && a.ValueKind == JsonValueKind.Number) docEntry = a.GetInt32();
                    if (root.TryGetProperty("DocNum", out var b) && b.ValueKind == JsonValueKind.Number) docNum = b.GetInt32();
                }
                catch { }

                await UpsertEntregaSapLogAsync(referencia, source, true, "Enviado con éxito.", docEntry, docNum);
                return (true, "Enviado con éxito");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EnviarAsync fallo ref={Ref} source={Source}", referencia, source);
                await UpsertEntregaSapLogAsync(referencia, source, false, ex.Message);
                return (false, ex.Message);
            }
        }

        private async Task UpsertEntregaSapLogAsync(
            string referencia,
            string source,
            bool estatus,
            string? mensaje = null,
            int? docEntry = null,
            int? docNum = null)
        {
            var usuario = "AUTO"; // no hay HttpContext aquí

            var row = await _db.EntregaSapLogs
                .FirstOrDefaultAsync(x => x.Referencia == referencia && x.Source == source);

            if (row == null)
            {
                row = new EntregaSapLog { Referencia = referencia, Source = source };
                _db.EntregaSapLogs.Add(row);
            }

            row.Estatus = estatus;
            row.Mensaje = (mensaje ?? (estatus ? "Enviado con éxito." : "No se pudo enviar."));
            if (row.Mensaje.Length > 300) row.Mensaje = row.Mensaje.Substring(0, 300);

            row.DocEntry = docEntry;
            row.DocNum = docNum;
            row.FechaIntento = DateTime.Now;
            row.Usuario = usuario;

            await _db.SaveChangesAsync();
        }

        private static string ODataEscape(string s) => (s ?? "").Replace("'", "''");

        private async Task<(bool found, int? docEntry, int? docNum)> BuscarEntregaEnSapAsync(string uDocMeat, string numAtCard)
        {
            var filters = new List<string>();

            if (!string.IsNullOrWhiteSpace(uDocMeat))
                filters.Add($"U_DocMeat eq '{ODataEscape(uDocMeat)}'");

            if (!string.IsNullOrWhiteSpace(numAtCard))
                filters.Add($"NumAtCard eq '{ODataEscape(numAtCard)}'");

            if (filters.Count == 0) return (false, null, null);

            var filter = string.Join(" or ", filters);

            var endpoint =
                $"DeliveryNotes?$select=DocEntry,DocNum&$top=1&$filter={Uri.EscapeDataString(filter)}";

            var g = await _sap.GetAsync(endpoint);
            if (!g.ok || string.IsNullOrWhiteSpace(g.response)) return (false, null, null);

            try
            {
                using var doc = JsonDocument.Parse(g.response);
                var root = doc.RootElement;

                if (root.TryGetProperty("value", out var val) &&
                    val.ValueKind == JsonValueKind.Array &&
                    val.GetArrayLength() > 0)
                {
                    var first = val[0];

                    int? docEntry = null;
                    int? docNum = null;

                    if (first.TryGetProperty("DocEntry", out var p1) && p1.ValueKind == JsonValueKind.Number)
                        docEntry = p1.GetInt32();

                    if (first.TryGetProperty("DocNum", out var p2) && p2.ValueKind == JsonValueKind.Number)
                        docNum = p2.GetInt32();

                    return (true, docEntry, docNum);
                }
            }
            catch { }

            return (false, null, null);
        }
    }
}
