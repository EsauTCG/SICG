CREATE TABLE Carousel (
    Id INT IDENTITY(1,1) PRIMARY KEY,      -- Identificador único
    Title NVARCHAR(200) NOT NULL,          -- Título del slide
    Text NVARCHAR(500) NULL,               -- Texto descriptivo
    Image NVARCHAR(500) NOT NULL,          -- Ruta o URL de la imagen
    CreatedAt DATETIME DEFAULT GETDATE()   -- Fecha de creación
);
