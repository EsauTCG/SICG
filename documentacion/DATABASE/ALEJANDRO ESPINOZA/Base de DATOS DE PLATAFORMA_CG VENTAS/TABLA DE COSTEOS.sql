/* =========================================================
   1) HISTÓRICO GENERAL NO SE USA
   ========================================================= */
IF OBJECT_ID('dbo.CosteoHistorico', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CosteoHistorico
    (
        Id                BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FechaEjecucion    DATETIME2(0) NOT NULL
            CONSTRAINT DF_CosteoHistorico_FechaEjecucion DEFAULT (GETDATE()),

        Source            VARCHAR(10) NOT NULL,      -- P1, TIF, ALL
        TipoProceso       VARCHAR(20) NOT NULL,      -- CAJAS, RETRABAJO, AMBOS
        SpEjecutado       VARCHAR(150) NULL,

        FechaInicial      DATE NULL,
        FechaFinal        DATE NULL,
        LoteId            INT NULL,
        TipoCosteoId      INT NULL,

        BrincarSinCosto   BIT NULL,
        ContinuarConError BIT NULL,

        Ok                BIT NOT NULL,
        Mensaje           VARCHAR(1000) NULL,

        Usuario           VARCHAR(100) NULL,
        Parametros        VARCHAR(MAX) NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_CosteoHistorico_FechaEjecucion'
      AND object_id = OBJECT_ID('dbo.CosteoHistorico')
)
BEGIN
    CREATE INDEX IX_CosteoHistorico_FechaEjecucion
    ON dbo.CosteoHistorico (FechaEjecucion DESC);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_CosteoHistorico_Source_TipoProceso'
      AND object_id = OBJECT_ID('dbo.CosteoHistorico')
)
BEGIN
    CREATE INDEX IX_CosteoHistorico_Source_TipoProceso
    ON dbo.CosteoHistorico (Source, TipoProceso, FechaEjecucion DESC);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_CosteoHistorico_LoteId'
      AND object_id = OBJECT_ID('dbo.CosteoHistorico')
)
BEGIN
    CREATE INDEX IX_CosteoHistorico_LoteId
    ON dbo.CosteoHistorico (LoteId);
END
GO


/* =========================================================
   2) PROGRAMACIÓN AUTOMÁTICA
   ========================================================= */
IF OBJECT_ID('dbo.meat_CosteoProgramado', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.meat_CosteoProgramado
    (
        Id                    INT IDENTITY(1,1) NOT NULL PRIMARY KEY,

        Source                VARCHAR(10) NOT NULL,      -- P1, TIF
        TipoProceso           VARCHAR(20) NOT NULL,      -- CAJAS, RETRABAJO
        TipoCosteoId          INT NOT NULL,
        HoraProgramada        TIME(0) NOT NULL,

        BrincarSinCosto       BIT NOT NULL
            CONSTRAINT DF_meat_CosteoProgramado_BrincarSinCosto DEFAULT (0),

        ContinuarConError     BIT NOT NULL
            CONSTRAINT DF_meat_CosteoProgramado_ContinuarConError DEFAULT (0),

        Activo                BIT NOT NULL
            CONSTRAINT DF_meat_CosteoProgramado_Activo DEFAULT (1),

        UsuarioAlta           VARCHAR(100) NULL,
        FechaAlta             DATETIME2(0) NOT NULL
            CONSTRAINT DF_meat_CosteoProgramado_FechaAlta DEFAULT (GETDATE()),

        UsuarioModifica       VARCHAR(100) NULL,
        FechaModifica         DATETIME2(0) NULL,

        UltimaEjecucion       DATETIME NULL,
        UltimoResultado       BIT NULL,
        UltimoMensaje         VARCHAR(1000) NULL
    );
END
GO

/* Índice único final: un registro por Source + TipoProceso */
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_meat_CosteoProgramado_Source_TipoProceso_Hora'
      AND object_id = OBJECT_ID('dbo.meat_CosteoProgramado')
)
BEGIN
    DROP INDEX UX_meat_CosteoProgramado_Source_TipoProceso_Hora
    ON dbo.meat_CosteoProgramado;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_meat_CosteoProgramado_Source_TipoProceso'
      AND object_id = OBJECT_ID('dbo.meat_CosteoProgramado')
)
BEGIN
    CREATE UNIQUE INDEX UX_meat_CosteoProgramado_Source_TipoProceso
    ON dbo.meat_CosteoProgramado (Source, TipoProceso);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_meat_CosteoProgramado_Activo'
      AND object_id = OBJECT_ID('dbo.meat_CosteoProgramado')
)
BEGIN
    CREATE INDEX IX_meat_CosteoProgramado_Activo
    ON dbo.meat_CosteoProgramado (Activo, Source, TipoProceso);
END
GO


/* =========================================================
   3) BITÁCORA DE EJECUCIÓN AUTOMÁTICA / MANUAL
   ========================================================= */
IF OBJECT_ID('dbo.meat_CosteoBitacora', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.meat_CosteoBitacora
    (
        Id                    BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FechaEjecucion        DATETIME2(0) NOT NULL
            CONSTRAINT DF_meat_CosteoBitacora_FechaEjecucion DEFAULT (GETDATE()),

        FechaInicioReal       DATETIME2(0) NULL,
        FechaFinReal          DATETIME2(0) NULL,

        Source                VARCHAR(10) NOT NULL,      -- P1, TIF, ALL
        TipoProceso           VARCHAR(20) NOT NULL,      -- CAJAS, RETRABAJO, AMBOS
        SpEjecutado           VARCHAR(150) NULL,

        FechaInicial          DATE NULL,
        FechaFinal            DATE NULL,
        LoteId                INT NULL,
        TipoCosteoId          INT NULL,

        HoraProgramada        TIME(0) NULL,
        EsAutomatico          BIT NOT NULL
            CONSTRAINT DF_meat_CosteoBitacora_EsAutomatico DEFAULT (0),

        BrincarSinCosto       BIT NOT NULL
            CONSTRAINT DF_meat_CosteoBitacora_BrincarSinCosto DEFAULT (0),

        ContinuarConError     BIT NOT NULL
            CONSTRAINT DF_meat_CosteoBitacora_ContinuarConError DEFAULT (0),

        Ok                    BIT NOT NULL,
        Mensaje               VARCHAR(2000) NULL,

        Usuario               VARCHAR(100) NULL,
        Parametros            VARCHAR(MAX) NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_meat_CosteoBitacora_FechaEjecucion'
      AND object_id = OBJECT_ID('dbo.meat_CosteoBitacora')
)
BEGIN
    CREATE INDEX IX_meat_CosteoBitacora_FechaEjecucion
    ON dbo.meat_CosteoBitacora (FechaEjecucion DESC);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_meat_CosteoBitacora_Source_TipoProceso_Fecha'
      AND object_id = OBJECT_ID('dbo.meat_CosteoBitacora')
)
BEGIN
    CREATE INDEX IX_meat_CosteoBitacora_Source_TipoProceso_Fecha
    ON dbo.meat_CosteoBitacora (Source, TipoProceso, FechaEjecucion DESC);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_meat_CosteoBitacora_Ok_Fecha'
      AND object_id = OBJECT_ID('dbo.meat_CosteoBitacora')
)
BEGIN
    CREATE INDEX IX_meat_CosteoBitacora_Ok_Fecha
    ON dbo.meat_CosteoBitacora (Ok, FechaEjecucion DESC);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_meat_CosteoBitacora_FechaInicial_FechaFinal'
      AND object_id = OBJECT_ID('dbo.meat_CosteoBitacora')
)
BEGIN
    CREATE INDEX IX_meat_CosteoBitacora_FechaInicial_FechaFinal
    ON dbo.meat_CosteoBitacora (FechaInicial, FechaFinal);
END
GO


/* =========================================================
   4) CONSULTAS ÚTILES
   ========================================================= */

-- Resumen diario
SELECT
    CONVERT(date, FechaEjecucion) AS Dia,
    Source,
    TipoProceso,
    COUNT(*) AS TotalEjecuciones,
    SUM(CASE WHEN Ok = 1 THEN 1 ELSE 0 END) AS Correctas,
    SUM(CASE WHEN Ok = 0 THEN 1 ELSE 0 END) AS ConError,
    MIN(FechaEjecucion) AS PrimeraEjecucion,
    MAX(FechaEjecucion) AS UltimaEjecucion
FROM dbo.meat_CosteoBitacora
GROUP BY CONVERT(date, FechaEjecucion), Source, TipoProceso
ORDER BY Dia DESC, Source, TipoProceso;
GO

-- Detalle por rango
SELECT
    Id,
    FechaEjecucion,
    FechaInicioReal,
    FechaFinReal,
    Source,
    TipoProceso,
    SpEjecutado,
    FechaInicial,
    FechaFinal,
    LoteId,
    HoraProgramada,
    EsAutomatico,
    BrincarSinCosto,
    ContinuarConError,
    Ok,
    Usuario,
    Mensaje,
    Parametros
FROM dbo.meat_CosteoBitacora
WHERE FechaEjecucion >= @FechaInicial
  AND FechaEjecucion < DATEADD(DAY, 1, @FechaFinal)
ORDER BY FechaEjecucion DESC;
GO
