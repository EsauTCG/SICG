CREATE TABLE dbo.AppSettings (
    [Key]          NVARCHAR(100) NOT NULL,
    [Value]        NVARCHAR(400) NOT NULL,
    UpdatedAtUtc   DATETIME2(0)  NOT NULL CONSTRAINT DF_AppSettings_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedBy      NVARCHAR(256) NULL,
    RowVer         ROWVERSION    NOT NULL,
    CONSTRAINT PK_AppSettings PRIMARY KEY ([Key])
);
GO

---- Seed inicial (modo por defecto)
--INSERT INTO dbo.AppSettings ([Key], [Value], UpdatedBy)
--VALUES ('Presupuesto.Modo', 'VENDEDOR', 'seed');
--GO

select * from AppSettings

IF NOT EXISTS (SELECT 1 FROM dbo.AppSettings WHERE [Key] = 'Presupuesto.Modo')
BEGIN
  INSERT INTO dbo.AppSettings ([Key], [Value], UpdatedBy)
  VALUES ('Presupuesto.Modo', 'VENDEDOR', 'seed');
END
GO