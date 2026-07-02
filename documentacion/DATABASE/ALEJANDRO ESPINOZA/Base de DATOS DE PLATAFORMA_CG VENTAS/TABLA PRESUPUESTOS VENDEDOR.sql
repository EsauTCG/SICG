CREATE TABLE dbo.PresupuestoVendedor (
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PresupuestoVendedor PRIMARY KEY,

    VendedorId INT NOT NULL,    
    Mes  INT NOT NULL,
    Anio INT NOT NULL,

    ProductoCodigo NVARCHAR(50) NOT NULL,
    Master NVARCHAR(100) NOT NULL CONSTRAINT DF_PresVendedor_Master DEFAULT ('SIN_MASTER'),

    Objetivo DECIMAL(18,2) NOT NULL,
    PresupuestoAsignado DECIMAL(18,2) NOT NULL,

    Comentario NVARCHAR(500) NULL,

    CreadoPor NVARCHAR(100) NOT NULL,
    CreadoEn  DATETIME2(0) NOT NULL CONSTRAINT DF_PresVendedor_CreadoEn DEFAULT (SYSUTCDATETIME())
);

-- Evitar duplicados por vendedor/periodo/sku
CREATE UNIQUE INDEX UX_PresVendedor_Vendedor_Periodo_SKU
ON dbo.PresupuestoVendedor (VendedorId, Anio, Mes, ProductoCodigo);

-- Opcional: validaciones básicas
ALTER TABLE dbo.PresupuestoVendedor
ADD CONSTRAINT CK_PresVendedor_Mes CHECK (Mes BETWEEN 1 AND 12);

ALTER TABLE dbo.PresupuestoVendedor
ADD CONSTRAINT CK_PresVendedor_Anio CHECK (Anio >= 2000);

ALTER TABLE dbo.PresupuestoVendedor
ADD CONSTRAINT CK_PresVendedor_Presupuesto CHECK (PresupuestoAsignado > 0);


--select * from PresupuestoVendedor a
--inner join clientesap b on a.vendedorid = b.vendedorid