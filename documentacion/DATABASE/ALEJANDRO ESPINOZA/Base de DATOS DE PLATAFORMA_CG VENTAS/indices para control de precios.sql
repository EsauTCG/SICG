CREATE INDEX IX_CatalogoPrecioSap_Cliente_ProductoCodigo
ON CatalogoPrecioSap (Cliente, ProductoCodigo)
INCLUDE (Precio);

CREATE INDEX IX_CatalogoPrecioSap_ProductoCodigo
ON CatalogoPrecioSap (ProductoCodigo);

CREATE UNIQUE INDEX IX_ArticuloSap_ProductoCodigo
ON ArticuloSap (ProductoCodigo);

CREATE INDEX IX_ClienteSap_VendedorNombre_Cliente
ON ClienteSap (VendedorNombre, Cliente);

CREATE INDEX IX_ClienteSap_Clasificacion_Cliente
ON ClienteSap (U_MT_Clasificacion, Cliente);

CREATE UNIQUE INDEX IX_DemandaProducto_ProductoCodigo
ON DemandaProducto (ProductoCodigo);