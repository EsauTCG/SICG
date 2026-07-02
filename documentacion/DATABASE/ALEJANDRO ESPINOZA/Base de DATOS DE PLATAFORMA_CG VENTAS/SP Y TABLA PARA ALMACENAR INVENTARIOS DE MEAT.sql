/* 1) TABLA DESTINO */
IF OBJECT_ID('dbo.InventarioAlmacenado_Meat','U') IS NULL
BEGIN
    CREATE TABLE dbo.InventarioAlmacenado_Meat
    (
        ProduccionId     int               NULL,
        FechaInventario  date              NOT NULL,
        FechaProduccion  date              NULL,

        Almacen          nvarchar(200) COLLATE Modern_Spanish_CI_AS NULL,
        CodigoEtiqueta   nvarchar(200) COLLATE Modern_Spanish_CI_AS NOT NULL,
        Sku              nvarchar(100) COLLATE Modern_Spanish_CI_AS NULL,
        Articulo         nvarchar(250) COLLATE Modern_Spanish_CI_AS NULL,

        PesoNeto         decimal(18,4)     NULL,
        CostoEtiqueta    decimal(18,4)     NULL,

        Sucursal         nvarchar(100) COLLATE Modern_Spanish_CI_AS NOT NULL,

        FechaSync        datetime2(0)      NOT NULL CONSTRAINT DF_InvMeat_FechaSync DEFAULT (sysdatetime()),
        OrigenHash       varbinary(32)     NULL
    );
END
GO

/* 2) ÍNDICES (sobre la MISMA tabla) */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_InvMeat_CodigoEtiqueta_Sucursal' AND object_id=OBJECT_ID('dbo.InventarioAlmacenado_Meat'))
BEGIN
    CREATE UNIQUE INDEX UX_InvMeat_CodigoEtiqueta_Sucursal_FechaInventario
ON dbo.InventarioAlmacenado_Meat (CodigoEtiqueta, Sucursal, FechaInventario);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_InvMeat_FechaInventario' AND object_id=OBJECT_ID('dbo.InventarioAlmacenado_Meat'))
BEGIN
    CREATE INDEX IX_InvMeat_FechaInventario
    ON dbo.InventarioAlmacenado_Meat (FechaInventario);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_InvMeat_Sucursal_Fecha' AND object_id=OBJECT_ID('dbo.InventarioAlmacenado_Meat'))
BEGIN
    CREATE INDEX IX_InvMeat_Sucursal_Fecha
    ON dbo.InventarioAlmacenado_Meat (Sucursal, FechaInventario);
END
GO

--DROP INDEX UX_InvMeat_CodigoEtiqueta_Sucursal
--ON dbo.InventarioAlmacenado_Meat;
--GO


/* 3) PROCEDIMIENTO */
USE [SIGO]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
--exec [sp_SyncInventarioAlmacenado]

ALTER PROCEDURE [dbo].[sp_SyncInventarioAlmacenado]
    @FechaDesde date = '20260101'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        IF OBJECT_ID('tempdb..#stgInventario') IS NOT NULL
            DROP TABLE #stgInventario;

        CREATE TABLE #stgInventario
        (
            ProduccionId      int            NULL,
            FechaInventario   date           NOT NULL,
            FechaProduccion   date           NULL,
            Almacen           nvarchar(200) COLLATE Modern_Spanish_CI_AS NULL,
            CodigoEtiqueta    nvarchar(200) COLLATE Modern_Spanish_CI_AS NOT NULL,
            Sku               nvarchar(100) COLLATE Modern_Spanish_CI_AS NULL,
            Articulo          nvarchar(250) COLLATE Modern_Spanish_CI_AS NULL,
            PesoNeto          decimal(18,4) NULL,
            CostoEtiqueta     decimal(18,4) NULL,
            Sucursal          nvarchar(100) COLLATE Modern_Spanish_CI_AS NOT NULL
        );

        INSERT INTO #stgInventario
        (
            ProduccionId, FechaInventario, FechaProduccion, Almacen, CodigoEtiqueta,
            Sku, Articulo, PesoNeto, CostoEtiqueta, Sucursal
        )
        SELECT
            ProduccionId,
            CONVERT(date, FechaInventario),
            CONVERT(date, FechaProduccion),
            Almacen        COLLATE Modern_Spanish_CI_AS,
            CodigoEtiqueta COLLATE Modern_Spanish_CI_AS,
            Sku            COLLATE Modern_Spanish_CI_AS,
            Articulo       COLLATE Modern_Spanish_CI_AS,
            PesoNeto,
            CostoEtiqueta,
            Sucursal       COLLATE Modern_Spanish_CI_AS
        FROM [MEAT_P1].meat.dbo.InventarioAlmacenado a
        INNER JOIN [MEAT_P1].commercianet.dbo.almacen b
            ON a.Almacen = b.nombre
        WHERE FechaInventario >= @FechaDesde

        UNION ALL

        SELECT
            ProduccionId,
            CONVERT(date, FechaInventario),
            CONVERT(date, FechaProduccion),
            Almacen        COLLATE Modern_Spanish_CI_AS,
            CodigoEtiqueta COLLATE Modern_Spanish_CI_AS,
            Sku            COLLATE Modern_Spanish_CI_AS,
            Articulo       COLLATE Modern_Spanish_CI_AS,
            PesoNeto,
            CostoEtiqueta,
            Sucursal       COLLATE Modern_Spanish_CI_AS
        FROM [MEAT_TIF].TIF_meat.dbo.InventarioAlmacenado a
        INNER JOIN [MEAT_TIF].TIF_commercianet.dbo.almacen b
            ON a.Almacen = b.nombre
        WHERE FechaInventario >= @FechaDesde;

        ;WITH d AS
        (
            SELECT *,
                   ROW_NUMBER() OVER
                   (
                       PARTITION BY CodigoEtiqueta, Sucursal, FechaInventario
                       ORDER BY ProduccionId DESC
                   ) AS rn
            FROM #stgInventario
        )
        DELETE FROM d
        WHERE rn > 1;

        MERGE dbo.InventarioAlmacenado_Meat AS T
        USING #stgInventario AS S
          ON  T.CodigoEtiqueta  = S.CodigoEtiqueta
          AND T.Sucursal        = S.Sucursal
          AND T.FechaInventario = S.FechaInventario

        WHEN MATCHED AND
        (
            ISNULL(T.ProduccionId, -1) <> ISNULL(S.ProduccionId, -1)
            OR ISNULL(T.FechaProduccion, '19000101') <> ISNULL(S.FechaProduccion, '19000101')
            OR ISNULL(T.Almacen, '') <> ISNULL(S.Almacen, '')
            OR ISNULL(T.Sku, '') <> ISNULL(S.Sku, '')
            OR ISNULL(T.Articulo, '') <> ISNULL(S.Articulo, '')
            OR ISNULL(T.PesoNeto, 0) <> ISNULL(S.PesoNeto, 0)
            OR ISNULL(T.CostoEtiqueta, 0) <> ISNULL(S.CostoEtiqueta, 0)
        )
        THEN UPDATE SET
            T.ProduccionId    = S.ProduccionId,
            T.FechaInventario = S.FechaInventario,
            T.FechaProduccion = S.FechaProduccion,
            T.Almacen         = S.Almacen,
            T.Sku             = S.Sku,
            T.Articulo        = S.Articulo,
            T.PesoNeto        = S.PesoNeto,
            T.CostoEtiqueta   = S.CostoEtiqueta,
            T.FechaSync       = SYSDATETIME()

        WHEN NOT MATCHED BY TARGET
        THEN INSERT
        (
            ProduccionId, FechaInventario, FechaProduccion, Almacen, CodigoEtiqueta,
            Sku, Articulo, PesoNeto, CostoEtiqueta, Sucursal, FechaSync
        )
        VALUES
        (
            S.ProduccionId, S.FechaInventario, S.FechaProduccion, S.Almacen, S.CodigoEtiqueta,
            S.Sku, S.Articulo, S.PesoNeto, S.CostoEtiqueta, S.Sucursal, SYSDATETIME()
        );

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO


--select * from InventarioAlmacenado_Meat


--EXEC dbo.sp_SyncInventarioAlmacenado;