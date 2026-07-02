DECLARE @FechaInicio DATE = '2026-05-10';
DECLARE @FechaFin DATE = '2026-06-10';

SELECT
    p.Id,
    p.Consecutivo,
    p.Serie,
    s.NombreSerie,
    s.Sucursal,
    s.sucursalId,

    p.FechaEntrega,
    p.FechaEmbarque,
    p.HoraEmbarque,

    p.Cliente,
    p.Vendedor,
    p.Ruta,
    p.Presentacion,
    p.Observacion,

    p.Saldo,
    p.OtrosPedidos,
    p.Credito,
    p.FechaRegistro,
    p.Estatus,
    p.Documentacion,
    p.AutorizacionPresupuesto,
    p.AutorizacionPrecio,
    p.AutorizacionCredito,

    ISNULL(l.Fletera, '') AS Fletera,
    ISNULL(l.EspacioTarimas, 0) AS EspacioTarimas,
    CONVERT(VARCHAR(5), l.HoraLlegadaUnidad, 108) AS HoraLlegadaUnidad,
    ISNULL(l.EstatusLogistico, 'PENDIENTE FLETERA') AS EstatusLogistico,
    ISNULL(l.ObservacionLogistica, '') AS ObservacionLogistica,
    ISNULL(l.MotivoCancelacion, '') AS MotivoCancelacion,
    ISNULL(l.MotivoCancelacionFletera, '') AS MotivoCancelacionFletera,
    ISNULL(l.Cancelado, 0) AS Cancelado,
    ISNULL(l.CanceladoFletera, 0) AS CanceladoFletera
FROM dbo.OrdenVenta p
INNER JOIN dbo.series s 
    ON UPPER(LTRIM(RTRIM(s.NombreSerie))) = UPPER(LTRIM(RTRIM(p.Serie)))
LEFT JOIN dbo.LogisticaOrdenVenta l
    ON l.OrdenVentaId = p.Id
WHERE
    p.FechaEntrega >= @FechaInicio
    AND p.FechaEntrega < DATEADD(DAY, 1, @FechaFin)
    AND UPPER(LTRIM(RTRIM(s.Sucursal))) = 'MATRIZ'
    AND p.Estatus = 0
ORDER BY
    p.FechaEntrega,
    p.Ruta,
    p.Cliente;


    select * from LogisticaOrdenVenta



    CREATE TABLE dbo.LogisticaOrdenVenta (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OrdenVentaId INT NOT NULL,

    Fletera VARCHAR(150) NULL,
    EspacioTarimas INT NULL,
    HoraLlegadaUnidad TIME NULL,

    EstatusLogistico VARCHAR(50) NOT NULL DEFAULT 'PENDIENTE FLETERA',

    ObservacionLogistica VARCHAR(MAX) NULL,
    MotivoCancelacion VARCHAR(MAX) NULL,
    MotivoCancelacionFletera VARCHAR(MAX) NULL,

    Cancelado BIT NOT NULL DEFAULT 0,
    CanceladoFletera BIT NOT NULL DEFAULT 0,

    UsuarioRegistro VARCHAR(100) NULL,
    FechaRegistro DATETIME NOT NULL DEFAULT GETDATE(),

    UsuarioModificacion VARCHAR(100) NULL,
    FechaModificacion DATETIME NULL
);