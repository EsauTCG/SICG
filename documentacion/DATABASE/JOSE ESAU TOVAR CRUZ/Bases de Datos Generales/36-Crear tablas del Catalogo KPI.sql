-- ==========================================
-- 1) Catálogo de KPIs
-- ==========================================
CREATE TABLE KpiCatalogo (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Titulo NVARCHAR(200) NOT NULL,
    Descripcion NVARCHAR(MAX) NOT NULL,
    Categoria NVARCHAR(100) NOT NULL DEFAULT ('General'),
    EmbedUrl NVARCHAR(1000) NOT NULL,
    ImagenUrl NVARCHAR(260) NULL,
    Activo BIT NOT NULL DEFAULT (1),
    FechaAlta DATETIME NOT NULL DEFAULT (GETDATE())
);

-- ==========================================
-- 2) Permisos por usuario (AD/SQL) -> KPI
-- UsuarioKey ejemplo: "AD:JUAN.PEREZ" o "SQL:JPEREZ"
-- ==========================================
--CREATE TABLE UsuarioKpiPermiso (
   -- Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
   -- UsuarioKey NVARCHAR(200) NOT NULL,
   -- KpiCatalogoId INT NOT NULL,
   -- CONSTRAINT FK_UsuarioKpiPermiso_KpiCatalogo
    --    FOREIGN KEY (KpiCatalogoId) REFERENCES dbo.KpiCatalogo(Id)
     --   ON DELETE CASCADE
);


-- Evita duplicados (mismo usuario, mismo KPI)
--CREATE UNIQUE INDEX UX_UsuarioKpiPermiso_UsuarioKey_KpiCatalogoId
--ON dbo.UsuarioKpiPermiso(UsuarioKey, KpiCatalogoId);



-- ==========================================
-- Permisos por PERFIL -> KPI
-- ==========================================
CREATE TABLE dbo.PerfilKpiPermiso (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PerfilId INT NOT NULL,
    KpiCatalogoId INT NOT NULL,

    CONSTRAINT FK_PerfilKpiPermiso_Perfiles
        FOREIGN KEY (PerfilId) REFERENCES dbo.Perfiles(Id)
        ON DELETE CASCADE,

    CONSTRAINT FK_PerfilKpiPermiso_KpiCatalogo
        FOREIGN KEY (KpiCatalogoId) REFERENCES dbo.KpiCatalogo(Id)
        ON DELETE CASCADE
);

-- Evita duplicados (mismo perfil, mismo KPI)
CREATE UNIQUE INDEX UX_PerfilKpiPermiso_PerfilId_KpiCatalogoId
ON dbo.PerfilKpiPermiso(PerfilId, KpiCatalogoId);



INSERT INTO KpiCatalogo (Titulo, Descripcion, Categoria, EmbedUrl, ThumbUrl, Activo)
VALUES
('KPI_Producción', 'Indicadores principales de producción del CEDIS.', 'Producción',
 'https://app.powerbi.com/reportEmbed?reportId=0db96832-8a1f-4a27-919e-884d3129860a&autoAuth=true&ctid=85e4dc0f-bb30-42c8-96c9-067a5deb5747',
 NULL, 1);

 INSERT INTO KpiCatalogo (Titulo, Descripcion, Categoria, EmbedUrl, ThumbUrl, Activo)
VALUES
('KPI_Producción_Publico', 'Este indicador mide la relación porcentual entre la producción real obtenida y la capacidad estándar teórica programada para un periodo determinado. Su objetivo es identificar desviaciones en el ritmo de trabajo y el aprovechamiento de la jornada laboral.', 'Producción',
 'https://app.powerbi.com/view?r=eyJrIjoiOTM3YzYzOTQtYWZmYi00OGI2LTg3NjgtMDU2NjBhZmY0MDkwIiwidCI6Ijg1ZTRkYzBmLWJiMzAtNDJjOC05NmM5LTA2N2E1ZGViNTc0NyJ9',
 NULL, 1);

  INSERT INTO KpiCatalogo (Titulo, Descripcion, Categoria, EmbedUrl, ThumbUrl, Activo)
VALUES
('KPI_TEST', 'Este indicador mide la [capacidad/eficiencia/volumen] del equipo comercial para transformar oportunidades en ingresos reales durante un periodo determinado. Representa el termómetro principal del crecimiento del negocio y permite evaluar si las estrategias de captación y cierre están alineadas con los objetivos anuales.', 'Ventas',
 'https://app.powerbi.com/view?r=eyJrIjoiOTM3YzYzOTQtYWZmYi00OGI2LTg3NjgtMDU2NjBhZmY0MDkwIiwidCI6Ijg1ZTRkYzBmLWJiMzAtNDJjOC05NmM5LTA2N2E1ZGViNTc0NyJ9',
 NULL, 1);

 -- usuario SQL: admin
INSERT INTO UsuarioKpiPermiso (UsuarioKey, KpiCatalogoId)
VALUES ('SQL:admin', 1);
