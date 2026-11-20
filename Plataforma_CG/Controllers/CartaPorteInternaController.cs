using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using QRCoder;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plataforma_CG.Filters;
using Plataforma_CG.ViewModels;

namespace Plataforma_CG.Controllers
{
    // Consulta SOLO LECTURA de SIGO (no llama a SAP ni genera VREM).
    // El documento emitido es operativo, NO fiscal.
    [Route("ProcesosCG")]
    public sealed class CartaPorteInternaController : Controller
    {
        private readonly IConfiguration _cfg;
        private readonly ILogger<CartaPorteInternaController> _log;

        private const string Vista = "~/Views/ProcesosCG/CartaPorteInterna.cshtml";

        public CartaPorteInternaController(IConfiguration cfg, ILogger<CartaPorteInternaController> log)
        {
            _cfg = cfg;
            _log = log;
        }

        private sealed class Encabezado
        {
            public int SolicitudSurtidoId { get; set; }
            public string Pedido { get; set; } = "";
            public string Remision { get; set; } = "";
            public DateTime? FechaValidacion { get; set; }
            public string Sucursal { get; set; } = "";
            public string CodigoSap { get; set; } = "";
        }

        private sealed class Detalle
        {
            public int SolicitudSurtidoId { get; set; }
            public long TarimaId { get; set; }
            public string Articulo { get; set; } = "";
            public decimal Kg { get; set; }
            public decimal Cajas { get; set; }
        }

        private sealed class ArticuloNombre
        {
            public string ArticuloId { get; set; } = "";
            public string Nombre { get; set; } = "";
        }

        [HttpGet("CartaPorteInterna")]
        [RevisarPermiso("ENTREGAS_SAP", "LEER")]
        public async Task<IActionResult> CartaPorteInterna(
            [FromQuery] string[] referencias,
            [FromQuery] string source = "P1",
            CancellationToken ct = default)
        {
            source = (source ?? "P1").Trim().ToUpperInvariant();
            if (source != "P1" && source != "TIF")
                return BadRequest("Planta inválida. Elige P1 o TIF.");

            var refs = (referencias ?? Array.Empty<string>())
                .Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0 && x.Length <= 100)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (refs.Length == 0) return BadRequest("Selecciona una remisión.");
            if (refs.Length > 40) return BadRequest("Máximo 40 remisiones.");

            // La cadena SIGO usa servidor independiente del de MEAT.
            var cs = _cfg.GetConnectionString("CadenaSQLSIGO");
            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(503, "Falta ConnectionStrings:CadenaSQLSIGO.");

            try
            {
                await using var sigo = new SqlConnection(cs);
                await sigo.OpenAsync(ct);

                const string sqlEnc = @"
SELECT SolicitudSurtidoId, Pedido, Remision, FechaValidacion, Sucursal, CodigoSap
FROM dbo.SurtidoEncabezado
WHERE Remision IN @Refs OR Pedido IN @Refs;";
                var enc = (await sigo.QueryAsync<Encabezado>(new CommandDefinition(
                    sqlEnc, new { Refs = refs }, cancellationToken: ct, commandTimeout: 30))).ToList();

                var elegidos = new List<Encabezado>();
                foreach (var referencia in refs)
                {
                    var coinciden = enc.Where(e =>
                        string.Equals(e.Remision?.Trim(), referencia, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.Pedido?.Trim(), referencia, StringComparison.OrdinalIgnoreCase))
                        .GroupBy(e => e.SolicitudSurtidoId).Select(g => g.First()).ToList();
                    if (coinciden.Count == 0)
                        return NotFound($"La remisión/pedido {referencia} no está en SurtidoEncabezado de SIGO.");
                    if (coinciden.Count != 1)
                        return BadRequest($"La referencia {referencia} corresponde a varias solicitudes.");
                    var e = coinciden[0];
                    var suc = (e.Sucursal ?? "").ToUpperInvariant().Replace(" ", "");
                    if (source == "TIF" ? !suc.Contains("TIF") : suc != "P1" && suc != "PLANTA1")
                        return BadRequest($"{referencia} corresponde a {e.Sucursal}; planta solicitada: {source}.");
                    elegidos.Add(e);
                }
                elegidos = elegidos.GroupBy(e => e.SolicitudSurtidoId).Select(g => g.First()).ToList();
                var clientes = elegidos.Select(e => (e.CodigoSap ?? "").Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (clientes.Count != 1 || string.IsNullOrWhiteSpace(clientes[0]))
                    return BadRequest("Las remisiones deben corresponder a un solo cliente SAP.");

                var ids = elegidos.Select(e => e.SolicitudSurtidoId).ToArray();
                const string sqlDet = @"
SELECT SolicitudSurtidoId, TarimaId, Articulo,
       CAST(ISNULL(Kg, 0) AS decimal(18,3)) AS Kg,
       CAST(ISNULL(Cajas, 0) AS decimal(18,3)) AS Cajas
FROM dbo.SurtidoDetalleTarimas
WHERE SolicitudSurtidoId IN @Ids;";
                var detalles = (await sigo.QueryAsync<Detalle>(new CommandDefinition(
                    sqlDet, new { Ids = ids }, cancellationToken: ct, commandTimeout: 30))).ToList();
                var faltantes = ids.Where(id => !detalles.Any(d => d.SolicitudSurtidoId == id)).ToList();
                if (faltantes.Count > 0)
                    return BadRequest("No existe detalle de tarimas para solicitud(es): " +
                        string.Join(", ", faltantes) +
                        ". Revisar surtidos por etiqueta individual antes de imprimir.");
                if (detalles.Any(d => d.Kg <= 0 || d.Cajas < 0 || string.IsNullOrWhiteSpace(d.Articulo)))
                    return BadRequest("Hay líneas de surtido con SKU, peso o cajas inválidos.");

                // Catálogo de artículos de MEAT: consulta batched de solo lectura.
                // Si falta disponibilidad, nunca inventamos nombres; se conserva SKU.
                var nombres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var meatCs = _cfg.GetConnectionString(source == "TIF" ? "CadenaMeatTIF" : "CadenaMeatP1");
                if (!string.IsNullOrWhiteSpace(meatCs))
                {
                    try
                    {
                        await using var meat = new SqlConnection(meatCs);
                        await meat.OpenAsync(ct);
                        var skus = detalles.Select(d => (d.Articulo ?? "").Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        var filas = await meat.QueryAsync<ArticuloNombre>(new CommandDefinition(@"
SELECT ArticuloId, Nombre FROM dbo.Articulo WHERE ArticuloId IN @Skus;",
                            new { Skus = skus }, cancellationToken: ct, commandTimeout: 20));
                        foreach (var item in filas)
                            if (!string.IsNullOrWhiteSpace(item.ArticuloId) && !string.IsNullOrWhiteSpace(item.Nombre))
                                nombres[item.ArticuloId.Trim()] = item.Nombre.Trim();
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (SqlException ex)
                    {
                        _log.LogWarning(ex, "No fue posible consultar nombres de artículos en MEAT; se usarán SKU.");
                    }
                }

                var refPorSolicitud = elegidos.ToDictionary(e => e.SolicitudSurtidoId, e => e.Remision ?? "");
                var vm = new CartaPorteInternaVm
                {
                    Source = source,
                    ClienteCodigo = clientes[0],
                    Sucursal = elegidos[0].Sucursal ?? "",
                    Referencias = elegidos.Select(e => e.Remision ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Solicitudes = ids.ToList(),
                    Pedidos = elegidos.Select(e => e.Pedido ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    FechaValidacion = elegidos.Max(e => e.FechaValidacion),
                    TotalCajas = checked((int)detalles.Sum(d => d.Cajas)),
                    TotalTarimas = detalles.Select(d => new { d.SolicitudSurtidoId, d.TarimaId }).Distinct().Count(),
                    FechaPreparacion = DateTime.Now
                };

                vm.Mercancias = detalles
                    .GroupBy(d => (d.Articulo ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new CartaPorteMercanciaVm
                    {
                        Codigo = g.Key,
                        Descripcion = nombres.TryGetValue(g.Key, out var nombre) ? nombre : g.Key,
                        Unidad = "KGM",
                        PesoKg = g.Sum(d => d.Kg),
                        Cajas = g.Sum(d => d.Cajas),
                        Referencia = string.Join(", ", g.Select(d => refPorSolicitud[d.SolicitudSurtidoId]).Distinct(StringComparer.OrdinalIgnoreCase))
                    }).OrderBy(m => m.Codigo).ToList();
                vm.PesoTotalKg = vm.Mercancias.Sum(m => m.PesoKg);
                if (vm.PesoTotalKg <= 0) return BadRequest("La carta porte no tiene mercancías con kilogramos válidos.");

                // Folio irrepetible para esta visualización. Para folios consecutivos,
                // reimpresión histórica o validación pública se requiere persistencia.
                vm.FolioInterno = $"CP-SIGO-{vm.FechaPreparacion:yyyyMMdd}-{Guid.NewGuid():N}".ToUpperInvariant();

                // QR interno INFORMATIVO (NO CFDI). No contiene timbre, UUID fiscal,
                // sellos ni enlace del SAT. Se genera sin servicios externos.
                var identidad = string.Join("|", vm.Referencias.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    + "|" + vm.PesoTotalKg.ToString("0.000", CultureInfo.InvariantCulture)
                    + "|" + vm.TotalCajas.ToString(CultureInfo.InvariantCulture);
                string huella;
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identidad));
                    huella = BitConverter.ToString(hash).Replace("-", "").Substring(0, 24);
                }
                var textoQr = string.Join("\n", new[]
                {
                    "SIGO - CARTA PORTE OPERATIVA INTERNA",
                    "NO ES CFDI / NO TIMBRADO SAT",
                    "FOLIO: " + vm.FolioInterno,
                    "FECHA: " + vm.FechaPreparacion.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    "PLANTA: " + vm.Source,
                    "REMISIONES: " + vm.Referencias.Count.ToString(CultureInfo.InvariantCulture),
                    "PRIMERA: " + (vm.Referencias.FirstOrDefault() ?? ""),
                    "PESO KG: " + vm.PesoTotalKg.ToString("0.000", CultureInfo.InvariantCulture),
                    "CAJAS: " + vm.TotalCajas.ToString(CultureInfo.InvariantCulture),
                    "HUELLA DATOS: " + huella
                });
                using (var generator = new QRCodeGenerator())
                using (var qrData = generator.CreateQrCode(textoQr, QRCodeGenerator.ECCLevel.M))
                using (var qr = new PngByteQRCode(qrData))
                {
                    vm.CodigoQrInternoBase64 = Convert.ToBase64String(qr.GetGraphic(5));
                }
                Response.Headers["Cache-Control"] = "no-store";
                return View(Vista, vm);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return StatusCode(499, "Solicitud cancelada.");
            }
            catch (SqlException ex)
            {
                _log.LogError(ex, "Carta porte: error consultando SIGO/MEAT.");
                return StatusCode(503, "No fue posible consultar el surtido. Revisa el registro del servidor.");
            }
        }
    }
}
