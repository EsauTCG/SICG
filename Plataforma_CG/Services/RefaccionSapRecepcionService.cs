using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Globalization;
using System.Text.Json;

namespace Plataforma_CG.Services;

public sealed class RefaccionSapRecepcionService
{
    private const int TipoOrdenCompraSap = 22;
    private const int TipoSolicitudCompraSap = 1470000113;
    private static readonly SemaphoreSlim SendGate = new(1, 1);
    private readonly AppDbContext _context;
    private readonly ISapServiceLayerClient _sap;

    public RefaccionSapRecepcionService(AppDbContext context, ISapServiceLayerClient sap)
    {
        _context = context;
        _sap = sap;
    }

    public async Task<object> ObtenerOrdenesAbiertasAsync()
    {
        var solicitudes = await _context.RefaccionSapDocumentos.AsNoTracking()
            .Where(x => x.TipoMovimiento == "SOLICITUD_COMPRA" && x.Estado == "ENVIADO" && x.SapDocEntry != null)
            .OrderByDescending(x => x.FechaEnvio)
            .Take(100)
            .Select(x => new { x.Id, x.Folio, x.SapDocEntry, x.SapDocNum, x.FechaCreacion })
            .ToListAsync();

        var articulos = await _context.Refacciones.AsNoTracking()
            .Where(x => x.Activo)
            .Select(x => new { x.Id, x.Sku, x.Descripcion, x.Planta })
            .ToListAsync();
        // Un SKU puede tener un registro por planta (TIF/P1). No se debe
        // convertir directamente a diccionario porque eso falla con claves repetidas.
        var porSku = articulos
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
            .GroupBy(x => x.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var solicitudesPorSap = solicitudes.Where(x => x.SapDocEntry.HasValue)
            .ToDictionary(x => x.SapDocEntry!.Value);
        var ordenes = new Dictionary<(int SolicitudId, int OrdenDocEntry), OrdenAbierta>();
        var rowsSap = await ConsultarOrdenesRelacionadasAsync(solicitudesPorSap.Keys);
        foreach (var row in rowsSap)
        {
            if (!solicitudesPorSap.TryGetValue(row.SolicitudDocEntry, out var solicitud)) continue;
            var key = (solicitud.Id, row.DocEntry);
            if (!ordenes.TryGetValue(key, out var orden))
            {
                orden = new OrdenAbierta
                {
                    SolicitudId = solicitud.Id,
                    FolioSolicitud = solicitud.Folio,
                    SolicitudSap = solicitud.SapDocNum,
                    DocEntry = row.DocEntry,
                    DocNum = row.DocNum,
                    Proveedor = row.Proveedor,
                    Fecha = row.Fecha,
                    Estado = row.Estado
                };
                ordenes[key] = orden;
            }

            if (row.Linea == null || row.CantidadAbierta <= 0 ||
                !porSku.TryGetValue((row.ItemCode ?? "").Trim(), out var articulosSku))
                continue;

            var plantaOrden = RefaccionPlantaHelper.PlantaDeAlmacen(row.Almacen);
            var articulo = plantaOrden == null
                ? (articulosSku.Count == 1 ? articulosSku[0] : null)
                : articulosSku.FirstOrDefault(x =>
                    string.Equals(RefaccionPlantaHelper.NormalizarPlanta(x.Planta), plantaOrden,
                        StringComparison.OrdinalIgnoreCase));
            if (articulo == null && articulosSku.Count == 1)
                articulo = articulosSku[0];
            if (articulo == null)
                continue;

            orden.Lineas.Add(new
            {
                articuloId = articulo.Id,
                sku = articulo.Sku,
                descripcion = string.IsNullOrWhiteSpace(row.Descripcion) ? articulo.Descripcion : row.Descripcion,
                lineNum = row.Linea.Value,
                cantidadOrdenada = row.CantidadOrdenada,
                cantidadAbierta = row.CantidadAbierta,
                almacen = row.Almacen
            });
        }

        var history = await _context.RefaccionSapDocumentos.AsNoTracking()
            .Where(x => x.TipoMovimiento == "ENTRADA")
            .OrderByDescending(x => x.FechaCreacion).Take(50)
            .Select(x => new { x.Id, x.Folio, x.SolicitudCompraId, x.OrdenCompraDocNum,
                x.Estado, x.Usuario, x.FechaCreacion, x.SapDocNum, x.Error, x.InventarioAplicado })
            .ToListAsync();

        return new
        {
            ok = true,
            ordenes = ordenes.Values.Where(x => x.Lineas.Count > 0)
                .OrderByDescending(x => x.Fecha).ToList(),
            recepciones = history
        };
    }

    public async Task<object> ObtenerDocumentoOrdenSapAsync(int docEntry)
    {
        if (docEntry <= 0)
            throw new ArgumentOutOfRangeException(nameof(docEntry), "El identificador de la orden SAP no es válido.");

        var response = await _sap.GetAsync($"PurchaseOrders({docEntry})");
        if (!response.ok || string.IsNullOrWhiteSpace(response.response))
            throw new InvalidOperationException(
                $"No se pudo recuperar la orden de compra {docEntry} desde SAP. {response.error} {response.response}");

        using var document = JsonDocument.Parse(response.response);
        var root = document.RootElement;
        var lineas = new List<object>();
        if (root.TryGetProperty("DocumentLines", out var lineasJson) && lineasJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var linea in lineasJson.EnumerateArray())
            {
                lineas.Add(new
                {
                    linea = Int(linea, "LineNum"),
                    sku = Str(linea, "ItemCode"),
                    descripcion = Str(linea, "ItemDescription"),
                    cantidad = Decimal(linea, "Quantity"),
                    unidad = Str(linea, "UoMCode", "MeasureUnit"),
                    almacen = Str(linea, "WarehouseCode"),
                    precioUnitario = Decimal(linea, "UnitPrice"),
                    descuento = Decimal(linea, "DiscountPercent"),
                    impuesto = Str(linea, "TaxCode"),
                    importe = Decimal(linea, "LineTotal")
                });
            }
        }

        return new
        {
            ok = true,
            docEntry = Int(root, "DocEntry"),
            docNum = Int(root, "DocNum"),
            proveedorCodigo = Str(root, "CardCode"),
            proveedor = Str(root, "CardName"),
            fecha = Str(root, "DocDate"),
            fechaEntrega = Str(root, "DocDueDate"),
            referenciaProveedor = Str(root, "NumAtCard"),
            moneda = Str(root, "DocCurrency"),
            subtotal = Decimal(root, "DocTotal") - Decimal(root, "VatSum"),
            impuestoTotal = Decimal(root, "VatSum"),
            total = Decimal(root, "DocTotal"),
            comentarios = Str(root, "Comments"),
            lineas
        };
    }

    public async Task<(bool Ok, string Mensaje, int? SapDocNum)> RegistrarAsync(
        RefaccionSapRecepcionCrearRequest request, string usuario)
    {
        if (request.SolicitudCompraId <= 0 || request.OrdenCompraDocEntry <= 0 || request.Lineas == null || request.Lineas.Count == 0)
            return (false, "Selecciona una orden y al menos un artículo para recibir.", null);
        if (request.Lineas.Any(x => x.LineNum < 0 || x.Cantidad <= 0))
            return (false, "Las cantidades de recepción deben ser mayores que cero.", null);
        if ((request.Comentario ?? "").Trim().Length > 140)
            return (false, "El comentario no puede exceder 140 caracteres.", null);

        await SendGate.WaitAsync();
        try
        {
            var solicitud = await _context.RefaccionSapDocumentos
                .FirstOrDefaultAsync(x => x.TipoMovimiento == "SOLICITUD_COMPRA" && x.Id == request.SolicitudCompraId && x.Estado == "ENVIADO");
            if (solicitud?.SapDocEntry == null)
                return (false, "No se encontró la solicitud enviada a SAP.", null);

            var ordenes = await ConsultarOrdenesRelacionadasAsync(new[] { solicitud.SapDocEntry.Value });
            var lineasOrden = ordenes.Where(x => x.DocEntry == request.OrdenCompraDocEntry && x.Linea.HasValue)
                .GroupBy(x => x.Linea!.Value)
                .ToDictionary(g => g.Key, g => g.First());
            if (lineasOrden.Count == 0)
                return (false, "La orden ya no está vinculada a esta solicitud en SAP.", null);

            var lineasSeleccionadas = new List<Dictionary<string, object?>>();
            var localLineas = new List<(Refaccion Articulo, int Cantidad, string Almacen)>();
            foreach (var grupo in request.Lineas.GroupBy(x => x.LineNum))
            {
                if (!lineasOrden.TryGetValue(grupo.Key, out var lineaSap))
                    return (false, $"No se encontró el renglón {grupo.Key + 1} de la orden.", null);
                var cantidad = grupo.Sum(x => x.Cantidad);
                if (cantidad > lineaSap.CantidadAbierta)
                    return (false, $"La cantidad de {lineaSap.ItemCode} supera lo pendiente en SAP ({lineaSap.CantidadAbierta}). Actualiza la lista.", null);
                var articulosSku = await _context.Refacciones
                    .Where(x => x.Activo && x.Sku == lineaSap.ItemCode)
                    .ToListAsync();
                var plantaOrden = RefaccionPlantaHelper.PlantaDeAlmacen(lineaSap.Almacen);
                var articulo = plantaOrden == null
                    ? (articulosSku.Count == 1 ? articulosSku[0] : null)
                    : articulosSku.FirstOrDefault(x =>
                        string.Equals(RefaccionPlantaHelper.NormalizarPlanta(x.Planta), plantaOrden,
                            StringComparison.OrdinalIgnoreCase));
                if (articulo == null && articulosSku.Count == 1)
                    articulo = articulosSku[0];
                if (articulo == null)
                {
                    var detalle = articulosSku.Count == 0
                        ? "no está registrado en SIGO"
                        : $"tiene varios registros en SIGO y no se encontró uno asociado a la planta del almacén {lineaSap.Almacen}";
                    return (false, $"El artículo {lineaSap.ItemCode} {detalle}.", null);
                }
                if (articulo.Stock > int.MaxValue - cantidad)
                    return (false, $"La cantidad recibida de {lineaSap.ItemCode} excede el límite de inventario de SIGO.", null);

                lineasSeleccionadas.Add(new Dictionary<string, object?>
                {
                    ["BaseType"] = TipoOrdenCompraSap,
                    ["BaseEntry"] = request.OrdenCompraDocEntry,
                    ["BaseLine"] = grupo.Key,
                    ["ItemCode"] = lineaSap.ItemCode,
                    ["Quantity"] = cantidad,
                    ["WarehouseCode"] = lineaSap.Almacen
                });
                localLineas.Add((articulo, cantidad, lineaSap.Almacen));
            }

            var folio = "REC-REF-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
            var comentario = $"SIGO-REF-REC:{folio} | Solicitud {solicitud.SapDocNum} | OC {lineasOrden[request.Lineas[0].LineNum].DocNum}";
            if (!string.IsNullOrWhiteSpace(request.Comentario)) comentario += " | " + request.Comentario.Trim();
            var json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["DocDate"] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Comments"] = comentario,
                ["JournalMemo"] = "Entrada de refacciones desde SIGO",
                ["DocumentLines"] = lineasSeleccionadas
            });

            var recepcion = new RefaccionSapDocumento
            {
                TipoMovimiento = "ENTRADA",
                Folio = folio,
                SolicitudCompraId = solicitud.Id,
                OrdenCompraDocEntry = request.OrdenCompraDocEntry,
                OrdenCompraDocNum = lineasOrden[request.Lineas[0].LineNum].DocNum,
                Estado = "ENVIANDO",
                Usuario = string.IsNullOrWhiteSpace(usuario) ? "Usuario SIGO" : usuario.Trim(),
                FechaCreacion = DateTime.Now,
                CantidadLineas = lineasSeleccionadas.Count,
                Referencia = $"Entrada SAP basada en OC {lineasOrden[request.Lineas[0].LineNum].DocNum}",
                Comentario = request.Comentario?.Trim(),
                JsonSap = json
            };
            _context.RefaccionSapDocumentos.Add(recepcion);
            await _context.SaveChangesAsync();

            try
            {
                var previo = await BuscarRecepcionEnSapAsync(comentario);
                if (!previo.ConsultaCorrecta)
                    throw new InvalidOperationException("No se pudo verificar en SAP si la entrada ya existe. No se envió. " + previo.Error);
                if (previo.DocEntry.HasValue || previo.DocNum.HasValue)
                {
                    recepcion.SapDocEntry = previo.DocEntry;
                    recepcion.SapDocNum = previo.DocNum;
                    recepcion.RespuestaSap = previo.Respuesta;
                }
                else
                {
                    var response = await _sap.PostJsonAsync("PurchaseDeliveryNotes", json);
                    if (!response.ok)
                    {
                        var confirmacion = await BuscarRecepcionEnSapAsync(comentario);
                        if (confirmacion.ConsultaCorrecta && (confirmacion.DocEntry.HasValue || confirmacion.DocNum.HasValue))
                        {
                            recepcion.SapDocEntry = confirmacion.DocEntry;
                            recepcion.SapDocNum = confirmacion.DocNum;
                            recepcion.RespuestaSap = response.response ?? confirmacion.Respuesta;
                        }
                        else
                        {
                            throw new InvalidOperationException($"SAP no confirmó la entrada. {response.error} {response.response}");
                        }
                    }
                    else
                    {
                        var doc = LeerDocumento(response.response);
                        recepcion.SapDocEntry = doc.DocEntry;
                        recepcion.SapDocNum = doc.DocNum;
                        recepcion.RespuestaSap = response.response;
                    }
                }

                recepcion.FechaEnvio = DateTime.Now;
                recepcion.Estado = "SAP_ENVIADO_PENDIENTE_LOCAL";
                await _context.SaveChangesAsync();
                await AplicarInventarioLocalAsync(recepcion.Id, localLineas);
                return (true, $"Entrada registrada en SAP y SIGO. Documento SAP {recepcion.SapDocNum?.ToString() ?? "creado"}.", recepcion.SapDocNum);
            }
            catch (Exception ex)
            {
                _context.ChangeTracker.Clear();
                var actual = await _context.RefaccionSapDocumentos.FirstAsync(x => x.Id == recepcion.Id);
                actual.Estado = actual.SapDocEntry.HasValue
                    ? "SAP_ENVIADO_PENDIENTE_LOCAL"
                    : "ERROR";
                actual.Error = ex.GetBaseException().Message;
                await _context.SaveChangesAsync();
                return (false, actual.SapDocEntry.HasValue
                    ? "SAP confirmó la entrada, pero SIGO no pudo actualizar el inventario. Usa la opción de sincronizar inventario para completar el movimiento. " + actual.Error
                    : "SAP no confirmó la entrada; no se actualizó el inventario de SIGO. " + actual.Error, actual.SapDocNum);
            }
        }
        finally { SendGate.Release(); }
    }

    public async Task<(bool Ok, string Mensaje)> ReaplicarInventarioAsync(int id)
    {
        var recepcion = await _context.RefaccionSapDocumentos.FirstOrDefaultAsync(x => x.TipoMovimiento == "ENTRADA" && x.Id == id);
        if (recepcion == null) return (false, "No se encontró la recepción.");
        if (!recepcion.SapDocEntry.HasValue) return (false, "SAP aún no confirma esta entrada.");
        if (recepcion.InventarioAplicado) return (true, "El inventario de SIGO ya está actualizado.");
        try
        {
            await AplicarInventarioLocalAsync(id);
            return (true, "Inventario de SIGO actualizado desde la entrada confirmada por SAP.");
        }
        catch (Exception ex)
        {
            _context.ChangeTracker.Clear();
            var actual = await _context.RefaccionSapDocumentos.FirstAsync(x => x.TipoMovimiento == "ENTRADA" && x.Id == id);
            actual.Estado = "SAP_ENVIADO_PENDIENTE_LOCAL";
            actual.Error = ex.GetBaseException().Message;
            await _context.SaveChangesAsync();
            return (false, "No se pudo actualizar el inventario de SIGO. " + actual.Error);
        }
    }

    public async Task<(bool Ok, string Mensaje)> ReintentarAsync(int id)
    {
        await SendGate.WaitAsync();
        try
        {
            var recepcion = await _context.RefaccionSapDocumentos.FirstOrDefaultAsync(x => x.TipoMovimiento == "ENTRADA" && x.Id == id);
            if (recepcion == null) return (false, "No se encontró la recepción.");
            if (recepcion.InventarioAplicado) return (true, "Esta recepción ya está aplicada en SIGO.");
            try
            {
                if (!recepcion.SapDocEntry.HasValue)
                {
                    using var payloadDoc = JsonDocument.Parse(recepcion.JsonSap);
                    var comentario = Str(payloadDoc.RootElement, "Comments");
                    var existente = await BuscarRecepcionEnSapAsync(comentario);
                    if (!existente.ConsultaCorrecta)
                        throw new InvalidOperationException("No se pudo comprobar si la entrada ya existe en SAP. No se repitió el envío. " + existente.Error);
                    if (!existente.DocEntry.HasValue && !existente.DocNum.HasValue)
                    {
                        var enviado = await _sap.PostJsonAsync("PurchaseDeliveryNotes", recepcion.JsonSap);
                        if (!enviado.ok)
                        {
                            var confirmacion = await BuscarRecepcionEnSapAsync(comentario);
                            if (!confirmacion.ConsultaCorrecta || (!confirmacion.DocEntry.HasValue && !confirmacion.DocNum.HasValue))
                                throw new InvalidOperationException($"SAP no confirmó la entrada. {enviado.error} {enviado.response}");
                            recepcion.SapDocEntry = confirmacion.DocEntry;
                            recepcion.SapDocNum = confirmacion.DocNum;
                            recepcion.RespuestaSap = enviado.response ?? confirmacion.Respuesta;
                        }
                        else
                        {
                            var doc = LeerDocumento(enviado.response);
                            recepcion.SapDocEntry = doc.DocEntry;
                            recepcion.SapDocNum = doc.DocNum;
                            recepcion.RespuestaSap = enviado.response;
                        }
                    }
                    else
                    {
                        recepcion.SapDocEntry = existente.DocEntry;
                        recepcion.SapDocNum = existente.DocNum;
                        recepcion.RespuestaSap = existente.Respuesta;
                    }
                    recepcion.FechaEnvio ??= DateTime.Now;
                }
                recepcion.Estado = "SAP_ENVIADO_PENDIENTE_LOCAL";
                recepcion.Error = null;
                await _context.SaveChangesAsync();
                await AplicarInventarioLocalAsync(id);
                return (true, $"Entrada SAP {recepcion.SapDocNum?.ToString() ?? "confirmada"} sincronizada con el inventario de SIGO.");
            }
            catch (Exception ex)
            {
                _context.ChangeTracker.Clear();
                var actual = await _context.RefaccionSapDocumentos.FirstAsync(x => x.TipoMovimiento == "ENTRADA" && x.Id == id);
                actual.Estado = actual.SapDocEntry.HasValue ? "SAP_ENVIADO_PENDIENTE_LOCAL" : "ERROR";
                actual.Error = ex.GetBaseException().Message;
                await _context.SaveChangesAsync();
                return (false, actual.Error);
            }
        }
        finally { SendGate.Release(); }
    }

    private async Task AplicarInventarioLocalAsync(int recepcionId, List<(Refaccion Articulo, int Cantidad, string Almacen)>? lineasRecibidas = null)
    {
        var recepcion = await _context.RefaccionSapDocumentos.FirstAsync(x => x.TipoMovimiento == "ENTRADA" && x.Id == recepcionId);
        if (recepcion.InventarioAplicado) return;
        if (recepcion.SapDocEntry == null)
            throw new InvalidOperationException("SAP todavía no confirmó el documento de entrada.");

        if (lineasRecibidas == null)
        {
            using var jsonDoc = JsonDocument.Parse(recepcion.JsonSap);
            lineasRecibidas = new();
            foreach (var line in jsonDoc.RootElement.GetProperty("DocumentLines").EnumerateArray())
            {
                var sku = line.GetProperty("ItemCode").GetString() ?? "";
                var cantidad = line.GetProperty("Quantity").GetInt32();
                var almacen = Str(line, "WarehouseCode", "WhsCode");
                var articulosSku = await _context.Refacciones
                    .Where(x => x.Activo && x.Sku == sku)
                    .ToListAsync();
                var planta = RefaccionPlantaHelper.PlantaDeAlmacen(almacen);
                var articulo = planta == null
                    ? (articulosSku.Count == 1 ? articulosSku[0] : null)
                    : articulosSku.FirstOrDefault(x => string.Equals(
                        RefaccionPlantaHelper.NormalizarPlanta(x.Planta), planta,
                        StringComparison.OrdinalIgnoreCase));
                if (articulo == null && articulosSku.Count == 1)
                    articulo = articulosSku[0];
                if (articulo == null)
                    throw new InvalidOperationException(
                        $"No se encontró en SIGO un registro único para {sku} asociado a la planta del almacén {almacen}.");

                lineasRecibidas.Add((articulo, cantidad, almacen));
            }
        }

        await using var tx = await _context.Database.BeginTransactionAsync();
        foreach (var (articulo, cantidad, almacen) in lineasRecibidas)
        {
            var tracked = await _context.Refacciones.FirstAsync(x => x.Id == articulo.Id);
            tracked.Stock += cantidad;
            tracked.FechaModificacion = DateTime.Now;
            tracked.UsuarioModificacion = recepcion.Usuario;
            _context.RefaccionMovimientos.Add(new RefaccionMovimiento
            {
                RefaccionId = tracked.Id,
                Tipo = "ENTRADA",
                Cantidad = cantidad,
                StockResultante = tracked.Stock,
                Folio = recepcion.Folio,
                Referencia = $"Entrada SAP {recepcion.SapDocNum} / OC {recepcion.OrdenCompraDocNum} / Almacén {almacen}",
                Comentario = "Entrada basada en orden de compra SAP",
                Usuario = recepcion.Usuario,
                Fecha = DateTime.Now
            });
        }
        recepcion.InventarioAplicado = true;
        recepcion.Estado = "ENVIADO";
        recepcion.Error = null;
        await _context.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task<List<OrdenLineaSap>> ConsultarOrdenesRelacionadasAsync(IEnumerable<int> solicitudDocEntries)
    {
        var docEntries = solicitudDocEntries.Distinct().Where(x => x > 0).ToList();
        if (docEntries.Count == 0) return new List<OrdenLineaSap>();
        var queryPath = "$crossjoin(PurchaseOrders,PurchaseOrders/DocumentLines)";
        var filtroSolicitudes = string.Join(" or ", docEntries.Select(x => $"PurchaseOrders/DocumentLines/BaseEntry eq {x}"));
        var queryOption = "$expand=" +
            "PurchaseOrders($select=DocEntry,DocNum,DocDate,DocumentStatus,Cancelled,CardName)," +
            "PurchaseOrders/DocumentLines($select=DocEntry,LineNum,BaseType,BaseEntry,BaseLine,ItemCode,ItemDescription,Quantity,RemainingOpenQuantity,WarehouseCode)" +
            "&$filter=PurchaseOrders/DocEntry eq PurchaseOrders/DocumentLines/DocEntry " +
            $"and PurchaseOrders/DocumentLines/BaseType eq {TipoSolicitudCompraSap} and ({filtroSolicitudes})";
        var payload = JsonSerializer.Serialize(new { QueryPath = queryPath, QueryOption = queryOption });
        var response = await _sap.PostJsonAsync("QueryService_PostQuery", payload);
        if (!response.ok || string.IsNullOrWhiteSpace(response.response))
            throw new InvalidOperationException($"No se pudieron consultar las órdenes de compra en SAP. {response.error} {response.response}");

        using var document = JsonDocument.Parse(response.response);
        var resultado = new List<OrdenLineaSap>();
        if (!document.RootElement.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return resultado;
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("PurchaseOrders", out var header) ||
                !row.TryGetProperty("PurchaseOrders/DocumentLines", out var line)) continue;
            var baseType = Int(line, "BaseType");
            var baseEntry = Int(line, "BaseEntry");
            if (baseType != TipoSolicitudCompraSap || !docEntries.Contains(baseEntry)) continue;
            var status = Str(header, "DocumentStatus", "DocStatus");
            if (Str(header, "Cancelled", "Canceled").Equals("tYES", StringComparison.OrdinalIgnoreCase) ||
                Str(header, "Cancelled", "Canceled").Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("Close", StringComparison.OrdinalIgnoreCase)) continue;
            resultado.Add(new OrdenLineaSap
            {
                DocEntry = Int(header, "DocEntry"), DocNum = Int(header, "DocNum"),
                SolicitudDocEntry = baseEntry,
                Proveedor = Str(header, "CardName"), Fecha = DateTime.TryParse(Str(header, "DocDate"), out var fecha) ? fecha : null,
                Linea = Int(line, "LineNum"), ItemCode = Str(line, "ItemCode"), Descripcion = Str(line, "ItemDescription"),
                CantidadOrdenada = Decimal(line, "Quantity"), CantidadAbierta = Decimal(line, "RemainingOpenQuantity"),
                Almacen = Str(line, "WarehouseCode", "WhsCode")
            });
        }
        return resultado;
    }

    private async Task<(bool ConsultaCorrecta, int? DocEntry, int? DocNum, string? Respuesta, string? Error)> BuscarRecepcionEnSapAsync(string comentario)
    {
        var filter = $"Comments eq '{comentario.Replace("'", "''")}'";
        var result = await _sap.GetAsync("PurchaseDeliveryNotes?$select=DocEntry,DocNum&$top=1&$filter=" + Uri.EscapeDataString(filter));
        if (!result.ok || string.IsNullOrWhiteSpace(result.response))
            return (false, null, null, result.response, result.error ?? "Consulta SAP sin respuesta.");
        try
        {
            using var doc = JsonDocument.Parse(result.response);
            if (!doc.RootElement.TryGetProperty("value", out var values) || values.GetArrayLength() == 0)
                return (true, null, null, result.response, null);
            return (true, Int(values[0], "DocEntry"), Int(values[0], "DocNum"), result.response, null);
        }
        catch (Exception ex) { return (false, null, null, result.response, ex.Message); }
    }

    private static (int? DocEntry, int? DocNum) LeerDocumento(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return (null, null);
        using var doc = JsonDocument.Parse(response);
        return (Int(doc.RootElement, "DocEntry"), Int(doc.RootElement, "DocNum"));
    }
    private static int Int(JsonElement element, params string[] names) =>
        names.Select(n => element.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0).FirstOrDefault(x => x != 0);
    private static decimal Decimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : 0m;
    private static string Str(JsonElement element, params string[] names) =>
        names.Select(n => element.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

    private sealed class OrdenAbierta
    {
        public int SolicitudId { get; set; }
        public string FolioSolicitud { get; set; } = "";
        public int? SolicitudSap { get; set; }
        public int DocEntry { get; set; }
        public int DocNum { get; set; }
        public string Proveedor { get; set; } = "";
        public DateTime? Fecha { get; set; }
        public string Estado { get; set; } = "ABIERTA";
        public List<object> Lineas { get; set; } = new();
    }
    private sealed class OrdenLineaSap
    {
        public int DocEntry { get; set; }
        public int DocNum { get; set; }
        public int SolicitudDocEntry { get; set; }
        public string Proveedor { get; set; } = "";
        public DateTime? Fecha { get; set; }
        public string Estado { get; set; } = "ABIERTA";
        public int? Linea { get; set; }
        public string ItemCode { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public decimal CantidadOrdenada { get; set; }
        public decimal CantidadAbierta { get; set; }
        public string Almacen { get; set; } = "";
    }
}
