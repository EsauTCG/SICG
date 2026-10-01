using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plataforma_CG.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public sealed class IntegracionSapService : IIntegracionSapService
    {
        // Evita que el envío manual y el automático procesen documentos al mismo tiempo
        // dentro de la misma instancia de la aplicación.
        private static readonly SemaphoreSlim SendGate = new(1, 1);

        private readonly IConfiguration _configuration;
        private readonly ISapServiceLayerClient _sap;
        private readonly ILogger<IntegracionSapService> _logger;

        public IntegracionSapService(
            IConfiguration configuration,
            ISapServiceLayerClient sap,
            ILogger<IntegracionSapService> logger)
        {
            _configuration = configuration;
            _sap = sap;
            _logger = logger;
        }

        public async Task<IntegracionSapIndexVM> ListarAsync(IntegracionSapFiltroVM filtro)
        {
            filtro ??= new IntegracionSapFiltroVM();
            filtro.Source = NormalizeSource(filtro.Source);
            filtro.Tipo = NormalizeTipo(filtro.Tipo);
            filtro.Top = Math.Clamp(filtro.Top <= 0 ? 500 : filtro.Top, 1, 1000);

            if (filtro.Desde == default)
                filtro.Desde = DateTime.Today;

            if (filtro.Hasta == default)
                filtro.Hasta = filtro.Desde;

            if (filtro.Hasta.Date < filtro.Desde.Date)
                (filtro.Desde, filtro.Hasta) = (filtro.Hasta.Date, filtro.Desde.Date);

            var cfg = GetPlantConfig(filtro.Source);
            var tipoId = GetTipoIntegracionId(filtro.Tipo);
            var endpoint = GetEndpoint(filtro.Tipo);

            await using var cn = new SqlConnection(cfg.ConnectionString);
            await cn.OpenAsync();
            CambiarBaseSiEsNecesario(cn, cfg.Database);
            await EnsureLogTableAsync(cn);

            var rows = (await cn.QueryAsync<IntegracionSapRowVM>(
                filtro.Tipo == "SALIDA" ? SqlSalida : SqlEntrada,
                new
                {
                    TipoIntegracionId = tipoId,
                    FechaDesde = filtro.Desde.Date,
                    FechaHasta = filtro.Hasta.Date,
                    filtro.Folio,
                    filtro.Estatus,
                    IntegracionId = (int?)null,
                    filtro.Top,
                    CuentaSalida = GetSalidaAccountCode()
                },
                commandTimeout: 180)).ToList();

            PrepararRows(rows, filtro.Source, cfg.Database, filtro.Tipo, endpoint);

            return new IntegracionSapIndexVM
            {
                Filtro = filtro,
                Rows = rows,
                BaseDatos = cfg.Database,
                Endpoint = endpoint,
                TipoIntegracionId = tipoId,
                CuentaSalida = GetSalidaAccountCode()
            };
        }

        public async Task<IntegracionSapRowVM?> ObtenerAsync(
            int integracionId,
            string source,
            string tipo)
        {
            source = NormalizeSource(source);
            tipo = NormalizeTipo(tipo);

            var cfg = GetPlantConfig(source);
            var tipoId = GetTipoIntegracionId(tipo);
            var endpoint = GetEndpoint(tipo);

            await using var cn = new SqlConnection(cfg.ConnectionString);
            await cn.OpenAsync();
            CambiarBaseSiEsNecesario(cn, cfg.Database);
            await EnsureLogTableAsync(cn);

            var row = await cn.QueryFirstOrDefaultAsync<IntegracionSapRowVM>(
                tipo == "SALIDA" ? SqlSalida : SqlEntrada,
                new
                {
                    TipoIntegracionId = tipoId,
                    FechaDesde = (DateTime?)null,
                    FechaHasta = (DateTime?)null,
                    Folio = (int?)null,
                    Estatus = (int?)null,
                    IntegracionId = (int?)integracionId,
                    Top = 1,
                    CuentaSalida = GetSalidaAccountCode()
                },
                commandTimeout: 180);

            if (row != null)
                PrepararRows(new[] { row }, source, cfg.Database, tipo, endpoint);

            return row;
        }

        public async Task<IntegracionSapResultadoVM> EnviarAsync(
            int integracionId,
            string source,
            string tipo,
            string usuario,
            bool forzar = false)
        {
            source = NormalizeSource(source);
            tipo = NormalizeTipo(tipo);
            usuario = string.IsNullOrWhiteSpace(usuario) ? "SISTEMA" : usuario.Trim();

            await SendGate.WaitAsync();
            try
            {
                return await EnviarCoreAsync(
                    integracionId,
                    source,
                    tipo,
                    usuario,
                    forzar);
            }
            finally
            {
                SendGate.Release();
            }
        }

        private async Task<IntegracionSapResultadoVM> EnviarCoreAsync(
            int integracionId,
            string source,
            string tipo,
            string usuario,
            bool forzar)
        {
            var endpoint = GetEndpoint(tipo);
            var tipoId = GetTipoIntegracionId(tipo);
            var cfg = GetPlantConfig(source);

            IntegracionSapRowVM? row = null;
            string json = "{}";

            try
            {
                row = await ObtenerAsync(integracionId, source, tipo);

                if (row == null)
                {
                    return new IntegracionSapResultadoVM
                    {
                        IntegracionId = integracionId,
                        Ok = false,
                        Endpoint = endpoint,
                        Mensaje = "No se encontró la integración en la planta y tipo seleccionados."
                    };
                }

                // Si Meat ya la tiene confirmada, no se hace otro POST.
                if (row.Enviado && !forzar)
                {
                    return new IntegracionSapResultadoVM
                    {
                        IntegracionId = integracionId,
                        Ok = true,
                        YaEnviado = true,
                        Endpoint = endpoint,
                        DocEntry = row.SapDocEntry,
                        DocNum = row.SapDocNum,
                        Mensaje = "La integración ya estaba marcada como enviada. No se volvió a procesar."
                    };
                }

                if (row.CantidadLineas <= 0)
                {
                    const string msg = "El documento no contiene líneas válidas para enviar a SAP.";
                    await RegistrarIntentoAsync(
                        cfg, row, endpoint, false, null, null,
                        msg, null, row.JsonSap, usuario);

                    return ErrorResult(integracionId, endpoint, msg);
                }

                if (tipo == "SALIDA" && row.UbicacionesSinResolver > 0)
                {
                    var msg = $"La salida tiene {row.UbicacionesSinResolver} ubicación(es) sin BinAbsEntry numérico. No se envió a SAP.";

                    await RegistrarIntentoAsync(
                        cfg, row, endpoint, false, null, null,
                        msg, null, row.JsonSap, usuario);

                    return ErrorResult(integracionId, endpoint, msg);
                }

                json = row.JsonSap;

                if (tipo == "SALIDA")
                    json = AsegurarCuentaSalida(json, GetSalidaAccountCode());

                ValidarJsonAntesDeEnviar(json, tipo);

                // Idempotencia: primero se consulta SAP mediante U_DocMeat/NumAtCard.
                // Si ya existe, se sincroniza el estatus local sin crear un duplicado.
                if (!forzar)
                {
                    var existente = await BuscarDocumentoEnSapAsync(endpoint, json);
                    if (existente.Found)
                    {
                        const string msg = "El documento ya existía en SAP. Se sincronizó como enviado y no se volvió a crear.";

                        await MarcarComoEnviadoAsync(
                            cfg, row, tipoId, endpoint,
                            existente.DocEntry, existente.DocNum,
                            msg, existente.Response, json, usuario, source);

                        return new IntegracionSapResultadoVM
                        {
                            IntegracionId = integracionId,
                            Ok = true,
                            YaEnviado = true,
                            Endpoint = endpoint,
                            DocEntry = existente.DocEntry,
                            DocNum = existente.DocNum,
                            Mensaje = msg,
                            RespuestaSap = existente.Response
                        };
                    }
                }

                var sapResponse = await _sap.PostJsonAsync(endpoint, json);

                if (!sapResponse.ok)
                {
                    // SAP pudo haber creado el documento y haberse perdido la respuesta,
                    // o pudo responder que ya existía. Se verifica antes de dejarlo fallido.
                    var existenteDespuesDelError =
                        await BuscarDocumentoEnSapAsync(endpoint, json);

                    if (existenteDespuesDelError.Found)
                    {
                        var msg =
                            "SAP devolvió un error durante el POST, pero después se confirmó " +
                            "que el documento ya existe. Se dejó marcado como enviado. " +
                            $"Error SAP: {sapResponse.error ?? "sin detalle"}";

                        await MarcarComoEnviadoAsync(
                            cfg, row, tipoId, endpoint,
                            existenteDespuesDelError.DocEntry,
                            existenteDespuesDelError.DocNum,
                            msg,
                            sapResponse.response ?? existenteDespuesDelError.Response,
                            json,
                            usuario,
                            source);

                        return new IntegracionSapResultadoVM
                        {
                            IntegracionId = integracionId,
                            Ok = true,
                            YaEnviado = true,
                            Endpoint = endpoint,
                            DocEntry = existenteDespuesDelError.DocEntry,
                            DocNum = existenteDespuesDelError.DocNum,
                            Mensaje = msg,
                            Error = sapResponse.error,
                            RespuestaSap = sapResponse.response
                        };
                    }

                    var errorSap = sapResponse.error ?? "SAP rechazó la integración.";

                    await RegistrarIntentoAsync(
                        cfg, row, endpoint, false, null, null,
                        errorSap, sapResponse.response, json, usuario);

                    return new IntegracionSapResultadoVM
                    {
                        IntegracionId = integracionId,
                        Ok = false,
                        Endpoint = endpoint,
                        Mensaje = "No se pudo enviar la integración a SAP.",
                        Error = errorSap,
                        RespuestaSap = sapResponse.response
                    };
                }

                var (docEntry, docNum) = LeerDocumentoSap(sapResponse.response);

                await MarcarComoEnviadoAsync(
                    cfg, row, tipoId, endpoint,
                    docEntry, docNum,
                    "Enviado correctamente a SAP.",
                    sapResponse.response,
                    json,
                    usuario,
                    source);

                return new IntegracionSapResultadoVM
                {
                    IntegracionId = integracionId,
                    Ok = true,
                    Endpoint = endpoint,
                    DocEntry = docEntry,
                    DocNum = docNum,
                    Mensaje = "Integración enviada correctamente a SAP.",
                    RespuestaSap = sapResponse.response
                };
            }
            catch (IntegracionPendienteException ex)
            {
                // No se registra como intento fallido en IntegracionSapEnvioLog porque
                // todavía no se hizo ningún POST a SAP. La integración permanece
                // pendiente (Estatus = 0) y será reevaluada en el siguiente ciclo.
                _logger.LogInformation(
                    "Integración SAP pendiente. Id={IntegracionId} Source={Source} Tipo={Tipo}. Motivo={Motivo}",
                    integracionId, source, tipo, ex.Message);

                return ErrorResult(integracionId, endpoint, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error enviando integración SAP. Id={IntegracionId} Source={Source} Tipo={Tipo}",
                    integracionId, source, tipo);

                if (row != null)
                {
                    try
                    {
                        await RegistrarIntentoAsync(
                            cfg, row, endpoint, false, null, null,
                            ex.Message, null, json, usuario);
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogWarning(logEx,
                            "No se pudo registrar el error de integración SAP. Id={IntegracionId}",
                            integracionId);
                    }
                }

                return new IntegracionSapResultadoVM
                {
                    IntegracionId = integracionId,
                    Ok = false,
                    Endpoint = endpoint,
                    Mensaje = "Error interno al procesar la integración.",
                    Error = ex.Message
                };
            }
        }

        public async Task<List<IntegracionSapResultadoVM>> EnviarLoteAsync(
            IEnumerable<int> integracionIds,
            string source,
            string tipo,
            string usuario,
            bool forzar = false)
        {
            var ids = (integracionIds ?? Enumerable.Empty<int>())
                .Where(x => x > 0)
                .Distinct()
                .Take(200)
                .ToList();

            var resultados = new List<IntegracionSapResultadoVM>(ids.Count);

            // Intencionalmente secuencial para no saturar Service Layer y para
            // conservar un resultado independiente por integración.
            foreach (var id in ids)
            {
                resultados.Add(await EnviarAsync(id, source, tipo, usuario, forzar));
            }

            return resultados;
        }

        private async Task MarcarComoEnviadoAsync(
            PlantConfig cfg,
            IntegracionSapRowVM row,
            int tipoId,
            string endpoint,
            int? docEntry,
            int? docNum,
            string mensaje,
            string? respuestaSap,
            string json,
            string usuario,
            string source)
        {
            await using var cn = new SqlConnection(cfg.ConnectionString);
            await cn.OpenAsync();
            CambiarBaseSiEsNecesario(cn, cfg.Database);
            await EnsureLogTableAsync(cn);

            await using var tx = await cn.BeginTransactionAsync();
            try
            {
                await cn.ExecuteAsync(@"
UPDATE dbo.Integracion
SET Estatus = 1,
    FechaEnvio = GETDATE()
WHERE IntegracionId = @IntegracionId
  AND TipoIntegracionId = @TipoIntegracionId;",
                    new
                    {
                        IntegracionId = row.IntegracionId,
                        TipoIntegracionId = tipoId
                    },
                    transaction: tx);

                await InsertLogAsync(
                    cn, tx, row, endpoint, true, docEntry, docNum,
                    mensaje, respuestaSap, json, usuario, source);

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private async Task<SapExistingDocument> BuscarDocumentoEnSapAsync(
            string endpoint,
            string json)
        {
            var (uDocMeat, numAtCard) = LeerClavesDocumento(json);
            var filtros = new List<string>();

            if (!string.IsNullOrWhiteSpace(uDocMeat))
                filtros.Add($"U_DocMeat eq '{ODataEscape(uDocMeat)}'");

            // InventoryGenExits se identifica principalmente con U_DocMeat.
            // NumAtCard se usa cuando la propiedad está presente en el JSON.
            if (!string.IsNullOrWhiteSpace(numAtCard))
                filtros.Add($"NumAtCard eq '{ODataEscape(numAtCard)}'");

            if (filtros.Count == 0)
                return SapExistingDocument.NotFound;

            var filtro = string.Join(" or ", filtros);
            var query =
                $"{endpoint}?$select=DocEntry,DocNum&$top=1&$filter={Uri.EscapeDataString(filtro)}";

            var response = await _sap.GetAsync(query);
            if (!response.ok || string.IsNullOrWhiteSpace(response.response))
            {
                _logger.LogWarning(
                    "No se pudo validar duplicado en SAP. Endpoint={Endpoint} Error={Error}",
                    endpoint,
                    response.error);

                return SapExistingDocument.NotFound;
            }

            try
            {
                using var doc = JsonDocument.Parse(response.response);
                var root = doc.RootElement;

                if (!root.TryGetProperty("value", out var value) ||
                    value.ValueKind != JsonValueKind.Array ||
                    value.GetArrayLength() == 0)
                {
                    return SapExistingDocument.NotFound;
                }

                var first = value[0];

                int? docEntry =
                    first.TryGetProperty("DocEntry", out var de) &&
                    de.ValueKind == JsonValueKind.Number
                        ? de.GetInt32()
                        : null;

                int? docNum =
                    first.TryGetProperty("DocNum", out var dn) &&
                    dn.ValueKind == JsonValueKind.Number
                        ? dn.GetInt32()
                        : null;

                return new SapExistingDocument(
                    true,
                    docEntry,
                    docNum,
                    response.response);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "No se pudo interpretar la búsqueda de duplicados en SAP. Endpoint={Endpoint}",
                    endpoint);

                return SapExistingDocument.NotFound;
            }
        }

        private static (string? UDocMeat, string? NumAtCard) LeerClavesDocumento(
            string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? uDocMeat =
                    root.TryGetProperty("U_DocMeat", out var udf) &&
                    udf.ValueKind == JsonValueKind.String
                        ? udf.GetString()
                        : null;

                string? numAtCard =
                    root.TryGetProperty("NumAtCard", out var num) &&
                    num.ValueKind == JsonValueKind.String
                        ? num.GetString()
                        : null;

                return (uDocMeat, numAtCard);
            }
            catch
            {
                return (null, null);
            }
        }

        private static string ODataEscape(string value) =>
            (value ?? string.Empty).Replace("'", "''");

        private async Task RegistrarIntentoAsync(
            PlantConfig cfg,
            IntegracionSapRowVM row,
            string endpoint,
            bool exitoso,
            int? docEntry,
            int? docNum,
            string mensaje,
            string? respuestaSap,
            string? jsonEnviado,
            string usuario)
        {
            await using var cn = new SqlConnection(cfg.ConnectionString);
            await cn.OpenAsync();
            CambiarBaseSiEsNecesario(cn, cfg.Database);
            await EnsureLogTableAsync(cn);

            await InsertLogAsync(
                cn, null, row, endpoint, exitoso, docEntry, docNum,
                mensaje, respuestaSap, jsonEnviado, usuario, row.Planta);
        }

        private static Task InsertLogAsync(
            SqlConnection cn,
            System.Data.Common.DbTransaction? tx,
            IntegracionSapRowVM row,
            string endpoint,
            bool exitoso,
            int? docEntry,
            int? docNum,
            string mensaje,
            string? respuestaSap,
            string? jsonEnviado,
            string usuario,
            string source)
        {
            return cn.ExecuteAsync(@"
INSERT INTO dbo.IntegracionSapEnvioLog
(
    IntegracionId,
    TipoIntegracionId,
    Planta,
    Endpoint,
    Exitoso,
    DocEntry,
    DocNum,
    Mensaje,
    RespuestaSap,
    JsonEnviado,
    Usuario,
    FechaIntento
)
VALUES
(
    @IntegracionId,
    @TipoIntegracionId,
    @Planta,
    @Endpoint,
    @Exitoso,
    @DocEntry,
    @DocNum,
    @Mensaje,
    @RespuestaSap,
    @JsonEnviado,
    @Usuario,
    SYSDATETIME()
);",
                new
                {
                    row.IntegracionId,
                    row.TipoIntegracionId,
                    Planta = source,
                    endpoint,
                    exitoso,
                    docEntry,
                    docNum,
                    Mensaje = Truncate(mensaje, 2000),
                    RespuestaSap = respuestaSap,
                    JsonEnviado = jsonEnviado,
                    Usuario = Truncate(usuario, 150)
                },
                transaction: tx);
        }

        private static async Task EnsureLogTableAsync(SqlConnection cn)
        {
            await cn.ExecuteAsync(@"
IF OBJECT_ID('dbo.IntegracionSapEnvioLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegracionSapEnvioLog
    (
        Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_IntegracionSapEnvioLog PRIMARY KEY,
        IntegracionId INT NOT NULL,
        TipoIntegracionId INT NOT NULL,
        Planta VARCHAR(10) NOT NULL,
        Endpoint VARCHAR(80) NOT NULL,
        Exitoso BIT NOT NULL,
        DocEntry INT NULL,
        DocNum INT NULL,
        Mensaje NVARCHAR(2000) NULL,
        RespuestaSap NVARCHAR(MAX) NULL,
        JsonEnviado NVARCHAR(MAX) NULL,
        Usuario NVARCHAR(150) NULL,
        FechaIntento DATETIME2(0) NOT NULL
            CONSTRAINT DF_IntegracionSapEnvioLog_Fecha DEFAULT SYSDATETIME()
    );

    CREATE INDEX IX_IntegracionSapEnvioLog_Integracion
        ON dbo.IntegracionSapEnvioLog
        (IntegracionId, TipoIntegracionId, FechaIntento DESC);
END;");
        }


        private static void CambiarBaseSiEsNecesario(SqlConnection cn, string database)
        {
            if (!string.Equals(cn.Database, database, StringComparison.OrdinalIgnoreCase))
                cn.ChangeDatabase(database);
        }

        private void PrepararRows(
            IEnumerable<IntegracionSapRowVM> rows,
            string source,
            string database,
            string tipo,
            string endpoint)
        {
            foreach (var row in rows)
            {
                row.Planta = source;
                row.BaseDatos = database;
                row.Tipo = tipo;
                row.Endpoint = endpoint;

                if (tipo == "SALIDA")
                    row.CuentaContable = GetSalidaAccountCode();
            }
        }

        private PlantConfig GetPlantConfig(string source)
        {
            source = NormalizeSource(source);

            var defaultConnectionName = source == "TIF"
                ? "CadenaMeatTIF"
                : "CadenaMeatP1";

            var defaultDatabase = source == "TIF"
                ? "TIF_CommerciaMobile"
                : "Next";

            var section = _configuration.GetSection($"IntegracionesSap:{source}");
            var connectionName = section["ConnectionStringName"] ?? defaultConnectionName;
            var database = section["Database"] ?? defaultDatabase;
            var connectionString = _configuration.GetConnectionString(connectionName);

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    $"No existe la cadena de conexión '{connectionName}' para la planta {source}.");

            return new PlantConfig(source, database, connectionName, connectionString);
        }

        private string GetSalidaAccountCode()
        {
            return (_configuration["IntegracionesSap:SalidaAccountCode"] ?? "21010300").Trim();
        }

        private static string NormalizeSource(string? source)
        {
            return string.Equals(source?.Trim(), "TIF", StringComparison.OrdinalIgnoreCase)
                ? "TIF"
                : "P1";
        }

        private static string NormalizeTipo(string? tipo)
        {
            var value = (tipo ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(' ', '_')
                .Replace('-', '_');

            return value switch
            {
                "SALIDA" => "SALIDA",
                "TRANSFERENCIA" => "TRANSFERENCIA_ENTRADA",
                "TRANSFERENCIA_ENTRADA" => "TRANSFERENCIA_ENTRADA",
                "ENTRADA_TRANSFERENCIA" => "TRANSFERENCIA_ENTRADA",
                _ => "ENTRADA"
            };
        }

        private static int GetTipoIntegracionId(string tipo)
        {
            return NormalizeTipo(tipo) switch
            {
                "TRANSFERENCIA_ENTRADA" => 2,
                "SALIDA" => 4,
                _ => 1
            };
        }

        private static string GetEndpoint(string tipo) =>
            NormalizeTipo(tipo) == "SALIDA"
                ? "InventoryGenExits"
                : "PurchaseDeliveryNotes";

        private static IntegracionSapResultadoVM ErrorResult(
            int integracionId,
            string endpoint,
            string mensaje)
        {
            return new IntegracionSapResultadoVM
            {
                IntegracionId = integracionId,
                Ok = false,
                Endpoint = endpoint,
                Mensaje = mensaje,
                Error = mensaje
            };
        }

        private static string AsegurarCuentaSalida(string json, string accountCode)
        {
            var root = JsonNode.Parse(json)?.AsObject()
                ?? throw new InvalidOperationException("El JSON de salida no es un objeto válido.");

            var lines = root["DocumentLines"] as JsonArray
                ?? throw new InvalidOperationException("El JSON de salida no contiene DocumentLines.");

            if (lines.Count == 0)
                throw new InvalidOperationException("El JSON de salida no contiene líneas.");

            foreach (var node in lines)
            {
                if (node is JsonObject line)
                    line["AccountCode"] = accountCode;
            }

            return root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }

        private static void ValidarJsonAntesDeEnviar(string json, string tipo)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("El JSON SAP no contiene un objeto raíz.");

            if (!root.TryGetProperty("DocumentLines", out var lines) ||
                lines.ValueKind != JsonValueKind.Array ||
                lines.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("El JSON SAP no contiene DocumentLines válidas.");
            }

            var tipoNormalizado = NormalizeTipo(tipo);

            if (tipoNormalizado == "SALIDA")
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (!line.TryGetProperty("AccountCode", out var account) ||
                        account.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(account.GetString()))
                    {
                        throw new InvalidOperationException(
                            "Una línea de salida no contiene AccountCode.");
                    }
                }

                return;
            }

            // ENTRADA / TRANSFERENCIA_ENTRADA:
            // - Si la línea viene basada en una OC (BaseEntry), SAP toma el costo
            //   desde el documento base y no exigimos PriceAfterVAT.
            // - Si NO viene basada en OC, debe existir un precio/costo > 0.
            //   Si todavía no existe, NO se hace POST y la integración se queda
            //   pendiente para que el automático la vuelva a evaluar después.
            var indice = 0;

            foreach (var line in lines.EnumerateArray())
            {
                indice++;

                if (TieneBaseEntryValido(line))
                    continue;

                if (TienePrecioPositivo(line, "PriceAfterVAT"))
                    continue;

                var itemCode =
                    line.TryGetProperty("ItemCode", out var item) &&
                    item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : null;

                var sku = string.IsNullOrWhiteSpace(itemCode)
                    ? "SIN-SKU"
                    : itemCode;

                throw new IntegracionPendienteException(
                    $"Pendiente sin costo: la línea {indice} (SKU {sku}) no está basada en una orden de compra " +
                    "y todavía no tiene PriceAfterVAT/UnitPrice mayor a 0. No se envió nada a SAP. " +
                    "La integración permanecerá pendiente y se volverá a evaluar cuando aparezca el costo.");
            }
        }

        private static bool TieneBaseEntryValido(JsonElement line)
        {
            if (!line.TryGetProperty("BaseEntry", out var baseEntry))
                return false;

            if (baseEntry.ValueKind == JsonValueKind.Number &&
                baseEntry.TryGetInt32(out var numero))
            {
                return numero > 0;
            }

            if (baseEntry.ValueKind == JsonValueKind.String &&
                int.TryParse(baseEntry.GetString(), out numero))
            {
                return numero > 0;
            }

            return false;
        }

        private static bool TienePrecioPositivo(JsonElement line, string propertyName)
        {
            if (!line.TryGetProperty(propertyName, out var price))
                return false;

            if (price.ValueKind == JsonValueKind.Number &&
                price.TryGetDecimal(out var numero))
            {
                return numero > 0m;
            }

            if (price.ValueKind == JsonValueKind.String &&
                decimal.TryParse(
                    price.GetString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out numero))
            {
                return numero > 0m;
            }

            return false;
        }

        private static (int? docEntry, int? docNum) LeerDocumentoSap(string? response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return (null, null);

            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                int? docEntry = root.TryGetProperty("DocEntry", out var de) &&
                                de.ValueKind == JsonValueKind.Number
                    ? de.GetInt32()
                    : null;

                int? docNum = root.TryGetProperty("DocNum", out var dn) &&
                              dn.ValueKind == JsonValueKind.Number
                    ? dn.GetInt32()
                    : null;

                return (docEntry, docNum);
            }
            catch
            {
                return (null, null);
            }
        }

        private static string? Truncate(string? value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= length)
                return value;

            return value[..length];
        }

        private sealed class IntegracionPendienteException : InvalidOperationException
        {
            public IntegracionPendienteException(string message)
                : base(message)
            {
            }
        }

        private sealed record SapExistingDocument(
            bool Found,
            int? DocEntry,
            int? DocNum,
            string? Response)
        {
            public static readonly SapExistingDocument NotFound =
                new(false, null, null, null);
        }

        private sealed record PlantConfig(
            string Source,
            string Database,
            string ConnectionName,
            string ConnectionString);

        private const string SqlEntrada = @"
;WITH Integraciones AS
(
    SELECT TOP (@Top)
        I.IntegracionId,
        I.TipoIntegracionId,
        I.Folio,
        I.FechaDesde,
        I.FechaHasta,
        I.FechaEnvio,
        I.FechaHora,
        I.Estatus,
        I.SucursalId,
        CONVERT(DATE, I.FechaDesde) AS FechaDocumento,
        CONVERT(NVARCHAR(MAX), I.JsonRequest) AS JsonRequest
    FROM dbo.Integracion I
    WHERE I.TipoIntegracionId = @TipoIntegracionId
      AND (@IntegracionId IS NULL OR I.IntegracionId = @IntegracionId)
      AND (@FechaDesde IS NULL OR CONVERT(DATE, I.FechaDesde) >= @FechaDesde)
      AND (@FechaHasta IS NULL OR CONVERT(DATE, I.FechaDesde) <= @FechaHasta)
      AND (@Folio IS NULL OR I.Folio = @Folio)
      AND (@Estatus IS NULL OR I.Estatus = @Estatus)
      AND ISJSON(CONVERT(NVARCHAR(MAX), I.JsonRequest)) = 1
    ORDER BY I.FechaDesde DESC, I.IntegracionId DESC
),
Documentos AS
(
    SELECT
        I.*,
        D.NumAtCard,
        D.Comments,
        D.CardCode,
        D.UDF,
        D.Lines
    FROM Integraciones I
    CROSS APPLY OPENJSON(I.JsonRequest, '$.Document')
    WITH
    (
        NumAtCard NVARCHAR(100) '$.NumAtCard',
        Comments  NVARCHAR(500) '$.Comments',
        CardCode  NVARCHAR(50)  '$.CardCode',
        UDF       NVARCHAR(MAX) '$.UDF'   AS JSON,
        Lines     NVARCHAR(MAX) '$.Lines' AS JSON
    ) D
),
DocumentosPreparados AS
(
    SELECT
        D.*,
        U.U_DocMeat
    FROM Documentos D
    OUTER APPLY
    (
        SELECT MAX(CASE WHEN X.Name = 'U_DocMeat' THEN X.Value END) AS U_DocMeat
        FROM OPENJSON(D.UDF)
        WITH
        (
            Name  NVARCHAR(100) '$.Name',
            Value NVARCHAR(255) '$.Value'
        ) X
    ) U
),
LineasOrigen AS
(
    SELECT
        D.IntegracionId,
        L.LineNum,
        L.ItemCode,
        L.BaseLineOv,
        L.BaseEntryOC,
        COALESCE(
            CASE WHEN L.UnitPriceLower > 0 THEN L.UnitPriceLower END,
            CASE WHEN L.UnitPriceUpper > 0 THEN L.UnitPriceUpper END
        ) AS UnitPrice,
        L.PriceAfterVAT,
        L.WhsCode,
        L.Quantity,
        L.Batchs,
        ROW_NUMBER() OVER
        (
            PARTITION BY D.IntegracionId
            ORDER BY ISNULL(L.LineNum, 2147483647), L.ItemCode
        ) - 1 AS JsonLineNumber
    FROM DocumentosPreparados D
    CROSS APPLY OPENJSON(D.Lines)
    WITH
    (
        LineNum       INT           '$.LineNum',
        ItemCode      NVARCHAR(50)  '$.ItemCode',
        BaseLineOv     INT           '$.BaseLineOv',
        BaseEntryOC    NVARCHAR(100) '$.BaseEntryOC',
        UnitPriceLower DECIMAL(20,4) '$.unitPrice',
        UnitPriceUpper DECIMAL(20,4) '$.UnitPrice',
        PriceAfterVAT  DECIMAL(20,4) '$.PriceAfterVAT',
        WhsCode       NVARCHAR(50)  '$.WhsCode',
        Quantity      DECIMAL(20,4) '$.Quantity',
        Batchs        NVARCHAR(MAX) '$.Batchs' AS JSON
    ) L
    WHERE NULLIF(LTRIM(RTRIM(L.ItemCode)), '') IS NOT NULL
      AND ISNULL(L.Quantity, 0) > 0
),
LotesOrigen AS
(
    SELECT
        L.IntegracionId,
        L.JsonLineNumber,
        B.LineNum AS BatchLineNum,
        B.BatchNumber,
        B.Quantity,
        ROW_NUMBER() OVER
        (
            PARTITION BY L.IntegracionId, L.JsonLineNumber
            ORDER BY ISNULL(B.LineNum, 2147483647), B.BatchNumber
        ) - 1 AS BatchIndex
    FROM LineasOrigen L
    CROSS APPLY OPENJSON(L.Batchs)
    WITH
    (
        LineNum     INT           '$.LineNum',
        BatchNumber NVARCHAR(100) '$.BatchNumber',
        Quantity    DECIMAL(20,4) '$.Quantity'
    ) B
    WHERE NULLIF(LTRIM(RTRIM(B.BatchNumber)), '') IS NOT NULL
      AND ISNULL(B.Quantity, 0) > 0
)
SELECT
    D.IntegracionId,
    D.TipoIntegracionId,
    D.Folio,
    D.FechaDocumento,
    D.Estatus,
    D.CardCode AS SocioNegocio,
    D.NumAtCard AS Referencia,
    (SELECT COUNT(*) FROM LineasOrigen L WHERE L.IntegracionId = D.IntegracionId) AS CantidadLineas,
    (SELECT COUNT(*) FROM LotesOrigen B WHERE B.IntegracionId = D.IntegracionId) AS CantidadLotes,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM LineasOrigen L
        WHERE L.IntegracionId = D.IntegracionId
          AND TRY_CONVERT(INT, L.BaseEntryOC) IS NOT NULL
    ) THEN 1 ELSE 0 END AS BIT) AS TieneOrdenCompra,
    0 AS UbicacionesSinResolver,
    CAST(NULL AS NVARCHAR(50)) AS CuentaContable,
    LOG.FechaIntento AS UltimoIntento,
    LOG.Exitoso AS UltimoExitoso,
    LOG.DocEntry AS SapDocEntry,
    LOG.DocNum AS SapDocNum,
    LOG.Mensaje AS UltimoMensaje,
    (
        SELECT
            D.CardCode AS [CardCode],
            CONVERT(CHAR(10), D.FechaDocumento, 23) AS [DocDate],
            CONVERT(CHAR(10), D.FechaDocumento, 23) AS [DocDueDate],
            CONVERT(CHAR(10), D.FechaDocumento, 23) AS [TaxDate],
            D.NumAtCard AS [NumAtCard],
            CASE
                WHEN NULLIF(LTRIM(RTRIM(D.Comments)), '') IS NOT NULL THEN D.Comments
                ELSE CASE
                    WHEN D.TipoIntegracionId = 2 THEN
                        CONCAT('Transferencia de entrada generada desde Sigo. Folio: ', D.Folio,
                               ' / Referencia: ', ISNULL(D.NumAtCard, ''))
                    ELSE
                        CONCAT('Entrada de mercancía generada desde Sigo. Folio: ', D.Folio,
                               ' / Referencia: ', ISNULL(D.NumAtCard, ''))
                END
            END AS [Comments],
            D.U_DocMeat AS [U_DocMeat],
            JSON_QUERY
            (
                (
                    SELECT
                        CASE WHEN TRY_CONVERT(INT, L.BaseEntryOC) IS NOT NULL THEN 22 END AS [BaseType],
                        TRY_CONVERT(INT, L.BaseEntryOC) AS [BaseEntry],
                        CASE
                            WHEN TRY_CONVERT(INT, L.BaseEntryOC) IS NOT NULL
                                THEN ISNULL(L.BaseLineOv, L.LineNum)
                        END AS [BaseLine],
                        CASE
                            WHEN TRY_CONVERT(INT, L.BaseEntryOC) IS NULL THEN L.ItemCode
                        END AS [ItemCode],
                        CAST(L.Quantity AS DECIMAL(20,4)) AS [Quantity],
                        L.WhsCode AS [WarehouseCode],
                        CASE
                            WHEN TRY_CONVERT(INT, L.BaseEntryOC) IS NULL
                                THEN CAST(
                                    COALESCE(
                                        CASE WHEN L.PriceAfterVAT > 0 THEN L.PriceAfterVAT END,
                                        CASE WHEN L.UnitPrice > 0 THEN L.UnitPrice END
                                    )
                                    AS DECIMAL(20,4)
                                )
                        END AS [PriceAfterVAT],
                        JSON_QUERY
                        (
                            COALESCE
                            (
                                NULLIF
                                (
                                    (
                                        SELECT
                                            B.BatchNumber AS [BatchNumber],
                                            CAST(B.Quantity AS DECIMAL(20,4)) AS [Quantity],
                                            L.JsonLineNumber AS [BaseLineNumber]
                                        FROM LotesOrigen B
                                        WHERE B.IntegracionId = L.IntegracionId
                                          AND B.JsonLineNumber = L.JsonLineNumber
                                        ORDER BY B.BatchIndex
                                        FOR JSON PATH
                                    ),
                                    '[]'
                                ),
                                '[]'
                            )
                        ) AS [BatchNumbers]
                    FROM LineasOrigen L
                    WHERE L.IntegracionId = D.IntegracionId
                    ORDER BY L.JsonLineNumber
                    FOR JSON PATH
                )
            ) AS [DocumentLines]
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    ) AS JsonSap
FROM DocumentosPreparados D
OUTER APPLY
(
    SELECT TOP (1)
        L.FechaIntento,
        L.Exitoso,
        L.DocEntry,
        L.DocNum,
        L.Mensaje
    FROM dbo.IntegracionSapEnvioLog L
    WHERE L.IntegracionId = D.IntegracionId
      AND L.TipoIntegracionId = D.TipoIntegracionId
    ORDER BY L.Id DESC
) LOG
ORDER BY D.FechaDocumento DESC, D.IntegracionId DESC;";

        private const string SqlSalida = @"
;WITH Integraciones AS
(
    SELECT TOP (@Top)
        I.IntegracionId,
        I.TipoIntegracionId,
        I.Folio,
        I.FechaDesde,
        I.FechaHasta,
        I.FechaEnvio,
        I.FechaHora,
        I.Estatus,
        I.SucursalId,
        CONVERT(DATE, I.FechaDesde) AS FechaDocumento,
        CONVERT(NVARCHAR(MAX), I.JsonRequest) AS JsonRequest
    FROM dbo.Integracion I
    WHERE I.TipoIntegracionId = @TipoIntegracionId
      AND (@IntegracionId IS NULL OR I.IntegracionId = @IntegracionId)
      AND (@FechaDesde IS NULL OR CONVERT(DATE, I.FechaDesde) >= @FechaDesde)
      AND (@FechaHasta IS NULL OR CONVERT(DATE, I.FechaDesde) <= @FechaHasta)
      AND (@Folio IS NULL OR I.Folio = @Folio)
      AND (@Estatus IS NULL OR I.Estatus = @Estatus)
      AND ISJSON(CONVERT(NVARCHAR(MAX), I.JsonRequest)) = 1
    ORDER BY I.FechaDesde DESC, I.IntegracionId DESC
),
Documentos AS
(
    SELECT
        I.*,
        D.NumAtCard,
        D.Comments,
        D.UDF,
        D.Lines
    FROM Integraciones I
    CROSS APPLY OPENJSON(I.JsonRequest, '$.Document')
    WITH
    (
        NumAtCard NVARCHAR(100) '$.NumAtCard',
        Comments  NVARCHAR(500) '$.Comments',
        UDF       NVARCHAR(MAX) '$.UDF' AS JSON,
        Lines     NVARCHAR(MAX) '$.Lines' AS JSON
    ) D
),
DocumentosPreparados AS
(
    SELECT
        D.*,
        U.U_DocMeat
    FROM Documentos D
    OUTER APPLY
    (
        SELECT MAX(CASE WHEN X.Name = 'U_DocMeat' THEN X.Value END) AS U_DocMeat
        FROM OPENJSON(D.UDF)
        WITH
        (
            Name  NVARCHAR(100) '$.Name',
            Value NVARCHAR(255) '$.Value'
        ) X
    ) U
),
LineasOrigen AS
(
    SELECT
        D.IntegracionId,
        L.LineNum AS LineNumOrigen,
        L.ItemCode,
        L.PriceAfterVAT,
        L.WhsCode,
        L.Quantity,
        L.Batchs,
        ROW_NUMBER() OVER
        (
            PARTITION BY D.IntegracionId
            ORDER BY ISNULL(L.LineNum, 2147483647), L.ItemCode
        ) - 1 AS JsonLineNumber
    FROM DocumentosPreparados D
    CROSS APPLY OPENJSON(D.Lines)
    WITH
    (
        LineNum       INT           '$.LineNum',
        ItemCode      NVARCHAR(50)  '$.ItemCode',
        PriceAfterVAT DECIMAL(20,4) '$.PriceAfterVAT',
        WhsCode       NVARCHAR(50)  '$.WhsCode',
        Quantity      DECIMAL(20,4) '$.Quantity',
        Batchs        NVARCHAR(MAX) '$.Batchs' AS JSON
    ) L
    WHERE NULLIF(LTRIM(RTRIM(L.ItemCode)), '') IS NOT NULL
      AND ISNULL(L.Quantity, 0) > 0
),
LotesBase AS
(
    SELECT
        L.IntegracionId,
        L.JsonLineNumber,
        L.LineNumOrigen,
        L.ItemCode,
        L.WhsCode,
        B.LineNum AS BatchLineNumOrigen,
        B.BatchNumber,
        B.Quantity AS BatchQuantity,
        B.FromLocation
    FROM LineasOrigen L
    CROSS APPLY OPENJSON(L.Batchs)
    WITH
    (
        LineNum      INT            '$.LineNum',
        BatchNumber  NVARCHAR(100)  '$.BatchNumber',
        Quantity     DECIMAL(20,4)  '$.Quantity',
        FromLocation NVARCHAR(MAX)  '$.FromLocation' AS JSON
    ) B
    WHERE NULLIF(LTRIM(RTRIM(B.BatchNumber)), '') IS NOT NULL
      AND ISNULL(B.Quantity, 0) > 0
),
LotesOrigen AS
(
    SELECT
        LB.*,
        ROW_NUMBER() OVER
        (
            PARTITION BY LB.IntegracionId, LB.JsonLineNumber
            ORDER BY ISNULL(LB.BatchLineNumOrigen, 2147483647), LB.BatchNumber
        ) - 1 AS BatchIndex
    FROM LotesBase LB
),
UbicacionesOrigen AS
(
    SELECT
        LT.IntegracionId,
        LT.JsonLineNumber,
        LT.BatchIndex,
        LT.BatchNumber,
        LT.WhsCode,
        U.Location,
        U.Quantity AS LocationQuantity,
        TRY_CONVERT(INT, NULLIF(LTRIM(RTRIM(U.Location)), '')) AS BinAbsEntry
    FROM LotesOrigen LT
    CROSS APPLY OPENJSON(LT.FromLocation)
    WITH
    (
        Location NVARCHAR(100) '$.Location',
        Quantity DECIMAL(20,4) '$.Quantity'
    ) U
    WHERE NULLIF(LTRIM(RTRIM(U.Location)), '') IS NOT NULL
      AND ISNULL(U.Quantity, 0) > 0
)
SELECT
    D.IntegracionId,
    D.TipoIntegracionId,
    D.Folio,
    D.FechaDocumento,
    D.Estatus,
    CAST(NULL AS NVARCHAR(50)) AS SocioNegocio,
    D.NumAtCard AS Referencia,
    (SELECT COUNT(*) FROM LineasOrigen L WHERE L.IntegracionId = D.IntegracionId) AS CantidadLineas,
    (SELECT COUNT(*) FROM LotesOrigen B WHERE B.IntegracionId = D.IntegracionId) AS CantidadLotes,
    CAST(0 AS BIT) AS TieneOrdenCompra,
    (
        SELECT COUNT(*)
        FROM UbicacionesOrigen U
        WHERE U.IntegracionId = D.IntegracionId
          AND U.BinAbsEntry IS NULL
    ) AS UbicacionesSinResolver,
    @CuentaSalida AS CuentaContable,
    LOG.FechaIntento AS UltimoIntento,
    LOG.Exitoso AS UltimoExitoso,
    LOG.DocEntry AS SapDocEntry,
    LOG.DocNum AS SapDocNum,
    LOG.Mensaje AS UltimoMensaje,
    (
        SELECT
            CONVERT(CHAR(10), D.FechaDocumento, 23) AS [DocDate],
            CONVERT(CHAR(10), D.FechaDocumento, 23) AS [DocDueDate],
            CONVERT(CHAR(10), D.FechaDocumento, 23) AS [TaxDate],
            CASE
                WHEN NULLIF(LTRIM(RTRIM(D.Comments)), '') IS NOT NULL THEN D.Comments
                ELSE CONCAT('Salida de mercancía generada desde Sigo. Folio: ', D.Folio,
                            ' / Lote: ', ISNULL(D.NumAtCard, ''))
            END AS [Comments],
            D.U_DocMeat AS [U_DocMeat],
            JSON_QUERY
            (
                (
                    SELECT
                        L.ItemCode AS [ItemCode],
                        CAST(L.Quantity AS DECIMAL(20,4)) AS [Quantity],
                        L.WhsCode AS [WarehouseCode],
                        @CuentaSalida AS [AccountCode],
                        JSON_QUERY
                        (
                            NULLIF
                            (
                                (
                                    SELECT
                                        B.BatchNumber AS [BatchNumber],
                                        CAST(B.BatchQuantity AS DECIMAL(20,4)) AS [Quantity],
                                        L.JsonLineNumber AS [BaseLineNumber]
                                    FROM LotesOrigen B
                                    WHERE B.IntegracionId = L.IntegracionId
                                      AND B.JsonLineNumber = L.JsonLineNumber
                                    ORDER BY B.BatchIndex
                                    FOR JSON PATH
                                ),
                                '[]'
                            )
                        ) AS [BatchNumbers],
                        JSON_QUERY
                        (
                            NULLIF
                            (
                                (
                                    SELECT
                                        U.BinAbsEntry AS [BinAbsEntry],
                                        CAST(U.LocationQuantity AS DECIMAL(20,4)) AS [Quantity],
                                        L.JsonLineNumber AS [BaseLineNumber],
                                        U.BatchIndex AS [SerialAndBatchNumbersBaseLine]
                                    FROM UbicacionesOrigen U
                                    WHERE U.IntegracionId = L.IntegracionId
                                      AND U.JsonLineNumber = L.JsonLineNumber
                                      AND U.BinAbsEntry IS NOT NULL
                                    ORDER BY U.BatchIndex, U.BinAbsEntry
                                    FOR JSON PATH
                                ),
                                '[]'
                            )
                        ) AS [DocumentLinesBinAllocations]
                    FROM LineasOrigen L
                    WHERE L.IntegracionId = D.IntegracionId
                    ORDER BY L.JsonLineNumber
                    FOR JSON PATH
                )
            ) AS [DocumentLines]
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    ) AS JsonSap
FROM DocumentosPreparados D
OUTER APPLY
(
    SELECT TOP (1)
        L.FechaIntento,
        L.Exitoso,
        L.DocEntry,
        L.DocNum,
        L.Mensaje
    FROM dbo.IntegracionSapEnvioLog L
    WHERE L.IntegracionId = D.IntegracionId
      AND L.TipoIntegracionId = D.TipoIntegracionId
    ORDER BY L.Id DESC
) LOG
ORDER BY D.FechaDocumento DESC, D.IntegracionId DESC;";
    }
}
