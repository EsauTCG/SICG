
	  DECLARE @OrdenVentaId INT = 1135;

;WITH ult AS (
    SELECT TOP(1) pv.Id AS PedidoVentaId, pv.OrdenVentaId, pv.FechaEmbarque, pv.AlmacenSurtir
    FROM dbo.PedidoVenta pv
    WHERE pv.OrdenVentaId = @OrdenVentaId
    ORDER BY pv.FechaGestion DESC
),
rows AS (
    SELECT 
        pvp.PedidoVentaId,
        pvp.Id             AS PedidoVentaProductoId,
        pvp.ProductoCodigo,
        pvp.ProductoNombre,
        pvp.KilosCaja as Kilos,
        pvp.Precio,
        pvp.Cajas,
        pvp.Almacen
       
    FROM dbo.PedidoVentaProducto pvp
    JOIN ult u ON u.PedidoVentaId = pvp.PedidoVentaId
),
grp AS (
    SELECT 
        r.*,
        DENSE_RANK() OVER (ORDER BY NULLIF(LTRIM(RTRIM(r.Almacen)),'') ) AS SubNum
    FROM rows r
),
head AS (
    SELECT 
        o.Id,
        o.Consecutivo
    FROM dbo.OrdenVenta o
    WHERE o.Id = @OrdenVentaId
)
SELECT 
   h.Consecutivo + '-' + RIGHT('00' + CAST(g.SubNum AS varchar(10)), 2) AS SubFolio,  -- p.ej. OV123-01, OV123-02
    g.Almacen,
    g.ProductoCodigo, g.ProductoNombre,
    g.Kilos, g.Precio, g.Cajas
FROM grp g
CROSS JOIN head h
ORDER BY g.SubNum, g.ProductoCodigo;
