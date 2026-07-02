CREATE TABLE Embarque (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    FechaCreacion DATETIME NOT NULL DEFAULT(GETDATE()),
    UsuarioGenera NVARCHAR(100) NOT NULL,
    Estatus INT NOT NULL, -- 1 = Generado, 2 = Validado
    Observaciones NVARCHAR(MAX) NULL
);

ALTER TABLE Embarque 
ADD FechaEntrada DATETIME NULL,
    FechaSalida DATETIME NULL;

-- Agregar nuevas columnas a la tabla Embarque
ALTER TABLE Embarque
ADD FechaLlegadaDestino DATETIME NULL,
    FechaRetrasado DATETIME NULL,
    FechaEntregado DATETIME NULL,
    FechaDevuelto DATETIME NULL;

CREATE TABLE EmbarqueOrdenes (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    EmbarqueId INT NOT NULL,
    OrdenId INT NOT NULL,

    CONSTRAINT FK_EmbarqueOrdenes_Embarque 
        FOREIGN KEY (EmbarqueId) REFERENCES Embarque(Id),

    CONSTRAINT FK_EmbarqueOrdenes_Orden 
        FOREIGN KEY (OrdenId) REFERENCES OrdenVenta(Id)
);


CREATE TABLE EmbarqueQR (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    EmbarqueId INT NOT NULL,
    Token NVARCHAR(200) NOT NULL UNIQUE,
    UrlQR NVARCHAR(MAX) NOT NULL,
    FechaGeneracion DATETIME NOT NULL DEFAULT(GETDATE()),
    FechaValidacion DATETIME NULL,
    Estado INT NOT NULL, -- 1 Activo, 2 Usado, 3 Expirado
    UsuarioGenera NVARCHAR(100) NOT NULL,
    UsuarioValida NVARCHAR(100) NULL,

    CONSTRAINT FK_EmbarqueQR_Embarque 
        FOREIGN KEY (EmbarqueId) REFERENCES Embarque(Id)
);
