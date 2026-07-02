CREATE TABLE dbo.PresupuestoLineasHistorico
(
    Id INT IDENTITY(1,1) PRIMARY KEY,

    FechaRegistro       DATETIME        NOT NULL,

    -- OV
    OrdenVentaId        INT             NOT NULL,
    OrdenVentaConsecutivo VARCHAR(50)   NOT NULL,

    -- Cliente
    ClienteId           VARCHAR(50)     NOT NULL,
    ClienteNombre       VARCHAR(255)    NULL,

    -- Producto
    ProductoCodigo      VARCHAR(50)     NOT NULL,
    ProductoNombre      VARCHAR(255)    NULL,

    -- Periodo del presupuesto
    Mes                 INT             NOT NULL,
    Anio                INT             NOT NULL,

    -- Valores de presupuesto
    KilosPresupuestoMes DECIMAL(18,2)   NOT NULL,
    KilosConsumidosAntes DECIMAL(18,2)  NOT NULL,
    KilosSolicitadosLinea DECIMAL(18,2) NOT NULL,
    KilosAutorizados      DECIMAL(18,2) NOT NULL,

    -- Fuente del presupuesto (CLIENTE / CEDIS / SIN PRESUPUESTO)
    FuentePresupuesto   VARCHAR(50)     NULL,

    -- Usuario que autorizó
    Usuario             VARCHAR(100)    NULL
);


CREATE INDEX IX_PresupuestoHistorico_ClientePeriodo
ON dbo.PresupuestoLineasHistorico (ClienteId, Mes, Anio);


CREATE INDEX IX_PresupuestoHistorico_OV
ON dbo.PresupuestoLineasHistorico (OrdenVentaId);



select * from PresupuestoLineasHistorico