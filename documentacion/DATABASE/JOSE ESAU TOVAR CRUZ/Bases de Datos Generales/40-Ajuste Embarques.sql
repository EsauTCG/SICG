ALTER TABLE Embarque
ADD 
    FichaTecnicaArchivo NVARCHAR(MAX) NULL,
    CartaGarantiaArchivo NVARCHAR(MAX) NULL,
    DocumentacionAprobada BIT NULL,
    FechaValidacionDocumentacion DATETIME NULL,
    UsuarioValidaDocumentacion NVARCHAR(250) NULL;