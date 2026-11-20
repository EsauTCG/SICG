using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using Plataforma_CG.ViewModels;
using System.Data;
using System.Text.Json;

namespace Plataforma_CG.Controllers
{
    [Authorize]
    public class SurtidoController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuracion;

        public SurtidoController(
            AppDbContext context,
            IConfiguration configuracion)
        {
            _context = context;
            _configuracion = configuracion;
        }


        // Control de intentos por login+IP en la instancia IIS. Para una granja
        // de servidores debe sustituirse por rate limit distribuido.
        private sealed class SurtidoIntentosClave
        {
            public int Fallos { get; set; }
            public DateTime InicioUtc { get; set; }
        }
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SurtidoIntentosClave>
            _intentosSupervisor = new();
        private static readonly object _lockIntentosSupervisor = new();
        private static bool AutorizadorBloqueado(string clave)
        {
            lock (_lockIntentosSupervisor)
            {
                if (!_intentosSupervisor.TryGetValue(clave, out var e)) return false;
                if (DateTime.UtcNow - e.InicioUtc > TimeSpan.FromMinutes(15))
                {
                    _intentosSupervisor.TryRemove(clave, out _);
                    return false;
                }
                return e.Fallos >= 5;
            }
        }
        private static void RegistrarFalloSupervisor(string clave)
        {
            lock (_lockIntentosSupervisor)
            {
                if (!_intentosSupervisor.TryGetValue(clave, out var e) ||
                   DateTime.UtcNow - e.InicioUtc > TimeSpan.FromMinutes(15))
                    _intentosSupervisor[clave] = new SurtidoIntentosClave { Fallos = 1, InicioUtc = DateTime.UtcNow };
                else e.Fallos++;
            }
        }
        private static void ReiniciarFalloSupervisor(string clave)
        {
            lock (_lockIntentosSupervisor) _intentosSupervisor.TryRemove(clave, out _);
        }

        // ============================================================
        // CONFIGURACIÓN DE ALMACÉN LEÍDA DESDE appsettings.json
        // ============================================================
        private sealed class WarehouseConfig
        {
            public string Id { get; set; } = "";

            public string Name { get; set; } = "";

            public string Sucursal { get; set; } = "";
        }


        private sealed class SurtidoAlmacenReglaDto
        {
            public string CodigoAlmacen { get; set; } = "";

            public bool ObligaUbicacion { get; set; }
        }


        // ============================================================
        // DTO INTERNO PARA COUNT_BIG POR ARTÍCULO
        // Evita Dictionary<string, dynamic>.
        // ============================================================
        private sealed class SurtidoConteoArticuloDto
        {
            public string Articulo { get; set; } = "";

            public long Cajas { get; set; }
        }


        private sealed class SurtidoPedidoPrioridadDto
        {
            public int SolicitudSurtidoId { get; set; }

            public int Prioridad { get; set; }
        }


        private sealed class SurtidoPedidoPrioridadMasivaDto
        {
            public string Planta { get; set; } = "";

            public string CodigoAlmacen { get; set; } = "";

            public int SolicitudSurtidoId { get; set; }

            public int Prioridad { get; set; }
        }



        // ============================================================
        // UBICACIÓN DINÁMICA POR ALMACÉN / MASTER / COLOR
        //
        // IMPORTANTE:
        // - El controller NO conoce colores fijos.
        // - El controller NO construye códigos de ubicación.
        // - Colores, ubicaciones, racks, posiciones, alturas y orden físico
        //   se administran en tablas de SIGO por CodigoAlmacen.
        // - La rotación sólo decide cómo recorrer OrdenFisico.
        // ============================================================
        private sealed class UbicacionDinamicaArticuloDto
        {
            public string ProductoCodigo { get; set; } = "";
            public string ProductoNombre { get; set; } = "";
            public decimal? Rotacion { get; set; }
            public string Master { get; set; } = "";
        }


        private sealed class UbicacionDinamicaUnidadDto
        {
            public long ProduccionId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
            public string Articulo { get; set; } = "";
            public long? TarimaId { get; set; }
            public string TarimaCodigo { get; set; } = "";
        }


        private sealed class UbicacionDinamicaLayoutDto
        {
            public int LayoutId { get; set; }
            public string CodigoAlmacen { get; set; } = "";
            public string CodigoLayout { get; set; } = "";
            public string NombreLayout { get; set; } = "";
        }


        private sealed class UbicacionDinamicaColorDto
        {
            public int LayoutId { get; set; }
            public int ColorAlmacenId { get; set; }
            public string CodigoAlmacen { get; set; } = "";
            public string CodigoColor { get; set; } = "";
            public string NombreColor { get; set; } = "";
            public string HexColor { get; set; } = "";
        }


        private sealed class UbicacionDinamicaCatalogoDto
        {
            public int LayoutId { get; set; }
            public int UbicacionId { get; set; }
            public string CodigoAlmacen { get; set; } = "";
            public int ColorAlmacenId { get; set; }
            public string CodigoColor { get; set; } = "";
            public string NombreColor { get; set; } = "";
            public string HexColor { get; set; } = "";
            public string Rack { get; set; } = "";
            public string Posicion { get; set; } = "";
            public string Altura { get; set; } = "";
            public string CodigoUbicacion { get; set; } = "";
            public string CodigoMapa3D { get; set; } = "";
            public int OrdenFisico { get; set; }
        }


        private sealed class UbicacionDinamicaReglaRotacionDto
        {
            public int Id { get; set; }
            public int LayoutId { get; set; }
            public string CodigoAlmacen { get; set; } = "";
            public string NombreRegla { get; set; } = "";
            public decimal? Desde { get; set; }
            public decimal? Hasta { get; set; }
            public bool AplicaSinRotacion { get; set; }
            public string ModoOrden { get; set; } = "";
            public int Prioridad { get; set; }
        }


        private sealed class UbicacionDinamicaProduccionDto
        {
            public long ProduccionId { get; set; }
            public string CodigoUbicacion { get; set; } = "";
        }


        private sealed class UbicacionDinamicaAsignacionActualDto
        {
            public long Id { get; set; }
            public int LayoutId { get; set; }
            public int UbicacionId { get; set; }
            public int ColorAlmacenId { get; set; }
            public string CodigoColor { get; set; } = "";
            public string NombreColor { get; set; } = "";
            public string HexColor { get; set; } = "";
            public string CodigoUbicacion { get; set; } = "";
            public string CodigoMapa3D { get; set; } = "";
        }


        private sealed class UbicacionDinamicaRecomendacionDto
        {
            public int LayoutId { get; set; }
            public string CodigoLayout { get; set; } = "";
            public string NombreLayout { get; set; } = "";
            public string UnidadCodigo { get; set; } = "";
            public string Articulo { get; set; } = "";
            public string ProductoNombre { get; set; } = "";
            public string Master { get; set; } = "";
            public decimal? Rotacion { get; set; }

            public int ColorAlmacenId { get; set; }
            public string CodigoColor { get; set; } = "";
            public string NombreColor { get; set; } = "";
            public string HexColor { get; set; } = "";

            public string NombreReglaRotacion { get; set; } = "";
            public string ModoOrden { get; set; } = "";

            public int UbicacionId { get; set; }
            public string CodigoUbicacion { get; set; } = "";
            public string CodigoMapa3D { get; set; } = "";

            public bool YaUbicada { get; set; }

            public List<UbicacionDinamicaUnidadDto> Producciones { get; set; } =
                new();
        }


        // ============================================================
        // DTO INTERNO DEL USUARIO
        // ============================================================
        private sealed class UsuarioSurtidoDto
        {
            public int Id { get; set; }

            public string Usuario { get; set; } = "";

            public string Nombre { get; set; } = "";

            public string AlmacenesPermitidos { get; set; } = "";

            public bool LogisticaMontacarguista { get; set; }

            public bool LogisticaCapturista { get; set; }

            public bool LogisticaUbicador { get; set; }

            public bool LogisticaCoordinador { get; set; }
        }


        // ============================================================
        // NORMALIZAR LOGIN
        // ============================================================
        private static (
            string raw,
            string username,
            string usernameEmail)
            NormalizeLogin(string? identityName)
        {
            var raw =
                (identityName ?? string.Empty).Trim();

            var username =
                raw.Contains("\\")
                    ? raw.Split("\\").Last()
                    : raw;

            var usernameEmail =
                username.Contains("@")
                    ? username
                    : $"{username}@carnesg.net";

            return (
                raw,
                username,
                usernameEmail
            );
        }


        // ============================================================
        // CONEXIÓN A LA BD PRINCIPAL DE SIGO
        // ============================================================
        private async Task<IDbConnection> ObtenerConexionSigoAsync(
            CancellationToken ct)
        {
            var conn =
                _context.Database.GetDbConnection();

            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(ct);
            }

            return conn;
        }


        // ============================================================
        // USUARIO ACTUAL
        // ============================================================
        private async Task<UsuarioSurtidoDto?> ObtenerUsuarioActualAsync(
            CancellationToken ct = default)
        {
            var (raw, username, usernameEmail) =
                NormalizeLogin(
                    User?.Identity?.Name
                );

            if (string.IsNullOrWhiteSpace(raw) &&
                string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var conn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT TOP 1
    Id                      = u.Id,
    Usuario                 = ISNULL(u.Usuario, ''),
    Nombre                  = ISNULL(u.Nombre, ''),
    AlmacenesPermitidos     = ISNULL(u.AlmacenesPermitidos, ''),

    LogisticaMontacarguista = ISNULL(u.LogisticaMontacarguista, 0),
    LogisticaCapturista     = ISNULL(u.LogisticaCapturista, 0),
    LogisticaUbicador       = ISNULL(u.LogisticaUbicador, 0),
    LogisticaCoordinador    = ISNULL(u.LogisticaCoordinador, 0)

FROM dbo.UsuarioSQL u

WHERE u.Activo = 1
  AND
  (
         u.Usuario = @Raw
      OR u.Usuario = @Username
      OR u.Usuario = @UsernameEmail
      OR u.Nombre  = @Raw
      OR u.Nombre  = @Username
  )

ORDER BY
    CASE
        WHEN u.Usuario = @Raw THEN 0
        WHEN u.Usuario = @Username THEN 1
        WHEN u.Usuario = @UsernameEmail THEN 2
        ELSE 3
    END;
";

            return await conn
                .QueryFirstOrDefaultAsync<UsuarioSurtidoDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Raw = raw,
                            Username = username,
                            UsernameEmail = usernameEmail
                        },
                        cancellationToken: ct
                    )
                );
        }


        // ============================================================
        // PLANTAS AUTORIZADAS DESDE UsuarioSerie -> Series
        //
        // Planta1 / PLANTA 1 -> P1
        // TIF                -> TIF
        // ============================================================
        private async Task<List<string>> ObtenerPlantasUsuarioAsync(
            int usuarioId,
            CancellationToken ct = default)
        {
            var conn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT DISTINCT
    NombreSerie = ISNULL(s.NombreSerie, '')

FROM dbo.UsuarioSerie us

INNER JOIN dbo.Series s
    ON s.Id = us.SerieId

WHERE us.UsuarioId = @UsuarioId

ORDER BY NombreSerie;
";

            var nombres =
                (
                    await conn.QueryAsync<string>(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                UsuarioId = usuarioId
                            },
                            cancellationToken: ct
                        )
                    )
                )
                .ToList();

            var plantas =
                new List<string>();

            foreach (var valor in nombres)
            {
                var serie =
                    (valor ?? "")
                    .Trim()
                    .ToUpperInvariant();

                if (serie == "PLANTA1" ||
                    serie == "PLANTA 1" ||
                    serie == "P1")
                {
                    if (!plantas.Contains("P1"))
                    {
                        plantas.Add("P1");
                    }

                    continue;
                }

                if (serie == "TIF" ||
                    serie == "TIF 776" ||
                    serie.Contains("TIF"))
                {
                    if (!plantas.Contains("TIF"))
                    {
                        plantas.Add("TIF");
                    }
                }
            }

            return plantas;
        }


        // ============================================================
        // CATÁLOGO REAL DE WAREHOUSES DEL appsettings.json
        // ============================================================
        private List<WarehouseConfig> ObtenerWarehousesConfigurados()
        {
            return _configuracion
                .GetSection("Warehouses")
                .Get<List<WarehouseConfig>>()
                ?? new List<WarehouseConfig>();
        }


        // ============================================================
        // ALIAS PARA CÓDIGOS ANTIGUOS / DIFERENCIAS DE ESCRITURA
        //
        // En configuraciones anteriores se observó TIFRE.
        // En tu appsettings actual el Id es TIFFRE.
        // ============================================================
        private static string NormalizarCodigoAlmacen(
            string? codigo)
        {
            var valor =
                (codigo ?? "")
                .Trim()
                .ToUpperInvariant();

            return valor switch
            {
                "TIFRE" => "TIFFRE",
                _ => valor
            };
        }


        // ============================================================
        // PARSEAR UsuarioSQL.AlmacenesPermitidos
        // Ejemplo: ["3","CNT","6","7","VL","TIFPIE","TIFFRE","TIFCED"]
        // ============================================================
        private static List<string> ParsearAlmacenesPermitidos(
            string? almacenesJson)
        {
            if (string.IsNullOrWhiteSpace(almacenesJson))
            {
                return new List<string>();
            }

            try
            {
                var data =
                    JsonSerializer.Deserialize<List<string>>(
                        almacenesJson
                    );

                if (data != null)
                {
                    return data
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x)
                        )
                        .Select(NormalizarCodigoAlmacen)
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase
                        )
                        .ToList();
                }
            }
            catch
            {
                // Compatibilidad con registros viejos que no sean JSON.
            }

            return almacenesJson
                .Replace("[", "")
                .Replace("]", "")
                .Replace("\"", "")
                .Split(
                    new[] { ',', ';', '|' },
                    StringSplitOptions.RemoveEmptyEntries
                )
                .Select(NormalizarCodigoAlmacen)
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x)
                )
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();
        }


        // ============================================================
        // SUCURSAL DEL appsettings -> PLANTA LÓGICA
        //
        // PLANTA 1 -> P1 -> CadenaMeatP1
        // TIF 776  -> TIF -> CadenaMeatTIF
        // ============================================================
        private static string ResolverPlantaDesdeSucursal(
            string? sucursal)
        {
            var valor =
                (sucursal ?? "")
                .Trim()
                .ToUpperInvariant();

            if (valor == "PLANTA 1" ||
                valor == "PLANTA1" ||
                valor == "P1")
            {
                return "P1";
            }

            if (valor == "TIF 776" ||
                valor == "TIF776" ||
                valor == "TIF")
            {
                return "TIF";
            }

            return "POR DEFINIR";
        }


        // ============================================================
        // CLASIFICACIÓN OPERATIVA
        //
        // OJO:
        // Planta y clasificación NO son lo mismo.
        //
        // Ejemplo:
        // VL = RETENCION TIF 805
        // Sucursal = PLANTA 1
        //
        // Por lo tanto:
        // Planta = P1
        // Clasificación = TIF
        // Conexión = CadenaMeatP1
        // ============================================================
        private static string ResolverClasificacion(
            WarehouseConfig config)
        {
            var nombre =
                (config.Name ?? "")
                .Trim()
                .ToUpperInvariant();

            if (nombre.Contains("NO TIF"))
            {
                return "NO_TIF";
            }

            if (nombre.Contains("TIF"))
            {
                return "TIF";
            }

            return "OPERATIVO";
        }


        // ============================================================
        // RESOLVER UN CÓDIGO REAL DE ALMACÉN
        // ============================================================
        private SurtidoAlmacenVM ResolverAlmacen(
            string codigo)
        {
            var codigoN =
                NormalizarCodigoAlmacen(
                    codigo
                );

            var config =
                ObtenerWarehousesConfigurados()
                    .FirstOrDefault(x =>
                        string.Equals(
                            NormalizarCodigoAlmacen(x.Id),
                            codigoN,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

            if (config == null)
            {
                return new SurtidoAlmacenVM
                {
                    Codigo = codigoN,
                    Nombre = codigoN,
                    Sucursal = "NO CONFIGURADO",
                    Planta = "POR DEFINIR",
                    Clasificacion = "PENDIENTE",
                    TieneLayout3D = false
                };
            }

            var planta =
                ResolverPlantaDesdeSucursal(
                    config.Sucursal
                );

            return new SurtidoAlmacenVM
            {
                Codigo =
                    NormalizarCodigoAlmacen(
                        config.Id
                    ),

                Nombre =
                    config.Name ?? config.Id,

                Sucursal =
                    config.Sucursal ?? "",

                Planta =
                    planta,

                Clasificacion =
                    ResolverClasificacion(
                        config
                    ),

                // Layout físico 3D confirmado para el CEDIS TIF 776.
                // Los demás almacenes conservan vista 3D enfocada al rack,
                // pero no se dibuja un layout completo inventado.
                TieneLayout3D =
                    string.Equals(
                        NormalizarCodigoAlmacen(config.Id),
                        "TIFCED",
                        StringComparison.OrdinalIgnoreCase
                    )
            };
        }


        // ============================================================
        // ALMACENES DEL USUARIO
        //
        // 1. UsuarioSQL.AlmacenesPermitidos
        // 2. appsettings.Warehouses
        // 3. UsuarioSerie -> Series
        //
        // Se toma la intersección de permisos.
        // ============================================================
        private List<SurtidoAlmacenVM> ConstruirAlmacenesUsuario(
            UsuarioSurtidoDto usuario,
            IReadOnlyCollection<string> plantasPermitidas)
        {
            var codigos =
                ParsearAlmacenesPermitidos(
                    usuario.AlmacenesPermitidos
                );

            var almacenes =
                codigos
                    .Select(ResolverAlmacen)
                    .ToList();

            // Si existe configuración de series, sólo dejamos
            // almacenes cuya sucursal/planta esté autorizada.
            if (plantasPermitidas.Count > 0)
            {
                almacenes =
                    almacenes
                        .Where(a =>
                            a.Planta == "POR DEFINIR"
                            ||
                            plantasPermitidas.Contains(
                                a.Planta,
                                StringComparer.OrdinalIgnoreCase
                            )
                        )
                        .ToList();
            }

            return almacenes
                .OrderBy(x =>
                    x.Planta
                )
                .ThenBy(x =>
                    x.Nombre
                )
                .ToList();
        }



        // ============================================================
        // APLICAR REGLAS DE UBICACIÓN CONTROLADAS POR SIGO
        //
        // Fuente:
        // dbo.SurtidoAlmacenConfiguracion
        //
        // Si no existe configuración:
        // - TieneConfiguracionUbicacion = false
        // - ObligaUbicacion = false
        // ============================================================
        private async Task AplicarConfiguracionUbicacionAsync(
            List<SurtidoAlmacenVM> almacenes,
            CancellationToken ct = default)
        {
            if (almacenes == null || almacenes.Count == 0)
            {
                return;
            }

            var codigos =
                almacenes
                    .Select(x =>
                        NormalizarCodigoAlmacen(x.Codigo)
                    )
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x)
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            if (codigos.Count == 0)
            {
                return;
            }

            var conn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    CodigoAlmacen =
        UPPER(
            LTRIM(
                RTRIM(
                    ISNULL(CodigoAlmacen, '')
                )
            )
        ),

    ObligaUbicacion =
        ISNULL(
            ObligaUbicacion,
            0
        )

FROM dbo.SurtidoAlmacenConfiguracion WITH (NOLOCK)

WHERE Activo = 1
  AND CodigoAlmacen IN @Codigos;
";

            var reglas =
                (
                    await conn.QueryAsync<SurtidoAlmacenReglaDto>(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                Codigos =
                                    codigos
                            },
                            cancellationToken: ct
                        )
                    )
                )
                .ToList();

            var mapa =
                reglas
                    .GroupBy(
                        x =>
                            NormalizarCodigoAlmacen(
                                x.CodigoAlmacen
                            ),
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToDictionary(
                        g => g.Key,
                        g => g.First(),
                        StringComparer.OrdinalIgnoreCase
                    );

            foreach (var almacen in almacenes)
            {
                var codigo =
                    NormalizarCodigoAlmacen(
                        almacen.Codigo
                    );

                if (mapa.TryGetValue(
                        codigo,
                        out var regla))
                {
                    almacen.TieneConfiguracionUbicacion =
                        true;

                    almacen.ObligaUbicacion =
                        regla.ObligaUbicacion;
                }
                else
                {
                    almacen.TieneConfiguracionUbicacion =
                        false;

                    almacen.ObligaUbicacion =
                        false;
                }
            }
        }


        // ============================================================
        // COMPLETAR NOMBRE Y REGLA DE ALMACÉN EN EL DETALLE DEL PEDIDO
        // ============================================================
        private async Task CompletarDetalleConAlmacenAsync(
            List<SurtidoPedidoDetalleVM> detalle,
            CancellationToken ct = default)
        {
            if (detalle == null || detalle.Count == 0)
            {
                return;
            }

            var codigos =
                detalle
                    .Select(x =>
                        NormalizarCodigoAlmacen(
                            x.Almacen
                        )
                    )
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x)
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            var almacenes =
                codigos
                    .Select(ResolverAlmacen)
                    .ToList();

            await AplicarConfiguracionUbicacionAsync(
                almacenes,
                ct
            );

            var mapa =
                almacenes
                    .GroupBy(
                        x =>
                            NormalizarCodigoAlmacen(
                                x.Codigo
                            ),
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToDictionary(
                        g => g.Key,
                        g => g.First(),
                        StringComparer.OrdinalIgnoreCase
                    );

            foreach (var item in detalle)
            {
                var codigo =
                    NormalizarCodigoAlmacen(
                        item.Almacen
                    );

                if (mapa.TryGetValue(
                        codigo,
                        out var almacen))
                {
                    item.Almacen =
                        almacen.Codigo;

                    item.AlmacenNombre =
                        almacen.Nombre;

                    item.ObligaUbicacion =
                        almacen.ObligaUbicacion;

                    item.TieneConfiguracionUbicacion =
                        almacen.TieneConfiguracionUbicacion;
                }
                else
                {
                    item.AlmacenNombre =
                        item.Almacen;
                }
            }
        }


        // ============================================================
        // BUSCAR ALMACÉN PERMITIDO
        // ============================================================
        private static SurtidoAlmacenVM? BuscarAlmacenPermitido(
            IEnumerable<SurtidoAlmacenVM> almacenes,
            string? codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo))
            {
                return null;
            }

            var codigoN =
                NormalizarCodigoAlmacen(
                    codigo
                );

            return almacenes.FirstOrDefault(x =>
                string.Equals(
                    NormalizarCodigoAlmacen(x.Codigo),
                    codigoN,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }


        // ============================================================
        // RESOLVER CADENA SEGÚN SUCURSAL / PLANTA DEL WAREHOUSE
        //
        // P1  -> CadenaMeatP1
        // TIF -> CadenaMeatTIF
        //
        // NO HAY FALLBACK P1 -> TIF NI TIF -> P1.
        // ============================================================
        private (
            string Nombre,
            string Cadena)
            ObtenerCadenaMeatPorAlmacen(
                SurtidoAlmacenVM almacen)
        {
            var nombreCadena =
                almacen.Planta switch
                {
                    "P1" =>
                        "CadenaMeatP1",

                    "TIF" =>
                        "CadenaMeatTIF",

                    _ =>
                        ""
                };

            if (string.IsNullOrWhiteSpace(nombreCadena))
            {
                throw new InvalidOperationException(
                    $"El almacén '{almacen.Codigo} - {almacen.Nombre}' " +
                    $"tiene Sucursal '{almacen.Sucursal}' y no se pudo " +
                    "determinar si pertenece a PLANTA 1 o TIF 776."
                );
            }

            var cadena =
                _configuracion.GetConnectionString(
                    nombreCadena
                );

            if (string.IsNullOrWhiteSpace(cadena))
            {
                throw new InvalidOperationException(
                    $"No existe ConnectionStrings:{nombreCadena} en appsettings.json."
                );
            }

            return (
                nombreCadena,
                cadena
            );
        }


        // ============================================================
        // PEDIDOS PENDIENTES
        //
        // TipoReferenciaId = 9 -> Pedido VPED
        // TipoReferenciaId = 6 -> Nombre del cliente
        // SolicitudSurtido.EstatusId = 1 -> Pendiente
        // ============================================================
        private async Task<List<SurtidoPedidoVM>>
            ObtenerPedidosPendientesAsync(
                SurtidoAlmacenVM almacen,
                DateTime? fechaInicio,
                DateTime? fechaFin,
                CancellationToken ct = default)
        {
            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            // IMPORTANTE DE RENDIMIENTO:
            // Primero reducimos el universo a SolicitudSurtido pendientes.
            // Después agregamos SolicitudSurtidoDetalle / SalidaEmbarque
            // únicamente para esos IDs. Así evitamos recorrer el histórico
            // completo de MEAT para construir una cola operativa pequeña.
            const string sql = @"
;WITH Pendientes AS
(
    SELECT
        ss.SolicitudSurtidoId,
        ss.FechaHora,
        ss.EstatusId
    FROM dbo.SolicitudSurtido ss WITH (NOLOCK)
    WHERE ss.EstatusId = 1
      AND (
            @FechaInicio IS NULL
            OR ss.FechaHora >= @FechaInicio
          )
      AND (
            @FechaFin IS NULL
            OR ss.FechaHora < DATEADD(DAY, 1, @FechaFin)
          )
),
Solicitado AS
(
    SELECT
        d.SolicitudSurtidoId,
        CajasSolicitadas =
            SUM(
                CONVERT(
                    INT,
                    ISNULL(d.Cantidad, 0)
                )
            )
    FROM dbo.SolicitudSurtidoDetalle d WITH (NOLOCK)
    INNER JOIN Pendientes pe
        ON pe.SolicitudSurtidoId = d.SolicitudSurtidoId
    WHERE CONVERT(VARCHAR(40), d.Almacen) = @Almacen
    GROUP BY
        d.SolicitudSurtidoId
),
Surtido AS
(
    SELECT
        se.SolicitudSurtidoId,
        CajasSurtidas =
            COUNT_BIG(1)
    FROM Pendientes pe
    INNER JOIN dbo.SalidaEmbarque se WITH (NOLOCK)
        ON se.SolicitudSurtidoId = pe.SolicitudSurtidoId
    INNER JOIN dbo.Produccion p WITH (NOLOCK)
        ON p.ProduccionId = se.ProduccionId
    WHERE CONVERT(VARCHAR(40), p.Almacen) = @Almacen
    GROUP BY
        se.SolicitudSurtidoId
)
SELECT
    pe.SolicitudSurtidoId,
    pe.FechaHora,

    Pedido =
        CASE
            WHEN CHARINDEX(
                    '.',
                    REVERSE(
                        ISNULL(srp.Referencia, '')
                    )
                 ) > 0
            THEN RIGHT(
                    srp.Referencia,
                    CHARINDEX(
                        '.',
                        REVERSE(srp.Referencia)
                    ) - 1
                 )
            ELSE ISNULL(
                    srp.Referencia,
                    ''
                 )
        END,

    Cliente =
        ISNULL(
            src.Referencia,
            ''
        ),

    pe.EstatusId,

    CajasSolicitadas =
        ISNULL(sol.CajasSolicitadas, 0),

    CajasSurtidas =
        CONVERT(
            INT,
            ISNULL(sur.CajasSurtidas, 0)
        )

FROM Pendientes pe

INNER JOIN Solicitado sol
    ON sol.SolicitudSurtidoId = pe.SolicitudSurtidoId

INNER JOIN dbo.SurtidoReferencia srp WITH (NOLOCK)
    ON srp.SolicitudSurtidoId = pe.SolicitudSurtidoId
   AND srp.TipoReferenciaId = 9

LEFT JOIN dbo.SurtidoReferencia src WITH (NOLOCK)
    ON src.SolicitudSurtidoId = pe.SolicitudSurtidoId
   AND src.TipoReferenciaId = 6

LEFT JOIN Surtido sur
    ON sur.SolicitudSurtidoId = pe.SolicitudSurtidoId

ORDER BY
    pe.FechaHora ASC,
    pe.SolicitudSurtidoId ASC;
";

            await using var conn =
                new SqlConnection(
                    conexion.Cadena
                );

            await conn.OpenAsync(ct);

            var lista =
                (
                    await conn.QueryAsync<SurtidoPedidoVM>(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                FechaInicio =
                                    fechaInicio?.Date,

                                FechaFin =
                                    fechaFin?.Date,

                                Almacen =
                                    almacen.Codigo
                            },
                            cancellationToken: ct
                        )
                    )
                )
                .ToList();

            foreach (var pedido in lista)
            {
                pedido.Planta =
                    almacen.Planta;

                pedido.OrigenConexion =
                    conexion.Nombre;

                pedido.AlmacenCodigo =
                    almacen.Codigo;

                pedido.AlmacenNombre =
                    almacen.Nombre;

                pedido.AlmacenSucursal =
                    almacen.Sucursal;

                pedido.AlmacenClasificacion =
                    almacen.Clasificacion;
            }

            await CompletarBajadasEnPedidosAsync(
                lista,
                almacen,
                ct
            );

            return lista
                .Where(x =>
                    x.CajasPendientes > 0
                    || (x.CajasBajadas > 0 && x.CajasSurtidas < x.CajasSolicitadas)
                )
                .ToList();
        }



        // ============================================================
        // PRIORIDAD MANUAL DE PEDIDOS
        //
        // La tabla vive en SIGO:
        // dbo.SurtidoPedidoPrioridad
        //
        // Si todavía no existe una prioridad guardada para un pedido,
        // conserva como fallback el orden por FechaHora + Solicitud.
        // ============================================================
        private async Task<Dictionary<int, int>>
            ObtenerPrioridadesPedidoAsync(
                SurtidoAlmacenVM almacen,
                IEnumerable<int> solicitudes,
                CancellationToken ct)
        {
            var ids =
                solicitudes
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

            if (ids.Count == 0)
            {
                return new Dictionary<int, int>();
            }

            var conn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    SolicitudSurtidoId,
    Prioridad
FROM dbo.SurtidoPedidoPrioridad WITH (NOLOCK)
WHERE Activo = 1
  AND Planta = @Planta
  AND CodigoAlmacen = @CodigoAlmacen
  AND SolicitudSurtidoId IN @Ids;
";

            try
            {
                var rows =
                    (
                        await conn.QueryAsync<SurtidoPedidoPrioridadDto>(
                            new CommandDefinition(
                                sql,
                                new
                                {
                                    Planta =
                                        almacen.Planta,

                                    CodigoAlmacen =
                                        almacen.Codigo,

                                    Ids =
                                        ids
                                },
                                cancellationToken: ct
                            )
                        )
                    )
                    .ToList();

                return rows
                    .GroupBy(x =>
                        x.SolicitudSurtidoId
                    )
                    .ToDictionary(
                        g => g.Key,
                        g => Math.Max(
                            1,
                            g.OrderBy(x => x.Prioridad)
                                .First()
                                .Prioridad
                        )
                    );
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                // Permite que la pantalla siga funcionando antes
                // de ejecutar el script de creación de tabla.
                return new Dictionary<int, int>();
            }
        }


        // ============================================================
        // PRIORIDADES MASIVAS PARA PICKING (SÓLO SIGO)
        //
        // Evita volver a reconstruir la cola de MEAT después de que
        // los pedidos ya fueron cargados en memoria.
        // ============================================================
        private async Task<Dictionary<string, int>>
            ObtenerPrioridadesGuardadasMasivasAsync(
                IEnumerable<SurtidoAlmacenVM> almacenes,
                IEnumerable<SurtidoPedidoVM> pedidos,
                CancellationToken ct)
        {
            var listaAlmacenes =
                almacenes
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Codigo)
                    )
                    .ToList();

            var ids =
                pedidos
                    .Where(x =>
                        x.SolicitudSurtidoId > 0
                    )
                    .Select(x =>
                        x.SolicitudSurtidoId
                    )
                    .Distinct()
                    .ToList();

            var codigos =
                listaAlmacenes
                    .Select(x =>
                        NormalizarCodigoAlmacen(x.Codigo)
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            if (ids.Count == 0 || codigos.Count == 0)
            {
                return new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase
                );
            }

            var paresPermitidos =
                listaAlmacenes
                    .Select(x =>
                        $"{(x.Planta ?? "").Trim().ToUpperInvariant()}|{NormalizarCodigoAlmacen(x.Codigo)}"
                    )
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase
                    );

            var conn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    Planta = ISNULL(Planta, ''),
    CodigoAlmacen = ISNULL(CodigoAlmacen, ''),
    SolicitudSurtidoId,
    Prioridad
FROM dbo.SurtidoPedidoPrioridad WITH (NOLOCK)
WHERE Activo = 1
  AND CodigoAlmacen IN @Codigos
  AND SolicitudSurtidoId IN @Ids;
";

            try
            {
                var rows =
                    (
                        await conn.QueryAsync<SurtidoPedidoPrioridadMasivaDto>(
                            new CommandDefinition(
                                sql,
                                new
                                {
                                    Codigos =
                                        codigos,

                                    Ids =
                                        ids
                                },
                                cancellationToken: ct
                            )
                        )
                    )
                    .Where(x =>
                        paresPermitidos.Contains(
                            $"{(x.Planta ?? "").Trim().ToUpperInvariant()}|{NormalizarCodigoAlmacen(x.CodigoAlmacen)}"
                        )
                    )
                    .ToList();

                return rows
                    .GroupBy(
                        x =>
                            $"{NormalizarCodigoAlmacen(x.CodigoAlmacen)}|{x.SolicitudSurtidoId}",
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToDictionary(
                        g => g.Key,
                        g => Math.Max(
                            1,
                            g.OrderBy(x => x.Prioridad)
                                .First()
                                .Prioridad
                        ),
                        StringComparer.OrdinalIgnoreCase
                    );
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase
                );
            }
        }


        private async Task<Dictionary<string, int>>
            ConstruirPrioridadesPickingDesdePedidosAsync(
                IEnumerable<SurtidoAlmacenVM> almacenes,
                IEnumerable<SurtidoPedidoVM> pedidos,
                CancellationToken ct)
        {
            var listaPedidos =
                pedidos
                    .Where(x =>
                        x.CajasPendientes > 0
                    )
                    .ToList();

            var guardadas =
                await ObtenerPrioridadesGuardadasMasivasAsync(
                    almacenes,
                    listaPedidos,
                    ct
                );

            var resultado =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var grupo in listaPedidos.GroupBy(
                x => NormalizarCodigoAlmacen(x.AlmacenCodigo),
                StringComparer.OrdinalIgnoreCase))
            {
                var ordenados =
                    grupo
                        .OrderBy(x =>
                        {
                            var clave =
                                $"{NormalizarCodigoAlmacen(x.AlmacenCodigo)}|{x.SolicitudSurtidoId}";

                            return guardadas.TryGetValue(
                                clave,
                                out var prioridad
                            )
                                ? prioridad
                                : int.MaxValue;
                        })
                        .ThenBy(x =>
                            x.FechaHora
                        )
                        .ThenBy(x =>
                            x.SolicitudSurtidoId
                        )
                        .ToList();

                for (var i = 0; i < ordenados.Count; i++)
                {
                    var clave =
                        $"{NormalizarCodigoAlmacen(ordenados[i].AlmacenCodigo)}|{ordenados[i].SolicitudSurtidoId}";

                    resultado[clave] =
                        i + 1;
                }
            }

            return resultado;
        }


        // ============================================================
        // FIRMA LIGERA DE PRIORIDADES PARA SINCRONIZACIÓN DE LA VISTA.
        //
        // Esta consulta toca únicamente SIGO. No consulta MEAT.
        // ============================================================
        private async Task<string>
            ObtenerFirmaPrioridadesGuardadasAsync(
                IEnumerable<SurtidoAlmacenVM> almacenes,
                CancellationToken ct)
        {
            var listaAlmacenes =
                almacenes
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Codigo)
                    )
                    .ToList();

            var codigos =
                listaAlmacenes
                    .Select(x =>
                        NormalizarCodigoAlmacen(x.Codigo)
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            if (codigos.Count == 0)
            {
                return "";
            }

            var paresPermitidos =
                listaAlmacenes
                    .Select(x =>
                        $"{(x.Planta ?? "").Trim().ToUpperInvariant()}|{NormalizarCodigoAlmacen(x.Codigo)}"
                    )
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase
                    );

            var conn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    Planta = ISNULL(Planta, ''),
    CodigoAlmacen = ISNULL(CodigoAlmacen, ''),
    SolicitudSurtidoId,
    Prioridad
FROM dbo.SurtidoPedidoPrioridad WITH (NOLOCK)
WHERE Activo = 1
  AND CodigoAlmacen IN @Codigos;
";

            try
            {
                var rows =
                    (
                        await conn.QueryAsync<SurtidoPedidoPrioridadMasivaDto>(
                            new CommandDefinition(
                                sql,
                                new
                                {
                                    Codigos =
                                        codigos
                                },
                                cancellationToken: ct
                            )
                        )
                    )
                    .Where(x =>
                        paresPermitidos.Contains(
                            $"{(x.Planta ?? "").Trim().ToUpperInvariant()}|{NormalizarCodigoAlmacen(x.CodigoAlmacen)}"
                        )
                    )
                    .OrderBy(x =>
                        x.Planta
                    )
                    .ThenBy(x =>
                        x.CodigoAlmacen
                    )
                    .ThenBy(x =>
                        x.SolicitudSurtidoId
                    )
                    .ThenBy(x =>
                        x.Prioridad
                    )
                    .ToList();

                return string.Join(
                    "|",
                    rows.Select(x =>
                        $"{(x.Planta ?? "").Trim().ToUpperInvariant()}:{NormalizarCodigoAlmacen(x.CodigoAlmacen)}:{x.SolicitudSurtidoId}:{x.Prioridad}"
                    )
                );
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return "";
            }
        }


        // ============================================================
        // FIRMA LIGERA DE SOLICITUDES PENDIENTES EN MEAT.
        //
        // No cuenta cajas, no consulta Produccion, no ejecuta PEPS y no
        // reconstruye la cola. Únicamente detecta altas/bajas de solicitudes
        // pendientes por almacén. Se ejecuta como máximo una vez por cada
        // conexión MEAT (P1/TIF), no una vez por almacén.
        // ============================================================
        private async Task<string>
            ObtenerFirmaSolicitudesPendientesMeatAsync(
                IEnumerable<SurtidoAlmacenVM> almacenes,
                CancellationToken ct)
        {
            var lista =
                almacenes
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Codigo)
                    )
                    .ToList();

            if (lista.Count == 0)
            {
                return "";
            }

            var partes =
                new List<string>();

            var grupos =
                lista
                    .GroupBy(x =>
                        ObtenerCadenaMeatPorAlmacen(x).Nombre,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            const string sql = @"
SELECT
    AlmacenCodigo =
        CONVERT(VARCHAR(40), d.Almacen),
    ss.SolicitudSurtidoId
FROM dbo.SolicitudSurtido ss WITH (NOLOCK)
INNER JOIN dbo.SolicitudSurtidoDetalle d WITH (NOLOCK)
    ON d.SolicitudSurtidoId = ss.SolicitudSurtidoId
WHERE ss.EstatusId = 1
  AND CONVERT(VARCHAR(40), d.Almacen) IN @Almacenes
GROUP BY
    CONVERT(VARCHAR(40), d.Almacen),
    ss.SolicitudSurtidoId
ORDER BY
    AlmacenCodigo,
    ss.SolicitudSurtidoId;
";

            foreach (var grupo in grupos)
            {
                var almacenReferencia =
                    grupo.First();

                var conexion =
                    ObtenerCadenaMeatPorAlmacen(
                        almacenReferencia
                    );

                var codigos =
                    grupo
                        .Select(x =>
                            NormalizarCodigoAlmacen(x.Codigo)
                        )
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase
                        )
                        .ToList();

                await using var cn =
                    new SqlConnection(
                        conexion.Cadena
                    );

                await cn.OpenAsync(ct);

                var rows =
                    (
                        await cn.QueryAsync<dynamic>(
                            new CommandDefinition(
                                sql,
                                new
                                {
                                    Almacenes =
                                        codigos
                                },
                                cancellationToken: ct
                            )
                        )
                    )
                    .ToList();

                foreach (var row in rows)
                {
                    partes.Add(
                        $"{conexion.Nombre}:{NormalizarCodigoAlmacen(Convert.ToString(row.AlmacenCodigo))}:{Convert.ToInt32(row.SolicitudSurtidoId)}"
                    );
                }
            }

            return string.Join(
                "|",
                partes.OrderBy(
                    x => x,
                    StringComparer.OrdinalIgnoreCase
                )
            );
        }


        private async Task<string>
            ObtenerFirmaSincronizacionPickingAsync(
                IEnumerable<SurtidoAlmacenVM> almacenes,
                CancellationToken ct)
        {
            var lista =
                almacenes.ToList();

            var firmaPrioridades =
                await ObtenerFirmaPrioridadesGuardadasAsync(
                    lista,
                    ct
                );

            var firmaSolicitudes =
                await ObtenerFirmaSolicitudesPendientesMeatAsync(
                    lista,
                    ct
                );

            return
                firmaPrioridades
                + "||"
                + firmaSolicitudes;
        }




        private async Task<(
            List<SurtidoPedidoVM> Pedidos,
            Dictionary<int, int> Prioridades)>
            ConstruirColaPrioridadAsync(
                SurtidoAlmacenVM almacen,
                CancellationToken ct)
        {
            var pendientes =
                await ObtenerPedidosPendientesAsync(
                    almacen,
                    null,
                    null,
                    ct
                );

            pendientes =
                pendientes
                    .Where(x =>
                        x.CajasPendientes > 0
                    )
                    .ToList();

            var prioridadesGuardadas =
                await ObtenerPrioridadesPedidoAsync(
                    almacen,
                    pendientes.Select(x =>
                        x.SolicitudSurtidoId
                    ),
                    ct
                );

            var ordenados =
                pendientes
                    .OrderBy(x =>
                        prioridadesGuardadas.TryGetValue(
                            x.SolicitudSurtidoId,
                            out var prioridad
                        )
                            ? prioridad
                            : int.MaxValue
                    )
                    .ThenBy(x =>
                        x.FechaHora
                    )
                    .ThenBy(x =>
                        x.SolicitudSurtidoId
                    )
                    .ToList();

            // Siempre exponemos una cola limpia 1,2,3... aunque
            // la tabla tenga huecos por datos históricos.
            var prioridadesEfectivas =
                new Dictionary<int, int>();

            for (var i = 0; i < ordenados.Count; i++)
            {
                prioridadesEfectivas[
                    ordenados[i].SolicitudSurtidoId
                ] =
                    i + 1;
            }

            return (
                ordenados,
                prioridadesEfectivas
            );
        }


        // ============================================================
        // PRIORIDAD ESTRICTA DE PICKING POR ALMACÉN
        //
        // Prioridad manual del coordinador tiene preferencia.
        // Si aún no existe configuración, el fallback sigue siendo
        // FechaHora ASC + SolicitudSurtidoId ASC.
        // ============================================================
        private async Task<(
            bool Ok,
            SurtidoPedidoVM? Prioridad,
            string Mensaje)>
            ValidarPrioridadPickingAsync(
                SurtidoAlmacenVM almacen,
                int solicitudSurtidoId,
                CancellationToken ct)
        {
            var cola =
                await ConstruirColaPrioridadAsync(
                    almacen,
                    ct
                );

            var prioridad =
                cola.Pedidos.FirstOrDefault();

            if (prioridad == null)
            {
                return (
                    false,
                    null,
                    "No hay pedidos pendientes para surtir en este almacén."
                );
            }

            if (prioridad.SolicitudSurtidoId != solicitudSurtidoId)
            {
                var pedidoTexto =
                    string.IsNullOrWhiteSpace(
                        prioridad.Pedido
                    )
                        ? $"Solicitud {prioridad.SolicitudSurtidoId}"
                        : $"Pedido {prioridad.Pedido}";

                return (
                    false,
                    prioridad,
                    $"Pedido bloqueado por prioridad. Primero debes terminar " +
                    $"la Prioridad 1: {pedidoTexto} " +
                    $"(Solicitud {prioridad.SolicitudSurtidoId})."
                );
            }

            return (
                true,
                prioridad,
                ""
            );
        }


        // ============================================================
        // Cajas que ya fueron BAJADAS por Montacarguista.
        // Todavía NO son SalidaEmbarque.
        // ============================================================
        // ============================================================
        // CONTADORES PARA PICKING: DATOS REALES DE SIGO
        // Bajadas = cajas físicas en SurtidoBajada (no tarimas).
        // Surtidas = cajas activas capturadas por el capturista.
        // No modifica PEPS ni genera movimientos en MEAT.
        // ============================================================
        private async Task CompletarBajadasEnPedidosAsync(
            List<SurtidoPedidoVM> pedidos,
            SurtidoAlmacenVM almacen,
            CancellationToken ct)
        {
            if (pedidos.Count == 0) return;

            var ids = pedidos.Select(p => p.SolicitudSurtidoId)
                             .Distinct().ToArray();
            var conn = await ObtenerConexionSigoAsync(ct);

            const string sql = @"
;WITH Bajadas AS
(
    SELECT SolicitudSurtidoId,
           COUNT(DISTINCT ProduccionId) AS CajasBajadas
    FROM dbo.SurtidoBajada
    WHERE Activo = 1
      AND Estatus = 'BAJADA'
      AND Planta = @Planta
      AND CodigoAlmacen = @Almacen
      AND SolicitudSurtidoId IN @Ids
    GROUP BY SolicitudSurtidoId
),
Capturas AS
(
    SELECT sc.SolicitudSurtidoId,
           COUNT(DISTINCT d.ProduccionId) AS CajasSurtidas
    FROM dbo.SurtidoCaptura sc
    INNER JOIN dbo.SurtidoCapturaDetalle d
        ON d.SurtidoCapturaId = sc.Id
    WHERE d.Activo = 1
      AND sc.Estatus IN ('CAPTURANDO', 'VALIDADO')
      AND sc.Planta = @Planta
      AND sc.CodigoAlmacen = @Almacen
      AND sc.SolicitudSurtidoId IN @Ids
    GROUP BY sc.SolicitudSurtidoId
)
SELECT ISNULL(b.SolicitudSurtidoId,c.SolicitudSurtidoId) AS SolicitudSurtidoId,
       ISNULL(b.CajasBajadas,0) AS CajasBajadas,
       ISNULL(c.CajasSurtidas,0) AS CajasSurtidas
FROM Bajadas b
FULL OUTER JOIN Capturas c
    ON c.SolicitudSurtidoId = b.SolicitudSurtidoId;";

            var rows = await conn.QueryAsync<dynamic>(
                new CommandDefinition(sql,
                    new
                    {
                        Planta = almacen.Planta,
                        Almacen = almacen.Codigo,
                        Ids = ids
                    },
                    cancellationToken: ct));

            var porSolicitud = rows.ToDictionary(
                r => Convert.ToInt64(r.SolicitudSurtidoId));
            foreach (var pedido in pedidos)
            {
                if (!porSolicitud.TryGetValue(
                        Convert.ToInt64(pedido.SolicitudSurtidoId),
                        out var datos)) continue;

                pedido.CajasBajadas = Convert.ToInt32(datos.CajasBajadas);
                // Mantener el valor de Meat cuando ya se haya integrado.
                // En fase SIGO usar las etiquetas activas de la captura.
                pedido.CajasSurtidas = Math.Max(
                    pedido.CajasSurtidas,
                    Convert.ToInt32(datos.CajasSurtidas));
            }
        }


        // ============================================================
        // OBTENER CABECERA DE UN PEDIDO POR SolicitudSurtidoId
        //
        // TipoReferenciaId = 9 -> Pedido
        // TipoReferenciaId = 6 -> Cliente
        // ============================================================
        private async Task<SurtidoPedidoVM?> ObtenerPedidoPorSolicitudAsync(
            SurtidoAlmacenVM almacen,
            int solicitudSurtidoId,
            CancellationToken ct = default)
        {
            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            const string sql = @"
SELECT TOP 1
    ss.SolicitudSurtidoId,
    ss.FechaHora,

    Pedido =
        CASE
            WHEN CHARINDEX(
                    '.',
                    REVERSE(
                        ISNULL(srp.Referencia, '')
                    )
                 ) > 0
            THEN RIGHT(
                    srp.Referencia,
                    CHARINDEX(
                        '.',
                        REVERSE(srp.Referencia)
                    ) - 1
                 )
            ELSE ISNULL(
                    srp.Referencia,
                    ''
                 )
        END,

    Cliente =
        ISNULL(
            src.Referencia,
            ''
        ),

    ss.EstatusId

FROM dbo.SolicitudSurtido ss WITH (NOLOCK)

INNER JOIN dbo.SurtidoReferencia srp WITH (NOLOCK)
    ON srp.SolicitudSurtidoId = ss.SolicitudSurtidoId
   AND srp.TipoReferenciaId = 9

LEFT JOIN dbo.SurtidoReferencia src WITH (NOLOCK)
    ON src.SolicitudSurtidoId = ss.SolicitudSurtidoId
   AND src.TipoReferenciaId = 6

WHERE ss.SolicitudSurtidoId = @SolicitudSurtidoId;
";

            await using var conn =
                new SqlConnection(
                    conexion.Cadena
                );

            await conn.OpenAsync(ct);

            var pedido =
                await conn.QueryFirstOrDefaultAsync<SurtidoPedidoVM>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            SolicitudSurtidoId =
                                solicitudSurtidoId
                        },
                        cancellationToken: ct
                    )
                );

            if (pedido != null)
            {
                pedido.Planta =
                    almacen.Planta;

                pedido.OrigenConexion =
                    conexion.Nombre;
            }

            return pedido;
        }


        // ============================================================
        // DETALLE DE LA SOLICITUD DESDE MEAT
        //
        // SolicitudSurtidoDetalle:
        // - SolicitudSurtidoId
        // - Articulo
        // - Almacen
        // - Cantidad
        // - FechaHora
        // ============================================================
        private async Task<List<SurtidoPedidoDetalleVM>>
            ObtenerDetalleSolicitudMeatAsync(
                SurtidoAlmacenVM almacen,
                int solicitudSurtidoId,
                CancellationToken ct = default)
        {
            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            const string sql = @"
SELECT
    SolicitudSurtidoId =
        d.SolicitudSurtidoId,

    Articulo =
        ISNULL(
            d.Articulo,
            ''
        ),

    Almacen =
        ISNULL(
            CONVERT(
                VARCHAR(40),
                d.Almacen
            ),
            ''
        ),

    Cantidad =
        ISNULL(
            d.Cantidad,
            0
        ),

    FechaHora =
        d.FechaHora

FROM dbo.SolicitudSurtidoDetalle d WITH (NOLOCK)

WHERE d.SolicitudSurtidoId = @SolicitudSurtidoId

ORDER BY
    d.Articulo,
    d.Almacen;
";

            await using var conn =
                new SqlConnection(
                    conexion.Cadena
                );

            await conn.OpenAsync(ct);

            var detalle =
                await conn.QueryAsync<SurtidoPedidoDetalleVM>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            SolicitudSurtidoId =
                                solicitudSurtidoId
                        },
                        cancellationToken: ct
                    )
                );

            return detalle.ToList();
        }


        // ============================================================
        // COMPLETAR DESCRIPCIÓN / DATOS DE ARTÍCULO DESDE SIGO
        //
        // Relación:
        // SolicitudSurtidoDetalle.Articulo
        //              =
        // ArticuloSap.ProductoCodigo
        //
        // Se consulta mediante la conexión del AppDbContext de SIGO.
        // No se hace JOIN cross-database.
        // ============================================================
        private async Task CompletarDetalleConArticuloSapAsync(
            List<SurtidoPedidoDetalleVM> detalle,
            CancellationToken ct = default)
        {
            var codigos =
                detalle
                    .Select(x =>
                        (x.Articulo ?? "").Trim()
                    )
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x)
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            if (codigos.Count == 0)
            {
                return;
            }

            var conn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    ProductoCodigo =
        ISNULL(
            a.ProductoCodigo,
            ''
        ),

    ProductoNombre =
        ISNULL(
            a.ProductoNombre,
            ''
        ),

    KilosCaja =
        a.U_KilosCaja,

    Rotacion =
        a.Rotacion,

    Master =
        ISNULL(
            a.U_MASTER,
            ''
        )

FROM dbo.ArticuloSap a WITH (NOLOCK)

WHERE a.ProductoCodigo IN @Codigos;
";

            var articulos =
                (
                    await conn.QueryAsync<SurtidoArticuloSapVM>(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                Codigos =
                                    codigos
                            },
                            cancellationToken: ct
                        )
                    )
                )
                .ToList();

            var catalogo =
                articulos
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.ProductoCodigo
                        )
                    )
                    .GroupBy(
                        x => x.ProductoCodigo.Trim(),
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToDictionary(
                        g => g.Key,
                        g => g.First(),
                        StringComparer.OrdinalIgnoreCase
                    );

            foreach (var item in detalle)
            {
                var codigo =
                    (item.Articulo ?? "")
                    .Trim();

                if (catalogo.TryGetValue(
                        codigo,
                        out var articulo))
                {
                    item.ProductoNombre =
                        articulo.ProductoNombre;

                    item.KilosCaja =
                        articulo.KilosCaja;

                    item.Rotacion =
                        articulo.Rotacion;

                    item.Master =
                        articulo.Master;

                    item.EncontradoEnArticuloSap =
                        true;
                }
                else
                {
                    item.ProductoNombre =
                        codigo;

                    item.EncontradoEnArticuloSap =
                        false;
                }
            }
        }


        // ============================================================
        // ARMAR PÁGINA DE DETALLE DEL PEDIDO
        // ============================================================
        private async Task<SurtidoPedidoDetallePaginaVM>
            ConstruirDetallePedidoAsync(
                SurtidoAlmacenVM almacen,
                int solicitudSurtidoId,
                CancellationToken ct = default)
        {
            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            var vm =
                new SurtidoPedidoDetallePaginaVM
                {
                    Almacen =
                        almacen,

                    SolicitudSurtidoId =
                        solicitudSurtidoId,

                    NombreConexion =
                        conexion.Nombre
                };

            try
            {
                vm.Pedido =
                    await ObtenerPedidoPorSolicitudAsync(
                        almacen,
                        solicitudSurtidoId,
                        ct
                    );

                if (vm.Pedido == null)
                {
                    vm.Error =
                        $"No se encontró la SolicitudSurtidoId {solicitudSurtidoId}.";

                    return vm;
                }

                vm.Detalle =
                    await ObtenerDetalleSolicitudMeatAsync(
                        almacen,
                        solicitudSurtidoId,
                        ct
                    );

                await CompletarDetalleConArticuloSapAsync(
                    vm.Detalle,
                    ct
                );

                await CompletarDetalleConAlmacenAsync(
                    vm.Detalle,
                    ct
                );
            }
            catch (SqlException ex)
            {
                vm.Error =
                    $"No fue posible consultar el detalle. " +
                    $"SQL {ex.Number}: {ex.Message}";
            }

            return vm;
        }




        // ============================================================
        // UBICACIÓN DINÁMICA
        //
        // Flujo:
        // Escaneo -> SKU -> U_MASTER -> Color configurado por almacén
        // -> regla de rotación -> ubicaciones activas del color
        // -> elimina ocupadas -> recomienda destino.
        // ============================================================

        private async Task<List<UbicacionDinamicaUnidadDto>>
            BuscarUnidadInventarioUbicacionAsync(
                SurtidoAlmacenVM almacen,
                string codigoEscaneado,
                CancellationToken ct)
        {
            var codigo =
                (codigoEscaneado ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(codigo))
            {
                return new List<UbicacionDinamicaUnidadDto>();
            }

            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            const string sql = @"
SELECT
    ProduccionId =
        CONVERT(BIGINT, p.ProduccionId),

    CodigoEtiqueta =
        ISNULL(p.CodigoEtiqueta, ''),

    Articulo =
        ISNULL(p.Articulo, ''),

    TarimaId =
        CONVERT(BIGINT, tx.TarimaId),

    TarimaCodigo =
        ISNULL(tx.TarimaCodigo, '')

FROM dbo.Produccion p WITH (NOLOCK)

OUTER APPLY
(
    SELECT TOP 1
        td.TarimaId,

        TarimaCodigo =
            ISNULL(
                t.Nombre,
                ''
            )

    FROM dbo.TarimaDetalle td WITH (NOLOCK)

    INNER JOIN dbo.Tarima t WITH (NOLOCK)
        ON t.TarimaId = td.TarimaId

    WHERE td.ProduccionId = p.ProduccionId

    ORDER BY
        td.FechaHora DESC,
        td.TarimaId DESC
) tx

WHERE p.Estatus = 1
  AND CONVERT(VARCHAR(40), p.Almacen) = @Almacen
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.SalidaEmbarque se WITH (NOLOCK)
      WHERE se.ProduccionId = p.ProduccionId
  )
  AND
  (
         UPPER(LTRIM(RTRIM(ISNULL(p.CodigoEtiqueta, '')))) = @Codigo
      OR UPPER(LTRIM(RTRIM(ISNULL(tx.TarimaCodigo, '')))) = @Codigo
  )

ORDER BY
    p.ProduccionId;
";

            await using var cn =
                new SqlConnection(
                    conexion.Cadena
                );

            await cn.OpenAsync(ct);

            return (
                await cn.QueryAsync<UbicacionDinamicaUnidadDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Almacen =
                                almacen.Codigo,

                            Codigo =
                                codigo
                        },
                        cancellationToken: ct
                    )
                )
            ).ToList();
        }


        private async Task<UbicacionDinamicaArticuloDto?>
            ObtenerArticuloUbicacionDinamicaAsync(
                string articulo,
                CancellationToken ct)
        {
            var codigo =
                (articulo ?? "")
                    .Trim();

            if (string.IsNullOrWhiteSpace(codigo))
            {
                return null;
            }

            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT TOP 1
    ProductoCodigo =
        ISNULL(a.ProductoCodigo, ''),

    ProductoNombre =
        ISNULL(a.ProductoNombre, ''),

    Rotacion =
        a.Rotacion,

    Master =
        ISNULL(a.U_MASTER, '')

FROM dbo.ArticuloSap a WITH (NOLOCK)

WHERE a.ProductoCodigo = @Articulo;
";

            return await cn
                .QueryFirstOrDefaultAsync<UbicacionDinamicaArticuloDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Articulo =
                                codigo
                        },
                        cancellationToken: ct
                    )
                );
        }


        private async Task<UbicacionDinamicaLayoutDto?>
            ObtenerLayoutOperativoUbicacionAsync(
                string codigoAlmacen,
                CancellationToken ct)
        {
            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT TOP 1
    LayoutId = Id,
    CodigoAlmacen,
    CodigoLayout = ISNULL(CodigoLayout,''),
    NombreLayout = ISNULL(NombreLayout,'')
FROM dbo.SurtidoLayoutAlmacen WITH (NOLOCK)
WHERE CodigoAlmacen = @CodigoAlmacen
  AND Activo = 1
  AND EsOperativo = 1
ORDER BY Orden, Id;
";

            return await cn.QueryFirstOrDefaultAsync<UbicacionDinamicaLayoutDto>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        CodigoAlmacen =
                            NormalizarCodigoAlmacen(codigoAlmacen)
                    },
                    cancellationToken: ct
                )
            );
        }


        private async Task<UbicacionDinamicaColorDto?>
            ObtenerColorMasterUbicacionAsync(
                string codigoAlmacen,
                int layoutId,
                string master,
                CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(master))
            {
                return null;
            }

            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT TOP 1
    LayoutId = c.LayoutId,

    ColorAlmacenId =
        c.Id,

    CodigoAlmacen =
        c.CodigoAlmacen,

    CodigoColor =
        ISNULL(c.CodigoColor, ''),

    NombreColor =
        ISNULL(c.NombreColor, ''),

    HexColor =
        ISNULL(c.HexColor, '')

FROM dbo.SurtidoMasterColorAlmacen mc WITH (NOLOCK)

INNER JOIN dbo.SurtidoColorAlmacen c WITH (NOLOCK)
    ON c.Id = mc.ColorAlmacenId
   AND c.Activo = 1

WHERE mc.Activo = 1
  AND mc.CodigoAlmacen = @CodigoAlmacen
  AND mc.LayoutId = @LayoutId
  AND c.CodigoAlmacen = @CodigoAlmacen
  AND c.LayoutId = @LayoutId
  AND mc.Master = @Master

ORDER BY
    mc.Id DESC;
";

            return await cn
                .QueryFirstOrDefaultAsync<UbicacionDinamicaColorDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            CodigoAlmacen =
                                NormalizarCodigoAlmacen(
                                    codigoAlmacen
                                ),

                            LayoutId =
                                layoutId,

                            Master =
                                master.Trim()
                        },
                        cancellationToken: ct
                    )
                );
        }


        private async Task<UbicacionDinamicaReglaRotacionDto?>
            ObtenerReglaRotacionUbicacionAsync(
                string codigoAlmacen,
                int layoutId,
                decimal? rotacion,
                CancellationToken ct)
        {
            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT TOP 1
    Id,
    LayoutId,
    CodigoAlmacen,
    NombreRegla =
        ISNULL(NombreRegla, ''),

    Desde,
    Hasta,

    AplicaSinRotacion =
        ISNULL(AplicaSinRotacion, 0),

    ModoOrden =
        ISNULL(ModoOrden, ''),

    Prioridad =
        ISNULL(Prioridad, 1)

FROM dbo.SurtidoRotacionReglaAlmacen WITH (NOLOCK)

WHERE Activo = 1
  AND CodigoAlmacen = @CodigoAlmacen
  AND LayoutId = @LayoutId
  AND
  (
      (
          @Rotacion IS NULL
          AND AplicaSinRotacion = 1
      )
      OR
      (
          @Rotacion IS NOT NULL
          AND (Desde IS NULL OR @Rotacion >= Desde)
          AND (Hasta IS NULL OR @Rotacion <= Hasta)
      )
  )

ORDER BY
    Prioridad ASC,
    Id ASC;
";

            return await cn
                .QueryFirstOrDefaultAsync<UbicacionDinamicaReglaRotacionDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            CodigoAlmacen =
                                NormalizarCodigoAlmacen(
                                    codigoAlmacen
                                ),

                            LayoutId =
                                layoutId,

                            Rotacion =
                                rotacion
                        },
                        cancellationToken: ct
                    )
                );
        }


        private async Task<List<UbicacionDinamicaCatalogoDto>>
            ObtenerUbicacionesColorDinamicasAsync(
                string codigoAlmacen,
                int layoutId,
                int colorAlmacenId,
                CancellationToken ct)
        {
            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    LayoutId = u.LayoutId,

    UbicacionId =
        u.Id,

    CodigoAlmacen =
        u.CodigoAlmacen,

    ColorAlmacenId =
        u.ColorAlmacenId,

    CodigoColor =
        ISNULL(c.CodigoColor, ''),

    NombreColor =
        ISNULL(c.NombreColor, ''),

    HexColor =
        ISNULL(c.HexColor, ''),

    Rack =
        ISNULL(u.Rack, ''),

    Posicion =
        ISNULL(u.Posicion, ''),

    Altura =
        ISNULL(u.Altura, ''),

    CodigoUbicacion =
        ISNULL(u.CodigoUbicacion, ''),

    CodigoMapa3D =
        ISNULL(u.CodigoMapa3D, ''),

    OrdenFisico =
        u.OrdenFisico

FROM dbo.SurtidoUbicacionCatalogo u WITH (NOLOCK)

INNER JOIN dbo.SurtidoColorAlmacen c WITH (NOLOCK)
    ON c.Id = u.ColorAlmacenId
   AND c.Activo = 1

WHERE u.Activo = 1
  AND u.CodigoAlmacen = @CodigoAlmacen
  AND u.LayoutId = @LayoutId
  AND c.CodigoAlmacen = @CodigoAlmacen
  AND c.LayoutId = @LayoutId
  AND u.ColorAlmacenId = @ColorAlmacenId

ORDER BY
    u.OrdenFisico,
    u.Id;
";

            return (
                await cn.QueryAsync<UbicacionDinamicaCatalogoDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            CodigoAlmacen =
                                NormalizarCodigoAlmacen(
                                    codigoAlmacen
                                ),

                            LayoutId =
                                layoutId,

                            ColorAlmacenId =
                                colorAlmacenId
                        },
                        cancellationToken: ct
                    )
                )
            ).ToList();
        }


        private async Task<UbicacionDinamicaCatalogoDto?>
            ObtenerUbicacionDinamicaPorCodigoAsync(
                string codigoAlmacen,
                int layoutId,
                string codigoUbicacion,
                CancellationToken ct)
        {
            var codigo =
                (codigoUbicacion ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(codigo))
            {
                return null;
            }

            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT TOP 1
    LayoutId = u.LayoutId,

    UbicacionId =
        u.Id,

    CodigoAlmacen =
        u.CodigoAlmacen,

    ColorAlmacenId =
        u.ColorAlmacenId,

    CodigoColor =
        ISNULL(c.CodigoColor, ''),

    NombreColor =
        ISNULL(c.NombreColor, ''),

    HexColor =
        ISNULL(c.HexColor, ''),

    Rack =
        ISNULL(u.Rack, ''),

    Posicion =
        ISNULL(u.Posicion, ''),

    Altura =
        ISNULL(u.Altura, ''),

    CodigoUbicacion =
        ISNULL(u.CodigoUbicacion, ''),

    CodigoMapa3D =
        ISNULL(u.CodigoMapa3D, ''),

    OrdenFisico =
        u.OrdenFisico

FROM dbo.SurtidoUbicacionCatalogo u WITH (NOLOCK)

INNER JOIN dbo.SurtidoColorAlmacen c WITH (NOLOCK)
    ON c.Id = u.ColorAlmacenId
   AND c.Activo = 1

WHERE u.Activo = 1
  AND u.CodigoAlmacen = @CodigoAlmacen
  AND u.LayoutId = @LayoutId
  AND c.CodigoAlmacen = @CodigoAlmacen
  AND c.LayoutId = @LayoutId
  AND UPPER(LTRIM(RTRIM(u.CodigoUbicacion))) = @CodigoUbicacion

ORDER BY
    u.Id DESC;
";

            return await cn
                .QueryFirstOrDefaultAsync<UbicacionDinamicaCatalogoDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            CodigoAlmacen =
                                NormalizarCodigoAlmacen(
                                    codigoAlmacen
                                ),

                            LayoutId =
                                layoutId,

                            CodigoUbicacion =
                                codigo
                        },
                        cancellationToken: ct
                    )
                );
        }


        private async Task<UbicacionDinamicaAsignacionActualDto?>
            ObtenerAsignacionUnidadActivaAsync(
                string codigoAlmacen,
                int layoutId,
                string unidadCodigo,
                CancellationToken ct)
        {
            var unidad =
                (unidadCodigo ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(unidad))
            {
                return null;
            }

            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT TOP 1
    Id =
        a.Id,

    LayoutId = a.LayoutId,

    UbicacionId =
        u.Id,

    ColorAlmacenId =
        u.ColorAlmacenId,

    CodigoColor =
        ISNULL(c.CodigoColor, ''),

    NombreColor =
        ISNULL(c.NombreColor, ''),

    HexColor =
        ISNULL(c.HexColor, ''),

    CodigoUbicacion =
        ISNULL(u.CodigoUbicacion, ''),

    CodigoMapa3D =
        ISNULL(u.CodigoMapa3D, '')

FROM dbo.SurtidoUbicacionInventario a WITH (NOLOCK)

INNER JOIN dbo.SurtidoUbicacionCatalogo u WITH (NOLOCK)
    ON u.Id = a.UbicacionId

INNER JOIN dbo.SurtidoColorAlmacen c WITH (NOLOCK)
    ON c.Id = u.ColorAlmacenId

WHERE a.Activo = 1
  AND a.CodigoAlmacen = @CodigoAlmacen
  AND a.LayoutId = @LayoutId
  AND UPPER(LTRIM(RTRIM(a.UnidadCodigo))) = @UnidadCodigo

ORDER BY
    a.Id DESC;
";

            try
            {
                return await cn
                    .QueryFirstOrDefaultAsync<UbicacionDinamicaAsignacionActualDto>(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                CodigoAlmacen =
                                    NormalizarCodigoAlmacen(
                                        codigoAlmacen
                                    ),

                                LayoutId =
                                    layoutId,

                                UnidadCodigo =
                                    unidad
                            },
                            cancellationToken: ct
                        )
                    );
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return null;
            }
        }


        private static List<UbicacionDinamicaCatalogoDto>
            OrdenarUbicacionesDinamicas(
                IEnumerable<UbicacionDinamicaCatalogoDto> ubicaciones,
                string modoOrden)
        {
            var lista =
                ubicaciones
                    .ToList();

            var modo =
                (modoOrden ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (modo == "INICIO")
            {
                return lista
                    .OrderBy(x => x.OrdenFisico)
                    .ThenBy(x => x.UbicacionId)
                    .ToList();
            }

            if (modo == "FINAL")
            {
                return lista
                    .OrderByDescending(x => x.OrdenFisico)
                    .ThenByDescending(x => x.UbicacionId)
                    .ToList();
            }

            if (modo == "CENTRO")
            {
                if (lista.Count == 0)
                {
                    return lista;
                }

                var minimo =
                    lista.Min(x =>
                        x.OrdenFisico
                    );

                var maximo =
                    lista.Max(x =>
                        x.OrdenFisico
                    );

                var centro =
                    (minimo + maximo)
                    / 2.0;

                return lista
                    .OrderBy(x =>
                        Math.Abs(
                            x.OrdenFisico
                            - centro
                        )
                    )
                    .ThenBy(x =>
                        x.OrdenFisico
                    )
                    .ToList();
            }

            throw new InvalidOperationException(
                $"La regla de rotación tiene ModoOrden '{modoOrden}'. " +
                "Los valores permitidos son INICIO, CENTRO o FINAL."
            );
        }


        private async Task<HashSet<int>>
            ObtenerUbicacionesOcupadasDinamicasAsync(
                SurtidoAlmacenVM almacen,
                IReadOnlyCollection<UbicacionDinamicaCatalogoDto> ubicaciones,
                string unidadCodigo,
                IReadOnlyCollection<long> produccionesExcluir,
                CancellationToken ct)
        {
            var resultado =
                new HashSet<int>();

            if (ubicaciones.Count == 0)
            {
                return resultado;
            }

            var idsUbicacion =
                ubicaciones
                    .Select(x =>
                        x.UbicacionId
                    )
                    .Distinct()
                    .ToList();

            var unidad =
                (unidadCodigo ?? "")
                    .Trim()
                    .ToUpperInvariant();

            var sigo =
                await ObtenerConexionSigoAsync(ct);

            const string sqlSigo = @"
SELECT
    UbicacionId
FROM dbo.SurtidoUbicacionInventario WITH (NOLOCK)
WHERE Activo = 1
  AND CodigoAlmacen = @CodigoAlmacen
  AND UbicacionId IN @IdsUbicacion
  AND UPPER(LTRIM(RTRIM(UnidadCodigo))) <> @UnidadCodigo;
";

            try
            {
                var ocupadasSigo =
                    await sigo.QueryAsync<int>(
                        new CommandDefinition(
                            sqlSigo,
                            new
                            {
                                CodigoAlmacen =
                                    almacen.Codigo,

                                IdsUbicacion =
                                    idsUbicacion,

                                UnidadCodigo =
                                    unidad
                            },
                            cancellationToken: ct
                        )
                    );

                foreach (var id in ocupadasSigo)
                {
                    resultado.Add(id);
                }
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                // Permite desplegar primero el controller y después
                // ejecutar el script, aunque la recomendación final
                // seguirá requiriendo las tablas de configuración.
            }

            // ------------------------------------------------------------
            // Compatibilidad con inventario histórico:
            // ProduccionReferencia TipoReferenciaId = 16.
            //
            // Se comparan tanto CodigoUbicacion como CodigoMapa3D.
            // Ninguno de los dos formatos se construye en el controller.
            // ------------------------------------------------------------
            var aliasAUbicacion =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var u in ubicaciones)
            {
                var codigo =
                    (u.CodigoUbicacion ?? "")
                        .Trim()
                        .ToUpperInvariant();

                var mapa =
                    (u.CodigoMapa3D ?? "")
                        .Trim()
                        .ToUpperInvariant();

                if (!string.IsNullOrWhiteSpace(codigo))
                {
                    aliasAUbicacion[codigo] =
                        u.UbicacionId;
                }

                if (!string.IsNullOrWhiteSpace(mapa))
                {
                    aliasAUbicacion[mapa] =
                        u.UbicacionId;
                }
            }

            var aliases =
                aliasAUbicacion.Keys
                    .ToList();

            if (aliases.Count == 0)
            {
                return resultado;
            }

            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            var excluir =
                produccionesExcluir.Count > 0
                    ? produccionesExcluir
                        .Distinct()
                        .ToList()
                    : new List<long>
                    {
                        -1
                    };

            const string sqlMeat = @"
SELECT DISTINCT
    Ubicacion =
        UPPER(
            LTRIM(
                RTRIM(
                    ISNULL(
                        ux.Ubicacion,
                        ''
                    )
                )
            )
        )

FROM dbo.Produccion p WITH (NOLOCK)

CROSS APPLY
(
    SELECT TOP 1
        Ubicacion =
            pr.Referencia

    FROM dbo.ProduccionReferencia pr WITH (NOLOCK)

    WHERE pr.ProduccionId = p.ProduccionId
      AND pr.TipoReferenciaId = 16

    ORDER BY
        pr.FechaHora DESC
) ux

WHERE p.Estatus = 1
  AND CONVERT(VARCHAR(40), p.Almacen) = @Almacen
  AND p.ProduccionId NOT IN @ProduccionesExcluir
  AND UPPER(LTRIM(RTRIM(ISNULL(ux.Ubicacion, '')))) IN @Aliases
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.SalidaEmbarque se WITH (NOLOCK)
      WHERE se.ProduccionId = p.ProduccionId
  );
";

            await using var meat =
                new SqlConnection(
                    conexion.Cadena
                );

            await meat.OpenAsync(ct);

            var ocupadasMeat =
                await meat.QueryAsync<string>(
                    new CommandDefinition(
                        sqlMeat,
                        new
                        {
                            Almacen =
                                almacen.Codigo,

                            ProduccionesExcluir =
                                excluir,

                            Aliases =
                                aliases
                        },
                        cancellationToken: ct
                    )
                );

            foreach (var valor in ocupadasMeat)
            {
                var key =
                    (valor ?? "")
                        .Trim()
                        .ToUpperInvariant();

                if (aliasAUbicacion.TryGetValue(
                        key,
                        out var ubicacionId))
                {
                    resultado.Add(
                        ubicacionId
                    );
                }
            }

            return resultado;
        }


        private async Task<UbicacionDinamicaRecomendacionDto>
            ConstruirRecomendacionUbicacionDinamicaAsync(
                SurtidoAlmacenVM almacen,
                string codigoInventario,
                CancellationToken ct)
        {
            var codigoEscaneado =
                (codigoInventario ?? "")
                    .Trim()
                    .ToUpperInvariant();

            var layout =
                await ObtenerLayoutOperativoUbicacionAsync(
                    almacen.Codigo,
                    ct
                );

            if (layout == null)
            {
                throw new InvalidOperationException(
                    $"El almacén {almacen.Codigo} no tiene un layout operativo activo."
                );
            }

            var producciones =
                await BuscarUnidadInventarioUbicacionAsync(
                    almacen,
                    codigoEscaneado,
                    ct
                );

            if (producciones.Count == 0)
            {
                throw new InvalidOperationException(
                    "No se encontró la tarima o caja disponible en este almacén."
                );
            }

            var articulos =
                producciones
                    .Select(x =>
                        (x.Articulo ?? "")
                            .Trim()
                    )
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x)
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            if (articulos.Count != 1)
            {
                throw new InvalidOperationException(
                    "La unidad escaneada contiene más de un SKU. " +
                    "Debe revisarse antes de asignar una ubicación."
                );
            }

            var articulo =
                articulos[0];

            var datosArticulo =
                await ObtenerArticuloUbicacionDinamicaAsync(
                    articulo,
                    ct
                );

            if (datosArticulo == null)
            {
                throw new InvalidOperationException(
                    $"El SKU {articulo} no existe en ArticuloSap."
                );
            }

            if (string.IsNullOrWhiteSpace(
                    datosArticulo.Master))
            {
                throw new InvalidOperationException(
                    $"El SKU {articulo} no tiene U_MASTER configurado."
                );
            }

            var color =
                await ObtenerColorMasterUbicacionAsync(
                    almacen.Codigo,
                    layout.LayoutId,
                    datosArticulo.Master,
                    ct
                );

            if (color == null)
            {
                throw new InvalidOperationException(
                    $"El MASTER '{datosArticulo.Master}' no tiene un color activo " +
                    $"configurado para el almacén {almacen.Codigo}."
                );
            }

            var regla =
                await ObtenerReglaRotacionUbicacionAsync(
                    almacen.Codigo,
                    layout.LayoutId,
                    datosArticulo.Rotacion,
                    ct
                );

            if (regla == null)
            {
                throw new InvalidOperationException(
                    datosArticulo.Rotacion.HasValue
                        ? $"No existe una regla de rotación para el valor {datosArticulo.Rotacion} " +
                          $"en el almacén {almacen.Codigo}."
                        : $"El SKU {articulo} no tiene Rotacion y tampoco existe una regla " +
                          $"marcada para productos sin rotación en {almacen.Codigo}."
                );
            }

            var ubicaciones =
                await ObtenerUbicacionesColorDinamicasAsync(
                    almacen.Codigo,
                    layout.LayoutId,
                    color.ColorAlmacenId,
                    ct
                );

            if (ubicaciones.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No existen ubicaciones activas para el color " +
                    $"{color.NombreColor} ({color.CodigoColor}) en {almacen.Codigo}."
                );
            }

            var unidadCodigo =
                codigoEscaneado;

            var asignacionActual =
                await ObtenerAsignacionUnidadActivaAsync(
                    almacen.Codigo,
                    layout.LayoutId,
                    unidadCodigo,
                    ct
                );

            if (asignacionActual != null &&
                asignacionActual.ColorAlmacenId ==
                    color.ColorAlmacenId)
            {
                return new UbicacionDinamicaRecomendacionDto
                {
                    LayoutId = layout.LayoutId,
                    CodigoLayout = layout.CodigoLayout,
                    NombreLayout = layout.NombreLayout,

                    UnidadCodigo =
                        unidadCodigo,

                    Articulo =
                        articulo,

                    ProductoNombre =
                        datosArticulo.ProductoNombre,

                    Master =
                        datosArticulo.Master.Trim(),

                    Rotacion =
                        datosArticulo.Rotacion,

                    ColorAlmacenId =
                        color.ColorAlmacenId,

                    CodigoColor =
                        color.CodigoColor,

                    NombreColor =
                        color.NombreColor,

                    HexColor =
                        color.HexColor,

                    NombreReglaRotacion =
                        regla.NombreRegla,

                    ModoOrden =
                        regla.ModoOrden,

                    UbicacionId =
                        asignacionActual.UbicacionId,

                    CodigoUbicacion =
                        asignacionActual.CodigoUbicacion,

                    CodigoMapa3D =
                        asignacionActual.CodigoMapa3D,

                    YaUbicada =
                        true,

                    Producciones =
                        producciones
                };
            }

            var idsProduccion =
                producciones
                    .Select(x =>
                        x.ProduccionId
                    )
                    .Distinct()
                    .ToList();

            var ocupadas =
                await ObtenerUbicacionesOcupadasDinamicasAsync(
                    almacen,
                    ubicaciones,
                    unidadCodigo,
                    idsProduccion,
                    ct
                );

            var ordenadas =
                OrdenarUbicacionesDinamicas(
                    ubicaciones,
                    regla.ModoOrden
                );

            var recomendada =
                ordenadas.FirstOrDefault(x =>
                    !ocupadas.Contains(
                        x.UbicacionId
                    )
                );

            if (recomendada == null)
            {
                throw new InvalidOperationException(
                    $"No hay ubicaciones libres para el color " +
                    $"{color.NombreColor} ({color.CodigoColor}) en {almacen.Codigo}."
                );
            }

            return new UbicacionDinamicaRecomendacionDto
            {
                LayoutId = layout.LayoutId,
                CodigoLayout = layout.CodigoLayout,
                NombreLayout = layout.NombreLayout,

                UnidadCodigo =
                    unidadCodigo,

                Articulo =
                    articulo,

                ProductoNombre =
                    datosArticulo.ProductoNombre,

                Master =
                    datosArticulo.Master.Trim(),

                Rotacion =
                    datosArticulo.Rotacion,

                ColorAlmacenId =
                    color.ColorAlmacenId,

                CodigoColor =
                    color.CodigoColor,

                NombreColor =
                    color.NombreColor,

                HexColor =
                    color.HexColor,

                NombreReglaRotacion =
                    regla.NombreRegla,

                ModoOrden =
                    regla.ModoOrden,

                UbicacionId =
                    recomendada.UbicacionId,

                CodigoUbicacion =
                    recomendada.CodigoUbicacion,

                CodigoMapa3D =
                    recomendada.CodigoMapa3D,

                YaUbicada =
                    false,

                Producciones =
                    producciones
            };
        }


        private async Task CompletarUbicacionesDesdeSigoAsync(
            List<SurtidoPepsCajaVM> cajas,
            SurtidoAlmacenVM almacen,
            CancellationToken ct)
        {
            if (cajas == null ||
                cajas.Count == 0)
            {
                return;
            }

            var ids =
                cajas
                    .Select(x =>
                        x.ProduccionId
                    )
                    .Distinct()
                    .ToList();

            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    d.ProduccionId,

    CodigoUbicacion =
        ISNULL(
            u.CodigoUbicacion,
            ''
        )

FROM dbo.SurtidoUbicacionInventario a WITH (NOLOCK)

INNER JOIN dbo.SurtidoLayoutAlmacen l WITH (NOLOCK)
    ON l.Id = a.LayoutId
   AND l.CodigoAlmacen = a.CodigoAlmacen
   AND l.Activo = 1
   AND l.EsOperativo = 1

INNER JOIN dbo.SurtidoUbicacionInventarioDetalle d WITH (NOLOCK)
    ON d.AsignacionId = a.Id
   AND d.Activo = 1

INNER JOIN dbo.SurtidoUbicacionCatalogo u WITH (NOLOCK)
    ON u.Id = a.UbicacionId
   AND u.Activo = 1

WHERE a.Activo = 1
  AND a.CodigoAlmacen = @CodigoAlmacen
  AND d.ProduccionId IN @Ids;
";

            try
            {
                var rows =
                    (
                        await cn.QueryAsync<UbicacionDinamicaProduccionDto>(
                            new CommandDefinition(
                                sql,
                                new
                                {
                                    CodigoAlmacen =
                                        almacen.Codigo,

                                    Ids =
                                        ids
                                },
                                cancellationToken: ct
                            )
                        )
                    )
                    .GroupBy(x =>
                        x.ProduccionId
                    )
                    .ToDictionary(
                        g =>
                            g.Key,
                        g =>
                            g.First()
                             .CodigoUbicacion
                    );

                foreach (var caja in cajas)
                {
                    if (rows.TryGetValue(
                            caja.ProduccionId,
                            out var ubicacion)
                        &&
                        !string.IsNullOrWhiteSpace(
                            ubicacion))
                    {
                        caja.UbicacionOrigen =
                            ubicacion
                                .Trim()
                                .ToUpperInvariant();
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                // Durante una migración sin tablas nuevas,
                // se conserva ProduccionReferencia Tipo 16.
            }
        }


        private async Task LiberarProduccionUbicacionAsync(
            SurtidoAlmacenVM almacen,
            long produccionId,
            CancellationToken ct)
        {
            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
UPDATE d
SET
    Activo = 0,
    FechaSalida = SYSDATETIME()

FROM dbo.SurtidoUbicacionInventarioDetalle d

INNER JOIN dbo.SurtidoUbicacionInventario a
    ON a.Id = d.AsignacionId

WHERE a.Activo = 1
  AND a.CodigoAlmacen = @CodigoAlmacen
  AND d.Activo = 1
  AND d.ProduccionId = @ProduccionId;


UPDATE a
SET
    Activo = 0,
    FechaLiberacion = SYSDATETIME()

FROM dbo.SurtidoUbicacionInventario a

WHERE a.Activo = 1
  AND a.CodigoAlmacen = @CodigoAlmacen
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.SurtidoUbicacionInventarioDetalle d
      WHERE d.AsignacionId = a.Id
        AND d.Activo = 1
  );
";

            try
            {
                await cn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            CodigoAlmacen =
                                almacen.Codigo,

                            ProduccionId =
                                produccionId
                        },
                        cancellationToken: ct
                    )
                );
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                // Compatibilidad temporal de despliegue.
            }
        }


        // ============================================================
        // CANDIDATO DE PRODUCCIÓN PARA PEPS.
        //
        // Regla real:
        // 1) mismo Articulo;
        // 2) mismo Almacen;
        // 3) Produccion.Estatus = 1;
        // 4) todavía NO aparece en SalidaEmbarque;
        // 5) todavía NO está bajada/reservada activamente en SIGO;
        // 6) orden por fecha de lote/producción ASC.
        // ============================================================
        private async Task<Dictionary<string, List<SurtidoPepsCajaVM>>>
            ObtenerCajasPepsDisponiblesPorArticulosAsync(
                SurtidoAlmacenVM almacen,
                IReadOnlyDictionary<string, int> topPorArticulo,
                CancellationToken ct)
        {
            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            var limites =
                topPorArticulo
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Key)
                    )
                    .GroupBy(
                        x => x.Key.Trim(),
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToDictionary(
                        g => g.Key,
                        g => Math.Max(
                            50,
                            Math.Min(
                                g.Max(x => x.Value),
                                1000
                            )
                        ),
                        StringComparer.OrdinalIgnoreCase
                    );

            if (limites.Count == 0)
            {
                return new Dictionary<string, List<SurtidoPepsCajaVM>>(
                    StringComparer.OrdinalIgnoreCase
                );
            }

            var articulos =
                limites.Keys.ToList();

            var topMaximo =
                limites.Values.Max();

            // Una sola consulta MEAT para todos los SKU del pedido.
            // Antes se abría una conexión y se ejecutaba todo este SQL
            // por cada artículo, provocando el patrón N+1.
            const string sql = @"
;WITH Candidatos AS
(
    SELECT
        ProduccionId =
            CONVERT(
                BIGINT,
                p.ProduccionId
            ),

        CodigoEtiqueta =
            ISNULL(
                p.CodigoEtiqueta,
                ''
            ),

        Articulo =
            ISNULL(
                p.Articulo,
                ''
            ),

        Almacen =
            ISNULL(
                CONVERT(
                    VARCHAR(40),
                    p.Almacen
                ),
                ''
            ),

        Lote =
            ISNULL(
                l.Nombre,
                ''
            ),

        FechaProduccion =
            COALESCE(
                l.FechaProduccion,
                p.FechaProduccion
            ),

        PesoNeto =
            CONVERT(
                DECIMAL(18,4),
                ISNULL(
                    p.PesoNeto,
                    0
                )
            ),

        TarimaId =
            CONVERT(
                BIGINT,
                tx.TarimaId
            ),

        TarimaCodigo =
            ISNULL(
                tx.TarimaCodigo,
                ''
            ),

        UbicacionOrigen =
            ISNULL(
                ux.Ubicacion,
                ''
            ),

        OrdenArticulo =
            ROW_NUMBER() OVER
            (
                PARTITION BY p.Articulo
                ORDER BY
                    COALESCE(
                        l.FechaProduccion,
                        p.FechaProduccion
                    ) ASC,
                    p.ProduccionId ASC
            )

    FROM dbo.Produccion p WITH (NOLOCK)

    LEFT JOIN dbo.Lote l WITH (NOLOCK)
        ON l.LoteId = p.LoteId

    OUTER APPLY
    (
        SELECT TOP 1
            td.TarimaId,
            TarimaCodigo =
                ISNULL(
                    t.Nombre,
                    ''
                )
        FROM dbo.TarimaDetalle td WITH (NOLOCK)
        INNER JOIN dbo.Tarima t WITH (NOLOCK)
            ON t.TarimaId = td.TarimaId
        WHERE td.ProduccionId = p.ProduccionId
        ORDER BY
            td.FechaHora DESC,
            td.TarimaId DESC
    ) tx

    OUTER APPLY
    (
        SELECT TOP 1
            Ubicacion =
                LTRIM(
                    RTRIM(
                        ISNULL(
                            pr.Referencia,
                            ''
                        )
                    )
                )
        FROM dbo.ProduccionReferencia pr WITH (NOLOCK)
        WHERE pr.ProduccionId = p.ProduccionId
          AND pr.TipoReferenciaId = 16
        ORDER BY
            pr.FechaHora DESC
    ) ux

    WHERE p.Estatus = 1
      AND p.Articulo IN @Articulos
      AND CONVERT(VARCHAR(40), p.Almacen) = @Almacen
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.SalidaEmbarque se WITH (NOLOCK)
          WHERE se.ProduccionId = p.ProduccionId
      )
)
SELECT
    ProduccionId,
    CodigoEtiqueta,
    Articulo,
    Almacen,
    Lote,
    FechaProduccion,
    PesoNeto,
    TarimaId,
    TarimaCodigo,
    UbicacionOrigen
FROM Candidatos
WHERE OrdenArticulo <= @TopMaximo
ORDER BY
    Articulo,
    FechaProduccion ASC,
    ProduccionId ASC;
";

            await using var cn =
                new SqlConnection(
                    conexion.Cadena
                );

            await cn.OpenAsync(ct);

            var cajas =
                (
                    await cn.QueryAsync<SurtidoPepsCajaVM>(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                Articulos =
                                    articulos,

                                Almacen =
                                    almacen.Codigo,

                                TopMaximo =
                                    topMaximo
                            },
                            cancellationToken: ct
                        )
                    )
                )
                .ToList();

            // Las asignaciones activas de SIGO tienen prioridad sobre
            // ProduccionReferencia TipoReferenciaId = 16.
            // Si no existe asignación nueva, se conserva la ubicación legacy.
            await CompletarUbicacionesDesdeSigoAsync(
                cajas,
                almacen,
                ct
            );

            // Conserva exactamente el TOP original de cada SKU antes
            // de quitar reservadas, igual que hacía la consulta individual.
            cajas =
                cajas
                    .GroupBy(
                        x => (x.Articulo ?? "").Trim(),
                        StringComparer.OrdinalIgnoreCase
                    )
                    .SelectMany(g =>
                    {
                        if (!limites.TryGetValue(
                                g.Key,
                                out var limite))
                        {
                            limite =
                                50;
                        }

                        return g.Take(limite);
                    })
                    .ToList();

            // Una sola lectura en SIGO para excluir todas las cajas
            // ya reservadas/bajadas del pedido.
            var ids =
                cajas
                    .Select(x =>
                        x.ProduccionId
                    )
                    .Distinct()
                    .ToList();

            if (ids.Count > 0)
            {
                var sigo =
                    await ObtenerConexionSigoAsync(ct);

                const string sqlReservadas = @"
SELECT
    ProduccionId
FROM dbo.SurtidoBajada WITH (NOLOCK)
WHERE Activo = 1
  AND Planta = @Planta
  AND CodigoAlmacen = @CodigoAlmacen
  AND ProduccionId IN @Ids;
";

                var reservadas =
                    (
                        await sigo.QueryAsync<long>(
                            new CommandDefinition(
                                sqlReservadas,
                                new
                                {
                                    Planta =
                                        almacen.Planta,

                                    CodigoAlmacen =
                                        almacen.Codigo,

                                    Ids =
                                        ids
                                },
                                cancellationToken: ct
                            )
                        )
                    )
                    .ToHashSet();

                cajas =
                    cajas
                        .Where(x =>
                            !reservadas.Contains(
                                x.ProduccionId
                            )
                        )
                        .ToList();
            }

            var resultado =
                new Dictionary<string, List<SurtidoPepsCajaVM>>(
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var articulo in articulos)
            {
                resultado[articulo] =
                    cajas
                        .Where(x =>
                            string.Equals(
                                (x.Articulo ?? "").Trim(),
                                articulo,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .Take(
                            limites[articulo]
                        )
                        .ToList();
            }

            return resultado;
        }


        // ============================================================
        // CAJAS YA SURTIDAS OFICIALMENTE POR ARTÍCULO.
        // SalidaEmbarque es la fuente oficial del surtido Meat.
        // ============================================================
        private async Task<Dictionary<string, int>>
            ObtenerCajasSurtidasPorArticuloAsync(
                SurtidoAlmacenVM almacen,
                int solicitudSurtidoId,
                CancellationToken ct)
        {
            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            const string sql = @"
SELECT
    Articulo =
        ISNULL(
            p.Articulo,
            ''
        ),

    Cajas =
        COUNT_BIG(1)

FROM dbo.SalidaEmbarque se WITH (NOLOCK)

INNER JOIN dbo.Produccion p WITH (NOLOCK)
    ON p.ProduccionId = se.ProduccionId

WHERE se.SolicitudSurtidoId = @SolicitudSurtidoId
  AND CONVERT(VARCHAR(40), p.Almacen) = @Almacen

GROUP BY
    p.Articulo;
";

            await using var cn =
                new SqlConnection(
                    conexion.Cadena
                );

            await cn.OpenAsync(ct);

            var rows =
                (
                    await cn.QueryAsync<SurtidoConteoArticuloDto>(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                SolicitudSurtidoId =
                                    solicitudSurtidoId,

                                Almacen =
                                    almacen.Codigo
                            },
                            cancellationToken: ct
                        )
                    )
                )
                .ToList();

            return rows
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.Articulo
                    )
                )
                .GroupBy(
                    x => x.Articulo.Trim(),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(
                    g => g.Key,
                    g => checked(
                        (int)g.Sum(x => x.Cajas)
                    ),
                    StringComparer.OrdinalIgnoreCase
                );
        }


        // ============================================================
        // CAJAS BAJADAS A ZONA DE SURTIDO POR ARTÍCULO.
        // ============================================================
        private async Task<Dictionary<string, int>>
            ObtenerCajasBajadasPorArticuloAsync(
                SurtidoAlmacenVM almacen,
                int solicitudSurtidoId,
                CancellationToken ct)
        {
            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    Articulo,
    Cajas =
        COUNT_BIG(1)
FROM dbo.SurtidoBajada WITH (NOLOCK)
WHERE Activo = 1
  AND Estatus = 'BAJADA'
  AND Planta = @Planta
  AND CodigoAlmacen = @CodigoAlmacen
  AND SolicitudSurtidoId = @SolicitudSurtidoId
GROUP BY
    Articulo;
";

            var rows =
                (
                    await cn.QueryAsync<SurtidoConteoArticuloDto>(
                        new CommandDefinition(
                            sql,
                            new
                            {
                                Planta =
                                    almacen.Planta,

                                CodigoAlmacen =
                                    almacen.Codigo,

                                SolicitudSurtidoId =
                                    solicitudSurtidoId
                            },
                            cancellationToken: ct
                        )
                    )
                )
                .ToList();

            return rows
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.Articulo
                    )
                )
                .GroupBy(
                    x => x.Articulo.Trim(),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(
                    g => g.Key,
                    g => checked(
                        (int)g.Sum(x => x.Cajas)
                    ),
                    StringComparer.OrdinalIgnoreCase
                );
        }


        // ============================================================
        // Construye el pedido operativo con PEPS real.
        // ============================================================
        private async Task<SurtidoPedidoPepsVM>
            ConstruirPedidoPepsAsync(
                SurtidoAlmacenVM almacen,
                int solicitudSurtidoId,
                CancellationToken ct)
        {
            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            var vm =
                new SurtidoPedidoPepsVM
                {
                    Almacen =
                        almacen,

                    NombreConexion =
                        conexion.Nombre
                };

            try
            {
                vm.Pedido =
                    await ObtenerPedidoPorSolicitudAsync(
                        almacen,
                        solicitudSurtidoId,
                        ct
                    )
                    ?? new SurtidoPedidoVM
                    {
                        SolicitudSurtidoId =
                            solicitudSurtidoId
                    };

                var detalle =
                    await ObtenerDetalleSolicitudMeatAsync(
                        almacen,
                        solicitudSurtidoId,
                        ct
                    );

                detalle =
                    detalle
                        .Where(x =>
                            string.Equals(
                                NormalizarCodigoAlmacen(
                                    x.Almacen
                                ),
                                NormalizarCodigoAlmacen(
                                    almacen.Codigo
                                ),
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .GroupBy(x =>
                            (
                                (x.Articulo ?? "")
                                    .Trim()
                                    .ToUpperInvariant()
                                + "|"
                                + NormalizarCodigoAlmacen(
                                    x.Almacen
                                )
                            )
                        )
                        .Select(g =>
                            new SurtidoPedidoDetalleVM
                            {
                                SolicitudSurtidoId =
                                    solicitudSurtidoId,

                                Articulo =
                                    g.First().Articulo,

                                Almacen =
                                    g.First().Almacen,

                                Cantidad =
                                    g.Sum(x => x.Cantidad),

                                FechaHora =
                                    g.Min(x => x.FechaHora)
                            }
                        )
                        .OrderBy(x =>
                            x.Articulo
                        )
                        .ToList();

                await CompletarDetalleConArticuloSapAsync(
                    detalle,
                    ct
                );

                await CompletarDetalleConAlmacenAsync(
                    detalle,
                    ct
                );

                var surtidas =
                    await ObtenerCajasSurtidasPorArticuloAsync(
                        almacen,
                        solicitudSurtidoId,
                        ct
                    );

                var bajadas =
                    await ObtenerCajasBajadasPorArticuloAsync(
                        almacen,
                        solicitudSurtidoId,
                        ct
                    );

                var pendientesPorArticulo =
                    new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase
                    );

                var topPorArticulo =
                    new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase
                    );

                foreach (var d in detalle)
                {
                    var codigoArticulo =
                        (d.Articulo ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(codigoArticulo))
                    {
                        continue;
                    }

                    var cajasSolicitadas =
                        Math.Max(
                            0,
                            Convert.ToInt32(
                                Math.Ceiling(
                                    d.Cantidad
                                )
                            )
                        );

                    surtidas.TryGetValue(
                        codigoArticulo,
                        out var cajasSurtidas
                    );

                    bajadas.TryGetValue(
                        codigoArticulo,
                        out var cajasBajadas
                    );

                    var pendientes =
                        Math.Max(
                            0,
                            cajasSolicitadas
                            - cajasSurtidas
                            - cajasBajadas
                        );

                    pendientesPorArticulo[codigoArticulo] =
                        pendientes;

                    topPorArticulo[codigoArticulo] =
                        pendientes + 100;
                }

                // Una sola consulta PEPS para todos los SKU.
                var disponiblesPorArticulo =
                    await ObtenerCajasPepsDisponiblesPorArticulosAsync(
                        almacen,
                        topPorArticulo,
                        ct
                    );

                foreach (var d in detalle)
                {
                    var codigoArticulo =
                        (d.Articulo ?? "").Trim();

                    var cajasSolicitadas =
                        Math.Max(
                            0,
                            Convert.ToInt32(
                                Math.Ceiling(
                                    d.Cantidad
                                )
                            )
                        );

                    surtidas.TryGetValue(
                        codigoArticulo,
                        out var cajasSurtidas
                    );

                    bajadas.TryGetValue(
                        codigoArticulo,
                        out var cajasBajadas
                    );

                    pendientesPorArticulo.TryGetValue(
                        codigoArticulo,
                        out var pendientes
                    );

                    if (!disponiblesPorArticulo.TryGetValue(
                            codigoArticulo,
                            out var disponibles))
                    {
                        disponibles =
                            new List<SurtidoPepsCajaVM>();
                    }

                    var orden =
                        0;

                    foreach (var caja in disponibles)
                    {
                        orden++;

                        caja.OrdenPeps =
                            orden;

                        caja.ProductoNombre =
                            d.ProductoNombre;

                        caja.EsRecomendada =
                            orden <= pendientes;

                        if (
                            almacen.ObligaUbicacion
                            &&
                            string.IsNullOrWhiteSpace(
                                caja.UbicacionOrigen
                            )
                        )
                        {
                            caja.PuedeBajar =
                                false;

                            caja.MotivoBloqueo =
                                "La producción no tiene ubicación registrada en ProduccionReferencia (TipoReferenciaId = 16).";
                        }
                        else
                        {
                            caja.PuedeBajar =
                                caja.EsRecomendada;

                            caja.MotivoBloqueo =
                                caja.EsRecomendada
                                    ? ""
                                    : "Primero deben salir las cajas PEPS anteriores.";
                        }
                    }

                    vm.Productos.Add(
                        new SurtidoPepsProductoVM
                        {
                            Articulo =
                                d.Articulo,

                            ProductoNombre =
                                d.ProductoNombre,

                            Almacen =
                                d.Almacen,

                            AlmacenNombre =
                                d.AlmacenNombre,

                            CajasSolicitadas =
                                cajasSolicitadas,

                            CajasSurtidas =
                                cajasSurtidas,

                            CajasBajadas =
                                cajasBajadas,

                            CajasPendientes =
                                pendientes,

                            CajasDisponibles =
                                disponibles.Count,

                            KilosCaja =
                                d.KilosCaja,

                            Rotacion =
                                d.Rotacion,

                            Master =
                                d.Master,

                            Cajas =
                                disponibles
                        }
                    );
                }

                vm.Recomendada =
                    vm.Productos
                        .SelectMany(x => x.Cajas)
                        .Where(x =>
                            x.EsRecomendada
                        )
                        .OrderBy(x =>
                            x.FechaProduccion
                            ?? DateTime.MaxValue
                        )
                        .ThenBy(x =>
                            x.ProduccionId
                        )
                        .FirstOrDefault();

                vm.Pedido.CajasSolicitadas =
                    vm.CajasSolicitadas;

                vm.Pedido.CajasSurtidas =
                    vm.CajasSurtidas;

                vm.Pedido.CajasBajadas =
                    vm.CajasBajadas;
            }
            catch (Exception ex)
            {
                vm.Error =
                    ex.Message;
            }

            return vm;
        }


        // ============================================================
        // Caja específica. Siempre se reconstruye PEPS para evitar
        // confirmar una caja que dejó de ser la recomendada.
        // ============================================================
        private async Task<SurtidoPickingTareaVM>
            ConstruirPickingTareaAsync(
                SurtidoAlmacenVM almacen,
                int solicitudSurtidoId,
                long produccionId,
                CancellationToken ct)
        {
            var pedido =
                await ConstruirPedidoPepsAsync(
                    almacen,
                    solicitudSurtidoId,
                    ct
                );

            var caja =
                pedido.Productos
                    .SelectMany(x => x.Cajas)
                    .FirstOrDefault(x =>
                        x.ProduccionId == produccionId
                    );

            var vm =
                new SurtidoPickingTareaVM
                {
                    Almacen =
                        almacen,

                    Pedido =
                        pedido.Pedido,

                    NombreConexion =
                        pedido.NombreConexion,

                    Caja =
                        caja
                        ?? new SurtidoPepsCajaVM
                        {
                            ProduccionId =
                                produccionId
                        }
                };

            if (!string.IsNullOrWhiteSpace(
                    pedido.Error))
            {
                vm.Error =
                    pedido.Error;
            }
            else if (caja == null)
            {
                vm.Error =
                    "La caja ya no está disponible para picking. Actualiza el pedido.";
            }

            return vm;
        }


        // ============================================================
        // Registra PRODUCTO BAJADO en SIGO.
        //
        // NO INSERTA SalidaEmbarque.
        // Capturista hará la vinculación oficial posteriormente.
        // ============================================================
        private async Task RegistrarBajadaAsync(
            SurtidoAlmacenVM almacen,
            SurtidoPedidoVM pedido,
            SurtidoPepsCajaVM caja,
            string usuario,
            CancellationToken ct)
        {
            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
INSERT INTO dbo.SurtidoBajada
(
    Planta,
    CodigoAlmacen,
    SolicitudSurtidoId,
    ProduccionId,
    CodigoEtiqueta,
    TarimaId,
    TarimaCodigo,
    Articulo,
    Lote,
    FechaProduccion,
    PesoNeto,
    UbicacionOrigen,
    Estatus,
    Activo,
    UsuarioBaja,
    FechaBaja
)
VALUES
(
    @Planta,
    @CodigoAlmacen,
    @SolicitudSurtidoId,
    @ProduccionId,
    @CodigoEtiqueta,
    @TarimaId,
    @TarimaCodigo,
    @Articulo,
    @Lote,
    @FechaProduccion,
    @PesoNeto,
    @UbicacionOrigen,
    'BAJADA',
    1,
    @UsuarioBaja,
    SYSDATETIME()
);
";

            await cn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Planta =
                            almacen.Planta,

                        CodigoAlmacen =
                            almacen.Codigo,

                        SolicitudSurtidoId =
                            pedido.SolicitudSurtidoId,

                        ProduccionId =
                            caja.ProduccionId,

                        CodigoEtiqueta =
                            caja.CodigoEtiqueta,

                        TarimaId =
                            caja.TarimaId,

                        TarimaCodigo =
                            string.IsNullOrWhiteSpace(
                                caja.TarimaCodigo
                            )
                                ? null
                                : caja.TarimaCodigo,

                        Articulo =
                            caja.Articulo,

                        Lote =
                            caja.Lote,

                        FechaProduccion =
                            caja.FechaProduccion,

                        PesoNeto =
                            caja.PesoNeto,

                        UbicacionOrigen =
                            string.IsNullOrWhiteSpace(
                                caja.UbicacionOrigen
                            )
                                ? null
                                : caja.UbicacionOrigen,

                        UsuarioBaja =
                            usuario
                    },
                    cancellationToken: ct
                )
            );

            // La caja ya salió físicamente del rack.
            // Si era la última producción activa de la unidad,
            // también se libera la cabecera/ubicación.
            await LiberarProduccionUbicacionAsync(
                almacen,
                caja.ProduccionId,
                ct
            );
        }


        // ============================================================
        // BAJADA DE TARIMA: registra una fila por caja en SIGO.
        // Nunca inserta directamente en Meat/SalidaEmbarque.
        // Las cajas fuera de PEPS / sin ubicación / fuera del pedido
        // NO se incluyen en la bajada.
        // ============================================================
        private async Task<string> RegistrarBajadaTarimaAsync(
            SurtidoAlmacenVM almacen,
            int solicitudId,
            long produccionSeleccionada,
            string codigoTarima,
            string usuario,
            CancellationToken ct)
        {
            var conexionMeat = ObtenerCadenaMeatPorAlmacen(almacen);
            await using var meat = new SqlConnection(conexionMeat.Cadena);
            await meat.OpenAsync(ct);

            const string sqlTarima = @"
SELECT TOP 1 t.TarimaId
FROM dbo.Tarima t
WHERE UPPER(LTRIM(RTRIM(t.Nombre))) = @Codigo
  AND t.Estatus = 1;";
            var tarimaId = await meat.QueryFirstOrDefaultAsync<int?>(
                new CommandDefinition(sqlTarima, new { Codigo = codigoTarima },
                    cancellationToken: ct));
            if (!tarimaId.HasValue)
                throw new InvalidOperationException("Tarima inexistente o inactiva en Meat.");

            // Identificar todas las cajas realmente vigentes de la tarima.
            const string sqlContenido = @"
SELECT DISTINCT p.ProduccionId
FROM dbo.TarimaDetalle td
INNER JOIN dbo.Produccion p ON p.ProduccionId = td.ProduccionId
WHERE td.TarimaId = @TarimaId
  AND p.Estatus = 1
  AND p.Almacen = @Almacen
  AND NOT EXISTS
  (SELECT 1 FROM dbo.SalidaEmbarque se WHERE se.ProduccionId = p.ProduccionId);";
            var contenido = (await meat.QueryAsync<long>(new CommandDefinition(
                sqlContenido, new { TarimaId = tarimaId.Value, Almacen = almacen.Codigo },
                cancellationToken: ct))).ToHashSet();
            if (contenido.Count == 0)
                throw new InvalidOperationException("No hay cajas vigentes disponibles en esta tarima y almacén.");

            // Recalcular selección PEPS y la cantidad todavía pendiente.
            var picking = await ConstruirPedidoPepsAsync(almacen, solicitudId, ct);
            if (!string.IsNullOrWhiteSpace(picking.Error))
                throw new InvalidOperationException(picking.Error);
            var cajasTarima = picking.Productos.SelectMany(x => x.Cajas)
                .Where(x => contenido.Contains(x.ProduccionId)
                    && string.Equals((x.TarimaCodigo ?? "").Trim(),
                        codigoTarima, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!cajasTarima.Any(x => x.ProduccionId == produccionSeleccionada))
                throw new InvalidOperationException("La tarima no coincide con la caja seleccionada del pedido.");

            var candidatas = cajasTarima
                .Where(x => x.EsRecomendada && x.PuedeBajar)
                .GroupBy(x => x.ProduccionId).Select(g => g.First())
                .OrderBy(x => x.FechaProduccion ?? DateTime.MaxValue)
                .ThenBy(x => x.ProduccionId).ToList();
            if (candidatas.Count == 0)
                throw new InvalidOperationException("Ninguna caja de esta tarima cumple PEPS, ubicación y pendientes del pedido.");

            // Aplicar todos los INSERT en UNA transacción SIGO.
            // Una falla revierte la bajada entera.
            // Reutilizar la conexión EF Core de SIGO ya autenticada.
            // No copiar ConnectionString: con Persist Security Info=false
            // puede ocultar la contraseña y fallar el login SQL (18456).
            // La conexión pertenece al DbContext y NO debe cerrarse aquí.
            var sigo = (System.Data.Common.DbConnection)
                await ObtenerConexionSigoAsync(ct);
            await using var tx = await sigo.BeginTransactionAsync(ct);
            var idsInsertados = new List<long>();
            try
            {
                var bloqueo = await sigo.ExecuteScalarAsync<int>(new CommandDefinition(@"
DECLARE @Resultado int;
EXEC @Resultado = sys.sp_getapplock
  @Resource = @Recurso,
  @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
SELECT @Resultado;", new { Recurso = $"SIGO_BAJADA_SOLICITUD_{almacen.Planta}_{solicitudId}" },
                    transaction: tx, cancellationToken: ct));
                if (bloqueo < 0)
                    throw new InvalidOperationException("No se obtuvo bloqueo de la solicitud.");

                // Se bloquea la solicitud contra cambios simultáneos en esta ruta.
                // Volver a verificar la prioridad justo antes de llamar a este método
                // (se realiza en ConfirmarBajada).
                const string sqlInsert = @"
IF NOT EXISTS
(
 SELECT 1 FROM dbo.SurtidoBajada WITH (UPDLOCK, HOLDLOCK)
 WHERE ProduccionId = @ProduccionId AND Activo = 1
)
BEGIN
 INSERT INTO dbo.SurtidoBajada
 (Planta, CodigoAlmacen, SolicitudSurtidoId, ProduccionId,
  CodigoEtiqueta, TarimaId, TarimaCodigo, Articulo, Lote,
  FechaProduccion, PesoNeto, UbicacionOrigen, Estatus,
  Activo, UsuarioBaja, FechaBaja)
 VALUES
 (@Planta, @CodigoAlmacen, @SolicitudSurtidoId, @ProduccionId,
  @CodigoEtiqueta, @TarimaId, @TarimaCodigo, @Articulo, @Lote,
  @FechaProduccion, @PesoNeto, @UbicacionOrigen, 'BAJADA',
  1, @UsuarioBaja, SYSDATETIME());
 SELECT 1;
END
ELSE SELECT 0;";
                foreach (var caja in candidatas)
                {
                    var insertada = await sigo.ExecuteScalarAsync<int>(new CommandDefinition(
                        sqlInsert, new
                        {
                            Planta = almacen.Planta,
                            CodigoAlmacen = almacen.Codigo,
                            SolicitudSurtidoId = solicitudId,
                            ProduccionId = caja.ProduccionId,
                            CodigoEtiqueta = caja.CodigoEtiqueta,
                            TarimaId = tarimaId.Value,
                            TarimaCodigo = codigoTarima,
                            Articulo = caja.Articulo,
                            Lote = caja.Lote,
                            FechaProduccion = caja.FechaProduccion,
                            PesoNeto = caja.PesoNeto,
                            UbicacionOrigen = string.IsNullOrWhiteSpace(caja.UbicacionOrigen)
                                ? null : caja.UbicacionOrigen,
                            UsuarioBaja = usuario
                        }, transaction: tx, cancellationToken: ct));
                    if (insertada == 1)
                        idsInsertados.Add(caja.ProduccionId);
                }
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }

            // Reutilizar la liberación física del módulo existente.
            // Se ejecuta DESPUÉS del commit; un error debe revisarse
            // operativamente, sin volver a insertar cajas duplicadas.
            var fallasUbicacion = 0;
            foreach (var produccionId in idsInsertados)
            {
                try { await LiberarProduccionUbicacionAsync(almacen, produccionId, ct); }
                catch (Exception ex)
                {
                    fallasUbicacion++;
                    System.Diagnostics.Trace.TraceError($"Error liberando ubicación {produccionId}: {ex}");
                }
            }

            var excluidas = contenido.Count - candidatas.Count;
            return $"Tarima {codigoTarima}: {idsInsertados.Count} cajas bajadas; " +
                $"{candidatas.Count - idsInsertados.Count} ya reservadas; " +
                $"{excluidas} fuera de PEPS/pedido. " +
                (fallasUbicacion > 0 ? $"ATENCIÓN: {fallasUbicacion} liberaciones de ubicación requieren revisión." :
                    "Quedan pendientes de captura en SIGO.");
        }

        // ============================================================
        // Cola para Capturista.
        // ============================================================
        private async Task<List<SurtidoBajadaFilaVM>>
            ObtenerZonaSurtidoAsync(
                SurtidoAlmacenVM almacen,
                CancellationToken ct)
        {
            var cn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    Id,
    SolicitudSurtidoId,
    ProduccionId,
    CodigoEtiqueta,
    TarimaId,
    TarimaCodigo =
        ISNULL(
            TarimaCodigo,
            ''
        ),
    Articulo,
    Lote =
        ISNULL(
            Lote,
            ''
        ),
    FechaProduccion,
    PesoNeto,
    UbicacionOrigen =
        ISNULL(
            UbicacionOrigen,
            ''
        ),
    Estatus,
    UsuarioBaja,
    FechaBaja
FROM dbo.SurtidoBajada WITH (NOLOCK)
WHERE Activo = 1
  AND Estatus = 'BAJADA'
  AND Planta = @Planta
  AND CodigoAlmacen = @CodigoAlmacen
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.SurtidoCaptura sc
      WHERE sc.SolicitudSurtidoId = SurtidoBajada.SolicitudSurtidoId
        AND sc.Planta = @Planta
        AND sc.CodigoAlmacen = @CodigoAlmacen
        AND sc.Estatus IN ('VALIDADO','INTEGRANDO','INTEGRADO')
  )
ORDER BY
    FechaProduccion ASC,
    Id ASC;
";

            return (
                await cn.QueryAsync<SurtidoBajadaFilaVM>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Planta =
                                almacen.Planta,

                            CodigoAlmacen =
                                almacen.Codigo
                        },
                        cancellationToken: ct
                    )
                )
            ).ToList();
        }


        // ============================================================
        // VIEWMODEL PEDIDOS
        // ============================================================
        private async Task<SurtidoModuloPedidosVM>
            ConstruirModuloPedidosAsync(
                SurtidoAlmacenVM almacen,
                DateTime? fechaInicio,
                DateTime? fechaFin,
                CancellationToken ct)
        {
            var hoy =
                DateTime.Today;

            var inicio =
                fechaInicio?.Date
                ?? new DateTime(
                    hoy.Year,
                    hoy.Month,
                    1
                );

            var fin =
                fechaFin?.Date
                ?? hoy;

            if (fin < inicio)
            {
                (inicio, fin) =
                    (fin, inicio);
            }

            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            var vm =
                new SurtidoModuloPedidosVM
                {
                    Almacen =
                        almacen,

                    FechaInicio =
                        inicio,

                    FechaFin =
                        fin,

                    NombreConexion =
                        conexion.Nombre
                };

            try
            {
                vm.Pedidos =
                    await ObtenerPedidosPendientesAsync(
                        almacen,
                        inicio,
                        fin,
                        ct
                    );
            }
            catch (SqlException ex)
            {
                // Evita pantalla amarilla / excepción no controlada.
                // No mostramos la cadena ni credenciales.
                vm.Error =
                    $"No fue posible conectar con {conexion.Nombre}. " +
                    $"Servidor SQL no disponible o configuración incorrecta. " +
                    $"Detalle SQL: {ex.Number} - {ex.Message}";
            }

            return vm;
        }



        // ============================================================
        // PICKING MULTI-ALMACÉN
        //
        // Por defecto consulta TODOS los almacenes autorizados del usuario.
        //
        // Si filtroAlmacen trae un código:
        //     consulta sólo ese almacén.
        //
        // Cada almacén conserva:
        //     - su Código;
        //     - su Planta;
        //     - su CadenaMeatP1 / CadenaMeatTIF;
        //     - su regla de ubicación;
        //     - su contexto PEPS.
        //
        // No se mezclan físicamente las cajas entre almacenes.
        // Sólo se unifica la bandeja visual de pedidos.
        // ============================================================
        private async Task<SurtidoModuloPedidosVM>
            ConstruirModuloPedidosMultiAlmacenAsync(
                List<SurtidoAlmacenVM> almacenesAutorizados,
                string? filtroAlmacen,
                DateTime? fechaInicio,
                DateTime? fechaFin,
                CancellationToken ct)
        {
            var hoy =
                DateTime.Today;

            var inicio =
                fechaInicio?.Date
                ?? new DateTime(
                    hoy.Year,
                    hoy.Month,
                    1
                );

            var fin =
                fechaFin?.Date
                ?? hoy;

            if (fin < inicio)
            {
                (inicio, fin) =
                    (fin, inicio);
            }

            var filtro =
                NormalizarCodigoAlmacen(
                    filtroAlmacen
                );

            var esTodos =
                string.IsNullOrWhiteSpace(
                    filtro
                )
                ||
                filtro == "TODOS";

            var almacenesConsulta =
                esTodos
                    ? almacenesAutorizados.ToList()
                    : almacenesAutorizados
                        .Where(a =>
                            string.Equals(
                                NormalizarCodigoAlmacen(
                                    a.Codigo
                                ),
                                filtro,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .ToList();

            var vm =
                new SurtidoModuloPedidosVM
                {
                    Almacenes =
                        almacenesAutorizados,

                    FiltroAlmacen =
                        esTodos
                            ? ""
                            : filtro,

                    Almacen =
                        almacenesConsulta.FirstOrDefault()
                        ?? almacenesAutorizados.FirstOrDefault()
                        ?? new SurtidoAlmacenVM(),

                    FechaInicio =
                        inicio,

                    FechaFin =
                        fin,

                    TotalAlmacenesConsultados =
                        almacenesConsulta.Count
                };

            var pedidos =
                new List<SurtidoPedidoVM>();

            var conexiones =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

            var errores =
                new List<string>();


            foreach (var almacen in almacenesConsulta)
            {
                try
                {
                    var conexion =
                        ObtenerCadenaMeatPorAlmacen(
                            almacen
                        );

                    conexiones.Add(
                        conexion.Nombre
                    );

                    // Cola completa del almacén: los filtros de fecha
                    // NO pueden cambiar quién es Prioridad 1.
                    var pedidosAlmacen =
                        await ObtenerPedidosPendientesAsync(
                            almacen,
                            null,
                            null,
                            ct
                        );

                    pedidos.AddRange(
                        pedidosAlmacen
                    );
                }
                catch (SqlException ex)
                {
                    errores.Add(
                        $"{almacen.Codigo} - {almacen.Nombre}: " +
                        $"SQL {ex.Number} - {ex.Message}"
                    );
                }
                catch (InvalidOperationException ex)
                {
                    errores.Add(
                        $"{almacen.Codigo} - {almacen.Nombre}: {ex.Message}"
                    );
                }
            }


            // Un mismo SolicitudSurtidoId puede aparecer en varios almacenes.
            // Eso es correcto: cada tarjeta representa el tramo operativo
            // de ese pedido dentro de un almacén específico.
            vm.Pedidos =
                pedidos
                    .OrderBy(x =>
                        x.AlmacenNombre
                    )
                    .ThenBy(x =>
                        x.FechaHora
                    )
                    .ThenBy(x =>
                        x.SolicitudSurtidoId
                    )
                    .ToList();


            vm.NombreConexion =
                conexiones.Count == 0
                    ? "-"
                    : string.Join(
                        " + ",
                        conexiones.OrderBy(x => x)
                    );


            if (errores.Count > 0)
            {
                vm.Error =
                    "Algunos almacenes no pudieron consultarse: "
                    + string.Join(
                        " | ",
                        errores
                    );
            }


            return vm;
        }


        // ============================================================
        // INICIO
        // ============================================================
        [HttpGet("/Surtido")]
        public IActionResult Index()
        {
            return RedirectToAction(
                nameof(Surtido_cedis)
            );
        }


        // ============================================================
        // /Surtido/Surtido_cedis
        // ============================================================
        [HttpGet("/Surtido/Surtido_cedis")]
        public async Task<IActionResult> Surtido_cedis(
            string? almacen = null,
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            if (plantas.Count == 0)
            {
                return Content(
                    $"""
                    El usuario {usuario.Usuario} no tiene Planta1/TIF
                    configurado en UsuarioSerie -> Series.
                    """,
                    "text/plain"
                );
            }

            var almacenes =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            await AplicarConfiguracionUbicacionAsync(
                almacenes,
                ct
            );

            if (almacenes.Count == 0)
            {
                return Content(
                    $"""
                    El usuario {usuario.Usuario} no tiene almacenes
                    disponibles para sus Series y AlmacenesPermitidos.
                    """,
                    "text/plain"
                );
            }

            var almacenActivo =
                BuscarAlmacenPermitido(
                    almacenes,
                    almacen
                )
                ?? almacenes.First();

            almacenActivo.EsActivo =
                true;

            var vm =
                new SurtidoInicioVM
                {
                    UsuarioId =
                        usuario.Id,

                    Login =
                        usuario.Usuario,

                    NombreUsuario =
                        usuario.Nombre,

                    PlantasPermitidas =
                        plantas,

                    Almacenes =
                        almacenes,

                    PlantaActiva =
                        almacenActivo.Planta,

                    AlmacenActivoCodigo =
                        almacenActivo.Codigo,

                    AlmacenActivoNombre =
                        almacenActivo.Nombre,

                    ClasificacionActiva =
                        almacenActivo.Clasificacion,

                    Layout3DConfirmado =
                        almacenActivo.TieneLayout3D,

                    PuedeMontacargas =
                        usuario.LogisticaMontacarguista,

                    PuedeCapturar =
                        usuario.LogisticaCapturista,

                    PuedeUbicar =
                        usuario.LogisticaUbicador,

                    PuedeCoordinar =
                        usuario.LogisticaCoordinador
                };

            return View(
                "~/Views/Surtido/Surtido_cedis.cshtml",
                vm
            );
        }


        // ============================================================
        // VALIDAR MÓDULO
        // ============================================================
        private async Task<(
            bool Ok,
            IActionResult? Error,
            UsuarioSurtidoDto? Usuario,
            SurtidoAlmacenVM? Almacen)>
            ValidarModuloAsync(
                string? almacenCodigo,
                string modulo,
                CancellationToken ct)
        {
            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return (
                    false,
                    Forbid(),
                    null,
                    null
                );
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenes =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            await AplicarConfiguracionUbicacionAsync(
                almacenes,
                ct
            );

            var almacen =
                BuscarAlmacenPermitido(
                    almacenes,
                    almacenCodigo
                );

            if (almacen == null)
            {
                return (
                    false,
                    StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            ok = false,
                            message =
                                "El almacén no está permitido para este usuario."
                        }
                    ),
                    usuario,
                    null
                );
            }

            var permitido =
                (modulo ?? "")
                .Trim()
                .ToUpperInvariant()
                switch
                {
                    "MONTACARGAS" =>
                        usuario.LogisticaMontacarguista,

                    "CAPTURA" =>
                        usuario.LogisticaCapturista,

                    "UBICAR" =>
                        usuario.LogisticaUbicador,

                    "COORDINAR" =>
                        usuario.LogisticaCoordinador,

                    _ =>
                        false
                };

            if (!permitido)
            {
                return (
                    false,
                    StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            ok = false,
                            message =
                                $"No tienes permiso de {modulo}."
                        }
                    ),
                    usuario,
                    almacen
                );
            }

            return (
                true,
                null,
                usuario,
                almacen
            );
        }


        // ============================================================
        // RESUMEN REAL DE BAJADAS PARA PICKING (SOLO LECTURA)
        // Independiente de CajasPendientes, para que una tarima
        // ya bajada siga visible mientras no se cierre la jornada.
        // ============================================================
        private async Task<List<SurtidoPickingBajadaResumenVM>>
            ObtenerResumenBajadasPickingAsync(
                List<SurtidoAlmacenVM> almacenes,
                CancellationToken ct)
        {
            var resultado = new List<SurtidoPickingBajadaResumenVM>();
            if (almacenes.Count == 0) return resultado;

            // Se usa la misma conexion SIGO del controlador actual.
            // No se dispone: la conexion pertenece a AppDbContext.
            var cn = await ObtenerConexionSigoAsync(ct);
            const string sql = @"
SELECT TOP (150)
    b.Planta,
    b.CodigoAlmacen,
    b.SolicitudSurtidoId,
    ISNULL(b.TarimaCodigo, '') AS TarimaCodigo,
    b.Articulo AS SKU,
    COUNT(DISTINCT b.ProduccionId) AS Cajas,
    CONVERT(decimal(18,3), SUM(ISNULL(b.PesoNeto,0))) AS Kg,
    ISNULL(MAX(b.UsuarioBaja), '') AS UltimoOperador,
    MAX(b.FechaBaja) AS UltimaBajada,
    CONVERT(bit, CASE WHEN EXISTS (
        SELECT 1 FROM dbo.SurtidoCaptura sc
        WHERE sc.SolicitudSurtidoId = b.SolicitudSurtidoId
          AND sc.Planta = b.Planta
          AND sc.CodigoAlmacen = b.CodigoAlmacen
          AND sc.Estatus IN ('VALIDADO','INTEGRANDO','INTEGRADO')
    ) THEN 1 ELSE 0 END) AS Validado
FROM dbo.SurtidoBajada b
WHERE b.Activo = 1
  AND b.Estatus = 'BAJADA'
  AND b.Planta = @Planta
  AND b.CodigoAlmacen = @Almacen
  AND b.FechaBaja >= DATEADD(DAY,-14,GETDATE())
GROUP BY b.Planta,b.CodigoAlmacen,b.SolicitudSurtidoId,
         b.TarimaCodigo,b.Articulo
ORDER BY MAX(b.FechaBaja) DESC;
";
            foreach (var a in almacenes)
            {
                var filas = await cn.QueryAsync<SurtidoPickingBajadaResumenVM>(
                    new CommandDefinition(sql,
                        new { Planta = a.Planta, Almacen = a.Codigo },
                        cancellationToken: ct));
                resultado.AddRange(filas);
            }
            return resultado
                .OrderByDescending(x => x.UltimaBajada)
                .Take(150).ToList();
        }

        // ============================================================
        // PICKING / MONTACARGUISTA
        // ============================================================
        [HttpGet("/Surtido/Picking")]
        public async Task<IActionResult> Picking(
            string? almacen = null,
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null,
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            if (!usuario.LogisticaMontacarguista)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "No tienes permiso de Montacarguista."
                    }
                );
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenes =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            await AplicarConfiguracionUbicacionAsync(
                almacenes,
                ct
            );

            if (almacenes.Count == 0)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "No tienes almacenes autorizados para Picking."
                    }
                );
            }

            SurtidoAlmacenVM? almacenFiltro =
                null;

            if (!string.IsNullOrWhiteSpace(almacen))
            {
                almacenFiltro =
                    BuscarAlmacenPermitido(
                        almacenes,
                        almacen
                    );

                if (almacenFiltro == null)
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            ok = false,
                            message =
                                "El almacén solicitado no está permitido para este usuario."
                        }
                    );
                }
            }

            var vm =
                await ConstruirModuloPedidosMultiAlmacenAsync(
                    almacenes,
                    almacen,
                    fechaInicio,
                    fechaFin,
                    ct
                );

            vm.NombreUsuario =
                !string.IsNullOrWhiteSpace(
                    usuario.Nombre
                )
                    ? usuario.Nombre
                    : usuario.Usuario;

            // ========================================================
            // PRIORIDADES SIN VOLVER A CONSULTAR MEAT.
            //
            // vm.Pedidos ya contiene la cola obtenida arriba.
            // Sólo leemos la tabla ligera de prioridades en SIGO.
            // ========================================================
            var prioridadesPicking =
                await ConstruirPrioridadesPickingDesdePedidosAsync(
                    almacenes,
                    vm.Pedidos,
                    ct
                );

            ViewBag.PrioridadesPedido =
                prioridadesPicking;

            vm.Pedidos =
                vm.Pedidos
                    .OrderBy(x =>
                        x.AlmacenNombre
                    )
                    .ThenBy(x =>
                    {
                        var clave =
                            $"{NormalizarCodigoAlmacen(x.AlmacenCodigo)}|{x.SolicitudSurtidoId}";

                        return prioridadesPicking.TryGetValue(
                            clave,
                            out var prioridad
                        )
                            ? prioridad
                            : int.MaxValue;
                    })
                    .ThenBy(x =>
                        x.FechaHora
                    )
                    .ThenBy(x =>
                        x.SolicitudSurtidoId
                    )
                    .ToList();

            var almacenesSincronizacion =
                almacenFiltro == null
                    ? almacenes
                    : new List<SurtidoAlmacenVM>
                    {
                        almacenFiltro
                    };

            // Firma inicial para que el JavaScript pueda detectar
            // cambios posteriores sin tocar MEAT.
            ViewBag.FirmaPrioridades =
                await ObtenerFirmaSincronizacionPickingAsync(
                    almacenesSincronizacion,
                    ct
                );

            // Las bajadas se consultan por separado: un pedido puede
            // desaparecer de Pendientes aun cuando sus cajas ya se bajaron.
            try
            {
                ViewBag.BajadasPicking =
                    await ObtenerResumenBajadasPickingAsync(
                        almacenesSincronizacion, ct);
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Trace.TraceError(
                    $"No fue posible consultar resumen de bajadas Picking: {ex}");
                ViewBag.BajadasPicking =
                    new List<SurtidoPickingBajadaResumenVM>();
                ViewBag.ErrorBajadasPicking =
                    "No fue posible consultar las bajadas de SIGO.";
            }

            return View(
                "~/Views/Surtido/Picking.cshtml",
                vm
            );
        }



        // ============================================================
        // PRIORIDADES DE PICKING EN TIEMPO CASI REAL
        //
        // La vista consulta este endpoint periódicamente.
        // Si Coordinación modifica una prioridad, Picking detecta el
        // cambio y se recarga automáticamente.
        // ============================================================
        [HttpGet("/Surtido/PrioridadesPicking")]
        public async Task<IActionResult> PrioridadesPicking(
            string? almacen = null,
            CancellationToken ct = default)
        {
            Response.Headers.CacheControl =
                "no-store, no-cache, must-revalidate";

            Response.Headers.Pragma =
                "no-cache";

            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Unauthorized(
                    new
                    {
                        ok = false
                    }
                );
            }

            if (!usuario.LogisticaMontacarguista)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false
                    }
                );
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenes =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            if (!string.IsNullOrWhiteSpace(almacen))
            {
                var almacenFiltro =
                    BuscarAlmacenPermitido(
                        almacenes,
                        almacen
                    );

                if (almacenFiltro == null)
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            ok = false
                        }
                    );
                }

                almacenes =
                    new List<SurtidoAlmacenVM>
                    {
                        almacenFiltro
                    };
            }

            // Este endpoint ya NO reconstruye colas ni cuenta cajas.
            // Sólo lee prioridades de SIGO y una firma mínima de IDs
            // pendientes en MEAT para detectar pedidos nuevos/cerrados.
            var firma =
                await ObtenerFirmaSincronizacionPickingAsync(
                    almacenes,
                    ct
                );

            return Json(
                new
                {
                    ok = true,
                    firma
                }
            );
        }


        // ============================================================
        // PEDIDO ACTIVO - VISTA V7 + PEPS REAL
        // ============================================================
        [HttpGet("/Surtido/PedidoPicking")]
        public async Task<IActionResult> PedidoPicking(
            string almacen,
            int solicitudSurtidoId,
            CancellationToken ct = default)
        {
            var acceso =
                await ValidarModuloAsync(
                    almacen,
                    "MONTACARGAS",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            if (solicitudSurtidoId <= 0)
            {
                return BadRequest(
                    "SolicitudSurtidoId inválido."
                );
            }

            // ====================================================
            // BLOQUEO REAL DE PRIORIDAD.
            // Aunque escriban otra SolicitudSurtidoId en la URL,
            // sólo puede abrirse la Prioridad 1 del almacén.
            // ====================================================
            try
            {
                var prioridad =
                    await ValidarPrioridadPickingAsync(
                        acceso.Almacen!,
                        solicitudSurtidoId,
                        ct
                    );

                if (!prioridad.Ok)
                {
                    TempData["Error"] =
                        prioridad.Mensaje;

                    return RedirectToAction(
                        nameof(Picking),
                        new
                        {
                            almacen = acceso.Almacen!.Codigo
                        }
                    );
                }
            }
            catch (SqlException)
            {
                TempData["Error"] =
                    "No fue posible validar la prioridad de picking. Intenta nuevamente.";

                return RedirectToAction(
                    nameof(Picking),
                    new
                    {
                        almacen = acceso.Almacen!.Codigo
                    }
                );
            }

            var vm =
                await ConstruirPedidoPepsAsync(
                    acceso.Almacen!,
                    solicitudSurtidoId,
                    ct
                );

            return View(
                "~/Views/Surtido/PedidoPicking.cshtml",
                vm
            );
        }


        // ============================================================
        // TAREA OPERATIVA - ESCANEO / BAJADA
        // ============================================================
        [HttpGet("/Surtido/PickingTarea")]
        public async Task<IActionResult> PickingTarea(
            string almacen,
            int solicitudSurtidoId,
            long produccionId,
            CancellationToken ct = default)
        {
            var acceso =
                await ValidarModuloAsync(
                    almacen,
                    "MONTACARGAS",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            if (solicitudSurtidoId <= 0 || produccionId <= 0)
            {
                return BadRequest(
                    "SolicitudSurtidoId/ProduccionId inválido."
                );
            }

            try
            {
                var prioridad =
                    await ValidarPrioridadPickingAsync(
                        acceso.Almacen!,
                        solicitudSurtidoId,
                        ct
                    );

                if (!prioridad.Ok)
                {
                    TempData["Error"] =
                        prioridad.Mensaje;

                    return RedirectToAction(
                        nameof(Picking),
                        new
                        {
                            almacen = acceso.Almacen!.Codigo
                        }
                    );
                }
            }
            catch (SqlException)
            {
                TempData["Error"] =
                    "No fue posible validar la prioridad de picking. Intenta nuevamente.";

                return RedirectToAction(
                    nameof(Picking),
                    new
                    {
                        almacen = acceso.Almacen!.Codigo
                    }
                );
            }

            var vm =
                await ConstruirPickingTareaAsync(
                    acceso.Almacen!,
                    solicitudSurtidoId,
                    produccionId,
                    ct
                );

            return View(
                "~/Views/Surtido/PickingTarea.cshtml",
                vm
            );
        }


        public sealed class ConfirmarBajadaReq
        {
            public string Almacen { get; set; } = "";

            public int SolicitudSurtidoId { get; set; }

            public long ProduccionId { get; set; }

            public string CodigoEscaneado { get; set; } = "";
        }


        // ============================================================
        // CONFIRMAR PRODUCTO BAJADO
        //
        // El servidor recalcula PEPS antes de guardar.
        // No basta con ocultar botones en Razor.
        // ============================================================
        [HttpPost("/Surtido/ConfirmarBajada")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarBajada(
            [FromForm] ConfirmarBajadaReq req,
            CancellationToken ct = default)
        {
            var acceso =
                await ValidarModuloAsync(
                    req.Almacen,
                    "MONTACARGAS",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            if (req.SolicitudSurtidoId <= 0 ||
                req.ProduccionId <= 0)
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        message =
                            "Solicitud/Producción inválida."
                    }
                );
            }

            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            // ====================================================
            // SEGURIDAD SERVIDOR: prioridad antes de procesar POST.
            // Esto evita saltarse la cola enviando el formulario a mano.
            // ====================================================
            try
            {
                var prioridad =
                    await ValidarPrioridadPickingAsync(
                        acceso.Almacen!,
                        req.SolicitudSurtidoId,
                        ct
                    );

                if (!prioridad.Ok)
                {
                    TempData["Error"] =
                        prioridad.Mensaje;

                    return RedirectToAction(
                        nameof(Picking),
                        new
                        {
                            almacen = acceso.Almacen!.Codigo
                        }
                    );
                }
            }
            catch (SqlException)
            {
                TempData["Error"] =
                    "No fue posible validar la prioridad de picking. La bajada no fue registrada.";

                return RedirectToAction(
                    nameof(Picking),
                    new
                    {
                        almacen = acceso.Almacen!.Codigo
                    }
                );
            }

            // ESCANEO DE TARIMA: una lectura puede bajar varias producciones.
            // La etiqueta individual sigue usando el camino original.
            var codigoBajada = (req.CodigoEscaneado ?? "")
                .Trim().ToUpperInvariant();

            if (codigoBajada.StartsWith("TAR", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var resultadoTarima = await RegistrarBajadaTarimaAsync(
                        acceso.Almacen!, req.SolicitudSurtidoId,
                        req.ProduccionId, codigoBajada, usuario.Usuario, ct);
                    TempData["Exito"] = resultadoTarima;
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "No se bajó la tarima: " + ex.Message;
                }

                return RedirectToAction(nameof(PedidoPicking), new
                {
                    almacen = acceso.Almacen!.Codigo,
                    solicitudSurtidoId = req.SolicitudSurtidoId
                });
            }

            var vm =
                await ConstruirPickingTareaAsync(
                    acceso.Almacen!,
                    req.SolicitudSurtidoId,
                    req.ProduccionId,
                    ct
                );

            if (!string.IsNullOrWhiteSpace(vm.Error))
            {
                TempData["Error"] =
                    vm.Error;

                return RedirectToAction(
                    nameof(PedidoPicking),
                    new
                    {
                        almacen =
                            acceso.Almacen!.Codigo,

                        solicitudSurtidoId =
                            req.SolicitudSurtidoId
                    }
                );
            }

            var caja =
                vm.Caja;

            if (!caja.EsRecomendada)
            {
                TempData["Error"] =
                    "PEPS bloqueó esta caja. Primero debe salir una caja más antigua.";

                return RedirectToAction(
                    nameof(PedidoPicking),
                    new
                    {
                        almacen =
                            acceso.Almacen!.Codigo,

                        solicitudSurtidoId =
                            req.SolicitudSurtidoId
                    }
                );
            }

            if (!caja.PuedeBajar)
            {
                TempData["Error"] =
                    string.IsNullOrWhiteSpace(
                        caja.MotivoBloqueo
                    )
                        ? "La caja está bloqueada."
                        : caja.MotivoBloqueo;

                return RedirectToAction(
                    nameof(PedidoPicking),
                    new
                    {
                        almacen =
                            acceso.Almacen!.Codigo,

                        solicitudSurtidoId =
                            req.SolicitudSurtidoId
                    }
                );
            }

            var scan =
                (req.CodigoEscaneado ?? "")
                .Trim()
                .ToUpperInvariant();

            var etiqueta =
                (caja.CodigoEtiqueta ?? "")
                .Trim()
                .ToUpperInvariant();

            var tarima =
                (caja.TarimaCodigo ?? "")
                .Trim()
                .ToUpperInvariant();

            var scanValido =
                !string.IsNullOrWhiteSpace(scan)
                &&
                (
                    scan == etiqueta
                    ||
                    (
                        !string.IsNullOrWhiteSpace(tarima)
                        &&
                        scan == tarima
                    )
                );

            if (!scanValido)
            {
                TempData["Error"] =
                    "La etiqueta/tarima escaneada no corresponde a la caja PEPS indicada.";

                return RedirectToAction(
                    nameof(PickingTarea),
                    new
                    {
                        almacen =
                            acceso.Almacen!.Codigo,

                        solicitudSurtidoId =
                            req.SolicitudSurtidoId,

                        produccionId =
                            req.ProduccionId
                    }
                );
            }

            try
            {
                // Revalidación inmediatamente antes del INSERT.
                // Si otro operador terminó la Prioridad 1 mientras esta
                // pantalla estaba abierta, esta operación ya no continúa.
                var prioridadAntesDeGuardar =
                    await ValidarPrioridadPickingAsync(
                        acceso.Almacen!,
                        req.SolicitudSurtidoId,
                        ct
                    );

                if (!prioridadAntesDeGuardar.Ok)
                {
                    TempData["Error"] =
                        prioridadAntesDeGuardar.Mensaje;

                    return RedirectToAction(
                        nameof(Picking),
                        new
                        {
                            almacen = acceso.Almacen!.Codigo
                        }
                    );
                }

                await RegistrarBajadaAsync(
                    acceso.Almacen!,
                    vm.Pedido,
                    caja,
                    usuario.Usuario,
                    ct
                );
            }
            catch (SqlException ex)
            {
                // 2601 / 2627 = duplicado: ya bajada/reservada.
                TempData["Error"] =
                    ex.Number == 2601 ||
                    ex.Number == 2627
                        ? "Esta caja ya fue bajada o reservada por otro operador."
                        : $"No se pudo registrar la bajada: {ex.Message}";

                return RedirectToAction(
                    nameof(PedidoPicking),
                    new
                    {
                        almacen =
                            acceso.Almacen!.Codigo,

                        solicitudSurtidoId =
                            req.SolicitudSurtidoId
                    }
                );
            }

            TempData["Exito"] =
                $"Producto bajado: {caja.CodigoEtiqueta}. Queda pendiente de Capturista.";

            // Si todavía quedan cajas en este pedido, continuamos aquí.
            // Si ya terminó, volvemos a la cola y la Prioridad 2 pasa a 1.
            var pendientesDespues =
                await ObtenerPedidosPendientesAsync(
                    acceso.Almacen!,
                    null,
                    null,
                    ct
                );

            var pedidoSiguePendiente =
                pendientesDespues.Any(x =>
                    x.SolicitudSurtidoId == req.SolicitudSurtidoId
                    &&
                    x.CajasPendientes > 0
                );

            if (pedidoSiguePendiente)
            {
                return RedirectToAction(
                    nameof(PedidoPicking),
                    new
                    {
                        almacen =
                            acceso.Almacen!.Codigo,

                        solicitudSurtidoId =
                            req.SolicitudSurtidoId
                    }
                );
            }

            return RedirectToAction(
                nameof(Picking),
                new
                {
                    almacen = acceso.Almacen!.Codigo
                }
            );
        }


        // ============================================================
        // DETALLE DE PEDIDO / PRODUCTOS SOLICITADOS
        //
        // Ejemplo:
        // /Surtido/PedidoDetalle?almacen=3&solicitudSurtidoId=17387
        //
        // Reutiliza el permiso de Montacarguista.
        // Coordinación también puede abrirlo.
        // ============================================================
        [HttpGet("/Surtido/PedidoDetalle")]
        public async Task<IActionResult> PedidoDetalle(
            string almacen,
            int solicitudSurtidoId,
            CancellationToken ct = default)
        {
            if (solicitudSurtidoId <= 0)
            {
                return BadRequest(
                    "SolicitudSurtidoId inválido."
                );
            }

            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            // Puede entrar si tiene Montacarguista o Coordinador.
            if (!usuario.LogisticaMontacarguista &&
                !usuario.LogisticaCoordinador)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "No tienes permiso para consultar el detalle de surtido."
                    }
                );
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenes =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            await AplicarConfiguracionUbicacionAsync(
                almacenes,
                ct
            );

            var almacenActual =
                BuscarAlmacenPermitido(
                    almacenes,
                    almacen
                );

            if (almacenActual == null)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "El almacén no está permitido para este usuario."
                    }
                );
            }

            SurtidoPedidoDetallePaginaVM vm;

            try
            {
                vm =
                    await ConstruirDetallePedidoAsync(
                        almacenActual,
                        solicitudSurtidoId,
                        ct
                    );
            }
            catch (InvalidOperationException ex)
            {
                vm =
                    new SurtidoPedidoDetallePaginaVM
                    {
                        Almacen =
                            almacenActual,

                        SolicitudSurtidoId =
                            solicitudSurtidoId,

                        Error =
                            ex.Message
                    };
            }

            return View(
                "~/Views/Surtido/PedidoDetalle.cshtml",
                vm
            );
        }


        // ============================================================
        // CAPTURA
        // ============================================================
        [HttpGet("/Surtido/Captura")]
        public async Task<IActionResult> Captura(
            string almacen,
            CancellationToken ct = default)
        {
            var acceso =
                await ValidarModuloAsync(
                    almacen,
                    "CAPTURA",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            var zonaSurtido = await ObtenerZonaSurtidoAsync(
                acceso.Almacen!, ct);

            ViewBag.ZonaSurtido = zonaSurtido;

            // Datos de la solicitud/pedido, sólo lectura.
            // Se utiliza la relación ya existente en Meat:
            // SurtidoReferencia.TipoReferenciaId = 9.
            var pedidosPorSolicitud = new Dictionary<long, string>();
            foreach (var solicitudId in zonaSurtido
                         .Select(x => x.SolicitudSurtidoId)
                         .Distinct())
            {
                // El staging expone SolicitudSurtidoId como long.
                // El helper existente recibe int; evitar conversión implícita.
                if (solicitudId < 1 || solicitudId > int.MaxValue)
                {
                    pedidosPorSolicitud[solicitudId] = "Solicitud fuera de rango";
                    continue;
                }

                var pedido = await ObtenerPedidoPorSolicitudAsync(
                    acceso.Almacen!, (int)solicitudId, ct);
                pedidosPorSolicitud[solicitudId] =
                    string.IsNullOrWhiteSpace(pedido?.Pedido)
                        ? "Pedido sin referencia"
                        : pedido.Pedido;
            }
            ViewBag.PedidosPorSolicitud = pedidosPorSolicitud;
            ViewBag.ClientesPorSolicitud = new Dictionary<long, string>();
            var clientesPorSolicitud = new Dictionary<long, string>();
            foreach (var id in zonaSurtido.Select(z => z.SolicitudSurtidoId).Distinct())
            {
                if (id < 1 || id > int.MaxValue) continue;
                var p = await ObtenerPedidoPorSolicitudAsync(acceso.Almacen!, (int)id, ct);
                clientesPorSolicitud[id] = p?.Cliente ?? "";
            }
            ViewBag.ClientesPorSolicitud = clientesPorSolicitud;

            var cnCaptura = await ObtenerConexionSigoAsync(ct);
            var capturas = await cnCaptura.QueryAsync<SurtidoCapturaEstadoDto>(
                new CommandDefinition(@"
SELECT d.ProduccionId, h.Estatus
FROM dbo.SurtidoCapturaDetalle d
JOIN dbo.SurtidoCaptura h ON h.Id=d.SurtidoCapturaId
WHERE d.Activo=1 AND h.Planta=@Planta AND h.CodigoAlmacen=@Almacen",
                new { Planta = acceso.Almacen!.Planta, Almacen = acceso.Almacen!.Codigo },
                cancellationToken: ct));
            ViewBag.ProduccionesCapturadas = capturas.Select(x => x.ProduccionId).ToHashSet();


            return View(
                "~/Views/Surtido/Captura.cshtml",
                acceso.Almacen
            );
        }


        // ============================================================
        // UBICAR
        // ============================================================
        [HttpGet("/Surtido/Ubicar")]
        public async Task<IActionResult> Ubicar(
            string almacen,
            CancellationToken ct = default)
        {
            var acceso =
                await ValidarModuloAsync(
                    almacen,
                    "UBICAR",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            return View(
                "~/Views/Surtido/Ubicar.cshtml",
                acceso.Almacen
            );
        }



        // ============================================================
        // UBICAR - ENDPOINTS DE UBICACIÓN DINÁMICA
        //
        // Ninguno recibe ColorId / Master / Rotacion desde el cliente
        // como dato confiable. Todo se recalcula en servidor.
        // ============================================================

        [HttpGet("/Surtido/Ubicar/BuscarInventario")]
        public async Task<IActionResult> BuscarInventarioUbicacion(
            string almacen,
            string codigo,
            CancellationToken ct = default)
        {
            var acceso =
                await ValidarModuloAsync(
                    almacen,
                    "UBICAR",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            if (string.IsNullOrWhiteSpace(
                    codigo))
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        message =
                            "Escanea una tarima o caja."
                    }
                );
            }

            try
            {
                var r =
                    await ConstruirRecomendacionUbicacionDinamicaAsync(
                        acceso.Almacen!,
                        codigo,
                        ct
                    );

                return Json(
                    new
                    {
                        ok = true,
                        layout = new
                        {
                            id = r.LayoutId,
                            codigo = r.CodigoLayout,
                            nombre = r.NombreLayout
                        },

                        unidad =
                            r.UnidadCodigo,

                        articulo =
                            r.Articulo,

                        producto =
                            r.ProductoNombre,

                        master =
                            r.Master,

                        rotacion =
                            r.Rotacion,

                        reglaRotacion =
                            r.NombreReglaRotacion,

                        modoOrden =
                            r.ModoOrden,

                        color =
                            new
                            {
                                id =
                                    r.ColorAlmacenId,

                                codigo =
                                    r.CodigoColor,

                                nombre =
                                    r.NombreColor,

                                hex =
                                    r.HexColor
                            },

                        ubicacion =
                            new
                            {
                                id =
                                    r.UbicacionId,

                                codigo =
                                    r.CodigoUbicacion,

                                mapa3d =
                                    r.CodigoMapa3D
                            },

                        yaUbicada =
                            r.YaUbicada,

                        cajas =
                            r.Producciones.Count
                    }
                );
            }
            catch (Exception ex)
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        message =
                            ex.Message
                    }
                );
            }
        }


        [HttpGet("/Surtido/Ubicar/ValidarDestino")]
        public async Task<IActionResult> ValidarDestinoUbicacion(
            string almacen,
            string codigoInventario,
            string ubicacion,
            CancellationToken ct = default)
        {
            var acceso =
                await ValidarModuloAsync(
                    almacen,
                    "UBICAR",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            var destinoCodigo =
                (ubicacion ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(
                    destinoCodigo))
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        message =
                            "Escanea una ubicación."
                    }
                );
            }

            try
            {
                var recomendacion =
                    await ConstruirRecomendacionUbicacionDinamicaAsync(
                        acceso.Almacen!,
                        codigoInventario,
                        ct
                    );

                var destino =
                    await ObtenerUbicacionDinamicaPorCodigoAsync(
                        acceso.Almacen!.Codigo,
                        recomendacion.LayoutId,
                        destinoCodigo,
                        ct
                    );

                if (destino == null)
                {
                    return BadRequest(
                        new
                        {
                            ok = false,
                            message =
                                $"La ubicación '{destinoCodigo}' no está activa " +
                                $"en el almacén {acceso.Almacen.Codigo}."
                        }
                    );
                }

                if (destino.ColorAlmacenId !=
                    recomendacion.ColorAlmacenId)
                {
                    return BadRequest(
                        new
                        {
                            ok = false,
                            message =
                                $"Ese MASTER pertenece al color " +
                                $"{recomendacion.NombreColor} " +
                                $"({recomendacion.CodigoColor})."
                        }
                    );
                }

                var ocupadas =
                    await ObtenerUbicacionesOcupadasDinamicasAsync(
                        acceso.Almacen,
                        new List<UbicacionDinamicaCatalogoDto>
                        {
                            destino
                        },
                        recomendacion.UnidadCodigo,
                        recomendacion.Producciones
                            .Select(x =>
                                x.ProduccionId
                            )
                            .ToList(),
                        ct
                    );

                if (ocupadas.Contains(
                        destino.UbicacionId))
                {
                    return Conflict(
                        new
                        {
                            ok = false,
                            message =
                                $"La ubicación {destino.CodigoUbicacion} " +
                                "ya está ocupada."
                        }
                    );
                }

                return Json(
                    new
                    {
                        ok = true,

                        ubicacion =
                            new
                            {
                                id =
                                    destino.UbicacionId,

                                codigo =
                                    destino.CodigoUbicacion,

                                mapa3d =
                                    destino.CodigoMapa3D
                            },

                        color =
                            new
                            {
                                id =
                                    destino.ColorAlmacenId,

                                codigo =
                                    destino.CodigoColor,

                                nombre =
                                    destino.NombreColor,

                                hex =
                                    destino.HexColor
                            },

                        esSugerida =
                            destino.UbicacionId ==
                            recomendacion.UbicacionId
                    }
                );
            }
            catch (Exception ex)
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        message =
                            ex.Message
                    }
                );
            }
        }


        public sealed class ConfirmarUbicacionDinamicaReq
        {
            public string Almacen { get; set; } = "";
            public string CodigoInventario { get; set; } = "";
            public string CodigoUbicacion { get; set; } = "";
        }


        [HttpPost("/Surtido/Ubicar/Confirmar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarUbicacionDinamica(
            [FromForm] ConfirmarUbicacionDinamicaReq req,
            CancellationToken ct = default)
        {
            var acceso =
                await ValidarModuloAsync(
                    req.Almacen,
                    "UBICAR",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            var destinoCodigo =
                (req.CodigoUbicacion ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(
                    req.CodigoInventario)
                ||
                string.IsNullOrWhiteSpace(
                    destinoCodigo))
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        message =
                            "Inventario y ubicación son obligatorios."
                    }
                );
            }

            try
            {
                // Recalcular SIEMPRE en servidor.
                var recomendacion =
                    await ConstruirRecomendacionUbicacionDinamicaAsync(
                        acceso.Almacen!,
                        req.CodigoInventario,
                        ct
                    );

                var destino =
                    await ObtenerUbicacionDinamicaPorCodigoAsync(
                        acceso.Almacen.Codigo,
                        recomendacion.LayoutId,
                        destinoCodigo,
                        ct
                    );

                if (destino == null)
                {
                    return BadRequest(
                        new
                        {
                            ok = false,
                            message =
                                $"La ubicación '{destinoCodigo}' no está activa."
                        }
                    );
                }

                if (destino.ColorAlmacenId !=
                    recomendacion.ColorAlmacenId)
                {
                    return BadRequest(
                        new
                        {
                            ok = false,
                            message =
                                $"El MASTER '{recomendacion.Master}' pertenece " +
                                $"al color {recomendacion.NombreColor} " +
                                $"({recomendacion.CodigoColor})."
                        }
                    );
                }

                var ocupadas =
                    await ObtenerUbicacionesOcupadasDinamicasAsync(
                        acceso.Almacen,
                        new List<UbicacionDinamicaCatalogoDto>
                        {
                            destino
                        },
                        recomendacion.UnidadCodigo,
                        recomendacion.Producciones
                            .Select(x =>
                                x.ProduccionId
                            )
                            .ToList(),
                        ct
                    );

                if (ocupadas.Contains(
                        destino.UbicacionId))
                {
                    return Conflict(
                        new
                        {
                            ok = false,
                            message =
                                $"La ubicación {destino.CodigoUbicacion} " +
                                "ya está ocupada."
                        }
                    );
                }

                var cn =
                    await ObtenerConexionSigoAsync(ct);

                using var tx =
                    cn.BeginTransaction(
                        IsolationLevel.Serializable
                    );

                try
                {
                    // 1) La ubicación sigue activa y sigue perteneciendo
                    //    al color calculado para el MASTER.
                    const string sqlValidarCatalogo = @"
SELECT COUNT_BIG(1)
FROM dbo.SurtidoUbicacionCatalogo u WITH (UPDLOCK, HOLDLOCK)
INNER JOIN dbo.SurtidoColorAlmacen c WITH (UPDLOCK, HOLDLOCK)
    ON c.Id = u.ColorAlmacenId
WHERE u.Id = @UbicacionId
  AND u.Activo = 1
  AND c.Activo = 1
  AND u.CodigoAlmacen = @CodigoAlmacen
  AND u.LayoutId = @LayoutId
  AND c.CodigoAlmacen = @CodigoAlmacen
  AND c.LayoutId = @LayoutId
  AND u.ColorAlmacenId = @ColorAlmacenId;
";

                    var catalogoValido =
                        await cn.ExecuteScalarAsync<long>(
                            new CommandDefinition(
                                sqlValidarCatalogo,
                                new
                                {
                                    UbicacionId =
                                        destino.UbicacionId,

                                    CodigoAlmacen =
                                        acceso.Almacen.Codigo,

                                    LayoutId =
                                        recomendacion.LayoutId,

                                    ColorAlmacenId =
                                        recomendacion.ColorAlmacenId
                                },
                                transaction: tx,
                                cancellationToken: ct
                            )
                        );

                    if (catalogoValido == 0)
                    {
                        tx.Rollback();

                        return Conflict(
                            new
                            {
                                ok = false,
                                message =
                                    "La configuración de la ubicación cambió. " +
                                    "Vuelve a escanear."
                            }
                        );
                    }

                    // 2) Liberar asignación anterior de la MISMA unidad.
                    const string sqlLiberarUnidad = @"
DECLARE @Asignaciones TABLE (Id BIGINT);

INSERT INTO @Asignaciones(Id)
SELECT Id
FROM dbo.SurtidoUbicacionInventario WITH (UPDLOCK, HOLDLOCK)
WHERE CodigoAlmacen = @CodigoAlmacen
  AND UPPER(LTRIM(RTRIM(UnidadCodigo))) = @UnidadCodigo
  AND Activo = 1;


UPDATE d
SET
    Activo = 0,
    FechaSalida = SYSDATETIME()

FROM dbo.SurtidoUbicacionInventarioDetalle d

INNER JOIN @Asignaciones x
    ON x.Id = d.AsignacionId

WHERE d.Activo = 1;


UPDATE a
SET
    Activo = 0,
    FechaLiberacion = SYSDATETIME()

FROM dbo.SurtidoUbicacionInventario a

INNER JOIN @Asignaciones x
    ON x.Id = a.Id

WHERE a.Activo = 1;
";

                    await cn.ExecuteAsync(
                        new CommandDefinition(
                            sqlLiberarUnidad,
                            new
                            {
                                CodigoAlmacen =
                                    acceso.Almacen.Codigo,

                                UnidadCodigo =
                                    recomendacion.UnidadCodigo
                                        .Trim()
                                        .ToUpperInvariant()
                            },
                            transaction: tx,
                            cancellationToken: ct
                        )
                    );

                    // 3) Bloquear y verificar que nadie haya ocupado
                    //    el destino entre VALIDAR y CONFIRMAR.
                    const string sqlOcupada = @"
SELECT COUNT_BIG(1)
FROM dbo.SurtidoUbicacionInventario WITH (UPDLOCK, HOLDLOCK)
WHERE CodigoAlmacen = @CodigoAlmacen
  AND UbicacionId = @UbicacionId
  AND Activo = 1;
";

                    var ocupada =
                        await cn.ExecuteScalarAsync<long>(
                            new CommandDefinition(
                                sqlOcupada,
                                new
                                {
                                    CodigoAlmacen =
                                        acceso.Almacen.Codigo,

                                    UbicacionId =
                                        destino.UbicacionId
                                },
                                transaction: tx,
                                cancellationToken: ct
                            )
                        );

                    if (ocupada > 0)
                    {
                        tx.Rollback();

                        return Conflict(
                            new
                            {
                                ok = false,
                                message =
                                    $"La ubicación {destino.CodigoUbicacion} " +
                                    "acaba de ser ocupada por otro movimiento."
                            }
                        );
                    }

                    // 4) Guardar cabecera.
                    const string sqlCabecera = @"
INSERT INTO dbo.SurtidoUbicacionInventario
(
    Planta,
    CodigoAlmacen,
    LayoutId,
    UnidadCodigo,
    Articulo,
    Master,
    ColorAlmacenId,
    UbicacionId,
    Activo,
    UsuarioAsigno,
    FechaAsignacion
)
OUTPUT INSERTED.Id
VALUES
(
    @Planta,
    @CodigoAlmacen,
    @LayoutId,
    @UnidadCodigo,
    @Articulo,
    @Master,
    @ColorAlmacenId,
    @UbicacionId,
    1,
    @UsuarioAsigno,
    SYSDATETIME()
);
";

                    var asignacionId =
                        await cn.ExecuteScalarAsync<long>(
                            new CommandDefinition(
                                sqlCabecera,
                                new
                                {
                                    Planta =
                                        acceso.Almacen.Planta,

                                    CodigoAlmacen =
                                        acceso.Almacen.Codigo,

                                    LayoutId =
                                        recomendacion.LayoutId,

                                    UnidadCodigo =
                                        recomendacion.UnidadCodigo,

                                    Articulo =
                                        recomendacion.Articulo,

                                    Master =
                                        recomendacion.Master,

                                    ColorAlmacenId =
                                        recomendacion.ColorAlmacenId,

                                    UbicacionId =
                                        destino.UbicacionId,

                                    UsuarioAsigno =
                                        usuario.Usuario
                                },
                                transaction: tx,
                                cancellationToken: ct
                            )
                        );

                    // 5) Guardar todas las producciones que físicamente
                    //    forman la tarima/caja escaneada.
                    const string sqlDetalle = @"
INSERT INTO dbo.SurtidoUbicacionInventarioDetalle
(
    AsignacionId,
    ProduccionId,
    CodigoEtiqueta,
    Activo
)
VALUES
(
    @AsignacionId,
    @ProduccionId,
    @CodigoEtiqueta,
    1
);
";

                    foreach (var p in
                        recomendacion.Producciones)
                    {
                        await cn.ExecuteAsync(
                            new CommandDefinition(
                                sqlDetalle,
                                new
                                {
                                    AsignacionId =
                                        asignacionId,

                                    ProduccionId =
                                        p.ProduccionId,

                                    CodigoEtiqueta =
                                        p.CodigoEtiqueta
                                },
                                transaction: tx,
                                cancellationToken: ct
                            )
                        );
                    }

                    tx.Commit();

                    return Json(
                        new
                        {
                            ok = true,

                            message =
                                "Ubicación confirmada.",

                            ubicacion =
                                new
                                {
                                    id =
                                        destino.UbicacionId,

                                    codigo =
                                        destino.CodigoUbicacion,

                                    mapa3d =
                                        destino.CodigoMapa3D
                                },

                            color =
                                new
                                {
                                    id =
                                        recomendacion.ColorAlmacenId,

                                    codigo =
                                        recomendacion.CodigoColor,

                                    nombre =
                                        recomendacion.NombreColor,

                                    hex =
                                        recomendacion.HexColor
                                },

                            master =
                                recomendacion.Master
                        }
                    );
                }
                catch
                {
                    try
                    {
                        tx.Rollback();
                    }
                    catch
                    {
                    }

                    throw;
                }
            }
            catch (SqlException ex)
            {
                if (ex.Number == 2601 ||
                    ex.Number == 2627)
                {
                    return Conflict(
                        new
                        {
                            ok = false,
                            message =
                                "La unidad o la ubicación fue tomada por otro movimiento. " +
                                "Vuelve a escanear."
                        }
                    );
                }

                return BadRequest(
                    new
                    {
                        ok = false,
                        message =
                            $"No se pudo confirmar la ubicación. " +
                            $"SQL {ex.Number}: {ex.Message}"
                    }
                );
            }
            catch (Exception ex)
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        message =
                            ex.Message
                    }
                );
            }
        }


        // ============================================================
        // COORDINACIÓN SEGÚN PERMISOS DEL USUARIO
        //
        // Regla:
        //   UsuarioSerie -> plantas autorizadas
        //          +
        //   UsuarioSQL.AlmacenesPermitidos
        //          =
        //   almacenes que realmente puede administrar el coordinador.
        //
        // El almacén de la URL sólo ayuda a identificar la planta a abrir.
        // Si ese código ya no está permitido, se toma el primer almacén
        // permitido de esa misma planta.
        // ============================================================
        private async Task<(
            bool Ok,
            IActionResult? Error,
            UsuarioSurtidoDto? Usuario,
            SurtidoAlmacenVM? Almacen,
            List<SurtidoAlmacenVM> AlmacenesPermitidos)>
            ValidarCoordinacionUsuarioAsync(
                string? almacenCodigo,
                CancellationToken ct)
        {
            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return (
                    false,
                    Forbid(),
                    null,
                    null,
                    new List<SurtidoAlmacenVM>()
                );
            }

            if (!usuario.LogisticaCoordinador)
            {
                return (
                    false,
                    StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            ok = false,
                            message =
                                "No tienes permiso de COORDINAR."
                        }
                    ),
                    usuario,
                    null,
                    new List<SurtidoAlmacenVM>()
                );
            }

            var plantasPermitidas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenesPermitidos =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantasPermitidas
                );

            await AplicarConfiguracionUbicacionAsync(
                almacenesPermitidos,
                ct
            );

            if (almacenesPermitidos.Count == 0)
            {
                return (
                    false,
                    StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            ok = false,
                            message =
                                "No tienes almacenes autorizados para Coordinación."
                        }
                    ),
                    usuario,
                    null,
                    almacenesPermitidos
                );
            }

            // Primero intentamos respetar el almacén recibido en la URL,
            // pero sólo si está realmente permitido al usuario.
            var almacenPermitido =
                BuscarAlmacenPermitido(
                    almacenesPermitidos,
                    almacenCodigo
                );

            if (almacenPermitido == null &&
                !string.IsNullOrWhiteSpace(almacenCodigo))
            {
                // La URL puede traer un almacén viejo/no permitido (ej. DEV).
                // Lo usamos ÚNICAMENTE para identificar su planta y luego
                // elegimos un almacén que sí esté en AlmacenesPermitidos.
                var almacenReferencia =
                    ResolverAlmacen(
                        almacenCodigo
                    );

                almacenPermitido =
                    almacenesPermitidos.FirstOrDefault(x =>
                        string.Equals(
                            x.Planta,
                            almacenReferencia.Planta,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
            }

            almacenPermitido ??=
                almacenesPermitidos.First();

            return (
                true,
                null,
                usuario,
                almacenPermitido,
                almacenesPermitidos
            );
        }


        // ============================================================
        // COORDINADOR
        // ============================================================
        [HttpGet("/Surtido/Coordinador")]
        public async Task<IActionResult> Coordinador(
            string almacen,
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null,
            CancellationToken ct = default)
        {
            var acceso =
                await ValidarCoordinacionUsuarioAsync(
                    almacen,
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            var plantaActiva =
                acceso.Almacen!.Planta;

            // ========================================================
            // SÓLO ALMACENES ADMINISTRADOS POR ESTE USUARIO
            // dentro de la planta activa.
            //
            // Ejemplo ADMIN:
            //   Series = Planta1
            //   AlmacenesPermitidos = ["3"]
            // Resultado: únicamente almacén 3.
            // ========================================================
            var almacenesPlanta =
                acceso.AlmacenesPermitidos
                    .Where(x =>
                        string.Equals(
                            x.Planta,
                            plantaActiva,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .OrderBy(x =>
                        x.Nombre
                    )
                    .ToList();

            if (almacenesPlanta.Count == 0)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            $"No tienes almacenes administrados en la planta {plantaActiva}."
                    }
                );
            }

            SurtidoModuloPedidosVM vm;

            try
            {
                // Multi-almacén con filtro null = TODOS los almacenes
                // de la planta que acabamos de construir.
                vm =
                    await ConstruirModuloPedidosMultiAlmacenAsync(
                        almacenesPlanta,
                        null,
                        fechaInicio,
                        fechaFin,
                        ct
                    );

                // El contexto principal conserva el almacén desde el que
                // entró el coordinador, pero Model.Pedidos contiene toda
                // la planta y cada pedido conserva su AlmacenCodigo real.
                vm.Almacen =
                    acceso.Almacen!;

                vm.Almacenes =
                    almacenesPlanta;

                vm.FiltroAlmacen =
                    "";
            }
            catch (Exception ex)
            {
                vm =
                    new SurtidoModuloPedidosVM
                    {
                        Almacen =
                            acceso.Almacen!,

                        Almacenes =
                            almacenesPlanta,

                        FechaInicio =
                            fechaInicio?.Date
                            ?? DateTime.Today,

                        FechaFin =
                            fechaFin?.Date
                            ?? DateTime.Today,

                        Error =
                            ex.Message
                    };
            }

            // ========================================================
            // PRIORIDAD INDEPENDIENTE POR ALMACÉN.
            //
            // Los pedidos ya fueron cargados arriba desde MEAT.
            // No volvemos a reconstruir cada cola; únicamente leemos
            // las prioridades guardadas en SIGO y ordenamos en memoria.
            // ========================================================
            var erroresPrioridad =
                new List<string>();

            Dictionary<string, int> prioridades;

            try
            {
                prioridades =
                    await ConstruirPrioridadesPickingDesdePedidosAsync(
                        almacenesPlanta,
                        vm.Pedidos,
                        ct
                    );
            }
            catch (Exception ex)
            {
                prioridades =
                    new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase
                    );

                erroresPrioridad.Add(
                    ex.Message
                );
            }

            ViewBag.PrioridadesPedido =
                prioridades;

            ViewBag.PlantaActiva =
                plantaActiva;

            ViewBag.TotalAlmacenesPlanta =
                almacenesPlanta.Count;

            if (erroresPrioridad.Count > 0 &&
                string.IsNullOrWhiteSpace(vm.Error))
            {
                vm.Error =
                    "Algunas colas de prioridad no pudieron cargarse: " +
                    string.Join(
                        " | ",
                        erroresPrioridad
                    );
            }

            return View(
                "~/Views/Surtido/Coordinador.cshtml",
                vm
            );
        }


        public sealed class AsignarPrioridadPedidoReq
        {
            public string Almacen { get; set; } = "";

            public int SolicitudSurtidoId { get; set; }

            public int Prioridad { get; set; }

            public DateTime? FechaInicio { get; set; }

            public DateTime? FechaFin { get; set; }
        }


        // ============================================================
        // COORDINADOR: MOVER PEDIDO A UNA PRIORIDAD
        //
        // Ejemplo:
        // Pedido A está #5.
        // Coordinador escribe 1.
        // Resultado:
        // A = 1 y todos los demás se recorren 2,3,4,5...
        //
        // No quedan prioridades duplicadas.
        // ============================================================
        [HttpPost("/Surtido/AsignarPrioridadPedido")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AsignarPrioridadPedido(
            [FromForm] AsignarPrioridadPedidoReq req,
            CancellationToken ct = default)
        {
            // POST estricto: el coordinador sólo puede modificar
            // prioridades de almacenes incluidos en AlmacenesPermitidos.
            var acceso =
                await ValidarModuloAsync(
                    req.Almacen,
                    "COORDINAR",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            if (req.SolicitudSurtidoId <= 0 ||
                req.Prioridad <= 0)
            {
                TempData["Error"] =
                    "Solicitud o prioridad inválida.";

                return RedirectToAction(
                    nameof(Coordinador),
                    new
                    {
                        almacen =
                            acceso.Almacen!.Codigo,

                        fechaInicio =
                            req.FechaInicio?.ToString("yyyy-MM-dd"),

                        fechaFin =
                            req.FechaFin?.ToString("yyyy-MM-dd")
                    }
                );
            }

            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            try
            {
                var cola =
                    await ConstruirColaPrioridadAsync(
                        acceso.Almacen!,
                        ct
                    );

                if (cola.Pedidos.Count == 0)
                {
                    TempData["Error"] =
                        "No hay pedidos pendientes para priorizar.";

                    return RedirectToAction(
                        nameof(Coordinador),
                        new
                        {
                            almacen =
                                acceso.Almacen!.Codigo,

                            fechaInicio =
                                req.FechaInicio?.ToString("yyyy-MM-dd"),

                            fechaFin =
                                req.FechaFin?.ToString("yyyy-MM-dd")
                        }
                    );
                }

                var pedido =
                    cola.Pedidos.FirstOrDefault(x =>
                        x.SolicitudSurtidoId
                        ==
                        req.SolicitudSurtidoId
                    );

                if (pedido == null)
                {
                    TempData["Error"] =
                        "El pedido ya no está pendiente o no pertenece a este almacén.";

                    return RedirectToAction(
                        nameof(Coordinador),
                        new
                        {
                            almacen =
                                acceso.Almacen!.Codigo,

                            fechaInicio =
                                req.FechaInicio?.ToString("yyyy-MM-dd"),

                            fechaFin =
                                req.FechaFin?.ToString("yyyy-MM-dd")
                        }
                    );
                }

                var orden =
                    cola.Pedidos.ToList();

                orden.RemoveAll(x =>
                    x.SolicitudSurtidoId
                    ==
                    req.SolicitudSurtidoId
                );

                var posicion =
                    Math.Clamp(
                        req.Prioridad - 1,
                        0,
                        orden.Count
                    );

                orden.Insert(
                    posicion,
                    pedido
                );

                var conn =
                    await ObtenerConexionSigoAsync(ct);

                using var tx =
                    conn.BeginTransaction();

                const string sqlGuardar = @"
UPDATE dbo.SurtidoPedidoPrioridad
SET
    Prioridad = @Prioridad,
    Activo = 1,
    UsuarioModifico = @Usuario,
    FechaModificacion = SYSDATETIME()
WHERE Planta = @Planta
  AND CodigoAlmacen = @CodigoAlmacen
  AND SolicitudSurtidoId = @SolicitudSurtidoId;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.SurtidoPedidoPrioridad
    (
        Planta,
        CodigoAlmacen,
        SolicitudSurtidoId,
        Prioridad,
        Activo,
        UsuarioModifico,
        FechaModificacion
    )
    VALUES
    (
        @Planta,
        @CodigoAlmacen,
        @SolicitudSurtidoId,
        @Prioridad,
        1,
        @Usuario,
        SYSDATETIME()
    );
END;
";

                for (var i = 0; i < orden.Count; i++)
                {
                    await conn.ExecuteAsync(
                        sqlGuardar,
                        new
                        {
                            Planta =
                                acceso.Almacen!.Planta,

                            CodigoAlmacen =
                                acceso.Almacen.Codigo,

                            SolicitudSurtidoId =
                                orden[i].SolicitudSurtidoId,

                            Prioridad =
                                i + 1,

                            Usuario =
                                usuario.Usuario
                        },
                        tx
                    );
                }

                var idsPendientes =
                    orden
                        .Select(x =>
                            x.SolicitudSurtidoId
                        )
                        .ToList();

                const string sqlDesactivar = @"
UPDATE dbo.SurtidoPedidoPrioridad
SET
    Activo = 0,
    UsuarioModifico = @Usuario,
    FechaModificacion = SYSDATETIME()
WHERE Planta = @Planta
  AND CodigoAlmacen = @CodigoAlmacen
  AND Activo = 1
  AND SolicitudSurtidoId NOT IN @IdsPendientes;
";

                if (idsPendientes.Count > 0)
                {
                    await conn.ExecuteAsync(
                        sqlDesactivar,
                        new
                        {
                            Planta =
                                acceso.Almacen!.Planta,

                            CodigoAlmacen =
                                acceso.Almacen.Codigo,

                            Usuario =
                                usuario.Usuario,

                            IdsPendientes =
                                idsPendientes
                        },
                        tx
                    );
                }

                tx.Commit();

                TempData["Exito"] =
                    $"El pedido {pedido.Pedido} quedó en Prioridad {posicion + 1}. " +
                    "La cola fue renumerada automáticamente.";
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                TempData["Error"] =
                    "Falta crear dbo.SurtidoPedidoPrioridad en SIGO. " +
                    "Ejecuta el script SurtidoPedidoPrioridad.sql.";
            }
            catch (Exception ex)
            {
                TempData["Error"] =
                    $"No se pudo guardar la prioridad: {ex.Message}";
            }

            return RedirectToAction(
                nameof(Coordinador),
                new
                {
                    almacen =
                        acceso.Almacen!.Codigo,

                    fechaInicio =
                        req.FechaInicio?.ToString("yyyy-MM-dd"),

                    fechaFin =
                        req.FechaFin?.ToString("yyyy-MM-dd")
                }
            );
        }


        // ============================================================
        // PRUEBA SEGURA DE CONEXIÓN POR ALMACÉN
        //
        // Ejemplo:
        // /Surtido/TestConexion?almacen=3
        // /Surtido/TestConexion?almacen=TIFCED
        //
        // NO devuelve password ni cadena completa.
        // ============================================================
        [HttpGet("/Surtido/TestConexion")]
        public async Task<IActionResult> TestConexion(
            string almacen,
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenes =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            await AplicarConfiguracionUbicacionAsync(
                almacenes,
                ct
            );

            var almacenActual =
                BuscarAlmacenPermitido(
                    almacenes,
                    almacen
                );

            if (almacenActual == null)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "Almacén no permitido."
                    }
                );
            }

            try
            {
                var conexion =
                    ObtenerCadenaMeatPorAlmacen(
                        almacenActual
                    );

                var builder =
                    new SqlConnectionStringBuilder(
                        conexion.Cadena
                    );

                await using var conn =
                    new SqlConnection(
                        conexion.Cadena
                    );

                await conn.OpenAsync(ct);

                var info =
                    await conn.QueryFirstAsync<dynamic>(
                        new CommandDefinition(
                            @"
SELECT
    Servidor = @@SERVERNAME,
    BaseDatos = DB_NAME();
",
                            cancellationToken: ct
                        )
                    );

                return Json(
                    new
                    {
                        ok = true,

                        almacen =
                            almacenActual.Codigo,

                        nombre =
                            almacenActual.Nombre,

                        sucursal =
                            almacenActual.Sucursal,

                        planta =
                            almacenActual.Planta,

                        conexion =
                            conexion.Nombre,

                        dataSource =
                            builder.DataSource,

                        initialCatalog =
                            builder.InitialCatalog,

                        servidor =
                            info.Servidor,

                        baseDatos =
                            info.BaseDatos
                    }
                );
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        ok = false,

                        almacen =
                            almacenActual.Codigo,

                        nombre =
                            almacenActual.Nombre,

                        sucursal =
                            almacenActual.Sucursal,

                        planta =
                            almacenActual.Planta,

                        error =
                            ex.Message
                    }
                );
            }
        }



        // ============================================================
        // CONFIGURACIÓN DE ALMACENES
        //
        // SIGO decide si un almacén obliga ubicación.
        // Sólo usuarios con permiso de Coordinador.
        // ============================================================
        [HttpGet("/Surtido/ConfiguracionAlmacenes")]
        public async Task<IActionResult> ConfiguracionAlmacenes(
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            if (!usuario.LogisticaCoordinador)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "Se requiere permiso de Coordinador para configurar almacenes."
                    }
                );
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenes =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            await AplicarConfiguracionUbicacionAsync(
                almacenes,
                ct
            );

            var vm =
                new SurtidoConfiguracionAlmacenesVM
                {
                    Usuario =
                        usuario.Usuario,

                    Almacenes =
                        almacenes
                            .Select(a =>
                                new SurtidoAlmacenConfiguracionVM
                                {
                                    Codigo =
                                        a.Codigo,

                                    Nombre =
                                        a.Nombre,

                                    Sucursal =
                                        a.Sucursal,

                                    Planta =
                                        a.Planta,

                                    Clasificacion =
                                        a.Clasificacion,

                                    ObligaUbicacion =
                                        a.ObligaUbicacion,

                                    TieneConfiguracionUbicacion =
                                        a.TieneConfiguracionUbicacion
                                }
                            )
                            .OrderBy(a =>
                                a.Planta
                            )
                            .ThenBy(a =>
                                a.Nombre
                            )
                            .ToList()
                };

            return View(
                "~/Views/Surtido/ConfiguracionAlmacenes.cshtml",
                vm
            );
        }


        // ============================================================
        // GUARDAR REGLAS DE UBICACIÓN
        //
        // codigos          -> todos los almacenes mostrados
        // obligaUbicacion  -> sólo los checkboxes activos
        // ============================================================
        [HttpPost("/Surtido/GuardarConfiguracionAlmacenes")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarConfiguracionAlmacenes(
            List<string>? codigos,
            List<string>? obligaUbicacion,
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            if (!usuario.LogisticaCoordinador)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "Se requiere permiso de Coordinador."
                    }
                );
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenesPermitidos =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            var permitidos =
                almacenesPermitidos
                    .Select(x =>
                        NormalizarCodigoAlmacen(
                            x.Codigo
                        )
                    )
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase
                    );

            var enviados =
                (codigos ?? new List<string>())
                    .Select(NormalizarCodigoAlmacen)
                    .Where(x =>
                        permitidos.Contains(x)
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            var obligatorios =
                (obligaUbicacion ?? new List<string>())
                    .Select(NormalizarCodigoAlmacen)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase
                    );

            var conn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
IF EXISTS
(
    SELECT 1
    FROM dbo.SurtidoAlmacenConfiguracion
    WHERE CodigoAlmacen = @CodigoAlmacen
)
BEGIN
    UPDATE dbo.SurtidoAlmacenConfiguracion
    SET
        ObligaUbicacion =
            @ObligaUbicacion,

        Activo =
            1,

        FechaModificacion =
            SYSDATETIME(),

        UsuarioModificacion =
            @UsuarioModificacion

    WHERE CodigoAlmacen =
        @CodigoAlmacen;
END
ELSE
BEGIN
    INSERT INTO dbo.SurtidoAlmacenConfiguracion
    (
        CodigoAlmacen,
        ObligaUbicacion,
        Activo,
        FechaModificacion,
        UsuarioModificacion
    )
    VALUES
    (
        @CodigoAlmacen,
        @ObligaUbicacion,
        1,
        SYSDATETIME(),
        @UsuarioModificacion
    );
END;
";

            foreach (var codigo in enviados)
            {
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            CodigoAlmacen =
                                codigo,

                            ObligaUbicacion =
                                obligatorios.Contains(
                                    codigo
                                ),

                            UsuarioModificacion =
                                usuario.Usuario
                        },
                        cancellationToken: ct
                    )
                );
            }

            TempData["Exito"] =
                "Configuración de ubicación actualizada correctamente.";

            return RedirectToAction(
                nameof(ConfiguracionAlmacenes)
            );
        }



        // ============================================================
        // MAPA 3D
        //
        // - TIFCED: usa el layout completo confirmado de TIF 776.
        // - Otros almacenes: muestra rack 3D enfocado sin inventar
        //   la geometría completa del almacén.
        //
        // Puede entrar Montacarguista, Ubicador o Coordinador.
        // ============================================================
        [HttpGet("/Surtido/Mapa3D")]
        public async Task<IActionResult> Mapa3D(
            string almacen,
            string? ubicacion = null,
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            if (
                !usuario.LogisticaMontacarguista
                &&
                !usuario.LogisticaUbicador
                &&
                !usuario.LogisticaCoordinador
            )
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "No tienes permiso para consultar el mapa 3D."
                    }
                );
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenes =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            await AplicarConfiguracionUbicacionAsync(
                almacenes,
                ct
            );

            var almacenActual =
                BuscarAlmacenPermitido(
                    almacenes,
                    almacen
                );

            if (almacenActual == null)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "El almacén no está permitido para este usuario."
                    }
                );
            }

            var vm =
                new SurtidoMapa3DVM
                {
                    Almacen =
                        almacenActual,

                    UbicacionInicial =
                        (ubicacion ?? "")
                        .Trim()
                        .ToUpperInvariant()
                };

            return View(
                "~/Views/Surtido/Mapa3D.cshtml",
                vm
            );
        }


        // ============================================================
        // CONTEXTO PARA AJAX / HAND HELD
        // ============================================================
        [HttpGet("/Surtido/Contexto")]
        public async Task<IActionResult> Contexto(
            string almacen,
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioActualAsync(ct);

            if (usuario == null)
            {
                return Forbid();
            }

            var plantas =
                await ObtenerPlantasUsuarioAsync(
                    usuario.Id,
                    ct
                );

            var almacenes =
                ConstruirAlmacenesUsuario(
                    usuario,
                    plantas
                );

            await AplicarConfiguracionUbicacionAsync(
                almacenes,
                ct
            );

            var almacenActual =
                BuscarAlmacenPermitido(
                    almacenes,
                    almacen
                );

            if (almacenActual == null)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        message =
                            "Almacén no permitido."
                    }
                );
            }

            string nombreConexion;

            try
            {
                nombreConexion =
                    ObtenerCadenaMeatPorAlmacen(
                        almacenActual
                    ).Nombre;
            }
            catch
            {
                nombreConexion =
                    "POR DEFINIR";
            }

            return Json(
                new
                {
                    ok = true,

                    usuario =
                        new
                        {
                            id =
                                usuario.Id,

                            login =
                                usuario.Usuario,

                            nombre =
                                usuario.Nombre
                        },

                    plantas,

                    almacen =
                        new
                        {
                            codigo =
                                almacenActual.Codigo,

                            nombre =
                                almacenActual.Nombre,

                            sucursal =
                                almacenActual.Sucursal,

                            planta =
                                almacenActual.Planta,

                            clasificacion =
                                almacenActual.Clasificacion,

                            conexion =
                                nombreConexion
                        },

                    permisos =
                        new
                        {
                            montacarguista =
                                usuario.LogisticaMontacarguista,

                            capturista =
                                usuario.LogisticaCapturista,

                            ubicador =
                                usuario.LogisticaUbicador,

                            coordinador =
                                usuario.LogisticaCoordinador
                        }
                }
            );




        }



        // ============================================================
        // GESTIÓN DE LAYOUTS POR ALMACÉN
        // ============================================================

        private async Task<(
            bool Ok,
            IActionResult? Error,
            UsuarioSurtidoDto? Usuario,
            List<SurtidoAlmacenVM> Almacenes,
            SurtidoAlmacenVM? Almacen)>
            ValidarGestionLayoutAsync(
                string? codigoAlmacen,
                CancellationToken ct)
        {
            var usuario = await ObtenerUsuarioActualAsync(ct);
            if (usuario == null)
                return (false, Forbid(), null, new List<SurtidoAlmacenVM>(), null);

            if (!usuario.LogisticaCoordinador)
                return (
                    false,
                    StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        ok = false,
                        message = "Se requiere permiso de Coordinador para administrar layouts."
                    }),
                    usuario,
                    new List<SurtidoAlmacenVM>(),
                    null
                );

            var plantas = await ObtenerPlantasUsuarioAsync(usuario.Id, ct);
            var almacenes = ConstruirAlmacenesUsuario(usuario, plantas);
            await AplicarConfiguracionUbicacionAsync(almacenes, ct);

            if (almacenes.Count == 0)
                return (false, BadRequest(new { ok = false, message = "El usuario no tiene almacenes disponibles." }), usuario, almacenes, null);

            var almacen = string.IsNullOrWhiteSpace(codigoAlmacen)
                ? almacenes.OrderBy(x => x.Planta).ThenBy(x => x.Nombre).FirstOrDefault()
                : BuscarAlmacenPermitido(almacenes, codigoAlmacen);

            if (almacen == null)
                return (false, StatusCode(StatusCodes.Status403Forbidden, new { ok = false, message = "El almacén no está permitido para este usuario." }), usuario, almacenes, null);

            return (true, null, usuario, almacenes, almacen);
        }


        private IActionResult RedirectGestionLayout(
            string almacen,
            int layoutId,
            string anchor)
        {
            var url = Url.Action(
                nameof(GestionLayouts),
                "Surtido",
                new
                {
                    almacen,
                    layoutId
                }
            ) ?? "/Surtido/GestionLayouts";

            return Redirect(url + "#" + anchor);
        }


        private async Task<bool> LayoutPerteneceAlmacenAsync(
            IDbConnection cn,
            string codigoAlmacen,
            int layoutId,
            CancellationToken ct)
        {
            const string sql = @"
SELECT COUNT_BIG(1)
FROM dbo.SurtidoLayoutAlmacen WITH (NOLOCK)
WHERE Id = @LayoutId
  AND CodigoAlmacen = @CodigoAlmacen;
";

            return await cn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        LayoutId = layoutId,
                        CodigoAlmacen = NormalizarCodigoAlmacen(codigoAlmacen)
                    },
                    cancellationToken: ct
                )
            ) > 0;
        }


        [HttpGet("/Surtido/GestionLayouts")]
        public async Task<IActionResult> GestionLayouts(
            string? almacen = null,
            int? layoutId = null,
            CancellationToken ct = default)
        {
            var acceso = await ValidarGestionLayoutAsync(almacen, ct);
            if (!acceso.Ok) return acceso.Error!;

            var actual = acceso.Almacen!;
            var cn = await ObtenerConexionSigoAsync(ct);

            const string sqlLayouts = @"
SELECT
    l.Id,
    l.CodigoAlmacen,
    CodigoLayout = ISNULL(l.CodigoLayout,''),
    NombreLayout = ISNULL(l.NombreLayout,''),
    Descripcion = ISNULL(l.Descripcion,''),
    EsOperativo = ISNULL(l.EsOperativo,0),
    Orden = ISNULL(l.Orden,1),
    Activo = ISNULL(l.Activo,0),
    Racks = CONVERT(INT, ISNULL(r.Total,0)),
    Ubicaciones = CONVERT(INT, ISNULL(u.Total,0)),
    Ocupadas = CONVERT(INT, ISNULL(o.Total,0))
FROM dbo.SurtidoLayoutAlmacen l WITH (NOLOCK)
OUTER APPLY
(
    SELECT Total = COUNT_BIG(1)
    FROM dbo.SurtidoRackAlmacen x WITH (NOLOCK)
    WHERE x.CodigoAlmacen=l.CodigoAlmacen AND x.LayoutId=l.Id AND x.Activo=1
) r
OUTER APPLY
(
    SELECT Total = COUNT_BIG(1)
    FROM dbo.SurtidoUbicacionCatalogo x WITH (NOLOCK)
    WHERE x.CodigoAlmacen=l.CodigoAlmacen AND x.LayoutId=l.Id AND x.Activo=1
) u
OUTER APPLY
(
    SELECT Total = COUNT_BIG(1)
    FROM dbo.SurtidoUbicacionInventario x WITH (NOLOCK)
    WHERE x.CodigoAlmacen=l.CodigoAlmacen AND x.LayoutId=l.Id AND x.Activo=1
) o
WHERE l.CodigoAlmacen=@CodigoAlmacen
ORDER BY l.Activo DESC, l.EsOperativo DESC, l.Orden, l.Id;
";

            var layouts = (
                await cn.QueryAsync<SurtidoLayoutAlmacenAdminVM>(
                    new CommandDefinition(
                        sqlLayouts,
                        new { CodigoAlmacen = actual.Codigo },
                        cancellationToken: ct
                    )
                )
            ).ToList();

            SurtidoLayoutAlmacenAdminVM? seleccionado = null;
            if (layoutId.HasValue)
                seleccionado = layouts.FirstOrDefault(x => x.Id == layoutId.Value);

            seleccionado ??= layouts.FirstOrDefault(x => x.Activo && x.EsOperativo);
            seleccionado ??= layouts.FirstOrDefault(x => x.Activo);
            seleccionado ??= layouts.FirstOrDefault();

            var vm = new SurtidoLayoutAdminVM
            {
                Almacenes = acceso.Almacenes,
                Almacen = actual,
                Layouts = layouts,
                LayoutActual = seleccionado
            };

            if (seleccionado == null)
                return View("~/Views/Surtido/GestionLayouts.cshtml", vm);

            const string sql = @"
-- COLORES
SELECT Id, CodigoAlmacen, LayoutId,
       CodigoColor=ISNULL(CodigoColor,''), NombreColor=ISNULL(NombreColor,''), HexColor=ISNULL(HexColor,''),
       Orden=ISNULL(Orden,1), Activo=ISNULL(Activo,0)
FROM dbo.SurtidoColorAlmacen WITH (NOLOCK)
WHERE CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId
ORDER BY Activo DESC, Orden, NombreColor, Id;

-- MASTER / COLOR
SELECT mc.Id, mc.CodigoAlmacen, mc.LayoutId, Master=ISNULL(mc.Master,''), mc.ColorAlmacenId,
       CodigoColor=ISNULL(c.CodigoColor,''), NombreColor=ISNULL(c.NombreColor,''), HexColor=ISNULL(c.HexColor,''),
       Activo=ISNULL(mc.Activo,0)
FROM dbo.SurtidoMasterColorAlmacen mc WITH (NOLOCK)
INNER JOIN dbo.SurtidoColorAlmacen c WITH (NOLOCK)
    ON c.Id=mc.ColorAlmacenId AND c.LayoutId=mc.LayoutId
WHERE mc.CodigoAlmacen=@CodigoAlmacen AND mc.LayoutId=@LayoutId
ORDER BY mc.Activo DESC, mc.Master, mc.Id;

-- ROTACIÓN
SELECT Id, CodigoAlmacen, LayoutId, NombreRegla=ISNULL(NombreRegla,''), Desde, Hasta,
       AplicaSinRotacion=ISNULL(AplicaSinRotacion,0), ModoOrden=ISNULL(ModoOrden,''),
       Prioridad=ISNULL(Prioridad,1), Activo=ISNULL(Activo,0)
FROM dbo.SurtidoRotacionReglaAlmacen WITH (NOLOCK)
WHERE CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId
ORDER BY Activo DESC, Prioridad, Id;

-- RACKS
SELECT r.Id, r.CodigoAlmacen, r.LayoutId,
       CodigoRack=ISNULL(r.CodigoRack,''), NombreRack=ISNULL(r.NombreRack,''),
       PosX=ISNULL(r.PosX,0), PosZ=ISNULL(r.PosZ,0), Ancho=ISNULL(r.Ancho,10), Alto=ISNULL(r.Alto,6),
       Profundidad=ISNULL(r.Profundidad,2), RotacionY=ISNULL(r.RotacionY,0),
       Niveles=ISNULL(r.Niveles,4), PosicionesPorNivel=ISNULL(r.PosicionesPorNivel,10),
       HexColor=ISNULL(r.HexColor,''), Orden=ISNULL(r.Orden,1), Activo=ISNULL(r.Activo,0),
       TotalUbicaciones=CONVERT(INT,ISNULL(uc.Total,0)),
       UbicacionesOcupadas=CONVERT(INT,ISNULL(oc.Total,0))
FROM dbo.SurtidoRackAlmacen r WITH (NOLOCK)
OUTER APPLY
(
    SELECT Total=COUNT_BIG(1)
    FROM dbo.SurtidoUbicacionCatalogo u WITH (NOLOCK)
    WHERE u.CodigoAlmacen=r.CodigoAlmacen AND u.LayoutId=r.LayoutId AND u.RackId=r.Id AND u.Activo=1
) uc
OUTER APPLY
(
    SELECT Total=COUNT_BIG(1)
    FROM dbo.SurtidoUbicacionInventario a WITH (NOLOCK)
    WHERE a.CodigoAlmacen=r.CodigoAlmacen AND a.LayoutId=r.LayoutId AND a.Activo=1
      AND EXISTS (SELECT 1 FROM dbo.SurtidoUbicacionCatalogo u WITH (NOLOCK) WHERE u.Id=a.UbicacionId AND u.RackId=r.Id)
) oc
WHERE r.CodigoAlmacen=@CodigoAlmacen AND r.LayoutId=@LayoutId
ORDER BY r.Activo DESC, r.Orden, r.CodigoRack, r.Id;

-- UBICACIONES
SELECT u.Id, u.CodigoAlmacen, u.LayoutId, u.ColorAlmacenId,
       CodigoColor=ISNULL(c.CodigoColor,''), NombreColor=ISNULL(c.NombreColor,''), HexColor=ISNULL(c.HexColor,''),
       RackId=u.RackId, CodigoRack=ISNULL(r.CodigoRack,''), Rack=ISNULL(u.Rack,''),
       Posicion=ISNULL(u.Posicion,''), Altura=ISNULL(u.Altura,''),
       CodigoUbicacion=ISNULL(u.CodigoUbicacion,''), CodigoMapa3D=ISNULL(u.CodigoMapa3D,''),
       OrdenFisico=ISNULL(u.OrdenFisico,1), Activo=ISNULL(u.Activo,0),
       Ocupada=CONVERT(BIT,CASE WHEN a.Id IS NULL THEN 0 ELSE 1 END),
       UnidadCodigo=ISNULL(a.UnidadCodigo,'')
FROM dbo.SurtidoUbicacionCatalogo u WITH (NOLOCK)
INNER JOIN dbo.SurtidoColorAlmacen c WITH (NOLOCK)
    ON c.Id=u.ColorAlmacenId AND c.LayoutId=u.LayoutId
LEFT JOIN dbo.SurtidoRackAlmacen r WITH (NOLOCK)
    ON r.Id=u.RackId AND r.LayoutId=u.LayoutId
OUTER APPLY
(
    SELECT TOP 1 ai.Id, ai.UnidadCodigo
    FROM dbo.SurtidoUbicacionInventario ai WITH (NOLOCK)
    WHERE ai.CodigoAlmacen=u.CodigoAlmacen AND ai.LayoutId=u.LayoutId AND ai.UbicacionId=u.Id AND ai.Activo=1
    ORDER BY ai.Id DESC
) a
WHERE u.CodigoAlmacen=@CodigoAlmacen AND u.LayoutId=@LayoutId
ORDER BY u.Activo DESC, c.Orden, u.OrdenFisico, u.CodigoUbicacion, u.Id;

-- RESUMEN COLORES
SELECT c.Id AS ColorAlmacenId, CodigoColor=ISNULL(c.CodigoColor,''), NombreColor=ISNULL(c.NombreColor,''),
       HexColor=ISNULL(c.HexColor,''), Total=CONVERT(INT,COUNT(u.Id)),
       Ocupadas=CONVERT(INT,SUM(CASE WHEN a.Id IS NULL THEN 0 ELSE 1 END))
FROM dbo.SurtidoColorAlmacen c WITH (NOLOCK)
LEFT JOIN dbo.SurtidoUbicacionCatalogo u WITH (NOLOCK)
    ON u.ColorAlmacenId=c.Id AND u.LayoutId=c.LayoutId AND u.CodigoAlmacen=c.CodigoAlmacen AND u.Activo=1
OUTER APPLY
(
    SELECT TOP 1 ai.Id
    FROM dbo.SurtidoUbicacionInventario ai WITH (NOLOCK)
    WHERE ai.CodigoAlmacen=c.CodigoAlmacen AND ai.LayoutId=c.LayoutId AND ai.UbicacionId=u.Id AND ai.Activo=1
) a
WHERE c.CodigoAlmacen=@CodigoAlmacen AND c.LayoutId=@LayoutId AND c.Activo=1
GROUP BY c.Id,c.CodigoColor,c.NombreColor,c.HexColor,c.Orden
ORDER BY c.Orden,c.NombreColor;
";

            using var multi = await cn.QueryMultipleAsync(
                new CommandDefinition(
                    sql,
                    new { CodigoAlmacen = actual.Codigo, LayoutId = seleccionado.Id },
                    cancellationToken: ct
                )
            );

            vm.Colores = (await multi.ReadAsync<SurtidoColorAdminVM>()).ToList();
            vm.Masters = (await multi.ReadAsync<SurtidoMasterColorAdminVM>()).ToList();
            vm.ReglasRotacion = (await multi.ReadAsync<SurtidoRotacionReglaAdminVM>()).ToList();
            vm.Racks = (await multi.ReadAsync<SurtidoRackAdminVM>()).ToList();
            vm.Ubicaciones = (await multi.ReadAsync<SurtidoUbicacionAdminVM>()).ToList();
            vm.ResumenColores = (await multi.ReadAsync<SurtidoColorResumenVM>()).ToList();

            return View("~/Views/Surtido/GestionLayouts.cshtml", vm);
        }


        [HttpGet("/Surtido/GestionLayouts/Datos3D")]
        public async Task<IActionResult> DatosLayout3D(
            string almacen,
            int layoutId,
            CancellationToken ct = default)
        {
            var acceso = await ValidarGestionLayoutAsync(almacen, ct);
            if (!acceso.Ok) return acceso.Error!;

            var cn = await ObtenerConexionSigoAsync(ct);
            if (!await LayoutPerteneceAlmacenAsync(cn, acceso.Almacen!.Codigo, layoutId, ct))
                return BadRequest(new { ok = false, message = "El layout no pertenece al almacén." });

            return await ConstruirJsonLayout3DAsync(cn, acceso.Almacen, layoutId, ct);
        }


        private async Task<IActionResult> ConstruirJsonLayout3DAsync(
            IDbConnection cn,
            SurtidoAlmacenVM almacen,
            int layoutId,
            CancellationToken ct)
        {
            const string sql = @"
SELECT TOP 1 Id, CodigoAlmacen, CodigoLayout=ISNULL(CodigoLayout,''), NombreLayout=ISNULL(NombreLayout,''),
       Descripcion=ISNULL(Descripcion,''), EsOperativo=ISNULL(EsOperativo,0), Activo=ISNULL(Activo,0)
FROM dbo.SurtidoLayoutAlmacen WITH (NOLOCK)
WHERE Id=@LayoutId AND CodigoAlmacen=@CodigoAlmacen;

SELECT Id, LayoutId, CodigoRack=ISNULL(CodigoRack,''), NombreRack=ISNULL(NombreRack,''),
       PosX=ISNULL(PosX,0), PosZ=ISNULL(PosZ,0), Ancho=ISNULL(Ancho,10), Alto=ISNULL(Alto,6),
       Profundidad=ISNULL(Profundidad,2), RotacionY=ISNULL(RotacionY,0), Niveles=ISNULL(Niveles,4),
       PosicionesPorNivel=ISNULL(PosicionesPorNivel,10), HexColor=ISNULL(HexColor,''), Orden=ISNULL(Orden,1)
FROM dbo.SurtidoRackAlmacen WITH (NOLOCK)
WHERE CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId AND Activo=1
ORDER BY Orden,CodigoRack,Id;

SELECT u.Id,u.LayoutId,u.RackId,CodigoRack=ISNULL(r.CodigoRack,''),
       CodigoUbicacion=ISNULL(u.CodigoUbicacion,''), CodigoMapa3D=ISNULL(u.CodigoMapa3D,''),
       CodigoColor=ISNULL(c.CodigoColor,''), NombreColor=ISNULL(c.NombreColor,''), HexColor=ISNULL(c.HexColor,''),
       Posicion=ISNULL(u.Posicion,''), Altura=ISNULL(u.Altura,''), OrdenFisico=ISNULL(u.OrdenFisico,1),
       Activo=ISNULL(u.Activo,0),
       Ocupada=CONVERT(BIT,CASE WHEN a.Id IS NULL THEN 0 ELSE 1 END), UnidadCodigo=ISNULL(a.UnidadCodigo,'')
FROM dbo.SurtidoUbicacionCatalogo u WITH (NOLOCK)
INNER JOIN dbo.SurtidoColorAlmacen c WITH (NOLOCK)
    ON c.Id=u.ColorAlmacenId AND c.LayoutId=u.LayoutId
LEFT JOIN dbo.SurtidoRackAlmacen r WITH (NOLOCK)
    ON r.Id=u.RackId AND r.LayoutId=u.LayoutId
OUTER APPLY
(
    SELECT TOP 1 ai.Id,ai.UnidadCodigo
    FROM dbo.SurtidoUbicacionInventario ai WITH (NOLOCK)
    WHERE ai.CodigoAlmacen=u.CodigoAlmacen AND ai.LayoutId=u.LayoutId AND ai.UbicacionId=u.Id AND ai.Activo=1
    ORDER BY ai.Id DESC
) a
WHERE u.CodigoAlmacen=@CodigoAlmacen AND u.LayoutId=@LayoutId AND u.Activo=1
ORDER BY u.RackId,u.OrdenFisico,u.Id;
";

            using var multi = await cn.QueryMultipleAsync(
                new CommandDefinition(
                    sql,
                    new { CodigoAlmacen = almacen.Codigo, LayoutId = layoutId },
                    cancellationToken: ct
                )
            );

            var layout = await multi.ReadFirstOrDefaultAsync<dynamic>();
            var racks = (await multi.ReadAsync<dynamic>()).ToList();
            var ubicaciones = (await multi.ReadAsync<dynamic>()).ToList();

            if (layout == null)
                return NotFound(new { ok = false, message = "Layout no encontrado." });

            return Json(new
            {
                ok = true,
                almacen = new { codigo = almacen.Codigo, nombre = almacen.Nombre, planta = almacen.Planta },
                layout,
                racks,
                ubicaciones
            });
        }


        // Datos del MAPA operativo. No requiere permiso de coordinador.
        [HttpGet("/Surtido/Mapa3D/Datos")]
        public async Task<IActionResult> DatosMapa3DOperativo(
            string almacen,
            CancellationToken ct = default)
        {
            var usuario = await ObtenerUsuarioActualAsync(ct);
            if (usuario == null) return Forbid();

            if (!usuario.LogisticaMontacarguista && !usuario.LogisticaUbicador && !usuario.LogisticaCoordinador)
                return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, message = "No tienes permiso para consultar el mapa 3D." });

            var plantas = await ObtenerPlantasUsuarioAsync(usuario.Id, ct);
            var almacenes = ConstruirAlmacenesUsuario(usuario, plantas);
            await AplicarConfiguracionUbicacionAsync(almacenes, ct);
            var actual = BuscarAlmacenPermitido(almacenes, almacen);
            if (actual == null)
                return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, message = "Almacén no permitido." });

            var layout = await ObtenerLayoutOperativoUbicacionAsync(actual.Codigo, ct);
            if (layout == null)
                return NotFound(new { ok = false, message = "Este almacén no tiene un layout operativo activo." });

            var cn = await ObtenerConexionSigoAsync(ct);
            return await ConstruirJsonLayout3DAsync(cn, actual, layout.LayoutId, ct);
        }


        [HttpPost("/Surtido/GestionLayouts/GuardarLayout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarLayoutAlmacen(
            GuardarLayoutAlmacenReq req,
            CancellationToken ct = default)
        {
            var acceso = await ValidarGestionLayoutAsync(req.CodigoAlmacen, ct);
            if (!acceso.Ok) return acceso.Error!;
            var usuario = acceso.Usuario!;
            var codigoAlmacen = acceso.Almacen!.Codigo;
            var cn = await ObtenerConexionSigoAsync(ct);

            req.CodigoLayout = (req.CodigoLayout ?? "").Trim().ToUpperInvariant();
            req.NombreLayout = (req.NombreLayout ?? "").Trim();
            req.Descripcion = (req.Descripcion ?? "").Trim();
            if (string.IsNullOrWhiteSpace(req.CodigoLayout) || string.IsNullOrWhiteSpace(req.NombreLayout))
            {
                TempData["Error"] = "Código y nombre del layout son obligatorios.";
                return RedirectGestionLayout(codigoAlmacen, req.Id, "layouts");
            }

            using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                if (req.EsOperativo && req.Activo)
                {
                    const string sqlOcupado = @"
SELECT COUNT_BIG(1)
FROM dbo.SurtidoUbicacionInventario a WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.SurtidoLayoutAlmacen l WITH (UPDLOCK,HOLDLOCK)
    ON l.Id=a.LayoutId
WHERE a.CodigoAlmacen=@CodigoAlmacen AND a.Activo=1
  AND l.CodigoAlmacen=@CodigoAlmacen AND l.Activo=1 AND l.EsOperativo=1
  AND (@Id=0 OR l.Id<>@Id);
";
                    var ocupadas = await cn.ExecuteScalarAsync<long>(sqlOcupado, new { CodigoAlmacen = codigoAlmacen, Id = req.Id }, tx);
                    if (ocupadas > 0)
                    {
                        tx.Rollback();
                        TempData["Error"] = "No puedes cambiar el layout operativo porque el layout actual todavía tiene inventario ubicado.";
                        return RedirectGestionLayout(codigoAlmacen, req.Id, "layouts");
                    }

                    await cn.ExecuteAsync(@"
UPDATE dbo.SurtidoLayoutAlmacen
SET EsOperativo=0, UsuarioModificacion=@Usuario, FechaModificacion=SYSDATETIME()
WHERE CodigoAlmacen=@CodigoAlmacen AND EsOperativo=1 AND Activo=1 AND (@Id=0 OR Id<>@Id);",
                        new { CodigoAlmacen = codigoAlmacen, Id = req.Id, Usuario = usuario.Usuario }, tx);
                }

                const string sql = @"
IF @Id > 0
BEGIN
    UPDATE dbo.SurtidoLayoutAlmacen
    SET CodigoLayout=@CodigoLayout, NombreLayout=@NombreLayout, Descripcion=@Descripcion,
        EsOperativo=@EsOperativo, Orden=@Orden, Activo=@Activo,
        UsuarioModificacion=@Usuario, FechaModificacion=SYSDATETIME()
    WHERE Id=@Id AND CodigoAlmacen=@CodigoAlmacen;
    SELECT @Id;
END
ELSE
BEGIN
    INSERT INTO dbo.SurtidoLayoutAlmacen
    (CodigoAlmacen,CodigoLayout,NombreLayout,Descripcion,EsOperativo,Orden,Activo,UsuarioModificacion)
    VALUES(@CodigoAlmacen,@CodigoLayout,@NombreLayout,@Descripcion,@EsOperativo,@Orden,@Activo,@Usuario);
    SELECT CONVERT(INT,SCOPE_IDENTITY());
END";

                var id = await cn.ExecuteScalarAsync<int>(sql, new
                {
                    req.Id,
                    CodigoAlmacen = codigoAlmacen,
                    req.CodigoLayout,
                    req.NombreLayout,
                    req.Descripcion,
                    req.EsOperativo,
                    req.Orden,
                    req.Activo,
                    Usuario = usuario.Usuario
                }, tx);
                tx.Commit();
                TempData["Exito"] = "Layout guardado correctamente.";
                return RedirectGestionLayout(codigoAlmacen, id, "layouts");
            }
            catch (Exception ex)
            {
                tx.Rollback();
                TempData["Error"] = $"No se pudo guardar el layout: {ex.Message}";
                return RedirectGestionLayout(codigoAlmacen, req.Id, "layouts");
            }
        }


        [HttpPost("/Surtido/GestionLayouts/ActivarLayout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivarLayoutOperativo(
            ActivarLayoutAlmacenReq req,
            CancellationToken ct = default)
        {
            var acceso = await ValidarGestionLayoutAsync(req.CodigoAlmacen, ct);
            if (!acceso.Ok) return acceso.Error!;
            var cn = await ObtenerConexionSigoAsync(ct);
            var usuario = acceso.Usuario!;
            var codigoAlmacen = acceso.Almacen!.Codigo;

            using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                const string sqlValidar = @"
SELECT COUNT_BIG(1)
FROM dbo.SurtidoLayoutAlmacen WITH (UPDLOCK,HOLDLOCK)
WHERE Id=@LayoutId AND CodigoAlmacen=@CodigoAlmacen AND Activo=1;

SELECT COUNT_BIG(1)
FROM dbo.SurtidoUbicacionInventario a WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.SurtidoLayoutAlmacen l WITH (UPDLOCK,HOLDLOCK) ON l.Id=a.LayoutId
WHERE a.CodigoAlmacen=@CodigoAlmacen AND a.Activo=1
  AND l.CodigoAlmacen=@CodigoAlmacen AND l.Activo=1 AND l.EsOperativo=1
  AND l.Id<>@LayoutId;";

                using var multi = await cn.QueryMultipleAsync(sqlValidar, new { req.LayoutId, CodigoAlmacen = codigoAlmacen }, tx);
                var existe = await multi.ReadFirstAsync<long>();
                var ocupadas = await multi.ReadFirstAsync<long>();
                if (existe == 0)
                {
                    tx.Rollback(); TempData["Error"] = "Layout inválido o inactivo.";
                    return RedirectGestionLayout(codigoAlmacen, req.LayoutId, "layouts");
                }
                if (ocupadas > 0)
                {
                    tx.Rollback(); TempData["Error"] = "El layout operativo actual todavía tiene inventario ubicado. Libéralo antes de cambiar.";
                    return RedirectGestionLayout(codigoAlmacen, req.LayoutId, "layouts");
                }

                await cn.ExecuteAsync(@"
UPDATE dbo.SurtidoLayoutAlmacen
SET EsOperativo=CASE WHEN Id=@LayoutId THEN 1 ELSE 0 END,
    UsuarioModificacion=@Usuario, FechaModificacion=SYSDATETIME()
WHERE CodigoAlmacen=@CodigoAlmacen AND Activo=1;",
                    new { req.LayoutId, CodigoAlmacen = codigoAlmacen, Usuario = usuario.Usuario }, tx);
                tx.Commit();
                TempData["Exito"] = "Layout operativo actualizado.";
            }
            catch (Exception ex)
            {
                tx.Rollback(); TempData["Error"] = $"No se pudo activar el layout: {ex.Message}";
            }
            return RedirectGestionLayout(codigoAlmacen, req.LayoutId, "layouts");
        }


        [HttpPost("/Surtido/GestionLayouts/GuardarColor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarColorLayout(GuardarColorLayoutReq req, CancellationToken ct = default)
        {
            var acceso = await ValidarGestionLayoutAsync(req.CodigoAlmacen, ct); if (!acceso.Ok) return acceso.Error!;
            var cn = await ObtenerConexionSigoAsync(ct); var usuario = acceso.Usuario!; var alm = acceso.Almacen!.Codigo;
            if (!await LayoutPerteneceAlmacenAsync(cn, alm, req.LayoutId, ct)) return BadRequest("Layout inválido.");
            try
            {
                req.CodigoColor = (req.CodigoColor ?? "").Trim().ToUpperInvariant(); req.NombreColor = (req.NombreColor ?? "").Trim();
                const string sql = @"
IF @Id>0
 UPDATE dbo.SurtidoColorAlmacen SET CodigoColor=@CodigoColor,NombreColor=@NombreColor,HexColor=@HexColor,Orden=@Orden,Activo=@Activo,UsuarioModificacion=@Usuario,FechaModificacion=SYSDATETIME()
 WHERE Id=@Id AND CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId;
ELSE
 INSERT INTO dbo.SurtidoColorAlmacen(CodigoAlmacen,LayoutId,CodigoColor,NombreColor,HexColor,Orden,Activo,UsuarioModificacion)
 VALUES(@CodigoAlmacen,@LayoutId,@CodigoColor,@NombreColor,@HexColor,@Orden,@Activo,@Usuario);";
                await cn.ExecuteAsync(new CommandDefinition(sql, new { req.Id, CodigoAlmacen = alm, req.LayoutId, req.CodigoColor, req.NombreColor, req.HexColor, req.Orden, req.Activo, Usuario = usuario.Usuario }, cancellationToken: ct));
                TempData["Exito"] = "Color guardado.";
            }
            catch (Exception ex) { TempData["Error"] = $"No se pudo guardar el color: {ex.Message}"; }
            return RedirectGestionLayout(alm, req.LayoutId, "colores");
        }


        [HttpPost("/Surtido/GestionLayouts/GuardarMasterColor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarMasterColorLayout(GuardarMasterColorLayoutReq req, CancellationToken ct = default)
        {
            var acceso = await ValidarGestionLayoutAsync(req.CodigoAlmacen, ct); if (!acceso.Ok) return acceso.Error!;
            var cn = await ObtenerConexionSigoAsync(ct); var usuario = acceso.Usuario!; var alm = acceso.Almacen!.Codigo;
            try
            {
                var colorOk = await cn.ExecuteScalarAsync<long>(new CommandDefinition(@"SELECT COUNT_BIG(1) FROM dbo.SurtidoColorAlmacen WHERE Id=@ColorId AND CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId;", new { ColorId = req.ColorAlmacenId, CodigoAlmacen = alm, req.LayoutId }, cancellationToken: ct));
                if (colorOk == 0) { TempData["Error"] = "El color no pertenece al layout seleccionado."; return RedirectGestionLayout(alm, req.LayoutId, "masters"); }
                const string sql = @"
IF @Id>0
 UPDATE dbo.SurtidoMasterColorAlmacen SET Master=@Master,ColorAlmacenId=@ColorAlmacenId,Activo=@Activo,UsuarioModificacion=@Usuario,FechaModificacion=SYSDATETIME()
 WHERE Id=@Id AND CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId;
ELSE
 INSERT INTO dbo.SurtidoMasterColorAlmacen(CodigoAlmacen,LayoutId,Master,ColorAlmacenId,Activo,UsuarioModificacion)
 VALUES(@CodigoAlmacen,@LayoutId,@Master,@ColorAlmacenId,@Activo,@Usuario);";
                await cn.ExecuteAsync(new CommandDefinition(sql, new { req.Id, CodigoAlmacen = alm, req.LayoutId, Master = (req.Master ?? "").Trim(), req.ColorAlmacenId, req.Activo, Usuario = usuario.Usuario }, cancellationToken: ct));
                TempData["Exito"] = "MASTER / Color guardado.";
            }
            catch (Exception ex) { TempData["Error"] = $"No se pudo guardar MASTER / Color: {ex.Message}"; }
            return RedirectGestionLayout(alm, req.LayoutId, "masters");
        }


        [HttpPost("/Surtido/GestionLayouts/GuardarRotacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarRotacionLayout(GuardarRotacionLayoutReq req, CancellationToken ct = default)
        {
            var acceso = await ValidarGestionLayoutAsync(req.CodigoAlmacen, ct); if (!acceso.Ok) return acceso.Error!;
            var cn = await ObtenerConexionSigoAsync(ct); var usuario = acceso.Usuario!; var alm = acceso.Almacen!.Codigo;
            var modo = (req.ModoOrden ?? "").Trim().ToUpperInvariant();
            if (modo != "INICIO" && modo != "CENTRO" && modo != "FINAL") { TempData["Error"] = "Modo inválido."; return RedirectGestionLayout(alm, req.LayoutId, "rotacion"); }
            try
            {
                const string sql = @"
IF @Id>0
 UPDATE dbo.SurtidoRotacionReglaAlmacen SET NombreRegla=@NombreRegla,Desde=@Desde,Hasta=@Hasta,AplicaSinRotacion=@AplicaSinRotacion,ModoOrden=@ModoOrden,Prioridad=@Prioridad,Activo=@Activo,UsuarioModificacion=@Usuario,FechaModificacion=SYSDATETIME()
 WHERE Id=@Id AND CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId;
ELSE
 INSERT INTO dbo.SurtidoRotacionReglaAlmacen(CodigoAlmacen,LayoutId,NombreRegla,Desde,Hasta,AplicaSinRotacion,ModoOrden,Prioridad,Activo,UsuarioModificacion)
 VALUES(@CodigoAlmacen,@LayoutId,@NombreRegla,@Desde,@Hasta,@AplicaSinRotacion,@ModoOrden,@Prioridad,@Activo,@Usuario);";
                await cn.ExecuteAsync(new CommandDefinition(sql, new { req.Id, CodigoAlmacen = alm, req.LayoutId, NombreRegla = (req.NombreRegla ?? "").Trim(), req.Desde, req.Hasta, req.AplicaSinRotacion, ModoOrden = modo, req.Prioridad, req.Activo, Usuario = usuario.Usuario }, cancellationToken: ct));
                TempData["Exito"] = "Regla de rotación guardada.";
            }
            catch (Exception ex) { TempData["Error"] = $"No se pudo guardar la regla: {ex.Message}"; }
            return RedirectGestionLayout(alm, req.LayoutId, "rotacion");
        }


        [HttpPost("/Surtido/GestionLayouts/GuardarRack")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarRackLayout(GuardarRackLayoutReq req, CancellationToken ct = default)
        {
            var acceso = await ValidarGestionLayoutAsync(req.CodigoAlmacen, ct); if (!acceso.Ok) return acceso.Error!;
            var cn = await ObtenerConexionSigoAsync(ct); var usuario = acceso.Usuario!; var alm = acceso.Almacen!.Codigo;
            try
            {
                const string sql = @"
IF @Id>0
 UPDATE dbo.SurtidoRackAlmacen SET CodigoRack=@CodigoRack,NombreRack=@NombreRack,PosX=@PosX,PosZ=@PosZ,Ancho=@Ancho,Alto=@Alto,Profundidad=@Profundidad,RotacionY=@RotacionY,Niveles=@Niveles,PosicionesPorNivel=@PosicionesPorNivel,HexColor=@HexColor,Orden=@Orden,Activo=@Activo,UsuarioModificacion=@Usuario,FechaModificacion=SYSDATETIME()
 WHERE Id=@Id AND CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId;
ELSE
 INSERT INTO dbo.SurtidoRackAlmacen(CodigoAlmacen,LayoutId,CodigoRack,NombreRack,PosX,PosZ,Ancho,Alto,Profundidad,RotacionY,Niveles,PosicionesPorNivel,HexColor,Orden,Activo,UsuarioModificacion)
 VALUES(@CodigoAlmacen,@LayoutId,@CodigoRack,@NombreRack,@PosX,@PosZ,@Ancho,@Alto,@Profundidad,@RotacionY,@Niveles,@PosicionesPorNivel,@HexColor,@Orden,@Activo,@Usuario);";
                await cn.ExecuteAsync(new CommandDefinition(sql, new { req.Id, CodigoAlmacen = alm, req.LayoutId, CodigoRack = (req.CodigoRack ?? "").Trim().ToUpperInvariant(), NombreRack = (req.NombreRack ?? "").Trim(), req.PosX, req.PosZ, req.Ancho, req.Alto, req.Profundidad, req.RotacionY, req.Niveles, req.PosicionesPorNivel, req.HexColor, req.Orden, req.Activo, Usuario = usuario.Usuario }, cancellationToken: ct));
                TempData["Exito"] = "Rack guardado.";
            }
            catch (Exception ex) { TempData["Error"] = $"No se pudo guardar el rack: {ex.Message}"; }
            return RedirectGestionLayout(alm, req.LayoutId, "racks");
        }


        [HttpPost("/Surtido/GestionLayouts/GuardarUbicacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarUbicacionLayout(GuardarUbicacionLayoutReq req, CancellationToken ct = default)
        {
            var acceso = await ValidarGestionLayoutAsync(req.CodigoAlmacen, ct); if (!acceso.Ok) return acceso.Error!;
            var cn = await ObtenerConexionSigoAsync(ct); var usuario = acceso.Usuario!; var alm = acceso.Almacen!.Codigo;
            try
            {
                var colorOk = await cn.ExecuteScalarAsync<long>(new CommandDefinition(@"SELECT COUNT_BIG(1) FROM dbo.SurtidoColorAlmacen WHERE Id=@ColorId AND CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId AND Activo=1;", new { ColorId = req.ColorAlmacenId, CodigoAlmacen = alm, req.LayoutId }, cancellationToken: ct));
                if (colorOk == 0) { TempData["Error"] = "El color no pertenece al layout."; return RedirectGestionLayout(alm, req.LayoutId, "ubicaciones"); }
                if (req.RackId.HasValue)
                {
                    var rackOk = await cn.ExecuteScalarAsync<long>(new CommandDefinition(@"SELECT COUNT_BIG(1) FROM dbo.SurtidoRackAlmacen WHERE Id=@RackId AND CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId AND Activo=1;", new { req.RackId, CodigoAlmacen = alm, req.LayoutId }, cancellationToken: ct));
                    if (rackOk == 0) { TempData["Error"] = "El rack no pertenece al layout."; return RedirectGestionLayout(alm, req.LayoutId, "ubicaciones"); }
                }
                const string sql = @"
IF @Id>0
 UPDATE dbo.SurtidoUbicacionCatalogo SET ColorAlmacenId=@ColorAlmacenId,RackId=@RackId,Rack=@Rack,Posicion=@Posicion,Altura=@Altura,CodigoUbicacion=@CodigoUbicacion,CodigoMapa3D=@CodigoMapa3D,OrdenFisico=@OrdenFisico,Activo=@Activo,UsuarioModificacion=@Usuario,FechaModificacion=SYSDATETIME()
 WHERE Id=@Id AND CodigoAlmacen=@CodigoAlmacen AND LayoutId=@LayoutId;
ELSE
 INSERT INTO dbo.SurtidoUbicacionCatalogo(CodigoAlmacen,LayoutId,ColorAlmacenId,RackId,Rack,Posicion,Altura,CodigoUbicacion,CodigoMapa3D,OrdenFisico,Activo,UsuarioModificacion)
 VALUES(@CodigoAlmacen,@LayoutId,@ColorAlmacenId,@RackId,@Rack,@Posicion,@Altura,@CodigoUbicacion,@CodigoMapa3D,@OrdenFisico,@Activo,@Usuario);";
                await cn.ExecuteAsync(new CommandDefinition(sql, new { req.Id, CodigoAlmacen = alm, req.LayoutId, req.ColorAlmacenId, req.RackId, Rack = (req.Rack ?? "").Trim().ToUpperInvariant(), Posicion = (req.Posicion ?? "").Trim().ToUpperInvariant(), Altura = (req.Altura ?? "").Trim().ToUpperInvariant(), CodigoUbicacion = (req.CodigoUbicacion ?? "").Trim().ToUpperInvariant(), CodigoMapa3D = (req.CodigoMapa3D ?? "").Trim().ToUpperInvariant(), req.OrdenFisico, req.Activo, Usuario = usuario.Usuario }, cancellationToken: ct));
                TempData["Exito"] = "Ubicación guardada.";
            }
            catch (Exception ex) { TempData["Error"] = $"No se pudo guardar la ubicación: {ex.Message}"; }
            return RedirectGestionLayout(alm, req.LayoutId, "ubicaciones");
        }


        // ============================================================
        // FASE 1B: ESCÁNER DE CAJA O TARIMA - SOLO EVIDENCIA EN SIGO
        // No escribe en Meat, no ejecuta SP, ni modifica remisiones.
        // ============================================================
        [HttpPost("/Surtido/CapturarCodigo")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CapturarCodigo(
            string almacen, int solicitudSurtidoId, string codigoEtiqueta,
            CancellationToken ct = default)
        {
            string codigo = (codigoEtiqueta ?? "").Trim();
            if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 80)
                return BadRequest(new { ok = false, message = "Código inválido." });

            // Los códigos de tarima de Meat empiezan por TAR (p. ej. TARTIF000214005).
            // Las etiquetas individuales siguen utilizando el método ya probado.
            if (!codigo.StartsWith("TAR", StringComparison.OrdinalIgnoreCase))
                return await CapturarCaja(almacen, solicitudSurtidoId, codigo, ct);

            var acceso = await ValidarModuloAsync(almacen, "CAPTURA", ct);
            if (!acceso.Ok) return acceso.Error!;
            var a = acceso.Almacen!;
            if (solicitudSurtidoId <= 0)
                return BadRequest(new { ok = false, message = "Solicitud inválida." });

            var pedido = await ObtenerPedidoPorSolicitudAsync(a, solicitudSurtidoId, ct);
            if (pedido == null)
                return BadRequest(new { ok = false, message = "La solicitud no existe en Meat." });

            // Solo lectura en la base Meat de la planta correcta.
            // Validar de nuevo el contenido actual de la tarima antes de capturar.
            var conexionMeat = ObtenerCadenaMeatPorAlmacen(a);
            await using var meat = new SqlConnection(conexionMeat.Cadena);
            await meat.OpenAsync(ct);
            const string sqlTarima = @"
SELECT t.TarimaId, t.Nombre, t.Estatus
FROM dbo.Tarima t
WHERE t.Nombre = @Codigo;";
            var tarima = await meat.QueryFirstOrDefaultAsync<SurtidoTarimaLecturaDto>(
                new CommandDefinition(sqlTarima, new { Codigo = codigo }, cancellationToken: ct));
            if (tarima == null)
                return BadRequest(new { ok = false, message = "La tarima no existe en Meat." });
            if (!tarima.Estatus)
                return Conflict(new { ok = false, message = "La tarima está inactiva en Meat." });

            const string sqlContenido = @"
SELECT DISTINCT td.ProduccionId
FROM dbo.TarimaDetalle td
JOIN dbo.Produccion p ON p.ProduccionId = td.ProduccionId
WHERE td.TarimaId = @TarimaId AND p.Estatus = 1;";
            var ids = (await meat.QueryAsync<long>(new CommandDefinition(sqlContenido,
                new { tarima.TarimaId }, cancellationToken: ct))).Distinct().ToList();
            if (ids.Count == 0)
                return BadRequest(new { ok = false, message = "La tarima no tiene cajas activas." });
            if (ids.Count > 1000)
                return BadRequest(new { ok = false, message = "Tarima demasiado grande; requiere revisión." });

            var usuario = User.Identity?.Name ?? "SIN_USUARIO";
            var sigo = (SqlConnection)await ObtenerConexionSigoAsync(ct);
            using var tr = (SqlTransaction)await sigo.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                // Solamente cajas BAJADAS para ESTA solicitud y almacén.
                // Nunca se registra todo el contenido de una tarima indiscriminadamente.
                const string sqlPermitidas = @"
SELECT b.ProduccionId, b.CodigoEtiqueta, b.Articulo AS SKU, b.PesoNeto
FROM dbo.SurtidoBajada b WITH (UPDLOCK, HOLDLOCK)
WHERE b.Planta = @Planta AND b.CodigoAlmacen = @Almacen
  AND b.SolicitudSurtidoId = @Solicitud AND b.Activo = 1
  AND b.Estatus = 'BAJADA' AND b.ProduccionId IN @Ids;";
                var permitidas = (await sigo.QueryAsync<SurtidoCajaEscaneoDto>(
                    new CommandDefinition(sqlPermitidas, new
                    {
                        Planta = a.Planta,
                        Almacen = a.Codigo,
                        Solicitud = solicitudSurtidoId,
                        Ids = ids
                    }, tr, cancellationToken: ct)))
                    .GroupBy(x => x.ProduccionId).Select(g => g.First()).ToList();

                if (permitidas.Count == 0)
                {
                    await tr.RollbackAsync(ct);
                    return Conflict(new
                    {
                        ok = false,
                        message =
                        "Ninguna caja de esta tarima está bajada para la solicitud seleccionada."
                    });
                }

                // No crear cabecera nueva para solicitudes cerradas.
                const string sqlCabecera = @"
DECLARE @Id bigint;
SELECT @Id = Id FROM dbo.SurtidoCaptura WITH (UPDLOCK,HOLDLOCK)
WHERE Planta=@Planta AND CodigoAlmacen=@Almacen AND SolicitudSurtidoId=@Solicitud;
IF @Id IS NULL BEGIN
  INSERT dbo.SurtidoCaptura(Planta,CodigoAlmacen,SolicitudSurtidoId,Pedido,Cliente,CreadoPor)
  VALUES(@Planta,@Almacen,@Solicitud,@Pedido,@Cliente,@Usuario);
  SET @Id = CONVERT(bigint,SCOPE_IDENTITY());
END;
SELECT Id,Estatus FROM dbo.SurtidoCaptura WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id;";
                var cab = await sigo.QuerySingleAsync<SurtidoCabeceraCapturaDto>(
                    new CommandDefinition(sqlCabecera, new
                    {
                        Planta = a.Planta,
                        Almacen = a.Codigo,
                        Solicitud = solicitudSurtidoId,
                        Pedido = pedido.Pedido ?? "",
                        Cliente = pedido.Cliente ?? "",
                        Usuario = usuario
                    }, tr, cancellationToken: ct));
                if (cab.Estatus != "CAPTURANDO")
                {
                    await tr.RollbackAsync(ct);
                    return Conflict(new { ok = false, message = "La captura ya está validada o cerrada." });
                }

                // Control de duplicados tanto en la solicitud como en otra activa.
                int nuevas = 0, repetidas = 0, conflicto = 0;
                foreach (var caja in permitidas)
                {
                    if (caja.ProduccionId < 1 || caja.ProduccionId > int.MaxValue)
                    {
                        await tr.RollbackAsync(ct);
                        return Conflict(new { ok = false, message = "Una producción está fuera del rango de Meat." });
                    }
                    const string sqlExistente = @"
SELECT TOP(1) SurtidoCapturaId FROM dbo.SurtidoCapturaDetalle WITH (UPDLOCK,HOLDLOCK)
WHERE Planta=@Planta AND ProduccionId=@Produccion AND Activo=1;";
                    var existente = await sigo.QueryFirstOrDefaultAsync<long?>(
                        new CommandDefinition(sqlExistente,
                            new { Planta = a.Planta, Produccion = caja.ProduccionId }, tr, cancellationToken: ct));
                    if (existente.HasValue)
                    {
                        if (existente.Value == cab.Id) repetidas++;
                        else conflicto++;
                        continue;
                    }
                    const string sqlInsert = @"
INSERT dbo.SurtidoCapturaDetalle
 (SurtidoCapturaId,Planta,ProduccionId,CodigoEtiqueta,SKU,PesoNeto,Tarima,UsuarioCaptura)
 VALUES(@Id,@Planta,@Produccion,@Etiqueta,@SKU,@Peso,@Tarima,@Usuario);
INSERT dbo.SurtidoCapturaEvento
 (SurtidoCapturaId,SolicitudSurtidoId,ProduccionId,CodigoEtiqueta,Evento,Detalle,Usuario)
 VALUES(@Id,@Solicitud,@Produccion,@Etiqueta,'ESCANEO_TARIMA',@Tarima,@Usuario);";
                    await sigo.ExecuteAsync(new CommandDefinition(sqlInsert, new
                    {
                        Id = cab.Id,
                        Planta = a.Planta,
                        Solicitud = solicitudSurtidoId,
                        Produccion = caja.ProduccionId,
                        Etiqueta = caja.CodigoEtiqueta,
                        SKU = caja.SKU,
                        Peso = caja.PesoNeto,
                        Tarima = tarima.Nombre,
                        Usuario = usuario
                    }, tr, cancellationToken: ct));
                    nuevas++;
                }
                await tr.CommitAsync(ct);
                return Ok(new
                {
                    ok = true,
                    tipo = "TARIMA",
                    codigo = tarima.Nombre,
                    detectadas = ids.Count,
                    elegibles = permitidas.Count,
                    capturadas = nuevas,
                    repetidas,
                    conflicto,
                    excluidas = ids.Count - permitidas.Count,
                    message = $"Tarima registrada en SIGO: {nuevas} nuevas, {repetidas} repetidas, {conflicto} asignadas a otra captura, {ids.Count - permitidas.Count} fuera de esta solicitud. Meat no fue modificado."
                });
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                await tr.RollbackAsync(ct);
                return Conflict(new { ok = false, message = "Se detectó una captura simultánea. Actualiza y verifica antes de reintentar." });
            }
            catch { await tr.RollbackAsync(ct); throw; }
        }

        private sealed class SurtidoTarimaLecturaDto
        {
            public int TarimaId { get; set; }
            public string Nombre { get; set; } = "";
            public bool Estatus { get; set; }
        }

        // PEGAR DENTRO DE LA CLASE SurtidoController (antes del último cierre de llave).
        // Depende de: Dapper, Microsoft.Data.SqlClient, ValidarModuloAsync,
        // ObtenerConexionSigoAsync, ObtenerPedidoPorSolicitudAsync.
        // Esta fase SOLO captura en SIGO y genera cola pendiente: NO escribe a Meat.

        [HttpPost("/Surtido/CapturarCaja")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CapturarCaja(
            string almacen, int solicitudSurtidoId, string codigoEtiqueta,
            CancellationToken ct = default)
        {
            var acceso = await ValidarModuloAsync(almacen, "CAPTURA", ct);
            if (!acceso.Ok) return acceso.Error!;
            var a = acceso.Almacen!;
            var usuario = User.Identity?.Name ?? "SIN_USUARIO";
            var etiqueta = (codigoEtiqueta ?? "").Trim();
            if (solicitudSurtidoId <= 0 || string.IsNullOrEmpty(etiqueta))
                return BadRequest(new { ok = false, message = "Solicitud y etiqueta requeridas." });

            // Consulta externa de solo lectura: nunca se hace un INSERT a Meat al escanear.
            var pedido = await ObtenerPedidoPorSolicitudAsync(a, solicitudSurtidoId, ct);
            if (pedido == null)
                return BadRequest(new { ok = false, message = "La solicitud no existe en Meat." });

            var cn = (SqlConnection)await ObtenerConexionSigoAsync(ct);
            using var tr = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            try
            {
                // Bloqueo sobre la evidencia: debe estar bajada en este almacén y solicitud.
                const string sqlBajada = @"
SELECT TOP(1) ProduccionId, CodigoEtiqueta,
       Articulo AS SKU, PesoNeto,
       ISNULL(TarimaCodigo,'') AS Tarima
FROM dbo.SurtidoBajada WITH (UPDLOCK, HOLDLOCK)
WHERE Activo=1 AND Estatus='BAJADA'
  AND Planta=@Planta AND CodigoAlmacen=@Almacen
  AND SolicitudSurtidoId=@Solicitud
  AND CodigoEtiqueta=@Etiqueta
ORDER BY Id DESC;";
                var caja = await cn.QueryFirstOrDefaultAsync<SurtidoCajaEscaneoDto>(
                    new CommandDefinition(sqlBajada, new
                    {
                        Planta = a.Planta,
                        Almacen = a.Codigo,
                        Solicitud = solicitudSurtidoId,
                        Etiqueta = etiqueta
                    }, tr, cancellationToken: ct));
                if (caja == null)
                {
                    await tr.RollbackAsync(ct);
                    return BadRequest(new { ok = false, message = "La caja no está bajada para esta solicitud y almacén." });
                }
                if (caja.ProduccionId < 1 || caja.ProduccionId > int.MaxValue)
                {
                    await tr.RollbackAsync(ct);
                    return BadRequest(new { ok = false, message = "Producción inválida para Meat." });
                }

                const string sqlCabecera = @"
DECLARE @Id bigint;
SELECT @Id=Id FROM dbo.SurtidoCaptura WITH(UPDLOCK,HOLDLOCK)
 WHERE Planta=@Planta AND CodigoAlmacen=@Almacen AND SolicitudSurtidoId=@Solicitud;
IF @Id IS NULL
BEGIN
 INSERT dbo.SurtidoCaptura(Planta,CodigoAlmacen,SolicitudSurtidoId,Pedido,Cliente,CreadoPor)
 VALUES(@Planta,@Almacen,@Solicitud,@Pedido,@Cliente,@Usuario);
 SET @Id=CONVERT(bigint,SCOPE_IDENTITY());
END;
SELECT Id,Estatus FROM dbo.SurtidoCaptura WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;";
                var cab = await cn.QuerySingleAsync<SurtidoCabeceraCapturaDto>(
                    new CommandDefinition(sqlCabecera, new
                    {
                        Planta = a.Planta,
                        Almacen = a.Codigo,
                        Solicitud = solicitudSurtidoId,
                        Pedido = pedido.Pedido ?? "",
                        Cliente = pedido.Cliente ?? "",
                        Usuario = usuario
                    }, tr, cancellationToken: ct));
                if (cab.Estatus != "CAPTURANDO")
                {
                    await tr.RollbackAsync(ct);
                    return Conflict(new { ok = false, message = "La solicitud ya fue cerrada o validada." });
                }

                const string sqlInsertar = @"
IF EXISTS(SELECT 1 FROM dbo.SurtidoCapturaDetalle WITH(UPDLOCK,HOLDLOCK)
          WHERE Planta=@Planta AND ProduccionId=@Produccion AND Activo=1)
 THROW 51001, 'La producción ya está capturada en una solicitud activa.', 1;
INSERT dbo.SurtidoCapturaDetalle
 (SurtidoCapturaId,Planta,ProduccionId,CodigoEtiqueta,SKU,PesoNeto,Tarima,UsuarioCaptura)
 VALUES(@Id,@Planta,@Produccion,@Etiqueta,@SKU,@Peso,@Tarima,@Usuario);
INSERT dbo.SurtidoCapturaEvento
 (SurtidoCapturaId,SolicitudSurtidoId,ProduccionId,CodigoEtiqueta,Evento,Usuario)
 VALUES(@Id,@Solicitud,@Produccion,@Etiqueta,'ESCANEO_ACEPTADO',@Usuario);";
                await cn.ExecuteAsync(new CommandDefinition(sqlInsertar, new
                {
                    Id = cab.Id,
                    Planta = a.Planta,
                    Produccion = caja.ProduccionId,
                    Etiqueta = caja.CodigoEtiqueta,
                    SKU = caja.SKU,
                    Peso = caja.PesoNeto,
                    Tarima = caja.Tarima,
                    Solicitud = solicitudSurtidoId,
                    Usuario = usuario
                }, tr, cancellationToken: ct));
                await tr.CommitAsync(ct);
                return Ok(new
                {
                    ok = true,
                    message = "Caja capturada en SIGO; pendiente de validación.",
                    produccionId = caja.ProduccionId
                });
            }
            catch (SqlException ex) when (ex.Number is 51001 or 2601 or 2627)
            {
                await tr.RollbackAsync(ct);
                return Conflict(new { ok = false, message = "La caja ya había sido capturada." });
            }
            catch
            {
                await tr.RollbackAsync(ct);
                throw;
            }
        }

        // ============================================================
        // SURTIDO 96%: Resumen de cierre (solo lectura).
        // La cifra se recalcula nuevamente bajo transacción al cerrar.
        // ============================================================
        private sealed class SurtidoCantidadSolicitudDto
        {
            public string Articulo { get; set; } = "";
            public string Almacen { get; set; } = "";
            public decimal Cantidad { get; set; }
        }
        private sealed class SurtidoCantidadCapturadaDto
        {
            public string SKU { get; set; } = "";
            public decimal Cajas { get; set; }
            public decimal Kg { get; set; } // Peso real informativo, nunca porcentaje.
        }
        private sealed class SurtidoResumenSkuDto
        {
            public string SKU { get; set; } = "";
            public decimal Solicitado { get; set; }
            public decimal Capturado { get; set; }
            public decimal Faltante { get; set; }
            public decimal Porcentaje { get; set; }
        }
        private sealed class SurtidoResumen96Dto
        {
            public decimal Solicitado { get; set; }
            public decimal Capturado { get; set; }
            public decimal Porcentaje { get; set; }
            public decimal KgCapturados { get; set; }
            public bool RequiereAutorizacion { get; set; }
            public List<SurtidoResumenSkuDto> Skus { get; set; } = new();
        }
        private sealed class SurtidoAutorizadorDto
        {
            public int UsuarioId { get; set; }
            public string Usuario { get; set; } = "";
            public string PasswordHash { get; set; } = "";
        }

        private async Task<List<SurtidoCantidadSolicitudDto>> ObtenerCantidadesSurtido96Async(
            SurtidoAlmacenVM almacen, int solicitud, CancellationToken ct)
        {
            var origen = ObtenerCadenaMeatPorAlmacen(almacen);
            await using var meat = new SqlConnection(origen.Cadena);
            await meat.OpenAsync(ct);
            const string sql = @"
SELECT Articulo = LTRIM(RTRIM(ISNULL(Articulo,''))),
       Almacen = LTRIM(RTRIM(CONVERT(VARCHAR(40),Almacen))),
       Cantidad = CONVERT(DECIMAL(18,3),ISNULL(Cantidad,0))
FROM dbo.SolicitudSurtidoDetalle
WHERE SolicitudSurtidoId = @Solicitud;";
            return (await meat.QueryAsync<SurtidoCantidadSolicitudDto>(
                new CommandDefinition(sql, new { Solicitud = solicitud }, cancellationToken: ct))).ToList();
        }

        private static SurtidoResumen96Dto CalcularResumen96(
            IReadOnlyCollection<SurtidoCantidadSolicitudDto> solicitadas,
            IReadOnlyCollection<SurtidoCantidadCapturadaDto> capturadas, string almacen)
        {
            var pedido = solicitadas
                .Where(x => string.Equals(x.Almacen?.Trim(), almacen.Trim(), StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => (x.Articulo ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Sum(z => z.Cantidad), StringComparer.OrdinalIgnoreCase);
            if (pedido.Count == 0 || pedido.Values.Any(x => x <= 0))
                throw new InvalidOperationException("No hay cantidades solicitadas válidas para este almacén.");
            var capt = capturadas.GroupBy(x => (x.SKU ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Sum(z => z.Cajas), StringComparer.OrdinalIgnoreCase);
            if (capt.Any(x => !pedido.ContainsKey(x.Key) || x.Value <= 0))
                throw new InvalidOperationException("Existen cajas de un SKU no solicitado o cantidades inválidas.");
            // Regla obligatoria: no permitir más cajas de las solicitadas por SKU.
            foreach (var item in capt)
                if (item.Value > pedido[item.Key])
                    throw new InvalidOperationException($"Exceso en SKU {item.Key}: {item.Value:N0} cajas escaneadas, {pedido[item.Key]:N0} solicitadas. Corrige la captura antes de validar.");
            var res = new SurtidoResumen96Dto();
            foreach (var kv in pedido.OrderBy(x => x.Key))
            {
                var real = capt.TryGetValue(kv.Key, out var cajas) ? cajas : 0m;
                res.Skus.Add(new SurtidoResumenSkuDto
                {
                    SKU = kv.Key,
                    Solicitado = kv.Value,
                    Capturado = real,
                    Faltante = Math.Max(0, kv.Value - real),
                    Porcentaje = Math.Round(Math.Min(100m, 100m * real / kv.Value), 2)
                });
            }
            res.Solicitado = res.Skus.Sum(x => x.Solicitado);
            res.Capturado = capt.Values.Sum();
            res.KgCapturados = capturadas.Sum(x => x.Kg);
            // No compensar faltantes de un SKU con excedentes de otro.
            var cumplido = res.Skus.Sum(x => Math.Min(x.Solicitado, x.Capturado));
            res.Porcentaje = Math.Round(100m * cumplido / res.Solicitado, 2);
            res.RequiereAutorizacion = res.Porcentaje < 96m ||
                                        res.Skus.Any(x => x.Porcentaje < 96m);
            return res;
        }

        private const string SQL_CAPTURADAS_96 = @"
;WITH CajasUnicas AS
(
    SELECT d.SKU, d.ProduccionId,
           Kg=MAX(CONVERT(DECIMAL(18,3),ISNULL(d.PesoNeto,0)))
    FROM dbo.SurtidoCapturaDetalle d WITH (HOLDLOCK)
    JOIN dbo.SurtidoCaptura h WITH (UPDLOCK,HOLDLOCK) ON h.Id=d.SurtidoCapturaId
    WHERE h.Planta=@Planta AND h.CodigoAlmacen=@Almacen
      AND h.SolicitudSurtidoId=@Solicitud AND d.Activo=1
    GROUP BY d.SKU,d.ProduccionId
)
SELECT SKU,
       Cajas=CONVERT(DECIMAL(18,3),COUNT_BIG(*)),
       Kg=SUM(Kg)
FROM CajasUnicas GROUP BY SKU;";

        [HttpGet("/Surtido/ResumenCierre")]
        public async Task<IActionResult> ResumenCierre(string almacen, int solicitudSurtidoId, CancellationToken ct = default)
        {
            var acceso = await ValidarModuloAsync(almacen, "CAPTURA", ct);
            if (!acceso.Ok) return acceso.Error!;
            if (solicitudSurtidoId <= 0) return BadRequest(new { ok = false, message = "Solicitud inválida." });
            var a = acceso.Almacen!;
            try
            {
                var origen = await ObtenerCantidadesSurtido96Async(a, solicitudSurtidoId, ct);
                var cn = await ObtenerConexionSigoAsync(ct);
                var capt = (await cn.QueryAsync<SurtidoCantidadCapturadaDto>(new CommandDefinition(
                    SQL_CAPTURADAS_96, new { Planta = a.Planta, Almacen = a.Codigo, Solicitud = solicitudSurtidoId }, cancellationToken: ct))).ToList();
                var resumen = CalcularResumen96(origen, capt, a.Codigo);
                return Ok(new
                {
                    ok = true,
                    resumen.Porcentaje,
                    resumen.Solicitado,
                    resumen.Capturado,
                    resumen.KgCapturados,
                    resumen.RequiereAutorizacion,
                    resumen.Skus,
                    Unidad = "CAJAS"
                });
            }
            catch (Exception)
            {
                return Conflict(new { ok = false, message = "No se pudo calcular el cumplimiento. Comprueba la solicitud y conexión Meat." });
            }
        }

        [HttpPost("/Surtido/ValidarCaptura")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidarCaptura(
            string almacen, int solicitudSurtidoId,
            string? usuarioAutorizador, string? contrasenaAutorizador, string? motivoAutorizacion,
            CancellationToken ct = default)
        {
            var acceso = await ValidarModuloAsync(almacen, "CAPTURA", ct);
            if (!acceso.Ok) return acceso.Error!;
            if (solicitudSurtidoId <= 0) return BadRequest(new { ok = false, message = "Solicitud inválida." });
            var a = acceso.Almacen!;
            var usuario = User.Identity?.Name ?? "SIN_USUARIO";

            // Consultar el pedido real antes de bloquear SIGO. Si Meat falla, no cerrar.
            List<SurtidoCantidadSolicitudDto> origen;
            try { origen = await ObtenerCantidadesSurtido96Async(a, solicitudSurtidoId, ct); }
            catch { return Conflict(new { ok = false, message = "No se pudo comprobar el detalle del pedido en Meat." }); }

            var cn = (SqlConnection)await ObtenerConexionSigoAsync(ct);
            using var tr = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                const string sqlCab = @"
SELECT Id FROM dbo.SurtidoCaptura WITH (UPDLOCK,HOLDLOCK)
WHERE Planta=@Planta AND CodigoAlmacen=@Almacen AND SolicitudSurtidoId=@Solicitud
 AND Estatus='CAPTURANDO';";
                var args = new { Planta = a.Planta, Almacen = a.Codigo, Solicitud = solicitudSurtidoId };
                long id = await cn.QueryFirstOrDefaultAsync<long>(new CommandDefinition(sqlCab, args, tr, cancellationToken: ct));
                if (id <= 0) { await tr.RollbackAsync(ct); return Conflict(new { ok = false, message = "No existe captura abierta para esta solicitud." }); }

                var capt = (await cn.QueryAsync<SurtidoCantidadCapturadaDto>(new CommandDefinition(
                    SQL_CAPTURADAS_96, args, tr, cancellationToken: ct))).ToList();
                if (capt.Count == 0) { await tr.RollbackAsync(ct); return Conflict(new { ok = false, message = "No hay cajas capturadas." }); }
                var pendientes = await cn.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT COUNT(1) FROM dbo.SurtidoBajada b WITH (HOLDLOCK)
WHERE b.Activo=1 AND b.Estatus='BAJADA' AND b.Planta=@Planta AND b.CodigoAlmacen=@Almacen
 AND b.SolicitudSurtidoId=@Solicitud AND NOT EXISTS (
 SELECT 1 FROM dbo.SurtidoCapturaDetalle d WITH (HOLDLOCK)
 WHERE d.SurtidoCapturaId=@Id AND d.Activo=1 AND d.ProduccionId=b.ProduccionId);",
                new { args.Planta, args.Almacen, args.Solicitud, Id = id }, tr, cancellationToken: ct));
                if (pendientes > 0) { await tr.RollbackAsync(ct); return Conflict(new { ok = false, message = $"Hay {pendientes} cajas bajadas sin escanear; primero captúralas o corrige su bajada." }); }

                SurtidoResumen96Dto resumen;
                try { resumen = CalcularResumen96(origen, capt, a.Codigo); }
                catch (InvalidOperationException ex) { await tr.RollbackAsync(ct); return Conflict(new { ok = false, message = ex.Message }); }
                string? supervisor = null;
                if (resumen.RequiereAutorizacion)
                {
                    if (string.IsNullOrWhiteSpace(usuarioAutorizador) || string.IsNullOrWhiteSpace(contrasenaAutorizador) ||
                       string.IsNullOrWhiteSpace(motivoAutorizacion) || motivoAutorizacion.Trim().Length < 10 ||
                       motivoAutorizacion.Length > 500)
                    {
                        await tr.RollbackAsync(ct);
                        return Conflict(new
                        {
                            ok = false,
                            requiereAutorizacion = true,
                            porcentaje = resumen.Porcentaje,
                            message = "Surtido por debajo del 96%. Se requiere usuario, contraseña de SIGO y motivo de al menos 10 caracteres."
                        });
                    }
                    var claveIntentos = usuarioAutorizador.Trim().ToUpperInvariant() + "|" +
                        (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "SIN_IP");
                    if (AutorizadorBloqueado(claveIntentos))
                    {
                        await tr.RollbackAsync(ct);
                        return StatusCode(429, new { ok = false, message = "Demasiados intentos. Reintenta después de 15 minutos o consulta Sistemas." });
                    }
                    // Se reutiliza el hash SHA-256/Base64 de UsuarioSQL del inicio de sesión SIGO.
                    // El permiso se revisa siempre en servidor, no en JavaScript.
                    const string sqlPermiso = @"
SELECT TOP(1) u.Id AS UsuarioId, u.Usuario, u.Password AS PasswordHash
FROM dbo.UsuarioSQL u
JOIN dbo.PerfilPermisoModulo ppm ON ppm.PerfilId=u.PerfilId
 AND ppm.Activo=1 AND ppm.PuedeEscribir=1
JOIN dbo.ModulosSistema m ON m.Id=ppm.ModuloId
 AND m.Clave='SURTIDO_AUTORIZAR_MENOR_96' AND m.Activo=1
WHERE u.Activo=1 AND u.Usuario=@Login;";
                    var supervisorRegistro = await cn.QueryFirstOrDefaultAsync<SurtidoAutorizadorDto>(new CommandDefinition(
                        sqlPermiso, new { Login = usuarioAutorizador.Trim() }, tr, cancellationToken: ct));
                    var loginActual = NormalizeLogin(usuario).username;
                    if (supervisorRegistro == null ||
                       string.Equals(NormalizeLogin(supervisorRegistro.Usuario).username, loginActual, StringComparison.OrdinalIgnoreCase))
                    {
                        RegistrarFalloSupervisor(claveIntentos);
                        await tr.RollbackAsync(ct);
                        return StatusCode(403, new { ok = false, message = "No se pudo autorizar. Revisa usuario, contraseña y permiso del perfil." });
                    }
                    // Comparación en tiempo constante de SHA256(UTF8(contraseña)) contra Base64 almacenado.
                    // Si el esquema de login de SIGO cambia, actualizar este verificador junto con CuentaController.
                    bool valido = false;
                    try
                    {
                        var guardado = Convert.FromBase64String(supervisorRegistro.PasswordHash);
                        var recibido = System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes(contrasenaAutorizador));
                        valido = guardado.Length == recibido.Length &&
                            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(recibido, guardado);
                    }
                    catch (FormatException) { valido = false; }
                    if (!valido)
                    {
                        RegistrarFalloSupervisor(claveIntentos);
                        await tr.RollbackAsync(ct);
                        return StatusCode(403, new { ok = false, message = "No se pudo autorizar. Revisa usuario, contraseña y permiso del perfil." });
                    }
                    ReiniciarFalloSupervisor(claveIntentos);
                    supervisor = supervisorRegistro.Usuario;
                    await cn.ExecuteAsync(new CommandDefinition(@"
INSERT dbo.SurtidoAutorizacionExcepcion
 (SurtidoCapturaId,SolicitudSurtidoId,CodigoAlmacen,Porcentaje,KgSolicitados,KgCapturados,
  CajasSolicitadas,CajasCapturadas,KgInformativos,
  UsuarioCaptura,UsuarioAutorizador,Motivo,DetalleSku)
VALUES(@Id,@Solicitud,@Almacen,@Porcentaje,0,@Kg,
       @Solicitado,@Capturado,@Kg,@Usuario,@Supervisor,@Motivo,@Detalle);",
                    // KgSolicitados heredado: 0 = sin objetivo kg (pedido por CAJAS).
                    // Los datos verificables se guardan en columnas explicitas.
                    new
                    {
                        Id = id,
                        Solicitud = solicitudSurtidoId,
                        Almacen = a.Codigo,
                        Porcentaje = resumen.Porcentaje,
                        Solicitado = resumen.Solicitado,
                        Capturado = resumen.Capturado,
                        Kg = resumen.KgCapturados,
                        Usuario = usuario,
                        Supervisor = supervisor,
                        Motivo = motivoAutorizacion.Trim(),
                        Detalle = System.Text.Json.JsonSerializer.Serialize(new { Unidad = "CAJAS", KgInformativos = resumen.KgCapturados, Skus = resumen.Skus })
                    }, tr, cancellationToken: ct));
                }
                // Registro de cierre y cola de integración en la misma transacción.
                await cn.ExecuteAsync(new CommandDefinition(@"
UPDATE dbo.SurtidoCaptura SET Estatus='VALIDADO',ValidadoPor=@Usuario,FechaValidacion=SYSDATETIME()
WHERE Id=@Id AND Estatus='CAPTURANDO';
IF @@ROWCOUNT<>1 THROW 51020,'La captura cambió de estado.',1;
INSERT dbo.SurtidoIntegracionCola(SurtidoCapturaId,Tipo) VALUES(@Id,'SALIDA_EMBARQUE');
INSERT dbo.SurtidoCapturaEvento(SurtidoCapturaId,SolicitudSurtidoId,Evento,Usuario,Detalle)
VALUES(@Id,@Solicitud,@Evento,@Usuario,@Detalle);",
                new
                {
                    Id = id,
                    Solicitud = solicitudSurtidoId,
                    Usuario = usuario,
                    Evento = supervisor == null ? "CAPTURA_VALIDADA" : "CAPTURA_AUTORIZADA_96",
                    Detalle = $"Cumplimiento por CAJAS {resumen.Porcentaje:N2}%; {resumen.Capturado:N0}/{resumen.Solicitado:N0} cajas; {resumen.KgCapturados:N3} kg; autorizado por {supervisor ?? "regla estándar"}."
                },
                tr, cancellationToken: ct));
                await tr.CommitAsync(ct);
                return Ok(new
                {
                    ok = true,
                    porcentaje = resumen.Porcentaje,
                    cajasSolicitadas = resumen.Solicitado,
                    cajasCapturadas = resumen.Capturado,
                    kgReales = resumen.KgCapturados,
                    message = "Pedido validado y retirado de pendientes. Integración Meat aún pendiente.",
                    id
                });
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627 or 51020)
            {
                await tr.RollbackAsync(ct);
                return Conflict(new { ok = false, message = "La solicitud ya fue validada o se procesó simultáneamente." });
            }
            catch (Exception ex)
            {
                await tr.RollbackAsync(ct);
                System.Diagnostics.Trace.TraceError("Error en surtido/validación: " + ex);
                return StatusCode(500, new { ok = false, message = "Error al procesar la operación. Revisa el registro de SIGO; no se guardaron cambios." });
            }
        }


        // ============================================================
        // QUITAR CAJA / TARIMA DE LA CAPTURA: NO BORRA BAJADA FÍSICA.
        // En caso de necesitar retirar producto de la zona física, es otro flujo
        // del montacarguista que debe validar ubicación y asignaciones.
        // ============================================================
        [HttpPost("/Surtido/QuitarCaptura")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuitarCaptura(
            string almacen, int solicitudSurtidoId, long? produccionId,
            string? tarimaCodigo, string? motivo, CancellationToken ct = default)
        {
            var acceso = await ValidarModuloAsync(almacen, "CAPTURA", ct);
            if (!acceso.Ok) return acceso.Error!;
            var a = acceso.Almacen!;
            var borrarCaja = produccionId.HasValue && produccionId.Value > 0;
            var borrarTarima = !string.IsNullOrWhiteSpace(tarimaCodigo);
            if (solicitudSurtidoId <= 0 || borrarCaja == borrarTarima)
                return BadRequest(new { ok = false, message = "Indica solamente una caja o una tarima." });
            var cod = (tarimaCodigo ?? "").Trim();
            if (cod.Length > 80 || (motivo ?? "").Trim().Length < 5 || motivo!.Length > 500)
                return BadRequest(new { ok = false, message = "Introduce un motivo de al menos 5 caracteres (máximo 500)." });
            var usuario = User.Identity?.Name ?? "SIN_USUARIO";
            var cn = (SqlConnection)await ObtenerConexionSigoAsync(ct);
            using var tr = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var cab = await cn.QueryFirstOrDefaultAsync<SurtidoCabeceraCapturaDto>(new CommandDefinition(@"
SELECT Id,Estatus FROM dbo.SurtidoCaptura WITH(UPDLOCK,HOLDLOCK)
WHERE Planta=@Planta AND CodigoAlmacen=@Almacen AND SolicitudSurtidoId=@Solicitud;",
                new { Planta = a.Planta, Almacen = a.Codigo, Solicitud = solicitudSurtidoId }, tr, cancellationToken: ct));
                if (cab == null || cab.Estatus != "CAPTURANDO")
                {
                    await tr.RollbackAsync(ct);
                    return Conflict(new { ok = false, message = "Solo se pueden retirar cajas de pedidos en captura." });
                }
                // Actualización lógica: el inventario de Meat NO se toca.
                // Se acota por cabecera/solicitud, almacén y estado no integrado.
                // Tarima completa: incluye todos los SKU bajo ese mismo código.
                var sql = @"
DECLARE @Afectadas TABLE(ProduccionId BIGINT,CodigoEtiqueta VARCHAR(80));
UPDATE d SET Activo=0
OUTPUT deleted.ProduccionId,deleted.CodigoEtiqueta INTO @Afectadas
FROM dbo.SurtidoCapturaDetalle d WITH(UPDLOCK)
WHERE d.SurtidoCapturaId=@Id AND d.Activo=1 AND d.EstadoIntegracion='PENDIENTE'
AND ((@EsCaja=1 AND d.ProduccionId=@ProduccionId)
 OR (@EsCaja=0 AND UPPER(LTRIM(RTRIM(d.Tarima)))=@Tarima));
SELECT ProduccionId,CodigoEtiqueta FROM @Afectadas;";
                var cajas = (await cn.QueryAsync<SurtidoCajaEliminadaDto>(new CommandDefinition(sql,
                    new { Id = cab.Id, EsCaja = borrarCaja ? 1 : 0, ProduccionId = produccionId ?? 0, Tarima = cod.ToUpperInvariant() },
                    tr, cancellationToken: ct))).ToList();
                if (cajas.Count == 0)
                {
                    await tr.RollbackAsync(ct);
                    return Conflict(new { ok = false, message = "No hay cajas capturadas activas para retirar; verifica el estado actual." });
                }
                const string sqlEvento = @"
INSERT dbo.SurtidoCapturaEvento
(SurtidoCapturaId,SolicitudSurtidoId,ProduccionId,CodigoEtiqueta,Evento,Detalle,Usuario)
VALUES(@Id,@Solicitud,@Produccion,@Etiqueta,@Evento,@Detalle,@Usuario);";
                foreach (var caja in cajas)
                    await cn.ExecuteAsync(new CommandDefinition(sqlEvento, new
                    {
                        Id = cab.Id,
                        Solicitud = solicitudSurtidoId,
                        Produccion = caja.ProduccionId,
                        Etiqueta = caja.CodigoEtiqueta,
                        Evento = borrarCaja ? "CAPTURA_CAJA_RETIRADA" : "CAPTURA_TARIMA_RETIRADA",
                        Detalle = $"{(borrarCaja ? "Caja" : "Tarima " + cod)}. Motivo: {motivo!.Trim()}",
                        Usuario = usuario
                    }, tr, cancellationToken: ct));
                await tr.CommitAsync(ct);
                return Ok(new
                {
                    ok = true,
                    retiradas = cajas.Count,
                    message = $"Se retiraron {cajas.Count} caja(s) de la captura. La bajada física sigue registrada."
                });
            }
            catch (Exception ex)
            {
                await tr.RollbackAsync(ct);
                System.Diagnostics.Trace.TraceError("Error en surtido/validación: " + ex);
                return StatusCode(500, new { ok = false, message = "Error al procesar la operación. Revisa el registro de SIGO; no se guardaron cambios." });
            }
        }
        private sealed class SurtidoCajaEliminadaDto
        {
            public long ProduccionId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
        }

        private sealed class SurtidoCapturaEstadoDto { public long ProduccionId { get; set; } public string Estatus { get; set; } = ""; }
        private sealed class SurtidoCajaEscaneoDto
        {
            public long ProduccionId { get; set; }
            public string CodigoEtiqueta { get; set; } = "";
            public string SKU { get; set; } = "";
            public decimal? PesoNeto { get; set; }
            public string Tarima { get; set; } = "";
        }
        private sealed class SurtidoCabeceraCapturaDto
        {
            public long Id { get; set; }
            public string Estatus { get; set; } = "";
        }



        // ============================================================
        // HISTORIAL Y REPORTE DE SURTIDOS VALIDADOS (SOLO LECTURA)
        // Origen: SIGO. No infiere remision ni afectacion en Meat.
        // ============================================================
        private sealed class SurtidoCantidadReporteSku
        {
            public int SolicitudSurtidoId { get; set; }
            public string SKU { get; set; } = "";
            public decimal CajasSolicitadas { get; set; }
        }

        [HttpGet("/Surtido/SurtidosRealizados")]
        public async Task<IActionResult> SurtidosRealizados(
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null,
            string? almacen = null,
            List<string>? clientes = null,
            List<int>? solicitudes = null,
            string? formato = null,
            CancellationToken ct = default)
        {
            var usuario = await ObtenerUsuarioActualAsync(ct);
            if (usuario == null) return Forbid();

            // Reporte de consulta: solo capturistas/coordinadores.
            if (!usuario.LogisticaCapturista && !usuario.LogisticaCoordinador)
                return Forbid();

            var plantas = await ObtenerPlantasUsuarioAsync(usuario.Id, ct);
            var almacenes = ConstruirAlmacenesUsuario(usuario, plantas);
            await AplicarConfiguracionUbicacionAsync(almacenes, ct);
            if (almacenes.Count == 0) return Forbid();

            var hoy = DateTime.Today;
            var vm = new SurtidoReporteVM
            {
                FechaInicio = (fechaInicio ?? hoy.AddDays(-7)).Date,
                FechaFin = (fechaFin ?? hoy).Date,
                Almacen = (almacen ?? "").Trim(),
                ClientesSeleccionados = (clientes ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(100).ToList(),
                SolicitudesSeleccionadas = (solicitudes ?? new List<int>())
                    .Where(x => x > 0).Distinct().Take(100).ToList(),
                Formato = string.Equals(formato, "DETALLADO", StringComparison.OrdinalIgnoreCase)
                    ? "DETALLADO" : "CONCENTRADO",
                Almacenes = almacenes.Select(a => new SurtidoReporteAlmacenVM
                { Codigo = a.Codigo, Nombre = a.Nombre })
                    .GroupBy(a => a.Codigo, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First()).OrderBy(a => a.Codigo).ToList()
            };
            if (vm.FechaFin < vm.FechaInicio || (vm.FechaFin - vm.FechaInicio).TotalDays > 92)
            {
                vm.Error = "Selecciona un rango de fechas valido de hasta 93 dias.";
                return View("~/Views/Surtido/SurtidosRealizados.cshtml", vm);
            }

            var seleccionados = string.IsNullOrWhiteSpace(vm.Almacen)
                ? almacenes
                : almacenes.Where(a => string.Equals(a.Codigo, vm.Almacen,
                        StringComparison.OrdinalIgnoreCase)).ToList();
            if (seleccionados.Count == 0) return Forbid();

            // La seguridad se filtra en SQL por par Planta + Almacen,
            // nunca solo por la seleccion de la interfaz.
            var claves = seleccionados.Select(a => a.Planta + "|" + a.Codigo)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            try
            {
                var cn = await ObtenerConexionSigoAsync(ct);
                // El catalogo de filtros muestra surtidos reales validados
                // dentro del rango y exclusivamente en almacenes autorizados.
                const string sqlCatalogo = @"
SELECT TOP (2001)
    h.SolicitudSurtidoId,
    ISNULL(h.Pedido,'') AS Pedido,
    ISNULL(h.Cliente,'') AS Cliente
FROM dbo.SurtidoCaptura h
WHERE (h.Planta + '|' + h.CodigoAlmacen) IN @Claves
  AND h.Estatus IN ('VALIDADO','INTEGRANDO','INTEGRADO','ERROR')
  AND h.FechaValidacion >= @Inicio
  AND h.FechaValidacion < @FinExclusivo
ORDER BY h.FechaValidacion DESC,h.Id DESC;";
                var catalogo = (await cn.QueryAsync<SurtidoReportePedidoVM>(
                    new CommandDefinition(sqlCatalogo, new
                    {
                        Claves = claves,
                        Inicio = vm.FechaInicio,
                        FinExclusivo = vm.FechaFin.AddDays(1)
                    }, cancellationToken: ct))).ToList();
                vm.CatalogoLimitado = catalogo.Count > 2000;
                catalogo = catalogo.Take(2000).ToList();
                vm.ClientesDisponibles = catalogo
                    .Where(x => !string.IsNullOrWhiteSpace(x.Cliente))
                    .Select(x => x.Cliente.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new SurtidoReporteOpcionVM { Valor = x, Texto = x })
                    .ToList();
                vm.SolicitudesDisponibles = catalogo
                    .GroupBy(x => x.SolicitudSurtidoId)
                    .Select(g => g.First())
                    .OrderBy(x => x.Pedido).ThenBy(x => x.SolicitudSurtidoId)
                    .Select(x => new SurtidoReporteOpcionVM
                    {
                        Valor = x.SolicitudSurtidoId.ToString(),
                        Texto = (string.IsNullOrWhiteSpace(x.Pedido)
                            ? "Solicitud " + x.SolicitudSurtidoId
                            : x.Pedido + " · Solicitud " + x.SolicitudSurtidoId)
                            + " · " + x.Cliente
                    }).ToList();

                const string sqlPedidos = @"
SELECT TOP (251)
    h.Id AS CapturaId, h.SolicitudSurtidoId, h.Pedido,
    h.Cliente, h.Planta, h.CodigoAlmacen, h.Estatus,
    ISNULL(h.ValidadoPor,'') AS ValidadoPor,
    h.FechaValidacion, h.FechaIntegracion,
    ISNULL(q.Estatus,'SIN COLA') AS IntegracionEstatus,
    ISNULL(d.Cajas,0) AS Cajas,
    ISNULL(d.Kg,0) AS Kg
FROM dbo.SurtidoCaptura h
OUTER APPLY
(
    SELECT COUNT(*) AS Cajas, SUM(ISNULL(x.PesoNeto,0)) AS Kg
    FROM dbo.SurtidoCapturaDetalle x
    WHERE x.SurtidoCapturaId=h.Id AND x.Activo=1
) d
OUTER APPLY
(
    SELECT TOP (1) t.Estatus
    FROM dbo.SurtidoIntegracionCola t
    WHERE t.SurtidoCapturaId=h.Id AND t.Tipo='SALIDA_EMBARQUE'
    ORDER BY t.Id DESC
) q
WHERE (h.Planta + '|' + h.CodigoAlmacen) IN @Claves
  AND h.Estatus IN ('VALIDADO','INTEGRANDO','INTEGRADO','ERROR')
  AND h.FechaValidacion >= @Inicio
  AND h.FechaValidacion < @FinExclusivo
  AND (@SinClientes=1 OR h.Cliente IN @Clientes)
  AND (@SinSolicitudes=1 OR h.SolicitudSurtidoId IN @Solicitudes)
ORDER BY h.FechaValidacion DESC,h.Id DESC;";
                var rows = (await cn.QueryAsync<SurtidoReportePedidoVM>(
                    new CommandDefinition(sqlPedidos, new
                    {
                        Claves = claves,
                        Inicio = vm.FechaInicio,
                        FinExclusivo = vm.FechaFin.AddDays(1),
                        SinClientes = vm.ClientesSeleccionados.Count == 0 ? 1 : 0,
                        Clientes = vm.ClientesSeleccionados.Count == 0
                            ? new[] { "" } : vm.ClientesSeleccionados.ToArray(),
                        SinSolicitudes = vm.SolicitudesSeleccionadas.Count == 0 ? 1 : 0,
                        Solicitudes = vm.SolicitudesSeleccionadas.Count == 0
                            ? new[] { -1 } : vm.SolicitudesSeleccionadas.ToArray()
                    }, cancellationToken: ct))).ToList();
                vm.LimiteAlcanzado = rows.Count > 250;
                vm.Pedidos = rows.Take(250).ToList();
                vm.TotalCajas = vm.Pedidos.Sum(p => p.Cajas);
                vm.TotalKg = vm.Pedidos.Sum(p => p.Kg);
                // Cantidades solicitadas: SolicitudSurtidoDetalle.Cantidad son CAJAS.
                // Leer desde la conexion Meat de cada almacen autorizado.
                foreach (var grupoAlmacen in vm.Pedidos.GroupBy(p => p.CodigoAlmacen, StringComparer.OrdinalIgnoreCase))
                {
                    var aReporte = seleccionados.FirstOrDefault(a =>
                        string.Equals(a.Codigo, grupoAlmacen.Key, StringComparison.OrdinalIgnoreCase));
                    if (aReporte == null) continue;
                    try
                    {
                        var origenMeat = ObtenerCadenaMeatPorAlmacen(aReporte);
                        await using var cnMeat = new SqlConnection(origenMeat.Cadena);
                        await cnMeat.OpenAsync(ct);
                        const string sqlSolicitadas = @"
SELECT SolicitudSurtidoId, Articulo AS SKU,
       CajasSolicitadas=SUM(CONVERT(DECIMAL(18,3),ISNULL(Cantidad,0)))
FROM dbo.SolicitudSurtidoDetalle
WHERE SolicitudSurtidoId IN @Ids AND LTRIM(RTRIM(Almacen))=@Almacen
GROUP BY SolicitudSurtidoId,Articulo;";
                        var porSku = (await cnMeat.QueryAsync<SurtidoCantidadReporteSku>(
                            new CommandDefinition(sqlSolicitadas,
                                new { Ids = grupoAlmacen.Select(p => p.SolicitudSurtidoId).Distinct().ToArray(), Almacen = grupoAlmacen.Key },
                                cancellationToken: ct))).ToList();
                        foreach (var p in grupoAlmacen)
                        {
                            p.SkusSolicitados = porSku.Where(x => x.SolicitudSurtidoId == p.SolicitudSurtidoId)
                                .Select(x => new SurtidoReporteSkuVM { SKU = x.SKU, CajasSolicitadas = x.CajasSolicitadas }).ToList();
                            p.CajasSolicitadas = p.SkusSolicitados.Sum(x => x.CajasSolicitadas);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceWarning("Reporte surtido: no disponible cantidad solicitada Meat: " + ex.Message);
                        foreach (var p in grupoAlmacen) p.SolicitadasDisponibles = false;
                    }
                }

                if (vm.Pedidos.Count != 0)
                {
                    var ids = vm.Pedidos.Select(p => p.CapturaId).ToArray();
                    const string sqlCajas = @"
SELECT d.SurtidoCapturaId AS CapturaId,
    d.ProduccionId, d.CodigoEtiqueta,d.SKU,
    ISNULL(d.PesoNeto,0) AS PesoNeto,
    ISNULL(d.Tarima,'') AS Tarima,
    d.FechaCaptura, d.UsuarioCaptura,d.EstadoIntegracion,
    ISNULL(b.Lote,'') AS Lote,
    b.FechaProduccion
FROM dbo.SurtidoCapturaDetalle d
INNER JOIN dbo.SurtidoCaptura h ON h.Id=d.SurtidoCapturaId
OUTER APPLY
(
    SELECT TOP (1) sb.Lote,sb.FechaProduccion
    FROM dbo.SurtidoBajada sb
    WHERE sb.Planta=h.Planta AND sb.CodigoAlmacen=h.CodigoAlmacen
      AND sb.SolicitudSurtidoId=h.SolicitudSurtidoId
      AND sb.ProduccionId=d.ProduccionId
    ORDER BY sb.Id DESC
) b
WHERE d.Activo=1 AND d.SurtidoCapturaId IN @Ids
ORDER BY d.SurtidoCapturaId,d.Tarima,d.SKU,d.CodigoEtiqueta;";
                    vm.Cajas = (await cn.QueryAsync<SurtidoReporteCajaVM>(
                        new CommandDefinition(sqlCajas, new { Ids = ids },
                            cancellationToken: ct))).ToList();
                }
            }
            catch (SqlException)
            {
                vm.Error = "No fue posible consultar el historial de surtidos en SIGO.";
            }
            return View("~/Views/Surtido/SurtidosRealizados.cshtml", vm);
        }

        // =============================================================
        // AUTORIZACION COMERCIAL KG - SIGO (no dispara remision)
        // =============================================================
        private sealed class SurtidoAutKgDatos
        {
            public int SolicitudSurtidoId { get; set; }
            public long ColaId { get; set; }
            public string Pedido { get; set; } = "";
            public string Planta { get; set; } = "";
            public string CodigoAlmacen { get; set; } = "";
            public string EstadoCaptura { get; set; } = "";
            public string EstadoCola { get; set; } = "";
            public string EstadoEvaluacion { get; set; } = "";
            public string Sku { get; set; } = "";
            public int Cajas { get; set; }
            public decimal KgReales { get; set; }
            public bool YaAutorizada { get; set; }
        }
        private sealed class SurtidoAutKgUsuario
        {
            public int UsuarioId { get; set; }
            public string Usuario { get; set; } = "";
            public string PasswordHash { get; set; } = "";
        }

        private string CadenaComercialAutKg(string planta)
        {
            var p = (planta ?? "").Trim().ToUpperInvariant();
            string nombreCom = p switch
            {
                "TIF" or "TIF776" or "TIF 776" => "CadenaCommerciaTIF",
                "P1" or "PLANTA1" or "PLANTA 1" => "CadenaCommerciaP1",
                _ => throw new InvalidOperationException("Planta desconocida.")
            };
            string? explicitConnection = _configuracion.GetConnectionString(nombreCom);
            if (!string.IsNullOrWhiteSpace(explicitConnection)) return explicitConnection;
            if (!_configuracion.GetValue<bool>("SurtidoIntegracion:CommerciaEnMismaInstanciaQueMeat"))
                throw new InvalidOperationException("Falta conexión comercial de la planta.");
            string meatKey = p.StartsWith("TIF", StringComparison.Ordinal)
                ? "CadenaMeatTIF" : "CadenaMeatP1";
            string? meatConnection = _configuracion.GetConnectionString(meatKey);
            if (string.IsNullOrWhiteSpace(meatConnection))
                throw new InvalidOperationException("Falta conexión Meat de la planta.");
            var builder = new SqlConnectionStringBuilder(meatConnection)
            {
                InitialCatalog = p.StartsWith("TIF", StringComparison.Ordinal)
                    ? "TIF_CommerciaNET" : "CommerciaNET"
            };
            return builder.ConnectionString;
        }



        private async Task<Plataforma_CG.ViewModels.SurtidoAutorizarKgVM?>
            LeerAutorizacionKgAsync(int solicitudId, CancellationToken ct)
        {
            await using var cn = new SqlConnection(
                _configuracion.GetConnectionString("CadenaSQLSIGO"));

            await cn.OpenAsync(ct);

            // =========================================================
            // 1. OBTENER CAPTURA, COLA, EVALUACION Y MANIFIESTO
            // =========================================================
            var datos = await cn.QuerySingleOrDefaultAsync<SurtidoAutKgDatos>(
                new CommandDefinition(@"
SELECT
    c.SolicitudSurtidoId,
    q.Id AS ColaId,
    c.Pedido,
    c.Planta,
    c.CodigoAlmacen,
    c.Estatus AS EstadoCaptura,
    q.Estatus AS EstadoCola,
    ISNULL(e.Estado, '') AS EstadoEvaluacion,
    MIN(m.SKU) AS Sku,
    COUNT(DISTINCT m.ProduccionId) AS Cajas,
    CAST(SUM(m.KgSIGO) AS decimal(18,3)) AS KgReales,
    CAST(
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.SurtidoAutorizacionKg a
            WHERE a.ColaId = q.Id
              AND a.Estado = 'APROBADA'
        )
        THEN 1 ELSE 0 END
        AS bit
    ) AS YaAutorizada
FROM dbo.SurtidoCaptura c
INNER JOIN dbo.SurtidoIntegracionCola q
    ON q.SurtidoCapturaId = c.Id
   AND q.Tipo = 'SALIDA_EMBARQUE'
INNER JOIN dbo.SurtidoIntegracionEvaluacion e
    ON e.ColaId = q.Id
INNER JOIN dbo.SurtidoIntegracionManifiesto m
    ON m.ColaId = q.Id
   AND m.SolicitudSurtidoId = c.SolicitudSurtidoId
WHERE c.SolicitudSurtidoId = @Solicitud
GROUP BY
    c.SolicitudSurtidoId,
    q.Id,
    c.Pedido,
    c.Planta,
    c.CodigoAlmacen,
    c.Estatus,
    q.Estatus,
    e.Estado;",
                    new { Solicitud = solicitudId },
                    cancellationToken: ct));

            if (datos is null)
                return null;

            // =========================================================
            // 2. VALIDAR ESTADOS
            // =========================================================
            if (!string.Equals(
                    datos.EstadoCaptura,
                    "VALIDADO",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    datos.EstadoCola,
                    "PENDIENTE",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    datos.EstadoEvaluacion,
                    "REVISION_COMERCIAL",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La captura no está validada o la cola no está lista " +
                    "para revisión comercial.");
            }

            // =========================================================
            // 3. VALIDAR MANIFIESTO
            // =========================================================
            if (datos.Cajas <= 0 || datos.KgReales <= 0m)
            {
                throw new InvalidOperationException(
                    "El manifiesto no contiene cajas o kilogramos válidos.");
            }

            // Mantener la protección existente: un solo SKU.
            // Para varios SKU se requiere autorización individual.
            var numSku = await cn.ExecuteScalarAsync<int>(
                new CommandDefinition(@"
SELECT COUNT(DISTINCT SKU)
FROM dbo.SurtidoIntegracionManifiesto
WHERE ColaId = @Cola;",
                    new { Cola = datos.ColaId },
                    cancellationToken: ct));

            if (numSku != 1)
            {
                throw new InvalidOperationException(
                    "Solicitud con varios SKU: requiere autorización por SKU; " +
                    "bloqueada en esta versión.");
            }

            // =========================================================
            // 4. CONECTAR CON MEAT SEGUN PLANTA
            // =========================================================
            var meatKey = (datos.Planta ?? "")
                .Trim()
                .ToUpperInvariant()
                .StartsWith("TIF", StringComparison.Ordinal)
                    ? "CadenaMeatTIF"
                    : "CadenaMeatP1";

            await using var meat = new SqlConnection(
                _configuracion.GetConnectionString(meatKey));

            await meat.OpenAsync(ct);

            // =========================================================
            // 5. VALIDAR REFERENCIA VPED EN MEAT
            // =========================================================
            var referencia = await meat.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(@"
SELECT TOP (1)
    Referencia
FROM dbo.SurtidoReferencia
WHERE SolicitudSurtidoId = @Solicitud
  AND TipoReferenciaId = 9;",
                    new { Solicitud = datos.SolicitudSurtidoId },
                    cancellationToken: ct));

            var partes = (referencia ?? "").Trim().Split('.');

            if (partes.Length != 4 ||
                !partes[2].Equals(
                    "VPED",
                    StringComparison.OrdinalIgnoreCase) ||
                !partes[3].Equals(
                    datos.Pedido,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La referencia VPED de Meat no coincide con la solicitud.");
            }

            // =========================================================
            // 6. CONECTAR A COMMERCIA SEGUN PLANTA
            // =========================================================
            var connCom = CadenaComercialAutKg(datos.Planta);

            await using var com = new SqlConnection(connCom);

            await com.OpenAsync(ct);

            // =========================================================
            // 7. CONSULTAR KG SOLICITADOS EN EL VPED
            // =========================================================
            var kgVped = await com.ExecuteScalarAsync<decimal?>(
                new CommandDefinition(@"
SELECT CAST(
    SUM(
        CAST(m.Unidad AS decimal(18,3)) *
        CAST(m.FactorUnidad AS decimal(18,3))
    ) AS decimal(18,3)
)
FROM dbo.Movimiento m
INNER JOIN dbo.Documento d
    ON d.EmpresaId = m.EmpresaId
   AND d.SucursalId = m.SucursalId
   AND d.OperacionId = m.OperacionId
   AND d.Folio = m.Folio
WHERE m.EmpresaId = @Empresa
  AND m.SucursalId = @Sucursal
  AND m.OperacionId = 'VPED'
  AND m.Folio = @Folio
  AND m.ArticuloId = @Sku
  AND m.EmpaqueId = 'KG'
  AND d.AlmacenId = @Almacen;",
                    new
                    {
                        Empresa = partes[0],
                        Sucursal = partes[1],
                        Folio = partes[3],
                        Sku = datos.Sku,
                        Almacen = datos.CodigoAlmacen
                    },
                    cancellationToken: ct));

            if (kgVped is null || kgVped <= 0m)
            {
                throw new InvalidOperationException(
                    "No se encontró KG comercial válido para " +
                    "el pedido, SKU y almacén.");
            }

            // =========================================================
            // 8. CALCULAR DIFERENCIA DE KILOGRAMOS
            // =========================================================
            decimal pct = Math.Round(
                Math.Abs(kgVped.Value - datos.KgReales)
                    * 100m / kgVped.Value,
                4);

            decimal tolerancia =
                _configuracion.GetValue<decimal?>(
                    "SurtidoIntegracion:ToleranciaKgPorcentaje")
                ?? 0m;

            // =========================================================
            // 9. CONFIGURACION DE AUTORIZACION EXCEPCIONAL
            // =========================================================
            bool permitirExcepcion =
                _configuracion.GetValue<bool>(
                    "SurtidoIntegracion:HabilitarAutorizacionExcepcional");

            decimal maximoExcepcional =
                _configuracion.GetValue<decimal?>(
                    "SurtidoIntegracion:MaximoDiferenciaAutorizablePorcentaje")
                ?? tolerancia;

            // Validar límites de configuración.
            if (tolerancia < 0m || tolerancia > 10m)
            {
                throw new InvalidOperationException(
                    "La tolerancia comercial configurada no es válida.");
            }

            if (permitirExcepcion)
            {
                if (maximoExcepcional < tolerancia ||
                    maximoExcepcional > 25m)
                {
                    throw new InvalidOperationException(
                        "El límite de autorización excepcional " +
                        "configurado no es válido.");
                }

                // Se permite abrir la autorización excepcional,
                // pero NO se aprueba automáticamente.
                if (pct > maximoExcepcional)
                {
                    throw new InvalidOperationException(
                        $"La diferencia de {pct:0.00}% supera " +
                        $"el límite excepcional de {maximoExcepcional:0.00}%.");
                }
            }
            else
            {
                // Comportamiento anterior cuando las
                // autorizaciones excepcionales están deshabilitadas.
                if (pct > tolerancia)
                {
                    throw new InvalidOperationException(
                        $"La diferencia de {pct:0.00}% supera " +
                        $"la tolerancia comercial de {tolerancia:0.00}%.");
                }
            }

            // =========================================================
            // 10. CONSTRUIR MODELO DE AUTORIZACION
            // =========================================================
            // Este método NO aprueba la diferencia.
            // El POST existente valida permiso, contraseña y motivo.
            return new Plataforma_CG.ViewModels.SurtidoAutorizarKgVM
            {
                SolicitudSurtidoId = datos.SolicitudSurtidoId,
                ColaId = datos.ColaId,
                Pedido = datos.Pedido,
                Planta = datos.Planta,
                SKU = datos.Sku,
                Cajas = datos.Cajas,
                KgPlaneados = kgVped.Value,
                KgReales = datos.KgReales,
                DiferenciaPorcentaje = pct,
                ToleranciaPorcentaje = tolerancia,
                YaAutorizada = datos.YaAutorizada
            };
        }





        // ============================================================
        // VALIDAR USUARIO CON PERMISO PARA AUTORIZAR DIFERENCIA KG
        //
        // 1. Identifica al usuario autenticado.
        // 2. Consulta los permisos efectivos de SIGO.
        // 3. Exige permiso ESCRIBIR.
        // 4. Obtiene la identidad desde UsuarioSQL.
        // 5. Devuelve UsuarioId y PasswordHash para la validación
        //    de contraseña que ya realiza el POST.
        //
        // No modifica permisos ni autoriza automáticamente.
        // ============================================================
        private async Task<SurtidoAutKgUsuario?>
     UsuarioAutKgConPermisoAsync(CancellationToken ct)
        {
            // 1. Obtener la identidad autenticada.
            var login = (User?.Identity?.Name ?? "").Trim();

            if (User?.Identity?.IsAuthenticated != true ||
                string.IsNullOrWhiteSpace(login))
            {
                return null;
            }

            // 2. Validar permiso efectivo del módulo.
            var (_, puedeEscribir, _) =
                await PermisosHelper.ObtenerPermisoEfectivoAsync(
                    _context,
                    login,
                    "SURTIDO_AUTORIZAR_DIF_KG"
                );

            if (!puedeEscribir)
                return null;

            // 3. Usar el método de identificación ya existente
            // en SurtidoController, que contempla Usuario y Nombre.
            var usuarioActual = await ObtenerUsuarioActualAsync(ct);

            if (usuarioActual == null || usuarioActual.Id <= 0)
                return null;

            // 4. Consultar el mismo usuario mediante su ID,
            // usando el AppDbContext de SIGO.
            var cn = _context.Database.GetDbConnection();

            if (cn.State != ConnectionState.Open)
                await _context.Database.OpenConnectionAsync(ct);

            const string sql = @"
SELECT
    u.Id AS UsuarioId,
    u.Usuario,
    u.Password AS PasswordHash
FROM dbo.UsuarioSQL u
WHERE u.Id = @UsuarioId
  AND u.Activo = 1;";

            return await cn.QuerySingleOrDefaultAsync<SurtidoAutKgUsuario>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        UsuarioId = usuarioActual.Id
                    },
                    cancellationToken: ct
                )
            );
        }




        // =============================================================
        // BANDEJA COMERCIAL DE AUTORIZACIONES DE DIFERENCIA KG
        // Solo lectura. No modifica colas, remisiones ni inventario.
        // =============================================================
        public sealed class SurtidoAutorizacionFilaVM
        {
            public long ColaId { get; set; }
            public int SolicitudSurtidoId { get; set; }
            public string Pedido { get; set; } = "";
            public string Planta { get; set; } = "";
            public string CodigoAlmacen { get; set; } = "";
            public string EstadoCaptura { get; set; } = "";
            public string EstadoCola { get; set; } = "";
            public string EstadoEvaluacion { get; set; } = "";
            public string EstadoAutorizacion { get; set; } = "";
            public DateTime? FechaValidacion { get; set; }
            public int Cajas { get; set; }
            public int TotalSku { get; set; }
            public string Sku { get; set; } = "";
            public decimal KgReales { get; set; }
            public string Detalle { get; set; } = "";
            public bool PuedeAutorizar =>
                EstadoCaptura == "VALIDADO" &&
                EstadoCola == "PENDIENTE" &&
                EstadoEvaluacion == "REVISION_COMERCIAL" &&
                string.IsNullOrEmpty(EstadoAutorizacion) &&
                Cajas > 0 && TotalSku == 1;
        }

        [HttpGet("/Surtido/AutorizacionesDiferenciaKg")]
        [RevisarPermiso("SURTIDO_AUTORIZAR_DIF_KG", "ESCRIBIR")]
        public async Task<IActionResult> AutorizacionesDiferenciaKg(CancellationToken ct)
        {
            await using var cn = new SqlConnection(
                _configuracion.GetConnectionString("CadenaSQLSIGO"));
            await cn.OpenAsync(ct);

            // Una fila por cola. No se mezclan almacenes, solicitudes ni SKU.
            // Estados de evaluación se muestran, pero solo REVISION_COMERCIAL
            // es autorizable en el controlador existente.
            const string sql = @"
SELECT TOP (200)
    q.Id AS ColaId,
    c.SolicitudSurtidoId,
    c.Pedido,
    c.Planta,
    c.CodigoAlmacen,
    c.Estatus AS EstadoCaptura,
    q.Estatus AS EstadoCola,
    ISNULL(e.Estado, '') AS EstadoEvaluacion,
    ISNULL(a.Estado, '') AS EstadoAutorizacion,
    c.FechaValidacion,
    ISNULL(man.Cajas,0) AS Cajas,
    ISNULL(man.TotalSku,0) AS TotalSku,
    ISNULL(man.Sku,'') AS Sku,
    ISNULL(man.KgReales,0) AS KgReales,
    ISNULL(e.Detalle,'') AS Detalle
FROM dbo.SurtidoIntegracionCola q
INNER JOIN dbo.SurtidoCaptura c ON c.Id=q.SurtidoCapturaId
LEFT JOIN dbo.SurtidoIntegracionEvaluacion e ON e.ColaId=q.Id
OUTER APPLY (
    SELECT TOP (1) a0.Estado
    FROM dbo.SurtidoAutorizacionKg a0
    WHERE a0.ColaId=q.Id
    ORDER BY a0.ColaId
) a
OUTER APPLY (
    SELECT COUNT(DISTINCT m.ProduccionId) AS Cajas,
           COUNT(DISTINCT m.SKU) AS TotalSku,
           MIN(m.SKU) AS Sku,
           CAST(SUM(m.KgSIGO) AS decimal(18,3)) AS KgReales
    FROM dbo.SurtidoIntegracionManifiesto m
    WHERE m.ColaId=q.Id AND m.SolicitudSurtidoId=c.SolicitudSurtidoId
) man
WHERE q.Tipo='SALIDA_EMBARQUE'
  AND c.Estatus='VALIDADO'
  AND (q.Estatus='PENDIENTE' OR a.Estado='APROBADA')
ORDER BY
    CASE WHEN q.Estatus='PENDIENTE'
               AND e.Estado='REVISION_COMERCIAL'
               AND a.Estado IS NULL
               AND ISNULL(man.Cajas,0)>0
               AND ISNULL(man.TotalSku,0)=1
         THEN 0 ELSE 1 END,
    q.Id DESC;";

            var solicitudes = (await cn.QueryAsync<SurtidoAutorizacionFilaVM>(
                new CommandDefinition(sql, cancellationToken: ct))).ToList();

            return View("~/Views/Surtido/AutorizacionesDiferenciaKg.cshtml", solicitudes);
        }

        [HttpGet("/Surtido/AutorizarDiferenciaKg")]
        [RevisarPermiso("SURTIDO_AUTORIZAR_DIF_KG", "ESCRIBIR")]
        public async Task<IActionResult> AutorizarDiferenciaKg(
     int solicitudSurtidoId,
     CancellationToken ct)
        {
            var vm = await LeerAutorizacionKgAsync(
                solicitudSurtidoId, ct);

            if (vm is null)
                return NotFound("No existe el surtido solicitado.");

            return View(
                "~/Views/Surtido/AutorizarDiferenciaKg.cshtml",
                vm);
        }


        [HttpPost("/Surtido/AutorizarDiferenciaKg")]
        [ValidateAntiForgeryToken]
        [RevisarPermiso("SURTIDO_AUTORIZAR_DIF_KG", "ESCRIBIR")]
        public async Task<IActionResult> AutorizarDiferenciaKg(
            Plataforma_CG.ViewModels.SurtidoAutorizarKgVM entrada,
            CancellationToken ct)
        {
            const string rutaVista =
                "~/Views/Surtido/AutorizarDiferenciaKg.cshtml";

            const string clavePermiso =
                "SURTIDO_AUTORIZAR_DIF_KG";

            // ==========================================================
            // 1. VALIDAR DATOS DE ENTRADA
            // ==========================================================
            if (entrada == null || entrada.SolicitudSurtidoId <= 0)
            {
                return BadRequest(
                    "No se recibió una solicitud de surtido válida.");
            }

            // ==========================================================
            // 2. CARGAR ESTADO REAL DESDE SIGO
            // No confiar en KG, cajas ni cola recibidos del navegador.
            // ==========================================================
            Plataforma_CG.ViewModels.SurtidoAutorizarKgVM vm;

            try
            {
                vm = await LeerAutorizacionKgAsync(
                    entrada.SolicitudSurtidoId,
                    ct
                ) ?? throw new InvalidOperationException(
                    "No se encontró la solicitud de surtido.");
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }

            vm.Motivo = (entrada.Motivo ?? "").Trim();

            if (vm.YaAutorizada)
            {
                return Conflict(
                    "Esta solicitud ya tiene una autorización registrada.");
            }

            // ==========================================================
            // 3. VALIDAR IDENTIDAD AUTENTICADA
            // ==========================================================
            var login = (User?.Identity?.Name ?? "").Trim();

            if (User?.Identity?.IsAuthenticated != true ||
                string.IsNullOrWhiteSpace(login))
            {
                return Unauthorized(
                    "No se identificó una sesión de SIGO válida.");
            }

            // ==========================================================
            // 4. VALIDAR PERMISO EFECTIVO DE SIGO
            // Incluye la resolución habitual de permisos por usuario
            // y perfil. No se otorgan permisos automáticamente.
            // ==========================================================
            var (_, puedeEscribir, _) =
                await PermisosHelper.ObtenerPermisoEfectivoAsync(
                    _context,
                    login,
                    clavePermiso
                );

            if (!puedeEscribir)
            {
                return StatusCode(
                    403,
                    "El usuario autenticado no tiene permiso efectivo " +
                    "ESCRIBIR para autorizar diferencias de KG.");
            }

            // ==========================================================
            // 5. OBTENER IDENTIDAD PARA CONFIRMAR CONTRASEÑA
            // ==========================================================
            var usuario = await UsuarioAutKgConPermisoAsync(ct);

            if (usuario == null)
            {
                return StatusCode(
                    403,
                    "El permiso fue evaluado, pero no se pudo " +
                    "identificar al usuario activo en UsuarioSQL. " +
                    "Verifica el login de la sesión y la cuenta SQL.");
            }

            // ==========================================================
            // 6. VALIDAR MOTIVO Y CONTRASEÑA
            // ==========================================================
            if (vm.Motivo.Length < 10 || vm.Motivo.Length > 500)
            {
                vm.Error =
                    "El motivo debe contener entre 10 y 500 caracteres.";

                return View(rutaVista, vm);
            }

            if (string.IsNullOrWhiteSpace(entrada.Contrasena))
            {
                vm.Error =
                    "Debes ingresar tu contraseña de SIGO " +
                    "para confirmar la autorización.";

                return View(rutaVista, vm);
            }

            // ==========================================================
            // 7. CONTROL DE INTENTOS
            // ==========================================================
            var claveIntentos =
                usuario.Usuario.ToUpperInvariant() + "|" +
                (HttpContext.Connection.RemoteIpAddress?.ToString()
                 ?? "SIN_IP");

            if (AutorizadorBloqueado(claveIntentos))
            {
                return StatusCode(
                    429,
                    "Demasiados intentos de autorización. " +
                    "Intenta nuevamente más tarde.");
            }

            // ==========================================================
            // 8. VALIDACIÓN DE CONTRASEÑA
            //
            // Conserva el esquema SHA256/Base64 de tu código anterior.
            // Debe coincidir con el esquema real de acceso de SIGO.
            // ==========================================================
            bool passwordOk = false;

            try
            {
                var hashGuardado =
                    Convert.FromBase64String(usuario.PasswordHash ?? "");

                var hashIngresado =
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(
                            entrada.Contrasena
                        )
                    );

                passwordOk =
                    hashGuardado.Length == hashIngresado.Length &&
                    System.Security.Cryptography.CryptographicOperations
                        .FixedTimeEquals(
                            hashGuardado,
                            hashIngresado
                        );
            }
            catch (FormatException)
            {
                passwordOk = false;
            }

            if (!passwordOk)
            {
                RegistrarFalloSupervisor(claveIntentos);

                vm.Error =
                    "No se pudo confirmar la contraseña del usuario.";

                return View(rutaVista, vm);
            }

            ReiniciarFalloSupervisor(claveIntentos);

            // ==========================================================
            // 9. REVALIDAR PERMISO ANTES DE GUARDAR
            // ==========================================================
            var (_, permisoEscrituraActual, _) =
                await PermisosHelper.ObtenerPermisoEfectivoAsync(
                    _context,
                    login,
                    clavePermiso
                );

            if (!permisoEscrituraActual)
            {
                return StatusCode(
                    403,
                    "El permiso de autorización ya no está vigente.");
            }

            // ==========================================================
            // 10. CONEXIÓN SIGO
            // ==========================================================
            var cadena =
                _configuracion.GetConnectionString("CadenaSQLSIGO");

            if (string.IsNullOrWhiteSpace(cadena))
            {
                throw new InvalidOperationException(
                    "No existe ConnectionStrings:CadenaSQLSIGO.");
            }

            await using var cn = new SqlConnection(cadena);

            await cn.OpenAsync(ct);

            // ==========================================================
            // 11. GUARDAR AUTORIZACIÓN
            //
            // La transacción comprueba:
            // - Cola pendiente
            // - Captura validada
            // - Evaluación comercial
            // - Usuario activo
            // - Ausencia de autorización previa
            // - Cajas y pesos del manifiesto
            //
            // El permiso efectivo ya fue comprobado en el servidor.
            // No repetir aquí un JOIN exclusivo a PerfilPermisoModulo,
            // porque podría contradecir los permisos individuales.
            // ==========================================================
            const string sql = @"
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.SurtidoIntegracionCola q
            WITH (UPDLOCK, HOLDLOCK)

        INNER JOIN dbo.SurtidoCaptura c
            ON c.Id = q.SurtidoCapturaId

        INNER JOIN dbo.SurtidoIntegracionEvaluacion e
            ON e.ColaId = q.Id

        WHERE q.Id = @Cola
          AND c.SolicitudSurtidoId = @Solicitud
          AND c.Pedido = @Pedido
          AND c.Planta = @Planta
          AND c.Estatus = 'VALIDADO'
          AND q.Estatus = 'PENDIENTE'
          AND e.Estado = 'REVISION_COMERCIAL'
    )
    BEGIN
        THROW 51710,
            'La cola ya no permite autorizacion.', 1;
    END;

    -- Identidad activa: la autorización siempre se atribuye
    -- al usuario real que inició sesión.
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.UsuarioSQL u
        WHERE u.Id = @UsuarioId
          AND u.Activo = 1
          AND u.Usuario = @Usuario
    )
    BEGIN
        THROW 51713,
            'El usuario autorizador ya no esta activo.', 1;
    END;

    -- Evitar duplicados.
    IF EXISTS
    (
        SELECT 1
        FROM dbo.SurtidoAutorizacionKg
            WITH (UPDLOCK, HOLDLOCK)
        WHERE ColaId = @Cola
    )
    BEGIN
        THROW 51711,
            'Ya existe autorizacion para esta cola.', 1;
    END;

    -- Revisión de cajas y kilogramos reales.
    IF
    (
        SELECT COUNT(DISTINCT ProduccionId)
        FROM dbo.SurtidoIntegracionManifiesto
        WHERE ColaId = @Cola
          AND SolicitudSurtidoId = @Solicitud
          AND SKU = @Sku
    ) <> @Cajas
    BEGIN
        THROW 51712,
            'La cantidad de cajas del manifiesto cambio.', 1;
    END;

    IF ABS
    (
        ISNULL
        (
            (
                SELECT SUM(KgSIGO)
                FROM dbo.SurtidoIntegracionManifiesto
                WHERE ColaId = @Cola
                  AND SolicitudSurtidoId = @Solicitud
                  AND SKU = @Sku
            ), 0
        ) - @KgReales
    ) > 0.001
    BEGIN
        THROW 51714,
            'Los KG del manifiesto cambiaron.', 1;
    END;

    -- No admitir otros SKU en la cola.
    IF EXISTS
    (
        SELECT 1
        FROM dbo.SurtidoIntegracionManifiesto
        WHERE ColaId = @Cola
          AND
          (
              SolicitudSurtidoId <> @Solicitud
              OR SKU <> @Sku
          )
    )
    BEGIN
        THROW 51715,
            'El manifiesto contiene articulos inesperados.', 1;
    END;

    -- Autorizacion EXCEPCIONAL: la diferencia debe SUPERAR
    -- la tolerancia ordinaria. La pantalla ya verifico el
    -- maximo excepcional configurado y el permiso del usuario.
    -- Se recalculan los porcentajes a partir de los kg del servidor.
    IF @KgPlaneados <= 0
       OR @KgReales <= 0
       OR @Tolerancia < 0
       OR @Tolerancia > 10.000
       OR ABS(
           (
               ABS(@KgPlaneados - @KgReales)
               * 100.0 / NULLIF(@KgPlaneados, 0)
           ) - @Pct
       ) > 0.01
       OR @Pct <= @Tolerancia
    BEGIN
        THROW 51716,
            'La diferencia no requiere autorizacion excepcional o los datos cambiaron.', 1;
    END;

    -- Registrar evidencia comercial.
    INSERT INTO dbo.SurtidoAutorizacionKg
    (
        ColaId,
        SolicitudSurtidoId,
        FolioVPED,
        Planta,
        SKU,
        Cajas,
        KgPlaneados,
        KgReales,
        DiferenciaPorcentaje,
        ToleranciaPorcentaje,
        UsuarioAutorizadorId,
        UsuarioAutorizador,
        Motivo
    )
    VALUES
    (
        @Cola,
        @Solicitud,
        @Pedido,
        @Planta,
        @Sku,
        @Cajas,
        @KgPlaneados,
        @KgReales,
        @Pct,
        @Tolerancia,
        @UsuarioId,
        @Usuario,
        @Motivo
    );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;";

            try
            {
                await cn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Cola = vm.ColaId,
                            Solicitud = vm.SolicitudSurtidoId,
                            Pedido = vm.Pedido,
                            Planta = vm.Planta,
                            Sku = vm.SKU,
                            Cajas = vm.Cajas,
                            KgPlaneados = vm.KgPlaneados,
                            KgReales = vm.KgReales,
                            Pct = vm.DiferenciaPorcentaje,
                            Tolerancia = vm.ToleranciaPorcentaje,
                            UsuarioId = usuario.UsuarioId,
                            Usuario = usuario.Usuario,
                            Motivo = vm.Motivo
                        },
                        cancellationToken: ct
                    )
                );
            }
            catch (SqlException ex)
            {
                vm.Error = ex.Number switch
                {
                    51710 =>
                        "La solicitud ya no está pendiente de autorización.",

                    51711 or 2601 or 2627 =>
                        "La autorización ya fue registrada anteriormente.",

                    51712 or 51714 or 51715 =>
                        "El manifiesto cambió. Es necesario volver a revisarlo.",

                    51713 =>
                        "El usuario ya no está activo.",

                    51716 =>
                        "La diferencia no requiere autorización excepcional o cambiaron los kilogramos.",

                    _ =>
                        "No fue posible guardar la autorización. " +
                        "Error SQL: " + ex.Number
                };

                return View(rutaVista, vm);
            }

            // ==========================================================
            // 12. FINALIZAR
            //
            // No ejecutar VREM.
            // No modificar inventario.
            // No enviar a SAP.
            // ==========================================================
            TempData["Exito"] =
                "La diferencia comercial de KG fue autorizada " +
                "y registrada correctamente.";

            return Redirect(
      Url.Action(
          nameof(AutorizarDiferenciaKg),
          "Surtido",
          new { solicitudSurtidoId = vm.SolicitudSurtidoId }
      )!
  );
        }


    }
}