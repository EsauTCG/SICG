using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
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
        private async Task CompletarBajadasEnPedidosAsync(
            List<SurtidoPedidoVM> pedidos,
            SurtidoAlmacenVM almacen,
            CancellationToken ct)
        {
            if (pedidos.Count == 0)
            {
                return;
            }

            var ids =
                pedidos
                    .Select(x =>
                        Convert.ToInt64(
                            x.SolicitudSurtidoId
                        )
                    )
                    .Distinct()
                    .ToList();

            var conn =
                await ObtenerConexionSigoAsync(ct);

            const string sql = @"
SELECT
    SolicitudSurtidoId,
    Cajas =
        COUNT_BIG(1)
FROM dbo.SurtidoBajada WITH (NOLOCK)
WHERE Activo = 1
  AND Estatus = 'BAJADA'
  AND Planta = @Planta
  AND CodigoAlmacen = @CodigoAlmacen
  AND SolicitudSurtidoId IN @Ids
GROUP BY
    SolicitudSurtidoId;
";

            var rows =
                (
                    await conn.QueryAsync<dynamic>(
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

            var mapa =
                rows.ToDictionary(
                    x =>
                        Convert.ToInt64(
                            x.SolicitudSurtidoId
                        ),
                    x =>
                        Convert.ToInt32(
                            x.Cajas
                        )
                );

            foreach (var pedido in pedidos)
            {
                if (mapa.TryGetValue(
                        pedido.SolicitudSurtidoId,
                        out var cajas))
                {
                    pedido.CajasBajadas =
                        cajas;
                }
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

            ViewBag.ZonaSurtido =
                await ObtenerZonaSurtidoAsync(
                    acceso.Almacen!,
                    ct
                );

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

    }
}