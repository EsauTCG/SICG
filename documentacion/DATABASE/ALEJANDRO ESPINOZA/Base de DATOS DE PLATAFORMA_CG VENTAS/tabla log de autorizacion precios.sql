CREATE TABLE PrecioLineasHistorico (
    Id INT IDENTITY(1,1) PRIMARY KEY,   
    OrdenVentaId INT NOT NULL,
    OrdenVentaConsecutivo NVARCHAR(50) NULL,
    LineaId INT NOT NULL,
    ClienteId NVARCHAR(50) NULL,
    ClienteNombre NVARCHAR(250) NULL,
    ProductoCodigo NVARCHAR(100) NULL,
    ProductoNombre NVARCHAR(250) NULL,
    PrecioLista DECIMAL(18, 4) NOT NULL,
    PrecioOVAntes DECIMAL(18, 4) NOT NULL,
    PrecioAutorizado DECIMAL(18, 4) NOT NULL,
    Diferencia DECIMAL(18, 4) NOT NULL,
    Usuario NVARCHAR(150) NULL,
    Fuente NVARCHAR(100) NULL,
    Motivo NVARCHAR(250) NULL,
    FechaRegistro DATETIME NOT NULL,
);


select * from PrecioLineasHistorico



