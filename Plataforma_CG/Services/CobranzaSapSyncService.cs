using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public class CobranzaSapSyncService
    {
        // Evita que el botón manual y el worker automatico sincronicen al mismo tiempo.
        private static readonly SemaphoreSlim SyncLock = new(1, 1);

        private readonly AppDbContext _context;
        private readonly SapServiceLayerClient _sap;
        private readonly ILogger<CobranzaSapSyncService> _logger;

        public CobranzaSapSyncService(
            AppDbContext context,
            SapServiceLayerClient sap,
            ILogger<CobranzaSapSyncService> logger)
        {
            _context = context;
            _sap = sap;
            _logger = logger;
        }

        public async Task<int> SincronizarTodosAsync(
            string usuarioProceso = "AUTO_SAP",
            CancellationToken ct = default)
        {
            await SyncLock.WaitAsync(ct);

            try
            {
                // CUMPLIDO ya no requiere consultas periodicas.
                var ids =
                    await _context.CobranzaCompromisos
                        .AsNoTracking()
                        .Where(x => x.Estatus != "CUMPLIDO")
                        .OrderBy(x => x.FechaCompromiso)
                        .Select(x => x.Id)
                        .ToListAsync(ct);

                var revisados = 0;

                foreach (var id in ids)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var ok =
                            await SincronizarCompromisoInternoAsync(
                                id,
                                usuarioProceso,
                                ct);

                        if (ok)
                            revisados++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error sincronizando compromiso de cobranza Id={Id}",
                            id);
                    }
                }

                return revisados;
            }
            finally
            {
                SyncLock.Release();
            }
        }

        public async Task<bool> SincronizarCompromisoAsync(
            int compromisoId,
            string usuarioProceso,
            CancellationToken ct = default)
        {
            await SyncLock.WaitAsync(ct);

            try
            {
                return await SincronizarCompromisoInternoAsync(
                    compromisoId,
                    usuarioProceso,
                    ct);
            }
            finally
            {
                SyncLock.Release();
            }
        }

        private async Task<bool> SincronizarCompromisoInternoAsync(
            int compromisoId,
            string usuarioProceso,
            CancellationToken ct)
        {
            var compromiso =
                await _context.CobranzaCompromisos
                    .FirstOrDefaultAsync(
                        x => x.Id == compromisoId,
                        ct);

            if (compromiso == null)
                return false;

            var facturas =
                await _context.CobranzaCompromisoFacturas
                    .Where(x =>
                        x.CobranzaCompromisoId ==
                        compromisoId)
                    .OrderBy(x =>
                        x.FechaVencimiento)
                    .ToListAsync(ct);

            if (facturas.Count == 0)
            {
                _logger.LogWarning(
                    "Compromiso {Id} no tiene facturas relacionadas.",
                    compromisoId);

                return false;
            }

            var ahora =
                DateTime.Now;

            var estatusAnteriorCompromiso =
                compromiso.Estatus;

            var saldoAnteriorCompromiso =
                compromiso.SaldoPendienteActual;

            var requiereRevision =
                false;

            // =========================================================
            // PAGOS RECIBIDOS SAP
            //
            // Una sola consulta por cliente/compromiso.
            // Si esta consulta falla, todavía se puede actualizar
            // el saldo real de cada factura.
            // =========================================================
            List<PagoAplicadoFacturaSapViewModel> pagosCliente;

            try
            {
                pagosCliente =
                    await _sap.ObtenerPagosAplicadosClienteAsync(
                        compromiso.ClienteCodigo,
                        compromiso.FechaRegistro.Date.AddDays(-1),
                        ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No fue posible leer IncomingPayments de SAP. " +
                    "Compromiso={Id}. Se actualizaran los saldos de factura.",
                    compromisoId);

                pagosCliente =
                    new List<PagoAplicadoFacturaSapViewModel>();
            }

            // =========================================================
            // HISTORICO INICIAL
            // =========================================================
            var tieneHistorico =
                await _context.CobranzaCompromisoHistoricos
                    .AnyAsync(
                        x =>
                            x.CobranzaCompromisoId ==
                            compromisoId,
                        ct);

            if (!tieneHistorico)
            {
                _context.CobranzaCompromisoHistoricos.Add(
                    new CobranzaCompromisoHistorico
                    {
                        CobranzaCompromisoId =
                            compromiso.Id,

                        TipoEvento =
                            "CREADO",

                        EstatusNuevo =
                            compromiso.Estatus,

                        PendienteNuevo =
                            compromiso.SaldoVencidoInicial,

                        FechaEvento =
                            compromiso.FechaRegistro,

                        UsuarioProceso =
                            compromiso.UsuarioRegistro,

                        Detalle =
                            "Compromiso de pago registrado al guardar la OV."
                    });
            }

            // =========================================================
            // VALIDAR CADA FACTURA CONTRA SAP
            // =========================================================
            foreach (var factura in facturas)
            {
                ct.ThrowIfCancellationRequested();

                var pendienteAnterior =
                    decimal.Round(
                        factura.PendienteActual,
                        2,
                        MidpointRounding.AwayFromZero);

                EstadoFacturaCobranzaSapViewModel? estado;

                try
                {
                    estado =
                        await _sap.ObtenerEstadoFacturaCobranzaAsync(
                            factura.SapDocEntry,
                            ct);
                }
                catch (Exception ex)
                {
                    requiereRevision =
                        true;

                    _logger.LogError(
                        ex,
                        "No se pudo consultar factura SAP. " +
                        "Compromiso={CompromisoId}, DocEntry={DocEntry}",
                        compromiso.Id,
                        factura.SapDocEntry);

                    // Dejamos el ultimo saldo conocido.
                    continue;
                }

                if (estado == null)
                {
                    requiereRevision =
                        true;

                    _logger.LogWarning(
                        "Factura SAP no encontrada. " +
                        "Compromiso={CompromisoId}, DocEntry={DocEntry}",
                        compromiso.Id,
                        factura.SapDocEntry);

                    continue;
                }

                decimal pendienteNuevo;

                if (estado.Cancelada)
                {
                    // Cancelada NO significa pagada.
                    // La retiramos del saldo operativo pero marcamos REVISAR
                    // para que Cobranza vea el caso.
                    pendienteNuevo =
                        0m;

                    factura.Pagada =
                        false;

                    requiereRevision =
                        true;
                }
                else
                {
                    pendienteNuevo =
                        decimal.Round(
                            Math.Max(
                                0m,
                                estado.Pendiente),
                            2,
                            MidpointRounding.AwayFromZero);

                    factura.Pagada =
                        pendienteNuevo <= 0.01m;
                }

                factura.PendienteActual =
                    pendienteNuevo;

                factura.FechaUltimaValidacion =
                    ahora;

                var cambioSaldo =
                    Math.Abs(
                        pendienteAnterior -
                        pendienteNuevo) > 0.01m;

                // =====================================================
                // REGISTRAR CAMBIO DE SALDO
                // =====================================================
                if (cambioSaldo)
                {
                    string tipoEvento;

                    if (estado.Cancelada)
                    {
                        tipoEvento =
                            "FACTURA_CANCELADA";
                    }
                    else if (
                        pendienteNuevo <= 0.01m &&
                        pendienteAnterior > 0.01m)
                    {
                        tipoEvento =
                            "PAGO_TOTAL";
                    }
                    else if (
                        pendienteNuevo <
                        pendienteAnterior)
                    {
                        tipoEvento =
                            "PAGO_PARCIAL";
                    }
                    else
                    {
                        tipoEvento =
                            "AJUSTE_SALDO";
                    }

                    _context.CobranzaCompromisoHistoricos.Add(
                        new CobranzaCompromisoHistorico
                        {
                            CobranzaCompromisoId =
                                compromiso.Id,

                            CobranzaCompromisoFacturaId =
                                factura.Id,

                            TipoEvento =
                                tipoEvento,

                            PendienteAnterior =
                                pendienteAnterior,

                            PendienteNuevo =
                                pendienteNuevo,

                            FechaEvento =
                                ahora,

                            UsuarioProceso =
                                usuarioProceso,

                            Detalle =
                                estado.Cancelada
                                    ? $"La factura SAP " +
                                      $"{factura.SapDocNum ?? factura.SapDocEntry} " +
                                      $"aparece cancelada. Requiere revision."
                                    : $"SAP actualizo el saldo de la factura " +
                                      $"{factura.SapDocNum ?? factura.SapDocEntry}."
                        });
                }

                // =====================================================
                // EVIDENCIA DE PAGO SAP
                //
                // Se cruza por DocEntry de factura.
                // =====================================================
                var pagosFactura =
                    pagosCliente
                        .Where(x =>
                            x.FacturaDocEntry ==
                            factura.SapDocEntry)
                        .ToList();

                foreach (var pago in pagosFactura)
                {
                    if (pago.PagoDocEntry <= 0)
                        continue;

                    var yaExiste =
                        await _context.CobranzaCompromisoPagosSap
                            .AnyAsync(
                                x =>
                                    x.CobranzaCompromisoFacturaId ==
                                        factura.Id
                                    &&
                                    x.SapPagoDocEntry ==
                                        pago.PagoDocEntry,
                                ct);

                    if (yaExiste)
                        continue;

                    _context.CobranzaCompromisoPagosSap.Add(
                        new CobranzaCompromisoPagoSap
                        {
                            CobranzaCompromisoId =
                                compromiso.Id,

                            CobranzaCompromisoFacturaId =
                                factura.Id,

                            FacturaSapDocEntry =
                                factura.SapDocEntry,

                            SapPagoDocEntry =
                                pago.PagoDocEntry,

                            SapPagoDocNum =
                                pago.PagoDocNum,

                            FechaPagoSap =
                                pago.FechaPago,

                            MontoAplicado =
                                pago.MontoAplicado,

                            Referencia =
                                pago.Referencia,

                            Comentario =
                                pago.Comentario,

                            FechaDeteccion =
                                ahora,

                            UsuarioProceso =
                                usuarioProceso
                        });

                    _context.CobranzaCompromisoHistoricos.Add(
                        new CobranzaCompromisoHistorico
                        {
                            CobranzaCompromisoId =
                                compromiso.Id,

                            CobranzaCompromisoFacturaId =
                                factura.Id,

                            TipoEvento =
                                "PAGO_SAP_DETECTADO",

                            PendienteNuevo =
                                factura.PendienteActual,

                            FechaEvento =
                                ahora,

                            UsuarioProceso =
                                usuarioProceso,

                            Detalle =
                                $"Pago SAP #" +
                                $"{pago.PagoDocNum?.ToString() ?? pago.PagoDocEntry.ToString()} " +
                                $"aplicado por {pago.MontoAplicado:N2}. " +
                                $"Fecha SAP: " +
                                $"{(pago.FechaPago.HasValue ? pago.FechaPago.Value.ToString("dd/MM/yyyy") : "sin fecha")}."
                        });
                }
            }

            // =========================================================
            // RECALCULAR CABECERA
            // =========================================================
            var saldoActual =
                decimal.Round(
                    facturas.Sum(x =>
                        x.PendienteActual),
                    2,
                    MidpointRounding.AwayFromZero);

            string estatusNuevo;

            if (requiereRevision)
            {
                estatusNuevo =
                    "REVISAR";
            }
            else if (saldoActual <= 0.01m)
            {
                estatusNuevo =
                    "CUMPLIDO";
            }
            else if (
                saldoActual <
                compromiso.SaldoVencidoInicial - 0.01m)
            {
                estatusNuevo =
                    "PARCIAL";
            }
            else if (
                compromiso.FechaCompromiso.Date <
                DateTime.Today)
            {
                estatusNuevo =
                    "INCUMPLIDO";
            }
            else if (
                compromiso.FechaCompromiso.Date ==
                DateTime.Today)
            {
                estatusNuevo =
                    "VENCE_HOY";
            }
            else
            {
                estatusNuevo =
                    "PENDIENTE";
            }

            compromiso.SaldoPendienteActual =
                saldoActual;

            compromiso.Estatus =
                estatusNuevo;

            compromiso.UsuarioUltimaValidacion =
                usuarioProceso;

            compromiso.FechaUltimaValidacion =
                ahora;

            // =========================================================
            // CAMBIO DE ESTATUS
            // =========================================================
            if (!string.Equals(
                    estatusAnteriorCompromiso,
                    estatusNuevo,
                    StringComparison.OrdinalIgnoreCase))
            {
                _context.CobranzaCompromisoHistoricos.Add(
                    new CobranzaCompromisoHistorico
                    {
                        CobranzaCompromisoId =
                            compromiso.Id,

                        TipoEvento =
                            "CAMBIO_ESTATUS",

                        EstatusAnterior =
                            estatusAnteriorCompromiso,

                        EstatusNuevo =
                            estatusNuevo,

                        PendienteAnterior =
                            saldoAnteriorCompromiso,

                        PendienteNuevo =
                            saldoActual,

                        FechaEvento =
                            ahora,

                        UsuarioProceso =
                            usuarioProceso,

                        Detalle =
                            $"Estatus actualizado automaticamente contra SAP: " +
                            $"{estatusAnteriorCompromiso} -> {estatusNuevo}."
                    });
            }

            await _context.SaveChangesAsync(ct);

            return true;
        }
    }
}
