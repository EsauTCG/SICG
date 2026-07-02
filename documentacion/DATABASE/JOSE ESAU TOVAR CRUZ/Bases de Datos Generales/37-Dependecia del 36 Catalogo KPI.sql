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