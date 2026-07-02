CREATE TABLE EmbarqueDocumento (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    EmbarqueId INT NOT NULL,
    DocumentoId INT NOT NULL,
    TipoDocumento VARCHAR(20) NOT NULL, -- 'OV' o 'TRANSFERENCIA'
    FechaRegistro DATETIME NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_EmbarqueDocumento_Embarque
        FOREIGN KEY (EmbarqueId)
        REFERENCES Embarque(Id)
        ON DELETE CASCADE
);
