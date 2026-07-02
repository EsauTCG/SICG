using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Text.Json;
namespace Plataforma_CG.Services
{
    public class SapDireccionesSyncService : ISapDireccionesSyncService
    {
        private readonly ISapServiceLayerClient _sap;
        private readonly AppDbContext _context;

        public SapDireccionesSyncService(
            ISapServiceLayerClient sap,
            AppDbContext context)
        {
            _sap = sap;
            _context = context;
        }


        private static string Safe(string? value, string fallback, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return value.Length <= max ? value : value.Substring(0, max);
        }


        public async Task<int> SincronizarDireccionesClienteDesdeSapAsync(string cardCode)
        {
            if (string.IsNullOrWhiteSpace(cardCode))
                return 0;

            var (ok, response, error, _) =
                await _sap.GetAsync($"BusinessPartners('{cardCode}')");

            if (!ok)
                throw new Exception(error);

            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("BPAddresses", out var addresses) ||
                addresses.ValueKind != JsonValueKind.Array)
                return 0;

            var actuales = await _context.DireccionesCliente
                .Where(d => d.Cliente == cardCode)
                .ToListAsync();

            // 🔑 Diccionario NORMALIZADO por Alias
            var actualesDict = actuales.ToDictionary(
                d => d.AliasDireccion.Trim().ToUpperInvariant(),
                d => d
            );

            int cambios = 0;

            foreach (var addr in addresses.EnumerateArray())
            {
                // =========================
                // NORMALIZACIÓN SEGURA
                // =========================
                string aliasRaw = Safe(
                    addr.TryGetProperty("AddressName", out var an) ? an.GetString() : null,
                    "SIN NOMBRE",
                    100
                );

                string aliasKey = aliasRaw.Trim().ToUpperInvariant();

                actualesDict.TryGetValue(aliasKey, out var existente);

                int? sapRow =
                    addr.TryGetProperty("RowNum", out var rn) && rn.ValueKind == JsonValueKind.Number
                        ? rn.GetInt32()
                        : null;

                string? sapType =
                    addr.TryGetProperty("AddressType", out var at) ? at.GetString() : null;

                string calle = Safe(
                    addr.TryGetProperty("Street", out var s) ? s.GetString() : null,
                    "SIN CALLE",
                    200
                );

                string? colonia = Safe(
                    addr.TryGetProperty("Block", out var b) ? b.GetString() : null,
                    null,
                    100
                );

                string ciudad = Safe(
                    addr.TryGetProperty("City", out var c) ? c.GetString()
                        : addr.TryGetProperty("County", out var co) ? co.GetString()
                        : null,
                    "NO ESPECIFICADA",
                    100
                );

                string estado = Safe(
                    addr.TryGetProperty("State", out var st) ? st.GetString() : null,
                    "NO ESPECIFICADO",
                    100
                );

                string? cp = Safe(
                    addr.TryGetProperty("ZipCode", out var zip) ? zip.GetString() : null,
                    null,
                    10
                );

                string pais = Safe(
                    addr.TryGetProperty("Country", out var p) ? p.GetString() : null,
                    "MEXICO",
                    50
                );

                bool esPrincipal = sapRow == 0;

                // =========================
                // INSERT
                // =========================
                if (existente == null)
                {
                    // 🔒 Protección extra contra duplicados
                    if (actualesDict.ContainsKey(aliasKey))
                        continue;

                    var nuevo = new DireccionCliente
                    {
                        Cliente = cardCode,
                        Origen = "SAP",
                        Activa = true,
                        FechaAlta = DateTime.Now,

                        AliasDireccion = aliasRaw,
                        EsPrincipal = esPrincipal,

                        Cedis = "DEFAULT",      // 🔴 OBLIGATORIO (NOT NULL)
                        Ruta = null,

                        Calle = calle,
                        Colonia = colonia,
                        Ciudad = ciudad,
                        Estado = estado,
                        CodigoPostal = cp,
                        Pais = pais,

                        SapRowNum = sapRow,
                        SapAddressType = sapType,
                        SapAddressCode = aliasRaw
                    };

                    _context.DireccionesCliente.Add(nuevo);
                    actualesDict[aliasKey] = nuevo;
                    cambios++;
                    continue;
                }

                // =========================
                // UPDATE SOLO SI CAMBIÓ
                // =========================
                bool actualizado = false;

                if (existente.Calle != calle) { existente.Calle = calle; actualizado = true; }
                if (existente.Colonia != colonia) { existente.Colonia = colonia; actualizado = true; }
                if (existente.Ciudad != ciudad) { existente.Ciudad = ciudad; actualizado = true; }
                if (existente.Estado != estado) { existente.Estado = estado; actualizado = true; }
                if (existente.CodigoPostal != cp) { existente.CodigoPostal = cp; actualizado = true; }
                if (existente.Pais != pais) { existente.Pais = pais; actualizado = true; }

                if (existente.EsPrincipal != esPrincipal) { existente.EsPrincipal = esPrincipal; actualizado = true; }
                if (!existente.Activa) { existente.Activa = true; actualizado = true; }

                if (actualizado)
                {
                    existente.FechaActualizacion = DateTime.Now;
                    cambios++;
                }
            }

            // =========================
            // DESACTIVAR LAS QUE YA NO EXISTEN EN SAP
            // =========================
            var aliasesSap = addresses.EnumerateArray()
                .Select(a =>
                    a.TryGetProperty("AddressName", out var an) && !string.IsNullOrWhiteSpace(an.GetString())
                        ? an.GetString()!.Trim().ToUpperInvariant()
                        : null
                )
                .Where(a => a != null)!
                .ToHashSet();

            foreach (var d in actualesDict.Values)
            {
                var key = d.AliasDireccion.Trim().ToUpperInvariant();
                if (!aliasesSap.Contains(key) && d.Activa)
                {
                    d.Activa = false;
                    d.FechaActualizacion = DateTime.Now;
                    cambios++;
                }
            }

            await _context.SaveChangesAsync();
            return cambios;
        }



        public async Task<int> SincronizarDireccionesClientesDesdeSapAsync()
        {
            var clientes = await _context.ClienteSap
                .Select(c => c.Cliente)
                .ToListAsync();

            int totalCambios = 0;

            foreach (var cardCode in clientes)
            {
                totalCambios += await SincronizarDireccionesClienteDesdeSapAsync(cardCode);
            }

            return totalCambios;
        }






        public async Task<int> SincronizarDireccionesTodosClientesAsync()
        {
            var login = await _sap.EnsureLoginAsync();
            if (!login.ok)
                throw new Exception(login.error);

            // 👉 Traer TODOS los clientes
            var (ok, response, error, _) =
                await _sap.GetAsync("BusinessPartners?$filter=CardType eq 'C'&$select=CardCode");

            if (!ok)
                throw new Exception(error);

            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("value", out var clientes))
                return 0;

            int totalProcesadas = 0;

            foreach (var c in clientes.EnumerateArray())
            {
                var cardCodeCliente = c.GetProperty("CardCode").GetString();

                if (string.IsNullOrWhiteSpace(cardCodeCliente))
                    continue;

                totalProcesadas +=
                    await SincronizarDireccionesClienteDesdeSapAsync(cardCodeCliente);
            }

            return totalProcesadas;
        }




    }
}
