using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.Services;
using Plataforma_CG.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    [Authorize]
    public class CobranzaController : Controller
    {
        private readonly AppDbContext _context;
        private readonly CobranzaSapSyncService _sync;
        private readonly ILogger<CobranzaController> _logger;

        public CobranzaController(
            AppDbContext context,
            CobranzaSapSyncService sync,
            ILogger<CobranzaController> logger)
        {
            _context = context;
            _sync = sync;
            _logger = logger;
        }

        // =========================================================
        // REPORTE PRINCIPAL
        // GET /Cobranza/Compromisos
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Compromisos(
            string? estatus = null,
            string? cliente = null,
            DateTime? desde = null,
            DateTime? hasta = null,
            CancellationToken ct = default)
        {
            var query =
                _context.CobranzaCompromisos
                    .AsNoTracking()
                    .AsQueryable();

            if (!string.IsNullOrWhiteSpace(estatus) &&
                !estatus.Equals(
                    "TODOS",
                    StringComparison.OrdinalIgnoreCase))
            {
                var e =
                    estatus.Trim().ToUpper();

                query =
                    query.Where(x =>
                        x.Estatus == e);
            }

            if (!string.IsNullOrWhiteSpace(cliente))
            {
                var c =
                    cliente.Trim();

                query =
                    query.Where(x =>
                        x.ClienteCodigo.Contains(c)
                        ||
                        (x.ClienteNombre ?? "").Contains(c)
                        ||
                        x.OrdenVentaConsecutivo.Contains(c));
            }

            if (desde.HasValue)
            {
                var inicio =
                    desde.Value.Date;

                query =
                    query.Where(x =>
                        x.FechaRegistro >= inicio);
            }

            if (hasta.HasValue)
            {
                var fin =
                    hasta.Value.Date.AddDays(1);

                query =
                    query.Where(x =>
                        x.FechaRegistro < fin);
            }

            var registros =
                await query
                    .OrderBy(x =>
                        x.FechaCompromiso)
                    .ThenByDescending(x =>
                        x.FechaRegistro)
                    .ToListAsync(ct);

            var ids =
                registros
                    .Select(x => x.Id)
                    .ToList();

            var facturasPorCompromiso =
                ids.Count == 0
                    ? new Dictionary<int, int>()
                    : await _context
                        .CobranzaCompromisoFacturas
                        .AsNoTracking()
                        .Where(x =>
                            ids.Contains(
                                x.CobranzaCompromisoId))
                        .GroupBy(x =>
                            x.CobranzaCompromisoId)
                        .Select(g => new
                        {
                            Id = g.Key,
                            Cantidad = g.Count()
                        })
                        .ToDictionaryAsync(
                            x => x.Id,
                            x => x.Cantidad,
                            ct);

            var ultimoPagoPorCompromiso =
                ids.Count == 0
                    ? new Dictionary<int, DateTime?>()
                    : await _context
                        .CobranzaCompromisoPagosSap
                        .AsNoTracking()
                        .Where(x =>
                            ids.Contains(
                                x.CobranzaCompromisoId))
                        .GroupBy(x =>
                            x.CobranzaCompromisoId)
                        .Select(g => new
                        {
                            Id = g.Key,
                            Fecha =
                                g.Max(x =>
                                    x.FechaPagoSap)
                        })
                        .ToDictionaryAsync(
                            x => x.Id,
                            x => x.Fecha,
                            ct);

            var vm =
                new CobranzaReporteViewModel
                {
                    Estatus =
                        estatus,

                    Cliente =
                        cliente,

                    Desde =
                        desde,

                    Hasta =
                        hasta,

                    Total =
                        registros.Count,

                    Pendientes =
                        registros.Count(x =>
                            x.Estatus == "PENDIENTE"
                            ||
                            x.Estatus == "VENCE_HOY"),

                    Incumplidos =
                        registros.Count(x =>
                            x.Estatus == "INCUMPLIDO"),

                    Parciales =
                        registros.Count(x =>
                            x.Estatus == "PARCIAL"),

                    Cumplidos =
                        registros.Count(x =>
                            x.Estatus == "CUMPLIDO"),

                    Revisar =
                        registros.Count(x =>
                            x.Estatus == "REVISAR"),

                    SaldoPendiente =
                        registros.Sum(x =>
                            x.SaldoPendienteActual),

                    Items =
                        registros
                            .OrderBy(x =>
                                OrdenEstatus(
                                    x.Estatus))
                            .ThenBy(x =>
                                x.FechaCompromiso)
                            .ThenByDescending(x =>
                                x.FechaRegistro)
                            .Select(x =>
                                new CobranzaReporteItemViewModel
                                {
                                    Id =
                                        x.Id,

                                    OrdenVentaConsecutivo =
                                        x.OrdenVentaConsecutivo,

                                    ClienteCodigo =
                                        x.ClienteCodigo,

                                    ClienteNombre =
                                        x.ClienteNombre ?? "",

                                    SaldoVencidoInicial =
                                        x.SaldoVencidoInicial,

                                    SaldoPendienteActual =
                                        x.SaldoPendienteActual,

                                    FechaCompromiso =
                                        x.FechaCompromiso,

                                    Estatus =
                                        x.Estatus,

                                    UsuarioRegistro =
                                        x.UsuarioRegistro,

                                    FechaRegistro =
                                        x.FechaRegistro,

                                    FechaUltimaValidacion =
                                        x.FechaUltimaValidacion,

                                    Facturas =
                                        facturasPorCompromiso
                                            .TryGetValue(
                                                x.Id,
                                                out var cantidad)
                                            ? cantidad
                                            : 0,

                                    UltimoPagoSap =
                                        ultimoPagoPorCompromiso
                                            .TryGetValue(
                                                x.Id,
                                                out var fecha)
                                            ? fecha
                                            : null
                                })
                            .ToList()
                };

            return View(
                "~/Views/Cobranza/Compromisos.cshtml",
                vm);
        }

        // =========================================================
        // DETALLE
        // GET /Cobranza/Detalle/15
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Detalle(
            int id,
            CancellationToken ct = default)
        {
            var c =
                await _context.CobranzaCompromisos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.Id == id,
                        ct);

            if (c == null)
                return NotFound();

            var facturas =
                await _context
                    .CobranzaCompromisoFacturas
                    .AsNoTracking()
                    .Where(x =>
                        x.CobranzaCompromisoId == id)
                    .OrderBy(x =>
                        x.FechaVencimiento)
                    .ToListAsync(ct);

            var facturaIds =
                facturas
                    .Select(x => x.Id)
                    .ToList();

            var pagos =
                facturaIds.Count == 0
                    ? new List<CobranzaCompromisoPagoSap>()
                    : await _context
                        .CobranzaCompromisoPagosSap
                        .AsNoTracking()
                        .Where(x =>
                            facturaIds.Contains(
                                x.CobranzaCompromisoFacturaId))
                        .OrderByDescending(x =>
                            x.FechaPagoSap)
                        .ThenByDescending(x =>
                            x.FechaDeteccion)
                        .ToListAsync(ct);

            var archivos =
                await _context
                    .CobranzaCompromisoArchivos
                    .AsNoTracking()
                    .Where(x =>
                        x.CobranzaCompromisoId == id)
                    .OrderByDescending(x =>
                        x.FechaRegistro)
                    .ToListAsync(ct);

            var historico =
                await _context
                    .CobranzaCompromisoHistoricos
                    .AsNoTracking()
                    .Where(x =>
                        x.CobranzaCompromisoId == id)
                    .OrderByDescending(x =>
                        x.FechaEvento)
                    .ToListAsync(ct);

            var vm =
                new CobranzaDetalleViewModel
                {
                    Id =
                        c.Id,

                    OrdenVentaId =
                        c.OrdenVentaId,

                    OrdenVentaConsecutivo =
                        c.OrdenVentaConsecutivo,

                    ClienteCodigo =
                        c.ClienteCodigo,

                    ClienteNombre =
                        c.ClienteNombre ?? "",

                    SaldoVencidoInicial =
                        c.SaldoVencidoInicial,

                    SaldoPendienteActual =
                        c.SaldoPendienteActual,

                    FechaCompromiso =
                        c.FechaCompromiso,

                    Motivo =
                        c.Motivo,

                    Estatus =
                        c.Estatus,

                    UsuarioRegistro =
                        c.UsuarioRegistro,

                    FechaRegistro =
                        c.FechaRegistro,

                    UsuarioUltimaValidacion =
                        c.UsuarioUltimaValidacion,

                    FechaUltimaValidacion =
                        c.FechaUltimaValidacion,

                    ObservacionCobranza =
                        c.ObservacionCobranza,

                    Facturas =
                        facturas.Select(f =>
                            new CobranzaFacturaDetalleViewModel
                            {
                                Id =
                                    f.Id,

                                SapDocEntry =
                                    f.SapDocEntry,

                                SapDocNum =
                                    f.SapDocNum,

                                FechaFactura =
                                    f.FechaFactura,

                                FechaVencimiento =
                                    f.FechaVencimiento,

                                Moneda =
                                    f.Moneda,

                                Importe =
                                    f.Importe,

                                PagadoInicial =
                                    f.PagadoInicial,

                                PendienteInicial =
                                    f.PendienteInicial,

                                PendienteActual =
                                    f.PendienteActual,

                                Pagada =
                                    f.Pagada,

                                FechaUltimaValidacion =
                                    f.FechaUltimaValidacion,

                                Pagos =
                                    pagos
                                        .Where(p =>
                                            p.CobranzaCompromisoFacturaId ==
                                            f.Id)
                                        .Select(p =>
                                            new CobranzaPagoSapDetalleViewModel
                                            {
                                                Id =
                                                    p.Id,

                                                SapPagoDocEntry =
                                                    p.SapPagoDocEntry,

                                                SapPagoDocNum =
                                                    p.SapPagoDocNum,

                                                FechaPagoSap =
                                                    p.FechaPagoSap,

                                                MontoAplicado =
                                                    p.MontoAplicado,

                                                Referencia =
                                                    p.Referencia,

                                                Comentario =
                                                    p.Comentario,

                                                FechaDeteccion =
                                                    p.FechaDeteccion
                                            })
                                        .ToList()
                            })
                        .ToList(),

                    Archivos =
                        archivos.Select(a =>
                            new CobranzaArchivoDetalleViewModel
                            {
                                Id =
                                    a.Id,

                                NombreOriginal =
                                    a.NombreOriginal,

                                TipoContenido =
                                    a.TipoContenido,

                                Extension =
                                    a.Extension,

                                TamanoBytes =
                                    a.TamanoBytes,

                                FechaRegistro =
                                    a.FechaRegistro
                            })
                        .ToList(),

                    Historico =
                        historico.Select(h =>
                            new CobranzaHistoricoDetalleViewModel
                            {
                                Id =
                                    h.Id,

                                CobranzaCompromisoFacturaId =
                                    h.CobranzaCompromisoFacturaId,

                                TipoEvento =
                                    h.TipoEvento,

                                EstatusAnterior =
                                    h.EstatusAnterior,

                                EstatusNuevo =
                                    h.EstatusNuevo,

                                PendienteAnterior =
                                    h.PendienteAnterior,

                                PendienteNuevo =
                                    h.PendienteNuevo,

                                FechaEvento =
                                    h.FechaEvento,

                                UsuarioProceso =
                                    h.UsuarioProceso,

                                Detalle =
                                    h.Detalle
                            })
                        .ToList()
                };

            return View(
                "~/Views/Cobranza/Detalle.cshtml",
                vm);
        }

        // =========================================================
        // VALIDACION MANUAL
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidarAhora(
            int id,
            CancellationToken ct = default)
        {
            var usuario =
                User.Identity?.Name
                ??
                User.FindFirst(
                    System.Security.Claims.ClaimTypes.Email)?.Value
                ??
                "COBRANZA";

            try
            {
                var ok =
                    await _sync.SincronizarCompromisoAsync(
                        id,
                        usuario,
                        ct);

                TempData[ok ? "Success" : "Error"] =
                    ok
                        ? "Compromiso validado correctamente contra SAP."
                        : "No se encontró el compromiso o no tiene facturas relacionadas.";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error validando compromiso {Id} contra SAP",
                    id);

                TempData["Error"] =
                    "No fue posible validar contra SAP: " +
                    ex.GetBaseException().Message;
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id });
        }

        // =========================================================
        // OBSERVACION DE COBRANZA
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarObservacion(
            int id,
            string? observacionCobranza,
            CancellationToken ct = default)
        {
            var c =
                await _context.CobranzaCompromisos
                    .FirstOrDefaultAsync(
                        x => x.Id == id,
                        ct);

            if (c == null)
                return NotFound();

            var nuevaObservacion =
                string.IsNullOrWhiteSpace(
                    observacionCobranza)
                    ? null
                    : observacionCobranza
                        .Trim();

            if (nuevaObservacion != null &&
                nuevaObservacion.Length > 1000)
            {
                nuevaObservacion =
                    nuevaObservacion[..1000];
            }

            c.ObservacionCobranza =
                nuevaObservacion;

            _context.CobranzaCompromisoHistoricos.Add(
                new CobranzaCompromisoHistorico
                {
                    CobranzaCompromisoId =
                        c.Id,

                    TipoEvento =
                        "OBSERVACION_COBRANZA",

                    FechaEvento =
                        DateTime.Now,

                    UsuarioProceso =
                        User.Identity?.Name ??
                        "COBRANZA",

                    Detalle =
                        nuevaObservacion ??
                        "Observación eliminada."
                });

            await _context.SaveChangesAsync(ct);

            TempData["Success"] =
                "Observación de cobranza guardada.";

            return RedirectToAction(
                nameof(Detalle),
                new { id });
        }

        // =========================================================
        // VER / DESCARGAR EVIDENCIA ADJUNTA POR VENDEDOR
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Archivo(
            int id,
            bool descargar = false,
            CancellationToken ct = default)
        {
            var archivo =
                await _context
                    .CobranzaCompromisoArchivos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.Id == id,
                        ct);

            if (archivo == null)
                return NotFound();

            var tipo =
                string.IsNullOrWhiteSpace(
                    archivo.TipoContenido)
                    ? "application/octet-stream"
                    : archivo.TipoContenido;

            if (descargar)
            {
                return File(
                    archivo.Contenido,
                    tipo,
                    archivo.NombreOriginal);
            }

            // Sin fileDownloadName: el navegador intenta mostrar
            // JPG/PNG/PDF en lugar de forzar descarga.
            return File(
                archivo.Contenido,
                tipo);
        }

        // =========================================================
        // EXPORTACION CSV (ABRE EN EXCEL)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> ExportarCsv(
            string? estatus = null,
            string? cliente = null,
            DateTime? desde = null,
            DateTime? hasta = null,
            CancellationToken ct = default)
        {
            var query =
                _context.CobranzaCompromisos
                    .AsNoTracking()
                    .AsQueryable();

            if (!string.IsNullOrWhiteSpace(estatus) &&
                !estatus.Equals(
                    "TODOS",
                    StringComparison.OrdinalIgnoreCase))
            {
                var e =
                    estatus.Trim().ToUpper();

                query =
                    query.Where(x =>
                        x.Estatus == e);
            }

            if (!string.IsNullOrWhiteSpace(cliente))
            {
                var c =
                    cliente.Trim();

                query =
                    query.Where(x =>
                        x.ClienteCodigo.Contains(c)
                        ||
                        (x.ClienteNombre ?? "").Contains(c)
                        ||
                        x.OrdenVentaConsecutivo.Contains(c));
            }

            if (desde.HasValue)
            {
                var inicio =
                    desde.Value.Date;

                query =
                    query.Where(x =>
                        x.FechaRegistro >= inicio);
            }

            if (hasta.HasValue)
            {
                var fin =
                    hasta.Value.Date.AddDays(1);

                query =
                    query.Where(x =>
                        x.FechaRegistro < fin);
            }

            var rows =
                await query
                    .OrderByDescending(x =>
                        x.FechaRegistro)
                    .ToListAsync(ct);

            static string Csv(string? s) =>
                "\"" +
                (s ?? "")
                    .Replace("\"", "\"\"")
                + "\"";

            var sb =
                new StringBuilder();

            sb.AppendLine(
                "Id,OV,ClienteCodigo,ClienteNombre," +
                "SaldoInicial,SaldoActual,FechaCompromiso," +
                "Estatus,UsuarioRegistro,FechaRegistro,UltimaValidacion");

            foreach (var x in rows)
            {
                sb.AppendLine(
                    string.Join(
                        ",",
                        x.Id,
                        Csv(x.OrdenVentaConsecutivo),
                        Csv(x.ClienteCodigo),
                        Csv(x.ClienteNombre),
                        x.SaldoVencidoInicial.ToString(
                            "0.00",
                            System.Globalization.CultureInfo.InvariantCulture),
                        x.SaldoPendienteActual.ToString(
                            "0.00",
                            System.Globalization.CultureInfo.InvariantCulture),
                        x.FechaCompromiso.ToString("yyyy-MM-dd"),
                        Csv(x.Estatus),
                        Csv(x.UsuarioRegistro),
                        x.FechaRegistro.ToString(
                            "yyyy-MM-dd HH:mm:ss"),
                        x.FechaUltimaValidacion?.ToString(
                            "yyyy-MM-dd HH:mm:ss") ?? ""
                    ));
            }

            var bytes =
                Encoding.UTF8
                    .GetPreamble()
                    .Concat(
                        Encoding.UTF8.GetBytes(
                            sb.ToString()))
                    .ToArray();

            return File(
                bytes,
                "text/csv; charset=utf-8",
                $"Cobranza_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private static int OrdenEstatus(
            string? estatus)
        {
            return (estatus ?? "")
                .ToUpperInvariant() switch
            {
                "INCUMPLIDO" => 0,
                "VENCE_HOY" => 1,
                "PARCIAL" => 2,
                "PENDIENTE" => 3,
                "REVISAR" => 4,
                "CUMPLIDO" => 5,
                _ => 6
            };
        }
    }
}
