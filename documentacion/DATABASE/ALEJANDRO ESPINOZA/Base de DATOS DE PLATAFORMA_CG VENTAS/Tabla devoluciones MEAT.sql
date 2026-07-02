CREATE TABLE dbo.DevolucionMeat
(
    DevolucionMeatId   INT IDENTITY(1,1) PRIMARY KEY,
    Sucursal           VARCHAR(20)   NOT NULL,
    FechaSurtido       DATE          NOT NULL,
    FechaDevolucion    DATE          NOT NULL,
    CodigoSap          VARCHAR(50)   NULL,
    Cliente            VARCHAR(200)  NULL,
    Remision           VARCHAR(100)  NULL,
    SolicitudSurtidoId INT           NOT NULL,
    Articulo           VARCHAR(100)  NULL,
    CodigoEtiqueta     VARCHAR(100)  NOT NULL,
    Peso               DECIMAL(18,3) NULL,
    Lote               VARCHAR(20)   NULL,
    FechaActualizacion DATETIME      NOT NULL DEFAULT(GETDATE())
);
GO


CREATE UNIQUE INDEX UX_DevolucionMeat_Hist
ON dbo.DevolucionMeat (Sucursal, CodigoEtiqueta, FechaDevolucion, SolicitudSurtidoId);
GO

select * from DevolucionMeat

exec SincronizarDevolucionMeat




---2
USE [SIGO]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[SincronizarDevolucionMeat]
    @FechaInicio DATE = '2026-01-01'
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('tempdb..#DevolucionesTemp') IS NOT NULL
        DROP TABLE #DevolucionesTemp;

    IF OBJECT_ID('tempdb..#DevolucionesMerge') IS NOT NULL
        DROP TABLE #DevolucionesMerge;

    CREATE TABLE #DevolucionesTemp
    (
        Sucursal           VARCHAR(20),
        FechaSurtido       DATE,
        FechaDevolucion    DATE,
        CodigoSap          VARCHAR(50),
        Cliente            VARCHAR(200),
        Remision           VARCHAR(100),
        SolicitudSurtidoId INT,
        Articulo           VARCHAR(100),
        CodigoEtiqueta     VARCHAR(100),
        Peso               DECIMAL(18,3),
        Lote               VARCHAR(20)
    );

    CREATE TABLE #DevolucionesMerge
    (
        Sucursal           VARCHAR(20),
        FechaSurtido       DATE,
        FechaDevolucion    DATE,
        CodigoSap          VARCHAR(50),
        Cliente            VARCHAR(200),
        Remision           VARCHAR(100),
        SolicitudSurtidoId INT,
        Articulo           VARCHAR(100),
        CodigoEtiqueta     VARCHAR(100),
        Peso               DECIMAL(18,3),
        Lote               VARCHAR(20)
    );

    /* =========================
       PLANTA TIF
       ========================= */
    INSERT INTO #DevolucionesTemp
    (
        Sucursal,
        FechaSurtido,
        FechaDevolucion,
        CodigoSap,
        Cliente,
        Remision,
        SolicitudSurtidoId,
        Articulo,
        CodigoEtiqueta,
        Peso,
        Lote
    )
    SELECT DISTINCT
        'PLANTA TIF',
        CONVERT(DATE, SAL.FechaHora),
        CONVERT(DATE, PLG.FechaHora),
        CLIENT.CodigoId,
        RCLI.Referencia,
        REF.Referencia,
        SAL.SolicitudSurtidoId,
        PLG.Articulo,
        PLG.CodigoEtiqueta,
        PLG.Peso,
        SUBSTRING(PLG.CodigoEtiqueta, 1, 12)
    FROM [MEAT_TIF].[tif_Meat].[dbo].[ProduccionLog] PLG
    INNER JOIN [MEAT_TIF].[tif_Meat].[dbo].[SalidaEmbarque] SAL
        ON PLG.ProduccionId = SAL.ProduccionId
       AND SAL.FechaHora < PLG.FechaHoraServer
    INNER JOIN [MEAT_TIF].[tif_Meat].[dbo].[SurtidoReferencia] REF
        ON SAL.SolicitudSurtidoId = REF.SolicitudSurtidoId
       AND REF.TipoReferenciaId = 12
    INNER JOIN [MEAT_TIF].[tif_Meat].[dbo].[SurtidoReferencia] RCLI
        ON SAL.SolicitudSurtidoId = RCLI.SolicitudSurtidoId
       AND RCLI.TipoReferenciaId = 6
    INNER JOIN [MEAT_TIF].[tif_Meat].[dbo].[SurtidoReferencia] RCLI2
        ON SAL.SolicitudSurtidoId = RCLI2.SolicitudSurtidoId
       AND RCLI2.TipoReferenciaId = 5
    INNER JOIN [MEAT_TIF].[tif_CommerciaNet].[dbo].[Cliente] CLIENT
        ON RCLI2.Referencia = CLIENT.ClienteId or Client.CODIGOID = RCLI2.Referencia 
    WHERE PLG.ProcesoId = 31
      AND SAL.FechaHora >= @FechaInicio;

    /* =========================
       PLANTA 1
       ========================= */
    INSERT INTO #DevolucionesTemp
    (
        Sucursal,
        FechaSurtido,
        FechaDevolucion,
        CodigoSap,
        Cliente,
        Remision,
        SolicitudSurtidoId,
        Articulo,
        CodigoEtiqueta,
        Peso,
        Lote
    )
    SELECT DISTINCT
        'PLANTA 1',
        CONVERT(DATE, SAL.FechaHora),
        CONVERT(DATE, PLG.FechaHora),
        CLIENT.CodigoId,
        RCLI.Referencia,
        REF.Referencia,
        SAL.SolicitudSurtidoId,
        PLG.Articulo,
        PLG.CodigoEtiqueta,
        PLG.Peso,
        SUBSTRING(PLG.CodigoEtiqueta, 1, 12)
    FROM [MEAT_P1].[Meat].[dbo].[ProduccionLog] PLG
    INNER JOIN [MEAT_P1].[Meat].[dbo].[SalidaEmbarque] SAL
        ON PLG.ProduccionId = SAL.ProduccionId
       AND SAL.FechaHora < PLG.FechaHoraServer
    INNER JOIN [MEAT_P1].[Meat].[dbo].[SurtidoReferencia] REF
        ON SAL.SolicitudSurtidoId = REF.SolicitudSurtidoId
       AND REF.TipoReferenciaId = 12
    INNER JOIN [MEAT_P1].[Meat].[dbo].[SurtidoReferencia] RCLI
        ON SAL.SolicitudSurtidoId = RCLI.SolicitudSurtidoId
       AND RCLI.TipoReferenciaId = 6
    INNER JOIN [MEAT_P1].[Meat].[dbo].[SurtidoReferencia] RCLI2
        ON SAL.SolicitudSurtidoId = RCLI2.SolicitudSurtidoId
       AND RCLI2.TipoReferenciaId = 5
    INNER JOIN [MEAT_P1].[CommerciaNet].[dbo].[Cliente] CLIENT
        ON RCLI2.Referencia = CLIENT.ClienteId or Client.CODIGOID = RCLI2.Referencia 
    WHERE PLG.ProcesoId = 31
      AND SAL.FechaHora >= @FechaInicio;

    /* =========================
       DEPURAR SEGUN LA LLAVE DEL INDICE UNICO
       Sucursal + CodigoEtiqueta + FechaDevolucion + SolicitudSurtidoId
       ========================= */
    ;WITH CTE_Devoluciones AS
    (
        SELECT
            T.Sucursal,
            T.FechaSurtido,
            T.FechaDevolucion,
            T.CodigoSap,
            T.Cliente,
            T.Remision,
            T.SolicitudSurtidoId,
            T.Articulo,
            T.CodigoEtiqueta,
            T.Peso,
            T.Lote,
            RN = ROW_NUMBER() OVER
            (
                PARTITION BY
                    T.Sucursal,
                    T.CodigoEtiqueta,
                    T.FechaDevolucion,
                    T.SolicitudSurtidoId
                ORDER BY
                    T.FechaSurtido DESC,
                    ISNULL(T.Remision, '') DESC,
                    ISNULL(T.Articulo, '') DESC,
                    ISNULL(T.Peso, 0) DESC,
                    ISNULL(T.Lote, '') DESC
            )
        FROM #DevolucionesTemp T
    )
    INSERT INTO #DevolucionesMerge
    (
        Sucursal,
        FechaSurtido,
        FechaDevolucion,
        CodigoSap,
        Cliente,
        Remision,
        SolicitudSurtidoId,
        Articulo,
        CodigoEtiqueta,
        Peso,
        Lote
    )
    SELECT
        Sucursal,
        FechaSurtido,
        FechaDevolucion,
        CodigoSap,
        Cliente,
        Remision,
        SolicitudSurtidoId,
        Articulo,
        CodigoEtiqueta,
        Peso,
        Lote
    FROM CTE_Devoluciones
    WHERE RN = 1;

    /* =========================
       MERGE ALINEADO AL INDICE UNICO
       ========================= */
    MERGE dbo.DevolucionMeat AS DEST
    USING #DevolucionesMerge AS SRC
       ON DEST.Sucursal = SRC.Sucursal
      AND DEST.CodigoEtiqueta = SRC.CodigoEtiqueta
      AND DEST.FechaDevolucion = SRC.FechaDevolucion
      AND DEST.SolicitudSurtidoId = SRC.SolicitudSurtidoId

    WHEN MATCHED THEN
        UPDATE SET
            DEST.FechaSurtido       = SRC.FechaSurtido,
            DEST.CodigoSap          = SRC.CodigoSap,
            DEST.Cliente            = SRC.Cliente,
            DEST.Remision           = SRC.Remision,
            DEST.Articulo           = SRC.Articulo,
            DEST.Peso               = SRC.Peso,
            DEST.Lote               = SRC.Lote,
            DEST.FechaActualizacion = GETDATE()

    WHEN NOT MATCHED BY TARGET THEN
        INSERT
        (
            Sucursal,
            FechaSurtido,
            FechaDevolucion,
            CodigoSap,
            Cliente,
            Remision,
            SolicitudSurtidoId,
            Articulo,
            CodigoEtiqueta,
            Peso,
            Lote,
            FechaActualizacion
        )
        VALUES
        (
            SRC.Sucursal,
            SRC.FechaSurtido,
            SRC.FechaDevolucion,
            SRC.CodigoSap,
            SRC.Cliente,
            SRC.Remision,
            SRC.SolicitudSurtidoId,
            SRC.Articulo,
            SRC.CodigoEtiqueta,
            SRC.Peso,
            SRC.Lote,
            GETDATE()
        );

    /* =========================
       DELETE DE REGISTROS QUE YA NO EXISTEN EN ORIGEN
       ========================= */
    DELETE DEST
    FROM dbo.DevolucionMeat DEST
    WHERE DEST.FechaSurtido >= @FechaInicio
      AND NOT EXISTS
      (
          SELECT 1
          FROM #DevolucionesMerge SRC
          WHERE SRC.Sucursal = DEST.Sucursal
            AND SRC.CodigoEtiqueta = DEST.CodigoEtiqueta
            AND SRC.FechaDevolucion = DEST.FechaDevolucion
            AND SRC.SolicitudSurtidoId = DEST.SolicitudSurtidoId
      );

    DROP TABLE #DevolucionesMerge;
    DROP TABLE #DevolucionesTemp;
END
GO