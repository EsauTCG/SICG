
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public class EntregasSapService : IEntregasSapService
    {
        private readonly IConfiguration _cfg;
        private readonly AppDbContext _db;
        private readonly ISapServiceLayerClient _sap;

        public EntregasSapService(
            AppDbContext db,
            IConfiguration configuration,
            ISapServiceLayerClient sap)
        {
            _db = db;
            _cfg = configuration;
            _sap = sap;
        }

        private static string Norm(string? source)
            => (source ?? "P1").Trim().ToUpperInvariant();

        private string GetConn(string? source)
        {
            var s = Norm(source);

            return s == "TIF"
                ? _cfg.GetConnectionString("CadenaMeatTIF")!
                : _cfg.GetConnectionString("CadenaMeatP1")!;
        }

        private static string GetCommerciaDb(string? source)
        {
            return Norm(source) == "TIF"
                ? "TIF_CommerciaNET"
                : "CommerciaNET";
        }

        private static string Q(string name)
            => $"[{name.Replace("]", "]]")}]";

        private static string Db(string db, string table)
            => $"{Q(db)}.[dbo].{Q(table)}";

        // ============================================================
        // LISTADO DE ENTREGAS - LOGICA ORIGINAL
        // ============================================================
        public async Task<List<EntregaSapRowVM>> ListarAsync(
            DateTime desde,
            DateTime hasta,
            string source)
        {
            var cnStr = GetConn(source);
            var src = Norm(source);

            var sql = @"
SELECT
    d.SolicitudSurtidoId,
    d.Referencia AS ReferenciaDocMeat,
    sr9.Referencia AS Remision,
    d.FechaHora AS FechaDocumento,
    sr10.Referencia AS Cliente
FROM SurtidoReferencia d
INNER JOIN SurtidoReferencia sr9
    ON d.SolicitudSurtidoId = sr9.SolicitudSurtidoId
   AND sr9.TipoReferenciaId = 9
LEFT JOIN SurtidoReferencia sr10
    ON d.SolicitudSurtidoId = sr10.SolicitudSurtidoId
   AND sr10.TipoReferenciaId = 6
WHERE d.TipoReferenciaId = 12
  AND d.FechaHora >= @desde
  AND d.FechaHora < @hasta
ORDER BY d.FechaHora DESC;
";

            var list = new List<EntregaSapRowVM>();

            await using (var cn = new SqlConnection(cnStr))
            {
                await cn.OpenAsync();

                await using var cmd = new SqlCommand(sql, cn);

                cmd.Parameters.AddWithValue("@desde", desde);
                cmd.Parameters.AddWithValue("@hasta", hasta);

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    list.Add(new EntregaSapRowVM
                    {
                        SolicitudSurtidoId = rd.GetInt32(0),
                        ReferenciaDocMeat = rd.IsDBNull(1)
                            ? "" : rd.GetString(1),
                        Remision = rd.IsDBNull(2)
                            ? "" : rd.GetString(2),
                        FechaDocumento = rd.IsDBNull(3)
                            ? null : rd.GetDateTime(3),
                        Cliente = rd.IsDBNull(4)
                            ? null : rd.GetString(4)
                    });
                }
            }

            var refs = list
                .Select(x => (x.ReferenciaDocMeat ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (refs.Count > 0)
            {
                var logs = await _db.EntregaSapLogs
                    .Where(l =>
                        l.Source == src &&
                        refs.Contains(l.Referencia))
                    .Select(l => new
                    {
                        l.Referencia,
                        l.Estatus,
                        l.FechaIntento,
                        l.Mensaje
                    })
                    .ToListAsync();

                var map = logs.ToDictionary(
                    x => x.Referencia,
                    x => x);

                foreach (var r in list)
                {
                    var refx =
                        (r.ReferenciaDocMeat ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(refx))
                        continue;

                    if (map.TryGetValue(refx, out var lg))
                    {
                        r.EnviadoSap = lg.Estatus;
                        r.FechaEnvioSap = lg.FechaIntento;
                        r.MsgSap = lg.Mensaje;
                    }
                    else
                    {
                        r.EnviadoSap = null;
                    }
                }
            }

            return list;
        }

        // ============================================================
        // ENRUTADOR JSON
        //
        // Las entregas historicas conservan su logica.
        // Solo las VREM confirmadas por surtido SIGO usan
        // la construccion agrupada SKU / LOTE.
        // ============================================================
        public async Task<string> BuildJsonAsync(
            string referenciaDocMeat,
            string source)
        {
            if (string.IsNullOrWhiteSpace(referenciaDocMeat))
            {
                return await BuildJsonOriginalAsync(
                    referenciaDocMeat,
                    source);
            }

            var origen = Norm(source);
            var referencia = referenciaDocMeat.Trim();

            if (origen != "TIF" && origen != "P1")
            {
                return await BuildJsonOriginalAsync(
                    referenciaDocMeat,
                    source);
            }

            var esNuevaVrem =
                await EsVremSigoConfirmadaAsync(
                    referencia,
                    origen);

            if (esNuevaVrem)
            {
                return await BuildJsonVremSigoAsync(
                    referencia,
                    origen);
            }

            return await BuildJsonOriginalAsync(
                referenciaDocMeat,
                source);
        }

        // ============================================================
        // IDENTIFICAR VREM CONFIRMADA POR SIGO
        // ============================================================
        private async Task<bool> EsVremSigoConfirmadaAsync(
            string referencia,
            string origen)
        {
            var cnStr =
                _cfg.GetConnectionString("CadenaSQLSIGO");

            if (string.IsNullOrWhiteSpace(cnStr))
            {
                throw new InvalidOperationException(
                    "Falta la conexion CadenaSQLSIGO.");
            }

            const string sql = @"
SELECT COUNT_BIG(1)
FROM dbo.SurtidoIntegracionVremConfirmada v
INNER JOIN dbo.SurtidoIntegracionCola q
    ON q.Id = v.ColaId
INNER JOIN dbo.SurtidoCaptura c
    ON c.Id = q.SurtidoCapturaId
WHERE v.DocumentoVrem = @Documento
  AND q.Estatus = 'COMPLETADO'
  AND c.Planta = @Planta;
";

            await using var cn = new SqlConnection(cnStr);

            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@Documento",
                System.Data.SqlDbType.VarChar,
                80
            ).Value = referencia;

            cmd.Parameters.Add(
                "@Planta",
                System.Data.SqlDbType.VarChar,
                20
            ).Value = origen;

            var coincidencias =
                Convert.ToInt64(
                    await cmd.ExecuteScalarAsync());

            if (coincidencias > 1)
            {
                throw new InvalidOperationException(
                    "La VREM esta conciliada mas de una vez.");
            }

            return coincidencias == 1;
        }

        // ============================================================
        // NUEVO JSON VREM SIGO
        //
        // 1 LINEA POR SKU
        // VARIOS LOTES DENTRO DE BatchNumbers
        //
        // NO INSERTA
        // NO ACTUALIZA
        // NO ENVIA A SAP
        // ============================================================
        private async Task<string> BuildJsonVremSigoAsync(
            string referencia,
            string origen)
        {
            var cnStr = GetConn(origen);

            var bd = GetCommerciaDb(origen);

            var movimiento = Db(
                bd,
                "Movimiento");

            var referenciaMov = Db(
                bd,
                "MovimientoReferencia");

            var cliente = Db(
                bd,
                "Cliente");

            var wh = origen == "TIF"
                ? "PLATIFGE"
                : "PLAP1GEN";

            var sql = $@"
SET NOCOUNT ON;

DECLARE @SolicitudId INT;

-- =======================================================
-- 1. OBTENER SOLICITUD DE LA VREM
-- =======================================================
SELECT TOP (1)
    @SolicitudId = sr.SolicitudSurtidoId
FROM dbo.SurtidoReferencia sr
WHERE sr.TipoReferenciaId = 12
  AND sr.Referencia = @ref;

IF @SolicitudId IS NULL
BEGIN
    THROW 52401,
        'La referencia VREM no existe en Meat.',
        1;
END;

-- =======================================================
-- 2. VALIDAR REFERENCIAS DE CADA MOVIMIENTO
-- =======================================================
IF EXISTS
(
    SELECT 1
    FROM {movimiento} m

    OUTER APPLY
    (
        SELECT
            COUNT(*) AS TotalPR,
            MAX(
                TRY_CONVERT(INT, mr.Referencia)
            ) AS ProduccionId
        FROM {referenciaMov} mr
        WHERE mr.MovimientoId = CONCAT(
            m.EmpresaId, '.',
            m.SucursalId, '.',
            m.OperacionId, '.',
            m.Folio, '.',
            m.RenglonId
        )
          AND mr.Tipo = 'PR'
    ) x

    LEFT JOIN dbo.Produccion p
        ON p.ProduccionId = x.ProduccionId

    WHERE CONCAT(
        m.EmpresaId, '.',
        m.SucursalId, '.',
        m.OperacionId, '.',
        m.Folio
    ) = @ref

      AND
      (
          m.OperacionId <> 'VREM'
          OR x.TotalPR <> 1
          OR p.ProduccionId IS NULL
          OR p.Articulo <> m.ArticuloId

          OR NOT EXISTS
          (
              SELECT 1
              FROM dbo.ProduccionLog pl
              WHERE pl.ProduccionLogId = @SolicitudId
                AND pl.ProcesoId = 17
                AND pl.ProduccionId = p.ProduccionId
                AND pl.FechaHoraServer IS NOT NULL
          )
      )
)
BEGIN
    THROW 52402,
        'VREM con caja sin PR, SKU distinto o sin evidencia Meat.',
        1;
END;

-- =======================================================
-- 3. DETALLE COMERCIAL DE CAJAS
-- =======================================================
SELECT
    m.ArticuloId AS SKU,

    CAST(
        m.Unidad * m.FactorUnidad
        AS DECIMAL(20,4)
    ) AS Kg,

    l.Nombre AS Lote

INTO #DetalleVrem

FROM {movimiento} m

INNER JOIN {referenciaMov} mr
    ON mr.MovimientoId = CONCAT(
        m.EmpresaId, '.',
        m.SucursalId, '.',
        m.OperacionId, '.',
        m.Folio, '.',
        m.RenglonId
    )
   AND mr.Tipo = 'PR'

INNER JOIN dbo.Produccion p
    ON p.ProduccionId =
       TRY_CONVERT(INT, mr.Referencia)

INNER JOIN dbo.Lote l
    ON l.LoteId = p.LoteId

WHERE CONCAT(
    m.EmpresaId, '.',
    m.SucursalId, '.',
    m.OperacionId, '.',
    m.Folio
) = @ref

AND m.OperacionId = 'VREM';

-- =======================================================
-- 4. VALIDACIONES
-- =======================================================
IF NOT EXISTS
(
    SELECT 1
    FROM #DetalleVrem
)
BEGIN
    THROW 52403,
        'VREM sin movimientos para crear DocumentLines.',
        1;
END;

IF EXISTS
(
    SELECT 1
    FROM #DetalleVrem
    WHERE Kg <= 0
       OR Kg IS NULL
       OR Lote IS NULL
       OR LTRIM(RTRIM(Lote)) = ''
)
BEGIN
    THROW 52404,
        'VREM con kilogramos o lotes invalidos.',
        1;
END;

DECLARE @MovimientosCommercia INT;
DECLARE @KgCommercia DECIMAL(20,4);

SELECT
    @MovimientosCommercia = COUNT(*),

    @KgCommercia =
        CAST(
            COALESCE(
                SUM(m.Unidad * m.FactorUnidad),
                0
            )
            AS DECIMAL(20,4)
        )

FROM {movimiento} m

WHERE CONCAT(
    m.EmpresaId, '.',
    m.SucursalId, '.',
    m.OperacionId, '.',
    m.Folio
) = @ref

AND m.OperacionId = 'VREM';

IF
(
    SELECT COUNT(*)
    FROM #DetalleVrem
) <> @MovimientosCommercia
BEGIN
    THROW 52405,
        'Faltan movimientos o existen PR duplicados.',
        1;
END;

IF ABS(
    (
        SELECT SUM(Kg)
        FROM #DetalleVrem
    ) - @KgCommercia
) > 0.001
BEGIN
    THROW 52406,
        'KG del detalle no coinciden con CommerciaNET.',
        1;
END;

-- =======================================================
-- 5. JSON AGRUPADO POR SKU Y LOTE
-- =======================================================
SELECT
(
    SELECT
        COALESCE(
            (
                SELECT TOP (1)
                    cl.CodigoId

                FROM {cliente} cl

                WHERE cl.ClienteId =
                (
                    SELECT TOP (1)
                        s5.Referencia

                    FROM dbo.SurtidoReferencia s5

                    WHERE s5.SolicitudSurtidoId =
                          d.SolicitudSurtidoId

                      AND s5.TipoReferenciaId = 5
                )
            ),

            (
                SELECT TOP (1)
                    s5.Referencia

                FROM dbo.SurtidoReferencia s5

                WHERE s5.SolicitudSurtidoId =
                      d.SolicitudSurtidoId

                  AND s5.TipoReferenciaId = 5
            )
        ) AS [CardCode],

        (
            SELECT TOP (1)
                s9.Referencia

            FROM dbo.SurtidoReferencia s9

            WHERE s9.SolicitudSurtidoId =
                  d.SolicitudSurtidoId

              AND s9.TipoReferenciaId = 9
        ) AS [NumAtCard],

        CONCAT(
            'Documento creado desde SIGO [',

            FORMAT(
                GETDATE(),
                'dd/MM/yyyy hh:mm:ss tt',
                'es-MX'
            ),

            ']'
        ) AS [Comments],

        d.Referencia AS [U_DocMeat],

        -- ===============================================
        -- UNA LINEA POR CADA SKU
        -- ===============================================
        JSON_QUERY(
        (
            SELECT
                sku.SKU AS [ItemCode],

                @wh AS [WarehouseCode],

                CAST(
                    (
                        SELECT SUM(x.Kg)
                        FROM #DetalleVrem x
                        WHERE x.SKU = sku.SKU
                    )
                    AS DECIMAL(20,4)
                ) AS [Quantity],

                17 AS [BaseType],

                (
                    SELECT TOP (1)
                        TRY_CONVERT(
                            INT,
                            s43.Referencia
                        )

                    FROM dbo.SurtidoReferencia s43

                    WHERE s43.SolicitudSurtidoId =
                          d.SolicitudSurtidoId

                      AND s43.TipoReferenciaId = 43
                ) AS [BaseEntry],

                -- PROVISIONAL:
                -- validar contra LineNum de la OV SAP.
                CAST(
                    ROW_NUMBER() OVER(
                        ORDER BY sku.SKU
                    ) - 1
                    AS INT
                ) AS [BaseLine],

                -- =======================================
                -- LOTES AGRUPADOS DENTRO DEL SKU
                -- =======================================
                JSON_QUERY(
                (
                    SELECT
                        x.Lote AS [BatchNumber],

                        CAST(
                            SUM(x.Kg)
                            AS DECIMAL(20,4)
                        ) AS [Quantity]

                    FROM #DetalleVrem x

                    WHERE x.SKU = sku.SKU

                    GROUP BY x.Lote

                    ORDER BY x.Lote

                    FOR JSON PATH
                )) AS [BatchNumbers]

            FROM
            (
                SELECT DISTINCT
                    SKU
                FROM #DetalleVrem
            ) sku

            ORDER BY sku.SKU

            FOR JSON PATH
        )) AS [DocumentLines]

    FROM dbo.SurtidoReferencia d

    WHERE d.TipoReferenciaId = 12
      AND d.Referencia = @ref

    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER

) AS Json;

-- =======================================================
-- 6. RESULTADO DE CONTROL
-- =======================================================
SELECT
    @MovimientosCommercia AS Movimientos,

    @KgCommercia AS Kg,

    (
        SELECT COUNT(DISTINCT SKU)
        FROM #DetalleVrem
    ) AS Skus;
";

            await using var cn =
                new SqlConnection(cnStr);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.CommandTimeout = 120;

            cmd.Parameters.Add(
                "@ref",
                System.Data.SqlDbType.VarChar,
                80
            ).Value = referencia;

            cmd.Parameters.Add(
                "@wh",
                System.Data.SqlDbType.VarChar,
                20
            ).Value = wh;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync() ||
                rd.IsDBNull(0))
            {
                throw new InvalidOperationException(
                    "No se genero el JSON de la VREM.");
            }

            var json = rd.GetString(0);

            if (!await rd.NextResultAsync() ||
                !await rd.ReadAsync())
            {
                throw new InvalidOperationException(
                    "No se obtuvo el total comercial.");
            }

            var movimientos = rd.GetInt32(0);
            var kgCommercia = rd.GetDecimal(1);
            var skus = rd.GetInt32(2);

            using var doc =
                JsonDocument.Parse(json);

            var root = doc.RootElement;

            if (!root.TryGetProperty(
                    "DocumentLines",
                    out var lines) ||
                lines.ValueKind != JsonValueKind.Array ||
                lines.GetArrayLength() != skus ||
                skus <= 0)
            {
                throw new InvalidOperationException(
                    "El JSON no contiene todas las lineas por SKU.");
            }

            decimal kgJson = 0m;

            foreach (var line in lines.EnumerateArray())
            {
                var qty = line.GetProperty(
                    "Quantity").GetDecimal();

                var batches = line.GetProperty(
                    "BatchNumbers");

                if (qty <= 0m ||
                    batches.ValueKind != JsonValueKind.Array ||
                    batches.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException(
                        "SKU sin cantidad o sin lotes.");
                }

                decimal kgLotes = 0m;

                foreach (var batch in batches.EnumerateArray())
                {
                    var lote = batch
                        .GetProperty("BatchNumber")
                        .GetString();

                    if (string.IsNullOrWhiteSpace(lote))
                    {
                        throw new InvalidOperationException(
                            "Lote vacio en el JSON.");
                    }

                    kgLotes += batch
                        .GetProperty("Quantity")
                        .GetDecimal();
                }

                if (Math.Abs(kgLotes - qty) > 0.001m)
                {
                    throw new InvalidOperationException(
                        "Los KG de los lotes no coinciden con el SKU.");
                }

                kgJson += qty;
            }

            if (movimientos <= 0 ||
                Math.Abs(kgJson - kgCommercia) > 0.001m)
            {
                throw new InvalidOperationException(
                    "Los KG del JSON no coinciden con la VREM.");
            }

            return json;
        }

        // ============================================================
        // JSON ORIGINAL
        //
        // CONSERVADO PARA LAS REMISIONES HISTORICAS
        // ============================================================
        private async Task<string> BuildJsonOriginalAsync(
            string referenciaDocMeat,
            string source)
        {
            var cnStr = GetConn(source);
            var commerciaDb = GetCommerciaDb(source);

            var warehouseCode =
                Norm(source) == "TIF"
                    ? "PLATIFGE"
                    : "PLAP1GEN";

            var tblCliente =
                Db(commerciaDb, "Cliente");

            var tblMovimiento =
                Db(commerciaDb, "Movimiento");

            var sql = $@"
SELECT (
    SELECT
        ISNULL(
            (SELECT CodigoId
             FROM {tblCliente} c WITH (NOLOCK)
             WHERE c.Clienteid = (
                 SELECT sr5.Referencia
                 FROM SurtidoReferencia sr5 WITH (NOLOCK)
                 WHERE sr5.SolicitudSurtidoId =
                       Document.SolicitudSurtidoId
                   AND sr5.TipoReferenciaId = 5
             )),
            (SELECT sr5b.Referencia
             FROM SurtidoReferencia sr5b WITH (NOLOCK)
             WHERE sr5b.SolicitudSurtidoId =
                   Document.SolicitudSurtidoId
               AND sr5b.TipoReferenciaId = 5)
        ) AS [CardCode],

        sr9.Referencia AS [NumAtCard],

        'Documento creado desde SIGO ['
        + FORMAT(
            GETDATE(),
            'dd/MM/yyyy hh:mm:ss tt',
            'es-MX'
        )
        + ']' AS [Comments],

        Document.Referencia AS [U_DocMeat],

        (
            SELECT
                mov.ArticuloId AS [ItemCode],
                @wh AS [WarehouseCode],

                CAST(
                    SUM(P.PesoNeto)
                    AS DECIMAL(20,4)
                ) AS [Quantity],

                17 AS [BaseType],

                TRY_CONVERT(
                    INT,
                    sr43.Referencia
                ) AS [BaseEntry],

                mov.RenglonId AS [BaseLine],

                (
                    SELECT
                        L.Nombre AS [BatchNumber],

                        CAST(
                            SUM(PR.PesoNeto)
                            AS DECIMAL(20,4)
                        ) AS [Quantity]

                    FROM SalidaEmbarque SE2 WITH (NOLOCK)

                    INNER JOIN Produccion PR WITH (NOLOCK)
                        ON PR.ProduccionId =
                           SE2.ProduccionId

                    INNER JOIN Lote L WITH (NOLOCK)
                        ON PR.LoteId = L.LoteId

                    WHERE SE2.SolicitudSurtidoId =
                          Document.SolicitudSurtidoId

                      AND PR.Articulo = mov.ArticuloId

                      AND PR.UltimoProcesoId != 31

                    GROUP BY L.Nombre

                    FOR JSON PATH
                ) AS [BatchNumbers]

            FROM SalidaEmbarque SE WITH (NOLOCK)

            INNER JOIN Produccion P WITH (NOLOCK)
                ON P.ProduccionId = SE.ProduccionId

            LEFT JOIN SurtidoReferencia sr43 WITH (NOLOCK)
                ON SE.SolicitudSurtidoId =
                   sr43.SolicitudSurtidoId
               AND sr43.TipoReferenciaId = 43

            INNER JOIN {tblMovimiento} mov WITH (NOLOCK)
                ON CONCAT(
                    mov.EmpresaId, '.',
                    mov.SucursalId, '.',
                    mov.OperacionId, '.',
                    mov.Folio
                ) = sr9.Referencia

            WHERE SE.SolicitudSurtidoId =
                  Document.SolicitudSurtidoId

              AND P.Articulo = mov.ArticuloId

              AND P.UltimoProcesoId != 31

            GROUP BY
                mov.ArticuloId,
                mov.RenglonId,
                sr43.Referencia

            FOR JSON PATH
        ) AS [DocumentLines]

    FROM SurtidoReferencia Document WITH (NOLOCK)

    INNER JOIN SurtidoReferencia sr9 WITH (NOLOCK)
        ON Document.SolicitudSurtidoId =
           sr9.SolicitudSurtidoId
       AND sr9.TipoReferenciaId = 9

    WHERE Document.Referencia = @ref
      AND Document.TipoReferenciaId = 12

    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS Json;
";

            await using var cn =
                new SqlConnection(cnStr);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.CommandTimeout = 300;

            cmd.Parameters.Add(
                "@ref",
                System.Data.SqlDbType.VarChar,
                80
            ).Value = referenciaDocMeat ?? "";

            cmd.Parameters.Add(
                "@wh",
                System.Data.SqlDbType.VarChar,
                20
            ).Value = warehouseCode;

            var json =
                (string?)await cmd.ExecuteScalarAsync();

            return string.IsNullOrWhiteSpace(json)
                ? "{}"
                : json;
        }

        // ============================================================
        // FACTURA DE RESERVA
        // LOGICA EXISTENTE CONSERVADA
        // ============================================================
        public async Task<string> BuildReserveInvoiceJsonAsync(
            string referenciaDocMeat,
            string source)
        {
            var baseJson = await BuildJsonAsync(
                referenciaDocMeat,
                source);

            JsonObject? obj;

            try
            {
                obj = JsonNode.Parse(baseJson) as JsonObject;
            }
            catch
            {
                obj = null;
            }

            if (obj == null)
                return "{}";

            obj["Comments"] =
                $"Factura de reserva creada desde SIGO [{DateTime.Now:dd/MM/yyyy HH:mm:ss}]";

            obj["ReserveInvoice"] = "tYES";

            var lines = obj["DocumentLines"] as JsonArray;

            if (lines == null || lines.Count == 0)
            {
                return obj.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });
            }

            int? orderDocEntry = null;

            foreach (var ln in lines)
            {
                if (ln is not JsonObject lno)
                    continue;

                if (TryGetInt(
                    lno,
                    "BaseEntry",
                    out var be) && be > 0)
                {
                    orderDocEntry = be;
                    break;
                }
            }

            if (orderDocEntry == null)
            {
                RemoveBatchesAndSerialsOnlyForReserve(
                    lines);

                return obj.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });
            }

            List<SapOrderLine> ovLines;

            try
            {
                ovLines = await GetOrderLinesFromSapAsync(
                    orderDocEntry.Value);
            }
            catch
            {
                RemoveBatchesAndSerialsOnlyForReserve(
                    lines);

                return obj.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });
            }

            foreach (var ln in lines)
            {
                if (ln is not JsonObject lno)
                    continue;

                var itemCode =
                    (lno["ItemCode"]?.ToString() ?? "").Trim();

                if (string.IsNullOrWhiteSpace(itemCode))
                    continue;

                var qtyDec = 0m;

                _ = TryGetDecimal(
                    lno,
                    "Quantity",
                    out qtyDec);

                var hasBaseLine = TryGetInt(
                    lno,
                    "BaseLine",
                    out var currentBaseLine);

                bool baseLineMatchesItem = false;

                if (hasBaseLine && currentBaseLine >= 0)
                {
                    var match = ovLines.FirstOrDefault(
                        x => x.LineNum == currentBaseLine);

                    baseLineMatchesItem =
                        match != null &&
                        string.Equals(
                            match.ItemCode,
                            itemCode,
                            StringComparison.OrdinalIgnoreCase);
                }

                if (!hasBaseLine ||
                    currentBaseLine < 0 ||
                    !baseLineMatchesItem)
                {
                    var resolved = ResolveBaseLineBySku(
                        itemCode,
                        qtyDec,
                        ovLines);

                    if (resolved >= 0)
                        lno["BaseLine"] = resolved;
                }

                lno["BaseType"] = 17;
                lno["BaseEntry"] = orderDocEntry.Value;

                lno.Remove("BatchNumbers");
                lno.Remove("SerialNumbers");
            }

            RemoveBatchesAndSerialsOnlyForReserve(lines);

            return obj.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = false
                });
        }

        private static void RemoveBatchesAndSerialsOnlyForReserve(
            JsonArray lines)
        {
            foreach (var ln in lines)
            {
                if (ln is not JsonObject lno)
                    continue;

                lno.Remove("BatchNumbers");
                lno.Remove("SerialNumbers");
            }
        }

        private static bool TryGetInt(
            JsonObject obj,
            string prop,
            out int value)
        {
            value = default;

            if (!obj.TryGetPropertyValue(
                    prop,
                    out var node) ||
                node == null)
            {
                return false;
            }

            try
            {
                if (node is JsonValue v)
                {
                    if (v.TryGetValue<int>(out var i))
                    {
                        value = i;
                        return true;
                    }

                    if (v.TryGetValue<long>(out var l))
                    {
                        value = (int)l;
                        return true;
                    }

                    if (v.TryGetValue<decimal>(out var d))
                    {
                        value = (int)d;
                        return true;
                    }

                    if (v.TryGetValue<string>(out var s) &&
                        int.TryParse(s, out var p))
                    {
                        value = p;
                        return true;
                    }
                }
            }
            catch
            {
            }

            var str = node.ToString();

            return int.TryParse(str, out value);
        }

        private static bool TryGetDecimal(
            JsonObject obj,
            string prop,
            out decimal value)
        {
            value = default;

            if (!obj.TryGetPropertyValue(
                    prop,
                    out var node) ||
                node == null)
            {
                return false;
            }

            try
            {
                if (node is JsonValue v)
                {
                    if (v.TryGetValue<decimal>(out var d))
                    {
                        value = d;
                        return true;
                    }

                    if (v.TryGetValue<double>(out var db))
                    {
                        value = (decimal)db;
                        return true;
                    }

                    if (v.TryGetValue<int>(out var i))
                    {
                        value = i;
                        return true;
                    }

                    if (v.TryGetValue<long>(out var l))
                    {
                        value = l;
                        return true;
                    }

                    if (v.TryGetValue<string>(out var s) &&
                        decimal.TryParse(s, out var p))
                    {
                        value = p;
                        return true;
                    }
                }
            }
            catch
            {
            }

            var str = node.ToString();

            return decimal.TryParse(str, out value);
        }

        // ============================================================
        // CONSULTAR LINEAS DE OV EN SAP
        // ============================================================
        private async Task<List<SapOrderLine>> GetOrderLinesFromSapAsync(
            int orderDocEntry)
        {
            var endpoint =
                $"Orders({orderDocEntry})?$select=DocEntry,DocumentLines";

            var g = await _sap.GetAsync(endpoint);

            if (!g.ok ||
                string.IsNullOrWhiteSpace(g.response))
            {
                throw new Exception(
                    "No se pudo leer la OV desde SAP Service Layer. " +
                    $"orderDocEntry={orderDocEntry} | " +
                    $"endpoint={endpoint} | " +
                    $"error={g.error} | " +
                    $"response={g.response}");
            }

            using var doc =
                JsonDocument.Parse(g.response);

            var root = doc.RootElement;

            var list = new List<SapOrderLine>();

            if (!root.TryGetProperty(
                    "DocumentLines",
                    out var arr) ||
                arr.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var it in arr.EnumerateArray())
            {
                var lineNum =
                    it.TryGetProperty(
                        "LineNum",
                        out var pLineNum) &&
                    pLineNum.ValueKind == JsonValueKind.Number
                        ? pLineNum.GetInt32()
                        : -1;

                if (lineNum < 0)
                    continue;

                var item =
                    it.TryGetProperty(
                        "ItemCode",
                        out var pItem) &&
                    pItem.ValueKind == JsonValueKind.String
                        ? (pItem.GetString() ?? "").Trim()
                        : "";

                decimal openQ = 0m;

                if (it.TryGetProperty(
                        "OpenQty",
                        out var pOpen1) &&
                    pOpen1.ValueKind == JsonValueKind.Number)
                {
                    openQ = pOpen1.GetDecimal();
                }
                else if (it.TryGetProperty(
                             "OpenQuantity",
                             out var pOpen2) &&
                         pOpen2.ValueKind == JsonValueKind.Number)
                {
                    openQ = pOpen2.GetDecimal();
                }
                else if (it.TryGetProperty(
                             "RemainingOpenQuantity",
                             out var pOpen3) &&
                         pOpen3.ValueKind == JsonValueKind.Number)
                {
                    openQ = pOpen3.GetDecimal();
                }

                decimal qty = 0m;

                if (it.TryGetProperty(
                        "Quantity",
                        out var pQty) &&
                    pQty.ValueKind == JsonValueKind.Number)
                {
                    qty = pQty.GetDecimal();
                }

                list.Add(new SapOrderLine
                {
                    LineNum = lineNum,
                    ItemCode = item,
                    OpenQuantity = openQ,
                    Quantity = qty
                });
            }

            return list;
        }

        // ============================================================
        // RESOLVER BASELINE POR SKU
        // ============================================================
        private static int ResolveBaseLineBySku(
            string itemCode,
            decimal qty,
            List<SapOrderLine> orderLines)
        {
            itemCode = (itemCode ?? "").Trim();

            var candidates = orderLines
                .Where(x =>
                    string.Equals(
                        x.ItemCode,
                        itemCode,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.OpenQuantity)
                .ThenBy(x => x.LineNum)
                .ToList();

            if (candidates.Count == 0)
            {
                throw new Exception(
                    $"La OV no contiene el SKU '{itemCode}'.");
            }

            var hit = candidates.FirstOrDefault(
                x => x.OpenQuantity >= qty &&
                     x.OpenQuantity > 0);

            if (hit != null)
                return hit.LineNum;

            var openTotal = candidates.Sum(
                x => x.OpenQuantity);

            throw new Exception(
                $"No hay saldo suficiente en OV para SKU '{itemCode}'. " +
                $"OpenQuantity total={openTotal}, requerido={qty}.");
        }

        private sealed class SapOrderLine
        {
            public int LineNum { get; set; }

            public string ItemCode { get; set; } = "";

            public decimal OpenQuantity { get; set; }

            public decimal Quantity { get; set; }
        }

        // ============================================================
        // FACTURA DE RESERVA MANUAL
        // LOGICA EXISTENTE CONSERVADA
        // ============================================================
        private string BuildFacturaReservaManualJson(
            string entregaJson,
            List<ManualReserveLineDto> lineas)
        {
            var entrega =
                JsonNode.Parse(entregaJson)!.AsObject();

            var entregaLines =
                entrega["DocumentLines"]!.AsArray();

            var facturaLines = new JsonArray();

            foreach (var l in lineas)
            {
                if (l.Cantidad <= 0)
                    continue;

                var src = entregaLines.FirstOrDefault(
                    x => x!["BaseLine"]!.GetValue<int>()
                         == l.BaseLine);

                if (src == null)
                {
                    throw new Exception(
                        $"No existe BaseLine {l.BaseLine} en la entrega.");
                }

                var maxQty =
                    src["Quantity"]!.GetValue<decimal>();

                if (l.Cantidad > maxQty)
                {
                    throw new Exception(
                        $"Cantidad excede la entrega. " +
                        $"BaseLine={l.BaseLine}, " +
                        $"Max={maxQty}, Req={l.Cantidad}");
                }

                facturaLines.Add(new JsonObject
                {
                    ["ItemCode"] =
                        src["ItemCode"]!.GetValue<string>(),

                    ["Quantity"] = l.Cantidad,

                    ["BaseType"] = 17,

                    ["BaseEntry"] =
                        src["BaseEntry"]!.GetValue<int>(),

                    ["BaseLine"] = l.BaseLine
                });
            }

            if (facturaLines.Count == 0)
            {
                throw new Exception(
                    "No hay lineas validas para facturar.");
            }

            var factura = new JsonObject
            {
                ["CardCode"] =
                    entrega["CardCode"]!.GetValue<string>(),

                ["NumAtCard"] =
                    entrega["NumAtCard"]!.GetValue<string>(),

                ["Comments"] =
                    $"Factura reserva manual SIGO [{DateTime.Now:dd/MM/yyyy HH:mm:ss}]",

                ["ReserveInvoice"] = "tYES",

                ["DocumentLines"] = facturaLines
            };

            return factura.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = false
                });
        }

        public async Task<string> BuildReserveInvoiceJsonManualAsync(
            string referencia,
            string source,
            List<ManualReserveLineDto> lineas)
        {
            var entregaJson =
                await BuildJsonAsync(referencia, source);

            return BuildFacturaReservaManualJson(
                entregaJson,
                lineas);
        }

        public Task<int?> TryGetEntregaDocEntryAsync(
            string referencia,
            string source)
        {
            return Task.FromResult<int?>(null);
        }
    }
}
