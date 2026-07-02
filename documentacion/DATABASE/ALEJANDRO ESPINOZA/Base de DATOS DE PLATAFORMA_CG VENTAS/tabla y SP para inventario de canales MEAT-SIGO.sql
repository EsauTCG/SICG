CREATE TABLE dbo.InventarioCanales (
    FechaProduccion DATE,
    Sku NVARCHAR(50),
    Articulo NVARCHAR(200),
    Etiqueta NVARCHAR(50) NOT NULL,
    Lote NVARCHAR(100),
    AlmacenId NVARCHAR(100),
    Almacen NVARCHAR(100),
    Kg DECIMAL(18,2),
    ClasificacionPesoCaliente NVARCHAR(50),
    FechaActualizacion DATETIME NOT NULL DEFAULT SYSDATETIME(),

    CONSTRAINT PK_InventarioPesoCaliente
        PRIMARY KEY (Etiqueta)
);



alter  PROCEDURE dbo.Sync_InventarioPesoCaliente
AS
BEGIN
    SET NOCOUNT ON;

    /*=========================================================
      FUENTE
    =========================================================*/
    ;WITH SRC AS
    (
        SELECT
            MAX(CONVERT(DATE, a.FechaProduccion)) AS FechaProduccion,

            ISNULL(
                CASE 
                    WHEN c.Nombre LIKE '%SACT%' THEN g.ArticuloId
                    ELSE a.Articulo
                END,
                'Sin clasificar'
            ) AS Sku,

            ISNULL(
                CASE 
                    WHEN c.Nombre LIKE '%SACT%' THEN g.Nombre
                    ELSE d.Nombre
                END,
                'Sin clasificar'
            ) AS Articulo,

            a.CodigoEtiqueta AS Etiqueta,
            c.Nombre AS Lote,
            b.AlmacenId,
            b.Nombre AS Almacen,
            SUM(a.PesoNeto) AS Kg,
            pc.ClasificacionPesoCaliente
        FROM MEAT_TIF.TIF_Meat.dbo.Produccion a
        INNER JOIN MEAT_TIF.TIF_CommerciaNET.dbo.Almacen b
            ON a.Almacen = b.AlmacenId
        INNER JOIN MEAT_TIF.TIF_Meat.dbo.Lote c
            ON a.LoteId = c.LoteId
        INNER JOIN MEAT_TIF.TIF_CommerciaNET.dbo.Articulo d
            ON a.Articulo = d.ArticuloId
        LEFT JOIN MEAT_TIF.TIF_Meat.dbo.CanalDetalle e
            ON a.ProduccionId = e.ProduccionId
        LEFT JOIN MEAT_TIF.TIF_Meat.dbo.Clasificacion f
            ON e.ClasificacionId = f.ClasificacionId
        LEFT JOIN MEAT_TIF.TIF_CommerciaNET.dbo.Articulo g
            ON f.ClasificacionId = g.Clasifica1
        INNER JOIN MEAT_TIF.TIF_Meat.dbo.LogPesoCalienteReclasificacion pc
            ON a.ProduccionId = pc.ProduccionId
        WHERE 
            a.Estatus = 1
            AND pc.ClasificacionPesoCaliente IS NOT NULL
            AND LTRIM(RTRIM(pc.ClasificacionPesoCaliente)) <> ''
            AND pc.ClasificacionPesoCaliente <> 'MAQUILAS'
        GROUP BY 
            c.Nombre,
            a.CodigoEtiqueta,
            b.AlmacenId,
            b.Nombre,
            a.Articulo,
            d.Nombre,
            g.ArticuloId,
            g.Nombre,
            a.ProduccionId,
            pc.ClasificacionPesoCaliente
    )

    /*=========================================================
      MERGE FINAL
    =========================================================*/
    MERGE dbo.InventarioCanales AS DEST
    USING SRC
        ON DEST.Etiqueta = SRC.Etiqueta

    WHEN MATCHED AND (
           ISNULL(DEST.Sku,'') <> ISNULL(SRC.Sku,'')
        OR ISNULL(DEST.Articulo,'') <> ISNULL(SRC.Articulo,'')
        OR ISNULL(DEST.Lote,'') <> ISNULL(SRC.Lote,'')
        OR ISNULL(DEST.AlmacenId,0) <> ISNULL(SRC.AlmacenId,0)
        OR ISNULL(DEST.Kg,0) <> ISNULL(SRC.Kg,0)
        OR ISNULL(DEST.ClasificacionPesoCaliente,'') <> ISNULL(SRC.ClasificacionPesoCaliente,'')
    )
    THEN UPDATE SET
        DEST.FechaProduccion = SRC.FechaProduccion,
        DEST.Sku = SRC.Sku,
        DEST.Articulo = SRC.Articulo,
        DEST.Lote = SRC.Lote,
        DEST.AlmacenId = SRC.AlmacenId,
        DEST.Almacen = SRC.Almacen,
        DEST.Kg = SRC.Kg,
        DEST.ClasificacionPesoCaliente = SRC.ClasificacionPesoCaliente,
        DEST.FechaActualizacion = SYSDATETIME()

    WHEN NOT MATCHED BY TARGET THEN
        INSERT (
            FechaProduccion,
            Sku,
            Articulo,
            Etiqueta,
            Lote,
            AlmacenId,
            Almacen,
            Kg,
            ClasificacionPesoCaliente,
            FechaActualizacion
        )
        VALUES (
            SRC.FechaProduccion,
            SRC.Sku,
            SRC.Articulo,
            SRC.Etiqueta,
            SRC.Lote,
            SRC.AlmacenId,
            SRC.Almacen,
            SRC.Kg,
            SRC.ClasificacionPesoCaliente,
            SYSDATETIME()
        )

    WHEN NOT MATCHED BY SOURCE THEN
        DELETE;
END;
GO
