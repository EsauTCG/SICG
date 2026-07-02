select * from TransferenciaSyncJob where TransferenciaId = 1445
--update TransferenciaSyncJob set Procesadas =  TransferenciaId = 1445

select * from TransferenciaSyncDetalle where JobId = 83


BEGIN TRAN;

-- 1) Verifica cuántas están atoradas
SELECT *
FROM dbo.TransferenciaSyncDetalle
WHERE JobId = 83
  AND Estado = 'EnProceso'
  AND ProduccionIdP1 IS NULL;

-- 2) Regresar esas etiquetas a Pendiente para que el proceso las vuelva a tomar
UPDATE dbo.TransferenciaSyncDetalle
SET 
    Estado = 'Pendiente',
    UltimoError = NULL,
    FechaUltimoIntento = NULL
WHERE JobId = 83
  AND Estado = 'EnProceso'
  AND ProduccionIdP1 IS NULL;

-- 3) Reactivar el job
UPDATE dbo.TransferenciaSyncJob
SET
    Estado = 'Pendiente',
    Procesadas = (
        SELECT COUNT(*)
        FROM dbo.TransferenciaSyncDetalle
        WHERE JobId = 83
          AND Estado IN ('Ok', 'Error')
    ),
    Exitosas = (
        SELECT COUNT(*)
        FROM dbo.TransferenciaSyncDetalle
        WHERE JobId = 83
          AND Estado = 'Ok'
    ),
    Fallidas = (
        SELECT COUNT(*)
        FROM dbo.TransferenciaSyncDetalle
        WHERE JobId = 83
          AND Estado = 'Error'
    ),
    TotalEtiquetas = (
        SELECT COUNT(*)
        FROM dbo.TransferenciaSyncDetalle
        WHERE JobId = 83
    ),
    UltimoError = NULL,
    FechaFin = NULL
WHERE JobId = 83;

COMMIT;