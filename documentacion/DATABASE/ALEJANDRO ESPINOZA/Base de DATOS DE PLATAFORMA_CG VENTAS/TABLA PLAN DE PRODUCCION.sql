CREATE TABLE dbo.PlaneacionProduccion
(
    PlaneacionId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlaneacionProduccion PRIMARY KEY,
    FechaPlan          DATE NOT NULL,                 -- Día que planeas producir
    TipoPlan           VARCHAR(10) NOT NULL, -- ya no CHAR(3),              -- 'VG' / 'NOV' (o catálogo)
    Estatus            VARCHAR(20) NOT NULL DEFAULT('Creado'), -- BORRADOR / CERRADO / CANCELADO
    Version            INT NOT NULL DEFAULT(1),        -- Para replaneaciones del mismo día
    Notas              NVARCHAR(400) NULL,
        -- ✅ NUEVAS COLUMNAS
    ProgramacionId       INT NULL,
    NombreProgramacion   NVARCHAR(150) NULL,

    CreadoPor          NVARCHAR(80) NULL,
    FechaCreacion      DATETIME2(0) NOT NULL DEFAULT(SYSDATETIME()),
    FechaActualizacion DATETIME2(0) NOT NULL DEFAULT(SYSDATETIME()),

    CONSTRAINT CK_Planeacion_TipoPlan CHECK (TipoPlan IN ('VG','NOV','VR','SUB'))
);

-- Para evitar duplicados por día+tipo+versión
CREATE UNIQUE INDEX UX_Planeacion_Fecha_Tipo_Version
ON dbo.PlaneacionProduccion(FechaPlan, TipoPlan, Version);



CREATE TABLE dbo.PlaneacionProduccionLinea
(
    PlaneacionLineaId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlaneacionProduccionLinea PRIMARY KEY,
    PlaneacionId       INT NOT NULL,
    
    -- Identidad del renglón
    GroupKey           VARCHAR(60) NOT NULL,   -- lo que usas en data-group (MasterSku o Sku)
    Nivel              TINYINT NOT NULL,       -- 0=master, 1=derivado (como tu Modelo)
    Orden              INT NOT NULL,           -- para mantener orden visual

    -- Selecciones del usuario
    SkuDeshuese        VARCHAR(30) NULL,
    SkuInyeccion       VARCHAR(30) NULL,

    -- Inputs del usuario (VG1/VG2/VR)
    VG1                DECIMAL(5,4) NOT NULL DEFAULT(0),
    VG2                DECIMAL(5,4) NOT NULL DEFAULT(0),
    VR                 DECIMAL(5,4) NOT NULL DEFAULT(0),

    -- Parámetros del renglón
    RendPct            DECIMAL(9,6) NOT NULL DEFAULT(0.068200),  -- el rend que usas
    Etiquetado         NVARCHAR(60) NULL,
    Almacen            NVARCHAR(60) NULL,

    -- (Opcional) “snapshots” para consultas rápidas
    KgLoteCalc         DECIMAL(18,2) NULL,
    CanalesCalc        INT NULL,
    SubtotalCalc       DECIMAL(18,2) NULL,
    PiezasCalc         INT NULL,

    Observaciones      NVARCHAR(400) NULL,

    CONSTRAINT FK_PlaneacionLinea_Planeacion
        FOREIGN KEY (PlaneacionId) REFERENCES dbo.PlaneacionProduccion(PlaneacionId)
        ON DELETE CASCADE
);

-- Índices recomendados
CREATE INDEX IX_PlaneacionLinea_PlaneacionId ON dbo.PlaneacionProduccionLinea(PlaneacionId);
CREATE INDEX IX_PlaneacionLinea_GroupKey ON dbo.PlaneacionProduccionLinea(PlaneacionId, GroupKey);



CREATE UNIQUE INDEX UX_Planeacion_UnBorrador
ON dbo.PlaneacionProduccion(FechaPlan, TipoPlan)
WHERE Estatus = 'Creado';







select * from PlaneacionProduccion
select * from PlaneacionProduccionLinea



----ayuda correrlo despues de la creacion de las tablas
--BEGIN TRAN;

---- 1) Quitar dependencias
--ALTER TABLE dbo.PlaneacionProduccion
--DROP CONSTRAINT CK_Planeacion_TipoPlan;

--DROP INDEX UX_Planeacion_Fecha_Tipo_Version ON dbo.PlaneacionProduccion;
--DROP INDEX UX_Planeacion_UnBorrador ON dbo.PlaneacionProduccion;

---- 2) Cambiar el tipo de columna
--ALTER TABLE dbo.PlaneacionProduccion
--ALTER COLUMN TipoPlan VARCHAR(10) NOT NULL;

---- 3) Volver a crear CHECK con PLAN incluido
--ALTER TABLE dbo.PlaneacionProduccion
--ADD CONSTRAINT CK_Planeacion_TipoPlan
--CHECK (TipoPlan IN ('VG','NOV','PLAN'));

---- 4) Volver a crear índices
--CREATE UNIQUE INDEX UX_Planeacion_Fecha_Tipo_Version
--ON dbo.PlaneacionProduccion(FechaPlan, TipoPlan, Version);

--CREATE UNIQUE INDEX UX_Planeacion_UnBorrador
--ON dbo.PlaneacionProduccion(FechaPlan, TipoPlan)
--WHERE Estatus = 'BORRADOR';

--COMMIT;








select * from PlaneacionProduccion
select * from PlaneacionProduccionLinea



--drop table  PlaneacionProduccion
--drop table PlaneacionProduccionLinea



select * 
from PlaneacionProduccion a
inner join PlaneacionProduccionLinea b on a.PlaneacionId = b.PlaneacionId


