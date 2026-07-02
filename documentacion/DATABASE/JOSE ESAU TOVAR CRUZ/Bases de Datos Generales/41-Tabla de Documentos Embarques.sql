CREATE TABLE EmbarqueArchivo (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    EmbarqueId INT NOT NULL,
    Tipo NVARCHAR(50) NOT NULL,
    RutaArchivo NVARCHAR(500) NOT NULL,
    FechaRegistro DATETIME NOT NULL CONSTRAINT DF_EmbarqueArchivo_FechaRegistro DEFAULT(GETDATE()),
    UsuarioRegistro NVARCHAR(150) NULL,

    CONSTRAINT FK_EmbarqueArchivo_Embarque
        FOREIGN KEY (EmbarqueId) REFERENCES Embarque(Id)
        ON DELETE CASCADE
);

CREATE INDEX IX_EmbarqueArchivo_EmbarqueId_Tipo
ON EmbarqueArchivo (EmbarqueId, Tipo);