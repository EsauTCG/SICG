using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using ClosedXML.Excel;
using System.Text.Json;
using Plataforma_CG.ViewModels;
using System.Globalization;
using static Plataforma_CG.Models.Embarque;

public class EmbarquesController : Controller
{
    private readonly AppDbContextQR _qrContext;
    private readonly AppDbContext _ovContext;
    private readonly IWebHostEnvironment _environment;

    public EmbarquesController(AppDbContextQR qrContext, AppDbContext ovContext, IWebHostEnvironment environment)
    {
        _qrContext = qrContext;
        _ovContext = ovContext;
        _environment = environment;
    }

    // ============================================================ 
    // 1. LISTADO DE EMBARQUES 
    // ============================================================ 
    public async Task<IActionResult> Embarque(
        string? busqueda,
        DateTime? fechaInicio,
        DateTime? fechaFin,
        int? estatus,

        // Filtros exclusivos para la sección de completados
        string? busquedaCompletados,
        DateTime? fechaInicioCompletados,
        DateTime? fechaFinCompletados)
    {
        // ============================================================
        // LISTADO PRINCIPAL: SOLO ACTIVOS / NO ENTREGADOS
        // ============================================================
        var query = _qrContext.Embarque
            .AsNoTracking()
            .Include(e => e.Documentos)
            .Include(e => e.QR)
            .Where(e => e.Estatus != 5) // 5 = Entregado
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            busqueda = busqueda.Trim();

            query = query.Where(e =>
                e.Consecutivo.Contains(busqueda) ||
                e.Id.ToString().Contains(busqueda) ||
                (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busqueda))
            );
        }

        if (fechaInicio.HasValue)
        {
            var inicio = fechaInicio.Value.Date;
            query = query.Where(e => e.FechaCreacion >= inicio);
        }

        if (fechaFin.HasValue)
        {
            var fin = fechaFin.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(e => e.FechaCreacion <= fin);
        }

        // En el listado principal ya no permitimos cargar Entregados
        if (estatus.HasValue && estatus.Value != 5)
        {
            query = query.Where(e => e.Estatus == estatus.Value);
        }

        var embarques = await query
            .OrderByDescending(e => e.FechaCreacion)
            .ToListAsync();


        // ============================================================
        // SECCIÓN HISTÓRICA: ENTREGADOS
        // NO CARGA NADA HASTA QUE TENGA FECHA INICIO Y FECHA FIN
        // ============================================================
        var embarquesCompletados = new List<Embarque>();

        bool buscarCompletados =
            fechaInicioCompletados.HasValue &&
            fechaFinCompletados.HasValue;

        if (buscarCompletados)
        {
            var queryCompletados = _qrContext.Embarque
                .AsNoTracking()
                .Include(e => e.Documentos)
                .Include(e => e.QR)
                .Where(e => e.Estatus == 5) // 5 = Entregado
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(busquedaCompletados))
            {
                busquedaCompletados = busquedaCompletados.Trim();

                queryCompletados = queryCompletados.Where(e =>
                    e.Consecutivo.Contains(busquedaCompletados) ||
                    e.Id.ToString().Contains(busquedaCompletados) ||
                    (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busquedaCompletados))
                );
            }

            var inicioCompletados = fechaInicioCompletados.Value.Date;
            var finCompletados = fechaFinCompletados.Value.Date.AddDays(1).AddTicks(-1);

            // Para completados usamos FechaEntregado.
            // Si por algún registro viejo viene null, usamos FechaCreacion como respaldo.
            queryCompletados = queryCompletados.Where(e =>
                (e.FechaEntregado ?? e.FechaCreacion) >= inicioCompletados &&
                (e.FechaEntregado ?? e.FechaCreacion) <= finCompletados
            );

            embarquesCompletados = await queryCompletados
                .OrderByDescending(e => e.FechaEntregado ?? e.FechaCreacion)
                .ToListAsync();
        }

        // Filtros del listado principal
        ViewBag.Busqueda = busqueda;
        ViewBag.FechaInicio = fechaInicio?.ToString("yyyy-MM-dd");
        ViewBag.FechaFin = fechaFin?.ToString("yyyy-MM-dd");
        ViewBag.Estatus = estatus;

        // Filtros de completados
        ViewBag.BusquedaCompletados = busquedaCompletados;
        ViewBag.FechaInicioCompletados = fechaInicioCompletados?.ToString("yyyy-MM-dd");
        ViewBag.FechaFinCompletados = fechaFinCompletados?.ToString("yyyy-MM-dd");
        ViewBag.BuscoCompletados = buscarCompletados;
        ViewBag.EmbarquesCompletados = embarquesCompletados;

        return View(embarques);
    }

    // ============================================================
// CREAR EMBARQUE - PÁGINA INICIAL LIGERA
// ============================================================
[HttpGet]
[Authorize(Roles = "Administracion de Ventas,Administrador")]
public async Task<IActionResult> Crear(CancellationToken cancellationToken)
{
    // Solo obtenemos los conteos.
    // Ya no cargamos miles de registros al abrir la página.
    var totalOrdenes = await _ovContext.OrdenVenta
        .AsNoTracking()
        .CountAsync(o => o.Estatus == 5, cancellationToken);

        var transferenciasYaEnEmbarque = await _qrContext.EmbarqueDocumento
            .AsNoTracking()
            .Where(d => d.TipoDocumento == "TRANSFERENCIA")
            .Select(d => d.DocumentoId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var totalTransferencias = await _ovContext.Transferencias
            .AsNoTracking()
            .CountAsync(t =>
                t.Estatus == 4 &&
                !transferenciasYaEnEmbarque.Contains(t.Id),
                cancellationToken);

        var viewModel = new CrearEmbarqueViewModel
    {
        TotalOrdenes = totalOrdenes,
        TotalTransferencias = totalTransferencias
    };

    return View(viewModel);
}

    // ============================================================
    // CONSULTAR OV DISPONIBLES CON FILTROS Y PAGINACIÓN
    // ============================================================
    [HttpGet]
    [Authorize(Roles = "Administracion de Ventas,Administrador")]
    public async Task<IActionResult> ObtenerOrdenesDisponibles(
        string? busqueda,
        string? cliente,
        string? consecutivo,
        string? ruta,
        int pagina = 1,
        int tamanoPagina = 50,
        CancellationToken cancellationToken = default)
    {
        pagina = Math.Max(pagina, 1);
        tamanoPagina = Math.Clamp(tamanoPagina, 10, 100);

        busqueda = busqueda?.Trim();
        cliente = cliente?.Trim();
        consecutivo = consecutivo?.Trim();
        ruta = ruta?.Trim();

        // Agrupamos para evitar duplicados en caso de que ClienteSap
        // tenga más de un registro para el mismo código.
        var clientesSapQuery = _ovContext.ClienteSap
            .AsNoTracking()
            .GroupBy(c => c.Cliente)
            .Select(g => new
            {
                Cliente = g.Key,
                NombreCliente = g.Max(x => x.Nombrecliente)
            });

        var query =
            from o in _ovContext.OrdenVenta.AsNoTracking()
            join c in clientesSapQuery
                on o.Cliente equals c.Cliente into clientes
            from c in clientes.DefaultIfEmpty()
            where o.Estatus == 5
            select new OrdenDisponibleViewModel
            {
                Id = o.Id,
                Cliente = o.Cliente ?? "",
                NombreCliente =
                    c != null && c.NombreCliente != null
                        ? c.NombreCliente
                        : o.Cliente ?? "",
                Consecutivo = o.Consecutivo ?? "",
                Ruta = o.Ruta ?? ""
            };

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            query = query.Where(x =>
                x.Id.ToString().Contains(busqueda) ||
                x.Cliente.Contains(busqueda) ||
                x.NombreCliente.Contains(busqueda) ||
                x.Consecutivo.Contains(busqueda) ||
                x.Ruta.Contains(busqueda));
        }

        if (!string.IsNullOrWhiteSpace(cliente))
        {
            query = query.Where(x =>
                x.Cliente.Contains(cliente) ||
                x.NombreCliente.Contains(cliente));
        }

        if (!string.IsNullOrWhiteSpace(consecutivo))
        {
            query = query.Where(x => x.Consecutivo.Contains(consecutivo));
        }

        if (!string.IsNullOrWhiteSpace(ruta))
        {
            query = query.Where(x => x.Ruta.Contains(ruta));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .ToListAsync(cancellationToken);

        return Json(new
        {
            items,
            total,
            pagina,
            tamanoPagina,
            hayMas = pagina * tamanoPagina < total
        });
    }

    // ============================================================
    // CONSULTAR TRANSFERENCIAS CON FILTROS Y PAGINACIÓN
    // ============================================================
    [HttpGet]
    [Authorize(Roles = "Administracion de Ventas,Administrador")]
    public async Task<IActionResult> ObtenerTransferenciasDisponibles(
        string? busqueda,
        string? sucursal,
        string? consecutivo,
        string? fecha,
        int pagina = 1,
        int tamanoPagina = 50,
        CancellationToken cancellationToken = default)
    {
        pagina = Math.Max(pagina, 1);
        tamanoPagina = Math.Clamp(tamanoPagina, 10, 100);

        busqueda = busqueda?.Trim();
        sucursal = sucursal?.Trim();
        consecutivo = consecutivo?.Trim();
        fecha = fecha?.Trim();

        // Transferencias que ya están ligadas a cualquier embarque.
        // Estas ya no deben volver a salir como disponibles.
        var transferenciasYaEnEmbarque = await _qrContext.EmbarqueDocumento
            .AsNoTracking()
            .Where(d => d.TipoDocumento == "TRANSFERENCIA")
            .Select(d => d.DocumentoId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var query = _ovContext.Transferencias
            .AsNoTracking()
            .Where(t =>
                t.Estatus == 4 &&
                !transferenciasYaEnEmbarque.Contains(t.Id));

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            query = query.Where(t =>
                t.Id.ToString().Contains(busqueda) ||
                (t.Sucursal != null && t.Sucursal.Contains(busqueda)) ||
                (t.Consecutivo != null && t.Consecutivo.Contains(busqueda)));
        }

        if (!string.IsNullOrWhiteSpace(sucursal))
        {
            query = query.Where(t =>
                t.Sucursal != null &&
                t.Sucursal.Contains(sucursal));
        }

        if (!string.IsNullOrWhiteSpace(consecutivo))
        {
            query = query.Where(t =>
                t.Consecutivo != null &&
                t.Consecutivo.Contains(consecutivo));
        }

        if (!string.IsNullOrWhiteSpace(fecha))
        {
            var culturaMexico = CultureInfo.GetCultureInfo("es-MX");

            if (DateTime.TryParse(
                fecha,
                culturaMexico,
                DateTimeStyles.None,
                out var fechaFiltro))
            {
                var fechaInicio = fechaFiltro.Date;
                var fechaFin = fechaInicio.AddDays(1);

                query = query.Where(t =>
                    t.FechaSolicitud >= fechaInicio &&
                    t.FechaSolicitud < fechaFin);
            }
        }

        var total = await query.CountAsync(cancellationToken);

        var datos = await query
            .OrderByDescending(t => t.FechaSolicitud)
            .ThenByDescending(t => t.Id)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(t => new
            {
                t.Id,
                Sucursal = t.Sucursal ?? "",
                Consecutivo = t.Consecutivo ?? "",
                t.FechaSolicitud
            })
            .ToListAsync(cancellationToken);

        var items = datos.Select(t => new TransferenciaDisponibleViewModel
        {
            Id = t.Id,
            Sucursal = t.Sucursal,
            Consecutivo = t.Consecutivo,
            FechaSolicitud = t.FechaSolicitud,
            FechaSolicitudTexto = t.FechaSolicitud?.ToString("dd/MM/yyyy") ?? ""
        }).ToList();

        return Json(new
        {
            items,
            total,
            pagina,
            tamanoPagina,
            hayMas = pagina * tamanoPagina < total
        });
    }


    // ============================================================
    // GUARDAR EMBARQUE NUEVO - OPTIMIZADO
    // ============================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Administracion de Ventas,Administrador")]
    public async Task<IActionResult> Crear(
        List<int>? ordenesSeleccionadas,
        List<int>? transferenciasSeleccionadas,
        string? nombreEmbarque,
        string? observaciones,
        CancellationToken cancellationToken)
    {
        ordenesSeleccionadas = ordenesSeleccionadas?
            .Where(id => id > 0)
            .Distinct()
            .ToList() ?? new List<int>();

        transferenciasSeleccionadas = transferenciasSeleccionadas?
            .Where(id => id > 0)
            .Distinct()
            .ToList() ?? new List<int>();

        if (!ordenesSeleccionadas.Any() &&
            !transferenciasSeleccionadas.Any())
        {
            TempData["Error"] =
                "Debes seleccionar al menos una orden o transferencia.";

            return RedirectToAction(nameof(Crear));
        }

        nombreEmbarque = nombreEmbarque?.Trim();
        observaciones = observaciones?.Trim();

        if (!string.IsNullOrWhiteSpace(nombreEmbarque) &&
            nombreEmbarque.Length > 150)
        {
            TempData["Error"] =
                "El nombre del embarque no puede tener más de 150 caracteres.";

            return RedirectToAction(nameof(Crear));
        }

        // Una sola consulta para todas las órdenes.
        var ordenes = ordenesSeleccionadas.Any()
            ? await _ovContext.OrdenVenta
                .Where(o =>
                    ordenesSeleccionadas.Contains(o.Id) &&
                    o.Estatus == 5)
                .ToListAsync(cancellationToken)
            : new List<OrdenVenta>();

        // Una sola consulta para todas las transferencias.
        var transferenciasYaEnEmbarqueSeleccionadas = transferenciasSeleccionadas.Any()
            ? await _qrContext.EmbarqueDocumento
                .AsNoTracking()
                .Where(d =>
                    d.TipoDocumento == "TRANSFERENCIA" &&
                    transferenciasSeleccionadas.Contains(d.DocumentoId))
                .Select(d => d.DocumentoId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : new List<int>();

        if (transferenciasYaEnEmbarqueSeleccionadas.Any())
        {
            TempData["Error"] =
                "Una o más transferencias seleccionadas ya pertenecen a otro embarque. Actualiza la página e intenta nuevamente.";

            return RedirectToAction(nameof(Crear));
        }

        var transferencias = transferenciasSeleccionadas.Any()
            ? await _ovContext.Transferencias
                .Where(t =>
                    transferenciasSeleccionadas.Contains(t.Id) &&
                    t.Estatus == 4)
                .ToListAsync(cancellationToken)
            : new List<Transferencia>();

        // Evita crear el embarque si algún registro ya fue utilizado
        // por otro usuario mientras esta pantalla estaba abierta.
        if (ordenes.Count != ordenesSeleccionadas.Count)
        {
            TempData["Error"] =
                "Una o más órdenes seleccionadas ya no están disponibles. Actualiza la página e intenta nuevamente.";

            return RedirectToAction(nameof(Crear));
        }

        if (transferencias.Count != transferenciasSeleccionadas.Count)
        {
            TempData["Error"] =
                "Una o más transferencias seleccionadas ya no están disponibles. Actualiza la página e intenta nuevamente.";

            return RedirectToAction(nameof(Crear));
        }

        var ahora = DateTime.Now;

        await using var transaccionQr =
            await _qrContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var embarque = new Embarque
            {
                FechaCreacion = ahora,
                UsuarioGenera = User.Identity?.Name ?? "Sistema",
                Estatus = 1,
                NombreEmbarque = string.IsNullOrWhiteSpace(nombreEmbarque)
                    ? null
                    : nombreEmbarque,
                Observaciones = observaciones,
                CalidadAprobada = false,
                DocumentacionAprobada = false,
                DocumentacionCalidadAprobada = false
            };

            _qrContext.Embarque.Add(embarque);

            // Necesitamos este guardado para obtener embarque.Id.
            await _qrContext.SaveChangesAsync(cancellationToken);

            var documentos = new List<EmbarqueDocumento>(
                ordenes.Count + transferencias.Count);

            documentos.AddRange(
                ordenes.Select(o => new EmbarqueDocumento
                {
                    EmbarqueId = embarque.Id,
                    DocumentoId = o.Id,
                    TipoDocumento = "OV"
                }));

            documentos.AddRange(
                transferencias.Select(t => new EmbarqueDocumento
                {
                    EmbarqueId = embarque.Id,
                    DocumentoId = t.Id,
                    TipoDocumento = "TRANSFERENCIA"
                }));

            // Una sola llamada AddRange.
            _qrContext.EmbarqueDocumento.AddRange(documentos);

            // Actualización en memoria de todos los registros recuperados
            // en las dos consultas anteriores.
            foreach (var orden in ordenes)
            {
                orden.Estatus = 6;
                orden.FechaEmbarque = ahora;
            }

            // IMPORTANTE:
            // Las transferencias NO se cambian de estatus aquí.
            // Se quedan en 4 para que su flujo externo pueda pasarlas a 5.
            // El candado para que no vuelvan a aparecer es la relación en EmbarqueDocumento.

            // Un guardado por DbContext.
            await _qrContext.SaveChangesAsync(cancellationToken);
            await _ovContext.SaveChangesAsync(cancellationToken);

            await transaccionQr.CommitAsync(cancellationToken);

            return RedirectToAction("Detalle", new
            {
                id = embarque.Id
            });
        }
        catch
        {
            await transaccionQr.RollbackAsync(cancellationToken);

            TempData["Error"] =
                "No fue posible crear el embarque. Ningún cambio del embarque fue confirmado.";

            return RedirectToAction(nameof(Crear));
        }
    }


    // ============================================================ 
    // 4. VER DETALLE 
    // ============================================================ 
    public async Task<IActionResult> Detalle(int id)
    {
        var embarque = await _qrContext.Embarque
            .Include(e => e.Documentos)
            .Include(e => e.Archivos)
            .Include(e => e.QR)
            .FirstOrDefaultAsync(e => e.Id == id); ;

        if (embarque == null)
            return NotFound();

        // ============================ 
        // SEPARAR POR TIPO 
        // ============================ 

        var ordenesIds = embarque.Documentos
            .Where(d => d.TipoDocumento == "OV")
            .Select(d => d.DocumentoId)
            .ToList();

        var transferenciasIds = embarque.Documentos
            .Where(d => d.TipoDocumento == "TRANSFERENCIA")
            .Select(d => d.DocumentoId)
            .ToList();

        // ============================ 
        // TRAER ÓRDENES 
        // ============================ 

        var ordenesReal = await _ovContext.OrdenVenta
            .Where(o => ordenesIds.Contains(o.Id))
            .Select(o => new
            {
                o.Id,
                o.Consecutivo,
                o.Ruta,
                o.FechaEntrega,
                CodigoCliente = o.Cliente,
                NombreCliente = _ovContext.ClienteSap
                    .Where(c => c.Cliente == o.Cliente)
                    .Select(c => c.Nombrecliente)
                    .FirstOrDefault()
            })
            .ToListAsync();

        // ============================ 
        // TRAER TRANSFERENCIAS 
        // ============================ 

        var transferenciasReal = await _ovContext.Transferencias
            .Where(t => transferenciasIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.Consecutivo,
                t.Sucursal,
                t.FechaSolicitud,
                t.UsuarioSolicita
            })
            .ToListAsync();

        ViewBag.OrdenesVenta = ordenesReal;
        ViewBag.Transferencias = transferenciasReal;

        var fotosCalidad = await _qrContext.Set<Embarque.EmbarqueCalidadFoto>()
            .AsNoTracking()
            .Where(f => f.EmbarqueId == embarque.Id)
            .OrderByDescending(f => f.FechaRegistro)
            .ToListAsync();

        ViewBag.FotosCalidad = fotosCalidad;

        return View(embarque);
    }


    // ============================================================ 
    // 5. GENERAR QR 
    // ============================================================ 
    public async Task<IActionResult> GenerarQR(int id)
    {
        var embarque = await _qrContext.Embarque
            .Include(e => e.QR)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (embarque == null)
            return NotFound();

        if (embarque.CalidadAprobada != true
            || embarque.DocumentacionAprobada != true
            || embarque.DocumentacionCalidadAprobada != true)
        {
            TempData["Error"] = "No se puede generar el QR hasta que Calidad, Documentación Logística y Documentación de Calidad validen el embarque.";
            return RedirectToAction("Detalle", new { id });
        }

        if (embarque.Estatus != 7)
        {
            embarque.Estatus = 7;
            await _qrContext.SaveChangesAsync();
        }

        if (embarque.QR != null)
        {
            TempData["Error"] = "Este embarque ya tiene un QR generado.";
            return RedirectToAction("Detalle", new { id });
        }

        string token = Guid.NewGuid().ToString("N");
        //string urlValidar = $"{Request.Scheme}://{Request.Host}/Embarques/Caseta?token={token}"; Para que Tome la URL actual de donde se Crea (Podria servir en un futuro)
        string urlValidar = $"http://10.2.1.81:1465/Embarques/Caseta?token={token}";

        var qrGenerator = new QRCoder.QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(urlValidar, QRCoder.QRCodeGenerator.ECCLevel.Q);
        var qrCode = new QRCoder.PngByteQRCode(qrData);
        var qrBytes = qrCode.GetGraphic(20);
        string qrBase64 = Convert.ToBase64String(qrBytes);

        var qr = new EmbarqueQR
        {
            EmbarqueId = id,
            Token = token,
            UrlQR = "data:image/png;base64," + qrBase64,
            FechaGeneracion = DateTime.Now,
            Estado = 1,
            UsuarioGenera = User.Identity?.Name ?? "Sistema"
        };

        _qrContext.EmbarqueQR.Add(qr);
        await _qrContext.SaveChangesAsync();

        return RedirectToAction("Detalle", new { id });
    }

    // ============================================================ 
    // 6. ENDPOINT PARA CASETA 
    // ============================================================ 
    [HttpGet]
    public async Task<IActionResult> ValidarQR(string token)
    {
        var qr = await _qrContext.EmbarqueQR
            .Include(q => q.Embarque)
            .ThenInclude(e => e.Documentos)
            .FirstOrDefaultAsync(q => q.Token == token);

        if (qr == null)
            return NotFound("QR no válido");

        // Ids de las OV incluidas en el embarque 
        var ordenesIds = qr.Embarque.Documentos
           .Where(d => d.TipoDocumento == "OV")
           .Select(d => d.DocumentoId)
           .ToList();


        // Traemos la info de las OV junto con el nombre del cliente desde ClienteSap 
        var ordenesInfo = await _ovContext.OrdenVenta
            .Where(o => ordenesIds.Contains(o.Id))
            .Select(o => new
            {
                id = o.Id,
                clienteId = o.Cliente,           // código (ej. C000176) por si lo necesitas 
                cliente = _ovContext.ClienteSap   // buscamos el nombre real 
                            .Where(c => c.Cliente == o.Cliente)
                            .Select(c => c.Nombrecliente)
                            .FirstOrDefault() ?? o.Cliente, // fallback al código si no existe 
                consecutivo = o.Consecutivo,
                ruta = o.Ruta,
                presentacion = o.Presentacion,
                fechaEntrega = o.FechaEntrega.ToString("yyyy-MM-dd")
            })
            .ToListAsync();


        var transferenciasIds = qr.Embarque.Documentos
            .Where(d => d.TipoDocumento == "TRANSFERENCIA")
            .Select(d => d.DocumentoId)
            .ToList();

        var transferenciasInfo = (await _ovContext.Transferencias
            .Where(t => transferenciasIds.Contains(t.Id))
            .ToListAsync())
            .Select(t => new
            {
                id = t.Id,
                consecutivo = t.Consecutivo,
                sucursal = t.Sucursal,
                fechaSolicitud = t.FechaSolicitud?.ToString("yyyy-MM-dd")
            })
            .ToList();



        // Retornamos las claves tal como las usa tu JS (embarque, qrGenerado, ordenes) 
        return Ok(new
        {
            embarqueId = qr.EmbarqueId,

            embarqueConsecutivo = string.IsNullOrWhiteSpace(qr.Embarque.Consecutivo)
                ? $"EMB-{qr.EmbarqueId}"
                : qr.Embarque.Consecutivo,

            embarqueNombre = qr.Embarque.NombreEmbarque ?? "",

            qrGenerado = qr.FechaGeneracion.ToString("yyyy-MM-dd HH:mm"),

            ordenes = ordenesInfo,
            transferencias = transferenciasInfo
        });
    }


    public IActionResult Caseta(string? token)
    {
        if (!User.Identity.IsAuthenticated)
        {
            // Construir ReturnUrl completo con el token 
            var returnUrl = Url.Action("Caseta", "Embarques", new { token });

            return RedirectToAction(
                "Index",  // ← Index es donde está el modal de login 
                "Home",   // ← Home es el controller correcto 
                new { ReturnUrl = returnUrl }
            );
        }

        var embarques = _qrContext.Embarque
            .Where(e => e.Estatus == 7 || e.Estatus == 2)
            .OrderByDescending(e => e.FechaCreacion)
            .ToList();

        ViewBag.Token = token ?? "";
        return View(embarques);
    }


    public IActionResult RegistrarEntrada(int id)
    {
        var e = _qrContext.Embarque.Find(id);
        e.Estatus = 1;
        e.FechaEntrada = DateTime.Now;
        _qrContext.SaveChanges();

        return RedirectToAction("Caseta");
    }

    public async Task<IActionResult> RegistrarSalida(int id)
    {
        var e = await _qrContext.Embarque
            .Include(x => x.Documentos)
            .Include(x => x.QR)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (e == null)
            return RedirectToAction("Caseta", new { alert = "notfound" });

        // Ya tiene salida registrada 
        if (e.FechaSalida != null)
        {
            return RedirectToAction("Caseta", new { alert = "ya-salida" });
        }

        // Solo puede salir si ya fue aprobado por Calidad 
        if (e.Estatus != 7
            || e.CalidadAprobada != true
            || e.DocumentacionAprobada != true
            || e.DocumentacionCalidadAprobada != true)
        {
            return RedirectToAction("Caseta", new { alert = "no-calidad" });
        }

        // Cambiar estatus del embarque a "En tránsito" 
        e.Estatus = 2;
        e.FechaSalida = DateTime.Now;

        // Guardar usuario que valida 
        if (e.QR != null)
        {
            e.QR.UsuarioValida = User.Identity?.Name ?? "Sistema";
            e.QR.FechaValidacion = DateTime.Now;
            e.QR.Estado = 2;
        }

        await _qrContext.SaveChangesAsync();

        // ============================================ 
        // ACTUALIZAR DOCUMENTOS → estatus 7 
        // ============================================ 
        if (e.Documentos != null && e.Documentos.Any())
        {
            foreach (var doc in e.Documentos)
            {
                if (doc.TipoDocumento == "OV")
                {
                    var ov = await _ovContext.OrdenVenta
                        .FirstOrDefaultAsync(x => x.Id == doc.DocumentoId);

                    if (ov != null)
                        ov.Estatus = 7;
                }
                else if (doc.TipoDocumento == "TRANSFERENCIA")
                {
                    // No se modifica el estatus de la transferencia.
                    // El seguimiento del viaje se maneja con el estatus del Embarque.
                    // El flujo externo de Transferencias sigue controlando su 4 → 5.
                }
            }

            await _ovContext.SaveChangesAsync();
        }

        return RedirectToAction("Caseta", new { alert = "salida-ok" });
    }



    // Reemplaza tu método TrackingSIGO con este: 

    public async Task<IActionResult> TrackingSIGO(string token)
    {
        // Si no hay token, mostrar formulario de búsqueda 
        if (string.IsNullOrEmpty(token))
        {
            return View();
        }

        try
        {
            // Si hay token, buscar el embarque 
            var qr = await _qrContext.EmbarqueQR
                .Include(q => q.Embarque)
                .FirstOrDefaultAsync(q => q.Token == token);

            if (qr == null)
            {
                ViewBag.Error = "Token no válido o embarque no encontrado";
                return View();
            }

            var embarque = qr.Embarque;

            // Pasar TODAS las fechas a la vista 
            ViewBag.EmbarqueId = embarque.Id; // interno, por si después lo ocupas 
            ViewBag.EmbarqueConsecutivo = string.IsNullOrWhiteSpace(embarque.Consecutivo)
                ? $"EMB-{embarque.Id}"
                : embarque.Consecutivo;
            ViewBag.NombreEmbarque = embarque.NombreEmbarque ?? "";
            ViewBag.Estatus = embarque.Estatus;
            ViewBag.FechaCreacion = embarque.FechaCreacion;
            ViewBag.FechaEntrada = embarque.FechaEntrada;
            ViewBag.FechaSalida = embarque.FechaSalida;
            ViewBag.FechaLlegadaDestino = embarque.FechaLlegadaDestino;
            ViewBag.FechaRetrasado = embarque.FechaRetrasado;
            ViewBag.FechaEntregado = embarque.FechaEntregado;
            ViewBag.FechaDevuelto = embarque.FechaDevuelto;
            ViewBag.Token = token;

            return View();
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Ocurrió un error al buscar el embarque. Intenta nuevamente.";
            return View();
        }
    }


    // Agregar este método a EmbarquesController 

    public async Task<IActionResult> ControlCenter(
        string? busqueda,
        DateTime? fechaInicio,
        DateTime? fechaFin,

        // Filtros de la sección de completados
        string? busquedaCompletados,
        DateTime? fechaInicioCompletados,
        DateTime? fechaFinCompletados)
    {
        // ============================================================
        // LISTADO PRINCIPAL: ACTIVOS / EN PROCESO
        // Aquí NO cargamos entregados
        // ============================================================
        var query = _qrContext.Embarque
            .AsNoTracking()
            .Include(e => e.Documentos)
            .Include(e => e.QR)
            .Where(e => e.Estatus >= 2 && e.Estatus != 5);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            busqueda = busqueda.Trim();

            query = query.Where(e =>
                e.Consecutivo.Contains(busqueda) ||
                e.Id.ToString().Contains(busqueda) ||
                (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busqueda))
            );
        }

        if (fechaInicio.HasValue)
        {
            var inicio = fechaInicio.Value.Date;
            query = query.Where(e =>
                (e.FechaSalida ?? e.FechaCreacion) >= inicio
            );
        }

        if (fechaFin.HasValue)
        {
            var fin = fechaFin.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(e =>
                (e.FechaSalida ?? e.FechaCreacion) <= fin
            );
        }

        var embarques = await query
            .OrderByDescending(e => e.FechaSalida ?? e.FechaCreacion)
            .ToListAsync();

        var embarquesConInfo = await ConstruirInfoControlCenter(embarques);


        // ============================================================
        // SECCIÓN ABAJO: COMPLETADOS / ENTREGADOS
        // NO CARGA NADA HASTA QUE TENGA FECHA INICIO Y FECHA FIN
        // ============================================================
        var embarquesCompletadosConInfo = new List<dynamic>();

        bool buscoCompletados =
            fechaInicioCompletados.HasValue &&
            fechaFinCompletados.HasValue;

        if (buscoCompletados)
        {
            var inicioCompletados = fechaInicioCompletados!.Value.Date;
            var finCompletados = fechaFinCompletados!.Value.Date.AddDays(1).AddTicks(-1);

            var queryCompletados = _qrContext.Embarque
                .AsNoTracking()
                .Include(e => e.Documentos)
                .Include(e => e.QR)
                .Where(e => e.Estatus == 5)
                .Where(e =>
                    (e.FechaEntregado ?? e.FechaCreacion) >= inicioCompletados &&
                    (e.FechaEntregado ?? e.FechaCreacion) <= finCompletados
                )
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(busquedaCompletados))
            {
                busquedaCompletados = busquedaCompletados.Trim();

                queryCompletados = queryCompletados.Where(e =>
                    e.Consecutivo.Contains(busquedaCompletados) ||
                    e.Id.ToString().Contains(busquedaCompletados) ||
                    (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busquedaCompletados))
                );
            }

            var embarquesCompletados = await queryCompletados
                .OrderByDescending(e => e.FechaEntregado ?? e.FechaCreacion)
                .ToListAsync();

            embarquesCompletadosConInfo = await ConstruirInfoControlCenter(embarquesCompletados);
        }

        ViewBag.Embarques = embarquesConInfo;

        ViewBag.EmbarquesCompletados = embarquesCompletadosConInfo;
        ViewBag.BuscoCompletados = buscoCompletados;
        ViewBag.BusquedaCompletados = busquedaCompletados;
        ViewBag.FechaInicioCompletados = fechaInicioCompletados?.ToString("yyyy-MM-dd");
        ViewBag.FechaFinCompletados = fechaFinCompletados?.ToString("yyyy-MM-dd");

        return View();
    }

    private async Task<List<dynamic>> ConstruirInfoControlCenter(List<Embarque> embarques)
    {
        var embarquesConInfo = new List<dynamic>();

        foreach (var emb in embarques)
        {
            var ordenesIds = emb.Documentos
                .Where(d => d.TipoDocumento == "OV")
                .Select(d => d.DocumentoId)
                .ToList();

            var transferenciasIds = emb.Documentos
                .Where(d => d.TipoDocumento == "TRANSFERENCIA")
                .Select(d => d.DocumentoId)
                .ToList();

            var ordenesInfo = new List<ControlCenterDocumentoInfo>();
            var transferenciasInfo = new List<ControlCenterDocumentoInfo>();

            if (ordenesIds.Any())
            {
                ordenesInfo = await _ovContext.OrdenVenta
                    .AsNoTracking()
                    .Where(o => ordenesIds.Contains(o.Id))
                    .Select(o => new ControlCenterDocumentoInfo
                    {
                        Ruta = o.Ruta,
                        Cliente = _ovContext.ClienteSap
                            .Where(c => c.Cliente == o.Cliente)
                            .Select(c => c.Nombrecliente)
                            .FirstOrDefault() ?? o.Cliente,
                        Presentacion = o.Presentacion,
                        FechaEntrega = o.FechaEntrega,
                        Pedido = o.Consecutivo
                    })
                    .ToListAsync();
            }

            if (transferenciasIds.Any())
            {
                transferenciasInfo = await _ovContext.Transferencias
                    .AsNoTracking()
                    .Where(t => transferenciasIds.Contains(t.Id))
                    .Select(t => new ControlCenterDocumentoInfo
                    {
                        Ruta = $"Transferencia → {t.Sucursal}",
                        Cliente = t.Sucursal,
                        Presentacion = "Transferencia",
                        FechaEntrega = t.FechaSolicitud,
                        Pedido = t.Consecutivo
                    })
                    .ToListAsync();
            }

            var tipos = emb.Documentos
                .Select(d => d.TipoDocumento)
                .Distinct()
                .ToList();

            string tipoDocumento = tipos.Count switch
            {
                1 when tipos.Contains("OV") => "OV",
                1 when tipos.Contains("TRANSFERENCIA") => "TR",
                2 => "MIXTO",
                _ => "N/A"
            };

            var data = ordenesInfo.FirstOrDefault() ?? transferenciasInfo.FirstOrDefault();

            var pedidos = ordenesInfo
                .Where(x => !string.IsNullOrWhiteSpace(x.Pedido))
                .Select(x => $"OV {x.Pedido}")
                .Concat(
                    transferenciasInfo
                        .Where(x => !string.IsNullOrWhiteSpace(x.Pedido))
                        .Select(x => $"TR {x.Pedido}")
                )
                .Distinct()
                .ToList();

            bool validadoParaQR =
                emb.CalidadAprobada == true &&
                emb.DocumentacionAprobada == true &&
                emb.DocumentacionCalidadAprobada == true;

            int estatusVisual =
                emb.Estatus == 7 && !validadoParaQR
                    ? 1
                    : emb.Estatus;

            embarquesConInfo.Add(new
            {
                EmbarqueId = emb.Id,
                EmbarqueConsecutivo = string.IsNullOrWhiteSpace(emb.Consecutivo)
                    ? $"EMB-{emb.Id}"
                    : emb.Consecutivo,

                NombreEmbarque = emb.NombreEmbarque ?? "",

                Estatus = estatusVisual,
                EstatusReal = emb.Estatus,

                CalidadAprobada = emb.CalidadAprobada == true,
                DocumentacionAprobada = emb.DocumentacionAprobada == true,
                DocumentacionCalidadAprobada = emb.DocumentacionCalidadAprobada == true,
                ValidadoParaQR = validadoParaQR,

                FechaCreacion = emb.FechaCreacion,
                FechaSalida = emb.FechaSalida,
                FechaLlegadaDestino = emb.FechaLlegadaDestino,
                FechaRetrasado = emb.FechaRetrasado,
                FechaEntregado = emb.FechaEntregado,
                FechaDevuelto = emb.FechaDevuelto,

                Ruta = data?.Ruta ?? "Sin información",
                Cliente = data?.Cliente ?? "Sin información",
                Temperatura = data?.Presentacion ?? "Sin información",
                Pedido = pedidos.Any() ? string.Join("||", pedidos) : "",
                FechaEntrega = data?.FechaEntrega,

                Token = validadoParaQR ? emb.QR?.Token : null,
                TipoDocumento = tipoDocumento
            });
        }

        return embarquesConInfo;
    }

    // Reemplaza tu método ActualizarEstatus en EmbarquesController con este: 

    [HttpPost]
    public async Task<IActionResult> ActualizarEstatus([FromBody] ActualizarEstatusRequest request)
    {
        try
        {
            var embarque = await _qrContext.Embarque.FindAsync(request.EmbarqueId);

            if (embarque == null)
                return Json(new { success = false, message = "Embarque no encontrado" });

            // Validar transición permitida 
            bool transicionValida = embarque.Estatus switch
            {
                // En Ruta: puede llegar a destino, marcar incidencia o marcar devuelto 
                2 => request.NuevoEstatus == 3
                  || request.NuevoEstatus == 4
                  || request.NuevoEstatus == 6,

                // En Destino: puede entregarse, devolverse, regresar a ruta o marcar incidencia 
                3 => request.NuevoEstatus == 5
                  || request.NuevoEstatus == 6
                  || request.NuevoEstatus == 2
                  || request.NuevoEstatus == 4,

                // Incidencia: puede volver a ruta, llegar a destino o marcarse devuelto 
                4 => request.NuevoEstatus == 2
                  || request.NuevoEstatus == 3
                  || request.NuevoEstatus == 6,

                // Entregado: solo corrección a En Destino 
                5 => request.NuevoEstatus == 3,

                // Devuelto: puede corregirse a En Ruta o En Destino 
                6 => request.NuevoEstatus == 2
                  || request.NuevoEstatus == 3,

                _ => false
            };

            if (!transicionValida)
            {
                return Json(new { success = false, message = "Transición de estatus no permitida" });
            }

            embarque.Estatus = request.NuevoEstatus;

            // Actualizar fechas según el nuevo estatus 
            switch (request.NuevoEstatus)
            {
                case 2:
                    // Regresa a ruta / reanuda ruta. 
                    embarque.FechaLlegadaDestino = null;
                    embarque.FechaRetrasado = null;
                    embarque.FechaEntregado = null;
                    embarque.FechaDevuelto = null;
                    break;

                case 3:
                    // Llegó a destino. 
                    embarque.FechaEntregado = null;
                    embarque.FechaDevuelto = null;
                    embarque.FechaLlegadaDestino ??= DateTime.Now;
                    break;

                case 4:
                    // Incidencia. 
                    embarque.FechaRetrasado = DateTime.Now;
                    embarque.FechaEntregado = null;
                    embarque.FechaDevuelto = null;
                    break;

                case 5:
                    // Entregado. 
                    embarque.FechaEntregado = DateTime.Now;
                    embarque.FechaDevuelto = null;
                    break;

                case 6:
                    // Devuelto. 
                    embarque.FechaDevuelto = DateTime.Now;
                    embarque.FechaEntregado = null;
                    break;
            }

            await _qrContext.SaveChangesAsync();

            return Json(new { success = true, message = "Estatus actualizado correctamente" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error: {ex.Message}" });
        }
    }

    public async Task<IActionResult> Calidad(
        string? busqueda,
        DateTime? fechaInicio,
        DateTime? fechaFin,

        // Filtros exclusivos para historial
        string? busquedaHistorial,
        DateTime? fechaInicioHistorial,
        DateTime? fechaFinHistorial)
    {
        // ============================================================
        // LISTADO PRINCIPAL: SOLO PENDIENTES DE CALIDAD
        // ============================================================
        var query = _qrContext.Embarque
            .AsNoTracking()
            .Include(e => e.Documentos)
            .Include(e => e.QR)
            .Where(e =>
                (e.Estatus == 1 || e.Estatus == 7) &&
                e.CalidadAprobada != true
            );

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            busqueda = busqueda.Trim();

            query = query.Where(e =>
                e.Consecutivo.Contains(busqueda) ||
                e.Id.ToString().Contains(busqueda) ||
                (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busqueda))
            );
        }

        if (fechaInicio.HasValue)
        {
            var inicio = fechaInicio.Value.Date;
            query = query.Where(e => e.FechaCreacion >= inicio);
        }

        if (fechaFin.HasValue)
        {
            var fin = fechaFin.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(e => e.FechaCreacion <= fin);
        }

        var embarques = await query
            .OrderByDescending(e => e.FechaCreacion)
            .ToListAsync();


        // ============================================================
        // HISTORIAL: FORMULARIOS YA VALIDADOS
        // NO CARGA NADA HASTA QUE TENGA FECHA INICIO Y FECHA FIN
        // ============================================================
        var historialCalidad = new List<Embarque>();

        bool buscoHistorial =
            fechaInicioHistorial.HasValue &&
            fechaFinHistorial.HasValue;

        if (buscoHistorial)
        {
            var inicioHistorial = fechaInicioHistorial.Value.Date;
            var finHistorial = fechaFinHistorial.Value.Date.AddDays(1).AddTicks(-1);

            var queryHistorial = _qrContext.Embarque
                .AsNoTracking()
                .Include(e => e.Documentos)
                .Include(e => e.QR)
                .Where(e => e.CalidadAprobada == true)
                .Where(e =>
                    (e.FechaValidacionCalidad ?? e.FechaCreacion) >= inicioHistorial &&
                    (e.FechaValidacionCalidad ?? e.FechaCreacion) <= finHistorial
                );

            if (!string.IsNullOrWhiteSpace(busquedaHistorial))
            {
                busquedaHistorial = busquedaHistorial.Trim();

                queryHistorial = queryHistorial.Where(e =>
                    e.Consecutivo.Contains(busquedaHistorial) ||
                    e.Id.ToString().Contains(busquedaHistorial) ||
                    (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busquedaHistorial))
                );
            }

            historialCalidad = await queryHistorial
                .OrderByDescending(e => e.FechaValidacionCalidad ?? e.FechaCreacion)
                .ToListAsync();
        }


        // ============================================================
        // DOCUMENTOS PARA PENDIENTES + HISTORIAL
        // ============================================================
        var embarquesDocumentos = new Dictionary<int, object>();

        var todosLosEmbarques = new List<Embarque>();
        todosLosEmbarques.AddRange(embarques);
        todosLosEmbarques.AddRange(historialCalidad);

        foreach (var embarque in todosLosEmbarques)
        {
            var ordenesIds = embarque.Documentos?
                .Where(d => d.TipoDocumento == "OV")
                .Select(d => d.DocumentoId)
                .ToList() ?? new List<int>();

            var transferenciasIds = embarque.Documentos?
                .Where(d => d.TipoDocumento == "TRANSFERENCIA")
                .Select(d => d.DocumentoId)
                .ToList() ?? new List<int>();

            var ordenesReal = await _ovContext.OrdenVenta
                .AsNoTracking()
                .Where(o => ordenesIds.Contains(o.Id))
                .Select(o => new
                {
                    o.Id,
                    o.Consecutivo,
                    o.Ruta,
                    o.FechaEntrega,
                    CodigoCliente = o.Cliente,
                    NombreCliente = _ovContext.ClienteSap
                        .Where(c => c.Cliente == o.Cliente)
                        .Select(c => c.Nombrecliente)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var transferenciasReal = await _ovContext.Transferencias
                .AsNoTracking()
                .Where(t => transferenciasIds.Contains(t.Id))
                .Select(t => new
                {
                    t.Id,
                    t.Consecutivo,
                    t.Sucursal,
                    t.FechaSolicitud,
                    t.UsuarioSolicita
                })
                .ToListAsync();

            embarquesDocumentos[embarque.Id] = new
            {
                OrdenesVenta = ordenesReal,
                Transferencias = transferenciasReal
            };
        }

        // ============================================================
        // PRODUCTOS / SKUS PARA CAPTURA DE TEMPERATURA POR EMBARQUE
        // ============================================================
        var productosTemperaturaCalidad =
            new Dictionary<int, List<EmbarqueProductoTemperaturaItemVm>>();

        foreach (var embarque in todosLosEmbarques)
        {
            productosTemperaturaCalidad[embarque.Id] =
                await ConstruirProductosTemperaturaCalidad(embarque.Id);
        }

        ViewBag.ProductosTemperaturaCalidad = productosTemperaturaCalidad;


        // ============================================================
        // FOTOS DE CALIDAD PARA HISTORIAL
        // ============================================================
        var fotosCalidadHistorial = new Dictionary<int, List<Embarque.EmbarqueCalidadFoto>>();

        if (historialCalidad.Any())
        {
            var idsHistorial = historialCalidad.Select(x => x.Id).ToList();

            var fotos = await _qrContext.Set<Embarque.EmbarqueCalidadFoto>()
                .AsNoTracking()
                .Where(f => idsHistorial.Contains(f.EmbarqueId))
                .OrderByDescending(f => f.FechaRegistro)
                .ToListAsync();

            fotosCalidadHistorial = fotos
                .GroupBy(f => f.EmbarqueId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }


        // Pendientes
        ViewBag.EmbarquesDocumentos = embarquesDocumentos;

        // Historial
        ViewBag.HistorialCalidad = historialCalidad;
        ViewBag.BuscoHistorialCalidad = buscoHistorial;
        ViewBag.BusquedaHistorialCalidad = busquedaHistorial;
        ViewBag.FechaInicioHistorialCalidad = fechaInicioHistorial?.ToString("yyyy-MM-dd");
        ViewBag.FechaFinHistorialCalidad = fechaFinHistorial?.ToString("yyyy-MM-dd");
        ViewBag.FotosCalidadHistorial = fotosCalidadHistorial;

        return View(embarques);
    }

    [HttpPost]
    public async Task<IActionResult> RegistrarCalidad(
        int id,
        string salidaTipo,
        string? placaTransporte,
        decimal? temperaturaProgramacion,
        string estadoUnidad,
        DateTime? horaInicioEmbarque,
        DateTime? horaTerminoEmbarque,
        decimal? temperaturaUnidadInicio,
        decimal? temperaturaUnidadTermino,
        string estadoProductos,
        string? codigoTermograficador,
        string? numeroTermometro,
        string? accionesCorrectivasCalidad,
        string? observacionesCalidad,
        List<IFormFile>? fotosCalidad)
    {
        var embarque = await _qrContext.Embarque
            .Include(e => e.QR)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (embarque == null)
        {
            TempData["Error"] = "El embarque no existe.";
            return RedirectToAction("Calidad");
        }

        if (embarque.CalidadAprobada == true)
        {
            TempData["Error"] = "Este embarque ya fue validado en calidad.";
            return RedirectToAction("Calidad");
        }

        embarque.SalidaTipo = salidaTipo;
        embarque.PlacaTransporte = placaTransporte;
        embarque.TemperaturaProgramacion = temperaturaProgramacion;
        embarque.EstadoUnidadCalidad = estadoUnidad;
        embarque.HoraInicioEmbarque = horaInicioEmbarque;
        embarque.HoraTerminoEmbarque = horaTerminoEmbarque;
        embarque.TemperaturaUnidadInicio = temperaturaUnidadInicio;
        embarque.TemperaturaUnidadTermino = temperaturaUnidadTermino;
        embarque.TemperaturaUnidadCalidad = temperaturaUnidadTermino ?? temperaturaUnidadInicio;
        embarque.EstadoProductosCalidad = estadoProductos;
        embarque.CodigoTermograficador = codigoTermograficador;
        embarque.NumeroTermometro = numeroTermometro;
        embarque.AccionesCorrectivasCalidad = accionesCorrectivasCalidad;
        embarque.ObservacionesCalidad = observacionesCalidad;

        embarque.FechaValidacionCalidad = DateTime.Now;
        embarque.UsuarioValidaCalidad = User.Identity?.Name ?? "Sistema";
        embarque.CalidadAprobada = true;

        // SOLO se libera si ya están aprobadas las 3 validaciones:
        // 1. Calidad operativa
        // 2. Documentación logística
        // 3. Documentación de calidad
        embarque.Estatus =
            embarque.CalidadAprobada == true &&
            embarque.DocumentacionAprobada == true &&
            embarque.DocumentacionCalidadAprobada == true
                ? 7
                : 1;

        var productosTemperatura = await ConstruirProductosTemperaturaCalidad(id);

        if (productosTemperatura.Any())
        {
            var faltantes = productosTemperatura
                .Where(x => !x.Temperatura.HasValue)
                .ToList();

            if (faltantes.Any())
            {
                TempData["Error"] =
                    $"No se puede validar Calidad del embarque #{embarque.Consecutivo}. " +
                    $"Faltan temperaturas por capturar en {faltantes.Count} SKU(s).";

                return RedirectToAction("Calidad");
            }
        }

        if (fotosCalidad != null && fotosCalidad.Any())
        {
            var carpetaDestino = Path.Combine(_environment.WebRootPath, "uploads", "calidad");

            if (!Directory.Exists(carpetaDestino))
                Directory.CreateDirectory(carpetaDestino);

            foreach (var foto in fotosCalidad.Where(f => f.Length > 0))
            {
                var extension = Path.GetExtension(foto.FileName);
                var nombreArchivo = $"calidad_{embarque.Id}_{Guid.NewGuid():N}{extension}";
                var rutaFisica = Path.Combine(carpetaDestino, nombreArchivo);

                using var stream = new FileStream(rutaFisica, FileMode.Create);
                await foto.CopyToAsync(stream);

                _qrContext.Set<Embarque.EmbarqueCalidadFoto>().Add(new Embarque.EmbarqueCalidadFoto
                {
                    EmbarqueId = embarque.Id,
                    RutaArchivo = $"/uploads/calidad/{nombreArchivo}",
                    FechaRegistro = DateTime.Now,
                    UsuarioRegistro = User.Identity?.Name ?? "Sistema"
                });
            }
        }

        await _qrContext.SaveChangesAsync();

        TempData["Success"] =
            embarque.DocumentacionAprobada == true &&
            embarque.DocumentacionCalidadAprobada == true
                ? $"Calidad validó correctamente el embarque #{embarque.Consecutivo}. Ya puede generar QR."
                : $"Calidad validó correctamente el embarque #{embarque.Consecutivo}. Aún faltan validaciones de documentación.";

        return RedirectToAction("Calidad");
    }

    public async Task<IActionResult> MapaCarga(int id)
    {
        var embarque = await _qrContext.Embarque
            .Include(e => e.Documentos)
            .Include(e => e.QR)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (embarque == null)
            return NotFound();

        var ordenesIds = embarque.Documentos?
            .Where(d => d.TipoDocumento == "OV")
            .Select(d => d.DocumentoId)
            .ToList() ?? new List<int>();

        var transferenciasIds = embarque.Documentos?
            .Where(d => d.TipoDocumento == "TRANSFERENCIA")
            .Select(d => d.DocumentoId)
            .ToList() ?? new List<int>();

        // ============================================================
        // ÓRDENES DE VENTA PARA MAPA
        // IMPORTANTE:
        // El embarque guarda DocumentoId = OrdenVenta.Id.
        // Por eso aquí buscamos PedidoVenta.OrdenVentaId.
        // ============================================================
        var pedidosVenta = await _ovContext.PedidoVenta
            .AsNoTracking()
            .Include(p => p.Productos)
            .Where(p => ordenesIds.Contains(p.OrdenVentaId))
            .ToListAsync();

        // ============================================================
        // RESOLVER CLIENTES SAP PARA MAPA DE CARGA
        // PedidoVenta.Cliente puede venir como código.
        // Aquí lo convertimos a nombre real usando ClienteSap.
        // ============================================================
        var clientesCodigos = pedidosVenta
            .Select(p => p.Cliente)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Distinct()
            .ToList();

        var clientesSap = await _ovContext.ClienteSap
            .AsNoTracking()
            .Where(c => clientesCodigos.Contains(c.Cliente))
            .Select(c => new
            {
                Codigo = c.Cliente,
                Nombre = c.Nombrecliente
            })
            .ToListAsync();

        var clientesSapDic = clientesSap
            .GroupBy(c => c.Codigo)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Nombre).FirstOrDefault() ?? g.Key
            );

        var ordenesMapa = pedidosVenta
            .Select(p =>
            {
                var clienteCodigo = p.Cliente?.Trim() ?? "";
                var clienteNombre = clientesSapDic.TryGetValue(clienteCodigo, out var nombreSap)
                    ? nombreSap
                    : clienteCodigo;

                return new
                {
                    // Mantener este Id como OV-{OrdenVentaId}
                    // para no romper el orden guardado del mapa.
                    Id = p.OrdenVentaId,

                    PedidoVentaId = p.Id,
                    Consecutivo = p.OrdenVentaConsecutivo,
                    Ruta = p.AlmacenSurtir,

                    // Código original por si después lo ocupas
                    ClienteCodigo = clienteCodigo,

                    // Este es el que ya consume la vista como nombre
                    Cliente = string.IsNullOrWhiteSpace(clienteNombre)
                        ? p.OrdenVentaConsecutivo
                        : clienteNombre,

                    p.FechaEntrega,
                    p.Vendedor,
                    p.ObservacionGestion,

                    Kilos = p.Productos.Sum(d => d.KilosCaja),
                    Cajas = p.Productos.Sum(d => d.Cajas),

                    Productos = p.Productos
                    .Select(d => d.ProductoCodigo)
                    .Distinct()
                    .Count(),

                     Detalles = p.Productos
                    .OrderBy(d => d.Id)
                    .Select(d => new
                    {
                        ProductoCodigo = d.ProductoCodigo,
                        ProductoNombre = d.ProductoNombre,
                        Cajas = d.Cajas,

                        // OV: KilosCaja ya viene como peso correcto, NO multiplicar por cajas
                        Kilos = d.KilosCaja,

                        Almacen = d.Almacen,
                        KilosCaja = d.KilosCaja,
                        Precio = d.Precio
                    })
                    .ToList()
                };
            })
            .ToList();

        // ============================================================
        // TRANSFERENCIAS USANDO PedidosTransferencia + Detalles
        // ESTO SE QUEDA COMO YA LO TENÍAS
        // ============================================================
        var transferenciasPedido = await _ovContext.PedidosTransferencia
            .AsNoTracking()
            .Include(p => p.Detalles)
            .Where(p => transferenciasIds.Contains(p.TransferenciaId))
            .ToListAsync();

        var transferenciasMapa = transferenciasPedido
            .Select(t => new
            {
                Id = t.TransferenciaId,
                PedidoTransferenciaId = t.Id,
                t.Consecutivo,
                Ruta = t.Destino,
                Cliente = t.Destino,
                t.FechaSolicitud,
                t.Observacion,
                t.UsuarioSolicita,

                Kilos = t.Detalles.Sum(d => d.CantidadKg),
                Cajas = t.Detalles.Sum(d => d.Cajas),

                Productos = t.Detalles
                    .Select(d => d.ProductoCodigo)
                    .Distinct()
                    .Count(),

                Detalles = t.Detalles
                    .OrderBy(d => d.Orden)
                    .ThenBy(d => d.Id)
                    .Select(d => new
                    {
                        d.Id,
                        d.TransferenciaDetalleIdOriginal,
                        d.ProductoCodigo,
                        Cajas = d.Cajas,
                        Kilos = d.CantidadKg,
                        d.Orden
                    })
                    .ToList()
            })
            .ToList();

        ViewBag.OrdenesMapa = ordenesMapa;
        ViewBag.TransferenciasMapa = transferenciasMapa;
        ViewBag.MapaCargaOrdenJson = embarque.MapaCargaOrdenJson ?? "";

        return View(embarque);
    }

    [HttpPost]
    public async Task<IActionResult> GuardarOrdenMapaCarga([FromBody] GuardarOrdenMapaCargaRequest request)
    {
        if (request == null || request.EmbarqueId <= 0 || request.Orden == null)
        {
            return Json(new
            {
                success = false,
                message = "Datos inválidos para guardar el orden."
            });
        }

        var embarque = await _qrContext.Embarque
            .FirstOrDefaultAsync(e => e.Id == request.EmbarqueId);

        if (embarque == null)
        {
            return Json(new
            {
                success = false,
                message = "Embarque no encontrado."
            });
        }

        // Limpia IDs vacíos o duplicados
        var ordenLimpio = request.Orden
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct()
            .ToList();

        embarque.MapaCargaOrdenJson = System.Text.Json.JsonSerializer.Serialize(ordenLimpio);

        await _qrContext.SaveChangesAsync();

        return Json(new
        {
            success = true,
            message = "Orden del mapa guardado correctamente."
        });
    }

    public async Task<IActionResult> Documentacion(
        string? busqueda,
        DateTime? fechaInicio,
        DateTime? fechaFin,

        // Filtros exclusivos para historial
        string? busquedaHistorial,
        DateTime? fechaInicioHistorial,
        DateTime? fechaFinHistorial)
    {
        // ============================================================
        // LISTADO PRINCIPAL: SOLO PENDIENTES DE DOCUMENTACIÓN LOGÍSTICA
        // ============================================================
        var query = _qrContext.Embarque
            .AsNoTracking()
            .Include(e => e.Documentos)
            .Include(e => e.Archivos)
            .Include(e => e.QR)
            .Where(e =>
                (e.Estatus == 1 || e.Estatus == 7) &&
                e.DocumentacionAprobada != true
            );

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            busqueda = busqueda.Trim();

            query = query.Where(e =>
                e.Consecutivo.Contains(busqueda) ||
                e.Id.ToString().Contains(busqueda) ||
                (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busqueda))
            );
        }

        if (fechaInicio.HasValue)
        {
            var inicio = fechaInicio.Value.Date;
            query = query.Where(e => e.FechaCreacion >= inicio);
        }

        if (fechaFin.HasValue)
        {
            var fin = fechaFin.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(e => e.FechaCreacion <= fin);
        }

        var embarques = await query
            .OrderByDescending(e => e.FechaCreacion)
            .ToListAsync();


        // ============================================================
        // HISTORIAL: DOCUMENTACIÓN LOGÍSTICA YA VALIDADA
        // NO CARGA NADA HASTA QUE TENGA FECHA INICIO Y FECHA FIN
        // ============================================================
        var historialDocumentacion = new List<Embarque>();

        bool buscoHistorial =
            fechaInicioHistorial.HasValue &&
            fechaFinHistorial.HasValue;

        if (buscoHistorial)
        {
            var inicioHistorial = fechaInicioHistorial.Value.Date;
            var finHistorial = fechaFinHistorial.Value.Date.AddDays(1).AddTicks(-1);

            var queryHistorial = _qrContext.Embarque
                .AsNoTracking()
                .Include(e => e.Documentos)
                .Include(e => e.Archivos)
                .Include(e => e.QR)
                .Where(e => e.DocumentacionAprobada == true)
                .Where(e =>
                    (e.FechaValidacionDocumentacion ?? e.FechaCreacion) >= inicioHistorial &&
                    (e.FechaValidacionDocumentacion ?? e.FechaCreacion) <= finHistorial
                );

            if (!string.IsNullOrWhiteSpace(busquedaHistorial))
            {
                busquedaHistorial = busquedaHistorial.Trim();

                queryHistorial = queryHistorial.Where(e =>
                    e.Consecutivo.Contains(busquedaHistorial) ||
                    e.Id.ToString().Contains(busquedaHistorial) ||
                    (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busquedaHistorial))
                );
            }

            historialDocumentacion = await queryHistorial
                .OrderByDescending(e => e.FechaValidacionDocumentacion ?? e.FechaCreacion)
                .ToListAsync();
        }


        // ============================================================
        // DOCUMENTOS PARA PENDIENTES + HISTORIAL
        // ============================================================
        var embarquesDocumentos = new Dictionary<int, object>();

        var todosLosEmbarques = new List<Embarque>();
        todosLosEmbarques.AddRange(embarques);
        todosLosEmbarques.AddRange(historialDocumentacion);

        foreach (var embarque in todosLosEmbarques)
        {
            var ordenesIds = embarque.Documentos?
                .Where(d => d.TipoDocumento == "OV")
                .Select(d => d.DocumentoId)
                .ToList() ?? new List<int>();

            var transferenciasIds = embarque.Documentos?
                .Where(d => d.TipoDocumento == "TRANSFERENCIA")
                .Select(d => d.DocumentoId)
                .ToList() ?? new List<int>();

            var ordenesReal = await _ovContext.OrdenVenta
                .AsNoTracking()
                .Where(o => ordenesIds.Contains(o.Id))
                .Select(o => new
                {
                    o.Id,
                    o.Consecutivo,
                    o.Ruta,
                    CodigoCliente = o.Cliente,
                    NombreCliente = _ovContext.ClienteSap
                        .Where(c => c.Cliente == o.Cliente)
                        .Select(c => c.Nombrecliente)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var transferenciasReal = await _ovContext.Transferencias
                .AsNoTracking()
                .Where(t => transferenciasIds.Contains(t.Id))
                .Select(t => new
                {
                    t.Id,
                    t.Consecutivo,
                    t.Sucursal,
                    t.FechaSolicitud,
                    t.UsuarioSolicita
                })
                .ToListAsync();

            embarquesDocumentos[embarque.Id] = new
            {
                OrdenesVenta = ordenesReal,
                Transferencias = transferenciasReal
            };
        }

        ViewBag.EmbarquesDocumentos = embarquesDocumentos;

        ViewBag.HistorialDocumentacion = historialDocumentacion;
        ViewBag.BuscoHistorialDocumentacion = buscoHistorial;
        ViewBag.BusquedaHistorialDocumentacion = busquedaHistorial;
        ViewBag.FechaInicioHistorialDocumentacion = fechaInicioHistorial?.ToString("yyyy-MM-dd");
        ViewBag.FechaFinHistorialDocumentacion = fechaFinHistorial?.ToString("yyyy-MM-dd");

        return View(embarques);
    }

    [HttpPost]
    public async Task<IActionResult> CargarDocumentos(
     int id,
     bool? requiereCartaPorte,
     List<IFormFile>? cartaPorteArchivos,
     List<IFormFile>? fichaTecnicaArchivos,
     List<IFormFile>? cartaGarantiaArchivos,

     // NUEVOS OPCIONALES
     List<IFormFile>? certificacionLavadoArchivos,
     List<IFormFile>? certificacionFumigacionArchivos)
    {
        var embarque = await _qrContext.Embarque
            .Include(e => e.QR)
            .Include(e => e.Archivos)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (embarque == null)
        {
            TempData["Error"] = "El embarque no existe.";
            return RedirectToAction("Documentacion");
        }

        if (embarque.QR != null)
        {
            TempData["Error"] = $"No se puede modificar la documentación del embarque #{embarque.Consecutivo} porque ya tiene QR generado.";
            return RedirectToAction("Documentacion");
        }

        if (requiereCartaPorte == null)
        {
            TempData["Error"] = $"Debes indicar si el embarque #{embarque.Consecutivo} requiere Carta Porte o si no aplica.";
            return RedirectToAction("Documentacion");
        }

        // Guardamos la decisión de logística:
        // true = requiere Carta Porte
        // false = no aplica
        embarque.RequiereCartaPorte = requiereCartaPorte.Value;

        bool seCargoAlgunArchivoNuevo = false;

        // =========================================================
        // 1. GUARDAR MÚLTIPLES ARCHIVOS DE CARTA PORTE
        // =========================================================
        if (cartaPorteArchivos != null && cartaPorteArchivos.Any(f => f.Length > 0))
        {
            foreach (var archivo in cartaPorteArchivos.Where(f => f.Length > 0))
            {
                var ruta = await GuardarArchivo(archivo, "cartasPorte", "carta_porte");

                _qrContext.Set<EmbarqueArchivo>().Add(new EmbarqueArchivo
                {
                    EmbarqueId = embarque.Id,
                    Tipo = "CARTA_PORTE",
                    RutaArchivo = ruta,
                    FechaRegistro = DateTime.Now,
                    UsuarioRegistro = User.Identity?.Name ?? "Sistema"
                });

                seCargoAlgunArchivoNuevo = true;
            }
        }

        // =========================================================
        // 2. GUARDAR MÚLTIPLES ARCHIVOS DE FICHA TÉCNICA
        // Se deja por compatibilidad aunque actualmente no lo estés mostrando.
        // =========================================================
        if (fichaTecnicaArchivos != null && fichaTecnicaArchivos.Any(f => f.Length > 0))
        {
            foreach (var archivo in fichaTecnicaArchivos.Where(f => f.Length > 0))
            {
                var ruta = await GuardarArchivo(archivo, "fichasTecnicas", "ficha_tecnica");

                _qrContext.Set<EmbarqueArchivo>().Add(new EmbarqueArchivo
                {
                    EmbarqueId = embarque.Id,
                    Tipo = "FICHA_TECNICA",
                    RutaArchivo = ruta,
                    FechaRegistro = DateTime.Now,
                    UsuarioRegistro = User.Identity?.Name ?? "Sistema"
                });

                seCargoAlgunArchivoNuevo = true;
            }
        }

        // =========================================================
        // 3. GUARDAR MÚLTIPLES ARCHIVOS DE CARTA GARANTÍA
        // Se deja por compatibilidad aunque actualmente no lo estés mostrando.
        // =========================================================
        if (cartaGarantiaArchivos != null && cartaGarantiaArchivos.Any(f => f.Length > 0))
        {
            foreach (var archivo in cartaGarantiaArchivos.Where(f => f.Length > 0))
            {
                var ruta = await GuardarArchivo(archivo, "cartasGarantia", "carta_garantia");

                _qrContext.Set<EmbarqueArchivo>().Add(new EmbarqueArchivo
                {
                    EmbarqueId = embarque.Id,
                    Tipo = "CARTA_GARANTIA",
                    RutaArchivo = ruta,
                    FechaRegistro = DateTime.Now,
                    UsuarioRegistro = User.Identity?.Name ?? "Sistema"
                });

                seCargoAlgunArchivoNuevo = true;
            }
        }

        // =========================================================
        // 4. GUARDAR CERTIFICACIÓN DE LAVADO - OPCIONAL
        // =========================================================
        if (certificacionLavadoArchivos != null && certificacionLavadoArchivos.Any(f => f.Length > 0))
        {
            foreach (var archivo in certificacionLavadoArchivos.Where(f => f.Length > 0))
            {
                var ruta = await GuardarArchivo(archivo, "certificacionesLavado", "certificacion_lavado");

                _qrContext.Set<EmbarqueArchivo>().Add(new EmbarqueArchivo
                {
                    EmbarqueId = embarque.Id,
                    Tipo = "CERTIFICACION_LAVADO",
                    RutaArchivo = ruta,
                    FechaRegistro = DateTime.Now,
                    UsuarioRegistro = User.Identity?.Name ?? "Sistema"
                });

                seCargoAlgunArchivoNuevo = true;
            }
        }

        // =========================================================
        // 5. GUARDAR CERTIFICACIÓN DE FUMIGACIÓN - OPCIONAL
        // =========================================================
        if (certificacionFumigacionArchivos != null && certificacionFumigacionArchivos.Any(f => f.Length > 0))
        {
            foreach (var archivo in certificacionFumigacionArchivos.Where(f => f.Length > 0))
            {
                var ruta = await GuardarArchivo(archivo, "certificacionesFumigacion", "certificacion_fumigacion");

                _qrContext.Set<EmbarqueArchivo>().Add(new EmbarqueArchivo
                {
                    EmbarqueId = embarque.Id,
                    Tipo = "CERTIFICACION_FUMIGACION",
                    RutaArchivo = ruta,
                    FechaRegistro = DateTime.Now,
                    UsuarioRegistro = User.Identity?.Name ?? "Sistema"
                });

                seCargoAlgunArchivoNuevo = true;
            }
        }

        await _qrContext.SaveChangesAsync();

        // =========================================================
        // VALIDACIÓN CARTA PORTE
        // Esta se queda como ya estaba:
        // solo es obligatoria si logística indicó que aplica.
        // =========================================================
        bool tieneCartaPorte = await _qrContext.Set<EmbarqueArchivo>()
            .AnyAsync(a => a.EmbarqueId == embarque.Id && a.Tipo == "CARTA_PORTE");

        if (embarque.RequiereCartaPorte == true && !tieneCartaPorte)
        {
            embarque.DocumentacionAprobada = false;
            embarque.FechaValidacionDocumentacion = null;
            embarque.UsuarioValidaDocumentacion = null;

            await _qrContext.SaveChangesAsync();

            TempData["Error"] = $"El embarque #{embarque.Consecutivo} requiere Carta Porte porque logística indicó que aplica. Debes cargarla para validar.";
            return RedirectToAction("Documentacion");
        }

        // =========================================================
        // VALIDACIÓN FINAL LOGÍSTICA
        // Lavado y Fumigación NO bloquean, solo se avisan en frontend.
        // =========================================================
        embarque.DocumentacionAprobada = true;
        embarque.FechaValidacionDocumentacion = DateTime.Now;
        embarque.UsuarioValidaDocumentacion = User.Identity?.Name ?? "Sistema";

        embarque.Estatus =
            embarque.CalidadAprobada == true &&
            embarque.DocumentacionAprobada == true &&
            embarque.DocumentacionCalidadAprobada == true
                ? 7
                : 1;

        await _qrContext.SaveChangesAsync();

        bool tieneLavado = await _qrContext.Set<EmbarqueArchivo>()
            .AnyAsync(a => a.EmbarqueId == embarque.Id && a.Tipo == "CERTIFICACION_LAVADO");

        bool tieneFumigacion = await _qrContext.Set<EmbarqueArchivo>()
            .AnyAsync(a => a.EmbarqueId == embarque.Id && a.Tipo == "CERTIFICACION_FUMIGACION");

        string textoCartaPorte = embarque.RequiereCartaPorte == true
            ? "Carta Porte requerida y validada."
            : "Carta Porte marcada como No aplica.";

        string textoCertificaciones =
            tieneLavado && tieneFumigacion
                ? "Certificaciones de Lavado y Fumigación cargadas."
                : !tieneLavado && !tieneFumigacion
                    ? "Se validó sin Certificación de Lavado ni Certificación de Fumigación."
                    : !tieneLavado
                        ? "Se validó sin Certificación de Lavado."
                        : "Se validó sin Certificación de Fumigación.";

        TempData["Success"] =
            embarque.CalidadAprobada == true &&
            embarque.DocumentacionCalidadAprobada == true
                ? $"Documentación logística validada correctamente para el embarque #{embarque.Consecutivo}. {textoCartaPorte} {textoCertificaciones} Ya puede generar QR."
                : $"Documentación logística validada correctamente para el embarque #{embarque.Consecutivo}. {textoCartaPorte} {textoCertificaciones} Aún faltan otras validaciones.";

        return RedirectToAction("Documentacion");
    }

    private async Task<string> GuardarArchivo(IFormFile archivo, string carpeta, string prefijo)
    {
        var carpetaDestino = Path.Combine(_environment.WebRootPath, "uploads", carpeta);

        if (!Directory.Exists(carpetaDestino))
        {
            Directory.CreateDirectory(carpetaDestino);
        }

        var extension = Path.GetExtension(archivo.FileName);
        var nombreGenerado = $"{prefijo}_{DateTime.Now:yyyyMMddHHmmssfff}{extension}";
        var rutaFisica = Path.Combine(carpetaDestino, nombreGenerado);

        using (var stream = new FileStream(rutaFisica, FileMode.Create))
        {
            await archivo.CopyToAsync(stream);
        }

        return $"/uploads/{carpeta}/{nombreGenerado}";
    }


    public async Task<IActionResult> TableroAeropuerto()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> TableroAeropuertoData()
    {
        // Tiempo que un embarque entregado seguirá visible en el tablero
        const int horasVisiblesEntregado = 2;

        var limiteEntregados = DateTime.Now.AddHours(-horasVisiblesEntregado);

        var embarques = await _qrContext.Embarque
            .AsNoTracking()
            .Include(e => e.Documentos)
            .Include(e => e.QR)
            .Where(e =>
                // Activos / seguimiento normal
                e.Estatus == 1 ||
                e.Estatus == 7 ||
                e.Estatus == 2 ||
                e.Estatus == 3 ||
                e.Estatus == 4 ||
                e.Estatus == 6 ||

                // Entregados: solo visibles durante 2 horas después de entregarse
                (
                    e.Estatus == 5 &&
                    e.FechaEntregado != null &&
                    e.FechaEntregado >= limiteEntregados
                )
            )
            // Prioriza embarques activos sobre entregados recientes
            .OrderBy(e => e.Estatus == 5 ? 1 : 0)
            .ThenByDescending(e =>
                e.Estatus == 5
                    ? (e.FechaEntregado ?? e.FechaCreacion)
                    : (e.FechaSalida ?? e.FechaCreacion)
            )
            .Take(80)
            .ToListAsync();

        var resultado = new List<object>();

        foreach (var emb in embarques)
        {
            var ordenesIds = emb.Documentos
                .Where(d => d.TipoDocumento == "OV")
                .Select(d => d.DocumentoId)
                .ToList();

            var transferenciasIds = emb.Documentos
                .Where(d => d.TipoDocumento == "TRANSFERENCIA")
                .Select(d => d.DocumentoId)
                .ToList();

            ControlCenterDocumentoInfo? orden = null;
            ControlCenterDocumentoInfo? transferencia = null;

            // ORDEN
            if (ordenesIds.Any())
            {
                orden = await _ovContext.OrdenVenta
                    .AsNoTracking()
                    .Where(o => ordenesIds.Contains(o.Id))
                    .Select(o => new ControlCenterDocumentoInfo
                    {
                        Pedido = o.Consecutivo,
                        Ruta = o.Ruta,
                        Cliente = _ovContext.ClienteSap
                            .Where(c => c.Cliente == o.Cliente)
                            .Select(c => c.Nombrecliente)
                            .FirstOrDefault() ?? o.Cliente,
                        FechaEntrega = o.FechaEntrega
                    })
                    .FirstOrDefaultAsync();
            }

            // TRANSFERENCIA
            if (transferenciasIds.Any())
            {
                transferencia = await _ovContext.Transferencias
                    .AsNoTracking()
                    .Where(t => transferenciasIds.Contains(t.Id))
                    .Select(t => new ControlCenterDocumentoInfo
                    {
                        Pedido = t.Consecutivo,
                        Ruta = t.Sucursal,
                        Cliente = t.Sucursal,
                        FechaEntrega = t.FechaSolicitud
                    })
                    .FirstOrDefaultAsync();
            }

            var data = orden ?? transferencia;

            var ovCount = emb.Documentos.Count(d => d.TipoDocumento == "OV");
            var trCount = emb.Documentos.Count(d => d.TipoDocumento == "TRANSFERENCIA");

            bool validadoParaQR =
                emb.CalidadAprobada == true &&
                emb.DocumentacionAprobada == true &&
                emb.DocumentacionCalidadAprobada == true;

            int estatusVisual =
                emb.Estatus == 7 && !validadoParaQR
                    ? 1
                    : emb.Estatus;

            resultado.Add(new
            {
                id = emb.Id,
                consecutivo = emb.Consecutivo,
                fechaCreacion = emb.FechaCreacion.ToString("dd/MM/yyyy HH:mm"),
                fechaSalida = emb.FechaSalida?.ToString("dd/MM/yyyy HH:mm") ?? "",
                fechaDestino = emb.FechaLlegadaDestino?.ToString("dd/MM/yyyy HH:mm") ?? "",
                fechaEntregado = emb.FechaEntregado?.ToString("dd/MM/yyyy HH:mm") ?? "",
                fechaDevuelto = emb.FechaDevuelto?.ToString("dd/MM/yyyy HH:mm") ?? "",

                ruta = data?.Ruta ?? "Sin ruta",
                cliente = data?.Cliente ?? "Sin cliente",
                pedido = data?.Pedido ?? "N/A",
                fechaEntrega = data?.FechaEntrega?.ToString("dd/MM/yyyy") ?? "",

                estatus = estatusVisual,
                estatusTexto = ObtenerTextoEstatusTablero(estatusVisual),
                estatusTipo = ObtenerTipoEstatusTablero(estatusVisual),

                calidad = emb.CalidadAprobada == true ? "OK" : "PENDIENTE",
                documentacion = emb.DocumentacionAprobada == true ? "OK" : "PENDIENTE",
                documentacionCalidad = emb.DocumentacionCalidadAprobada == true ? "OK" : "PENDIENTE",

                placa = emb.PlacaTransporte ?? "",
                salidaTipo = emb.SalidaTipo ?? "",
                temperatura = emb.TemperaturaUnidadCalidad?.ToString("0.##") ?? "",

                tipoDocumento = ovCount > 0 && trCount > 0
                    ? "MIXTO"
                    : ovCount > 0
                        ? "OV"
                        : trCount > 0
                            ? "TR"
                            : "N/A",

                totalOV = ovCount,
                totalTR = trCount,
                token = validadoParaQR ? emb.QR?.Token ?? "" : ""
            });
        }

        return Json(resultado);
    }

    public async Task<IActionResult> DocumentacionCalidad(
    string? busqueda,
    DateTime? fechaInicio,
    DateTime? fechaFin,

    // Filtros exclusivos para historial
    string? busquedaHistorial,
    DateTime? fechaInicioHistorial,
    DateTime? fechaFinHistorial)
    {
        // ============================================================
        // LISTADO PRINCIPAL: SOLO PENDIENTES DE DOCUMENTACIÓN DE CALIDAD
        // ============================================================
        var query = _qrContext.Embarque
            .AsNoTracking()
            .Include(e => e.Documentos)
            .Include(e => e.Archivos)
            .Include(e => e.QR)
            .Where(e =>
                (e.Estatus == 1 || e.Estatus == 7) &&
                e.DocumentacionCalidadAprobada != true
            );

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            busqueda = busqueda.Trim();

            query = query.Where(e =>
                e.Consecutivo.Contains(busqueda) ||
                e.Id.ToString().Contains(busqueda) ||
                (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busqueda))
            );
        }

        if (fechaInicio.HasValue)
        {
            var inicio = fechaInicio.Value.Date;
            query = query.Where(e => e.FechaCreacion >= inicio);
        }

        if (fechaFin.HasValue)
        {
            var fin = fechaFin.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(e => e.FechaCreacion <= fin);
        }

        var embarques = await query
            .OrderByDescending(e => e.FechaCreacion)
            .ToListAsync();


        // ============================================================
        // HISTORIAL: DOCUMENTACIÓN DE CALIDAD YA VALIDADA
        // NO CARGA NADA HASTA QUE TENGA FECHA INICIO Y FECHA FIN
        // ============================================================
        var historialDocumentacionCalidad = new List<Embarque>();

        bool buscoHistorial =
            fechaInicioHistorial.HasValue &&
            fechaFinHistorial.HasValue;

        if (buscoHistorial)
        {
            var inicioHistorial = fechaInicioHistorial.Value.Date;
            var finHistorial = fechaFinHistorial.Value.Date.AddDays(1).AddTicks(-1);

            var queryHistorial = _qrContext.Embarque
                .AsNoTracking()
                .Include(e => e.Documentos)
                .Include(e => e.Archivos)
                .Include(e => e.QR)
                .Where(e => e.DocumentacionCalidadAprobada == true)
                .Where(e =>
                    (e.FechaValidacionDocumentacionCalidad ?? e.FechaCreacion) >= inicioHistorial &&
                    (e.FechaValidacionDocumentacionCalidad ?? e.FechaCreacion) <= finHistorial
                );

            if (!string.IsNullOrWhiteSpace(busquedaHistorial))
            {
                busquedaHistorial = busquedaHistorial.Trim();

                queryHistorial = queryHistorial.Where(e =>
                    e.Consecutivo.Contains(busquedaHistorial) ||
                    e.Id.ToString().Contains(busquedaHistorial) ||
                    (e.NombreEmbarque != null && e.NombreEmbarque.Contains(busquedaHistorial))
                );
            }

            historialDocumentacionCalidad = await queryHistorial
                .OrderByDescending(e => e.FechaValidacionDocumentacionCalidad ?? e.FechaCreacion)
                .ToListAsync();
        }


        // ============================================================
        // DOCUMENTOS PARA PENDIENTES + HISTORIAL
        // ============================================================
        var embarquesDocumentos = new Dictionary<int, object>();

        var todosLosEmbarques = new List<Embarque>();
        todosLosEmbarques.AddRange(embarques);
        todosLosEmbarques.AddRange(historialDocumentacionCalidad);

        foreach (var embarque in todosLosEmbarques)
        {
            var ordenesIds = embarque.Documentos?
                .Where(d => d.TipoDocumento == "OV")
                .Select(d => d.DocumentoId)
                .ToList() ?? new List<int>();

            var transferenciasIds = embarque.Documentos?
                .Where(d => d.TipoDocumento == "TRANSFERENCIA")
                .Select(d => d.DocumentoId)
                .ToList() ?? new List<int>();

            var ordenesReal = await _ovContext.OrdenVenta
                .AsNoTracking()
                .Where(o => ordenesIds.Contains(o.Id))
                .Select(o => new
                {
                    o.Id,
                    o.Consecutivo,
                    o.Ruta,
                    CodigoCliente = o.Cliente,
                    NombreCliente = _ovContext.ClienteSap
                        .Where(c => c.Cliente == o.Cliente)
                        .Select(c => c.Nombrecliente)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var transferenciasReal = await _ovContext.Transferencias
                .AsNoTracking()
                .Where(t => transferenciasIds.Contains(t.Id))
                .Select(t => new
                {
                    t.Id,
                    t.Consecutivo,
                    t.Sucursal,
                    t.FechaSolicitud,
                    t.UsuarioSolicita
                })
                .ToListAsync();

            embarquesDocumentos[embarque.Id] = new
            {
                OrdenesVenta = ordenesReal,
                Transferencias = transferenciasReal
            };
        }

        ViewBag.EmbarquesDocumentos = embarquesDocumentos;

        ViewBag.HistorialDocumentacionCalidad = historialDocumentacionCalidad;
        ViewBag.BuscoHistorialDocumentacionCalidad = buscoHistorial;
        ViewBag.BusquedaHistorialDocumentacionCalidad = busquedaHistorial;
        ViewBag.FechaInicioHistorialDocumentacionCalidad = fechaInicioHistorial?.ToString("yyyy-MM-dd");
        ViewBag.FechaFinHistorialDocumentacionCalidad = fechaFinHistorial?.ToString("yyyy-MM-dd");

        return View(embarques);
    }

    [HttpPost]
    public async Task<IActionResult> CargarDocumentosCalidad(
        int id,
        List<IFormFile>? cartaGarantiaCalidadArchivos,
        List<IFormFile>? cartaLibreClembuterolArchivos,
        List<IFormFile>? cartaLibreResiduosToxicosArchivos,
        List<IFormFile>? cartaEEBArchivos,
        List<IFormFile>? avisoMovilizacionArchivos,
        List<IFormFile>? hojaTrabajoArchivos,

        // NUEVOS
        List<IFormFile>? microbiologicosArchivos,
        List<IFormFile>? fichaTecnicaCalidadArchivos,
        List<IFormFile>? facturaCalidadArchivos,
        List<IFormFile>? romaneosArchivos,
        List<IFormFile>? remisionArchivos)
    {
        var embarque = await _qrContext.Embarque
            .Include(e => e.QR)
            .Include(e => e.Archivos)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (embarque == null)
        {
            TempData["Error"] = "El embarque no existe.";
            return RedirectToAction("DocumentacionCalidad");
        }

        // =========================
        // YA ESTÁN
        // =========================

        await GuardarArchivosPorTipo(
            embarque.Id,
            cartaGarantiaCalidadArchivos,
            "calidadCartasGarantia",
            "calidad_carta_garantia",
            "CALIDAD_CARTA_GARANTIA"
        );

        await GuardarArchivosPorTipo(
            embarque.Id,
            cartaLibreClembuterolArchivos,
            "calidadLibreClembuterol",
            "calidad_libre_clembuterol",
            "CALIDAD_LIBRE_CLEMBUTEROL"
        );

        await GuardarArchivosPorTipo(
            embarque.Id,
            cartaLibreResiduosToxicosArchivos,
            "calidadLibreResiduosToxicos",
            "calidad_libre_residuos_toxicos",
            "CALIDAD_LIBRE_RESIDUOS_TOXICOS"
        );

        await GuardarArchivosPorTipo(
            embarque.Id,
            cartaEEBArchivos,
            "calidadCartaEEB",
            "calidad_carta_eeb",
            "CALIDAD_CARTA_EEB"
        );

        await GuardarArchivosPorTipo(
            embarque.Id,
            avisoMovilizacionArchivos,
            "calidadAvisoMovilizacion",
            "calidad_aviso_movilizacion",
            "CALIDAD_AVISO_MOVILIZACION"
        );

        await GuardarArchivosPorTipo(
            embarque.Id,
            hojaTrabajoArchivos,
            "calidadHojaTrabajo",
            "calidad_hoja_trabajo",
            "CALIDAD_HOJA_TRABAJO"
        );

        // =========================
        // NUEVOS: FALTAN
        // =========================

        await GuardarArchivosPorTipo(
            embarque.Id,
            microbiologicosArchivos,
            "calidadMicrobiologicos",
            "calidad_microbiologicos",
            "CALIDAD_MICROBIOLOGICOS"
        );

        await GuardarArchivosPorTipo(
            embarque.Id,
            fichaTecnicaCalidadArchivos,
            "calidadFichaTecnica",
            "calidad_ficha_tecnica",
            "CALIDAD_FICHA_TECNICA"
        );

        await GuardarArchivosPorTipo(
            embarque.Id,
            facturaCalidadArchivos,
            "calidadFacturas",
            "calidad_factura",
            "CALIDAD_FACTURA"
        );

        // =========================
        // NUEVOS: POCO PRESENTES
        // =========================

        await GuardarArchivosPorTipo(
            embarque.Id,
            romaneosArchivos,
            "calidadRomaneos",
            "calidad_romaneos",
            "CALIDAD_ROMANEOS"
        );

        await GuardarArchivosPorTipo(
            embarque.Id,
            remisionArchivos,
            "calidadRemisiones",
            "calidad_remision",
            "CALIDAD_REMISION"
        );

        await _qrContext.SaveChangesAsync();

        var tiposCalidad = new[]
        {
        "CALIDAD_CARTA_GARANTIA",
        "CALIDAD_LIBRE_CLEMBUTEROL",
        "CALIDAD_LIBRE_RESIDUOS_TOXICOS",
        "CALIDAD_CARTA_EEB",
        "CALIDAD_AVISO_MOVILIZACION",
        "CALIDAD_HOJA_TRABAJO",

        // NUEVOS
        "CALIDAD_MICROBIOLOGICOS",
        "CALIDAD_FICHA_TECNICA",
        "CALIDAD_FACTURA",
        "CALIDAD_ROMANEOS",
        "CALIDAD_REMISION"
    };

        bool tieneAlgunDocumentoCalidad = await _qrContext.Set<EmbarqueArchivo>()
            .AnyAsync(a => a.EmbarqueId == embarque.Id && tiposCalidad.Contains(a.Tipo));

        if (!tieneAlgunDocumentoCalidad)
        {
            embarque.DocumentacionCalidadAprobada = false;
            embarque.FechaValidacionDocumentacionCalidad = null;
            embarque.UsuarioValidaDocumentacionCalidad = null;

            await _qrContext.SaveChangesAsync();

            TempData["Error"] = $"Debes cargar al menos un documento de calidad para validar el embarque #{embarque.Consecutivo}.";
            return RedirectToAction("DocumentacionCalidad");
        }

        embarque.DocumentacionCalidadAprobada = true;
        embarque.FechaValidacionDocumentacionCalidad = DateTime.Now;
        embarque.UsuarioValidaDocumentacionCalidad = User.Identity?.Name ?? "Sistema";

        embarque.Estatus =
            embarque.CalidadAprobada == true &&
            embarque.DocumentacionAprobada == true &&
            embarque.DocumentacionCalidadAprobada == true
                ? 7
                : 1;

        await _qrContext.SaveChangesAsync();

        TempData["Success"] =
            embarque.CalidadAprobada == true && embarque.DocumentacionAprobada == true
                ? $"Documentación de calidad validada correctamente para el embarque #{embarque.Consecutivo}. Ya puede generar QR."
                : $"Documentación de calidad validada correctamente para el embarque #{embarque.Consecutivo}. Aún faltan otras validaciones.";

        return RedirectToAction("DocumentacionCalidad");
    }

    private async Task GuardarArchivosPorTipo(
        int embarqueId,
        List<IFormFile>? archivos,
        string carpeta,
        string prefijo,
        string tipo)
    {
        if (archivos == null || !archivos.Any(f => f.Length > 0))
            return;

        foreach (var archivo in archivos.Where(f => f.Length > 0))
        {
            var ruta = await GuardarArchivo(archivo, carpeta, prefijo);

            _qrContext.Set<EmbarqueArchivo>().Add(new EmbarqueArchivo
            {
                EmbarqueId = embarqueId,
                Tipo = tipo,
                RutaArchivo = ruta,
                FechaRegistro = DateTime.Now,
                UsuarioRegistro = User.Identity?.Name ?? "Sistema"
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> DescargarMapaCargaExcel(int id)
    {
        var embarque = await _qrContext.Embarque
            .Include(e => e.Documentos)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (embarque == null)
            return NotFound();

        var ordenesIds = embarque.Documentos?
            .Where(d => d.TipoDocumento == "OV")
            .Select(d => d.DocumentoId)
            .ToList() ?? new List<int>();

        var transferenciasIds = embarque.Documentos?
            .Where(d => d.TipoDocumento == "TRANSFERENCIA")
            .Select(d => d.DocumentoId)
            .ToList() ?? new List<int>();

        var pedidosExcel = new List<MapaCargaExcelPedidoDto>();

        // ============================================================
        // ÓRDENES DE VENTA CON DETALLE
        // IMPORTANTE:
        // El embarque guarda DocumentoId = OrdenVenta.Id.
        // Para Mapa/Excel usamos PedidoVenta.OrdenVentaId.
        // ============================================================
        var pedidosVentaExcel = await _ovContext.PedidoVenta
            .AsNoTracking()
            .Include(p => p.Productos)
            .Where(p => ordenesIds.Contains(p.OrdenVentaId))
            .ToListAsync();

        // ============================================================
        // OBSERVACIONES DE ÓRDENES DE VENTA
        // El Excel se arma con PedidoVenta, pero la observación vive en OrdenVenta.
        // Relación:
        // PedidoVenta.OrdenVentaId = OrdenVenta.Id
        // ============================================================
        var observacionesOrdenVenta = await _ovContext.OrdenVenta
            .AsNoTracking()
            .Where(o => ordenesIds.Contains(o.Id))
            .Select(o => new
            {
                o.Id,
                o.Observacion
            })
            .ToDictionaryAsync(
                x => x.Id,
                x => x.Observacion ?? ""
            );

        // ============================================================
        // NO. MEAT DESDE SUBPEDIDO / SUBPEDIDOPRODUCTO
        // Relación:
        // Subpedido.OrdenVentaId = PedidoVenta.OrdenVentaId
        // SubpedidoProducto.SubpedidoId = Subpedido.Id
        // SubpedidoProducto.ProductoCodigo = PedidoVentaProducto.ProductoCodigo
        // SubpedidoProducto.Almacen = PedidoVentaProducto.Almacen
        // ============================================================
        static string NormalizarClaveNoMeat(int ordenVentaId, string? almacen, string? sku)
        {
            var alm = (almacen ?? "").Trim().ToUpperInvariant();
            var producto = (sku ?? "").Trim().ToUpperInvariant();

            return $"{ordenVentaId}|{alm}|{producto}";
        }

        static string ObtenerNombreAlmacen(string? almacen)
        {
            if (string.IsNullOrWhiteSpace(almacen))
                return "";

            var codigo = almacen.Trim().ToUpperInvariant();

            var almacenes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "3", "CEDIS P1 TIF" },
        { "CNT", "CEDIS P1 NO TIF" },
        { "6", "CAMARA FRESCO P1" },
        { "7", "RETENCION P1" },
        { "VL", "RETENCION TIF 805" },
        { "TIFPIE", "TIF 776 ALMACEN DE PIELES" },
        { "TIFFRE", "TIF 776 CAMARA FRESCO" },
        { "TIFCED", "TIF 776 ALMACEN CEDIS" },
        { "FM", "FRIGORIFICO MERIDA" },
        { "FMM", "FRIGORIFICO MONTERREY" },
        { "FMX", "FRIGORIFICO MEXICALI" },
        { "FTJ", "FRIGORIFICO TIJUANA" },
        { "FCDMX", "FRIGORIFICO CDMX" },
        { "FSLW", "FRIGORIFICO SALTILLO" },
        { "FCUN", "FRIGORIFICO CANCUN" },
        { "DMO", "PEDIDOS ESPECIALES" },
        { "DEV", "CAMARA DEVOLUCIONES" },
        { "FMT", "FRIGORIFICO LEÓN" }
    };

            return almacenes.TryGetValue(codigo, out var nombre)
                ? nombre
                : almacen.Trim();
        }

        static int ObtenerPrioridadAlmacenExcel(string? almacen)
        {
            if (string.IsNullOrWhiteSpace(almacen))
                return 99;

            var codigo = almacen.Trim().ToUpperInvariant();

            return codigo switch
            {
                "3" => 1,      // CEDIS P1 TIF
                "CNT" => 2,    // CEDIS P1 NO TIF
                _ => 99        // Los demás al final
            };
        }

        var subpedidosNoMeat = await _ovContext.Subpedidos
            .AsNoTracking()
            .Include(s => s.Productos)
            .Where(s => ordenesIds.Contains(s.OrdenVentaId))
            .ToListAsync();

        var noMeatPorOrdenAlmacenSku = subpedidosNoMeat
            .SelectMany(s => s.Productos.Select(p => new
            {
                s.OrdenVentaId,
                s.U_DocMeat,
                Almacen = p.Almacen,
                ProductoCodigo = p.ProductoCodigo
            }))
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.U_DocMeat) &&
                !string.IsNullOrWhiteSpace(x.ProductoCodigo))
            .GroupBy(x => NormalizarClaveNoMeat(
                x.OrdenVentaId,
                x.Almacen,
                x.ProductoCodigo
            ))
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.U_DocMeat).FirstOrDefault() ?? ""
            );

        // ============================================================
        // RESOLVER CLIENTES SAP PARA EXCEL MAPA DE CARGA
        // ============================================================
        var clientesCodigosExcel = pedidosVentaExcel
            .Select(p => p.Cliente)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Distinct()
            .ToList();

        var clientesSapExcel = await _ovContext.ClienteSap
            .AsNoTracking()
            .Where(c => clientesCodigosExcel.Contains(c.Cliente))
            .Select(c => new
            {
                Codigo = c.Cliente,
                Nombre = c.Nombrecliente
            })
            .ToListAsync();

        var clientesSapExcelDic = clientesSapExcel
            .GroupBy(c => c.Codigo)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Nombre).FirstOrDefault() ?? g.Key
            );

        foreach (var ov in pedidosVentaExcel)
        {
            var clienteCodigo = ov.Cliente?.Trim() ?? "";
            var clienteNombre = clientesSapExcelDic.TryGetValue(clienteCodigo, out var nombreSap)
                ? nombreSap
                : clienteCodigo;

            var observacionOv = observacionesOrdenVenta.TryGetValue(ov.OrdenVentaId, out var obs)
                ? obs
                : "";
            var detallesOv = ov.Productos
                .OrderBy(p => ObtenerPrioridadAlmacenExcel(p.Almacen))
                .ThenBy(p => p.ProductoCodigo)
                .ThenBy(p => p.Id)
                .Select((p, index) =>
                {
                    var claveNoMeat = NormalizarClaveNoMeat(
                        ov.OrdenVentaId,
                        p.Almacen,
                        p.ProductoCodigo
                    );

                    var noMeat = noMeatPorOrdenAlmacenSku.TryGetValue(claveNoMeat, out var docMeat)
                        ? docMeat
                        : "";

                    return new MapaCargaExcelDetalleDto
                    {
                        NoSigo = ov.OrdenVentaConsecutivo,
                        NoMeat = noMeat,
                        Almacen = ObtenerNombreAlmacen(p.Almacen),
                        Sku = p.ProductoCodigo,
                        Producto = p.ProductoNombre ?? "",
                        Cajas = p.Cajas,
                        Kilos = p.KilosCaja,

                        // La observación queda en el primer producto ya ordenado
                        Etiqueta = index == 0 && !string.IsNullOrWhiteSpace(observacionOv)
                            ? $" {observacionOv}"
                            : "",

                        Orden = p.Id
                    };
                })
                .ToList();

            pedidosExcel.Add(new MapaCargaExcelPedidoDto
            {
                // Mantener OV-{OrdenVentaId} para respetar el orden guardado del mapa.
                IdOrdenMapa = $"OV-{ov.OrdenVentaId}",
                Tipo = "OV",
                DocumentoId = ov.OrdenVentaId,
                Referencia = ov.OrdenVentaConsecutivo,
                ClienteDestino = clienteNombre,
                Ruta = ov.AlmacenSurtir ?? "",
                Fecha = ov.FechaEntrega,
                Detalles = detallesOv
            });
        }

        // ============================================================
        // TRANSFERENCIAS CON DETALLE DESDE PedidosTransferencia
        // ============================================================
        var pedidosTransferencia = await _ovContext.PedidosTransferencia
            .AsNoTracking()
            .Include(p => p.Detalles)
            .Where(p => transferenciasIds.Contains(p.TransferenciaId))
            .ToListAsync();

        foreach (var tr in pedidosTransferencia)
        {
            var detallesTr = tr.Detalles
                .OrderBy(d => d.Orden)
                .ThenBy(d => d.Id)
                .Select((d, index) => new MapaCargaExcelDetalleDto
                {
                    NoSigo = tr.Consecutivo,
                    NoMeat = "",
                    Almacen = "",
                    Sku = d.ProductoCodigo,
                    Producto = d.ProductoCodigo,
                    Cajas = d.Cajas,
                    Kilos = d.CantidadKg,

                    // Solo en el primer producto de la transferencia
                    Etiqueta = index == 0 && !string.IsNullOrWhiteSpace(tr.Observacion)
                        ? $" {tr.Observacion}"
                        : "",

                    Orden = d.Orden
                })
                .ToList();

            pedidosExcel.Add(new MapaCargaExcelPedidoDto
            {
                IdOrdenMapa = $"TR-{tr.TransferenciaId}",
                Tipo = "TRANSFERENCIA",
                DocumentoId = tr.TransferenciaId,
                Referencia = tr.Consecutivo,
                ClienteDestino = tr.Destino,
                Ruta = tr.Destino,
                Fecha = tr.FechaSolicitud,
                Detalles = detallesTr
            });
        }

        // ============================================================
        // RESPETAR ORDEN GUARDADO DEL MAPA DE CARGA
        // ============================================================
        var ordenGuardado = new List<string>();

        if (!string.IsNullOrWhiteSpace(embarque.MapaCargaOrdenJson))
        {
            try
            {
                ordenGuardado = JsonSerializer.Deserialize<List<string>>(embarque.MapaCargaOrdenJson) ?? new List<string>();
            }
            catch
            {
                ordenGuardado = new List<string>();
            }
        }

        pedidosExcel = pedidosExcel
            .OrderBy(p =>
            {
                var index = ordenGuardado.IndexOf(p.IdOrdenMapa);
                return index >= 0 ? index : int.MaxValue;
            })
            .ThenBy(p => p.IdOrdenMapa)
            .ToList();

        // ============================================================
        // CREAR EXCEL
        // ============================================================
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Mapa de Carga");

        ws.Style.Font.FontName = "Arial";
        ws.Style.Font.FontSize = 10;

        // Columnas:
        // A NO SIGO
        // B No Meat
        // C Almacén
        // D SKU
        // E Producto
        // F Cajas
        // G Kgs
        // H Etiqueta / Observaciones

        ws.Column("A").Width = 18;
        ws.Column("B").Width = 12;
        ws.Column("C").Width = 20;
        ws.Column("D").Width = 14;
        ws.Column("E").Width = 45;
        ws.Column("F").Width = 12;
        ws.Column("G").Width = 14;
        ws.Column("H").Width = 45;

        var rojo = XLColor.FromHtml("#C00000");
        var amarillo = XLColor.FromHtml("#FFC000");
        var rosa = XLColor.FromHtml("#F4C2E0");
        var verde = XLColor.FromHtml("#92D050");
        var grisBorde = XLColor.FromHtml("#D9D9D9");

        decimal totalKilos = pedidosExcel.Sum(p => p.Detalles.Sum(d => d.Kilos));
        int totalCajas = pedidosExcel.Sum(p => p.Detalles.Sum(d => d.Cajas));

        string tituloRuta = !string.IsNullOrWhiteSpace(embarque.NombreEmbarque)
            ? embarque.NombreEmbarque
            : !string.IsNullOrWhiteSpace(embarque.Consecutivo)
                ? embarque.Consecutivo
                : $"EMB-{embarque.Id}";

        DateTime fechaExcel =
            pedidosExcel.Select(p => p.Fecha).FirstOrDefault(f => f.HasValue)?.Date
            ?? embarque.FechaCreacion.Date;

        // ============================================================
        // ENCABEZADO PRINCIPAL
        // ============================================================
        ws.Range("A1:B1").Merge().Value = "FECHA";
        ws.Range("C1:H1").Merge().Value = fechaExcel.ToString("dd/MM/yyyy");

        ws.Range("A1:H1").Style.Fill.BackgroundColor = rojo;
        ws.Range("A1:H1").Style.Font.FontColor = XLColor.White;
        ws.Range("A1:H1").Style.Font.Bold = true;
        ws.Range("A1:H1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A1:H1").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 28;

        ws.Range("A2:E2").Merge().Value = tituloRuta.ToUpper();
        ws.Cell("F2").Value = "CAJAS";
        ws.Cell("G2").Value = totalCajas;
        ws.Cell("H2").Value = "OBSERVACIONES";

        ws.Range("A2:H2").Style.Fill.BackgroundColor = rojo;
        ws.Range("A2:H2").Style.Font.FontColor = XLColor.White;
        ws.Range("A2:H2").Style.Font.Bold = true;
        ws.Range("A2:H2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A2:H2").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(2).Height = 28;

        ws.Range("A3:E3").Merge().Value = "";
        ws.Cell("F3").Value = "KILOS";
        ws.Cell("G3").Value = totalKilos;
        ws.Cell("H3").Value = "";

        ws.Range("A3:H3").Style.Fill.BackgroundColor = rojo;
        ws.Range("A3:H3").Style.Font.FontColor = XLColor.White;
        ws.Range("A3:H3").Style.Font.Bold = true;
        ws.Range("A3:H3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A3:H3").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Cell("G3").Style.NumberFormat.Format = "#,##0.00";
        ws.Row(3).Height = 24;

        // ============================================================
        // CABECERA DE TABLA
        // ============================================================
        int row = 4;

        ws.Cell(row, 1).Value = "NO. SIGO";
        ws.Cell(row, 2).Value = "No. Meat";
        ws.Cell(row, 3).Value = "Almacén";
        ws.Cell(row, 4).Value = "SKU";
        ws.Cell(row, 5).Value = "Producto";
        ws.Cell(row, 6).Value = "Cajas";
        ws.Cell(row, 7).Value = "Kgs.";
        ws.Cell(row, 8).Value = "Etiqueta";

        ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = rojo;
        ws.Range(row, 1, row, 8).Style.Font.FontColor = XLColor.White;
        ws.Range(row, 1, row, 8).Style.Font.Bold = true;
        ws.Range(row, 1, row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(row, 1, row, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        row++;

        // ============================================================
        // CUERPO
        // ============================================================
        foreach (var pedido in pedidosExcel)
        {
            // Fila agrupadora como en tu imagen: cliente/destino en rosa
            ws.Range(row, 1, row, 4).Merge().Value = "";
            ws.Cell(row, 5).Value = pedido.ClienteDestino.ToUpper();

            ws.Cell(row, 5).Style.Fill.BackgroundColor = rosa;
            ws.Cell(row, 5).Style.Font.Bold = true;
            ws.Cell(row, 5).Style.Font.Italic = true;
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            row++;

            foreach (var det in pedido.Detalles)
            {
                ws.Cell(row, 1).Value = det.NoSigo;
                ws.Cell(row, 2).Value = det.NoMeat;
                ws.Cell(row, 3).Value = string.IsNullOrWhiteSpace(det.Almacen) ? "CEDIS P1 TIF" : det.Almacen;
                ws.Cell(row, 4).Value = det.Sku;
                ws.Cell(row, 5).Value = det.Producto;
                ws.Cell(row, 6).Value = det.Cajas;
                ws.Cell(row, 7).Value = det.Kilos;
                ws.Cell(row, 8).Value = det.Etiqueta;

                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";

                // Diferenciar transferencias visualmente
                if (pedido.Tipo == "TRANSFERENCIA")
                {
                    ws.Cell(row, 1).Style.Font.Bold = true;
                }

                row++;
            }

            // Renglón vacío entre grupos
            row++;
        }

        // ============================================================
        // ESTILO GENERAL
        // ============================================================
        var usedRange = ws.RangeUsed();

        if (usedRange != null)
        {
            usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            usedRange.Style.Border.OutsideBorderColor = grisBorde;
            usedRange.Style.Border.InsideBorderColor = grisBorde;
            usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        ws.Range(5, 6, row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(5, 1, row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Range(5, 8, row, 8).Style.Alignment.WrapText = true;

        ws.SheetView.FreezeRows(4);

        // Bordes negros en encabezado como la referencia
        ws.Range("A1:H4").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A1:H4").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A1:H4").Style.Border.OutsideBorderColor = XLColor.Black;
        ws.Range("A1:H4").Style.Border.InsideBorderColor = XLColor.Black;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var nombreBaseExcel = !string.IsNullOrWhiteSpace(embarque.NombreEmbarque)
        ? embarque.NombreEmbarque
        : embarque.Consecutivo;

            nombreBaseExcel = LimpiarNombreArchivo(nombreBaseExcel);

            var fileName = $"{nombreBaseExcel}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName
        );
    }

    // ============================================================
    // 4.1 QUITAR DOCUMENTO DEL EMBARQUE
    // ============================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Administracion de Ventas,Administrador")]
    public async Task<IActionResult> QuitarDocumentoEmbarque(int embarqueId, int documentoId, string tipoDocumento)
    {
        tipoDocumento = tipoDocumento?.Trim().ToUpper() ?? "";

        if (tipoDocumento != "OV" && tipoDocumento != "TRANSFERENCIA")
        {
            TempData["Error"] = "Tipo de documento no válido.";
            return RedirectToAction("Detalle", new { id = embarqueId });
        }

        var embarque = await _qrContext.Embarque
            .Include(e => e.Documentos)
            .Include(e => e.QR)
            .FirstOrDefaultAsync(e => e.Id == embarqueId);

        if (embarque == null)
        {
            TempData["Error"] = "No se encontró el embarque.";
            return RedirectToAction("Embarque");
        }

        // Seguridad: no permitir quitar documentos cuando ya salió o ya está en ruta/destino/entregado/devuelto
        if (embarque.FechaSalida != null
            || embarque.Estatus == 2
            || embarque.Estatus == 3
            || embarque.Estatus == 4
            || embarque.Estatus == 5
            || embarque.Estatus == 6)
        {
            TempData["Error"] = "No se pueden quitar documentos porque el embarque ya salió o ya está en seguimiento.";
            return RedirectToAction("Detalle", new { id = embarqueId });
        }

        // Seguridad: no permitir quitar documentos si el embarque ya tiene QR generado
        if (embarque.QR != null)
        {
            TempData["Error"] = "No se pueden quitar documentos porque el embarque ya tiene QR generado.";
            return RedirectToAction("Detalle", new { id = embarqueId });
        }

        var documento = embarque.Documentos
            .FirstOrDefault(d =>
                d.DocumentoId == documentoId &&
                d.TipoDocumento == tipoDocumento);

        if (documento == null)
        {
            TempData["Error"] = "El documento no está ligado a este embarque.";
            return RedirectToAction("Detalle", new { id = embarqueId });
        }

        // Validar que no se quede vacío el embarque
        var totalDocumentos = embarque.Documentos.Count;

        if (totalDocumentos <= 1)
        {
            TempData["Error"] = "No puedes quitar el último documento del embarque. Si ya no se usará, elimina o cancela el embarque completo.";
            return RedirectToAction("Detalle", new { id = embarqueId });
        }

        // 1) Quitar relación de EmbarqueDocumento
        _qrContext.EmbarqueDocumento.Remove(documento);

        // 2) Regresar el estatus anterior del documento
        if (tipoDocumento == "OV")
        {
            var orden = await _ovContext.OrdenVenta
                .FirstOrDefaultAsync(o => o.Id == documentoId);

            if (orden != null)
            {
                orden.Estatus = 5;
                orden.FechaEmbarque = null;
            }
        }
        else if (tipoDocumento == "TRANSFERENCIA")
        {
            // No modificamos el estatus de la transferencia.
            // Al quitar la relación de EmbarqueDocumento, si sigue en estatus 4,
            // volverá a aparecer como disponible automáticamente.
        }

        await _qrContext.SaveChangesAsync();
        await _ovContext.SaveChangesAsync();

        TempData["Success"] = tipoDocumento == "OV"
            ? "La orden fue quitada del embarque y regresó a estatus autorizado."
            : "La transferencia fue quitada del embarque y regresó a estatus autorizado.";

        return RedirectToAction("Detalle", new { id = embarqueId });
    }

    // ============================================================
    // 4.1.1 ELIMINAR EMBARQUE COMPLETO
    // ============================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Administracion de Ventas,Administrador")]
    public async Task<IActionResult> EliminarEmbarque(int embarqueId)
    {
        var embarque = await _qrContext.Embarque
            .Include(e => e.Documentos)
            .Include(e => e.QR)
            .FirstOrDefaultAsync(e => e.Id == embarqueId);

        if (embarque == null)
        {
            TempData["Error"] = "No se encontró el embarque.";
            return RedirectToAction("Embarque");
        }

        if (embarque.FechaSalida != null
            || embarque.Estatus == 2
            || embarque.Estatus == 3
            || embarque.Estatus == 4
            || embarque.Estatus == 5
            || embarque.Estatus == 6)
        {
            TempData["Error"] = "No se puede eliminar el embarque porque ya salió o está en seguimiento.";
            return RedirectToAction("Detalle", new { id = embarqueId });
        }

        if (embarque.QR != null)
        {
            TempData["Error"] = "No se puede eliminar el embarque porque ya tiene QR generado.";
            return RedirectToAction("Detalle", new { id = embarqueId });
        }

        var documentos = embarque.Documentos.ToList();

        foreach (var doc in documentos)
        {
            if (doc.TipoDocumento == "OV")
            {
                var orden = await _ovContext.OrdenVenta
                    .FirstOrDefaultAsync(o => o.Id == doc.DocumentoId);

                if (orden != null)
                {
                    orden.Estatus = 5;
                    orden.FechaEmbarque = null;
                }
            }
        }

        _qrContext.EmbarqueDocumento.RemoveRange(documentos);

        if (embarque.QR != null)
        {
            _qrContext.EmbarqueQR.Remove(embarque.QR);
        }

        var archivos = await _qrContext.EmbarqueArchivo
            .Where(a => a.EmbarqueId == embarqueId)
            .ToListAsync();

        if (archivos.Any())
        {
            _qrContext.EmbarqueArchivo.RemoveRange(archivos);
        }

        var fotosCalidad = await _qrContext.Set<Embarque.EmbarqueCalidadFoto>()
            .Where(f => f.EmbarqueId == embarqueId)
            .ToListAsync();

        if (fotosCalidad.Any())
        {
            _qrContext.Set<Embarque.EmbarqueCalidadFoto>().RemoveRange(fotosCalidad);
        }

        var temperaturas = await _qrContext.EmbarqueProductoTemperaturas
            .Where(t => t.EmbarqueId == embarqueId)
            .ToListAsync();

        if (temperaturas.Any())
        {
            _qrContext.EmbarqueProductoTemperaturas.RemoveRange(temperaturas);
        }

        _qrContext.Embarque.Remove(embarque);

        await _qrContext.SaveChangesAsync();
        await _ovContext.SaveChangesAsync();

        TempData["Success"] = $"El embarque {embarque.Consecutivo} fue eliminado correctamente. Las órdenes de venta regresaron a estatus autorizado.";

        return RedirectToAction("Embarque");
    }

    // ============================================================
    // 4.2 PANTALLA PARA EDITAR EMBARQUE - CARGA INICIAL LIGERA
    // ============================================================
    [HttpGet]
    [Authorize(Roles = "Administracion de Ventas,Administrador")]
    public async Task<IActionResult> EditarEmbarques(
        int id,
        CancellationToken cancellationToken)
    {
        // Solo obtenemos la información básica del embarque.
        // Las órdenes y transferencias se consultarán después por AJAX.
        var embarque = await _qrContext.Embarque
            .AsNoTracking()
            .Include(e => e.QR)
            .FirstOrDefaultAsync(
                e => e.Id == id,
                cancellationToken);

        if (embarque == null)
        {
            TempData["Error"] = "No se encontró el embarque.";
            return RedirectToAction("Embarque");
        }

        if (embarque.FechaSalida != null ||
            embarque.Estatus == 2 ||
            embarque.Estatus == 3 ||
            embarque.Estatus == 4 ||
            embarque.Estatus == 5 ||
            embarque.Estatus == 6)
        {
            TempData["Error"] =
                "No se pueden agregar documentos porque el embarque ya salió o ya está en seguimiento.";

            return RedirectToAction("Detalle", new
            {
                id
            });
        }

        if (embarque.QR != null)
        {
            TempData["Error"] =
                "No se pueden agregar documentos porque el embarque ya tiene QR generado.";

            return RedirectToAction("Detalle", new
            {
                id
            });
        }

        // Únicamente obtenemos los conteos.
        // Ya no cargamos todas las filas al abrir la vista.
        var totalOrdenes = await _ovContext.OrdenVenta
            .AsNoTracking()
            .CountAsync(
                o => o.Estatus == 5,
                cancellationToken);

        var transferenciasYaEnEmbarque = await _qrContext.EmbarqueDocumento
            .AsNoTracking()
            .Where(d => d.TipoDocumento == "TRANSFERENCIA")
            .Select(d => d.DocumentoId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var totalTransferencias = await _ovContext.Transferencias
            .AsNoTracking()
            .CountAsync(
                t => t.Estatus == 4 &&
                     !transferenciasYaEnEmbarque.Contains(t.Id),
                cancellationToken);

        ViewBag.TotalOrdenesDisponibles = totalOrdenes;
        ViewBag.TotalTransferenciasDisponibles = totalTransferencias;

        return View(embarque);
    }


    // ============================================================
    // 4.3 AGREGAR DOCUMENTOS AL EMBARQUE - OPTIMIZADO
    // ============================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Administracion de Ventas,Administrador")]
    public async Task<IActionResult> AgregarDocumentosEmbarque(
        int embarqueId,
        List<int>? ordenesSeleccionadas,
        List<int>? transferenciasSeleccionadas,
        CancellationToken cancellationToken)
    {
        ordenesSeleccionadas = ordenesSeleccionadas?
            .Where(id => id > 0)
            .Distinct()
            .ToList() ?? new List<int>();

        transferenciasSeleccionadas = transferenciasSeleccionadas?
            .Where(id => id > 0)
            .Distinct()
            .ToList() ?? new List<int>();

        if (!ordenesSeleccionadas.Any() &&
            !transferenciasSeleccionadas.Any())
        {
            TempData["Error"] =
                "Debes seleccionar al menos una orden o transferencia para agregar.";

            return RedirectToAction("EditarEmbarques", new
            {
                id = embarqueId
            });
        }

        var embarque = await _qrContext.Embarque
            .Include(e => e.Documentos)
            .Include(e => e.QR)
            .FirstOrDefaultAsync(
                e => e.Id == embarqueId,
                cancellationToken);

        if (embarque == null)
        {
            TempData["Error"] = "No se encontró el embarque.";
            return RedirectToAction("Embarque");
        }

        if (embarque.FechaSalida != null ||
            embarque.Estatus == 2 ||
            embarque.Estatus == 3 ||
            embarque.Estatus == 4 ||
            embarque.Estatus == 5 ||
            embarque.Estatus == 6)
        {
            TempData["Error"] =
                "No se pueden agregar documentos porque el embarque ya salió o ya está en seguimiento.";

            return RedirectToAction("Detalle", new
            {
                id = embarqueId
            });
        }

        if (embarque.QR != null)
        {
            TempData["Error"] =
                "No se pueden agregar documentos porque el embarque ya tiene QR generado.";

            return RedirectToAction("Detalle", new
            {
                id = embarqueId
            });
        }

        // Evita volver a agregar documentos que ya estén relacionados.
        var ordenesExistentes = embarque.Documentos
            .Where(d => d.TipoDocumento == "OV")
            .Select(d => d.DocumentoId)
            .ToHashSet();

        var transferenciasExistentes = embarque.Documentos
            .Where(d => d.TipoDocumento == "TRANSFERENCIA")
            .Select(d => d.DocumentoId)
            .ToHashSet();

        var idsOrdenesPorAgregar = ordenesSeleccionadas
            .Where(id => !ordenesExistentes.Contains(id))
            .ToList();

        var idsTransferenciasPorAgregar = transferenciasSeleccionadas
            .Where(id => !transferenciasExistentes.Contains(id))
            .ToList();

        // Una sola consulta para todas las órdenes seleccionadas.
        var ordenes = idsOrdenesPorAgregar.Any()
            ? await _ovContext.OrdenVenta
                .Where(o =>
                    idsOrdenesPorAgregar.Contains(o.Id) &&
                    o.Estatus == 5)
                .ToListAsync(cancellationToken)
            : new List<OrdenVenta>();

        // Una sola consulta para todas las transferencias seleccionadas.
        var transferenciasYaEnOtroEmbarque = idsTransferenciasPorAgregar.Any()
            ? await _qrContext.EmbarqueDocumento
                .AsNoTracking()
                .Where(d =>
                    d.TipoDocumento == "TRANSFERENCIA" &&
                    idsTransferenciasPorAgregar.Contains(d.DocumentoId))
                .Select(d => d.DocumentoId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : new List<int>();

        idsTransferenciasPorAgregar = idsTransferenciasPorAgregar
            .Where(id => !transferenciasYaEnOtroEmbarque.Contains(id))
            .ToList();

        var transferencias = idsTransferenciasPorAgregar.Any()
            ? await _ovContext.Transferencias
                .Where(t =>
                    idsTransferenciasPorAgregar.Contains(t.Id) &&
                    t.Estatus == 4)
                .ToListAsync(cancellationToken)
            : new List<Transferencia>();

        if (!ordenes.Any() && !transferencias.Any())
        {
            TempData["Error"] =
                "No se agregó ningún documento. Es posible que ya no estén disponibles o que ya pertenezcan a otro embarque.";

            return RedirectToAction("EditarEmbarques", new
            {
                id = embarqueId
            });
        }

        var ahora = DateTime.Now;

        var documentosNuevos = new List<EmbarqueDocumento>(
            ordenes.Count + transferencias.Count);

        documentosNuevos.AddRange(
            ordenes.Select(orden => new EmbarqueDocumento
            {
                EmbarqueId = embarque.Id,
                DocumentoId = orden.Id,
                TipoDocumento = "OV"
            }));

        documentosNuevos.AddRange(
            transferencias.Select(transferencia => new EmbarqueDocumento
            {
                EmbarqueId = embarque.Id,
                DocumentoId = transferencia.Id,
                TipoDocumento = "TRANSFERENCIA"
            }));

        _qrContext.EmbarqueDocumento.AddRange(documentosNuevos);

        foreach (var orden in ordenes)
        {
            orden.Estatus = 6;
            orden.FechaEmbarque = ahora;
        }

        // Las transferencias no se cambian de estatus.
        // Se bloquean para este módulo por EmbarqueDocumento.

        await _qrContext.SaveChangesAsync(cancellationToken);
        await _ovContext.SaveChangesAsync(cancellationToken);

        TempData["Success"] =
            $"Se agregaron {ordenes.Count} orden(es) y " +
            $"{transferencias.Count} transferencia(s) al embarque.";

        return RedirectToAction("Detalle", new
        {
            id = embarqueId
        });
    }

    private string ObtenerTextoEstatusTablero(int estatus)
    {
        return estatus switch
        {
            1 => "Pendiente validación",
            7 => "Liberado QR",
            2 => "En ruta",
            3 => "En destino",
            4 => "Retrasado",
            5 => "Entregado",
            6 => "Devuelto",
            _ => "Desconocido"
        };
    }

    private static string LimpiarNombreArchivo(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return "MapaCarga";

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            nombre = nombre.Replace(c, '_');
        }

        return nombre.Trim();
    }

    private string ObtenerTipoEstatusTablero(int estatus)
    {
        return estatus switch
        {
            5 => "ok",
            7 => "ready",
            2 => "route",
            3 => "destino",
            4 => "bad",
            6 => "return",
            1 => "warn",
            _ => "neutral"
        };
    }

    [HttpPost]
    public async Task<IActionResult> GuardarTemperaturasSku([FromBody] GuardarTemperaturasSkuRequest request)
    {
        if (request == null || request.EmbarqueId <= 0)
        {
            return Json(new
            {
                success = false,
                message = "Datos inválidos para guardar temperaturas."
            });
        }

        var embarque = await _qrContext.Embarque
            .Include(e => e.QR)
            .FirstOrDefaultAsync(e => e.Id == request.EmbarqueId);

        if (embarque == null)
        {
            return Json(new
            {
                success = false,
                message = "El embarque no existe."
            });
        }

        if (embarque.CalidadAprobada == true)
        {
            return Json(new
            {
                success = false,
                message = "Este embarque ya fue validado por Calidad."
            });
        }

        if (embarque.QR != null)
        {
            return Json(new
            {
                success = false,
                message = "Este embarque ya tiene QR generado y no se puede modificar."
            });
        }

        var productosFuente = await ConstruirProductosTemperaturaCalidad(request.EmbarqueId);

        var productosFuenteDic = productosFuente
            .GroupBy(CrearClaveProducto)
            .ToDictionary(g => g.Key, g => g.First());

        var itemsRequest = request.Items ?? new List<GuardarTemperaturaSkuItemRequest>();

        var itemsDic = itemsRequest
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.TipoDocumento) &&
                x.DocumentoId > 0 &&
                x.OrigenDetalleId > 0)
            .GroupBy(x => CrearClaveProducto(
                x.TipoDocumento,
                x.DocumentoId,
                x.OrigenDetalleId,
                x.ProductoCodigo,
                x.Almacen))
            .ToDictionary(g => g.Key, g => g.Last());

        var existentes = await _qrContext.EmbarqueProductoTemperaturas
            .Where(x => x.EmbarqueId == request.EmbarqueId)
            .ToListAsync();

        var existentesDic = existentes
            .GroupBy(CrearClaveProducto)
            .ToDictionary(g => g.Key, g => g.First());

        var usuario = User.Identity?.Name ?? "Sistema";
        var fecha = DateTime.Now;

        foreach (var item in itemsDic)
        {
            var clave = item.Key;
            var capturado = item.Value;

            if (!productosFuenteDic.TryGetValue(clave, out var productoFuente))
                continue;

            var observaciones = capturado.Observaciones?.Trim();

            bool vieneVacio =
                capturado.Temperatura == null &&
                string.IsNullOrWhiteSpace(observaciones);

            if (vieneVacio && !existentesDic.ContainsKey(clave))
                continue;

            if (!existentesDic.TryGetValue(clave, out var registro))
            {
                registro = new EmbarqueProductoTemperatura
                {
                    EmbarqueId = request.EmbarqueId,
                    TipoDocumento = productoFuente.TipoDocumento,
                    DocumentoId = productoFuente.DocumentoId,
                    DocumentoConsecutivo = productoFuente.DocumentoConsecutivo,
                    OrigenDetalleId = productoFuente.OrigenDetalleId,
                    ProductoCodigo = productoFuente.ProductoCodigo,
                    ProductoNombre = productoFuente.ProductoNombre,
                    Almacen = productoFuente.Almacen,
                    Cajas = productoFuente.Cajas,
                    Kilos = productoFuente.Kilos,
                    FechaRegistro = fecha,
                    UsuarioRegistro = usuario
                };

                _qrContext.EmbarqueProductoTemperaturas.Add(registro);
                existentesDic[clave] = registro;
            }
            else
            {
                registro.DocumentoConsecutivo = productoFuente.DocumentoConsecutivo;
                registro.ProductoCodigo = productoFuente.ProductoCodigo;
                registro.ProductoNombre = productoFuente.ProductoNombre;
                registro.Almacen = productoFuente.Almacen;
                registro.Cajas = productoFuente.Cajas;
                registro.Kilos = productoFuente.Kilos;
                registro.FechaActualizacion = fecha;
                registro.UsuarioActualiza = usuario;
            }

            registro.Temperatura = capturado.Temperatura;
            registro.Observaciones = string.IsNullOrWhiteSpace(observaciones)
                ? null
                : observaciones;
        }

        await _qrContext.SaveChangesAsync();

        var guardadasDespues = await _qrContext.EmbarqueProductoTemperaturas
            .AsNoTracking()
            .Where(x => x.EmbarqueId == request.EmbarqueId)
            .ToListAsync();

        var guardadasDespuesDic = guardadasDespues
            .GroupBy(CrearClaveProducto)
            .ToDictionary(g => g.Key, g => g.First());

        int total = productosFuente.Count;

        int capturadas = productosFuente.Count(p =>
        {
            var clave = CrearClaveProducto(p);

            return guardadasDespuesDic.TryGetValue(clave, out var registro)
                   && registro.Temperatura.HasValue;
        });

        return Json(new
        {
            success = true,
            total,
            capturadas,
            completo = total > 0 && capturadas == total,
            message = $"Temperaturas guardadas: {capturadas}/{total} SKU(s)."
        });
    }

    private static string NormalizarClaveProducto(string? valor)
    {
        return (valor ?? "").Trim().ToUpperInvariant();
    }

    private static string CrearClaveProducto(
        string? tipoDocumento,
        int documentoId,
        int origenDetalleId,
        string? productoCodigo,
        string? almacen)
    {
        return string.Join("|",
            NormalizarClaveProducto(tipoDocumento),
            documentoId,
            origenDetalleId,
            NormalizarClaveProducto(productoCodigo),
            NormalizarClaveProducto(almacen)
        );
    }

    private static string CrearClaveProducto(EmbarqueProductoTemperaturaItemVm item)
    {
        return CrearClaveProducto(
            item.TipoDocumento,
            item.DocumentoId,
            item.OrigenDetalleId,
            item.ProductoCodigo,
            item.Almacen
        );
    }

    private static string CrearClaveProducto(EmbarqueProductoTemperatura item)
    {
        return CrearClaveProducto(
            item.TipoDocumento,
            item.DocumentoId,
            item.OrigenDetalleId,
            item.ProductoCodigo,
            item.Almacen
        );
    }

    private async Task<List<EmbarqueProductoTemperaturaItemVm>> ConstruirProductosTemperaturaCalidad(int embarqueId)
    {
        var embarque = await _qrContext.Embarque
            .AsNoTracking()
            .Include(e => e.Documentos)
            .FirstOrDefaultAsync(e => e.Id == embarqueId);

        if (embarque == null)
            return new List<EmbarqueProductoTemperaturaItemVm>();

        var ordenesIds = embarque.Documentos?
            .Where(d => d.TipoDocumento == "OV")
            .Select(d => d.DocumentoId)
            .ToList() ?? new List<int>();

        var transferenciasIds = embarque.Documentos?
            .Where(d => d.TipoDocumento == "TRANSFERENCIA")
            .Select(d => d.DocumentoId)
            .ToList() ?? new List<int>();

        // ============================================================
        // METADATA DE OV: CONSECUTIVO + CLIENTE
        // IMPORTANTE: va después de crear ordenesIds
        // ============================================================
        var ordenesMeta = await _ovContext.OrdenVenta
            .AsNoTracking()
            .Where(o => ordenesIds.Contains(o.Id))
            .Select(o => new
            {
                o.Id,
                o.Consecutivo,
                Cliente = _ovContext.ClienteSap
                    .Where(c => c.Cliente == o.Cliente)
                    .Select(c => c.Nombrecliente)
                    .FirstOrDefault() ?? o.Cliente
            })
            .ToDictionaryAsync(
                x => x.Id,
                x => new
                {
                    x.Consecutivo,
                    x.Cliente
                });

        // ============================================================
        // METADATA DE TRANSFERENCIAS: CONSECUTIVO + SUCURSAL
        // IMPORTANTE: va después de crear transferenciasIds
        // ============================================================
        var transferenciasMeta = await _ovContext.Transferencias
            .AsNoTracking()
            .Where(t => transferenciasIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.Consecutivo,
                Cliente = t.Sucursal
            })
            .ToDictionaryAsync(
                x => x.Id,
                x => new
                {
                    x.Consecutivo,
                    x.Cliente
                });

        var productos = new List<EmbarqueProductoTemperaturaItemVm>();

        // ============================================================
        // OV: usar Subpedido → U_DocMeat → SurtidoEncabezado → SurtidoDetalleTarimas
        // SKUs realmente surtidos en vez de los generales de PedidoVentaProducto
        // ============================================================

        // 1. Obtener Subpedidos de las OVs del embarque
        var subpedidos = await _ovContext.Subpedidos
            .AsNoTracking()
            .Where(s => ordenesIds.Contains(s.OrdenVentaId))
            .ToListAsync();

        // 2. Mapear OrdenVentaId → U_DocMeat (tomar el primero por OV)
        var docMeatPorOV = subpedidos
            .Where(s => !string.IsNullOrWhiteSpace(s.U_DocMeat))
            .GroupBy(s => s.OrdenVentaId)
            .ToDictionary(g => g.Key, g => g.First().U_DocMeat!.Trim());

        // 3. Consultar SurtidoEncabezado usando los U_DocMeat
        //    SolicitudSurtidoId es int pero U_DocMeat es string, convertir a int para la query
        var docMeatInts = docMeatPorOV.Values
            .Distinct()
            .Select(v => int.TryParse(v, out var n) ? n : (int?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToList();

        var surtidoEncabezados = docMeatInts.Any()
            ? await _ovContext.SurtidoEncabezado
                .AsNoTracking()
                .Where(se => docMeatInts.Contains(se.SolicitudSurtidoId))
                .ToListAsync()
            : new List<SurtidoEncabezado>();

        // 4. Consultar SurtidoDetalleTarimas para estos encabezados
        var surtidoDetalle = docMeatInts.Any()
            ? await _ovContext.SurtidoDetalleTarimas
                .AsNoTracking()
                .Where(sd => docMeatInts.Contains(sd.SolicitudSurtidoId))
                .OrderBy(sd => sd.Articulo)
                .ThenBy(sd => sd.Tarima)
                .ToListAsync()
            : new List<SurtidoDetalleTarima>();

        // 5. Lookup de nombres de producto desde ArticuloSap
        var skusUnicos = surtidoDetalle
            .Select(sd => sd.Articulo)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!.Trim())
            .Distinct()
            .ToList();

        var nombresArticulo = skusUnicos.Any()
            ? await _ovContext.ArticuloSap
                .AsNoTracking()
                .Where(a => skusUnicos.Contains(a.ProductoCodigo))
                .ToDictionaryAsync(a => a.ProductoCodigo, a => a.ProductoNombre)
            : new Dictionary<string, string>();

        // 6. Construir productos desde SurtidoDetalleTarimas
        //    Crear diccionario inverso: SolicitudSurtidoId (int) → OrdenVentaId
        var ovPorSurtidoId = docMeatPorOV
            .Where(x => int.TryParse(x.Value, out _))
            .ToDictionary(
                x => int.Parse(x.Value),
                x => x.Key);

        foreach (var detalle in surtidoDetalle)
        {
            // Encontrar a qué OV pertenece este surtido
            ovPorSurtidoId.TryGetValue(detalle.SolicitudSurtidoId, out var ordenVentaId);
            ordenesMeta.TryGetValue(ordenVentaId, out var metaOv);

            var skuCodigo = (detalle.Articulo ?? "").Trim();
            var nombreProducto = nombresArticulo.TryGetValue(skuCodigo, out var nombre)
                ? nombre
                : skuCodigo;

            productos.Add(new EmbarqueProductoTemperaturaItemVm
            {
                TipoDocumento = "OV",
                DocumentoId = ordenVentaId,
                DocumentoConsecutivo = metaOv?.Consecutivo ?? "",
                DocumentoCliente = metaOv?.Cliente ?? "",
                OrigenDetalleId = detalle.SurtidoDetalleTarimaId,

                ProductoCodigo = skuCodigo,
                ProductoNombre = nombre,
                Almacen = (detalle.Sucursal ?? "").Trim(),

                Cajas = detalle.Cajas,
                Kilos = detalle.Kg,
                Tarima = (detalle.Tarima ?? "").Trim()
            });
        }

        // ============================================================
        // TRANSFERENCIAS: usar TransferenciaScanEtiqueta
        // Agrupar por TransferenciaId + Sku + TarimaCodigo
        // Cada grupo = 1 fila en calidad (misma tarima = misma temperatura)
        // ============================================================
        var scanEtiquetas = transferenciasIds.Any()
            ? await _ovContext.TransferenciaScanEtiquetas
                .AsNoTracking()
                .Where(s => transferenciasIds.Contains(s.TransferenciaId))
                .ToListAsync()
            : new List<TransferenciaScanEtiqueta>();

        // Lookup de nombres de producto desde ArticuloSap
        var skusTr = scanEtiquetas
            .Select(s => (s.Sku ?? "").Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        var nombresArticuloTr = skusTr.Any()
            ? await _ovContext.ArticuloSap
                .AsNoTracking()
                .Where(a => skusTr.Contains(a.ProductoCodigo))
                .ToDictionaryAsync(a => a.ProductoCodigo, a => a.ProductoNombre)
            : new Dictionary<string, string>();

        // Agrupar por TransferenciaId + Sku + TarimaCodigo
        var trGroups = scanEtiquetas
            .Where(s => !string.IsNullOrWhiteSpace(s.TarimaCodigo))
            .GroupBy(s => new
            {
                s.TransferenciaId,
                Sku = (s.Sku ?? "").Trim(),
                Tarima = (s.TarimaCodigo ?? "").Trim()
            });

        foreach (var g in trGroups)
        {
            var key = g.Key;
            transferenciasMeta.TryGetValue(key.TransferenciaId, out var metaTr);

            var skuCodigo = key.Sku;
            var nombreProducto = nombresArticuloTr.TryGetValue(skuCodigo, out var nombre)
                ? nombre
                : skuCodigo;

            // min(Id) como OrigenDetalleId estable para la clave
            var minId = g.Min(s => s.Id);

            productos.Add(new EmbarqueProductoTemperaturaItemVm
            {
                TipoDocumento = "TRANSFERENCIA",
                DocumentoId = key.TransferenciaId,
                DocumentoConsecutivo = metaTr?.Consecutivo ?? "",
                DocumentoCliente = metaTr?.Cliente ?? "",
                OrigenDetalleId = minId,

                ProductoCodigo = skuCodigo,
                ProductoNombre = nombreProducto,
                Almacen = "",

                Cajas = g.Count(),
                Kilos = g.Sum(s => s.Kg),
                Tarima = key.Tarima
            });
        }

        var guardadas = await _qrContext.EmbarqueProductoTemperaturas
            .AsNoTracking()
            .Where(x => x.EmbarqueId == embarqueId)
            .ToListAsync();

        var guardadasDic = guardadas
            .GroupBy(CrearClaveProducto)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.FechaActualizacion ?? x.FechaRegistro).First()
            );

        foreach (var producto in productos)
        {
            var clave = CrearClaveProducto(producto);

            if (guardadasDic.TryGetValue(clave, out var guardada))
            {
                producto.Id = guardada.Id;
                producto.Temperatura = guardada.Temperatura;
                producto.Observaciones = guardada.Observaciones;
                producto.FechaUltimaCaptura = guardada.FechaActualizacion ?? guardada.FechaRegistro;
                producto.UsuarioUltimaCaptura = guardada.UsuarioActualiza ?? guardada.UsuarioRegistro;
            }
        }

        return productos;
    }

    public class GuardarTemperaturasSkuRequest
    {
        public int EmbarqueId { get; set; }
        public List<GuardarTemperaturaSkuItemRequest> Items { get; set; } = new();
    }

    public class GuardarTemperaturaSkuItemRequest
    {
        public string TipoDocumento { get; set; } = "";
        public int DocumentoId { get; set; }
        public int OrigenDetalleId { get; set; }

        public string? ProductoCodigo { get; set; }
        public string? Almacen { get; set; }

        public decimal? Temperatura { get; set; }
        public string? Observaciones { get; set; }
    }

    // Agrega esta clase al final de tu EmbarquesController o en una carpeta Models 
    public class ActualizarEstatusRequest
    {
        public int EmbarqueId { get; set; }
        public int NuevoEstatus { get; set; }
    }

    public class ControlCenterDocumentoInfo
    {
        public string Ruta { get; set; }
        public string Cliente { get; set; }
        public string Presentacion { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public string Pedido { get; set; }
    }

    public class GuardarOrdenMapaCargaRequest
    {
        public int EmbarqueId { get; set; }
        public List<string> Orden { get; set; } = new List<string>();
    }

    private class MapaCargaExcelPedidoDto
    {
        public string IdOrdenMapa { get; set; } = "";
        public string Tipo { get; set; } = "";
        public int DocumentoId { get; set; }
        public string Referencia { get; set; } = "";
        public string ClienteDestino { get; set; } = "";
        public string Ruta { get; set; } = "";
        public DateTime? Fecha { get; set; }
        public List<MapaCargaExcelDetalleDto> Detalles { get; set; } = new();
    }

    private class MapaCargaExcelDetalleDto
    {
        public string NoSigo { get; set; } = "";
        public string NoMeat { get; set; } = "";
        public string Almacen { get; set; } = "";
        public string Sku { get; set; } = "";
        public string Producto { get; set; } = "";
        public int Cajas { get; set; }
        public decimal Kilos { get; set; }
        public string Etiqueta { get; set; } = "";
        public int Orden { get; set; }
    }
}