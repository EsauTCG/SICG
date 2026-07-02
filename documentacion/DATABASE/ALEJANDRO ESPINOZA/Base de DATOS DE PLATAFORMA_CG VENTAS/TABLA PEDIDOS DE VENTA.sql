-- Tabla cabecera de las ordenes procesadas
CREATE TABLE OrdenesProcesadas (
    IdPedido INT PRIMARY KEY IDENTITY(1,1),
    OrdenVentaId INT NOT NULL,             -- Relación con OrdenVenta original
    Consecutivo NVARCHAR(50) NOT NULL,     -- Folio OV
    Cliente NVARCHAR(100) NOT NULL,
    FechaEntrega DATE NOT NULL,
    ImporteTotal DECIMAL(18,2) NOT NULL,
    FechaAutorizacion DATETIME NOT NULL DEFAULT(GETDATE()),
    UsuarioAutorizo NVARCHAR(100) NULL,
    Estatus INT NOT NULL DEFAULT 3,
    Observaciones NVARCHAR(MAX) NULL
);

ALTER TABLE OrdenesProcesadas
ADD CONSTRAINT FK_OrdenesProcesadas_OrdenVenta
FOREIGN KEY (OrdenVentaId) REFERENCES OrdenVenta(id);




-- Tabla detalle ligada a OrdenesProcesadas
CREATE TABLE OrdenesProcesadasDetalle (
    IdDetalle INT PRIMARY KEY IDENTITY(1,1),
    PedidoId INT NOT NULL,               -- FK hacia OrdenesProcesadas
    OrdenVentaProductoId INT NOT NULL,   -- Relación directa con productos originales
    ProductoId INT NOT NULL,
    Cantidad DECIMAL(18,2) NOT NULL,
    PrecioLista DECIMAL(18,2) NOT NULL,
    PrecioOV DECIMAL(18,2) NOT NULL,
    Subtotal DECIMAL(18,2) NOT NULL
);

ALTER TABLE OrdenesProcesadasDetalle
ADD CONSTRAINT FK_OrdenesProcesadasDetalle_Pedido
FOREIGN KEY (PedidoId) REFERENCES OrdenesProcesadas(IdPedido);

ALTER TABLE OrdenesProcesadasDetalle
ADD CONSTRAINT FK_OrdenesProcesadasDetalle_OVProducto
FOREIGN KEY (OrdenVentaProductoId) REFERENCES OrdenVentaProducto(id);














select * from Presupuestos
select * from OrdenVenta
--update OrdenVenta set AutorizacionPresupuesto = 0
select * from OrdenVentaProducto 

delete from OrdenVenta 
delete from OrdenVentaProducto 


DBCC CHECKIDENT ('OrdenVenta', RESEED, 0);
DBCC CHECKIDENT ('OrdenVentaProducto', RESEED, 0);