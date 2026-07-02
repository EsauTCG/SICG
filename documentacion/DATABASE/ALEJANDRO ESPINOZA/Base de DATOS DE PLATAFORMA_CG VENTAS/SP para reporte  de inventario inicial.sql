USE [SIGO]
GO

/* ============================================================
   PROCEDIMIENTO 1: FILTROS PARA LA VISTA EJECUTIVA
   - Devuelve resultset 1: MASTER / Familia
   - Devuelve resultset 2: SKU / Producto
   ============================================================ */
 ALTER PROCEDURE [dbo].[sp_InventarioInicial]
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #ProductosRaw
    (
        Sku VARCHAR(50) NOT NULL,
        Articulo VARCHAR(250) NULL,
        MasterProducto VARCHAR(150) NULL
    );

    CREATE TABLE #Productos
    (
        Sku VARCHAR(50) NOT NULL PRIMARY KEY,
        Articulo VARCHAR(250) NULL,
        MasterProducto VARCHAR(150) NOT NULL
    );

    DECLARE 
        @Sql NVARCHAR(MAX),
        @MasterCol SYSNAME,
        @MasterExpr NVARCHAR(MAX);

    /* ------------------------------------------------------------
       Fuente 1: InventarioAlmacenado_Meat
       ------------------------------------------------------------ */
    IF OBJECT_ID('dbo.InventarioAlmacenado_Meat', 'U') IS NOT NULL
    BEGIN
        SELECT TOP 1 @MasterCol = c.name
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID('dbo.InventarioAlmacenado_Meat')
          AND c.name IN
          (
              'Master', 'MasterProducto', 'ProductoMaster', 'Familia',
              'Categoria', 'CategoriaProducto', 'Linea', 'Grupo',
              'GrupoProducto', 'Clasificacion'
          )
        ORDER BY
            CASE c.name
                WHEN 'Master' THEN 1
                WHEN 'MasterProducto' THEN 2
                WHEN 'ProductoMaster' THEN 3
                WHEN 'Familia' THEN 4
                WHEN 'Categoria' THEN 5
                WHEN 'CategoriaProducto' THEN 6
                WHEN 'Linea' THEN 7
                WHEN 'Grupo' THEN 8
                WHEN 'GrupoProducto' THEN 9
                WHEN 'Clasificacion' THEN 10
                ELSE 99
            END;

        SET @MasterExpr = CASE 
            WHEN @MasterCol IS NOT NULL
                THEN 'NULLIF(UPPER(LTRIM(RTRIM(CAST(i.' + QUOTENAME(@MasterCol) + ' AS VARCHAR(150))))), '''')'
            ELSE '''SIN MASTER'''
        END;

        SET @Sql = N'
            INSERT INTO #ProductosRaw (Sku, Articulo, MasterProducto)
            SELECT
                UPPER(LTRIM(RTRIM(CAST(i.Sku AS VARCHAR(50))))) AS Sku,
                MAX(NULLIF(LTRIM(RTRIM(CAST(ISNULL(i.Articulo, '''') AS VARCHAR(250)))), '''')) AS Articulo,
                COALESCE(MAX(' + @MasterExpr + N'), ''SIN MASTER'') AS MasterProducto
            FROM dbo.InventarioAlmacenado_Meat i
            WHERE
                i.Sku IS NOT NULL
                AND LTRIM(RTRIM(CAST(i.Sku AS VARCHAR(50)))) <> ''''
            GROUP BY
                UPPER(LTRIM(RTRIM(CAST(i.Sku AS VARCHAR(50)))));
        ';

        EXEC sys.sp_executesql @Sql;
    END;

    /* ------------------------------------------------------------
       Fuente 2: OrdenVentaProducto
       ------------------------------------------------------------ */
    SET @MasterCol = NULL;

    IF OBJECT_ID('dbo.OrdenVentaProducto', 'U') IS NOT NULL
    BEGIN
        SELECT TOP 1 @MasterCol = c.name
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID('dbo.OrdenVentaProducto')
          AND c.name IN
          (
              'Master', 'MasterProducto', 'ProductoMaster', 'Familia',
              'Categoria', 'CategoriaProducto', 'Linea', 'Grupo',
              'GrupoProducto', 'Clasificacion'
          )
        ORDER BY
            CASE c.name
                WHEN 'Master' THEN 1
                WHEN 'MasterProducto' THEN 2
                WHEN 'ProductoMaster' THEN 3
                WHEN 'Familia' THEN 4
                WHEN 'Categoria' THEN 5
                WHEN 'CategoriaProducto' THEN 6
                WHEN 'Linea' THEN 7
                WHEN 'Grupo' THEN 8
                WHEN 'GrupoProducto' THEN 9
                WHEN 'Clasificacion' THEN 10
                ELSE 99
            END;

        SET @MasterExpr = CASE 
            WHEN @MasterCol IS NOT NULL
                THEN 'NULLIF(UPPER(LTRIM(RTRIM(CAST(p.' + QUOTENAME(@MasterCol) + ' AS VARCHAR(150))))), '''')'
            ELSE '''SIN MASTER'''
        END;

        SET @Sql = N'
            INSERT INTO #ProductosRaw (Sku, Articulo, MasterProducto)
            SELECT
                UPPER(LTRIM(RTRIM(CAST(p.ProductoCodigo AS VARCHAR(50))))) AS Sku,
                MAX(NULLIF(LTRIM(RTRIM(CAST(ISNULL(p.ProductoNombre, '''') AS VARCHAR(250)))), '''')) AS Articulo,
                COALESCE(MAX(' + @MasterExpr + N'), ''SIN MASTER'') AS MasterProducto
            FROM dbo.OrdenVentaProducto p
            WHERE
                p.ProductoCodigo IS NOT NULL
                AND LTRIM(RTRIM(CAST(p.ProductoCodigo AS VARCHAR(50)))) <> ''''
                AND ISNULL(p.Eliminado, 0) = 0
            GROUP BY
                UPPER(LTRIM(RTRIM(CAST(p.ProductoCodigo AS VARCHAR(50)))));
        ';

        EXEC sys.sp_executesql @Sql;
    END;

    /* ------------------------------------------------------------
       Fuente 3: PlanDiario / Planeación
       ------------------------------------------------------------ */
    SET @MasterCol = NULL;

    IF OBJECT_ID('dbo.PlanDiario', 'U') IS NOT NULL
    BEGIN
        SELECT TOP 1 @MasterCol = c.name
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID('dbo.PlanDiario')
          AND c.name IN
          (
              'Master', 'MasterProducto', 'ProductoMaster', 'Familia',
              'Categoria', 'CategoriaProducto', 'Linea', 'Grupo',
              'GrupoProducto'
          )
        ORDER BY
            CASE c.name
                WHEN 'Master' THEN 1
                WHEN 'MasterProducto' THEN 2
                WHEN 'ProductoMaster' THEN 3
                WHEN 'Familia' THEN 4
                WHEN 'Categoria' THEN 5
                WHEN 'CategoriaProducto' THEN 6
                WHEN 'Linea' THEN 7
                WHEN 'Grupo' THEN 8
                WHEN 'GrupoProducto' THEN 9
                ELSE 99
            END;

        SET @MasterExpr = CASE 
            WHEN @MasterCol IS NOT NULL
                THEN 'NULLIF(UPPER(LTRIM(RTRIM(CAST(b.' + QUOTENAME(@MasterCol) + ' AS VARCHAR(150))))), '''')'
            ELSE '''SIN MASTER'''
        END;

        SET @Sql = N'
            INSERT INTO #ProductosRaw (Sku, Articulo, MasterProducto)
            SELECT
                UPPER(LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(50))))) AS Sku,
                MAX(UPPER(LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(250)))))) AS Articulo,
                COALESCE(MAX(' + @MasterExpr + N'), ''SIN MASTER'') AS MasterProducto
            FROM dbo.PlanDiario b
            WHERE
                b.ProductoCodigoConvertido IS NOT NULL
                AND LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(50)))) <> ''''
            GROUP BY
                UPPER(LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(50)))));
        ';

        EXEC sys.sp_executesql @Sql;
    END;

    /* ------------------------------------------------------------
       Normalizar catálogo final
       - Si no existe campo MASTER en tus tablas, saldrá SIN MASTER.
       - Si existe Master/Familia/Categoria/etc., se usará automáticamente.
       ------------------------------------------------------------ */
    INSERT INTO #Productos (Sku, Articulo, MasterProducto)
    SELECT
        r.Sku,
        COALESCE(
            MAX(NULLIF(r.Articulo, '')),
            r.Sku
        ) AS Articulo,
        COALESCE(
            MAX(CASE WHEN NULLIF(r.MasterProducto, '') IS NOT NULL AND r.MasterProducto <> 'SIN MASTER' THEN r.MasterProducto END),
            'SIN MASTER'
        ) AS MasterProducto
    FROM #ProductosRaw r
    WHERE
        r.Sku IS NOT NULL
        AND LTRIM(RTRIM(r.Sku)) <> ''
    GROUP BY
        r.Sku;

    SELECT
        MasterProducto,
        COUNT(DISTINCT Sku) AS TotalSkus
    FROM #Productos
    GROUP BY
        MasterProducto
    ORDER BY
        CASE WHEN MasterProducto = 'SIN MASTER' THEN 1 ELSE 0 END,
        MasterProducto;

    SELECT
        Sku,
        Articulo,
        MasterProducto
    FROM #Productos
    ORDER BY
        MasterProducto,
        Sku;
END;
GO


/* ============================================================
   PROCEDIMIENTO 2: INVENTARIO INICIAL EJECUTIVO
   - Acepta filtros múltiples por CSV:
       @SkusCsv    = 'V008,V045,N036'
       @MastersCsv = 'ARRACHERA,BLANCA'
   - Si @SkusCsv o @MastersCsv vienen NULL/vacíos, se toman TODOS.
   - Compatible con llamada anterior por @Sku.
   ============================================================ */
CREATE OR ALTER PROCEDURE [dbo].[sp_InventarioInicial]
    @Sku VARCHAR(50) = NULL,             -- Compatibilidad con versión anterior
    @SkusCsv VARCHAR(MAX) = NULL,        -- Nuevo filtro múltiple
    @MastersCsv VARCHAR(MAX) = NULL,     -- Nuevo filtro múltiple
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

    -- Si viene la forma anterior @Sku y no viene @SkusCsv, se respeta @Sku.
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

    CREATE TABLE #ProductosRaw
    (
        Sku VARCHAR(50) NOT NULL,
        Articulo VARCHAR(250) NULL,
        MasterProducto VARCHAR(150) NULL
    );

    CREATE TABLE #Productos
    (
        Sku VARCHAR(50) NOT NULL PRIMARY KEY,
        Articulo VARCHAR(250) NULL,
        MasterProducto VARCHAR(150) NOT NULL
    );

    CREATE TABLE #ProductosFiltrados
    (
        Sku VARCHAR(50) NOT NULL PRIMARY KEY,
        Articulo VARCHAR(250) NULL,
        MasterProducto VARCHAR(150) NOT NULL
    );

    DECLARE 
        @Sql NVARCHAR(MAX),
        @MasterCol SYSNAME,
        @MasterExpr NVARCHAR(MAX);

    /* ------------------------------------------------------------
       Catálogo dinámico desde Inventario
       ------------------------------------------------------------ */
    IF OBJECT_ID('dbo.InventarioAlmacenado_Meat', 'U') IS NOT NULL
    BEGIN
        SELECT TOP 1 @MasterCol = c.name
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID('dbo.InventarioAlmacenado_Meat')
          AND c.name IN
          (
              'Master', 'MasterProducto', 'ProductoMaster', 'Familia',
              'Categoria', 'CategoriaProducto', 'Linea', 'Grupo',
              'GrupoProducto', 'Clasificacion'
          )
        ORDER BY
            CASE c.name
                WHEN 'Master' THEN 1
                WHEN 'MasterProducto' THEN 2
                WHEN 'ProductoMaster' THEN 3
                WHEN 'Familia' THEN 4
                WHEN 'Categoria' THEN 5
                WHEN 'CategoriaProducto' THEN 6
                WHEN 'Linea' THEN 7
                WHEN 'Grupo' THEN 8
                WHEN 'GrupoProducto' THEN 9
                WHEN 'Clasificacion' THEN 10
                ELSE 99
            END;

        SET @MasterExpr = CASE 
            WHEN @MasterCol IS NOT NULL
                THEN 'NULLIF(UPPER(LTRIM(RTRIM(CAST(i.' + QUOTENAME(@MasterCol) + ' AS VARCHAR(150))))), '''')'
            ELSE '''SIN MASTER'''
        END;

        SET @Sql = N'
            INSERT INTO #ProductosRaw (Sku, Articulo, MasterProducto)
            SELECT
                UPPER(LTRIM(RTRIM(CAST(i.Sku AS VARCHAR(50))))) AS Sku,
                MAX(NULLIF(LTRIM(RTRIM(CAST(ISNULL(i.Articulo, '''') AS VARCHAR(250)))), '''')) AS Articulo,
                COALESCE(MAX(' + @MasterExpr + N'), ''SIN MASTER'') AS MasterProducto
            FROM dbo.InventarioAlmacenado_Meat i
            WHERE
                i.Sku IS NOT NULL
                AND LTRIM(RTRIM(CAST(i.Sku AS VARCHAR(50)))) <> ''''
            GROUP BY
                UPPER(LTRIM(RTRIM(CAST(i.Sku AS VARCHAR(50)))));
        ';

        EXEC sys.sp_executesql @Sql;
    END;

    /* ------------------------------------------------------------
       Catálogo dinámico desde Pedidos
       ------------------------------------------------------------ */
    SET @MasterCol = NULL;

    IF OBJECT_ID('dbo.OrdenVentaProducto', 'U') IS NOT NULL
    BEGIN
        SELECT TOP 1 @MasterCol = c.name
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID('dbo.OrdenVentaProducto')
          AND c.name IN
          (
              'Master', 'MasterProducto', 'ProductoMaster', 'Familia',
              'Categoria', 'CategoriaProducto', 'Linea', 'Grupo',
              'GrupoProducto', 'Clasificacion'
          )
        ORDER BY
            CASE c.name
                WHEN 'Master' THEN 1
                WHEN 'MasterProducto' THEN 2
                WHEN 'ProductoMaster' THEN 3
                WHEN 'Familia' THEN 4
                WHEN 'Categoria' THEN 5
                WHEN 'CategoriaProducto' THEN 6
                WHEN 'Linea' THEN 7
                WHEN 'Grupo' THEN 8
                WHEN 'GrupoProducto' THEN 9
                WHEN 'Clasificacion' THEN 10
                ELSE 99
            END;

        SET @MasterExpr = CASE 
            WHEN @MasterCol IS NOT NULL
                THEN 'NULLIF(UPPER(LTRIM(RTRIM(CAST(p.' + QUOTENAME(@MasterCol) + ' AS VARCHAR(150))))), '''')'
            ELSE '''SIN MASTER'''
        END;

        SET @Sql = N'
            INSERT INTO #ProductosRaw (Sku, Articulo, MasterProducto)
            SELECT
                UPPER(LTRIM(RTRIM(CAST(p.ProductoCodigo AS VARCHAR(50))))) AS Sku,
                MAX(NULLIF(LTRIM(RTRIM(CAST(ISNULL(p.ProductoNombre, '''') AS VARCHAR(250)))), '''')) AS Articulo,
                COALESCE(MAX(' + @MasterExpr + N'), ''SIN MASTER'') AS MasterProducto
            FROM dbo.OrdenVentaProducto p
            WHERE
                p.ProductoCodigo IS NOT NULL
                AND LTRIM(RTRIM(CAST(p.ProductoCodigo AS VARCHAR(50)))) <> ''''
                AND ISNULL(p.Eliminado, 0) = 0
            GROUP BY
                UPPER(LTRIM(RTRIM(CAST(p.ProductoCodigo AS VARCHAR(50)))));
        ';

        EXEC sys.sp_executesql @Sql;
    END;

    /* ------------------------------------------------------------
       Catálogo dinámico desde Producción / PlanDiario
       ------------------------------------------------------------ */
    SET @MasterCol = NULL;

    IF OBJECT_ID('dbo.PlanDiario', 'U') IS NOT NULL
    BEGIN
        SELECT TOP 1 @MasterCol = c.name
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID('dbo.PlanDiario')
          AND c.name IN
          (
              'Master', 'MasterProducto', 'ProductoMaster', 'Familia',
              'Categoria', 'CategoriaProducto', 'Linea', 'Grupo',
              'GrupoProducto'
          )
        ORDER BY
            CASE c.name
                WHEN 'Master' THEN 1
                WHEN 'MasterProducto' THEN 2
                WHEN 'ProductoMaster' THEN 3
                WHEN 'Familia' THEN 4
                WHEN 'Categoria' THEN 5
                WHEN 'CategoriaProducto' THEN 6
                WHEN 'Linea' THEN 7
                WHEN 'Grupo' THEN 8
                WHEN 'GrupoProducto' THEN 9
                ELSE 99
            END;

        SET @MasterExpr = CASE 
            WHEN @MasterCol IS NOT NULL
                THEN 'NULLIF(UPPER(LTRIM(RTRIM(CAST(b.' + QUOTENAME(@MasterCol) + ' AS VARCHAR(150))))), '''')'
            ELSE '''SIN MASTER'''
        END;

        SET @Sql = N'
            INSERT INTO #ProductosRaw (Sku, Articulo, MasterProducto)
            SELECT
                UPPER(LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(50))))) AS Sku,
                MAX(UPPER(LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(250)))))) AS Articulo,
                COALESCE(MAX(' + @MasterExpr + N'), ''SIN MASTER'') AS MasterProducto
            FROM dbo.PlanDiario b
            WHERE
                b.ProductoCodigoConvertido IS NOT NULL
                AND LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(50)))) <> ''''
            GROUP BY
                UPPER(LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(50)))));
        ';

        EXEC sys.sp_executesql @Sql;
    END;

    INSERT INTO #Productos (Sku, Articulo, MasterProducto)
    SELECT
        r.Sku,
        COALESCE(MAX(NULLIF(r.Articulo, '')), r.Sku) AS Articulo,
        COALESCE(
            MAX(CASE WHEN NULLIF(r.MasterProducto, '') IS NOT NULL AND r.MasterProducto <> 'SIN MASTER' THEN r.MasterProducto END),
            'SIN MASTER'
        ) AS MasterProducto
    FROM #ProductosRaw r
    WHERE
        r.Sku IS NOT NULL
        AND LTRIM(RTRIM(r.Sku)) <> ''
    GROUP BY
        r.Sku;

    INSERT INTO #ProductosFiltrados (Sku, Articulo, MasterProducto)
    SELECT
        p.Sku,
        p.Articulo,
        p.MasterProducto
    FROM #Productos p
    WHERE
        (
            NOT EXISTS (SELECT 1 FROM #FiltroSkus)
            OR EXISTS
            (
                SELECT 1
                FROM #FiltroSkus fs
                WHERE fs.Sku = p.Sku
            )
        )
        AND
        (
            NOT EXISTS (SELECT 1 FROM #FiltroMasters)
            OR EXISTS
            (
                SELECT 1
                FROM #FiltroMasters fm
                WHERE fm.MasterProducto = p.MasterProducto
            )
        );

    IF NOT EXISTS (SELECT 1 FROM #ProductosFiltrados)
    BEGIN
        ;WITH Numeros AS
        (
            SELECT 0 AS N
            UNION ALL
            SELECT N + 1 FROM Numeros WHERE N < @DiasRango
        )
        SELECT
            DATEADD(DAY, N, @FechaInicio) AS Fecha,
            CAST('SIN DATOS' AS VARCHAR(50)) AS Sku,
            CAST('SIN DATOS' AS VARCHAR(150)) AS MasterProducto,
            CAST('Sin productos para los filtros seleccionados' AS VARCHAR(250)) AS Articulo,
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
            CAST(CASE WHEN N = 0 THEN 1 ELSE 0 END AS BIT) AS InventarioCapturado
        FROM Numeros
        ORDER BY Fecha
        OPTION (MAXRECURSION 32767);

        RETURN;
    END;

    DECLARE
        @TotalSkus INT = (SELECT COUNT(DISTINCT Sku) FROM #ProductosFiltrados),
        @TotalMasters INT = (SELECT COUNT(DISTINCT MasterProducto) FROM #ProductosFiltrados),
        @SkuResumen VARCHAR(50),
        @MasterResumen VARCHAR(150),
        @ArticuloResumen VARCHAR(250);

    SELECT
        @SkuResumen =
            CASE
                WHEN COUNT(DISTINCT Sku) = 1 THEN MAX(Sku)
                ELSE CONCAT(COUNT(DISTINCT Sku), ' SKU')
            END,
        @MasterResumen =
            CASE
                WHEN COUNT(DISTINCT MasterProducto) = 1 THEN MAX(MasterProducto)
                ELSE CONCAT(COUNT(DISTINCT MasterProducto), ' MASTER')
            END,
        @ArticuloResumen =
            CASE
                WHEN COUNT(DISTINCT Sku) = 1 THEN MAX(Articulo)
                ELSE 'CONSOLIDADO EJECUTIVO'
            END
    FROM #ProductosFiltrados;

    CREATE TABLE #Fechas
    (
        N INT NOT NULL PRIMARY KEY,
        Fecha DATE NOT NULL
    );

    ;WITH Numeros AS
    (
        SELECT 0 AS N
        UNION ALL
        SELECT N + 1 FROM Numeros WHERE N < @DiasRango
    )
    INSERT INTO #Fechas (N, Fecha)
    SELECT
        N,
        DATEADD(DAY, N, @FechaInicio)
    FROM Numeros
    OPTION (MAXRECURSION 32767);

    CREATE TABLE #InventarioInicial
    (
        CajasInventario DECIMAL(18,3) NOT NULL,
        KgInventario DECIMAL(18,3) NOT NULL,
        KgPromedioCaja DECIMAL(18,3) NOT NULL
    );

   INSERT INTO #InventarioInicial (CajasInventario, KgInventario, KgPromedioCaja)
SELECT
    CAST(COUNT(*) AS DECIMAL(18,3)) AS CajasInventario,
    CAST(SUM(ISNULL(i.PesoNeto, 0)) AS DECIMAL(18,3)) AS KgInventario,
    CAST(AVG(NULLIF(i.PesoNeto, 0)) AS DECIMAL(18,3)) AS KgPromedioCaja
FROM dbo.InventarioAlmacenado_Meat i
INNER JOIN #ProductosFiltrados pf
    ON pf.Sku = UPPER(LTRIM(RTRIM(CAST(i.Sku AS VARCHAR(50)))))
WHERE
    i.Sku IS NOT NULL
    AND LTRIM(RTRIM(CAST(i.Sku AS VARCHAR(50)))) <> ''
    AND i.FechaInventario >= @FechaInicio
    AND i.FechaInventario < DATEADD(DAY, 1, @FechaInicio);

    IF NOT EXISTS (SELECT 1 FROM #InventarioInicial)
    BEGIN
        INSERT INTO #InventarioInicial (CajasInventario, KgInventario, KgPromedioCaja)
        VALUES (0, 0, 0);
    END;

    CREATE TABLE #ProduccionDia
    (
        Fecha DATE NOT NULL PRIMARY KEY,
        KgProduccion DECIMAL(18,3) NOT NULL
    );

    INSERT INTO #ProduccionDia (Fecha, KgProduccion)
    SELECT
        CAST(a.FechaPlan AS DATE) AS Fecha,
        CAST(SUM(ISNULL(b.KgInyeccion, 0)) AS DECIMAL(18,3)) AS KgProduccion
    FROM dbo.PlaneacionProduccion a
    INNER JOIN dbo.PlanDiario b
        ON b.PlaneacionId = a.PlaneacionId
    INNER JOIN #ProductosFiltrados pf
        ON pf.Sku = UPPER(LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(50)))))
    WHERE
        b.ProductoCodigoConvertido IS NOT NULL
        AND LTRIM(RTRIM(CAST(b.ProductoCodigoConvertido AS VARCHAR(50)))) <> ''
        AND CAST(a.FechaPlan AS DATE) >= @FechaInicio
        AND CAST(a.FechaPlan AS DATE) <= @FechaFin
    GROUP BY
        CAST(a.FechaPlan AS DATE);

    CREATE TABLE #PedidosDia
    (
        Fecha DATE NOT NULL PRIMARY KEY,
        CajasPedido DECIMAL(18,3) NOT NULL,
        KgPedido DECIMAL(18,3) NOT NULL,
        ImportePedido DECIMAL(18,2) NOT NULL
    );

    INSERT INTO #PedidosDia (Fecha, CajasPedido, KgPedido, ImportePedido)
    SELECT
        CAST(o.FechaEntrega AS DATE) AS Fecha,
        CAST(SUM(ISNULL(p.Cajas, 0)) AS DECIMAL(18,3)) AS CajasPedido,
        CAST(SUM(ISNULL(p.Peso, 0)) AS DECIMAL(18,3)) AS KgPedido,
        CAST(SUM(ISNULL(p.Importe, 0)) AS DECIMAL(18,2)) AS ImportePedido
    FROM dbo.OrdenVentaProducto p
    INNER JOIN dbo.OrdenVenta o
        ON o.Id = p.PedidoId
    INNER JOIN #ProductosFiltrados pf
        ON pf.Sku = UPPER(LTRIM(RTRIM(CAST(p.ProductoCodigo AS VARCHAR(50)))))
    WHERE
        ISNULL(p.Eliminado, 0) = 0
        AND p.ProductoCodigo IS NOT NULL
        AND LTRIM(RTRIM(CAST(p.ProductoCodigo AS VARCHAR(50)))) <> ''
        AND CAST(o.FechaEntrega AS DATE) >= @FechaInicio
        AND CAST(o.FechaEntrega AS DATE) <= @FechaFin

        /*
          Si necesitas excluir pedidos cancelados, activa el filtro correcto de tu sistema.
          Ejemplos:
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

            CASE WHEN f.N = 0 THEN ISNULL(ii.CajasInventario, 0) ELSE CAST(0 AS DECIMAL(18,3)) END AS CajasInventarioInicial,
            CASE WHEN f.N = 0 THEN ISNULL(ii.KgInventario, 0) ELSE CAST(0 AS DECIMAL(18,3)) END AS KgInventarioInicial,

            ISNULL(ii.KgPromedioCaja, 0) AS KgPromedioCaja,

            ISNULL(pd.CajasPedido, 0) AS CajasPedido,
            ISNULL(pd.KgPedido, 0) AS KgPedido,
            ISNULL(pd.ImportePedido, 0) AS ImportePedido,

            ISNULL(pr.KgProduccion, 0) AS KgProduccion
        FROM #Fechas f
        CROSS JOIN #InventarioInicial ii
        LEFT JOIN #PedidosDia pd
            ON pd.Fecha = f.Fecha
        LEFT JOIN #ProduccionDia pr
            ON pr.Fecha = f.Fecha
    ),

    Calculo AS
    (
        SELECT
            b.N,
            b.Fecha,

            CAST(b.CajasInventarioInicial AS DECIMAL(18,3)) AS CajasInventario,
            CAST(b.KgInventarioInicial AS DECIMAL(18,3)) AS KgInventario,
            CAST(b.KgPromedioCaja AS DECIMAL(18,3)) AS KgPromedioCaja,

            CAST(b.CajasPedido AS DECIMAL(18,3)) AS CajasPedido,
            CAST(b.KgPedido AS DECIMAL(18,3)) AS KgPedido,
            CAST(b.ImportePedido AS DECIMAL(18,2)) AS ImportePedido,

            CAST(
                CASE 
                    WHEN b.KgPromedioCaja > 0 THEN b.KgProduccion / b.KgPromedioCaja
                    ELSE 0
                END
                AS DECIMAL(18,3)
            ) AS CajasProduccion,

            CAST(b.KgProduccion AS DECIMAL(18,3)) AS KgProduccion,

            CAST(
                b.CajasInventarioInicial
                + CASE 
                    WHEN b.KgPromedioCaja > 0 THEN b.KgProduccion / b.KgPromedioCaja
                    ELSE 0
                  END
                - b.CajasPedido
                AS DECIMAL(18,3)
            ) AS CajasDisponible,

            CAST(
                b.KgInventarioInicial + b.KgProduccion - b.KgPedido
                AS DECIMAL(18,3)
            ) AS KgDisponible
        FROM Base b
        WHERE b.N = 0

        UNION ALL

        SELECT
            b.N,
            b.Fecha,

            CAST(c.CajasDisponible AS DECIMAL(18,3)) AS CajasInventario,
            CAST(c.KgDisponible AS DECIMAL(18,3)) AS KgInventario,
            CAST(b.KgPromedioCaja AS DECIMAL(18,3)) AS KgPromedioCaja,

            CAST(b.CajasPedido AS DECIMAL(18,3)) AS CajasPedido,
            CAST(b.KgPedido AS DECIMAL(18,3)) AS KgPedido,
            CAST(b.ImportePedido AS DECIMAL(18,2)) AS ImportePedido,

            CAST(
                CASE 
                    WHEN b.KgPromedioCaja > 0 THEN b.KgProduccion / b.KgPromedioCaja
                    ELSE 0
                END
                AS DECIMAL(18,3)
            ) AS CajasProduccion,

            CAST(b.KgProduccion AS DECIMAL(18,3)) AS KgProduccion,

            CAST(
                c.CajasDisponible
                + CASE 
                    WHEN b.KgPromedioCaja > 0 THEN b.KgProduccion / b.KgPromedioCaja
                    ELSE 0
                  END
                - b.CajasPedido
                AS DECIMAL(18,3)
            ) AS CajasDisponible,

            CAST(
                c.KgDisponible + b.KgProduccion - b.KgPedido
                AS DECIMAL(18,3)
            ) AS KgDisponible
        FROM Base b
        INNER JOIN Calculo c
            ON b.N = c.N + 1
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

        -- Compatibilidad con la vista anterior
        CAST(KgPedido AS DECIMAL(18,3)) AS KgPedidoEstimado,
        CAST(KgDisponible AS DECIMAL(18,3)) AS KgDisponibleEstimado,

        -- Nombre claro para la nueva vista
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
    OPTION (MAXRECURSION 32767, RECOMPILE);
END;
GO



--CREATE NONCLUSTERED INDEX IX_InventarioAlmacenado_Meat_Sku
--ON dbo.InventarioAlmacenado_Meat (Sku)
--INCLUDE (Articulo, PesoNeto);


--CREATE NONCLUSTERED INDEX IX_OrdenVentaProducto_ProductoCodigo_Eliminado
--ON dbo.OrdenVentaProducto (ProductoCodigo, Eliminado, PedidoId)
--INCLUDE (ProductoNombre, Cajas, Importe);


--CREATE NONCLUSTERED INDEX IX_OrdenVenta_Id_Estatus_FechaEntrega
--ON dbo.OrdenVenta (Id, Estatus, FechaEntrega)
--INCLUDE (Consecutivo, Cliente, Saldo);