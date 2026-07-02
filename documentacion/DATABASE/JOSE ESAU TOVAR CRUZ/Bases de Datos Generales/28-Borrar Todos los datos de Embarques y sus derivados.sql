Use SIGO;
-- Ejecutar en una sola transacción para asegurar la consistencia.
BEGIN TRANSACTION;

-- Borrar de las tablas dependientes
DELETE FROM EmbarqueOrdenes;
DELETE FROM EmbarqueQR;
DELETE FROM EmbarqueArchivo;
DELETE FROM EmbarqueCalidadFoto;
DELETE FROM EmbarqueDocumento;


-- Borrar de la tabla principal
DELETE FROM Embarque;

-- Reiniciar los contadores IDENTITY (Opcional, si quieres que vuelvan a 1)
DBCC CHECKIDENT ('Embarque', RESEED, 0);
DBCC CHECKIDENT ('EmbarqueOrdenes', RESEED, 0);
DBCC CHECKIDENT ('EmbarqueQR', RESEED, 0);
DBCC CHECKIDENT ('EmbarqueArchivo', RESEED, 0);
DBCC CHECKIDENT ('EmbarqueCalidadFoto', RESEED, 0);
DBCC CHECKIDENT ('EmbarqueDocumento', RESEED, 0);


COMMIT TRANSACTION;