ALTER TABLE Embarque
ADD DocumentacionCalidadAprobada bit NULL;

ALTER TABLE Embarque
ADD FechaValidacionDocumentacionCalidad datetime2 NULL;

ALTER TABLE Embarque
ADD UsuarioValidaDocumentacionCalidad nvarchar(max) NULL;