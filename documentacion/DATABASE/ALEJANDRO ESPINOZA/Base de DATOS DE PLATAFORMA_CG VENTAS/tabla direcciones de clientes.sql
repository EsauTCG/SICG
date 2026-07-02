CREATE TABLE DireccionesCliente (
    Id INT IDENTITY(1,1) PRIMARY KEY,

    -- =========================
    -- Relación con cliente
    -- =========================
    Cliente NVARCHAR(20) NOT NULL,              -- CardCode SAP (C000123)

    -- =========================
    -- Origen / Control
    -- =========================
    Origen NVARCHAR(20) NOT NULL DEFAULT 'SAP', -- SAP | MANUAL
    Activa BIT NOT NULL DEFAULT 1,
    FechaAlta DATETIME NOT NULL DEFAULT GETDATE(),
    FechaActualizacion DATETIME NULL,

    -- =========================
    -- Identidad de la dirección
    -- =========================
    AliasDireccion NVARCHAR(100) NOT NULL,      -- AddressName SAP / Alias interno
    EsPrincipal BIT NOT NULL DEFAULT 0,

    -- =========================
    -- Logística
    -- =========================
    Cedis NVARCHAR(50) NOT NULL,                -- TIJUANA / MTY / HMO
    Ruta NVARCHAR(50) NULL,

    -- =========================
    -- Dirección física
    -- =========================
    Calle NVARCHAR(200) NOT NULL,
    Colonia NVARCHAR(100) NULL,
    Ciudad NVARCHAR(100) NOT NULL,
    Estado NVARCHAR(100) NOT NULL,
    CodigoPostal NVARCHAR(10) NULL,
    Pais NVARCHAR(50) NOT NULL DEFAULT 'MEXICO',

    -- =========================
    -- Campos SAP (opcional)
    -- =========================
    SapAddressType NVARCHAR(20) NULL,            -- bo_ShipTo / bo_BillTo
    SapRowNum INT NULL,                          -- índice BPAddresses
    SapAddressCode NVARCHAR(50) NULL             -- si SAP lo expone

);


-- Búsqueda rápida por cliente
CREATE INDEX IX_DireccionesCliente_Cliente
ON DireccionesCliente (Cliente);

-- Solo direcciones activas
CREATE INDEX IX_DireccionesCliente_Cliente_Activa
ON DireccionesCliente (Cliente, Activa);

-- Evitar duplicados por cliente + alias
CREATE UNIQUE INDEX UX_DireccionesCliente_Cliente_Alias
ON DireccionesCliente (Cliente, AliasDireccion);



select * from DireccionesCliente


