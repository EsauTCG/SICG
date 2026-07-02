USE [SIGO]
GO

/* ============================================================
   INVENTARIO INICIAL EJECUTIVO - VERSIÓN CORREGIDA
   ------------------------------------------------------------
   CORRECCIÓN PRINCIPAL:
   - Catálogo MASTER/SKU: sale de dbo.ArticuloSAP.U_MASTER.
   - Inventario inicial: sale de dbo.InventarioAlmacenado_Meat
     filtrado por @FechaInicio.
   - Ya NO se usa InventarioInicialStockResumen para calcular
     inventario inicial, porque acumulaba histórico completo.
   ============================================================ */


/* ============================================================
   1. COLUMNAS NORMALIZADAS E ÍNDICES
   ============================================================ */

IF COL_LENGTH('dbo.ArticuloSAP', 'ProductoCodigoNorm') IS NULL
BEGIN
    ALTER TABLE dbo.ArticuloSAP
    ADD ProductoCodigoNorm AS CONVERT(VARCHAR(50), UPPER(LTRIM(RTRIM(ISNULL(ProductoCodigo, ''))))) PERSISTED;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_ArticuloSAP_ProductoCodigoNorm'
      AND object_id = OBJECT_ID('dbo.ArticuloSAP')
)
BEGIN
    CREATE INDEX IX_ArticuloSAP_ProductoCodigoNorm
    ON dbo.ArticuloSAP (ProductoCodigoNorm)
    INCLUDE (ProductoNombre, U_MASTER);
END
GO


IF COL_LENGTH('dbo.InventarioAlmacenado_Meat', 'SkuNorm') IS NULL
BEGIN
    ALTER TABLE dbo.InventarioAlmacenado_Meat
    ADD SkuNorm AS CONVERT(VARCHAR(50), UPPER(LTRIM(RTRIM(ISNULL(Sku, ''))))) PERSISTED;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_InventarioAlmacenado_Meat_SkuNorm_FechaInventario'
      AND object_id = OBJECT_ID('dbo.InventarioAlmacenado_Meat')
)
BEGIN
    CREATE INDEX IX_InventarioAlmacenado_Meat_SkuNorm_FechaInventario
    ON dbo.InventarioAlmacenado_Meat (SkuNorm, FechaInventario)
    INCLUDE (Articulo, PesoNeto);
END
GO


IF COL_LENGTH('dbo.OrdenVentaProducto', 'ProductoCodigoNorm') IS NULL
BEGIN
    ALTER TABLE dbo.OrdenVentaProducto
    ADD ProductoCodigoNorm AS CONVERT(VARCHAR(50), UPPER(LTRIM(RTRIM(ISNULL(ProductoCodigo, ''))))) PERSISTED;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_OrdenVentaProducto_ProductoCodigoNorm'
      AND object_id = OBJECT_ID('dbo.OrdenVentaProducto')
)
BEGIN
    CREATE INDEX IX_OrdenVentaProducto_ProductoCodigoNorm
    ON dbo.OrdenVentaProducto (ProductoCodigoNorm, Eliminado, PedidoId)
    INCLUDE (ProductoNombre, Cajas, Peso, Importe);
END
GO


IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_OrdenVenta_FechaEntrega_Id'
      AND object_id = OBJECT_ID('dbo.OrdenVenta')
)
BEGIN
    CREATE INDEX IX_OrdenVenta_FechaEntrega_Id
    ON dbo.OrdenVenta (FechaEntrega, Id)
    INCLUDE (Estatus);
END
GO


IF COL_LENGTH('dbo.PlanDiario', 'ProductoCodigoConvertidoNorm') IS NULL
BEGIN
    ALTER TABLE dbo.PlanDiario
    ADD ProductoCodigoConvertidoNorm AS CONVERT(VARCHAR(50), UPPER(LTRIM(RTRIM(ISNULL(ProductoCodigoConvertido, ''))))) PERSISTED;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_PlanDiario_ProductoCodigoConvertidoNorm'
      AND object_id = OBJECT_ID('dbo.PlanDiario')
)
BEGIN
    CREATE INDEX IX_PlanDiario_ProductoCodigoConvertidoNorm
    ON dbo.PlanDiario (ProductoCodigoConvertidoNorm, PlaneacionId)
    INCLUDE (KgInyeccion, Porcentaje, PorcentajeInyeccion);
END
GO


IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_PlaneacionProduccion_FechaPlan_PlaneacionId'
      AND object_id = OBJECT_ID('dbo.PlaneacionProduccion')
)
BEGIN
    CREATE INDEX IX_PlaneacionProduccion_FechaPlan_PlaneacionId
    ON dbo.PlaneacionProduccion (FechaPlan, PlaneacionId);
END
GO


IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_PlaneacionProduccion_PlaneacionId_FechaPlan'
      AND object_id = OBJECT_ID('dbo.PlaneacionProduccion')
)
BEGIN
    CREATE INDEX IX_PlaneacionProduccion_PlaneacionId_FechaPlan
    ON dbo.PlaneacionProduccion (PlaneacionId, FechaPlan);
END
GO


/* ============================================================
   2. TABLA CACHE SOLO PARA CATÁLOGO MASTER/SKU
   ============================================================ */

IF OBJECT_ID('dbo.InventarioInicialCatalogo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventarioInicialCatalogo
    (
        Sku VARCHAR(50) NOT NULL PRIMARY KEY,
        Articulo VARCHAR(250) NULL,
        MasterProducto VARCHAR(150) NOT NULL DEFAULT ('SIN MASTER'),
        Activo BIT NOT NULL DEFAULT (1),
        FechaActualizacion DATETIME NOT NULL DEFAULT (GETDATE())
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_InventarioInicialCatalogo_Master'
      AND object_id = OBJECT_ID('dbo.InventarioInicialCatalogo')
)
BEGIN
    CREATE INDEX IX_InventarioInicialCatalogo_Master
    ON dbo.InventarioInicialCatalogo (MasterProducto, Sku)
    INCLUDE (Articulo, Activo);
END
GO


/* ============================================================
   3. REFRESCAR CATÁLOGO MASTER/SKU
   ------------------------------------------------------------
   IMPORTANTE:
   - Este SP solo refresca catálogo.
   - No calcula inventario inicial.
   - El inventario inicial se calcula en sp_InventarioInicial
     filtrando por @FechaInicio.
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.sp_RefrescarInventarioInicialCache
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRAN;

            DELETE FROM dbo.InventarioInicialCatalogo;

            /* =====================================================
               Catálogo principal desde ArticuloSAP
               MASTER = ArticuloSAP.U_MASTER
               ===================================================== */

            INSERT INTO dbo.InventarioInicialCatalogo
            (
                Sku,
                Articulo,
                MasterProducto,
                Activo,
                FechaActualizacion
            )
            SELECT
                a.ProductoCodigoNorm AS Sku,

                MAX(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), '')) AS Articulo,

                COALESCE(
                    MAX(NULLIF(UPPER(LTRIM(RTRIM(a.U_MASTER))), '')),
                    'SIN MASTER'
                ) AS MasterProducto,

                CAST(1 AS BIT) AS Activo,
                GETDATE() AS FechaActualizacion
            FROM dbo.ArticuloSAP a WITH (NOLOCK)
            WHERE
                a.ProductoCodigoNorm IS NOT NULL
                AND a.ProductoCodigoNorm <> ''
            GROUP BY
                a.ProductoCodigoNorm;


            /* =====================================================
               Agregar SKU que existan en inventario pero no estén
               en ArticuloSAP, para que no se pierdan.
               ===================================================== */

            INSERT INTO dbo.InventarioInicialCatalogo
            (
                Sku,
                Articulo,
                MasterProducto,
                Activo,
                FechaActualizacion
            )
            SELECT
                i.SkuNorm AS Sku,

                COALESCE(
                    MAX(NULLIF(LTRIM(RTRIM(i.Articulo)), '')),
                    i.SkuNorm
                ) AS Articulo,

                'SIN MASTER' AS MasterProducto,

                CAST(1 AS BIT) AS Activo,
                GETDATE() AS FechaActualizacion
            FROM dbo.InventarioAlmacenado_Meat i WITH (NOLOCK)
            WHERE
                i.SkuNorm IS NOT NULL
                AND i.SkuNorm <> ''
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.InventarioInicialCatalogo c
                    WHERE c.Sku = i.SkuNorm
                )
            GROUP BY
                i.SkuNorm;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK;

        THROW;
    END CATCH;
END;
GO


/* ============================================================
   4. SP RÁPIDO PARA FILTROS MASTER/SKU
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.sp_InventarioInicialFiltros
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        MasterProducto,
        COUNT(DISTINCT Sku) AS TotalSkus
    FROM dbo.InventarioInicialCatalogo WITH (NOLOCK)
    WHERE Activo = 1
    GROUP BY
        MasterProducto
    ORDER BY
        CASE WHEN MasterProducto = 'SIN MASTER' THEN 1 ELSE 0 END,
        MasterProducto;

    SELECT
        Sku,
        Articulo,
        MasterProducto
    FROM dbo.InventarioInicialCatalogo WITH (NOLOCK)
    WHERE Activo = 1
    ORDER BY
        MasterProducto,
        Sku;
END;
GO


/* ============================================================
   5. SP PRINCIPAL CORREGIDO
   ------------------------------------------------------------
   Inventario inicial:
   - Sale de InventarioAlmacenado_Meat
   - Filtrado por @FechaInicio
   - Ya NO sale de InventarioInicialStockResumen
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.sp_InventarioInicial
    @Sku VARCHAR(50) = NULL,
    @SkusCsv VARCHAR(MAX) = NULL,
    @MastersCsv VARCHAR(MAX) = NULL,
    @FechaInicio DATE = NULL,
    @FechaFin DATE = NULL,
    @Dias INT = 23
AS
BEGIN
    SET NOCOUNT ON;

    IF @Dias IS NULL OR @Dias < 0
        SET @Dias = 23;

    IF @Dias > 90
        SET @Dias = 90;

    IF @FechaInicio IS NULL
        SET @FechaInicio = CAST(GETDATE() AS DATE);

    IF @FechaFin IS NULL
        SET @FechaFin = DATEADD(DAY, @Dias, @FechaInicio);

    IF @FechaFin < @FechaInicio
    BEGIN
        RAISERROR('La fecha final no puede ser menor a la fecha inicial.', 16, 1);
        RETURN;
    END;

    SET @Sku = NULLIF(UPPER(LTRIM(RTRIM(ISNULL(@Sku, '')))), '');
    SET @SkusCsv = NULLIF(UPPER(LTRIM(RTRIM(ISNULL(@SkusCsv, '')))), '');
    SET @MastersCsv = NULLIF(UPPER(LTRIM(RTRIM(ISNULL(@MastersCsv, '')))), '');

    IF @SkusCsv IS NULL AND @Sku IS NOT NULL
        SET @SkusCsv = @Sku;

    DECLARE @DiasRango INT = DATEDIFF(DAY, @FechaInicio, @FechaFin);

    CREATE TABLE #FiltroSkus
    (
        Sku VARCHAR(50) NOT NULL PRIMARY KEY
    );

    CREATE TABLE #FiltroMasters
    (
        MasterProducto VARCHAR(150) NOT NULL PRIMARY KEY
    );

    IF @SkusCsv IS NOT NULL
    BEGIN
        INSERT INTO #FiltroSkus (Sku)
        SELECT DISTINCT UPPER(LTRIM(RTRIM(value)))
        FROM STRING_SPLIT(@SkusCsv, ',')
        WHERE LTRIM(RTRIM(value)) <> '';
    END;

    IF @MastersCsv IS NOT NULL
    BEGIN
        INSERT INTO #FiltroMasters (MasterProducto)
        SELECT DISTINCT UPPER(LTRIM(RTRIM(value)))
        FROM STRING_SPLIT(@MastersCsv, ',')
        WHERE LTRIM(RTRIM(value)) <> '';
    END;

    CREATE TABLE #ProductosFiltrados
    (
        Sku VARCHAR(50) NOT NULL PRIMARY KEY,
        Articulo VARCHAR(250) NULL,
        MasterProducto VARCHAR(150) NOT NULL
    );

    INSERT INTO #ProductosFiltrados
    (
        Sku,
        Articulo,
        MasterProducto
    )
    SELECT
        c.Sku,
        c.Articulo,
        c.MasterProducto
    FROM dbo.InventarioInicialCatalogo c WITH (NOLOCK)
    WHERE
        c.Activo = 1
        AND
        (
            NOT EXISTS (SELECT 1 FROM #FiltroSkus)
            OR EXISTS
            (
                SELECT 1
                FROM #FiltroSkus fs
                WHERE fs.Sku = c.Sku
            )
        )
        AND
        (
            NOT EXISTS (SELECT 1 FROM #FiltroMasters)
            OR EXISTS
            (
                SELECT 1
                FROM #FiltroMasters fm
                WHERE fm.MasterProducto = c.MasterProducto
            )
        );

    DECLARE
        @TotalSkus INT = ISNULL((SELECT COUNT(DISTINCT Sku) FROM #ProductosFiltrados), 0),
        @TotalMasters INT = ISNULL((SELECT COUNT(DISTINCT MasterProducto) FROM #ProductosFiltrados), 0),
        @SkuResumen VARCHAR(50),
        @MasterResumen VARCHAR(150),
        @ArticuloResumen VARCHAR(250);

    SELECT
        @SkuResumen =
            CASE
                WHEN COUNT(DISTINCT Sku) = 1 THEN MAX(Sku)
                WHEN COUNT(DISTINCT Sku) = 0 THEN 'SIN SKU'
                ELSE CONCAT(COUNT(DISTINCT Sku), ' SKU')
            END,

        @MasterResumen =
            CASE
                WHEN COUNT(DISTINCT MasterProducto) = 1 THEN MAX(MasterProducto)
                WHEN COUNT(DISTINCT MasterProducto) = 0 THEN 'SIN MASTER'
                ELSE CONCAT(COUNT(DISTINCT MasterProducto), ' MASTER')
            END,

        @ArticuloResumen =
            CASE
                WHEN COUNT(DISTINCT Sku) = 1 THEN MAX(Articulo)
                WHEN COUNT(DISTINCT Sku) = 0 THEN 'SIN DATOS'
                ELSE 'CONSOLIDADO EJECUTIVO'
            END
    FROM #ProductosFiltrados;


    CREATE TABLE #Fechas
    (
        N INT NOT NULL PRIMARY KEY,
        Fecha DATE NOT NULL
    );

    ;WITH N AS
    (
        SELECT TOP (@DiasRango + 1)
            ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS N
        FROM sys.all_objects
    )
    INSERT INTO #Fechas
    (
        N,
        Fecha
    )
    SELECT
        N,
        DATEADD(DAY, N, @FechaInicio)
    FROM N
    ORDER BY
        N;


    IF @TotalSkus = 0
    BEGIN
        SELECT
            f.Fecha,
            CAST('SIN SKU' AS VARCHAR(50)) AS Sku,
            CAST('SIN MASTER' AS VARCHAR(150)) AS MasterProducto,
            CAST('SIN DATOS' AS VARCHAR(250)) AS Articulo,

            CAST(0 AS INT) AS TotalSkus,
            CAST(0 AS INT) AS TotalMasters,

            CAST(0 AS DECIMAL(18,3)) AS CajasInventario,
            CAST(0 AS DECIMAL(18,3)) AS KgInventario,
            CAST(0 AS DECIMAL(18,3)) AS KgPromedioCaja,

            CAST(0 AS DECIMAL(18,3)) AS CajasPedido,
            CAST(0 AS DECIMAL(18,3)) AS KgPedido,
            CAST(0 AS DECIMAL(18,2)) AS ImportePedido,

            CAST(0 AS DECIMAL(18,3)) AS CajasProduccion,
            CAST(0 AS DECIMAL(18,3)) AS KgProduccion,

            CAST(0 AS DECIMAL(18,3)) AS CajasDisponible,
            CAST(0 AS DECIMAL(18,3)) AS KgPedidoEstimado,
            CAST(0 AS DECIMAL(18,3)) AS KgDisponibleEstimado,
            CAST(0 AS DECIMAL(18,3)) AS KgDisponible,

            CAST(NULL AS DATE) AS PrimeraFechaEntrega,
            CAST(NULL AS DATE) AS UltimaFechaEntrega,

            CAST('SIN DATOS' AS VARCHAR(30)) AS EstatusDisponible,
            CAST(CASE WHEN f.N = 0 THEN 1 ELSE 0 END AS BIT) AS InventarioCapturado
        FROM #Fechas f
        ORDER BY
            f.Fecha;

        RETURN;
    END;


    /* ============================================================
       INVENTARIO INICIAL REAL DEL DÍA
       ------------------------------------------------------------
       AQUÍ ESTÁ LA CORRECCIÓN:
       Sólo toma inventario de @FechaInicio.
       Ejemplo:
       @FechaInicio = '2026-06-22'
       toma de:
       2026-06-22 00:00:00 hasta antes de 2026-06-23.
       ============================================================ */

    CREATE TABLE #InventarioInicial
    (
        CajasInventario DECIMAL(18,3) NOT NULL,
        KgInventario DECIMAL(18,3) NOT NULL,
        KgPromedioCaja DECIMAL(18,3) NOT NULL
    );

    INSERT INTO #InventarioInicial
    (
        CajasInventario,
        KgInventario,
        KgPromedioCaja
    )
    SELECT
        CAST(COUNT_BIG(*) AS DECIMAL(18,3)) AS CajasInventario,

        CAST(ISNULL(SUM(ISNULL(i.PesoNeto, 0)), 0) AS DECIMAL(18,3)) AS KgInventario,

        CAST(
            CASE
                WHEN COUNT_BIG(*) > 0
                    THEN ISNULL(SUM(ISNULL(i.PesoNeto, 0)), 0) / COUNT_BIG(*)
                ELSE 0
            END
            AS DECIMAL(18,3)
        ) AS KgPromedioCaja
    FROM dbo.InventarioAlmacenado_Meat i WITH (NOLOCK)
    INNER JOIN #ProductosFiltrados pf
        ON pf.Sku = i.SkuNorm
    WHERE
        i.SkuNorm IS NOT NULL
        AND i.SkuNorm <> ''
        AND i.FechaInventario >= @FechaInicio
        AND i.FechaInventario < DATEADD(DAY, 1, @FechaInicio);


    CREATE TABLE #ProduccionDia
    (
        Fecha DATE NOT NULL PRIMARY KEY,
        KgProduccion DECIMAL(18,3) NOT NULL
    );

    INSERT INTO #ProduccionDia
    (
        Fecha,
        KgProduccion
    )
    SELECT
        CAST(a.FechaPlan AS DATE) AS Fecha,
        CAST(ISNULL(SUM(ISNULL(b.KgInyeccion, 0)), 0) AS DECIMAL(18,3)) AS KgProduccion
    FROM dbo.PlaneacionProduccion a WITH (NOLOCK)
    INNER JOIN dbo.PlanDiario b WITH (NOLOCK)
        ON b.PlaneacionId = a.PlaneacionId
    INNER JOIN #ProductosFiltrados pf
        ON pf.Sku = b.ProductoCodigoConvertidoNorm
    WHERE
        a.FechaPlan >= @FechaInicio
        AND a.FechaPlan < DATEADD(DAY, 1, @FechaFin)
    GROUP BY
        CAST(a.FechaPlan AS DATE);


    CREATE TABLE #PedidosDia
    (
        Fecha DATE NOT NULL PRIMARY KEY,
        CajasPedido DECIMAL(18,3) NOT NULL,
        KgPedido DECIMAL(18,3) NOT NULL,
        ImportePedido DECIMAL(18,2) NOT NULL
    );

    INSERT INTO #PedidosDia
    (
        Fecha,
        CajasPedido,
        KgPedido,
        ImportePedido
    )
    SELECT
        CAST(o.FechaEntrega AS DATE) AS Fecha,

        CAST(ISNULL(SUM(ISNULL(p.Cajas, 0)), 0) AS DECIMAL(18,3)) AS CajasPedido,

        CAST(ISNULL(SUM(ISNULL(p.Peso, 0)), 0) AS DECIMAL(18,3)) AS KgPedido,

        CAST(ISNULL(SUM(ISNULL(p.Importe, 0)), 0) AS DECIMAL(18,2)) AS ImportePedido
    FROM dbo.OrdenVenta o WITH (NOLOCK)
    INNER JOIN dbo.OrdenVentaProducto p WITH (NOLOCK)
        ON p.PedidoId = o.Id
    INNER JOIN #ProductosFiltrados pf
        ON pf.Sku = p.ProductoCodigoNorm
    WHERE
        o.FechaEntrega >= @FechaInicio
        AND o.FechaEntrega < DATEADD(DAY, 1, @FechaFin)
        AND ISNULL(p.Eliminado, 0) = 0

        /*
          Si necesitas filtrar estatus del pedido, activa el que aplique:
          AND ISNULL(o.Estatus, 0) = 0
          AND ISNULL(o.Estatus, 0) IN (0, 1)
          AND ISNULL(o.Estatus, 0) <> 3
        */
    GROUP BY
        CAST(o.FechaEntrega AS DATE);


    DECLARE
        @PrimeraFechaEntrega DATE = (SELECT MIN(Fecha) FROM #PedidosDia),
        @UltimaFechaEntrega DATE = (SELECT MAX(Fecha) FROM #PedidosDia);


    ;WITH Base AS
    (
        SELECT
            f.N,
            f.Fecha,

            ii.CajasInventario AS CajasInventarioInicial,
            ii.KgInventario AS KgInventarioInicial,
            ii.KgPromedioCaja,

            ISNULL(pd.CajasPedido, 0) AS CajasPedido,
            ISNULL(pd.KgPedido, 0) AS KgPedido,
            ISNULL(pd.ImportePedido, 0) AS ImportePedido,

            ISNULL(pr.KgProduccion, 0) AS KgProduccion,

            CAST(
                CASE
                    WHEN ii.KgPromedioCaja > 0
                        THEN ISNULL(pr.KgProduccion, 0) / ii.KgPromedioCaja
                    ELSE 0
                END
                AS DECIMAL(18,3)
            ) AS CajasProduccion
        FROM #Fechas f
        CROSS JOIN #InventarioInicial ii
        LEFT JOIN #PedidosDia pd
            ON pd.Fecha = f.Fecha
        LEFT JOIN #ProduccionDia pr
            ON pr.Fecha = f.Fecha
    ),

    Neto AS
    (
        SELECT
            *,
            CAST(KgProduccion - KgPedido AS DECIMAL(18,3)) AS KgNeto,
            CAST(CajasProduccion - CajasPedido AS DECIMAL(18,3)) AS CajasNeto
        FROM Base
    ),

    Calculo AS
    (
        SELECT
            N,
            Fecha,

            CAST(
                KgInventarioInicial
                + ISNULL(
                    SUM(KgNeto) OVER
                    (
                        ORDER BY N
                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                    ),
                    0
                )
                AS DECIMAL(18,3)
            ) AS KgInventario,

            CAST(
                KgInventarioInicial
                + SUM(KgNeto) OVER
                (
                    ORDER BY N
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                )
                AS DECIMAL(18,3)
            ) AS KgDisponible,

            CAST(
                CajasInventarioInicial
                + ISNULL(
                    SUM(CajasNeto) OVER
                    (
                        ORDER BY N
                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                    ),
                    0
                )
                AS DECIMAL(18,3)
            ) AS CajasInventario,

            CAST(
                CajasInventarioInicial
                + SUM(CajasNeto) OVER
                (
                    ORDER BY N
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                )
                AS DECIMAL(18,3)
            ) AS CajasDisponible,

            KgPromedioCaja,
            CajasPedido,
            KgPedido,
            ImportePedido,
            CajasProduccion,
            KgProduccion
        FROM Neto
    )

    SELECT
        Fecha,

        CAST(@SkuResumen AS VARCHAR(50)) AS Sku,
        CAST(@MasterResumen AS VARCHAR(150)) AS MasterProducto,
        CAST(@ArticuloResumen AS VARCHAR(250)) AS Articulo,

        CAST(@TotalSkus AS INT) AS TotalSkus,
        CAST(@TotalMasters AS INT) AS TotalMasters,

        CAST(CajasInventario AS DECIMAL(18,3)) AS CajasInventario,
        CAST(KgInventario AS DECIMAL(18,3)) AS KgInventario,
        CAST(KgPromedioCaja AS DECIMAL(18,3)) AS KgPromedioCaja,

        CAST(CajasPedido AS DECIMAL(18,3)) AS CajasPedido,
        CAST(KgPedido AS DECIMAL(18,3)) AS KgPedido,
        CAST(ImportePedido AS DECIMAL(18,2)) AS ImportePedido,

        CAST(CajasProduccion AS DECIMAL(18,3)) AS CajasProduccion,
        CAST(KgProduccion AS DECIMAL(18,3)) AS KgProduccion,

        CAST(CajasDisponible AS DECIMAL(18,3)) AS CajasDisponible,

        CAST(KgPedido AS DECIMAL(18,3)) AS KgPedidoEstimado,
        CAST(KgDisponible AS DECIMAL(18,3)) AS KgDisponibleEstimado,
        CAST(KgDisponible AS DECIMAL(18,3)) AS KgDisponible,

        @PrimeraFechaEntrega AS PrimeraFechaEntrega,
        @UltimaFechaEntrega AS UltimaFechaEntrega,

        CASE
            WHEN KgInventario = 0 AND KgPedido > 0
                THEN 'SIN INVENTARIO'
            WHEN KgDisponible < 0
                THEN 'FALTANTE'
            WHEN KgDisponible <= 10
                THEN 'ALERTA'
            ELSE 'OK'
        END AS EstatusDisponible,

        CAST(CASE WHEN N = 0 THEN 1 ELSE 0 END AS BIT) AS InventarioCapturado
    FROM Calculo
    ORDER BY
        Fecha
    OPTION (RECOMPILE);
END;
GO


/* ============================================================
   6. REFRESCAR CATÁLOGO
   ============================================================ */

EXEC dbo.sp_RefrescarInventarioInicialCache;
GO


/* ============================================================
   7. PRUEBAS RÁPIDAS
   ============================================================ */

-- Filtros:
EXEC dbo.sp_InventarioInicialFiltros;
GO

-- Prueba N003 del 22/06/2026:
EXEC dbo.sp_InventarioInicial
    @SkusCsv = 'N003',
    @FechaInicio = '2026-06-22',
    @FechaFin = '2026-07-20',
    @Dias = 28;
GO

-- Validación directa contra la tabla de inventario:
SELECT
    SkuNorm AS Sku,
    COUNT(*) AS Cajas,
    SUM(ISNULL(PesoNeto, 0)) AS KgInventario
FROM dbo.InventarioAlmacenado_Meat
WHERE SkuNorm = 'N003'
  AND FechaInventario >= '2026-06-22'
  AND FechaInventario <  '2026-06-23'
GROUP BY
    SkuNorm;
GO