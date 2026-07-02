/* =========================================================
   1) PLANTILLAS (VG, NOV, FUTURAS)
   ========================================================= */
IF OBJECT_ID('dbo.Plantilla', 'U') IS NOT NULL DROP TABLE dbo.Plantilla;
GO

CREATE TABLE dbo.Plantilla
(
    PlantillaId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Plantilla PRIMARY KEY,
    Codigo        NVARCHAR(10) NOT NULL,
    Nombre        NVARCHAR(100) NOT NULL,
    Activo        BIT NOT NULL CONSTRAINT DF_Plantilla_Activo DEFAULT(1),
    CreatedAt     DATETIME2(0) NOT NULL CONSTRAINT DF_Plantilla_CreatedAt DEFAULT(SYSDATETIME()),
    UpdatedAt     DATETIME2(0) NULL
);

ALTER TABLE dbo.Plantilla
ADD CONSTRAINT UQ_Plantilla_Codigo UNIQUE (Codigo);
GO

CREATE INDEX IX_Plantilla_Activo
ON dbo.Plantilla (Activo, Codigo);
GO


/* =========================================================
   2) GRUPOS (FAMILIAS) POR MASTER SKU
   ========================================================= */
IF OBJECT_ID('dbo.SkuGrupo', 'U') IS NOT NULL DROP TABLE dbo.SkuGrupo;
GO

CREATE TABLE dbo.SkuGrupo
(
    GrupoId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SkuGrupo PRIMARY KEY,
    MasterSku    NVARCHAR(20) NOT NULL,
    NombreGrupo  NVARCHAR(100) NULL,
    Activo       BIT NOT NULL CONSTRAINT DF_SkuGrupo_Activo DEFAULT(1),
    CreatedAt    DATETIME2(0) NOT NULL CONSTRAINT DF_SkuGrupo_CreatedAt DEFAULT(SYSDATETIME()),
    UpdatedAt    DATETIME2(0) NULL
);

ALTER TABLE dbo.SkuGrupo
ADD CONSTRAINT UQ_SkuGrupo_MasterSku UNIQUE (MasterSku);
GO

CREATE INDEX IX_SkuGrupo_Activo
ON dbo.SkuGrupo (Activo, MasterSku);
GO


/* =========================================================
   3) ITEMS DEL GRUPO (JERARQUÍA: MASTER -> DERIVADOS -> ...)
   ========================================================= */
IF OBJECT_ID('dbo.SkuGrupoItem', 'U') IS NOT NULL DROP TABLE dbo.SkuGrupoItem;
GO

CREATE TABLE dbo.SkuGrupoItem
(
    GrupoId       INT NOT NULL,
    Sku           NVARCHAR(20) NOT NULL,
    ParentSku     NVARCHAR(20) NULL,  -- apunta a otro SKU del MISMO GrupoId
    Nivel         INT NOT NULL,        -- 0 = master, 1 = derivado, 2 = sub-derivado...
    Orden         INT NOT NULL,        -- orden manual dentro del nivel o del padre
    TipoRelacion  NVARCHAR(30) NOT NULL CONSTRAINT DF_SkuGrupoItem_Tipo DEFAULT('Derivado'),
    Activo        BIT NOT NULL CONSTRAINT DF_SkuGrupoItem_Activo DEFAULT(1),
    CreatedAt     DATETIME2(0) NOT NULL CONSTRAINT DF_SkuGrupoItem_CreatedAt DEFAULT(SYSDATETIME()),
    UpdatedAt     DATETIME2(0) NULL,

    CONSTRAINT PK_SkuGrupoItem PRIMARY KEY (GrupoId, Sku),

    CONSTRAINT FK_SkuGrupoItem_Grupo
        FOREIGN KEY (GrupoId) REFERENCES dbo.SkuGrupo(GrupoId),

    -- FK compuesta: el padre debe existir dentro del mismo GrupoId
    CONSTRAINT FK_SkuGrupoItem_Parent
        FOREIGN KEY (GrupoId, ParentSku) REFERENCES dbo.SkuGrupoItem(GrupoId, Sku),

    CONSTRAINT CK_SkuGrupoItem_Nivel
        CHECK (Nivel >= 0),

    CONSTRAINT CK_SkuGrupoItem_Orden
        CHECK (Orden >= 0),

    -- Reglas básicas: si es Nivel 0, no debe tener padre.
    CONSTRAINT CK_SkuGrupoItem_MasterSinPadre
        CHECK (
            (Nivel = 0 AND ParentSku IS NULL)
            OR (Nivel > 0 AND ParentSku IS NOT NULL)
        )
);
GO

CREATE INDEX IX_SkuGrupoItem_GrupoOrden
ON dbo.SkuGrupoItem (GrupoId, Activo, Nivel, Orden, Sku);
GO

CREATE INDEX IX_SkuGrupoItem_Parent
ON dbo.SkuGrupoItem (GrupoId, ParentSku);
GO


/* =========================================================
   4) RELACIÓN PLANTILLA <-> GRUPO (MUCHOS A MUCHOS)
   ========================================================= */
IF OBJECT_ID('dbo.PlantillaGrupo', 'U') IS NOT NULL DROP TABLE dbo.PlantillaGrupo;
GO

CREATE TABLE dbo.PlantillaGrupo
(
    PlantillaId  INT NOT NULL,
    GrupoId      INT NOT NULL,
    OrdenGrupo   INT NOT NULL,
    Activo       BIT NOT NULL CONSTRAINT DF_PlantillaGrupo_Activo DEFAULT(1),
    CreatedAt    DATETIME2(0) NOT NULL CONSTRAINT DF_PlantillaGrupo_CreatedAt DEFAULT(SYSDATETIME()),
    UpdatedAt    DATETIME2(0) NULL,

    CONSTRAINT PK_PlantillaGrupo PRIMARY KEY (PlantillaId, GrupoId),

    CONSTRAINT FK_PlantillaGrupo_Plantilla
        FOREIGN KEY (PlantillaId) REFERENCES dbo.Plantilla(PlantillaId),

    CONSTRAINT FK_PlantillaGrupo_Grupo
        FOREIGN KEY (GrupoId) REFERENCES dbo.SkuGrupo(GrupoId),

    CONSTRAINT CK_PlantillaGrupo_Orden
        CHECK (OrdenGrupo >= 0)
);
GO

CREATE INDEX IX_PlantillaGrupo_PlantillaOrden
ON dbo.PlantillaGrupo (PlantillaId, Activo, OrdenGrupo, GrupoId);
GO

CREATE INDEX IX_PlantillaGrupo_Grupo
ON dbo.PlantillaGrupo (GrupoId, Activo);
GO

/* =========================================================
   5) CONSULTA PARA PLANTILLAS
   ========================================================= */

SELECT
  p.Codigo AS Plantilla,
  g.MasterSku,
  i.Sku,
  i.ParentSku,
  i.Nivel,
  i.Orden,
  i.TipoRelacion
FROM PlantillaGrupo pg
JOIN Plantilla p      ON p.PlantillaId = pg.PlantillaId
JOIN SkuGrupo g       ON g.GrupoId = pg.GrupoId
JOIN SkuGrupoItem i   ON i.GrupoId = g.GrupoId
WHERE
   pg.Activo = 1
  AND g.Activo = 1
  AND i.Activo = 1
ORDER BY  i.Orden asc










/* =========================================================
   6) INSERT ALA TABLA
   ========================================================= */

--1) Insertar Plantillas (VG, NOV, etc.)
INSERT INTO dbo.Plantilla (Codigo, Nombre)
VALUES
('VG',  'Vaca Gorda'),
('NOV', 'Novillo');


--2) Insertar Grupos (cada grupo tiene un MasterSku)

INSERT INTO dbo.SkuGrupo (MasterSku, NombreGrupo)
VALUES
('V005', 'Grupo V005 - S-PALETA C/H VG'),
('N108', 'Grupo N108 - H-COSTILLA C/H NOV'),
('V008', 'Grupo V008 - S-CHULETON VG');


--3) Insertar Items del Grupo (Master + Derivados)
--3.1 Grupo V301: Master (nivel 0) y derivado (nivel 1)


DECLARE @GrupoV005 INT = (SELECT GrupoId FROM dbo.SkuGrupo WHERE MasterSku = 'V005');

-- Master (Nivel 0, sin padre)
INSERT INTO dbo.SkuGrupoItem (GrupoId, Sku, ParentSku, Nivel, Orden, TipoRelacion)
VALUES
(@GrupoV005, 'V005', NULL, 0, 0, 'Master');


-- Derivado (Nivel 1, padre = V005)
INSERT INTO dbo.SkuGrupoItem (GrupoId, Sku, ParentSku, Nivel, Orden, TipoRelacion)
VALUES
(@GrupoV005, 'V029', 'V005', 1, 1, 'Derivado');

DECLARE @GrupoV005 INT = (SELECT GrupoId FROM dbo.SkuGrupo WHERE MasterSku = 'V005');
-- Derivado (Nivel 1, padre = V005)
INSERT INTO dbo.SkuGrupoItem (GrupoId, Sku, ParentSku, Nivel, Orden, TipoRelacion)
VALUES
(@GrupoV005, 'N137', 'V005', 1, 1, 'Derivado');


--3.2 Grupo N140: Master y derivado

DECLARE @GrupoN140 INT = (SELECT GrupoId FROM dbo.SkuGrupo WHERE MasterSku = 'N140');

INSERT INTO dbo.SkuGrupoItem (GrupoId, Sku, ParentSku, Nivel, Orden, TipoRelacion)
VALUES
(@GrupoN140, 'N140', NULL, 0, 0, 'Master');

INSERT INTO dbo.SkuGrupoItem (GrupoId, Sku, ParentSku, Nivel, Orden, TipoRelacion)
VALUES
(@GrupoN140, 'N137', 'N140', 1, 1, 'Derivado');


--4) Asignar grupos a una plantilla (PlantillaGrupo)

--Ejemplo:

--Plantilla VG usa el grupo V005

--Plantilla NOV usa el grupo N140

DECLARE @PlantillaVG  INT = (SELECT PlantillaId FROM dbo.Plantilla WHERE Codigo = 'VG');
--DECLARE @PlantillaNOV INT = (SELECT PlantillaId FROM dbo.Plantilla WHERE Codigo = 'NOV');

DECLARE @GrupoV005 INT = (SELECT GrupoId FROM dbo.SkuGrupo WHERE MasterSku = 'V005');
--DECLARE @GrupoN140 INT = (SELECT GrupoId FROM dbo.SkuGrupo WHERE MasterSku = 'N140');

-- OrdenGrupo: el orden en que quieres que aparezcan los grupos en esa plantilla
INSERT INTO dbo.PlantillaGrupo (PlantillaId, GrupoId, OrdenGrupo)
VALUES
(@PlantillaVG,  @GrupoV005,  1)
--(@PlantillaNOV, @GrupoN140,  1);



select * from Plantilla
select * from SkuGrupo 
select * from SkuGrupoItem  
select * from PlantillaGrupo  


DECLARE @GrupoId INT = (SELECT GrupoId FROM dbo.SkuGrupo WHERE MasterSku = 'V005');

SELECT GrupoId, Sku, ParentSku, Nivel, Orden, TipoRelacion
FROM dbo.SkuGrupoItem
WHERE GrupoId = @GrupoId
ORDER BY Nivel, Orden, Sku;




select * from SkuConversion where SkuOrigen = 'V029'


select * from Plantilla
select * from SkuGrupo 
select * from SkuGrupoItem  
update SkuGrupoItem set Orden = 3  where GrupoId = 1 and sku = 'N137'
select * from PlantillaGrupo  

