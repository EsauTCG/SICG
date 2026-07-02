-- Agregar nuevas columnas a la tabla Embarque

ALTER TABLE Embarque
ADD FechaLlegadaDestino DATETIME NULL,
    FechaRetrasado DATETIME NULL,
    FechaEntregado DATETIME NULL,
    FechaDevuelto DATETIME NULL;

Use SIGO;
--Crear Consecutivo
CREATE SEQUENCE Seq_EmbarqueFolio
    AS INT
    START WITH 1001
    INCREMENT BY 1;

--ALTER TABLE Embarque
--ADD Consecutivo NVARCHAR(20) NOT NULL;

ALTER TABLE Embarque
ADD CONSTRAINT DF_Embarque_Consecutivo
DEFAULT (
    'EMB-' + RIGHT(
        '00000000' + CAST(NEXT VALUE FOR Seq_EmbarqueFolio AS VARCHAR(8)),
        8
    )
) FOR Consecutivo;


CREATE UNIQUE INDEX UX_Embarque_Consecutivo
ON Embarque(Consecutivo);