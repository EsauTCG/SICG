/* ==============================================================
   BASCULA CAMIONERA - TABLAS SQL SERVER + SOPORTE OFFLINE SYNC
   Base: SQL Server
   Objetivo:
   - Guardar movimientos de entrada/salida en servidor central.
   - Soportar registros generados fuera de línea desde la caseta.
   - Evitar duplicados usando MovimientoGuid generado en la PC/caseta.
   ============================================================== */

/* 1) Terminales / casetas autorizadas */
IF OBJECT_ID('dbo.BasculaTerminal', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BasculaTerminal (
        TerminalId          NVARCHAR(60)    NOT NULL CONSTRAINT PK_BasculaTerminal PRIMARY KEY,
        Nombre              NVARCHAR(120)   NOT NULL,
        Sitio               NVARCHAR(120)   NULL,
        BasculaNombre       NVARCHAR(120)   NULL,
        IpBascula           NVARCHAR(80)    NULL,
        PuertoBascula       INT             NULL,
        ImpresoraNombre     NVARCHAR(180)   NULL,
        Activa              BIT             NOT NULL CONSTRAINT DF_BasculaTerminal_Activa DEFAULT (1),
        UltimaConexion      DATETIME2(0)    NULL,
        FechaAlta           DATETIME2(0)    NOT NULL CONSTRAINT DF_BasculaTerminal_FechaAlta DEFAULT (SYSDATETIME()),
        FechaModificacion   DATETIME2(0)    NULL
    );
END;
GO

/* 2) Movimientos de báscula */
IF OBJECT_ID('dbo.BasculaMovimiento', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BasculaMovimiento (
        MovimientoId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BasculaMovimiento PRIMARY KEY,

        /* Id único generado en la PC/caseta. Es la clave para sincronizar sin duplicar. */
        MovimientoGuid      UNIQUEIDENTIFIER NOT NULL,
        TerminalId          NVARCHAR(60)     NOT NULL,

        /* Folio local puede generarse sin internet. Ejemplo: BAS-CAS01-20260627-000001 */
        FolioLocal          NVARCHAR(60)     NOT NULL,
        FolioServidor       NVARCHAR(60)     NULL,

        Estatus             NVARCHAR(20)     NOT NULL, -- PENDIENTE / CERRADO / CANCELADO
        TipoMovimiento      NVARCHAR(60)     NOT NULL,
        Clasificacion       NVARCHAR(80)     NULL,

        Tercero             NVARCHAR(250)    NOT NULL,
        CodigoSap           NVARCHAR(60)     NULL,
        Placas              NVARCHAR(40)     NOT NULL,

        Producto            NVARCHAR(250)    NOT NULL,
        Sku                 NVARCHAR(80)     NULL,
        Documento           NVARCHAR(120)    NULL,
        Chofer              NVARCHAR(160)    NULL,
        Origen              NVARCHAR(180)    NULL,
        Destino             NVARCHAR(180)    NULL,
        Condicion           NVARCHAR(120)    NULL,

        PesoEntrada         DECIMAL(18,2)    NOT NULL,
        PesoSalida          DECIMAL(18,2)    NULL,
        PesoNeto            AS (ABS(PesoEntrada - ISNULL(PesoSalida, PesoEntrada))) PERSISTED,

        CapturaManual       BIT              NOT NULL CONSTRAINT DF_BasculaMovimiento_CapturaManual DEFAULT (0),
        MotivoManual        NVARCHAR(300)    NULL,
        Observaciones       NVARCHAR(1000)   NULL,

        FechaEntrada        DATETIME2(0)     NOT NULL,
        FechaSalida         DATETIME2(0)     NULL,
        UsuarioEntrada      NVARCHAR(160)    NULL,
        UsuarioSalida       NVARCHAR(160)    NULL,

        /* Evidencia operacional de la lectura */
        RawEntrada          NVARCHAR(1000)   NULL,
        RawSalida           NVARCHAR(1000)   NULL,
        PesoEntradaEstable  BIT              NOT NULL CONSTRAINT DF_BasculaMovimiento_EntradaEstable DEFAULT (0),
        PesoSalidaEstable   BIT              NOT NULL CONSTRAINT DF_BasculaMovimiento_SalidaEstable DEFAULT (0),

        /* Control de sincronización */
        CreadoOffline       BIT              NOT NULL CONSTRAINT DF_BasculaMovimiento_CreadoOffline DEFAULT (0),
        FechaCreacionLocal  DATETIME2(0)     NOT NULL,
        FechaSyncServidor   DATETIME2(0)     NOT NULL CONSTRAINT DF_BasculaMovimiento_FechaSync DEFAULT (SYSDATETIME()),
        FechaModificacion   DATETIME2(0)     NOT NULL CONSTRAINT DF_BasculaMovimiento_Mod DEFAULT (SYSDATETIME()),
        RowVer              ROWVERSION       NOT NULL,

        CONSTRAINT UQ_BasculaMovimiento_Guid UNIQUE (MovimientoGuid),
        CONSTRAINT UQ_BasculaMovimiento_TerminalFolio UNIQUE (TerminalId, FolioLocal),
        CONSTRAINT FK_BasculaMovimiento_Terminal FOREIGN KEY (TerminalId) REFERENCES dbo.BasculaTerminal(TerminalId),
        CONSTRAINT CK_BasculaMovimiento_Estatus CHECK (Estatus IN ('PENDIENTE','CERRADO','CANCELADO')),
        CONSTRAINT CK_BasculaMovimiento_PesoEntrada CHECK (PesoEntrada > 0),
        CONSTRAINT CK_BasculaMovimiento_PesoSalida CHECK (PesoSalida IS NULL OR PesoSalida > 0),
        CONSTRAINT CK_BasculaMovimiento_Cierre CHECK (
            (Estatus = 'PENDIENTE' AND PesoSalida IS NULL AND FechaSalida IS NULL)
            OR (Estatus = 'CERRADO' AND PesoSalida IS NOT NULL AND FechaSalida IS NOT NULL)
            OR (Estatus = 'CANCELADO')
        )
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BasculaMovimiento_FechaEntrada' AND object_id = OBJECT_ID('dbo.BasculaMovimiento'))
    CREATE INDEX IX_BasculaMovimiento_FechaEntrada ON dbo.BasculaMovimiento(FechaEntrada DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BasculaMovimiento_Estatus' AND object_id = OBJECT_ID('dbo.BasculaMovimiento'))
    CREATE INDEX IX_BasculaMovimiento_Estatus ON dbo.BasculaMovimiento(Estatus, FechaEntrada DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BasculaMovimiento_Terminal_Estatus' AND object_id = OBJECT_ID('dbo.BasculaMovimiento'))
    CREATE INDEX IX_BasculaMovimiento_Terminal_Estatus ON dbo.BasculaMovimiento(TerminalId, Estatus, FechaEntrada DESC);
GO

/* 3) Bitácora / auditoría */
IF OBJECT_ID('dbo.BasculaBitacora', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BasculaBitacora (
        BitacoraId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BasculaBitacora PRIMARY KEY,
        BitacoraGuid        UNIQUEIDENTIFIER NOT NULL,
        MovimientoGuid      UNIQUEIDENTIFIER NULL,
        TerminalId          NVARCHAR(60)     NOT NULL,
        FolioLocal          NVARCHAR(60)     NULL,
        Fecha               DATETIME2(0)     NOT NULL,
        Usuario             NVARCHAR(160)    NULL,
        Accion              NVARCHAR(250)    NOT NULL,
        Detalle             NVARCHAR(1000)   NULL,
        CreadoOffline       BIT              NOT NULL CONSTRAINT DF_BasculaBitacora_CreadoOffline DEFAULT (0),
        FechaSyncServidor   DATETIME2(0)     NOT NULL CONSTRAINT DF_BasculaBitacora_FechaSync DEFAULT (SYSDATETIME()),

        CONSTRAINT UQ_BasculaBitacora_Guid UNIQUE (BitacoraGuid),
        CONSTRAINT FK_BasculaBitacora_Terminal FOREIGN KEY (TerminalId) REFERENCES dbo.BasculaTerminal(TerminalId),
        CONSTRAINT FK_BasculaBitacora_MovimientoGuid FOREIGN KEY (MovimientoGuid) REFERENCES dbo.BasculaMovimiento(MovimientoGuid)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BasculaBitacora_MovimientoGuid' AND object_id = OBJECT_ID('dbo.BasculaBitacora'))
    CREATE INDEX IX_BasculaBitacora_MovimientoGuid ON dbo.BasculaBitacora(MovimientoGuid, Fecha DESC);
GO

/* 4) Inbox de sincronización. Permite idempotencia por lote/operación. */
IF OBJECT_ID('dbo.BasculaSyncInbox', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BasculaSyncInbox (
        SyncId              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BasculaSyncInbox PRIMARY KEY,
        TerminalId          NVARCHAR(60)     NOT NULL,
        MovimientoGuid      UNIQUEIDENTIFIER NULL,
        TipoOperacion       NVARCHAR(40)     NOT NULL, -- UPSERT_MOVIMIENTO / BITACORA / LOTE
        PayloadHash         VARBINARY(32)    NULL,
        Estatus             NVARCHAR(20)     NOT NULL, -- RECIBIDO / PROCESADO / ERROR
        Mensaje             NVARCHAR(1000)   NULL,
        FechaRecibido       DATETIME2(0)     NOT NULL CONSTRAINT DF_BasculaSyncInbox_Recibido DEFAULT (SYSDATETIME()),
        FechaProcesado      DATETIME2(0)     NULL,

        CONSTRAINT FK_BasculaSyncInbox_Terminal FOREIGN KEY (TerminalId) REFERENCES dbo.BasculaTerminal(TerminalId),
        CONSTRAINT CK_BasculaSyncInbox_Estatus CHECK (Estatus IN ('RECIBIDO','PROCESADO','ERROR'))
    );
END;
GO

/* 5) Procedimiento sugerido para upsert idempotente de movimiento */
/* ==============================================================
   BASCULA CAMIONERA - TABLAS SQL SERVER + SOPORTE OFFLINE SYNC
   Base: SQL Server
   Objetivo:
   - Guardar movimientos de entrada/salida en servidor central.
   - Soportar registros generados fuera de línea desde la caseta.
   - Evitar duplicados usando MovimientoGuid generado en la PC/caseta.
   ============================================================== */

/* 1) Terminales / casetas autorizadas */
IF OBJECT_ID('dbo.BasculaTerminal', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BasculaTerminal (
        TerminalId          NVARCHAR(60)    NOT NULL CONSTRAINT PK_BasculaTerminal PRIMARY KEY,
        Nombre              NVARCHAR(120)   NOT NULL,
        Sitio               NVARCHAR(120)   NULL,
        BasculaNombre       NVARCHAR(120)   NULL,
        IpBascula           NVARCHAR(80)    NULL,
        PuertoBascula       INT             NULL,
        ImpresoraNombre     NVARCHAR(180)   NULL,
        Activa              BIT             NOT NULL CONSTRAINT DF_BasculaTerminal_Activa DEFAULT (1),
        UltimaConexion      DATETIME2(0)    NULL,
        FechaAlta           DATETIME2(0)    NOT NULL CONSTRAINT DF_BasculaTerminal_FechaAlta DEFAULT (SYSDATETIME()),
        FechaModificacion   DATETIME2(0)    NULL
    );
END;
GO

/* 2) Movimientos de báscula */
IF OBJECT_ID('dbo.BasculaMovimiento', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BasculaMovimiento (
        MovimientoId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BasculaMovimiento PRIMARY KEY,

        /* Id único generado en la PC/caseta. Es la clave para sincronizar sin duplicar. */
        MovimientoGuid      UNIQUEIDENTIFIER NOT NULL,
        TerminalId          NVARCHAR(60)     NOT NULL,

        /* Folio local puede generarse sin internet. Ejemplo: BAS-CAS01-20260627-000001 */
        FolioLocal          NVARCHAR(60)     NOT NULL,
        FolioServidor       NVARCHAR(60)     NULL,

        Estatus             NVARCHAR(20)     NOT NULL, -- PENDIENTE / CERRADO / CANCELADO
        TipoMovimiento      NVARCHAR(60)     NOT NULL,
        Clasificacion       NVARCHAR(80)     NULL,

        Tercero             NVARCHAR(250)    NOT NULL,
        CodigoSap           NVARCHAR(60)     NULL,
        Placas              NVARCHAR(40)     NOT NULL,

        Producto            NVARCHAR(250)    NOT NULL,
        Sku                 NVARCHAR(80)     NULL,
        Documento           NVARCHAR(120)    NULL,
        Chofer              NVARCHAR(160)    NULL,
        Origen              NVARCHAR(180)    NULL,
        Destino             NVARCHAR(180)    NULL,
        Condicion           NVARCHAR(120)    NULL,

        PesoEntrada         DECIMAL(18,2)    NOT NULL,
        PesoSalida          DECIMAL(18,2)    NULL,
        PesoNeto            AS (ABS(PesoEntrada - ISNULL(PesoSalida, PesoEntrada))) PERSISTED,

        CapturaManual       BIT              NOT NULL CONSTRAINT DF_BasculaMovimiento_CapturaManual DEFAULT (0),
        MotivoManual        NVARCHAR(300)    NULL,
        Observaciones       NVARCHAR(1000)   NULL,

        FechaEntrada        DATETIME2(0)     NOT NULL,
        FechaSalida         DATETIME2(0)     NULL,
        UsuarioEntrada      NVARCHAR(160)    NULL,
        UsuarioSalida       NVARCHAR(160)    NULL,

        /* Evidencia operacional de la lectura */
        RawEntrada          NVARCHAR(1000)   NULL,
        RawSalida           NVARCHAR(1000)   NULL,
        PesoEntradaEstable  BIT              NOT NULL CONSTRAINT DF_BasculaMovimiento_EntradaEstable DEFAULT (0),
        PesoSalidaEstable   BIT              NOT NULL CONSTRAINT DF_BasculaMovimiento_SalidaEstable DEFAULT (0),

        /* Control de sincronización */
        CreadoOffline       BIT              NOT NULL CONSTRAINT DF_BasculaMovimiento_CreadoOffline DEFAULT (0),
        FechaCreacionLocal  DATETIME2(0)     NOT NULL,
        FechaSyncServidor   DATETIME2(0)     NOT NULL CONSTRAINT DF_BasculaMovimiento_FechaSync DEFAULT (SYSDATETIME()),
        FechaModificacion   DATETIME2(0)     NOT NULL CONSTRAINT DF_BasculaMovimiento_Mod DEFAULT (SYSDATETIME()),
        RowVer              ROWVERSION       NOT NULL,

        CONSTRAINT UQ_BasculaMovimiento_Guid UNIQUE (MovimientoGuid),
        CONSTRAINT UQ_BasculaMovimiento_TerminalFolio UNIQUE (TerminalId, FolioLocal),
        CONSTRAINT FK_BasculaMovimiento_Terminal FOREIGN KEY (TerminalId) REFERENCES dbo.BasculaTerminal(TerminalId),
        CONSTRAINT CK_BasculaMovimiento_Estatus CHECK (Estatus IN ('PENDIENTE','CERRADO','CANCELADO')),
        CONSTRAINT CK_BasculaMovimiento_PesoEntrada CHECK (PesoEntrada > 0),
        CONSTRAINT CK_BasculaMovimiento_PesoSalida CHECK (PesoSalida IS NULL OR PesoSalida > 0),
        CONSTRAINT CK_BasculaMovimiento_Cierre CHECK (
            (Estatus = 'PENDIENTE' AND PesoSalida IS NULL AND FechaSalida IS NULL)
            OR (Estatus = 'CERRADO' AND PesoSalida IS NOT NULL AND FechaSalida IS NOT NULL)
            OR (Estatus = 'CANCELADO')
        )
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BasculaMovimiento_FechaEntrada' AND object_id = OBJECT_ID('dbo.BasculaMovimiento'))
    CREATE INDEX IX_BasculaMovimiento_FechaEntrada ON dbo.BasculaMovimiento(FechaEntrada DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BasculaMovimiento_Estatus' AND object_id = OBJECT_ID('dbo.BasculaMovimiento'))
    CREATE INDEX IX_BasculaMovimiento_Estatus ON dbo.BasculaMovimiento(Estatus, FechaEntrada DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BasculaMovimiento_Terminal_Estatus' AND object_id = OBJECT_ID('dbo.BasculaMovimiento'))
    CREATE INDEX IX_BasculaMovimiento_Terminal_Estatus ON dbo.BasculaMovimiento(TerminalId, Estatus, FechaEntrada DESC);
GO

/* 3) Bitácora / auditoría */
IF OBJECT_ID('dbo.BasculaBitacora', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BasculaBitacora (
        BitacoraId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BasculaBitacora PRIMARY KEY,
        BitacoraGuid        UNIQUEIDENTIFIER NOT NULL,
        MovimientoGuid      UNIQUEIDENTIFIER NULL,
        TerminalId          NVARCHAR(60)     NOT NULL,
        FolioLocal          NVARCHAR(60)     NULL,
        Fecha               DATETIME2(0)     NOT NULL,
        Usuario             NVARCHAR(160)    NULL,
        Accion              NVARCHAR(250)    NOT NULL,
        Detalle             NVARCHAR(1000)   NULL,
        CreadoOffline       BIT              NOT NULL CONSTRAINT DF_BasculaBitacora_CreadoOffline DEFAULT (0),
        FechaSyncServidor   DATETIME2(0)     NOT NULL CONSTRAINT DF_BasculaBitacora_FechaSync DEFAULT (SYSDATETIME()),

        CONSTRAINT UQ_BasculaBitacora_Guid UNIQUE (BitacoraGuid),
        CONSTRAINT FK_BasculaBitacora_Terminal FOREIGN KEY (TerminalId) REFERENCES dbo.BasculaTerminal(TerminalId),
        CONSTRAINT FK_BasculaBitacora_MovimientoGuid FOREIGN KEY (MovimientoGuid) REFERENCES dbo.BasculaMovimiento(MovimientoGuid)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BasculaBitacora_MovimientoGuid' AND object_id = OBJECT_ID('dbo.BasculaBitacora'))
    CREATE INDEX IX_BasculaBitacora_MovimientoGuid ON dbo.BasculaBitacora(MovimientoGuid, Fecha DESC);
GO

/* 4) Inbox de sincronización. Permite idempotencia por lote/operación. */
IF OBJECT_ID('dbo.BasculaSyncInbox', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BasculaSyncInbox (
        SyncId              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BasculaSyncInbox PRIMARY KEY,
        TerminalId          NVARCHAR(60)     NOT NULL,
        MovimientoGuid      UNIQUEIDENTIFIER NULL,
        TipoOperacion       NVARCHAR(40)     NOT NULL, -- UPSERT_MOVIMIENTO / BITACORA / LOTE
        PayloadHash         VARBINARY(32)    NULL,
        Estatus             NVARCHAR(20)     NOT NULL, -- RECIBIDO / PROCESADO / ERROR
        Mensaje             NVARCHAR(1000)   NULL,
        FechaRecibido       DATETIME2(0)     NOT NULL CONSTRAINT DF_BasculaSyncInbox_Recibido DEFAULT (SYSDATETIME()),
        FechaProcesado      DATETIME2(0)     NULL,

        CONSTRAINT FK_BasculaSyncInbox_Terminal FOREIGN KEY (TerminalId) REFERENCES dbo.BasculaTerminal(TerminalId),
        CONSTRAINT CK_BasculaSyncInbox_Estatus CHECK (Estatus IN ('RECIBIDO','PROCESADO','ERROR'))
    );
END;
GO

/* 5) Procedimiento sugerido para upsert idempotente de movimiento */
/* ==============================================================
   PATCH - BASCULA CAMIONERA
   Guardar registros también en:
   - dbo.BasculaBitacora
   - dbo.BasculaSyncInbox

   Ejecutar este script DESPUÉS del script principal de tablas.
   No borra datos existentes.
   ============================================================== */

 ALTER PROCEDURE dbo.sp_Bascula_UpsertMovimiento
    @MovimientoGuid       UNIQUEIDENTIFIER,
    @TerminalId           NVARCHAR(60),
    @FolioLocal           NVARCHAR(60),
    @Estatus              NVARCHAR(20),
    @TipoMovimiento       NVARCHAR(60),
    @Clasificacion        NVARCHAR(80) = NULL,
    @Tercero              NVARCHAR(250),
    @CodigoSap            NVARCHAR(60) = NULL,
    @Placas               NVARCHAR(40),
    @Producto             NVARCHAR(250),
    @Sku                  NVARCHAR(80) = NULL,
    @Documento            NVARCHAR(120) = NULL,
    @Chofer               NVARCHAR(160) = NULL,
    @Origen               NVARCHAR(180) = NULL,
    @Destino              NVARCHAR(180) = NULL,
    @Condicion            NVARCHAR(120) = NULL,
    @PesoEntrada          DECIMAL(18,2),
    @PesoSalida           DECIMAL(18,2) = NULL,
    @CapturaManual        BIT = 0,
    @MotivoManual         NVARCHAR(300) = NULL,
    @Observaciones        NVARCHAR(1000) = NULL,
    @FechaEntrada         DATETIME2(0),
    @FechaSalida          DATETIME2(0) = NULL,
    @UsuarioEntrada       NVARCHAR(160) = NULL,
    @UsuarioSalida        NVARCHAR(160) = NULL,
    @RawEntrada           NVARCHAR(1000) = NULL,
    @RawSalida            NVARCHAR(1000) = NULL,
    @PesoEntradaEstable   BIT = 0,
    @PesoSalidaEstable    BIT = 0,
    @CreadoOffline        BIT = 0,
    @FechaCreacionLocal   DATETIME2(0)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Ahora DATETIME2(0) = SYSDATETIME();
    DECLARE @Operacion NVARCHAR(20);
    DECLARE @Accion NVARCHAR(250);
    DECLARE @Usuario NVARCHAR(160);
    DECLARE @Detalle NVARCHAR(1000);

    IF @MovimientoGuid IS NULL OR @MovimientoGuid = '00000000-0000-0000-0000-000000000000'
        THROW 51010, 'MovimientoGuid inválido.', 1;

    IF NULLIF(LTRIM(RTRIM(@TerminalId)), '') IS NULL
        THROW 51011, 'TerminalId requerido.', 1;

    IF NULLIF(LTRIM(RTRIM(@FolioLocal)), '') IS NULL
        THROW 51012, 'FolioLocal requerido.', 1;

    IF @PesoEntrada <= 0
        THROW 51000, 'El peso de entrada debe ser mayor a cero.', 1;

    IF @Estatus = 'CERRADO' AND (ISNULL(@PesoSalida, 0) <= 0 OR @FechaSalida IS NULL)
        THROW 51001, 'Para cerrar salida debe existir peso de salida mayor a cero y fecha de salida.', 1;

    IF @Estatus NOT IN ('PENDIENTE','CERRADO','CANCELADO')
        THROW 51013, 'Estatus inválido.', 1;

    SET @Usuario = COALESCE(NULLIF(@UsuarioSalida, ''), NULLIF(@UsuarioEntrada, ''), 'Usuario SIGO');
    SET @Accion = CASE
        WHEN @Estatus = 'PENDIENTE' THEN 'Sincronizó entrada pendiente'
        WHEN @Estatus = 'CERRADO' THEN 'Sincronizó cierre de salida'
        WHEN @Estatus = 'CANCELADO' THEN 'Sincronizó movimiento cancelado'
        ELSE 'Sincronizó movimiento'
    END;

    SET @Detalle = CONCAT(
        'Tipo=', COALESCE(@TipoMovimiento, ''),
        '; Tercero=', COALESCE(@Tercero, ''),
        '; Producto=', COALESCE(@Producto, ''),
        '; Placas=', COALESCE(@Placas, ''),
        '; Entrada=', CONVERT(NVARCHAR(40), @PesoEntrada),
        '; Salida=', COALESCE(CONVERT(NVARCHAR(40), @PesoSalida), 'NULL')
    );

    BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.BasculaTerminal WITH (UPDLOCK, HOLDLOCK) WHERE TerminalId = @TerminalId)
        BEGIN
            INSERT INTO dbo.BasculaTerminal (TerminalId, Nombre, Sitio, UltimaConexion, FechaModificacion)
            VALUES (@TerminalId, @TerminalId, 'Sin clasificar', @Ahora, @Ahora);
        END;

        IF EXISTS (SELECT 1 FROM dbo.BasculaMovimiento WITH (UPDLOCK, HOLDLOCK) WHERE MovimientoGuid = @MovimientoGuid)
        BEGIN
            SET @Operacion = 'UPDATE';

            UPDATE dbo.BasculaMovimiento
            SET
                FolioLocal = @FolioLocal,
                Estatus = @Estatus,
                TipoMovimiento = @TipoMovimiento,
                Clasificacion = @Clasificacion,
                Tercero = @Tercero,
                CodigoSap = @CodigoSap,
                Placas = @Placas,
                Producto = @Producto,
                Sku = @Sku,
                Documento = @Documento,
                Chofer = @Chofer,
                Origen = @Origen,
                Destino = @Destino,
                Condicion = @Condicion,
                PesoEntrada = @PesoEntrada,
                PesoSalida = @PesoSalida,
                CapturaManual = @CapturaManual,
                MotivoManual = @MotivoManual,
                Observaciones = @Observaciones,
                FechaEntrada = @FechaEntrada,
                FechaSalida = @FechaSalida,
                UsuarioEntrada = @UsuarioEntrada,
                UsuarioSalida = @UsuarioSalida,
                RawEntrada = COALESCE(@RawEntrada, RawEntrada),
                RawSalida = COALESCE(@RawSalida, RawSalida),
                PesoEntradaEstable = @PesoEntradaEstable,
                PesoSalidaEstable = @PesoSalidaEstable,
                CreadoOffline = CASE WHEN CreadoOffline = 1 OR @CreadoOffline = 1 THEN 1 ELSE 0 END,
                FechaSyncServidor = @Ahora,
                FechaModificacion = @Ahora
            WHERE MovimientoGuid = @MovimientoGuid;
        END
        ELSE
        BEGIN
            SET @Operacion = 'INSERT';

            INSERT INTO dbo.BasculaMovimiento (
                MovimientoGuid, TerminalId, FolioLocal, Estatus, TipoMovimiento, Clasificacion,
                Tercero, CodigoSap, Placas, Producto, Sku, Documento, Chofer, Origen, Destino, Condicion,
                PesoEntrada, PesoSalida, CapturaManual, MotivoManual, Observaciones,
                FechaEntrada, FechaSalida, UsuarioEntrada, UsuarioSalida,
                RawEntrada, RawSalida, PesoEntradaEstable, PesoSalidaEstable,
                CreadoOffline, FechaCreacionLocal, FechaSyncServidor, FechaModificacion
            )
            VALUES (
                @MovimientoGuid, @TerminalId, @FolioLocal, @Estatus, @TipoMovimiento, @Clasificacion,
                @Tercero, @CodigoSap, @Placas, @Producto, @Sku, @Documento, @Chofer, @Origen, @Destino, @Condicion,
                @PesoEntrada, @PesoSalida, @CapturaManual, @MotivoManual, @Observaciones,
                @FechaEntrada, @FechaSalida, @UsuarioEntrada, @UsuarioSalida,
                @RawEntrada, @RawSalida, @PesoEntradaEstable, @PesoSalidaEstable,
                @CreadoOffline, COALESCE(@FechaCreacionLocal, @Ahora), @Ahora, @Ahora
            );
        END;

        UPDATE dbo.BasculaTerminal
        SET UltimaConexion = @Ahora, FechaModificacion = @Ahora
        WHERE TerminalId = @TerminalId;

        /* Guarda/actualiza control de sincronización. Una fila por movimiento. */
        IF EXISTS (
            SELECT 1
            FROM dbo.BasculaSyncInbox WITH (UPDLOCK, HOLDLOCK)
            WHERE MovimientoGuid = @MovimientoGuid
              AND TerminalId = @TerminalId
              AND TipoOperacion = 'UPSERT_MOVIMIENTO'
        )
        BEGIN
            UPDATE dbo.BasculaSyncInbox
            SET
                Estatus = 'PROCESADO',
                Mensaje = CONCAT('Movimiento ', @Operacion, ' sincronizado correctamente.'),
                FechaProcesado = @Ahora
            WHERE MovimientoGuid = @MovimientoGuid
              AND TerminalId = @TerminalId
              AND TipoOperacion = 'UPSERT_MOVIMIENTO';
        END
        ELSE
        BEGIN
            INSERT INTO dbo.BasculaSyncInbox (
                SyncId, TerminalId, MovimientoGuid, TipoOperacion,
                PayloadHash, Estatus, Mensaje, FechaRecibido, FechaProcesado
            )
            VALUES (
                NEWID(), @TerminalId, @MovimientoGuid, 'UPSERT_MOVIMIENTO',
                NULL, 'PROCESADO', CONCAT('Movimiento ', @Operacion, ' sincronizado correctamente.'), @Ahora, @Ahora
            );
        END;

        /* Bitácora: una fila por estado del movimiento para no duplicar por reintentos. */
        IF NOT EXISTS (
            SELECT 1
            FROM dbo.BasculaBitacora WITH (UPDLOCK, HOLDLOCK)
            WHERE MovimientoGuid = @MovimientoGuid
              AND TerminalId = @TerminalId
              AND FolioLocal = @FolioLocal
              AND Accion = @Accion
        )
        BEGIN
            INSERT INTO dbo.BasculaBitacora (
                BitacoraGuid, MovimientoGuid, TerminalId, FolioLocal,
                Fecha, Usuario, Accion, Detalle, CreadoOffline, FechaSyncServidor
            )
            VALUES (
                NEWID(), @MovimientoGuid, @TerminalId, @FolioLocal,
                @Ahora, @Usuario, @Accion, @Detalle, @CreadoOffline, @Ahora
            );
        END;

    COMMIT TRANSACTION;

    SELECT
        ok = CAST(1 AS BIT),
        MovimientoId = MovimientoId,
        MovimientoGuid = MovimientoGuid,
        FolioLocal = FolioLocal,
        FolioServidor = COALESCE(FolioServidor, FolioLocal),
        Estatus = Estatus,
        FechaSyncServidor = FechaSyncServidor
    FROM dbo.BasculaMovimiento
    WHERE MovimientoGuid = @MovimientoGuid;
END;
GO

/* ==============================================================
   OPCIONAL: Poblar SyncInbox y Bitacora para movimientos que YA existen
   y que se guardaron antes de aplicar este patch.
   ============================================================== */

INSERT INTO dbo.BasculaSyncInbox (
    SyncId, TerminalId, MovimientoGuid, TipoOperacion,
    PayloadHash, Estatus, Mensaje, FechaRecibido, FechaProcesado
)
SELECT
    NEWID(), m.TerminalId, m.MovimientoGuid, 'UPSERT_MOVIMIENTO',
    NULL, 'PROCESADO', 'Movimiento existente regularizado en SyncInbox.',
    SYSDATETIME(), SYSDATETIME()
FROM dbo.BasculaMovimiento m
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.BasculaSyncInbox s
    WHERE s.MovimientoGuid = m.MovimientoGuid
      AND s.TerminalId = m.TerminalId
      AND s.TipoOperacion = 'UPSERT_MOVIMIENTO'
);
GO

INSERT INTO dbo.BasculaBitacora (
    BitacoraGuid, MovimientoGuid, TerminalId, FolioLocal,
    Fecha, Usuario, Accion, Detalle, CreadoOffline, FechaSyncServidor
)
SELECT
    NEWID(), m.MovimientoGuid, m.TerminalId, m.FolioLocal,
    SYSDATETIME(), COALESCE(NULLIF(m.UsuarioSalida, ''), NULLIF(m.UsuarioEntrada, ''), 'Usuario SIGO'),
    CASE
        WHEN m.Estatus = 'PENDIENTE' THEN 'Sincronizó entrada pendiente'
        WHEN m.Estatus = 'CERRADO' THEN 'Sincronizó cierre de salida'
        WHEN m.Estatus = 'CANCELADO' THEN 'Sincronizó movimiento cancelado'
        ELSE 'Sincronizó movimiento'
    END,
    CONCAT('Regularizado desde BasculaMovimiento. Estatus=', m.Estatus, '; Folio=', m.FolioLocal),
    m.CreadoOffline,
    SYSDATETIME()
FROM dbo.BasculaMovimiento m
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.BasculaBitacora b
    WHERE b.MovimientoGuid = m.MovimientoGuid
      AND b.TerminalId = m.TerminalId
      AND b.FolioLocal = m.FolioLocal
      AND b.Accion = CASE
            WHEN m.Estatus = 'PENDIENTE' THEN 'Sincronizó entrada pendiente'
            WHEN m.Estatus = 'CERRADO' THEN 'Sincronizó cierre de salida'
            WHEN m.Estatus = 'CANCELADO' THEN 'Sincronizó movimiento cancelado'
            ELSE 'Sincronizó movimiento'
        END
);
GO

/* Validación rápida */
SELECT COUNT(*) AS TotalMovimientos FROM dbo.BasculaMovimiento;
SELECT COUNT(*) AS TotalBitacora FROM dbo.BasculaBitacora;
SELECT COUNT(*) AS TotalSyncInbox FROM dbo.BasculaSyncInbox;
GO
