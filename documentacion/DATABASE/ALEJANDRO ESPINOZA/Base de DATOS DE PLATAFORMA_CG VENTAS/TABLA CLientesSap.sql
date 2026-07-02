-- =============================================
-- CREACIÓN DE BASE DE DATOS
-- =============================================
CREATE DATABASE SistemaIntegralCG;
GO

USE SistemaIntegralCG;
GO

-- =============================================
-- TABLA: ClienteSap
-- =============================================
CREATE TABLE ClienteSap (
    Cliente NVARCHAR(50) NOT NULL PRIMARY KEY,     -- CardCode de SAP
    Nombrecliente NVARCHAR(255) NOT NULL,          -- CardName de SAP
    U_MT_Clasificacion NVARCHAR(100) NULL,         -- Clasificación (campo personalizado SAP)
    U_CANAL NVARCHAR(100) NULL,                    -- Canal (campo personalizado SAP)
	VendedorId INT NULL,
	VendedorNombre NVARCHAR(150) NULL,
    FechaModificacion DATETIME NOT NULL DEFAULT(GETDATE())  -- Última fecha de actualización
);
GO

ALTER TABLE dbo.clienteSap
ADD AplicaPresupuesto bit NOT NULL
CONSTRAINT DF_ClientesSap_AplicaPresupuesto DEFAULT (1);



IF COL_LENGTH('dbo.ClienteSap', 'PriceListNum') IS NULL
BEGIN
    ALTER TABLE dbo.ClienteSap
    ADD PriceListNum INT NULL;
END;
GO

IF COL_LENGTH('dbo.ClienteSap', 'PriceListName') IS NULL
BEGIN
    ALTER TABLE dbo.ClienteSap
    ADD PriceListName NVARCHAR(100) NULL;
END;
GO

-- =============================================
-- INDICES
-- =============================================
CREATE INDEX IX_ClienteSap_Nombrecliente ON ClienteSap (Nombrecliente);
CREATE INDEX IX_ClienteSap_U_CANAL ON ClienteSap (U_CANAL);
CREATE INDEX IX_ClienteSap_VendedorId ON dbo.ClienteSap(VendedorId);
GO



ALTER TABLE dbo.ClienteSap ADD VendedorId INT NULL;
ALTER TABLE dbo.ClienteSap ADD VendedorNombre NVARCHAR(150) NULL;
CREATE INDEX IX_ClienteSap_VendedorId ON dbo.ClienteSap(VendedorId);