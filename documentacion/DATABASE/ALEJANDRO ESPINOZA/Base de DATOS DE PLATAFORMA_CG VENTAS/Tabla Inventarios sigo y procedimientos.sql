--CREAR TABLA 
CREATE TABLE InventarioSigo (
    AlmacenId NVARCHAR(50) NOT NULL,
    Almacen NVARCHAR(100),
    ProductoCodigo NVARCHAR(50) NOT NULL,
    ProductoNombre NVARCHAR(200),
    Cajas INT,
    Kg DECIMAL(18, 2),
    Sucursal NVARCHAR(100),
    Colonia NVARCHAR(150) NULL,
    FechaActualizacion DATETIME DEFAULT GETDATE(),
    CONSTRAINT PK_InventarioSigo PRIMARY KEY (AlmacenId, ProductoCodigo)
);


--USE [SIGO]
--GO

--IF COL_LENGTH('dbo.InventarioSigo', 'Lote') IS NULL
--BEGIN
--    ALTER TABLE dbo.InventarioSigo
--    ADD Lote NVARCHAR(100) NULL;
--END
--GO


--PROCEDIMIENTO ALMACENADO PARA INSERTAR Y ACTUALIZAR INVENTARIO DE AMBAS PLANTAS

USE [SIGO]
GO
/****** Object:  StoredProcedure [dbo].[ActualizarInventarioSigo]    Script Date: 01/06/2026 11:14:03 a. m. ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[ActualizarInventarioSigo]
AS
BEGIN
    SET NOCOUNT ON;

    /*=========================================================
      1) TABLA TEMPORAL BASE
    =========================================================*/
    IF OBJECT_ID('tempdb..#InventarioTemp') IS NOT NULL DROP TABLE #InventarioTemp;

    CREATE TABLE #InventarioTemp (
        AlmacenId       NVARCHAR(50)  NULL,
        Almacen         NVARCHAR(100),
        Colonia         NVARCHAR(150) NULL,
        ProductoCodigo  NVARCHAR(50)  NOT NULL,
        ProductoNombre  NVARCHAR(200),
        Lote            NVARCHAR(100) NULL,
        Cajas           INT,
        Kg              DECIMAL(18,2),
        Sucursal        NVARCHAR(100)
    );

    /*=========================================================
      2) MEAT_P1
    =========================================================*/
    INSERT INTO #InventarioTemp (
        AlmacenId,
        Almacen,
        Colonia,
        ProductoCodigo,
        ProductoNombre,
        Lote,
        Cajas,
        Kg,
        Sucursal
    )
    SELECT 
        e.AlmacenId,
        ISNULL(e.Nombre,'-') AS Almacen,
        e.Colonia,
        CAST(d.ArticuloId AS NVARCHAR(50)) AS ProductoCodigo,
        d.Nombre AS ProductoNombre,
        g.Nombre AS Lote,
        COUNT(DISTINCT a.ProduccionId) AS Cajas,
        ROUND(SUM(a.PesoNeto),2) AS Kg,
        'PLANTA 1' AS Sucursal
    FROM [MEAT_P1].[Meat].[dbo].[Produccion] a
    INNER JOIN [MEAT_P1].[CommerciaNet].[dbo].[Articulo] d 
        ON a.Articulo = d.ArticuloId
    LEFT JOIN [MEAT_P1].[CommerciaNet].[dbo].[Almacen] e 
        ON a.Almacen = e.AlmacenId
    LEFT JOIN [MEAT_P1].[Meat].[dbo].[SalidaEmbarque] f 
        ON a.ProduccionId = f.ProduccionId
    LEFT JOIN [MEAT_P1].[Meat].[dbo].[Lote] g
        ON a.LoteId = g.LoteId
    WHERE a.Estatus = 1 
      AND e.Colonia NOT LIKE '%Frigorifico%'
    GROUP BY 
        e.AlmacenId,
        e.Nombre,
        e.Colonia,
        d.ArticuloId,
        d.Nombre,
        g.Nombre;

    /*=========================================================
      3) MEAT_TIF
    =========================================================*/
    INSERT INTO #InventarioTemp (
        AlmacenId,
        Almacen,
        Colonia,
        ProductoCodigo,
        ProductoNombre,
        Lote,
        Cajas,
        Kg,
        Sucursal
    )
    SELECT 
        e.AlmacenId,
        ISNULL(e.Nombre,'-') AS Almacen,
        e.Colonia,
        CAST(
            CASE 
                WHEN i.ArticuloId LIKE '%RY%' THEN i.ArticuloId 
                ELSE d.ArticuloId 
            END AS NVARCHAR(50)
        ) AS ProductoCodigo,
        CASE 
            WHEN i.ArticuloId LIKE '%RY%' THEN i.Nombre 
            ELSE d.Nombre 
        END AS ProductoNombre,
        lot.Nombre AS Lote,
        COUNT(DISTINCT a.ProduccionId)
            - COUNT(DISTINCT f.SolicitudSurtidoId) AS Cajas,
        ROUND(SUM(a.PesoNeto),2) AS Kg,
        'TIF' AS Sucursal
    FROM [MEAT_TIF].[TIF_Meat].[dbo].[Produccion] a
    INNER JOIN [MEAT_TIF].[TIF_CommerciaNet].[dbo].[Articulo] d 
        ON a.Articulo = d.ArticuloId
    LEFT JOIN [MEAT_TIF].[TIF_CommerciaNet].[dbo].[Almacen] e 
        ON a.Almacen = e.AlmacenId
    LEFT JOIN [MEAT_TIF].[TIF_Meat].[dbo].[SalidaEmbarque] f 
        ON a.ProduccionId = f.ProduccionId
    LEFT JOIN [MEAT_TIF].[TIF_Meat].[dbo].[CanalDetalle] g 
        ON a.ProduccionId = g.ProduccionId
    LEFT JOIN [MEAT_TIF].[TIF_Meat].[dbo].[Clasificacion] cls 
        ON g.ClasificacionId = cls.ClasificacionId
    LEFT JOIN [MEAT_TIF].[TIF_CommerciaNet].[dbo].[Articulo] i 
        ON cls.ClasificacionId = i.Clasifica1
    LEFT JOIN [MEAT_TIF].[TIF_Meat].[dbo].[Lote] lot
        ON a.LoteId = lot.LoteId
    WHERE a.Estatus = 1 
      AND e.Colonia NOT LIKE '%Frigorifico%'
    GROUP BY 
        e.AlmacenId,
        e.Nombre,
        e.Colonia,
        lot.Nombre,
        CASE 
            WHEN i.ArticuloId LIKE '%RY%' THEN i.ArticuloId 
            ELSE d.ArticuloId 
        END,
        CASE 
            WHEN i.ArticuloId LIKE '%RY%' THEN i.Nombre 
            ELSE d.Nombre 
        END;

    /*=========================================================
      4) NORMALIZAR POR ALMACÉN + PRODUCTO + LOTE
    =========================================================*/
    IF OBJECT_ID('tempdb..#InventarioFinal') IS NOT NULL DROP TABLE #InventarioFinal;

    SELECT
        AlmacenId,
        MAX(Almacen) AS Almacen,
        MAX(Colonia) AS Colonia,
        ProductoCodigo,
        MAX(ProductoNombre) AS ProductoNombre,
        ISNULL(Lote, '-') AS Lote,
        SUM(Cajas) AS Cajas,
        SUM(Kg) AS Kg,
        MAX(Sucursal) AS Sucursal
    INTO #InventarioFinal
    FROM #InventarioTemp
    GROUP BY
        AlmacenId,
        ProductoCodigo,
        ISNULL(Lote, '-');

    /*=========================================================
      5) MERGE FINAL
    =========================================================*/
    MERGE sigo.dbo.InventarioSigo AS destino
    USING #InventarioFinal AS fuente
       ON ISNULL(destino.AlmacenId, '') = ISNULL(fuente.AlmacenId, '')
      AND destino.ProductoCodigo = fuente.ProductoCodigo
      AND ISNULL(destino.Lote, '-') = ISNULL(fuente.Lote, '-')

    WHEN MATCHED THEN
        UPDATE SET
            destino.Almacen = fuente.Almacen,
            destino.Colonia = fuente.Colonia,
            destino.ProductoNombre = fuente.ProductoNombre,
            destino.Lote = fuente.Lote,
            destino.Cajas = fuente.Cajas,
            destino.Kg = fuente.Kg,
            destino.Sucursal = fuente.Sucursal,
            destino.FechaActualizacion = GETDATE()

    WHEN NOT MATCHED BY TARGET THEN
        INSERT (
            AlmacenId,
            Almacen,
            Colonia,
            ProductoCodigo,
            ProductoNombre,
            Lote,
            Cajas,
            Kg,
            Sucursal,
            FechaActualizacion
        )
        VALUES (
            fuente.AlmacenId,
            fuente.Almacen,
            fuente.Colonia,
            fuente.ProductoCodigo,
            fuente.ProductoNombre,
            fuente.Lote,
            fuente.Cajas,
            fuente.Kg,
            fuente.Sucursal,
            GETDATE()
        );

    /*=========================================================
      6) DELETE CONTROLADO
    =========================================================*/
    DELETE d
    FROM sigo.dbo.InventarioSigo d
    WHERE NOT EXISTS (
        SELECT 1
        FROM #InventarioFinal f
        WHERE ISNULL(f.AlmacenId, '') = ISNULL(d.AlmacenId, '')
          AND f.ProductoCodigo = d.ProductoCodigo
          AND ISNULL(f.Lote, '-') = ISNULL(d.Lote, '-')
    );
END;





CREATE INDEX IX_InventarioSigo_Colonia
ON sigo.dbo.InventarioSigo (Colonia);


--ejecutar procedimiento almacenado
EXEC ActualizarInventarioSigo;

select * from InventarioSigo
select * from ArticuloSap

select 
B.U_MASTER,
SUM(a.Kg) AS Inventario
from InventarioSigo a
inner join ArticuloSap B ON a.ProductoCodigo = B.ProductoCodigo
group by B.U_MASTER



select 
a.ProductoCodigo,
a.ProductoNombre,
a.Kg,
B.U_MASTER,
B.U_TipoporSKU
from InventarioSigo a
inner join ArticuloSap B ON a.ProductoCodigo = B.ProductoCodigo





USE [SIGO];
GO

/* 1) Agregar columna Lote si no existe */
IF COL_LENGTH('dbo.InventarioSigo', 'Lote') IS NULL
BEGIN
    ALTER TABLE dbo.InventarioSigo
    ADD Lote NVARCHAR(100) NULL;
END
GO

/* 2) Limpiar registros existentes */
UPDATE dbo.InventarioSigo
SET Lote = '-'
WHERE Lote IS NULL 
   OR LTRIM(RTRIM(Lote)) = '';
GO

/* 3) Hacer Lote obligatorio */
ALTER TABLE dbo.InventarioSigo
ALTER COLUMN Lote NVARCHAR(100) NOT NULL;
GO

/* 4) Eliminar la llave primaria actual */
ALTER TABLE dbo.InventarioSigo
DROP CONSTRAINT PK_InventarioSigo;
GO

/* 5) Crear la nueva llave primaria con Lote */
ALTER TABLE dbo.InventarioSigo
ADD CONSTRAINT PK_InventarioSigo
PRIMARY KEY CLUSTERED (
    AlmacenId,
    ProductoCodigo,
    Lote
);
GO