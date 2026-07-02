ALTER TABLE Embarque
ADD TemperaturaUnidadCalidad DECIMAL(10,2) NULL;

ALTER TABLE Embarque
ADD EstadoUnidadCalidad NVARCHAR(100) NULL;

ALTER TABLE Embarque
ADD EstadoProductosCalidad NVARCHAR(100) NULL;

ALTER TABLE Embarque
ADD ObservacionesCalidad NVARCHAR(MAX) NULL;

ALTER TABLE Embarque
ADD FechaValidacionCalidad DATETIME NULL;

ALTER TABLE Embarque
ADD UsuarioValidaCalidad NVARCHAR(255) NULL;

ALTER TABLE Embarque
ADD CalidadAprobada BIT NULL;

CREATE TABLE EmbarqueCalidadFoto
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    EmbarqueId INT NOT NULL,
    RutaArchivo NVARCHAR(500) NOT NULL,
    FechaRegistro DATETIME NOT NULL CONSTRAINT DF_EmbarqueCalidadFoto_FechaRegistro DEFAULT(GETDATE()),
    UsuarioRegistro NVARCHAR(255) NULL
);

ALTER TABLE EmbarqueCalidadFoto
ADD CONSTRAINT FK_EmbarqueCalidadFoto_Embarque
FOREIGN KEY (EmbarqueId) REFERENCES Embarque(Id);

CREATE INDEX IX_EmbarqueCalidadFoto_EmbarqueId
ON EmbarqueCalidadFoto(EmbarqueId);