using Dapper;
using Plataforma_CG.Models.ReporteRetrabajo;
using System.Data.SqlClient;

namespace Plataforma_CG.Services.ReporteRetrabajo
{
    public class RetrabajosService
    {
        private readonly string _connectionString;

        public RetrabajosService(IConfiguration configuration)
        {
            _connectionString =
                configuration.GetConnectionString("CadenaMeatTIF")
                ?? throw new InvalidOperationException(
                    "No se encontró la conexión de PyMeat.");
        }

        // Consulta base compartida entre resumen y detalle.
        private const string SqlBase = @"
WITH Catalogo AS
(
    SELECT SKU
    FROM (VALUES
        ('Y056'), ('Y059'), ('Y061'), ('Y066'),
        ('Y072'), ('Y092'), ('Y093'), ('Y094'),
        ('Y095'), ('Y096'), ('Y097'), ('Y098'),
        ('Y099'), ('Y101'), ('Y102'), ('Y103'),
        ('Y104'), ('Y112'), ('Y113')
    ) AS C(SKU)
),
Solicitudes AS
(
    SELECT DISTINCT
        PL.SolicitudProduccionId
    FROM ProduccionLogistica PL
    INNER JOIN Produccion E
        ON E.ProduccionId = PL.ProduccionId
    INNER JOIN Catalogo C
        ON C.SKU = E.Articulo
    WHERE (@SKUEntrada IS NULL
           OR E.Articulo = @SKUEntrada)
      AND EXISTS
      (
          SELECT 1
          FROM Produccion P
          WHERE P.LoteId = PL.SolicitudProduccionId
            AND P.FechaProduccion >= @FechaInicio
            AND P.FechaProduccion < DATEADD(DAY, 1, @FechaFin)
      )
),
   
Entradas AS
(
    SELECT DISTINCT
        'ENTRADA' AS TipoMovimiento,
        PL.SolicitudProduccionId,
        P.ProduccionId,
        P.FechaProduccion AS Fecha,
        P.CodigoEtiqueta,
        P.Articulo AS SKU,
        P.PesoNeto AS Kg,
        P.LoteId,
        P.Almacen AS AlmacenId,
        P.Estatus
    FROM Solicitudes S
    INNER JOIN ProduccionLogistica PL
        ON PL.SolicitudProduccionId = S.SolicitudProduccionId
    INNER JOIN Produccion P
        ON P.ProduccionId = PL.ProduccionId
),
Salidas AS
(
    SELECT
        'SALIDA' AS TipoMovimiento,
        S.SolicitudProduccionId,
        P.ProduccionId,
        P.FechaProduccion AS Fecha,
        P.CodigoEtiqueta,
        P.Articulo AS SKU,
        P.PesoNeto AS Kg,
        P.LoteId,
        P.Almacen AS AlmacenId,
        P.Estatus
    
FROM Solicitudes S
INNER JOIN Produccion P
    ON P.LoteId = S.SolicitudProduccionId
WHERE P.FechaProduccion >= @FechaInicio
  AND P.FechaProduccion < DATEADD(DAY, 1, @FechaFin)
),
Movimientos AS
(
    SELECT * FROM Entradas
    UNION ALL
    SELECT * FROM Salidas
)
";

        public async Task<IEnumerable<ResumenRetrabajo>>
            ConsultarResumenAsync(
                DateTime fechaInicio,
                DateTime fechaFin,
                string? skuEntrada)
        {
            string sql = SqlBase + @"
SELECT
    M.SolicitudProduccionId,
    M.TipoMovimiento,
    M.SKU,
    A.Nombre AS Producto,
    COUNT(*) AS CantidadEtiquetas,
    CAST(SUM(M.Kg) AS DECIMAL(18,3)) AS TotalKg
FROM Movimientos M
LEFT JOIN TIF_CommerciaNet.dbo.Articulo A
    ON A.ArticuloId = M.SKU
GROUP BY
    M.SolicitudProduccionId,
    M.TipoMovimiento,
    M.SKU,
    A.Nombre
ORDER BY
    M.SolicitudProduccionId,
    M.TipoMovimiento,
    M.SKU;";

            using var conexion =
                new SqlConnection(_connectionString);

            return await conexion.QueryAsync<ResumenRetrabajo>(
                sql,
                new
                {
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    SKUEntrada = skuEntrada
                },
                commandTimeout: 120);
        }

        public async Task<IEnumerable<MovimientoRetrabajo>>
            ConsultarDetalleAsync(
                DateTime fechaInicio,
                DateTime fechaFin,
                string? skuEntrada,
                int solicitudId,
                string tipoMovimiento,
                string sku,
                DateTime? fechaSalida = null)
        {
            string sql = SqlBase + @"
SELECT
    M.TipoMovimiento,
    M.SolicitudProduccionId,
    M.ProduccionId,
    M.Fecha,
    M.CodigoEtiqueta,
    M.SKU,
    A.Nombre AS Producto,
    CAST(M.Kg AS DECIMAL(18,3)) AS Kg,
    L.Nombre AS Lote,
    AL.Nombre AS Almacen,
    CONVERT(VARCHAR(20), M.Estatus) AS Estatus,
    CASE
        WHEN M.TipoMovimiento = 'ENTRADA'
             AND C.SKU IS NOT NULL
            THEN 'ENTRADA_EXCEL'
        WHEN M.TipoMovimiento = 'ENTRADA'
            THEN 'ENTRADA_ADICIONAL'
        ELSE 'SALIDA_ASOCIADA'
    END AS Clasificacion
FROM Movimientos M
LEFT JOIN TIF_CommerciaNet.dbo.Articulo A
    ON A.ArticuloId = M.SKU
LEFT JOIN Lote L
    ON L.LoteId = M.LoteId
LEFT JOIN TIF_CommerciaNet.dbo.Almacen AL
    ON AL.AlmacenId = M.AlmacenId
LEFT JOIN Catalogo C
    ON C.SKU = M.SKU
WHERE M.SolicitudProduccionId = @SolicitudId
  AND M.TipoMovimiento = @TipoMovimiento
  AND M.SKU = @SKU
AND (
    @FechaSalida IS NULL
    OR (
        M.TipoMovimiento = 'SALIDA'
        AND M.Fecha >= @FechaSalida
        AND M.Fecha < DATEADD(DAY, 1, @FechaSalida)
    )
)
ORDER BY M.Fecha, M.ProduccionId;";

            using var conexion =
                new SqlConnection(_connectionString);

            return await conexion.QueryAsync<MovimientoRetrabajo>(
                sql,
                new
                {
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    SKUEntrada = skuEntrada,
                    SolicitudId = solicitudId,
                    TipoMovimiento = tipoMovimiento,
                    SKU = sku,
                    fechaSalida = fechaSalida?.Date
                },
                commandTimeout: 120);
        }

        private const string SqlIndicadoresSalida = @"
WITH Catalogo AS
(
    SELECT SKU
    FROM (VALUES
        ('Y056'), ('Y059'), ('Y061'), ('Y066'),
        ('Y072'), ('Y092'), ('Y093'), ('Y094'),
        ('Y095'), ('Y096'), ('Y097'), ('Y098'),
        ('Y099'), ('Y101'), ('Y102'), ('Y103'),
        ('Y104'), ('Y112'), ('Y113')
    ) AS C(SKU)
),
Solicitudes AS
(
    SELECT DISTINCT
        PL.SolicitudProduccionId
    FROM ProduccionLogistica PL
    INNER JOIN Produccion E
        ON E.ProduccionId = PL.ProduccionId
    INNER JOIN Catalogo C
        ON C.SKU = E.Articulo
    WHERE @SKUEntrada IS NULL
       OR E.Articulo = @SKUEntrada
),
Salidas AS
(
    SELECT
        S.SolicitudProduccionId,
        P.ProduccionId,
        CONVERT(DATE, P.FechaProduccion) AS Fecha,
        P.Articulo AS SKU,
        P.PesoNeto AS Kg
    FROM Solicitudes S
    INNER JOIN Produccion P
        ON P.LoteId = S.SolicitudProduccionId
    WHERE P.FechaProduccion >= @FechaInicio
      AND P.FechaProduccion < DATEADD(DAY, 1, @FechaFin)
)
SELECT
    S.Fecha,
    S.SKU,
    A.Nombre AS Producto,
    COUNT(*) AS CantidadEtiquetas,
    CAST(SUM(S.Kg) AS DECIMAL(18,3)) AS TotalKg
FROM Salidas S
LEFT JOIN TIF_CommerciaNet.dbo.Articulo A
    ON A.ArticuloId = S.SKU
GROUP BY
    S.Fecha,
    S.SKU,
    A.Nombre
ORDER BY
    S.Fecha,
    S.SKU;";


        public async Task<IEnumerable<IndicadorSalidaRetrabajo>>
            ConsultarIndicadoresSalidaAsync(
                DateTime fechaInicio,
                DateTime fechaFin,
                string? skuEntrada)
        {
            using var conexion =
                new SqlConnection(_connectionString);

            return await conexion.QueryAsync<IndicadorSalidaRetrabajo>(
                SqlIndicadoresSalida,
                new
                {
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    SKUEntrada = skuEntrada
                },
                commandTimeout: 120);
        }


        private const string SqlEntradasAsociadas = @"
WITH Catalogo AS
(
    SELECT SKU
    FROM (VALUES
        ('Y056'), ('Y059'), ('Y061'), ('Y066'),
        ('Y072'), ('Y092'), ('Y093'), ('Y094'),
        ('Y095'), ('Y096'), ('Y097'), ('Y098'),
        ('Y099'), ('Y101'), ('Y102'), ('Y103'),
        ('Y104'), ('Y112'), ('Y113')
    ) AS C(SKU)
),
SolicitudesConEntrada AS
(
    SELECT DISTINCT
        PL.SolicitudProduccionId
    FROM ProduccionLogistica PL
    INNER JOIN Produccion E
        ON E.ProduccionId = PL.ProduccionId
    INNER JOIN Catalogo C
        ON C.SKU = E.Articulo
    WHERE @SKUEntrada IS NULL
       OR E.Articulo = @SKUEntrada
),
SolicitudesConSalidaPeriodo AS
(
    SELECT DISTINCT
        S.SolicitudProduccionId
    FROM SolicitudesConEntrada S
    INNER JOIN Produccion P
        ON P.LoteId = S.SolicitudProduccionId
    WHERE P.FechaProduccion >= @FechaInicio
      AND P.FechaProduccion < DATEADD(DAY, 1, @FechaFin)
),
Entradas AS
(
    SELECT DISTINCT
        PL.SolicitudProduccionId,
        E.ProduccionId,
        E.FechaProduccion,
        E.Articulo AS SKU,
        E.PesoNeto AS Kg
    FROM SolicitudesConSalidaPeriodo S
    INNER JOIN ProduccionLogistica PL
        ON PL.SolicitudProduccionId = S.SolicitudProduccionId
    INNER JOIN Produccion E
        ON E.ProduccionId = PL.ProduccionId
    INNER JOIN Catalogo C
        ON C.SKU = E.Articulo
)
SELECT
    E.SolicitudProduccionId,
    'ENTRADA' AS TipoMovimiento,
    E.SKU,
    A.Nombre AS Producto,
    COUNT(*) AS CantidadEtiquetas,
    CAST(SUM(E.Kg) AS DECIMAL(18,3)) AS TotalKg
FROM Entradas E
LEFT JOIN TIF_CommerciaNet.dbo.Articulo A
    ON A.ArticuloId = E.SKU
GROUP BY
    E.SolicitudProduccionId,
    E.SKU,
    A.Nombre
ORDER BY
    E.SolicitudProduccionId,
    E.SKU;";


        public async Task<IEnumerable<ResumenRetrabajo>>
            ConsultarEntradasAsociadasAsync(
                DateTime fechaInicio,
                DateTime fechaFin,
                string? skuEntrada)
        {
            using var conexion =
                new SqlConnection(_connectionString);

            return await conexion.QueryAsync<ResumenRetrabajo>(
                SqlEntradasAsociadas,
                new
                {
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    SKUEntrada = skuEntrada
                },
                commandTimeout: 120);
        }


        public async Task<ReporteViscerasResultado>
            ConsultarReporteViscerasAsync(
                DateTime fechaInicio,
                DateTime fechaFin,
                string? skuEntrada)
        {
            const string sql = @"
SET NOCOUNT ON;

-- Catálogo inicial de entradas del Excel
CREATE TABLE #Catalogo (
    SKU VARCHAR(50) PRIMARY KEY
);

INSERT INTO #Catalogo (SKU)
VALUES
('Y056'), ('Y059'), ('Y061'), ('Y066'),
('Y072'), ('Y092'), ('Y093'), ('Y094'),
('Y095'), ('Y096'), ('Y097'), ('Y098'),
('Y099'), ('Y101'), ('Y102'), ('Y103'),
('Y104'), ('Y112'), ('Y113');

-- Solicitudes que contienen entradas conocidas
-- y al menos una salida dentro del periodo
SELECT DISTINCT
    PL.SolicitudProduccionId
INTO #Solicitudes
FROM ProduccionLogistica PL
INNER JOIN Produccion E
    ON E.ProduccionId = PL.ProduccionId
INNER JOIN #Catalogo C
    ON C.SKU = E.Articulo
WHERE (@SKUEntrada IS NULL
       OR E.Articulo = @SKUEntrada)
  AND EXISTS (
      SELECT 1
      FROM Produccion S
      WHERE S.LoteId = PL.SolicitudProduccionId
        AND S.FechaProduccion >= @FechaInicio
        AND S.FechaProduccion < DATEADD(DAY, 1, @FechaFin)
  );

-- Todas las entradas del catálogo en esas solicitudes.
-- Una etiqueta se cuenta una sola vez por solicitud.
SELECT DISTINCT
    PL.SolicitudProduccionId,
    E.ProduccionId,
    CONVERT(DATE, E.FechaProduccion) AS FechaEntrada,
    E.Articulo AS SKU,
    E.PesoNeto AS Kg
INTO #Entradas
FROM #Solicitudes S
INNER JOIN ProduccionLogistica PL
    ON PL.SolicitudProduccionId =
       S.SolicitudProduccionId
INNER JOIN Produccion E
    ON E.ProduccionId = PL.ProduccionId
INNER JOIN #Catalogo C
    ON C.SKU = E.Articulo;

-- Salidas reales, cada etiqueta en su propia fecha.
SELECT
    S.SolicitudProduccionId,
    P.ProduccionId,
    CONVERT(DATE, P.FechaProduccion) AS Fecha,
    P.Articulo AS SKU,
    P.PesoNeto AS Kg
INTO #Salidas
FROM #Solicitudes S
INNER JOIN Produccion P
    ON P.LoteId = S.SolicitudProduccionId;

-- RESULTADO 1: entradas individuales por etiqueta
SELECT
    E.FechaEntrada,
    E.SolicitudProduccionId,
    E.ProduccionId,
    P.CodigoEtiqueta,
    E.SKU,
    A.Nombre AS Producto,
    1 AS CantidadEtiquetas,
    CAST(E.Kg AS DECIMAL(18,3)) AS TotalKg
FROM #Entradas E
INNER JOIN Produccion P
    ON P.ProduccionId = E.ProduccionId
LEFT JOIN TIF_CommerciaNet.dbo.Articulo A
    ON A.ArticuloId = E.SKU
ORDER BY
    E.FechaEntrada,
    E.SolicitudProduccionId,
    E.ProduccionId;

-- RESULTADO 2: salidas por fecha, solicitud y SKU
SELECT
    S.Fecha,
    S.SolicitudProduccionId,
    S.SKU,
    A.Nombre AS Producto,
    COUNT(*) AS CantidadEtiquetas,
    CAST(SUM(S.Kg) AS DECIMAL(18,3)) AS TotalKg
FROM #Salidas S
LEFT JOIN TIF_CommerciaNet.dbo.Articulo A
    ON A.ArticuloId = S.SKU
WHERE S.Fecha >= @FechaInicio
  AND S.Fecha < DATEADD(DAY, 1, @FechaFin)
GROUP BY
    S.Fecha,
    S.SolicitudProduccionId,
    S.SKU,
    A.Nombre
ORDER BY
    S.Fecha,
    S.SolicitudProduccionId,
    S.SKU;

-- RESULTADO 3: solicitudes con salidas
-- que también ocurren fuera del periodo
SELECT
    S.SolicitudProduccionId,
    MIN(S.Fecha) AS PrimeraSalida,
    MAX(S.Fecha) AS UltimaSalida
FROM #Salidas S
GROUP BY S.SolicitudProduccionId
HAVING MIN(S.Fecha) < @FechaInicio
    OR MAX(S.Fecha) > @FechaFin;

DROP TABLE #Salidas;
DROP TABLE #Entradas;
DROP TABLE #Solicitudes;
DROP TABLE #Catalogo;
";

            using var conexion = new SqlConnection(_connectionString);

            using var multi = await conexion.QueryMultipleAsync(
                sql,
                new
                {
                    FechaInicio = fechaInicio.Date,
                    FechaFin = fechaFin.Date,
                    SKUEntrada = skuEntrada
                },
                commandTimeout: 120
            );

            var entradas =
                (await multi.ReadAsync<EntradaVisceras>()).ToList();
            var salidas =
                (await multi.ReadAsync<SalidasVisceras>()).ToList();

            var advertencias =
                (await multi.ReadAsync<AdvertenciaVisceras>()).ToList();

            return new ReporteViscerasResultado
            {
                Entradas = entradas,
                Salidas = salidas,
                Advertencias = advertencias
            };
        }

    }
}
