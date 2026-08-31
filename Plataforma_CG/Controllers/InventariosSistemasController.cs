using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using QRCoder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Plataforma_CG.Services;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Plataforma_CG.Controllers
{
    public class InventariosSistemasController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<InventariosSistemasController> _logger;
        private readonly SapServiceLayerClient _sap;

        public InventariosSistemasController(
            AppDbContext context,
            IWebHostEnvironment env,
            ILogger<InventariosSistemasController> logger,
            SapServiceLayerClient sap)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sap = sap ?? throw new ArgumentNullException(nameof(sap));
        }

        public IActionResult InventariosSis() => View();


        [HttpGet]
        public IActionResult GetInventario()
        {
            try
            {
                var inventario = _context.InventarioSistemas
                    .AsNoTracking()
                    .Select(i => new
                    {
                        i.Id,
                        i.IdArticuloSap,
                        i.Nombre,
                        i.TipoArticulo,
                        i.Marca,
                        i.Modelo,
                        i.Proveedor,
                        i.Costo,
                        i.FechaCompra,
                        i.DiasGarantia,
                        i.NumeroSerie,
                        i.Asignacion,
                        i.FechaEntrada,
                        i.FechaSalida,
                        i.TiempoVida,
                        i.Ubicacion,
                        i.Planta,
                        i.Stock,
                        i.StockMinimo,
                        i.IP,


                        FotoUsuario = string.IsNullOrWhiteSpace(i.FotoUsuario) ? "" : "OK",
                        DocumentoComodato = string.IsNullOrWhiteSpace(i.DocumentoComodato) ? "" : "OK",
                        FirmaDigital = string.IsNullOrWhiteSpace(i.FirmaDigital) ? "" : "OK",

                        i.EnRecuperacion,
                        i.EnReparacion,
                        i.MotivoFalla,
                        i.BitacoraReparacion,
                        TieneFotoFalla = !string.IsNullOrEmpty(i.FotoFalla),

                        HistorialCount = i.RegistrosHistorial.Count()
                    })
                    .OrderBy(i => i.IdArticuloSap)
                    .ToList();

                var movimientos = _context.MovimientoInventario
                    .AsNoTracking()
                    .OrderByDescending(m => m.Fecha)
                    .Take(100)
                    .Select(m => new
                    {
                        m.Id,
                        m.ArticuloSap,
                        m.NombreArticulo,
                        m.TipoMovimiento,
                        m.Cantidad,
                        m.Fecha,
                        m.Referencia
                    })
                    .ToList();

                return Json(new { ok = true, inventario, movimientos });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetHistorialArticulo(int id)
        {
            try
            {
                var historial = _context.RegistroHistorial
                    .AsNoTracking()
                    .Where(h => h.InventarioSistemasId == id)
                    .OrderByDescending(h => h.Id)
                    .Select(h => new
                    {
                        h.Id,
                        h.InventarioSistemasId,
                        h.FechaHora,
                        h.Nota,
                        TieneFoto = !string.IsNullOrEmpty(h.FotoBase64),
                        TieneDocumento = !string.IsNullOrEmpty(h.DocumentoBase64),
                        TieneFirma = !string.IsNullOrEmpty(h.FirmaBase64)
                    })
                    .ToList();

                return Json(new { ok = true, historial });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetDetalleArticulo(int id)
        {
            try
            {
                var articulo = _context.InventarioSistemas
                    .AsNoTracking()
                    .Where(i => i.Id == id)
                    .Select(i => new
                    {
                        i.Id,
                        i.IdArticuloSap,
                        i.Nombre,
                        i.TipoArticulo,
                        i.Marca,
                        i.Modelo,
                        i.Proveedor,
                        i.Costo,
                        i.FechaCompra,
                        i.DiasGarantia,
                        i.NumeroSerie,
                        i.Asignacion,
                        i.FechaEntrada,
                        i.FechaSalida,
                        i.TiempoVida,
                        i.Ubicacion,
                        i.Planta,
                        i.Stock,
                        i.StockMinimo,
                        i.FotoUsuario,
                        i.DocumentoComodato,
                        i.FirmaDigital,
                        i.HistorialAsignaciones,
                        i.IP
                    })
                    .FirstOrDefault();

                if (articulo == null)
                    return Json(new { ok = false, mensaje = "Artículo no encontrado" });

                return Json(new { ok = true, articulo });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetDetalleHistorial(int id)
        {
            try
            {
                var historial = _context.RegistroHistorial
                    .AsNoTracking()
                    .Where(h => h.Id == id)
                    .Select(h => new
                    {
                        h.Id,
                        h.InventarioSistemasId,
                        h.FechaHora,
                        h.Nota,
                        h.FotoBase64,
                        h.DocumentoBase64,
                        h.FirmaBase64
                    })
                    .FirstOrDefault();

                if (historial == null)
                    return Json(new { ok = false, mensaje = "Registro no encontrado" });

                return Json(new { ok = true, historial });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult RegistrarMovimiento(int idArticulo, string tipo, int cantidad, string referencia)
        {
            using var tx = _context.Database.BeginTransaction();

            try
            {
                var articulo = _context.InventarioSistemas.FirstOrDefault(x => x.Id == idArticulo);
                if (articulo == null)
                    return Json(new { ok = false, mensaje = "Artículo no encontrado" });

                tipo = (tipo ?? "").Trim().ToUpper();
                referencia = referencia ?? "";

                if (cantidad <= 0)
                    return Json(new { ok = false, mensaje = "La cantidad debe ser mayor a 0." });

                if (tipo == "SALIDA" && articulo.Stock < cantidad)
                    return Json(new { ok = false, mensaje = "Stock insuficiente." });

                if (tipo == "ENTRADA")
                    articulo.Stock += cantidad;
                else if (tipo == "SALIDA")
                    articulo.Stock -= cantidad;
                else
                    return Json(new { ok = false, mensaje = "Tipo de movimiento no válido." });

                _context.MovimientoInventario.Add(new MovimientoInventario
                {
                    ArticuloSap = articulo.IdArticuloSap,
                    NombreArticulo = articulo.Nombre,
                    TipoMovimiento = tipo,
                    Cantidad = cantidad,
                    Fecha = DateTime.Now,
                    Referencia = referencia
                });

                _context.SaveChanges();
                tx.Commit();

                return Json(new { ok = true, mensaje = "Movimiento registrado con éxito" });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult GuardarArticulo([FromBody] InventarioSistemas modelo, bool esCorreccion = false)
        {
            using var tx = _context.Database.BeginTransaction();

            try
            {
                if (modelo == null)
                    return Json(new { ok = false, mensaje = "No se recibió información." });

                modelo.IdArticuloSap = modelo.IdArticuloSap ?? "";
                modelo.Nombre = modelo.Nombre ?? "";
                modelo.TipoArticulo = modelo.TipoArticulo ?? "";
                modelo.Marca = modelo.Marca ?? "";
                modelo.Modelo = modelo.Modelo ?? "";
                modelo.Proveedor = modelo.Proveedor ?? "";
                modelo.NumeroSerie = modelo.NumeroSerie ?? "";
                modelo.Asignacion = modelo.Asignacion ?? "";
                modelo.TiempoVida = modelo.TiempoVida ?? "";
                modelo.Ubicacion = modelo.Ubicacion ?? "";
                modelo.Planta = modelo.Planta ?? "";
                modelo.DocumentoComodato = modelo.DocumentoComodato ?? "";
                modelo.FotoUsuario = modelo.FotoUsuario ?? "";
                modelo.FirmaDigital = modelo.FirmaDigital ?? "";
                modelo.HistorialAsignaciones = modelo.HistorialAsignaciones ?? new List<string>();
                modelo.IP = modelo.IP ?? "";
                // Valida duplicado: bloquea solo si el MISMO SAP existe en la MISMA planta
                if (!string.IsNullOrWhiteSpace(modelo.IdArticuloSap))
                {
                    // La planta se agrupa para la validación: P1 incluye 'ALMACÉN P1', TIF incluye 'ALMACÉN TIF'
                    string grupoPlanta = NormalizarGrupoPlanta(modelo.Planta);
                    string[] plantasGrupo = grupoPlanta == "P1"
                        ? new[] { "P1", "ALMACÉN P1", "ALMACEN P1" }
                        : grupoPlanta == "TIF"
                            ? new[] { "TIF", "ALMACÉN TIF", "ALMACEN TIF" }
                            : new[] { modelo.Planta ?? "" };

                    bool sapDuplicado = _context.InventarioSistemas
                                                .Any(x => x.IdArticuloSap == modelo.IdArticuloSap
                                                       && plantasGrupo.Contains(x.Planta)
                                                       && x.Id != modelo.Id);

                    if (sapDuplicado)
                    {
                        return Json(new { ok = false, mensaje = $"El ID SAP '{modelo.IdArticuloSap}' ya se encuentra registrado en la planta '{modelo.Planta}'." });
                    }
                }


                if (modelo.Id == 0)
                {
                    // Guardar archivos físicos y dejar solo ruta en BD
                    modelo.FotoUsuario = GuardarArchivoSiEsBase64(modelo.FotoUsuario, "inventario/fotos", "foto");
                    modelo.DocumentoComodato = GuardarArchivoSiEsBase64(modelo.DocumentoComodato, "inventario/documentos", "comodato");
                    modelo.FirmaDigital = GuardarArchivoSiEsBase64(modelo.FirmaDigital, "inventario/firmas", "firma");

                    _context.InventarioSistemas.Add(modelo);

                    if (modelo.Stock > 0)
                    {
                        _context.MovimientoInventario.Add(new MovimientoInventario
                        {
                            ArticuloSap = modelo.IdArticuloSap,
                            NombreArticulo = modelo.Nombre,
                            TipoMovimiento = "ENTRADA",
                            Cantidad = modelo.Stock,
                            Fecha = DateTime.Now,
                            Referencia = "Alta en sistema"
                        });
                    }

                    _context.SaveChanges();
                    tx.Commit();

                    return Json(new { ok = true, mensaje = "Artículo registrado exitosamente" });
                }
                else
                {
                    var original = _context.InventarioSistemas.FirstOrDefault(x => x.Id == modelo.Id);
                    if (original == null)
                        return Json(new { ok = false, mensaje = "Artículo no encontrado" });

                    if (esCorreccion)
                    {
                        // ========================================================
                        //  MODO CORRECCIÓN SILENCIOSA (Viene del botón Editar)
                        // ========================================================

                        original.Nombre = !string.IsNullOrEmpty(modelo.Nombre) ? modelo.Nombre : original.Nombre;
                        original.Marca = !string.IsNullOrEmpty(modelo.Marca) ? modelo.Marca : original.Marca;
                        original.Modelo = !string.IsNullOrEmpty(modelo.Modelo) ? modelo.Modelo : original.Modelo;
                        original.NumeroSerie = !string.IsNullOrEmpty(modelo.NumeroSerie) ? modelo.NumeroSerie : original.NumeroSerie;
                        original.Proveedor = !string.IsNullOrEmpty(modelo.Proveedor) ? modelo.Proveedor : original.Proveedor;
                        original.IdArticuloSap = !string.IsNullOrEmpty(modelo.IdArticuloSap) ? modelo.IdArticuloSap : original.IdArticuloSap;
                        original.TipoArticulo = !string.IsNullOrEmpty(modelo.TipoArticulo) ? modelo.TipoArticulo : original.TipoArticulo;

                        //  APLICAMOS LA CORRECCIÓN DEL NOMBRE ✨
                        original.Asignacion = modelo.Asignacion ?? "";

                        if (modelo.Costo > 0) original.Costo = modelo.Costo;
                        if (modelo.FechaCompra != null) original.FechaCompra = modelo.FechaCompra;
                        if (modelo.FechaEntrada != null) original.FechaEntrada = modelo.FechaEntrada;
                        if (modelo.DiasGarantia > 0) original.DiasGarantia = modelo.DiasGarantia;
                        if (!string.IsNullOrEmpty(modelo.TiempoVida)) original.TiempoVida = modelo.TiempoVida;
                        if (modelo.StockMinimo > 0) original.StockMinimo = modelo.StockMinimo;

                        original.Planta = modelo.Planta ?? original.Planta;
                        original.Ubicacion = modelo.Ubicacion ?? original.Ubicacion;
                        original.IP = modelo.IP ?? original.IP;

                        _context.SaveChanges();
                        tx.Commit();

                        return Json(new { ok = true, mensaje = "Datos y asignación actualizados (Modo Silencioso)" });
                    }
                    else
                    {
                        // ========================================================
                        //  MODO ASIGNACIÓN NORMAL 
                        // ========================================================

                        string nombreIngresado = (modelo.Asignacion ?? "").Trim();

                        bool cambioDeResponsable =
                            !string.Equals((original.Asignacion ?? "").Trim(), nombreIngresado, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(nombreIngresado);

                        bool cambioDeUbicacion =
                            !string.Equals(original.Ubicacion ?? "", modelo.Ubicacion ?? "", StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(original.Planta ?? "", modelo.Planta ?? "", StringComparison.OrdinalIgnoreCase);

                        string notaParaHistorial = "";

                        if (cambioDeResponsable)
                        {
                            bool esActivoFijo = (original.TipoArticulo ?? "").Equals("Activo Fijo", StringComparison.OrdinalIgnoreCase);

                            // A los activos fijos no se les descuenta stock al asignar: el equipo es una pieza física única.
                            bool descontarStock = !esActivoFijo;

                            if (descontarStock && original.Stock <= 0)
                            {
                                return Json(new { ok = false, mensaje = "Operación denegada: No hay stock disponible de este artículo." });
                            }

                            if (descontarStock)
                            {
                                original.Stock -= 1;

                                _context.MovimientoInventario.Add(new MovimientoInventario
                                {
                                    ArticuloSap = original.IdArticuloSap,
                                    NombreArticulo = original.Nombre,
                                    TipoMovimiento = "SALIDA",
                                    Cantidad = 1,
                                    Fecha = DateTime.Now,
                                    Referencia = $"Entregado a: {modelo.Asignacion}"
                                });
                            }

                            original.Asignacion = modelo.Asignacion;
                            notaParaHistorial = $"{original.TipoArticulo} asignado a: {modelo.Asignacion} | Ubicación: {modelo.Ubicacion}";
                        }
                        else if (cambioDeUbicacion)
                        {
                            notaParaHistorial = $"Equipo movido a Ubicación: {modelo.Ubicacion}";
                        }

                        original.Nombre = !string.IsNullOrEmpty(modelo.Nombre) ? modelo.Nombre : original.Nombre;
                        original.Marca = !string.IsNullOrEmpty(modelo.Marca) ? modelo.Marca : original.Marca;
                        original.Modelo = !string.IsNullOrEmpty(modelo.Modelo) ? modelo.Modelo : original.Modelo;
                        original.NumeroSerie = !string.IsNullOrEmpty(modelo.NumeroSerie) ? modelo.NumeroSerie : original.NumeroSerie;
                        original.Proveedor = !string.IsNullOrEmpty(modelo.Proveedor) ? modelo.Proveedor : original.Proveedor;
                        original.IdArticuloSap = !string.IsNullOrEmpty(modelo.IdArticuloSap) ? modelo.IdArticuloSap : original.IdArticuloSap;
                        original.TipoArticulo = !string.IsNullOrEmpty(modelo.TipoArticulo) ? modelo.TipoArticulo : original.TipoArticulo;

                        if (modelo.Costo > 0) original.Costo = modelo.Costo;
                        if (modelo.FechaCompra != null) original.FechaCompra = modelo.FechaCompra;
                        if (modelo.FechaEntrada != null) original.FechaEntrada = modelo.FechaEntrada;
                        if (modelo.DiasGarantia > 0) original.DiasGarantia = modelo.DiasGarantia;
                        if (!string.IsNullOrEmpty(modelo.TiempoVida)) original.TiempoVida = modelo.TiempoVida;
                        if (modelo.StockMinimo > 0) original.StockMinimo = modelo.StockMinimo;

                        original.Asignacion = modelo.Asignacion ?? original.Asignacion;
                        original.Planta = modelo.Planta ?? original.Planta;
                        original.Ubicacion = modelo.Ubicacion ?? original.Ubicacion;
                        original.IP = modelo.IP ?? original.IP;

                        // Guardar archivos solo si llega algo nuevo
                        if (!string.IsNullOrWhiteSpace(modelo.FotoUsuario))
                        {
                            original.FotoUsuario = GuardarArchivoSiEsBase64(modelo.FotoUsuario, "inventario/fotos", "foto");
                        }

                        if (!string.IsNullOrWhiteSpace(modelo.DocumentoComodato))
                        {
                            original.DocumentoComodato = GuardarArchivoSiEsBase64(modelo.DocumentoComodato, "inventario/documentos", "comodato");
                        }

                        if (!string.IsNullOrWhiteSpace(modelo.FirmaDigital))
                        {
                            original.FirmaDigital = GuardarArchivoSiEsBase64(modelo.FirmaDigital, "inventario/firmas", "firma");
                        }

                        if (cambioDeResponsable || cambioDeUbicacion)
                        {
                            string fotoHistorial = !string.IsNullOrWhiteSpace(modelo.FotoUsuario)
                                ? GuardarArchivoSiEsBase64(modelo.FotoUsuario, "historial/fotos", "foto_hist")
                                : "";

                            string documentoHistorial = !string.IsNullOrWhiteSpace(modelo.DocumentoComodato)
                                ? GuardarArchivoSiEsBase64(modelo.DocumentoComodato, "historial/documentos", "doc_hist")
                                : "";

                            string firmaHistorial = !string.IsNullOrWhiteSpace(modelo.FirmaDigital)
                                ? GuardarArchivoSiEsBase64(modelo.FirmaDigital, "historial/firmas", "firma_hist")
                                : "";

                            _context.RegistroHistorial.Add(new RegistroHistorial
                            {
                                InventarioSistemasId = original.Id,
                                FechaHora = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                                Nota = notaParaHistorial,
                                FotoBase64 = fotoHistorial,
                                DocumentoBase64 = documentoHistorial,
                                FirmaBase64 = firmaHistorial
                            });
                        }

                        _context.SaveChanges();
                        tx.Commit();

                        return Json(new { ok = true, mensaje = "Información procesada correctamente" });
                    }
                }
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult EscanerRapido(int id)
        {
            var articulo = _context.InventarioSistemas
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == id);

            if (articulo == null)
            {
                return Content(
                    "<h2 style='text-align:center; margin-top:50px; font-family:sans-serif;'>El equipo no existe o fue eliminado.</h2>",
                    "text/html"
                );
            }

            string responsable = string.IsNullOrEmpty(articulo.Asignacion) ? "Stock Disponible" : articulo.Asignacion;
            string estadoClass = string.IsNullOrEmpty(articulo.Asignacion) ? "bg-success" : "bg-primary";
            string ipEquipo = string.IsNullOrEmpty(articulo.IP) ? "No asignada" : articulo.IP;

            string html = $@"
            <!DOCTYPE html>
            <html lang='es'>
            <head>
                <meta charset='utf-8' />
                <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no' />
                <title>Inspección IT: {articulo.IdArticuloSap}</title>
                <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'>
                <link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.1/font/bootstrap-icons.css'>
                <style>
                    body {{ background-color: #f4f6f8; font-family: 'Segoe UI', Tahoma, sans-serif; }}
                    .brand-header {{ background-color: #4a0e0e; color: white; padding: 18px 15px; text-align: center; font-weight: 700; letter-spacing: 1px; text-transform: uppercase; font-size: 1.1rem; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
                    .card {{ border: none; border-radius: 12px; box-shadow: 0 4px 6px rgba(0,0,0,0.05); margin-bottom: 20px; }}
                    .card-header {{ background-color: white; border-bottom: 1px solid #eee; border-radius: 12px 12px 0 0 !important; font-weight: 700; padding: 15px 20px; color: #333; }}
                    .info-label {{ font-size: 0.75rem; text-transform: uppercase; color: #888; font-weight: 700; margin-bottom: 2px; display: block; }}
                    .info-value {{ font-size: 1rem; color: #333; font-weight: 500; margin-bottom: 12px; }}
                    .btn-camara {{ border: 2px dashed #4a0e0e; color: #4a0e0e; background: #fffaf9; border-radius: 10px; transition: all 0.2s; }}
                    .btn-camara:active {{ background: #fdf5f4; transform: scale(0.98); }}
                    .btn-submit {{ background-color: #4a0e0e; color: white; border-radius: 10px; border: none; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; transition: all 0.2s; }}
                    .btn-submit:active {{ transform: scale(0.98); }}
                    .btn-submit:disabled {{ background-color: #8a5e5e; }}
                    .form-control {{ border-radius: 8px; border: 1px solid #ddd; padding: 12px; font-size: 0.95rem; }}
                    .form-control:focus {{ border-color: #4a0e0e; box-shadow: 0 0 0 0.2rem rgba(74, 14, 14, 0.15); }}
                </style>
            </head>
            <body>
                <div class='brand-header'>
                    <i class='bi bi-shield-check me-2'></i> Inspección de Activos IT
                </div>
                <div class='container mt-4 mb-5'>
                    <div class='card'>
                        <div class='card-body'>
                            <div class='d-flex justify-content-between align-items-center mb-3'>
                                <span class='badge bg-secondary px-3 py-2'>{articulo.TipoArticulo}</span>
                                <span class='badge {estadoClass} px-3 py-2'><i class='bi bi-person-badge me-1'></i> {responsable}</span>
                            </div>

                            <h4 class='fw-bold mb-1' style='color: #4a0e0e;'>{articulo.IdArticuloSap}</h4>
                            <h5 class='mb-3 text-dark'>{articulo.Nombre}</h5>

                            <div class='row border-top pt-3'>
                                <div class='col-6'>
                                    <span class='info-label'>Número de Serie</span>
                                    <div class='info-value'>{articulo.NumeroSerie}</div>
                                </div>
                                <div class='col-6'>
                                    <span class='info-label'>Ubicación</span>
                                    <div class='info-value'>{articulo.Planta} | {articulo.Ubicacion}</div>
                                </div>
                                <div class='col-12 mt-2'>
                                    <span class='info-label'><i class='bi bi-ethernet text-primary'></i> Dirección IP</span>
                                    <div class='info-value' style='font-family: monospace; font-size: 1.1rem;'>{ipEquipo}</div>
                                </div>
                            </div>
                        </div>
                    </div>

                    <div class='card'>
                        <div class='card-header'>
                            <i class='bi bi-clipboard2-data me-2'></i> Formulario de Reporte
                        </div>
                        <div class='card-body p-4'>
                            <div class='mb-4'>
                                <label class='info-label mb-2'>Comentarios del estado del equipo *</label>
                                <textarea id='txtNota' class='form-control' rows='4' placeholder='Describa las condiciones actuales del equipo...'></textarea>
                            </div>

                            <div class='mb-4'>
                                <label class='info-label mb-2'>Evidencia Fotográfica</label>
                                <label for='fotoEvidencia' class='btn btn-camara w-100 py-4 fw-bold' style='cursor:pointer;'>
                                    <i class='bi bi-camera fs-3 d-block mb-2'></i>
                                    ABRIR CÁMARA
                                </label>
                                <input type='file' id='fotoEvidencia' class='d-none' accept='image/*' capture='environment' onchange='previewFoto(this)'>

                                <div id='previewContainer' style='display:none; position:relative; margin-top: 15px;'>
                                    <img id='imgPreview' src='' style='width: 100%; border-radius: 10px; border: 1px solid #ddd;' />
                                    <button type='button' class='btn btn-sm btn-dark position-absolute top-0 end-0 m-2' onclick='borrarFoto()'><i class='bi bi-trash'></i> Cambiar foto</button>
                                </div>
                            </div>

                            <button id='btnGuardar' class='btn btn-submit w-100 py-3 mt-2' onclick='guardarReporteRapido()'>
                                <i class='bi bi-cloud-arrow-up me-2'></i> Guardar Reporte
                            </button>
                        </div>
                    </div>
                </div>

                <script>
                    function previewFoto(input) {{
                        if (input.files && input.files[0]) {{
                            var reader = new FileReader();
                            reader.onload = function (e) {{
                                document.getElementById('imgPreview').src = e.target.result;
                                document.getElementById('previewContainer').style.display = 'block';
                                document.querySelector('label[for=fotoEvidencia]').style.display = 'none';
                            }}
                            reader.readAsDataURL(input.files[0]);
                        }}
                    }}

                    function borrarFoto() {{
                        document.getElementById('fotoEvidencia').value = '';
                        document.getElementById('imgPreview').src = '';
                        document.getElementById('previewContainer').style.display = 'none';
                        document.querySelector('label[for=fotoEvidencia]').style.display = 'block';
                    }}

                    async function guardarReporteRapido() {{
                        const btn = document.getElementById('btnGuardar');
                        const nota = document.getElementById('txtNota').value.trim();
                        const fotoInput = document.getElementById('fotoEvidencia');
                        let fotoBase64 = '';

                        if (!nota) {{
                            alert('Por favor ingrese un comentario antes de guardar.');
                            document.getElementById('txtNota').focus();
                            return;
                        }}

                        btn.disabled = true;
                        btn.innerHTML = '<span class=""spinner-border spinner-border-sm me-2""></span> Procesando...';

                        if (fotoInput.files.length > 0) {{
                            const reader = new FileReader();
                            reader.readAsDataURL(fotoInput.files[0]);
                            await new Promise(resolve => reader.onload = () => {{ fotoBase64 = reader.result; resolve(); }});
                        }}

                        try {{
                            const response = await fetch('/InventariosSistemas/GuardarReporteQR', {{
                                method: 'POST',
                                headers: {{ 'Content-Type': 'application/json' }},
                                body: JSON.stringify({{
                                    InventarioSistemasId: {articulo.Id},
                                    Nota: 'REPORTE QR: ' + nota,
                                    FotoBase64: fotoBase64
                                }})
                            }});

                            if ((await response.json()).ok) {{
                                alert('Reporte técnico guardado correctamente en el sistema.');
                                document.getElementById('txtNota').value = '';
                                borrarFoto();
                                btn.innerHTML = '<i class=""bi bi-check-circle me-2""></i> Reporte Guardado';
                                btn.classList.replace('btn-submit', 'btn-success');
                            }} else {{
                                alert('Error al guardar la información.');
                                btn.disabled = false;
                                btn.innerHTML = '<i class=""bi bi-cloud-arrow-up me-2""></i> Guardar Reporte';
                            }}
                        }} catch (error) {{
                            alert('Ocurrió un error al intentar comunicar con el servidor.');
                            btn.disabled = false;
                            btn.innerHTML = '<i class=""bi bi-cloud-arrow-up me-2""></i> Guardar Reporte';
                        }}
                    }}
                </script>
            </body>
            </html>";

            return Content(html, "text/html");
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult GuardarReporteQR([FromBody] RegistroHistorial reporte)
        {
            try
            {
                if (reporte == null)
                    return Json(new { ok = false, mensaje = "No se recibió información." });

                string fotoRuta = GuardarArchivoSiEsBase64(reporte.FotoBase64 ?? "", "reportes/fotos", "reporte");

                reporte.FechaHora = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                reporte.Nota = reporte.Nota ?? "";
                reporte.FotoBase64 = fotoRuta;
                reporte.DocumentoBase64 = "";
                reporte.FirmaBase64 = "";

                _context.RegistroHistorial.Add(reporte);
                _context.SaveChanges();

                return Json(new { ok = true, mensaje = "Reporte guardado." });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GenerarQRLocal(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return BadRequest();

            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(texto, QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                return File(qrCode.GetGraphic(10), "image/png");
            }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult DarDeBajaArticulo(int id, string motivo, string tipoBaja = "PIEZAS")
        {
            using var tx = _context.Database.BeginTransaction();

            try
            {
                var articulo = _context.InventarioSistemas.FirstOrDefault(x => x.Id == id);
                if (articulo == null)
                    return Json(new { ok = false, mensaje = "Artículo no encontrado" });

                motivo = motivo ?? "";
                int stockAnterior = articulo.Stock;

                // Asignamos según lo que decida el usuario
                articulo.Asignacion = (tipoBaja == "PIEZAS") ? "PARA PIEZAS" : "BAJA DEFINITIVA";
                articulo.Planta = (tipoBaja == "PIEZAS") ? "BANCO DE PIEZAS" : "BAJA TOTAL";
                articulo.Ubicacion = "ALMACÉN DE PIEZAS";
                articulo.Stock = 0;

                articulo.EnReparacion = false;
                articulo.EnRecuperacion = false;

                if (stockAnterior > 0)
                {
                    _context.MovimientoInventario.Add(new MovimientoInventario
                    {
                        ArticuloSap = articulo.IdArticuloSap,
                        NombreArticulo = articulo.Nombre,
                        TipoMovimiento = "SALIDA (BAJA)",
                        Cantidad = stockAnterior,
                        Fecha = DateTime.Now,
                        Referencia = $"[{articulo.Asignacion}] Motivo: {motivo}"
                    });
                }

                _context.RegistroHistorial.Add(new RegistroHistorial
                {
                    InventarioSistemasId = articulo.Id,
                    FechaHora = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    Nota = $"EQUIPO DADO DE BAJA ({articulo.Asignacion}). Motivo: {motivo}",
                    FotoBase64 = "",
                    DocumentoBase64 = "",
                    FirmaBase64 = ""
                });

                _context.SaveChanges();
                tx.Commit();

                return Json(new { ok = true, mensaje = "El equipo ha sido procesado correctamente." });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        // =========================================================================================
        //  MÓDULO  CONTROL DE IPs Y VLANs 
        // =========================================================================================

        [HttpGet]
        public IActionResult ControlIPs()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetVlans()
        {
            try
            {
                var vlans = _context.VlanRedes.Select(v => new {
                    planta = v.Planta,
                    id = v.VlanId,
                    nombre = v.Nombre
                }).ToList();

                return Json(new { ok = true, vlans = vlans });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpPost]
        [RevisarPermiso("MODULOIPS", "ESCRIBIR")]
        public IActionResult GuardarVlan(string planta, string id, string nombre)
        {
            try
            {

                if (!int.TryParse(id, out int vlanIdNumerico))
                {
                    return Json(new { ok = false, mensaje = "El ID de la VLAN debe ser un número." });
                }


                var nuevaVlan = new VlanRed
                {
                    Planta = planta,
                    VlanId = vlanIdNumerico.ToString(),
                    Nombre = nombre
                };

                _context.VlanRedes.Add(nuevaVlan);
                _context.SaveChanges();

                return Json(new { ok = true });
            }
            catch (Exception ex)
            {

                var mensajeError = ex.InnerException?.Message ?? ex.Message;
                return Json(new { ok = false, mensaje = "Error en SQL: " + mensajeError });
            }
        }

        [HttpGet]
        public IActionResult GetIPs()
        {
            try
            {
                var ips = _context.ControlIPs.ToList();
                return Json(new { ok = true, ips = ips });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpGet]
        public IActionResult VerificarPing(string ipAddress)
        {
            try
            {
                Ping myPing = new Ping();
                PingReply reply = myPing.Send(ipAddress, 1500);

                if (reply.Status == IPStatus.Success)
                {
                    return Json(new { ok = true, responde = true, tiempoMs = reply.RoundtripTime });
                }
                else
                {
                    return Json(new { ok = true, responde = false });
                }
            }
            catch
            {
                return Json(new { ok = true, responde = false });
            }
        }

        [HttpPost]
        [RevisarPermiso("MODULOIPS", "ESCRIBIR")]
        public IActionResult GuardarIP([FromBody] ControlRedIp modelo)
        {
            try
            {
                var usuarioReal = User.Identity?.Name ?? "Desconocido";

                var ipExistente = _context.ControlIPs.FirstOrDefault(x => x.IpAddress == modelo.IpAddress && x.Id != modelo.Id);
                if (ipExistente != null) return Json(new { ok = false, mensaje = $"La IP {modelo.IpAddress} ya existe en el sistema." });

                if (modelo.Id == 0)
                {
                    modelo.FechaAlta = DateTime.Now;
                    modelo.FechaModificacion = DateTime.Now;
                    modelo.ModificadoPor = usuarioReal;
                    _context.ControlIPs.Add(modelo);
                }
                else
                {
                    var original = _context.ControlIPs.FirstOrDefault(x => x.Id == modelo.Id);
                    if (original == null) return Json(new { ok = false, mensaje = "IP no encontrada en la base de datos." });

                    original.EquipoAsignado = modelo.EquipoAsignado ?? "";
                    original.TipoConexion = modelo.TipoConexion ?? "-";
                    original.VlanId = modelo.VlanId;
                    original.Observaciones = modelo.Observaciones ?? "";
                    original.FechaModificacion = DateTime.Now;
                    original.ModificadoPor = usuarioReal;
                    original.Usuario = modelo.Usuario ?? "";
                    original.Area = modelo.Area ?? "";
                }

                _context.SaveChanges();
                return Json(new { ok = true });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpGet]
        public IActionResult GetLogsRed()
        {
            try
            {
                var logs = _context.LogsMovimientoRed
                                   .OrderByDescending(l => l.IdLog)
                                   .Take(100)
                                   .ToList();

                return Json(new { ok = true, logs = logs });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpPost]
        public IActionResult AddLogRed([FromBody] LogMovimientoRed log)
        {
            try
            {
                log.Usuario = User.Identity?.Name ?? "Desconocido";

                _context.LogsMovimientoRed.Add(log);
                _context.SaveChanges();

                return Json(new { ok = true });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }
        [HttpPost]
        [RevisarPermiso("MODULOIPS", "ESCRIBIR")]
        public IActionResult EliminarIP(int id)
        {
            try
            {
                var ipRecord = _context.ControlIPs.Find(id);

                if (ipRecord == null)
                    return Json(new { ok = false, mensaje = "La IP no fue encontrada o ya fue eliminada." });

                _context.ControlIPs.Remove(ipRecord);
                _context.SaveChanges();

                return Json(new { ok = true, mensaje = "Dirección IP eliminada correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error al eliminar de la base de datos: " + (ex.InnerException?.Message ?? ex.Message) });
            }
        }
        [HttpGet]
        public async Task<IActionResult> ObtenerPermisosVistaControlIPs()
        {
            var login = (User?.Identity?.Name ?? "").Trim();

            var permiso = await (
                from u in _context.UsuarioSQL
                join p in _context.Perfiles on u.PerfilId equals p.Id
                join ppm in _context.PerfilPermisoModulo on p.Id equals ppm.PerfilId
                join m in _context.ModulosSistema on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == "MODULOIPS"
                      && ppm.Activo
                      && m.Activo
                select new { ppm.PuedeLeer, ppm.PuedeEscribir, ppm.PuedeEliminar }
            ).FirstOrDefaultAsync();

            if (permiso == null)
                return Json(new { puedeLeer = false, puedeEscribir = false, puedeEliminar = false });

            return Json(new { puedeLeer = permiso.PuedeLeer, puedeEscribir = permiso.PuedeEscribir, puedeEliminar = permiso.PuedeEliminar });
        }

        // =========================================================================================
        //  FIN MÓDULO ips
        // =========================================================================================
        // =========================================================================================
        //  MÓDULO DE RECUPERACIONES Y BAJAS (OFFBOARDING)
        // =========================================================================================

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult MarcarBajaUsuario(string usuario)
        {
            try
            {
                if (string.IsNullOrEmpty(usuario))
                    return Json(new { ok = false, mensaje = "Usuario no válido." });

                var equipos = _context.InventarioSistemas.Where(x => x.Asignacion == usuario && !x.EnRecuperacion).ToList();

                foreach (var e in equipos)
                {
                    e.EnRecuperacion = true;

                    _context.MovimientoInventario.Add(new MovimientoInventario
                    {
                        ArticuloSap = e.IdArticuloSap,
                        NombreArticulo = e.Nombre,
                        TipoMovimiento = "INFO",
                        Cantidad = 0,
                        Fecha = DateTime.Now,
                        Referencia = $"INICIO RECUPERACIÓN: Usuario {usuario} dado de baja manual."
                    });
                }

                _context.SaveChanges();
                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.InnerException?.Message ?? ex.Message });
            }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult FinalizarRecuperacion(int id)
        {
            try
            {
                var e = _context.InventarioSistemas.Find(id);
                if (e != null)
                {
                    string exUsuario = e.Asignacion;

                    e.EnRecuperacion = false;
                    e.Asignacion = ""; // Lo liberamos
                    e.Stock += 1; // Regresa al stock físico

                    _context.MovimientoInventario.Add(new MovimientoInventario
                    {
                        ArticuloSap = e.IdArticuloSap,
                        NombreArticulo = e.Nombre,
                        TipoMovimiento = "ENTRADA",
                        Cantidad = 1,
                        Fecha = DateTime.Now,
                        Referencia = $"RECUPERADO: Devuelto por {exUsuario}. Disponible nuevamente."
                    });


                    _context.RegistroHistorial.Add(new RegistroHistorial
                    {
                        InventarioSistemasId = e.Id,
                        FechaHora = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                        Nota = $"EQUIPO RECUPERADO. Ex-asignado: {exUsuario}. Regresa a Stock Disponible.",
                        FotoBase64 = "",
                        DocumentoBase64 = "",
                        FirmaBase64 = ""
                    });

                    _context.SaveChanges();
                    return Json(new { ok = true });
                }
                return Json(new { ok = false, mensaje = "No se encontró el equipo." });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.InnerException?.Message ?? ex.Message });
            }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult BajaMasivaUsuarios([FromBody] List<string> usuariosBaja)
        {
            try
            {
                if (usuariosBaja == null || !usuariosBaja.Any())
                    return Json(new { ok = false, mensaje = "La lista de usuarios está vacía." });

                // Buscamos todos los equipos de los nombres que hicieron match con el Excel
                var equiposAfectados = _context.InventarioSistemas
                                               .Where(x => usuariosBaja.Contains(x.Asignacion) && !x.EnRecuperacion)
                                               .ToList();

                foreach (var equipo in equiposAfectados)
                {
                    equipo.EnRecuperacion = true;

                    _context.MovimientoInventario.Add(new MovimientoInventario
                    {
                        ArticuloSap = equipo.IdArticuloSap,
                        NombreArticulo = equipo.Nombre,
                        TipoMovimiento = "INFO",
                        Cantidad = 0,
                        Fecha = DateTime.Now,
                        Referencia = $"RECUPERACIÓN (MASIVA): RRHH reportó baja de {equipo.Asignacion}."
                    });
                }

                _context.SaveChanges();
                return Json(new { ok = true, equiposAfectados = equiposAfectados.Count });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.InnerException?.Message ?? ex.Message });
            }
        }

        // =========================================================================================
        //  TALLER  / REPARACIONES
        // =========================================================================================

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult MandarATaller([FromBody] InventarioSistemas modelo)
        {
            try
            {
                var e = _context.InventarioSistemas.Find(modelo.Id);
                if (e == null) return Json(new { ok = false, mensaje = "Equipo no encontrado." });

                e.EnReparacion = true;
                e.MotivoFalla = modelo.MotivoFalla ?? "No especificado";
                e.FotoFalla = modelo.FotoFalla; // El Base64 de la foto
                e.BitacoraReparacion = $"[{DateTime.Now.ToString("dd/MM/yy HH:mm")}] INGRESO: {e.MotivoFalla}";

                // Si estaba disponible y NO es activo fijo, le descontamos 1 al stock físico temporalmente porque está roto.
                // (Los activos fijos son piezas físicas únicas: no se les descuenta stock.)
                bool esAF = (e.TipoArticulo ?? "").Equals("Activo Fijo", StringComparison.OrdinalIgnoreCase);
                bool descontarStockTaller = !esAF && string.IsNullOrEmpty(e.Asignacion) && e.Stock > 0;
                if (descontarStockTaller) e.Stock -= 1;

                _context.MovimientoInventario.Add(new MovimientoInventario
                {
                    ArticuloSap = e.IdArticuloSap,
                    NombreArticulo = e.Nombre,
                    TipoMovimiento = "SALIDA",
                    Cantidad = esAF ? 0 : 1,
                    Fecha = DateTime.Now,
                    Referencia = $"ENVIADO A TALLER: {e.MotivoFalla}"
                });

                _context.SaveChanges();
                return Json(new { ok = true });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        // ========================================================
        //  TRANSFERENCIAS ENTRE PLANTAS
        // ========================================================
        [HttpGet]
        public IActionResult ObtenerTransferencias(string? estado = null)
        {
            try
            {
                var query = _context.TransferenciasInventario
                    .AsNoTracking()
                    .OrderByDescending(t => t.FechaEnvio);

                if (!string.IsNullOrWhiteSpace(estado))
                    query = (IOrderedQueryable<TransferenciaInventario>)query.Where(t => t.Estado == estado);

                var lista = query.ToList().Select(t => new
                {
                    t.Id,
                    t.IdInventario,
                    t.IdArticuloSap,
                    t.Nombre,
                    PlantaOrigen = t.PlantaOrigen,
                    PlantaDestino = t.PlantaDestino,
                    t.Estado,
                    t.Cantidad,
                    FechaEnvio = t.FechaEnvio.ToString("dd/MM/yyyy HH:mm"),
                    FechaRecepcion = t.FechaRecepcion?.ToString("dd/MM/yyyy HH:mm"),
                    t.Nota
                }).ToList();

                return Json(new { ok = true, data = lista });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult ObtenerCatalogosInventario()
        {
            try
            {
                var marcas = _context.MarcasInventario.Where(m => m.Activa).OrderBy(m => m.Nombre).Select(m => m.Nombre).ToList();
                var areas = _context.AreasInventario.Where(a => a.Activa).OrderBy(a => a.Nombre).Select(a => a.Nombre).ToList();
                return Json(new { ok = true, marcas = marcas, areas = areas });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult AgregarMarcaInventario(string nombre)
        {
            try
            {
                nombre = (nombre ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(nombre))
                    return Json(new { ok = false, mensaje = "Indica el nombre de la marca." });

                if (_context.MarcasInventario.Any(m => m.Nombre == nombre))
                    return Json(new { ok = false, mensaje = "Esa marca ya existe en el catálogo." });

                _context.MarcasInventario.Add(new MarcaInventario { Nombre = nombre });
                _context.SaveChanges();
                return Json(new { ok = true, mensaje = "Marca agregada." });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult AgregarAreaInventario(string nombre)
        {
            try
            {
                nombre = (nombre ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(nombre))
                    return Json(new { ok = false, mensaje = "Indica el nombre del área/ubicación." });

                if (_context.AreasInventario.Any(a => a.Nombre == nombre))
                    return Json(new { ok = false, mensaje = "Esa área ya existe en el catálogo." });

                _context.AreasInventario.Add(new AreaInventario { Nombre = nombre });
                _context.SaveChanges();
                return Json(new { ok = true, mensaje = "Área agregada." });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult CrearTransferencia(int idInventario, string plantaDestino, string? nota, int cantidad = 1)
        {
            try
            {
                var articulo = _context.InventarioSistemas.Find(idInventario);
                if (articulo == null)
                    return Json(new { ok = false, mensaje = "Artículo no encontrado." });

                string grupoOrigen = NormalizarGrupoPlanta(articulo.Planta);
                string grupoDestino = NormalizarGrupoPlanta(plantaDestino);
                if (string.IsNullOrWhiteSpace(plantaDestino) || grupoDestino == grupoOrigen || grupoDestino == "")
                    return Json(new { ok = false, mensaje = "La planta destino debe ser distinta a la planta de origen." });

                bool esConsumible = (articulo.TipoArticulo ?? "").Equals("Consumible", StringComparison.OrdinalIgnoreCase);

                // Para consumibles se transfiere una cantidad; para el resto, la unidad completa (1)
                if (esConsumible)
                {
                    if (cantidad < 1)
                        return Json(new { ok = false, mensaje = "La cantidad a transferir debe ser al menos 1." });
                    if (cantidad > articulo.Stock)
                        return Json(new { ok = false, mensaje = $"Solo hay {articulo.Stock} unidades disponibles en la planta de origen." });
                }
                else
                {
                    cantidad = 1;
                }

                _context.TransferenciasInventario.Add(new TransferenciaInventario
                {
                    IdInventario = articulo.Id,
                    IdArticuloSap = articulo.IdArticuloSap,
                    Nombre = articulo.Nombre,
                    PlantaOrigen = articulo.Planta,
                    PlantaDestino = plantaDestino,
                    Estado = "ENVIADO",
                    Cantidad = cantidad,
                    FechaEnvio = DateTime.Now,
                    Nota = nota
                });

                _context.MovimientoInventario.Add(new MovimientoInventario
                {
                    ArticuloSap = articulo.IdArticuloSap,
                    NombreArticulo = articulo.Nombre,
                    TipoMovimiento = "SALIDA",
                    Cantidad = cantidad,
                    Fecha = DateTime.Now,
                    Referencia = $"TRANSFERENCIA ENVIADA ({cantidad}x): {articulo.Planta} → {plantaDestino}"
                });

                _context.SaveChanges();
                return Json(new { ok = true, mensaje = "Envío registrado. La planta destino deberá marcarlo como recibido." });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult RecibirTransferencia(int idTransferencia)
        {
            try
            {
                var t = _context.TransferenciasInventario.FirstOrDefault(x => x.Id == idTransferencia);
                if (t == null)
                    return Json(new { ok = false, mensaje = "Transferencia no encontrada." });

                if (t.Estado == "RECIBIDO")
                    return Json(new { ok = false, mensaje = "Esta transferencia ya fue recibida." });

                var articulo = _context.InventarioSistemas.Find(t.IdInventario);
                if (articulo == null)
                    return Json(new { ok = false, mensaje = "El artículo ya no existe en el inventario." });

                bool esConsumible = (articulo.TipoArticulo ?? "").Equals("Consumible", StringComparison.OrdinalIgnoreCase);

                if (esConsumible && t.Cantidad > 1)
                {
                    // Buscar si ya existe un artículo con el mismo SAP en la planta de destino
                    var destino = _context.InventarioSistemas
                        .FirstOrDefault(x => x.IdArticuloSap == articulo.IdArticuloSap
                            && ((x.Planta ?? "").Trim().ToLower() == (t.PlantaDestino ?? "").Trim().ToLower()));

                    if (destino != null)
                    {
                        // Sumar la cantidad transferida al stock del artículo de destino
                        destino.Stock += t.Cantidad;
                    }
                    else
                    {
                        // Crear una fila nueva en la planta destino con la cantidad transferida
                        _context.InventarioSistemas.Add(new InventarioSistemas
                        {
                            IdArticuloSap = articulo.IdArticuloSap,
                            Nombre = articulo.Nombre,
                            TipoArticulo = articulo.TipoArticulo,
                            Marca = articulo.Marca ?? "",
                            Modelo = articulo.Modelo ?? "",
                            Proveedor = articulo.Proveedor ?? "",
                            Costo = articulo.Costo,
                            FechaCompra = articulo.FechaCompra,
                            DiasGarantia = articulo.DiasGarantia,
                            Planta = t.PlantaDestino,
                            Ubicacion = articulo.Ubicacion ?? "",
                            NumeroSerie = articulo.NumeroSerie ?? "",
                            Asignacion = articulo.Asignacion ?? "",
                            TiempoVida = articulo.TiempoVida ?? "",
                            Stock = t.Cantidad,
                            StockMinimo = articulo.StockMinimo,
                            FotoUsuario = articulo.FotoUsuario ?? "",
                            DocumentoComodato = articulo.DocumentoComodato ?? "",
                            FirmaDigital = articulo.FirmaDigital ?? "",
                            IP = articulo.IP ?? "",
                            FechaEntrada = DateTime.Now
                        });
                    }

                    // Restar la cantidad transferida del stock de la planta de origen
                    articulo.Stock -= t.Cantidad;
                }
                else
                {
                    // No consumible (o cantidad 1): pasamos la unidad completa a la planta de destino
                    articulo.Planta = t.PlantaDestino;
                }

                _context.MovimientoInventario.Add(new MovimientoInventario
                {
                    ArticuloSap = articulo.IdArticuloSap,
                    NombreArticulo = articulo.Nombre,
                    TipoMovimiento = "ENTRADA",
                    Cantidad = t.Cantidad,
                    Fecha = DateTime.Now,
                    Referencia = $"TRANSFERENCIA RECIBIDA ({t.Cantidad}x): {t.PlantaOrigen} → {t.PlantaDestino}"
                });

                t.Estado = "RECIBIDO";
                t.FechaRecepcion = DateTime.Now;

                _context.SaveChanges();
                return Json(new { ok = true, mensaje = "Artículo recibido y actualizado a la nueva planta." });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult AgregarBitacoraReparacion(int id, string nota)
        {
            try
            {
                var e = _context.InventarioSistemas.Find(id);
                if (e == null) return Json(new { ok = false, mensaje = "Equipo no encontrado." });

                // Agregamos la nueva nota a la historia existente separada por un salto de línea (o |)
                string nuevaEntrada = $"[{DateTime.Now.ToString("dd/MM/yy HH:mm")}] ACTUALIZACIÓN: {nota}";
                e.BitacoraReparacion = string.IsNullOrEmpty(e.BitacoraReparacion) ? nuevaEntrada : e.BitacoraReparacion + "||" + nuevaEntrada;

                _context.SaveChanges();
                return Json(new { ok = true });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpPost]
        [RevisarPermiso("INVENTARIOSISTEMAS", "ESCRIBIR")]
        public IActionResult FinalizarReparacion(int id, string comentarioFinal, bool regresarAlUsuario = false)
        {
            try
            {
                var e = _context.InventarioSistemas.Find(id);
                if (e == null) return Json(new { ok = false, mensaje = "Equipo no encontrado." });

                string usuarioAnterior = e.Asignacion ?? "";
                string notaHistorial = $"REPARACIÓN FINALIZADA: {comentarioFinal}";

                e.EnReparacion = false;
                e.MotivoFalla = "";
                e.BitacoraReparacion = "";
                e.FotoFalla = "";

                if (regresarAlUsuario && !string.IsNullOrEmpty(usuarioAnterior))
                {
                    // No tocamos la asignación ni el stock, solo guardamos el historial del usuario
                    _context.RegistroHistorial.Add(new RegistroHistorial
                    {
                        InventarioSistemasId = e.Id,
                        FechaHora = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                        Nota = $"EQUIPO REPARADO Y DEVUELTO AL USUARIO. Reporte: {comentarioFinal}",
                        FotoBase64 = "",
                        DocumentoBase64 = "",
                        FirmaBase64 = ""
                    });
                }
                else
                {
                    // Lo mandas a Stock General
                    e.Asignacion = "";
                    e.Stock += 1;

                    _context.MovimientoInventario.Add(new MovimientoInventario
                    {
                        ArticuloSap = e.IdArticuloSap,
                        NombreArticulo = e.Nombre,
                        TipoMovimiento = "ENTRADA",
                        Cantidad = 1,
                        Fecha = DateTime.Now,
                        Referencia = notaHistorial
                    });
                }

                _context.SaveChanges();
                return Json(new { ok = true });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpGet]
        public IActionResult GetFotoTaller(int id)
        {
            try
            {
                var e = _context.InventarioSistemas.Find(id);
                if (e != null && !string.IsNullOrEmpty(e.FotoFalla))
                {
                    return Json(new { ok = true, fotoBase64 = e.FotoFalla });
                }
                return Json(new { ok = false, mensaje = "Sin foto" });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult ExtraerPiezas(int idDestino, int idDonante, string piezas)
        {
            try
            {
                var destino = _context.InventarioSistemas.Find(idDestino);
                var donante = _context.InventarioSistemas.Find(idDonante);

                if (destino == null || donante == null)
                    return Json(new { ok = false, mensaje = "No se encontraron los equipos." });

                //  Actualizar la bitácora del equipo que se está salvando
                string notaDestino = $"[{DateTime.Now:dd/MM/yy HH:mm}] IMPORTACIÓN DE PIEZAS: Se instaló '{piezas}' (Extraído de SAP: {donante.IdArticuloSap})";
                destino.BitacoraReparacion = string.IsNullOrEmpty(destino.BitacoraReparacion) ? notaDestino : destino.BitacoraReparacion + "||" + notaDestino;

                //  Dejar marca  en el historial del equipo Donador (Chatarra)
                _context.RegistroHistorial.Add(new RegistroHistorial
                {
                    InventarioSistemasId = donante.Id,
                    FechaHora = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    Nota = $"DONACIÓN DE PIEZAS: Se le extrajo '{piezas}' para reparar el equipo SAP: {destino.IdArticuloSap}",
                    FotoBase64 = "",
                    DocumentoBase64 = "",
                    FirmaBase64 = ""
                });

                _context.SaveChanges();
                return Json(new { ok = true });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }

        [HttpGet]
        public IActionResult GetInventarioChatarra()
        {
            try
            {
                var chatarra = _context.InventarioSistemas.AsNoTracking()
                    .Where(x => x.Asignacion == "PARA PIEZAS") // SOLO trae los que sirven para piezas
                    .Select(x => new
                    {
                        x.Id,
                        x.IdArticuloSap,
                        x.Nombre,
                        x.Marca,
                        x.Modelo,
                        Extracciones = _context.RegistroHistorial
                            .Where(h => h.InventarioSistemasId == x.Id && (h.Nota.Contains("DONACIÓN") || h.Nota.Contains("EXTRAÍDO")))
                            .Select(h => new { h.FechaHora, h.Nota })
                            .OrderByDescending(h => h.FechaHora).ToList()
                    }).ToList();

                return Json(new { ok = true, data = chatarra });
            }
            catch (Exception ex) { return Json(new { ok = false, mensaje = ex.Message }); }
        }
        [HttpGet]
        public async Task<IActionResult> ObtenerPermisosVistaInventarioSistemas()
        {
            var login = (User?.Identity?.Name ?? "").Trim();

            var permiso = await (
                from u in _context.UsuarioSQL
                join p in _context.Perfiles on u.PerfilId equals p.Id
                join ppm in _context.PerfilPermisoModulo on p.Id equals ppm.PerfilId
                join m in _context.ModulosSistema on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == "INVENTARIOSISTEMAS"
                      && ppm.Activo
                      && m.Activo
                select new
                {
                    ppm.PuedeLeer,
                    ppm.PuedeEscribir,
                    ppm.PuedeEliminar
                }
            ).FirstOrDefaultAsync();

            if (permiso == null)
                return Json(new { puedeLeer = false, puedeEscribir = false, puedeEliminar = false });

            return Json(new
            {
                puedeLeer = permiso.PuedeLeer,
                puedeEscribir = permiso.PuedeEscribir,
                puedeEliminar = permiso.PuedeEliminar
            });
        }

        [HttpGet]
        public IActionResult GetHistorialBajas()
        {
            var bajas = _context.MovimientoInventario
                .Where(m => m.TipoMovimiento == "SALIDA (BAJA)")
                .OrderByDescending(m => m.Fecha)
                .Select(m => new { m.Fecha, m.ArticuloSap, m.NombreArticulo, m.Referencia })
                .ToList();
            return Json(new { ok = true, data = bajas });
        }
        // =========================================================================================
        // METODOS AUXILIARES (ARCHIVOS, FOTOS, ETC)
        // =========================================================================================

        private string GuardarArchivoSiEsBase64(string valor, string carpetaRelativa, string prefijo)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return "";

            if (!EsBase64DataUrl(valor))
                return valor;

            try
            {
                var partes = valor.Split(',');
                if (partes.Length < 2)
                    return "";

                string metadata = partes[0];
                string contenidoBase64 = partes[1];

                string extension = ObtenerExtensionDesdeDataUrl(metadata);
                byte[] bytes = Convert.FromBase64String(contenidoBase64);

                string carpetaFisica = Path.Combine(_env.WebRootPath, "uploads", carpetaRelativa.Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (!Directory.Exists(carpetaFisica))
                    Directory.CreateDirectory(carpetaFisica);

                string nombreArchivo = $"{prefijo}_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
                string rutaFisica = Path.Combine(carpetaFisica, nombreArchivo);

                System.IO.File.WriteAllBytes(rutaFisica, bytes);

                return $"/uploads/{carpetaRelativa}/{nombreArchivo}".Replace("\\", "/");
            }
            catch
            {
                return "";
            }
        }

        private bool EsBase64DataUrl(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return false;

            return valor.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                   && valor.Contains(";base64,");
        }

        private string ObtenerExtensionDesdeDataUrl(string metadata)
        {
            metadata = (metadata ?? "").ToLower();

            if (metadata.Contains("image/jpeg")) return ".jpg";
            if (metadata.Contains("image/jpg")) return ".jpg";
            if (metadata.Contains("image/png")) return ".png";
            if (metadata.Contains("image/gif")) return ".gif";
            if (metadata.Contains("application/pdf")) return ".pdf";
            if (metadata.Contains("image/webp")) return ".webp";

            return ".bin";
        }


        // =============================================================
        // PEGAR DENTRO DE LA CLASE InventariosSistemasController
        // Requiere: using Microsoft.EntityFrameworkCore;
        // =============================================================

        public sealed class GuardarTopologiaRedDto
        {
            public string Planta { get; set; } = "";
            public string ConfiguracionJson { get; set; } = "{}";
            public string PosicionesJson { get; set; } = "{}";
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerTopologiaRed(string planta)
        {
            planta = (planta ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(planta))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La planta es obligatoria."
                });
            }

            var cn = _context.Database.GetDbConnection();
            var cerrarConexion = cn.State != System.Data.ConnectionState.Open;

            try
            {
                if (cerrarConexion)
                    await cn.OpenAsync();

                await using var cmd = cn.CreateCommand();

                cmd.CommandText = @"
SELECT TOP (1)
    ConfiguracionJson,
    PosicionesJson,
    FechaModificacion,
    ModificadoPor
FROM dbo.TopologiaRedConfiguracion
WHERE Planta = @Planta;";

                var pPlanta = cmd.CreateParameter();
                pPlanta.ParameterName = "@Planta";
                pPlanta.Value = planta;
                cmd.Parameters.Add(pPlanta);

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                {
                    return Json(new
                    {
                        ok = true,
                        existe = false,
                        configuracionJson = "{}",
                        posicionesJson = "{}",
                        fechaModificacion = (DateTime?)null,
                        modificadoPor = ""
                    });
                }

                return Json(new
                {
                    ok = true,
                    existe = true,
                    configuracionJson =
                        rd.IsDBNull(0) ? "{}" : rd.GetString(0),
                    posicionesJson =
                        rd.IsDBNull(1) ? "{}" : rd.GetString(1),
                    fechaModificacion =
                        rd.IsDBNull(2) ? (DateTime?)null : rd.GetDateTime(2),
                    modificadoPor =
                        rd.IsDBNull(3) ? "" : rd.GetString(3)
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje =
                        "No se pudo consultar la configuración de topología: "
                        + ex.Message
                });
            }
            finally
            {
                if (cerrarConexion &&
                    cn.State == System.Data.ConnectionState.Open)
                {
                    await cn.CloseAsync();
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarTopologiaRed(
            [FromBody] GuardarTopologiaRedDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La solicitud está vacía."
                });
            }

            var planta = (dto.Planta ?? "").Trim().ToUpperInvariant();
            var configuracionJson =
                string.IsNullOrWhiteSpace(dto.ConfiguracionJson)
                    ? "{}"
                    : dto.ConfiguracionJson;
            var posicionesJson =
                string.IsNullOrWhiteSpace(dto.PosicionesJson)
                    ? "{}"
                    : dto.PosicionesJson;

            if (string.IsNullOrWhiteSpace(planta))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La planta es obligatoria."
                });
            }

            if (planta.Length > 50)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El nombre de la planta excede 50 caracteres."
                });
            }

            try
            {
                using var configDoc =
                    System.Text.Json.JsonDocument.Parse(configuracionJson);
                using var positionsDoc =
                    System.Text.Json.JsonDocument.Parse(posicionesJson);
            }
            catch (System.Text.Json.JsonException)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La configuración o las posiciones contienen JSON inválido."
                });
            }

            var usuario =
                User?.Identity?.Name
                ?? HttpContext?.User?.Identity?.Name
                ?? "SISTEMA";

            if (usuario.Length > 150)
                usuario = usuario.Substring(0, 150);

            var cn = _context.Database.GetDbConnection();
            var cerrarConexion = cn.State != System.Data.ConnectionState.Open;

            try
            {
                if (cerrarConexion)
                    await cn.OpenAsync();

                await using var tx = await cn.BeginTransactionAsync();
                await using var cmd = cn.CreateCommand();

                cmd.Transaction = tx;

                cmd.CommandText = @"
UPDATE dbo.TopologiaRedConfiguracion
SET
    ConfiguracionJson = @ConfiguracionJson,
    PosicionesJson = @PosicionesJson,
    FechaModificacion = SYSDATETIME(),
    ModificadoPor = @ModificadoPor
WHERE Planta = @Planta;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.TopologiaRedConfiguracion
    (
        Planta,
        ConfiguracionJson,
        PosicionesJson,
        FechaModificacion,
        ModificadoPor
    )
    VALUES
    (
        @Planta,
        @ConfiguracionJson,
        @PosicionesJson,
        SYSDATETIME(),
        @ModificadoPor
    );
END;";

                void AddParameter(string name, object value)
                {
                    var parameter = cmd.CreateParameter();
                    parameter.ParameterName = name;
                    parameter.Value = value;
                    cmd.Parameters.Add(parameter);
                }

                AddParameter("@Planta", planta);
                AddParameter("@ConfiguracionJson", configuracionJson);
                AddParameter("@PosicionesJson", posicionesJson);
                AddParameter("@ModificadoPor", usuario);

                await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = "Topología guardada correctamente.",
                    planta,
                    fechaModificacion = DateTime.Now,
                    modificadoPor = usuario
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje =
                        "No se pudo guardar la configuración de topología: "
                        + ex.Message
                });
            }
            finally
            {
                if (cerrarConexion &&
                    cn.State == System.Data.ConnectionState.Open)
                {
                    await cn.CloseAsync();
                }
            }
        }

        // =============================================================
        // PEGAR DENTRO DE LA CLASE InventariosSistemasController
        //
        // Requiere:
        // using Microsoft.EntityFrameworkCore;
        // =============================================================

        public sealed class GuardarMapaTiIndustrialDto
        {
            public string ClaveMapa { get; set; } =
                "MAPA_TI_INDUSTRIAL_GLOBAL";

            public string MapaJson { get; set; } = "{}";
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerMapaTiIndustrial(
            string claveMapa = "MAPA_TI_INDUSTRIAL_GLOBAL")
        {
            claveMapa = (claveMapa ?? "")
                .Trim()
                .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(claveMapa))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La clave del mapa es obligatoria."
                });
            }

            var cn = _context.Database.GetDbConnection();
            var cerrarConexion =
                cn.State != System.Data.ConnectionState.Open;

            try
            {
                if (cerrarConexion)
                    await cn.OpenAsync();

                await using var cmd = cn.CreateCommand();

                cmd.CommandText = @"
SELECT TOP (1)
    MapaJson,
    FechaModificacion,
    ModificadoPor
FROM dbo.MapaTiIndustrialConfiguracion
WHERE ClaveMapa = @ClaveMapa;";

                var pClave = cmd.CreateParameter();
                pClave.ParameterName = "@ClaveMapa";
                pClave.Value = claveMapa;
                cmd.Parameters.Add(pClave);

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                {
                    return Json(new
                    {
                        ok = true,
                        existe = false,
                        mapaJson = "",
                        fechaModificacion = (DateTime?)null,
                        modificadoPor = ""
                    });
                }

                return Json(new
                {
                    ok = true,
                    existe = true,
                    mapaJson =
                        rd.IsDBNull(0) ? "{}" : rd.GetString(0),
                    fechaModificacion =
                        rd.IsDBNull(1)
                            ? (DateTime?)null
                            : rd.GetDateTime(1),
                    modificadoPor =
                        rd.IsDBNull(2) ? "" : rd.GetString(2)
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje =
                        "No se pudo consultar el Mapa TI: "
                        + ex.Message
                });
            }
            finally
            {
                if (cerrarConexion &&
                    cn.State == System.Data.ConnectionState.Open)
                {
                    await cn.CloseAsync();
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(15_000_000)]
        public async Task<IActionResult> GuardarMapaTiIndustrial(
            [FromBody] GuardarMapaTiIndustrialDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La solicitud está vacía."
                });
            }

            var claveMapa = (dto.ClaveMapa ?? "")
                .Trim()
                .ToUpperInvariant();

            var mapaJson = string.IsNullOrWhiteSpace(dto.MapaJson)
                ? "{}"
                : dto.MapaJson;

            if (string.IsNullOrWhiteSpace(claveMapa))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La clave del mapa es obligatoria."
                });
            }

            if (claveMapa.Length > 100)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "La clave del mapa excede 100 caracteres."
                });
            }

            /*
             * Protege el servidor contra estados excesivamente grandes.
             * El plano cargado desde la vista ya está limitado a 3 MB.
             */
            if (System.Text.Encoding.UTF8.GetByteCount(mapaJson)
                > 12_000_000)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "El mapa excede el tamaño máximo permitido."
                });
            }

            try
            {
                using var json =
                    System.Text.Json.JsonDocument.Parse(mapaJson);

                if (json.RootElement.ValueKind
                    != System.Text.Json.JsonValueKind.Object)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje =
                            "El contenido del mapa debe ser un objeto JSON."
                    });
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El mapa contiene JSON inválido."
                });
            }

            var usuario =
                User?.Identity?.Name
                ?? HttpContext?.User?.Identity?.Name
                ?? "SISTEMA";

            if (usuario.Length > 150)
                usuario = usuario.Substring(0, 150);

            var cn = _context.Database.GetDbConnection();
            var cerrarConexion =
                cn.State != System.Data.ConnectionState.Open;

            try
            {
                if (cerrarConexion)
                    await cn.OpenAsync();

                await using var tx =
                    await cn.BeginTransactionAsync();

                await using var cmd = cn.CreateCommand();
                cmd.Transaction = tx;

                cmd.CommandText = @"
UPDATE dbo.MapaTiIndustrialConfiguracion
SET
    MapaJson = @MapaJson,
    FechaModificacion = SYSDATETIME(),
    ModificadoPor = @ModificadoPor
WHERE ClaveMapa = @ClaveMapa;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.MapaTiIndustrialConfiguracion
    (
        ClaveMapa,
        MapaJson,
        FechaModificacion,
        ModificadoPor
    )
    VALUES
    (
        @ClaveMapa,
        @MapaJson,
        SYSDATETIME(),
        @ModificadoPor
    );
END;";

                void AddParameter(string name, object value)
                {
                    var parameter = cmd.CreateParameter();
                    parameter.ParameterName = name;
                    parameter.Value = value;
                    cmd.Parameters.Add(parameter);
                }

                AddParameter("@ClaveMapa", claveMapa);
                AddParameter("@MapaJson", mapaJson);
                AddParameter("@ModificadoPor", usuario);

                await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = "Mapa TI guardado correctamente.",
                    claveMapa,
                    fechaModificacion = DateTime.Now,
                    modificadoPor = usuario
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje =
                        "No se pudo guardar el Mapa TI: "
                        + ex.Message
                });
            }
            finally
            {
                if (cerrarConexion &&
                    cn.State == System.Data.ConnectionState.Open)
                {
                    await cn.CloseAsync();
                }
            }
        }




        // =========================================================================================
        // MÓDULO CONTROL DE COMPRAS TI
        // Solicitud SAP -> Cotización -> Autorización -> Recepción -> Factura -> Pago
        // Reutiliza ProveedorSap, ArticuloSap y SapServiceLayerClient.
        // =========================================================================================

        private const string MODULO_COMPRAS_TI = "MODULO_COMPRAS_TI";
        private const decimal TOLERANCIA_CONCILIACION = 0.02m;
        private const int SAP_OBJETO_SOLICITUD_COMPRA = 1470000113;

        [HttpGet("/InventariosSistemas/ControlCompras")]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public IActionResult ControlComprasTi()
        {
            return View("~/Views/InventariosSistemas/ControlCompras.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerPermisosVistaComprasTi()
        {
            var login = (User?.Identity?.Name ?? "").Trim();

            var permiso = await (
                from u in _context.UsuarioSQL
                join p in _context.Perfiles on u.PerfilId equals p.Id
                join ppm in _context.PerfilPermisoModulo on p.Id equals ppm.PerfilId
                join m in _context.ModulosSistema on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == MODULO_COMPRAS_TI
                      && ppm.Activo
                      && m.Activo
                select new
                {
                    ppm.PuedeLeer,
                    ppm.PuedeEscribir,
                    ppm.PuedeEliminar
                }
            ).FirstOrDefaultAsync();

            if (permiso == null)
            {
                return Json(new
                {
                    puedeLeer = false,
                    puedeEscribir = false,
                    puedeEliminar = false
                });
            }

            return Json(new
            {
                puedeLeer = permiso.PuedeLeer,
                puedeEscribir = permiso.PuedeEscribir,
                puedeEliminar = permiso.PuedeEliminar
            });
        }

        [HttpGet]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public async Task<IActionResult> GetDashboardComprasTi(
            CancellationToken ct = default)
        {
            var query = _context.CompraTiSolicitudes.AsNoTracking();

            var total = await query.CountAsync(ct);
            var pendientesAutorizacion = await query.CountAsync(x =>
                x.Estatus == "COTIZACION_REGISTRADA", ct);
            var pendientesOrdenCompra = await query.CountAsync(x =>
                x.Autorizada &&
                !x.LiberadaPago &&
                !_context.CompraTiOrdenesCompraSap.Any(o =>
                    o.SolicitudId == x.Id &&
                    o.Activa &&
                    !o.Cancelada), ct);

            var pendientesRecepcion = await query.CountAsync(x =>
                x.Autorizada && !x.RecibidaConforme && !x.LiberadaPago, ct);
            var diferencias = await query.CountAsync(x =>
                x.Estatus == "DIFERENCIA_FACTURA", ct);
            var listasPago = await query.CountAsync(x =>
                x.Autorizada && x.RecibidaConforme && x.ConciliacionOk &&
                !x.LiberadaPago && x.TotalFactura > 0, ct);
            var liberadasPago = await query.CountAsync(x => x.LiberadaPago, ct);

            return Json(new
            {
                ok = true,
                total,
                pendientesAutorizacion,
                pendientesOrdenCompra,
                pendientesRecepcion,
                diferencias,
                listasPago,
                liberadasPago
            });
        }

        [HttpGet]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public async Task<IActionResult> GetSolicitudesCompraTi(
            int page = 1,
            int pageSize = 50,
            string search = "",
            string estatus = "",
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            search = (search ?? "").Trim().ToLowerInvariant();
            estatus = (estatus ?? "").Trim().ToUpperInvariant();

            var query = _context.CompraTiSolicitudes.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    (x.Folio ?? "").ToLower().Contains(search) ||
                    (x.SolicitudSap ?? "").ToLower().Contains(search) ||
                    (x.Titulo ?? "").ToLower().Contains(search) ||
                    (x.ProveedorSapCodigo ?? "").ToLower().Contains(search) ||
                    (x.ProveedorNombreSnapshot ?? "").ToLower().Contains(search) ||
                    (x.Solicitante ?? "").ToLower().Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(estatus))
                query = query.Where(x => x.Estatus == estatus);

            var total = await query.CountAsync(ct);

            var solicitudes = await query
                .OrderByDescending(x => x.FechaCreacion)
                .ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Id,
                    x.Folio,
                    x.SolicitudSap,
                    x.SolicitudSapDocEntry,
                    x.SolicitudSapFecha,
                    x.SolicitudSapEstatus,
                    x.SolicitudSapSolicitante,
                    x.TipoCompra,
                    x.Titulo,
                    x.CentroCosto,
                    x.Planta,
                    x.Solicitante,
                    x.ProveedorSapCodigo,
                    x.ProveedorNombreSnapshot,
                    x.Moneda,
                    x.Estatus,
                    x.TotalCotizado,
                    x.TotalFactura,
                    x.DiferenciaFacturaCotizacion,
                    x.Autorizada,
                    x.RecibidaConforme,
                    x.ConciliacionOk,
                    x.LiberadaPago,
                    x.FechaCreacion,
                    x.FechaModificacion,
                    Cotizaciones = _context.CompraTiCotizaciones.Count(c => c.SolicitudId == x.Id),
                    Facturas = _context.CompraTiFacturas.Count(f => f.SolicitudId == x.Id),
                    Recepciones = _context.CompraTiRecepciones.Count(r => r.SolicitudId == x.Id),
                    OrdenesCompraSap = _context.CompraTiOrdenesCompraSap.Count(o =>
                        o.SolicitudId == x.Id &&
                        o.Activa),
                    OrdenCompraSapDocNum = _context.CompraTiOrdenesCompraSap
                        .Where(o =>
                            o.SolicitudId == x.Id &&
                            o.Activa)
                        .OrderByDescending(o => o.DocEntry)
                        .Select(o => (int?)o.DocNum)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            return Json(new
            {
                ok = true,
                total,
                page,
                pageSize,
                solicitudes
            });
        }

        [HttpGet]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public async Task<IActionResult> GetDetalleCompraTi(
            int id,
            CancellationToken ct = default)
        {
            var solicitud = await _context.CompraTiSolicitudes
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.Folio,
                    x.SolicitudSap,
                    x.SolicitudSapDocEntry,
                    x.SolicitudSapFecha,
                    x.SolicitudSapEstatus,
                    x.SolicitudSapSolicitante,
                    x.TipoCompra,
                    x.Titulo,
                    x.Justificacion,
                    x.CentroCosto,
                    x.Planta,
                    x.Solicitante,
                    x.ProveedorSapCodigo,
                    x.ProveedorNombreSnapshot,
                    x.ProveedorRfcSnapshot,
                    x.Moneda,
                    x.Estatus,
                    x.SubtotalCotizado,
                    x.IvaCotizado,
                    x.TotalCotizado,
                    x.SubtotalFactura,
                    x.IvaFactura,
                    x.TotalFactura,
                    x.DiferenciaFacturaCotizacion,
                    x.Autorizada,
                    x.RecibidaConforme,
                    x.ConciliacionOk,
                    x.LiberadaPago,
                    x.FechaLiberacionPago,
                    x.LiberadoPagoPor,
                    x.FechaCreacion,
                    x.FechaModificacion,
                    x.CreadoPor,
                    x.ModificadoPor
                })
                .FirstOrDefaultAsync(ct);

            if (solicitud == null)
                return NotFound(new { ok = false, mensaje = "Expediente no encontrado." });

            var detalles = await _context.CompraTiDetalles
                .AsNoTracking()
                .Where(x => x.SolicitudId == id && x.Activo)
                .OrderBy(x => x.Id)
                .ToListAsync(ct);

            var cotizaciones = await _context.CompraTiCotizaciones
                .AsNoTracking()
                .Where(x => x.SolicitudId == id)
                .OrderByDescending(x => x.FechaRegistro)
                .ToListAsync(ct);

            var recepciones = await _context.CompraTiRecepciones
                .AsNoTracking()
                .Where(x => x.SolicitudId == id)
                .OrderByDescending(x => x.FechaRecepcion)
                .ToListAsync(ct);

            var facturas = await _context.CompraTiFacturas
                .AsNoTracking()
                .Where(x => x.SolicitudId == id)
                .OrderByDescending(x => x.FechaRegistro)
                .ToListAsync(ct);

            var ordenesCompraSap = await _context.CompraTiOrdenesCompraSap
                .AsNoTracking()
                .Where(x =>
                    x.SolicitudId == id &&
                    x.Activa)
                .OrderByDescending(x => x.DocEntry)
                .ToListAsync(ct);

            var autorizaciones = await _context.CompraTiAutorizaciones
                .AsNoTracking()
                .Where(x => x.SolicitudId == id)
                .OrderByDescending(x => x.Fecha)
                .ToListAsync(ct);

            var bitacora = await _context.CompraTiBitacoras
                .AsNoTracking()
                .Where(x => x.SolicitudId == id)
                .OrderByDescending(x => x.Fecha)
                .Take(300)
                .ToListAsync(ct);

            var bloqueosPago = ObtenerBloqueosPago(
                solicitud.Autorizada,
                solicitud.RecibidaConforme,
                solicitud.ConciliacionOk,
                solicitud.TotalFactura,
                solicitud.LiberadaPago);

            var tieneOrdenCompraSap = ordenesCompraSap.Any(x => !x.Cancelada);

            var seguimiento = new[]
            {
                new
                {
                    orden = 1,
                    etapa = "SOLICITUD SAP",
                    completada = solicitud.SolicitudSapDocEntry.HasValue,
                    detalle = solicitud.SolicitudSapDocEntry.HasValue
                        ? $"Solicitud {solicitud.SolicitudSap} ligada."
                        : "Falta ligar la solicitud SAP."
                },
                new
                {
                    orden = 2,
                    etapa = "COTIZACIÓN",
                    completada = cotizaciones.Count > 0,
                    detalle = cotizaciones.Count > 0
                        ? $"{cotizaciones.Count} cotización(es) registrada(s)."
                        : "Falta registrar la cotización."
                },
                new
                {
                    orden = 3,
                    etapa = "AUTORIZACIÓN",
                    completada = solicitud.Autorizada,
                    detalle = solicitud.Autorizada
                        ? "Compra autorizada."
                        : "Falta autorización."
                },
                new
                {
                    orden = 4,
                    etapa = "ORDEN DE COMPRA SAP",
                    completada = tieneOrdenCompraSap,
                    detalle = tieneOrdenCompraSap
                        ? $"{ordenesCompraSap.Count(x => !x.Cancelada)} orden(es) de compra relacionada(s)."
                        : "SAP todavía no reporta una orden de compra relacionada."
                },
                new
                {
                    orden = 5,
                    etapa = "RECEPCIÓN",
                    completada = solicitud.RecibidaConforme,
                    detalle = solicitud.RecibidaConforme
                        ? "Recepción conforme."
                        : "Falta recepción conforme."
                },
                new
                {
                    orden = 6,
                    etapa = "FACTURA Y CONCILIACIÓN",
                    completada = solicitud.ConciliacionOk,
                    detalle = solicitud.ConciliacionOk
                        ? "Factura conciliada."
                        : facturas.Count > 0
                            ? "La factura tiene diferencias o validaciones pendientes."
                            : "Falta registrar la factura."
                },
                new
                {
                    orden = 7,
                    etapa = "PAGO",
                    completada = solicitud.LiberadaPago,
                    detalle = solicitud.LiberadaPago
                        ? "Expediente liberado a pago."
                        : "Pendiente de liberación a pago."
                }
            };

            var pendientesSeguimiento = seguimiento
                .Where(x => !x.completada)
                .OrderBy(x => x.orden)
                .Select(x => x.detalle)
                .ToList();

            return Json(new
            {
                ok = true,
                solicitud,
                detalles,
                cotizaciones,
                recepciones,
                facturas,
                ordenesCompraSap,
                autorizaciones,
                bitacora,
                seguimiento,
                pendientesSeguimiento,
                bloqueosPago,
                puedeLiberarPago = bloqueosPago.Count == 0
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RevisarPermiso(MODULO_COMPRAS_TI, "ESCRIBIR")]
        public async Task<IActionResult> SincronizarProveedoresComprasTi(
            CancellationToken ct = default)
        {
            try
            {
                var resultado = await _sap.SincronizarProveedoresAsync(ct);

                return Json(new
                {
                    ok = true,
                    mensaje = "Catálogo de proveedores sincronizado correctamente.",
                    totalSap = resultado.totalSap,
                    insertados = resultado.insertados,
                    actualizados = resultado.actualizados,
                    fueraDeSap = resultado.fueraDeSap,
                    fecha = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sincronizando proveedores desde Compras TI.");
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = ex.GetBaseException().Message
                });
            }
        }


        // =========================================================================================
        // CATÁLOGOS Y SOLICITUDES DE COMPRA DESDE SAP BUSINESS ONE
        // =========================================================================================

        [HttpGet]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public async Task<IActionResult> BuscarSolicitudesCompraSapTi(
            string term = "",
            int top = 80,
            bool incluirCerradas = false,
            CancellationToken ct = default)
        {
            term = (term ?? "").Trim();
            top = Math.Clamp(top, 10, 150);

            try
            {
                string endpoint;

                if (int.TryParse(term, out var numeroSap) && numeroSap > 0)
                {
                    var filter = $"DocNum eq {numeroSap} or DocEntry eq {numeroSap}";
                    endpoint =
                        "PurchaseRequests" +
                        $"?$filter={Uri.EscapeDataString(filter)}" +
                        "&$orderby=DocEntry desc" +
                        "&$top=20";
                }
                else
                {
                    endpoint =
                        "PurchaseRequests" +
                        "?$orderby=DocEntry desc" +
                        $"&$top={top}";
                }

                var sap = await _sap.GetAsync(endpoint);

                if (!sap.ok || string.IsNullOrWhiteSpace(sap.response))
                {
                    return StatusCode(
                        sap.statusCode > 0 ? sap.statusCode : 502,
                        new
                        {
                            ok = false,
                            mensaje = "No se pudieron consultar las solicitudes de compra en SAP.",
                            error = sap.error,
                            detalle = sap.response
                        });
                }

                using var doc = JsonDocument.Parse(sap.response);
                var root = doc.RootElement;

                var solicitudes = new List<SapCompraSolicitudVm>();

                if (root.TryGetProperty("value", out var value) &&
                    value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in value.EnumerateArray())
                    {
                        var item = MapearSolicitudCompraSapTi(row);

                        if (!incluirCerradas &&
                            (item.Cancelada ||
                             string.Equals(item.Estado, "CERRADA", StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(term) &&
                            !int.TryParse(term, out _))
                        {
                            var searchable = string.Join(
                                " ",
                                item.DocEntry,
                                item.DocNum,
                                item.Solicitante,
                                item.Comentarios,
                                item.PrimeraDescripcion,
                                item.CentroCosto,
                                item.ProveedorPreferido,
                                string.Join(" ", item.Detalles.Select(x =>
                                    $"{x.ItemCode} {x.Descripcion} {x.CentroCostoSap} {x.ProveedorPreferidoSap}")));

                            if (!searchable.Contains(
                                    term,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }

                        solicitudes.Add(item);
                    }
                }

                var result = solicitudes
                    .OrderByDescending(x => x.DocEntry)
                    .Take(top)
                    .Select(x => new
                    {
                        x.DocEntry,
                        x.DocNum,
                        x.FechaDocumento,
                        x.FechaRequerida,
                        x.Estado,
                        x.Cancelada,
                        x.Solicitante,
                        x.Comentarios,
                        x.PrimeraDescripcion,
                        x.Lineas,
                        x.CentroCosto,
                        x.ProveedorPreferido,
                        x.PlantaSugerida,
                        x.TipoCompraSugerido
                    })
                    .ToList();

                return Json(new
                {
                    ok = true,
                    total = result.Count,
                    solicitudes = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error consultando solicitudes de compra SAP para Compras TI. Term={Term}",
                    term);

                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "No se pudieron consultar las solicitudes de compra en SAP.",
                    error = ex.GetBaseException().Message
                });
            }
        }

        [HttpGet]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public async Task<IActionResult> ObtenerSolicitudCompraSapTi(
            int docEntry,
            CancellationToken ct = default)
        {
            if (docEntry <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El DocEntry de la solicitud SAP no es válido."
                });
            }

            try
            {
                var raw = await ObtenerSolicitudCompraSapRawAsync(docEntry, ct);
                var solicitud = MapearSolicitudCompraSapTi(raw);

                ProveedorSap? proveedorPreferido = null;

                if (!string.IsNullOrWhiteSpace(solicitud.ProveedorPreferido))
                {
                    proveedorPreferido = await _context.ProveedorSap
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.Proveedor == solicitud.ProveedorPreferido &&
                            x.Activo &&
                            x.ExisteEnSap &&
                            !x.Congelado &&
                            x.GrupoNombre != null &&
                            EF.Functions.Like(x.GrupoNombre, "%PROVEEDOR%") &&
                            EF.Functions.Like(x.GrupoNombre, "%CONSUMIBLE%"),
                            ct);
                }

                var ordenesCompra = await ObtenerOrdenesCompraRelacionadasSapAsync(
                    docEntry,
                    ct);

                return Json(new
                {
                    ok = true,
                    solicitud = new
                    {
                        docEntry = solicitud.DocEntry,
                        docNum = solicitud.DocNum,
                        fechaDocumento = solicitud.FechaDocumento,
                        fechaRequerida = solicitud.FechaRequerida,
                        estado = solicitud.Estado,
                        cancelada = solicitud.Cancelada,
                        solicitante = solicitud.Solicitante,
                        comentarios = solicitud.Comentarios,
                        primeraDescripcion = solicitud.PrimeraDescripcion,
                        lineas = solicitud.Lineas,
                        centroCosto = solicitud.CentroCosto,

                        // Nombres diferentes para evitar colisión con System.Text.Json.
                        proveedorPreferidoCodigoSap = solicitud.ProveedorPreferido,
                        proveedorPreferidoDisponible = proveedorPreferido != null,
                        proveedorPreferidoDetalle = proveedorPreferido == null
                            ? null
                            : new
                            {
                                codigo = proveedorPreferido.Proveedor,
                                nombre = proveedorPreferido.NombreProveedor,
                                rfc = proveedorPreferido.RFC,
                                moneda = proveedorPreferido.Moneda,
                                grupo = proveedorPreferido.GrupoNombre,
                                condicionPago = proveedorPreferido.CondicionPagoNombre
                            },

                        plantaSugerida = solicitud.PlantaSugerida,
                        tipoCompraSugerido = solicitud.TipoCompraSugerido
                    },
                    detalles = solicitud.Detalles,
                    ordenesCompra
                });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(502, new
                {
                    ok = false,
                    mensaje = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error consultando solicitud de compra SAP. DocEntry={DocEntry}",
                    docEntry);

                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "No se pudo consultar el detalle de la solicitud SAP.",
                    error = ex.GetBaseException().Message
                });
            }
        }


        [HttpGet]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public async Task<IActionResult> ObtenerOrdenesCompraRelacionadasSapTi(
            int solicitudSapDocEntry,
            CancellationToken ct = default)
        {
            if (solicitudSapDocEntry <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El DocEntry de la solicitud SAP no es válido."
                });
            }

            try
            {
                var ordenes = await ObtenerOrdenesCompraRelacionadasSapAsync(
                    solicitudSapDocEntry,
                    ct);

                return Json(new
                {
                    ok = true,
                    solicitudSapDocEntry,
                    total = ordenes.Count,
                    ordenes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error consultando órdenes de compra relacionadas. Solicitud DocEntry={DocEntry}",
                    solicitudSapDocEntry);

                return StatusCode(502, new
                {
                    ok = false,
                    mensaje = "No se pudieron consultar las órdenes de compra relacionadas en SAP.",
                    error = ex.GetBaseException().Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RevisarPermiso(MODULO_COMPRAS_TI, "ESCRIBIR")]
        public async Task<IActionResult> SincronizarOrdenesCompraSapTi(
            int solicitudId,
            CancellationToken ct = default)
        {
            if (solicitudId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El expediente no es válido."
                });
            }

            var solicitud = await _context.CompraTiSolicitudes
                .FirstOrDefaultAsync(x => x.Id == solicitudId, ct);

            if (solicitud == null)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje = "Expediente no encontrado."
                });
            }

            if (!solicitud.SolicitudSapDocEntry.HasValue ||
                solicitud.SolicitudSapDocEntry.Value <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El expediente no tiene un DocEntry de solicitud SAP."
                });
            }

            try
            {
                var resultado = await SincronizarOrdenesCompraSapAsync(
                    solicitud,
                    ct);

                return Json(new
                {
                    ok = true,
                    mensaje = resultado.total > 0
                        ? $"Se sincronizaron {resultado.total} orden(es) de compra SAP."
                        : "SAP todavía no reporta órdenes de compra relacionadas.",
                    resultado.total,
                    resultado.insertadas,
                    resultado.actualizadas,
                    resultado.desactivadas,
                    ordenes = resultado.ordenes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error sincronizando órdenes SAP del expediente {SolicitudId}",
                    solicitudId);

                return StatusCode(502, new
                {
                    ok = false,
                    mensaje = "No se pudieron sincronizar las órdenes de compra SAP.",
                    error = ex.GetBaseException().Message
                });
            }
        }

        [HttpGet]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public async Task<IActionResult> ObtenerCentrosCostoSapTi(
            string term = "",
            int dimension = 0,
            CancellationToken ct = default)
        {
            term = (term ?? "").Trim();

            try
            {
                var endpoint =
                    "ProfitCenters" +
                    "?$orderby=CenterCode" +
                    "&$top=500";

                var sap = await _sap.GetAsync(endpoint);

                if (!sap.ok || string.IsNullOrWhiteSpace(sap.response))
                {
                    return StatusCode(
                        sap.statusCode > 0 ? sap.statusCode : 502,
                        new
                        {
                            ok = false,
                            mensaje = "No se pudieron consultar los centros de costo en SAP.",
                            error = sap.error,
                            detalle = sap.response
                        });
                }

                using var doc = JsonDocument.Parse(sap.response);
                var root = doc.RootElement;
                var centros = new List<SapCentroCostoVm>();

                if (root.TryGetProperty("value", out var value) &&
                    value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in value.EnumerateArray())
                    {
                        var codigo = SapStringCompraTi(row, "CenterCode");
                        var nombre = SapStringCompraTi(row, "CenterName");
                        var grupo = SapStringCompraTi(row, "GroupCode");
                        var dim = SapIntCompraTi(row, "InWhichDimension", "Dimension") ?? 0;
                        var activa = SapActivoCompraTi(row, "Active");
                        var desde = SapDateCompraTi(row, "EffectiveFrom");
                        var hasta = SapDateCompraTi(row, "EffectiveTo");

                        if (string.IsNullOrWhiteSpace(codigo))
                            continue;

                        if (!activa)
                            continue;

                        var hoy = DateTime.Today;

                        if (desde.HasValue && desde.Value.Date > hoy)
                            continue;

                        if (hasta.HasValue &&
                            hasta.Value.Year > 1900 &&
                            hasta.Value.Date < hoy)
                        {
                            continue;
                        }

                        if (dimension > 0 && dim > 0 && dim != dimension)
                            continue;

                        if (!string.IsNullOrWhiteSpace(term) &&
                            !codigo.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                            !nombre.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                            !grupo.Contains(term, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        centros.Add(new SapCentroCostoVm
                        {
                            Codigo = codigo,
                            Nombre = nombre,
                            Grupo = grupo,
                            Dimension = dim,
                            VigenciaDesde = desde,
                            VigenciaHasta = hasta
                        });
                    }
                }

                var result = centros
                    .OrderBy(x => x.Dimension)
                    .ThenBy(x => x.Codigo)
                    .Take(300)
                    .ToList();

                return Json(new
                {
                    ok = true,
                    total = result.Count,
                    centros = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error consultando centros de costo SAP para Compras TI.");

                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "No se pudieron consultar los centros de costo en SAP.",
                    error = ex.GetBaseException().Message
                });
            }
        }

        [HttpGet]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public async Task<IActionResult> ObtenerProveedoresComprasTi(
     string term = "",
     CancellationToken ct = default)
        {
            term = (term ?? string.Empty).Trim();

            var query = _context.ProveedorSap
                .AsNoTracking()
                .Where(x =>
                    x.Activo &&
                    x.ExisteEnSap &&
                    !x.Congelado &&
                    x.GrupoNombre != null &&
                    (
                        EF.Functions.Like(x.GrupoNombre, "%PROVEEDOR%") ||
                        EF.Functions.Like(x.GrupoNombre, "%CONSUMIBLE%")
                    ));

            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(x =>
                    EF.Functions.Like(x.Proveedor ?? "", $"%{term}%") ||
                    EF.Functions.Like(x.NombreProveedor ?? "", $"%{term}%") ||
                    EF.Functions.Like(x.RFC ?? "", $"%{term}%"));
            }

            var proveedores = await query
                .OrderBy(x => x.NombreProveedor)
                .Take(50)
                .Select(x => new
                {
                    id = x.Proveedor,

                    text =
                        (x.NombreProveedor ?? "SIN NOMBRE") +
                        " (" + x.Proveedor + ")",

                    codigo = x.Proveedor,
                    nombre = x.NombreProveedor,
                    rfc = x.RFC,
                    moneda = x.Moneda,
                    grupo = x.GrupoNombre,
                    condicionPago = x.CondicionPagoNombre,
                    correo = x.Correo
                })
                .ToListAsync(ct);

            return Json(proveedores);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RevisarPermiso(MODULO_COMPRAS_TI, "ESCRIBIR")]
        public async Task<IActionResult> AsignarProveedorCompraTi(
            int solicitudId,
            string proveedorSapCodigo,
            CancellationToken ct = default)
        {
            proveedorSapCodigo = (proveedorSapCodigo ?? "")
                .Trim()
                .ToUpperInvariant();

            if (solicitudId <= 0 || string.IsNullOrWhiteSpace(proveedorSapCodigo))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La solicitud y el proveedor son obligatorios."
                });
            }

            var solicitud = await _context.CompraTiSolicitudes
                .FirstOrDefaultAsync(x => x.Id == solicitudId, ct);

            if (solicitud == null)
                return NotFound(new { ok = false, mensaje = "Expediente no encontrado." });

            if (ExpedienteCerradoComprasTi(solicitud))
                return Conflict(new { ok = false, mensaje = "El expediente ya está cerrado." });

            if (solicitud.Autorizada)
            {
                return Conflict(new
                {
                    ok = false,
                    mensaje = "No se puede cambiar el proveedor después de autorizar la compra."
                });
            }

            var proveedor = await _context.ProveedorSap
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Proveedor == proveedorSapCodigo &&
                    x.Activo &&
                    x.ExisteEnSap &&
                    !x.Congelado &&
                    x.GrupoNombre != null &&
                    EF.Functions.Like(x.GrupoNombre, "%PROVEEDOR%") &&
                    EF.Functions.Like(x.GrupoNombre, "%CONSUMIBLE%"),
                    ct);

            if (proveedor == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El proveedor no está disponible o su grupo no contiene PROVEEDOR y CONSUMIBLE."
                });
            }

            solicitud.ProveedorSapCodigo = proveedor.Proveedor;
            solicitud.ProveedorNombreSnapshot = proveedor.NombreProveedor ?? "";
            solicitud.ProveedorRfcSnapshot = proveedor.RFC ?? "";
            solicitud.Moneda = string.IsNullOrWhiteSpace(proveedor.Moneda)
                ? "MXN"
                : proveedor.Moneda.Trim().ToUpperInvariant();
            solicitud.FechaModificacion = DateTime.Now;
            solicitud.ModificadoPor = UsuarioActualComprasTi();

            RegistrarBitacoraComprasTi(
                solicitud.Id,
                "PROVEEDOR_ASIGNADO",
                $"Proveedor {proveedor.Proveedor} - {proveedor.NombreProveedor} asignado al expediente.");

            await _context.SaveChangesAsync(ct);

            return Json(new
            {
                ok = true,
                codigo = solicitud.ProveedorSapCodigo,
                nombre = solicitud.ProveedorNombreSnapshot,
                rfc = solicitud.ProveedorRfcSnapshot,
                moneda = solicitud.Moneda
            });
        }

        [HttpGet]
        [RevisarPermiso(MODULO_COMPRAS_TI, "LEER")]
        public async Task<IActionResult> ObtenerArticulosSapComprasTi(
            string term = "",
            CancellationToken ct = default)
        {
            term = (term ?? string.Empty).Trim();

            var query = _context.ArticuloSap.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(x =>
                    (x.ProductoCodigo ?? "").Contains(term) ||
                    (x.ProductoNombre ?? "").Contains(term));
            }

            var articulos = await query
                .OrderBy(x => x.ProductoCodigo)
                .Take(50)
                .Select(x => new
                {
                    id = x.ProductoCodigo,
                    text = x.ProductoCodigo + " - " + x.ProductoNombre,
                    codigo = x.ProductoCodigo,
                    nombre = x.ProductoNombre
                })
                .ToListAsync(ct);

            return Json(articulos);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RevisarPermiso(MODULO_COMPRAS_TI, "ESCRIBIR")]
        public async Task<IActionResult> CrearSolicitudCompraTi(
            [FromBody] CrearCompraTiDto dto,
            CancellationToken ct = default)
        {
            if (dto == null)
                return BadRequest(new { ok = false, mensaje = "No se recibió información." });

            dto.SolicitudSap = (dto.SolicitudSap ?? "").Trim().ToUpperInvariant();
            dto.TipoCompra = (dto.TipoCompra ?? "").Trim().ToUpperInvariant();
            dto.ProveedorSapCodigo = (dto.ProveedorSapCodigo ?? "").Trim().ToUpperInvariant();
            dto.CentroCosto = (dto.CentroCosto ?? "").Trim().ToUpperInvariant();
            dto.Planta = (dto.Planta ?? "").Trim().ToUpperInvariant();

            if (dto.SolicitudSapDocEntry <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "Selecciona una solicitud directamente desde SAP."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.SolicitudSap))
                return BadRequest(new { ok = false, mensaje = "La solicitud SAP es obligatoria." });

            if (!new[] { "ACTIVO_FIJO", "SERVICIO", "CONSUMIBLE" }.Contains(dto.TipoCompra))
                return BadRequest(new { ok = false, mensaje = "Tipo de compra no válido." });

            if (!new[] { "PLANTA 1", "TIF 776" }.Contains(dto.Planta))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La planta debe ser PLANTA 1 o TIF 776."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.CentroCosto))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "Selecciona un centro de costo del catálogo SAP."
                });
            }

            if (dto.Detalles == null || dto.Detalles.Count == 0)
                return BadRequest(new { ok = false, mensaje = "Debe existir al menos un concepto." });

            if (dto.Detalles.Any(x => x.Cantidad <= 0 || string.IsNullOrWhiteSpace(x.Descripcion)))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "Cada concepto debe tener descripción y cantidad mayor a cero."
                });
            }

            JsonElement solicitudSapRaw;
            SapCompraSolicitudVm solicitudSap;

            try
            {
                solicitudSapRaw = await ObtenerSolicitudCompraSapRawAsync(
                    dto.SolicitudSapDocEntry,
                    ct);

                solicitudSap = MapearSolicitudCompraSapTi(solicitudSapRaw);
            }
            catch (Exception ex)
            {
                return StatusCode(502, new
                {
                    ok = false,
                    mensaje =
                        "No fue posible confirmar la solicitud directamente en SAP. " +
                        ex.GetBaseException().Message
                });
            }

            if (!string.Equals(
                    dto.SolicitudSap,
                    solicitudSap.DocNum.ToString(CultureInfo.InvariantCulture),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new
                {
                    ok = false,
                    mensaje =
                        $"La solicitud seleccionada cambió. SAP reporta el número " +
                        $"{solicitudSap.DocNum} para el DocEntry {solicitudSap.DocEntry}."
                });
            }

            // Las solicitudes cerradas sí pueden ligarse para conservar la trazabilidad.
            // Únicamente se bloquean las solicitudes canceladas.
            if (solicitudSap.Cancelada)
            {
                return Conflict(new
                {
                    ok = false,
                    mensaje =
                        $"La solicitud SAP {solicitudSap.DocNum} está cancelada " +
                        "y no puede ligarse a un expediente."
                });
            }

            bool solicitudSapCerrada = string.Equals(
                solicitudSap.Estado,
                "CERRADA",
                StringComparison.OrdinalIgnoreCase);

            var duplicada = await _context.CompraTiSolicitudes
                .AnyAsync(x =>
                    x.SolicitudSap == dto.SolicitudSap ||
                    x.SolicitudSapDocEntry == dto.SolicitudSapDocEntry,
                    ct);

            if (duplicada)
            {
                return Conflict(new
                {
                    ok = false,
                    mensaje = "La solicitud SAP ya tiene un expediente."
                });
            }

            ProveedorSap? proveedor = null;

            if (!string.IsNullOrWhiteSpace(dto.ProveedorSapCodigo))
            {
                proveedor = await _context.ProveedorSap
    .AsNoTracking()
    .FirstOrDefaultAsync(
        x => x.Proveedor == dto.ProveedorSapCodigo,
        ct);

                if (proveedor == null)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje =
                            $"El proveedor con código '{dto.ProveedorSapCodigo}' " +
                            "no existe en el catálogo local de proveedores SAP."
                    });
                }

                if (!proveedor.Activo)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje =
                            $"El proveedor '{proveedor.NombreProveedor}' está inactivo.",
                        codigo = proveedor.Proveedor,
                        grupo = proveedor.GrupoNombre
                    });
                }

                if (!proveedor.ExisteEnSap)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje =
                            $"El proveedor '{proveedor.NombreProveedor}' ya no existe en SAP.",
                        codigo = proveedor.Proveedor,
                        grupo = proveedor.GrupoNombre
                    });
                }

                if (proveedor.Congelado)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje =
                            $"El proveedor '{proveedor.NombreProveedor}' está congelado en SAP.",
                        codigo = proveedor.Proveedor,
                        grupo = proveedor.GrupoNombre
                    });
                }

                var grupoProveedor = (proveedor.GrupoNombre ?? "")
                    .Trim()
                    .ToUpperInvariant();

                var perteneceGrupoPermitido =
                    grupoProveedor.Contains("PROVEEDOR") ||
                    grupoProveedor.Contains("CONSUMIBLE");

                if (!perteneceGrupoPermitido)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje =
                            $"El proveedor '{proveedor.NombreProveedor}' pertenece al grupo " +
                            $"'{proveedor.GrupoNombre}', que no está autorizado para Compras TI.",
                        codigo = proveedor.Proveedor,
                        grupo = proveedor.GrupoNombre
                    });
                }
            }

            var ordenesRelacionadasSap = new List<SapOrdenCompraVm>();
            string advertenciaOrdenCompraSap = "";

            try
            {
                ordenesRelacionadasSap = await ObtenerOrdenesCompraRelacionadasSapAsync(
                    dto.SolicitudSapDocEntry,
                    ct);
            }
            catch (Exception ex)
            {
                advertenciaOrdenCompraSap =
                    "El expediente se guardará, pero no fue posible consultar las órdenes " +
                    "de compra relacionadas en SAP: " +
                    ex.GetBaseException().Message;

                _logger.LogWarning(
                    ex,
                    "No se pudieron consultar órdenes SAP al crear el expediente. Solicitud DocEntry={DocEntry}",
                    dto.SolicitudSapDocEntry);
            }

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            try
            {
                var solicitud = new CompraTiSolicitud
                {
                    Folio = "TMP-" + Guid.NewGuid().ToString("N").Substring(0, 26),
                    SolicitudSap = dto.SolicitudSap,
                    SolicitudSapDocEntry = dto.SolicitudSapDocEntry,
                    SolicitudSapFecha = solicitudSap.FechaDocumento,
                    SolicitudSapEstatus = solicitudSap.Estado,
                    SolicitudSapSolicitante = solicitudSap.Solicitante,
                    SolicitudSapSnapshotJson = solicitudSapRaw.GetRawText(),
                    TipoCompra = dto.TipoCompra,
                    Titulo = (dto.Titulo ?? "").Trim(),
                    Justificacion = (dto.Justificacion ?? "").Trim(),
                    CentroCosto = dto.CentroCosto,
                    Planta = dto.Planta,
                    Solicitante = UsuarioActualComprasTi(),
                    ProveedorSapCodigo = proveedor?.Proveedor,
                    ProveedorNombreSnapshot = proveedor?.NombreProveedor ?? "",
                    ProveedorRfcSnapshot = proveedor?.RFC ?? "",
                    Moneda = string.IsNullOrWhiteSpace(proveedor?.Moneda)
                        ? "MXN"
                        : proveedor.Moneda.Trim().ToUpperInvariant(),
                    Estatus = "BORRADOR",
                    FechaCreacion = DateTime.Now,
                    CreadoPor = UsuarioActualComprasTi()
                };

                _context.CompraTiSolicitudes.Add(solicitud);
                await _context.SaveChangesAsync(ct);

                solicitud.Folio = $"CTI-{solicitud.Id:D8}";

                foreach (var linea in dto.Detalles)
                {
                    _context.CompraTiDetalles.Add(new CompraTiDetalle
                    {
                        SolicitudId = solicitud.Id,
                        LineaSap = linea.LineaSap,
                        ArticuloSapCodigo = NormalizarNullableComprasTi(linea.ArticuloSapCodigo),
                        TipoLinea = NormalizarTipoLineaComprasTi(linea.TipoLinea, dto.TipoCompra),
                        Descripcion = (linea.Descripcion ?? "").Trim(),
                        CantidadSolicitada = linea.Cantidad,
                        Unidad = string.IsNullOrWhiteSpace(linea.Unidad)
                            ? "PZA"
                            : linea.Unidad.Trim().ToUpperInvariant(),
                        CentroCostoSap = NormalizarNullableComprasTi(linea.CentroCostoSap),
                        AlmacenSap = NormalizarNullableComprasTi(linea.AlmacenSap),
                        ProveedorPreferidoSap =
                            NormalizarNullableComprasTi(linea.ProveedorPreferidoSap),
                        Activo = true
                    });
                }

                RegistrarBitacoraComprasTi(
             solicitud.Id,
             "SOLICITUD_SAP_LIGADA",
             $"Solicitud SAP DocEntry {solicitud.SolicitudSapDocEntry}, " +
             $"DocNum {solicitud.SolicitudSap}, " +
             $"estado SAP {solicitud.SolicitudSapEstatus}, " +
             $"fecha {solicitud.SolicitudSapFecha:dd/MM/yyyy}, " +
             $"solicitante SAP {solicitud.SolicitudSapSolicitante}. " +
             $"Planta {solicitud.Planta}; centro de costo {solicitud.CentroCosto}. " +
             (solicitudSapCerrada
                 ? "La solicitud se ligó estando cerrada en SAP para conservar su trazabilidad."
                 : "La solicitud se ligó estando abierta en SAP."));

                RegistrarBitacoraComprasTi(
                    solicitud.Id,
                    "SOLICITUD_CREADA",
                    $"Expediente creado desde solicitud SAP {solicitud.SolicitudSap}.");

                foreach (var ordenSap in ordenesRelacionadasSap)
                {
                    var entidadOrden = new CompraTiOrdenCompraSap
                    {
                        SolicitudId = solicitud.Id
                    };

                    AplicarDatosOrdenCompraSap(
                        entidadOrden,
                        ordenSap,
                        UsuarioActualComprasTi());

                    _context.CompraTiOrdenesCompraSap.Add(entidadOrden);
                }

                if (ordenesRelacionadasSap.Count > 0)
                {
                    RegistrarBitacoraComprasTi(
                        solicitud.Id,
                        "ORDEN_COMPRA_SAP_DETECTADA",
                        $"Se detectaron {ordenesRelacionadasSap.Count} orden(es) de compra SAP " +
                        $"relacionadas con la solicitud {solicitud.SolicitudSap}.");
                }

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Json(new
                {
                    ok = true,
                    solicitudId = solicitud.Id,
                    folio = solicitud.Folio,
                    estatus = solicitud.Estatus,
                    solicitudSapDocEntry = solicitud.SolicitudSapDocEntry,
                    solicitudSap = solicitud.SolicitudSap,
                    solicitudSapEstatus = solicitud.SolicitudSapEstatus,
                    solicitudSapCerrada,
                    mensaje = solicitudSapCerrada
         ? "Expediente creado. La solicitud ya estaba cerrada en SAP y se ligó para trazabilidad."
         : "Expediente creado correctamente."
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);

                _logger.LogError(
                    ex,
                    "Error creando expediente de compra TI para {SolicitudSap}",
                    dto.SolicitudSap);

                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = ex.GetBaseException().Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(30_000_000)]
        [RevisarPermiso(MODULO_COMPRAS_TI, "ESCRIBIR")]
        public async Task<IActionResult> RegistrarCotizacionCompraTi(
            [FromForm] RegistrarCotizacionCompraTiDto dto,
            CancellationToken ct = default)
        {
            if (dto == null)
                return BadRequest(new { ok = false, mensaje = "No se recibió información." });

            var solicitud = await _context.CompraTiSolicitudes
                .FirstOrDefaultAsync(x => x.Id == dto.SolicitudId, ct);

            if (solicitud == null)
                return NotFound(new { ok = false, mensaje = "Expediente no encontrado." });

            if (ExpedienteCerradoComprasTi(solicitud))
                return Conflict(new { ok = false, mensaje = "El expediente ya está cerrado." });

            if (string.IsNullOrWhiteSpace(solicitud.ProveedorSapCodigo))
                return BadRequest(new { ok = false, mensaje = "Primero asigna un proveedor SAP al expediente." });

            if (dto.Archivo == null || dto.Archivo.Length == 0)
                return BadRequest(new { ok = false, mensaje = "La cotización es obligatoria." });

            if (dto.Subtotal < 0 || dto.Iva < 0 || dto.Total <= 0)
                return BadRequest(new { ok = false, mensaje = "Importes de cotización inválidos." });

            var subtotal = RedondearImporte(dto.Subtotal);
            var iva = RedondearImporte(dto.Iva);
            var total = RedondearImporte(dto.Total);
            var suma = RedondearImporte(subtotal + iva);

            if (Math.Abs(suma - total) > TOLERANCIA_CONCILIACION)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = $"Subtotal + IVA ({suma:N2}) no coincide con el total ({total:N2})."
                });
            }

            var hash = await CalcularSha256ComprasTiAsync(dto.Archivo, ct);
            var documentoDuplicado = await _context.CompraTiCotizaciones
                .AnyAsync(x => x.HashSha256 == hash, ct);

            if (documentoDuplicado)
            {
                return Conflict(new
                {
                    ok = false,
                    mensaje = "Esta cotización ya fue cargada anteriormente."
                });
            }

            var ruta = await GuardarArchivoCompraTiAsync(
                dto.Archivo,
                solicitud.Folio,
                "cotizaciones",
                ct);

            var cotizacionesAnteriores = await _context.CompraTiCotizaciones
                .Where(x => x.SolicitudId == solicitud.Id &&
                            x.Estatus != "RECHAZADA" &&
                            x.Estatus != "SUSTITUIDA")
                .ToListAsync(ct);

            foreach (var anterior in cotizacionesAnteriores)
                anterior.Estatus = "SUSTITUIDA";

            _context.CompraTiCotizaciones.Add(new CompraTiCotizacion
            {
                SolicitudId = solicitud.Id,
                ProveedorSapCodigo = solicitud.ProveedorSapCodigo,
                NumeroCotizacion = (dto.NumeroCotizacion ?? "").Trim(),
                FechaCotizacion = dto.FechaCotizacion ?? DateTime.Today,
                VigenciaHasta = dto.VigenciaHasta,
                Subtotal = subtotal,
                Iva = iva,
                Total = total,
                Moneda = string.IsNullOrWhiteSpace(dto.Moneda)
                    ? solicitud.Moneda
                    : dto.Moneda.Trim().ToUpperInvariant(),
                RutaArchivo = ruta,
                HashSha256 = hash,
                Estatus = "PENDIENTE_AUTORIZACION",
                FechaRegistro = DateTime.Now,
                RegistradoPor = UsuarioActualComprasTi()
            });

            solicitud.SubtotalCotizado = subtotal;
            solicitud.IvaCotizado = iva;
            solicitud.TotalCotizado = total;
            solicitud.Autorizada = false;
            solicitud.ConciliacionOk = false;
            solicitud.Estatus = "COTIZACION_REGISTRADA";
            solicitud.FechaModificacion = DateTime.Now;
            solicitud.ModificadoPor = UsuarioActualComprasTi();

            RegistrarBitacoraComprasTi(
                solicitud.Id,
                "COTIZACION_REGISTRADA",
                $"Cotización por {total:N2} {solicitud.Moneda} registrada.");

            await _context.SaveChangesAsync(ct);

            return Json(new
            {
                ok = true,
                solicitud.Id,
                solicitud.Folio,
                solicitud.Estatus,
                subtotal,
                iva,
                total
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RevisarPermiso(MODULO_COMPRAS_TI, "ESCRIBIR")]
        public async Task<IActionResult> AutorizarCompraTi(
            [FromBody] AutorizarCompraTiDto dto,
            CancellationToken ct = default)
        {
            if (dto == null)
                return BadRequest(new { ok = false, mensaje = "No se recibió información." });

            var solicitud = await _context.CompraTiSolicitudes
                .FirstOrDefaultAsync(x => x.Id == dto.SolicitudId, ct);

            if (solicitud == null)
                return NotFound(new { ok = false, mensaje = "Expediente no encontrado." });

            if (ExpedienteCerradoComprasTi(solicitud))
                return Conflict(new { ok = false, mensaje = "El expediente ya está cerrado." });

            if (solicitud.TotalCotizado <= 0)
                return BadRequest(new { ok = false, mensaje = "No existe una cotización válida." });

            var cotizacion = await _context.CompraTiCotizaciones
                .Where(x => x.SolicitudId == solicitud.Id && x.Estatus != "SUSTITUIDA")
                .OrderByDescending(x => x.FechaRegistro)
                .FirstOrDefaultAsync(ct);

            if (cotizacion == null)
                return BadRequest(new { ok = false, mensaje = "No se encontró la cotización vigente." });

            var decision = dto.Autorizar ? "AUTORIZADA" : "RECHAZADA";

            _context.CompraTiAutorizaciones.Add(new CompraTiAutorizacion
            {
                SolicitudId = solicitud.Id,
                Etapa = "COTIZACION",
                Decision = decision,
                Comentario = (dto.Comentario ?? "").Trim(),
                Usuario = UsuarioActualComprasTi(),
                Fecha = DateTime.Now
            });

            cotizacion.Estatus = decision;
            solicitud.Autorizada = dto.Autorizar;
            solicitud.Estatus = dto.Autorizar ? "AUTORIZADA" : "RECHAZADA";
            solicitud.FechaModificacion = DateTime.Now;
            solicitud.ModificadoPor = UsuarioActualComprasTi();

            RegistrarBitacoraComprasTi(
                solicitud.Id,
                "AUTORIZACION_" + decision,
                string.IsNullOrWhiteSpace(dto.Comentario)
                    ? decision
                    : dto.Comentario.Trim());

            await _context.SaveChangesAsync(ct);
            return Json(new { ok = true, solicitud.Estatus });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(30_000_000)]
        [RevisarPermiso(MODULO_COMPRAS_TI, "ESCRIBIR")]
        public async Task<IActionResult> RegistrarRecepcionCompraTi(
            [FromForm] RegistrarRecepcionCompraTiDto dto,
            CancellationToken ct = default)
        {
            if (dto == null)
                return BadRequest(new { ok = false, mensaje = "No se recibió información." });

            var solicitud = await _context.CompraTiSolicitudes
                .FirstOrDefaultAsync(x => x.Id == dto.SolicitudId, ct);

            if (solicitud == null)
                return NotFound(new { ok = false, mensaje = "Expediente no encontrado." });

            if (ExpedienteCerradoComprasTi(solicitud))
                return Conflict(new { ok = false, mensaje = "El expediente ya está cerrado." });

            if (!solicitud.Autorizada)
                return BadRequest(new { ok = false, mensaje = "La compra todavía no está autorizada." });

            if (dto.RecibidaConforme && dto.Evidencia == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La evidencia de recepción es obligatoria."
                });
            }

            var rutaEvidencia = dto.Evidencia == null
                ? ""
                : await GuardarArchivoCompraTiAsync(
                    dto.Evidencia,
                    solicitud.Folio,
                    "recepciones",
                    ct);

            var recepcion = new CompraTiRecepcion
            {
                SolicitudId = solicitud.Id,
                FechaRecepcion = dto.FechaRecepcion ?? DateTime.Now,
                RecibidaConforme = dto.RecibidaConforme,
                RecepcionParcial = dto.RecepcionParcial,
                Observaciones = (dto.Observaciones ?? "").Trim(),
                EvidenciaRuta = rutaEvidencia,
                RecibidoPor = UsuarioActualComprasTi()
            };

            _context.CompraTiRecepciones.Add(recepcion);

            solicitud.RecibidaConforme = dto.RecibidaConforme && !dto.RecepcionParcial;
            solicitud.Estatus = dto.RecibidaConforme
                ? (dto.RecepcionParcial ? "RECEPCION_PARCIAL" : "RECIBIDA_CONFORME")
                : "RECEPCION_NO_CONFORME";
            solicitud.FechaModificacion = DateTime.Now;
            solicitud.ModificadoPor = UsuarioActualComprasTi();

            RegistrarBitacoraComprasTi(
                solicitud.Id,
                solicitud.Estatus,
                string.IsNullOrWhiteSpace(dto.Observaciones)
                    ? "Recepción registrada."
                    : dto.Observaciones.Trim());

            await _context.SaveChangesAsync(ct);

            return Json(new
            {
                ok = true,
                recepcionId = recepcion.Id,
                solicitud.RecibidaConforme,
                solicitud.Estatus
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(40_000_000)]
        [RevisarPermiso(MODULO_COMPRAS_TI, "ESCRIBIR")]
        public async Task<IActionResult> RegistrarFacturaCompraTi(
            [FromForm] RegistrarFacturaCompraTiDto dto,
            CancellationToken ct = default)
        {
            if (dto == null)
                return BadRequest(new { ok = false, mensaje = "No se recibió información." });

            var solicitud = await _context.CompraTiSolicitudes
                .FirstOrDefaultAsync(x => x.Id == dto.SolicitudId, ct);

            if (solicitud == null)
                return NotFound(new { ok = false, mensaje = "Expediente no encontrado." });

            if (ExpedienteCerradoComprasTi(solicitud))
                return Conflict(new { ok = false, mensaje = "El expediente ya está cerrado." });

            if (!solicitud.Autorizada)
                return BadRequest(new { ok = false, mensaje = "La compra todavía no está autorizada." });

            if (dto.Pdf == null && dto.Xml == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "Adjunta al menos el PDF o el XML de la factura."
                });
            }

            CfdiCompraTiAnalisis? analisisXml = null;
            if (dto.Xml != null)
            {
                try
                {
                    analisisXml = await AnalizarCfdiCompraTiAsync(dto.Xml, ct);
                }
                catch (Exception ex)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "El XML no es un CFDI válido: " + ex.GetBaseException().Message
                    });
                }
            }

            var serie = analisisXml?.Serie ?? (dto.Serie ?? "").Trim();
            var folio = analisisXml?.Folio ?? (dto.Folio ?? "").Trim();
            var uuid = (analisisXml?.Uuid ?? dto.Uuid ?? "").Trim().ToUpperInvariant();
            var rfcEmisor = (analisisXml?.RfcEmisor ?? dto.RfcEmisor ?? "").Trim().ToUpperInvariant();
            var fechaFactura = analisisXml?.Fecha ?? dto.FechaFactura ?? DateTime.Today;
            var subtotalFactura = RedondearImporte(analisisXml?.Subtotal ?? dto.Subtotal);
            var ivaFactura = RedondearImporte(analisisXml?.Iva ?? dto.Iva);
            var totalFactura = RedondearImporte(analisisXml?.Total ?? dto.Total);

            if (subtotalFactura < 0 || ivaFactura < 0 || totalFactura <= 0)
                return BadRequest(new { ok = false, mensaje = "Importes de factura inválidos." });

            var sumaFactura = RedondearImporte(subtotalFactura + ivaFactura);
            var aritmeticaFacturaOk = Math.Abs(sumaFactura - totalFactura) <= TOLERANCIA_CONCILIACION;

            if (!string.IsNullOrWhiteSpace(uuid))
            {
                var uuidDuplicado = await _context.CompraTiFacturas
                    .AnyAsync(x => x.Uuid == uuid, ct);

                if (uuidDuplicado)
                {
                    return Conflict(new
                    {
                        ok = false,
                        mensaje = "El UUID de esta factura ya está registrado."
                    });
                }
            }

            var diferenciaSubtotal = RedondearImporte(subtotalFactura - solicitud.SubtotalCotizado);
            var diferenciaIva = RedondearImporte(ivaFactura - solicitud.IvaCotizado);
            var diferenciaTotal = RedondearImporte(totalFactura - solicitud.TotalCotizado);

            var rfcCoincide = string.IsNullOrWhiteSpace(solicitud.ProveedorRfcSnapshot) ||
                              string.IsNullOrWhiteSpace(rfcEmisor) ||
                              NormalizarRfc(solicitud.ProveedorRfcSnapshot) == NormalizarRfc(rfcEmisor);

            var conciliacionOk =
                aritmeticaFacturaOk &&
                rfcCoincide &&
                Math.Abs(diferenciaSubtotal) <= TOLERANCIA_CONCILIACION &&
                Math.Abs(diferenciaIva) <= TOLERANCIA_CONCILIACION &&
                Math.Abs(diferenciaTotal) <= TOLERANCIA_CONCILIACION;

            var rutaPdf = dto.Pdf == null
                ? ""
                : await GuardarArchivoCompraTiAsync(
                    dto.Pdf,
                    solicitud.Folio,
                    "facturas",
                    ct);

            var rutaXml = dto.Xml == null
                ? ""
                : await GuardarArchivoCompraTiAsync(
                    dto.Xml,
                    solicitud.Folio,
                    "facturas",
                    ct);

            _context.CompraTiFacturas.Add(new CompraTiFactura
            {
                SolicitudId = solicitud.Id,
                Serie = serie,
                Folio = folio,
                Uuid = uuid,
                RfcEmisor = rfcEmisor,
                FechaFactura = fechaFactura,
                Subtotal = subtotalFactura,
                Iva = ivaFactura,
                Total = totalFactura,
                RutaPdf = rutaPdf,
                RutaXml = rutaXml,
                DiferenciaContraCotizacion = diferenciaTotal,
                ConciliacionOk = conciliacionOk,
                FechaRegistro = DateTime.Now,
                RegistradoPor = UsuarioActualComprasTi()
            });

            solicitud.SubtotalFactura = subtotalFactura;
            solicitud.IvaFactura = ivaFactura;
            solicitud.TotalFactura = totalFactura;
            solicitud.DiferenciaFacturaCotizacion = diferenciaTotal;
            solicitud.ConciliacionOk = conciliacionOk;
            solicitud.Estatus = conciliacionOk
                ? "FACTURA_CONCILIADA"
                : "DIFERENCIA_FACTURA";
            solicitud.FechaModificacion = DateTime.Now;
            solicitud.ModificadoPor = UsuarioActualComprasTi();

            var detalleConciliacion =
                $"Factura: subtotal {subtotalFactura:N2}, IVA {ivaFactura:N2}, total {totalFactura:N2}. " +
                $"Cotización: subtotal {solicitud.SubtotalCotizado:N2}, IVA {solicitud.IvaCotizado:N2}, total {solicitud.TotalCotizado:N2}. " +
                $"Diferencias: subtotal {diferenciaSubtotal:N2}, IVA {diferenciaIva:N2}, total {diferenciaTotal:N2}. " +
                $"RFC coincide: {(rfcCoincide ? "SÍ" : "NO")}.";

            RegistrarBitacoraComprasTi(
                solicitud.Id,
                conciliacionOk ? "FACTURA_CONCILIADA" : "DIFERENCIA_FACTURA",
                detalleConciliacion);

            await _context.SaveChangesAsync(ct);

            return Json(new
            {
                ok = true,
                conciliacionOk,
                valoresTomadosDeXml = analisisXml != null,
                aritmeticaFacturaOk,
                rfcCoincide,
                diferenciaSubtotal,
                diferenciaIva,
                diferenciaTotal,
                bloqueadaParaPago = !conciliacionOk,
                solicitud.Estatus
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RevisarPermiso(MODULO_COMPRAS_TI, "ESCRIBIR")]
        public async Task<IActionResult> LiberarCompraTiAPago(
            [FromBody] LiberarCompraTiPagoDto dto,
            CancellationToken ct = default)
        {
            if (dto == null)
                return BadRequest(new { ok = false, mensaje = "No se recibió información." });

            var solicitud = await _context.CompraTiSolicitudes
                .FirstOrDefaultAsync(x => x.Id == dto.SolicitudId, ct);

            if (solicitud == null)
                return NotFound(new { ok = false, mensaje = "Expediente no encontrado." });

            var bloqueos = ObtenerBloqueosPago(
                solicitud.Autorizada,
                solicitud.RecibidaConforme,
                solicitud.ConciliacionOk,
                solicitud.TotalFactura,
                solicitud.LiberadaPago);

            if (bloqueos.Count > 0)
            {
                return Conflict(new
                {
                    ok = false,
                    mensaje = "Expediente bloqueado.",
                    bloqueos
                });
            }

            solicitud.LiberadaPago = true;
            solicitud.FechaLiberacionPago = DateTime.Now;
            solicitud.LiberadoPagoPor = UsuarioActualComprasTi();
            solicitud.Estatus = "LIBERADA_PAGO";
            solicitud.FechaModificacion = DateTime.Now;
            solicitud.ModificadoPor = UsuarioActualComprasTi();

            _context.CompraTiAutorizaciones.Add(new CompraTiAutorizacion
            {
                SolicitudId = solicitud.Id,
                Etapa = "PAGO",
                Decision = "LIBERADA",
                Comentario = (dto.Comentario ?? "").Trim(),
                Usuario = UsuarioActualComprasTi(),
                Fecha = DateTime.Now
            });

            RegistrarBitacoraComprasTi(
                solicitud.Id,
                "LIBERADA_PAGO",
                string.IsNullOrWhiteSpace(dto.Comentario)
                    ? "Expediente validado y liberado a pago."
                    : dto.Comentario.Trim());

            await _context.SaveChangesAsync(ct);

            return Json(new
            {
                ok = true,
                solicitud.Estatus,
                solicitud.FechaLiberacionPago,
                solicitud.LiberadoPagoPor
            });
        }



        private sealed class SapOrdenCompraVm
        {
            public int DocEntry { get; set; }
            public int DocNum { get; set; }
            public DateTime? FechaDocumento { get; set; }
            public DateTime? FechaEntrega { get; set; }
            public string Estado { get; set; } = "";
            public bool Cancelada { get; set; }
            public string ProveedorCodigo { get; set; } = "";
            public string ProveedorNombre { get; set; } = "";
            public string Moneda { get; set; } = "MXN";
            public decimal Total { get; set; }
            public string Comentarios { get; set; } = "";
            public List<SapOrdenCompraLineaVm> Detalles { get; set; } = new();
        }

        private sealed class SapOrdenCompraLineaVm
        {
            public int LineNum { get; set; }
            public int BaseEntry { get; set; }
            public int BaseLine { get; set; }
            public int BaseType { get; set; }
            public string ItemCode { get; set; } = "";
            public string Descripcion { get; set; } = "";
            public decimal Cantidad { get; set; }
            public decimal ImporteLinea { get; set; }
            public string Almacen { get; set; } = "";
        }

        private async Task<List<SapOrdenCompraVm>> ObtenerOrdenesCompraRelacionadasSapAsync(
            int solicitudDocEntry,
            CancellationToken ct)
        {
            if (solicitudDocEntry <= 0)
                return new List<SapOrdenCompraVm>();

            var queryPath =
                "$crossjoin(PurchaseOrders,PurchaseOrders/DocumentLines)";

            var queryOption =
                "$expand=" +
                "PurchaseOrders(" +
                    "$select=DocEntry,DocNum,DocDate,DocDueDate,DocumentStatus,Cancelled," +
                    "CardCode,CardName,DocCurrency,DocTotal,Comments" +
                ")," +
                "PurchaseOrders/DocumentLines(" +
                    "$select=DocEntry,LineNum,BaseType,BaseEntry,BaseLine,ItemCode," +
                    "ItemDescription,Quantity,LineTotal,WarehouseCode" +
                ")" +
                "&$filter=" +
                "PurchaseOrders/DocEntry eq PurchaseOrders/DocumentLines/DocEntry " +
                $"and PurchaseOrders/DocumentLines/BaseType eq {SAP_OBJETO_SOLICITUD_COMPRA} " +
                $"and PurchaseOrders/DocumentLines/BaseEntry eq {solicitudDocEntry}";

            var payload = JsonSerializer.Serialize(new
            {
                QueryPath = queryPath,
                QueryOption = queryOption
            });

            var sap = await _sap.PostJsonAsync(
                "QueryService_PostQuery",
                payload);

            if (!sap.ok || string.IsNullOrWhiteSpace(sap.response))
            {
                throw new InvalidOperationException(
                    "SAP QueryService no pudo consultar las órdenes relacionadas. " +
                    $"{sap.error}. {sap.response}");
            }

            using var document = JsonDocument.Parse(sap.response);

            if (!document.RootElement.TryGetProperty("value", out var rows) ||
                rows.ValueKind != JsonValueKind.Array)
            {
                return new List<SapOrdenCompraVm>();
            }

            var agrupadas = new Dictionary<int, SapOrdenCompraVm>();

            foreach (var row in rows.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                if (!row.TryGetProperty("PurchaseOrders", out var header) ||
                    header.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!row.TryGetProperty(
                        "PurchaseOrders/DocumentLines",
                        out var line) ||
                    line.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var baseType = SapIntCompraTi(line, "BaseType") ?? 0;
                var baseEntry = SapIntCompraTi(line, "BaseEntry") ?? 0;

                if (baseType != SAP_OBJETO_SOLICITUD_COMPRA ||
                    baseEntry != solicitudDocEntry)
                {
                    continue;
                }

                var docEntry = SapIntCompraTi(header, "DocEntry") ?? 0;
                if (docEntry <= 0)
                    continue;

                if (!agrupadas.TryGetValue(docEntry, out var orden))
                {
                    var cancelada = SapSiCompraTi(
                        header,
                        "Cancelled",
                        "Canceled");

                    var docStatus = PrimerNoVacioCompraTi(
                        SapStringCompraTi(header, "DocumentStatus"),
                        SapStringCompraTi(header, "DocStatus"));

                    orden = new SapOrdenCompraVm
                    {
                        DocEntry = docEntry,
                        DocNum = SapIntCompraTi(header, "DocNum") ?? 0,
                        FechaDocumento = SapDateCompraTi(
                            header,
                            "DocDate",
                            "TaxDate"),
                        FechaEntrega = SapDateCompraTi(
                            header,
                            "DocDueDate"),
                        Cancelada = cancelada,
                        Estado = cancelada
                            ? "CANCELADA"
                            : docStatus.Contains(
                                "Close",
                                StringComparison.OrdinalIgnoreCase)
                                ? "CERRADA"
                                : "ABIERTA",
                        ProveedorCodigo = SapStringCompraTi(
                            header,
                            "CardCode"),
                        ProveedorNombre = SapStringCompraTi(
                            header,
                            "CardName"),
                        Moneda = PrimerNoVacioCompraTi(
                            SapStringCompraTi(header, "DocCurrency"),
                            "MXN"),
                        Total = SapDecimalCompraTi(
                            header,
                            "DocTotal"),
                        Comentarios = SapStringCompraTi(
                            header,
                            "Comments")
                    };

                    agrupadas[docEntry] = orden;
                }

                var lineNum = SapIntCompraTi(line, "LineNum") ?? 0;
                var baseLine = SapIntCompraTi(line, "BaseLine") ?? -1;

                if (orden.Detalles.Any(x =>
                        x.LineNum == lineNum &&
                        x.BaseLine == baseLine))
                {
                    continue;
                }

                orden.Detalles.Add(new SapOrdenCompraLineaVm
                {
                    LineNum = lineNum,
                    BaseEntry = baseEntry,
                    BaseLine = baseLine,
                    BaseType = baseType,
                    ItemCode = SapStringCompraTi(
                        line,
                        "ItemCode"),
                    Descripcion = PrimerNoVacioCompraTi(
                        SapStringCompraTi(line, "ItemDescription"),
                        SapStringCompraTi(line, "Dscription")),
                    Cantidad = SapDecimalCompraTi(
                        line,
                        "Quantity"),
                    ImporteLinea = SapDecimalCompraTi(
                        line,
                        "LineTotal"),
                    Almacen = SapStringCompraTi(
                        line,
                        "WarehouseCode",
                        "WhsCode")
                });
            }

            return agrupadas.Values
                .OrderByDescending(x => x.DocEntry)
                .ToList();
        }

        private static void AplicarDatosOrdenCompraSap(
            CompraTiOrdenCompraSap destino,
            SapOrdenCompraVm origen,
            string usuario)
        {
            destino.DocEntry = origen.DocEntry;
            destino.DocNum = origen.DocNum;
            destino.FechaDocumento = origen.FechaDocumento;
            destino.FechaEntrega = origen.FechaEntrega;
            destino.Estado = origen.Estado;
            destino.Cancelada = origen.Cancelada;
            destino.ProveedorCodigo = origen.ProveedorCodigo;
            destino.ProveedorNombre = origen.ProveedorNombre;
            destino.Moneda = string.IsNullOrWhiteSpace(origen.Moneda)
                ? "MXN"
                : origen.Moneda;
            destino.Total = origen.Total;
            destino.LineasRelacionadas = origen.Detalles.Count;
            destino.Comentarios = origen.Comentarios;
            destino.SnapshotJson = JsonSerializer.Serialize(origen);
            destino.Activa = true;
            destino.FechaUltimaConsulta = DateTime.Now;
            destino.ActualizadoPor = usuario;
        }

        private async Task<(
            int total,
            int insertadas,
            int actualizadas,
            int desactivadas,
            List<CompraTiOrdenCompraSap> ordenes)>
            SincronizarOrdenesCompraSapAsync(
                CompraTiSolicitud solicitud,
                CancellationToken ct)
        {
            if (!solicitud.SolicitudSapDocEntry.HasValue ||
                solicitud.SolicitudSapDocEntry.Value <= 0)
            {
                return (
                    0,
                    0,
                    0,
                    0,
                    new List<CompraTiOrdenCompraSap>());
            }

            var sapOrdenes = await ObtenerOrdenesCompraRelacionadasSapAsync(
                solicitud.SolicitudSapDocEntry.Value,
                ct);

            var existentes = await _context.CompraTiOrdenesCompraSap
                .Where(x => x.SolicitudId == solicitud.Id)
                .ToListAsync(ct);

            var existentesPorDocEntry = existentes
                .ToDictionary(x => x.DocEntry);

            var encontrados = new HashSet<int>();
            var usuario = UsuarioActualComprasTi();

            var insertadas = 0;
            var actualizadas = 0;
            var desactivadas = 0;

            foreach (var sapOrden in sapOrdenes)
            {
                encontrados.Add(sapOrden.DocEntry);

                if (!existentesPorDocEntry.TryGetValue(
                        sapOrden.DocEntry,
                        out var entidad))
                {
                    entidad = new CompraTiOrdenCompraSap
                    {
                        SolicitudId = solicitud.Id
                    };

                    _context.CompraTiOrdenesCompraSap.Add(entidad);
                    existentes.Add(entidad);
                    existentesPorDocEntry[sapOrden.DocEntry] = entidad;
                    insertadas++;
                }
                else
                {
                    actualizadas++;
                }

                AplicarDatosOrdenCompraSap(
                    entidad,
                    sapOrden,
                    usuario);
            }

            foreach (var entidad in existentes)
            {
                if (encontrados.Contains(entidad.DocEntry))
                    continue;

                if (entidad.Activa)
                {
                    entidad.Activa = false;
                    entidad.FechaUltimaConsulta = DateTime.Now;
                    entidad.ActualizadoPor = usuario;
                    desactivadas++;
                }
            }

            solicitud.FechaModificacion = DateTime.Now;
            solicitud.ModificadoPor = usuario;

            if (insertadas > 0 ||
                actualizadas > 0 ||
                desactivadas > 0)
            {
                RegistrarBitacoraComprasTi(
                    solicitud.Id,
                    "ORDENES_COMPRA_SAP_SINCRONIZADAS",
                    $"Órdenes detectadas: {sapOrdenes.Count}; " +
                    $"insertadas: {insertadas}; actualizadas: {actualizadas}; " +
                    $"desactivadas: {desactivadas}.");
            }

            await _context.SaveChangesAsync(ct);

            var activas = existentes
                .Where(x => x.Activa)
                .OrderByDescending(x => x.DocEntry)
                .ToList();

            return (
                sapOrdenes.Count,
                insertadas,
                actualizadas,
                desactivadas,
                activas);
        }

        private sealed class SapCompraSolicitudVm
        {
            public int DocEntry { get; set; }
            public int DocNum { get; set; }
            public DateTime? FechaDocumento { get; set; }
            public DateTime? FechaRequerida { get; set; }
            public string Estado { get; set; } = "";
            public bool Cancelada { get; set; }
            public string Solicitante { get; set; } = "";
            public string Comentarios { get; set; } = "";
            public string PrimeraDescripcion { get; set; } = "";
            public int Lineas { get; set; }
            public string CentroCosto { get; set; } = "";
            public string ProveedorPreferido { get; set; } = "";
            public string PlantaSugerida { get; set; } = "PLANTA 1";
            public string TipoCompraSugerido { get; set; } = "";
            public List<SapCompraLineaVm> Detalles { get; set; } = new();
        }

        private sealed class SapCompraLineaVm
        {
            public int? LineaSap { get; set; }
            public string ItemCode { get; set; } = "";
            public string Descripcion { get; set; } = "";
            public decimal Cantidad { get; set; }
            public string Unidad { get; set; } = "PZA";
            public string CentroCostoSap { get; set; } = "";
            public string CentroCostoSap2 { get; set; } = "";
            public string AlmacenSap { get; set; } = "";
            public string ProveedorPreferidoSap { get; set; } = "";
            public DateTime? FechaRequerida { get; set; }
            public string TipoLinea { get; set; } = "ARTICULO";
        }

        private sealed class SapCentroCostoVm
        {
            public string Codigo { get; set; } = "";
            public string Nombre { get; set; } = "";
            public string Grupo { get; set; } = "";
            public int Dimension { get; set; }
            public DateTime? VigenciaDesde { get; set; }
            public DateTime? VigenciaHasta { get; set; }
        }

        private async Task<JsonElement> ObtenerSolicitudCompraSapRawAsync(
            int docEntry,
            CancellationToken ct)
        {
            var endpoint = $"PurchaseRequests({docEntry})";
            var sap = await _sap.GetAsync(endpoint);

            if (!sap.ok || string.IsNullOrWhiteSpace(sap.response))
            {
                throw new InvalidOperationException(
                    $"SAP no devolvió la solicitud {docEntry}. " +
                    $"HTTP {sap.statusCode}. {sap.error}. {sap.response}");
            }

            using var doc = JsonDocument.Parse(sap.response);
            return doc.RootElement.Clone();
        }

        private static SapCompraSolicitudVm MapearSolicitudCompraSapTi(
            JsonElement root)
        {
            var vm = new SapCompraSolicitudVm
            {
                DocEntry = SapIntCompraTi(root, "DocEntry") ?? 0,
                DocNum = SapIntCompraTi(root, "DocNum") ?? 0,
                FechaDocumento = SapDateCompraTi(root, "DocDate", "TaxDate"),
                FechaRequerida = SapDateCompraTi(
                    root,
                    "RequriedDate",
                    "RequiredDate",
                    "DocDueDate"),
                Solicitante = PrimerNoVacioCompraTi(
                    SapStringCompraTi(root, "RequesterName"),
                    SapStringCompraTi(root, "Requester"),
                    SapStringCompraTi(root, "ReqName"),
                    SapStringCompraTi(root, "UserSign")),
                Comentarios = PrimerNoVacioCompraTi(
                    SapStringCompraTi(root, "Comments"),
                    SapStringCompraTi(root, "JournalMemo")),
                Cancelada = SapSiCompraTi(root, "Cancelled", "Canceled")
            };

            var status = PrimerNoVacioCompraTi(
                SapStringCompraTi(root, "DocumentStatus"),
                SapStringCompraTi(root, "DocStatus"));

            vm.Estado = vm.Cancelada
                ? "CANCELADA"
                : status.Contains("Close", StringComparison.OrdinalIgnoreCase)
                    ? "CERRADA"
                    : "ABIERTA";

            var docType = SapStringCompraTi(root, "DocType");

            if (root.TryGetProperty("DocumentLines", out var lineas) &&
                lineas.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lineas.EnumerateArray())
                {
                    var itemCode = SapStringCompraTi(line, "ItemCode");
                    var descripcion = PrimerNoVacioCompraTi(
                        SapStringCompraTi(line, "ItemDescription"),
                        SapStringCompraTi(line, "Dscription"),
                        SapStringCompraTi(line, "FreeText"),
                        SapStringCompraTi(line, "AccountCode"));

                    var unidad = PrimerNoVacioCompraTi(
                        SapStringCompraTi(line, "MeasureUnit"),
                        SapStringCompraTi(line, "UnitsOfMeasurment"),
                        SapStringCompraTi(line, "UoMCode"),
                        "PZA");

                    var tipoLinea =
                        docType.Contains("Service", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(itemCode)
                            ? "SERVICIO"
                            : "ARTICULO";

                    vm.Detalles.Add(new SapCompraLineaVm
                    {
                        LineaSap = SapIntCompraTi(line, "LineNum", "LineNumber"),
                        ItemCode = itemCode,
                        Descripcion = descripcion,
                        Cantidad = SapDecimalCompraTi(line, "Quantity"),
                        Unidad = unidad,
                        CentroCostoSap = SapStringCompraTi(line, "CostingCode"),
                        CentroCostoSap2 = SapStringCompraTi(line, "CostingCode2"),
                        AlmacenSap = SapStringCompraTi(line, "WarehouseCode", "WhsCode"),
                        ProveedorPreferidoSap =
                            SapStringCompraTi(line, "LineVendor", "PreferredVendor"),
                        FechaRequerida = SapDateCompraTi(
                            line,
                            "RequiredDate",
                            "ShipDate")
                    });
                }
            }

            vm.Lineas = vm.Detalles.Count;
            vm.PrimeraDescripcion = vm.Detalles
                .Select(x => x.Descripcion)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

            vm.CentroCosto = vm.Detalles
                .SelectMany(x => new[]
                {
                    x.CentroCostoSap,
                    x.CentroCostoSap2
                })
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

            vm.ProveedorPreferido = vm.Detalles
                .Select(x => x.ProveedorPreferidoSap)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

            var bplId = SapIntCompraTi(
                root,
                "BPL_IDAssignedToInvoice",
                "BPL_IDAssignedToInvoiceField",
                "BranchID",
                "BPLId");

            vm.PlantaSugerida = ResolverPlantaCompraTi(
                bplId,
                vm.Detalles.Select(x => x.AlmacenSap));

            vm.TipoCompraSugerido =
                docType.Contains("Service", StringComparison.OrdinalIgnoreCase) ||
                (vm.Detalles.Count > 0 &&
                 vm.Detalles.All(x => string.IsNullOrWhiteSpace(x.ItemCode)))
                    ? "SERVICIO"
                    : "";

            return vm;
        }

        // Agrupa la planta para validaciones y filtrado: P1 incluye 'ALMACÉN P1',
        // TIF incluye 'ALMACÉN TIF'. Devuelve el grupo (P1/TIF) o el valor normalizado.
        private static string NormalizarGrupoPlanta(string? planta)
        {
            var p = (planta ?? "").Trim().ToUpperInvariant();
            if (p == "P1" || p == "ALMACÉN P1" || p == "ALMACEN P1") return "P1";
            if (p == "TIF" || p == "ALMACÉN TIF" || p == "ALMACEN TIF") return "TIF";
            return p;
        }

        private static string ResolverPlantaCompraTi(
            int? bplId,
            IEnumerable<string> almacenes)
        {
            if (bplId == 776)
                return "TIF 776";

            if ((almacenes ?? Enumerable.Empty<string>())
                .Any(x =>
                    !string.IsNullOrWhiteSpace(x) &&
                    x.Contains("TIF", StringComparison.OrdinalIgnoreCase)))
            {
                return "TIF 776";
            }

            return "PLANTA 1";
        }

        private static string SapStringCompraTi(
            JsonElement element,
            params string[] propertyNames)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return "";

            foreach (var propertyName in propertyNames)
            {
                if (!element.TryGetProperty(propertyName, out var value) ||
                    value.ValueKind == JsonValueKind.Null ||
                    value.ValueKind == JsonValueKind.Undefined)
                {
                    continue;
                }

                return value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : value.ToString();
            }

            return "";
        }

        private static int? SapIntCompraTi(
            JsonElement element,
            params string[] propertyNames)
        {
            var value = SapStringCompraTi(element, propertyNames);

            return int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                ? parsed
                : null;
        }

        private static decimal SapDecimalCompraTi(
            JsonElement element,
            params string[] propertyNames)
        {
            var value = SapStringCompraTi(element, propertyNames);

            return decimal.TryParse(
                    value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                ? parsed
                : 0m;
        }

        private static DateTime? SapDateCompraTi(
            JsonElement element,
            params string[] propertyNames)
        {
            var value = SapStringCompraTi(element, propertyNames);

            return DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsed)
                ? parsed
                : null;
        }

        private static bool SapSiCompraTi(
            JsonElement element,
            params string[] propertyNames)
        {
            var value = SapStringCompraTi(element, propertyNames);

            return value.Equals("tYES", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SapActivoCompraTi(
            JsonElement element,
            params string[] propertyNames)
        {
            var value = SapStringCompraTi(element, propertyNames);

            if (string.IsNullOrWhiteSpace(value))
                return true;

            return !(
                value.Equals("tNO", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("NO", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("0", StringComparison.OrdinalIgnoreCase));
        }

        private static string PrimerNoVacioCompraTi(params string[] values)
        {
            return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
        }

        private string UsuarioActualComprasTi()
        {
            var usuario = (User?.Identity?.Name ?? "SISTEMA").Trim();
            return usuario.Length <= 150 ? usuario : usuario.Substring(0, 150);
        }

        private static string? NormalizarNullableComprasTi(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim().ToUpperInvariant();
        }

        private static string NormalizarTipoLineaComprasTi(
            string? tipoLinea,
            string? tipoCompra)
        {
            var value = (tipoLinea ?? tipoCompra ?? "ARTICULO")
                .Trim()
                .ToUpperInvariant();

            return new[] { "ARTICULO", "SERVICIO", "ACTIVO_FIJO", "CONSUMIBLE" }
                .Contains(value)
                ? value
                : "ARTICULO";
        }

        private static decimal RedondearImporte(decimal valor)
        {
            return decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
        }

        private static string NormalizarRfc(string rfc)
        {
            return new string((rfc ?? "")
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToUpperInvariant();
        }

        private static bool ExpedienteCerradoComprasTi(CompraTiSolicitud solicitud)
        {
            return solicitud.LiberadaPago || solicitud.Estatus == "CANCELADA";
        }

        private static List<string> ObtenerBloqueosPago(
            bool autorizada,
            bool recibidaConforme,
            bool conciliacionOk,
            decimal totalFactura,
            bool yaLiberada)
        {
            var bloqueos = new List<string>();

            if (yaLiberada) bloqueos.Add("El expediente ya fue liberado a pago.");
            if (!autorizada) bloqueos.Add("La compra no está autorizada.");
            if (!recibidaConforme) bloqueos.Add("No existe recepción conforme.");
            if (!conciliacionOk) bloqueos.Add("La factura no coincide con la cotización.");
            if (totalFactura <= 0) bloqueos.Add("No existe una factura válida.");

            return bloqueos;
        }

        private void RegistrarBitacoraComprasTi(
            int solicitudId,
            string accion,
            string detalle)
        {
            _context.CompraTiBitacoras.Add(new CompraTiBitacora
            {
                SolicitudId = solicitudId,
                Accion = (accion ?? "").Trim().ToUpperInvariant(),
                Detalle = (detalle ?? "").Trim(),
                Usuario = UsuarioActualComprasTi(),
                Fecha = DateTime.Now
            });
        }

        private async Task<string> GuardarArchivoCompraTiAsync(
            IFormFile archivo,
            string folio,
            string tipo,
            CancellationToken ct)
        {
            if (archivo == null || archivo.Length <= 0)
                throw new InvalidOperationException("El archivo está vacío.");

            var extensionesPermitidas = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ".pdf", ".xml", ".xlsx", ".xls", ".csv",
                ".jpg", ".jpeg", ".png", ".webp"
            };

            var extension = Path.GetExtension(archivo.FileName);
            if (!extensionesPermitidas.Contains(extension))
                throw new InvalidOperationException("Tipo de archivo no permitido.");

            var folioSeguro = LimpiarSegmentoRutaComprasTi(folio);
            var tipoSeguro = LimpiarSegmentoRutaComprasTi(tipo);

            var carpeta = Path.Combine(
                _env.WebRootPath,
                "uploads",
                "compras-ti",
                folioSeguro,
                tipoSeguro);

            Directory.CreateDirectory(carpeta);

            var nombreSeguro =
                $"{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var rutaFisica = Path.Combine(carpeta, nombreSeguro);

            await using var fs = new FileStream(
                rutaFisica,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            await archivo.CopyToAsync(fs, ct);

            return $"/uploads/compras-ti/{folioSeguro}/{tipoSeguro}/{nombreSeguro}"
                .Replace("\\", "/");
        }

        private static string LimpiarSegmentoRutaComprasTi(string valor)
        {
            var limpio = new string((valor ?? "")
                .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
                .ToArray());

            return string.IsNullOrWhiteSpace(limpio) ? "general" : limpio;
        }

        private static async Task<string> CalcularSha256ComprasTiAsync(
            IFormFile archivo,
            CancellationToken ct)
        {
            await using var stream = archivo.OpenReadStream();
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, ct);
            return BitConverter.ToString(hash).Replace("-", "");
        }

        private sealed class CfdiCompraTiAnalisis
        {
            public string Serie { get; set; } = "";
            public string Folio { get; set; } = "";
            public string Uuid { get; set; } = "";
            public string RfcEmisor { get; set; } = "";
            public DateTime Fecha { get; set; }
            public decimal Subtotal { get; set; }
            public decimal Iva { get; set; }
            public decimal Total { get; set; }
        }

        private static async Task<CfdiCompraTiAnalisis> AnalizarCfdiCompraTiAsync(
            IFormFile xml,
            CancellationToken ct)
        {
            if (xml == null || xml.Length == 0)
                throw new InvalidOperationException("El XML está vacío.");

            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            await using var stream = xml.OpenReadStream();
            using var reader = XmlReader.Create(stream, settings);
            var doc = await XDocument.LoadAsync(reader, LoadOptions.None, ct);

            var comprobante = doc.Root;
            if (comprobante == null ||
                !string.Equals(comprobante.Name.LocalName, "Comprobante", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("No se encontró el nodo Comprobante.");
            }

            string Attr(XElement element, string name)
            {
                return element.Attributes()
                    .FirstOrDefault(a =>
                        string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                    ?.Value ?? "";
            }

            decimal ParseDecimal(string value, string campo, bool obligatorio = true)
            {
                if (decimal.TryParse(
                    value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed))
                {
                    return parsed;
                }

                if (!obligatorio && string.IsNullOrWhiteSpace(value))
                    return 0m;

                throw new InvalidOperationException($"El campo {campo} no contiene un importe válido.");
            }

            var emisor = comprobante.Descendants()
                .FirstOrDefault(x => x.Name.LocalName == "Emisor");
            var timbre = comprobante.Descendants()
                .FirstOrDefault(x => x.Name.LocalName == "TimbreFiscalDigital");
            var impuestos = comprobante.Elements()
                .FirstOrDefault(x => x.Name.LocalName == "Impuestos");

            var fechaTexto = Attr(comprobante, "Fecha");
            if (!DateTime.TryParse(
                    fechaTexto,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var fecha))
            {
                fecha = DateTime.Today;
            }

            var subtotal = ParseDecimal(Attr(comprobante, "SubTotal"), "SubTotal");
            var total = ParseDecimal(Attr(comprobante, "Total"), "Total");
            var ivaTexto = impuestos == null
                ? ""
                : Attr(impuestos, "TotalImpuestosTrasladados");
            var iva = ParseDecimal(ivaTexto, "TotalImpuestosTrasladados", false);

            if (iva == 0m)
            {
                var diferencia = total - subtotal;
                iva = diferencia > 0m ? diferencia : 0m;
            }

            return new CfdiCompraTiAnalisis
            {
                Serie = Attr(comprobante, "Serie"),
                Folio = Attr(comprobante, "Folio"),
                Uuid = timbre == null ? "" : Attr(timbre, "UUID"),
                RfcEmisor = emisor == null ? "" : Attr(emisor, "Rfc"),
                Fecha = fecha,
                Subtotal = subtotal,
                Iva = iva,
                Total = total
            };
        }

        // =========================================================================================
        // FIN MÓDULO CONTROL DE COMPRAS TI
        // =========================================================================================

    }
}