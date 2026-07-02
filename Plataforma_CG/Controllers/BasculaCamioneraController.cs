using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers
{
    [Route("BasculaCamionera")]
    public class BasculaCamioneraController : Controller
    {
        private readonly ILogger<BasculaCamioneraController> _logger;
        private readonly IConfiguration _configuration;

        private static readonly List<BasculaMovimientoDto> _movimientos = new();
        private static readonly List<BasculaBitacoraDto> _bitacora = new();

        public BasculaCamioneraController(
            ILogger<BasculaCamioneraController> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        [HttpGet("BasculaCamionera")]
        public IActionResult BasculaCamionera()
        {
            return View("~/Views/BasculaCamionera/BasculaCamionera.cshtml");
        }

        [HttpGet("Ping")]
        public IActionResult Ping()
        {
            return Ok(new
            {
                ok = true,
                msg = "BasculaCamioneraController funcionando",
                fecha = DateTime.Now
            });
        }

        [HttpGet("Listar")]
        public IActionResult Listar()
        {
            var rows = _movimientos
                .OrderByDescending(x => x.FechaEntrada)
                .ToList();

            return Ok(new
            {
                ok = true,
                total = rows.Count,
                rows
            });
        }

        [HttpPost("GuardarEntrada")]
        [ValidateAntiForgeryToken]
        public IActionResult GuardarEntrada([FromBody] BasculaMovimientoDto dto)
        {
            if (dto == null)
                return BadRequest(new { ok = false, msg = "Solicitud vacía." });

            if (string.IsNullOrWhiteSpace(dto.Tercero))
                return BadRequest(new { ok = false, msg = "Capture proveedor / cliente." });

            if (string.IsNullOrWhiteSpace(dto.Producto))
                return BadRequest(new { ok = false, msg = "Capture producto." });

            if (string.IsNullOrWhiteSpace(dto.Placas))
                return BadRequest(new { ok = false, msg = "Capture placas." });

            if (dto.PesoEntrada <= 0)
                return BadRequest(new { ok = false, msg = "El peso de entrada debe ser mayor a cero." });

            dto.Folio = string.IsNullOrWhiteSpace(dto.Folio)
                ? NuevoFolio()
                : dto.Folio.Trim();

            dto.Estatus = "PENDIENTE";
            dto.PesoSalida = 0;
            dto.PesoNeto = 0;
            dto.FechaEntrada = dto.FechaEntrada == default ? DateTime.Now : dto.FechaEntrada;
            dto.FechaSalida = null;
            dto.Usuario = User?.Identity?.Name ?? dto.Usuario ?? "Usuario SIGO";

            var existente = _movimientos.FirstOrDefault(x => x.Folio == dto.Folio);

            if (existente == null)
                _movimientos.Add(dto);
            else
                Copiar(dto, existente);

            RegistrarBitacora(dto.Folio, "Guardó entrada de báscula");

            return Ok(new
            {
                ok = true,
                msg = "Entrada guardada.",
                folio = dto.Folio
            });
        }

        [HttpPost("CerrarSalida")]
        [ValidateAntiForgeryToken]
        public IActionResult CerrarSalida([FromBody] BasculaMovimientoDto dto)
        {
            if (dto == null)
                return BadRequest(new { ok = false, msg = "Solicitud vacía." });

            if (string.IsNullOrWhiteSpace(dto.Folio))
                return BadRequest(new { ok = false, msg = "Folio inválido." });

            if (dto.PesoEntrada <= 0 || dto.PesoSalida <= 0)
                return BadRequest(new { ok = false, msg = "Capture peso de entrada y peso de salida." });

            var existente = _movimientos.FirstOrDefault(x => x.Folio == dto.Folio);

            if (existente == null)
                return NotFound(new { ok = false, msg = "No existe la entrada pendiente." });

            dto.Estatus = "CERRADO";
            dto.PesoNeto = Math.Abs(dto.PesoEntrada - dto.PesoSalida);
            dto.FechaEntrada = existente.FechaEntrada;
            dto.FechaSalida = DateTime.Now;
            dto.Usuario = User?.Identity?.Name ?? dto.Usuario ?? "Usuario SIGO";

            Copiar(dto, existente);
            RegistrarBitacora(dto.Folio, "Cerró salida y calculó peso neto");

            return Ok(new
            {
                ok = true,
                msg = "Salida cerrada.",
                folio = dto.Folio,
                neto = dto.PesoNeto
            });
        }

        [HttpGet("Bitacora")]
        public IActionResult Bitacora()
        {
            return Ok(new
            {
                ok = true,
                rows = _bitacora
                    .OrderByDescending(x => x.Fecha)
                    .ToList()
            });
        }

        [HttpGet("Exportar")]
        public IActionResult Exportar()
        {
            var csv = "Folio,Estatus,TipoMovimiento,Clasificacion,Tercero,CodigoSap,Placas,Producto,Sku,Documento,PesoEntrada,PesoSalida,PesoNeto,FechaEntrada,FechaSalida,Usuario\r\n";

            foreach (var r in _movimientos.OrderByDescending(x => x.FechaEntrada))
            {
                csv += $"{Csv(r.Folio)},{Csv(r.Estatus)},{Csv(r.TipoMovimiento)},{Csv(r.Clasificacion)},{Csv(r.Tercero)},{Csv(r.CodigoSap)},{Csv(r.Placas)},{Csv(r.Producto)},{Csv(r.Sku)},{Csv(r.Documento)},{r.PesoEntrada},{r.PesoSalida},{r.PesoNeto},{Csv(r.FechaEntrada.ToString("yyyy-MM-dd HH:mm:ss"))},{Csv(r.FechaSalida?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")},{Csv(r.Usuario)}\r\n";
            }

            var bytes = Encoding.UTF8.GetBytes(csv);

            return File(bytes, "text/csv", $"BasculaCamionera_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }

        [HttpPost("Sync/Movimiento")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SyncMovimiento([FromBody] BasculaMovimientoDto dto)
        {
            if (dto == null)
                return BadRequest(new { ok = false, msg = "Solicitud vacía." });

            if (dto.MovimientoGuid == Guid.Empty)
                dto.MovimientoGuid = Guid.NewGuid();

            if (string.IsNullOrWhiteSpace(dto.TerminalId))
                dto.TerminalId = "CASETA-01";

            if (string.IsNullOrWhiteSpace(dto.Folio))
                dto.Folio = $"BAS-{dto.TerminalId}-{DateTime.Now:yyyyMMddHHmmss}";

            if (string.IsNullOrWhiteSpace(dto.Tercero))
                return BadRequest(new { ok = false, msg = "Capture proveedor / cliente." });

            if (string.IsNullOrWhiteSpace(dto.Producto))
                return BadRequest(new { ok = false, msg = "Capture producto." });

            if (string.IsNullOrWhiteSpace(dto.Placas))
                return BadRequest(new { ok = false, msg = "Capture placas." });

            if (dto.PesoEntrada <= 0)
                return BadRequest(new { ok = false, msg = "El peso de entrada debe ser mayor a cero." });

            var estatus = string.IsNullOrWhiteSpace(dto.Estatus) ? "PENDIENTE" : dto.Estatus.Trim().ToUpperInvariant();

            if (estatus == "CERRADO" && dto.PesoSalida <= 0)
                return BadRequest(new { ok = false, msg = "El peso de salida debe ser mayor a cero." });

            if (dto.FechaEntrada == default)
                dto.FechaEntrada = DateTime.Now;

            if (estatus == "CERRADO" && !dto.FechaSalida.HasValue)
                dto.FechaSalida = DateTime.Now;

            using var cn = new SqlConnection(GetConnectionString());
            await cn.OpenAsync();

            using var cmd = new SqlCommand("dbo.sp_Bascula_UpsertMovimiento", cn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@MovimientoGuid", dto.MovimientoGuid);
            cmd.Parameters.AddWithValue("@TerminalId", dto.TerminalId);
            cmd.Parameters.AddWithValue("@FolioLocal", dto.Folio);
            cmd.Parameters.AddWithValue("@Estatus", estatus);
            cmd.Parameters.AddWithValue("@TipoMovimiento", DbValue(dto.TipoMovimiento));
            cmd.Parameters.AddWithValue("@Clasificacion", DbValue(dto.Clasificacion));
            cmd.Parameters.AddWithValue("@Tercero", DbValue(dto.Tercero));
            cmd.Parameters.AddWithValue("@CodigoSap", DbValue(dto.CodigoSap));
            cmd.Parameters.AddWithValue("@Placas", DbValue(dto.Placas));
            cmd.Parameters.AddWithValue("@Producto", DbValue(dto.Producto));
            cmd.Parameters.AddWithValue("@Sku", DbValue(dto.Sku));
            cmd.Parameters.AddWithValue("@Documento", DbValue(dto.Documento));
            cmd.Parameters.AddWithValue("@Chofer", DbValue(dto.Chofer));
            cmd.Parameters.AddWithValue("@Origen", DbValue(dto.Origen));
            cmd.Parameters.AddWithValue("@Destino", DbValue(dto.Destino));
            cmd.Parameters.AddWithValue("@Condicion", DbValue(dto.Condicion));
            cmd.Parameters.AddWithValue("@PesoEntrada", dto.PesoEntrada);
            cmd.Parameters.AddWithValue("@PesoSalida", dto.PesoSalida > 0 ? (object)dto.PesoSalida : DBNull.Value);
            cmd.Parameters.AddWithValue("@CapturaManual", EsManual(dto.CapturaManual));
            cmd.Parameters.AddWithValue("@MotivoManual", DbValue(dto.MotivoManual));
            cmd.Parameters.AddWithValue("@Observaciones", DbValue(dto.Observaciones));
            cmd.Parameters.AddWithValue("@FechaEntrada", dto.FechaEntrada);
            cmd.Parameters.AddWithValue("@FechaSalida", dto.FechaSalida.HasValue ? (object)dto.FechaSalida.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@UsuarioEntrada", DbValue(dto.UsuarioEntrada));
            cmd.Parameters.AddWithValue("@UsuarioSalida", DbValue(dto.UsuarioSalida));
            cmd.Parameters.AddWithValue("@RawEntrada", DbValue(dto.RawEntrada));
            cmd.Parameters.AddWithValue("@RawSalida", DbValue(dto.RawSalida));
            cmd.Parameters.AddWithValue("@PesoEntradaEstable", dto.PesoEntradaEstable);
            cmd.Parameters.AddWithValue("@PesoSalidaEstable", dto.PesoSalidaEstable);
            cmd.Parameters.AddWithValue("@CreadoOffline", dto.CreadoOffline);
            cmd.Parameters.AddWithValue("@FechaCreacionLocal", dto.FechaCreacionLocal.HasValue ? (object)dto.FechaCreacionLocal.Value : DateTime.Now);

            using var rd = await cmd.ExecuteReaderAsync();

            if (await rd.ReadAsync())
            {
                return Ok(new
                {
                    ok = true,
                    msg = "Movimiento sincronizado correctamente.",
                    movimientoId = rd["MovimientoId"],
                    movimientoGuid = rd["MovimientoGuid"],
                    folioLocal = rd["FolioLocal"],
                    folioServidor = rd["FolioServidor"],
                    estatus = rd["Estatus"],
                    fechaSyncServidor = rd["FechaSyncServidor"]
                });
            }

            return Ok(new
            {
                ok = true,
                msg = "Movimiento enviado al servidor.",
                movimientoGuid = dto.MovimientoGuid,
                folioLocal = dto.Folio,
                estatus
            });
        }

        [HttpGet("BuscarClientes")]
        public async Task<IActionResult> BuscarClientes(string? q = "", int take = 30)
        {
            try
            {
                take = Math.Max(1, Math.Min(take, 100));

                var query = (q ?? "").Trim();
                var rows = new List<ClienteSapLookupDto>();

                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var sql = @"
SELECT TOP (@take)
    Cliente,
    Nombrecliente,
    U_MT_Clasificacion,
    U_CANAL,
    VendedorId,
    VendedorNombre,
    PriceListNum,
    PriceListName,
    AplicaPresupuesto
FROM ClienteSap
WHERE
    (@q = '' OR Cliente LIKE @like OR Nombrecliente LIKE @like)
ORDER BY Nombrecliente;";

                using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@take", take);
                cmd.Parameters.AddWithValue("@q", query);
                cmd.Parameters.AddWithValue("@like", "%" + query + "%");

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    rows.Add(new ClienteSapLookupDto
                    {
                        CodigoSap = GetString(rd, "Cliente"),
                        Nombre = GetString(rd, "Nombrecliente"),
                        Clasificacion = GetString(rd, "U_MT_Clasificacion"),
                        Canal = GetString(rd, "U_CANAL"),
                        VendedorId = GetInt(rd, "VendedorId"),
                        VendedorNombre = GetString(rd, "VendedorNombre"),
                        PriceListNum = GetInt(rd, "PriceListNum"),
                        PriceListName = GetString(rd, "PriceListName"),
                        AplicaPresupuesto = GetInt(rd, "AplicaPresupuesto")
                    });
                }

                return Ok(new
                {
                    ok = true,
                    total = rows.Count,
                    rows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error buscando clientes SAP");

                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudieron cargar clientes: " + ex.Message
                });
            }
        }

        [HttpGet("BuscarArticulos")]
        public async Task<IActionResult> BuscarArticulos(string? q = "", int take = 30)
        {
            try
            {
                take = Math.Max(1, Math.Min(take, 100));

                var query = (q ?? "").Trim();
                var rows = new List<ArticuloSapLookupDto>();

                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var sql = @"
SELECT TOP (@take)
    ProductoCodigo,
    ProductoNombre
FROM ArticuloSap
WHERE
    (@q = '' OR ProductoCodigo LIKE @like OR ProductoNombre LIKE @like)
ORDER BY ProductoNombre;";

                using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@take", take);
                cmd.Parameters.AddWithValue("@q", query);
                cmd.Parameters.AddWithValue("@like", "%" + query + "%");

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    rows.Add(new ArticuloSapLookupDto
                    {
                        ProductoCodigo = GetString(rd, "ProductoCodigo"),
                        ProductoNombre = GetString(rd, "ProductoNombre")
                    });
                }

                return Ok(new
                {
                    ok = true,
                    total = rows.Count,
                    rows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error buscando artículos SAP");

                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudieron cargar artículos: " + ex.Message
                });
            }
        }

        [HttpGet("CatalogosOffline")]
        public async Task<IActionResult> CatalogosOffline(int take = 20000)
        {
            try
            {
                take = Math.Max(1, Math.Min(take, 20000));

                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var clientes = await LeerClientesOffline(cn, take);
                var productos = await LeerArticulosOffline(cn, take);
                var proveedores = await LeerProveedoresOffline(cn, take);

                return Ok(new
                {
                    ok = true,
                    fechaServidor = DateTime.Now,
                    totalClientes = clientes.Count,
                    totalProveedores = proveedores.Count,
                    totalProductos = productos.Count,
                    clientes,
                    proveedores,
                    productos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando catálogos offline de báscula");
                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudieron cargar catálogos offline: " + ex.Message
                });
            }
        }

        private static async Task<List<ClienteSapLookupDto>> LeerClientesOffline(SqlConnection cn, int take)
        {
            var rows = new List<ClienteSapLookupDto>();

            var sql = @"
SELECT TOP (@take)
    Cliente,
    Nombrecliente,
    U_MT_Clasificacion,
    U_CANAL,
    VendedorId,
    VendedorNombre,
    PriceListNum,
    PriceListName,
    AplicaPresupuesto
FROM ClienteSap
ORDER BY Nombrecliente;";

            using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@take", take);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                rows.Add(new ClienteSapLookupDto
                {
                    CodigoSap = GetString(rd, "Cliente"),
                    Nombre = GetString(rd, "Nombrecliente"),
                    Clasificacion = GetString(rd, "U_MT_Clasificacion"),
                    Canal = GetString(rd, "U_CANAL"),
                    VendedorId = GetInt(rd, "VendedorId"),
                    VendedorNombre = GetString(rd, "VendedorNombre"),
                    PriceListNum = GetInt(rd, "PriceListNum"),
                    PriceListName = GetString(rd, "PriceListName"),
                    AplicaPresupuesto = GetInt(rd, "AplicaPresupuesto")
                });
            }

            return rows;
        }

        private static async Task<List<ArticuloSapLookupDto>> LeerArticulosOffline(SqlConnection cn, int take)
        {
            var rows = new List<ArticuloSapLookupDto>();

            var sql = @"
SELECT TOP (@take)
    ProductoCodigo,
    ProductoNombre
FROM ArticuloSap
ORDER BY ProductoNombre;";

            using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@take", take);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                rows.Add(new ArticuloSapLookupDto
                {
                    ProductoCodigo = GetString(rd, "ProductoCodigo"),
                    ProductoNombre = GetString(rd, "ProductoNombre")
                });
            }

            return rows;
        }

        private static async Task<List<ClienteSapLookupDto>> LeerProveedoresOffline(SqlConnection cn, int take)
        {
            var rows = new List<ClienteSapLookupDto>();

            try
            {
                var existsSql = @"
SELECT CASE WHEN OBJECT_ID('dbo.ProveedorSap', 'U') IS NULL THEN 0 ELSE 1 END;";

                using (var existsCmd = new SqlCommand(existsSql, cn))
                {
                    var exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync());
                    if (exists != 1) return rows;
                }

                var sql = @"
SELECT TOP (@take)
    CAST(Proveedor AS varchar(80)) AS CodigoSap,
    CAST(NombreProveedor AS varchar(250)) AS Nombre,
    CAST('Proveedor' AS varchar(80)) AS Canal
FROM ProveedorSap
ORDER BY NombreProveedor;";

                using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@take", take);

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    rows.Add(new ClienteSapLookupDto
                    {
                        CodigoSap = GetString(rd, "CodigoSap"),
                        Nombre = GetString(rd, "Nombre"),
                        Canal = GetString(rd, "Canal")
                    });
                }
            }
            catch
            {
                // Si la tabla o columnas de proveedores tienen otro nombre, no se detiene la báscula.
                // La vista seguirá usando clientes/productos offline y podemos mapear proveedores después.
                return new List<ClienteSapLookupDto>();
            }

            return rows;
        }

        [HttpGet("ImpresorasLocales")]
        public IActionResult ImpresorasLocales()
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = "La detección de impresoras locales solo está disponible en Windows."
                    });
                }

                var defaultPrinter = new PrinterSettings().PrinterName;

                var impresoras = PrinterSettings.InstalledPrinters
                    .Cast<string>()
                    .Select(x => new
                    {
                        name = x,
                        isDefault = string.Equals(x, defaultPrinter, StringComparison.OrdinalIgnoreCase)
                    })
                    .OrderByDescending(x => x.isDefault)
                    .ThenBy(x => x.name)
                    .ToList();

                return Ok(new
                {
                    ok = true,
                    defaultPrinter,
                    total = impresoras.Count,
                    rows = impresoras
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detectando impresoras locales");

                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudieron detectar impresoras locales: " + ex.Message
                });
            }
        }

        [HttpPost("Tcp/ProbarConexion")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ProbarConexionTcp([FromBody] TcpTestDto dto)
        {
            if (dto == null)
                return BadRequest(new { ok = false, msg = "Solicitud vacía." });

            var host = (dto.Host ?? "").Trim();
            var port = dto.Port;
            var timeoutMs = dto.TimeoutMs > 0 ? dto.TimeoutMs : 3000;

            if (string.IsNullOrWhiteSpace(host))
                return BadRequest(new { ok = false, msg = "Capture IP de la báscula." });

            if (port <= 0)
                return BadRequest(new { ok = false, msg = "Capture puerto TCP válido." });

            try
            {
                using var client = new TcpClient();

                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(timeoutMs);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    return Ok(new
                    {
                        ok = false,
                        connected = false,
                        host,
                        port,
                        msg = $"No respondió la báscula en {timeoutMs} ms."
                    });
                }

                await connectTask;

                return Ok(new
                {
                    ok = true,
                    connected = true,
                    host,
                    port,
                    msg = $"Conexión TCP exitosa con {host}:{port}."
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    ok = false,
                    connected = false,
                    host,
                    port,
                    msg = "No se pudo conectar a la báscula: " + ex.Message
                });
            }
        }

        [HttpPost("Tcp/LeerPeso")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> LeerPesoTcp([FromBody] TcpReadPesoDto dto)
        {
            if (dto == null)
                return BadRequest(new { ok = false, msg = "Solicitud vacía." });

            var host = (dto.Host ?? "").Trim();
            var port = dto.Port;
            var command = DecodeCommand(dto.Command ?? "");
            var timeoutMs = dto.TimeoutMs > 0 ? dto.TimeoutMs : 3000;

            if (string.IsNullOrWhiteSpace(host))
                return BadRequest(new { ok = false, msg = "Capture IP de la báscula." });

            if (port <= 0)
                return BadRequest(new { ok = false, msg = "Capture puerto TCP válido." });

            try
            {
                using var client = new TcpClient
                {
                    ReceiveTimeout = timeoutMs,
                    SendTimeout = timeoutMs,
                    NoDelay = true
                };

                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(timeoutMs);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    return Ok(new
                    {
                        ok = false,
                        raw = "",
                        pesoKg = (decimal?)null,
                        msg = $"No respondió la báscula en {timeoutMs} ms."
                    });
                }

                await connectTask;

                using var stream = client.GetStream();

                if (!string.IsNullOrEmpty(command))
                {
                    var cmdBytes = Encoding.ASCII.GetBytes(command);
                    await stream.WriteAsync(cmdBytes, 0, cmdBytes.Length);
                    await stream.FlushAsync();
                }

                var raw = LeerTramaTcp(stream, timeoutMs);
                var peso = ExtraerPeso(raw);

                return Ok(new
                {
                    ok = peso.HasValue,
                    raw,
                    pesoKg = peso,
                    msg = peso.HasValue
                        ? "Peso real leído correctamente."
                        : "No se detectó peso en la trama recibida."
                });
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Timeout leyendo peso TCP/IP");

                return Ok(new
                {
                    ok = false,
                    raw = "",
                    pesoKg = (decimal?)null,
                    msg = "No se recibieron datos de la báscula dentro del tiempo configurado."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leyendo peso TCP/IP");

                return Ok(new
                {
                    ok = false,
                    raw = "",
                    pesoKg = (decimal?)null,
                    msg = "Error leyendo báscula: " + ex.Message
                });
            }
        }

        private string GetConnectionString()
        {
            var cs = _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection'.");

            return cs;
        }

        private static string NuevoFolio()
        {
            return $"BAS-{DateTime.Now:yyyyMMdd}-{(_movimientos.Count + 1):00000}";
        }

        private void RegistrarBitacora(string folio, string accion)
        {
            _bitacora.Add(new BasculaBitacoraDto
            {
                Fecha = DateTime.Now,
                Usuario = User?.Identity?.Name ?? "Usuario SIGO",
                Accion = accion,
                Folio = folio
            });
        }

        private static void Copiar(BasculaMovimientoDto src, BasculaMovimientoDto dst)
        {
            dst.TipoMovimiento = src.TipoMovimiento;
            dst.Clasificacion = src.Clasificacion;
            dst.Tercero = src.Tercero;
            dst.CodigoSap = src.CodigoSap;
            dst.Placas = src.Placas;
            dst.Producto = src.Producto;
            dst.Sku = src.Sku;
            dst.Documento = src.Documento;
            dst.Chofer = src.Chofer;
            dst.Origen = src.Origen;
            dst.Destino = src.Destino;
            dst.Condicion = src.Condicion;
            dst.PesoEntrada = src.PesoEntrada;
            dst.PesoSalida = src.PesoSalida;
            dst.PesoNeto = src.PesoNeto;
            dst.CapturaManual = src.CapturaManual;
            dst.MotivoManual = src.MotivoManual;
            dst.Observaciones = src.Observaciones;
            dst.Estatus = src.Estatus;
            dst.FechaEntrada = src.FechaEntrada;
            dst.FechaSalida = src.FechaSalida;
            dst.Usuario = src.Usuario;
        }

        private static string Csv(string? value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }

        private static string GetString(SqlDataReader rd, string column)
        {
            return rd[column] == DBNull.Value ? "" : rd[column]?.ToString() ?? "";
        }

        private static int? GetInt(SqlDataReader rd, string column)
        {
            if (rd[column] == DBNull.Value)
                return null;

            return Convert.ToInt32(rd[column]);
        }

        private static decimal? GetDecimal(SqlDataReader rd, string column)
        {
            if (rd[column] == DBNull.Value)
                return null;

            return Convert.ToDecimal(rd[column]);
        }


        private static object DbValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
        }

        private static bool EsManual(string? value)
        {
            var txt = (value ?? "").Trim().ToUpperInvariant();
            return txt == "SI" || txt == "SÍ" || txt == "TRUE" || txt == "1";
        }

        private static string DecodeCommand(string command)
        {
            return (command ?? "")
                .Replace("\\r", "\r")
                .Replace("\\n", "\n")
                .Replace("\\t", "\t");
        }

        private static string LeerTramaTcp(NetworkStream stream, int timeoutMs)
        {
            var buffer = new byte[2048];
            var sb = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                if (stream.DataAvailable)
                {
                    var read = stream.Read(buffer, 0, buffer.Length);

                    if (read > 0)
                    {
                        sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        Thread.Sleep(80);

                        if (!stream.DataAvailable)
                            break;
                    }
                }
                else
                {
                    Thread.Sleep(40);
                }
            }

            if (sb.Length == 0)
            {
                try
                {
                    var read = stream.Read(buffer, 0, buffer.Length);

                    if (read > 0)
                        sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                catch (IOException)
                {
                }
            }

            return sb.ToString();
        }

        private static decimal? ExtraerPeso(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var clean = raw.Replace(",", ".");
            var matches = Regex.Matches(clean, @"[-+]?\d+(?:\.\d+)?");

            if (matches.Count == 0)
                return null;

            decimal? best = null;

            foreach (Match match in matches)
            {
                if (decimal.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                {
                    value = Math.Abs(value);

                    if (best == null || value > best.Value)
                        best = value;
                }
            }

            return best;
        }
    }

    public class BasculaMovimientoDto
    {
        public Guid MovimientoGuid { get; set; }
        public string TerminalId { get; set; } = "CASETA-01";
        public string Folio { get; set; } = "";
        public string TipoMovimiento { get; set; } = "";
        public string Clasificacion { get; set; } = "";
        public string Tercero { get; set; } = "";
        public string CodigoSap { get; set; } = "";
        public string Placas { get; set; } = "";
        public string Producto { get; set; } = "";
        public string Sku { get; set; } = "";
        public string Documento { get; set; } = "";
        public string Chofer { get; set; } = "";
        public string Origen { get; set; } = "";
        public string Destino { get; set; } = "";
        public string Condicion { get; set; } = "";
        public decimal PesoEntrada { get; set; }
        public decimal PesoSalida { get; set; }
        public decimal PesoNeto { get; set; }
        public string CapturaManual { get; set; } = "No";
        public string MotivoManual { get; set; } = "";
        public string Observaciones { get; set; } = "";
        public string Estatus { get; set; } = "PENDIENTE";
        public DateTime FechaEntrada { get; set; }
        public DateTime? FechaSalida { get; set; }
        public string Usuario { get; set; } = "";
        public string UsuarioEntrada { get; set; } = "";
        public string UsuarioSalida { get; set; } = "";
        public string RawEntrada { get; set; } = "";
        public string RawSalida { get; set; } = "";
        public bool PesoEntradaEstable { get; set; } = true;
        public bool PesoSalidaEstable { get; set; }
        public bool CreadoOffline { get; set; } = true;
        public DateTime? FechaCreacionLocal { get; set; }
    }

    public class BasculaBitacoraDto
    {
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; } = "";
        public string Accion { get; set; } = "";
        public string Folio { get; set; } = "";
    }

    public class ClienteSapLookupDto
    {
        public string CodigoSap { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Clasificacion { get; set; } = "";
        public string Canal { get; set; } = "";
        public int? VendedorId { get; set; }
        public string VendedorNombre { get; set; } = "";
        public int? PriceListNum { get; set; }
        public string PriceListName { get; set; } = "";
        public int? AplicaPresupuesto { get; set; }
    }

    public class ArticuloSapLookupDto
    {
        public string ProductoCodigo { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
    }

    public class TcpTestDto
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public int TimeoutMs { get; set; } = 3000;
    }

    public class TcpReadPesoDto
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Command { get; set; } = "";
        public int TimeoutMs { get; set; } = 3000;
    }
}
