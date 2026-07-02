--PLANTA TIF 

SELECT 
       e.AlmacenId,
       ISNULL(e.Nombre, '-') AS almacen,
       d.ArticuloId AS Sku,
       d.Nombre AS Producto,
       COUNT(a.ProduccionId)AS Cajas,
	   ROUND(SUM(a.PesoNeto),2) as Kg,
	   'PLANTA TIF' as Sucursal
       --AVG(DATEDIFF(DAY, a.FechaProduccion, GETDATE())) AS DiasPromedio,
       --ISNULL(COUNT(f.SolicitudSurtidoId), 0) AS Pedido
FROM Produccion a
LEFT JOIN ProduccionReferencia b ON a.ProduccionId = b.ProduccionId
LEFT JOIN TarimaDetalle c ON a.ProduccionId = c.ProduccionId
LEFT JOIN Tarima c1 ON c.TarimaId = c1.TarimaId
INNER JOIN TIF_CommerciaNet.dbo.Articulo d ON a.Articulo = d.ArticuloId
LEFT JOIN TIF_CommerciaNet.dbo.Almacen e ON a.Almacen = e.AlmacenId
LEFT JOIN SalidaEmbarque f ON a.ProduccionId = f.ProduccionId
LEFT JOIN
  (SELECT MAX(s.ProduccionId) AS ProduccionId
   FROM SalidaEmbarque s
   LEFT JOIN SolicitudSurtido ss ON s.SolicitudSurtidoId = ss.SolicitudSurtidoId
   WHERE ss.EstatusId = 1
   GROUP BY s.ProduccionId) AS se ON a.ProduccionId = se.ProduccionId
WHERE a.Estatus = 1
GROUP BY b.Referencia,
         e.Nombre,
         d.ArticuloId,
         d.Nombre,
		 e.AlmacenId

--PLANTA 1
SELECT 
       e.AlmacenId,
       ISNULL(e.Nombre, '-') AS almacen,
       d.ArticuloId AS Sku,
       d.Nombre AS Producto,
       COUNT(a.ProduccionId)AS Cajas,
	   ROUND(SUM(a.PesoNeto),2) as Kg,
	   'PLANTA 1' as Sucursal
       --AVG(DATEDIFF(DAY, a.FechaProduccion, GETDATE())) AS DiasPromedio,
       --ISNULL(COUNT(f.SolicitudSurtidoId), 0) AS Pedido
FROM Produccion a
LEFT JOIN ProduccionReferencia b ON a.ProduccionId = b.ProduccionId
LEFT JOIN TarimaDetalle c ON a.ProduccionId = c.ProduccionId
LEFT JOIN Tarima c1 ON c.TarimaId = c1.TarimaId
INNER JOIN CommerciaNet.dbo.Articulo d ON a.Articulo = d.ArticuloId
LEFT JOIN CommerciaNet.dbo.Almacen e ON a.Almacen = e.AlmacenId
LEFT JOIN SalidaEmbarque f ON a.ProduccionId = f.ProduccionId
LEFT JOIN
  (SELECT MAX(s.ProduccionId) AS ProduccionId
   FROM SalidaEmbarque s
   LEFT JOIN SolicitudSurtido ss ON s.SolicitudSurtidoId = ss.SolicitudSurtidoId
   WHERE ss.EstatusId = 1
   GROUP BY s.ProduccionId) AS se ON a.ProduccionId = se.ProduccionId
WHERE a.Estatus = 1
GROUP BY b.Referencia,
         e.Nombre,
         d.ArticuloId,
         d.Nombre,
		 e.AlmacenId

-- SIGO

select 
ProductoCodigo,
ProductoNombre,
U_MASTER,
Rotacion
from ArticuloSap


--EXEC SincronizarInventario;
--PROCEDIMIENTO ALMACENADO PARA INSERTAR INVENTARIOS REALES EN LA BASE DE DATOS SIGO

--		 -- Este procedimiento sincroniza el inventario desde la consulta origen
--CREATE PROCEDURE SincronizarInventario
--AS
--BEGIN
--    SET NOCOUNT ON;

--    -- 1. Crear tabla temporal para los datos origen
--    IF OBJECT_ID('tempdb..#InventarioOrigen') IS NOT NULL
--        DROP TABLE #InventarioOrigen;

--    SELECT 
--        e.AlmacenId,
--        ISNULL(e.Nombre, '-') AS Almacen,
--        d.ArticuloId AS Sku,
--        d.Nombre AS Producto,
--        COUNT(a.ProduccionId) AS Cajas,
--        ROUND(SUM(a.PesoNeto), 2) AS Kg,
--        'PLANTA TIF' AS Sucursal
--    INTO #InventarioOrigen
--    FROM Produccion a
--    LEFT JOIN ProduccionReferencia b ON a.ProduccionId = b.ProduccionId
--    LEFT JOIN TarimaDetalle c ON a.ProduccionId = c.ProduccionId
--    LEFT JOIN Tarima c1 ON c.TarimaId = c1.TarimaId
--    INNER JOIN TIF_CommerciaNet.dbo.Articulo d ON a.Articulo = d.ArticuloId
--    LEFT JOIN TIF_CommerciaNet.dbo.Almacen e ON a.Almacen = e.AlmacenId
--    LEFT JOIN SalidaEmbarque f ON a.ProduccionId = f.ProduccionId
--    LEFT JOIN (
--        SELECT MAX(s.ProduccionId) AS ProduccionId
--        FROM SalidaEmbarque s
--        LEFT JOIN SolicitudSurtido ss ON s.SolicitudSurtidoId = ss.SolicitudSurtidoId
--        WHERE ss.EstatusId = 1
--        GROUP BY s.ProduccionId
--    ) AS se ON a.ProduccionId = se.ProduccionId
--    WHERE a.Estatus = 1
--    GROUP BY b.Referencia, e.Nombre, d.ArticuloId, d.Nombre, e.AlmacenId;

--    -- 2. Actualizar registros existentes
--    UPDATE d
--    SET 
--        d.Cajas = o.Cajas,
--        d.Kg = o.Kg,
--        d.Producto = o.Producto,
--        d.Almacen = o.Almacen,
--        d.Sucursal = o.Sucursal
--    FROM InventarioSigo d
--    INNER JOIN #InventarioOrigen o
--        ON d.AlmacenId = o.AlmacenId AND d.Sku = o.Sku;

--    -- 3. Eliminar registros que ya no existen en origen
--    DELETE FROM InventarioSigo
--    WHERE NOT EXISTS (
--        SELECT 1
--        FROM #InventarioOrigen o
--        WHERE o.AlmacenId = InventarioSigo.AlmacenId AND o.Sku = InventarioSigo.Sku
--    );

--    -- 4. Insertar nuevos registros
--    INSERT INTO InventarioSigo (AlmacenId, Almacen, Sku, Producto, Cajas, Kg, Sucursal)
--    SELECT o.AlmacenId, o.Almacen, o.Sku, o.Producto, o.Cajas, o.Kg, o.Sucursal
--    FROM #InventarioOrigen o
--    WHERE NOT EXISTS (
--        SELECT 1
--        FROM InventarioSigo d
--        WHERE d.AlmacenId = o.AlmacenId AND d.Sku = o.Sku
--    );

--    -- Limpieza
--    DROP TABLE #InventarioOrigen;
--END;


--CREAR TABLA 
--CREATE TABLE InventarioSigo (
--    AlmacenId INT NOT NULL,
--    Almacen NVARCHAR(100),
--    Sku NVARCHAR(50) NOT NULL,
--    Producto NVARCHAR(200),
--    Cajas INT,
--    Kg DECIMAL(18, 2),
--    Sucursal NVARCHAR(100),
--    FechaActualizacion DATETIME DEFAULT GETDATE(),
--    CONSTRAINT PK_InventarioSigo PRIMARY KEY (AlmacenId, Sku)
--);