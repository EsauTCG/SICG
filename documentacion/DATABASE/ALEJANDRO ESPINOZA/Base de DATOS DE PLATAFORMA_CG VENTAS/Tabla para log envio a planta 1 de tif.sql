CREATE TABLE dbo.TransferenciaSyncJob
(
    JobId               INT IDENTITY(1,1) PRIMARY KEY,
    TransferenciaId     INT NOT NULL,
    Estado              VARCHAR(20) NOT NULL,   -- Pendiente, EnProceso, Completado, ErrorParcial, Error
    TotalEtiquetas      INT NOT NULL DEFAULT 0,
    Procesadas          INT NOT NULL DEFAULT 0,
    Exitosas            INT NOT NULL DEFAULT 0,
    Fallidas            INT NOT NULL DEFAULT 0,
    Intentos            INT NOT NULL DEFAULT 0,
    UltimoError         NVARCHAR(1000) NULL,
    FechaCreacion       DATETIME NOT NULL DEFAULT GETDATE(),
    FechaInicio         DATETIME NULL,
    FechaFin            DATETIME NULL
);


CREATE TABLE dbo.TransferenciaSyncDetalle
(
    SyncDetalleId           INT IDENTITY(1,1) PRIMARY KEY,
    JobId                   INT NOT NULL,
    CodigoEtiqueta          VARCHAR(50) NOT NULL,
    Estado                  VARCHAR(20) NOT NULL,   -- Pendiente, EnProceso, Ok, Error
    Intentos                INT NOT NULL DEFAULT 0,
    UltimoError             NVARCHAR(1000) NULL,
    FechaUltimoIntento      DATETIME NULL,
    ProduccionIdP1          INT NULL,
    CONSTRAINT FK_TransferenciaSyncDetalle_Job
        FOREIGN KEY (JobId) REFERENCES dbo.TransferenciaSyncJob(JobId)
);

CREATE UNIQUE INDEX UX_TransferenciaSyncDetalle_Job_Etiqueta
ON dbo.TransferenciaSyncDetalle(JobId, CodigoEtiqueta);


select * from TransferenciaSyncJob where jobid=8
select * from TransferenciaSyncDetalle where jobid = 8

select * from Transferencias where id = 1263