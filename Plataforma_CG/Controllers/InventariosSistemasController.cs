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

namespace Plataforma_CG.Controllers
{
    public class InventariosSistemasController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public InventariosSistemasController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
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

                        
                        i.EnRecuperacion,
                        i.EnReparacion,
                        i.MotivoFalla,
                        i.BitacoraReparacion,
                        TieneFotoFalla = !string.IsNullOrEmpty(i.FotoFalla),

                        HistorialCount = i.RegistrosHistorial.Count()
                    })
                    .OrderBy(i => i.Nombre)
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
             //agre
                if (!string.IsNullOrWhiteSpace(modelo.IdArticuloSap))
                {
                    bool sapDuplicado = _context.InventarioSistemas
                                                .Any(x => x.IdArticuloSap == modelo.IdArticuloSap && x.Id != modelo.Id);

                    if (sapDuplicado)
                    {
                        return Json(new { ok = false, mensaje = $"El ID SAP '{modelo.IdArticuloSap}' ya se encuentra registrado en otro artículo." });
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
                            if (original.Stock <= 0)
                            {
                                return Json(new { ok = false, mensaje = "Operación denegada: No hay stock disponible de este artículo." });
                            }

                            
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

                // Si estaba disponible, le descontamos 1 al stock físico temporalmente porque está roto
                if (string.IsNullOrEmpty(e.Asignacion) && e.Stock > 0) e.Stock -= 1;

                _context.MovimientoInventario.Add(new MovimientoInventario
                {
                    ArticuloSap = e.IdArticuloSap,
                    NombreArticulo = e.Nombre,
                    TipoMovimiento = "SALIDA",
                    Cantidad = string.IsNullOrEmpty(e.Asignacion) ? 1 : 0,
                    Fecha = DateTime.Now,
                    Referencia = $"ENVIADO A TALLER: {e.MotivoFalla}"
                });

                _context.SaveChanges();
                return Json(new { ok = true });
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
    }
}