-- Tabla de catálogo de precios por cliente
CREATE TABLE CatalogoPrecioSap
(
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ProductoCodigo NVARCHAR(50) NOT NULL,    
    Cliente NVARCHAR(50) NOT NULL,
    PriceListNum INT NOT NULL,
    PriceListName NVARCHAR(100) NULL,
    Precio DECIMAL(18, 4) NOT NULL,   
    FechaModificacion DATETIME NOT NULL DEFAULT GETDATE(),
);
GO



-- Base de paginado y filtros
CREATE INDEX IX_CatPrecio_Producto_Cliente
ON dbo.CatalogoPrecioSap (ProductoCodigo, Cliente)
INCLUDE (PriceListName, Precio, PriceListNum, FechaModificacion);

CREATE INDEX IX_CatPrecio_PriceListName
ON dbo.CatalogoPrecioSap (PriceListName);

-- Catálogos
CREATE INDEX IX_Articulo_ProductoCodigo
ON dbo.ArticuloSap (ProductoCodigo)
INCLUDE (ProductoNombre);   -- o ItemName

CREATE INDEX IX_Cliente_Cliente
ON dbo.ClienteSap (Cliente)
INCLUDE (NombreCliente);

CREATE NONCLUSTERED INDEX IX_CatalogoPrecioSap_Cliente_ProductoCodigo
ON CatalogoPrecioSap (Cliente, ProductoCodigo);




-- Catálogo principal: cubrir joins y select
CREATE NONCLUSTERED INDEX IX_CatalogoPrecioSap_Producto_Cliente
ON dbo.CatalogoPrecioSap (ProductoCodigo, Cliente)
INCLUDE (PriceListNum, PriceListName, Precio, FechaModificacion);

-- ClienteSap: join por Cliente + búsqueda por Nombrecliente
CREATE NONCLUSTERED INDEX IX_ClienteSap_Cliente
ON dbo.ClienteSap (Cliente)
INCLUDE (Nombrecliente);

CREATE NONCLUSTERED INDEX IX_ClienteSap_Nombrecliente
ON dbo.ClienteSap (Nombrecliente);

-- ArticuloSap: join por ProductoCodigo + búsqueda por ProductoNombre
CREATE NONCLUSTERED INDEX IX_ArticuloSap_ProductoCodigo
ON dbo.ArticuloSap (ProductoCodigo)
INCLUDE (ProductoNombre);

CREATE NONCLUSTERED INDEX IX_ArticuloSap_ProductoNombre
ON dbo.ArticuloSap (ProductoNombre);

-- Si filtras mucho por PriceListName:
CREATE NONCLUSTERED INDEX IX_CatalogoPrecioSap_PriceListName
ON dbo.CatalogoPrecioSap (PriceListName);