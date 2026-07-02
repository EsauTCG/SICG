/* ===========================================================
   TABLAS: PedidosTransferencia (HEADER) + Detalle
   Incluye: Cajas + KilosCaja y CantidadKg calculado
   =========================================================== */

IF OBJECT_ID('dbo.PedidosTransferenciaDetalle', 'U') IS NOT NULL
    DROP TABLE dbo.PedidosTransferenciaDetalle;
GO

IF OBJECT_ID('dbo.PedidosTransferencia', 'U') IS NOT NULL
    DROP TABLE dbo.PedidosTransferencia;
GO


/* ===========================================================
   HEADER
   =========================================================== */
CREATE TABLE dbo.PedidosTransferencia
(
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PedidosTransferencia PRIMARY KEY,
    TransferenciaId INT NOT NULL,                       -- FK a Transferencias(Id)
    Consecutivo     NVARCHAR(20)  NOT NULL,             -- copia de Transferencias.Consecutivo
    Destino         NVARCHAR(50)  NOT NULL,             -- copia de Transferencias.Sucursal
    FechaSolicitud  DATETIME      NULL,
    Observacion     NVARCHAR(500) NOT NULL CONSTRAINT DF_PedidosTransferencia_Observacion DEFAULT (N''),
    Estatus         INT           NOT NULL CONSTRAINT DF_PedidosTransferencia_Estatus DEFAULT (0),
    UsuarioSolicita NVARCHAR(100) NOT NULL CONSTRAINT DF_PedidosTransferencia_Usuario DEFAULT (N''),
    FechaCreacion   DATETIME2(0)  NOT NULL CONSTRAINT DF_PedidosTransferencia_FechaCreacion DEFAULT (SYSDATETIME())
);
GO

ALTER TABLE dbo.PedidosTransferencia
ADD CONSTRAINT FK_PedidosTransferencia_Transferencias
FOREIGN KEY (TransferenciaId) REFERENCES dbo.Transferencias(Id);
GO

CREATE INDEX IX_PedidosTransferencia_TransferenciaId
ON dbo.PedidosTransferencia(TransferenciaId);
GO

CREATE INDEX IX_PedidosTransferencia_Estatus
ON dbo.PedidosTransferencia(Estatus);
GO

/* 1 solo pedido "abierto" por transferencia (Estatus <> 4) */
--CREATE UNIQUE INDEX UX_PedidosTransferencia_Transferencia_Abierto
--ON dbo.PedidosTransferencia(TransferenciaId)
--WHERE Estatus <> 4;
--GO


/* ===========================================================
   DETALLE
   - Cajas: lo que capturas
   - KilosCaja: peso por caja (si lo traes del producto / lista)
   - CantidadKg: calculado = Cajas * KilosCaja (4 decimales)
   =========================================================== */
CREATE TABLE dbo.PedidosTransferenciaDetalle
(
    Id                             INT IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_PedidosTransferenciaDetalle PRIMARY KEY,

    PedidoTransferenciaId          INT NOT NULL,                  -- FK a PedidosTransferencia(Id)
    TransferenciaDetalleIdOriginal INT NULL,                      -- referencia al detalle original (si lo usas)

    ProductoCodigo                 NVARCHAR(50) NOT NULL,

    Cajas                          INT NOT NULL
        CONSTRAINT DF_PTD_Cajas DEFAULT (0),

  

    -- ✅ AHORA ES COLUMNA REAL (se guarda lo que manda la vista)
    CantidadKg                     DECIMAL(18,4) NOT NULL
        CONSTRAINT DF_PTD_CantidadKg DEFAULT (0),

    Orden                          INT NOT NULL
);
GO


ALTER TABLE dbo.PedidosTransferenciaDetalle
ADD CONSTRAINT FK_PedidosTransferenciaDetalle_Pedido
FOREIGN KEY (PedidoTransferenciaId) REFERENCES dbo.PedidosTransferencia(Id)
ON DELETE CASCADE;
GO

/* Validaciones */
ALTER TABLE dbo.PedidosTransferenciaDetalle
ADD CONSTRAINT CK_PTD_Cajas_NoNeg CHECK (Cajas >= 0);
GO


/* Índices útiles */
CREATE INDEX IX_PedidosTransferenciaDetalle_PedidoTransferenciaId
ON dbo.PedidosTransferenciaDetalle(PedidoTransferenciaId);
GO

CREATE INDEX IX_PedidosTransferenciaDetalle_ProductoCodigo
ON dbo.PedidosTransferenciaDetalle(ProductoCodigo);
GO

CREATE UNIQUE INDEX UX_PedidosTransferenciaDetalle_Pedido_Orden
ON dbo.PedidosTransferenciaDetalle(PedidoTransferenciaId, Orden);
GO


/* ===========================================================
   OPCIONAL:
   Si existe tu tabla de detalles original, amarra FK
   (ajusta el nombre real de la tabla)
   =========================================================== */
-- ALTER TABLE dbo.PedidosTransferenciaDetalle
-- ADD CONSTRAINT FK_PTD_TransferenciaDetalleOriginal
-- FOREIGN KEY (TransferenciaDetalleIdOriginal) REFERENCES dbo.TransferenciaDetalles(Id);
-- GO
