-- ==========================================
-- TABLA INVENTARIO SCAN ETIQUETA (NUEVA)
-- ==========================================

CREATE TABLE dbo.InventarioScanEtiqueta (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventarioScanEtiqueta PRIMARY KEY,
    Almacen       NVARCHAR(50) NOT NULL,
    Sku           NVARCHAR(50) NOT NULL,
    CodigoEtiqueta NVARCHAR(60) NOT NULL,
    Kg            DECIMAL(18,4) NOT NULL,

    Origen        NVARCHAR(10) NOT NULL
        CONSTRAINT DF_InventarioScanEtiqueta_Origen DEFAULT(''),

    Usuario       NVARCHAR(80) NOT NULL
        CONSTRAINT DF_InventarioScanEtiqueta_Usuario DEFAULT(''),

    Fecha         DATETIME NOT NULL
        CONSTRAINT DF_InventarioScanEtiqueta_Fecha DEFAULT (GETDATE()),

    -- Solo fecha (sin hora) para duplicado por día
    FechaDia      AS CONVERT(date, Fecha) PERSISTED
);
GO

-- Índice para reportes por fecha (muy útil)
CREATE INDEX IX_InventarioScanEtiqueta_Fecha
ON dbo.InventarioScanEtiqueta(Fecha);
GO

-- Índice para reportes por almacén+fecha
CREATE INDEX IX_InventarioScanEtiqueta_Almacen_Fecha
ON dbo.InventarioScanEtiqueta(Almacen, FechaDia, Fecha);
GO

-- ÚNICO: permite misma etiqueta en otro día, pero NO el mismo día en el mismo almacén
CREATE UNIQUE INDEX UX_InventarioScanEtiqueta_Dia
ON dbo.InventarioScanEtiqueta(Almacen, CodigoEtiqueta, FechaDia);
GO
