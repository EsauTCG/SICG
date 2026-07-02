
CREATE SEQUENCE Seq_EmbarqueFolio
    AS INT
    START WITH 1001
    INCREMENT BY 1;

ALTER TABLE Embarque
ADD Consecutivo NVARCHAR(20) NOT NULL;

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

-----------------------------------------------------
-----------------------------------------------------
-----------------------------------------------------
-- SOLO PARA REINICIAR EL CONTADOR DEL CONSECUTIVO --
-----------------------------------------------------
-----------------------------------------------------
-----------------------------------------------------

ALTER SEQUENCE Seq_EmbarqueFolio
RESTART WITH 1001;

UPDATE Embarque
SET Consecutivo = 
    'EMB-' + RIGHT(
        '00000000' + CAST(NEXT VALUE FOR Seq_EmbarqueFolio AS VARCHAR(8)),
        8
    )
WHERE Consecutivo IS NULL;


ALTER TABLE Embarque
ALTER COLUMN Consecutivo NVARCHAR(20) NOT NULL;
