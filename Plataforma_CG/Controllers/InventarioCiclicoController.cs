using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Plataforma_CG.Controllers
{
    /// <summary>
    /// Inventarios cíclicos independientes del inventario general.
    ///
    /// FUENTES:
    /// - DefaultConnection: planeación, snapshot e histórico del cíclico.
    /// - CadenaMeatP1 / CadenaMeatTIF: existencia real de Produccion.
    /// - MEAT ProduccionReferencia: ubicación real de cada ProduccionId.
    ///
    /// No requiere DbSet ni migración de EF; trabaja con Dapper/SQL directo.
    /// </summary>
    [Authorize]
    [Route("InventarioCiclico")]
    public sealed class InventarioCiclicoController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<InventarioCiclicoController> _logger;

        private static readonly Regex RxUbicacion = new(
            @"(?:^|[^A-Z0-9])([RW])\s*(\d{1,2})\s*-\s*(\d{1,2})\s*([A-Z])(?:$|[^A-Z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const int MaxClavesPorCiclico = 1500;
        private const int MaxLecturasPorLote = 75;

        public InventarioCiclicoController(
            IConfiguration configuration,
            ILogger<InventarioCiclicoController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        // ============================================================
        // DTOs públicos
        // ============================================================

        public sealed class IniciarCiclicoRequest
        {
            public int Anio { get; set; }
            public string Planta { get; set; } = "";
            public string AlmacenId { get; set; } = "";
            public string Tipo { get; set; } = "UBICACION";
            public List<string> Claves { get; set; } = new();
            public string? Observacion { get; set; }
        }

        public sealed class RegistrarLecturasRequest
        {
            public long CiclicoId { get; set; }
            public List<LecturaCapturaRequest> Lecturas { get; set; } = new();
        }

        public sealed class LecturaCapturaRequest
        {
            public string CodigoEtiqueta { get; set; } = "";
            public string? UbicacionCaptura { get; set; }
        }

        public sealed class CerrarCiclicoRequest
        {
            public long CiclicoId { get; set; }
        }

        // ============================================================
        // DTOs internos
        // ============================================================

        private sealed class WarehouseCfg
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Planta { get; set; } = "P1";
            public string Sucursal { get; set; } = "";
            public string NombreMostrar => string.IsNullOrWhiteSpace(Sucursal)
                ? Name
                : $"{Sucursal} · {Name}";
        }

        private sealed class CatalogoRow
        {
            public int Id { get; set; }
            public string Planta { get; set; } = "";
            public string AlmacenCatalogo { get; set; } = "";
            public string Camara { get; set; } = "";
            public string Rack { get; set; } = "";
            public string Ubicacion { get; set; } = "";
            public int Orden { get; set; }
            public string Observacion { get; set; } = "";
            public bool Inventariada { get; set; }
            public DateTime? UltimaFecha { get; set; }
            public string UltimoFolio { get; set; } = "";
        }

        private sealed class MeatRawRow
        {
            public long ProduccionId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
            public string Articulo { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public int Estatus { get; set; }
            public string Almacen { get; set; } = "";
            public string? ReferenciaUbicacion { get; set; }
        }

        private sealed class MeatBox
        {
            public long ProduccionId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
            public string ProductoCodigo { get; set; } = "";
            public string ProductoNombre { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public int Estatus { get; set; }
            public string AlmacenId { get; set; } = "";
            public string Ubicacion { get; set; } = "";
            public int CoincidenciasCodigo { get; set; } = 1;
        }

        private sealed class SnapshotRow
        {
            public long Id { get; set; }
            public long ProduccionId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
            public string ProductoCodigo { get; set; } = "";
            public string ProductoNombre { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public string AlmacenId { get; set; } = "";
            public string Ubicacion { get; set; } = "";
        }

        private sealed class HeaderRow
        {
            public long Id { get; set; }
            public string Folio { get; set; } = "";
            public int Anio { get; set; }
            public string Planta { get; set; } = "";
            public string AlmacenId { get; set; } = "";
            public string AlmacenNombre { get; set; } = "";
            public string Tipo { get; set; } = "";
            public string Estatus { get; set; } = "";
            public string Usuario { get; set; } = "";
            public string Observacion { get; set; } = "";
            public int CajasEsperadas { get; set; }
            public decimal KgEsperados { get; set; }
            public DateTime FechaInicio { get; set; }
            public DateTime? FechaCierre { get; set; }
        }

        private sealed class AlcanceRow
        {
            public long Id { get; set; }
            public long CiclicoId { get; set; }
            public string Tipo { get; set; } = "";
            public string Clave { get; set; } = "";
            public int? CatalogoUbicacionId { get; set; }
            public string Camara { get; set; } = "";
            public string Rack { get; set; } = "";
            public int Orden { get; set; }
            public int CajasEsperadas { get; set; }
            public decimal KgEsperados { get; set; }
        }

        private sealed class LecturaRow
        {
            public long Id { get; set; }
            public long CiclicoId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
            public long? ProduccionId { get; set; }
            public string ProductoCodigo { get; set; } = "";
            public string ProductoNombre { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public string UbicacionEsperada { get; set; } = "";
            public string UbicacionMeat { get; set; } = "";
            public string UbicacionCaptura { get; set; } = "";
            public string Resultado { get; set; } = "";
            public bool EsEsperada { get; set; }
            public string Usuario { get; set; } = "";
            public DateTime FechaLectura { get; set; }
        }

        private sealed class RefSchema
        {
            public string TableSql { get; init; } = "";
            public string ProduccionIdSql { get; init; } = "";
            public string ReferenceExpressionSql { get; init; } = "";
            public string ExtraJoinSql { get; init; } = "";
            public string OrderSql { get; init; } = "";
            public string SourceDescription { get; init; } = "";
            public List<string> Columns { get; init; } = new();
        }

        private sealed class ReporteData
        {
            public int Anio { get; set; }
            public string Planta { get; set; } = "";
            public string AlmacenId { get; set; } = "";
            public string AlmacenNombre { get; set; } = "";
            public int CatalogoTotal { get; set; }
            public int UbicacionesInventariadas { get; set; }
            public int UbicacionesPendientes { get; set; }
            public decimal CoberturaPct { get; set; }
            public int CiclicosCompletados { get; set; }
            public int CajasEsperadas { get; set; }
            public int CajasEncontradasEsperadas { get; set; }
            public int CajasFaltantes { get; set; }
            public int CajasSobrantes { get; set; }
            public int CajasMalUbicadas { get; set; }
            public decimal KgEsperados { get; set; }
            public decimal KgLeidos { get; set; }
            public List<dynamic> Cobertura { get; set; } = new();
            public List<dynamic> Ciclicos { get; set; } = new();
            public List<dynamic> Lecturas { get; set; } = new();
            public List<dynamic> Skus { get; set; } = new();
        }

        // ============================================================
        // Vista principal de inventarios cíclicos
        // GET: /InventarioCiclico
        // ============================================================
        [HttpGet("Comercial/Inventario_Ciclicos")]
        public IActionResult Index()
        {
            return View("~/Views/Comercial/Inventario_Ciclicos.cshtml");
        }



        // ============================================================
        // Endpoints: catálogo / configuración
        // ============================================================

        [HttpGet("Almacenes")]
        public async Task<IActionResult> Almacenes(CancellationToken ct)
        {
            var allowed = await ObtenerAlmacenesPermitidosAsync(ct);
            var cfg = ObtenerWarehousesConfigurados();

            var rows = cfg
                .Where(x => allowed.Contains(x.Id, StringComparer.OrdinalIgnoreCase))
                .OrderBy(x => x.Planta)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    nombre = x.Name,
                    nombreMostrar = x.NombreMostrar,
                    planta = x.Planta,
                    sucursal = x.Sucursal
                })
                .ToList();

            return Ok(new { almacenes = rows });
        }

        [HttpGet("Catalogo")]
        public async Task<IActionResult> Catalogo(
            int anio,
            string planta,
            string almacenId,
            bool soloPendientes = true,
            string? camara = null,
            string? rack = null,
            CancellationToken ct = default)
        {
            var validation = await ValidarContextoAsync(anio, planta, almacenId, ct);
            if (!validation.Ok)
                return validation.Result!;

            var plant = NormalizarPlanta(planta);
            var wh = NormalizarTexto(almacenId);

            await using var cn = CrearConexionLocal();
            await cn.OpenAsync(ct);

            const string sqlCatalogo = @"
;WITH Ultimo AS
(
    SELECT
        a.Clave,
        UltimaFecha = MAX(c.FechaCierre),
        UltimoId = MAX(c.Id)
    FROM dbo.InventarioCiclico c
    INNER JOIN dbo.InventarioCiclicoAlcance a
        ON a.CiclicoId = c.Id
    WHERE c.Anio = @Anio
      AND c.Planta = @Planta
      AND c.AlmacenId = @AlmacenId
      AND c.Tipo = 'UBICACION'
      AND a.Tipo = 'UBICACION'
      AND c.Estatus LIKE 'COMPLETADO%'
    GROUP BY a.Clave
),
UltimoFolio AS
(
    SELECT
        u.Clave,
        u.UltimaFecha,
        c.Folio
    FROM Ultimo u
    LEFT JOIN dbo.InventarioCiclico c
        ON c.Id = u.UltimoId
)
SELECT
    cat.Id,
    cat.Planta,
    cat.AlmacenCatalogo,
    cat.Camara,
    cat.Rack,
    cat.Ubicacion,
    cat.Orden,
    Observacion = ISNULL(cat.Observacion,''),
    Inventariada = CONVERT(bit, CASE WHEN u.Clave IS NULL THEN 0 ELSE 1 END),
    u.UltimaFecha,
    UltimoFolio = ISNULL(u.Folio,'')
FROM dbo.InventarioCiclicoCatalogoUbicacion cat
LEFT JOIN UltimoFolio u
    ON u.Clave = cat.Ubicacion
WHERE cat.Activo = 1
  AND cat.Planta = @Planta
  AND (@Camara = '' OR cat.Camara = @Camara)
  AND (@Rack = '' OR cat.Rack = @Rack)
ORDER BY cat.Orden, cat.Ubicacion;";

            var catalogo = (await cn.QueryAsync<CatalogoRow>(new CommandDefinition(
                sqlCatalogo,
                new
                {
                    Anio = anio,
                    Planta = plant,
                    AlmacenId = wh,
                    Camara = NormalizarTexto(camara),
                    Rack = NormalizarRack(rack)
                },
                commandTimeout: 60,
                cancellationToken: ct))).ToList();

            var boxes = await ConsultarProduccionMeatAsync(
                plant,
                wh,
                skus: null,
                etiquetas: null,
                soloActivas: true,
                ct);

            var porUbicacion = boxes
                .Where(x => !string.IsNullOrWhiteSpace(x.Ubicacion))
                .GroupBy(x => x.Ubicacion, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        cajas = g.Select(x => x.ProduccionId).Distinct().Count(),
                        kg = g.GroupBy(x => x.ProduccionId).Sum(x => x.First().PesoNeto),
                        skus = g.Select(x => x.ProductoCodigo)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Count()
                    },
                    StringComparer.OrdinalIgnoreCase);

            var result = catalogo
                .Where(x => !soloPendientes || !x.Inventariada)
                .Select(x =>
                {
                    porUbicacion.TryGetValue(x.Ubicacion, out var inv);
                    return new
                    {
                        id = x.Id,
                        planta = x.Planta,
                        almacenCatalogo = x.AlmacenCatalogo,
                        camara = x.Camara,
                        rack = x.Rack,
                        ubicacion = x.Ubicacion,
                        orden = x.Orden,
                        observacion = x.Observacion,
                        inventariada = x.Inventariada,
                        ultimaFecha = x.UltimaFecha,
                        ultimoFolio = x.UltimoFolio,
                        cajas = inv?.cajas ?? 0,
                        kg = Math.Round(inv?.kg ?? 0m, 3),
                        skus = inv?.skus ?? 0
                    };
                })
                .ToList();

            var catalogSet = catalogo
                .Select(x => x.Ubicacion)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fueraCatalogo = porUbicacion
                .Where(x => !catalogSet.Contains(x.Key))
                .OrderBy(x => x.Key)
                .Select(x => new
                {
                    ubicacion = x.Key,
                    x.Value.cajas,
                    kg = Math.Round(x.Value.kg, 3),
                    x.Value.skus
                })
                .ToList();

            return Ok(new
            {
                anio,
                planta = plant,
                almacenId = wh,
                soloPendientes,
                totalCatalogo = catalogo.Count,
                inventariadas = catalogo.Count(x => x.Inventariada),
                pendientes = catalogo.Count(x => !x.Inventariada),
                ubicaciones = result,
                fueraCatalogo
            });
        }

        [HttpGet("Skus")]
        public async Task<IActionResult> Skus(
            int anio,
            string planta,
            string almacenId,
            bool soloPendientes = true,
            string? search = null,
            CancellationToken ct = default)
        {
            var validation = await ValidarContextoAsync(anio, planta, almacenId, ct);
            if (!validation.Ok)
                return validation.Result!;

            var plant = NormalizarPlanta(planta);
            var wh = NormalizarTexto(almacenId);

            var boxes = await ConsultarProduccionMeatAsync(
                plant,
                wh,
                skus: null,
                etiquetas: null,
                soloActivas: true,
                ct);

            var skus = boxes
                .Select(x => x.ProductoCodigo)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var nombres = await ObtenerNombresSkuAsync(skus, ct);

            await using var cn = CrearConexionLocal();
            await cn.OpenAsync(ct);

            var completados = (await cn.QueryAsync<string>(new CommandDefinition(@"
SELECT DISTINCT a.Clave
FROM dbo.InventarioCiclico c
INNER JOIN dbo.InventarioCiclicoAlcance a ON a.CiclicoId = c.Id
WHERE c.Anio = @Anio
  AND c.Planta = @Planta
  AND c.AlmacenId = @AlmacenId
  AND c.Tipo = 'SKU'
  AND a.Tipo = 'SKU'
  AND c.Estatus LIKE 'COMPLETADO%';",
                new { Anio = anio, Planta = plant, AlmacenId = wh },
                commandTimeout: 60,
                cancellationToken: ct)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var term = NormalizarTexto(search);

            var result = boxes
                .GroupBy(x => x.ProductoCodigo, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    nombres.TryGetValue(g.Key, out var nombre);
                    var locations = g.Select(x => x.Ubicacion)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();

                    return new
                    {
                        sku = g.Key,
                        producto = nombre ?? "",
                        cajas = g.Select(x => x.ProduccionId).Distinct().Count(),
                        kg = Math.Round(g.GroupBy(x => x.ProduccionId).Sum(x => x.First().PesoNeto), 3),
                        ubicaciones = locations,
                        totalUbicaciones = locations.Count,
                        inventariado = completados.Contains(g.Key)
                    };
                })
                .Where(x => !soloPendientes || !x.inventariado)
                .Where(x => string.IsNullOrWhiteSpace(term)
                    || x.sku.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || x.producto.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.sku)
                .ToList();

            return Ok(new
            {
                anio,
                planta = plant,
                almacenId = wh,
                skus = result
            });
        }

        // ============================================================
        // Endpoints: sesión cíclica
        // ============================================================

        [HttpGet("SesionActiva")]
        public async Task<IActionResult> SesionActiva(CancellationToken ct)
        {
            var user = UsuarioActual();
            await using var cn = CrearConexionLocal();
            await cn.OpenAsync(ct);

            var id = await cn.QueryFirstOrDefaultAsync<long?>(new CommandDefinition(@"
SELECT TOP (1) Id
FROM dbo.InventarioCiclico
WHERE Usuario = @Usuario
  AND Estatus = 'ABIERTO'
ORDER BY FechaInicio DESC, Id DESC;",
                new { Usuario = user },
                commandTimeout: 30,
                cancellationToken: ct));

            if (!id.HasValue)
                return Ok(new { sesion = (object?)null });

            var dto = await ConstruirSesionDtoAsync(cn, id.Value, ct);
            return Ok(new { sesion = dto });
        }

        [HttpPost("Iniciar")]
        public async Task<IActionResult> Iniciar(
            [FromBody] IniciarCiclicoRequest req,
            CancellationToken ct)
        {
            req ??= new IniciarCiclicoRequest();

            var validation = await ValidarContextoAsync(req.Anio, req.Planta, req.AlmacenId, ct);
            if (!validation.Ok)
                return validation.Result!;

            var tipo = NormalizarTipo(req.Tipo);
            if (tipo is null)
                return BadRequest(new { mensaje = "Tipo de cíclico inválido. Usa UBICACION o SKU." });

            var claves = (req.Claves ?? new List<string>())
                .Select(x => tipo == "UBICACION" ? NormalizarUbicacion(x) : NormalizarSku(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (claves.Count == 0)
                return BadRequest(new { mensaje = "Selecciona al menos una ubicación o SKU." });

            if (claves.Count > MaxClavesPorCiclico)
                return BadRequest(new { mensaje = $"Máximo {MaxClavesPorCiclico} elementos por cíclico." });

            var plant = NormalizarPlanta(req.Planta);
            var wh = NormalizarTexto(req.AlmacenId);
            var user = UsuarioActual();
            var warehouse = ObtenerWarehousesConfigurados()
                .FirstOrDefault(x => x.Id.Equals(wh, StringComparison.OrdinalIgnoreCase));

            await using var cn = CrearConexionLocal();
            await cn.OpenAsync(ct);

            var existing = await cn.QueryFirstOrDefaultAsync<HeaderRow>(new CommandDefinition(@"
SELECT TOP (1) *
FROM dbo.InventarioCiclico
WHERE Usuario = @Usuario
  AND Estatus = 'ABIERTO'
ORDER BY FechaInicio DESC, Id DESC;",
                new { Usuario = user },
                commandTimeout: 30,
                cancellationToken: ct));

            if (existing != null)
            {
                return Conflict(new
                {
                    mensaje = $"Ya tienes el cíclico {existing.Folio} abierto. Ciérralo o cancélalo antes de iniciar otro.",
                    sesionId = existing.Id,
                    folio = existing.Folio
                });
            }

            // Validar que las ubicaciones existan en el catálogo anual.
            Dictionary<string, CatalogoRow> catalogoPorUbicacion = new(StringComparer.OrdinalIgnoreCase);
            if (tipo == "UBICACION")
            {
                var catalogRows = (await cn.QueryAsync<CatalogoRow>(new CommandDefinition(@"
SELECT Id, Planta, AlmacenCatalogo, Camara, Rack, Ubicacion, Orden,
       Observacion = ISNULL(Observacion,'')
FROM dbo.InventarioCiclicoCatalogoUbicacion
WHERE Activo = 1
  AND Planta = @Planta
  AND Ubicacion IN @Ubicaciones;",
                    new { Planta = plant, Ubicaciones = claves },
                    commandTimeout: 60,
                    cancellationToken: ct))).ToList();

                catalogoPorUbicacion = catalogRows
                    .GroupBy(x => x.Ubicacion, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var inexistentes = claves.Where(x => !catalogoPorUbicacion.ContainsKey(x)).ToList();
                if (inexistentes.Count > 0)
                {
                    return BadRequest(new
                    {
                        mensaje = "Hay ubicaciones que no existen en el catálogo anual.",
                        ubicaciones = inexistentes.Take(50).ToList()
                    });
                }
            }

            // Evita dos cíclicos abiertos sobre el mismo alcance y almacén.
            var overlap = (await cn.QueryAsync<dynamic>(new CommandDefinition(@"
SELECT DISTINCT TOP (25)
    a.Clave,
    c.Folio,
    c.Usuario
FROM dbo.InventarioCiclico c
INNER JOIN dbo.InventarioCiclicoAlcance a ON a.CiclicoId = c.Id
WHERE c.Anio = @Anio
  AND c.Planta = @Planta
  AND c.AlmacenId = @AlmacenId
  AND c.Tipo = @Tipo
  AND c.Estatus = 'ABIERTO'
  AND a.Clave IN @Claves;",
                new
                {
                    Anio = req.Anio,
                    Planta = plant,
                    AlmacenId = wh,
                    Tipo = tipo,
                    Claves = claves
                },
                commandTimeout: 60,
                cancellationToken: ct))).ToList();

            if (overlap.Count > 0)
            {
                return Conflict(new
                {
                    mensaje = "Parte del alcance ya está siendo inventariado en otro cíclico abierto.",
                    conflictos = overlap
                });
            }

            // Fotografía real tomada de MEAT al INICIAR.
            var meat = await ConsultarProduccionMeatAsync(
                plant,
                wh,
                skus: tipo == "SKU" ? claves : null,
                etiquetas: null,
                soloActivas: true,
                ct);

            var snapshot = tipo == "UBICACION"
                ? meat.Where(x => claves.Contains(x.Ubicacion, StringComparer.OrdinalIgnoreCase)).ToList()
                : meat.Where(x => claves.Contains(x.ProductoCodigo, StringComparer.OrdinalIgnoreCase)).ToList();

            var nombres = await ObtenerNombresSkuAsync(
                snapshot.Select(x => x.ProductoCodigo).Distinct(StringComparer.OrdinalIgnoreCase),
                ct);

            foreach (var box in snapshot)
            {
                if (nombres.TryGetValue(box.ProductoCodigo, out var nombre))
                    box.ProductoNombre = nombre;
            }

            await using var tx = await cn.BeginTransactionAsync(ct);
            try
            {
                var id = await cn.ExecuteScalarAsync<long>(new CommandDefinition(@"
INSERT INTO dbo.InventarioCiclico
(
    Anio, Planta, AlmacenId, AlmacenNombre, Tipo, Estatus,
    Usuario, Observacion, CajasEsperadas, KgEsperados,
    FechaInicio, FechaModificacion
)
OUTPUT INSERTED.Id
VALUES
(
    @Anio, @Planta, @AlmacenId, @AlmacenNombre, @Tipo, 'ABIERTO',
    @Usuario, @Observacion, @CajasEsperadas, @KgEsperados,
    SYSDATETIME(), SYSDATETIME()
);",
                    new
                    {
                        Anio = req.Anio,
                        Planta = plant,
                        AlmacenId = wh,
                        AlmacenNombre = warehouse?.Name ?? wh,
                        Tipo = tipo,
                        Usuario = user,
                        Observacion = (req.Observacion ?? "").Trim(),
                        CajasEsperadas = snapshot.Select(x => x.ProduccionId).Distinct().Count(),
                        KgEsperados = snapshot.GroupBy(x => x.ProduccionId).Sum(x => x.First().PesoNeto)
                    },
                    tx,
                    commandTimeout: 60,
                    cancellationToken: ct));

                var folio = $"CIC-{req.Anio}-{id:D7}";
                await cn.ExecuteAsync(new CommandDefinition(@"
UPDATE dbo.InventarioCiclico
SET Folio = @Folio
WHERE Id = @Id;",
                    new { Folio = folio, Id = id },
                    tx,
                    commandTimeout: 30,
                    cancellationToken: ct));

                var alcanceRows = claves.Select((clave, index) =>
                {
                    catalogoPorUbicacion.TryGetValue(clave, out var cat);
                    var expected = tipo == "UBICACION"
                        ? snapshot.Where(x => x.Ubicacion.Equals(clave, StringComparison.OrdinalIgnoreCase)).ToList()
                        : snapshot.Where(x => x.ProductoCodigo.Equals(clave, StringComparison.OrdinalIgnoreCase)).ToList();

                    return new
                    {
                        CiclicoId = id,
                        Tipo = tipo,
                        Clave = clave,
                        CatalogoUbicacionId = cat?.Id,
                        Camara = cat?.Camara ?? "",
                        Rack = cat?.Rack ?? "",
                        Orden = cat?.Orden ?? (index + 1),
                        CajasEsperadas = expected.Select(x => x.ProduccionId).Distinct().Count(),
                        KgEsperados = expected.GroupBy(x => x.ProduccionId).Sum(x => x.First().PesoNeto)
                    };
                }).ToList();

                await cn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO dbo.InventarioCiclicoAlcance
(
    CiclicoId, Tipo, Clave, CatalogoUbicacionId, Camara, Rack,
    Orden, CajasEsperadas, KgEsperados
)
VALUES
(
    @CiclicoId, @Tipo, @Clave, @CatalogoUbicacionId, @Camara, @Rack,
    @Orden, @CajasEsperadas, @KgEsperados
);",
                    alcanceRows,
                    tx,
                    commandTimeout: 120,
                    cancellationToken: ct));

                if (snapshot.Count > 0)
                {
                    var snapshotRows = snapshot
                        .GroupBy(x => x.ProduccionId)
                        .Select(g => g.First())
                        .Select(x => new
                        {
                            CiclicoId = id,
                            x.ProduccionId,
                            CodigoEtiqueta = NormalizarEtiqueta(x.CodigoEtiqueta),
                            ProductoCodigo = NormalizarSku(x.ProductoCodigo),
                            x.ProductoNombre,
                            x.PesoNeto,
                            AlmacenId = wh,
                            Ubicacion = NormalizarUbicacion(x.Ubicacion)
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.CodigoEtiqueta))
                        .ToList();

                    await cn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO dbo.InventarioCiclicoSnapshot
(
    CiclicoId, ProduccionId, CodigoEtiqueta, ProductoCodigo,
    ProductoNombre, PesoNeto, AlmacenId, Ubicacion, FechaSnapshot
)
VALUES
(
    @CiclicoId, @ProduccionId, @CodigoEtiqueta, @ProductoCodigo,
    @ProductoNombre, @PesoNeto, @AlmacenId, @Ubicacion, SYSDATETIME()
);",
                        snapshotRows,
                        tx,
                        commandTimeout: 180,
                        cancellationToken: ct));
                }

                await tx.CommitAsync(ct);

                var dto = await ConstruirSesionDtoAsync(cn, id, ct);
                return Ok(new
                {
                    ok = true,
                    mensaje = $"Cíclico {folio} iniciado. La fotografía MEAT quedó congelada.",
                    sesion = dto
                });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        [HttpPost("RegistrarLecturas")]
        public async Task<IActionResult> RegistrarLecturas(
            [FromBody] RegistrarLecturasRequest req,
            CancellationToken ct)
        {
            if (req == null || req.CiclicoId <= 0)
                return BadRequest(new { mensaje = "Cíclico inválido." });

            var lecturas = (req.Lecturas ?? new List<LecturaCapturaRequest>())
                .Where(x => !string.IsNullOrWhiteSpace(x.CodigoEtiqueta))
                .Take(MaxLecturasPorLote)
                .Select(x => new LecturaCapturaRequest
                {
                    CodigoEtiqueta = NormalizarEtiqueta(x.CodigoEtiqueta),
                    UbicacionCaptura = NormalizarUbicacion(x.UbicacionCaptura)
                })
                .ToList();

            if (lecturas.Count == 0)
                return BadRequest(new { mensaje = "No se recibieron etiquetas." });

            var user = UsuarioActual();

            await using var cn = CrearConexionLocal();
            await cn.OpenAsync(ct);

            var header = await cn.QueryFirstOrDefaultAsync<HeaderRow>(new CommandDefinition(@"
SELECT *
FROM dbo.InventarioCiclico
WHERE Id = @Id;",
                new { Id = req.CiclicoId },
                commandTimeout: 30,
                cancellationToken: ct));

            if (header == null)
                return NotFound(new { mensaje = "El cíclico no existe." });

            if (!header.Usuario.Equals(user, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            if (!header.Estatus.Equals("ABIERTO", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { mensaje = $"El cíclico está {header.Estatus}." });

            var allowed = await ObtenerAlmacenesPermitidosAsync(ct);
            if (!allowed.Contains(header.AlmacenId, StringComparer.OrdinalIgnoreCase))
                return Forbid();

            var scopeRows = (await cn.QueryAsync<AlcanceRow>(new CommandDefinition(@"
SELECT *
FROM dbo.InventarioCiclicoAlcance
WHERE CiclicoId = @Id
ORDER BY Orden, Clave;",
                new { Id = header.Id },
                commandTimeout: 30,
                cancellationToken: ct))).ToList();

            var scope = scopeRows.Select(x => x.Clave).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (header.Tipo == "UBICACION")
            {
                var invalidCapture = lecturas
                    .Where(x => string.IsNullOrWhiteSpace(x.UbicacionCaptura) || !scope.Contains(x.UbicacionCaptura!))
                    .Select(x => x.CodigoEtiqueta)
                    .Take(10)
                    .ToList();

                if (invalidCapture.Count > 0)
                {
                    return BadRequest(new
                    {
                        mensaje = "Selecciona una ubicación activa válida antes de escanear.",
                        etiquetas = invalidCapture
                    });
                }
            }

            var codes = lecturas.Select(x => x.CodigoEtiqueta)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existing = (await cn.QueryAsync<string>(new CommandDefinition(@"
SELECT CodigoEtiqueta
FROM dbo.InventarioCiclicoLectura
WHERE CiclicoId = @CiclicoId
  AND CodigoEtiqueta IN @Codigos;",
                new { CiclicoId = header.Id, Codigos = codes },
                commandTimeout: 60,
                cancellationToken: ct)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var snapshots = (await cn.QueryAsync<SnapshotRow>(new CommandDefinition(@"
SELECT *
FROM dbo.InventarioCiclicoSnapshot
WHERE CiclicoId = @CiclicoId
  AND CodigoEtiqueta IN @Codigos;",
                new { CiclicoId = header.Id, Codigos = codes },
                commandTimeout: 60,
                cancellationToken: ct))).ToList();

            var snapshotByCode = snapshots
                .GroupBy(x => NormalizarEtiqueta(x.CodigoEtiqueta), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Para el escaneo consultamos incluso inactivas y sin filtrar el almacén,
            // para distinguir INACTIVA / OTRO_ALMACEN / NO_ENCONTRADA.
            var meatRows = await ConsultarProduccionMeatAsync(
                header.Planta,
                almacenId: null,
                skus: null,
                etiquetas: codes,
                soloActivas: false,
                ct);

            var meatByCode = meatRows
                .GroupBy(x => NormalizarEtiqueta(x.CodigoEtiqueta), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var nombresSku = await ObtenerNombresSkuAsync(
                meatRows.Select(x => x.ProductoCodigo).Distinct(StringComparer.OrdinalIgnoreCase),
                ct);

            var output = new List<object>();
            var toInsert = new List<object>();
            var seenThisBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scan in lecturas)
            {
                var code = scan.CodigoEtiqueta;

                if (!seenThisBatch.Add(code) || existing.Contains(code))
                {
                    output.Add(new
                    {
                        codigoEtiqueta = code,
                        kind = "dup",
                        resultado = "DUPLICADA",
                        msg = "La etiqueta ya fue registrada en este cíclico."
                    });
                    continue;
                }

                snapshotByCode.TryGetValue(code, out var expected);
                meatByCode.TryGetValue(code, out var meatMatches);

                MeatBox? meat = null;
                string resultado;
                string msg;
                bool esEsperada = expected != null;

                if (meatMatches == null || meatMatches.Count == 0)
                {
                    resultado = "NO_ENCONTRADA";
                    msg = "La etiqueta no existe en MEAT para la planta seleccionada.";
                }
                else
                {
                    var distinctProduction = meatMatches
                        .GroupBy(x => x.ProduccionId)
                        .Select(g => g.First())
                        .ToList();

                    if (distinctProduction.Count > 1)
                    {
                        resultado = "AMBIGUA";
                        msg = "La etiqueta aparece ligada a más de un ProduccionId en MEAT.";
                        meat = distinctProduction.OrderByDescending(x => x.ProduccionId).First();
                    }
                    else
                    {
                        meat = distinctProduction[0];

                        if (meat.Estatus != 1)
                        {
                            resultado = "INACTIVA";
                            msg = $"La caja existe en MEAT pero no está activa. Estatus {meat.Estatus}.";
                        }
                        else if (!NormalizarTexto(meat.AlmacenId)
                            .Equals(NormalizarTexto(header.AlmacenId), StringComparison.OrdinalIgnoreCase))
                        {
                            resultado = "OTRO_ALMACEN";
                            msg = $"MEAT indica almacén {meat.AlmacenId}; el cíclico corresponde a {header.AlmacenId}.";
                        }
                        else if (header.Tipo == "UBICACION")
                        {
                            var capture = NormalizarUbicacion(scan.UbicacionCaptura);

                            if (expected != null)
                            {
                                var expectedLocation = NormalizarUbicacion(expected.Ubicacion);
                                if (expectedLocation.Equals(capture, StringComparison.OrdinalIgnoreCase))
                                {
                                    resultado = "CORRECTA";
                                    msg = "Caja esperada encontrada en la ubicación correcta.";
                                }
                                else
                                {
                                    resultado = "UBICACION_DIFERENTE";
                                    msg = $"La fotografía esperaba la caja en {expectedLocation}; fue encontrada en {capture}.";
                                }
                            }
                            else
                            {
                                resultado = "SOBRANTE";
                                msg = "La caja está activa en el almacén, pero no pertenecía a la fotografía inicial del alcance.";
                            }
                        }
                        else
                        {
                            var sku = NormalizarSku(meat.ProductoCodigo);
                            if (expected != null)
                            {
                                resultado = "CORRECTA";
                                msg = "Caja esperada del SKU encontrada.";
                            }
                            else if (scope.Contains(sku))
                            {
                                resultado = "SOBRANTE";
                                msg = "Caja del SKU seleccionado que no estaba en la fotografía inicial.";
                            }
                            else
                            {
                                resultado = "FUERA_ALCANCE";
                                msg = $"El SKU {sku} no forma parte de este cíclico.";
                            }
                        }
                    }
                }

                var skuFinal = NormalizarSku(meat?.ProductoCodigo ?? expected?.ProductoCodigo);
                var productName = meat?.ProductoNombre ?? expected?.ProductoNombre ?? "";
                if (string.IsNullOrWhiteSpace(productName)
                    && nombresSku.TryGetValue(skuFinal, out var nombreLocal))
                {
                    productName = nombreLocal;
                }

                var insert = new
                {
                    CiclicoId = header.Id,
                    CodigoEtiqueta = code,
                    ProduccionId = meat?.ProduccionId ?? expected?.ProduccionId,
                    ProductoCodigo = skuFinal,
                    ProductoNombre = productName,
                    PesoNeto = meat?.PesoNeto ?? expected?.PesoNeto ?? 0m,
                    UbicacionEsperada = NormalizarUbicacion(expected?.Ubicacion),
                    UbicacionMeat = NormalizarUbicacion(meat?.Ubicacion),
                    UbicacionCaptura = header.Tipo == "UBICACION"
                        ? NormalizarUbicacion(scan.UbicacionCaptura)
                        : NormalizarUbicacion(meat?.Ubicacion),
                    Resultado = resultado,
                    EsEsperada = esEsperada,
                    Usuario = user
                };

                toInsert.Add(insert);

                var kind = resultado switch
                {
                    "CORRECTA" => "ok",
                    "DUPLICADA" => "dup",
                    "UBICACION_DIFERENTE" => "warn",
                    "SOBRANTE" => "warn",
                    _ => "err"
                };

                output.Add(new
                {
                    codigoEtiqueta = code,
                    kind,
                    resultado,
                    msg,
                    sku = skuFinal,
                    producto = productName,
                    pesoNeto = insert.PesoNeto,
                    ubicacionEsperada = insert.UbicacionEsperada,
                    ubicacionMeat = insert.UbicacionMeat,
                    ubicacionCaptura = insert.UbicacionCaptura
                });
            }

            if (toInsert.Count > 0)
            {
                try
                {
                    await cn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO dbo.InventarioCiclicoLectura
(
    CiclicoId, CodigoEtiqueta, ProduccionId, ProductoCodigo, ProductoNombre,
    PesoNeto, UbicacionEsperada, UbicacionMeat, UbicacionCaptura,
    Resultado, EsEsperada, Usuario, FechaLectura
)
VALUES
(
    @CiclicoId, @CodigoEtiqueta, @ProduccionId, @ProductoCodigo, @ProductoNombre,
    @PesoNeto, @UbicacionEsperada, @UbicacionMeat, @UbicacionCaptura,
    @Resultado, @EsEsperada, @Usuario, SYSDATETIME()
);",
                        toInsert,
                        commandTimeout: 120,
                        cancellationToken: ct));
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    // Otro request pudo guardar una de las mismas etiquetas.
                    _logger.LogWarning(ex,
                        "Lectura cíclica duplicada por concurrencia en {CiclicoId}.",
                        header.Id);
                }
            }

            await cn.ExecuteAsync(new CommandDefinition(@"
UPDATE dbo.InventarioCiclico
SET FechaModificacion = SYSDATETIME()
WHERE Id = @Id;",
                new { Id = header.Id },
                commandTimeout: 30,
                cancellationToken: ct));

            var sesion = await ConstruirSesionDtoAsync(cn, header.Id, ct);

            return Ok(new
            {
                ok = true,
                results = output,
                sesion
            });
        }

        [HttpPost("Cerrar")]
        public async Task<IActionResult> Cerrar(
            [FromBody] CerrarCiclicoRequest req,
            CancellationToken ct)
        {
            if (req == null || req.CiclicoId <= 0)
                return BadRequest(new { mensaje = "Cíclico inválido." });

            var user = UsuarioActual();
            await using var cn = CrearConexionLocal();
            await cn.OpenAsync(ct);

            var header = await cn.QueryFirstOrDefaultAsync<HeaderRow>(new CommandDefinition(@"
SELECT * FROM dbo.InventarioCiclico WHERE Id = @Id;",
                new { Id = req.CiclicoId },
                commandTimeout: 30,
                cancellationToken: ct));

            if (header == null)
                return NotFound(new { mensaje = "El cíclico no existe." });

            if (!header.Usuario.Equals(user, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            if (!header.Estatus.Equals("ABIERTO", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { mensaje = $"El cíclico ya está {header.Estatus}." });

            var stats = await ObtenerStatsAsync(cn, header.Id, ct);
            var hasDifference = stats.Faltantes > 0
                || stats.Sobrantes > 0
                || stats.MalUbicadas > 0
                || stats.Invalidas > 0;

            var status = hasDifference
                ? "COMPLETADO_CON_DIFERENCIAS"
                : "COMPLETADO";

            await cn.ExecuteAsync(new CommandDefinition(@"
UPDATE dbo.InventarioCiclico
SET Estatus = @Estatus,
    FechaCierre = SYSDATETIME(),
    FechaModificacion = SYSDATETIME()
WHERE Id = @Id
  AND Estatus = 'ABIERTO';",
                new { Estatus = status, Id = header.Id },
                commandTimeout: 30,
                cancellationToken: ct));

            var sesion = await ConstruirSesionDtoAsync(cn, header.Id, ct);
            return Ok(new
            {
                ok = true,
                mensaje = status == "COMPLETADO"
                    ? "Cíclico cerrado sin diferencias."
                    : "Cíclico cerrado con diferencias para revisión.",
                sesion
            });
        }

        [HttpPost("Cancelar")]
        public async Task<IActionResult> Cancelar(
            [FromBody] CerrarCiclicoRequest req,
            CancellationToken ct)
        {
            if (req == null || req.CiclicoId <= 0)
                return BadRequest(new { mensaje = "Cíclico inválido." });

            var user = UsuarioActual();
            await using var cn = CrearConexionLocal();
            await cn.OpenAsync(ct);

            var affected = await cn.ExecuteAsync(new CommandDefinition(@"
UPDATE dbo.InventarioCiclico
SET Estatus = 'CANCELADO',
    FechaCierre = SYSDATETIME(),
    FechaModificacion = SYSDATETIME()
WHERE Id = @Id
  AND Usuario = @Usuario
  AND Estatus = 'ABIERTO';",
                new { Id = req.CiclicoId, Usuario = user },
                commandTimeout: 30,
                cancellationToken: ct));

            if (affected == 0)
                return Conflict(new { mensaje = "No se pudo cancelar; verifica que siga abierto y te pertenezca." });

            return Ok(new { ok = true, mensaje = "Cíclico cancelado. No genera cobertura anual." });
        }

        // ============================================================
        // Endpoints: reporte anual
        // ============================================================

        [HttpGet("Reporte")]
        public async Task<IActionResult> Reporte(
            int anio,
            string planta,
            string almacenId,
            CancellationToken ct = default)
        {
            var validation = await ValidarContextoAsync(anio, planta, almacenId, ct);
            if (!validation.Ok)
                return validation.Result!;

            var data = await ObtenerReporteDataAsync(
                anio,
                NormalizarPlanta(planta),
                NormalizarTexto(almacenId),
                includeLecturas: false,
                ct);

            return Ok(data);
        }

        [HttpGet("ReporteExcel")]
        public async Task<IActionResult> ReporteExcel(
            int anio,
            string planta,
            string almacenId,
            CancellationToken ct = default)
        {
            var validation = await ValidarContextoAsync(anio, planta, almacenId, ct);
            if (!validation.Ok)
                return validation.Result!;

            var plant = NormalizarPlanta(planta);
            var wh = NormalizarTexto(almacenId);
            var data = await ObtenerReporteDataAsync(anio, plant, wh, includeLecturas: true, ct);

            using var wb = new XLWorkbook();

            var ws = wb.Worksheets.Add("RESUMEN");
            ws.Cell("A1").Value = "INVENTARIOS CÍCLICOS - CARNES G";
            ws.Range("A1:F1").Merge();
            ws.Range("A1:F1").Style.Font.Bold = true;
            ws.Range("A1:F1").Style.Font.FontSize = 16;

            ws.Cell("A3").Value = "Año";
            ws.Cell("B3").Value = data.Anio;
            ws.Cell("A4").Value = "Planta";
            ws.Cell("B4").Value = data.Planta;
            ws.Cell("A5").Value = "Almacén";
            ws.Cell("B5").Value = data.AlmacenNombre;
            ws.Cell("A6").Value = "ID almacén";
            ws.Cell("B6").Value = data.AlmacenId;
            ws.Cell("A7").Value = "Generado";
            ws.Cell("B7").Value = DateTime.Now;
            ws.Cell("B7").Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

            var kpis = new (string Label, object Value)[]
            {
                ("Ubicaciones catálogo", data.CatalogoTotal),
                ("Ubicaciones inventariadas", data.UbicacionesInventariadas),
                ("Ubicaciones pendientes", data.UbicacionesPendientes),
                ("Cobertura anual", data.CoberturaPct / 100m),
                ("Cíclicos completados", data.CiclicosCompletados),
                ("Cajas esperadas", data.CajasEsperadas),
                ("Cajas esperadas encontradas", data.CajasEncontradasEsperadas),
                ("Cajas faltantes", data.CajasFaltantes),
                ("Cajas sobrantes", data.CajasSobrantes),
                ("Cajas mal ubicadas", data.CajasMalUbicadas),
                ("Kg esperados", data.KgEsperados),
                ("Kg leídos", data.KgLeidos)
            };

            var row = 10;
            foreach (var (label, value) in kpis)
            {
                ws.Cell(row, 1).Value = label;
                if (value is int i) ws.Cell(row, 2).Value = i;
                else if (value is decimal d) ws.Cell(row, 2).Value = d;
                else ws.Cell(row, 2).Value = value?.ToString() ?? "";
                row++;
            }
            ws.Cell(13, 2).Style.NumberFormat.Format = "0.00%";
            ws.Columns().AdjustToContents();

            var wc = wb.Worksheets.Add("COBERTURA UBICACIONES");
            var coberturaRows = data.Cobertura.Select(x => (IDictionary<string, object>)x).ToList();
            EscribirDynamicTable(wc, coberturaRows);

            var wcy = wb.Worksheets.Add("CICLICOS");
            var ciclosRows = data.Ciclicos.Select(x => (IDictionary<string, object>)x).ToList();
            EscribirDynamicTable(wcy, ciclosRows);

            var wsk = wb.Worksheets.Add("COBERTURA SKU");
            var skuRows = data.Skus.Select(x => (IDictionary<string, object>)x).ToList();
            EscribirDynamicTable(wsk, skuRows);

            var wl = wb.Worksheets.Add("LECTURAS");
            var lecturaRows = data.Lecturas.Select(x => (IDictionary<string, object>)x).ToList();
            EscribirDynamicTable(wl, lecturaRows);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var bytes = ms.ToArray();
            var fileName = $"Inventarios_Ciclicos_{plant}_{wh}_{anio}.xlsx"
                .Replace("/", "-")
                .Replace("\\", "-")
                .Replace(" ", "_");

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // ============================================================
        // Diagnóstico MEAT - lectura únicamente
        // ============================================================

        [HttpGet("DiagnosticoMeat")]
        public async Task<IActionResult> DiagnosticoMeat(
            string planta,
            CancellationToken ct = default)
        {
            var plant = NormalizarPlanta(planta);
            if (plant != "P1" && plant != "TIF")
                return BadRequest(new { mensaje = "Planta inválida." });

            var cs = ObtenerCadenaMeat(plant);
            if (string.IsNullOrWhiteSpace(cs))
                return StatusCode(500, new { mensaje = $"No existe la cadena MEAT para {plant}." });

            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync(ct);

            try
            {
                var schema = await ResolverProduccionReferenciaAsync(cn, ct);
                var sample = (await cn.QueryAsync<dynamic>(new CommandDefinition($@"
SELECT TOP (25)
    p.ProduccionId,
    p.CodigoEtiqueta,
    p.Articulo,
    p.Estatus,
    p.Almacen,
    ReferenciaUbicacion = {schema.ReferenceExpressionSql}
FROM dbo.Produccion p
LEFT JOIN {schema.TableSql} pr
    ON pr.{schema.ProduccionIdSql} = p.ProduccionId
{schema.ExtraJoinSql}
WHERE p.Estatus = 1
ORDER BY p.ProduccionId DESC {schema.OrderSql};",
                    commandTimeout: 45,
                    cancellationToken: ct))).ToList();

                return Ok(new
                {
                    ok = true,
                    planta = plant,
                    tabla = schema.TableSql,
                    fuenteUbicacion = schema.SourceDescription,
                    columnas = schema.Columns,
                    muestra = sample
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Diagnóstico ProduccionReferencia falló en {Planta}.", plant);
                return StatusCode(500, new
                {
                    ok = false,
                    planta = plant,
                    mensaje = ex.Message
                });
            }
        }

        // ============================================================
        // Helpers: reporte
        // ============================================================

        private async Task<ReporteData> ObtenerReporteDataAsync(
            int anio,
            string planta,
            string almacenId,
            bool includeLecturas,
            CancellationToken ct)
        {
            var cfg = ObtenerWarehousesConfigurados()
                .FirstOrDefault(x => x.Id.Equals(almacenId, StringComparison.OrdinalIgnoreCase));

            await using var cn = CrearConexionLocal();
            await cn.OpenAsync(ct);

            var catalogTotal = await cn.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT COUNT(*)
FROM dbo.InventarioCiclicoCatalogoUbicacion
WHERE Activo = 1
  AND Planta = @Planta;",
                new { Planta = planta },
                commandTimeout: 30,
                cancellationToken: ct));

            var completedLocations = await cn.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT COUNT(DISTINCT a.Clave)
FROM dbo.InventarioCiclico c
INNER JOIN dbo.InventarioCiclicoAlcance a ON a.CiclicoId = c.Id
WHERE c.Anio = @Anio
  AND c.Planta = @Planta
  AND c.AlmacenId = @AlmacenId
  AND c.Tipo = 'UBICACION'
  AND a.Tipo = 'UBICACION'
  AND c.Estatus LIKE 'COMPLETADO%';",
                new { Anio = anio, Planta = planta, AlmacenId = almacenId },
                commandTimeout: 30,
                cancellationToken: ct));

            var stats = await cn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(@"
;WITH Ciclos AS
(
    SELECT Id
    FROM dbo.InventarioCiclico
    WHERE Anio = @Anio
      AND Planta = @Planta
      AND AlmacenId = @AlmacenId
      AND Estatus LIKE 'COMPLETADO%'
),
S AS
(
    SELECT
        CiclicosCompletados = COUNT(*),
        CajasEsperadas = ISNULL(SUM(CajasEsperadas),0),
        KgEsperados = ISNULL(SUM(KgEsperados),0)
    FROM dbo.InventarioCiclico
    WHERE Id IN (SELECT Id FROM Ciclos)
),
L AS
(
    SELECT
        CajasEncontradasEsperadas = COUNT(DISTINCT CASE WHEN l.EsEsperada = 1 THEN l.CodigoEtiqueta END),
        CajasSobrantes = SUM(CASE WHEN l.Resultado = 'SOBRANTE' THEN 1 ELSE 0 END),
        CajasMalUbicadas = SUM(CASE WHEN l.Resultado = 'UBICACION_DIFERENTE' THEN 1 ELSE 0 END),
        KgLeidos = ISNULL(SUM(l.PesoNeto),0)
    FROM dbo.InventarioCiclicoLectura l
    WHERE l.CiclicoId IN (SELECT Id FROM Ciclos)
)
SELECT
    S.CiclicosCompletados,
    S.CajasEsperadas,
    S.KgEsperados,
    CajasEncontradasEsperadas = ISNULL(L.CajasEncontradasEsperadas,0),
    CajasSobrantes = ISNULL(L.CajasSobrantes,0),
    CajasMalUbicadas = ISNULL(L.CajasMalUbicadas,0),
    KgLeidos = ISNULL(L.KgLeidos,0)
FROM S CROSS JOIN L;",
                new { Anio = anio, Planta = planta, AlmacenId = almacenId },
                commandTimeout: 60,
                cancellationToken: ct));

            int ciclos = Convert.ToInt32(stats?.CiclicosCompletados ?? 0);
            int expected = Convert.ToInt32(stats?.CajasEsperadas ?? 0);
            int foundExpected = Convert.ToInt32(stats?.CajasEncontradasEsperadas ?? 0);
            int extras = Convert.ToInt32(stats?.CajasSobrantes ?? 0);
            int misplaced = Convert.ToInt32(stats?.CajasMalUbicadas ?? 0);
            decimal kgExpected = Convert.ToDecimal(stats?.KgEsperados ?? 0m, CultureInfo.InvariantCulture);
            decimal kgRead = Convert.ToDecimal(stats?.KgLeidos ?? 0m, CultureInfo.InvariantCulture);

            var cobertura = (await cn.QueryAsync<dynamic>(new CommandDefinition(@"
;WITH Hist AS
(
    SELECT
        a.Clave,
        c.Id AS CiclicoId,
        c.Folio,
        c.FechaCierre,
        a.CajasEsperadas,
        a.KgEsperados,
        rn = ROW_NUMBER() OVER
        (
            PARTITION BY a.Clave
            ORDER BY c.FechaCierre DESC, c.Id DESC
        ),
        VecesInventariada = COUNT(*) OVER (PARTITION BY a.Clave)
    FROM dbo.InventarioCiclico c
    INNER JOIN dbo.InventarioCiclicoAlcance a ON a.CiclicoId = c.Id
    WHERE c.Anio = @Anio
      AND c.Planta = @Planta
      AND c.AlmacenId = @AlmacenId
      AND c.Tipo = 'UBICACION'
      AND a.Tipo = 'UBICACION'
      AND c.Estatus LIKE 'COMPLETADO%'
),
Leidas AS
(
    SELECT
        s.CiclicoId,
        s.Ubicacion,
        EncontradasEsperadas = COUNT(DISTINCT CASE WHEN l.EsEsperada = 1 THEN l.CodigoEtiqueta END),
        Correctas = SUM(CASE WHEN l.Resultado = 'CORRECTA' THEN 1 ELSE 0 END),
        MalUbicadas = SUM(CASE WHEN l.Resultado = 'UBICACION_DIFERENTE' THEN 1 ELSE 0 END)
    FROM dbo.InventarioCiclicoSnapshot s
    LEFT JOIN dbo.InventarioCiclicoLectura l
        ON l.CiclicoId = s.CiclicoId
       AND l.CodigoEtiqueta = s.CodigoEtiqueta
    GROUP BY s.CiclicoId, s.Ubicacion
),
Extras AS
(
    SELECT
        CiclicoId,
        UbicacionCaptura,
        Sobrantes = SUM(CASE WHEN Resultado = 'SOBRANTE' THEN 1 ELSE 0 END)
    FROM dbo.InventarioCiclicoLectura
    GROUP BY CiclicoId, UbicacionCaptura
)
SELECT
    cat.Camara,
    cat.Rack,
    cat.Ubicacion,
    Estatus = CASE WHEN h.Clave IS NULL THEN 'PENDIENTE' ELSE 'INVENTARIADA' END,
    h.Folio AS UltimoFolio,
    h.FechaCierre AS UltimaFecha,
    VecesInventariada = ISNULL(h.VecesInventariada,0),
    CajasEsperadas = ISNULL(h.CajasEsperadas,0),
    CajasEncontradas = ISNULL(l.EncontradasEsperadas,0),
    Correctas = ISNULL(l.Correctas,0),
    MalUbicadas = ISNULL(l.MalUbicadas,0),
    Sobrantes = ISNULL(e.Sobrantes,0),
    Diferencia = ISNULL(l.EncontradasEsperadas,0) - ISNULL(h.CajasEsperadas,0),
    cat.Observacion
FROM dbo.InventarioCiclicoCatalogoUbicacion cat
LEFT JOIN Hist h
    ON h.Clave = cat.Ubicacion
   AND h.rn = 1
LEFT JOIN Leidas l
    ON l.CiclicoId = h.CiclicoId
   AND l.Ubicacion = cat.Ubicacion
LEFT JOIN Extras e
    ON e.CiclicoId = h.CiclicoId
   AND e.UbicacionCaptura = cat.Ubicacion
WHERE cat.Activo = 1
  AND cat.Planta = @Planta
ORDER BY cat.Orden, cat.Ubicacion;",
                new { Anio = anio, Planta = planta, AlmacenId = almacenId },
                commandTimeout: 120,
                cancellationToken: ct))).ToList();

            var ciclicos = (await cn.QueryAsync<dynamic>(new CommandDefinition(@"
;WITH A AS
(
    SELECT CiclicoId, Alcance = COUNT(*)
    FROM dbo.InventarioCiclicoAlcance
    GROUP BY CiclicoId
),
L AS
(
    SELECT
        CiclicoId,
        CajasEncontradasEsperadas = COUNT(DISTINCT CASE WHEN EsEsperada = 1 THEN CodigoEtiqueta END),
        CajasSobrantes = SUM(CASE WHEN Resultado = 'SOBRANTE' THEN 1 ELSE 0 END),
        MalUbicadas = SUM(CASE WHEN Resultado = 'UBICACION_DIFERENTE' THEN 1 ELSE 0 END),
        Invalidas = SUM(CASE WHEN Resultado IN ('NO_ENCONTRADA','AMBIGUA','INACTIVA','OTRO_ALMACEN','FUERA_ALCANCE') THEN 1 ELSE 0 END)
    FROM dbo.InventarioCiclicoLectura
    GROUP BY CiclicoId
)
SELECT
    c.Folio,
    c.Tipo,
    c.Estatus,
    c.Usuario,
    c.FechaInicio,
    c.FechaCierre,
    Alcance = ISNULL(a.Alcance,0),
    c.CajasEsperadas,
    c.KgEsperados,
    CajasEncontradasEsperadas = ISNULL(l.CajasEncontradasEsperadas,0),
    CajasSobrantes = ISNULL(l.CajasSobrantes,0),
    MalUbicadas = ISNULL(l.MalUbicadas,0),
    Invalidas = ISNULL(l.Invalidas,0)
FROM dbo.InventarioCiclico c
LEFT JOIN A a ON a.CiclicoId = c.Id
LEFT JOIN L l ON l.CiclicoId = c.Id
WHERE c.Anio = @Anio
  AND c.Planta = @Planta
  AND c.AlmacenId = @AlmacenId
ORDER BY c.FechaInicio DESC, c.Id DESC;",
                new { Anio = anio, Planta = planta, AlmacenId = almacenId },
                commandTimeout: 120,
                cancellationToken: ct))).ToList();

            var skuCoverage = (await cn.QueryAsync<dynamic>(new CommandDefinition(@"
;WITH H AS
(
    SELECT
        a.Clave AS SKU,
        UltimaFecha = MAX(c.FechaCierre),
        VecesInventariado = COUNT(*)
    FROM dbo.InventarioCiclico c
    INNER JOIN dbo.InventarioCiclicoAlcance a ON a.CiclicoId = c.Id
    WHERE c.Anio = @Anio
      AND c.Planta = @Planta
      AND c.AlmacenId = @AlmacenId
      AND c.Tipo = 'SKU'
      AND a.Tipo = 'SKU'
      AND c.Estatus LIKE 'COMPLETADO%'
    GROUP BY a.Clave
)
SELECT
    h.SKU,
    Producto = ISNULL(a.ProductoNombre,''),
    h.VecesInventariado,
    h.UltimaFecha
FROM H h
LEFT JOIN dbo.ArticuloSap a
    ON UPPER(LTRIM(RTRIM(ISNULL(a.ProductoCodigo,'')))) = h.SKU
ORDER BY h.SKU;",
                new { Anio = anio, Planta = planta, AlmacenId = almacenId },
                commandTimeout: 60,
                cancellationToken: ct))).ToList();

            var lecturas = new List<dynamic>();
            if (includeLecturas)
            {
                lecturas = (await cn.QueryAsync<dynamic>(new CommandDefinition(@"
SELECT
    c.Folio,
    c.Tipo,
    l.FechaLectura,
    l.CodigoEtiqueta,
    l.ProductoCodigo,
    l.ProductoNombre,
    l.PesoNeto,
    l.UbicacionEsperada,
    l.UbicacionMeat,
    l.UbicacionCaptura,
    l.Resultado,
    l.EsEsperada,
    l.Usuario
FROM dbo.InventarioCiclicoLectura l
INNER JOIN dbo.InventarioCiclico c ON c.Id = l.CiclicoId
WHERE c.Anio = @Anio
  AND c.Planta = @Planta
  AND c.AlmacenId = @AlmacenId
ORDER BY l.FechaLectura, l.Id;",
                    new { Anio = anio, Planta = planta, AlmacenId = almacenId },
                    commandTimeout: 180,
                    cancellationToken: ct))).ToList();
            }

            return new ReporteData
            {
                Anio = anio,
                Planta = planta,
                AlmacenId = almacenId,
                AlmacenNombre = cfg?.Name ?? almacenId,
                CatalogoTotal = catalogTotal,
                UbicacionesInventariadas = completedLocations,
                UbicacionesPendientes = Math.Max(0, catalogTotal - completedLocations),
                CoberturaPct = catalogTotal <= 0
                    ? 0m
                    : Math.Round((decimal)completedLocations * 100m / catalogTotal, 2),
                CiclicosCompletados = ciclos,
                CajasEsperadas = expected,
                CajasEncontradasEsperadas = foundExpected,
                CajasFaltantes = Math.Max(0, expected - foundExpected),
                CajasSobrantes = extras,
                CajasMalUbicadas = misplaced,
                KgEsperados = Math.Round(kgExpected, 3),
                KgLeidos = Math.Round(kgRead, 3),
                Cobertura = cobertura,
                Ciclicos = ciclicos,
                Lecturas = lecturas,
                Skus = skuCoverage
            };
        }

        private static void EscribirDynamicTable(IXLWorksheet ws, List<IDictionary<string, object>> rows)
        {
            if (rows.Count == 0)
            {
                ws.Cell("A1").Value = "Sin registros";
                return;
            }

            var headers = rows[0].Keys.ToList();
            for (int c = 0; c < headers.Count; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
                ws.Cell(1, c + 1).Style.Font.Bold = true;
            }

            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < headers.Count; c++)
                {
                    var value = rows[r].TryGetValue(headers[c], out var v) ? v : null;
                    var cell = ws.Cell(r + 2, c + 1);

                    if (value == null || value is DBNull)
                    {
                        cell.Value = "";
                    }
                    else if (value is DateTime dt)
                    {
                        cell.Value = dt;
                        cell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                    }
                    else if (value is bool b)
                    {
                        cell.Value = b ? "SI" : "NO";
                    }
                    else if (value is int i)
                    {
                        cell.Value = i;
                    }
                    else if (value is long l)
                    {
                        cell.Value = l;
                    }
                    else if (value is decimal d)
                    {
                        cell.Value = d;
                    }
                    else if (value is double db)
                    {
                        cell.Value = db;
                    }
                    else
                    {
                        cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    }
                }
            }

            var range = ws.Range(1, 1, rows.Count + 1, headers.Count);
            range.CreateTable();
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(8, 45);
        }

        // ============================================================
        // Helpers: sesión y estadísticas
        // ============================================================

        private async Task<object> ConstruirSesionDtoAsync(
            SqlConnection cn,
            long ciclicoId,
            CancellationToken ct)
        {
            var header = await cn.QueryFirstAsync<HeaderRow>(new CommandDefinition(@"
SELECT * FROM dbo.InventarioCiclico WHERE Id = @Id;",
                new { Id = ciclicoId },
                commandTimeout: 30,
                cancellationToken: ct));

            var alcance = (await cn.QueryAsync<AlcanceRow>(new CommandDefinition(@"
SELECT *
FROM dbo.InventarioCiclicoAlcance
WHERE CiclicoId = @Id
ORDER BY Orden, Clave;",
                new { Id = ciclicoId },
                commandTimeout: 30,
                cancellationToken: ct))).ToList();

            var stats = await ObtenerStatsAsync(cn, ciclicoId, ct);

            var recent = (await cn.QueryAsync<LecturaRow>(new CommandDefinition(@"
SELECT TOP (100) *
FROM dbo.InventarioCiclicoLectura
WHERE CiclicoId = @Id
ORDER BY FechaLectura DESC, Id DESC;",
                new { Id = ciclicoId },
                commandTimeout: 60,
                cancellationToken: ct))).ToList();

            var perScope = (await cn.QueryAsync<dynamic>(new CommandDefinition(@"
;WITH Found AS
(
    SELECT
        ScopeKey = CASE WHEN c.Tipo = 'UBICACION' THEN s.Ubicacion ELSE s.ProductoCodigo END,
        Encontradas = COUNT(DISTINCT CASE WHEN l.Id IS NOT NULL AND l.EsEsperada = 1 THEN s.CodigoEtiqueta END),
        Correctas = COUNT(DISTINCT CASE WHEN l.Resultado = 'CORRECTA' THEN s.CodigoEtiqueta END),
        MalUbicadas = COUNT(DISTINCT CASE WHEN l.Resultado = 'UBICACION_DIFERENTE' THEN s.CodigoEtiqueta END)
    FROM dbo.InventarioCiclico c
    INNER JOIN dbo.InventarioCiclicoSnapshot s ON s.CiclicoId = c.Id
    LEFT JOIN dbo.InventarioCiclicoLectura l
        ON l.CiclicoId = s.CiclicoId
       AND l.CodigoEtiqueta = s.CodigoEtiqueta
    WHERE c.Id = @Id
    GROUP BY CASE WHEN c.Tipo = 'UBICACION' THEN s.Ubicacion ELSE s.ProductoCodigo END
),
Extras AS
(
    SELECT
        ScopeKey = CASE WHEN c.Tipo = 'UBICACION' THEN l.UbicacionCaptura ELSE l.ProductoCodigo END,
        Sobrantes = SUM(CASE WHEN l.Resultado = 'SOBRANTE' THEN 1 ELSE 0 END)
    FROM dbo.InventarioCiclico c
    INNER JOIN dbo.InventarioCiclicoLectura l ON l.CiclicoId = c.Id
    WHERE c.Id = @Id
    GROUP BY CASE WHEN c.Tipo = 'UBICACION' THEN l.UbicacionCaptura ELSE l.ProductoCodigo END
)
SELECT
    a.Id,
    a.Tipo,
    a.Clave,
    a.Camara,
    a.Rack,
    a.Orden,
    a.CajasEsperadas,
    a.KgEsperados,
    Encontradas = ISNULL(f.Encontradas,0),
    Correctas = ISNULL(f.Correctas,0),
    MalUbicadas = ISNULL(f.MalUbicadas,0),
    Sobrantes = ISNULL(e.Sobrantes,0),
    Faltantes = CASE
        WHEN a.CajasEsperadas - ISNULL(f.Encontradas,0) < 0 THEN 0
        ELSE a.CajasEsperadas - ISNULL(f.Encontradas,0)
    END
FROM dbo.InventarioCiclicoAlcance a
LEFT JOIN Found f ON f.ScopeKey = a.Clave
LEFT JOIN Extras e ON e.ScopeKey = a.Clave
WHERE a.CiclicoId = @Id
ORDER BY a.Orden, a.Clave;",
                new { Id = ciclicoId },
                commandTimeout: 120,
                cancellationToken: ct))).ToList();

            return new
            {
                id = header.Id,
                folio = header.Folio,
                anio = header.Anio,
                planta = header.Planta,
                almacenId = header.AlmacenId,
                almacenNombre = header.AlmacenNombre,
                tipo = header.Tipo,
                estatus = header.Estatus,
                usuario = header.Usuario,
                observacion = header.Observacion,
                fechaInicio = header.FechaInicio,
                fechaCierre = header.FechaCierre,
                alcance = perScope,
                kpis = new
                {
                    esperadas = stats.Esperadas,
                    encontradasEsperadas = stats.EncontradasEsperadas,
                    faltantes = stats.Faltantes,
                    correctas = stats.Correctas,
                    sobrantes = stats.Sobrantes,
                    malUbicadas = stats.MalUbicadas,
                    invalidas = stats.Invalidas,
                    kgEsperados = stats.KgEsperados,
                    kgLeidos = stats.KgLeidos,
                    avance = stats.Esperadas <= 0
                        ? 100m
                        : Math.Round((decimal)stats.EncontradasEsperadas * 100m / stats.Esperadas, 2)
                },
                lecturas = recent.Select(x => new
                {
                    id = x.Id,
                    codigoEtiqueta = x.CodigoEtiqueta,
                    produccionId = x.ProduccionId,
                    sku = x.ProductoCodigo,
                    producto = x.ProductoNombre,
                    pesoNeto = x.PesoNeto,
                    ubicacionEsperada = x.UbicacionEsperada,
                    ubicacionMeat = x.UbicacionMeat,
                    ubicacionCaptura = x.UbicacionCaptura,
                    resultado = x.Resultado,
                    esEsperada = x.EsEsperada,
                    usuario = x.Usuario,
                    fechaLectura = x.FechaLectura
                })
            };
        }

        private sealed class StatsRow
        {
            public int Esperadas { get; set; }
            public int EncontradasEsperadas { get; set; }
            public int Faltantes { get; set; }
            public int Correctas { get; set; }
            public int Sobrantes { get; set; }
            public int MalUbicadas { get; set; }
            public int Invalidas { get; set; }
            public decimal KgEsperados { get; set; }
            public decimal KgLeidos { get; set; }
        }

        private async Task<StatsRow> ObtenerStatsAsync(
            SqlConnection cn,
            long ciclicoId,
            CancellationToken ct)
        {
            var row = await cn.QueryFirstAsync<StatsRow>(new CommandDefinition(@"
;WITH S AS
(
    SELECT
        Esperadas = COUNT(*),
        KgEsperados = ISNULL(SUM(PesoNeto),0)
    FROM dbo.InventarioCiclicoSnapshot
    WHERE CiclicoId = @Id
),
L AS
(
    SELECT
        EncontradasEsperadas = COUNT(DISTINCT CASE WHEN EsEsperada = 1 THEN CodigoEtiqueta END),
        Correctas = SUM(CASE WHEN Resultado = 'CORRECTA' THEN 1 ELSE 0 END),
        Sobrantes = SUM(CASE WHEN Resultado = 'SOBRANTE' THEN 1 ELSE 0 END),
        MalUbicadas = SUM(CASE WHEN Resultado = 'UBICACION_DIFERENTE' THEN 1 ELSE 0 END),
        Invalidas = SUM(CASE WHEN Resultado IN ('NO_ENCONTRADA','AMBIGUA','INACTIVA','OTRO_ALMACEN','FUERA_ALCANCE') THEN 1 ELSE 0 END),
        KgLeidos = ISNULL(SUM(PesoNeto),0)
    FROM dbo.InventarioCiclicoLectura
    WHERE CiclicoId = @Id
)
SELECT
    Esperadas = ISNULL(S.Esperadas,0),
    EncontradasEsperadas = ISNULL(L.EncontradasEsperadas,0),
    Faltantes = CASE
        WHEN ISNULL(S.Esperadas,0) - ISNULL(L.EncontradasEsperadas,0) < 0 THEN 0
        ELSE ISNULL(S.Esperadas,0) - ISNULL(L.EncontradasEsperadas,0)
    END,
    Correctas = ISNULL(L.Correctas,0),
    Sobrantes = ISNULL(L.Sobrantes,0),
    MalUbicadas = ISNULL(L.MalUbicadas,0),
    Invalidas = ISNULL(L.Invalidas,0),
    KgEsperados = ISNULL(S.KgEsperados,0),
    KgLeidos = ISNULL(L.KgLeidos,0)
FROM S CROSS JOIN L;",
                new { Id = ciclicoId },
                commandTimeout: 60,
                cancellationToken: ct));

            return row;
        }

        // ============================================================
        // Helpers: MEAT Produccion + ProduccionReferencia
        // ============================================================

        private async Task<List<MeatBox>> ConsultarProduccionMeatAsync(
            string planta,
            string? almacenId,
            IReadOnlyCollection<string>? skus,
            IReadOnlyCollection<string>? etiquetas,
            bool soloActivas,
            CancellationToken ct)
        {
            var plant = NormalizarPlanta(planta);
            var cs = ObtenerCadenaMeat(plant);
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException($"No existe la cadena de conexión MEAT para {plant}.");

            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync(ct);

            var schema = await ResolverProduccionReferenciaAsync(cn, ct);

            var filters = new StringBuilder("WHERE 1=1\n");
            var dp = new DynamicParameters();

            if (soloActivas)
                filters.AppendLine("  AND ISNULL(p.Estatus,0) = 1");

            var wh = NormalizarTexto(almacenId);
            if (!string.IsNullOrWhiteSpace(wh))
            {
                filters.AppendLine("  AND UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(100),ISNULL(p.Almacen,''))))) = @AlmacenId");
                dp.Add("AlmacenId", wh);
            }

            var skuList = (skus ?? Array.Empty<string>())
                .Select(NormalizarSku)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (skuList.Length > 0)
            {
                filters.AppendLine("  AND UPPER(LTRIM(RTRIM(ISNULL(p.Articulo,'')))) IN @Skus");
                dp.Add("Skus", skuList);
            }

            var codeList = (etiquetas ?? Array.Empty<string>())
                .Select(NormalizarEtiqueta)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (codeList.Length > 0)
            {
                filters.AppendLine("  AND UPPER(LTRIM(RTRIM(ISNULL(p.CodigoEtiqueta,'')))) IN @Etiquetas");
                dp.Add("Etiquetas", codeList);
            }

            var sql = $@"
SELECT
    p.ProduccionId,
    CodigoEtiqueta = UPPER(LTRIM(RTRIM(ISNULL(p.CodigoEtiqueta,'')))),
    Articulo = UPPER(LTRIM(RTRIM(ISNULL(p.Articulo,'')))),
    PesoNeto = ISNULL(TRY_CONVERT(decimal(18,3),p.PesoNeto),0),
    Estatus = ISNULL(p.Estatus,0),
    Almacen = UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(100),ISNULL(p.Almacen,''))))),
    ReferenciaUbicacion = {schema.ReferenceExpressionSql}
FROM dbo.Produccion p
LEFT JOIN {schema.TableSql} pr
    ON pr.{schema.ProduccionIdSql} = p.ProduccionId
{schema.ExtraJoinSql}
{filters}
ORDER BY p.ProduccionId DESC {schema.OrderSql};";

            var raw = (await cn.QueryAsync<MeatRawRow>(new CommandDefinition(
                sql,
                dp,
                commandTimeout: 120,
                cancellationToken: ct))).ToList();

            // Varias referencias pueden existir por ProduccionId. Elegimos la primera
            // referencia que tenga forma de ubicación (R8-01A / W8-01A, etc.).
            var byProduction = raw
                .GroupBy(x => x.ProduccionId)
                .Select(g =>
                {
                    var first = g.First();
                    var location = g
                        .Select(x => NormalizarUbicacion(x.ReferenciaUbicacion))
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

                    return new MeatBox
                    {
                        ProduccionId = first.ProduccionId,
                        CodigoEtiqueta = NormalizarEtiqueta(first.CodigoEtiqueta),
                        ProductoCodigo = NormalizarSku(first.Articulo),
                        PesoNeto = first.PesoNeto,
                        Estatus = first.Estatus,
                        AlmacenId = NormalizarTexto(first.Almacen),
                        Ubicacion = location
                    };
                })
                .ToList();

            // Detectar códigos repetidos sin multiplicar la fotografía.
            var codeCounts = byProduction
                .Where(x => !string.IsNullOrWhiteSpace(x.CodigoEtiqueta))
                .GroupBy(x => x.CodigoEtiqueta, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var box in byProduction)
            {
                if (codeCounts.TryGetValue(box.CodigoEtiqueta, out var count))
                    box.CoincidenciasCodigo = count;
            }

            return byProduction;
        }

        private async Task<RefSchema> ResolverProduccionReferenciaAsync(
            SqlConnection cn,
            CancellationToken ct)
        {
            var tableInfo = await cn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(@"
SELECT TOP (1)
    SchemaName = s.name,
    TableName = t.name
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name = 'ProduccionReferencia'
ORDER BY CASE WHEN s.name = 'dbo' THEN 0 ELSE 1 END, s.name;",
                commandTimeout: 30,
                cancellationToken: ct));

            if (tableInfo == null)
                throw new InvalidOperationException("MEAT no contiene la tabla ProduccionReferencia.");

            var schemaName = Convert.ToString(tableInfo.SchemaName) ?? "dbo";
            var tableName = Convert.ToString(tableInfo.TableName) ?? "ProduccionReferencia";
            var tableSql = $"{Q(schemaName)}.{Q(tableName)}";

            var columns = (await cn.QueryAsync<string>(new CommandDefinition(@"
SELECT c.name
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(@FullName)
ORDER BY c.column_id;",
                new { FullName = $"{schemaName}.{tableName}" },
                commandTimeout: 30,
                cancellationToken: ct))).ToList();

            string? Find(params string[] candidates) =>
                candidates.FirstOrDefault(candidate =>
                    columns.Any(x => x.Equals(candidate, StringComparison.OrdinalIgnoreCase))) is string found
                    ? columns.First(x => x.Equals(found, StringComparison.OrdinalIgnoreCase))
                    : null;

            var productionColumn = Find("ProduccionId", "ProduccionID", "Fk_Produccion", "fk_Produccion", "Produccion_Id");
            if (productionColumn == null)
            {
                throw new InvalidOperationException(
                    $"ProduccionReferencia existe pero no pude localizar la llave ProduccionId. Columnas: {string.Join(", ", columns)}");
            }

            var directLocation = Find("Ubicacion", "Ubicación", "Location");
            var locationId = Find("UbicacionId", "UbicaciónId", "ReferenciaId");
            var referenceText = Find("Referencia", "Valor", "Codigo", "Código", "Descripcion", "Descripción", "Nombre");

            string extraJoin = "";
            string referenceExpression;
            string source;

            if (directLocation != null)
            {
                referenceExpression = $"LTRIM(RTRIM(CONVERT(NVARCHAR(200),pr.{Q(directLocation)})))";
                source = $"ProduccionReferencia.{directLocation}";
            }
            else if (locationId != null)
            {
                var ubicacionTable = await ResolverTablaUbicacionAsync(cn, ct);
                if (ubicacionTable != null)
                {
                    extraJoin = $@"
LEFT JOIN {ubicacionTable.Value.TableSql} u
    ON CONVERT(NVARCHAR(200),u.{Q(ubicacionTable.Value.KeyColumn)})
     = CONVERT(NVARCHAR(200),pr.{Q(locationId)})";
                    referenceExpression = $"COALESCE(NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(200),u.{Q(ubicacionTable.Value.NameColumn)}))),''), LTRIM(RTRIM(CONVERT(NVARCHAR(200),pr.{Q(locationId)}))))";
                    source = $"ProduccionReferencia.{locationId} -> {ubicacionTable.Value.TableSql}.{ubicacionTable.Value.NameColumn}";
                }
                else
                {
                    referenceExpression = $"LTRIM(RTRIM(CONVERT(NVARCHAR(200),pr.{Q(locationId)})))";
                    source = $"ProduccionReferencia.{locationId}";
                }
            }
            else if (referenceText != null)
            {
                referenceExpression = $"LTRIM(RTRIM(CONVERT(NVARCHAR(200),pr.{Q(referenceText)})))";
                source = $"ProduccionReferencia.{referenceText}";
            }
            else
            {
                throw new InvalidOperationException(
                    "No pude identificar el campo de ubicación dentro de ProduccionReferencia. " +
                    $"Columnas detectadas: {string.Join(", ", columns)}. " +
                    "Ejecuta SQL/00_Validar_ProduccionReferencia_MEAT.sql y ajusta la prioridad del resolver.");
            }

            var orderColumn = Find(
                "FechaModificacion", "FechaRegistro", "FechaHora", "Fecha",
                "ProduccionReferenciaId", "Id");

            var orderSql = orderColumn == null
                ? ""
                : $", pr.{Q(orderColumn)} DESC";

            return new RefSchema
            {
                TableSql = tableSql,
                ProduccionIdSql = Q(productionColumn),
                ReferenceExpressionSql = referenceExpression,
                ExtraJoinSql = extraJoin,
                OrderSql = orderSql,
                SourceDescription = source,
                Columns = columns
            };
        }

        private async Task<(string TableSql, string KeyColumn, string NameColumn)?> ResolverTablaUbicacionAsync(
            SqlConnection cn,
            CancellationToken ct)
        {
            var tables = (await cn.QueryAsync<dynamic>(new CommandDefinition(@"
SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.name IN ('Ubicacion','Ubicaciones')
ORDER BY CASE WHEN t.name = 'Ubicacion' THEN 0 ELSE 1 END,
         CASE WHEN s.name = 'dbo' THEN 0 ELSE 1 END;",
                commandTimeout: 30,
                cancellationToken: ct))).ToList();

            foreach (var t in tables)
            {
                var schema = Convert.ToString(t.SchemaName) ?? "dbo";
                var table = Convert.ToString(t.TableName) ?? "";
                var full = $"{schema}.{table}";
                var cols = (await cn.QueryAsync<string>(new CommandDefinition(@"
SELECT c.name
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(@FullName)
ORDER BY c.column_id;",
                    new { FullName = full },
                    commandTimeout: 30,
                    cancellationToken: ct))).ToList();

                var key = cols.FirstOrDefault(x =>
                    x.Equals("UbicacionId", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("Id", StringComparison.OrdinalIgnoreCase));

                var name = cols.FirstOrDefault(x =>
                    x.Equals("Nombre", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("Ubicacion", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("Descripcion", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("Codigo", StringComparison.OrdinalIgnoreCase));

                if (key != null && name != null)
                    return ($"{Q(schema)}.{Q(table)}", key, name);
            }

            return null;
        }

        // ============================================================
        // Helpers: seguridad / configuración / utilerías
        // ============================================================

        private SqlConnection CrearConexionLocal()
        {
            var cs = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("No existe DefaultConnection.");
            return new SqlConnection(cs);
        }

        private string ObtenerCadenaMeat(string planta)
        {
            var key = NormalizarPlanta(planta) == "TIF"
                ? "CadenaMeatTIF"
                : "CadenaMeatP1";
            return _configuration.GetConnectionString(key) ?? "";
        }

        private List<WarehouseCfg> ObtenerWarehousesConfigurados()
        {
            var result = new List<WarehouseCfg>();

            foreach (var child in _configuration.GetSection("Warehouses").GetChildren())
            {
                var id = NormalizarTexto(child["Id"]);
                var name = (child["Name"] ?? "").Trim();
                var plantConfigured = NormalizarTexto(child["Planta"]);
                var sucursal = (child["Sucursal"] ?? child["Branch"] ?? "").Trim();

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;

                var inferText = $"{id} {name} {plantConfigured} {sucursal}".ToUpperInvariant();
                var plant = inferText.Contains("NO TIF")
                    ? "P1"
                    : plantConfigured == "TIF" || inferText.Contains(" TIF") || inferText.StartsWith("TIF")
                        ? "TIF"
                        : "P1";

                result.Add(new WarehouseCfg
                {
                    Id = id,
                    Name = name,
                    Planta = plant,
                    Sucursal = sucursal
                });
            }

            return result;
        }

        private async Task<HashSet<string>> ObtenerAlmacenesPermitidosAsync(CancellationToken ct)
        {
            // REGLA: UsuarioSQL.AlmacenesPermitidos manda para TODOS los usuarios.
            // Incluso Administrador/Sistemas sólo verá los almacenes que tenga asignados.
            // appsettings:Warehouses se usa únicamente como catálogo para nombre/planta/sucursal.
            var all = ObtenerWarehousesConfigurados();
            var configured = all
                .Select(x => NormalizarTexto(x.Id))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var raw = (UsuarioActual() ?? string.Empty).Trim();
            var username = raw.Contains('\\') ? raw.Split('\\').Last() : raw;
            var usernameNoMail = username.Contains('@') ? username.Split('@')[0] : username;

            var candidatos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AgregarCandidato(string? valor)
            {
                var v = (valor ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(v))
                    candidatos.Add(v.ToUpperInvariant());
            }

            AgregarCandidato(raw);
            AgregarCandidato(username);
            AgregarCandidato(usernameNoMail);

            // En IIS/AD el Identity.Name puede venir como DOMINIO\usuario,
            // mientras UsuarioSQL.Usuario normalmente está guardado como correo.
            if (!string.IsNullOrWhiteSpace(usernameNoMail) && !usernameNoMail.Contains('@'))
                AgregarCandidato($"{usernameNoMail}@carnesg.net");

            if (candidatos.Count == 0)
            {
                _logger.LogWarning("No se pudo identificar al usuario actual para resolver AlmacenesPermitidos.");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                await using var cn = CrearConexionLocal();
                await cn.OpenAsync(ct);

                var row = await cn.QueryFirstOrDefaultAsync<(string Usuario, string? AlmacenesPermitidos)>(
                    new CommandDefinition(@"
SELECT TOP (1)
    Usuario = ISNULL(Usuario,''),
    AlmacenesPermitidos
FROM dbo.UsuarioSQL WITH (NOLOCK)
WHERE Activo = 1
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(Usuario,'')))) IN @Usuarios
      OR UPPER(LTRIM(RTRIM(ISNULL(Nombre,'')))) IN @Usuarios
  )
ORDER BY
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(Usuario,'')))) = @UsuarioRaw THEN 0
        ELSE 1
    END,
    Id;",
                        new
                        {
                            Usuarios = candidatos.ToArray(),
                            UsuarioRaw = raw.ToUpperInvariant()
                        },
                        commandTimeout: 30,
                        cancellationToken: ct));

                if (string.IsNullOrWhiteSpace(row.Usuario))
                {
                    _logger.LogWarning(
                        "El usuario {Usuario} no fue encontrado como activo en dbo.UsuarioSQL.",
                        raw);

                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                if (string.IsNullOrWhiteSpace(row.AlmacenesPermitidos))
                {
                    _logger.LogWarning(
                        "El usuario {UsuarioSQL} no tiene AlmacenesPermitidos configurados.",
                        row.Usuario);

                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                List<string> ids;
                try
                {
                    ids = JsonSerializer.Deserialize<List<string>>(row.AlmacenesPermitidos!)
                          ?? new List<string>();
                }
                catch (JsonException ex)
                {
                    _logger.LogError(
                        ex,
                        "AlmacenesPermitidos del usuario {UsuarioSQL} no contiene JSON válido: {Json}",
                        row.Usuario,
                        row.AlmacenesPermitidos);

                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                var permitidos = ids
                    .Select(NormalizarTexto)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(configured.Contains)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var noConfigurados = ids
                    .Select(NormalizarTexto)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(x => !configured.Contains(x))
                    .ToList();

                if (noConfigurados.Count > 0)
                {
                    _logger.LogWarning(
                        "Usuario {UsuarioSQL}: hay almacenes permitidos que no existen en appsettings:Warehouses: {Almacenes}",
                        row.Usuario,
                        string.Join(", ", noConfigurados));
                }

                return permitidos;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "No fue posible resolver AlmacenesPermitidos desde UsuarioSQL para {Usuario}.",
                    raw);

                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task<(bool Ok, IActionResult? Result)> ValidarContextoAsync(
            int anio,
            string? planta,
            string? almacenId,
            CancellationToken ct)
        {
            if (anio < 2020 || anio > 2100)
                return (false, BadRequest(new { mensaje = "Año inválido." }));

            var plant = NormalizarPlanta(planta);
            if (plant != "P1" && plant != "TIF")
                return (false, BadRequest(new { mensaje = "Planta inválida." }));

            var wh = NormalizarTexto(almacenId);
            if (string.IsNullOrWhiteSpace(wh))
                return (false, BadRequest(new { mensaje = "Selecciona un almacén." }));

            var allowed = await ObtenerAlmacenesPermitidosAsync(ct);
            if (!allowed.Contains(wh))
                return (false, Forbid());

            var cfg = ObtenerWarehousesConfigurados()
                .FirstOrDefault(x => x.Id.Equals(wh, StringComparison.OrdinalIgnoreCase));

            if (cfg == null)
                return (false, BadRequest(new { mensaje = "El almacén no existe en Warehouses de appsettings.json." }));

            if (!cfg.Planta.Equals(plant, StringComparison.OrdinalIgnoreCase))
            {
                return (false, BadRequest(new
                {
                    mensaje = $"El almacén {cfg.Name} está configurado para {cfg.Planta}, no para {plant}."
                }));
            }

            return (true, null);
        }

        private async Task<Dictionary<string, string>> ObtenerNombresSkuAsync(
            IEnumerable<string> skus,
            CancellationToken ct)
        {
            var list = skus
                .Select(NormalizarSku)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2000)
                .ToArray();

            if (list.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var cn = CrearConexionLocal();
            await cn.OpenAsync(ct);

            var rows = await cn.QueryAsync<(string ProductoCodigo, string ProductoNombre)>(new CommandDefinition(@"
SELECT
    ProductoCodigo = UPPER(LTRIM(RTRIM(ISNULL(ProductoCodigo,'')))),
    ProductoNombre = ISNULL(ProductoNombre,'')
FROM dbo.ArticuloSap
WHERE UPPER(LTRIM(RTRIM(ISNULL(ProductoCodigo,'')))) IN @Skus;",
                new { Skus = list },
                commandTimeout: 60,
                cancellationToken: ct));

            return rows
                .GroupBy(x => x.ProductoCodigo, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().ProductoNombre ?? "", StringComparer.OrdinalIgnoreCase);
        }

        private string UsuarioActual() =>
            (User?.Identity?.Name ?? "DESCONOCIDO").Trim();

        private static string? NormalizarTipo(string? value)
        {
            var x = NormalizarTexto(value);
            return x switch
            {
                "UBICACION" => "UBICACION",
                "UBICACIÓN" => "UBICACION",
                "SKU" => "SKU",
                _ => null
            };
        }

        private static string NormalizarPlanta(string? value)
        {
            var x = NormalizarTexto(value);
            return x.Contains("TIF") ? "TIF" : x is "P1" or "PLANTA 1" or "PLANTA1" ? "P1" : x;
        }

        private static string NormalizarRack(string? value)
        {
            var x = NormalizarTexto(value);
            if (string.IsNullOrWhiteSpace(x)) return "";
            var m = Regex.Match(x, @"R?\s*(\d{1,2})", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out var n)
                ? $"R{n}"
                : x;
        }

        private static string NormalizarUbicacion(string? value)
        {
            var raw = NormalizarTexto(value);
            if (string.IsNullOrWhiteSpace(raw)) return "";

            var match = RxUbicacion.Match(raw);
            if (!match.Success) return "";

            if (!int.TryParse(match.Groups[2].Value, out var rack)) return "";
            if (!int.TryParse(match.Groups[3].Value, out var pos)) return "";
            var level = match.Groups[4].Value.ToUpperInvariant();

            return $"R{rack}-{pos:00}{level}";
        }

        private static string NormalizarEtiqueta(string? value) =>
            NormalizarTexto(value).Replace("\0", "");

        private static string NormalizarSku(string? value) =>
            NormalizarTexto(value);

        private static string NormalizarTexto(string? value) =>
            (value ?? "").Replace("\0", "").Trim().ToUpperInvariant();

        private static string Q(string identifier) =>
            "[" + (identifier ?? "").Replace("]", "]]", StringComparison.Ordinal) + "]";
    }
}
