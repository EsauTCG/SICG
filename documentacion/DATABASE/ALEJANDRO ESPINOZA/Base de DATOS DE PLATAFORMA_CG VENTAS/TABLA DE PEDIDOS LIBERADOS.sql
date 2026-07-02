/* =========================================================
   TABLA: PedidoVenta (encabezado)
   ========================================================= */
IF OBJECT_ID('dbo.PedidoVenta', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidoVenta
    (
        Id                       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PedidoVenta PRIMARY KEY,
        -- Relación con la OV original
        OrdenVentaId             INT               NOT NULL,
        OrdenVentaConsecutivo    VARCHAR(50)       NOT NULL,

        -- Snapshot de datos (opcionales, pero útiles para consulta)
        Cliente                  VARCHAR(200)      NOT NULL,
        Vendedor                 VARCHAR(100)      NOT NULL,
        FechaEntrega             DATETIME2         NULL,

        -- Gestión
        FechaEmbarque            DATETIME2         NULL,
        AlmacenSurtir            VARCHAR(50)       NULL,
        ObservacionGestion       VARCHAR(1000)     NULL,
        FechaGestion             DATETIME2         NOT NULL CONSTRAINT DF_PedidoVenta_FechaGestion DEFAULT (SYSUTCDATETIME()),

        -- Totales
        TotalImporte             DECIMAL(18,2)     NOT NULL CONSTRAINT DF_PedidoVenta_TotalImporte DEFAULT(0),
        TotalPeso                DECIMAL(18,3)     NOT NULL CONSTRAINT DF_PedidoVenta_TotalPeso    DEFAULT(0)
		

        -- Reglas básicas
        CONSTRAINT CK_PedidoVenta_Totales_NoNegativos
            CHECK (TotalImporte >= 0 AND TotalPeso >= 0)
    );

    -- Índices útiles
    CREATE INDEX IX_PedidoVenta_OrdenVentaId ON dbo.PedidoVenta (OrdenVentaId);
    CREATE INDEX IX_PedidoVenta_OrdenVentaConsecutivo ON dbo.PedidoVenta (OrdenVentaConsecutivo);
	
END
GO

/* =========================================================
   (OPCIONAL) FK hacia tu tabla OrdenVenta
   Descomenta si existe dbo.OrdenVenta(Id)
   ========================================================= */
/*
IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[OrdenVenta]') AND type = 'U')
BEGIN
    ALTER TABLE dbo.PedidoVenta  WITH CHECK
    ADD CONSTRAINT FK_PedidoVenta_OrdenVenta
        FOREIGN KEY (OrdenVentaId) REFERENCES dbo.OrdenVenta(Id)
        ON UPDATE NO ACTION
        ON DELETE NO ACTION;  -- o SET NULL si permites

    -- Si quieres evitar que existan varios "gestionados" por OV, puedes agregar UNIQUE:
    -- CREATE UNIQUE INDEX UX_PedidoVenta_OrdenVentaId ON dbo.PedidoVenta(OrdenVentaId);
END
GO
*/




/* =========================================================
   TABLA: PedidoVentaProducto (detalle)
   ========================================================= */
IF OBJECT_ID('dbo.PedidoVentaProducto', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidoVentaProducto
    (
        Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PedidoVentaProducto PRIMARY KEY,
        PedidoVentaId    INT               NOT NULL,

        ProductoCodigo   VARCHAR(50)       NOT NULL,
        ProductoNombre   VARCHAR(200)      NOT NULL,

        KilosCaja        DECIMAL(18,3)     NOT NULL CONSTRAINT DF_PVP_KilosCaja DEFAULT(0),
        Precio           DECIMAL(18,2)     NOT NULL CONSTRAINT DF_PVP_Precio    DEFAULT(0),
        Cajas            INT               NOT NULL CONSTRAINT DF_PVP_Cajas     DEFAULT(0),
		Almacen          NVARCHAR(50) NULL

        CONSTRAINT CK_PVP_NoNegativos
            CHECK (KilosCaja >= 0 AND Precio >= 0 AND Cajas >= 0)
    );

    -- FK al encabezado (borrado en cascada para el detalle)
    ALTER TABLE dbo.PedidoVentaProducto WITH CHECK
    ADD CONSTRAINT FK_PedidoVentaProducto_PedidoVenta
        FOREIGN KEY (PedidoVentaId) REFERENCES dbo.PedidoVenta(Id)
        ON UPDATE NO ACTION
        ON DELETE CASCADE;

    -- Índice para recuperar rápido el detalle por encabezado
    CREATE INDEX IX_PedidoVentaProducto_PedidoVentaId ON dbo.PedidoVentaProducto(PedidoVentaId);

    -- (Opcional) evitar duplicados por producto dentro del mismo pedido gestionado:
    -- CREATE UNIQUE INDEX UX_PVP_PedidoVenta_Producto ON dbo.PedidoVentaProducto(PedidoVentaId, ProductoCodigo);
END
GO



-- 4) (Opcional) Índice si filtras/reportas por Almacén
CREATE NONCLUSTERED INDEX IX_PedidoVentaProducto_Almacen
ON dbo.PedidoVentaProducto (Almacen);