CREATE TABLE Subpedido(
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    OrdenVentaId    INT NOT NULL,
    ConsecutivoOV   NVARCHAR(50)  NULL,
    SubFolio        NVARCHAR(70)  NULL,
    Almacen         NVARCHAR(50)  NULL,
    TotalPeso       DECIMAL(18,3) NOT NULL DEFAULT 0,
    TotalImporte    DECIMAL(18,2) NOT NULL DEFAULT 0,
    FechaCreacion   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    FechaEntrega    DATETIME2     NULL,
    FechaEmbarque   DATETIME2     NULL,
    Cliente         NVARCHAR(200) NULL,
    Vendedor        NVARCHAR(200) NULL,
	DocumentoSAP    VARCHAR(30) NULL,
	U_DocMeat NVARCHAR(100) NULL
);
CREATE INDEX IX_Subpedido_OrdenVentaId ON Subpedido(OrdenVentaId);
CREATE INDEX IX_Subpedido_OV_SubFolio ON Subpedido(OrdenVentaId, SubFolio);

CREATE TABLE SubpedidoProductos(
    Id                   INT IDENTITY(1,1) PRIMARY KEY,
    SubpedidoId          INT NOT NULL,
    OrdenVentaProductoId INT NULL,
    ProductoCodigo       NVARCHAR(50)  NULL,
    ProductoNombre       NVARCHAR(250) NULL,
    KilosCaja            DECIMAL(18,3) NOT NULL DEFAULT 0,
    Precio               DECIMAL(18,2) NOT NULL DEFAULT 0,
    Cajas                INT           NOT NULL DEFAULT 0,
    Almacen              NVARCHAR(50)  NULL
);
CREATE INDEX IX_SubpedidoProducto_SubpedidoId ON SubpedidoProductos(SubpedidoId);


select * from Subpedido
select * from SubpedidoProductos


