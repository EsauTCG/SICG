-- Tabla de encabezados
CREATE TABLE dbo.SurtidoEncabezado (
    SolicitudSurtidoId INT        NOT NULL,
    Pedido             NVARCHAR(50)  NULL,
    Remision           NVARCHAR(50)  NULL,
    FechaValidacion    DATETIME      NULL,
    Sucursal           NVARCHAR(50)  NOT NULL, -- 'PLANTA 1' o 'PLANTA TIF'
    FechaActualizacion DATETIME      NOT NULL DEFAULT(GETDATE()),
    CodigoSap VARCHAR(50) NULL,
    CONSTRAINT PK_SurtidoEnc PRIMARY KEY (SolicitudSurtidoId, Sucursal)
);

-- Tabla de detalles
CREATE TABLE dbo.SurtidoDetalle (
    SolicitudSurtidoId INT           NOT NULL,
    Sucursal           NVARCHAR(50)  NOT NULL,
    Articulo           NVARCHAR(100) NOT NULL,
    Kg                 DECIMAL(18,2) NOT NULL,
    Cajas INT NULL,
    FechaActualizacion DATETIME      NOT NULL DEFAULT(GETDATE()),
    CONSTRAINT PK_SurtidoDet PRIMARY KEY (SolicitudSurtidoId, Sucursal, Articulo)
);


select * from SurtidoEncabezado
select * from SurtidoDetalle



------PROCEDIMIENTO PARA TRAER LAS SOLICITUDID DE MEAT A SIGO
--USE [SistemaIntegralCG]
--GO

--ALTER PROCEDURE dbo.SincronizarSurtidosMeat
--AS
--BEGIN
--    SET NOCOUNT ON;

--    /* ============================================================
--       1) TABLAS TEMPORALES
--       ============================================================ */
--    IF OBJECT_ID('tempdb..#EncabezadoTemp') IS NOT NULL DROP TABLE #EncabezadoTemp;
--    IF OBJECT_ID('tempdb..#DetalleTemp')    IS NOT NULL DROP TABLE #DetalleTemp;

--    CREATE TABLE #EncabezadoTemp
--    (
--        SolicitudSurtidoId INT       NOT NULL,
--        Pedido             VARCHAR(50),
--        Remision           VARCHAR(50),
--        FechaValidacion    DATETIME,
--        Sucursal           VARCHAR(50)   -- 'PLANTA 1' o 'PLANTA TIF'
--    );

--    CREATE TABLE #DetalleTemp
--    (
--        SolicitudSurtidoId INT        NOT NULL,
--        Articulo           VARCHAR(100),
--        Kg                 DECIMAL(18,2),
--        Sucursal           VARCHAR(50)  -- para distinguir P1/TIF
--    );

--    /* ============================================================
--       2) POBLAR TEMPORALES DESDE MEAT_P1
--       ============================================================ */
--    INSERT INTO #EncabezadoTemp (SolicitudSurtidoId, Pedido, Remision, FechaValidacion, Sucursal)
--    SELECT 
--        c.solicitudsurtidoid,
--        d.referencia AS Pedido,
--        e.referencia AS Remision,
--        e.fechahora  AS FechaValidacion,
--        'PLANTA 1'   AS Sucursal
--    FROM [MEAT_P1].[meat].[dbo].[Solicitudsurtido] c
--    INNER JOIN [MEAT_P1].[meat].[dbo].[SurtidoReferencia] d
--        ON c.solicitudsurtidoid = d.solicitudsurtidoid
--       AND d.tiporeferenciaid = 9
--    INNER JOIN [MEAT_P1].[meat].[dbo].[SurtidoReferencia] e
--        ON c.solicitudsurtidoid = e.solicitudsurtidoid
--       AND e.tiporeferenciaid = 12
--    WHERE c.estatusid = 3
--      AND e.fechahora >= '2025-10-01'          -- 🔹 filtro fijo de fecha
--    GROUP BY 
--        c.solicitudsurtidoid,
--        d.referencia,
--        e.referencia,
--        e.fechahora;

--    INSERT INTO #DetalleTemp (SolicitudSurtidoId, Articulo, Kg, Sucursal)
--    SELECT 
--        a.solicitudsurtidoid,
--        b.Articulo,
--        SUM(b.pesoneto) AS Kg,
--        'PLANTA 1'      AS Sucursal
--    FROM [MEAT_P1].[meat].[dbo].[salidaembarque] a
--    INNER JOIN [MEAT_P1].[meat].[dbo].[PRODUCCION]   b ON a.produccionid    = b.produccionid
--    INNER JOIN [MEAT_P1].[meat].[dbo].[Solicitudsurtido] c ON a.solicitudsurtidoid = c.solicitudsurtidoid
--    INNER JOIN [MEAT_P1].[meat].[dbo].[SurtidoReferencia] d ON a.solicitudsurtidoid = d.solicitudsurtidoid AND d.tiporeferenciaid = 9
--    INNER JOIN [MEAT_P1].[meat].[dbo].[SurtidoReferencia] e ON a.solicitudsurtidoid = e.solicitudsurtidoid AND e.tiporeferenciaid = 12
--    WHERE c.estatusid = 3
--      AND e.fechahora >= '2025-10-01'          -- 🔹 mismo filtro
--    GROUP BY 
--        a.solicitudsurtidoid,
--        b.Articulo;

--    /* ============================================================
--       3) POBLAR TEMPORALES DESDE MEAT_TIF
--       ============================================================ */
--    INSERT INTO #EncabezadoTemp (SolicitudSurtidoId, Pedido, Remision, FechaValidacion, Sucursal)
--    SELECT 
--        c.solicitudsurtidoid,
--        d.referencia AS Pedido,
--        e.referencia AS Remision,
--        e.fechahora  AS FechaValidacion,
--        'PLANTA TIF' AS Sucursal
--    FROM [MEAT_TIF].[TIF_meat].[dbo].[Solicitudsurtido] c
--    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[SurtidoReferencia] d
--        ON c.solicitudsurtidoid = d.solicitudsurtidoid
--       AND d.tiporeferenciaid = 9
--    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[SurtidoReferencia] e
--        ON c.solicitudsurtidoid = e.solicitudsurtidoid
--       AND e.tiporeferenciaid = 12
--    WHERE c.estatusid = 3
--      AND e.fechahora >= '2025-10-01'          -- 🔹 filtro fijo
--    GROUP BY 
--        c.solicitudsurtidoid,
--        d.referencia,
--        e.referencia,
--        e.fechahora;

--    INSERT INTO #DetalleTemp (SolicitudSurtidoId, Articulo, Kg, Sucursal)
--    SELECT 
--        a.solicitudsurtidoid,
--        b.Articulo,
--        SUM(b.pesoneto) AS Kg,
--        'PLANTA TIF'    AS Sucursal
--    FROM [MEAT_TIF].[TIF_meat].[dbo].[salidaembarque] a
--    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[PRODUCCION]   b ON a.produccionid    = b.produccionid
--    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[Solicitudsurtido] c ON a.solicitudsurtidoid = c.solicitudsurtidoid
--    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[SurtidoReferencia] d ON a.solicitudsurtidoid = d.solicitudsurtidoid AND d.tiporeferenciaid = 9
--    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[SurtidoReferencia] e ON a.solicitudsurtidoid = e.solicitudsurtidoid AND e.tiporeferenciaid = 12
--    WHERE c.estatusid = 3
--      AND e.fechahora >= '2025-10-01'          -- 🔹 mismo filtro
--    GROUP BY 
--        a.solicitudsurtidoid,
--        b.Articulo;

--    /* ============================================================
--       4) MERGE ENCABEZADOS (upsert + delete)
--       ============================================================ */
--    MERGE dbo.SurtidoEncabezado AS dest
--    USING #EncabezadoTemp AS src
--       ON  dest.SolicitudSurtidoId = src.SolicitudSurtidoId
--       AND dest.Sucursal           = src.Sucursal
--    WHEN MATCHED 
--         AND (
--                ISNULL(dest.Pedido,        '') <> ISNULL(src.Pedido,        '')
--             OR ISNULL(dest.Remision,      '') <> ISNULL(src.Remision,      '')
--             OR ISNULL(dest.FechaValidacion,'19000101') <> ISNULL(src.FechaValidacion,'19000101')
--             )
--        THEN UPDATE SET
--             dest.Pedido             = src.Pedido,
--             dest.Remision           = src.Remision,
--             dest.FechaValidacion    = src.FechaValidacion,
--             dest.FechaActualizacion = GETDATE()
--    WHEN NOT MATCHED BY TARGET
--        THEN INSERT (SolicitudSurtidoId, Pedido, Remision, FechaValidacion, Sucursal, FechaActualizacion)
--             VALUES (src.SolicitudSurtidoId, src.Pedido, src.Remision, src.FechaValidacion, src.Sucursal, GETDATE())
--    ;

--    -- Eliminar encabezados que ya no existan en origen (solo rango que estamos manejando)
--    DELETE dest
--    FROM dbo.SurtidoEncabezado dest
--    WHERE dest.Sucursal IN ('PLANTA 1','PLANTA TIF')
--      AND dest.FechaValidacion >= '2025-10-01'
--      AND NOT EXISTS (
--            SELECT 1
--            FROM #EncabezadoTemp src
--            WHERE src.SolicitudSurtidoId = dest.SolicitudSurtidoId
--              AND src.Sucursal           = dest.Sucursal
--      );

--    /* ============================================================
--       5) MERGE DETALLES (upsert + delete)
--       ============================================================ */
--    MERGE dbo.SurtidoDetalle AS dest
--    USING #DetalleTemp AS src
--       ON  dest.SolicitudSurtidoId = src.SolicitudSurtidoId
--       AND dest.Articulo           = src.Articulo
--       AND dest.Sucursal           = src.Sucursal
--    WHEN MATCHED 
--         AND (ROUND(dest.Kg,2) <> ROUND(src.Kg,2))
--        THEN UPDATE SET
--             dest.Kg                 = src.Kg,
--             dest.FechaActualizacion = GETDATE()
--    WHEN NOT MATCHED BY TARGET
--        THEN INSERT (SolicitudSurtidoId, Articulo, Kg, Sucursal, FechaActualizacion)
--             VALUES (src.SolicitudSurtidoId, src.Articulo, src.Kg, src.Sucursal, GETDATE())
--    ;

--    -- Eliminar detalles que ya no existan para las solicitudes de este rango
--    DELETE dest
--    FROM dbo.SurtidoDetalle dest
--    WHERE dest.Sucursal IN ('PLANTA 1','PLANTA TIF')
--      AND EXISTS (
--          SELECT 1
--          FROM dbo.SurtidoEncabezado h
--          WHERE h.SolicitudSurtidoId = dest.SolicitudSurtidoId
--            AND h.Sucursal          = dest.Sucursal
--            AND h.FechaValidacion >= '2025-10-01'
--      )
--      AND NOT EXISTS (
--            SELECT 1
--            FROM #DetalleTemp src
--            WHERE src.SolicitudSurtidoId = dest.SolicitudSurtidoId
--              AND src.Articulo           = dest.Articulo
--              AND src.Sucursal           = dest.Sucursal
--      );

--END
--GO


--PROCEDIMIENTO NUEVO 




USE [SIGO]
GO
/****** Object:  StoredProcedure [dbo].[SincronizarSurtidosMeat]    Script Date: 14/04/2026 10:14:27 a. m. ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[SincronizarSurtidosMeat]
AS
BEGIN
    SET NOCOUNT ON;

    /* ============================================================
       1) TABLAS TEMPORALES
       ============================================================ */
    IF OBJECT_ID('tempdb..#EncabezadoTemp') IS NOT NULL DROP TABLE #EncabezadoTemp;
    IF OBJECT_ID('tempdb..#DetalleTemp')    IS NOT NULL DROP TABLE #DetalleTemp;

    CREATE TABLE #EncabezadoTemp
    (
        SolicitudSurtidoId INT       NOT NULL,
        Pedido             VARCHAR(50),
        Remision           VARCHAR(50),
        FechaValidacion    DATETIME,
        Sucursal           VARCHAR(50),   -- 'PLANTA 1' o 'PLANTA TIF'
        CodigoSap          VARCHAR(50)
    );

    CREATE TABLE #DetalleTemp
    (
        SolicitudSurtidoId INT             NOT NULL,
        Articulo           VARCHAR(100),
        Kg                 DECIMAL(18,2),
        Cajas              INT,
        Sucursal           VARCHAR(50)
    );

    /* ============================================================
       2) POBLAR TEMPORALES DESDE MEAT_P1
       ============================================================ */
    INSERT INTO #EncabezadoTemp
    (
        SolicitudSurtidoId,
        Pedido,
        Remision,
        FechaValidacion,
        Sucursal,
        CodigoSap
    )
    SELECT 
        c.solicitudsurtidoid,
        d.referencia AS Pedido,
        e.referencia AS Remision,
        e.fechahora  AS FechaValidacion,
        'PLANTA 1'   AS Sucursal,
        g.CodigoId   AS CodigoSap
    FROM [MEAT_P1].[meat].[dbo].[Solicitudsurtido] c
    INNER JOIN [MEAT_P1].[meat].[dbo].[SurtidoReferencia] d
        ON c.solicitudsurtidoid = d.solicitudsurtidoid
       AND d.tiporeferenciaid = 9
    INNER JOIN [MEAT_P1].[meat].[dbo].[SurtidoReferencia] e
        ON c.solicitudsurtidoid = e.solicitudsurtidoid
       AND e.tiporeferenciaid = 12
    INNER JOIN [MEAT_P1].CommerciaNet.DBO.Documento f
        ON e.Referencia = f.EmpresaId + '.' + f.SucursalId + '.' + f.OperacionId + '.' + f.Folio
    INNER JOIN [MEAT_P1].CommerciaNet.dbo.Cliente g
        ON f.ClienteProveedorId = g.ClienteId
    WHERE c.estatusid = 3
      AND e.fechahora >= '2025-10-01'
    GROUP BY 
        c.solicitudsurtidoid,
        d.referencia,
        e.referencia,
        e.fechahora,
        g.CodigoId;

    INSERT INTO #DetalleTemp
    (
        SolicitudSurtidoId,
        Articulo,
        Kg,
        Cajas,
        Sucursal
    )
    SELECT 
        a.solicitudsurtidoid,
        b.Articulo,
        SUM(b.pesoneto) AS Kg,
        COUNT(a.produccionid) AS Cajas,
        'PLANTA 1' AS Sucursal
    FROM [MEAT_P1].[meat].[dbo].[salidaembarque] a
    INNER JOIN [MEAT_P1].[meat].[dbo].[PRODUCCION] b
        ON a.produccionid = b.produccionid
    INNER JOIN [MEAT_P1].[meat].[dbo].[Solicitudsurtido] c
        ON a.solicitudsurtidoid = c.solicitudsurtidoid
    INNER JOIN [MEAT_P1].[meat].[dbo].[SurtidoReferencia] d
        ON a.solicitudsurtidoid = d.solicitudsurtidoid
       AND d.tiporeferenciaid = 9
    INNER JOIN [MEAT_P1].[meat].[dbo].[SurtidoReferencia] e
        ON a.solicitudsurtidoid = e.solicitudsurtidoid
       AND e.tiporeferenciaid = 12
    WHERE c.estatusid = 3
      AND e.fechahora >= '2025-10-01'
    GROUP BY 
        a.solicitudsurtidoid,
        b.Articulo;

    /* ============================================================
       3) POBLAR TEMPORALES DESDE MEAT_TIF
       ============================================================ */
    INSERT INTO #EncabezadoTemp
    (
        SolicitudSurtidoId,
        Pedido,
        Remision,
        FechaValidacion,
        Sucursal,
        CodigoSap
    )
    SELECT 
        c.solicitudsurtidoid,
        d.referencia AS Pedido,
        e.referencia AS Remision,
        e.fechahora  AS FechaValidacion,
        'PLANTA TIF' AS Sucursal,
        g.CodigoId   AS CodigoSap
    FROM [MEAT_TIF].[TIF_meat].[dbo].[Solicitudsurtido] c
    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[SurtidoReferencia] d
        ON c.solicitudsurtidoid = d.solicitudsurtidoid
       AND d.tiporeferenciaid = 9
    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[SurtidoReferencia] e
        ON c.solicitudsurtidoid = e.solicitudsurtidoid
       AND e.tiporeferenciaid = 12
    INNER JOIN [MEAT_TIF].TIF_CommerciaNet.DBO.Documento f
        ON e.Referencia = f.EmpresaId + '.' + f.SucursalId + '.' + f.OperacionId + '.' + f.Folio
    INNER JOIN [MEAT_TIF].TIF_CommerciaNet.dbo.Cliente g
        ON f.ClienteProveedorId = g.ClienteId
    WHERE c.estatusid = 3
      AND e.fechahora >= '2025-10-01'
    GROUP BY 
        c.solicitudsurtidoid,
        d.referencia,
        e.referencia,
        e.fechahora,
        g.CodigoId;

    INSERT INTO #DetalleTemp
    (
        SolicitudSurtidoId,
        Articulo,
        Kg,
        Cajas,
        Sucursal
    )
    SELECT 
        a.solicitudsurtidoid,
        b.Articulo,
        SUM(b.pesoneto) AS Kg,
        COUNT(a.produccionid) AS Cajas,
        'PLANTA TIF' AS Sucursal
    FROM [MEAT_TIF].[TIF_meat].[dbo].[salidaembarque] a
    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[PRODUCCION] b
        ON a.produccionid = b.produccionid
    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[Solicitudsurtido] c
        ON a.solicitudsurtidoid = c.solicitudsurtidoid
    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[SurtidoReferencia] d
        ON a.solicitudsurtidoid = d.solicitudsurtidoid
       AND d.tiporeferenciaid = 9
    INNER JOIN [MEAT_TIF].[TIF_meat].[dbo].[SurtidoReferencia] e
        ON a.solicitudsurtidoid = e.solicitudsurtidoid
       AND e.tiporeferenciaid = 12
    WHERE c.estatusid = 3
      AND e.fechahora >= '2025-10-01'
    GROUP BY 
        a.solicitudsurtidoid,
        b.Articulo;

    /* ============================================================
       4) MERGE ENCABEZADOS (upsert + delete)
       ============================================================ */
    MERGE dbo.SurtidoEncabezado AS dest
    USING #EncabezadoTemp AS src
       ON  dest.SolicitudSurtidoId = src.SolicitudSurtidoId
       AND dest.Sucursal           = src.Sucursal
    WHEN MATCHED 
         AND (
                ISNULL(dest.Pedido, '') <> ISNULL(src.Pedido, '')
             OR ISNULL(dest.Remision, '') <> ISNULL(src.Remision, '')
             OR ISNULL(dest.FechaValidacion, '19000101') <> ISNULL(src.FechaValidacion, '19000101')
             OR ISNULL(dest.CodigoSap, '') <> ISNULL(src.CodigoSap, '')
         )
        THEN UPDATE SET
             dest.Pedido             = src.Pedido,
             dest.Remision           = src.Remision,
             dest.FechaValidacion    = src.FechaValidacion,
             dest.CodigoSap          = src.CodigoSap,
             dest.FechaActualizacion = GETDATE()
    WHEN NOT MATCHED BY TARGET
        THEN INSERT
        (
            SolicitudSurtidoId,
            Pedido,
            Remision,
            FechaValidacion,
            Sucursal,
            CodigoSap,
            FechaActualizacion
        )
        VALUES
        (
            src.SolicitudSurtidoId,
            src.Pedido,
            src.Remision,
            src.FechaValidacion,
            src.Sucursal,
            src.CodigoSap,
            GETDATE()
        )
    ;

    DELETE dest
    FROM dbo.SurtidoEncabezado dest
    WHERE dest.Sucursal IN ('PLANTA 1', 'PLANTA TIF')
      AND dest.FechaValidacion >= '2025-10-01'
      AND NOT EXISTS
      (
          SELECT 1
          FROM #EncabezadoTemp src
          WHERE src.SolicitudSurtidoId = dest.SolicitudSurtidoId
            AND src.Sucursal = dest.Sucursal
      );

    /* ============================================================
       5) MERGE DETALLES (upsert + delete)
       ============================================================ */
    MERGE dbo.SurtidoDetalle AS dest
    USING #DetalleTemp AS src
       ON  dest.SolicitudSurtidoId = src.SolicitudSurtidoId
       AND dest.Articulo           = src.Articulo
       AND dest.Sucursal           = src.Sucursal
    WHEN MATCHED 
         AND (
                ROUND(ISNULL(dest.Kg, 0), 2) <> ROUND(ISNULL(src.Kg, 0), 2)
             OR ISNULL(dest.Cajas, 0) <> ISNULL(src.Cajas, 0)
         )
        THEN UPDATE SET
             dest.Kg                 = src.Kg,
             dest.Cajas              = src.Cajas,
             dest.FechaActualizacion = GETDATE()
    WHEN NOT MATCHED BY TARGET
        THEN INSERT
        (
            SolicitudSurtidoId,
            Articulo,
            Kg,
            Cajas,
            Sucursal,
            FechaActualizacion
        )
        VALUES
        (
            src.SolicitudSurtidoId,
            src.Articulo,
            src.Kg,
            src.Cajas,
            src.Sucursal,
            GETDATE()
        )
    ;

    DELETE dest
    FROM dbo.SurtidoDetalle dest
    WHERE dest.Sucursal IN ('PLANTA 1', 'PLANTA TIF')
      AND EXISTS
      (
          SELECT 1
          FROM dbo.SurtidoEncabezado h
          WHERE h.SolicitudSurtidoId = dest.SolicitudSurtidoId
            AND h.Sucursal = dest.Sucursal
            AND h.FechaValidacion >= '2025-10-01'
      )
      AND NOT EXISTS
      (
          SELECT 1
          FROM #DetalleTemp src
          WHERE src.SolicitudSurtidoId = dest.SolicitudSurtidoId
            AND src.Articulo = dest.Articulo
            AND src.Sucursal = dest.Sucursal
      );

END
GO