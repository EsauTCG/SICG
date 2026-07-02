DECLARE @Venta30 numeric(18,2) = 133200.18;
DECLARE @DiasVenta int = 30;

SELECT
  a.U_MASTER AS Master,
  ROUND(SUM(b.Kg),2) AS Inventario,
  @Venta30  AS Venta30dias,
  @Venta30 * 1.0 / @DiasVenta AS VentasProm,
  CASE  WHEN @Venta30 = 0 THEN NULL ELSE ROUND((SUM(b.Kg)*1.0) / (@Venta30*1.0/@DiasVenta),2) END AS DiasInv,
  CASE 
       WHEN @Venta30 = 0 THEN 0
       WHEN (SUM(b.Kg)*1.0)/(@Venta30*1.0/@DiasVenta) <= 3  THEN 3
       WHEN (SUM(b.Kg)*1.0)/(@Venta30*1.0/@DiasVenta) <= 5  THEN 5
       WHEN (SUM(b.Kg)*1.0)/(@Venta30*1.0/@DiasVenta) <= 7  THEN 7
       WHEN (SUM(b.Kg)*1.0)/(@Venta30*1.0/@DiasVenta) <= 10 THEN 10
       WHEN (SUM(b.Kg)*1.0)/(@Venta30*1.0/@DiasVenta) <= 14 THEN 14
       WHEN (SUM(b.Kg)*1.0)/(@Venta30*1.0/@DiasVenta) <= 21 THEN 21
       ELSE 30
  END AS Rotacion
FROM dbo.ArticuloSap a
JOIN dbo.InventarioSigo b ON a.ProductoCodigo = b.ProductoCodigo
GROUP BY a.U_MASTER
ORDER BY Master;
