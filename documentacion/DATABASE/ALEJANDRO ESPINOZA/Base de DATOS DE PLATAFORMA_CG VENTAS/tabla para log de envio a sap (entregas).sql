CREATE TABLE dbo.EntregaSapLog (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Referencia      NVARCHAR(80) NOT NULL,
    Source          NVARCHAR(10) NOT NULL,
    Estatus         BIT NOT NULL,             -- 1 ok, 0 fallo
    Mensaje         NVARCHAR(300) NULL,
    DocEntry        INT NULL,
    DocNum          INT NULL,
    FechaIntento    DATETIME2(0) NOT NULL CONSTRAINT DF_EntregaSapLog_Fecha DEFAULT (SYSDATETIME()),
    Usuario         NVARCHAR(80) NULL
);

-- Evita duplicados por referencia+source (una "última corrida" por entrega)
CREATE UNIQUE INDEX UX_EntregaSapLog_Ref_Source
ON dbo.EntregaSapLog(Referencia, Source);



select * from EntregaSapLog



