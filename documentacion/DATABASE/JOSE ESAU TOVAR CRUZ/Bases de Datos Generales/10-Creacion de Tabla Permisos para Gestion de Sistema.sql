CREATE TABLE Permisos (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Nombre NVARCHAR(100) NOT NULL,       
    Modulo NVARCHAR(100) NOT NULL,       
    Ruta NVARCHAR(255) NOT NULL,         
    Activo BIT NOT NULL DEFAULT 1
);