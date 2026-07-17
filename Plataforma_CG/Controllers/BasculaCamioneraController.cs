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

        private const string DefaultTerminalId = "CASETA-01";

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
        public async Task<IActionResult> Listar(int take = 500, string? estatus = "", string? q = "")
        {
            try
            {
                take = Math.Max(1, Math.Min(take, 5000));
                var rows = await LeerMovimientosAsync(take, estatus, q);

                return Ok(new
                {
                    ok = true,
                    total = rows.Count,
                    rows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listando movimientos de báscula desde SQL");
                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudieron consultar los movimientos de báscula: " + ex.Message
                });
            }
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

                await RegistrarBitacoraAsync(dto.FolioPreRegistro, "Creó pre-registro de báscula");

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

                await RegistrarBitacoraAsync(dto.FolioPreRegistro, "Escaneó pre-registro en caseta");

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

                await RegistrarBitacoraAsync(token, "Canceló pre-registro de báscula");

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

            var validation = ValidarMovimiento(dto, requiereSalida: false);
            if (!string.IsNullOrWhiteSpace(validation))
                return BadRequest(new { ok = false, msg = validation });

            dto.MovimientoGuid = dto.MovimientoGuid == Guid.Empty ? Guid.NewGuid() : dto.MovimientoGuid;
            dto.TerminalId = string.IsNullOrWhiteSpace(dto.TerminalId) ? DefaultTerminalId : dto.TerminalId.Trim().ToUpperInvariant();
            dto.Folio = string.IsNullOrWhiteSpace(dto.Folio) ? NuevoFolioLocal(dto.TerminalId) : dto.Folio.Trim().ToUpperInvariant();
            dto.Estatus = "PENDIENTE";
            dto.PesoSalida = 0;
            dto.PesoNeto = 0;
            dto.FechaEntrada = dto.FechaEntrada == default ? DateTime.Now : dto.FechaEntrada;
            dto.FechaSalida = null;
            dto.UsuarioEntrada = string.IsNullOrWhiteSpace(dto.UsuarioEntrada)
                ? (User?.Identity?.Name ?? dto.Usuario ?? "Usuario SIGO")
                : dto.UsuarioEntrada;
            dto.UsuarioSalida = "";
            dto.Usuario = dto.UsuarioEntrada;
            dto.OrigenCaptura = ResolverOrigenCaptura(dto);

            try
            {
                var result = await UpsertMovimientoAsync(dto, "PENDIENTE");
                dto.FolioServidor = result.FolioServidor;

                await MarcarPreRegistroMovimientoSiAplicaAsync(dto, "PESADO_ENTRADA", result.FolioServidor);

                return Ok(new
                {
                    ok = true,
                    msg = "Entrada guardada correctamente en SQL Server.",
                    movimientoId = result.MovimientoId,
                    movimientoGuid = result.MovimientoGuid,
                    folioLocal = result.FolioLocal,
                    folioServidor = result.FolioServidor,
                    folio = result.FolioServidor,
                    estatus = result.Estatus,
                    fechaSyncServidor = result.FechaSyncServidor,
                    origenCaptura = dto.OrigenCaptura,
                    folioPreRegistro = dto.FolioPreRegistro
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error guardando entrada de báscula en SQL");
                return BadRequest(new { ok = false, msg = "No se pudo guardar la entrada: " + ex.Message });
            }
        }

        [HttpPost("CerrarSalida")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CerrarSalida([FromBody] BasculaMovimientoDto dto)
        {
            if (dto == null)
                return BadRequest(new { ok = false, msg = "Solicitud vacía." });

            var validation = ValidarMovimiento(dto, requiereSalida: true);
            if (!string.IsNullOrWhiteSpace(validation))
                return BadRequest(new { ok = false, msg = validation });

            if (dto.MovimientoGuid == Guid.Empty)
                return BadRequest(new { ok = false, msg = "MovimientoGuid requerido para cerrar la misma entrada sin duplicarla." });

            dto.TerminalId = string.IsNullOrWhiteSpace(dto.TerminalId) ? DefaultTerminalId : dto.TerminalId.Trim().ToUpperInvariant();
            dto.Folio = string.IsNullOrWhiteSpace(dto.Folio) ? NuevoFolioLocal(dto.TerminalId) : dto.Folio.Trim().ToUpperInvariant();
            dto.Estatus = "CERRADO";
            dto.PesoNeto = Math.Abs(dto.PesoEntrada - dto.PesoSalida);
            dto.FechaEntrada = dto.FechaEntrada == default ? DateTime.Now : dto.FechaEntrada;
            dto.FechaSalida ??= DateTime.Now;
            dto.UsuarioEntrada = string.IsNullOrWhiteSpace(dto.UsuarioEntrada)
                ? (User?.Identity?.Name ?? dto.Usuario ?? "Usuario SIGO")
                : dto.UsuarioEntrada;
            dto.UsuarioSalida = string.IsNullOrWhiteSpace(dto.UsuarioSalida)
                ? (User?.Identity?.Name ?? dto.Usuario ?? "Usuario SIGO")
                : dto.UsuarioSalida;
            dto.Usuario = dto.UsuarioSalida;
            dto.OrigenCaptura = ResolverOrigenCaptura(dto);

            try
            {
                var result = await UpsertMovimientoAsync(dto, "CERRADO");
                dto.FolioServidor = result.FolioServidor;

                await MarcarPreRegistroMovimientoSiAplicaAsync(dto, "CERRADO", result.FolioServidor);

                return Ok(new
                {
                    ok = true,
                    msg = "Salida cerrada correctamente en SQL Server.",
                    movimientoId = result.MovimientoId,
                    movimientoGuid = result.MovimientoGuid,
                    folioLocal = result.FolioLocal,
                    folioServidor = result.FolioServidor,
                    folio = result.FolioServidor,
                    estatus = result.Estatus,
                    neto = dto.PesoNeto,
                    fechaSyncServidor = result.FechaSyncServidor,
                    origenCaptura = dto.OrigenCaptura,
                    folioPreRegistro = dto.FolioPreRegistro
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cerrando salida de báscula en SQL");
                return BadRequest(new { ok = false, msg = "No se pudo cerrar la salida: " + ex.Message });
            }
        }

        [HttpGet("Bitacora")]
        public async Task<IActionResult> Bitacora(int take = 500)
        {
            try
            {
                take = Math.Max(1, Math.Min(take, 5000));
                var rows = new List<BasculaBitacoraDto>();

                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var sql = @"
SELECT TOP (@take)
    Fecha,
    Usuario,
    Accion,
    FolioLocal,
    TerminalId,
    Detalle
FROM dbo.BasculaBitacora
ORDER BY Fecha DESC, BitacoraId DESC;";

                using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;

                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    rows.Add(new BasculaBitacoraDto
                    {
                        Fecha = GetDateTime(rd, "Fecha") ?? DateTime.MinValue,
                        Usuario = GetString(rd, "Usuario"),
                        Accion = GetString(rd, "Accion"),
                        Folio = GetString(rd, "FolioLocal"),
                        TerminalId = GetString(rd, "TerminalId"),
                        Detalle = GetString(rd, "Detalle")
                    });
                }

                return Ok(new { ok = true, total = rows.Count, rows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consultando bitácora de báscula");
                return BadRequest(new { ok = false, msg = "No se pudo consultar la bitácora: " + ex.Message });
            }
        }

        [HttpGet("Exportar")]
        public async Task<IActionResult> Exportar()
        {
            try
            {
                var rows = await LeerMovimientosAsync(50000, "", "");
                var csv = new StringBuilder();
                csv.AppendLine("FolioServidor,FolioLocal,Estatus,Terminal,TipoMovimiento,Clasificacion,Tercero,CodigoSap,Placas,Producto,Sku,Documento,Chofer,Origen,Destino,Condicion,PesoEntrada,PesoSalida,PesoNeto,FechaEntrada,FechaSalida,UsuarioEntrada,UsuarioSalida,OrigenCaptura,FolioPreRegistro");

                foreach (var r in rows)
                {
                    csv.Append(Csv(r.FolioServidor)).Append(',')
                       .Append(Csv(r.Folio)).Append(',')
                       .Append(Csv(r.Estatus)).Append(',')
                       .Append(Csv(r.TerminalId)).Append(',')
                       .Append(Csv(r.TipoMovimiento)).Append(',')
                       .Append(Csv(r.Clasificacion)).Append(',')
                       .Append(Csv(r.Tercero)).Append(',')
                       .Append(Csv(r.CodigoSap)).Append(',')
                       .Append(Csv(r.Placas)).Append(',')
                       .Append(Csv(r.Producto)).Append(',')
                       .Append(Csv(r.Sku)).Append(',')
                       .Append(Csv(r.Documento)).Append(',')
                       .Append(Csv(r.Chofer)).Append(',')
                       .Append(Csv(r.Origen)).Append(',')
                       .Append(Csv(r.Destino)).Append(',')
                       .Append(Csv(r.Condicion)).Append(',')
                       .Append(r.PesoEntrada.ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append(r.PesoSalida.ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append(r.PesoNeto.ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append(Csv(r.FechaEntrada.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
                       .Append(Csv(r.FechaSalida?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")).Append(',')
                       .Append(Csv(r.UsuarioEntrada)).Append(',')
                       .Append(Csv(r.UsuarioSalida)).Append(',')
                       .Append(Csv(r.OrigenCaptura)).Append(',')
                       .Append(Csv(r.FolioPreRegistro))
                       .AppendLine();
                }

                return File(
                    Encoding.UTF8.GetBytes(csv.ToString()),
                    "text/csv; charset=utf-8",
                    $"BasculaCamionera_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exportando movimientos de báscula");
                return BadRequest(new { ok = false, msg = "No se pudo exportar el historial: " + ex.Message });
            }
        }

        [HttpPost("Sync/Movimiento")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SyncMovimiento([FromBody] BasculaMovimientoDto dto)
        {
            if (dto == null)
                return BadRequest(new { ok = false, msg = "Solicitud vacía." });

            var estatus = string.IsNullOrWhiteSpace(dto.Estatus)
                ? "PENDIENTE"
                : dto.Estatus.Trim().ToUpperInvariant();

            var validation = ValidarMovimiento(dto, requiereSalida: estatus == "CERRADO");
            if (!string.IsNullOrWhiteSpace(validation))
                return BadRequest(new { ok = false, msg = validation });

            dto.MovimientoGuid = dto.MovimientoGuid == Guid.Empty ? Guid.NewGuid() : dto.MovimientoGuid;
            dto.TerminalId = string.IsNullOrWhiteSpace(dto.TerminalId) ? DefaultTerminalId : dto.TerminalId.Trim().ToUpperInvariant();
            dto.Folio = string.IsNullOrWhiteSpace(dto.Folio) ? NuevoFolioLocal(dto.TerminalId) : dto.Folio.Trim().ToUpperInvariant();
            dto.Estatus = estatus;
            dto.FechaEntrada = dto.FechaEntrada == default ? DateTime.Now : dto.FechaEntrada;
            dto.FechaCreacionLocal ??= DateTime.Now;

            if (estatus == "PENDIENTE")
            {
                dto.PesoSalida = 0;
                dto.FechaSalida = null;
                dto.UsuarioSalida = "";
            }
            else if (estatus == "CERRADO")
            {
                dto.FechaSalida ??= DateTime.Now;
                dto.PesoNeto = Math.Abs(dto.PesoEntrada - dto.PesoSalida);
            }

            dto.UsuarioEntrada = string.IsNullOrWhiteSpace(dto.UsuarioEntrada)
                ? (User?.Identity?.Name ?? dto.Usuario ?? "Usuario SIGO")
                : dto.UsuarioEntrada;

            if (estatus == "CERRADO" && string.IsNullOrWhiteSpace(dto.UsuarioSalida))
                dto.UsuarioSalida = User?.Identity?.Name ?? dto.Usuario ?? "Usuario SIGO";

            dto.OrigenCaptura = ResolverOrigenCaptura(dto);

            try
            {
                var result = await UpsertMovimientoAsync(dto, estatus);
                dto.FolioServidor = result.FolioServidor;

                await MarcarPreRegistroMovimientoSiAplicaAsync(
                    dto,
                    estatus == "CERRADO" ? "CERRADO" : "PESADO_ENTRADA",
                    result.FolioServidor);

                return Ok(new
                {
                    ok = true,
                    msg = estatus == "CERRADO"
                        ? "Salida sincronizada correctamente con SQL Server."
                        : "Entrada sincronizada correctamente con SQL Server.",
                    movimientoId = result.MovimientoId,
                    movimientoGuid = result.MovimientoGuid,
                    folioLocal = result.FolioLocal,
                    folioServidor = result.FolioServidor,
                    folio = result.FolioServidor,
                    estatus = result.Estatus,
                    fechaSyncServidor = result.FechaSyncServidor,
                    origenCaptura = dto.OrigenCaptura,
                    folioPreRegistro = dto.FolioPreRegistro
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sincronizando movimiento de báscula");
                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudo guardar el movimiento en SQL Server: " + ex.Message
                });
            }
        }
        [HttpGet("BuscarClientes")]
        public async Task<IActionResult> BuscarClientes(
            string? q = "",
            int take = 30)
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
    X.CodigoSap,
    X.Nombre,
    X.Clasificacion,
    X.Canal,
    X.VendedorId,
    X.VendedorNombre,
    X.PriceListNum,
    X.PriceListName,
    X.AplicaPresupuesto
FROM
(
    /* ============================================
       CLIENTES
       ============================================ */
    SELECT
        CAST(c.Cliente AS NVARCHAR(80)) AS CodigoSap,
        CAST(c.Nombrecliente AS NVARCHAR(250)) AS Nombre,
        CAST(ISNULL(c.U_MT_Clasificacion, 'CLIENTE') AS NVARCHAR(80))
            AS Clasificacion,
        CAST(ISNULL(c.U_CANAL, 'Cliente') AS NVARCHAR(100))
            AS Canal,
        c.VendedorId,
        CAST(c.VendedorNombre AS NVARCHAR(200))
            AS VendedorNombre,
        c.PriceListNum,
        CAST(c.PriceListName AS NVARCHAR(200))
            AS PriceListName,
        c.AplicaPresupuesto
    FROM dbo.ClienteSap c
    WHERE
        @q = ''
        OR c.Cliente LIKE @like
        OR c.Nombrecliente LIKE @like

    UNION ALL

    /* ============================================
       PROVEEDORES
       ============================================ */
    SELECT
        CAST(p.Proveedor AS NVARCHAR(80)) AS CodigoSap,
        CAST(p.NombreProveedor AS NVARCHAR(250)) AS Nombre,
        CAST('PROVEEDOR' AS NVARCHAR(80)) AS Clasificacion,
        CAST(
            ISNULL(NULLIF(p.GrupoNombre, ''), 'Proveedor')
            AS NVARCHAR(100)
        ) AS Canal,
        CAST(NULL AS INT) AS VendedorId,
        CAST(NULL AS NVARCHAR(200)) AS VendedorNombre,
        CAST(NULL AS INT) AS PriceListNum,
        CAST(NULL AS NVARCHAR(200)) AS PriceListName,
        CAST(0 AS INT) AS AplicaPresupuesto
    FROM dbo.ProveedorSap p
    WHERE
        @q = ''
        OR p.Proveedor LIKE @like
        OR p.NombreProveedor LIKE @like
        OR p.RFC LIKE @like
) X
ORDER BY
    CASE
        WHEN X.Clasificacion = 'PROVEEDOR' THEN 1
        ELSE 0
    END,
    X.Nombre;";

                using var cmd = new SqlCommand(sql, cn);

                cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
                cmd.Parameters.Add("@q", SqlDbType.NVarChar, 250).Value = query;
                cmd.Parameters.Add("@like", SqlDbType.NVarChar, 260).Value =
                    "%" + query + "%";

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    rows.Add(new ClienteSapLookupDto
                    {
                        CodigoSap = GetString(rd, "CodigoSap"),
                        Nombre = GetString(rd, "Nombre"),
                        Clasificacion = GetString(rd, "Clasificacion"),
                        Canal = GetString(rd, "Canal"),
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
                _logger.LogError(
                    ex,
                    "Error buscando clientes y proveedores SAP");

                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudieron cargar clientes y proveedores: "
                        + ex.Message
                });
            }
        }

        [HttpGet("BuscarProveedores")]
        public async Task<IActionResult> BuscarProveedores(
    string? q = "",
    int take = 30)
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
    CAST(Proveedor AS NVARCHAR(80)) AS CodigoSap,
    CAST(NombreProveedor AS NVARCHAR(250)) AS Nombre,
    CAST('PROVEEDOR' AS NVARCHAR(80)) AS Clasificacion,
    CAST(
        ISNULL(NULLIF(GrupoNombre, ''), 'Proveedor')
        AS NVARCHAR(100)
    ) AS Canal
FROM dbo.ProveedorSap
WHERE
    (
        @q = ''
        OR Proveedor LIKE @like
        OR NombreProveedor LIKE @like
        OR RFC LIKE @like
        OR GrupoNombre LIKE @like
    )
    AND ISNULL(Activo, 1) = 1
    AND ISNULL(ExisteEnSap, 1) = 1
ORDER BY NombreProveedor;";

                using var cmd = new SqlCommand(sql, cn);

                cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
                cmd.Parameters.Add("@q", SqlDbType.NVarChar, 250).Value = query;
                cmd.Parameters.Add("@like", SqlDbType.NVarChar, 260).Value =
                    "%" + query + "%";

                using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    rows.Add(new ClienteSapLookupDto
                    {
                        CodigoSap = GetString(rd, "CodigoSap"),
                        Nombre = GetString(rd, "Nombre"),
                        Clasificacion = GetString(rd, "Clasificacion"),
                        Canal = GetString(rd, "Canal"),
                        VendedorId = null,
                        VendedorNombre = "",
                        PriceListNum = null,
                        PriceListName = "",
                        AplicaPresupuesto = 0
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
                _logger.LogError(
                    ex,
                    "Error buscando proveedores SAP");

                return BadRequest(new
                {
                    ok = false,
                    msg = "No se pudieron cargar proveedores: "
                        + ex.Message
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

        private async Task MarcarPreRegistroMovimientoSiAplicaAsync(BasculaMovimientoDto dto, string estatusPreRegistro, string? folioServidor = null)
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
                cmd.Parameters.AddWithValue("@FolioMovimiento", DbValue(!string.IsNullOrWhiteSpace(folioServidor) ? folioServidor : dto.Folio));
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

        private static string NuevoFolioLocal(string terminalId)
        {
            var terminal = Regex.Replace((terminalId ?? DefaultTerminalId).ToUpperInvariant(), "[^A-Z0-9]", "");
            if (terminal.Length > 16) terminal = terminal.Substring(0, 16);
            if (string.IsNullOrWhiteSpace(terminal)) terminal = "CASETA01";

            var value = $"LOCAL-{terminal}-{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            return value.Length <= 60 ? value : value.Substring(0, 60);
        }

        private static string ValidarMovimiento(BasculaMovimientoDto dto, bool requiereSalida)
        {
            if (string.IsNullOrWhiteSpace(dto.Tercero))
                return "Capture proveedor / cliente.";

            if (string.IsNullOrWhiteSpace(dto.Producto))
                return "Capture producto.";

            if (string.IsNullOrWhiteSpace(dto.Placas))
                return "Capture placas.";

            if (string.IsNullOrWhiteSpace(dto.TipoMovimiento))
                return "Seleccione tipo de movimiento.";

            if (dto.PesoEntrada <= 0)
                return "El peso de entrada debe ser mayor a cero.";

            if (requiereSalida && dto.PesoSalida <= 0)
                return "El peso de salida debe ser mayor a cero.";

            return "";
        }

        private async Task<BasculaSyncResult> UpsertMovimientoAsync(BasculaMovimientoDto dto, string estatus)
        {
            using var cn = new SqlConnection(GetConnectionString());
            await cn.OpenAsync();

            using var cmd = new SqlCommand("dbo.sp_Bascula_UpsertMovimiento", cn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };

            cmd.Parameters.Add("@MovimientoGuid", SqlDbType.UniqueIdentifier).Value = dto.MovimientoGuid;
            cmd.Parameters.Add("@TerminalId", SqlDbType.NVarChar, 60).Value = dto.TerminalId;
            cmd.Parameters.Add("@FolioLocal", SqlDbType.NVarChar, 60).Value = dto.Folio;
            cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 20).Value = estatus;
            cmd.Parameters.Add("@TipoMovimiento", SqlDbType.NVarChar, 60).Value = dto.TipoMovimiento;
            cmd.Parameters.Add("@Clasificacion", SqlDbType.NVarChar, 80).Value = DbValue(dto.Clasificacion);
            cmd.Parameters.Add("@Tercero", SqlDbType.NVarChar, 250).Value = dto.Tercero.Trim();
            cmd.Parameters.Add("@CodigoSap", SqlDbType.NVarChar, 60).Value = DbValue(dto.CodigoSap);
            cmd.Parameters.Add("@Placas", SqlDbType.NVarChar, 40).Value = dto.Placas.Trim().ToUpperInvariant();
            cmd.Parameters.Add("@Producto", SqlDbType.NVarChar, 250).Value = dto.Producto.Trim();
            cmd.Parameters.Add("@Sku", SqlDbType.NVarChar, 80).Value = DbValue(dto.Sku);
            cmd.Parameters.Add("@Documento", SqlDbType.NVarChar, 120).Value = DbValue(dto.Documento);
            cmd.Parameters.Add("@Chofer", SqlDbType.NVarChar, 160).Value = DbValue(dto.Chofer);
            cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 180).Value = DbValue(dto.Origen);
            cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 180).Value = DbValue(dto.Destino);
            cmd.Parameters.Add("@Condicion", SqlDbType.NVarChar, 120).Value = DbValue(dto.Condicion);

            var pEntrada = cmd.Parameters.Add("@PesoEntrada", SqlDbType.Decimal);
            pEntrada.Precision = 18;
            pEntrada.Scale = 2;
            pEntrada.Value = dto.PesoEntrada;

            var pSalida = cmd.Parameters.Add("@PesoSalida", SqlDbType.Decimal);
            pSalida.Precision = 18;
            pSalida.Scale = 2;
            pSalida.Value = dto.PesoSalida > 0 ? (object)dto.PesoSalida : DBNull.Value;

            cmd.Parameters.Add("@CapturaManual", SqlDbType.Bit).Value = EsManual(dto.CapturaManual);
            cmd.Parameters.Add("@MotivoManual", SqlDbType.NVarChar, 300).Value = DbValue(dto.MotivoManual);
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = DbValue(dto.Observaciones);
            cmd.Parameters.Add("@FechaEntrada", SqlDbType.DateTime2).Value = dto.FechaEntrada == default ? DateTime.Now : dto.FechaEntrada;
            cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = dto.FechaSalida.HasValue ? (object)dto.FechaSalida.Value : DBNull.Value;
            cmd.Parameters.Add("@UsuarioEntrada", SqlDbType.NVarChar, 160).Value = DbValue(dto.UsuarioEntrada);
            cmd.Parameters.Add("@UsuarioSalida", SqlDbType.NVarChar, 160).Value = DbValue(dto.UsuarioSalida);
            cmd.Parameters.Add("@RawEntrada", SqlDbType.NVarChar, 1000).Value = DbValue(dto.RawEntrada);
            cmd.Parameters.Add("@RawSalida", SqlDbType.NVarChar, 1000).Value = DbValue(dto.RawSalida);
            cmd.Parameters.Add("@PesoEntradaEstable", SqlDbType.Bit).Value = dto.PesoEntradaEstable;
            cmd.Parameters.Add("@PesoSalidaEstable", SqlDbType.Bit).Value = dto.PesoSalidaEstable;
            cmd.Parameters.Add("@CreadoOffline", SqlDbType.Bit).Value = dto.CreadoOffline;
            cmd.Parameters.Add("@FechaCreacionLocal", SqlDbType.DateTime2).Value = dto.FechaCreacionLocal ?? DateTime.Now;

            using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                throw new InvalidOperationException("El procedimiento no devolvió el movimiento guardado.");

            return new BasculaSyncResult
            {
                MovimientoId = Convert.ToInt64(rd["MovimientoId"]),
                MovimientoGuid = rd["MovimientoGuid"] is Guid guid ? guid : Guid.Parse(rd["MovimientoGuid"].ToString()!),
                FolioLocal = rd["FolioLocal"]?.ToString() ?? dto.Folio,
                FolioServidor = rd["FolioServidor"]?.ToString() ?? "",
                Estatus = rd["Estatus"]?.ToString() ?? estatus,
                FechaSyncServidor = Convert.ToDateTime(rd["FechaSyncServidor"])
            };
        }

        private async Task<List<BasculaMovimientoDto>> LeerMovimientosAsync(int take, string? estatus, string? q)
        {
            var rows = new List<BasculaMovimientoDto>();
            var status = (estatus ?? "").Trim().ToUpperInvariant();
            var query = (q ?? "").Trim();

            using var cn = new SqlConnection(GetConnectionString());
            await cn.OpenAsync();

            var sql = @"
SELECT TOP (@take)
    MovimientoId, MovimientoGuid, TerminalId, FolioLocal, FolioServidor,
    Estatus, TipoMovimiento, Clasificacion, Tercero, CodigoSap, Placas,
    Producto, Sku, Documento, Chofer, Origen, Destino, Condicion,
    PesoEntrada, PesoSalida, PesoNeto,
    CapturaManual, MotivoManual, Observaciones,
    FechaEntrada, FechaSalida, UsuarioEntrada, UsuarioSalida,
    RawEntrada, RawSalida, PesoEntradaEstable, PesoSalidaEstable,
    CreadoOffline, FechaCreacionLocal, FechaSyncServidor
FROM dbo.BasculaMovimiento
WHERE
    (@estatus = '' OR Estatus = @estatus)
    AND
    (
        @q = ''
        OR FolioServidor LIKE @like
        OR FolioLocal LIKE @like
        OR Tercero LIKE @like
        OR Producto LIKE @like
        OR Placas LIKE @like
        OR Documento LIKE @like
        OR Chofer LIKE @like
    )
ORDER BY FechaEntrada DESC, MovimientoId DESC;";

            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
            cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
            cmd.Parameters.Add("@estatus", SqlDbType.NVarChar, 20).Value = status;
            cmd.Parameters.Add("@q", SqlDbType.NVarChar, 250).Value = query;
            cmd.Parameters.Add("@like", SqlDbType.NVarChar, 260).Value = "%" + query + "%";

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                rows.Add(MapMovimiento(rd));

            return rows;
        }

        private static BasculaMovimientoDto MapMovimiento(SqlDataReader rd)
        {
            var usuarioEntrada = GetString(rd, "UsuarioEntrada");
            var usuarioSalida = GetString(rd, "UsuarioSalida");

            return new BasculaMovimientoDto
            {
                MovimientoId = GetLongNullable(rd, "MovimientoId"),
                MovimientoGuid = GetGuid(rd, "MovimientoGuid"),
                TerminalId = GetString(rd, "TerminalId"),
                Folio = GetString(rd, "FolioLocal"),
                FolioServidor = GetString(rd, "FolioServidor"),
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
                PesoEntrada = GetDecimal(rd, "PesoEntrada") ?? 0,
                PesoSalida = GetDecimal(rd, "PesoSalida") ?? 0,
                PesoNeto = GetDecimal(rd, "PesoNeto") ?? 0,
                CapturaManual = GetBool(rd, "CapturaManual") ? "Sí" : "No",
                MotivoManual = GetString(rd, "MotivoManual"),
                Observaciones = GetString(rd, "Observaciones"),
                FechaEntrada = GetDateTime(rd, "FechaEntrada") ?? DateTime.MinValue,
                FechaSalida = GetDateTime(rd, "FechaSalida"),
                UsuarioEntrada = usuarioEntrada,
                UsuarioSalida = usuarioSalida,
                Usuario = !string.IsNullOrWhiteSpace(usuarioSalida) ? usuarioSalida : usuarioEntrada,
                RawEntrada = GetString(rd, "RawEntrada"),
                RawSalida = GetString(rd, "RawSalida"),
                PesoEntradaEstable = GetBool(rd, "PesoEntradaEstable"),
                PesoSalidaEstable = GetBool(rd, "PesoSalidaEstable"),
                CreadoOffline = GetBool(rd, "CreadoOffline"),
                FechaCreacionLocal = GetDateTime(rd, "FechaCreacionLocal")
            };
        }

        private async Task RegistrarBitacoraAsync(string folio, string accion, string? detalle = null)
        {
            try
            {
                using var cn = new SqlConnection(GetConnectionString());
                await cn.OpenAsync();

                var sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.BasculaTerminal WHERE TerminalId = @TerminalId)
BEGIN
    INSERT INTO dbo.BasculaTerminal(TerminalId, Nombre, Sitio, Activa, UltimaConexion, FechaModificacion)
    VALUES(@TerminalId, @TerminalId, 'Sin clasificar', 1, SYSDATETIME(), SYSDATETIME());
END;

INSERT INTO dbo.BasculaBitacora
(
    BitacoraGuid, MovimientoGuid, TerminalId, FolioLocal,
    Fecha, Usuario, Accion, Detalle, CreadoOffline, FechaSyncServidor
)
VALUES
(
    NEWID(), NULL, @TerminalId, @Folio,
    SYSDATETIME(), @Usuario, @Accion, @Detalle, 0, SYSDATETIME()
);";

                using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.Add("@TerminalId", SqlDbType.NVarChar, 60).Value = DefaultTerminalId;
                cmd.Parameters.Add("@Folio", SqlDbType.NVarChar, 60).Value = DbValue(folio);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 160).Value = DbValue(User?.Identity?.Name ?? "Usuario SIGO");
                cmd.Parameters.Add("@Accion", SqlDbType.NVarChar, 250).Value = accion;
                cmd.Parameters.Add("@Detalle", SqlDbType.NVarChar, 1000).Value = DbValue(detalle);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo registrar la bitácora de báscula en SQL");
            }
        }

        private static bool GetBool(SqlDataReader rd, string column)
        {
            return rd[column] != DBNull.Value && Convert.ToBoolean(rd[column]);
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
        public long? MovimientoId { get; set; }
        public Guid MovimientoGuid { get; set; }
        public string TerminalId { get; set; } = "CASETA-01";
        public string Folio { get; set; } = "";
        public string FolioServidor { get; set; } = "";
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
        public string TerminalId { get; set; } = "";
        public string Detalle { get; set; } = "";
    }

    internal sealed class BasculaSyncResult
    {
        public long MovimientoId { get; set; }
        public Guid MovimientoGuid { get; set; }
        public string FolioLocal { get; set; } = "";
        public string FolioServidor { get; set; } = "";
        public string Estatus { get; set; } = "";
        public DateTime FechaSyncServidor { get; set; }
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
