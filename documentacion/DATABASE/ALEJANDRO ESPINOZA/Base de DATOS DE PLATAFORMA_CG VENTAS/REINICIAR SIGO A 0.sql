select * from OrdenVenta where Id = 1141


select * from OrdenVentaProducto where PedidoId = 1141


select * from OrdenVenta
select * from OrdenVentaProducto
select * from PedidoVenta
select * from PedidoVentaProducto
select * from Subpedido
select * from SubpedidoProductos





--delete from OrdenVenta
--delete from OrdenVentaProducto
--delete from PedidoVenta
--delete from PedidoVentaProducto
--delete from Subpedido
--delete from SubpedidoProductos



DBCC CHECKIDENT ('OrdenVenta', RESEED, 1000);
DBCC CHECKIDENT ('OrdenVentaProducto', RESEED, 1000);
DBCC CHECKIDENT ('PedidoVenta', RESEED, 0);
DBCC CHECKIDENT ('PedidoVentaProducto', RESEED, 1000);
DBCC CHECKIDENT ('Subpedido', RESEED, 1000);
DBCC CHECKIDENT ('SubpedidoProductos', RESEED, 1000);


select * from Presupuestos
select * from PlanProduccion
select * from PlanDetalle

--update PlanProduccion set Fecha = '20251101' where Id = 7




-- Estado actual del IDENTITY
DBCC CHECKIDENT ('OrdenVenta', NORESEED);

-- Valores actuales/step
SELECT 
  IDENT_CURRENT('OrdenVenta')   AS IdentActual,
  IDENT_INCR('OrdenVenta')      AS Paso;

-- ¿Hay una secuencia/HiLo?
SELECT name, * 
FROM sys.sequences;

-- Busca si tu modelo usa HiLo/Sequence
-- (en tu código/migraciones: UseHiLo(), ForSqlServerUseSequenceHiLo(), etc.)