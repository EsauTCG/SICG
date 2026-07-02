CREATE  VIEW dbo.vw_AvisosMovilizacion_TIF
AS

WITH VentaTIF AS (
    SELECT
        se.ProduccionId,
        ss.SolicitudSurtidoId,

        FechaVenta = CONVERT(date, ss.FechaSurtido),

        Venta = MAX(CASE 
            WHEN sr.TipoReferenciaId = 9 
            THEN CONVERT(nvarchar(200), LTRIM(RTRIM(sr.Referencia))) COLLATE Modern_Spanish_CI_AS 
        END),

        Cliente = MAX(CASE 
            WHEN sr.TipoReferenciaId = 6 
            THEN CONVERT(nvarchar(200), LTRIM(RTRIM(sr.Referencia))) COLLATE Modern_Spanish_CI_AS 
        END)

    FROM TIF_MEAT.dbo.SolicitudSurtido ss
    INNER JOIN TIF_MEAT.dbo.SalidaEmbarque se
        ON se.SolicitudSurtidoId = ss.SolicitudSurtidoId
    LEFT JOIN TIF_MEAT.dbo.SurtidoReferencia sr
        ON sr.SolicitudSurtidoId = ss.SolicitudSurtidoId
       AND sr.TipoReferenciaId IN (6, 9)

    GROUP BY
        se.ProduccionId,
        ss.SolicitudSurtidoId,
        CONVERT(date, ss.FechaSurtido)
),

ProdTIF AS (
    SELECT
        pr.ProduccionId,

        SKU = CONVERT(nvarchar(50), pr.Articulo) COLLATE Modern_Spanish_CI_AS,

        FechaProduccion = CONVERT(date, pr.FechaProduccion),

        PesoKg = CAST(ISNULL(pr.PesoNeto, 0) AS decimal(18,4)),

        pr.LoteId,

        CodigoEtiquetaProd = CONVERT(nvarchar(200), pr.CodigoEtiqueta) COLLATE Modern_Spanish_CI_AS

    FROM TIF_MEAT.dbo.Produccion pr
    INNER JOIN (
        SELECT DISTINCT ProduccionId
        FROM VentaTIF
    ) v
        ON v.ProduccionId = pr.ProduccionId
),

LogBase AS (
    SELECT
        CodigoEtiqueta = CONVERT(nvarchar(200), LTRIM(RTRIM(pel.CodigoEtiqueta))) COLLATE Modern_Spanish_CI_AS,

        pel.ProduccionId,
        pel.EtiquetacionId,
        pel.FechaHoraEvento,

        rn = ROW_NUMBER() OVER (
            PARTITION BY CONVERT(nvarchar(200), LTRIM(RTRIM(pel.CodigoEtiqueta))) COLLATE Modern_Spanish_CI_AS
            ORDER BY pel.FechaHoraEvento DESC
        )

    FROM TIF_MEAT.dbo.ProduccionEtiquetacionLog pel
    INNER JOIN ProdTIF pt
        ON pt.ProduccionId = pel.ProduccionId
    WHERE pel.CodigoEtiqueta IS NOT NULL
),

LogTIF AS (
    SELECT
        CodigoEtiqueta,
        ProduccionId,
        EtiquetacionId,
        FechaHoraEvento
    FROM LogBase
    WHERE rn = 1
),

FechasTIF AS (
    SELECT
        pt.ProduccionId,

        FechaProduccion = pt.FechaProduccion,

        FechaCaducidad = cj.FechaCaducidad,

        FechaSacrificio = CASE 
            WHEN CHARINDEX('RETT', ISNULL(pt.CodigoEtiquetaProd, N'')) > 0
                 OR EXISTS (
                        SELECT 1
                        FROM TIF_MEAT.dbo.Lote lRet WITH (NOLOCK)
                        WHERE lRet.LoteId = pt.LoteId
                          AND lRet.Nombre LIKE N'RETT%'
                    )
                THEN can.FechaSacrificio
            ELSE COALESCE(
                srSac.FechaSacrificio,
                pCanal.FechaSacrificio
            )
        END

    FROM ProdTIF pt

    OUTER APPLY (
        SELECT
            FechaCaducidad = CONVERT(date, MAX(c.FechaCaducidad))
        FROM TIF_MEAT.dbo.Caja c
        WHERE c.ProduccionId = pt.ProduccionId
    ) cj

    OUTER APPLY (
        SELECT
            FechaSacrificio = COALESCE(
                MAX(TRY_CONVERT(date, sr.Referencia, 103)),
                MAX(TRY_CONVERT(date, sr.Referencia))
            )
        FROM TIF_MEAT.dbo.SolicitudReferencia sr
        WHERE sr.SolicitudProduccionId = pt.LoteId
          AND sr.TipoReferenciaId = 47
    ) srSac

    OUTER APPLY (
        SELECT
            FechaSacrificio = MIN(CONVERT(date, pc.FechaProduccion))
        FROM TIF_MEAT.dbo.ProduccionLogistica pl
        INNER JOIN TIF_MEAT.dbo.Produccion pc
            ON pc.ProduccionId = pl.ProduccionId
        WHERE pl.SolicitudProduccionId = pt.LoteId
    ) pCanal

    OUTER APPLY (
        SELECT
            FechaSacrificio = COALESCE(
                MAX(
                    COALESCE(
                        TRY_CONVERT(date, srSac2.Referencia, 103),
                        TRY_CONVERT(date, srSac2.Referencia)
                    )
                ),
                MAX(CONVERT(date, pCan.FechaProduccion)),
                MIN(CONVERT(date, pCaj.FechaProduccion)),
                MIN(CONVERT(date, pCajRet.FechaProduccion))
            )
        FROM TIF_MEAT.dbo.ProduccionLogistica plCajRet
        INNER JOIN TIF_MEAT.dbo.Produccion pCajRet WITH (NOLOCK)
            ON pCajRet.ProduccionId = plCajRet.ProduccionId
        INNER JOIN TIF_MEAT.dbo.Lote lCajDes WITH (NOLOCK)
            ON lCajDes.LoteId = pCajRet.LoteId
        LEFT JOIN TIF_MEAT.dbo.ProduccionLogistica plDes WITH (NOLOCK)
            ON plDes.SolicitudProduccionId = lCajDes.LoteId
        LEFT JOIN TIF_MEAT.dbo.Produccion pCaj WITH (NOLOCK)
            ON pCaj.ProduccionId = plDes.ProduccionId
        LEFT JOIN TIF_MEAT.dbo.ProduccionLogistica plCan WITH (NOLOCK)
            ON plCan.SolicitudProduccionId = pCaj.LoteId
        LEFT JOIN TIF_MEAT.dbo.Produccion pCan WITH (NOLOCK)
            ON pCan.ProduccionId = plCan.ProduccionId
        LEFT JOIN TIF_MEAT.dbo.SolicitudReferencia srSac2
            ON srSac2.SolicitudProduccionId = pCan.LoteId
           AND srSac2.TipoReferenciaId = 47
        WHERE plCajRet.SolicitudProduccionId = pt.LoteId
          AND lCajDes.TipoLoteId > 3
    ) can
)

SELECT
    planta = CONVERT(nvarchar(10), N'TIF') COLLATE Modern_Spanish_CI_AS,

    solicitud_surtido_id = vt.SolicitudSurtidoId,

    venta = COALESCE(
        NULLIF(vt.Venta, N''),
        N'SIN VENTA'
    ) COLLATE Modern_Spanish_CI_AS,

    cliente = COALESCE(
        NULLIF(vt.Cliente, N''),
        N'SIN CLIENTE'
    ) COLLATE Modern_Spanish_CI_AS,

    fecha_venta = vt.FechaVenta,

    fecha_venta_txt = CONVERT(varchar(10), vt.FechaVenta, 103),

    sku = COALESCE(
        NULLIF(pt.SKU, N''),
        N'SIN SKU'
    ) COLLATE Modern_Spanish_CI_AS,

    producto = COALESCE(
        NULLIF(art.Nombre, N''),
        N'SIN PRODUCTO'
    ) COLLATE Modern_Spanish_CI_AS,

    lote = COALESCE(
        CONVERT(nvarchar(200), LTRIM(RTRIM(l.Nombre))) COLLATE Modern_Spanish_CI_AS,
        N'SIN LOTE'
    ) COLLATE Modern_Spanish_CI_AS,

    fecha_sacrificio = ft.FechaSacrificio,

    fecha_sacrificio_txt = CONVERT(varchar(10), ft.FechaSacrificio, 103),

    fecha_produccion = ft.FechaProduccion,

    fecha_produccion_txt = CONVERT(varchar(10), ft.FechaProduccion, 103),

    fecha_caducidad = ft.FechaCaducidad,

    fecha_caducidad_txt = CONVERT(varchar(10), ft.FechaCaducidad, 103),

    cuenta_de_etiqueta = COUNT(1),

    suma_de_kg = CAST(SUM(pt.PesoKg) AS decimal(18,3))

FROM LogTIF lg
INNER JOIN ProdTIF pt
    ON pt.ProduccionId = lg.ProduccionId
INNER JOIN VentaTIF vt
    ON vt.ProduccionId = pt.ProduccionId
LEFT JOIN TIF_MEAT.dbo.Lote l
    ON l.LoteId = pt.LoteId
LEFT JOIN FechasTIF ft
    ON ft.ProduccionId = pt.ProduccionId
INNER JOIN TIF_CommerciaNET.dbo.Articulo art
    ON pt.SKU = art.ArticuloId

GROUP BY
    vt.SolicitudSurtidoId,
    vt.Venta,
    vt.Cliente,
    vt.FechaVenta,
    pt.SKU,
    art.Nombre,
    l.Nombre,
    ft.FechaSacrificio,
    ft.FechaProduccion,
    ft.FechaCaducidad;
GO

----------------------INDICES -----------

USE TIF_MEAT;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_SolicitudSurtido_FechaSurtido'
      AND object_id = OBJECT_ID('dbo.SolicitudSurtido')
)
CREATE INDEX IX_SolicitudSurtido_FechaSurtido
ON dbo.SolicitudSurtido (FechaSurtido, SolicitudSurtidoId);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_SalidaEmbarque_SolicitudSurtido_Produccion'
      AND object_id = OBJECT_ID('dbo.SalidaEmbarque')
)
CREATE INDEX IX_SalidaEmbarque_SolicitudSurtido_Produccion
ON dbo.SalidaEmbarque (SolicitudSurtidoId, ProduccionId);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_SurtidoReferencia_Solicitud_Tipo'
      AND object_id = OBJECT_ID('dbo.SurtidoReferencia')
)
CREATE INDEX IX_SurtidoReferencia_Solicitud_Tipo
ON dbo.SurtidoReferencia (SolicitudSurtidoId, TipoReferenciaId)
INCLUDE (Referencia);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Produccion_Lote'
      AND object_id = OBJECT_ID('dbo.Produccion')
)
CREATE INDEX IX_Produccion_Lote
ON dbo.Produccion (LoteId, ProduccionId)
INCLUDE (Articulo, FechaProduccion, PesoNeto, CodigoEtiqueta);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Produccion_ProduccionId'
      AND object_id = OBJECT_ID('dbo.Produccion')
)
CREATE INDEX IX_Produccion_ProduccionId
ON dbo.Produccion (ProduccionId)
INCLUDE (Articulo, FechaProduccion, PesoNeto, LoteId, CodigoEtiqueta);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_ProduccionEtiquetacionLog_Produccion'
      AND object_id = OBJECT_ID('dbo.ProduccionEtiquetacionLog')
)
CREATE INDEX IX_ProduccionEtiquetacionLog_Produccion
ON dbo.ProduccionEtiquetacionLog (ProduccionId, CodigoEtiqueta, FechaHoraEvento DESC)
INCLUDE (EtiquetacionId);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_ProduccionEtiquetacionLog_CodigoEtiqueta'
      AND object_id = OBJECT_ID('dbo.ProduccionEtiquetacionLog')
)
CREATE INDEX IX_ProduccionEtiquetacionLog_CodigoEtiqueta
ON dbo.ProduccionEtiquetacionLog (CodigoEtiqueta, FechaHoraEvento DESC)
INCLUDE (ProduccionId, EtiquetacionId);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Caja_Produccion'
      AND object_id = OBJECT_ID('dbo.Caja')
)
CREATE INDEX IX_Caja_Produccion
ON dbo.Caja (ProduccionId)
INCLUDE (FechaCaducidad);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_SolicitudReferencia_Solicitud_Tipo'
      AND object_id = OBJECT_ID('dbo.SolicitudReferencia')
)
CREATE INDEX IX_SolicitudReferencia_Solicitud_Tipo
ON dbo.SolicitudReferencia (SolicitudProduccionId, TipoReferenciaId)
INCLUDE (Referencia);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_ProduccionLogistica_Solicitud_Produccion'
      AND object_id = OBJECT_ID('dbo.ProduccionLogistica')
)
CREATE INDEX IX_ProduccionLogistica_Solicitud_Produccion
ON dbo.ProduccionLogistica (SolicitudProduccionId, ProduccionId);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Lote_LoteId'
      AND object_id = OBJECT_ID('dbo.Lote')
)
CREATE INDEX IX_Lote_LoteId
ON dbo.Lote (LoteId)
INCLUDE (Nombre, TipoLoteId);
GO


IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Lote_Nombre'
      AND object_id = OBJECT_ID('dbo.Lote')
)
CREATE INDEX IX_Lote_Nombre
ON dbo.Lote (Nombre)
INCLUDE (LoteId, TipoLoteId);
GO



USE TIF_CommerciaNET;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Articulo_ArticuloId'
      AND object_id = OBJECT_ID('dbo.Articulo')
)
CREATE INDEX IX_Articulo_ArticuloId
ON dbo.Articulo (ArticuloId)
INCLUDE (Nombre);
GO