USE sigo;
GO

IF OBJECT_ID('dbo.ProduccionSigo','U') IS NULL
BEGIN
    CREATE TABLE dbo.ProduccionSigo
    (
        Sucursal            NVARCHAR(20)  NOT NULL,      -- 'PLANTA 1' / 'TIF'
        ArticuloCodigo      NVARCHAR(50)  NOT NULL,
        Producto            NVARCHAR(200) NULL,
        LoteId              NVARCHAR(50)  NOT NULL,
        Lote                NVARCHAR(200) NULL,
        FechaProduccion     DATE          NOT NULL,
        KgProducidos        DECIMAL(18,2) NOT NULL,
        FechaActualizacion  DATETIME      NOT NULL CONSTRAINT DF_Prod_FechaAct DEFAULT (GETDATE()),

        -- Si quieres día/mes/año, mejor como columnas calculadas (sin redundancia)
        Dia AS (DATEPART(DAY,  FechaProduccion)) PERSISTED,
        Mes AS (DATEPART(MONTH,FechaProduccion)) PERSISTED,
        Anio AS (DATEPART(YEAR, FechaProduccion)) PERSISTED,

        CONSTRAINT PK_ProduccionSigo
            PRIMARY KEY CLUSTERED (Sucursal, ArticuloCodigo, LoteId, FechaProduccion)
    );

    CREATE INDEX IX_ProduccionSigo_Fecha
        ON dbo.ProduccionSigo(FechaProduccion);
END
GO






USE sigo;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.ActualizarProduccionSigo
    @FechaInicio DATE = '2026-01-01'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        /*=========================================================
          1) TABLA TEMPORAL BASE
        =========================================================*/
        IF OBJECT_ID('tempdb..#ProdTemp') IS NOT NULL DROP TABLE #ProdTemp;

        CREATE TABLE #ProdTemp
        (
            Sucursal        NVARCHAR(20)  NOT NULL,
            ArticuloCodigo  NVARCHAR(50)  NOT NULL,
            Producto        NVARCHAR(200) NULL,
            LoteId          NVARCHAR(50)  NOT NULL,
            Lote            NVARCHAR(200) NULL,
            FechaProduccion DATE          NOT NULL,
            KgProducidos    DECIMAL(18,2) NOT NULL
        );

        /*=========================================================
          2) MEAT_P1  (PLANTA 1)
        =========================================================*/
        INSERT INTO #ProdTemp (Sucursal, ArticuloCodigo, Producto, LoteId, Lote, FechaProduccion, KgProducidos)
        SELECT
            'PLANTA 1' AS Sucursal,
            CAST(a.Articulo AS NVARCHAR(50)) AS ArticuloCodigo,
            c.Nombre AS Producto,
            CAST(b.LoteId AS NVARCHAR(50)) AS LoteId,
            b.Nombre AS Lote,
            CONVERT(DATE, a.FechaProduccion) AS FechaProduccion,
            ROUND(SUM(a.PesoNeto), 2) AS KgProducidos
        FROM [MEAT_P1].MEAT.dbo.Produccion a
        INNER JOIN [MEAT_P1].MEAT.dbo.Lote b
            ON a.LoteId = b.LoteId
        INNER JOIN [MEAT_P1].CommerciaNET.dbo.Articulo c
            ON a.Articulo = c.ArticuloId
        WHERE a.FechaProduccion >= @FechaInicio
          AND b.TipoLoteId <> 11  and a.UltimoProcesoId  <> 29 and a.UltimoProcesoId  <> 24 and a.UltimoProcesoId  <> 14
        GROUP BY
            a.Articulo, c.Nombre, b.LoteId, b.Nombre, CONVERT(DATE, a.FechaProduccion);

        /*=========================================================
          3) MEAT_TIF  (TIF)
        =========================================================*/
        INSERT INTO #ProdTemp (Sucursal, ArticuloCodigo, Producto, LoteId, Lote, FechaProduccion, KgProducidos)
        SELECT
            'TIF' AS Sucursal,
            CAST(a.Articulo AS NVARCHAR(50)) AS ArticuloCodigo,
            c.Nombre AS Producto,
            CAST(b.LoteId AS NVARCHAR(50)) AS LoteId,
            b.Nombre AS Lote,
            CONVERT(DATE, a.FechaProduccion) AS FechaProduccion,
            ROUND(SUM(a.PesoNeto), 2) AS KgProducidos
        FROM [MEAT_TIF].TIF_MEAT.dbo.Produccion a
        INNER JOIN [MEAT_TIF].TIF_MEAT.dbo.Lote b
            ON a.LoteId = b.LoteId
        INNER JOIN [MEAT_TIF].TIF_CommerciaNET.dbo.Articulo c
            ON a.Articulo = c.ArticuloId
        WHERE a.FechaProduccion >= @FechaInicio and a.UltimoProcesoId  <> 29 and a.UltimoProcesoId  <> 24 and a.UltimoProcesoId  <> 14
        GROUP BY
            a.Articulo, c.Nombre, b.LoteId, b.Nombre, CONVERT(DATE, a.FechaProduccion);

        /*=========================================================
          4) NORMALIZAR (POR SI HAY DUPLICADOS)
        =========================================================*/
        IF OBJECT_ID('tempdb..#ProdFinal') IS NOT NULL DROP TABLE #ProdFinal;

        SELECT
            Sucursal,
            ArticuloCodigo,
            MAX(Producto) AS Producto,
            LoteId,
            MAX(Lote) AS Lote,
            FechaProduccion,
            ROUND(SUM(KgProducidos), 2) AS KgProducidos
        INTO #ProdFinal
        FROM #ProdTemp
        GROUP BY
            Sucursal, ArticuloCodigo, LoteId, FechaProduccion;

        /*=========================================================
          5) MERGE FINAL (UPSERT)
        =========================================================*/
        MERGE dbo.ProduccionSigo WITH (HOLDLOCK) AS destino
        USING #ProdFinal AS fuente
           ON destino.Sucursal        = fuente.Sucursal
          AND destino.ArticuloCodigo  = fuente.ArticuloCodigo
          AND destino.LoteId          = fuente.LoteId
          AND destino.FechaProduccion = fuente.FechaProduccion

        WHEN MATCHED AND (
               ISNULL(destino.Producto,'') <> ISNULL(fuente.Producto,'')
            OR ISNULL(destino.Lote,'')     <> ISNULL(fuente.Lote,'')
            OR destino.KgProducidos        <> fuente.KgProducidos
        )
        THEN UPDATE SET
            destino.Producto = fuente.Producto,
            destino.Lote = fuente.Lote,
            destino.KgProducidos = fuente.KgProducidos,
            destino.FechaActualizacion = GETDATE()

        WHEN NOT MATCHED BY TARGET
        THEN INSERT
        (
            Sucursal, ArticuloCodigo, Producto, LoteId, Lote,
            FechaProduccion, KgProducidos, FechaActualizacion
        )
        VALUES
        (
            fuente.Sucursal, fuente.ArticuloCodigo, fuente.Producto, fuente.LoteId, fuente.Lote,
            fuente.FechaProduccion, fuente.KgProducidos, GETDATE()
        );

        /*=========================================================
          6) DELETE CONTROLADO (SOLO EN EL RANGO SINCRONIZADO)
        =========================================================*/
        DELETE d
        FROM dbo.ProduccionSigo d
        WHERE d.FechaProduccion >= @FechaInicio
          AND NOT EXISTS
          (
              SELECT 1
              FROM #ProdFinal f
              WHERE f.Sucursal        = d.Sucursal
                AND f.ArticuloCodigo  = d.ArticuloCodigo
                AND f.LoteId          = d.LoteId
                AND f.FechaProduccion = d.FechaProduccion
          );

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;

        DECLARE
            @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE(),
            @ErrSev INT = ERROR_SEVERITY(),
            @ErrSta INT = ERROR_STATE();

        RAISERROR(@ErrMsg, @ErrSev, @ErrSta);
    END CATCH
END;
GO
