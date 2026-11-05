using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Globalization;
using System.Text.Json;

namespace Plataforma_CG.Services;

public sealed class RefaccionSapSolicitudCompraService
{
    private static readonly SemaphoreSlim SendGate = new(1, 1);
    private readonly AppDbContext _context;
    private readonly ISapServiceLayerClient _sap;

    public RefaccionSapSolicitudCompraService(AppDbContext context, ISapServiceLayerClient sap)
    {
        _context = context;
        _sap = sap;
    }

    public async Task<(bool Ok, string Mensaje, RefaccionSapDocumento? Solicitud)> CrearAsync(
        RefaccionSapSolicitudCompraCrearRequest request,
        string usuario)
    {
        if (request.Lineas == null || request.Lineas.Count == 0)
            return (false, "Selecciona al menos un artículo para solicitar.", null);

        var fechaNecesaria = (request.FechaNecesaria ?? DateTime.Today.AddDays(7)).Date;
        if (fechaNecesaria < DateTime.Today)
            return (false, "La fecha necesaria no puede estar en el pasado.", null);
        var comentarioUsuario = (request.Comentario ?? "").Trim();
        if (comentarioUsuario.Length > 140)
            return (false, "Los comentarios no pueden exceder 140 caracteres para ajustarse al límite de SAP.", null);

        var cuentas = await _context.CuentasContablesSap.AsNoTracking()
            .Where(x => x.Activa).Select(x => x.CodigoCuenta).ToListAsync();
        var cuentasPermitidas = cuentas.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var agrupadas = request.Lineas.GroupBy(x => x.Id).ToList();
        if (agrupadas.Any(x => x.Key <= 0 || x.Sum(y => y.Cantidad) <= 0))
            return (false, "Revisa las cantidades de los artículos seleccionados.", null);

        var ids = agrupadas.Select(x => x.Key).ToList();
        var articulos = await _context.Refacciones
            .Where(x => x.Activo && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        if (articulos.Count != ids.Count)
            return (false, "Uno o más artículos ya no están disponibles en el inventario.", null);
        var yaSolicitados = articulos.Values.Where(x => x.SolicitudStockActiva).Select(x => x.Sku).ToList();
        if (yaSolicitados.Count > 0)
            return (false, "Estos artículos ya están marcados como solicitados: " + string.Join(", ", yaSolicitados) + ". Quita la marca solo si realmente necesitas generar otra solicitud.", null);

        var lineasSap = new List<Dictionary<string, object?>>();
        foreach (var grupo in agrupadas)
        {
            var articulo = articulos[grupo.Key];
            var cantidad = grupo.Sum(x => x.Cantidad);
            if (cantidad <= 0 || cantidad > 1_000_000)
                return (false, $"La cantidad para {articulo.Sku} no es válida.", null);

            var almacenes = grupo.Select(x => (x.WarehouseCode ?? "").Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (almacenes.Count != 1 || !new[] { "MAN001", "MAN002", "MAN003" }
                    .Contains(almacenes[0], StringComparer.OrdinalIgnoreCase))
                return (false, $"Selecciona MAN001, MAN002 o MAN003 para {articulo.Sku}.", null);

            var cuentasLinea = grupo.Select(x => (x.AccountCode ?? "").Trim())
                .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (cuentasLinea.Count > 1)
                return (false, $"La cuenta contable debe ser la misma para las líneas de {articulo.Sku}.", null);
            if (cuentasLinea.Count == 1 && cuentasPermitidas.Count > 0 && !cuentasPermitidas.Contains(cuentasLinea[0]))
                return (false, $"La cuenta {cuentasLinea[0]} no está en el catálogo SAP sincronizado.", null);

            var linea = new Dictionary<string, object?>
            {
                ["ItemCode"] = articulo.Sku,
                ["Quantity"] = cantidad,
                ["WarehouseCode"] = almacenes[0],
                ["RequriedDate"] = fechaNecesaria.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
            if (cuentasLinea.Count == 1)
                linea["AccountCode"] = cuentasLinea[0];
            lineasSap.Add(linea);
        }

        var folio = "REQ-REF-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var comentario = CrearComentarioSap(folio, usuario, comentarioUsuario);
        var fechaSap = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["DocDate"] = fechaSap,
            ["DocDueDate"] = fechaNecesaria.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["RequriedDate"] = fechaNecesaria.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Comments"] = comentario,
            ["DocumentLines"] = lineasSap
        });

        var solicitud = new RefaccionSapDocumento
        {
            TipoMovimiento = "SOLICITUD_COMPRA",
            Folio = folio,
            Estado = "PENDIENTE",
            FechaCreacion = DateTime.Now,
            FechaNecesaria = fechaNecesaria,
            Usuario = string.IsNullOrWhiteSpace(usuario) ? "Usuario SIGO" : usuario.Trim(),
            Comentario = comentarioUsuario,
            CantidadLineas = lineasSap.Count,
            JsonSap = json
        };
        _context.RefaccionSapDocumentos.Add(solicitud);
        foreach (var articulo in articulos.Values)
        {
            articulo.SolicitudStockActiva = true;
            articulo.FechaSolicitudStock = DateTime.Now;
            articulo.UsuarioSolicitudStock = solicitud.Usuario;
            articulo.FechaModificacion = DateTime.Now;
            articulo.UsuarioModificacion = solicitud.Usuario;
        }
        await _context.SaveChangesAsync();
        return (true, "Solicitud guardada en SIGO y lista para enviar a SAP.", solicitud);
    }

    public async Task<(bool Ok, bool YaEnviada, string Mensaje, int? DocEntry, int? DocNum)> EnviarAsync(int id)
    {
        await SendGate.WaitAsync();
        try
        {
            var solicitud = await _context.RefaccionSapDocumentos.FirstOrDefaultAsync(x => x.TipoMovimiento == "SOLICITUD_COMPRA" && x.Id == id);
            if (solicitud == null) return (false, false, "No se encontró la solicitud de compra.", null, null);
            if (solicitud.Estado == "ENVIADO")
                return (true, true, "La solicitud ya fue enviada a SAP.", solicitud.SapDocEntry, solicitud.SapDocNum);

            solicitud.Estado = "ENVIANDO";
            solicitud.Error = null;
            await _context.SaveChangesAsync();
            try
            {
                var existente = await BuscarEnSapAsync(solicitud);
                if (!existente.ConsultaCorrecta)
                    throw new InvalidOperationException("No se pudo comprobar en SAP si el folio ya existe. No se repitió el envío. " + existente.Error);
                if (existente.DocEntry.HasValue || existente.DocNum.HasValue)
                {
                    MarcarEnviado(solicitud, existente.DocEntry, existente.DocNum, existente.Respuesta);
                    await _context.SaveChangesAsync();
                    return (true, true, "La solicitud ya existía en SAP y quedó sincronizada en SIGO.", solicitud.SapDocEntry, solicitud.SapDocNum);
                }

                var response = await _sap.PostJsonAsync("PurchaseRequests", solicitud.JsonSap);
                if (!response.ok)
                {
                    var despues = await BuscarEnSapAsync(solicitud);
                    if (despues.ConsultaCorrecta && (despues.DocEntry.HasValue || despues.DocNum.HasValue))
                    {
                        MarcarEnviado(solicitud, despues.DocEntry, despues.DocNum, response.response ?? despues.Respuesta);
                        await _context.SaveChangesAsync();
                        return (true, true, "SAP creó la solicitud y quedó sincronizada en SIGO.", solicitud.SapDocEntry, solicitud.SapDocNum);
                    }
                    throw new InvalidOperationException($"SAP rechazó la solicitud. {response.error} {response.response}");
                }

                var doc = LeerDocumento(response.response);
                MarcarEnviado(solicitud, doc.DocEntry, doc.DocNum, response.response);
                await _context.SaveChangesAsync();
                return (true, false, "Solicitud enviada correctamente a SAP.", solicitud.SapDocEntry, solicitud.SapDocNum);
            }
            catch (Exception ex)
            {
                solicitud.Estado = "ERROR";
                solicitud.Error = Limitar(ex.GetBaseException().Message, 4000);
                await _context.SaveChangesAsync();
                return (false, false, "La solicitud permanece guardada en SIGO, pero SAP no confirmó el envío. " + solicitud.Error, null, null);
            }
        }
        finally { SendGate.Release(); }
    }

    public async Task<object> ObtenerEstadoAsync()
    {
        var rows = await _context.RefaccionSapDocumentos.AsNoTracking()
            .Where(x => x.TipoMovimiento == "SOLICITUD_COMPRA")
            .OrderByDescending(x => x.FechaCreacion).Take(100)
            .Select(x => new { x.Id, x.Folio, x.Estado, x.FechaCreacion, x.FechaNecesaria, x.Usuario,
                x.Comentario, x.CantidadLineas, x.SapDocNum, x.Error, x.JsonSap })
            .ToListAsync();

        var articulosPorSolicitud = rows.ToDictionary(x => x.Id, x => LeerArticulosSolicitud(x.JsonSap));
        var skus = articulosPorSolicitud.Values.SelectMany(x => x).Select(x => x.ItemCode)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var descripciones = await _context.Refacciones.AsNoTracking()
            .Where(x => skus.Contains(x.Sku))
            .Select(x => new { x.Sku, x.Descripcion })
            .ToDictionaryAsync(x => x.Sku, x => x.Descripcion, StringComparer.OrdinalIgnoreCase);

        var items = rows.Select(x => new
        {
            x.Id, x.Folio, x.Estado, x.FechaCreacion, x.FechaNecesaria, x.Usuario,
            x.Comentario, x.CantidadLineas, x.SapDocNum, x.Error,
            articulos = articulosPorSolicitud[x.Id].Select(a => new
            {
                itemCode = a.ItemCode,
                descripcion = descripciones.TryGetValue(a.ItemCode, out var descripcion) ? descripcion : "",
                cantidad = a.Cantidad,
                almacen = a.Almacen,
                cuentaContable = a.CuentaContable
            }).ToList()
        });

        return new { ok = true, items };
    }

    private static List<SolicitudArticuloDetalle> LeerArticulosSolicitud(string? json)
    {
        var articulos = new List<SolicitudArticuloDetalle>();
        if (string.IsNullOrWhiteSpace(json)) return articulos;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("DocumentLines", out var lineas) || lineas.ValueKind != JsonValueKind.Array)
                return articulos;
            foreach (var linea in lineas.EnumerateArray())
            {
                articulos.Add(new SolicitudArticuloDetalle
                {
                    ItemCode = linea.TryGetProperty("ItemCode", out var sku) ? sku.GetString() ?? "" : "",
                    Cantidad = linea.TryGetProperty("Quantity", out var cantidad) && cantidad.ValueKind == JsonValueKind.Number ? cantidad.GetDecimal() : 0,
                    Almacen = linea.TryGetProperty("WarehouseCode", out var almacen) ? almacen.GetString() ?? "" : "",
                    CuentaContable = linea.TryGetProperty("AccountCode", out var cuenta) ? cuenta.GetString() ?? "" : ""
                });
            }
        }
        catch (JsonException) { }
        return articulos;
    }

    private sealed class SolicitudArticuloDetalle
    {
        public string ItemCode { get; set; } = "";
        public decimal Cantidad { get; set; }
        public string Almacen { get; set; } = "";
        public string CuentaContable { get; set; } = "";
    }

    private async Task<(bool ConsultaCorrecta, int? DocEntry, int? DocNum, string? Respuesta, string? Error)> BuscarEnSapAsync(RefaccionSapDocumento solicitud)
    {
        var comentario = CrearComentarioSap(solicitud.Folio, solicitud.Usuario, solicitud.Comentario ?? "");
        var filter = $"Comments eq '{EscaparOData(comentario)}'";
        var url = "PurchaseRequests?$select=DocEntry,DocNum&$top=1&$filter=" + Uri.EscapeDataString(filter);
        var result = await _sap.GetAsync(url);
        if (!result.ok || string.IsNullOrWhiteSpace(result.response))
            return (false, null, null, result.response, result.error ?? "Consulta SAP sin respuesta.");
        try
        {
            using var doc = JsonDocument.Parse(result.response);
            if (!doc.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0)
                return (true, null, null, result.response, null);
            var first = values[0];
            int? entry = first.TryGetProperty("DocEntry", out var de) && de.ValueKind == JsonValueKind.Number ? de.GetInt32() : null;
            int? number = first.TryGetProperty("DocNum", out var dn) && dn.ValueKind == JsonValueKind.Number ? dn.GetInt32() : null;
            return (true, entry, number, result.response, null);
        }
        catch (Exception ex) { return (false, null, null, result.response, "Respuesta SAP no interpretable: " + ex.Message); }
    }

    private static void MarcarEnviado(RefaccionSapDocumento solicitud, int? docEntry, int? docNum, string? respuesta)
    {
        solicitud.Estado = "ENVIADO";
        solicitud.SapDocEntry = docEntry;
        solicitud.SapDocNum = docNum;
        solicitud.FechaEnvio = DateTime.Now;
        solicitud.Error = null;
        solicitud.RespuestaSap = respuesta;
    }

    private static (int? DocEntry, int? DocNum) LeerDocumento(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return (null, null);
        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;
        int? entry = root.TryGetProperty("DocEntry", out var de) && de.ValueKind == JsonValueKind.Number ? de.GetInt32() : null;
        int? number = root.TryGetProperty("DocNum", out var dn) && dn.ValueKind == JsonValueKind.Number ? dn.GetInt32() : null;
        return (entry, number);
    }

    private static string EscaparOData(string value) => value.Replace("'", "''");
    private static string CrearComentarioSap(string folio, string usuario, string comentarioUsuario)
    {
        var solicitante = string.IsNullOrWhiteSpace(usuario) ? "Usuario SIGO" : usuario.Trim();
        if (solicitante.Length > 40) solicitante = solicitante[..40];
        var baseComentario = $"SIGO-REFACCIONES:{folio} | Solicitante:{solicitante}";
        return string.IsNullOrWhiteSpace(comentarioUsuario) ? baseComentario : baseComentario + " | " + comentarioUsuario.Trim();
    }
    private static string Limitar(string value, int max) => value.Length <= max ? value : value[..max];
}
