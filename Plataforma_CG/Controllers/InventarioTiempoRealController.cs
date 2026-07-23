using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plataforma_CG.Data;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    /// <summary>
    /// Controlador ADITIVO para el conteo físico en tiempo real.
    ///
    /// No sustituye ni modifica InventarioController. Los endpoints existentes
    /// /Inventario/ScanBatch, /Inventario/Reporte y /Inventario/ReportePdf
    /// continúan funcionando como hasta ahora.
    /// </summary>
    [Route("InventarioTiempoReal/[action]")]
    public sealed class InventarioTiempoRealController : Controller
    {
        /*
         * PDFsharp Core no resuelve fuentes automáticamente en todos los
         * entornos. Este resolver carga una fuente sans-serif instalada en el
         * servidor y la incorpora al PDF.
         */
        private static readonly object PdfFontResolverLock = new();
        private static bool PdfFontResolverConfigured;

        private sealed class InventarioPdfFontResolver : IFontResolver
        {
            private const string RegularFace =
                "InventarioSans-Regular";

            private const string BoldFace =
                "InventarioSans-Bold";

            private readonly byte[] _regularFont;
            private readonly byte[] _boldFont;
            private readonly bool _simulateBold;

            public InventarioPdfFontResolver()
            {
                var regularPath = FindFirstExistingFont(
                    GetRegularFontCandidates());

                if (string.IsNullOrWhiteSpace(regularPath))
                {
                    throw new InvalidOperationException(
                        "No se encontró una fuente compatible para generar el PDF. " +
                        "Verifica que Arial, Segoe UI, Liberation Sans o " +
                        "DejaVu Sans estén instaladas en el servidor.");
                }

                var boldPath = FindFirstExistingFont(
                    GetBoldFontCandidates());

                _regularFont = System.IO.File.ReadAllBytes(regularPath);

                if (string.IsNullOrWhiteSpace(boldPath))
                {
                    _boldFont = _regularFont;
                    _simulateBold = true;
                }
                else
                {
                    _boldFont = System.IO.File.ReadAllBytes(boldPath);
                    _simulateBold = false;
                }
            }

            public FontResolverInfo ResolveTypeface(
                string familyName,
                bool bold,
                bool italic)
            {
                var faceName = bold
                    ? BoldFace
                    : RegularFace;

                return new FontResolverInfo(
                    faceName,
                    bold && _simulateBold,
                    italic);
            }

            public byte[] GetFont(string faceName)
            {
                return string.Equals(
                    faceName,
                    BoldFace,
                    StringComparison.Ordinal)
                        ? _boldFont
                        : _regularFont;
            }

            private static string? FindFirstExistingFont(
                IEnumerable<string> candidates)
            {
                return candidates
                    .Where(path =>
                        !string.IsNullOrWhiteSpace(path))
                    .FirstOrDefault(path => System.IO.File.Exists(path));
            }

            private static IEnumerable<string>
                GetRegularFontCandidates()
            {
                var windowsFonts =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.Fonts);

                var baseDirectory =
                    AppContext.BaseDirectory;

                return new[]
                {
                    Path.Combine(
                        baseDirectory,
                        "Fonts",
                        "Arial.ttf"),

                    Path.Combine(
                        baseDirectory,
                        "Fonts",
                        "DejaVuSans.ttf"),

                    Path.Combine(
                        windowsFonts ?? "",
                        "arial.ttf"),

                    Path.Combine(
                        windowsFonts ?? "",
                        "segoeui.ttf"),

                    Path.Combine(
                        windowsFonts ?? "",
                        "calibri.ttf"),

                    @"C:\Windows\Fonts\arial.ttf",
                    @"C:\Windows\Fonts\segoeui.ttf",

                    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                    "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
                    "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
                    "/usr/share/fonts/truetype/freefont/FreeSans.ttf",

                    "/System/Library/Fonts/Supplemental/Arial.ttf",
                    "/System/Library/Fonts/Supplemental/Arial Unicode.ttf"
                };
            }

            private static IEnumerable<string>
                GetBoldFontCandidates()
            {
                var windowsFonts =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.Fonts);

                var baseDirectory =
                    AppContext.BaseDirectory;

                return new[]
                {
                    Path.Combine(
                        baseDirectory,
                        "Fonts",
                        "Arial-Bold.ttf"),

                    Path.Combine(
                        baseDirectory,
                        "Fonts",
                        "DejaVuSans-Bold.ttf"),

                    Path.Combine(
                        windowsFonts ?? "",
                        "arialbd.ttf"),

                    Path.Combine(
                        windowsFonts ?? "",
                        "segoeuib.ttf"),

                    Path.Combine(
                        windowsFonts ?? "",
                        "calibrib.ttf"),

                    @"C:\Windows\Fonts\arialbd.ttf",
                    @"C:\Windows\Fonts\segoeuib.ttf",

                    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                    "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf",
                    "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
                    "/usr/share/fonts/truetype/freefont/FreeSansBold.ttf",

                    "/System/Library/Fonts/Supplemental/Arial Bold.ttf"
                };
            }
        }

        private static void EnsurePdfSharpFontResolver()
        {
            if (PdfFontResolverConfigured)
            {
                return;
            }

            lock (PdfFontResolverLock)
            {
                if (PdfFontResolverConfigured)
                {
                    return;
                }

                /*
                 * Debe ejecutarse antes de crear el primer XFont.
                 * El resolver se asigna una sola vez por proceso.
                 */
                try
                {
                    GlobalFontSettings.FontResolver =
                        new InventarioPdfFontResolver();
                }
                catch (InvalidOperationException)
                {
                    /*
                     * PDFsharp impide sustituir el resolver después de crear
                     * una fuente. Si otra parte de la aplicación ya configuró
                     * uno válido, continuamos y la creación de XFont lo
                     * confirmará.
                     */
                }

                PdfFontResolverConfigured = true;
            }
        }

        private static readonly string[] TiposIncidenciaPermitidos =
        {
            "Producto sin etiqueta",
            "Caja dañada",
            "Producto no localizado",
            "Producto sobrante",
            "Producto mezclado",
            "Lote incorrecto",
            "Tarima incompleta"
        };

        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<InventarioTiempoRealController> _logger;

        public InventarioTiempoRealController(
            AppDbContext context,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<InventarioTiempoRealController> logger)
        {
            _context = context;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        // =============================================================
        //  MODELOS DE PETICIÓN
        // =============================================================
        public sealed class IniciarSesionRequest
        {
            public List<string> Almacenes { get; set; } = new();
        }

        public sealed class RegistrarLecturasRequest
        {
            public int SesionId { get; set; }
            public string AlmacenId { get; set; } = "";
            public List<string> Codigos { get; set; } = new();
        }

        public sealed class CerrarSesionRequest
        {
            public int SesionId { get; set; }
        }

        public sealed class ReporteHistoricoRequest
        {
            public string Almacen { get; set; } = "ALL";
            public DateTime? Desde { get; set; }
            public DateTime? Hasta { get; set; }
        }

        public sealed class ReportePaginaRequest
        {
            public string Almacen { get; set; } = "ALL";
            public DateTime? Desde { get; set; }
            public DateTime? Hasta { get; set; }
            public int Pagina { get; set; } = 1;
            public int TamanoPagina { get; set; } = 50;
            public string Tipo { get; set; } = "ALL";
        }

        public sealed class WarehouseConfigItem
        {
            public string Id { get; set; } = "";

            /*
             * Name debe coincidir con dbo.Almacen.Nombre en la fuente real.
             * Para cambiar lo que ve el operador utiliza Sucursal.
             */
            public string Name { get; set; } = "";

            /*
             * P1 consulta CadenaMeatP1.
             * TIF consulta CadenaMeatTIF.
             */
            public string Plant { get; set; } = "";

            // Compatibilidad con configuraciones anteriores.
            public string Source { get; set; } = "";

            /*
             * Nombre visible de la sucursal:
             * PLANTA 1, TIF 805, TIF 776, CANCÚN, MÉRIDA, etc.
             */
            public string Sucursal { get; set; } = "";
        }

        private sealed class SesionDbRow
        {
            public int Id { get; set; }
            public string Folio { get; set; } = "";
            public DateTime FechaInicio { get; set; }
            public DateTime? FechaCierre { get; set; }
            public string UsuarioInicio { get; set; } = "";
            public string Estatus { get; set; } = "";
            public string Observaciones { get; set; } = "";
        }

        private sealed class SesionAlmacenDbRow
        {
            public int Id { get; set; }
            public int SesionId { get; set; }
            public string AlmacenId { get; set; } = "";
            public string AlmacenNombre { get; set; } = "";
            public decimal TotalEsperado { get; set; }
            public decimal KgEsperados { get; set; }
        }

        private sealed class InventarioResumenDbRow
        {
            public decimal TotalEsperado { get; set; }
            public decimal KgEsperados { get; set; }
        }

        private sealed class InventarioRealDetalleRow
        {
            public long ProduccionId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
            public string Sku { get; set; } = "";
            public string Producto { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public DateTime? FechaProduccion { get; set; }
            public string Almacen { get; set; } = "";
        }

        private sealed class AlmacenRealDbRow
        {
            /*
             * En CommerciaNet existen identificadores alfanuméricos como CNT.
             * Nunca deben convertirse a int.
             */
            public string AlmacenId { get; set; } = "";
            public string Nombre { get; set; } = "";
        }

        private sealed class InventarioRealAlmacenSnapshot
        {
            public string Planta { get; set; } = "";
            public bool AlmacenExiste { get; set; }
            public WarehouseConfigItem Almacen { get; set; } = new();
            public List<InventarioRealDetalleRow> Detalle { get; set; } = new();
        }

        private sealed class InventarioEsperadoFuenteRow
        {
            public string AlmacenId { get; set; } = "";
            public string AlmacenNombre { get; set; } = "";
            public string Planta { get; set; } = "";
            public string CodigoEtiqueta { get; set; } = "";
            public string Sku { get; set; } = "";
            public string Producto { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public DateTime? FechaProduccion { get; set; }
        }

        private sealed class EsperadoDbRow
        {
            public int Id { get; set; }
            public string AlmacenId { get; set; } = "";
            public string AlmacenNombre { get; set; } = "";
            public string CodigoEtiqueta { get; set; } = "";
            public string Sku { get; set; } = "";
            public string Producto { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public DateTime? FechaProduccion { get; set; }
        }

        private sealed class EtiquetaMetadataDbRow
        {
            public string CodigoEtiqueta { get; set; } = "";
            public string Sku { get; set; } = "";
            public string Producto { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public DateTime? FechaProduccion { get; set; }
            public string Colonia { get; set; } = "";
        }

        private sealed class WarehouseSummaryDbRow
        {
            public string AlmacenId { get; set; } = "";
            public string Almacen { get; set; } = "";
            public decimal Ubicaciones { get; set; }
            public decimal KgEsperados { get; set; }
            public int Contadas { get; set; }
            public decimal Pendientes { get; set; }
            public decimal Avance { get; set; }
        }

        private sealed class AgingDbRow
        {
            public string Rango { get; set; } = "";
            public int Cantidad { get; set; }
        }

        private sealed class IncidenciaResumenDbRow
        {
            public string Tipo { get; set; } = "";
            public int Cantidad { get; set; }
        }

        private sealed class IncidenciaDbRow
        {
            public long Id { get; set; }
            public string Tipo { get; set; } = "";
            public string CodigoEtiqueta { get; set; } = "";
            public string Producto { get; set; } = "";
            public decimal PesoKg { get; set; }
            public string Almacen { get; set; } = "";
            public string Ubicacion { get; set; } = "";
            public string Comentario { get; set; } = "";
            public string FotoUrl { get; set; } = "";
            public DateTime Fecha { get; set; }
        }

        private sealed class RegistrarLecturaResultadoDbRow
        {
            public string CodigoEtiqueta { get; set; } = "";
            public string Kind { get; set; } = "";
            public string Msg { get; set; } = "";
            public string Sku { get; set; } = "";
            public string Producto { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public int Insertada { get; set; }
            public int EsIncidencia { get; set; }
        }

        private sealed class ReporteSesionAlmacenDbRow
        {
            public int SesionId { get; set; }
            public string Folio { get; set; } = "";
            public DateTime FechaInicio { get; set; }
            public DateTime? FechaCierre { get; set; }
            public string Estatus { get; set; } = "";
            public string UsuarioInicio { get; set; } = "";
            public string UsuarioCierre { get; set; } = "";
            public string AlmacenId { get; set; } = "";
            public string AlmacenNombre { get; set; } = "";
        }

        private sealed class ReporteSesionRow
        {
            public int SesionId { get; set; }
            public string Folio { get; set; } = "";
            public DateTime FechaInicio { get; set; }
            public DateTime? FechaCierre { get; set; }
            public string Estatus { get; set; } = "";
            public string UsuarioInicio { get; set; } = "";
            public string UsuarioCierre { get; set; } = "";
            public string Almacenes { get; set; } = "";
            public int TotalAlmacenes { get; set; }
        }

        private sealed class ReporteAlmacenRow
        {
            public string AlmacenId { get; set; } = "";
            public string Almacen { get; set; } = "";
            public string AlmacenMostrar { get; set; } = "";
            public string Sucursal { get; set; } = "";
            public string Planta { get; set; } = "";
            public int Sesiones { get; set; }
            public decimal CajasIniciales { get; set; }
            public decimal KgIniciales { get; set; }
            public int Contadas { get; set; }
            public decimal KgContados { get; set; }
            public decimal Pendientes { get; set; }
            public int Sobrantes { get; set; }
            public int Mezcladas { get; set; }
            public int IncidenciasManuales { get; set; }
            public decimal Avance { get; set; }
        }

        private sealed class ReporteSkuRow
        {
            public string AlmacenId { get; set; } = "";
            public string Almacen { get; set; } = "";
            public string AlmacenMostrar { get; set; } = "";
            public string Sucursal { get; set; } = "";
            public string Planta { get; set; } = "";
            public string Sku { get; set; } = "";
            public string Producto { get; set; } = "";
            public int Cantidad { get; set; }
            public decimal Kg { get; set; }
        }

        private sealed class ReporteIncidenciaRow
        {
            public int TotalRegistros { get; set; }
            public long Id { get; set; }
            public string Origen { get; set; } = "";
            public int SesionId { get; set; }
            public string Folio { get; set; } = "";
            public DateTime FechaSesion { get; set; }
            public DateTime Fecha { get; set; }
            public string Tipo { get; set; } = "";
            public string AlmacenId { get; set; } = "";
            public string Almacen { get; set; } = "";
            public string AlmacenMostrar { get; set; } = "";
            public string Sucursal { get; set; } = "";
            public string Planta { get; set; } = "";
            public string CodigoEtiqueta { get; set; } = "";
            public string Sku { get; set; } = "";
            public string Producto { get; set; } = "";
            public decimal PesoKg { get; set; }
            public string Ubicacion { get; set; } = "";
            public string Comentario { get; set; } = "";
            public string FotoUrl { get; set; } = "";
            public string UsuarioRegistro { get; set; } = "";
        }

        private sealed class ReporteLecturaRow
        {
            public int TotalRegistros { get; set; }
            public long Id { get; set; }
            public int SesionId { get; set; }
            public string Folio { get; set; } = "";
            public DateTime FechaSesion { get; set; }
            public DateTime FechaRegistro { get; set; }
            public string AlmacenId { get; set; } = "";
            public string Almacen { get; set; } = "";
            public string AlmacenMostrar { get; set; } = "";
            public string Sucursal { get; set; } = "";
            public string Planta { get; set; } = "";
            public string AlmacenEsperadoId { get; set; } = "";
            public string AlmacenEsperado { get; set; } = "";
            public string AlmacenEsperadoMostrar { get; set; } = "";
            public string CodigoEtiqueta { get; set; } = "";
            public string Sku { get; set; } = "";
            public string Producto { get; set; } = "";
            public decimal PesoNeto { get; set; }
            public DateTime? FechaProduccion { get; set; }
            public string Estado { get; set; } = "";
            public string UsuarioRegistro { get; set; } = "";
        }

        private sealed class ReporteIncidenciaResumenRow
        {
            public string Tipo { get; set; } = "";
            public int Cantidad { get; set; }
        }

        private sealed class ReporteHistoricoData
        {
            public DateTime Generado { get; set; }
            public DateTime Desde { get; set; }
            public DateTime Hasta { get; set; }
            public string AlmacenFiltro { get; set; } = "ALL";
            public string AlmacenFiltroMostrar { get; set; } = "TODOS";
            public int TotalSesiones { get; set; }
            public decimal TotalEsperado { get; set; }
            public decimal TotalKgEsperados { get; set; }
            public int TotalLecturas { get; set; }
            public int TotalContadas { get; set; }
            public decimal TotalKgContados { get; set; }
            public decimal TotalPendiente { get; set; }
            public int TotalIncidencias { get; set; }
            public decimal AvanceGeneral { get; set; }
            public List<ReporteSesionRow> Sesiones { get; set; } = new();
            public List<ReporteAlmacenRow> ResumenAlmacenes { get; set; } = new();
            public List<ReporteSkuRow> StockPorSku { get; set; } = new();
            public List<AgingDbRow> Antiguedad { get; set; } = new();
            public List<ReporteIncidenciaResumenRow> IncidenciasResumen { get; set; } = new();
            public List<ReporteIncidenciaRow> Incidencias { get; set; } = new();
            public List<ReporteLecturaRow> Lecturas { get; set; } = new();
            public List<object> AlmacenesDisponibles { get; set; } = new();
        }

        // =============================================================
        //  HELPERS
        // =============================================================
        private string UsuarioActual()
        {
            return (User?.Identity?.Name ?? "").Trim();
        }

        private static string Norm(string? value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private List<WarehouseConfigItem> ObtenerAlmacenesConfigurados()
        {
            return _configuration
                .GetSection("Warehouses")
                .Get<List<WarehouseConfigItem>>()
                ?.Where(x =>
                    !string.IsNullOrWhiteSpace(x.Id) &&
                    !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => new WarehouseConfigItem
                {
                    Id = x.Id.Trim(),
                    Name = x.Name.Trim(),
                    Plant = (x.Plant ?? "").Trim(),
                    Source = (x.Source ?? "").Trim(),
                    Sucursal = (x.Sucursal ?? "").Trim()
                })
                .GroupBy(x => Norm(x.Id))
                .Select(g => g.First())
                .OrderBy(x => ObtenerSucursal(x))
                .ThenBy(x => x.Name)
                .ToList()
                ?? new List<WarehouseConfigItem>();
        }

        private bool UsuarioPuedeVerTodosLosAlmacenes()
        {
            return User.IsInRole("Administrador") || User.IsInRole("Sistemas");
        }

        private async Task<HashSet<string>> ObtenerIdsAlmacenesPermitidosAsync(
            CancellationToken ct)
        {
            var configurados = ObtenerAlmacenesConfigurados();

            if (UsuarioPuedeVerTodosLosAlmacenes())
            {
                return configurados
                    .Select(x => Norm(x.Id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var login = UsuarioActual();
            if (string.IsNullOrWhiteSpace(login))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var usuario = await _context.UsuarioSQL
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Usuario == login || x.Nombre == login,
                    ct);

            if (usuario == null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            List<string> ids;
            try
            {
                ids = JsonSerializer.Deserialize<List<string>>(
                    usuario.AlmacenesPermitidos ?? "[]") ?? new List<string>();
            }
            catch (JsonException)
            {
                ids = new List<string>();
            }

            return ids
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Norm)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private SqlConnection CrearConexion()
        {
            var connectionString = _context.Database.GetDbConnection().ConnectionString;
            return new SqlConnection(connectionString);
        }

        private static string NormalizarPlanta(string? value)
        {
            return string.Equals(
                (value ?? "").Trim(),
                "TIF",
                StringComparison.OrdinalIgnoreCase)
                    ? "TIF"
                    : "P1";
        }

        private string ObtenerPlantaPreferida(WarehouseConfigItem almacen)
        {
            var configurada = !string.IsNullOrWhiteSpace(almacen.Plant)
                ? almacen.Plant
                : almacen.Source;

            if (!string.IsNullOrWhiteSpace(configurada))
            {
                return NormalizarPlanta(configurada);
            }

            var texto = Norm($"{almacen.Id} {almacen.Name}");

            /*
             * Ejemplo real: CEDIS P1 NO TIF contiene la palabra TIF, pero
             * pertenece a P1. Esta regla debe evaluarse antes de buscar TIF.
             */
            if (
                texto.Contains("NO TIF", StringComparison.OrdinalIgnoreCase) ||
                texto.Contains("NO-TIF", StringComparison.OrdinalIgnoreCase)
            )
            {
                return "P1";
            }

            return texto.Contains("TIF", StringComparison.OrdinalIgnoreCase)
                ? "TIF"
                : "P1";
        }

        private static bool TienePlantaConfigurada(
            WarehouseConfigItem almacen)
        {
            var value = !string.IsNullOrWhiteSpace(almacen.Plant)
                ? almacen.Plant
                : almacen.Source;

            return string.Equals(
                       value?.Trim(),
                       "P1",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   string.Equals(
                       value?.Trim(),
                       "TIF",
                       StringComparison.OrdinalIgnoreCase);
        }

        private string ObtenerSucursal(WarehouseConfigItem almacen)
        {
            if (!string.IsNullOrWhiteSpace(almacen.Sucursal))
            {
                return almacen.Sucursal.Trim();
            }

            return ObtenerPlantaPreferida(almacen) == "TIF"
                ? "TIF"
                : "PLANTA 1";
        }

        private string ObtenerNombreMostrar(WarehouseConfigItem almacen)
        {
            return $"{ObtenerSucursal(almacen)} · {almacen.Name}";
        }

        private string ObtenerCadenaMeat(string planta)
        {
            var key = NormalizarPlanta(planta) == "TIF"
                ? "CadenaMeatTIF"
                : "CadenaMeatP1";

            return _configuration.GetConnectionString(key) ?? "";
        }

        /// <summary>
        /// Obtiene el inventario físico vigente directamente de Meat/TIF_Meat.
        ///
        /// La fuente y el filtro son los mismos usados por
        /// ProcesosCgController.InventarioCamaras:
        ///   Produccion.Estatus = 1
        ///   Produccion.Almacen -> CommerciaNet.dbo.Almacen
        ///   Almacen.Nombre = cámara seleccionada
        ///
        /// Además se devuelve el detalle por CodigoEtiqueta porque el resumen
        /// agrupado por SKU no basta para identificar faltantes, sobrantes o
        /// producto mezclado durante el conteo.
        /// </summary>
        private async Task<InventarioRealAlmacenSnapshot>
            ObtenerInventarioRealAlmacenAsync(
                WarehouseConfigItem almacen,
                CancellationToken ct)
        {
            var preferida = ObtenerPlantaPreferida(almacen);
            var plantas = new[]
            {
                preferida,
                preferida == "TIF" ? "P1" : "TIF"
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            Exception? ultimoError = null;

            foreach (var planta in plantas)
            {
                var cs = ObtenerCadenaMeat(planta);
                if (string.IsNullOrWhiteSpace(cs))
                {
                    ultimoError = new InvalidOperationException(
                        $"No existe la cadena de conexión " +
                        $"'{(planta == "TIF" ? "CadenaMeatTIF" : "CadenaMeatP1")}'.");
                    continue;
                }

                var dbCommercia = planta == "TIF"
                    ? "TIF_CommerciaNet"
                    : "CommerciaNet";

                var dbMeat = planta == "TIF"
                    ? "TIF_Meat"
                    : "Meat";

                var prefijoCanal = planta == "TIF"
                    ? "SACT"
                    : "SACC";

                try
                {
                    await using var meat = new SqlConnection(cs);
                    await meat.OpenAsync(ct);

                    /*
                     * Primero se resuelve AlmacenId una sola vez. Después se filtra
                     * Produccion por su llave, evitando aplicar LTRIM/RTRIM sobre
                     * cada registro de inventario.
                     */
                    var sqlAlmacenExacto = $@"
SELECT TOP (1)
    AlmacenId = LTRIM(RTRIM(CONVERT(NVARCHAR(100), AlmacenId))),
    Nombre
FROM {dbCommercia}.dbo.Almacen WITH (NOLOCK)
WHERE Nombre = @Camara;";

                    var almacenReal =
                        await meat.QueryFirstOrDefaultAsync<AlmacenRealDbRow>(
                            new CommandDefinition(
                                sqlAlmacenExacto,
                                new { Camara = almacen.Name },
                                commandTimeout: 15,
                                cancellationToken: ct));

                    // Respaldo para catálogos con espacios heredados.
                    if (almacenReal == null)
                    {
                        var sqlAlmacenNormalizado = $@"
SELECT TOP (1)
    AlmacenId = LTRIM(RTRIM(CONVERT(NVARCHAR(100), AlmacenId))),
    Nombre
FROM {dbCommercia}.dbo.Almacen WITH (NOLOCK)
WHERE LTRIM(RTRIM(Nombre)) = @Camara;";

                        almacenReal =
                            await meat.QueryFirstOrDefaultAsync<AlmacenRealDbRow>(
                                new CommandDefinition(
                                    sqlAlmacenNormalizado,
                                    new { Camara = almacen.Name.Trim() },
                                    commandTimeout: 15,
                                    cancellationToken: ct));
                    }

                    if (
                        almacenReal == null ||
                        string.IsNullOrWhiteSpace(almacenReal.AlmacenId)
                    )
                    {
                        continue;
                    }

                    almacenReal.AlmacenId = almacenReal.AlmacenId.Trim();

                    /*
                     * Consulta optimizada:
                     *  - filtra por Produccion.Almacen y Produccion.Estatus;
                     *  - no ordena miles de etiquetas;
                     *  - no agrupa toda CanalDetalle;
                     *  - obtiene una clasificación por ProduccionId mediante APPLY.
                     *
                     * La depuración de códigos repetidos se realiza en memoria antes
                     * del SqlBulkCopy, por lo que no se requiere ROW_NUMBER aquí.
                     */
                    var sqlDetalle = $@"
SELECT
    ProduccionId = CONVERT(BIGINT, Prod.ProduccionId),
    CodigoEtiqueta =
        UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(200), Prod.CodigoEtiqueta)))),
    Sku =
        UPPER(LTRIM(RTRIM(
            CASE
                WHEN SUBSTRING(Prod.CodigoEtiqueta, 1, 4) = @PrefijoCanal
                    THEN ISNULL(a2.ArticuloId, Prod.Articulo)
                ELSE Prod.Articulo
            END
        ))),
    Producto =
        LTRIM(RTRIM(
            CASE
                WHEN SUBSTRING(Prod.CodigoEtiqueta, 1, 4) = @PrefijoCanal
                    THEN ISNULL(NULLIF(a2.Nombre, ''), a.Nombre)
                ELSE a.Nombre
            END
        )),
    PesoNeto =
        CAST(ISNULL(Prod.PesoNeto, 0) AS DECIMAL(18,3)),
    FechaProduccion =
        CONVERT(date, Prod.FechaProduccion),
    Almacen =
        @AlmacenNombre
FROM Produccion Prod WITH (NOLOCK)
INNER JOIN {dbCommercia}.dbo.Articulo a WITH (NOLOCK)
    ON Prod.Articulo = a.ArticuloId
OUTER APPLY
(
    SELECT TOP (1)
        cd.ClasificacionId
    FROM {dbMeat}.dbo.CanalDetalle cd WITH (NOLOCK)
    WHERE cd.ProduccionId = Prod.ProduccionId
    ORDER BY cd.ClasificacionId DESC
) cd
LEFT JOIN {dbCommercia}.dbo.Articulo a2 WITH (NOLOCK)
    ON cd.ClasificacionId = a2.Clasifica1
WHERE Prod.Estatus = 1
  AND Prod.Almacen = @AlmacenRealId
  AND Prod.CodigoEtiqueta IS NOT NULL
  AND Prod.CodigoEtiqueta <> '';";

                    var detalle =
                        (await meat.QueryAsync<InventarioRealDetalleRow>(
                            new CommandDefinition(
                                sqlDetalle,
                                new
                                {
                                    AlmacenRealId = almacenReal.AlmacenId,
                                    AlmacenNombre = almacenReal.Nombre.Trim(),
                                    PrefijoCanal = prefijoCanal
                                },
                                commandTimeout: 120,
                                cancellationToken: ct)))
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.CodigoEtiqueta))
                        .GroupBy(
                            x => Norm(x.CodigoEtiqueta),
                            StringComparer.OrdinalIgnoreCase)
                        .Select(g => g
                            .OrderByDescending(x => x.FechaProduccion)
                            .ThenByDescending(x => x.ProduccionId)
                            .First())
                        .ToList();

                    return new InventarioRealAlmacenSnapshot
                    {
                        Planta = planta,
                        AlmacenExiste = true,
                        Almacen = almacen,
                        Detalle = detalle
                    };
                }
                catch (Exception ex)
                {
                    ultimoError = ex;

                    _logger.LogWarning(
                        ex,
                        "No se pudo consultar inventario real. Planta={Planta}, Almacen={Almacen}",
                        planta,
                        almacen.Name);
                }
            }

            if (ultimoError != null)
            {
                throw new InvalidOperationException(
                    $"No se pudo consultar el inventario real del almacén " +
                    $"'{almacen.Name}'. {ultimoError.GetBaseException().Message}",
                    ultimoError);
            }

            return new InventarioRealAlmacenSnapshot
            {
                Planta = preferida,
                AlmacenExiste = false,
                Almacen = almacen,
                Detalle = new List<InventarioRealDetalleRow>()
            };
        }

        private async Task<EtiquetaMetadataDbRow?>
            BuscarEtiquetaEnInventarioRealAsync(
                string codigoEtiqueta,
                CancellationToken ct)
        {
            var codigo = Norm(codigoEtiqueta);
            if (string.IsNullOrWhiteSpace(codigo))
            {
                return null;
            }

            foreach (var planta in new[] { "P1", "TIF" })
            {
                var cs = ObtenerCadenaMeat(planta);
                if (string.IsNullOrWhiteSpace(cs))
                {
                    continue;
                }

                var dbCommercia = planta == "TIF"
                    ? "TIF_CommerciaNet"
                    : "CommerciaNet";

                var dbMeat = planta == "TIF"
                    ? "TIF_Meat"
                    : "Meat";

                var prefijoCanal = planta == "TIF"
                    ? "SACT"
                    : "SACC";

                var sql = $@"
;WITH ClasificacionPorProduccion AS
(
    SELECT
        ProduccionId,
        ClasificacionId = MAX(ClasificacionId)
    FROM {dbMeat}.dbo.CanalDetalle WITH (NOLOCK)
    GROUP BY ProduccionId
)
SELECT TOP (1)
    CodigoEtiqueta =
        UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(200), Prod.CodigoEtiqueta)))),
    Sku =
        UPPER(LTRIM(RTRIM(
            CASE
                WHEN SUBSTRING(Prod.CodigoEtiqueta, 1, 4) = @PrefijoCanal
                    THEN ISNULL(a2.ArticuloId, Prod.Articulo)
                ELSE Prod.Articulo
            END
        ))),
    Producto =
        LTRIM(RTRIM(
            CASE
                WHEN SUBSTRING(Prod.CodigoEtiqueta, 1, 4) = @PrefijoCanal
                    THEN ISNULL(NULLIF(a2.Nombre, ''), a.Nombre)
                ELSE a.Nombre
            END
        )),
    PesoNeto =
        CAST(ISNULL(Prod.PesoNeto, 0) AS DECIMAL(18,3)),
    FechaProduccion =
        CONVERT(date, Prod.FechaProduccion),
    Colonia =
        LTRIM(RTRIM(alm.Nombre))
FROM Produccion Prod WITH (NOLOCK)
INNER JOIN {dbCommercia}.dbo.Articulo a WITH (NOLOCK)
    ON Prod.Articulo = a.ArticuloId
INNER JOIN {dbCommercia}.dbo.Almacen alm WITH (NOLOCK)
    ON Prod.Almacen = alm.AlmacenId
LEFT JOIN ClasificacionPorProduccion cd
    ON Prod.ProduccionId = cd.ProduccionId
LEFT JOIN {dbCommercia}.dbo.Articulo a2 WITH (NOLOCK)
    ON cd.ClasificacionId = a2.Clasifica1
WHERE UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(200), Prod.CodigoEtiqueta)))) =
      @CodigoEtiqueta
ORDER BY
    CASE WHEN Prod.Estatus = 1 THEN 0 ELSE 1 END,
    Prod.FechaProduccion DESC,
    Prod.ProduccionId DESC;";

                try
                {
                    await using var meat = new SqlConnection(cs);
                    await meat.OpenAsync(ct);

                    var row = await meat.QueryFirstOrDefaultAsync<EtiquetaMetadataDbRow>(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                CodigoEtiqueta = codigo,
                                PrefijoCanal = prefijoCanal
                            },
                            cancellationToken: ct));

                    if (row != null)
                    {
                        return row;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "No se pudo buscar la etiqueta {Codigo} en {Planta}.",
                        codigo,
                        planta);
                }
            }

            return null;
        }

        private static DataTable CrearTablaEsperados(
            int sesionId,
            IReadOnlyCollection<InventarioEsperadoFuenteRow> rows)
        {
            var table = new DataTable();
            table.Columns.Add("SesionId", typeof(int));
            table.Columns.Add("AlmacenId", typeof(string));
            table.Columns.Add("AlmacenNombre", typeof(string));
            table.Columns.Add("CodigoEtiqueta", typeof(string));
            table.Columns.Add("Sku", typeof(string));
            table.Columns.Add("Producto", typeof(string));
            table.Columns.Add("PesoNeto", typeof(decimal));
            table.Columns.Add("FechaProduccion", typeof(DateTime));
            table.Columns.Add("Colonia", typeof(string));

            foreach (var item in rows)
            {
                var row = table.NewRow();
                row["SesionId"] = sesionId;
                row["AlmacenId"] = item.AlmacenId;
                row["AlmacenNombre"] = item.AlmacenNombre;
                row["CodigoEtiqueta"] = item.CodigoEtiqueta;
                row["Sku"] = item.Sku ?? "";
                row["Producto"] = item.Producto ?? "";
                row["PesoNeto"] = item.PesoNeto;
                row["FechaProduccion"] =
                    item.FechaProduccion.HasValue
                        ? item.FechaProduccion.Value.Date
                        : DBNull.Value;
                row["Colonia"] = item.AlmacenNombre;
                table.Rows.Add(row);
            }

            return table;
        }

        private static async Task InsertarEsperadosAsync(
            SqlConnection cn,
            SqlTransaction tx,
            int sesionId,
            IReadOnlyCollection<InventarioEsperadoFuenteRow> rows,
            CancellationToken ct)
        {
            if (rows.Count == 0)
            {
                return;
            }

            var table = CrearTablaEsperados(sesionId, rows);

            using var bulk = new SqlBulkCopy(
                cn,
                SqlBulkCopyOptions.CheckConstraints |
                SqlBulkCopyOptions.TableLock,
                tx)
            {
                DestinationTableName = "dbo.InventarioConteoEsperado",
                BatchSize = 2000,
                BulkCopyTimeout = 180
            };

            foreach (DataColumn column in table.Columns)
            {
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulk.WriteToServerAsync(table, ct);
        }

        private object ProyectarSesion(
            SesionDbRow sesion,
            IReadOnlyCollection<SesionAlmacenDbRow> almacenes)
        {
            var configurados = ObtenerAlmacenesConfigurados()
                .ToDictionary(
                    x => Norm(x.Id),
                    x => x,
                    StringComparer.OrdinalIgnoreCase);

            var almacenesProyectados = almacenes
                .Select(x =>
                {
                    configurados.TryGetValue(
                        Norm(x.AlmacenId),
                        out var configurado);

                    var planta = configurado == null
                        ? ""
                        : ObtenerPlantaPreferida(configurado);

                    var sucursal = configurado == null
                        ? ""
                        : ObtenerSucursal(configurado);

                    var nombreMostrar = configurado == null
                        ? x.AlmacenNombre
                        : ObtenerNombreMostrar(configurado);

                    return new
                    {
                        id = x.AlmacenId,
                        nombre = x.AlmacenNombre,
                        nombreMostrar,
                        sucursal,
                        planta,
                        plantaConfigurada =
                            configurado != null &&
                            TienePlantaConfigurada(configurado),
                        totalEsperado = x.TotalEsperado,
                        kgEsperados = x.KgEsperados
                    };
                })
                .ToList();

            return new
            {
                id = sesion.Id,
                folio = sesion.Folio,
                fechaInicio = sesion.FechaInicio,
                fechaCierre = sesion.FechaCierre,
                usuarioInicio = sesion.UsuarioInicio,
                estatus = sesion.Estatus,
                observaciones = sesion.Observaciones,
                almacenes = almacenesProyectados
            };
        }

        private async Task<(SesionDbRow? sesion, List<SesionAlmacenDbRow> almacenes)>
            ObtenerSesionAsync(
                SqlConnection cn,
                int sesionId,
                CancellationToken ct)
        {
            const string sqlSesion = @"
SELECT TOP (1)
    Id,
    Folio,
    FechaInicio,
    FechaCierre,
    UsuarioInicio,
    Estatus,
    ISNULL(Observaciones, '') AS Observaciones
FROM dbo.InventarioConteoSesion
WHERE Id = @SesionId;";

            var sesion = await cn.QueryFirstOrDefaultAsync<SesionDbRow>(
                new CommandDefinition(
                    sqlSesion,
                    new { SesionId = sesionId },
                    cancellationToken: ct));

            if (sesion == null)
            {
                return (null, new List<SesionAlmacenDbRow>());
            }

            const string sqlAlmacenes = @"
SELECT
    Id,
    SesionId,
    AlmacenId,
    AlmacenNombre,
    TotalEsperado,
    KgEsperados
FROM dbo.InventarioConteoSesionAlmacen
WHERE SesionId = @SesionId
ORDER BY AlmacenNombre;";

            var almacenes = (await cn.QueryAsync<SesionAlmacenDbRow>(
                new CommandDefinition(
                    sqlAlmacenes,
                    new { SesionId = sesionId },
                    cancellationToken: ct))).ToList();

            return (sesion, almacenes);
        }

        private async Task<bool> UsuarioPuedeConsultarSesionAsync(
            IReadOnlyCollection<SesionAlmacenDbRow> almacenes,
            CancellationToken ct)
        {
            var permitidos = await ObtenerIdsAlmacenesPermitidosAsync(ct);
            return almacenes.Count > 0 &&
                   almacenes.All(x => permitidos.Contains(Norm(x.AlmacenId)));
        }

        // =============================================================
        //  CATÁLOGO Y SESIÓN ACTIVA
        // =============================================================
        [HttpGet]
        public async Task<IActionResult> AlmacenesPermitidos(
            CancellationToken ct = default)
        {
            var permitidos = await ObtenerIdsAlmacenesPermitidosAsync(ct);

            var almacenes = ObtenerAlmacenesConfigurados()
                .Where(x => permitidos.Contains(Norm(x.Id)))
                .Select(x =>
                {
                    var planta = ObtenerPlantaPreferida(x);
                    var sucursal = ObtenerSucursal(x);

                    return new
                    {
                        id = x.Id,
                        nombre = x.Name,
                        nombreMostrar = ObtenerNombreMostrar(x),
                        sucursal,
                        planta,
                        plantaConfigurada = TienePlantaConfigurada(x)
                    };
                })
                .ToList();

            return Ok(new
            {
                ok = true,
                almacenes
            });
        }

        [HttpGet]
        public async Task<IActionResult> SesionActiva(
            int? sesionId = null,
            CancellationToken ct = default)
        {
            await using var cn = CrearConexion();
            await cn.OpenAsync(ct);

            int id;

            if (sesionId.HasValue && sesionId.Value > 0)
            {
                id = sesionId.Value;
            }
            else
            {
                const string sql = @"
SELECT TOP (1) Id
FROM dbo.InventarioConteoSesion
WHERE Estatus = 'ABIERTO'
  AND UsuarioInicio = @Usuario
ORDER BY FechaInicio DESC, Id DESC;";

                id = await cn.ExecuteScalarAsync<int?>(
                    new CommandDefinition(
                        sql,
                        new { Usuario = UsuarioActual() },
                        cancellationToken: ct)) ?? 0;
            }

            if (id <= 0)
            {
                return Ok(new
                {
                    ok = true,
                    sesion = (object?)null
                });
            }

            var (sesion, almacenes) = await ObtenerSesionAsync(cn, id, ct);

            if (sesion == null)
            {
                return Ok(new
                {
                    ok = true,
                    sesion = (object?)null
                });
            }

            if (!await UsuarioPuedeConsultarSesionAsync(almacenes, ct))
            {
                return StatusCode(403, new
                {
                    ok = false,
                    message = "No tienes permiso para consultar uno o más almacenes de esta sesión."
                });
            }

            return Ok(new
            {
                ok = true,
                sesion = ProyectarSesion(sesion, almacenes)
            });
        }

        // =============================================================
        //  INICIAR SESIÓN Y TOMAR FOTOGRAFÍA DEL INVENTARIO
        // =============================================================
        [HttpPost]
        public async Task<IActionResult> IniciarSesion(
            [FromBody] IniciarSesionRequest request,
            CancellationToken ct = default)
        {
            var solicitados = (request?.Almacenes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Norm)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (solicitados.Count == 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "Selecciona al menos un almacén."
                });
            }

            var permitidos = await ObtenerIdsAlmacenesPermitidosAsync(ct);
            var noPermitidos = solicitados
                .Where(x => !permitidos.Contains(x))
                .ToList();

            if (noPermitidos.Count > 0)
            {
                return StatusCode(403, new
                {
                    ok = false,
                    message = "No tienes permiso para uno o más almacenes.",
                    almacenes = noPermitidos
                });
            }

            var configurados = ObtenerAlmacenesConfigurados();
            var seleccionados = configurados
                .Where(x => solicitados.Contains(Norm(x.Id)))
                .ToList();

            if (seleccionados.Count != solicitados.Count)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "Uno o más almacenes no existen en la sección Warehouses de appsettings.json."
                });
            }

            var usuario = UsuarioActual();
            if (string.IsNullOrWhiteSpace(usuario))
            {
                usuario = "SYSTEM";
            }

            await using var cn = CrearConexion();
            await cn.OpenAsync(ct);

            // Evita abrir varias sesiones accidentales para el mismo usuario.
            const string sqlSesionAbierta = @"
SELECT TOP (1) Id
FROM dbo.InventarioConteoSesion
WHERE Estatus = 'ABIERTO'
  AND UsuarioInicio = @Usuario
ORDER BY FechaInicio DESC, Id DESC;";

            var sesionAbiertaId = await cn.ExecuteScalarAsync<int?>(
                new CommandDefinition(
                    sqlSesionAbierta,
                    new { Usuario = usuario },
                    cancellationToken: ct));

            if (sesionAbiertaId.HasValue && sesionAbiertaId.Value > 0)
            {
                var (sesionExistente, almacenesExistentes) =
                    await ObtenerSesionAsync(cn, sesionAbiertaId.Value, ct);

                return Conflict(new
                {
                    ok = false,
                    message = "Ya tienes una sesión de inventario abierta. Se puede continuar sin crear otra.",
                    sesion = sesionExistente == null
                        ? null
                        : ProyectarSesion(sesionExistente, almacenesExistentes)
                });
            }

            /*
             * Antes de crear la sesión se consulta el inventario vigente directamente
             * en Meat/TIF_Meat. Es la misma fuente y condición utilizadas por
             * ProcesosCgController.InventarioCamaras:
             *
             *     Produccion.Estatus = 1
             *     Produccion.Almacen = Almacen.AlmacenId
             *     Almacen.Nombre = almacén seleccionado
             *
             * El resumen por SKU sirve para visualizar el inventario, pero para el
             * conteo físico se necesita además el detalle por CodigoEtiqueta.
             */
            var snapshots = new List<InventarioRealAlmacenSnapshot>();

            try
            {
                foreach (var almacen in seleccionados)
                {
                    var snapshot = await ObtenerInventarioRealAlmacenAsync(
                        almacen,
                        ct);

                    if (!snapshot.AlmacenExiste)
                    {
                        return BadRequest(new
                        {
                            ok = false,
                            message =
                                $"No se encontró el almacén '{almacen.Name}' " +
                                "en CommerciaNet ni en TIF_CommerciaNet. " +
                                "Revisa que Warehouses:Name coincida exactamente con Almacen.Nombre.",
                            almacenId = almacen.Id,
                            almacen = almacen.Name
                        });
                    }

                    snapshots.Add(snapshot);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error consultando la fotografía inicial en Meat.");

                return StatusCode(500, new
                {
                    ok = false,
                    message = ex.GetBaseException().Message
                });
            }

            // Une la fotografía de todos los almacenes seleccionados.
            var esperadosSinDepurar = snapshots
                .SelectMany(snapshot => snapshot.Detalle.Select(item =>
                    new InventarioEsperadoFuenteRow
                    {
                        AlmacenId = snapshot.Almacen.Id,
                        AlmacenNombre = snapshot.Almacen.Name,
                        Planta = snapshot.Planta,
                        CodigoEtiqueta = Norm(item.CodigoEtiqueta),
                        Sku = Norm(item.Sku),
                        Producto = (item.Producto ?? "").Trim(),
                        PesoNeto = item.PesoNeto,
                        FechaProduccion = item.FechaProduccion
                    }))
                .Where(x => !string.IsNullOrWhiteSpace(x.CodigoEtiqueta))
                .ToList();

            var etiquetasDuplicadas = esperadosSinDepurar
                .GroupBy(
                    x => x.CodigoEtiqueta,
                    StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => new
                {
                    Codigo = g.Key,
                    Almacenes = string.Join(
                        ", ",
                        g.Select(x => x.AlmacenNombre).Distinct())
                })
                .ToList();

            if (etiquetasDuplicadas.Count > 0)
            {
                _logger.LogWarning(
                    "Se detectaron {Cantidad} etiquetas repetidas entre almacenes al crear la fotografía. Ejemplos: {Ejemplos}",
                    etiquetasDuplicadas.Count,
                    string.Join(
                        " | ",
                        etiquetasDuplicadas
                            .Take(10)
                            .Select(x => $"{x.Codigo}: {x.Almacenes}")));
            }

            // La tabla de fotografía tiene llave única SesionId + CodigoEtiqueta.
            // Conservamos una sola ubicación por etiqueta; un duplicado en la fuente
            // debe revisarse como inconsistencia de inventario.
            var esperados = esperadosSinDepurar
                .GroupBy(
                    x => x.CodigoEtiqueta,
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(ct);

            try
            {
                var ahora = DateTime.Now;
                var folio =
                    $"INV-{ahora:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..31]
                    .ToUpperInvariant();

                const string sqlInsertSesion = @"
INSERT INTO dbo.InventarioConteoSesion
(
    Folio,
    FechaInicio,
    UsuarioInicio,
    Estatus
)
OUTPUT INSERTED.Id
VALUES
(
    @Folio,
    @FechaInicio,
    @UsuarioInicio,
    'ABIERTO'
);";

                var nuevoSesionId = await cn.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        sqlInsertSesion,
                        new
                        {
                            Folio = folio,
                            FechaInicio = ahora,
                            UsuarioInicio = usuario
                        },
                        transaction: tx,
                        cancellationToken: ct));

                const string sqlInsertAlmacen = @"
INSERT INTO dbo.InventarioConteoSesionAlmacen
(
    SesionId,
    AlmacenId,
    AlmacenNombre,
    TotalEsperado,
    KgEsperados
)
VALUES
(
    @SesionId,
    @AlmacenId,
    @AlmacenNombre,
    @TotalEsperado,
    @KgEsperados
);";

                foreach (var almacen in seleccionados)
                {
                    var detalleAlmacen = esperados
                        .Where(x =>
                            Norm(x.AlmacenId) == Norm(almacen.Id))
                        .ToList();

                    var totalEsperado = detalleAlmacen.Count;
                    var kgEsperados = detalleAlmacen.Sum(x => x.PesoNeto);

                    await cn.ExecuteAsync(
                        new CommandDefinition(
                            sqlInsertAlmacen,
                            new
                            {
                                SesionId = nuevoSesionId,
                                AlmacenId = almacen.Id,
                                AlmacenNombre = almacen.Name,
                                TotalEsperado = totalEsperado,
                                KgEsperados = kgEsperados
                            },
                            transaction: tx,
                            cancellationToken: ct));
                }

                await InsertarEsperadosAsync(
                    cn,
                    tx,
                    nuevoSesionId,
                    esperados,
                    ct);

                await tx.CommitAsync(ct);

                foreach (var snapshot in snapshots)
                {
                    _logger.LogInformation(
                        "Fotografía inventario creada. Sesion={SesionId}, Planta={Planta}, Almacen={Almacen}, Cajas={Cajas}, Kg={Kg}",
                        nuevoSesionId,
                        snapshot.Planta,
                        snapshot.Almacen.Name,
                        snapshot.Detalle.Count,
                        snapshot.Detalle.Sum(x => x.PesoNeto));
                }

                var (sesion, almacenes) =
                    await ObtenerSesionAsync(cn, nuevoSesionId, ct);

                return Ok(new
                {
                    ok = true,
                    sesion = sesion == null
                        ? null
                        : ProyectarSesion(sesion, almacenes),
                    fuente = "Produccion activa de Meat/TIF_Meat",
                    almacenesFuente = snapshots.Select(x => new
                    {
                        id = x.Almacen.Id,
                        nombre = x.Almacen.Name,
                        planta = x.Planta,
                        cajas = x.Detalle.Count,
                        kg = x.Detalle.Sum(y => y.PesoNeto)
                    }),
                    etiquetasDuplicadas = etiquetasDuplicadas.Count
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(
                    ex,
                    "Error al iniciar la sesión de inventario.");

                return StatusCode(500, new
                {
                    ok = false,
                    message = ex.GetBaseException().Message
                });
            }
        }

        // =============================================================
        //  REGISTRAR EN LA SESIÓN LAS LECTURAS QUE ScanBatch GUARDÓ BIEN
        // =============================================================
        [HttpPost]
        public async Task<IActionResult> RegistrarLecturas(
            [FromBody] RegistrarLecturasRequest request,
            CancellationToken ct = default)
        {
            if (request == null || request.SesionId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "La sesión es obligatoria."
                });
            }

            var almacenId = Norm(request.AlmacenId);
            if (string.IsNullOrWhiteSpace(almacenId))
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "Selecciona el almacén activo."
                });
            }

            var codigos = (request.Codigos ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Norm)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (codigos.Count == 0)
            {
                return Ok(new
                {
                    ok = true,
                    insertadas = 0,
                    duplicadas = 0,
                    incidencias = 0,
                    results = Array.Empty<object>()
                });
            }

            await using var cn = CrearConexion();
            await cn.OpenAsync(ct);

            var (sesion, almacenes) = await ObtenerSesionAsync(
                cn,
                request.SesionId,
                ct);

            if (sesion == null)
            {
                return NotFound(new
                {
                    ok = false,
                    message = "No se encontró la sesión de inventario."
                });
            }

            if (!string.Equals(
                    sesion.Estatus,
                    "ABIERTO",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "La sesión ya está cerrada."
                });
            }

            if (!await UsuarioPuedeConsultarSesionAsync(almacenes, ct))
            {
                return StatusCode(403, new
                {
                    ok = false,
                    message = "No tienes permiso para esta sesión."
                });
            }

            var almacenActivo = almacenes.FirstOrDefault(
                x => Norm(x.AlmacenId) == almacenId);

            if (almacenActivo == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "El almacén activo no pertenece a esta sesión."
                });
            }

            /*
             * Esta operación se realiza en un solo viaje a SQL Server.
             * Antes se ejecutaban hasta tres consultas por cada etiqueta y, si no
             * estaba en la fotografía, también se consultaban P1 y TIF en línea.
             *
             * La fotografía inicial ya contiene las etiquetas esperadas, por lo
             * que el match normal debe resolverse solamente contra SIGO.
             * Una etiqueta ajena se registra inmediatamente como sobrante, sin
             * bloquear al lector consultando las bases Meat de ambas plantas.
             */
            const string sql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Codigos TABLE
(
    Orden            INT           NOT NULL,
    CodigoEtiqueta   NVARCHAR(200) NOT NULL PRIMARY KEY
);

INSERT INTO @Codigos (Orden, CodigoEtiqueta)
SELECT
    TRY_CONVERT(INT, [key]),
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(200), [value]))))
FROM OPENJSON(@CodigosJson)
WHERE NULLIF(
    LTRIM(RTRIM(CONVERT(NVARCHAR(200), [value]))),
    ''
) IS NOT NULL;

DECLARE @Estado TABLE
(
    Orden                    INT            NOT NULL,
    CodigoEtiqueta           NVARCHAR(200)  NOT NULL PRIMARY KEY,
    YaExistia                BIT            NOT NULL,
    EsperadoId               BIGINT         NULL,
    AlmacenEsperadoId        NVARCHAR(100)  NULL,
    AlmacenEsperadoNombre    NVARCHAR(200)  NULL,
    Sku                      NVARCHAR(100)  NULL,
    Producto                 NVARCHAR(300)  NULL,
    PesoNeto                 DECIMAL(18,3) NULL,
    FechaProduccion          DATE           NULL
);

INSERT INTO @Estado
(
    Orden,
    CodigoEtiqueta,
    YaExistia,
    EsperadoId,
    AlmacenEsperadoId,
    AlmacenEsperadoNombre,
    Sku,
    Producto,
    PesoNeto,
    FechaProduccion
)
SELECT
    c.Orden,
    c.CodigoEtiqueta,
    CASE WHEN l.Id IS NULL THEN 0 ELSE 1 END,
    e.Id,
    e.AlmacenId,
    e.AlmacenNombre,
    e.Sku,
    e.Producto,
    e.PesoNeto,
    e.FechaProduccion
FROM @Codigos c
LEFT JOIN dbo.InventarioConteoLectura l WITH (UPDLOCK, HOLDLOCK)
    ON l.SesionId = @SesionId
   AND l.CodigoEtiqueta = c.CodigoEtiqueta
LEFT JOIN dbo.InventarioConteoEsperado e WITH (NOLOCK)
    ON e.SesionId = @SesionId
   AND e.CodigoEtiqueta = c.CodigoEtiqueta;

INSERT INTO dbo.InventarioConteoLectura
(
    SesionId,
    AlmacenId,
    AlmacenNombre,
    AlmacenEsperadoId,
    CodigoEtiqueta,
    Sku,
    Producto,
    PesoNeto,
    FechaProduccion,
    EsEsperado,
    EsAlmacenCorrecto,
    UsuarioRegistro,
    FechaRegistro
)
SELECT
    @SesionId,
    @AlmacenId,
    @AlmacenNombre,
    e.AlmacenEsperadoId,
    e.CodigoEtiqueta,
    ISNULL(e.Sku, ''),
    ISNULL(e.Producto, ''),
    ISNULL(e.PesoNeto, 0),
    e.FechaProduccion,
    CASE WHEN e.EsperadoId IS NULL THEN 0 ELSE 1 END,
    CASE
        WHEN e.EsperadoId IS NOT NULL
         AND e.AlmacenEsperadoId = @AlmacenId
            THEN 1
        ELSE 0
    END,
    @UsuarioRegistro,
    SYSDATETIME()
FROM @Estado e
WHERE e.YaExistia = 0;

SELECT
    e.CodigoEtiqueta,
    Kind = CASE
        WHEN e.YaExistia = 1 THEN 'dup'
        WHEN e.EsperadoId IS NULL THEN 'warn'
        WHEN e.AlmacenEsperadoId <> @AlmacenId THEN 'warn'
        ELSE 'ok'
    END,
    Msg = CASE
        WHEN e.YaExistia = 1
            THEN 'La etiqueta ya estaba contada en esta sesión.'
        WHEN e.EsperadoId IS NULL
            THEN 'Producto sobrante: la etiqueta no formaba parte de la fotografía inicial.'
        WHEN e.AlmacenEsperadoId <> @AlmacenId
            THEN 'Producto mezclado: se esperaba en '
                 + ISNULL(e.AlmacenEsperadoNombre, 'otro almacén')
                 + ' y se contó en ' + @AlmacenNombre + '.'
        ELSE 'Etiqueta localizada y registrada correctamente.'
    END,
    Sku = ISNULL(e.Sku, ''),
    Producto = ISNULL(e.Producto, ''),
    PesoNeto = ISNULL(e.PesoNeto, 0),
    Insertada = CASE WHEN e.YaExistia = 0 THEN 1 ELSE 0 END,
    EsIncidencia = CASE
        WHEN e.YaExistia = 0
         AND (
                e.EsperadoId IS NULL
             OR e.AlmacenEsperadoId <> @AlmacenId
         )
            THEN 1
        ELSE 0
    END
FROM @Estado e
ORDER BY e.Orden;";

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(ct);

            try
            {
                var resultados = (await cn.QueryAsync<RegistrarLecturaResultadoDbRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            SesionId = request.SesionId,
                            AlmacenId = almacenActivo.AlmacenId,
                            AlmacenNombre = almacenActivo.AlmacenNombre,
                            UsuarioRegistro = UsuarioActual(),
                            CodigosJson = JsonSerializer.Serialize(codigos)
                        },
                        transaction: tx,
                        commandTimeout: 30,
                        cancellationToken: ct))).ToList();

                await tx.CommitAsync(ct);

                return Ok(new
                {
                    ok = true,
                    insertadas = resultados.Sum(x => x.Insertada),
                    duplicadas = resultados.Count(x =>
                        string.Equals(
                            x.Kind,
                            "dup",
                            StringComparison.OrdinalIgnoreCase)),
                    incidencias = resultados.Sum(x => x.EsIncidencia),
                    results = resultados.Select(x => new
                    {
                        codigoEtiqueta = x.CodigoEtiqueta,
                        kind = x.Kind,
                        msg = x.Msg,
                        sku = x.Sku,
                        producto = x.Producto,
                        pesoNeto = x.PesoNeto
                    })
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);

                _logger.LogError(
                    ex,
                    "Error al registrar lecturas de forma masiva en la sesión {SesionId}.",
                    request.SesionId);

                return StatusCode(500, new
                {
                    ok = false,
                    message = ex.GetBaseException().Message
                });
            }
        }

        // =============================================================
        //  TABLERO EN TIEMPO REAL
        // =============================================================
        [HttpGet]
        public async Task<IActionResult> Dashboard(
            int sesionId,
            CancellationToken ct = default)
        {
            if (sesionId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "La sesión es obligatoria."
                });
            }

            await using var cn = CrearConexion();
            await cn.OpenAsync(ct);

            var (sesion, almacenes) = await ObtenerSesionAsync(cn, sesionId, ct);

            if (sesion == null)
            {
                return NotFound(new
                {
                    ok = false,
                    message = "No se encontró la sesión de inventario."
                });
            }

            if (!await UsuarioPuedeConsultarSesionAsync(almacenes, ct))
            {
                return StatusCode(403, new
                {
                    ok = false,
                    message = "No tienes permiso para esta sesión."
                });
            }

            const string sqlResumen = @"
SELECT
    sa.AlmacenId,
    Almacen = sa.AlmacenNombre,
    Ubicaciones = sa.TotalEsperado,
    sa.KgEsperados,
    Contadas = COUNT(CASE
        WHEN l.EsEsperado = 1
         AND l.EsAlmacenCorrecto = 1
         AND l.AlmacenId = sa.AlmacenId
        THEN 1 END),
    Pendientes = CAST(
        CASE
            WHEN sa.TotalEsperado - COUNT(CASE
                WHEN l.EsEsperado = 1
                 AND l.EsAlmacenCorrecto = 1
                 AND l.AlmacenId = sa.AlmacenId
                THEN 1 END) < 0
            THEN 0
            ELSE sa.TotalEsperado - COUNT(CASE
                WHEN l.EsEsperado = 1
                 AND l.EsAlmacenCorrecto = 1
                 AND l.AlmacenId = sa.AlmacenId
                THEN 1 END)
        END
    AS DECIMAL(18,3)),
    Avance = CAST(
        CASE
            WHEN sa.TotalEsperado <= 0 THEN 0
            ELSE
                COUNT(CASE
                    WHEN l.EsEsperado = 1
                     AND l.EsAlmacenCorrecto = 1
                     AND l.AlmacenId = sa.AlmacenId
                    THEN 1 END) * 100.0 / sa.TotalEsperado
        END
    AS DECIMAL(10,2))
FROM dbo.InventarioConteoSesionAlmacen sa
LEFT JOIN dbo.InventarioConteoLectura l
    ON l.SesionId = sa.SesionId
WHERE sa.SesionId = @SesionId
GROUP BY
    sa.AlmacenId,
    sa.AlmacenNombre,
    sa.TotalEsperado,
    sa.KgEsperados
ORDER BY sa.AlmacenNombre;";

            var resumen = (await cn.QueryAsync<WarehouseSummaryDbRow>(
                new CommandDefinition(
                    sqlResumen,
                    new { SesionId = sesionId },
                    cancellationToken: ct))).ToList();

            const string sqlAntiguedad = @"
WITH Base AS
(
    SELECT
        Dias = CASE
            WHEN FechaProduccion IS NULL THEN 0
            WHEN DATEDIFF(DAY, FechaProduccion, @FechaReferencia) < 0 THEN 0
            ELSE DATEDIFF(DAY, FechaProduccion, @FechaReferencia)
        END
    FROM dbo.InventarioConteoEsperado
    WHERE SesionId = @SesionId
),
Rangos AS
(
    SELECT '0-15 días' AS Rango, 1 AS Orden
    UNION ALL SELECT '16-30 días', 2
    UNION ALL SELECT '31-60 días', 3
    UNION ALL SELECT '61-90 días', 4
    UNION ALL SELECT 'Más de 90 días', 5
),
Conteo AS
(
    SELECT
        Rango = CASE
            WHEN Dias BETWEEN 0 AND 15 THEN '0-15 días'
            WHEN Dias BETWEEN 16 AND 30 THEN '16-30 días'
            WHEN Dias BETWEEN 31 AND 60 THEN '31-60 días'
            WHEN Dias BETWEEN 61 AND 90 THEN '61-90 días'
            ELSE 'Más de 90 días'
        END,
        Cantidad = COUNT(1)
    FROM Base
    GROUP BY CASE
        WHEN Dias BETWEEN 0 AND 15 THEN '0-15 días'
        WHEN Dias BETWEEN 16 AND 30 THEN '16-30 días'
        WHEN Dias BETWEEN 31 AND 60 THEN '31-60 días'
        WHEN Dias BETWEEN 61 AND 90 THEN '61-90 días'
        ELSE 'Más de 90 días'
    END
)
SELECT
    r.Rango,
    Cantidad = ISNULL(c.Cantidad, 0)
FROM Rangos r
LEFT JOIN Conteo c ON c.Rango = r.Rango
ORDER BY r.Orden;";

            var antiguedad = (await cn.QueryAsync<AgingDbRow>(
                new CommandDefinition(
                    sqlAntiguedad,
                    new
                    {
                        SesionId = sesionId,
                        FechaReferencia = sesion.FechaInicio.Date
                    },
                    cancellationToken: ct))).ToList();

            // El pendiente del resumen representa lo que aún no se ha localizado
            // correctamente. También funciona cuando no fue posible obtener
            // fotografía individual y el respaldo proviene de InventarioSigo.
            var faltantes = Convert.ToInt32(
                Math.Ceiling(resumen.Sum(x => x.Pendientes)));

            const string sqlSobrantes = @"
SELECT COUNT(1)
FROM dbo.InventarioConteoLectura
WHERE SesionId = @SesionId
  AND EsEsperado = 0;";

            var sobrantes = await cn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    sqlSobrantes,
                    new { SesionId = sesionId },
                    cancellationToken: ct));

            const string sqlMezclados = @"
SELECT COUNT(1)
FROM dbo.InventarioConteoLectura
WHERE SesionId = @SesionId
  AND EsEsperado = 1
  AND EsAlmacenCorrecto = 0;";

            var mezclados = await cn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    sqlMezclados,
                    new { SesionId = sesionId },
                    cancellationToken: ct));

            const string sqlManualResumen = @"
SELECT
    Tipo,
    Cantidad = COUNT(1)
FROM dbo.InventarioConteoIncidencia
WHERE SesionId = @SesionId
GROUP BY Tipo;";

            var resumenManual = (await cn.QueryAsync<IncidenciaResumenDbRow>(
                new CommandDefinition(
                    sqlManualResumen,
                    new { SesionId = sesionId },
                    cancellationToken: ct))).ToList();

            var mapaIncidencias = TiposIncidenciaPermitidos
                .ToDictionary(x => x, _ => 0, StringComparer.OrdinalIgnoreCase);

            mapaIncidencias["Producto no localizado"] += faltantes;
            mapaIncidencias["Producto sobrante"] += sobrantes;
            mapaIncidencias["Producto mezclado"] += mezclados;

            foreach (var item in resumenManual)
            {
                if (!mapaIncidencias.ContainsKey(item.Tipo))
                {
                    mapaIncidencias[item.Tipo] = 0;
                }

                mapaIncidencias[item.Tipo] += item.Cantidad;
            }

            const string sqlManualDetalle = @"
SELECT TOP (300)
    Id,
    Tipo,
    ISNULL(CodigoEtiqueta, '') AS CodigoEtiqueta,
    ISNULL(Producto, '') AS Producto,
    ISNULL(PesoKg, 0) AS PesoKg,
    ISNULL(AlmacenNombre, '') AS Almacen,
    ISNULL(Ubicacion, '') AS Ubicacion,
    ISNULL(Comentario, '') AS Comentario,
    ISNULL(FotoUrl, '') AS FotoUrl,
    FechaRegistro AS Fecha
FROM dbo.InventarioConteoIncidencia
WHERE SesionId = @SesionId
ORDER BY FechaRegistro DESC, Id DESC;";

            var incidencias = (await cn.QueryAsync<IncidenciaDbRow>(
                new CommandDefinition(
                    sqlManualDetalle,
                    new { SesionId = sesionId },
                    cancellationToken: ct))).ToList();

            const string sqlLecturasIncidencia = @"
SELECT TOP (300)
    Id = -l.Id,
    Tipo = CASE
        WHEN l.EsEsperado = 0 THEN 'Producto sobrante'
        ELSE 'Producto mezclado'
    END,
    l.CodigoEtiqueta,
    ISNULL(l.Producto, l.Sku) AS Producto,
    ISNULL(l.PesoNeto, 0) AS PesoKg,
    l.AlmacenNombre AS Almacen,
    '' AS Ubicacion,
    Comentario = CASE
        WHEN l.EsEsperado = 0
            THEN 'La etiqueta no formaba parte de la fotografía inicial.'
        ELSE 'Se contó en un almacén diferente al esperado.'
    END,
    '' AS FotoUrl,
    l.FechaRegistro AS Fecha
FROM dbo.InventarioConteoLectura l
WHERE l.SesionId = @SesionId
  AND
  (
       l.EsEsperado = 0
    OR (l.EsEsperado = 1 AND l.EsAlmacenCorrecto = 0)
  )
ORDER BY l.FechaRegistro DESC, l.Id DESC;";

            incidencias.AddRange(await cn.QueryAsync<IncidenciaDbRow>(
                new CommandDefinition(
                    sqlLecturasIncidencia,
                    new { SesionId = sesionId },
                    cancellationToken: ct)));

            const string sqlFaltantesDetalle = @"
SELECT TOP (300)
    Id = -1000000000 - e.Id,
    Tipo = 'Producto no localizado',
    e.CodigoEtiqueta,
    ISNULL(e.Producto, e.Sku) AS Producto,
    ISNULL(e.PesoNeto, 0) AS PesoKg,
    e.AlmacenNombre AS Almacen,
    '' AS Ubicacion,
    Comentario = 'Pendiente de localizar y contar en la sesión actual.',
    '' AS FotoUrl,
    @Fecha AS Fecha
FROM dbo.InventarioConteoEsperado e
WHERE e.SesionId = @SesionId
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.InventarioConteoLectura l
      WHERE l.SesionId = e.SesionId
        AND l.CodigoEtiqueta = e.CodigoEtiqueta
        AND l.EsEsperado = 1
        AND l.EsAlmacenCorrecto = 1
  )
ORDER BY e.AlmacenNombre, e.CodigoEtiqueta;";

            incidencias.AddRange(await cn.QueryAsync<IncidenciaDbRow>(
                new CommandDefinition(
                    sqlFaltantesDetalle,
                    new
                    {
                        SesionId = sesionId,
                        Fecha = DateTime.Now
                    },
                    cancellationToken: ct)));

            incidencias = incidencias
                .OrderByDescending(x => x.Fecha)
                .ThenBy(x => x.Tipo)
                .Take(600)
                .ToList();

            var totalEsperado = resumen.Sum(x => x.Ubicaciones);
            var totalContado = resumen.Sum(x => x.Contadas);
            var totalPendiente = resumen.Sum(x => x.Pendientes);
            var avanceGeneral = totalEsperado > 0
                ? Math.Round(totalContado * 100m / totalEsperado, 2)
                : 0m;

            return Ok(new
            {
                ok = true,
                fechaActualizacion = DateTime.Now,
                sesion = new
                {
                    id = sesion.Id,
                    folio = sesion.Folio,
                    estatus = sesion.Estatus,
                    fechaInicio = sesion.FechaInicio,
                    fechaCierre = sesion.FechaCierre
                },
                totalEsperado,
                totalContado,
                totalPendiente,
                avanceGeneral,
                resumenAlmacenes = resumen.Select(x => new
                {
                    almacenId = x.AlmacenId,
                    almacen = x.Almacen,
                    ubicaciones = x.Ubicaciones,
                    kgEsperados = x.KgEsperados,
                    contadas = x.Contadas,
                    pendientes = x.Pendientes,
                    avance = x.Avance
                }),
                antiguedad,
                incidenciasResumen = mapaIncidencias.Select(x => new
                {
                    tipo = x.Key,
                    cantidad = x.Value
                }),
                incidencias
            });
        }

        // =============================================================
        //  INCIDENCIA MANUAL CON FOTO OPCIONAL
        // =============================================================
        [HttpPost]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> RegistrarIncidencia(
            [FromForm] int sesionId,
            [FromForm] string almacenId,
            [FromForm] string tipo,
            [FromForm] string? codigoEtiqueta,
            [FromForm] string? producto,
            [FromForm] decimal? pesoKg,
            [FromForm] string? ubicacion,
            [FromForm] string? comentario,
            [FromForm] IFormFile? foto,
            CancellationToken ct = default)
        {
            var tipoNormalizado = TiposIncidenciaPermitidos.FirstOrDefault(
                x => string.Equals(x, tipo?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (sesionId <= 0 || string.IsNullOrWhiteSpace(tipoNormalizado))
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "Sesión y tipo de incidencia son obligatorios."
                });
            }

            if (!pesoKg.HasValue || pesoKg.Value <= 0 || pesoKg.Value > 99999)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "Captura los kilogramos de la caja o producto. Deben ser mayores a cero."
                });
            }

            await using var cn = CrearConexion();
            await cn.OpenAsync(ct);

            var (sesion, almacenes) = await ObtenerSesionAsync(cn, sesionId, ct);

            if (sesion == null ||
                !string.Equals(sesion.Estatus, "ABIERTO", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "La sesión no existe o ya está cerrada."
                });
            }

            if (!await UsuarioPuedeConsultarSesionAsync(almacenes, ct))
            {
                return StatusCode(403, new
                {
                    ok = false,
                    message = "No tienes permiso para esta sesión."
                });
            }

            var almacen = almacenes.FirstOrDefault(
                x => Norm(x.AlmacenId) == Norm(almacenId));

            if (almacen == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "El almacén no pertenece a la sesión."
                });
            }

            string fotoUrl = "";

            if (foto != null && foto.Length > 0)
            {
                var extension = Path.GetExtension(foto.FileName).ToLowerInvariant();
                var permitidas = new[] { ".jpg", ".jpeg", ".png", ".webp" };

                if (!permitidas.Contains(extension))
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "La fotografía debe ser JPG, PNG o WEBP."
                    });
                }

                if (foto.Length > 8 * 1024 * 1024)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "La fotografía no puede exceder 8 MB."
                    });
                }

                var relativeFolder = Path.Combine(
                    "uploads",
                    "inventario-incidencias",
                    DateTime.Now.ToString("yyyy"),
                    DateTime.Now.ToString("MM"));

                var webRoot = string.IsNullOrWhiteSpace(_environment.WebRootPath)
                    ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                    : _environment.WebRootPath;

                var physicalFolder = Path.Combine(
                    webRoot,
                    relativeFolder);

                Directory.CreateDirectory(physicalFolder);

                var fileName = $"{Guid.NewGuid():N}{extension}";
                var physicalPath = Path.Combine(physicalFolder, fileName);

                await using var stream = System.IO.File.Create(physicalPath);
                await foto.CopyToAsync(stream, ct);

                fotoUrl = "/" + Path.Combine(relativeFolder, fileName)
                    .Replace('\\', '/');
            }

            const string sql = @"
INSERT INTO dbo.InventarioConteoIncidencia
(
    SesionId,
    AlmacenId,
    AlmacenNombre,
    Tipo,
    CodigoEtiqueta,
    Producto,
    PesoKg,
    Ubicacion,
    Comentario,
    FotoUrl,
    UsuarioRegistro,
    FechaRegistro
)
OUTPUT INSERTED.Id
VALUES
(
    @SesionId,
    @AlmacenId,
    @AlmacenNombre,
    @Tipo,
    @CodigoEtiqueta,
    @Producto,
    @PesoKg,
    @Ubicacion,
    @Comentario,
    @FotoUrl,
    @UsuarioRegistro,
    SYSDATETIME()
);";

            var id = await cn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        SesionId = sesionId,
                        AlmacenId = almacen.AlmacenId,
                        AlmacenNombre = almacen.AlmacenNombre,
                        Tipo = tipoNormalizado,
                        CodigoEtiqueta = Norm(codigoEtiqueta),
                        Producto = (producto ?? "").Trim(),
                        PesoKg = decimal.Round(pesoKg.Value, 3),
                        Ubicacion = (ubicacion ?? "").Trim(),
                        Comentario = (comentario ?? "").Trim(),
                        FotoUrl = fotoUrl,
                        UsuarioRegistro = UsuarioActual()
                    },
                    cancellationToken: ct));

            return Ok(new
            {
                ok = true,
                id,
                fotoUrl
            });
        }


        // =============================================================
        //  REPORTE HISTÓRICO: TODAS LAS SESIONES Y ALMACENES PERMITIDOS
        // =============================================================
        [HttpPost]
        public async Task<IActionResult> ReporteHistorico(
            [FromBody] ReporteHistoricoRequest? request,
            CancellationToken ct = default)
        {
            request ??= new ReporteHistoricoRequest();

            /*
             * Por omisión se consulta únicamente HOY. Esto evita abrir el
             * reporte con todo el mes y reduce drásticamente el tiempo inicial.
             */
            var desde = (request.Desde ?? DateTime.Today).Date;
            var hasta = (request.Hasta ?? DateTime.Today).Date;

            var validacion = await ValidarFiltroReporteAsync(
                request.Almacen,
                desde,
                hasta,
                ct);

            if (validacion.Error != null)
            {
                return validacion.Error;
            }

            try
            {
                var reporte = await ObtenerReporteHistoricoResumenAsync(
                    desde,
                    hasta,
                    validacion.Almacen,
                    validacion.Permitidos,
                    ct);

                /*
                 * El endpoint principal NO envía miles de incidencias ni todas
                 * las lecturas. Esos detalles se solicitan por páginas cuando
                 * el usuario abre cada sección.
                 */
                return Ok(new
                {
                    ok = true,
                    reporte.Generado,
                    reporte.Desde,
                    reporte.Hasta,
                    reporte.AlmacenFiltro,
                    reporte.AlmacenFiltroMostrar,
                    reporte.TotalSesiones,
                    reporte.TotalEsperado,
                    reporte.TotalKgEsperados,
                    reporte.TotalLecturas,
                    reporte.TotalContadas,
                    reporte.TotalKgContados,
                    reporte.TotalPendiente,
                    reporte.TotalIncidencias,
                    reporte.AvanceGeneral,
                    reporte.Sesiones,
                    reporte.ResumenAlmacenes,
                    reporte.StockPorSku,
                    reporte.Antiguedad,
                    reporte.IncidenciasResumen,
                    reporte.AlmacenesDisponibles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error generando resumen histórico de inventario.");

                return StatusCode(500, new
                {
                    ok = false,
                    message = "No se pudo generar el reporte histórico.",
                    error = ex.GetBaseException().Message
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ReporteHistoricoIncidencias(
            [FromBody] ReportePaginaRequest? request,
            CancellationToken ct = default)
        {
            request ??= new ReportePaginaRequest();

            var desde = (request.Desde ?? DateTime.Today).Date;
            var hasta = (request.Hasta ?? DateTime.Today).Date;

            var validacion = await ValidarFiltroReporteAsync(
                request.Almacen,
                desde,
                hasta,
                ct);

            if (validacion.Error != null)
            {
                return validacion.Error;
            }

            var pagina = Math.Max(1, request.Pagina);
            var tamanoPagina = Math.Clamp(
                request.TamanoPagina,
                20,
                200);

            var tipo = Norm(request.Tipo);
            if (string.IsNullOrWhiteSpace(tipo))
            {
                tipo = "ALL";
            }

            try
            {
                var resultado = await ObtenerIncidenciasPaginaAsync(
                    desde,
                    hasta,
                    validacion.Almacen,
                    tipo,
                    pagina,
                    tamanoPagina,
                    validacion.Permitidos,
                    ct);

                return Ok(new
                {
                    ok = true,
                    pagina,
                    tamanoPagina,
                    total = resultado.Total,
                    totalPaginas = Math.Max(
                        1,
                        (int)Math.Ceiling(
                            resultado.Total / (double)tamanoPagina)),
                    rows = resultado.Rows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error consultando incidencias históricas.");

                return StatusCode(500, new
                {
                    ok = false,
                    message = "No se pudieron consultar las incidencias.",
                    error = ex.GetBaseException().Message
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ReporteHistoricoLecturas(
            [FromBody] ReportePaginaRequest? request,
            CancellationToken ct = default)
        {
            request ??= new ReportePaginaRequest();

            var desde = (request.Desde ?? DateTime.Today).Date;
            var hasta = (request.Hasta ?? DateTime.Today).Date;

            var validacion = await ValidarFiltroReporteAsync(
                request.Almacen,
                desde,
                hasta,
                ct);

            if (validacion.Error != null)
            {
                return validacion.Error;
            }

            var pagina = Math.Max(1, request.Pagina);
            var tamanoPagina = Math.Clamp(
                request.TamanoPagina,
                20,
                200);

            try
            {
                var resultado = await ObtenerLecturasPaginaAsync(
                    desde,
                    hasta,
                    validacion.Almacen,
                    pagina,
                    tamanoPagina,
                    validacion.Permitidos,
                    ct);

                return Ok(new
                {
                    ok = true,
                    pagina,
                    tamanoPagina,
                    total = resultado.Total,
                    totalPaginas = Math.Max(
                        1,
                        (int)Math.Ceiling(
                            resultado.Total / (double)tamanoPagina)),
                    rows = resultado.Rows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error consultando lecturas históricas.");

                return StatusCode(500, new
                {
                    ok = false,
                    message = "No se pudieron consultar las lecturas.",
                    error = ex.GetBaseException().Message
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ReporteHistoricoPdf(
            string almacen = "ALL",
            DateTime? desde = null,
            DateTime? hasta = null,
            CancellationToken ct = default)
        {
            var fechaDesde = (desde ?? DateTime.Today).Date;
            var fechaHasta = (hasta ?? DateTime.Today).Date;

            var validacion = await ValidarFiltroReporteAsync(
                almacen,
                fechaDesde,
                fechaHasta,
                ct);

            if (validacion.Error != null)
            {
                return validacion.Error;
            }

            try
            {
                var reporte = await ObtenerReporteHistoricoResumenAsync(
                    fechaDesde,
                    fechaHasta,
                    validacion.Almacen,
                    validacion.Permitidos,
                    ct);

                /*
                 * El PDF sí incluye todo. Se recupera por lotes para no cargar
                 * una respuesta JSON gigantesca en el navegador.
                 */
                const int lotePdf = 2000;

                reporte.Incidencias = await ObtenerTodasIncidenciasAsync(
                    fechaDesde,
                    fechaHasta,
                    validacion.Almacen,
                    validacion.Permitidos,
                    lotePdf,
                    ct);

                reporte.Lecturas = await ObtenerTodasLecturasAsync(
                    fechaDesde,
                    fechaHasta,
                    validacion.Almacen,
                    validacion.Permitidos,
                    lotePdf,
                    ct);

                var bytes = GenerarReporteHistoricoPdf(reporte);

                var fileName =
                    $"Inventario_{fechaDesde:yyyyMMdd}_{fechaHasta:yyyyMMdd}" +
                    $"_{validacion.Almacen}.pdf";

                return File(bytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error generando PDF histórico de inventario.");

                return StatusCode(
                    500,
                    "No se pudo generar el PDF: " +
                    ex.GetBaseException().Message);
            }
        }

        private async Task<(
            string Almacen,
            HashSet<string> Permitidos,
            IActionResult? Error)> ValidarFiltroReporteAsync(
                string? almacen,
                DateTime desde,
                DateTime hasta,
                CancellationToken ct)
        {
            if (desde > hasta)
            {
                return (
                    "ALL",
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase),
                    BadRequest(new
                    {
                        ok = false,
                        message =
                            "La fecha DESDE no puede ser mayor que HASTA."
                    }));
            }

            var almacenNormalizado = Norm(almacen);
            if (string.IsNullOrWhiteSpace(almacenNormalizado))
            {
                almacenNormalizado = "ALL";
            }

            var permitidos = await ObtenerIdsAlmacenesPermitidosAsync(ct);

            if (permitidos.Count == 0)
            {
                return (
                    almacenNormalizado,
                    permitidos,
                    StatusCode(403, new
                    {
                        ok = false,
                        message =
                            "No tienes almacenes asignados para consultar."
                    }));
            }

            if (
                almacenNormalizado != "ALL" &&
                !permitidos.Contains(almacenNormalizado)
            )
            {
                return (
                    almacenNormalizado,
                    permitidos,
                    StatusCode(403, new
                    {
                        ok = false,
                        message =
                            "No tienes permiso para consultar ese almacén."
                    }));
            }

            return (
                almacenNormalizado,
                permitidos,
                null);
        }

        private async Task<ReporteHistoricoData>
            ObtenerReporteHistoricoResumenAsync(
                DateTime desde,
                DateTime hasta,
                string almacen,
                HashSet<string> permitidos,
                CancellationToken ct)
        {
            var permitidosJson = JsonSerializer.Serialize(
                permitidos.OrderBy(x => x).ToList());

            /*
             * Consulta ligera:
             * - no devuelve las miles de cajas pendientes;
             * - no devuelve el detalle completo de lecturas;
             * - usa igualdad directa en AlmacenId para aprovechar índices.
             */
            const string sql = @"
SET NOCOUNT ON;

DECLARE @HastaExclusivo DATETIME2 =
    DATEADD(DAY, 1, CAST(@Hasta AS date));

CREATE TABLE #Permitidos
(
    AlmacenId NVARCHAR(100) NOT NULL PRIMARY KEY
);

INSERT INTO #Permitidos (AlmacenId)
SELECT DISTINCT
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(100), value))))
FROM OPENJSON(@PermitidosJson)
WHERE NULLIF(
    LTRIM(RTRIM(CONVERT(NVARCHAR(100), value))),
    ''
) IS NOT NULL;

CREATE TABLE #AlmacenesFiltrados
(
    SesionId       INT            NOT NULL,
    AlmacenId      NVARCHAR(100)  NOT NULL,
    AlmacenNombre  NVARCHAR(300)  NOT NULL,
    TotalEsperado  DECIMAL(18,3)  NOT NULL,
    KgEsperados    DECIMAL(18,3)  NOT NULL,
    PRIMARY KEY (SesionId, AlmacenId)
);

INSERT INTO #AlmacenesFiltrados
(
    SesionId,
    AlmacenId,
    AlmacenNombre,
    TotalEsperado,
    KgEsperados
)
SELECT
    sa.SesionId,
    sa.AlmacenId,
    sa.AlmacenNombre,
    ISNULL(sa.TotalEsperado, 0),
    ISNULL(sa.KgEsperados, 0)
FROM dbo.InventarioConteoSesionAlmacen sa
INNER JOIN dbo.InventarioConteoSesion s
    ON s.Id = sa.SesionId
INNER JOIN #Permitidos p
    ON p.AlmacenId = sa.AlmacenId
WHERE s.FechaInicio >= @Desde
  AND s.FechaInicio < @HastaExclusivo
  AND
  (
      @Almacen = 'ALL'
      OR sa.AlmacenId = @Almacen
  );

CREATE TABLE #SesionesFiltradas
(
    SesionId INT NOT NULL PRIMARY KEY
);

INSERT INTO #SesionesFiltradas (SesionId)
SELECT DISTINCT SesionId
FROM #AlmacenesFiltrados;


/* 1. SESIONES */
SELECT
    SesionId = s.Id,
    s.Folio,
    s.FechaInicio,
    s.FechaCierre,
    s.Estatus,
    ISNULL(s.UsuarioInicio, '') AS UsuarioInicio,
    ISNULL(s.UsuarioCierre, '') AS UsuarioCierre,
    af.AlmacenId,
    af.AlmacenNombre
FROM #AlmacenesFiltrados af
INNER JOIN dbo.InventarioConteoSesion s
    ON s.Id = af.SesionId
ORDER BY
    s.FechaInicio DESC,
    s.Id DESC,
    af.AlmacenNombre;


/* 2. RESUMEN POR ALMACÉN */
;WITH LecturasAgg AS
(
    SELECT
        l.SesionId,
        l.AlmacenId,
        Contadas = SUM(CASE
            WHEN l.EsEsperado = 1
             AND l.EsAlmacenCorrecto = 1
            THEN 1 ELSE 0 END),
        KgContados = SUM(CASE
            WHEN l.EsEsperado = 1
             AND l.EsAlmacenCorrecto = 1
            THEN ISNULL(l.PesoNeto, 0) ELSE 0 END),
        Sobrantes = SUM(CASE
            WHEN l.EsEsperado = 0
            THEN 1 ELSE 0 END),
        Mezcladas = SUM(CASE
            WHEN l.EsEsperado = 1
             AND l.EsAlmacenCorrecto = 0
            THEN 1 ELSE 0 END)
    FROM dbo.InventarioConteoLectura l
    INNER JOIN #AlmacenesFiltrados af
        ON af.SesionId = l.SesionId
       AND af.AlmacenId = l.AlmacenId
    GROUP BY
        l.SesionId,
        l.AlmacenId
),
IncidenciasAgg AS
(
    SELECT
        i.SesionId,
        i.AlmacenId,
        IncidenciasManuales = COUNT(1)
    FROM dbo.InventarioConteoIncidencia i
    INNER JOIN #AlmacenesFiltrados af
        ON af.SesionId = i.SesionId
       AND af.AlmacenId = i.AlmacenId
    GROUP BY
        i.SesionId,
        i.AlmacenId
)
SELECT
    af.AlmacenId,
    Almacen = MAX(af.AlmacenNombre),
    Sesiones = COUNT(DISTINCT af.SesionId),
    CajasIniciales = SUM(af.TotalEsperado),
    KgIniciales = SUM(af.KgEsperados),
    Contadas = SUM(ISNULL(la.Contadas, 0)),
    KgContados = SUM(ISNULL(la.KgContados, 0)),
    Pendientes = SUM(
        CASE
            WHEN af.TotalEsperado - ISNULL(la.Contadas, 0) < 0
                THEN 0
            ELSE af.TotalEsperado - ISNULL(la.Contadas, 0)
        END
    ),
    Sobrantes = SUM(ISNULL(la.Sobrantes, 0)),
    Mezcladas = SUM(ISNULL(la.Mezcladas, 0)),
    IncidenciasManuales =
        SUM(CONVERT(INT, ISNULL(ia.IncidenciasManuales, 0))),
    Avance = CAST(
        CASE
            WHEN SUM(af.TotalEsperado) <= 0 THEN 0
            ELSE
                SUM(ISNULL(la.Contadas, 0)) * 100.0
                / SUM(af.TotalEsperado)
        END
    AS DECIMAL(10,2))
FROM #AlmacenesFiltrados af
LEFT JOIN LecturasAgg la
    ON la.SesionId = af.SesionId
   AND la.AlmacenId = af.AlmacenId
LEFT JOIN IncidenciasAgg ia
    ON ia.SesionId = af.SesionId
   AND ia.AlmacenId = af.AlmacenId
GROUP BY af.AlmacenId
ORDER BY MAX(af.AlmacenNombre);


/* 3. STOCK CONTADO POR SKU */
SELECT
    af.AlmacenId,
    Almacen = MAX(af.AlmacenNombre),
    Sku = ISNULL(
        NULLIF(LTRIM(RTRIM(l.Sku)), ''),
        'SIN SKU'
    ),
    Producto = ISNULL(
        NULLIF(LTRIM(RTRIM(MAX(l.Producto))), ''),
        ISNULL(
            NULLIF(LTRIM(RTRIM(l.Sku)), ''),
            'SIN SKU'
        )
    ),
    Cantidad = COUNT(1),
    Kg = CAST(
        SUM(ISNULL(l.PesoNeto, 0))
        AS DECIMAL(18,3)
    )
FROM dbo.InventarioConteoLectura l
INNER JOIN #AlmacenesFiltrados af
    ON af.SesionId = l.SesionId
   AND af.AlmacenId = l.AlmacenId
GROUP BY
    af.AlmacenId,
    ISNULL(
        NULLIF(LTRIM(RTRIM(l.Sku)), ''),
        'SIN SKU'
    )
ORDER BY
    MAX(af.AlmacenNombre),
    Cantidad DESC,
    Sku;


/* 4. ANTIGÜEDAD CONSOLIDADA */
;WITH Base AS
(
    SELECT
        Dias = CASE
            WHEN e.FechaProduccion IS NULL THEN 0
            WHEN DATEDIFF(
                DAY,
                e.FechaProduccion,
                @Hasta
            ) < 0 THEN 0
            ELSE DATEDIFF(
                DAY,
                e.FechaProduccion,
                @Hasta
            )
        END
    FROM dbo.InventarioConteoEsperado e
    INNER JOIN #AlmacenesFiltrados af
        ON af.SesionId = e.SesionId
       AND af.AlmacenId = e.AlmacenId
),
Rangos AS
(
    SELECT '0-15 días' AS Rango, 1 AS Orden
    UNION ALL SELECT '16-30 días', 2
    UNION ALL SELECT '31-60 días', 3
    UNION ALL SELECT '61-90 días', 4
    UNION ALL SELECT 'Más de 90 días', 5
),
Conteo AS
(
    SELECT
        Rango = CASE
            WHEN Dias BETWEEN 0 AND 15
                THEN '0-15 días'
            WHEN Dias BETWEEN 16 AND 30
                THEN '16-30 días'
            WHEN Dias BETWEEN 31 AND 60
                THEN '31-60 días'
            WHEN Dias BETWEEN 61 AND 90
                THEN '61-90 días'
            ELSE 'Más de 90 días'
        END,
        Cantidad = COUNT(1)
    FROM Base
    GROUP BY CASE
        WHEN Dias BETWEEN 0 AND 15
            THEN '0-15 días'
        WHEN Dias BETWEEN 16 AND 30
            THEN '16-30 días'
        WHEN Dias BETWEEN 31 AND 60
            THEN '31-60 días'
        WHEN Dias BETWEEN 61 AND 90
            THEN '61-90 días'
        ELSE 'Más de 90 días'
    END
)
SELECT
    r.Rango,
    Cantidad = CONVERT(INT, ISNULL(c.Cantidad, 0))
FROM Rangos r
LEFT JOIN Conteo c
    ON c.Rango = r.Rango
ORDER BY r.Orden;


/* 5. SOLO RESUMEN DE INCIDENCIAS, NO LOS 18,000 REGISTROS */
;WITH Conteos AS
(
    SELECT
        Tipo = ISNULL(
            NULLIF(LTRIM(RTRIM(i.Tipo)), ''),
            'Incidencia'
        ),
        Cantidad = COUNT(1)
    FROM dbo.InventarioConteoIncidencia i
    INNER JOIN #AlmacenesFiltrados af
        ON af.SesionId = i.SesionId
       AND af.AlmacenId = i.AlmacenId
    GROUP BY ISNULL(
        NULLIF(LTRIM(RTRIM(i.Tipo)), ''),
        'Incidencia'
    )

    UNION ALL

    SELECT
        Tipo = CASE
            WHEN l.EsEsperado = 0
                THEN 'Producto sobrante'
            ELSE 'Producto mezclado'
        END,
        Cantidad = COUNT(1)
    FROM dbo.InventarioConteoLectura l
    INNER JOIN #AlmacenesFiltrados af
        ON af.SesionId = l.SesionId
       AND af.AlmacenId = l.AlmacenId
    WHERE l.EsEsperado = 0
       OR
       (
           l.EsEsperado = 1
           AND l.EsAlmacenCorrecto = 0
       )
    GROUP BY CASE
        WHEN l.EsEsperado = 0
            THEN 'Producto sobrante'
        ELSE 'Producto mezclado'
    END
)
SELECT
    Tipo,
    Cantidad = CONVERT(INT, SUM(Cantidad))
FROM Conteos
GROUP BY Tipo
ORDER BY SUM(Cantidad) DESC, Tipo;
";

            await using var cn = CrearConexion();
            await cn.OpenAsync(ct);

            using var grid = await cn.QueryMultipleAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Desde = desde,
                        Hasta = hasta,
                        Almacen = almacen,
                        PermitidosJson = permitidosJson
                    },
                    commandTimeout: 90,
                    cancellationToken: ct));

            var sesionAlmacenes =
                (await grid.ReadAsync<ReporteSesionAlmacenDbRow>())
                .ToList();

            var resumen =
                (await grid.ReadAsync<ReporteAlmacenRow>())
                .ToList();

            var stock =
                (await grid.ReadAsync<ReporteSkuRow>())
                .ToList();

            var antiguedad =
                (await grid.ReadAsync<AgingDbRow>())
                .ToList();

            var incidenciasResumen =
                (await grid.ReadAsync<ReporteIncidenciaResumenRow>())
                .ToList();

            /*
             * El pendiente ya está calculado en el resumen por almacén.
             * Reutilizarlo evita volver a recorrer todas las etiquetas
             * esperadas durante la carga inicial del reporte.
             */
            var totalNoLocalizado = Convert.ToInt32(
                Math.Round(
                    resumen.Sum(x => x.Pendientes),
                    0,
                    MidpointRounding.AwayFromZero));

            if (totalNoLocalizado > 0)
            {
                var existente = incidenciasResumen
                    .FirstOrDefault(x =>
                        string.Equals(
                            x.Tipo,
                            "Producto no localizado",
                            StringComparison.OrdinalIgnoreCase));

                if (existente == null)
                {
                    incidenciasResumen.Add(
                        new ReporteIncidenciaResumenRow
                        {
                            Tipo = "Producto no localizado",
                            Cantidad = totalNoLocalizado
                        });
                }
                else
                {
                    existente.Cantidad += totalNoLocalizado;
                }
            }

            incidenciasResumen = incidenciasResumen
                .OrderByDescending(x => x.Cantidad)
                .ThenBy(x => x.Tipo)
                .ToList();

            AplicarNombresReporte(
                resumen,
                stock,
                null,
                null,
                permitidos);

            var sesiones = CrearResumenSesiones(
                sesionAlmacenes,
                permitidos);

            var configurados = ObtenerAlmacenesConfigurados()
                .Where(x => permitidos.Contains(Norm(x.Id)))
                .ToDictionary(
                    x => Norm(x.Id),
                    x => x,
                    StringComparer.OrdinalIgnoreCase);

            var almacenFiltroMostrar = almacen == "ALL"
                ? "TODOS LOS ALMACENES PERMITIDOS"
                : configurados.TryGetValue(
                    almacen,
                    out var filtro)
                    ? ObtenerNombreMostrar(filtro)
                    : almacen;

            var totalEsperado =
                resumen.Sum(x => x.CajasIniciales);

            var totalContadas =
                resumen.Sum(x => x.Contadas);

            var totalLecturas =
                stock.Sum(x => x.Cantidad);

            var data = new ReporteHistoricoData
            {
                Generado = DateTime.Now,
                Desde = desde,
                Hasta = hasta,
                AlmacenFiltro = almacen,
                AlmacenFiltroMostrar = almacenFiltroMostrar,
                TotalSesiones = sesiones.Count,
                TotalEsperado = totalEsperado,
                TotalKgEsperados =
                    resumen.Sum(x => x.KgIniciales),
                TotalLecturas = totalLecturas,
                TotalContadas = totalContadas,
                TotalKgContados =
                    stock.Sum(x => x.Kg),
                TotalPendiente =
                    resumen.Sum(x => x.Pendientes),
                TotalIncidencias =
                    incidenciasResumen.Sum(x => x.Cantidad),
                AvanceGeneral = totalEsperado > 0
                    ? Math.Round(
                        totalContadas * 100m / totalEsperado,
                        2)
                    : 0m,
                Sesiones = sesiones,
                ResumenAlmacenes = resumen,
                StockPorSku = stock,
                Antiguedad = antiguedad,
                IncidenciasResumen = incidenciasResumen,
                Incidencias = new List<ReporteIncidenciaRow>(),
                Lecturas = new List<ReporteLecturaRow>(),
                AlmacenesDisponibles =
                    ObtenerAlmacenesDisponiblesReporte(permitidos)
            };

            return data;
        }

        private async Task<(
            List<ReporteIncidenciaRow> Rows,
            int Total)> ObtenerIncidenciasPaginaAsync(
                DateTime desde,
                DateTime hasta,
                string almacen,
                string tipo,
                int pagina,
                int tamanoPagina,
                HashSet<string> permitidos,
                CancellationToken ct)
        {
            var permitidosJson = JsonSerializer.Serialize(
                permitidos.OrderBy(x => x).ToList());

            var offset = Math.Max(
                0,
                (pagina - 1) * tamanoPagina);

            const string sql = @"
SET NOCOUNT ON;

DECLARE @HastaExclusivo DATETIME2 =
    DATEADD(DAY, 1, CAST(@Hasta AS date));

CREATE TABLE #Permitidos
(
    AlmacenId NVARCHAR(100) NOT NULL PRIMARY KEY
);

INSERT INTO #Permitidos (AlmacenId)
SELECT DISTINCT
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(100), value))))
FROM OPENJSON(@PermitidosJson)
WHERE NULLIF(
    LTRIM(RTRIM(CONVERT(NVARCHAR(100), value))),
    ''
) IS NOT NULL;

CREATE TABLE #AlmacenesFiltrados
(
    SesionId       INT            NOT NULL,
    AlmacenId      NVARCHAR(100)  NOT NULL,
    AlmacenNombre  NVARCHAR(300)  NOT NULL,
    PRIMARY KEY (SesionId, AlmacenId)
);

INSERT INTO #AlmacenesFiltrados
(
    SesionId,
    AlmacenId,
    AlmacenNombre
)
SELECT
    sa.SesionId,
    sa.AlmacenId,
    sa.AlmacenNombre
FROM dbo.InventarioConteoSesionAlmacen sa
INNER JOIN dbo.InventarioConteoSesion s
    ON s.Id = sa.SesionId
INNER JOIN #Permitidos p
    ON p.AlmacenId = sa.AlmacenId
WHERE s.FechaInicio >= @Desde
  AND s.FechaInicio < @HastaExclusivo
  AND
  (
      @Almacen = 'ALL'
      OR sa.AlmacenId = @Almacen
  );

;WITH Incidencias AS
(
    SELECT
        Id = CONVERT(BIGINT, i.Id),
        Origen = CAST('MANUAL' AS NVARCHAR(30)),
        i.SesionId,
        s.Folio,
        FechaSesion = s.FechaInicio,
        Fecha = i.FechaRegistro,
        Tipo = ISNULL(
            NULLIF(LTRIM(RTRIM(i.Tipo)), ''),
            'Incidencia'
        ),
        i.AlmacenId,
        Almacen = ISNULL(i.AlmacenNombre, ''),
        CodigoEtiqueta =
            ISNULL(i.CodigoEtiqueta, ''),
        Sku = CAST('' AS NVARCHAR(100)),
        Producto = ISNULL(i.Producto, ''),
        PesoKg = ISNULL(i.PesoKg, 0),
        Ubicacion = ISNULL(i.Ubicacion, ''),
        Comentario = ISNULL(i.Comentario, ''),
        FotoUrl = ISNULL(i.FotoUrl, ''),
        UsuarioRegistro =
            ISNULL(i.UsuarioRegistro, '')
    FROM dbo.InventarioConteoIncidencia i
    INNER JOIN #AlmacenesFiltrados af
        ON af.SesionId = i.SesionId
       AND af.AlmacenId = i.AlmacenId
    INNER JOIN dbo.InventarioConteoSesion s
        ON s.Id = i.SesionId

    UNION ALL

    SELECT
        Id = -CONVERT(BIGINT, l.Id),
        Origen = CAST('LECTURA' AS NVARCHAR(30)),
        l.SesionId,
        s.Folio,
        FechaSesion = s.FechaInicio,
        Fecha = l.FechaRegistro,
        Tipo = CASE
            WHEN l.EsEsperado = 0
                THEN 'Producto sobrante'
            ELSE 'Producto mezclado'
        END,
        l.AlmacenId,
        Almacen = ISNULL(l.AlmacenNombre, ''),
        l.CodigoEtiqueta,
        Sku = ISNULL(l.Sku, ''),
        Producto =
            ISNULL(l.Producto, l.Sku),
        PesoKg = ISNULL(l.PesoNeto, 0),
        Ubicacion =
            CAST('' AS NVARCHAR(300)),
        Comentario = CASE
            WHEN l.EsEsperado = 0
                THEN
                    'La etiqueta no formaba parte de la fotografía inicial.'
            ELSE
                    'Se contó en un almacén diferente al esperado.'
        END,
        FotoUrl =
            CAST('' AS NVARCHAR(500)),
        UsuarioRegistro =
            ISNULL(l.UsuarioRegistro, '')
    FROM dbo.InventarioConteoLectura l
    INNER JOIN #AlmacenesFiltrados af
        ON af.SesionId = l.SesionId
       AND af.AlmacenId = l.AlmacenId
    INNER JOIN dbo.InventarioConteoSesion s
        ON s.Id = l.SesionId
    WHERE l.EsEsperado = 0
       OR
       (
           l.EsEsperado = 1
           AND l.EsAlmacenCorrecto = 0
       )

    UNION ALL

    SELECT
        Id =
            -1000000000 -
            CONVERT(BIGINT, e.Id),
        Origen =
            CAST('PENDIENTE' AS NVARCHAR(30)),
        e.SesionId,
        s.Folio,
        FechaSesion = s.FechaInicio,
        Fecha = ISNULL(s.FechaCierre, @Hasta),
        Tipo =
            CAST(
                'Producto no localizado'
                AS NVARCHAR(100)),
        e.AlmacenId,
        Almacen = ISNULL(e.AlmacenNombre, ''),
        e.CodigoEtiqueta,
        Sku = ISNULL(e.Sku, ''),
        Producto =
            ISNULL(e.Producto, e.Sku),
        PesoKg = ISNULL(e.PesoNeto, 0),
        Ubicacion =
            CAST('' AS NVARCHAR(300)),
        Comentario =
            CAST(
                'La etiqueta de la fotografía inicial no fue localizada correctamente.'
                AS NVARCHAR(1000)),
        FotoUrl =
            CAST('' AS NVARCHAR(500)),
        UsuarioRegistro =
            CAST('' AS NVARCHAR(150))
    FROM dbo.InventarioConteoEsperado e
    INNER JOIN #AlmacenesFiltrados af
        ON af.SesionId = e.SesionId
       AND af.AlmacenId = e.AlmacenId
    INNER JOIN dbo.InventarioConteoSesion s
        ON s.Id = e.SesionId
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventarioConteoLectura l
        WHERE l.SesionId = e.SesionId
          AND l.CodigoEtiqueta = e.CodigoEtiqueta
          AND l.EsEsperado = 1
          AND l.EsAlmacenCorrecto = 1
    )
),
Filtradas AS
(
    SELECT *
    FROM Incidencias
    WHERE
        @Tipo = 'ALL'
        OR UPPER(LTRIM(RTRIM(Tipo))) = @Tipo
)
SELECT
    TotalRegistros = COUNT(1) OVER(),
    Id,
    Origen,
    SesionId,
    Folio,
    FechaSesion,
    Fecha,
    Tipo,
    AlmacenId,
    Almacen,
    CodigoEtiqueta,
    Sku,
    Producto,
    PesoKg,
    Ubicacion,
    Comentario,
    FotoUrl,
    UsuarioRegistro
FROM Filtradas
ORDER BY
    Fecha DESC,
    SesionId DESC,
    Id DESC
OFFSET @Offset ROWS
FETCH NEXT @TamanoPagina ROWS ONLY;
";

            await using var cn = CrearConexion();
            await cn.OpenAsync(ct);

            var rows = (
                await cn.QueryAsync<ReporteIncidenciaRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Desde = desde,
                            Hasta = hasta,
                            Almacen = almacen,
                            Tipo = tipo,
                            Offset = offset,
                            TamanoPagina = tamanoPagina,
                            PermitidosJson = permitidosJson
                        },
                        commandTimeout: 90,
                        cancellationToken: ct))
                ).ToList();

            AplicarNombresReporte(
                null,
                null,
                rows,
                null,
                permitidos);

            return (
                rows,
                rows.FirstOrDefault()?.TotalRegistros ?? 0);
        }

        private async Task<(
            List<ReporteLecturaRow> Rows,
            int Total)> ObtenerLecturasPaginaAsync(
                DateTime desde,
                DateTime hasta,
                string almacen,
                int pagina,
                int tamanoPagina,
                HashSet<string> permitidos,
                CancellationToken ct)
        {
            var permitidosJson = JsonSerializer.Serialize(
                permitidos.OrderBy(x => x).ToList());

            var offset = Math.Max(
                0,
                (pagina - 1) * tamanoPagina);

            const string sql = @"
SET NOCOUNT ON;

DECLARE @HastaExclusivo DATETIME2 =
    DATEADD(DAY, 1, CAST(@Hasta AS date));

CREATE TABLE #Permitidos
(
    AlmacenId NVARCHAR(100) NOT NULL PRIMARY KEY
);

INSERT INTO #Permitidos (AlmacenId)
SELECT DISTINCT
    UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(100), value))))
FROM OPENJSON(@PermitidosJson)
WHERE NULLIF(
    LTRIM(RTRIM(CONVERT(NVARCHAR(100), value))),
    ''
) IS NOT NULL;

CREATE TABLE #AlmacenesFiltrados
(
    SesionId       INT            NOT NULL,
    AlmacenId      NVARCHAR(100)  NOT NULL,
    AlmacenNombre  NVARCHAR(300)  NOT NULL,
    PRIMARY KEY (SesionId, AlmacenId)
);

INSERT INTO #AlmacenesFiltrados
(
    SesionId,
    AlmacenId,
    AlmacenNombre
)
SELECT
    sa.SesionId,
    sa.AlmacenId,
    sa.AlmacenNombre
FROM dbo.InventarioConteoSesionAlmacen sa
INNER JOIN dbo.InventarioConteoSesion s
    ON s.Id = sa.SesionId
INNER JOIN #Permitidos p
    ON p.AlmacenId = sa.AlmacenId
WHERE s.FechaInicio >= @Desde
  AND s.FechaInicio < @HastaExclusivo
  AND
  (
      @Almacen = 'ALL'
      OR sa.AlmacenId = @Almacen
  );

;WITH Base AS
(
    SELECT
        l.Id,
        l.SesionId,
        s.Folio,
        FechaSesion = s.FechaInicio,
        l.FechaRegistro,
        l.AlmacenId,
        Almacen = ISNULL(l.AlmacenNombre, ''),
        AlmacenEsperadoId =
            ISNULL(l.AlmacenEsperadoId, ''),
        AlmacenEsperado =
            ISNULL(exp.AlmacenNombre, ''),
        l.CodigoEtiqueta,
        Sku = ISNULL(l.Sku, ''),
        Producto =
            ISNULL(l.Producto, l.Sku),
        PesoNeto = ISNULL(l.PesoNeto, 0),
        l.FechaProduccion,
        Estado = CASE
            WHEN l.EsEsperado = 0
                THEN 'SOBRANTE'
            WHEN l.EsEsperado = 1
             AND l.EsAlmacenCorrecto = 0
                THEN 'MEZCLADO'
            ELSE 'CORRECTO'
        END,
        UsuarioRegistro =
            ISNULL(l.UsuarioRegistro, '')
    FROM dbo.InventarioConteoLectura l
    INNER JOIN #AlmacenesFiltrados af
        ON af.SesionId = l.SesionId
       AND af.AlmacenId = l.AlmacenId
    INNER JOIN dbo.InventarioConteoSesion s
        ON s.Id = l.SesionId
    OUTER APPLY
    (
        SELECT TOP (1)
            e.AlmacenNombre
        FROM dbo.InventarioConteoEsperado e
        WHERE e.SesionId = l.SesionId
          AND e.CodigoEtiqueta =
              l.CodigoEtiqueta
        ORDER BY e.Id DESC
    ) exp
)
SELECT
    TotalRegistros = COUNT(1) OVER(),
    Id,
    SesionId,
    Folio,
    FechaSesion,
    FechaRegistro,
    AlmacenId,
    Almacen,
    AlmacenEsperadoId,
    AlmacenEsperado,
    CodigoEtiqueta,
    Sku,
    Producto,
    PesoNeto,
    FechaProduccion,
    Estado,
    UsuarioRegistro
FROM Base
ORDER BY
    FechaRegistro DESC,
    Id DESC
OFFSET @Offset ROWS
FETCH NEXT @TamanoPagina ROWS ONLY;
";

            await using var cn = CrearConexion();
            await cn.OpenAsync(ct);

            var rows = (
                await cn.QueryAsync<ReporteLecturaRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Desde = desde,
                            Hasta = hasta,
                            Almacen = almacen,
                            Offset = offset,
                            TamanoPagina = tamanoPagina,
                            PermitidosJson = permitidosJson
                        },
                        commandTimeout: 90,
                        cancellationToken: ct))
                ).ToList();

            AplicarNombresReporte(
                null,
                null,
                null,
                rows,
                permitidos);

            return (
                rows,
                rows.FirstOrDefault()?.TotalRegistros ?? 0);
        }

        private async Task<List<ReporteIncidenciaRow>>
            ObtenerTodasIncidenciasAsync(
                DateTime desde,
                DateTime hasta,
                string almacen,
                HashSet<string> permitidos,
                int tamanoLote,
                CancellationToken ct)
        {
            var result = new List<ReporteIncidenciaRow>();
            var pagina = 1;

            while (true)
            {
                var lote = await ObtenerIncidenciasPaginaAsync(
                    desde,
                    hasta,
                    almacen,
                    "ALL",
                    pagina,
                    tamanoLote,
                    permitidos,
                    ct);

                result.AddRange(lote.Rows);

                if (
                    lote.Rows.Count == 0 ||
                    result.Count >= lote.Total
                )
                {
                    break;
                }

                pagina++;
            }

            return result;
        }

        private async Task<List<ReporteLecturaRow>>
            ObtenerTodasLecturasAsync(
                DateTime desde,
                DateTime hasta,
                string almacen,
                HashSet<string> permitidos,
                int tamanoLote,
                CancellationToken ct)
        {
            var result = new List<ReporteLecturaRow>();
            var pagina = 1;

            while (true)
            {
                var lote = await ObtenerLecturasPaginaAsync(
                    desde,
                    hasta,
                    almacen,
                    pagina,
                    tamanoLote,
                    permitidos,
                    ct);

                result.AddRange(lote.Rows);

                if (
                    lote.Rows.Count == 0 ||
                    result.Count >= lote.Total
                )
                {
                    break;
                }

                pagina++;
            }

            return result;
        }

        private List<ReporteSesionRow> CrearResumenSesiones(
            IReadOnlyCollection<ReporteSesionAlmacenDbRow> rows,
            HashSet<string> permitidos)
        {
            var configurados = ObtenerAlmacenesConfigurados()
                .Where(x => permitidos.Contains(Norm(x.Id)))
                .ToDictionary(
                    x => Norm(x.Id),
                    x => x,
                    StringComparer.OrdinalIgnoreCase);

            string NombreMostrar(
                string almacenId,
                string almacenNombre)
            {
                return configurados.TryGetValue(
                    Norm(almacenId),
                    out var item)
                        ? ObtenerNombreMostrar(item)
                        : almacenNombre;
            }

            return rows
                .GroupBy(x => new
                {
                    x.SesionId,
                    x.Folio,
                    x.FechaInicio,
                    x.FechaCierre,
                    x.Estatus,
                    x.UsuarioInicio,
                    x.UsuarioCierre
                })
                .Select(g => new ReporteSesionRow
                {
                    SesionId = g.Key.SesionId,
                    Folio = g.Key.Folio,
                    FechaInicio = g.Key.FechaInicio,
                    FechaCierre = g.Key.FechaCierre,
                    Estatus = g.Key.Estatus,
                    UsuarioInicio = g.Key.UsuarioInicio,
                    UsuarioCierre = g.Key.UsuarioCierre,
                    TotalAlmacenes = g
                        .Select(x => Norm(x.AlmacenId))
                        .Distinct()
                        .Count(),
                    Almacenes = string.Join(
                        " | ",
                        g.Select(x => NombreMostrar(
                                x.AlmacenId,
                                x.AlmacenNombre))
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x))
                })
                .OrderByDescending(x => x.FechaInicio)
                .ThenByDescending(x => x.SesionId)
                .ToList();
        }

        private List<object> ObtenerAlmacenesDisponiblesReporte(
            HashSet<string> permitidos)
        {
            return ObtenerAlmacenesConfigurados()
                .Where(x => permitidos.Contains(Norm(x.Id)))
                .OrderBy(x => ObtenerSucursal(x))
                .ThenBy(x => x.Name)
                .Select(x => (object)new
                {
                    id = x.Id,
                    nombre = x.Name,
                    nombreMostrar = ObtenerNombreMostrar(x),
                    sucursal = ObtenerSucursal(x),
                    planta = ObtenerPlantaPreferida(x)
                })
                .ToList();
        }

        private void AplicarNombresReporte(
            List<ReporteAlmacenRow>? resumen,
            List<ReporteSkuRow>? stock,
            List<ReporteIncidenciaRow>? incidencias,
            List<ReporteLecturaRow>? lecturas,
            HashSet<string> permitidos)
        {
            var configurados = ObtenerAlmacenesConfigurados()
                .Where(x => permitidos.Contains(Norm(x.Id)))
                .ToDictionary(
                    x => Norm(x.Id),
                    x => x,
                    StringComparer.OrdinalIgnoreCase);

            string NombreMostrar(
                string almacenId,
                string almacenNombre)
            {
                return configurados.TryGetValue(
                    Norm(almacenId),
                    out var item)
                        ? ObtenerNombreMostrar(item)
                        : almacenNombre;
            }

            string Sucursal(string almacenId)
            {
                return configurados.TryGetValue(
                    Norm(almacenId),
                    out var item)
                        ? ObtenerSucursal(item)
                        : "";
            }

            string Planta(string almacenId)
            {
                return configurados.TryGetValue(
                    Norm(almacenId),
                    out var item)
                        ? ObtenerPlantaPreferida(item)
                        : "";
            }

            if (resumen != null)
            {
                foreach (var item in resumen)
                {
                    item.AlmacenMostrar = NombreMostrar(
                        item.AlmacenId,
                        item.Almacen);

                    item.Sucursal = Sucursal(item.AlmacenId);
                    item.Planta = Planta(item.AlmacenId);
                }
            }

            if (stock != null)
            {
                foreach (var item in stock)
                {
                    item.AlmacenMostrar = NombreMostrar(
                        item.AlmacenId,
                        item.Almacen);

                    item.Sucursal = Sucursal(item.AlmacenId);
                    item.Planta = Planta(item.AlmacenId);
                }
            }

            if (incidencias != null)
            {
                foreach (var item in incidencias)
                {
                    item.AlmacenMostrar = NombreMostrar(
                        item.AlmacenId,
                        item.Almacen);

                    item.Sucursal = Sucursal(item.AlmacenId);
                    item.Planta = Planta(item.AlmacenId);
                }
            }

            if (lecturas != null)
            {
                foreach (var item in lecturas)
                {
                    item.AlmacenMostrar = NombreMostrar(
                        item.AlmacenId,
                        item.Almacen);

                    item.Sucursal = Sucursal(item.AlmacenId);
                    item.Planta = Planta(item.AlmacenId);

                    item.AlmacenEsperadoMostrar =
                        string.IsNullOrWhiteSpace(
                            item.AlmacenEsperadoId)
                            ? ""
                            : NombreMostrar(
                                item.AlmacenEsperadoId,
                                item.AlmacenEsperado);
                }
            }
        }

        private byte[] GenerarReporteHistoricoPdf(
            ReporteHistoricoData reporte)
        {
            EnsurePdfSharpFontResolver();

            using var document = new PdfDocument();

            document.Info.Title = "Reporte histórico de inventario";
            document.Info.Author = "Carnes G";
            document.Info.Subject =
                $"Inventario {reporte.Desde:dd/MM/yyyy} - " +
                $"{reporte.Hasta:dd/MM/yyyy}";

            var titleFont = new XFont("InventarioSans", 15);
            var sectionFont = new XFont("InventarioSans", 11);
            var normalFont = new XFont("InventarioSans", 8);
            var smallFont = new XFont("InventarioSans", 7);

            PdfPage? page = null;
            XGraphics? graphics = null;
            double y = 0;
            int pageNumber = 0;

            const double left = 34;
            const double right = 34;
            const double top = 34;
            const double bottom = 34;
            const double lineHeight = 11;

            double ContentWidth()
            {
                if (page == null) return 0;
                return page.Width.Point - left - right;
            }

            void DrawFooter()
            {
                if (graphics == null || page == null) return;

                graphics.DrawString(
                    $"Página {pageNumber}",
                    smallFont,
                    XBrushes.Gray,
                    new XRect(
                        left,
                        page.Height.Point - 22,
                        ContentWidth(),
                        12),
                    XStringFormats.TopRight);
            }

            void NewPage()
            {
                if (graphics != null)
                {
                    DrawFooter();
                    graphics.Dispose();
                }

                page = document.AddPage();
                graphics = XGraphics.FromPdfPage(page);
                pageNumber++;
                y = top;
            }

            List<string> WrapText(
                string? value,
                XFont font,
                double width)
            {
                var text = (value ?? "").Replace(
                    "\r",
                    " ").Replace(
                    "\n",
                    " ").Trim();

                if (string.IsNullOrWhiteSpace(text))
                {
                    return new List<string> { "" };
                }

                var words = text.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

                var lines = new List<string>();
                var current = "";

                foreach (var word in words)
                {
                    var candidate = string.IsNullOrWhiteSpace(current)
                        ? word
                        : current + " " + word;

                    if (
                        graphics != null &&
                        graphics.MeasureString(
                            candidate,
                            font).Width <= width
                    )
                    {
                        current = candidate;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(current))
                    {
                        lines.Add(current);
                    }

                    current = word;
                }

                if (!string.IsNullOrWhiteSpace(current))
                {
                    lines.Add(current);
                }

                return lines.Count > 0
                    ? lines
                    : new List<string> { "" };
            }

            void EnsureSpace(double required)
            {
                if (page == null || graphics == null)
                {
                    NewPage();
                    return;
                }

                if (y + required > page.Height.Point - bottom)
                {
                    NewPage();
                }
            }

            void WriteText(
                string? value,
                XFont font,
                XBrush brush,
                double indent = 0,
                double spacingAfter = 2)
            {
                EnsureSpace(lineHeight);

                var width = Math.Max(
                    40,
                    ContentWidth() - indent);

                foreach (var line in WrapText(value, font, width))
                {
                    EnsureSpace(lineHeight);

                    graphics!.DrawString(
                        line,
                        font,
                        brush,
                        new XRect(
                            left + indent,
                            y,
                            width,
                            lineHeight),
                        XStringFormats.TopLeft);

                    y += lineHeight;
                }

                y += spacingAfter;
            }

            void WriteSection(string title)
            {
                EnsureSpace(28);
                y += 5;

                graphics!.DrawLine(
                    XPens.LightGray,
                    left,
                    y,
                    left + ContentWidth(),
                    y);

                y += 6;

                WriteText(
                    title,
                    sectionFont,
                    XBrushes.DarkRed,
                    0,
                    4);
            }

            NewPage();

            WriteText(
                "REPORTE HISTÓRICO DE INVENTARIO",
                titleFont,
                XBrushes.DarkRed,
                0,
                5);

            WriteText(
                $"Generado: {reporte.Generado:dd/MM/yyyy HH:mm:ss}",
                normalFont,
                XBrushes.Black);

            WriteText(
                $"Periodo de sesiones: " +
                $"{reporte.Desde:dd/MM/yyyy} al " +
                $"{reporte.Hasta:dd/MM/yyyy}",
                normalFont,
                XBrushes.Black);

            WriteText(
                $"Almacén: {reporte.AlmacenFiltroMostrar}",
                normalFont,
                XBrushes.Black);

            WriteSection("RESUMEN GENERAL");

            WriteText(
                $"Sesiones: {reporte.TotalSesiones:N0} | " +
                $"Cajas iniciales: {reporte.TotalEsperado:N0} | " +
                $"Lecturas: {reporte.TotalLecturas:N0} | " +
                $"Correctas: {reporte.TotalContadas:N0} | " +
                $"Pendientes: {reporte.TotalPendiente:N0}",
                normalFont,
                XBrushes.Black);

            WriteText(
                $"Kg iniciales: {reporte.TotalKgEsperados:N3} | " +
                $"Kg escaneados: {reporte.TotalKgContados:N3} | " +
                $"Incidencias: {reporte.TotalIncidencias:N0} | " +
                $"Avance general: {reporte.AvanceGeneral:N2}%",
                normalFont,
                XBrushes.Black);

            WriteSection("AVANCE POR ALMACÉN");

            foreach (var item in reporte.ResumenAlmacenes)
            {
                WriteText(
                    $"{item.AlmacenMostrar} | " +
                    $"Sesiones {item.Sesiones:N0} | " +
                    $"Inicial {item.CajasIniciales:N0} cajas / " +
                    $"{item.KgIniciales:N3} kg | " +
                    $"Contadas {item.Contadas:N0} / " +
                    $"{item.KgContados:N3} kg | " +
                    $"Pendientes {item.Pendientes:N0} | " +
                    $"Avance {item.Avance:N2}% | " +
                    $"Sobrantes {item.Sobrantes:N0} | " +
                    $"Mezcladas {item.Mezcladas:N0} | " +
                    $"Manuales {item.IncidenciasManuales:N0}",
                    normalFont,
                    XBrushes.Black,
                    5,
                    3);
            }

            WriteSection("ANTIGÜEDAD");

            foreach (var item in reporte.Antiguedad)
            {
                WriteText(
                    $"{item.Rango}: {item.Cantidad:N0} etiquetas",
                    normalFont,
                    XBrushes.Black,
                    5,
                    1);
            }

            WriteSection("STOCK CONTADO POR SKU");

            foreach (var item in reporte.StockPorSku)
            {
                WriteText(
                    $"{item.AlmacenMostrar} | " +
                    $"{item.Sku} | {item.Producto} | " +
                    $"{item.Cantidad:N0} cajas | {item.Kg:N3} kg",
                    normalFont,
                    XBrushes.Black,
                    5,
                    2);
            }

            WriteSection("SESIONES");

            foreach (var item in reporte.Sesiones)
            {
                WriteText(
                    $"{item.Folio} | {item.Estatus} | " +
                    $"Inicio {item.FechaInicio:dd/MM/yyyy HH:mm} | " +
                    $"Cierre " +
                    $"{(item.FechaCierre.HasValue ? item.FechaCierre.Value.ToString("dd/MM/yyyy HH:mm") : "ABIERTA")} | " +
                    $"Usuario {item.UsuarioInicio} | " +
                    $"{item.Almacenes}",
                    normalFont,
                    XBrushes.Black,
                    5,
                    3);
            }

            WriteSection("INCIDENCIAS");

            foreach (var item in reporte.Incidencias)
            {
                WriteText(
                    $"{item.Fecha:dd/MM/yyyy HH:mm} | " +
                    $"{item.Folio} | {item.Tipo} | " +
                    $"{item.AlmacenMostrar}",
                    normalFont,
                    XBrushes.DarkRed,
                    5,
                    1);

                WriteText(
                    $"Etiqueta: {item.CodigoEtiqueta} | " +
                    $"SKU: {item.Sku} | " +
                    $"Producto: {item.Producto} | " +
                    $"Peso: {item.PesoKg:N3} kg | " +
                    $"Ubicación: {item.Ubicacion}",
                    smallFont,
                    XBrushes.Black,
                    14,
                    1);

                WriteText(
                    $"Comentario: {item.Comentario} | " +
                    $"Usuario: {item.UsuarioRegistro} | " +
                    $"Foto: {item.FotoUrl}",
                    smallFont,
                    XBrushes.Gray,
                    14,
                    3);
            }

            WriteSection("DETALLE COMPLETO DE LECTURAS");

            foreach (var item in reporte.Lecturas)
            {
                WriteText(
                    $"{item.FechaRegistro:dd/MM/yyyy HH:mm:ss} | " +
                    $"{item.Folio} | {item.Estado} | " +
                    $"{item.AlmacenMostrar}",
                    normalFont,
                    item.Estado == "CORRECTO"
                        ? XBrushes.DarkGreen
                        : XBrushes.DarkRed,
                    5,
                    1);

                WriteText(
                    $"Etiqueta: {item.CodigoEtiqueta} | " +
                    $"SKU: {item.Sku} | " +
                    $"Producto: {item.Producto} | " +
                    $"{item.PesoNeto:N3} kg | " +
                    $"Esperado: {item.AlmacenEsperadoMostrar} | " +
                    $"Usuario: {item.UsuarioRegistro}",
                    smallFont,
                    XBrushes.Black,
                    14,
                    3);
            }

            if (graphics != null)
            {
                DrawFooter();
                graphics.Dispose();
            }

            using var stream = new MemoryStream();
            document.Save(stream);
            return stream.ToArray();
        }

        // =============================================================
        //  CERRAR SESIÓN
        // =============================================================
        [HttpPost]
        public async Task<IActionResult> CerrarSesion(
            [FromBody] CerrarSesionRequest request,
            CancellationToken ct = default)
        {
            if (request == null || request.SesionId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "La sesión es obligatoria."
                });
            }

            await using var cn = CrearConexion();
            await cn.OpenAsync(ct);

            var (sesion, almacenes) = await ObtenerSesionAsync(
                cn,
                request.SesionId,
                ct);

            if (sesion == null)
            {
                return NotFound(new
                {
                    ok = false,
                    message = "No se encontró la sesión."
                });
            }

            if (!await UsuarioPuedeConsultarSesionAsync(almacenes, ct))
            {
                return StatusCode(403, new
                {
                    ok = false,
                    message = "No tienes permiso para cerrar esta sesión."
                });
            }

            const string sql = @"
UPDATE dbo.InventarioConteoSesion
SET
    Estatus = 'CERRADO',
    FechaCierre = SYSDATETIME(),
    UsuarioCierre = @UsuarioCierre
WHERE Id = @SesionId
  AND Estatus = 'ABIERTO';";

            var updated = await cn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        SesionId = request.SesionId,
                        UsuarioCierre = UsuarioActual()
                    },
                    cancellationToken: ct));

            return Ok(new
            {
                ok = true,
                cerrada = updated > 0
            });
        }
    }
}