--SELECT
--    b.PedidoId AS Pedido,
--    a.Cliente, 
--    b.ProductoCodigo,
--    b.ProductoNombre,
--    COUNT(b.Cajas) AS Cajas,
--    SUM(b.Peso) AS Kg,
--    a.FechaEntrega,
--    d.Mes,
--    d.Año,
--    d.Presupuesto,
--    (d.Presupuesto - SUM(b.Peso)) AS 'Presupuesto Disponible',
--    CASE 
--        WHEN SUM(b.Peso) > d.Presupuesto THEN 'Excedido'
--        WHEN SUM(b.Peso) = d.Presupuesto THEN 'Cumplido'
--        ELSE 'Disponible'
--    END AS EstadoPresupuesto
--FROM OrdenVenta a
--left JOIN OrdenVentaProducto b ON a.Id = b.PedidoId
--LEFT JOIN Presupuestos d ON b.ProductoCodigo = d.ProductoCodigo  AND MONTH(a.FechaEntrega) = d.Mes  AND YEAR(a.FechaEntrega) = d.Año
--WHERE b.PedidoId = '2059'
--GROUP BY 
--    b.PedidoId,
--    a.Cliente, 
--    b.ProductoCodigo,
--    b.ProductoNombre,
--    a.FechaEntrega,
--    d.Mes,
--    d.Año,
--    d.Presupuesto
--ORDER BY b.PedidoId;



SELECT
d.ClienteId,
    b.ProductoCodigo,
    b.ProductoNombre,
    MONTH(a.FechaEntrega) AS MesEntrega,
    YEAR(a.FechaEntrega) AS AñoEntrega,
    SUM(b.Peso) AS KgPedidosMes,
    d.Presupuesto,
    (d.Presupuesto - SUM(b.Peso)) AS PresupuestoDisponible,
    CASE 
        WHEN SUM(b.Peso) > d.Presupuesto THEN 'Excedido'
        WHEN SUM(b.Peso) = d.Presupuesto THEN 'Cumplido'
        ELSE 'Disponible'
    END AS EstadoPresupuesto
FROM OrdenVenta a
LEFT JOIN OrdenVentaProducto b ON a.Id = b.PedidoId
LEFT JOIN Presupuestos d 
    ON b.ProductoCodigo = d.ProductoCodigo 
    AND MONTH(a.FechaEntrega) = d.Mes 
    AND YEAR(a.FechaEntrega) = d.Año
WHERE a.FechaEntrega IS NOT NULL 
GROUP BY 
    b.ProductoCodigo,
    b.ProductoNombre,
    MONTH(a.FechaEntrega),
    YEAR(a.FechaEntrega),
    d.Presupuesto,
	d.ClienteId
ORDER BY b.ProductoCodigo, AñoEntrega, MesEntrega;


---37441.28


select * from OrdenVenta
select * from OrdenVentaProducto


---45715330.28
---45715330.28


SELECT 
Id,
Consecutivo as 'Orden de Venta',
Cliente,
Vendedor,
FechaEntrega,
CONVERT(DATE,FechaRegistro) AS FechaRegistro,
Presentacion,
Observacion,
Ruta
FROM OrdenVenta
where Estatus = 3


--excedidos los pedidos
SELECT
    a.Id,
    a.Consecutivo AS OrdenVenta,
    a.Cliente,
    a.FechaEntrega AS Mes,
    ISNULL(b.ProductoNombre, '-') AS Producto,
    SUM(b.Peso) AS KilosOV,
    SUM(c.Presupuesto) AS KilosPresupuesto,
    SUM(b.Peso) - SUM(c.Presupuesto) AS Diferencia,
    'Excedido' AS EstadoPresupuesto
FROM OrdenVenta a
INNER JOIN OrdenVentaProducto b ON a.Id = b.PedidoId
INNER JOIN Presupuestos c ON b.ProductoCodigo = c.ProductoCodigo
GROUP BY 
    a.Id, a.Consecutivo, a.Cliente, a.FechaEntrega, b.ProductoNombre
HAVING SUM(b.Peso) > SUM(c.Presupuesto)
ORDER BY a.Id


select * from OrdenVenta