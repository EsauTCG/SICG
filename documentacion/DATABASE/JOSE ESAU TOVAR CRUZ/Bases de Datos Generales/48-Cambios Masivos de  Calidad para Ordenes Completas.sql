USE SIGO;

/* ============================================================
   TABLA: EmbarqueProductoTemperatura
   OBJETIVO:
   Guardar temperatura individual por SKU dentro de un embarque.
   Permite guardado parcial antes de validar Calidad.
   ============================================================ */

IF OBJECT_ID('dbo.EmbarqueProductoTemperatura', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmbarqueProductoTemperatura
    (
        Id INT IDENTITY(1,1) NOT NULL,

        EmbarqueId INT NOT NULL,

        TipoDocumento NVARCHAR(30) NOT NULL, -- OV / TRANSFERENCIA

        DocumentoId INT NOT NULL,

        DocumentoConsecutivo NVARCHAR(80) NULL,

        OrigenDetalleId INT NOT NULL, -- PedidoVentaProducto.Id o PedidoTransferenciaDetalle.Id

        ProductoCodigo NVARCHAR(80) NOT NULL,

        ProductoNombre NVARCHAR(250) NULL,

        Almacen NVARCHAR(80) NULL,

        Cajas INT NOT NULL CONSTRAINT DF_EmbarqueProductoTemperatura_Cajas DEFAULT (0),

        Kilos DECIMAL(18,2) NOT NULL CONSTRAINT DF_EmbarqueProductoTemperatura_Kilos DEFAULT (0),

        Temperatura DECIMAL(10,2) NULL,

        Observaciones NVARCHAR(500) NULL,

        FechaRegistro DATETIME2(7) NOT NULL CONSTRAINT DF_EmbarqueProductoTemperatura_FechaRegistro DEFAULT (SYSDATETIME()),

        UsuarioRegistro NVARCHAR(256) NULL,

        FechaActualizacion DATETIME2(7) NULL,

        UsuarioActualiza NVARCHAR(256) NULL,

        CONSTRAINT PK_EmbarqueProductoTemperatura 
            PRIMARY KEY CLUSTERED (Id ASC)
    );
END
GO


/* ============================================================
   FOREIGN KEY HACIA EMBARQUE
   Si tu tabla real se llama diferente, cambia dbo.Embarque.
   ============================================================ */

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_EmbarqueProductoTemperatura_Embarque_EmbarqueId'
)
BEGIN
    ALTER TABLE dbo.EmbarqueProductoTemperatura
    ADD CONSTRAINT FK_EmbarqueProductoTemperatura_Embarque_EmbarqueId
        FOREIGN KEY (EmbarqueId)
        REFERENCES dbo.Embarque (Id)
        ON DELETE CASCADE;
END
GO


/* ============================================================
   ÍNDICE ÚNICO
   Evita duplicar el mismo SKU/detalle del mismo documento
   dentro del mismo embarque.
   ============================================================ */

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_EmbarqueProductoTemperatura_UnicoSku'
      AND object_id = OBJECT_ID('dbo.EmbarqueProductoTemperatura')
)
BEGIN
    CREATE UNIQUE INDEX UX_EmbarqueProductoTemperatura_UnicoSku
    ON dbo.EmbarqueProductoTemperatura
    (
        EmbarqueId,
        TipoDocumento,
        DocumentoId,
        OrigenDetalleId
    );
END
GO


/* ============================================================
   ÍNDICES DE APOYO
   Para consultas por embarque y por documento.
   ============================================================ */

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_EmbarqueProductoTemperatura_EmbarqueId'
      AND object_id = OBJECT_ID('dbo.EmbarqueProductoTemperatura')
)
BEGIN
    CREATE INDEX IX_EmbarqueProductoTemperatura_EmbarqueId
    ON dbo.EmbarqueProductoTemperatura (EmbarqueId);
END
GO


IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_EmbarqueProductoTemperatura_Documento'
      AND object_id = OBJECT_ID('dbo.EmbarqueProductoTemperatura')
)
BEGIN
    CREATE INDEX IX_EmbarqueProductoTemperatura_Documento
    ON dbo.EmbarqueProductoTemperatura
    (
        TipoDocumento,
        DocumentoId
    );
END
GO