Use SIGO;

CREATE TABLE UsuarioSerie (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UsuarioId INT NOT NULL,
    SerieId INT NOT NULL,
    FechaAsignacion DATETIME2 NOT NULL DEFAULT SYSDATETIME(),

    CONSTRAINT FK_UsuarioSerie_UsuarioSQL
        FOREIGN KEY (UsuarioId) REFERENCES UsuarioSQL(Id),

    CONSTRAINT FK_UsuarioSerie_Series
        FOREIGN KEY (SerieId) REFERENCES Series(Id),

    CONSTRAINT UQ_UsuarioSerie_Usuario_Serie
        UNIQUE (UsuarioId, SerieId)
);