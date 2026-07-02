using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Plataforma_CG.Services
{
    public sealed class SapInvoiceSyncService : ISapInvoiceSyncService
    {
        private readonly ILogger<SapInvoiceSyncService> _logger;
        private readonly SapServiceLayerClient _sapClient;

        public SapInvoiceSyncService(ILogger<SapInvoiceSyncService> logger, SapServiceLayerClient sapClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sapClient = sapClient ?? throw new ArgumentNullException(nameof(sapClient));
        }

        /// <summary>
        /// Sincroniza facturas de UN cliente (upsert por (DocEntry, LineNum)).
        /// Usa tu método SAP GetInvoicesAll(cardCode).
        /// </summary>
        public async Task<int> SincronizarInvoicesClienteAsync(
            string cardCode,
            string sqlConnectionString,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                throw new ArgumentException("cardCode requerido", nameof(cardCode));
            if (string.IsNullOrWhiteSpace(sqlConnectionString))
                throw new ArgumentException("connection string requerido", nameof(sqlConnectionString));

            // 1) Trae facturas desde SAP
            var list = await _sapClient.GetInvoicesAll(cardCode);
            if (list == null || list.Count == 0)
            {
                _logger.LogInformation("ℹ️ No hay facturas para {CardCode}.", cardCode);
                return 0;
            }

            // 2) Blindaje: quitar duplicados por (DocEntry, LineNum)
            var dedupe = list
                .GroupBy(x => new { x.DocEntry, x.LineNum })
                .Select(g => g.First())
                .ToList();

            if (dedupe.Count == 0)
            {
                _logger.LogInformation("ℹ️ Después de dedupe no hay filas para {CardCode}.", cardCode);
                return 0;
            }

            // 3) TVP que coincide con dbo.TvpInvoiceLine
            var tvp = new DataTable();
            tvp.Columns.Add("sap_doc_entry", typeof(int));
            tvp.Columns.Add("sap_line_num", typeof(int));
            tvp.Columns.Add("card_code", typeof(string));
            tvp.Columns.Add("sku", typeof(string));
            tvp.Columns.Add("kilos", typeof(decimal));
            tvp.Columns.Add("doc_date", typeof(DateTime));

            foreach (var x in dedupe)
            {
                var r = tvp.NewRow();
                r["sap_doc_entry"] = x.DocEntry;
                r["sap_line_num"] = x.LineNum; // clave única con DocEntry
                r["card_code"] = string.IsNullOrWhiteSpace(x.CardCode) ? cardCode : x.CardCode;
                r["sku"] = x.SKU ?? "";
                r["kilos"] = Math.Round(x.Kilos, 4, MidpointRounding.AwayFromZero);
                r["doc_date"] = x.DocDate.Date;
                tvp.Rows.Add(r);
            }

            // 4) Enviar a SQL con SP (hace MERGE/UPSERT)
            using var con = new SqlConnection(sqlConnectionString);
            await con.OpenAsync(ct);

            using var cmd = new SqlCommand("dbo.Invoices_UpsertAndRebuild", con)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add(new SqlParameter("@CardCode", SqlDbType.NVarChar, 50) { Value = cardCode });
            var p = cmd.Parameters.AddWithValue("@Lote", tvp);
            p.SqlDbType = SqlDbType.Structured;
            p.TypeName = "dbo.TvpInvoiceLine";

            var affected = await cmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("✅ Sync {CardCode}: affected={Affected} (rows considered: {Rows})",
                cardCode, affected, dedupe.Count);

            return affected;
        }

        /// <summary>
        /// 🔥 NUEVO: Sincroniza facturas de TODOS los clientes (sin filtrar).
        /// Reutiliza el método por cliente para no tocar tu SP.
        /// </summary>
        public async Task<int> SincronizarInvoicesDeTodosLosClientesAsync(
            string sqlConnectionString,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sqlConnectionString))
                throw new ArgumentException("connection string requerido", nameof(sqlConnectionString));

            var clientes = await _sapClient.ObtenerTodosClientesAsync();
            var cardCodes = clientes
                .Select(x => (x.CardCode ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            if (cardCodes.Count == 0)
            {
                _logger.LogWarning("⚠️ No se obtuvieron clientes desde SAP.");
                return 0;
            }

            _logger.LogInformation("▶️ Iniciando sync de TODOS los clientes. Total={Count}", cardCodes.Count);

            int totalAffected = 0;
            int idx = 0;

            foreach (var cc in cardCodes)
            {
                idx++;
                try
                {
                    _logger.LogInformation("→ [{Idx}/{Total}] Sync {CardCode} ...", idx, cardCodes.Count, cc);
                    var affected = await SincronizarInvoicesClienteAsync(cc, sqlConnectionString, ct);
                    totalAffected += affected;
                }
                catch (Exception ex)
                {
                    // Loguea y continúa con el siguiente cliente
                    _logger.LogError(ex, "❌ Error sincronizando {CardCode}. Continúo con el siguiente.", cc);
                }

                if (ct.IsCancellationRequested)
                {
                    _logger.LogWarning("⏹ Cancelado por token.");
                    break;
                }
            }

            _logger.LogInformation("✅ Sync TODOS terminado. Total affected={TotalAffected}", totalAffected);
            return totalAffected;
        }

        /// <summary>
        /// (Opcional) Sincroniza facturas de un subconjunto de clientes.
        /// </summary>
        public async Task<int> SincronizarInvoicesDeClientesAsync(
            string[] cardCodes,
            string sqlConnectionString,
            CancellationToken ct = default)
        {
            if (cardCodes == null || cardCodes.Length == 0)
                return 0;

            int total = 0;
            foreach (var cc in cardCodes.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                try
                {
                    total += await SincronizarInvoicesClienteAsync(cc.Trim(), sqlConnectionString, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error sincronizando {CardCode}. Continúo.", cc);
                }
            }
            return total;
        }
    }
}
