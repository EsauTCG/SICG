
 ALTER VIEW dbo.VOrdenesVentaPorVendedor AS
SELECT
    o.Id,
    o.Consecutivo,
    o.FechaRegistro,
    o.FechaEntrega,
    o.Cliente,
    c.Nombrecliente AS ClienteNombre,
    c.VendedorId     AS VendedorId,       -- 👈 alias explícito
    o.Vendedor,
    o.Estatus,
    CASE 
        WHEN o.Estatus = 0 THEN 'Cancelado'
        WHEN o.Estatus IN (1,3) THEN 'Pendiente'
        WHEN o.Estatus = 4 THEN 'Validado'
        WHEN o.Estatus = 5 THEN 'Enviado a SAP'
        WHEN o.Estatus = 2 THEN
            CASE 
                WHEN o.AutorizacionPresupuesto = 0 AND o.AutorizacionPrecio = 0 AND o.AutorizacionCredito = 0 THEN 'presupuesto, precio, credito'
                WHEN o.AutorizacionPresupuesto = 0 AND o.AutorizacionPrecio = 0 THEN 'presupuesto, precio'
                WHEN o.AutorizacionPresupuesto = 0 AND o.AutorizacionCredito = 0 THEN 'presupuesto, credito'
                WHEN o.AutorizacionPrecio = 0 AND o.AutorizacionCredito = 0 THEN 'precio, credito'
                WHEN o.AutorizacionPresupuesto = 0 THEN 'presupuesto'
                WHEN o.AutorizacionPrecio = 0 THEN 'precio'
                WHEN o.AutorizacionCredito = 0 THEN 'credito'
                ELSE 'Autorizado'
            END
        ELSE NULL
    END AS AutorizacionPendiente,
    --SUM(COALESCE(op.Peso,0) * COALESCE(NULLIF(op.Cajas,0),1)) AS KgTotales,
    SUM(COALESCE(op.Peso, 0)) AS KgTotales,
    SUM(COALESCE(op.Precio, 0) * COALESCE(op.Peso, 0)) AS Importe,
    o.Observacion,
    o.Serie
FROM OrdenVenta o
LEFT JOIN OrdenVentaProducto op ON op.PedidoId = o.Id
LEFT JOIN ClienteSap c ON c.Cliente = o.Cliente
GROUP BY
    o.Id, o.Consecutivo, o.FechaRegistro, o.FechaEntrega,
    o.Cliente, c.Nombrecliente, c.VendedorId,  -- 👈 agrega aquí también
    o.Vendedor, o.Estatus,
    o.AutorizacionPresupuesto, o.AutorizacionPrecio, o.AutorizacionCredito,o.Observacion,o.Serie
	GO


select * from vOrdenesVentaPorVendedor



SELECT
    o.Id,
    o.Consecutivo,
    o.FechaRegistro,
    o.FechaEntrega,
    o.Cliente,
    c.Nombrecliente AS ClienteNombre,
	c.VendedorId,
    o.Vendedor,
    o.Estatus,

CASE 
    WHEN o.Estatus = 0 THEN 'Cancelado'
    WHEN o.Estatus = 1 THEN 'Pendiente'
    WHEN o.Estatus = 3 THEN 'Pendiente'
    WHEN o.Estatus = 4 THEN 'Validado'
    WHEN o.Estatus = 5 THEN 'Enviado a SAP'
    WHEN o.Estatus = 2 THEN
        CASE 
            WHEN o.AutorizacionPresupuesto = 0 AND o.AutorizacionPrecio = 0 AND o.AutorizacionCredito = 0 THEN 'presupuesto, precio, credito'
            WHEN o.AutorizacionPresupuesto = 0 AND o.AutorizacionPrecio = 0 THEN 'presupuesto, precio'
            WHEN o.AutorizacionPresupuesto = 0 AND o.AutorizacionCredito = 0 THEN 'presupuesto, credito'
            WHEN o.AutorizacionPrecio = 0 AND o.AutorizacionCredito = 0 THEN 'precio, credito'
            WHEN o.AutorizacionPresupuesto = 0 THEN 'presupuesto'
            WHEN o.AutorizacionPrecio = 0 THEN 'precio'
            WHEN o.AutorizacionCredito = 0 THEN 'credito'
            ELSE 'Autorizado'
        END
    ELSE NULL
END AS AutorizacionPendiente, 
    SUM(COALESCE(op.Peso,0) * COALESCE(NULLIF(op.Cajas,0),1)) AS KgTotales,
    SUM(COALESCE(op.Precio,0) * COALESCE(op.Peso,0) * COALESCE(NULLIF(op.Cajas,0),1)) AS Importe
FROM OrdenVenta o
LEFT JOIN OrdenVentaProducto op ON op.PedidoId = o.Id
LEFT JOIN ClienteSap c ON c.Cliente = o.Cliente
GROUP BY
    o.Id, o.Consecutivo, o.FechaRegistro, o.FechaEntrega,
    o.Cliente, c.Nombrecliente,
    o.Vendedor, o.Estatus,
    o.AutorizacionPresupuesto, o.AutorizacionPrecio, o.AutorizacionCredito,c.VendedorId



	SELECT TOP 1 VendedorId
FROM dbo.VOrdenesVentaPorVendedor;





