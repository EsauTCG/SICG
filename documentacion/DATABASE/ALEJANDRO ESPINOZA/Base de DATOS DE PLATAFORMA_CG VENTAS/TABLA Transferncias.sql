-- ==========================================
-- TABLA: Transferencias
-- ==========================================
CREATE TABLE dbo.Transferencias
(
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Consecutivo VARCHAR(20) NOT NULL UNIQUE,       -- Ejemplo: TRANSF-0000001
    Sucursal NVARCHAR(50) NOT NULL,
    FechaSolicitud DATE NOT NULL,
    -- Columnas calculadas automáticamente desde FechaSolicitud
    Mes AS MONTH(FechaSolicitud) PERSISTED,
    Anio AS YEAR(FechaSolicitud) PERSISTED,
    Observacion NVARCHAR(500) NULL,
	Estatus int default 1,
	UsuarioSolicita VARCHAR(100) NULL,
    FechaCreacion DATETIME NOT NULL DEFAULT(GETDATE())
);
GO

ALTER TABLE dbo.Transferencias
ADD CONSTRAINT UQ_Transferencias_Consecutivo UNIQUE (Consecutivo);




-- ==========================================
-- TABLA: TransferenciaDetalles
-- ==========================================
CREATE TABLE dbo.TransferenciaDetalles
(
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TransferenciaId INT NOT NULL,
    ProductoCodigo NVARCHAR(50) NOT NULL,
    ProductoNombre NVARCHAR(200) NULL,
    CantidadKg DECIMAL(18,2) NOT NULL,
    Cajas DECIMAL(18, 2) NULL;  -- o NOT NULL DEFAULT(0)
    Nota NVARCHAR(500) NULL,
    AutorizacionPresupuestoLinea Bit NOT NULL DEFAULT(0),

    CONSTRAINT FK_TransferenciaDetalles_Transferencias 
        FOREIGN KEY (TransferenciaId) REFERENCES dbo.Transferencias(Id)
        ON DELETE CASCADE
);
GO



select * from Transferencias
select * from TransferenciaDetalles
select * from series


select * from PresupuestoCedis


select * from ArticuloSap



