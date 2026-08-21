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
                DateTime fechaInicio,
                DateTime fechaFin,
                CancellationToken ct = default)
        {
            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            const string sql = @"
;WITH Solicitado AS
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
    FROM dbo.SalidaEmbarque se WITH (NOLOCK)
    INNER JOIN dbo.Produccion p WITH (NOLOCK)
        ON p.ProduccionId = se.ProduccionId
    WHERE CONVERT(VARCHAR(40), p.Almacen) = @Almacen
    GROUP BY
        se.SolicitudSurtidoId
)
SELECT
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

    ss.EstatusId,

    CajasSolicitadas =
        ISNULL(sol.CajasSolicitadas, 0),

    CajasSurtidas =
        CONVERT(
            INT,
            ISNULL(sur.CajasSurtidas, 0)
        )

FROM dbo.SolicitudSurtido ss WITH (NOLOCK)

INNER JOIN Solicitado sol
    ON sol.SolicitudSurtidoId = ss.SolicitudSurtidoId

INNER JOIN dbo.SurtidoReferencia srp WITH (NOLOCK)
    ON srp.SolicitudSurtidoId = ss.SolicitudSurtidoId
   AND srp.TipoReferenciaId = 9

LEFT JOIN dbo.SurtidoReferencia src WITH (NOLOCK)
    ON src.SolicitudSurtidoId = ss.SolicitudSurtidoId
   AND src.TipoReferenciaId = 6

LEFT JOIN Surtido sur
    ON sur.SolicitudSurtidoId = ss.SolicitudSurtidoId

WHERE ss.EstatusId = 1
  AND ss.FechaHora >= @FechaInicio
  AND ss.FechaHora < DATEADD(
        DAY,
        1,
        @FechaFin
      )

ORDER BY
    ss.FechaHora ASC,
    ss.SolicitudSurtidoId ASC;
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
                                    fechaInicio.Date,

                                FechaFin =
                                    fechaFin.Date,

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

            // Si esta parte del pedido ya quedó completamente atendida
            // en este almacén, no debe seguir haciendo ruido en "Mis pedidos".
            return lista
                .Where(x =>
                    x.CajasPendientes > 0
                )
                .ToList();
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
        private async Task<List<SurtidoPepsCajaVM>>
            ObtenerCajasPepsDisponiblesAsync(
                SurtidoAlmacenVM almacen,
                string articulo,
                int top,
                CancellationToken ct)
        {
            var conexion =
                ObtenerCadenaMeatPorAlmacen(
                    almacen
                );

            top =
                Math.Max(
                    50,
                    Math.Min(
                        top,
                        1000
                    )
                );

            const string sql = @"
SELECT TOP (@Top)

    -- =====================================================
    -- PRODUCCIÓN / CAJA
    -- =====================================================
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

    -- =====================================================
    -- LOTE / PEPS
    -- =====================================================
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

    -- =====================================================
    -- TARIMA
    -- =====================================================
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

    -- =====================================================
    -- UBICACIÓN REAL
    -- ProduccionReferencia.TipoReferenciaId = 16
    -- Ejemplo: R4-04A
    -- =====================================================
    UbicacionOrigen =
        ISNULL(
            ux.Ubicacion,
            ''
        )

FROM dbo.Produccion p WITH (NOLOCK)

LEFT JOIN dbo.Lote l WITH (NOLOCK)
    ON l.LoteId = p.LoteId


-- =========================================================
-- TARIMA ACTUAL DE LA PRODUCCIÓN
-- =========================================================
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

    WHERE td.ProduccionId =
        p.ProduccionId

    ORDER BY
        td.FechaHora DESC,
        td.TarimaId DESC

) tx


-- =========================================================
-- UBICACIÓN ACTUAL DE LA PRODUCCIÓN
-- TipoReferenciaId = 16
-- =========================================================
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

    WHERE pr.ProduccionId =
        p.ProduccionId

      AND pr.TipoReferenciaId =
        16

    ORDER BY
        pr.FechaHora DESC

) ux


WHERE p.Estatus = 1

  AND p.Articulo =
        @Articulo

  AND CONVERT(
        VARCHAR(40),
        p.Almacen
      ) =
        @Almacen


-- =========================================================
-- NO mostrar producción que ya salió oficialmente
-- =========================================================
AND NOT EXISTS
(
    SELECT 1

    FROM dbo.SalidaEmbarque se WITH (NOLOCK)

    WHERE se.ProduccionId =
        p.ProduccionId
)


-- =========================================================
-- PEPS:
-- fecha más antigua primero
-- =========================================================
ORDER BY

    COALESCE(
        l.FechaProduccion,
        p.FechaProduccion
    ) ASC,

    p.ProduccionId ASC;
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
                                Top =
                                    top,

                                Articulo =
                                    articulo,

                                Almacen =
                                    almacen.Codigo
                            },
                            cancellationToken: ct
                        )
                    )
                )
                .ToList();


            // =====================================================
            // Excluir cajas que ya fueron bajadas/reservadas
            // activamente en SIGO.
            // Aunque sigan activas en Meat, otro operador ya las
            // tomó para una solicitud.
            // =====================================================
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

  AND Planta =
        @Planta

  AND CodigoAlmacen =
        @CodigoAlmacen

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

            return cajas;
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

                // El pedido puede contener varios almacenes.
                // Picking sólo atiende el almacén activo.
                //
                // Además agrupamos Articulo + Almacen porque una solicitud
                // puede contener más de una línea del mismo SKU. PEPS debe
                // trabajar contra la cantidad TOTAL solicitada del SKU.
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

                foreach (var d in detalle)
                {
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
                        d.Articulo,
                        out var cajasSurtidas
                    );

                    bajadas.TryGetValue(
                        d.Articulo,
                        out var cajasBajadas
                    );

                    var pendientes =
                        Math.Max(
                            0,
                            cajasSolicitadas
                            - cajasSurtidas
                            - cajasBajadas
                        );

                    var disponibles =
                        await ObtenerCajasPepsDisponiblesAsync(
                            almacen,
                            d.Articulo,
                            pendientes + 100,
                            ct
                        );

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

                        // ==========================================
                        // UBICACIÓN REAL
                        // ProduccionReferencia.TipoReferenciaId = 16
                        //
                        // UbicacionOrigen ya viene desde SQL.
                        // Ejemplo:
                        // R4-04A
                        // ==========================================

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

                // =====================================================
                // PEPS ESTRICTO
                //
                // NO saltamos una caja más antigua sólo porque esté
                // bloqueada por falta de ubicación.
                //
                // La recomendación global es la primera caja PEPS pendiente.
                // Si esa caja no puede bajar, el flujo queda bloqueado hasta
                // resolver esa caja; no seleccionamos una producción más nueva.
                // =====================================================
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

                    var pedidosAlmacen =
                        await ObtenerPedidosPendientesAsync(
                            almacen,
                            inicio,
                            fin,
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
                        x.FechaHora
                    )
                    .ThenBy(x =>
                        x.AlmacenNombre
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
            // ========================================================
            // USUARIO / PERMISO
            // ========================================================
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


            // ========================================================
            // TODOS LOS ALMACENES AUTORIZADOS DEL USUARIO
            // ========================================================
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


            // ========================================================
            // FILTRO OPCIONAL
            //
            // Sin ?almacen= -> TODOS.
            // Con ?almacen=TIFCED -> sólo TIFCED.
            // ========================================================
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


            return View(
                "~/Views/Surtido/Picking.cshtml",
                vm
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
                await ValidarModuloAsync(
                    almacen,
                    "COORDINAR",
                    ct
                );

            if (!acceso.Ok)
            {
                return acceso.Error!;
            }

            SurtidoModuloPedidosVM vm;

            try
            {
                vm =
                    await ConstruirModuloPedidosAsync(
                        acceso.Almacen!,
                        fechaInicio,
                        fechaFin,
                        ct
                    );
            }
            catch (InvalidOperationException ex)
            {
                vm =
                    new SurtidoModuloPedidosVM
                    {
                        Almacen =
                            acceso.Almacen!,

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

            return View(
                "~/Views/Surtido/Coordinador.cshtml",
                vm
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
    }
}
