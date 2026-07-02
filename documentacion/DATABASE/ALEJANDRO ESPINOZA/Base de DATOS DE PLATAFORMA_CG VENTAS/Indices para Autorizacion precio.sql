-- Para “último precio” por cliente+producto
CREATE INDEX IX_CatPrecio_Prod_Cli_Fecha
ON dbo.CatalogoPrecioSap (Cliente, ProductoCodigo, FechaModificacion DESC)
INCLUDE (Precio, PriceListName);

-- Para joins/consultas de OV
CREATE INDEX IX_OVProd_Pedido
ON dbo.OrdenVentaProducto (PedidoId)
INCLUDE (ProductoCodigo, ProductoNombre, Precio);

CREATE INDEX IX_OVCab_Estatus_Autorizacion
ON dbo.OrdenVenta (Estatus, AutorizacionPrecio)
INCLUDE (Cliente, Consecutivo, FechaEntrega);