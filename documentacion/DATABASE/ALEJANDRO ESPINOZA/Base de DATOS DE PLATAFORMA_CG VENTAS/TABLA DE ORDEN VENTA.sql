-- 1️⃣ Crear base de datos
CREATE DATABASE SistemaIntegralCG;
GO

USE SistemaIntegralCG;
GO




-- 2️⃣ Tabla OrdenVenta
CREATE TABLE OrdenVenta
(
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Consecutivo NVARCHAR(50) NOT NULL UNIQUE,
    Serie NVARCHAR(50) NULL,
    FechaEntrega DATE NOT NULL,
    FechaEmbarque DATE  NULL,
    HoraEmbarque TIME NOT NULL,
    Cliente NVARCHAR(100) NULL,
    VendedorId INT NULL,
    Vendedor NVARCHAR(100) NULL,
    Ruta NVARCHAR(100) NULL,
    Presentacion NVARCHAR(100) NULL,
    Observacion NVARCHAR(MAX) NULL,
    Saldo DECIMAL(18, 2) NOT NULL DEFAULT 0,
    OtrosPedidos DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Credito DECIMAL(18, 2) NOT NULL DEFAULT 0,
    FechaRegistro DATETIME NOT NULL DEFAULT GETDATE(),
    Estatus INT NOT NULL DEFAULT 1,
    Documentacion NVARCHAR(MAX) NULL,
    AutorizacionPresupuesto BIT NOT NULL CONSTRAINT DF_OrdenVenta_AutorizacionPresupuesto DEFAULT 1,
    AutorizacionPrecio BIT NOT NULL CONSTRAINT DF_OrdenVenta_AutorizacionPrecio DEFAULT 1,
    AutorizacionCredito BIT NOT NULL CONSTRAINT DF_OrdenVenta_AutorizacionCredito DEFAULT 1,
    ModoPresupuesto NVARCHAR(20) NULL
);
GO


---- 2️⃣ Tabla OrdenVenta
--CREATE TABLE OrdenVenta
--(
--    Id INT IDENTITY(1,1) PRIMARY KEY,
--    Consecutivo NVARCHAR(50) NOT NULL,
--    Serie NVARCHAR(50) NULL,
--    Fecha DATE NOT NULL,
--    FechaEmbarque DATE NOT NULL,
--    HoraEmbarque TIME NOT NULL,
--    Cliente NVARCHAR(100) NULL,
--    Vendedor NVARCHAR(100) NULL,
--    Ruta NVARCHAR(100) NULL,
--    Presentacion NVARCHAR(100) NULL,
--    Observacion NVARCHAR(MAX) NULL,
--    Saldo DECIMAL(18, 2) NOT NULL DEFAULT 0,
--    OtrosPedidos DECIMAL(18, 2) NOT NULL DEFAULT 0,
--    Credito DECIMAL(18, 2) NOT NULL DEFAULT 0,
    
--    -- NUEVOS CAMPOS
--    FechaRegistro DATETIME NOT NULL DEFAULT GETDATE(),
--    Estatus NVARCHAR(50) NOT NULL DEFAULT 'Pendiente',
--    Documentacion NVARCHAR(MAX) NULL
--);

-- 3️⃣ Tabla OrdenVentaProducto
CREATE TABLE OrdenVentaProducto
(
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PedidoId INT NOT NULL, -- FK hacia OrdenVenta
    ProductoCodigo NVARCHAR(50) NOT NULL,
    ProductoNombre NVARCHAR(100) NULL,
    Peso DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Precio DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Cajas INT NOT NULL DEFAULT 0,
    Importe AS (Peso * Precio) PERSISTED, -- calculado automáticamente
	AutorizacionPresupuestoLinea BIT NOT NULL DEFAULT(0),
	AutorizacionPrecioLinea BIT NOT NULL DEFAULT(0),
	 Eliminado BIT NOT NULL CONSTRAINT DF_OrdenVentaProducto_Eliminado DEFAULT(0),
    EliminadoFecha DATETIME NULL,
    EliminadoUsuario NVARCHAR(128) NULL,

    CONSTRAINT FK_OrdenVentaProducto_OrdenVenta FOREIGN KEY (PedidoId)
        REFERENCES OrdenVenta(Id)
        ON DELETE CASCADE
);
GO

-- 4️⃣ Opcional: índices para búsqueda rápida
CREATE INDEX IX_OrdenVenta_Cliente ON OrdenVenta(Cliente);
CREATE INDEX IX_OrdenVentaProducto_ProductoCodigo ON OrdenVentaProducto(ProductoCodigo);
GO

ALTER TABLE OrdenVenta ALTER COLUMN HoraEmbarque TIME NULL;

select * from OrdenVenta
select * from OrdenVentaProducto



---- 1️⃣ Agregar nuevos campos a la tabla OrdenVenta
--ALTER TABLE OrdenVenta
--ADD 
--    FechaRegistro DATETIME NOT NULL DEFAULT GETDATE(),
--    Estatus INT NOT NULL DEFAULT 1,
--    Documentacion NVARCHAR(MAX) NULL;


--ALTER TABLE OrdenVenta
--ALTER COLUMN Estatus INT NOT NULL DEFAULT 1;



--EXEC sp_rename 'OrdenVenta.Fecha', 'FechaEntrega', 'COLUMN';







--ALTER TABLE OrdenVenta
--ALTER COLUMN FechaEmbarque DATE NULL;



--ALTER TABLE OrdenVenta
--ADD AutorizacionPresupuesto BIT NOT NULL CONSTRAINT DF_OrdenVenta_AutorizacionPresupuesto DEFAULT 1,
--    AutorizacionPrecio BIT NOT NULL CONSTRAINT DF_OrdenVenta_AutorizacionPrecio DEFAULT 1,
--    AutorizacionCredito BIT NOT NULL CONSTRAINT DF_OrdenVenta_AutorizacionCredito DEFAULT 1;
