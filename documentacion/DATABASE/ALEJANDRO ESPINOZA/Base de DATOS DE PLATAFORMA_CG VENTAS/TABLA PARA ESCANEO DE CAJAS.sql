CREATE TABLE dbo.TransferenciaScanEtiqueta(
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TransferenciaId INT NOT NULL,
    Sku NVARCHAR(50) NOT NULL,
    CodigoEtiqueta NVARCHAR(100) NOT NULL UNIQUE,  -- evita volver a contar la misma
    Kg DECIMAL(18,4) NOT NULL,
    TarimaCodigo NVARCHAR(50) NULL,
    Fecha DATETIME NOT NULL DEFAULT(GETDATE()),
    Usuario NVARCHAR(100) NULL
);

  CREATE UNIQUE INDEX UX_TransferenciaScanEtiqueta_Transferencia_Etiqueta
ON dbo.TransferenciaScanEtiqueta (TransferenciaId, CodigoEtiqueta);

CREATE TABLE dbo.TransferenciaSurtido(
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TransferenciaId INT NOT NULL,
    Sku NVARCHAR(50) NOT NULL,
    KgSurtido DECIMAL(18,4) NOT NULL DEFAULT(0),
    CajasSurtidas INT NOT NULL DEFAULT(0),
    CONSTRAINT UX_TransferenciaSurtido UNIQUE(TransferenciaId, Sku)
);