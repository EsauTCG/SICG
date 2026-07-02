-- Tabla principal: un renglón por SKU/canal/mes/año
IF OBJECT_ID('dbo.PresupuestoCedis','U') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoCedis(
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Canal NVARCHAR(80) NOT NULL,
        Mes INT NOT NULL CHECK (Mes BETWEEN 1 AND 12),
        Anio INT NOT NULL,
        ProductoCodigo NVARCHAR(50) NOT NULL,
        Master NVARCHAR(50) NULL,
        Objetivo DECIMAL(18,2) NOT NULL CHECK (Objetivo >= 0),
        PresupuestoAsignado DECIMAL(18,2) NOT NULL CHECK (PresupuestoAsignado >= 0),
        Comentario NVARCHAR(300) NULL,
        CreadoPor NVARCHAR(100) NULL,
        CreadoEn DATETIME2(0) NOT NULL CONSTRAINT DF_PresCedis_CreadoEn DEFAULT SYSUTCDATETIME()
    );

    -- Índice único por clave de negocio
    CREATE UNIQUE INDEX UX_PresupuestoCedis
    ON dbo.PresupuestoCedis(Canal, Anio, Mes, ProductoCodigo);
END
GO

---- (Opcional) bitácora simple de cambios
--IF OBJECT_ID('dbo.PresupuestoCedisHist','U') IS NULL
--BEGIN
--  CREATE TABLE dbo.PresupuestoCedisHist(
--      IdHist             BIGINT IDENTITY(1,1) PRIMARY KEY,
--      IdPresupuesto      INT          NOT NULL,
--      Canal              NVARCHAR(80) NOT NULL,
--      Año                INT          NOT NULL,
--      Mes                INT          NOT NULL,
--      ProductoCodigo     NVARCHAR(50) NOT NULL,
--      PresupuestoAnterior DECIMAL(18,2) NULL,
--      PresupuestoNuevo    DECIMAL(18,2) NULL,
--      ComentarioNuevo     NVARCHAR(300) NULL,
--      CambiadoPor        NVARCHAR(100) NULL,
--      CambiadoEn         DATETIME2(0) NOT NULL CONSTRAINT DF_PresCedisHist_CambiadoEn DEFAULT SYSUTCDATETIME()
--  );
--END
--GO


select * from PresupuestoCedis




select * from Presupuestos


