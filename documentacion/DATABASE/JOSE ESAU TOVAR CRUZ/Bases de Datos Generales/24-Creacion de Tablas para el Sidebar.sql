-- ===============================
-- TABLA: SidebarCategorias
-- ===============================
CREATE TABLE SidebarCategorias (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Nombre NVARCHAR(100) NOT NULL,
    Icono NVARCHAR(50) NULL
);

-- ===============================
-- TABLA: SidebarModulos
-- ===============================
CREATE TABLE SidebarModulos (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Nombre NVARCHAR(100) NOT NULL,
    Icono NVARCHAR(50) NULL,
    Url NVARCHAR(200) NULL,
    PadreId INT NULL,
    Orden INT DEFAULT 0,
    Activo BIT DEFAULT 1,
    CategoriaId INT NULL,
    FOREIGN KEY (PadreId) REFERENCES SidebarModulos(Id),
    FOREIGN KEY (CategoriaId) REFERENCES SidebarCategorias(Id)
);

-- ===============================
-- TABLA: SidebarPermisos
-- ===============================
CREATE TABLE SidebarPermisos (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PerfilId INT NOT NULL,
    ModuloId INT NOT NULL,
    FOREIGN KEY (PerfilId) REFERENCES Perfiles(Id),
    FOREIGN KEY (ModuloId) REFERENCES SidebarModulos(Id)
);
