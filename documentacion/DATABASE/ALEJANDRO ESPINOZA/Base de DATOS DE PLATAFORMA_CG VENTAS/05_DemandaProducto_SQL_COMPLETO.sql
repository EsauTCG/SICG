-- ============================================================
-- SCRIPT COMPLETO: Demanda Automática desde VentasHistoricas
-- Ejecutar en orden, todo de una vez
-- ============================================================

-- ============================================================
-- PASO 1: TABLAS
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = 'DemandaProducto' AND type = 'U')
CREATE TABLE [dbo].[DemandaProducto] (
    [Id]             INT           NOT NULL IDENTITY(1,1),
    [ProductoCodigo] NVARCHAR(50)  NOT NULL,
    [ProductoNombre] NVARCHAR(300) NULL,
    [Demanda]        NVARCHAR(10)  NOT NULL,      -- BAJA | MEDIA | ALTA
    [KgTotales]      DECIMAL(18,3) NOT NULL DEFAULT 0,
    [CajasTotales]   INT           NOT NULL DEFAULT 0,
    [Pedidos]        INT           NOT NULL DEFAULT 0,
    [PeriodoDias]    INT           NOT NULL DEFAULT 90,
    [FechaDesde]     DATE          NOT NULL,
    [FechaHasta]     DATE          NOT NULL,
    [Temporada]      NVARCHAR(20)  NULL,
    [UmbralBaja]     DECIMAL(18,3) NULL,
    [UmbralAlta]     DECIMAL(18,3) NULL,
    [FechaCalculo]   DATETIME2(0)  NOT NULL DEFAULT GETDATE(),
    [CalcPor]        NVARCHAR(150) NULL,
    CONSTRAINT [PK_DemandaProducto] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_DemandaProducto_SKU] UNIQUE ([ProductoCodigo])
);
ELSE
BEGIN
    -- Si la tabla ya existe, agregar columna Pedidos si no existe
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
        WHERE TABLE_NAME = 'DemandaProducto' AND COLUMN_NAME = 'Pedidos'
    )
    ALTER TABLE [dbo].[DemandaProducto] ADD [Pedidos] INT NOT NULL DEFAULT 0;
END;

GO

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = 'DemandaUmbral' AND type = 'U')
CREATE TABLE [dbo].[DemandaUmbral] (
    [Id]           INT           NOT NULL IDENTITY(1,1),
    [PeriodoDias]  INT           NOT NULL DEFAULT 90,
    [UmbralBaja]   DECIMAL(18,3) NOT NULL,
    [UmbralAlta]   DECIMAL(18,3) NOT NULL,
    [FechaCalculo] DATETIME2(0)  NOT NULL DEFAULT GETDATE(),
    [TotalSkus]    INT           NOT NULL DEFAULT 0,
    CONSTRAINT [PK_DemandaUmbral] PRIMARY KEY ([Id])
);

GO

-- ============================================================
-- PASO 2: STORED PROCEDURE
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[sp_RecalcularDemanda]
    @PeriodoDias INT = 30,
    @CalcPor NVARCHAR(150) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FechaHoy DATE = CAST(GETDATE() AS DATE);
    DECLARE @FechaDesdeConHoy DATE = DATEADD(DAY, -29, @FechaHoy);
    DECLARE @FechaDesdeSinHoy DATE = DATEADD(DAY, -30, @FechaHoy);
    DECLARE @Temporada NVARCHAR(20) =
        'Q' + CAST(DATEPART(QUARTER, GETDATE()) AS NVARCHAR(10))
        + '-' + CAST(YEAR(GETDATE()) AS NVARCHAR(10));

    ;WITH VentasBase AS (
        SELECT
            SKU AS ProductoCodigo,
            MAX(Producto) AS ProductoNombre,
            CAST(FechaVenta AS DATE) AS FechaVenta,
            SUM(ISNULL(Peso, 0)) AS KgDia,
            COUNT(DISTINCT DOC) AS PedidosDia
        FROM [dbo].[VentasHistoricas]
        WHERE SKU IS NOT NULL
          AND SKU <> ''
          AND ISNULL(Clasificacion, '') NOT LIKE '%SIN CLASIFICACION%'
          AND CAST(FechaVenta AS DATE) >= @FechaDesdeSinHoy
          AND CAST(FechaVenta AS DATE) <= @FechaHoy
        GROUP BY SKU, CAST(FechaVenta AS DATE)
    ),
    TieneVentaHoy AS (
        SELECT
            ProductoCodigo,
            MAX(ProductoNombre) AS ProductoNombre,
            MAX(CASE WHEN FechaVenta = @FechaHoy THEN 1 ELSE 0 END) AS TieneVentaHoy
        FROM VentasBase
        GROUP BY ProductoCodigo
    ),
    Ventas30Dias AS (
        SELECT
            v.ProductoCodigo,
            MAX(v.ProductoNombre) AS ProductoNombre,
            SUM(v.KgDia) AS KgTotales,
            SUM(v.PedidosDia) AS Pedidos,
            t.TieneVentaHoy
        FROM VentasBase v
        INNER JOIN TieneVentaHoy t
            ON t.ProductoCodigo = v.ProductoCodigo
        WHERE
            (t.TieneVentaHoy = 1 AND v.FechaVenta >= @FechaDesdeConHoy AND v.FechaVenta <= @FechaHoy)
            OR
            (t.TieneVentaHoy = 0 AND v.FechaVenta >= @FechaDesdeSinHoy AND v.FechaVenta < @FechaHoy)
        GROUP BY
            v.ProductoCodigo,
            t.TieneVentaHoy
    ),
    InventarioActual AS (
        SELECT
            ProductoCodigo,
            SUM(ISNULL(Kg, 0)) AS KgInventarioActual
        FROM [dbo].[InventarioSigo]
        GROUP BY ProductoCodigo
    ),
    Base AS (
        SELECT
            v.ProductoCodigo,
            v.ProductoNombre,
            v.KgTotales,
            v.Pedidos,
            ISNULL(i.KgInventarioActual, 0) AS KgInventarioActual,
            30 AS DiasConsiderados,
            t.TieneVentaHoy
        FROM Ventas30Dias v
        INNER JOIN TieneVentaHoy t
            ON t.ProductoCodigo = v.ProductoCodigo
        LEFT JOIN InventarioActual i
            ON i.ProductoCodigo = v.ProductoCodigo
    ),
    Clasificados AS (
        SELECT
            ProductoCodigo,
            ProductoNombre,
            KgTotales,
            Pedidos,
            KgInventarioActual,
            DiasConsiderados,
            TieneVentaHoy,
            CASE
                WHEN KgTotales <= 0 OR KgInventarioActual <= 0 THEN NULL
                ELSE CAST(KgInventarioActual / (KgTotales / CAST(30 AS DECIMAL(18,4))) AS DECIMAL(18,4))
            END AS DiasInventario,
            CASE
                WHEN KgTotales <= 0 OR KgInventarioActual <= 0 THEN 'BAJA'
                WHEN KgInventarioActual / (KgTotales / CAST(30 AS DECIMAL(18,4))) <= 7 THEN 'ALTA'
                WHEN KgInventarioActual / (KgTotales / CAST(30 AS DECIMAL(18,4))) <= 15 THEN 'MEDIA'
                ELSE 'BAJA'
            END AS Demanda
        FROM Base
    )
    MERGE [dbo].[DemandaProducto] AS target
    USING Clasificados AS source
        ON target.ProductoCodigo = source.ProductoCodigo
    WHEN MATCHED THEN
        UPDATE SET
            target.Demanda = source.Demanda,
            target.KgTotales = source.KgTotales,
            target.Pedidos = source.Pedidos,
            target.PeriodoDias = 30,
            target.FechaDesde = CASE WHEN source.TieneVentaHoy = 1 THEN @FechaDesdeConHoy ELSE @FechaDesdeSinHoy END,
            target.FechaHasta = CASE WHEN source.TieneVentaHoy = 1 THEN @FechaHoy ELSE DATEADD(DAY, -1, @FechaHoy) END,
            target.Temporada = @Temporada,
            target.UmbralBaja = 15,
            target.UmbralAlta = 7,
            target.FechaCalculo = GETDATE(),
            target.CalcPor = @CalcPor,
            target.ProductoNombre = source.ProductoNombre
    WHEN NOT MATCHED THEN
        INSERT (
            ProductoCodigo,
            ProductoNombre,
            Demanda,
            KgTotales,
            Pedidos,
            PeriodoDias,
            FechaDesde,
            FechaHasta,
            Temporada,
            UmbralBaja,
            UmbralAlta,
            FechaCalculo,
            CalcPor
        )
        VALUES (
            source.ProductoCodigo,
            source.ProductoNombre,
            source.Demanda,
            source.KgTotales,
            source.Pedidos,
            30,
            CASE WHEN source.TieneVentaHoy = 1 THEN @FechaDesdeConHoy ELSE @FechaDesdeSinHoy END,
            CASE WHEN source.TieneVentaHoy = 1 THEN @FechaHoy ELSE DATEADD(DAY, -1, @FechaHoy) END,
            @Temporada,
            15,
            7,
            GETDATE(),
            @CalcPor
        );

    INSERT INTO [dbo].[DemandaUmbral]
        (PeriodoDias, UmbralBaja, UmbralAlta, FechaCalculo, TotalSkus)
    SELECT
        30,
        15,
        7,
        GETDATE(),
        COUNT(*)
    FROM [dbo].[DemandaProducto];

    SELECT
        COUNT(*) AS TotalSkus,
        SUM(CASE WHEN Demanda = 'BAJA' THEN 1 ELSE 0 END) AS TotalBaja,
        SUM(CASE WHEN Demanda = 'MEDIA' THEN 1 ELSE 0 END) AS TotalMedia,
        SUM(CASE WHEN Demanda = 'ALTA' THEN 1 ELSE 0 END) AS TotalAlta,
        CAST(15 AS DECIMAL(18,2)) AS UmbralBaja,
        CAST(7 AS DECIMAL(18,2)) AS UmbralAlta,
        MIN(FechaDesde) AS FechaDesde,
        MAX(FechaHasta) AS FechaHasta,
        @Temporada AS Temporada
    FROM [dbo].[DemandaProducto];
END
GO

-- ============================================================
-- PASO 3: VISTA POR TEMPORADA (corregida)
-- ============================================================

DROP VIEW IF EXISTS [dbo].[vw_DemandaTemporada];
GO

CREATE VIEW [dbo].[vw_DemandaTemporada] AS
    SELECT
        SKU                                                        AS ProductoCodigo,
        MAX(Nombre) AS ProductoNombre,
        YEAR(FechaVenta)                                           AS Anio,
        DATEPART(QUARTER, FechaVenta)                              AS Trimestre,
        'Q' + CAST(DATEPART(QUARTER, FechaVenta) AS VARCHAR)
            + '-' + CAST(YEAR(FechaVenta) AS VARCHAR)              AS Temporada,
        CASE
            WHEN Clasificacion LIKE 'ACTIVO%'      THEN 'ACTIVO'
            WHEN Clasificacion LIKE 'ESTRATEGICO%' THEN 'ESTRATEGICO'
            WHEN Clasificacion LIKE 'SPOT%'        THEN 'SPOT'
            WHEN Clasificacion LIKE 'DESARROLLO%'  THEN 'ACTIVO'
            WHEN Clasificacion LIKE 'DETALLE%'     THEN 'SPOT'
            WHEN Clasificacion LIKE 'INACTIVO%'    THEN 'SPOT'
            ELSE 'SPOT'
        END                                                        AS Canal,
        SUM(ISNULL(Peso, 0))                                       AS KgTotales,
        SUM(ISNULL(Importe, 0))                                    AS ImporteTotal,
        COUNT(DISTINCT DOC)                                        AS NumeroPedidos,
        COUNT(DISTINCT ClienteID)                                  AS NumeroClientes
    FROM [dbo].[VentasHistoricas]
    WHERE SKU IS NOT NULL AND SKU <> ''
    GROUP BY
        SKU,
        Producto,
        Nombre,
        YEAR(FechaVenta),
        DATEPART(QUARTER, FechaVenta),
        Clasificacion;
GO

-- ============================================================
-- PASO 4: EJECUTAR EL PRIMER CÁLCULO
-- ============================================================

EXEC [dbo].[sp_RecalcularDemanda] @PeriodoDias = 90, @CalcPor = 'Sistema';
GO

-- ============================================================
-- VERIFICACIÓN: revisar resultados
-- ============================================================

-- Resumen de demanda
SELECT
    Demanda,
    COUNT(*)          AS TotalSkus,
    SUM(KgTotales)    AS KgTotalesGlobal,
    MIN(KgTotales)    AS KgMin,
    MAX(KgTotales)    AS KgMax,
    MAX(UmbralBaja)   AS UmbralBaja,
    MAX(UmbralAlta)   AS UmbralAlta
FROM [dbo].[DemandaProducto]
GROUP BY Demanda
ORDER BY Demanda;

-- Top 20 productos más vendidos
SELECT TOP 20
    ProductoCodigo,
    ProductoNombre,
    Demanda,
    KgTotales,
    Pedidos,
    FechaDesde,
    FechaHasta
FROM [dbo].[DemandaProducto]
ORDER BY KgTotales DESC;

-- Comportamiento histórico por temporada
SELECT TOP 50 *
FROM [dbo].[vw_DemandaTemporada]
ORDER BY Anio DESC, Trimestre DESC, KgTotales DESC;


SELECT * FROM [DemandaProducto]


SELECT * FROM vw_DemandaTemporada 



