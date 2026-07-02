use SIGO;
-- 1. Elimina el índice único
DROP INDEX UX_Embarque_Consecutivo ON Embarque;

-- 2. Elimina el constraint DEFAULT 
ALTER TABLE Embarque
DROP CONSTRAINT DF_Embarque_Consecutivo;

-- 3. Eliminar la secuencia
DROP SEQUENCE Seq_EmbarqueFolio;

-- 4. Opcional: Si también  se quiere eliminar la columna
ALTER TABLE Embarque
DROP COLUMN Consecutivo;