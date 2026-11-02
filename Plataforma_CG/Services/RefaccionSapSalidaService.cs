using Microsoft.EntityFrameworkCore;
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

                var salida = await _context.RefaccionSapSalidas
                    .FirstOrDefaultAsync(x => x.Folio == folio);
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

        private static void MarcarEnviado(RefaccionSapSalida salida, int? docEntry, int? docNum, string? respuesta)
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
