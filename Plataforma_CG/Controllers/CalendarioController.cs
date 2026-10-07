using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.Filters;
using System.Data;
using ClosedXML.Excel;

namespace Plataforma_CG.Controllers
{
    public class CalendarioController : Controller
    {
        private readonly AppDbContext _db;
        private readonly string _connString;

        private sealed class LogisticaTransportesPermisoVM
        {
            public bool PuedeLeer { get; set; }
            public bool PuedeEscribir { get; set; }
            public bool PuedeEliminar { get; set; }
        }

        private sealed class UsuarioLogisticaActualVM
        {
            public string Usuario { get; set; } = "";
            public string Nombre { get; set; } = "";
            public int? VendedorId { get; set; }
        }

        public sealed class TransporteCatalogoGuardarRequest
        {
            public int Id { get; set; }
            public string? Nombre { get; set; }
            public decimal CapacidadNominalKg { get; set; }
            public decimal CapacidadMaximaKg { get; set; }
        }

        public sealed class TransporteCatalogoEstatusRequest
        {
            public int Id { get; set; }
            public bool Activo { get; set; }
        }

        public CalendarioController(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _connString = config.GetConnectionString("DefaultConnection") ?? "";
        }

        public IActionResult Index()
        {
            return View("~/Views/Calendario/Index.cshtml");
        }


        // ============================================================
        // PERMISOS DEL USUARIO PARA EL CALENDARIO DE LOGÍSTICA
        //
        // REGLA:
        // - UsuarioSQL.VendedorId > 0  => vendedor:
        //      * puede solicitar transporte
        //      * NO puede modificar fletera/tarimas/hora/observación/etc.
        //
        // - UsuarioSQL.VendedorId NULL / 0 => logística:
        //      * puede ver todos los vendedores
        //      * puede modificar los campos de logística
        //      * no genera solicitudes como vendedor
        // ============================================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ObtenerPermisosLogisticaActual(
            CancellationToken ct = default)
        {
            var login =
                (User?.Identity?.Name ?? "")
                .Trim();

            if (string.IsNullOrWhiteSpace(login))
            {
                return Unauthorized(
                    new
                    {
                        ok = false,
                        mensaje = "No se pudo identificar el usuario actual."
                    }
                );
            }

            var username =
                login.Contains("\\")
                    ? login.Split("\\").Last()
                    : login;

            var usernameEmail =
                username.Contains("@")
                    ? username
                    : $"{username}@carnesg.net";

            var usuarioSql =
                await _db.UsuarioSQL
                    .AsNoTracking()
                    .Where(u =>
                        u.Activo &&
                        (
                            u.Usuario == login ||
                            u.Usuario == username ||
                            u.Usuario == usernameEmail ||
                            u.Nombre == login ||
                            u.Nombre == username
                        )
                    )
                    .Select(u => new
                    {
                        u.Usuario,
                        u.Nombre,
                        VendedorId = (int?)u.VendedorId
                    })
                    .FirstOrDefaultAsync(ct);

            if (usuarioSql == null)
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "El usuario actual no está configurado como usuario activo en UsuarioSQL."
                    }
                );
            }

            bool tieneVendedorId =
                usuarioSql.VendedorId.HasValue &&
                usuarioSql.VendedorId.Value > 0;

            var permisoTransportes =
                await ObtenerPermisoTransportesAsync(ct);

            bool esLogistica =
                !tieneVendedorId;

            return Json(
                new
                {
                    ok = true,

                    usuario =
                        usuarioSql.Usuario,

                    nombre =
                        usuarioSql.Nombre,

                    vendedorId =
                        tieneVendedorId
                            ? usuarioSql.VendedorId
                            : null,

                    esVendedor =
                        tieneVendedorId,

                    esLogistica =
                        !tieneVendedorId,

                    puedeSolicitarTransporte =
                        tieneVendedorId,

                    puedeEditarLogistica =
                        !tieneVendedorId,

                    puedeLeerCatalogoTransportes =
                        esLogistica &&
                        permisoTransportes.PuedeLeer,

                    puedeEscribirCatalogoTransportes =
                        esLogistica &&
                        permisoTransportes.PuedeEscribir,

                    puedeEliminarCatalogoTransportes =
                        esLogistica &&
                        permisoTransportes.PuedeEliminar
                }
            );
        }


        // ============================================================
        // CATÁLOGO DE TIPOS DE TRANSPORTE
        //
        // REGLA DE SEGURIDAD:
        // 1) El usuario debe NO tener VendedorId.
        // 2) Debe contar con permiso LOGISTICA_TRANSPORTES.
        //
        // LEER      = consultar catálogo.
        // ESCRIBIR  = alta y edición.
        // ELIMINAR  = inactivar / reactivar.
        //
        // NO se elimina físicamente porque puede existir historial
        // relacionado en LogisticaFolio.
        // ============================================================

        [Authorize]
        [HttpGet]
        [RevisarPermiso("LOGISTICA_TRANSPORTES", "LEER")]
        public async Task<IActionResult> TransporteCatalogoPermisos(
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioLogisticaActualAsync(ct);

            if (usuario == null)
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "El usuario actual no está configurado como usuario activo en UsuarioSQL."
                    }
                );
            }

            bool esLogistica =
                !usuario.VendedorId.HasValue ||
                usuario.VendedorId.Value <= 0;

            var permiso =
                await ObtenerPermisoTransportesAsync(ct);

            return Ok(new
            {
                ok = true,

                esLogistica,

                puedeLeer =
                    esLogistica &&
                    permiso.PuedeLeer,

                puedeEscribir =
                    esLogistica &&
                    permiso.PuedeEscribir,

                puedeEliminar =
                    esLogistica &&
                    permiso.PuedeEliminar,

                regla =
                    "LEER consulta; ESCRIBIR crea/edita; ELIMINAR inactiva o reactiva. " +
                    "Sólo usuarios sin VendedorId pueden administrar el catálogo."
            });
        }


        [Authorize]
        [HttpGet]
        [RevisarPermiso("LOGISTICA_TRANSPORTES", "LEER")]
        public async Task<IActionResult> TransportesCatalogo(
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioLogisticaActualAsync(ct);

            if (usuario == null)
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "El usuario actual no está configurado como usuario activo en UsuarioSQL."
                    }
                );
            }

            if (
                usuario.VendedorId.HasValue &&
                usuario.VendedorId.Value > 0
            )
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "Los vendedores no pueden administrar el catálogo de transportes."
                    }
                );
            }

            if (string.IsNullOrWhiteSpace(_connString))
            {
                return StatusCode(
                    500,
                    new
                    {
                        ok = false,
                        mensaje =
                            "No hay ConnectionString configurado: DefaultConnection."
                    }
                );
            }

            const string sql = @"
SELECT
    Id,
    Nombre,
    CapacidadNominalKg,
    CapacidadMaximaKg,
    Activo,
    FechaRegistro
FROM dbo.LogisticaTipoTransporte
ORDER BY
    Activo DESC,
    CapacidadNominalKg,
    Nombre;";

            var lista =
                new List<object>();

            await using var cn =
                new SqlConnection(_connString);

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(sql, cn);

            await using var rd =
                await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                lista.Add(new
                {
                    id =
                        rd.GetInt32(
                            rd.GetOrdinal("Id")
                        ),

                    nombre =
                        rd["Nombre"]?.ToString() ?? "",

                    capacidadNominalKg =
                        Convert.ToDecimal(
                            rd["CapacidadNominalKg"]
                        ),

                    capacidadMaximaKg =
                        Convert.ToDecimal(
                            rd["CapacidadMaximaKg"]
                        ),

                    activo =
                        Convert.ToBoolean(
                            rd["Activo"]
                        ),

                    fechaRegistro =
                        Convert.ToDateTime(
                            rd["FechaRegistro"]
                        )
                });
            }

            return Ok(new
            {
                ok = true,
                transportes = lista
            });
        }


        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RevisarPermiso("LOGISTICA_TRANSPORTES", "ESCRIBIR")]
        public async Task<IActionResult> TransporteGuardar(
            [FromBody] TransporteCatalogoGuardarRequest model,
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioLogisticaActualAsync(ct);

            if (usuario == null)
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "El usuario actual no está configurado como usuario activo en UsuarioSQL."
                    }
                );
            }

            if (
                usuario.VendedorId.HasValue &&
                usuario.VendedorId.Value > 0
            )
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "Sólo usuarios de Logística pueden administrar transportes."
                    }
                );
            }

            var nombre =
                (model.Nombre ?? "")
                .Trim()
                .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "Captura el nombre del transporte."
                });
            }

            if (nombre.Length > 100)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "El nombre del transporte no puede exceder 100 caracteres."
                });
            }

            if (model.CapacidadNominalKg <= 0m)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "La capacidad nominal debe ser mayor a cero."
                });
            }

            if (
                model.CapacidadMaximaKg <
                model.CapacidadNominalKg
            )
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "La capacidad máxima no puede ser menor que la capacidad nominal."
                });
            }

            if (string.IsNullOrWhiteSpace(_connString))
            {
                return StatusCode(
                    500,
                    new
                    {
                        ok = false,
                        mensaje =
                            "No hay ConnectionString configurado: DefaultConnection."
                    }
                );
            }

            await using var cn =
                new SqlConnection(_connString);

            await cn.OpenAsync(ct);

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(ct);

            try
            {
                const string sqlDuplicado = @"
SELECT COUNT(1)
FROM dbo.LogisticaTipoTransporte
WHERE
    UPPER(LTRIM(RTRIM(Nombre))) =
        UPPER(LTRIM(RTRIM(@Nombre)))
    AND Id <> @Id;";

                await using (
                    var cmdDuplicado =
                        new SqlCommand(
                            sqlDuplicado,
                            cn,
                            tx
                        )
                )
                {
                    cmdDuplicado.Parameters
                        .Add(
                            "@Nombre",
                            SqlDbType.NVarChar,
                            100
                        )
                        .Value =
                            nombre;

                    cmdDuplicado.Parameters
                        .Add(
                            "@Id",
                            SqlDbType.Int
                        )
                        .Value =
                            model.Id;

                    int existe =
                        Convert.ToInt32(
                            await cmdDuplicado
                                .ExecuteScalarAsync(ct)
                        );

                    if (existe > 0)
                    {
                        await tx.RollbackAsync(ct);

                        return BadRequest(new
                        {
                            ok = false,
                            mensaje =
                                "Ya existe un transporte con ese nombre."
                        });
                    }
                }

                int idFinal;

                if (model.Id <= 0)
                {
                    const string sqlInsert = @"
INSERT INTO dbo.LogisticaTipoTransporte
(
    Nombre,
    CapacidadNominalKg,
    CapacidadMaximaKg,
    Activo,
    FechaRegistro
)
OUTPUT INSERTED.Id
VALUES
(
    @Nombre,
    @CapacidadNominalKg,
    @CapacidadMaximaKg,
    1,
    SYSDATETIME()
);";

                    await using var cmdInsert =
                        new SqlCommand(
                            sqlInsert,
                            cn,
                            tx
                        );

                    cmdInsert.Parameters
                        .Add(
                            "@Nombre",
                            SqlDbType.NVarChar,
                            100
                        )
                        .Value =
                            nombre;

                    var pNominal =
                        cmdInsert.Parameters
                            .Add(
                                "@CapacidadNominalKg",
                                SqlDbType.Decimal
                            );

                    pNominal.Precision = 18;
                    pNominal.Scale = 2;
                    pNominal.Value =
                        model.CapacidadNominalKg;

                    var pMaxima =
                        cmdInsert.Parameters
                            .Add(
                                "@CapacidadMaximaKg",
                                SqlDbType.Decimal
                            );

                    pMaxima.Precision = 18;
                    pMaxima.Scale = 2;
                    pMaxima.Value =
                        model.CapacidadMaximaKg;

                    idFinal =
                        Convert.ToInt32(
                            await cmdInsert
                                .ExecuteScalarAsync(ct)
                        );
                }
                else
                {
                    const string sqlUpdate = @"
UPDATE dbo.LogisticaTipoTransporte
SET
    Nombre = @Nombre,
    CapacidadNominalKg = @CapacidadNominalKg,
    CapacidadMaximaKg = @CapacidadMaximaKg
WHERE
    Id = @Id;

SELECT @@ROWCOUNT;";

                    await using var cmdUpdate =
                        new SqlCommand(
                            sqlUpdate,
                            cn,
                            tx
                        );

                    cmdUpdate.Parameters
                        .Add(
                            "@Id",
                            SqlDbType.Int
                        )
                        .Value =
                            model.Id;

                    cmdUpdate.Parameters
                        .Add(
                            "@Nombre",
                            SqlDbType.NVarChar,
                            100
                        )
                        .Value =
                            nombre;

                    var pNominal =
                        cmdUpdate.Parameters
                            .Add(
                                "@CapacidadNominalKg",
                                SqlDbType.Decimal
                            );

                    pNominal.Precision = 18;
                    pNominal.Scale = 2;
                    pNominal.Value =
                        model.CapacidadNominalKg;

                    var pMaxima =
                        cmdUpdate.Parameters
                            .Add(
                                "@CapacidadMaximaKg",
                                SqlDbType.Decimal
                            );

                    pMaxima.Precision = 18;
                    pMaxima.Scale = 2;
                    pMaxima.Value =
                        model.CapacidadMaximaKg;

                    int afectados =
                        Convert.ToInt32(
                            await cmdUpdate
                                .ExecuteScalarAsync(ct)
                        );

                    if (afectados <= 0)
                    {
                        await tx.RollbackAsync(ct);

                        return NotFound(new
                        {
                            ok = false,
                            mensaje =
                                "No se encontró el transporte a editar."
                        });
                    }

                    idFinal =
                        model.Id;
                }

                await tx.CommitAsync(ct);

                return Ok(new
                {
                    ok = true,
                    id = idFinal,
                    mensaje =
                        model.Id <= 0
                            ? "Transporte creado correctamente."
                            : "Transporte actualizado correctamente."
                });
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(ct);
                }
                catch
                {
                }

                throw;
            }
        }


        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RevisarPermiso("LOGISTICA_TRANSPORTES", "ELIMINAR")]
        public async Task<IActionResult> TransporteCambiarEstatus(
            [FromBody] TransporteCatalogoEstatusRequest model,
            CancellationToken ct = default)
        {
            var usuario =
                await ObtenerUsuarioLogisticaActualAsync(ct);

            if (usuario == null)
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "El usuario actual no está configurado como usuario activo en UsuarioSQL."
                    }
                );
            }

            if (
                usuario.VendedorId.HasValue &&
                usuario.VendedorId.Value > 0
            )
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "Sólo usuarios de Logística pueden administrar transportes."
                    }
                );
            }

            if (model.Id <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "Transporte inválido."
                });
            }

            if (string.IsNullOrWhiteSpace(_connString))
            {
                return StatusCode(
                    500,
                    new
                    {
                        ok = false,
                        mensaje =
                            "No hay ConnectionString configurado: DefaultConnection."
                    }
                );
            }

            const string sql = @"
UPDATE dbo.LogisticaTipoTransporte
SET
    Activo = @Activo
WHERE
    Id = @Id;

SELECT @@ROWCOUNT;";

            await using var cn =
                new SqlConnection(_connString);

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters
                .Add(
                    "@Id",
                    SqlDbType.Int
                )
                .Value =
                    model.Id;

            cmd.Parameters
                .Add(
                    "@Activo",
                    SqlDbType.Bit
                )
                .Value =
                    model.Activo;

            int afectados =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync(ct)
                );

            if (afectados <= 0)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje =
                        "No se encontró el transporte."
                });
            }

            return Ok(new
            {
                ok = true,
                mensaje =
                    model.Activo
                        ? "Transporte reactivado correctamente."
                        : "Transporte inactivado correctamente."
            });
        }


        private async Task<UsuarioLogisticaActualVM?> ObtenerUsuarioLogisticaActualAsync(
            CancellationToken ct = default)
        {
            var login =
                (User?.Identity?.Name ?? "")
                .Trim();

            if (string.IsNullOrWhiteSpace(login))
                return null;

            var username =
                login.Contains("\\")
                    ? login.Split("\\").Last()
                    : login;

            var usernameEmail =
                username.Contains("@")
                    ? username
                    : $"{username}@carnesg.net";

            return await _db.UsuarioSQL
                .AsNoTracking()
                .Where(u =>
                    u.Activo &&
                    (
                        u.Usuario == login ||
                        u.Usuario == username ||
                        u.Usuario == usernameEmail ||
                        u.Nombre == login ||
                        u.Nombre == username
                    )
                )
                .Select(u =>
                    new UsuarioLogisticaActualVM
                    {
                        Usuario =
                            u.Usuario ?? "",

                        Nombre =
                            u.Nombre ?? "",

                        VendedorId =
                            (int?)u.VendedorId
                    }
                )
                .FirstOrDefaultAsync(ct);
        }


        private async Task<LogisticaTransportesPermisoVM> ObtenerPermisoTransportesAsync(
            CancellationToken ct = default)
        {
            var login =
                (User?.Identity?.Name ?? "")
                .Trim();

            if (string.IsNullOrWhiteSpace(login))
                return new LogisticaTransportesPermisoVM();

            var username =
                login.Contains("\\")
                    ? login.Split("\\").Last()
                    : login;

            var usernameEmail =
                username.Contains("@")
                    ? username
                    : $"{username}@carnesg.net";

            // Delegado en el resolver unico: el permiso del usuario sobrescribe al perfil.
            // Se pasan todas las formas del login porque aqui el dominio y el correo
            // pueden venir dentro o fuera de la cadena de conexion.
            var (puedeLeer, puedeEscribir, puedeEliminar) =
                await PermisosHelper.ObtenerPermisoEfectivoAsync(
                    _db,
                    new[] { login, username, usernameEmail },
                    "LOGISTICA_TRANSPORTES");

            return new LogisticaTransportesPermisoVM
            {
                PuedeLeer = puedeLeer,
                PuedeEscribir = puedeEscribir,
                PuedeEliminar = puedeEliminar
            };
        }


        // ============================================================
        // CALENDARIO DE LOGÍSTICA
        //
        // CAMBIO DE FLUJO:
        // Antes:
        //      OrdenVenta -> LogisticaOrdenVenta
        //
        // Ahora:
        //      LogisticaFolio -> OrdenVenta opcional
        //
        // El folio puede existir ANTES de que exista la OV.
        // ============================================================

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetPedidosLogistica(
            DateTime fechaInicio,
            DateTime fechaFin,
            CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_connString))
                {
                    return StatusCode(500, new
                    {
                        ok = false,
                        mensaje = "No hay ConnectionString configurado: DefaultConnection."
                    });
                }


                // ============================================================
                // 1) IDENTIFICAR USUARIO ACTUAL
                // ============================================================

                var login =
                    (User?.Identity?.Name ?? "")
                    .Trim();


                var username =
                    login.Contains("\\")
                        ? login.Split("\\").Last()
                        : login;


                var usernameEmail =
                    username.Contains("@")
                        ? username
                        : $"{username}@carnesg.net";


                // ============================================================
                // 2) FILTRO DE VENDEDOR DESDE UsuarioSQL
                // ============================================================

                var usuarioSql =
                    await _db.UsuarioSQL
                        .AsNoTracking()
                        .Where(u =>
                            u.Activo &&
                            (
                                u.Usuario == login ||
                                u.Usuario == username ||
                                u.Usuario == usernameEmail ||
                                u.Nombre == login ||
                                u.Nombre == username
                            )
                        )
                        .Select(u => new
                        {
                            VendedorId =
                                (int?)u.VendedorId
                        })
                        .FirstOrDefaultAsync(ct);


                int? vendedorFiltro =
                    usuarioSql?.VendedorId.HasValue == true &&
                    usuarioSql.VendedorId.Value > 0
                        ? usuarioSql.VendedorId.Value
                        : null;


                // ============================================================
                // 3) CONSULTAR CALENDARIO MULTI-OV
                //
                // IMPORTANTE:
                // - Un folio puede tener varias OV.
                // - LogisticaFolioDocumento es la fuente de los documentos.
                // - Una OV cancelada solamente se desactiva del folio.
                // - NO se cancela todo el transporte por una sola OV.
                // ============================================================

                var lista =
                    new List<object>();


                const string sql = @"
SET NOCOUNT ON;

/* ============================================================
   MULTI-OV: DESACTIVAR ÚNICAMENTE LAS OV CANCELADAS

   Antes se cancelaba TODO el folio cuando la OV guardada en
   LogisticaFolio.OrdenVentaId estaba cancelada.

   Ahora un folio puede contener varias OV, por lo que únicamente
   liberamos el documento cancelado.
   ============================================================ */
UPDATE d
SET
    d.Activo = 0
FROM dbo.LogisticaFolioDocumento d
INNER JOIN dbo.OrdenVenta ovCancelada
    ON ovCancelada.Id = d.DocumentoId
WHERE
    d.TipoDocumento = 'OV'
    AND d.Activo = 1
    AND ovCancelada.Estatus = 0;


/* ============================================================
   SINCRONIZAR KG ACUMULADOS DEL FOLIO
   ============================================================ */
UPDATE f
SET
    f.KgOrdenVenta =
        ISNULL(docTotal.KgCargados, 0)
FROM dbo.LogisticaFolio f
OUTER APPLY
(
    SELECT
        SUM(
            CAST(
                ISNULL(d.Kg, 0)
                AS DECIMAL(18,4)
            )
        ) AS KgCargados
    FROM dbo.LogisticaFolioDocumento d
    WHERE
        d.LogisticaFolioId = f.Id
        AND d.Activo = 1
) docTotal
WHERE
    f.FechaEmbarque >= @FechaInicio
    AND f.FechaEmbarque < DATEADD(DAY, 1, @FechaFin);


SELECT
    f.Id AS FolioLogisticaId,
    f.Folio,

    /* Primera OV activa únicamente para conservar compatibilidad
       con la vista actual. El total real está en los campos
       CantidadDocumentos / KgCargados. */
    ISNULL(
        ov.Id,
        0
    ) AS OrdenVentaId,

    COALESCE(
        NULLIF(
            LTRIM(
                RTRIM(
                    docPrincipal.DocumentoConsecutivo
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    ov.Consecutivo
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    f.OrdenVentaConsecutivo
                )
            ),
            ''
        ),
        ''
    ) AS Consecutivo,

    ISNULL(
        ov.Serie,
        ''
    ) AS Serie,

    ISNULL(
        s.NombreSerie,
        ''
    ) AS NombreSerie,

    ISNULL(
        s.Sucursal,
        ''
    ) AS Sucursal,

    ISNULL(
        CONVERT(
            VARCHAR(50),
            s.sucursalId
        ),
        ''
    ) AS SucursalId,

    CAST(
        f.FechaEmbarque
        AS DATETIME
    ) AS FechaEntrega,

    CAST(
        f.FechaEmbarque
        AS DATETIME
    ) AS FechaEmbarque,

    ISNULL(
        CONVERT(
            VARCHAR(5),
            ov.HoraEmbarque,
            108
        ),
        ''
    ) AS HoraEmbarque,

    COALESCE(
        NULLIF(
            LTRIM(
                RTRIM(
                    docPrincipal.ClienteCodigo
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    ov.Cliente
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    f.ClienteCodigo
                )
            ),
            ''
        ),
        ''
    ) AS ClienteCodigo,

    COALESCE(
        NULLIF(
            LTRIM(
                RTRIM(
                    docPrincipal.ClienteNombre
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    cs.Nombrecliente
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    ov.Cliente
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    f.ClienteNombre
                )
            ),
            ''
        ),
        'SIN OV LIGADA'
    ) AS ClienteNombre,

    ISNULL(
        f.VendedorId,
        0
    ) AS VendedorId,

    COALESCE(
        NULLIF(
            LTRIM(
                RTRIM(
                    f.Vendedor
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    ov.Vendedor
                )
            ),
            ''
        ),
        ''
    ) AS Vendedor,

    ISNULL(
        f.Destino,
        ''
    ) AS Destino,

    ISNULL(
        NULLIF(
            LTRIM(
                RTRIM(
                    f.TipoViaje
                )
            ),
            ''
        ),
        'SIN DEFINIR'
    ) AS TipoViaje,

    COALESCE(
        NULLIF(
            LTRIM(
                RTRIM(
                    docPrincipal.Ruta
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    ov.Ruta
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    f.Ruta
                )
            ),
            ''
        ),
        ''
    ) AS Ruta,

    COALESCE(
        NULLIF(
            LTRIM(
                RTRIM(
                    docPrincipal.Presentacion
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    ov.Presentacion
                )
            ),
            ''
        ),
        NULLIF(
            LTRIM(
                RTRIM(
                    f.Presentacion
                )
            ),
            ''
        ),
        ''
    ) AS Presentacion,

    ISNULL(
        ov.Observacion,
        ''
    ) AS Observacion,

    /* ========================================================
       CAPACIDAD / TRANSPORTE
       ======================================================== */

    ISNULL(
        f.CargaSolicitadaKg,
        0
    ) AS CargaSolicitadaKg,

    ISNULL(
        t.Id,
        0
    ) AS TipoTransporteId,

    ISNULL(
        t.Nombre,
        'SIN TIPO DE TRANSPORTE'
    ) AS TipoTransporte,

    CAST(
        ISNULL(
            f.CapacidadNominalKg,
            ISNULL(
                t.CapacidadNominalKg,
                ISNULL(
                    f.CargaSolicitadaKg,
                    0
                )
            )
        )
        AS DECIMAL(18,2)
    ) AS CapacidadNominalKg,

    CAST(
        ISNULL(
            f.CapacidadMaximaKg,
            ISNULL(
                t.CapacidadMaximaKg,
                ISNULL(
                    f.CargaSolicitadaKg,
                    0
                )
            )
        )
        AS DECIMAL(18,2)
    ) AS CapacidadMaximaKg,

    CAST(
        ISNULL(
            docTotal.KgCargados,
            0
        )
        AS DECIMAL(18,2)
    ) AS KgCargados,

    CAST(
        CASE
            WHEN
                ISNULL(
                    f.CapacidadMaximaKg,
                    ISNULL(
                        t.CapacidadMaximaKg,
                        ISNULL(
                            f.CargaSolicitadaKg,
                            0
                        )
                    )
                )
                -
                ISNULL(
                    docTotal.KgCargados,
                    0
                )
                > 0
            THEN
                ISNULL(
                    f.CapacidadMaximaKg,
                    ISNULL(
                        t.CapacidadMaximaKg,
                        ISNULL(
                            f.CargaSolicitadaKg,
                            0
                        )
                    )
                )
                -
                ISNULL(
                    docTotal.KgCargados,
                    0
                )
            ELSE 0
        END
        AS DECIMAL(18,2)
    ) AS KgDisponible,

    ISNULL(
        docTotal.CantidadDocumentos,
        0
    ) AS CantidadDocumentos,

    /* ========================================================
       TODOS LOS DOCUMENTOS LIGADOS AL FOLIO

       Regresamos JSON para que la vista pueda mostrar TODAS
       las OV y TRANSFERENCIAS del mismo transporte.
       ======================================================== */
    ISNULL(
        (
            SELECT
                TipoDocumento =
                    ISNULL(d.TipoDocumento, ''),

                DocumentoId =
                    d.DocumentoId,

                DocumentoConsecutivo =
                    ISNULL(d.DocumentoConsecutivo, ''),

                Kg =
                    CAST(
                        ISNULL(d.Kg, 0)
                        AS DECIMAL(18,2)
                    ),

                ClienteCodigo =
                    ISNULL(d.ClienteCodigo, ''),

                ClienteNombre =
                    ISNULL(d.ClienteNombre, ''),

                Ruta =
                    ISNULL(d.Ruta, ''),

                Presentacion =
                    ISNULL(d.Presentacion, '')

            FROM dbo.LogisticaFolioDocumento d

            WHERE
                d.LogisticaFolioId = f.Id
                AND d.Activo = 1

            ORDER BY
                d.Id

            FOR JSON PATH
        ),
        '[]'
    ) AS DocumentosJson,

    /* Compatibilidad: KgOrdenVenta ahora representa el total
       activo acumulado del folio. */
    CAST(
        ISNULL(
            docTotal.KgCargados,
            ISNULL(
                f.KgOrdenVenta,
                0
            )
        )
        AS DECIMAL(18,2)
    ) AS KgOrdenVenta,

    /* La vista existente usa Saldo como kilos en algunas partes.
       Conservamos el campo, pero ahora refleja el total cargado;
       si todavía no hay documentos refleja la carga solicitada. */
    CAST(
        CASE
            WHEN ISNULL(docTotal.CantidadDocumentos, 0) = 0
                THEN ISNULL(f.CargaSolicitadaKg, 0)
            ELSE ISNULL(docTotal.KgCargados, 0)
        END
        AS DECIMAL(18,2)
    ) AS Saldo,

    ISNULL(
        ov.OtrosPedidos,
        0
    ) AS OtrosPedidos,

    ISNULL(
        ov.Credito,
        0
    ) AS Credito,

    ISNULL(
        f.Fletera,
        ''
    ) AS Fletera,

    ISNULL(
        f.EspacioTarimas,
        0
    ) AS EspacioTarimas,

    ISNULL(
        CONVERT(
            VARCHAR(5),
            f.HoraLlegadaUnidad,
            108
        ),
        ''
    ) AS HoraLlegadaUnidad,

    ISNULL(
        NULLIF(
            LTRIM(
                RTRIM(
                    f.EstatusLogistico
                )
            ),
            ''
        ),
        CASE
            WHEN ISNULL(docTotal.CantidadDocumentos, 0) = 0
                THEN 'RESERVADO VENTA'
            ELSE 'PENDIENTE FLETERA'
        END
    ) AS EstatusLogistico,

    ISNULL(
        f.ObservacionLogistica,
        ''
    ) AS ObservacionLogistica,

    ISNULL(
        f.MotivoCancelacion,
        ''
    ) AS MotivoCancelacion,

    ISNULL(
        f.MotivoCancelacionFletera,
        ''
    ) AS MotivoCancelacionFletera,

    CAST(
        ISNULL(
            f.Cancelado,
            0
        )
        AS BIT
    ) AS Cancelado,

    CAST(
        ISNULL(
            f.CanceladoFletera,
            0
        )
        AS BIT
    ) AS CanceladoFletera

FROM dbo.LogisticaFolio f

LEFT JOIN dbo.LogisticaTipoTransporte t
    ON t.Id = f.TipoTransporteId

/* ============================================================
   PRIMERA OV ACTIVA PARA COMPATIBILIDAD VISUAL
   ============================================================ */
OUTER APPLY
(
    SELECT TOP (1)
        d.DocumentoId,
        d.DocumentoConsecutivo,
        d.ClienteCodigo,
        d.ClienteNombre,
        d.Ruta,
        d.Presentacion,
        d.Kg
    FROM dbo.LogisticaFolioDocumento d
    WHERE
        d.LogisticaFolioId = f.Id
        AND d.TipoDocumento = 'OV'
        AND d.Activo = 1
    ORDER BY
        d.Id
) docPrincipal

/* ============================================================
   TOTAL REAL DEL CAMIÓN / FOLIO
   Incluye cualquier documento activo: OV, TRANSFERENCIA, etc.
   ============================================================ */
OUTER APPLY
(
    SELECT
        KgCargados =
            SUM(
                CAST(
                    ISNULL(d.Kg, 0)
                    AS DECIMAL(18,4)
                )
            ),

        CantidadDocumentos =
            COUNT(*)

    FROM dbo.LogisticaFolioDocumento d
    WHERE
        d.LogisticaFolioId = f.Id
        AND d.Activo = 1
) docTotal

/* Saber si el folio ya usa el esquema nuevo, incluso cuando todos
   sus documentos históricos estén inactivos. */
OUTER APPLY
(
    SELECT
        COUNT(*) AS CantidadHistorica
    FROM dbo.LogisticaFolioDocumento d
    WHERE
        d.LogisticaFolioId = f.Id
) docHistorico

LEFT JOIN dbo.OrdenVenta ov
    ON ov.Id =
       CASE
           WHEN ISNULL(docHistorico.CantidadHistorica, 0) > 0
               THEN docPrincipal.DocumentoId
           ELSE f.OrdenVentaId
       END

LEFT JOIN dbo.series s
    ON UPPER(
        LTRIM(
            RTRIM(
                s.NombreSerie
            )
        )
    ) =
       UPPER(
           LTRIM(
               RTRIM(
                   ov.Serie
               )
           )
       )

LEFT JOIN dbo.ClienteSap cs
    ON UPPER(
        LTRIM(
            RTRIM(
                cs.Cliente
            )
        )
    ) =
       UPPER(
           LTRIM(
               RTRIM(
                   COALESCE(
                       NULLIF(
                           docPrincipal.ClienteCodigo,
                           ''
                       ),
                       NULLIF(
                           f.ClienteCodigo,
                           ''
                       ),
                       ov.Cliente
                   )
               )
           )
       )

WHERE
    f.FechaEmbarque >= @FechaInicio

    AND f.FechaEmbarque <
        DATEADD(
            DAY,
            1,
            @FechaFin
        )

    AND
    (
        @VendedorId IS NULL
        OR
        f.VendedorId = @VendedorId
    )

ORDER BY
    f.FechaEmbarque,
    f.Vendedor,
    f.Folio;
";


                // ============================================================
                // 4) EJECUTAR
                // ============================================================

                await using var cn =
                    new SqlConnection(
                        _connString
                    );


                await cn.OpenAsync(ct);


                await using var cmd =
                    new SqlCommand(
                        sql,
                        cn
                    );


                cmd.CommandTimeout =
                    60;


                cmd.Parameters
                    .Add(
                        "@FechaInicio",
                        SqlDbType.Date
                    )
                    .Value =
                        fechaInicio.Date;


                cmd.Parameters
                    .Add(
                        "@FechaFin",
                        SqlDbType.Date
                    )
                    .Value =
                        fechaFin.Date;


                cmd.Parameters
                    .Add(
                        "@VendedorId",
                        SqlDbType.Int
                    )
                    .Value =
                        vendedorFiltro.HasValue
                            ? vendedorFiltro.Value
                            : DBNull.Value;


                await using var rd =
                    await cmd.ExecuteReaderAsync(ct);


                // ============================================================
                // 5) RESPUESTA
                // ============================================================

                while (
                    await rd.ReadAsync(ct)
                )
                {
                    lista.Add(
                        new
                        {
                            Id =
                                GetInt(
                                    rd,
                                    "FolioLogisticaId"
                                ),

                            FolioLogisticaId =
                                GetInt(
                                    rd,
                                    "FolioLogisticaId"
                                ),

                            Folio =
                                GetString(
                                    rd,
                                    "Folio"
                                ),

                            OrdenVentaId =
                                GetInt(
                                    rd,
                                    "OrdenVentaId"
                                ),

                            Consecutivo =
                                GetString(
                                    rd,
                                    "Consecutivo"
                                ),

                            Serie =
                                GetString(
                                    rd,
                                    "Serie"
                                ),

                            NombreSerie =
                                GetString(
                                    rd,
                                    "NombreSerie"
                                ),

                            Sucursal =
                                GetString(
                                    rd,
                                    "Sucursal"
                                ),

                            SucursalId =
                                GetString(
                                    rd,
                                    "SucursalId"
                                ),

                            FechaEntrega =
                                GetDateTime(
                                    rd,
                                    "FechaEntrega"
                                ),

                            FechaEmbarque =
                                GetNullableDateTime(
                                    rd,
                                    "FechaEmbarque"
                                ),

                            HoraEmbarque =
                                GetString(
                                    rd,
                                    "HoraEmbarque"
                                ),

                            Cliente =
                                GetString(
                                    rd,
                                    "ClienteNombre"
                                ),

                            ClienteCodigo =
                                GetString(
                                    rd,
                                    "ClienteCodigo"
                                ),

                            ClienteNombre =
                                GetString(
                                    rd,
                                    "ClienteNombre"
                                ),

                            VendedorId =
                                GetInt(
                                    rd,
                                    "VendedorId"
                                ),

                            Vendedor =
                                GetString(
                                    rd,
                                    "Vendedor"
                                ),

                            Destino =
                                GetString(
                                    rd,
                                    "Destino"
                                ),

                            TipoViaje =
                                GetString(
                                    rd,
                                    "TipoViaje"
                                ),

                            Ruta =
                                GetString(
                                    rd,
                                    "Ruta"
                                ),

                            Presentacion =
                                GetString(
                                    rd,
                                    "Presentacion"
                                ),

                            Observacion =
                                GetString(
                                    rd,
                                    "Observacion"
                                ),

                            CargaSolicitadaKg =
                                GetDecimal(
                                    rd,
                                    "CargaSolicitadaKg"
                                ),

                            TipoTransporteId =
                                GetInt(
                                    rd,
                                    "TipoTransporteId"
                                ),

                            TipoTransporte =
                                GetString(
                                    rd,
                                    "TipoTransporte"
                                ),

                            CapacidadNominalKg =
                                GetDecimal(
                                    rd,
                                    "CapacidadNominalKg"
                                ),

                            CapacidadMaximaKg =
                                GetDecimal(
                                    rd,
                                    "CapacidadMaximaKg"
                                ),

                            KgCargados =
                                GetDecimal(
                                    rd,
                                    "KgCargados"
                                ),

                            KgDisponible =
                                GetDecimal(
                                    rd,
                                    "KgDisponible"
                                ),

                            CantidadDocumentos =
                                GetInt(
                                    rd,
                                    "CantidadDocumentos"
                                ),

                            DocumentosJson =
                                GetString(
                                    rd,
                                    "DocumentosJson"
                                ),

                            KgOrdenVenta =
                                GetDecimal(
                                    rd,
                                    "KgOrdenVenta"
                                ),

                            Saldo =
                                GetDecimal(
                                    rd,
                                    "Saldo"
                                ),

                            OtrosPedidos =
                                GetDecimal(
                                    rd,
                                    "OtrosPedidos"
                                ),

                            Credito =
                                GetDecimal(
                                    rd,
                                    "Credito"
                                ),

                            Fletera =
                                GetString(
                                    rd,
                                    "Fletera"
                                ),

                            EspacioTarimas =
                                GetInt(
                                    rd,
                                    "EspacioTarimas"
                                ),

                            HoraLlegadaUnidad =
                                GetString(
                                    rd,
                                    "HoraLlegadaUnidad"
                                ),

                            EstatusLogistico =
                                GetString(
                                    rd,
                                    "EstatusLogistico"
                                ),

                            ObservacionLogistica =
                                GetString(
                                    rd,
                                    "ObservacionLogistica"
                                ),

                            MotivoCancelacion =
                                GetString(
                                    rd,
                                    "MotivoCancelacion"
                                ),

                            MotivoCancelacionFletera =
                                GetString(
                                    rd,
                                    "MotivoCancelacionFletera"
                                ),

                            Cancelado =
                                GetBool(
                                    rd,
                                    "Cancelado"
                                ),

                            CanceladoFletera =
                                GetBool(
                                    rd,
                                    "CanceladoFletera"
                                )
                        }
                    );
                }


                return Json(lista);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        ok = false,

                        mensaje =
                            "Error en GetPedidosLogistica.",

                        error =
                            ex.Message,

                        inner =
                            ex.InnerException?.Message
                    }
                );
            }
        }


        // ============================================================
        // LOGÍSTICA COMPLETA SOLAMENTE LOS DATOS QUE LE CORRESPONDEN
        //
        // El folio puede existir SIN OV o contener VARIAS OV.
        //
        // Compatibilidad temporal:
        // - Preferido: FolioLogisticaId.
        // - Si la vista vieja todavía manda OrdenVentaId,
        //   se busca primero en LogisticaFolioDocumento.
        // ============================================================
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GuardarLogistica(
            [FromBody] GuardarLogisticaFolioDto model,
            CancellationToken ct = default)
        {
            if (model == null)
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        mensaje =
                            "Solicitud de logística inválida."
                    }
                );
            }

            if (
                model.FolioLogisticaId <= 0
                &&
                model.OrdenVentaId <= 0
            )
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        mensaje =
                            "Falta el folio de logística."
                    }
                );
            }

            if (string.IsNullOrWhiteSpace(_connString))
            {
                return StatusCode(
                    500,
                    new
                    {
                        ok = false,
                        mensaje =
                            "No hay ConnectionString configurado: DefaultConnection."
                    }
                );
            }


            // ============================================================
            // SEGURIDAD REAL DEL LADO DEL SERVIDOR
            //
            // Sólo un usuario ACTIVO de UsuarioSQL SIN VendedorId
            // puede modificar campos de logística.
            // ============================================================

            var loginPermiso =
                (User?.Identity?.Name ?? "")
                .Trim();

            var usernamePermiso =
                loginPermiso.Contains("\\")
                    ? loginPermiso.Split("\\").Last()
                    : loginPermiso;

            var usernameEmailPermiso =
                usernamePermiso.Contains("@")
                    ? usernamePermiso
                    : $"{usernamePermiso}@carnesg.net";

            var usuarioSqlPermiso =
                await _db.UsuarioSQL
                    .AsNoTracking()
                    .Where(u =>
                        u.Activo &&
                        (
                            u.Usuario == loginPermiso ||
                            u.Usuario == usernamePermiso ||
                            u.Usuario == usernameEmailPermiso ||
                            u.Nombre == loginPermiso ||
                            u.Nombre == usernamePermiso
                        )
                    )
                    .Select(u => new
                    {
                        VendedorId =
                            (int?)u.VendedorId
                    })
                    .FirstOrDefaultAsync(ct);

            if (usuarioSqlPermiso == null)
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "Tu usuario no está configurado en UsuarioSQL. " +
                            "No tienes permiso para modificar logística."
                    }
                );
            }

            bool usuarioEsVendedor =
                usuarioSqlPermiso.VendedorId.HasValue &&
                usuarioSqlPermiso.VendedorId.Value > 0;

            if (usuarioEsVendedor)
            {
                return StatusCode(
                    403,
                    new
                    {
                        ok = false,
                        mensaje =
                            "Los usuarios con VendedorId sólo pueden consultar su logística. " +
                            "Fletera, destino, tipo de viaje, tarimas, hora de llegada y observaciones " +
                            "sólo pueden ser modificados por Logística."
                    }
                );
            }


            var usuario =
                User?.Identity?.Name
                ?? "Sistema";


            TimeSpan? horaLlegada = null;

            if (
                !string.IsNullOrWhiteSpace(
                    model.HoraLlegadaUnidad
                )
                &&
                TimeSpan.TryParse(
                    model.HoraLlegadaUnidad,
                    out var hora
                )
            )
            {
                horaLlegada = hora;
            }


            var fletera =
                (model.Fletera ?? "")
                    .Trim()
                    .ToUpperInvariant();

            var destino =
                (model.Destino ?? "")
                    .Trim()
                    .ToUpperInvariant();

            var tipoViajeTexto =
                (model.TipoViaje ?? "")
                    .Trim()
                    .ToUpperInvariant();


            // Registros anteriores pueden venir SIN DEFINIR.
            // En ese caso NO pisamos el valor existente en SQL.
            string? tipoViaje =
                tipoViajeTexto == "CONFIRMADO"
                    ? "CONFIRMADO"
                    : tipoViajeTexto == "TENTATIVO"
                        ? "TENTATIVO"
                        : null;


            string estatusFinal;

            if (model.Cancelado)
            {
                estatusFinal =
                    "CANCELADO";
            }
            else if (model.CanceladoFletera)
            {
                estatusFinal =
                    "FLETERA CANCELADA";
            }
            else if (
                !string.IsNullOrWhiteSpace(
                    fletera
                )
            )
            {
                estatusFinal =
                    "ASIGNADO";
            }
            else
            {
                estatusFinal =
                    "PENDIENTE FLETERA";
            }


            // ============================================================
            // MULTI-OV
            //
            // Ya NO se cancela el folio porque una sola OV esté cancelada.
            // Si la vista vieja manda OrdenVentaId, primero buscamos el folio
            // en LogisticaFolioDocumento y luego usamos el campo antiguo como
            // compatibilidad.
            // ============================================================

            const string sql = @"
SET NOCOUNT ON;

DECLARE @FolioId INT =
    NULLIF(
        @FolioLogisticaId,
        0
    );


/* ============================================================
   COMPATIBILIDAD CON LLAMADAS QUE TODAVÍA MANDAN OrdenVentaId
   ============================================================ */
IF @FolioId IS NULL AND @OrdenVentaId > 0
BEGIN

    SELECT TOP (1)
        @FolioId =
            d.LogisticaFolioId
    FROM dbo.LogisticaFolioDocumento d
    WHERE
        d.TipoDocumento = 'OV'
        AND d.DocumentoId = @OrdenVentaId
        AND d.Activo = 1
    ORDER BY
        d.Id DESC;

END;


/* Compatibilidad adicional con folios antiguos. */
IF @FolioId IS NULL AND @OrdenVentaId > 0
BEGIN

    SELECT TOP (1)
        @FolioId =
            f.Id
    FROM dbo.LogisticaFolio f
    WHERE
        f.OrdenVentaId = @OrdenVentaId
    ORDER BY
        f.Id DESC;

END;


IF @FolioId IS NULL
BEGIN

    SELECT CAST(0 AS INT);
    RETURN;

END;


/* ============================================================
   ACTUALIZAR SOLAMENTE DATOS DEL VIAJE / TRANSPORTE
   ============================================================ */
UPDATE dbo.LogisticaFolio
SET
    Destino =
        COALESCE(
            NULLIF(
                @Destino,
                ''
            ),
            Destino
        ),

    TipoViaje =
        COALESCE(
            @TipoViaje,
            TipoViaje
        ),

    Fletera =
        @Fletera,

    EspacioTarimas =
        @EspacioTarimas,

    HoraLlegadaUnidad =
        @HoraLlegadaUnidad,

    EstatusLogistico =
        @EstatusLogistico,

    ObservacionLogistica =
        @ObservacionLogistica,

    MotivoCancelacion =
        @MotivoCancelacion,

    MotivoCancelacionFletera =
        @MotivoCancelacionFletera,

    Cancelado =
        @Cancelado,

    CanceladoFletera =
        @CanceladoFletera,

    UsuarioModificacion =
        @Usuario,

    FechaModificacion =
        SYSDATETIME()

WHERE
    Id = @FolioId;


SELECT @@ROWCOUNT;
";


            await using var cn =
                new SqlConnection(_connString);

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(sql, cn);


            cmd.Parameters
                .Add(
                    "@FolioLogisticaId",
                    SqlDbType.Int
                )
                .Value =
                    model.FolioLogisticaId;

            cmd.Parameters
                .Add(
                    "@OrdenVentaId",
                    SqlDbType.Int
                )
                .Value =
                    model.OrdenVentaId;

            cmd.Parameters
                .Add(
                    "@Destino",
                    SqlDbType.NVarChar,
                    200
                )
                .Value =
                    destino;

            cmd.Parameters
                .Add(
                    "@TipoViaje",
                    SqlDbType.VarChar,
                    20
                )
                .Value =
                    tipoViaje != null
                        ? tipoViaje
                        : DBNull.Value;

            cmd.Parameters
                .Add(
                    "@Fletera",
                    SqlDbType.NVarChar,
                    150
                )
                .Value =
                    ToDb(fletera);

            cmd.Parameters
                .Add(
                    "@EspacioTarimas",
                    SqlDbType.Int
                )
                .Value =
                    model.EspacioTarimas;

            cmd.Parameters
                .Add(
                    "@HoraLlegadaUnidad",
                    SqlDbType.Time
                )
                .Value =
                    horaLlegada.HasValue
                        ? horaLlegada.Value
                        : DBNull.Value;

            cmd.Parameters
                .Add(
                    "@EstatusLogistico",
                    SqlDbType.VarChar,
                    50
                )
                .Value =
                    estatusFinal;

            cmd.Parameters
                .Add(
                    "@ObservacionLogistica",
                    SqlDbType.NVarChar,
                    1000
                )
                .Value =
                    ToDb(
                        model.ObservacionLogistica
                    );

            cmd.Parameters
                .Add(
                    "@MotivoCancelacion",
                    SqlDbType.NVarChar,
                    1000
                )
                .Value =
                    ToDb(
                        model.MotivoCancelacion
                    );

            cmd.Parameters
                .Add(
                    "@MotivoCancelacionFletera",
                    SqlDbType.NVarChar,
                    1000
                )
                .Value =
                    ToDb(
                        model.MotivoCancelacionFletera
                    );

            cmd.Parameters
                .Add(
                    "@Cancelado",
                    SqlDbType.Bit
                )
                .Value =
                    model.Cancelado;

            cmd.Parameters
                .Add(
                    "@CanceladoFletera",
                    SqlDbType.Bit
                )
                .Value =
                    model.CanceladoFletera;

            cmd.Parameters
                .Add(
                    "@Usuario",
                    SqlDbType.NVarChar,
                    150
                )
                .Value =
                    usuario;


            var actualizados =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync(ct)
                );


            if (actualizados != 1)
            {
                return BadRequest(
                    new
                    {
                        ok = false,
                        mensaje =
                            "No se encontró el folio de logística."
                    }
                );
            }


            return Json(
                new
                {
                    ok = true,
                    mensaje =
                        "Logística guardada correctamente.",
                    estatus =
                        estatusFinal
                }
            );
        }


        // ============================================================
        // DTO LOCAL.
        // Lo dejamos dentro de este controller para que NO tengas que
        // modificar Models únicamente por este cambio.
        // ============================================================
        public sealed class GuardarLogisticaFolioDto
        {
            public int FolioLogisticaId { get; set; }

            // Compatibilidad temporal con la vista vieja.
            public int OrdenVentaId { get; set; }

            public string? Destino { get; set; }
            public string? TipoViaje { get; set; }

            public string? Fletera { get; set; }
            public int EspacioTarimas { get; set; }
            public string? HoraLlegadaUnidad { get; set; }

            public string? EstatusLogistico { get; set; }
            public string? ObservacionLogistica { get; set; }

            public string? MotivoCancelacion { get; set; }
            public string? MotivoCancelacionFletera { get; set; }

            public bool Cancelado { get; set; }
            public bool CanceladoFletera { get; set; }
        }


        private static object ToDb(object? value)
        {
            if (value == null)
                return DBNull.Value;

            if (
                value is string s
                &&
                string.IsNullOrWhiteSpace(s)
            )
            {
                return DBNull.Value;
            }

            return value;
        }


        private static string GetString(
            SqlDataReader rd,
            string column)
        {
            var i =
                rd.GetOrdinal(column);

            return rd.IsDBNull(i)
                ? ""
                : Convert.ToString(
                    rd.GetValue(i)
                ) ?? "";
        }


        private static int GetInt(
            SqlDataReader rd,
            string column)
        {
            var i =
                rd.GetOrdinal(column);

            return rd.IsDBNull(i)
                ? 0
                : Convert.ToInt32(
                    rd.GetValue(i)
                );
        }


        private static decimal GetDecimal(
            SqlDataReader rd,
            string column)
        {
            var i =
                rd.GetOrdinal(column);

            return rd.IsDBNull(i)
                ? 0m
                : Convert.ToDecimal(
                    rd.GetValue(i)
                );
        }


        private static bool GetBool(
            SqlDataReader rd,
            string column)
        {
            var i =
                rd.GetOrdinal(column);

            return
                !rd.IsDBNull(i)
                &&
                Convert.ToBoolean(
                    rd.GetValue(i)
                );
        }


        private static DateTime GetDateTime(
            SqlDataReader rd,
            string column)
        {
            var i =
                rd.GetOrdinal(column);

            return rd.IsDBNull(i)
                ? DateTime.MinValue
                : Convert.ToDateTime(
                    rd.GetValue(i)
                );
        }


        private static DateTime? GetNullableDateTime(
            SqlDataReader rd,
            string column)
        {
            var i =
                rd.GetOrdinal(column);

            return rd.IsDBNull(i)
                ? null
                : Convert.ToDateTime(
                    rd.GetValue(i)
                );
        }

        // ============================================================
        // AGREGAR ARRIBA DEL CalendarioController.cs
        // ============================================================
        //
        // using ClosedXML.Excel;
        // using Microsoft.EntityFrameworkCore;
        // using System.Globalization;
        //
        // ClosedXML ya está utilizado en tu proyecto, por lo que no necesitas
        // instalar otro paquete si el proyecto compila actualmente con ClosedXML.
        // ============================================================


        // ============================================================
        // DTO PRIVADO PARA EL EXCEL
        // PEGAR DENTRO DE LA CLASE CalendarioController
        // ============================================================

        private sealed class ExcelLogisticaRow
        {
            public DateTime FechaEmbarque { get; set; }
            public string Folio { get; set; } = "";
            public string OrdenVenta { get; set; } = "";
            public string Cliente { get; set; } = "";
            public string Destino { get; set; } = "";
            public string TipoViaje { get; set; } = "";
            public string Vendedor { get; set; } = "";
            public decimal CargaSolicitadaKg { get; set; }
            public decimal KgOrdenVenta { get; set; }
            public string Estatus { get; set; } = "";
            public string Fletera { get; set; } = "";
            public int Tarimas { get; set; }
            public string HoraLlegada { get; set; } = "";
            public string Ruta { get; set; } = "";
            public string Presentacion { get; set; } = "";
            public string ObservacionLogistica { get; set; } = "";
        }


        // ============================================================
        // EXCEL EJECUTIVO DE PLANEACIÓN DE TRANSPORTE
        //
        // REGLA DE VISIBILIDAD:
        // UsuarioSQL.VendedorId > 0  -> sólo sus registros.
        // UsuarioSQL.VendedorId NULL/0 -> todos.
        //
        // Además respeta los filtros visibles de:
        // - fecha
        // - vendedor (cuando el usuario puede ver todos)
        // - estatus
        // ============================================================

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ExportarExcelLogistica(
            DateTime fechaInicio,
            DateTime fechaFin,
            string? vendedor = null,
            string? estatus = null)
        {
            if (string.IsNullOrWhiteSpace(_connString))
            {
                return StatusCode(
                    500,
                    "No hay ConnectionString configurado: DefaultConnection."
                );
            }


            if (fechaInicio == default)
                fechaInicio = DateTime.Today;

            if (fechaFin == default)
                fechaFin = fechaInicio;

            if (fechaFin.Date < fechaInicio.Date)
            {
                var tmp = fechaInicio;
                fechaInicio = fechaFin;
                fechaFin = tmp;
            }


            // ========================================================
            // 1) USUARIO / VENDEDOR ACTUAL
            // ========================================================

            var login =
                (User?.Identity?.Name ?? "")
                .Trim();

            var username =
                login.Contains("\\")
                    ? login.Split("\\").Last()
                    : login;

            var usernameEmail =
                username.Contains("@")
                    ? username
                    : $"{username}@carnesg.net";


            var usuarioSql =
                await _db.UsuarioSQL
                    .AsNoTracking()
                    .Where(u =>
                        u.Activo &&
                        (
                            u.Usuario == login ||
                            u.Usuario == username ||
                            u.Usuario == usernameEmail ||
                            u.Nombre == login ||
                            u.Nombre == username
                        )
                    )
                    .Select(u => new
                    {
                        u.Nombre,
                        VendedorId =
                            (int?)u.VendedorId
                    })
                    .FirstOrDefaultAsync();


            int? vendedorFiltro =
                usuarioSql?.VendedorId.HasValue == true &&
                usuarioSql.VendedorId.Value > 0
                    ? usuarioSql.VendedorId.Value
                    : null;


            // Si el usuario tiene VendedorId, NO permitimos que el querystring
            // cambie el vendedor de su exportación.
            string? vendedorNombreFiltro =
                vendedorFiltro.HasValue
                    ? null
                    : (
                        string.IsNullOrWhiteSpace(vendedor)
                            ? null
                            : vendedor.Trim()
                      );


            string? estatusFiltro =
                string.IsNullOrWhiteSpace(estatus)
                    ? null
                    : estatus.Trim().ToUpperInvariant();


            // ========================================================
            // 2) CONSULTA
            // ========================================================

            const string sql = @"
SELECT
    FechaEmbarque =
        CAST(f.FechaEmbarque AS DATE),

    Folio =
        ISNULL(f.Folio, ''),

    OrdenVenta =
        COALESCE(
            NULLIF(LTRIM(RTRIM(f.OrdenVentaConsecutivo)), ''),
            NULLIF(LTRIM(RTRIM(ov.Consecutivo)), ''),
            ''
        ),

    Cliente =
        COALESCE(
            NULLIF(LTRIM(RTRIM(f.ClienteNombre)), ''),
            NULLIF(LTRIM(RTRIM(cs.Nombrecliente)), ''),
            NULLIF(LTRIM(RTRIM(ov.Cliente)), ''),
            'SIN OV LIGADA'
        ),

    Vendedor =
        COALESCE(
            NULLIF(LTRIM(RTRIM(f.Vendedor)), ''),
            NULLIF(LTRIM(RTRIM(ov.Vendedor)), ''),
            ''
        ),

    Destino =
        ISNULL(
            f.Destino,
            ''
        ),

    TipoViaje =
        ISNULL(
            NULLIF(
                LTRIM(RTRIM(f.TipoViaje)),
                ''
            ),
            'SIN DEFINIR'
        ),

    CargaSolicitadaKg =
        CAST(
            ISNULL(f.CargaSolicitadaKg, 0)
            AS DECIMAL(18,2)
        ),

    KgOrdenVenta =
        CAST(
            ISNULL(
                f.KgOrdenVenta,
                ISNULL(kg.KgProgramados, 0)
            )
            AS DECIMAL(18,2)
        ),

    Estatus =
        ISNULL(
            NULLIF(LTRIM(RTRIM(f.EstatusLogistico)), ''),
            CASE
                WHEN f.OrdenVentaId IS NULL
                    THEN 'RESERVADO VENTA'
                ELSE 'PENDIENTE FLETERA'
            END
        ),

    Fletera =
        ISNULL(f.Fletera, ''),

    Tarimas =
        ISNULL(f.EspacioTarimas, 0),

    HoraLlegada =
        ISNULL(
            CONVERT(
                VARCHAR(5),
                f.HoraLlegadaUnidad,
                108
            ),
            ''
        ),

    Ruta =
        COALESCE(
            NULLIF(LTRIM(RTRIM(f.Ruta)), ''),
            NULLIF(LTRIM(RTRIM(ov.Ruta)), ''),
            ''
        ),

    Presentacion =
        COALESCE(
            NULLIF(LTRIM(RTRIM(f.Presentacion)), ''),
            NULLIF(LTRIM(RTRIM(ov.Presentacion)), ''),
            ''
        ),

    ObservacionLogistica =
        ISNULL(f.ObservacionLogistica, '')

FROM dbo.LogisticaFolio f

LEFT JOIN dbo.OrdenVenta ov
    ON ov.Id = f.OrdenVentaId

LEFT JOIN dbo.ClienteSap cs
    ON UPPER(LTRIM(RTRIM(cs.Cliente))) =
       UPPER(
           LTRIM(
               RTRIM(
                   ISNULL(
                       NULLIF(f.ClienteCodigo, ''),
                       ov.Cliente
                   )
               )
           )
       )

OUTER APPLY
(
    SELECT
        KgProgramados =
            SUM(
                CAST(
                    ISNULL(op.Peso, 0)
                    AS DECIMAL(18,4)
                )
            )
    FROM dbo.OrdenVentaProducto op
    WHERE
        op.PedidoId = ov.Id
        AND ISNULL(op.Eliminado, 0) = 0
) kg

WHERE
    f.FechaEmbarque >= @FechaInicio
    AND f.FechaEmbarque < DATEADD(DAY, 1, @FechaFin)

    AND
    (
        @VendedorId IS NULL
        OR f.VendedorId = @VendedorId
    )

    AND
    (
        @VendedorNombre IS NULL
        OR
        COALESCE(
            NULLIF(LTRIM(RTRIM(f.Vendedor)), ''),
            NULLIF(LTRIM(RTRIM(ov.Vendedor)), ''),
            ''
        ) = @VendedorNombre
    )

    AND
    (
        @Estatus IS NULL
        OR
        UPPER(
            ISNULL(
                NULLIF(LTRIM(RTRIM(f.EstatusLogistico)), ''),
                CASE
                    WHEN f.OrdenVentaId IS NULL
                        THEN 'RESERVADO VENTA'
                    ELSE 'PENDIENTE FLETERA'
                END
            )
        ) = @Estatus
    )

ORDER BY
    f.FechaEmbarque,
    Vendedor,
    f.Folio;
";


            var registros =
                new List<ExcelLogisticaRow>();


            await using (
                var cn =
                    new SqlConnection(_connString)
            )
            {
                await cn.OpenAsync();


                await using var cmd =
                    new SqlCommand(sql, cn);

                cmd.CommandTimeout = 90;


                cmd.Parameters
                    .Add(
                        "@FechaInicio",
                        SqlDbType.Date
                    )
                    .Value =
                        fechaInicio.Date;


                cmd.Parameters
                    .Add(
                        "@FechaFin",
                        SqlDbType.Date
                    )
                    .Value =
                        fechaFin.Date;


                cmd.Parameters
                    .Add(
                        "@VendedorId",
                        SqlDbType.Int
                    )
                    .Value =
                        vendedorFiltro.HasValue
                            ? vendedorFiltro.Value
                            : DBNull.Value;


                cmd.Parameters
                    .Add(
                        "@VendedorNombre",
                        SqlDbType.NVarChar,
                        150
                    )
                    .Value =
                        vendedorNombreFiltro != null
                            ? vendedorNombreFiltro
                            : DBNull.Value;


                cmd.Parameters
                    .Add(
                        "@Estatus",
                        SqlDbType.NVarChar,
                        50
                    )
                    .Value =
                        estatusFiltro != null
                            ? estatusFiltro
                            : DBNull.Value;


                await using var rd =
                    await cmd.ExecuteReaderAsync();


                while (await rd.ReadAsync())
                {
                    registros.Add(
                        new ExcelLogisticaRow
                        {
                            FechaEmbarque =
                                rd.GetDateTime(
                                    rd.GetOrdinal(
                                        "FechaEmbarque"
                                    )
                                ),

                            Folio =
                                rd["Folio"]?.ToString()
                                ?? "",

                            OrdenVenta =
                                rd["OrdenVenta"]?.ToString()
                                ?? "",

                            Cliente =
                                rd["Cliente"]?.ToString()
                                ?? "",

                            Vendedor =
                                rd["Vendedor"]?.ToString()
                                ?? "",

                            Destino =
                                rd["Destino"]?.ToString()
                                ?? "",

                            TipoViaje =
                                rd["TipoViaje"]?.ToString()
                                ?? "SIN DEFINIR",

                            CargaSolicitadaKg =
                                rd["CargaSolicitadaKg"] == DBNull.Value
                                    ? 0m
                                    : Convert.ToDecimal(
                                        rd["CargaSolicitadaKg"]
                                      ),

                            KgOrdenVenta =
                                rd["KgOrdenVenta"] == DBNull.Value
                                    ? 0m
                                    : Convert.ToDecimal(
                                        rd["KgOrdenVenta"]
                                      ),

                            Estatus =
                                rd["Estatus"]?.ToString()
                                ?? "",

                            Fletera =
                                rd["Fletera"]?.ToString()
                                ?? "",

                            Tarimas =
                                rd["Tarimas"] == DBNull.Value
                                    ? 0
                                    : Convert.ToInt32(
                                        rd["Tarimas"]
                                      ),

                            HoraLlegada =
                                rd["HoraLlegada"]?.ToString()
                                ?? "",

                            Ruta =
                                rd["Ruta"]?.ToString()
                                ?? "",

                            Presentacion =
                                rd["Presentacion"]?.ToString()
                                ?? "",

                            ObservacionLogistica =
                                rd["ObservacionLogistica"]?.ToString()
                                ?? ""
                        }
                    );
                }
            }


            // ========================================================
            // 3) KPIs
            // ========================================================

            int totalFolios =
                registros.Count;

            decimal totalCarga =
                registros.Sum(x =>
                    x.CargaSolicitadaKg
                );

            decimal totalKgOv =
                registros.Sum(x =>
                    x.KgOrdenVenta
                );

            int totalPendientes =
                registros.Count(x =>
                    x.Estatus.Equals(
                        "PENDIENTE FLETERA",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            int totalAsignados =
                registros.Count(x =>
                    x.Estatus.Equals(
                        "ASIGNADO",
                        StringComparison.OrdinalIgnoreCase
                    )
                );


            // ========================================================
            // 4) CREAR EXCEL EJECUTIVO
            // ========================================================

            using var wb =
                new XLWorkbook();


            var ws =
                wb.Worksheets.Add(
                    "Planeación"
                );


            // Colores estilo SIGO / ejecutivo.
            var vino =
                XLColor.FromHtml(
                    "#65131A"
                );

            var vinoOscuro =
                XLColor.FromHtml(
                    "#4D0E14"
                );

            var verde =
                XLColor.FromHtml(
                    "#198754"
                );

            var amarillo =
                XLColor.FromHtml(
                    "#D39E00"
                );

            var grisAzul =
                XLColor.FromHtml(
                    "#DCE5EA"
                );

            var grisClaro =
                XLColor.FromHtml(
                    "#F5F7F8"
                );


            // ========================================================
            // TÍTULO
            // ========================================================

            ws.Range("A1:P1")
                .Merge();

            ws.Cell("A1").Value =
                "PLANEACIÓN DE TRANSPORTE / LOGÍSTICA";

            ws.Cell("A1")
                .Style
                .Font
                .Bold = true;

            ws.Cell("A1")
                .Style
                .Font
                .FontSize = 18;

            ws.Cell("A1")
                .Style
                .Font
                .FontColor =
                    XLColor.White;

            ws.Cell("A1")
                .Style
                .Fill
                .BackgroundColor =
                    vinoOscuro;

            ws.Cell("A1")
                .Style
                .Alignment
                .Horizontal =
                    XLAlignmentHorizontalValues.Left;

            ws.Row(1).Height =
                28;


            // ========================================================
            // INFORMACIÓN DEL REPORTE
            // ========================================================

            ws.Range("A2:P2")
                .Merge();

            string alcanceVendedor =
                vendedorFiltro.HasValue
                    ? $"VendedorId {vendedorFiltro.Value}"
                    : (
                        vendedorNombreFiltro != null
                            ? vendedorNombreFiltro
                            : "Todos los vendedores"
                      );

            ws.Cell("A2").Value =
                $"Periodo: {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}   |   " +
                $"Alcance: {alcanceVendedor}   |   " +
                $"Generado por: {login}   |   " +
                $"Fecha de generación: {DateTime.Now:dd/MM/yyyy HH:mm}";

            ws.Cell("A2")
                .Style
                .Font
                .FontSize = 9;

            ws.Cell("A2")
                .Style
                .Font
                .FontColor =
                    XLColor.FromHtml(
                        "#475569"
                    );

            ws.Cell("A2")
                .Style
                .Fill
                .BackgroundColor =
                    grisClaro;


            // ========================================================
            // KPIs
            // ========================================================

            void PintarKpi(
                string rangoTitulo,
                string rangoValor,
                string titulo,
                object valor,
                XLColor color)
            {
                ws.Range(rangoTitulo)
                    .Merge();

                ws.Range(rangoValor)
                    .Merge();

                var tituloCell =
                    ws.Range(rangoTitulo)
                        .FirstCell();

                var valorCell =
                    ws.Range(rangoValor)
                        .FirstCell();

                tituloCell.Value =
                    titulo;

                if (valor is int valorInt)
                {
                    valorCell.Value = valorInt;
                }
                else if (valor is decimal valorDecimal)
                {
                    valorCell.Value = valorDecimal;
                }
                else if (valor is double valorDouble)
                {
                    valorCell.Value = valorDouble;
                }
                else
                {
                    valorCell.Value =
                        Convert.ToString(valor)
                        ?? "";
                }

                ws.Range(rangoTitulo)
                    .Style
                    .Fill
                    .BackgroundColor =
                        color;

                ws.Range(rangoTitulo)
                    .Style
                    .Font
                    .FontColor =
                        XLColor.White;

                ws.Range(rangoTitulo)
                    .Style
                    .Font
                    .Bold = true;

                ws.Range(rangoTitulo)
                    .Style
                    .Alignment
                    .Horizontal =
                        XLAlignmentHorizontalValues.Center;

                ws.Range(rangoValor)
                    .Style
                    .Fill
                    .BackgroundColor =
                        XLColor.White;

                ws.Range(rangoValor)
                    .Style
                    .Font
                    .Bold = true;

                ws.Range(rangoValor)
                    .Style
                    .Font
                    .FontSize = 14;

                ws.Range(rangoValor)
                    .Style
                    .Alignment
                    .Horizontal =
                        XLAlignmentHorizontalValues.Center;

                ws.Range(rangoTitulo)
                    .Style
                    .Border
                    .OutsideBorder =
                        XLBorderStyleValues.Thin;

                ws.Range(rangoValor)
                    .Style
                    .Border
                    .OutsideBorder =
                        XLBorderStyleValues.Thin;

                ws.Range(rangoTitulo)
                    .Style
                    .Border
                    .OutsideBorderColor =
                        XLColor.FromHtml(
                            "#CBD5E1"
                        );

                ws.Range(rangoValor)
                    .Style
                    .Border
                    .OutsideBorderColor =
                        XLColor.FromHtml(
                            "#CBD5E1"
                        );
            }


            PintarKpi(
                "A4:B4",
                "A5:B5",
                "FOLIOS / EMBARQUES",
                totalFolios,
                vino
            );

            PintarKpi(
                "C4:E4",
                "C5:E5",
                "CARGA SOLICITADA KG",
                totalCarga,
                XLColor.FromHtml("#3D647A")
            );

            PintarKpi(
                "F4:H4",
                "F5:H5",
                "KG ORDEN DE VENTA",
                totalKgOv,
                XLColor.FromHtml("#287D8E")
            );

            PintarKpi(
                "I4:K4",
                "I5:K5",
                "PENDIENTE FLETERA",
                totalPendientes,
                amarillo
            );

            PintarKpi(
                "L4:N4",
                "L5:N5",
                "CON FLETERA",
                totalAsignados,
                verde
            );


            ws.Cell("C5")
                .Style
                .NumberFormat
                .Format =
                    "#,##0.00";

            ws.Cell("F5")
                .Style
                .NumberFormat
                .Format =
                    "#,##0.00";


            // ========================================================
            // TABLA
            // ========================================================

            int filaEncabezado =
                8;

            string[] headers =
            {
                "FECHA EMBARQUE",
                "FOLIO",
                "ORDEN DE VENTA",
                "CLIENTE",
                "DESTINO",
                "TIPO DE VIAJE",
                "VENDEDOR",
                "CARGA SOLICITADA KG",
                "KG OV",
                "ESTATUS",
                "FLETERA",
                "TARIMAS",
                "LLEGADA UNIDAD",
                "RUTA",
                "PRESENTACIÓN",
                "OBSERVACIÓN LOGÍSTICA"
            };


            for (
                int i = 0;
                i < headers.Length;
                i++
            )
            {
                var c =
                    ws.Cell(
                        filaEncabezado,
                        i + 1
                    );

                c.Value =
                    headers[i];

                c.Style
                    .Fill
                    .BackgroundColor =
                        vino;

                c.Style
                    .Font
                    .FontColor =
                        XLColor.White;

                c.Style
                    .Font
                    .Bold =
                        true;

                c.Style
                    .Alignment
                    .Horizontal =
                        XLAlignmentHorizontalValues.Center;

                c.Style
                    .Alignment
                    .Vertical =
                        XLAlignmentVerticalValues.Center;
            }


            ws.Row(filaEncabezado)
                .Height =
                    24;


            int fila =
                filaEncabezado + 1;


            foreach (
                var x in registros
            )
            {
                ws.Cell(fila, 1).Value =
                    x.FechaEmbarque;

                ws.Cell(fila, 2).Value =
                    x.Folio;

                ws.Cell(fila, 3).Value =
                    x.OrdenVenta;

                ws.Cell(fila, 4).Value =
                    x.Cliente;

                ws.Cell(fila, 5).Value =
                    x.Destino;

                ws.Cell(fila, 6).Value =
                    x.TipoViaje;

                ws.Cell(fila, 7).Value =
                    x.Vendedor;

                ws.Cell(fila, 8).Value =
                    x.CargaSolicitadaKg;

                ws.Cell(fila, 9).Value =
                    x.KgOrdenVenta;

                ws.Cell(fila, 10).Value =
                    x.Estatus;

                ws.Cell(fila, 11).Value =
                    x.Fletera;

                ws.Cell(fila, 12).Value =
                    x.Tarimas;

                ws.Cell(fila, 13).Value =
                    x.HoraLlegada;

                ws.Cell(fila, 14).Value =
                    x.Ruta;

                ws.Cell(fila, 15).Value =
                    x.Presentacion;

                ws.Cell(fila, 16).Value =
                    x.ObservacionLogistica;


                ws.Cell(fila, 1)
                    .Style
                    .NumberFormat
                    .Format =
                        "dd/MM/yyyy";

                ws.Cell(fila, 8)
                    .Style
                    .NumberFormat
                    .Format =
                        "#,##0.00";

                ws.Cell(fila, 9)
                    .Style
                    .NumberFormat
                    .Format =
                        "#,##0.00";


                if (
                    fila % 2 == 0
                )
                {
                    ws.Range(
                        fila,
                        1,
                        fila,
                        16
                    )
                    .Style
                    .Fill
                    .BackgroundColor =
                        grisClaro;
                }


                // Color ejecutivo del estatus.
                // Semáforo del tipo de viaje.
                var tipoViajeCell =
                    ws.Cell(
                        fila,
                        6
                    );

                switch (
                    (x.TipoViaje ?? "")
                        .Trim()
                        .ToUpperInvariant()
                )
                {
                    case "CONFIRMADO":
                        tipoViajeCell.Style.Fill.BackgroundColor =
                            XLColor.FromHtml("#D1FAE5");
                        tipoViajeCell.Style.Font.FontColor =
                            XLColor.FromHtml("#065F46");
                        break;

                    case "TENTATIVO":
                        tipoViajeCell.Style.Fill.BackgroundColor =
                            XLColor.FromHtml("#FEF3C7");
                        tipoViajeCell.Style.Font.FontColor =
                            XLColor.FromHtml("#92400E");
                        break;

                    default:
                        tipoViajeCell.Style.Fill.BackgroundColor =
                            grisAzul;
                        tipoViajeCell.Style.Font.FontColor =
                            XLColor.FromHtml("#475569");
                        break;
                }

                tipoViajeCell.Style.Font.Bold = true;

                // Color ejecutivo del estatus logístico.
                var estatusCell =
                    ws.Cell(
                        fila,
                        10
                    );

                switch (
                    x.Estatus
                        .Trim()
                        .ToUpperInvariant()
                )
                {
                    case "ASIGNADO":
                        estatusCell
                            .Style
                            .Fill
                            .BackgroundColor =
                                XLColor.FromHtml(
                                    "#D1FAE5"
                                );

                        estatusCell
                            .Style
                            .Font
                            .FontColor =
                                XLColor.FromHtml(
                                    "#065F46"
                                );
                        break;

                    case "PENDIENTE FLETERA":
                        estatusCell
                            .Style
                            .Fill
                            .BackgroundColor =
                                XLColor.FromHtml(
                                    "#FEF3C7"
                                );

                        estatusCell
                            .Style
                            .Font
                            .FontColor =
                                XLColor.FromHtml(
                                    "#92400E"
                                );
                        break;

                    case "CANCELADO":
                    case "FLETERA CANCELADA":
                        estatusCell
                            .Style
                            .Fill
                            .BackgroundColor =
                                XLColor.FromHtml(
                                    "#FEE2E2"
                                );

                        estatusCell
                            .Style
                            .Font
                            .FontColor =
                                XLColor.FromHtml(
                                    "#991B1B"
                                );
                        break;

                    default:
                        estatusCell
                            .Style
                            .Fill
                            .BackgroundColor =
                                grisAzul;
                        break;
                }


                estatusCell
                    .Style
                    .Font
                    .Bold =
                        true;


                fila++;
            }


            if (
                registros.Count == 0
            )
            {
                ws.Range(
                    fila,
                    1,
                    fila,
                    16
                )
                .Merge();

                ws.Cell(
                    fila,
                    1
                ).Value =
                    "Sin registros para los filtros seleccionados.";

                ws.Cell(
                    fila,
                    1
                )
                .Style
                .Alignment
                .Horizontal =
                    XLAlignmentHorizontalValues.Center;

                ws.Cell(
                    fila,
                    1
                )
                .Style
                .Font
                .Italic =
                    true;

                fila++;
            }


            int ultimaFila =
                Math.Max(
                    filaEncabezado,
                    fila - 1
                );


            // Bordes y ajuste visual.
            var rangoDatos =
                ws.Range(
                    filaEncabezado,
                    1,
                    ultimaFila,
                    16
                );

            rangoDatos
                .Style
                .Border
                .InsideBorder =
                    XLBorderStyleValues.Hair;

            rangoDatos
                .Style
                .Border
                .InsideBorderColor =
                    XLColor.FromHtml(
                        "#D5DDE2"
                    );

            rangoDatos
                .Style
                .Border
                .OutsideBorder =
                    XLBorderStyleValues.Thin;

            rangoDatos
                .Style
                .Border
                .OutsideBorderColor =
                    XLColor.FromHtml(
                        "#AAB7C0"
                    );


            // Autofiltro sin convertir todo a estilo de tabla.
            if (
                registros.Count > 0
            )
            {
                ws.Range(
                    filaEncabezado,
                    1,
                    ultimaFila,
                    16
                )
                .SetAutoFilter();
            }


            // Congelar encabezados.
            ws.SheetView
                .FreezeRows(
                    filaEncabezado
                );


            // Anchos.
            ws.Column(1).Width = 15;
            ws.Column(2).Width = 25;
            ws.Column(3).Width = 18;
            ws.Column(4).Width = 34;
            ws.Column(5).Width = 30;
            ws.Column(6).Width = 17;
            ws.Column(7).Width = 27;
            ws.Column(8).Width = 20;
            ws.Column(9).Width = 15;
            ws.Column(10).Width = 22;
            ws.Column(11).Width = 25;
            ws.Column(12).Width = 12;
            ws.Column(13).Width = 16;
            ws.Column(14).Width = 35;
            ws.Column(15).Width = 21;
            ws.Column(16).Width = 45;


            ws.Range(
                filaEncabezado + 1,
                1,
                ultimaFila,
                16
            )
            .Style
            .Alignment
            .Vertical =
                XLAlignmentVerticalValues.Top;


            ws.Columns(4, 7)
                .Style
                .Alignment
                .WrapText =
                    true;

            ws.Columns(10, 11)
                .Style
                .Alignment
                .WrapText =
                    true;

            ws.Columns(14, 16)
                .Style
                .Alignment
                .WrapText =
                    true;


            // Configuración de impresión.
            ws.PageSetup
                .PageOrientation =
                    XLPageOrientation.Landscape;

            ws.PageSetup
                .FitToPages(
                    1,
                    0
                );

            ws.PageSetup
                .Margins
                .Top =
                    0.4;

            ws.PageSetup
                .Margins
                .Bottom =
                    0.4;

            ws.PageSetup
                .Margins
                .Left =
                    0.3;

            ws.PageSetup
                .Margins
                .Right =
                    0.3;


            // ========================================================
            // 5) DESCARGA
            // ========================================================

            using var stream =
                new MemoryStream();

            wb.SaveAs(
                stream
            );


            string nombreArchivo =
                $"Planeacion_Logistica_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.xlsx";


            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                nombreArchivo
            );
        }


    }
}