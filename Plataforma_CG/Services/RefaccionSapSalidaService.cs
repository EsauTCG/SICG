using Microsoft.EntityFrameworkCore;
using System.Data;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Globalization;
using System.Text.Json;

namespace Plataforma_CG.Services
{
    public sealed class RefaccionSapSalidaService
    {
        private static readonly SemaphoreSlim SendGate = new(1, 1);
        private const string Endpoint = "InventoryGenExits";

        private readonly AppDbContext _context;
        private readonly ISapServiceLayerClient _sap;
        private readonly IConfiguration _configuration;

        public RefaccionSapSalidaService(
            AppDbContext context,
            ISapServiceLayerClient sap,
            IConfiguration configuration)
        {
            _context = context;
            _sap = sap;
            _configuration = configuration;
        }

        public string CrearDocumentoJson(
            string folio,
            DateTime fecha,
            string? referencia,
            string? comentario,
            IEnumerable<(Refaccion Articulo, int Cantidad)> lineas,
            RefaccionSapSalidaDatos? seleccion = null)
        {
            return CrearDocumentoJsonPorLinea(
                folio,
                fecha,
                referencia,
                comentario,
                lineas.Select(x => (x.Articulo, x.Cantidad, seleccion)));
        }

        public string CrearDocumentoJsonPorLinea(
            string folio,
            DateTime fecha,
            string? referencia,
            string? comentario,
            IEnumerable<(Refaccion Articulo, int Cantidad, RefaccionSapSalidaDatos? Sap)> lineas)
        {
            var lineasSap = new List<Dictionary<string, object?>>();
            foreach (var (articulo, cantidad, seleccion) in lineas)
            {
                var linea = new Dictionary<string, object?>
                {
                    ["ItemCode"] = articulo.Sku,
                    ["Quantity"] = Math.Abs(cantidad),
                    ["WarehouseCode"] = ValorSeleccionado("WarehouseCode", seleccion?.WarehouseCode),
                    ["AccountCode"] = ValorSeleccionado("AccountCode", seleccion?.AccountCode)
                };

                AgregarDimension(linea, "CostingCode", "CostingCode", seleccion?.CostingCode);
                AgregarDimension(linea, "CostingCode2", "CostingCode2", seleccion?.CostingCode2);
                AgregarDimension(linea, "CostingCode3", "CostingCode3", seleccion?.CostingCode3);
                AgregarDimension(linea, "CostingCode4", "CostingCode4", seleccion?.CostingCode4);
                AgregarDimension(linea, "CostingCode5", "CostingCode5", seleccion?.CostingCode5);
                lineasSap.Add(linea);
            }

            if (lineasSap.Count == 0)
                throw new InvalidOperationException("La salida no tiene líneas para enviar a SAP.");

            var fechaSap = fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var documento = new Dictionary<string, object?>
            {
                ["DocDate"] = fechaSap,
                ["DocDueDate"] = fechaSap,
                ["TaxDate"] = fechaSap,
                ["Comments"] = string.IsNullOrWhiteSpace(comentario)
                    ? $"Salida de refacciones registrada en Sigo. Folio {folio}"
                    : comentario.Trim(),
                ["JournalMemo"] = "Salida de refacciones desde Sigo",
                ["NumAtCard"] = string.IsNullOrWhiteSpace(referencia) ? folio : referencia.Trim(),
                ["U_DocMeat"] = folio,
                ["DocumentLines"] = lineasSap
            };

            return JsonSerializer.Serialize(documento);
        }

        public object ObtenerConfiguracion(IEnumerable<CuentaContableSap>? cuentasContables = null)
        {
            var opciones = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var clave in ClavesSap)
                opciones[clave] = ObtenerOpciones(clave);

            var cuentas = cuentasContables?.Where(x => x.Activa)
                .OrderBy(x => x.CodigoCuenta, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var etiquetas = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (cuentas?.Count > 0)
            {
                opciones["AccountCode"] = cuentas.Select(x => x.CodigoCuenta).ToArray();
                etiquetas["AccountCode"] = cuentas.ToDictionary(
                    x => x.CodigoCuenta,
                    x => $"{x.CodigoCuenta} — {x.NombreCuenta}",
                    StringComparer.OrdinalIgnoreCase);
            }

            var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WarehouseCode"] = ValorConfig("WarehouseCode"),
                ["AccountCode"] = ValorConfig("AccountCode"),
                ["CostingCode"] = ValorConfig("CostingCode"),
                ["CostingCode2"] = ValorConfig("CostingCode2"),
                ["CostingCode3"] = ValorConfig("CostingCode3"),
                ["CostingCode4"] = ValorConfig("CostingCode4"),
                ["CostingCode5"] = ValorConfig("CostingCode5")
            };
            if (cuentas?.Count > 0 && !cuentas.Any(x => string.Equals(
                    x.CodigoCuenta,
                    valores["AccountCode"],
                    StringComparison.OrdinalIgnoreCase)))
                valores["AccountCode"] = cuentas[0].CodigoCuenta;

            return new
            {
                habilitado = _configuration.GetValue<bool>("SapServiceLayer:RefaccionesSalidas:Habilitado"),
                almacen = ValorConfig("WarehouseCode"),
                cuenta = ValorConfig("AccountCode"),
                dimensiones = new[]
                {
                    ValorConfig("CostingCode"),
                    ValorConfig("CostingCode2"),
                    ValorConfig("CostingCode3")
                },
                valores,
                opciones,
                etiquetas
            };
        }

        private static readonly string[] ClavesSap =
        {
            "WarehouseCode", "AccountCode", "CostingCode", "CostingCode2",
            "CostingCode3", "CostingCode4", "CostingCode5"
        };

        public async Task<string?> ValidarSeleccionAsync(
            RefaccionSapSalidaDatos? seleccion,
            IReadOnlySet<string>? cuentasSapPermitidas = null)
        {
            if (seleccion == null) return null;

            if (cuentasSapPermitidas == null)
            {
                var cuentasLocales = await _context.CuentasContablesSap.AsNoTracking()
                    .Where(x => x.Activa)
                    .Select(x => x.CodigoCuenta)
                    .ToListAsync();
                cuentasSapPermitidas = new HashSet<string>(cuentasLocales, StringComparer.OrdinalIgnoreCase);
            }

            var valores = new Dictionary<string, string?>
            {
                ["WarehouseCode"] = seleccion.WarehouseCode,
                ["AccountCode"] = seleccion.AccountCode,
                ["CostingCode"] = seleccion.CostingCode,
                ["CostingCode2"] = seleccion.CostingCode2,
                ["CostingCode3"] = seleccion.CostingCode3,
                ["CostingCode4"] = seleccion.CostingCode4,
                ["CostingCode5"] = seleccion.CostingCode5
            };

            foreach (var (clave, valor) in valores)
            {
                if (string.IsNullOrWhiteSpace(valor))
                {
                    if (clave is "WarehouseCode" or "AccountCode" or "CostingCode")
                        return $"Selecciona un valor para {EtiquetaCampo(clave)}.";
                    continue;
                }

                var permitido = clave == "AccountCode" && cuentasSapPermitidas.Count > 0
                    ? cuentasSapPermitidas.Contains(valor.Trim())
                    : ObtenerOpciones(clave).Contains(valor.Trim(), StringComparer.OrdinalIgnoreCase);
                if (!permitido)
                    return $"El valor seleccionado para {EtiquetaCampo(clave)} no está permitido en la configuración SAP.";
            }

            return null;
        }

        public async Task<(bool Ok, string Mensaje)> EditarAsync(RefaccionSapEditarSalidaRequest req, string usuario)
        {
            await SendGate.WaitAsync();
            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var folio = (req.Folio ?? "").Trim();
                var salida = await _context.RefaccionSapDocumentos
                    .FirstOrDefaultAsync(x => x.TipoMovimiento == "SALIDA" && x.Folio == folio);
                if (salida == null) return (false, "No se encontró la salida.");
                if (salida.Estado is not ("PENDIENTE" or "ERROR") || salida.SapDocEntry.HasValue || salida.SapDocNum.HasValue)
                    return (false, "Esta salida ya no se puede editar porque SAP ya la recibió o está en proceso.");
                if (req.Lineas == null || req.Lineas.Count == 0)
                    return (false, "La salida debe conservar al menos un artículo.");
                if (req.Lineas.Any(x => x.Cantidad <= 0))
                    return (false, "Todas las cantidades deben ser mayores que cero.");
                if (req.Lineas.Select(x => x.Id).Distinct().Count() != req.Lineas.Count)
                    return (false, "No repitas el mismo artículo; combina sus cantidades en una sola línea.");
                if (req.Lineas.Any(x => string.IsNullOrWhiteSpace(x.Maquina)))
                    return (false, "Asigna una máquina o equipo a cada artículo.");

                var movimientosAnteriores = await _context.RefaccionMovimientos
                    .Where(x => x.Tipo == "SALIDA" && x.Folio == folio).ToListAsync();
                var cantidadesAnteriores = movimientosAnteriores
                    .GroupBy(x => x.RefaccionId)
                    .ToDictionary(g => g.Key, g => g.Sum(x => Math.Abs(x.Cantidad)));
                var ids = req.Lineas.Select(x => x.Id).Concat(cantidadesAnteriores.Keys).Distinct().ToList();
                var articulos = await _context.Refacciones.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
                if (req.Lineas.Any(x => !articulos.ContainsKey(x.Id) || !articulos[x.Id].Activo))
                    return (false, "Uno de los artículos ya no existe o está inactivo.");

                var configuracionSap = JsonDocument.Parse(salida.JsonSap);
                var seleccionesPrevias = new Dictionary<string, RefaccionSapSalidaDatos>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (configuracionSap.RootElement.TryGetProperty("DocumentLines", out var sapLineas) && sapLineas.ValueKind == JsonValueKind.Array)
                    foreach (var linea in sapLineas.EnumerateArray())
                    {
                        var sku = linea.TryGetProperty("ItemCode", out var codigo) ? codigo.GetString() : null;
                        if (string.IsNullOrWhiteSpace(sku)) continue;
                        seleccionesPrevias[sku] = new RefaccionSapSalidaDatos
                        {
                            WarehouseCode = Leer(linea, "WarehouseCode"), AccountCode = Leer(linea, "AccountCode"),
                            CostingCode = Leer(linea, "CostingCode"), CostingCode2 = Leer(linea, "CostingCode2"),
                            CostingCode3 = Leer(linea, "CostingCode3"), CostingCode4 = Leer(linea, "CostingCode4"),
                            CostingCode5 = Leer(linea, "CostingCode5")
                        };
                    }
                }
                finally { configuracionSap.Dispose(); }

                var cuentasPermitidas = (await _context.CuentasContablesSap.AsNoTracking()
                    .Where(x => x.Activa).Select(x => x.CodigoCuenta).ToListAsync())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var selecciones = new Dictionary<int, RefaccionSapSalidaDatos>();
                foreach (var linea in req.Lineas)
                {
                    var articulo = articulos[linea.Id];
                    var seleccion = linea.Sap;
                    if (seleccion == null && seleccionesPrevias.TryGetValue(articulo.Sku, out var previa)) seleccion = previa;
                    seleccion ??= new RefaccionSapSalidaDatos
                    {
                        WarehouseCode = ValorConfig("WarehouseCode"), AccountCode = ValorConfig("AccountCode"),
                        CostingCode = ValorConfig("CostingCode"), CostingCode2 = ValorConfig("CostingCode2"),
                        CostingCode3 = ValorConfig("CostingCode3"), CostingCode4 = ValorConfig("CostingCode4"),
                        CostingCode5 = ValorConfig("CostingCode5")
                    };
                    var errorSap = await ValidarSeleccionAsync(seleccion, cuentasPermitidas);
                    if (errorSap != null) return (false, $"{articulo.Descripcion}: {errorSap}");
                    selecciones[linea.Id] = seleccion;
                }

                foreach (var id in ids)
                {
                    var anterior = cantidadesAnteriores.GetValueOrDefault(id);
                    var nueva = req.Lineas.Where(x => x.Id == id).Sum(x => x.Cantidad);
                    var stock = articulos[id].Stock + anterior - nueva;
                    if (stock < 0)
                    {
                        await tx.RollbackAsync();
                        return (false, $"Stock insuficiente para {articulos[id].Sku}. Disponible para esta salida: {articulos[id].Stock + anterior}.");
                    }
                    articulos[id].Stock = stock;
                    articulos[id].FechaModificacion = DateTime.Now;
                    articulos[id].UsuarioModificacion = usuario;
                }

                _context.RefaccionMovimientos.RemoveRange(movimientosAnteriores);
                var referencia = string.IsNullOrWhiteSpace(req.Referencia) ? salida.Referencia : req.Referencia.Trim();
                var comentario = req.Comentario?.Trim();
                var maquinas = req.Lineas.Select(x => x.Maquina).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var comentarioSap = ConstruirComentario(comentario, maquinas);
                foreach (var linea in req.Lineas)
                {
                    var articulo = articulos[linea.Id];
                    _context.RefaccionMovimientos.Add(new RefaccionMovimiento
                    {
                        RefaccionId = articulo.Id, Tipo = "SALIDA", Cantidad = -linea.Cantidad,
                        StockResultante = articulo.Stock, Folio = folio, Referencia = referencia,
                        MaquinaDestino = linea.Maquina!.Trim(), Comentario = comentario, Usuario = usuario, Fecha = DateTime.Now
                    });
                }
                salida.Referencia = referencia;
                salida.Comentario = comentarioSap;
                salida.CantidadLineas = req.Lineas.Count;
                salida.JsonSap = CrearDocumentoJsonPorLinea(folio, salida.FechaCreacion.Date, referencia, comentarioSap,
                    req.Lineas.Select(x => (articulos[x.Id], x.Cantidad, (RefaccionSapSalidaDatos?)selecciones[x.Id])));
                salida.Estado = "PENDIENTE";
                salida.Error = null;
                salida.RespuestaSap = null;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                return (true, "Salida actualizada en SIGO. Puedes revisar y enviarla a SAP cuando esté lista.");
            }
            catch (Exception ex)
            {
                return (false, "No se pudo actualizar la salida: " + ex.GetBaseException().Message);
            }
            finally { SendGate.Release(); }
        }

        private static string? Leer(JsonElement linea, string propiedad) =>
            linea.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

        private static string? ConstruirComentario(string? comentario, IEnumerable<string?> maquinas)
        {
            var partes = new List<string>();
            if (!string.IsNullOrWhiteSpace(comentario)) partes.Add(comentario.Trim());
            var equipos = maquinas.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (equipos.Count > 0) partes.Add("Equipo(s) destino: " + string.Join(", ", equipos));
            var texto = string.Join(" | ", partes);
            return texto.Length == 0 ? null : texto[..Math.Min(texto.Length, 254)];
        }

        public async Task<(bool Ok, bool YaEnviada, string Mensaje, int? DocEntry, int? DocNum)> EnviarAsync(string folio)
        {
            await SendGate.WaitAsync();
            try
            {
                if (!_configuration.GetValue<bool>("SapServiceLayer:RefaccionesSalidas:Habilitado"))
                    return (false, false, "El envío de salidas de refacciones a SAP no está habilitado en esta configuración.", null, null);

                if (string.IsNullOrWhiteSpace(ValorConfig("WarehouseCode")) ||
                    string.IsNullOrWhiteSpace(ValorConfig("AccountCode")))
                    return (false, false, "Configura almacén y cuenta contable para habilitar el envío de salidas a SAP.", null, null);

                var salida = await _context.RefaccionSapDocumentos
                    .FirstOrDefaultAsync(x => x.TipoMovimiento == "SALIDA" && x.Folio == folio);
                if (salida == null)
                    return (false, false, "No se encontró la salida pendiente.", null, null);

                if (salida.Estado == "ENVIADO")
                    return (true, true, "La salida ya estaba enviada a SAP.", salida.SapDocEntry, salida.SapDocNum);

                salida.Estado = "ENVIANDO";
                salida.Error = null;
                await _context.SaveChangesAsync();

                try
                {
                    var existente = await BuscarEnSapAsync(salida.Folio);
                    if (!existente.ConsultaCorrecta)
                        throw new InvalidOperationException("No se pudo comprobar en SAP si el folio ya existe. No se hizo un nuevo envío. " + existente.Error);

                    if (existente.DocEntry.HasValue || existente.DocNum.HasValue)
                    {
                        MarcarEnviado(salida, existente.DocEntry, existente.DocNum, existente.Respuesta);
                        await _context.SaveChangesAsync();
                        return (true, true, "SAP ya tenía este folio; no se creó un duplicado.", salida.SapDocEntry, salida.SapDocNum);
                    }

                    var respuesta = await _sap.PostJsonAsync(Endpoint, salida.JsonSap);
                    if (!respuesta.ok)
                    {
                        // SAP puede haber creado el documento aunque la respuesta se haya perdido.
                        var despuesDelError = await BuscarEnSapAsync(salida.Folio);
                        if (despuesDelError.ConsultaCorrecta &&
                            (despuesDelError.DocEntry.HasValue || despuesDelError.DocNum.HasValue))
                        {
                            MarcarEnviado(salida, despuesDelError.DocEntry, despuesDelError.DocNum,
                                respuesta.response ?? despuesDelError.Respuesta);
                            await _context.SaveChangesAsync();
                            return (true, true, "La salida existe en SAP y quedó sincronizada.", salida.SapDocEntry, salida.SapDocNum);
                        }

                        throw new InvalidOperationException($"SAP rechazó la salida. {respuesta.error} {respuesta.response}");
                    }

                    var doc = LeerDocumento(respuesta.response);
                    MarcarEnviado(salida, doc.DocEntry, doc.DocNum, respuesta.response);
                    await _context.SaveChangesAsync();
                    return (true, false, "Salida enviada correctamente a SAP.", salida.SapDocEntry, salida.SapDocNum);
                }
                catch (Exception ex)
                {
                    salida.Estado = "ERROR";
                    salida.Error = Limitar(ex.GetBaseException().Message, 4000);
                    await _context.SaveChangesAsync();
                    return (false, false, "La salida quedó pendiente en Sigo, pero SAP no confirmó el envío. " + salida.Error, null, null);
                }
            }
            finally
            {
                SendGate.Release();
            }
        }

        private async Task<(bool ConsultaCorrecta, int? DocEntry, int? DocNum, string? Respuesta, string? Error)> BuscarEnSapAsync(string folio)
        {
            var filtro = $"U_DocMeat eq '{EscaparOData(folio)}'";
            var url = $"{Endpoint}?$select=DocEntry,DocNum&$top=1&$filter={Uri.EscapeDataString(filtro)}";
            var result = await _sap.GetAsync(url);
            if (!result.ok || string.IsNullOrWhiteSpace(result.response))
                return (false, null, null, result.response, result.error ?? "Consulta SAP sin respuesta.");

            try
            {
                using var doc = JsonDocument.Parse(result.response);
                if (!doc.RootElement.TryGetProperty("value", out var values) ||
                    values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0)
                    return (true, null, null, result.response, null);

                var first = values[0];
                int? docEntry = first.TryGetProperty("DocEntry", out var de) && de.ValueKind == JsonValueKind.Number ? de.GetInt32() : null;
                int? docNum = first.TryGetProperty("DocNum", out var dn) && dn.ValueKind == JsonValueKind.Number ? dn.GetInt32() : null;
                return (true, docEntry, docNum, result.response, null);
            }
            catch (Exception ex)
            {
                return (false, null, null, result.response, "Respuesta de SAP no interpretable: " + ex.Message);
            }
        }

        private string ValorConfig(string clave) =>
            (_configuration[$"SapServiceLayer:RefaccionesSalidas:{clave}"] ?? "").Trim();

        private void AgregarDimension(Dictionary<string, object?> linea, string propiedadSap, string claveConfig, string? seleccionado)
        {
            var valor = ValorSeleccionado(claveConfig, seleccionado);
            if (!string.IsNullOrWhiteSpace(valor))
                linea[propiedadSap] = valor;
        }

        private string ValorSeleccionado(string clave, string? seleccionado) =>
            string.IsNullOrWhiteSpace(seleccionado) ? ValorConfig(clave) : seleccionado.Trim();

        private string[] ObtenerOpciones(string clave)
        {
            var path = $"SapServiceLayer:RefaccionesSalidas:Opciones:{clave}";
            var configuradas = _configuration.GetSection(path).GetChildren()
                .Select(x => x.Value?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToList();
            var actual = ValorConfig(clave);
            if (!string.IsNullOrWhiteSpace(actual)) configuradas.Insert(0, actual);
            return configuradas.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string EtiquetaCampo(string clave) => clave switch
        {
            "WarehouseCode" => "almacén",
            "AccountCode" => "cuenta contable",
            "CostingCode" => "planta",
            "CostingCode2" => "unidad de negocio",
            "CostingCode3" => "departamento",
            "CostingCode4" => "área",
            "CostingCode5" => "empleado",
            _ => clave
        };

        private static void MarcarEnviado(RefaccionSapDocumento salida, int? docEntry, int? docNum, string? respuesta)
        {
            salida.Estado = "ENVIADO";
            salida.SapDocEntry = docEntry;
            salida.SapDocNum = docNum;
            salida.FechaEnvio = DateTime.Now;
            salida.Error = null;
            salida.RespuestaSap = respuesta;
        }

        private static (int? DocEntry, int? DocNum) LeerDocumento(string? respuesta)
        {
            if (string.IsNullOrWhiteSpace(respuesta)) return (null, null);
            using var doc = JsonDocument.Parse(respuesta);
            var root = doc.RootElement;
            int? docEntry = root.TryGetProperty("DocEntry", out var de) && de.ValueKind == JsonValueKind.Number ? de.GetInt32() : null;
            int? docNum = root.TryGetProperty("DocNum", out var dn) && dn.ValueKind == JsonValueKind.Number ? dn.GetInt32() : null;
            return (docEntry, docNum);
        }

        private static string EscaparOData(string valor) => valor.Replace("'", "''");
        private static string Limitar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
    }
}
