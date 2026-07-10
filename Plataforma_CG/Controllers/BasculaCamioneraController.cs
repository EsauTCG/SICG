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
        private static readonly List<BasculaPreRegistroDto> _preRegistrosDemo = new();

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


        [HttpGet("PreRegistro")]
        [HttpGet("PreRegistro/Index")]
        public IActionResult PreRegistro()
        {
            return View("~/Views/BasculaCamionera/PreRegistro.cshtml");
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

        [HttpGet("PreRegistro/Listar")]
        public async Task<IActionResult> ListarPreRegistros(string? estatus = "PENDIENTE_CASETA", string? q = "", int take = 100)
        {
            try
            {
                take = Math.Max(1, Math.Min(take, 500));

                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var rows = new List<BasculaPreRegistroDto>();
                var query = (q ?? "").Trim();
                var status = (estatus ?? "").Trim();

                var sql = @"
SELECT TOP (@take)
    PreRegistroId,
    PreRegistroGuid,
    Token,
    FolioPreRegistro,
    Estatus,
    TipoMovimiento,
    Clasificacion,
    Tercero,
    CodigoSap,
    Placas,
    Producto,
    Sku,
    Documento,
    Chofer,
    Origen,
    Destino,
    Condicion,
    Observaciones,
    AreaOrigen,
    UsuarioCaptura,
    FechaCaptura,
    MovimientoGuid,
    FolioMovimiento,
    FechaEscaneoCaseta,
    UsuarioCaseta
FROM dbo.BasculaPreRegistro
WHERE
    (@estatus = '' OR Estatus = @estatus)
    AND (
        @q = ''
        OR FolioPreRegistro LIKE @like
        OR Tercero LIKE @like
        OR Producto LIKE @like
        OR Placas LIKE @like
        OR Documento LIKE @like
        OR Origen LIKE @like
        OR Destino LIKE @like
    )
ORDER BY FechaCaptura DESC;";

                using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@take", take);
                cmd.Parameters.AddWithValue("@estatus", status);
                cmd.Parameters.AddWithValue("@q", query);
                cmd.Parameters.AddWithValue("@like", "%" + query + "%");

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    rows.Add(MapPreRegistro(rd));
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
                _logger.LogError(ex, "Error listando pre-registros de báscula");

                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudieron cargar los pre-registros. Verifique que exista dbo.BasculaPreRegistro. Detalle: " + ex.Message
                });
            }
        }

        [HttpPost("PreRegistro/Crear")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CrearPreRegistro([FromBody] BasculaPreRegistroDto dto)
        {
            if (dto == null)
                return BadRequest(new { ok = false, msg = "Solicitud vacía." });

            var error = ValidarPreRegistro(dto);

            if (!string.IsNullOrWhiteSpace(error))
                return BadRequest(new { ok = false, msg = error });

            dto.PreRegistroGuid = dto.PreRegistroGuid == Guid.Empty ? Guid.NewGuid() : dto.PreRegistroGuid;
            dto.Token = string.IsNullOrWhiteSpace(dto.Token) ? NuevoTokenPreRegistro() : NormalizarTokenEscaneo(dto.Token);
            dto.FolioPreRegistro = string.IsNullOrWhiteSpace(dto.FolioPreRegistro) ? NuevoFolioPreRegistro() : dto.FolioPreRegistro.Trim().ToUpperInvariant();
            dto.Estatus = string.IsNullOrWhiteSpace(dto.Estatus) ? "PENDIENTE_CASETA" : dto.Estatus.Trim().ToUpperInvariant();
            dto.UsuarioCaptura = User?.Identity?.Name ?? dto.UsuarioCaptura ?? "Usuario SIGO";
            dto.FechaCaptura = dto.FechaCaptura == default ? DateTime.Now : dto.FechaCaptura;

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var sql = @"
INSERT INTO dbo.BasculaPreRegistro
(
    PreRegistroGuid,
    Token,
    FolioPreRegistro,
    Estatus,
    TipoMovimiento,
    Clasificacion,
    Tercero,
    CodigoSap,
    Placas,
    Producto,
    Sku,
    Documento,
    Chofer,
    Origen,
    Destino,
    Condicion,
    Observaciones,
    AreaOrigen,
    UsuarioCaptura,
    FechaCaptura
)
OUTPUT
    inserted.PreRegistroId,
    inserted.PreRegistroGuid,
    inserted.Token,
    inserted.FolioPreRegistro,
    inserted.Estatus,
    inserted.TipoMovimiento,
    inserted.Clasificacion,
    inserted.Tercero,
    inserted.CodigoSap,
    inserted.Placas,
    inserted.Producto,
    inserted.Sku,
    inserted.Documento,
    inserted.Chofer,
    inserted.Origen,
    inserted.Destino,
    inserted.Condicion,
    inserted.Observaciones,
    inserted.AreaOrigen,
    inserted.UsuarioCaptura,
    inserted.FechaCaptura,
    inserted.MovimientoGuid,
    inserted.FolioMovimiento,
    inserted.FechaEscaneoCaseta,
    inserted.UsuarioCaseta
VALUES
(
    @PreRegistroGuid,
    @Token,
    @FolioPreRegistro,
    @Estatus,
    @TipoMovimiento,
    @Clasificacion,
    @Tercero,
    @CodigoSap,
    @Placas,
    @Producto,
    @Sku,
    @Documento,
    @Chofer,
    @Origen,
    @Destino,
    @Condicion,
    @Observaciones,
    @AreaOrigen,
    @UsuarioCaptura,
    @FechaCaptura
);";

                using var cmd = new SqlCommand(sql, cn);
                AddPreRegistroParams(cmd, dto);

                using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                    dto = MapPreRegistro(rd);

                var scanPayload = BuildScanPayload(dto.Token);

                RegistrarBitacora(dto.FolioPreRegistro, "Creó pre-registro de báscula");

                return Ok(new
                {
                    ok = true,
                    msg = "Pre-registro creado correctamente.",
                    preRegistro = dto,
                    token = dto.Token,
                    scanPayload,
                    qrPayload = scanPayload,
                    hojaUrl = Url.Action("HojaPreRegistro", "BasculaCamionera", new { token = dto.Token })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creando pre-registro de báscula");

                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudo crear el pre-registro. Verifique que exista dbo.BasculaPreRegistro. Detalle: " + ex.Message
                });
            }
        }

        [HttpGet("PreRegistroPorToken")]
        public async Task<IActionResult> PreRegistroPorToken(string token)
        {
            var cleanToken = NormalizarTokenEscaneo(token);

            if (string.IsNullOrWhiteSpace(cleanToken))
                return BadRequest(new { ok = false, msg = "Token / QR vacío o inválido." });

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var dto = await LeerPreRegistroPorTokenAsync(cn, cleanToken);

                if (dto == null)
                    return NotFound(new { ok = false, msg = "No se encontró un pre-registro con ese QR / código." });

                var estatus = (dto.Estatus ?? "").Trim().ToUpperInvariant();

                if (estatus == "CANCELADO" || estatus == "VENCIDO")
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = $"El pre-registro {dto.FolioPreRegistro} está {estatus.ToLowerInvariant()}."
                    });
                }

                if (estatus == "CERRADO")
                {
                    return BadRequest(new
                    {
                        ok = false,
                        msg = $"El pre-registro {dto.FolioPreRegistro} ya fue cerrado y no puede volver a usarse."
                    });
                }

                if (estatus == "PENDIENTE_CASETA")
                {
                    await MarcarPreRegistroEscaneadoAsync(cn, dto.Token);
                    dto.Estatus = "ESCANEADO_CASETA";
                    dto.UsuarioCaseta = User?.Identity?.Name ?? "Usuario SIGO";
                    dto.FechaEscaneoCaseta = DateTime.Now;
                }

                RegistrarBitacora(dto.FolioPreRegistro, "Escaneó pre-registro en caseta");

                return Ok(new
                {
                    ok = true,
                    msg = "Pre-registro cargado.",
                    preRegistro = dto,
                    token = dto.Token,
                    scanPayload = BuildScanPayload(dto.Token)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consultando pre-registro por token");

                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudo consultar el pre-registro: " + ex.Message
                });
            }
        }

        [HttpPost("PreRegistro/Cancelar")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CancelarPreRegistro([FromBody] BasculaPreRegistroCancelarDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Token))
                return BadRequest(new { ok = false, msg = "Token inválido." });

            var token = NormalizarTokenEscaneo(dto.Token);
            var usuario = User?.Identity?.Name ?? "Usuario SIGO";

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var sql = @"
UPDATE dbo.BasculaPreRegistro
SET
    Estatus = 'CANCELADO',
    Observaciones = CONCAT(ISNULL(Observaciones, ''), CASE WHEN ISNULL(Observaciones, '') = '' THEN '' ELSE CHAR(13) + CHAR(10) END, @Motivo),
    UsuarioCaseta = @UsuarioCaseta
WHERE Token = @Token
  AND Estatus NOT IN ('CERRADO', 'CANCELADO');";

                using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@Token", token);
                cmd.Parameters.AddWithValue("@Motivo", DbValue("Cancelado: " + (dto.Motivo ?? "")));
                cmd.Parameters.AddWithValue("@UsuarioCaseta", usuario);

                var rows = await cmd.ExecuteNonQueryAsync();

                if (rows <= 0)
                    return NotFound(new { ok = false, msg = "No se encontró el pre-registro o ya estaba cerrado/cancelado." });

                RegistrarBitacora(token, "Canceló pre-registro de báscula");

                return Ok(new { ok = true, msg = "Pre-registro cancelado." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelando pre-registro");

                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudo cancelar el pre-registro: " + ex.Message
                });
            }
        }

        [HttpGet("PreRegistro/Hoja")]
        public async Task<IActionResult> HojaPreRegistro(string token)
        {
            var cleanToken = NormalizarTokenEscaneo(token);

            if (string.IsNullOrWhiteSpace(cleanToken))
                return BadRequest("Token inválido.");

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var dto = await LeerPreRegistroPorTokenAsync(cn, cleanToken);

                if (dto == null)
                    return NotFound("No se encontró el pre-registro.");

                var payload = BuildScanPayload(dto.Token);
                var html = BuildHojaPreRegistroHtml(dto, payload);

                return Content(html, "text/html", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando hoja de pre-registro");
                return BadRequest("No se pudo generar la hoja: " + ex.Message);
            }
        }


        [HttpPost("GuardarEntrada")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarEntrada([FromBody] BasculaMovimientoDto dto)
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
            dto.OrigenCaptura = ResolverOrigenCaptura(dto);

            var existente = _movimientos.FirstOrDefault(x => x.Folio == dto.Folio);

            if (existente == null)
                _movimientos.Add(dto);
            else
                Copiar(dto, existente);

            RegistrarBitacora(dto.Folio, dto.OrigenCaptura == "PRE_REGISTRO_QR"
                ? "Guardó entrada desde pre-registro QR"
                : "Guardó entrada de báscula");

            await MarcarPreRegistroMovimientoSiAplicaAsync(dto, "PESADO_ENTRADA");

            return Ok(new
            {
                ok = true,
                msg = "Entrada guardada.",
                folio = dto.Folio,
                origenCaptura = dto.OrigenCaptura,
                folioPreRegistro = dto.FolioPreRegistro
            });
        }

        [HttpPost("CerrarSalida")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CerrarSalida([FromBody] BasculaMovimientoDto dto)
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
            dto.OrigenCaptura = ResolverOrigenCaptura(dto);

            Copiar(dto, existente);
            RegistrarBitacora(dto.Folio, dto.OrigenCaptura == "PRE_REGISTRO_QR"
                ? "Cerró salida desde pre-registro QR"
                : "Cerró salida y calculó peso neto");

            await MarcarPreRegistroMovimientoSiAplicaAsync(dto, "CERRADO");

            return Ok(new
            {
                ok = true,
                msg = "Salida cerrada.",
                folio = dto.Folio,
                neto = dto.PesoNeto,
                origenCaptura = dto.OrigenCaptura,
                folioPreRegistro = dto.FolioPreRegistro
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
            var csv = "Folio,Estatus,OrigenCaptura,FolioPreRegistro,TipoMovimiento,Clasificacion,Tercero,CodigoSap,Placas,Producto,Sku,Documento,PesoEntrada,PesoSalida,PesoNeto,FechaEntrada,FechaSalida,Usuario\r\n";

            foreach (var r in _movimientos.OrderByDescending(x => x.FechaEntrada))
            {
                csv += $"{Csv(r.Folio)},{Csv(r.Estatus)},{Csv(r.OrigenCaptura)},{Csv(r.FolioPreRegistro)},{Csv(r.TipoMovimiento)},{Csv(r.Clasificacion)},{Csv(r.Tercero)},{Csv(r.CodigoSap)},{Csv(r.Placas)},{Csv(r.Producto)},{Csv(r.Sku)},{Csv(r.Documento)},{r.PesoEntrada},{r.PesoSalida},{r.PesoNeto},{Csv(r.FechaEntrada.ToString("yyyy-MM-dd HH:mm:ss"))},{Csv(r.FechaSalida?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")},{Csv(r.Usuario)}\r\n";
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

            dto.OrigenCaptura = ResolverOrigenCaptura(dto);

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

            object result;

            using (var rd = await cmd.ExecuteReaderAsync())
            {
                if (await rd.ReadAsync())
                {
                    result = new
                    {
                        ok = true,
                        msg = "Movimiento sincronizado correctamente.",
                        movimientoId = rd["MovimientoId"],
                        movimientoGuid = rd["MovimientoGuid"],
                        folioLocal = rd["FolioLocal"],
                        folioServidor = rd["FolioServidor"],
                        estatus = rd["Estatus"],
                        fechaSyncServidor = rd["FechaSyncServidor"],
                        origenCaptura = dto.OrigenCaptura,
                        folioPreRegistro = dto.FolioPreRegistro
                    };
                }
                else
                {
                    result = new
                    {
                        ok = true,
                        msg = "Movimiento enviado al servidor.",
                        movimientoGuid = dto.MovimientoGuid,
                        folioLocal = dto.Folio,
                        estatus,
                        origenCaptura = dto.OrigenCaptura,
                        folioPreRegistro = dto.FolioPreRegistro
                    };
                }
            }

            await MarcarPreRegistroMovimientoSiAplicaAsync(dto, estatus == "CERRADO" ? "CERRADO" : "PESADO_ENTRADA");

            return Ok(result);
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


        private static string NuevoFolioPreRegistro()
        {
            return $"PRE-BAS-{DateTime.Now:yyyyMMdd-HHmmssfff}";
        }

        private static string NuevoTokenPreRegistro()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static string BuildScanPayload(string? token)
        {
            return "BASPRE|" + NormalizarTokenEscaneo(token);
        }

        private static string NormalizarTokenEscaneo(string? value)
        {
            var txt = (value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(txt))
                return "";

            if (txt.StartsWith("BASPRE|", StringComparison.OrdinalIgnoreCase))
                return txt.Split('|').LastOrDefault()?.Trim() ?? "";

            if (Uri.TryCreate(txt, UriKind.Absolute, out var absoluteUri))
            {
                var token = ExtraerQueryString(absoluteUri.Query, "token");

                if (!string.IsNullOrWhiteSpace(token))
                    return token.Trim();
            }

            if (Uri.TryCreate("http://local/" + txt.TrimStart('/'), UriKind.Absolute, out var relativeUri))
            {
                var token = ExtraerQueryString(relativeUri.Query, "token");

                if (!string.IsNullOrWhiteSpace(token))
                    return token.Trim();
            }

            return txt;
        }

        private static string ExtraerQueryString(string query, string key)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            var parts = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var kv = part.Split('=', 2);

                if (kv.Length == 2 && string.Equals(Uri.UnescapeDataString(kv[0]), key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(kv[1]);
            }

            return "";
        }

        private static string ValidarPreRegistro(BasculaPreRegistroDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TipoMovimiento))
                return "Seleccione tipo de movimiento.";

            if (string.IsNullOrWhiteSpace(dto.Tercero))
                return "Capture proveedor / cliente.";

            if (string.IsNullOrWhiteSpace(dto.Producto))
                return "Capture producto.";

            return "";
        }

        private static string ResolverOrigenCaptura(BasculaMovimientoDto dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.TokenPreRegistro) ||
                !string.IsNullOrWhiteSpace(dto.FolioPreRegistro) ||
                dto.PreRegistroGuid.HasValue ||
                dto.PreRegistroId.HasValue)
            {
                return "PRE_REGISTRO_QR";
            }

            return "CAPTURA_CASETA";
        }

        private async Task MarcarPreRegistroMovimientoSiAplicaAsync(BasculaMovimientoDto dto, string estatusPreRegistro)
        {
            var token = NormalizarTokenEscaneo(dto.TokenPreRegistro);

            if (string.IsNullOrWhiteSpace(token) &&
                string.IsNullOrWhiteSpace(dto.FolioPreRegistro) &&
                !dto.PreRegistroGuid.HasValue &&
                !dto.PreRegistroId.HasValue)
            {
                return;
            }

            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var sql = @"
UPDATE dbo.BasculaPreRegistro
SET
    Estatus = @Estatus,
    MovimientoGuid = @MovimientoGuid,
    FolioMovimiento = @FolioMovimiento,
    FechaEscaneoCaseta = ISNULL(FechaEscaneoCaseta, SYSDATETIME()),
    UsuarioCaseta = @UsuarioCaseta
WHERE
    (@Token <> '' AND Token = @Token)
    OR (@FolioPreRegistro <> '' AND FolioPreRegistro = @FolioPreRegistro)
    OR (@PreRegistroGuid IS NOT NULL AND PreRegistroGuid = @PreRegistroGuid)
    OR (@PreRegistroId IS NOT NULL AND PreRegistroId = @PreRegistroId);";

                using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@Estatus", DbValue(estatusPreRegistro));
                cmd.Parameters.AddWithValue("@MovimientoGuid", dto.MovimientoGuid == Guid.Empty ? (object)DBNull.Value : dto.MovimientoGuid);
                cmd.Parameters.AddWithValue("@FolioMovimiento", DbValue(dto.Folio));
                cmd.Parameters.AddWithValue("@UsuarioCaseta", DbValue(User?.Identity?.Name ?? dto.UsuarioEntrada ?? "Usuario SIGO"));
                cmd.Parameters.AddWithValue("@Token", token);
                cmd.Parameters.AddWithValue("@FolioPreRegistro", DbValue(dto.FolioPreRegistro));
                cmd.Parameters.AddWithValue("@PreRegistroGuid", dto.PreRegistroGuid.HasValue ? (object)dto.PreRegistroGuid.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@PreRegistroId", dto.PreRegistroId.HasValue ? (object)dto.PreRegistroId.Value : DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo marcar el pre-registro como usado. El movimiento principal no se revierte.");
            }
        }

        private static void AddPreRegistroParams(SqlCommand cmd, BasculaPreRegistroDto dto)
        {
            cmd.Parameters.AddWithValue("@PreRegistroGuid", dto.PreRegistroGuid == Guid.Empty ? Guid.NewGuid() : dto.PreRegistroGuid);
            cmd.Parameters.AddWithValue("@Token", DbValue(dto.Token));
            cmd.Parameters.AddWithValue("@FolioPreRegistro", DbValue(dto.FolioPreRegistro));
            cmd.Parameters.AddWithValue("@Estatus", DbValue(dto.Estatus));
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
            cmd.Parameters.AddWithValue("@Observaciones", DbValue(dto.Observaciones));
            cmd.Parameters.AddWithValue("@AreaOrigen", DbValue(dto.AreaOrigen));
            cmd.Parameters.AddWithValue("@UsuarioCaptura", DbValue(dto.UsuarioCaptura));
            cmd.Parameters.AddWithValue("@FechaCaptura", dto.FechaCaptura == default ? DateTime.Now : dto.FechaCaptura);
        }

        private static BasculaPreRegistroDto MapPreRegistro(SqlDataReader rd)
        {
            return new BasculaPreRegistroDto
            {
                PreRegistroId = GetLongNullable(rd, "PreRegistroId"),
                PreRegistroGuid = GetGuid(rd, "PreRegistroGuid"),
                Token = GetString(rd, "Token"),
                FolioPreRegistro = GetString(rd, "FolioPreRegistro"),
                Estatus = GetString(rd, "Estatus"),
                TipoMovimiento = GetString(rd, "TipoMovimiento"),
                Clasificacion = GetString(rd, "Clasificacion"),
                Tercero = GetString(rd, "Tercero"),
                CodigoSap = GetString(rd, "CodigoSap"),
                Placas = GetString(rd, "Placas"),
                Producto = GetString(rd, "Producto"),
                Sku = GetString(rd, "Sku"),
                Documento = GetString(rd, "Documento"),
                Chofer = GetString(rd, "Chofer"),
                Origen = GetString(rd, "Origen"),
                Destino = GetString(rd, "Destino"),
                Condicion = GetString(rd, "Condicion"),
                Observaciones = GetString(rd, "Observaciones"),
                AreaOrigen = GetString(rd, "AreaOrigen"),
                UsuarioCaptura = GetString(rd, "UsuarioCaptura"),
                FechaCaptura = GetDateTime(rd, "FechaCaptura") ?? DateTime.MinValue,
                MovimientoGuid = GetGuidNullable(rd, "MovimientoGuid"),
                FolioMovimiento = GetString(rd, "FolioMovimiento"),
                FechaEscaneoCaseta = GetDateTime(rd, "FechaEscaneoCaseta"),
                UsuarioCaseta = GetString(rd, "UsuarioCaseta")
            };
        }

        private static async Task<BasculaPreRegistroDto?> LeerPreRegistroPorTokenAsync(SqlConnection cn, string token)
        {
            var sql = @"
SELECT TOP (1)
    PreRegistroId,
    PreRegistroGuid,
    Token,
    FolioPreRegistro,
    Estatus,
    TipoMovimiento,
    Clasificacion,
    Tercero,
    CodigoSap,
    Placas,
    Producto,
    Sku,
    Documento,
    Chofer,
    Origen,
    Destino,
    Condicion,
    Observaciones,
    AreaOrigen,
    UsuarioCaptura,
    FechaCaptura,
    MovimientoGuid,
    FolioMovimiento,
    FechaEscaneoCaseta,
    UsuarioCaseta
FROM dbo.BasculaPreRegistro
WHERE Token = @Token
ORDER BY FechaCaptura DESC;";

            using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Token", token);

            using var rd = await cmd.ExecuteReaderAsync();

            if (await rd.ReadAsync())
                return MapPreRegistro(rd);

            return null;
        }

        private async Task MarcarPreRegistroEscaneadoAsync(SqlConnection cn, string token)
        {
            var sql = @"
UPDATE dbo.BasculaPreRegistro
SET
    Estatus = 'ESCANEADO_CASETA',
    FechaEscaneoCaseta = ISNULL(FechaEscaneoCaseta, SYSDATETIME()),
    UsuarioCaseta = @UsuarioCaseta
WHERE Token = @Token
  AND Estatus = 'PENDIENTE_CASETA';";

            using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Token", token);
            cmd.Parameters.AddWithValue("@UsuarioCaseta", DbValue(User?.Identity?.Name ?? "Usuario SIGO"));

            await cmd.ExecuteNonQueryAsync();
        }

        private static string BuildHojaPreRegistroHtml(BasculaPreRegistroDto p, string payload)
        {
            var safePayload = Html(payload);
            var html = $@"<!doctype html>
<html lang=""es"">
<head>
<meta charset=""utf-8"">
<title>{Html(p.FolioPreRegistro)} - Pre-registro báscula</title>
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<style>
body {{ font-family: Arial, Helvetica, sans-serif; margin: 0; background: #f4eeee; color: #1f1111; }}
.sheet {{ width: 210mm; min-height: 280mm; margin: 0 auto; padding: 14mm; background: #fff; box-sizing: border-box; }}
.header {{ display:flex; justify-content:space-between; align-items:flex-start; border-bottom:4px solid #7b1113; padding-bottom:10px; }}
.brand {{ font-size:26px; font-weight:900; color:#7b1113; }}
.sub {{ font-size:12px; color:#6b4b4b; margin-top:3px; }}
.folio {{ text-align:right; font-size:22px; font-weight:900; }}
.qrbox {{ margin:16px auto 12px; width:74mm; height:74mm; border:2px solid #111; display:flex; align-items:center; justify-content:center; }}
.qrhelp {{ text-align:center; font-size:11px; color:#555; }}
.payload {{ margin:10px auto; width:90%; padding:8px; border:1px dashed #555; text-align:center; font-family:Consolas, monospace; font-size:14px; word-break:break-all; }}
.grid {{ display:grid; grid-template-columns:1fr 1fr; gap:8px; margin-top:16px; }}
.box {{ border:1px solid #cdb4b4; padding:8px 10px; min-height:44px; }}
.box label {{ display:block; font-size:10px; color:#7b1113; font-weight:800; text-transform:uppercase; }}
.box strong {{ display:block; font-size:16px; margin-top:3px; }}
.full {{ grid-column:1/-1; }}
.signs {{ display:grid; grid-template-columns:1fr 1fr; gap:24mm; margin-top:28mm; }}
.sign {{ border-top:1px solid #111; text-align:center; padding-top:5px; font-size:11px; }}
.actions {{ text-align:center; margin:14px; }}
button {{ padding:10px 16px; font-weight:800; }}
@media print {{ .actions {{ display:none; }} body {{ background:#fff; }} .sheet {{ margin:0; }} }}
</style>
</head>
<body>
<div class=""actions""><button onclick=""window.print()"">Imprimir hoja</button></div>
<div class=""sheet"">
    <div class=""header"">
        <div>
            <div class=""brand"">CARNES G · BÁSCULA</div>
            <div class=""sub"">Hoja de pre-registro para caseta. Escanee el QR antes de pesar.</div>
        </div>
        <div class=""folio"">{Html(p.FolioPreRegistro)}<div class=""sub"">{Html(p.Estatus)}</div></div>
    </div>

    <div id=""qr"" class=""qrbox""></div>
    <div class=""qrhelp"">Escanear este QR/código en la pantalla de caseta</div>
    <div class=""payload"">{safePayload}</div>

    <div class=""grid"">
        <div class=""box""><label>Tipo movimiento</label><strong>{Html(p.TipoMovimiento)}</strong></div>
        <div class=""box""><label>Clasificación</label><strong>{Html(p.Clasificacion)}</strong></div>
        <div class=""box""><label>Proveedor / Cliente</label><strong>{Html(p.Tercero)}</strong></div>
        <div class=""box""><label>Código SAP</label><strong>{Html(p.CodigoSap)}</strong></div>
        <div class=""box""><label>Producto</label><strong>{Html(p.Producto)}</strong></div>
        <div class=""box""><label>SKU</label><strong>{Html(p.Sku)}</strong></div>
        <div class=""box""><label>Placas</label><strong>{Html(p.Placas)}</strong></div>
        <div class=""box""><label>Chofer</label><strong>{Html(p.Chofer)}</strong></div>
        <div class=""box""><label>Origen</label><strong>{Html(p.Origen)}</strong></div>
        <div class=""box""><label>Destino</label><strong>{Html(p.Destino)}</strong></div>
        <div class=""box""><label>Documento</label><strong>{Html(p.Documento)}</strong></div>
        <div class=""box""><label>Área origen</label><strong>{Html(p.AreaOrigen)}</strong></div>
        <div class=""box full""><label>Observaciones</label><strong>{Html(p.Observaciones)}</strong></div>
    </div>

    <div class=""signs"">
        <div class=""sign"">Entrega área origen</div>
        <div class=""sign"">Recibe caseta</div>
    </div>
</div>
<script src=""/lib/qrcode/qrcode.min.js""></script>
<script>
(function(){{
    var payload = {System.Text.Json.JsonSerializer.Serialize(payload)};
    var box = document.getElementById('qr');
    if (window.QRCode) {{
        new QRCode(box, {{ text: payload, width: 260, height: 260, correctLevel: QRCode.CorrectLevel.M }});
    }} else {{
        box.innerHTML = '<div style=""font-size:18px;font-weight:900;text-align:center;padding:10px;"">QR LIB NO INSTALADA<br><small>Use el código impreso abajo</small></div>';
    }}
}})();
</script>
</body>
</html>";
            return html;
        }

        private static string Html(string? value)
        {
            return System.Net.WebUtility.HtmlEncode(value ?? "");
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
            dst.PreRegistroId = src.PreRegistroId;
            dst.PreRegistroGuid = src.PreRegistroGuid;
            dst.TokenPreRegistro = src.TokenPreRegistro;
            dst.FolioPreRegistro = src.FolioPreRegistro;
            dst.OrigenCaptura = src.OrigenCaptura;
            dst.AreaOrigenPreRegistro = src.AreaOrigenPreRegistro;
            dst.UsuarioCapturaPreRegistro = src.UsuarioCapturaPreRegistro;
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

        private static long? GetLongNullable(SqlDataReader rd, string column)
        {
            if (rd[column] == DBNull.Value)
                return null;

            return Convert.ToInt64(rd[column]);
        }

        private static Guid GetGuid(SqlDataReader rd, string column)
        {
            if (rd[column] == DBNull.Value)
                return Guid.Empty;

            if (rd[column] is Guid guid)
                return guid;

            return Guid.TryParse(rd[column]?.ToString(), out var parsed) ? parsed : Guid.Empty;
        }

        private static Guid? GetGuidNullable(SqlDataReader rd, string column)
        {
            if (rd[column] == DBNull.Value)
                return null;

            if (rd[column] is Guid guid)
                return guid;

            return Guid.TryParse(rd[column]?.ToString(), out var parsed) ? parsed : (Guid?)null;
        }

        private static DateTime? GetDateTime(SqlDataReader rd, string column)
        {
            if (rd[column] == DBNull.Value)
                return null;

            return Convert.ToDateTime(rd[column]);
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


    public class BasculaPreRegistroDto
    {
        public long? PreRegistroId { get; set; }
        public Guid PreRegistroGuid { get; set; }
        public string Token { get; set; } = "";
        public string FolioPreRegistro { get; set; } = "";
        public string Estatus { get; set; } = "PENDIENTE_CASETA";

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
        public string Observaciones { get; set; } = "";

        public string AreaOrigen { get; set; } = "";
        public string UsuarioCaptura { get; set; } = "";
        public DateTime FechaCaptura { get; set; }

        public Guid? MovimientoGuid { get; set; }
        public string FolioMovimiento { get; set; } = "";
        public DateTime? FechaEscaneoCaseta { get; set; }
        public string UsuarioCaseta { get; set; } = "";
    }

    public class BasculaPreRegistroCancelarDto
    {
        public string Token { get; set; } = "";
        public string Motivo { get; set; } = "";
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

        public long? PreRegistroId { get; set; }
        public Guid? PreRegistroGuid { get; set; }
        public string TokenPreRegistro { get; set; } = "";
        public string FolioPreRegistro { get; set; } = "";
        public string OrigenCaptura { get; set; } = "CAPTURA_CASETA";
        public string AreaOrigenPreRegistro { get; set; } = "";
        public string UsuarioCapturaPreRegistro { get; set; } = "";
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
