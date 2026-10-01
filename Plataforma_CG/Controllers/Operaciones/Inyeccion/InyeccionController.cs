
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Plataforma_CG.AccesoDatos.Operaciones.Inyeccion;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Operaciones.Inyeccion;
using Plataforma_CG.Services;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Plataforma_CG.Controllers.Operaciones.Inyeccion
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class InyeccionController : ControllerBase
    {
        Lotes l = new Lotes();
        private readonly Receta r;
        Conexiones co = new Conexiones();
        AccesoPermisos permisos = new AccesoPermisos();
        private readonly ImagenProductoService _imgservice;
        private readonly BasculaService _basc;
        private readonly ILogger<InyeccionController> _logger;
        private readonly AccesoRecetas _accesoRecetas;
        private readonly int _capturaMaxAgeSeconds;
        private readonly int _capturaFutureToleranceSeconds;

        public InyeccionController(
            ImagenProductoService imgservice,
            AccesoRecetas accesoRecetas,
            ILogger<InyeccionController> logger,
            IConfiguration configuration)
        {
            _imgservice = imgservice;
            _basc = new BasculaService();
            _logger = logger;
            _accesoRecetas = accesoRecetas;
            _capturaMaxAgeSeconds = Math.Clamp(
                configuration.GetValue<int?>("InyeccionesSeguridad:CapturaMaxAgeSeconds") ?? 120,
                30,
                600);
            _capturaFutureToleranceSeconds = Math.Clamp(
                configuration.GetValue<int?>("InyeccionesSeguridad:CapturaFutureToleranceSeconds") ?? 60,
                0,
                300);
            l = new Lotes();
            r = new Receta(accesoRecetas);
            permisos = new AccesoPermisos();
        }
        [HttpGet("ObtenerLotes")]
        //[RevisarPermiso("INYECCION", "ESCRIBIR")]
        public async Task<IActionResult> ObtenerLotes()
        {
            var lista = await l.ConsultarLotes();
            return Ok(lista);
        }
        [HttpGet("ListarProductos")]
        public async Task<IActionResult> ObtenerProductos(string plan)
        {
            var lista = await r.ListarProductos(plan);
            return Ok(lista);
        }
        [HttpGet("ObtenerReceta")]
        public async Task<IActionResult> ObtenerReceta(string sku)
        {
            var dato = await r.ObtenerReceta(sku);
            return Ok(dato);
        }
        [HttpGet("ObtenerImagen")]
        public IActionResult ObtenerImagen(string nombre, string sku)
        {
            var ruta = _imgservice.ObtenerRutaImagen(nombre, sku);
            return PhysicalFile(ruta, "image/png");
        }
        [HttpGet("ObtenerPeso")]
        public async Task<string> Peso(string ip, string comando = "P")
        {
            var peso = await _basc.Bascula(ip, comando);
            return peso;
        }
        [HttpGet("ObtenerTaras")]
        public async Task<IActionResult> Taras()
        {
            var taras = await r.ObtenerTaras();
            return Ok(taras);
        }
        [HttpPost("CapturarEntrada")]
        public async Task<IActionResult> InsertarEntrada([FromBody] CapturaEntradaRequest solicitud)
        {
            try
            {
                Response.Headers.CacheControl = "no-store, no-cache, max-age=0";

                ValidarAntiguedadCaptura(solicitud);
                var contexto = await ValidarContextoNegocio(
                    solicitud.Entrada,
                    solicitud.ProductoSKU,
                    solicitud.Producto,
                    solicitud.Lote,
                    validarReceta: true);

                EntradaModel entrada = await r.InsertarEntrada(solicitud.Entrada, solicitud.CapturaGuid);
                EntradaModel? persistida = await r.ConsultarEntrada(entrada.Id);

                if (persistida == null)
                    throw new InvalidOperationException("La captura se insertó, pero no pudo leerse nuevamente para verificarla.");

                if (!EntradasCoinciden(entrada, persistida, out string diferencia))
                {
                    throw new InvalidOperationException(
                        $"La fila guardada no coincide con la captura confirmada ({diferencia}). Se bloqueó la impresión.");
                }

                return Ok(new
                {
                    success = true,
                    capturaGuid = solicitud.CapturaGuid,
                    entrada = persistida,
                    productoSKU = persistida.SKU,
                    producto = contexto.Producto,
                    lote = contexto.Lote
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (SqlException ex) when (ex.Number is 207 or 208)
            {
                _logger.LogError(ex, "Falta instalar el esquema actualizado de capturas de Inyecciones");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Falta instalar el esquema actualizado de Inyecciones en la base configurada. Ejecute el script SQL de bitácora antes de capturar."
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error de SQL Server al guardar una captura de Inyecciones");
                return StatusCode(503, new
                {
                    success = false,
                    message = "No fue posible guardar la captura directamente en SQL Server. Verifique la conexión de InyeccionesSql."
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "No fue posible revalidar el catálogo de la captura de Inyecciones");
                return StatusCode(503, new
                {
                    success = false,
                    message = "No fue posible validar producto, receta y lote contra el catálogo. No se guardó la captura."
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Se bloqueó una captura de Inyecciones por inconsistencia");
                return Conflict(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("Imprimir")]
        public async Task<IActionResult> Imprimir([FromBody] ImpresionEntradaRequest solicitud)
        {
            try
            {
                Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                ValidarSolicitudImpresion(solicitud);

                EntradaModel? persistida = await r.ConsultarEntrada(solicitud.Entrada.Id);
                if (persistida == null)
                    return NotFound(new { success = false, message = "No existe la entrada que se pretende imprimir." });

                if (!EntradasCoinciden(solicitud.Entrada, persistida, out string diferencia))
                {
                    return Conflict(new
                    {
                        success = false,
                        message = $"Se bloqueó la impresión: la etiqueta no coincide con SQL Server ({diferencia})."
                    });
                }

                var contexto = await ValidarContextoNegocio(
                    persistida,
                    solicitud.ProductoSKU,
                    solicitud.Producto,
                    solicitud.Lote,
                    validarReceta: false);

                solicitud.IpImpresora = solicitud.IpImpresora.Trim();
                solicitud.ProductoSKU = persistida.SKU;
                solicitud.Producto = contexto.Producto;
                solicitud.Lote = contexto.Lote;

                string payloadZpl = co.GenerarEtiqueta(persistida, contexto.Lote, contexto.Producto);
                string payloadSha256 = Convert.ToHexString(
                    SHA256.HashData(Encoding.ASCII.GetBytes(payloadZpl)));
                string usuario = User.FindFirst("Correo")?.Value
                    ?? User.Identity?.Name
                    ?? persistida.UsSIGO
                    ?? string.Empty;

                long logId = await _accesoRecetas.RegistrarIntentoImpresion(
                    solicitud,
                    persistida,
                    usuario,
                    payloadZpl,
                    payloadSha256);

                var resultado = await Task.Run(() =>
                    co.EnviarEtiqueta(solicitud.IpImpresora, payloadZpl));

                await _accesoRecetas.ActualizarResultadoImpresion(logId, resultado.ok, resultado.mensaje);

                if (!resultado.ok)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = resultado.mensaje,
                        logId,
                        payloadSha256
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = resultado.mensaje,
                    id = persistida.Id,
                    sku = persistida.SKU,
                    producto = contexto.Producto,
                    logId,
                    payloadSha256
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                _logger.LogError(ex, "No existe dbo.InyeccionImpresionLog");
                return StatusCode(500, new
                {
                    success = false,
                    message = "No existe la tabla de bitácora de impresión. Ejecute el script SQL del módulo antes de imprimir."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar o imprimir una entrada de Inyecciones");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Error al imprimir la entrada {solicitud?.Entrada?.Id}: {ex.Message}"
                });
            }
        }

        [HttpGet("BitacoraImpresiones")]
        public async Task<IActionResult> BitacoraImpresiones(
            DateTimeOffset? desdeUtc,
            DateTimeOffset? hastaUtc,
            string? sku,
            string? producto,
            string? estado,
            int limite = 500)
        {
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";

            DateTimeOffset hasta = hastaUtc ?? DateTimeOffset.UtcNow.AddMinutes(1);
            DateTimeOffset desde = desdeUtc ?? hasta.AddDays(-7);

            if (desde >= hasta)
                return BadRequest(new { success = false, message = "El rango de fechas no es válido." });
            if (hasta - desde > TimeSpan.FromDays(93))
                return BadRequest(new { success = false, message = "El rango máximo de consulta es de 93 días." });

            try
            {
                var datos = await _accesoRecetas.ConsultarBitacoraImpresion(
                    desde.UtcDateTime,
                    hasta.UtcDateTime,
                    sku,
                    producto,
                    estado,
                    limite);

                return Ok(datos);
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                _logger.LogError(ex, "No existe dbo.InyeccionImpresionLog para consultar");
                return StatusCode(500, new
                {
                    success = false,
                    message = "La tabla de bitácora de impresión todavía no está instalada."
                });
            }
        }

        [HttpGet("BitacoraImpresiones/{id:long}")]
        public async Task<IActionResult> BitacoraImpresionDetalle(long id)
        {
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";

            try
            {
                InyeccionImpresionLogModel? dato =
                    await _accesoRecetas.ConsultarBitacoraImpresionPorId(id);

                return dato == null
                    ? NotFound(new { success = false, message = "No se encontró el registro de impresión." })
                    : Ok(dato);
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                _logger.LogError(ex, "No existe dbo.InyeccionImpresionLog para consultar el detalle");
                return StatusCode(500, new
                {
                    success = false,
                    message = "La tabla de bitácora de impresión todavía no está instalada."
                });
            }
        }

        [HttpPost("BitacoraImpresiones/{id:long}/Reimprimir")]
        public async Task<IActionResult> ReimprimirDesdeBitacora(
            long id,
            [FromBody] ReimpresionBitacoraRequest solicitud)
        {
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";

            try
            {
                ValidarSolicitudReimpresion(solicitud);

                InyeccionImpresionLogModel? origen =
                    await _accesoRecetas.ConsultarBitacoraImpresionPorId(id);

                if (origen == null)
                    return NotFound(new { success = false, message = "No se encontró el registro que se pretende reimprimir." });

                if (string.IsNullOrWhiteSpace(origen.PayloadZpl) ||
                    string.IsNullOrWhiteSpace(origen.PayloadSha256))
                {
                    return Conflict(new
                    {
                        success = false,
                        message = "El registro histórico no contiene un ZPL y una huella válidos. Se bloqueó la reimpresión."
                    });
                }

                string hashCalculado = Convert.ToHexString(
                    SHA256.HashData(Encoding.ASCII.GetBytes(origen.PayloadZpl)));

                if (!string.Equals(hashCalculado, origen.PayloadSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Se bloqueó la reimpresión del log {LogId}: la huella SHA-256 del ZPL histórico no coincide",
                        id);
                    return Conflict(new
                    {
                        success = false,
                        message = "La huella del ZPL histórico no coincide con su contenido. Se bloqueó la reimpresión."
                    });
                }

                EntradaModel? persistida = await r.ConsultarEntrada(origen.EntradaId);
                if (persistida == null)
                    return Conflict(new { success = false, message = "La captura original ya no existe en SQL Server." });

                if (!BitacoraCoincideConEntrada(origen, persistida, out string diferencia))
                {
                    _logger.LogWarning(
                        "Se bloqueó la reimpresión del log {LogId}: el campo {Campo} no coincide con la entrada {EntradaId}",
                        id,
                        diferencia,
                        origen.EntradaId);
                    return Conflict(new
                    {
                        success = false,
                        message = $"El registro histórico no coincide con la captura original ({diferencia}). Se bloqueó la reimpresión."
                    });
                }

                solicitud.IpImpresora = solicitud.IpImpresora.Trim();
                var intento = new ImpresionEntradaRequest
                {
                    SolicitudGuid = solicitud.SolicitudGuid,
                    CapturaGuid = origen.CapturaGuid,
                    EsReimpresion = true,
                    IpImpresora = solicitud.IpImpresora,
                    Entrada = persistida,
                    ProductoSKU = origen.SKU,
                    Producto = origen.Producto,
                    Lote = origen.Lote
                };

                string usuario = User.FindFirst("Correo")?.Value
                    ?? User.Identity?.Name
                    ?? persistida.UsSIGO
                    ?? string.Empty;

                long nuevoLogId = await _accesoRecetas.RegistrarIntentoImpresion(
                    intento,
                    persistida,
                    usuario,
                    origen.PayloadZpl,
                    hashCalculado);

                var resultado = await Task.Run(() =>
                    co.EnviarEtiqueta(solicitud.IpImpresora, origen.PayloadZpl));

                await _accesoRecetas.ActualizarResultadoImpresion(
                    nuevoLogId,
                    resultado.ok,
                    resultado.mensaje);

                if (!resultado.ok)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = resultado.mensaje,
                        logId = nuevoLogId,
                        origenLogId = origen.Id,
                        payloadSha256 = hashCalculado
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = resultado.mensaje,
                    logId = nuevoLogId,
                    origenLogId = origen.Id,
                    impresoraIp = solicitud.IpImpresora,
                    payloadSha256 = hashCalculado
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                _logger.LogWarning(ex, "Se recibió una solicitud duplicada de reimpresión para el log {LogId}", id);
                return Conflict(new
                {
                    success = false,
                    message = "Esta solicitud de reimpresión ya fue registrada. Actualice la bitácora antes de volver a intentarlo."
                });
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                _logger.LogError(ex, "No existe dbo.InyeccionImpresionLog para reimprimir");
                return StatusCode(500, new
                {
                    success = false,
                    message = "La tabla de bitácora de impresión todavía no está instalada."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al reimprimir desde el registro {LogId} de Inyecciones", id);
                return StatusCode(500, new
                {
                    success = false,
                    message = $"No fue posible completar la reimpresión: {ex.Message}"
                });
            }
        }

        private void ValidarAntiguedadCaptura(CapturaEntradaRequest solicitud)
        {
            ArgumentNullException.ThrowIfNull(solicitud);

            if (solicitud.CapturaGuid == Guid.Empty)
                throw new ArgumentException("La captura no tiene un identificador único válido.");
            if (solicitud.CapturadaUtc == default)
                throw new ArgumentException("La captura no contiene una fecha válida.");

            DateTimeOffset ahora = DateTimeOffset.UtcNow;
            TimeSpan antiguedad = ahora - solicitud.CapturadaUtc.ToUniversalTime();

            if (antiguedad.TotalSeconds > _capturaMaxAgeSeconds)
            {
                throw new ArgumentException(
                    $"Se bloqueó una captura rezagada de {Math.Floor(antiguedad.TotalSeconds)} segundos. Confirme nuevamente el producto y el peso.");
            }

            if (antiguedad.TotalSeconds < -_capturaFutureToleranceSeconds)
                throw new ArgumentException("La hora de la terminal no coincide con la del servidor. No se guardó la captura.");
        }

        private static void ValidarSolicitudImpresion(ImpresionEntradaRequest solicitud)
        {
            ArgumentNullException.ThrowIfNull(solicitud);
            ArgumentNullException.ThrowIfNull(solicitud.Entrada);

            if (solicitud.SolicitudGuid == Guid.Empty)
                throw new ArgumentException("La impresión no tiene un identificador único válido.");
            if (solicitud.Entrada.Id <= 0)
                throw new ArgumentException("La etiqueta no contiene un Id de entrada válido.");
            if (string.IsNullOrWhiteSpace(solicitud.IpImpresora) || solicitud.IpImpresora.Length > 64)
                throw new ArgumentException("No se recibió una dirección válida de impresora.");
            if (string.IsNullOrWhiteSpace(solicitud.Producto))
                throw new ArgumentException("La etiqueta no contiene el nombre del producto.");
            if (string.IsNullOrWhiteSpace(solicitud.Lote))
                throw new ArgumentException("La etiqueta no contiene el lote.");
        }

        private static void ValidarSolicitudReimpresion(ReimpresionBitacoraRequest solicitud)
        {
            ArgumentNullException.ThrowIfNull(solicitud);

            if (solicitud.SolicitudGuid == Guid.Empty)
                throw new ArgumentException("La reimpresión no tiene un identificador único válido.");
            if (string.IsNullOrWhiteSpace(solicitud.IpImpresora) || solicitud.IpImpresora.Trim().Length > 64)
                throw new ArgumentException("Seleccione una dirección válida de impresora.");
        }

        private static bool BitacoraCoincideConEntrada(
            InyeccionImpresionLogModel bitacora,
            EntradaModel entrada,
            out string diferencia)
        {
            var comparaciones = new (string Campo, bool Coincide)[]
            {
                ("EntradaId", bitacora.EntradaId == entrada.Id),
                ("SKU", NormalizarSku(bitacora.SKU) == NormalizarSku(entrada.SKU)),
                ("Lote", bitacora.LoteId == entrada.fk_Lote),
                ("Plantilla", TextoCoincide(bitacora.Plantilla, entrada.Plantilla)),
                ("Folio", TextoCoincide(bitacora.Folio, entrada.Folio)),
                ("Peso", DecimalCoincide(bitacora.Peso, entrada.Peso, 3)),
                ("Tara", DecimalCoincide(bitacora.Tara, entrada.Tara, 3)),
                ("TipoPeso", TextoCoincide(bitacora.TipoPeso, entrada.TipoPeso))
            };

            var distinta = comparaciones.FirstOrDefault(x => !x.Coincide);
            diferencia = distinta.Campo ?? string.Empty;
            return string.IsNullOrEmpty(diferencia);
        }

        private async Task<(string Producto, string Lote)> ValidarContextoNegocio(
            EntradaModel entrada,
            string productoSku,
            string producto,
            string lote,
            bool validarReceta)
        {
            ArgumentNullException.ThrowIfNull(entrada);

            string sku = NormalizarSku(entrada.SKU);
            string skuProducto = NormalizarSku(productoSku);
            string nombreProducto = (producto ?? string.Empty).Trim();
            string nombreLote = (lote ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(sku) || sku != skuProducto)
                throw new ArgumentException("El SKU del producto no coincide con el SKU de la captura.");
            if (string.IsNullOrWhiteSpace(entrada.Plantilla))
                throw new ArgumentException("La captura no contiene una plantilla válida.");
            if (entrada.fk_Lote <= 0)
                throw new ArgumentException("La captura no contiene un lote válido.");

            var productos = await r.ListarProductos(entrada.Plantilla);
            ProductoModel? productoCatalogo = productos.FirstOrDefault(x => NormalizarSku(x.SKU) == sku);

            if (productoCatalogo == null)
                throw new ArgumentException($"El SKU {entrada.SKU} ya no pertenece a la plantilla {entrada.Plantilla}.");
            if (!TextoCoincide(productoCatalogo.Nombre, nombreProducto))
                throw new ArgumentException("El nombre del producto no corresponde al SKU seleccionado.");

            var lotes = await l.ConsultarLotes();
            LotePlantillaModel? loteCatalogo = lotes.FirstOrDefault(x => x.LoteId == entrada.fk_Lote);

            if (loteCatalogo == null)
                throw new ArgumentException("El lote seleccionado ya no está activo.");
            if (!TextoCoincide(loteCatalogo.Plantilla, entrada.Plantilla))
                throw new ArgumentException("La plantilla de la captura no corresponde al lote seleccionado.");
            if (!TextoCoincide(loteCatalogo.Lote, nombreLote))
                throw new ArgumentException("El nombre de lote de la etiqueta no corresponde al lote seleccionado.");

            if (validarReceta)
            {
                RecetaModel receta = await r.ObtenerReceta(entrada.SKU);

                if (NormalizarSku(receta.SKU) != sku ||
                    receta.Porcentaje != entrada.Porcentaje ||
                    receta.ModoInyeccion != entrada.ModoInyeccion ||
                    Math.Abs(Convert.ToDecimal(receta.Presion) - entrada.Presion) > 0.0001m ||
                    receta.Velocidad != entrada.Velocidad ||
                    receta.Altura != entrada.Altura ||
                    !TextoCoincide(receta.Avance, entrada.Avance))
                {
                    throw new ArgumentException("La receta confirmada ya no coincide con la receta vigente del SKU. Seleccione nuevamente el producto.");
                }
            }

            return ((productoCatalogo.Nombre ?? string.Empty).Trim(), (loteCatalogo.Lote ?? string.Empty).Trim());
        }

        private static bool EntradasCoinciden(EntradaModel esperada, EntradaModel persistida, out string diferencia)
        {
            var comparaciones = new (string Campo, bool Coincide)[]
            {
                ("Id", esperada.Id == 0 || esperada.Id == persistida.Id),
                ("SKU", NormalizarSku(esperada.SKU) == NormalizarSku(persistida.SKU)),
                ("Lote", esperada.fk_Lote == persistida.fk_Lote),
                ("Plantilla", TextoCoincide(esperada.Plantilla, persistida.Plantilla)),
                ("Porcentaje", esperada.Porcentaje == persistida.Porcentaje),
                ("ModoInyeccion", esperada.ModoInyeccion == persistida.ModoInyeccion),
                ("Presion", DecimalCoincide(esperada.Presion, persistida.Presion, 4)),
                ("Velocidad", esperada.Velocidad == persistida.Velocidad),
                ("Altura", esperada.Altura == persistida.Altura),
                ("Avance", TextoCoincide(esperada.Avance, persistida.Avance)),
                ("Bascula", TextoCoincide(esperada.Bascula, persistida.Bascula)),
                ("TipoPeso", TextoCoincide(esperada.TipoPeso, persistida.TipoPeso)),
                ("Autoriza", esperada.Autoriza == persistida.Autoriza),
                ("Peso", DecimalCoincide(esperada.Peso, persistida.Peso, 2)),
                ("Tara", DecimalCoincide(esperada.Tara, persistida.Tara, 2)),
                ("Usuario", TextoCoincide(esperada.UsSIGO, persistida.UsSIGO)),
                ("Folio", string.IsNullOrWhiteSpace(esperada.Folio) || TextoCoincide(esperada.Folio, persistida.Folio))
            };

            var distinta = comparaciones.FirstOrDefault(x => !x.Coincide);
            diferencia = distinta.Campo ?? string.Empty;
            return string.IsNullOrEmpty(diferencia);
        }

        private static string NormalizarSku(string? valor) =>
            (valor ?? string.Empty).Trim().ToUpperInvariant();

        private static bool TextoCoincide(string? izquierda, string? derecha) =>
            string.Equals(
                (izquierda ?? string.Empty).Trim(),
                (derecha ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);

        private static bool DecimalCoincide(decimal izquierda, decimal derecha, int decimales) =>
            decimal.Round(izquierda, decimales, MidpointRounding.AwayFromZero) ==
            decimal.Round(derecha, decimales, MidpointRounding.AwayFromZero);
        [HttpGet("ValidarModoManual")]
        public async Task<IActionResult> ValidarModoManual(int usrid, string nip)
        {
            try
            {
                var resultado = await permisos.Manual(usrid, nip);

                // Si fk_Permiso o usuarioId es 0, no tiene permisos
                if (resultado.fk_Permiso == 0 || resultado.usuarioId == 0)
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Usuario o NIP incorrectos"
                    });
                }

                return Ok(new
                {
                    success = true,
                    usuario = resultado.nombre,
                    permiso = resultado.descripcion
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    message = "Error al validar permisos: " + ex.Message
                });
            }
        }
        [HttpGet("ConsultarEntrada")]
        public async Task<IActionResult> ConsultarEntrada(int id)
        {
            var dato = await r.ConsultarEntrada(id);
            if (dato == null)
                return NotFound(new { success = false, message = "No se encontró la entrada solicitada." });

            return Ok(dato);
        }
    }
}
