CREATE TABLE Presupuestos (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ClienteId NVARCHAR(50) NULL,           -- el cliente SAP/CommerciaNet (viene de cardCode)
    ProductoCodigo NVARCHAR(50) NOT NULL,  -- clave del artículo
    Objetivo DECIMAL(18,2) NOT NULL,       -- objetivo calculado
    Presupuesto DECIMAL(18,2) NOT NULL,    -- presupuesto ajustado por el usuario
    Mes INT NOT NULL,                      -- mes al que aplica el presupuesto
    Año INT NOT NULL,                      -- año al que aplica
    FechaCreacion DATETIME NOT NULL DEFAULT GETDATE(), -- control de auditoría
    Usuario NVARCHAR(100) NULL,            -- opcional: quién lo guardó
    Comentario NVARCHAR(500) NULL          -- comentario del usuario
);


ALTER TABLE Presupuestos
ADD CONSTRAINT DF_Presupuestos_ProductoCodigo DEFAULT '0' FOR ProductoCodigo;

select * from Presupuestos where ClienteId = 'C000176' and ProductoCodigo = 'V101' AND Id = 527
--UPDATE Presupuestos SET Mes = '09' where ClienteId = 'C000176' and ProductoCodigo = 'V101' AND Id = 527


select * from OrdenVenta
select * from OrdenVentaProducto



--ALTER TABLE Presupuestos
--ADD Comentario NVARCHAR(500) NULL;