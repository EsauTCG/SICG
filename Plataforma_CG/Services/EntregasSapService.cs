using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Plataforma_CG.Services
{
    public class EntregasSapService : IEntregasSapService
    {
        private readonly IConfiguration _cfg;
        private readonly AppDbContext _db;
        private readonly ISapServiceLayerClient _sap;

        public EntregasSapService(AppDbContext db, IConfiguration configuration, ISapServiceLayerClient sap)
        {
            _db = db;
            _cfg = configuration;
            _sap = sap;
        }

        // Normaliza source para que no falle por minúsculas/espacios
        private static string Norm(string? source)
            => (source ?? "P1").Trim().ToUpperInvariant();

        private string GetConn(string? source)
        {
            var s = Norm(source);
            return (s == "TIF")
                ? _cfg.GetConnectionString("CadenaMeatTIF")!
                : _cfg.GetConnectionString("CadenaMeatP1")!;
        }

        // ✅ NOMBRE REAL DE LA BD COMMERCIANET SEGÚN ORIGEN
        private static string GetCommerciaDb(string? source)
        {
            var s = Norm(source);
            return (s == "TIF") ? "TIF_CommerciaNET" : "CommerciaNET";
        }

        // Helper para armar [Db].[dbo].[Tabla]
        private static string Q(string name) => $"[{name.Replace("]", "]]")}]";
        private static string Db(string db, string table) => $"{Q(db)}.[dbo].{Q(table)}";

        public async Task<List<EntregaSapRowVM>> ListarAsync(DateTime desde, DateTime hasta, string source)
        {
            var cnStr = GetConn(source);
            var src = Norm(source);

            var sql = @"
SELECT 
    d.SolicitudSurtidoId,
    d.Referencia AS ReferenciaDocMeat,
    sr9.Referencia AS Remision,
    d.FechaHora AS FechaDocumento,
    sr10.Referencia AS Cliente
FROM SurtidoReferencia d
INNER JOIN SurtidoReferencia sr9
    ON d.SolicitudSurtidoId = sr9.SolicitudSurtidoId
   AND sr9.TipoReferenciaId = 9
LEFT JOIN SurtidoReferencia sr10
    ON d.SolicitudSurtidoId = sr10.SolicitudSurtidoId
   AND sr10.TipoReferenciaId = 6
WHERE d.TipoReferenciaId = 12
  AND d.FechaHora >= @desde
  AND d.FechaHora <  @hasta
ORDER BY d.FechaHora DESC;
";

            var list = new List<EntregaSapRowVM>();

            await using (var cn = new SqlConnection(cnStr))
            {
                await cn.OpenAsync();

                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@desde", desde);
                cmd.Parameters.AddWithValue("@hasta", hasta);

                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    list.Add(new EntregaSapRowVM
                    {
                        SolicitudSurtidoId = rd.GetInt32(0),
                        ReferenciaDocMeat = rd.IsDBNull(1) ? "" : rd.GetString(1),
                        Remision = rd.IsDBNull(2) ? "" : rd.GetString(2),
                        FechaDocumento = rd.IsDBNull(3) ? null : rd.GetDateTime(3),
                        Cliente = rd.IsDBNull(4) ? null : rd.GetString(4)
                    });
                }
            }

            // ====== pegar estatus de Enviado a SAP (EntregaSapLog) ======
            var refs = list
                .Select(x => (x.ReferenciaDocMeat ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (refs.Count > 0)
            {
                var logs = await _db.EntregaSapLogs
                    .Where(l => l.Source == src && refs.Contains(l.Referencia))
                    .Select(l => new { l.Referencia, l.Estatus, l.FechaIntento, l.Mensaje })
                    .ToListAsync();

                var map = logs.ToDictionary(x => x.Referencia, x => x);

                foreach (var r in list)
                {
                    var refx = (r.ReferenciaDocMeat ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(refx)) continue;

                    if (map.TryGetValue(refx, out var lg))
                    {
                        r.EnviadoSap = lg.Estatus;
                        r.FechaEnvioSap = lg.FechaIntento;
                        r.MsgSap = lg.Mensaje;
                    }
                    else
                    {
                        r.EnviadoSap = null;
                    }
                }
            }

            return list;
        }

        public async Task<string> BuildJsonAsync(string referenciaDocMeat, string source)
        {
            var cnStr = GetConn(source);
            var commerciaDb = GetCommerciaDb(source);

            // ✅ Warehouse por origen
            var warehouseCode = (Norm(source) == "TIF") ? "PLATIFGE" : "PLAP1GEN";

            // ✅ tablas cruzadas de CommerciaNET dependen del nombre de BD
            var tblCliente = Db(commerciaDb, "Cliente");
            var tblMovimiento = Db(commerciaDb, "Movimiento");

            var sql = $@"
SELECT (
    SELECT 
        -- ====== HEADER SAP ======
        ISNULL(
            (SELECT CodigoId
             FROM {tblCliente} c WITH (NOLOCK)
             WHERE c.Clienteid = (
                 SELECT sr5.Referencia
                 FROM SurtidoReferencia sr5 WITH (NOLOCK)
                 WHERE sr5.SolicitudSurtidoId = Document.SolicitudSurtidoId
                   AND sr5.TipoReferenciaId = 5
             )),
            (SELECT sr5b.Referencia
             FROM SurtidoReferencia sr5b WITH (NOLOCK)
             WHERE sr5b.SolicitudSurtidoId = Document.SolicitudSurtidoId
               AND sr5b.TipoReferenciaId = 5)
        ) AS [CardCode],

        sr9.Referencia AS [NumAtCard],
        'Documento creado desde SIGO [' 
        + FORMAT(GETDATE(), 'dd/MM/yyyy hh:mm:ss tt', 'es-MX')
        + ']' AS [Comments],

        -- UDF (campo U_ directo en SL)
        Document.Referencia AS [U_DocMeat],

        -- ====== LINES SAP ======
        (
            SELECT 
                mov.ArticuloId AS [ItemCode],
                @wh            AS [WarehouseCode],
                CAST(SUM(P.PesoNeto) AS DECIMAL(20,4)) AS [Quantity],

                17 AS [BaseType],
                TRY_CONVERT(int, sr43.Referencia) AS [BaseEntry],
                mov.RenglonId AS [BaseLine],

                (
                    SELECT 
                        L.Nombre AS [BatchNumber],
                        CAST(SUM(PR.PesoNeto) AS DECIMAL(20,4)) AS [Quantity]
                    FROM SalidaEmbarque SE2 WITH (NOLOCK)
                    INNER JOIN Produccion PR WITH (NOLOCK) ON PR.ProduccionId = SE2.ProduccionId
                    INNER JOIN Lote L WITH (NOLOCK) ON PR.LoteId = L.LoteId
                    WHERE SE2.SolicitudSurtidoId = Document.SolicitudSurtidoId
                      AND PR.Articulo = mov.ArticuloId
                      AND PR.UltimoProcesoId != 31
                    GROUP BY L.Nombre
                    FOR JSON PATH
                ) AS [BatchNumbers]

            FROM SalidaEmbarque SE WITH (NOLOCK)
            INNER JOIN Produccion P WITH (NOLOCK) ON P.ProduccionId = SE.ProduccionId

            LEFT JOIN SurtidoReferencia sr43 WITH (NOLOCK)
                ON SE.SolicitudSurtidoId = sr43.SolicitudSurtidoId
               AND sr43.TipoReferenciaId = 43

            INNER JOIN {tblMovimiento} mov WITH (NOLOCK)
                ON CONCAT(mov.EmpresaId, '.', mov.SucursalId, '.', mov.OperacionId, '.', mov.Folio) = sr9.Referencia

            WHERE SE.SolicitudSurtidoId = Document.SolicitudSurtidoId
              AND P.Articulo = mov.ArticuloId
              AND P.UltimoProcesoId != 31

            GROUP BY mov.ArticuloId, mov.RenglonId, sr43.Referencia
            FOR JSON PATH
        ) AS [DocumentLines]

    FROM SurtidoReferencia Document WITH (NOLOCK)
    INNER JOIN SurtidoReferencia sr9 WITH (NOLOCK)
        ON Document.SolicitudSurtidoId = sr9.SolicitudSurtidoId
       AND sr9.TipoReferenciaId = 9
    WHERE Document.Referencia = @ref
      AND Document.TipoReferenciaId = 12
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS Json;
";

            await using var cn = new SqlConnection(cnStr);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.CommandTimeout = 300;
            cmd.Parameters.Add("@ref", System.Data.SqlDbType.VarChar, 80).Value = referenciaDocMeat ?? "";
            cmd.Parameters.Add("@wh", System.Data.SqlDbType.VarChar, 20).Value = warehouseCode ?? "";

            var json = (string?)await cmd.ExecuteScalarAsync();
            return string.IsNullOrWhiteSpace(json) ? "{}" : json;
        }

        // ============================================================
        // ✅ FACTURA DE RESERVA (Invoices + ReserveInvoice=tYES)
        // - Reusa la misma data de la entrega
        // - Corrige BaseLine POR SKU si el BaseLine viene incorrecto
        // ============================================================
       public async Task<string> BuildReserveInvoiceJsonAsync(string referenciaDocMeat, string source)
{
    // 1) Partimos del mismo armado que la entrega (tiene CardCode, NumAtCard, U_DocMeat, DocumentLines con BaseEntry/BaseLine)
    var baseJson = await BuildJsonAsync(referenciaDocMeat, source);

    // 2) Parse a JsonObject para mutar
    JsonObject? obj;
    try
    {
        obj = JsonNode.Parse(baseJson) as JsonObject;
    }
    catch
    {
        obj = null;
    }

    if (obj == null)
        return "{}";

    // 3) Header reserva: Comments con fecha/hora, ReserveInvoice
    obj["Comments"] = $"Factura de reserva creada desde SIGO [{DateTime.Now:dd/MM/yyyy HH:mm:ss}]";
    obj["ReserveInvoice"] = "tYES";

    // 4) Obtener líneas
    var lines = obj["DocumentLines"] as JsonArray;
    if (lines == null || lines.Count == 0)
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    // 5) Leer BaseEntry (DocEntry de OV) desde la primer línea que lo tenga
    int? orderDocEntry = null;
    foreach (var ln in lines)
    {
        if (ln is not JsonObject lno) continue;

        // BaseEntry puede venir como número o string
        if (TryGetInt(lno, "BaseEntry", out var be) && be > 0)
        {
            orderDocEntry = be;
            break;
        }
    }

    // Si no hay BaseEntry, no hay forma de heredar precios de OV. Regresamos lo que haya (pero igual quitamos lotes/series).
    if (orderDocEntry == null)
    {
        RemoveBatchesAndSerialsOnlyForReserve(lines);
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    // 6) Traer líneas de la OV desde SAP para validar/corregir BaseLine por SKU
    //    (si esto falla, NO queremos tumbar toda la factura; mejor regresamos con el payload "como viene" pero sin lotes/series)
    List<SapOrderLine> ovLines;
    try
    {
        ovLines = await GetOrderLinesFromSapAsync(orderDocEntry.Value);
    }
    catch
    {
        // fallback: sin ovLines no podemos re-matchear; seguimos con BaseLine como venga
        RemoveBatchesAndSerialsOnlyForReserve(lines);
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    // 7) Corregir BaseLine por SKU cuando:
    //    - venga null
    //    - o apunte a una LineNum que NO corresponde al mismo ItemCode
    foreach (var ln in lines)
    {
        if (ln is not JsonObject lno) continue;

        var itemCode = (lno["ItemCode"]?.ToString() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(itemCode)) continue;

        var qtyDec = 0m;
        _ = TryGetDecimal(lno, "Quantity", out qtyDec);

        // BaseLine actual
        var hasBaseLine = TryGetInt(lno, "BaseLine", out var currentBaseLine);

        bool baseLineMatchesItem = false;
        if (hasBaseLine && currentBaseLine >= 0)
        {
            var match = ovLines.FirstOrDefault(x => x.LineNum == currentBaseLine);
            baseLineMatchesItem =
                match != null &&
                string.Equals(match.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase);
        }

        if (!hasBaseLine || currentBaseLine < 0 || !baseLineMatchesItem)
        {
            // ✅ fallback por SKU (y saldo)
            var resolved = ResolveBaseLineBySku(itemCode, qtyDec, ovLines);

            // Si no resolvió (=-1), al menos NO cambies: deja el que venga
            if (resolved >= 0)
                lno["BaseLine"] = resolved;
        }

        // Asegurar BaseType/BaseEntry para herencia de precio
        lno["BaseType"] = 17;
        lno["BaseEntry"] = orderDocEntry.Value;

        // ✅ SOLO RESERVA: quita lotes/series (para que no te truenen por batch inválido)
        lno.Remove("BatchNumbers");
        lno.Remove("SerialNumbers");
    }

    // Por si hubiera líneas sin iterar por algún motivo, asegúrate global
    RemoveBatchesAndSerialsOnlyForReserve(lines);

    return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
}
        private static void RemoveBatchesAndSerialsOnlyForReserve(JsonArray lines)
        {
            foreach (var ln in lines)
            {
                if (ln is not JsonObject lno) continue;
                lno.Remove("BatchNumbers");
                lno.Remove("SerialNumbers");
            }
        }

        private static bool TryGetInt(JsonObject obj, string prop, out int value)
        {
            value = default;

            if (!obj.TryGetPropertyValue(prop, out var node) || node == null)
                return false;

            // node puede ser JsonValue number/string
            try
            {
                if (node is JsonValue v)
                {
                    if (v.TryGetValue<int>(out var i)) { value = i; return true; }
                    if (v.TryGetValue<long>(out var l)) { value = (int)l; return true; }
                    if (v.TryGetValue<decimal>(out var d)) { value = (int)d; return true; }
                    if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) { value = p; return true; }
                }
            }
            catch { }

            var str = node.ToString();
            return int.TryParse(str, out value);
        }

        private static bool TryGetDecimal(JsonObject obj, string prop, out decimal value)
        {
            value = default;

            if (!obj.TryGetPropertyValue(prop, out var node) || node == null)
                return false;

            try
            {
                if (node is JsonValue v)
                {
                    if (v.TryGetValue<decimal>(out var d)) { value = d; return true; }
                    if (v.TryGetValue<double>(out var db)) { value = (decimal)db; return true; }
                    if (v.TryGetValue<int>(out var i)) { value = i; return true; }
                    if (v.TryGetValue<long>(out var l)) { value = l; return true; }
                    if (v.TryGetValue<string>(out var s) && decimal.TryParse(s, out var p)) { value = p; return true; }
                }
            }
            catch { }

            var str = node.ToString();
            return decimal.TryParse(str, out value);
        }


        // ================== SAP OV lines ==================
        private async Task<List<SapOrderLine>> GetOrderLinesFromSapAsync(int orderDocEntry)
        {
            // ✅ En Service Layer, DocumentLines no se expande (no es nav property).
            // Pide el documento y trae DocumentLines completo.
            var endpoint = $"Orders({orderDocEntry})?$select=DocEntry,DocumentLines";

            var g = await _sap.GetAsync(endpoint);

            if (!g.ok || string.IsNullOrWhiteSpace(g.response))
            {
                throw new Exception(
                    "No se pudo leer la OV desde SAP Service Layer. " +
                    $"orderDocEntry={orderDocEntry} | endpoint={endpoint} | error={g.error} | response={g.response}"
                );
            }

            using var doc = JsonDocument.Parse(g.response);
            var root = doc.RootElement;

            var list = new List<SapOrderLine>();

            if (!root.TryGetProperty("DocumentLines", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var it in arr.EnumerateArray())
            {
                // LineNum (BaseLine) normalmente existe
                var lineNum =
                    it.TryGetProperty("LineNum", out var pLineNum) && pLineNum.ValueKind == JsonValueKind.Number
                        ? pLineNum.GetInt32()
                        : -1;

                if (lineNum < 0) continue;

                var item =
                    it.TryGetProperty("ItemCode", out var pItem) && pItem.ValueKind == JsonValueKind.String
                        ? (pItem.GetString() ?? "").Trim()
                        : "";

                // Open quantity puede variar por versión / entidad:
                // - OpenQty
                // - OpenQuantity
                // - RemainingOpenQuantity
                decimal openQ = 0m;

                if (it.TryGetProperty("OpenQty", out var pOpen1) && pOpen1.ValueKind == JsonValueKind.Number)
                    openQ = pOpen1.GetDecimal();
                else if (it.TryGetProperty("OpenQuantity", out var pOpen2) && pOpen2.ValueKind == JsonValueKind.Number)
                    openQ = pOpen2.GetDecimal();
                else if (it.TryGetProperty("RemainingOpenQuantity", out var pOpen3) && pOpen3.ValueKind == JsonValueKind.Number)
                    openQ = pOpen3.GetDecimal();

                decimal qty = 0m;
                if (it.TryGetProperty("Quantity", out var pQty) && pQty.ValueKind == JsonValueKind.Number)
                    qty = pQty.GetDecimal();

                list.Add(new SapOrderLine
                {
                    LineNum = lineNum,
                    ItemCode = item,
                    OpenQuantity = openQ,
                    Quantity = qty
                });
            }

            return list;
        }



        // ================== BaseLine resolver (por SKU) ==================
        private static int ResolveBaseLineBySku(string itemCode, decimal qty, List<SapOrderLine> orderLines)
        {
            itemCode = (itemCode ?? "").Trim();

            var candidates = orderLines
                .Where(x => string.Equals(x.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.OpenQuantity) // prioriza la que tenga más saldo
                .ThenBy(x => x.LineNum)
                .ToList();

            if (candidates.Count == 0)
                throw new Exception($"La OV no contiene el SKU '{itemCode}'.");

            // Si alguna línea tiene saldo suficiente, usa esa
            var hit = candidates.FirstOrDefault(x => x.OpenQuantity >= qty && x.OpenQuantity > 0);
            if (hit != null) return hit.LineNum;

            // Si no, al menos usa la de mayor saldo (pero normalmente eso tronará en SAP por saldo)
            // Mejor lanzar error claro:
            var openTotal = candidates.Sum(x => x.OpenQuantity);
            throw new Exception($"No hay saldo suficiente en OV para SKU '{itemCode}'. OpenQuantity total={openTotal}, requerido={qty}.");
        }

        private sealed class SapOrderLine
        {
            public int LineNum { get; set; }
            public string ItemCode { get; set; } = "";
            public decimal OpenQuantity { get; set; }
            public decimal Quantity { get; set; }
        }



        private string BuildFacturaReservaManualJson(
     string entregaJson,
     List<ManualReserveLineDto> lineas)
        {
            var entrega = JsonNode.Parse(entregaJson)!.AsObject();
            var entregaLines = entrega["DocumentLines"]!.AsArray();

            var facturaLines = new JsonArray();

            foreach (var l in lineas)
            {
                if (l.Cantidad <= 0)
                    continue;

                var src = entregaLines.FirstOrDefault(x =>
                    x!["BaseLine"]!.GetValue<int>() == l.BaseLine);

                if (src == null)
                    throw new Exception($"No existe BaseLine {l.BaseLine} en la entrega.");

                var maxQty = src["Quantity"]!.GetValue<decimal>();
                if (l.Cantidad > maxQty)
                    throw new Exception(
                        $"Cantidad excede la entrega. BaseLine={l.BaseLine}, Max={maxQty}, Req={l.Cantidad}");

                facturaLines.Add(new JsonObject
                {
                    ["ItemCode"] = src["ItemCode"]!.GetValue<string>(),
                    ["Quantity"] = l.Cantidad,

                    // 🔥 OV, no entrega
                    ["BaseType"] = 17,
                    ["BaseEntry"] = src["BaseEntry"]!.GetValue<int>(),
                    ["BaseLine"] = l.BaseLine
                });
            }

            if (facturaLines.Count == 0)
                throw new Exception("No hay líneas válidas para facturar.");

            var factura = new JsonObject
            {
                ["CardCode"] = entrega["CardCode"]!.GetValue<string>(),
                ["NumAtCard"] = entrega["NumAtCard"]!.GetValue<string>(),
                ["Comments"] = $"Factura reserva manual SIGO [{DateTime.Now:dd/MM/yyyy HH:mm:ss}]",
                ["ReserveInvoice"] = "tYES",
                ["DocumentLines"] = facturaLines
            };

            return factura.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }



        public async Task<string> BuildReserveInvoiceJsonManualAsync(
    string referencia,
    string source,
    List<ManualReserveLineDto> lineas)
        {
            var entregaJson = await BuildJsonAsync(referencia, source);

            return BuildFacturaReservaManualJson(entregaJson, lineas);
        }

        public Task<int?> TryGetEntregaDocEntryAsync(string referencia, string source)
        {
            // ❗ Reserva manual NO usa DocEntry
            // Se deja implementación neutra para cumplir la interfaz
            return Task.FromResult<int?>(null);
        }


    }
}
