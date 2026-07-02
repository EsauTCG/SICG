CREATE TABLE dbo.VentasHistoricas (
    Sucursal        varchar(20)   NOT NULL,
    DOC             varchar(80)   NOT NULL,
    Cliente         varchar(200)  NULL,
    ClienteID       varchar(50)   NULL,
    Clasificacion   varchar(100)  NULL,
    Unidad          decimal(18,6) NULL,
    SKU             varchar(50)   NOT NULL,
    Producto        varchar(200)  NULL,
    Nombre          varchar(200)  NULL,
    Peso            decimal(18,6) NULL,
    Importe         decimal(18,6) NULL,
    FechaVenta      date         NULL,
    FechaProduccion date         NULL,
    Costo           decimal (18,6) NULL
    

    -- auditoría opcional
    LastSyncAt      datetime2(0)  NOT NULL CONSTRAINT DF_VCC_LastSyncAt DEFAULT (SYSUTCDATETIME())
);

-- Llave única anti-duplicado
CREATE UNIQUE INDEX UX_VCC_Key
ON dbo.VentasHistoricas (Sucursal, DOC, SKU, FechaProduccion);


--PLANTA 1 
USE [SIGO]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[Sync_VentasHistoricas_P1]
    @FechaDesde date,
    @FechaHasta date = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Hasta date = ISNULL(@FechaHasta, CONVERT(date, GETDATE()));
    DECLARE @sql nvarchar(max);

    SET @sql = N'
INSERT INTO dbo.VentasHistoricas
(
  Sucursal, DOC, Cliente, ClienteID, Clasificacion,
  Unidad, SKU, Producto, Nombre, Peso, Importe,
  FechaVenta, FechaProduccion, LastSyncAt
)
SELECT
  q.Sucursal,
  q.DOC,
  q.Cliente,
  CAST(q.ClienteID AS varchar(50)) AS ClienteID,
  q.Clasificacion,
  q.Unidad,
  q.SKU,
  q.Producto,
  q.Nombre,
  q.Peso,
  q.Importe,
  q.FechaVenta,
  q.FechaProduccion,
  SYSUTCDATETIME()
FROM OPENQUERY([MEAT_P1], ''
;WITH PrRef AS (
    SELECT mr.MovimientoId, MAX(mr.Referencia) AS ProduccionId
    FROM CommerciaNet.dbo.MovimientoReferencia mr WITH (NOLOCK)
    WHERE mr.Tipo = ''''pr''''
    GROUP BY mr.MovimientoId
),
MovDet AS (
    SELECT
        ''''EMPACADORA'''' AS Sucursal,
        (d.EmpresaId + ''''.'''' + d.SucursalId + ''''.'''' + d.OperacionId + ''''.'''' + d.Folio) AS DOC,
        CASE WHEN NULLIF(LTRIM(RTRIM(c.Nombre)), '''''''') IS NULL THEN c.RazonSocial ELSE c.Nombre END AS Cliente,
        c.CodigoId AS ClienteID,
        ISNULL(tc.Nombre, ''''Cliente Sin Clasificacion'''') AS Clasificacion,
        m.ArticuloId AS SKU,
        a.Nombre AS Producto,
        l.Nombre AS Nombre,
        CONVERT(date, pr.FechaProduccion) AS FechaProduccion,
        CAST(m.Unidad AS decimal(18,6)) * o.FactorEfectivo AS Unidad,
        CAST(m.FactorUnidad AS decimal(18,6)) * o.FactorEfectivo AS Peso,
        CAST(m.PublicoInicial AS decimal(18,6)) * CAST(m.FactorUnidad AS decimal(18,6)) * o.FactorEfectivo AS Importe,
        CONVERT(date, d.FechaDocumento) AS FechaVenta
    FROM CommerciaNet.dbo.Movimiento m WITH (NOLOCK)
    INNER JOIN CommerciaNet.dbo.Documento d WITH (NOLOCK)
        ON  m.Folio = d.Folio
        AND m.OperacionId = d.OperacionId
        AND m.SucursalId = d.SucursalId
        AND m.EmpresaId = d.EmpresaId
    INNER JOIN CommerciaNet.dbo.Operacion o WITH (NOLOCK)
        ON d.OperacionId = o.OperacionId
    INNER JOIN CommerciaNet.dbo.Cliente c WITH (NOLOCK)
        ON d.ClienteProveedorId = c.ClienteId
    LEFT JOIN CommerciaNet.dbo.TipoCliente tc WITH (NOLOCK)
        ON c.Clasificacion = tc.TipoClienteId
    LEFT JOIN CommerciaNet.dbo.Articulo a WITH (NOLOCK)
        ON m.ArticuloId = a.ArticuloId
    LEFT JOIN CommerciaNet.dbo.Linea l WITH (NOLOCK)
        ON a.LineaId = l.LineaId
    LEFT JOIN PrRef r
        ON r.MovimientoId = (m.EmpresaId + ''''.'''' + m.SucursalId + ''''.'''' + m.OperacionId + ''''.'''' + m.Folio + ''''.'''' + CONVERT(varchar(20), m.RenglonId))
    LEFT JOIN Meat.dbo.Produccion pr WITH (NOLOCK)
        ON pr.ProduccionId = r.ProduccionId
       AND pr.UltimoProcesoId <> 29
    WHERE
        m.OperacionId = ''''VREM''''
        AND d.Estatus <> ''''Z0''''
        AND d.FechaDocumento >= ''''' + CONVERT(varchar(10), @FechaDesde, 120) + N'''''
        AND d.FechaDocumento <  DATEADD(day, 1, ''''' + CONVERT(varchar(10), @Hasta, 120) + N''''' )
        AND d.SucursalId <> ''''SUC02''''
),
VentasAgg AS (
    SELECT
        Sucursal, DOC, Cliente, ClienteID, Clasificacion,
        SKU, Producto, Nombre, FechaVenta, FechaProduccion,
        SUM(Unidad) AS Unidad,
        SUM(Peso) AS Peso,
        SUM(Importe) AS Importe
    FROM MovDet
    GROUP BY
        Sucursal, DOC, Cliente, ClienteID, Clasificacion,
        SKU, Producto, Nombre, FechaVenta, FechaProduccion
)
SELECT
  Sucursal, DOC, Cliente, ClienteID, Clasificacion,
  Unidad, SKU, Producto, Nombre, Peso, Importe,
  FechaVenta, FechaProduccion
FROM VentasAgg
'') AS q
WHERE NOT EXISTS (
  SELECT 1
  FROM dbo.VentasHistoricas vh
  WHERE vh.Sucursal COLLATE DATABASE_DEFAULT = q.Sucursal COLLATE DATABASE_DEFAULT
    AND vh.DOC      COLLATE DATABASE_DEFAULT = q.DOC      COLLATE DATABASE_DEFAULT
    AND vh.SKU      COLLATE DATABASE_DEFAULT = q.SKU      COLLATE DATABASE_DEFAULT
    AND vh.FechaVenta = q.FechaVenta
    AND ISNULL(vh.FechaProduccion, ''19000101'') = ISNULL(q.FechaProduccion, ''19000101'')
);
';

    EXEC sys.sp_executesql @sql;
END
GO

--FIN PLANTA 1 


--INICIO PLANTA TIF 

USE [SIGO]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[Sync_VentasHistoricas_TIF]
    @FechaDesde date,
    @FechaHasta date = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Hasta date = ISNULL(@FechaHasta, CONVERT(date, GETDATE()));
    DECLARE @sql  nvarchar(max);

    -- 1) staging en temp (colación local para evitar conflictos)
    IF OBJECT_ID('tempdb..#q') IS NOT NULL DROP TABLE #q;
    CREATE TABLE #q
    (
        Sucursal        varchar(30)   COLLATE DATABASE_DEFAULT NOT NULL,
        DOC             varchar(120)  COLLATE DATABASE_DEFAULT NOT NULL,
        Cliente         varchar(250)  COLLATE DATABASE_DEFAULT NULL,
        ClienteID       varchar(50)   COLLATE DATABASE_DEFAULT NULL,
        Clasificacion   varchar(150)  COLLATE DATABASE_DEFAULT NULL,
        Unidad          decimal(18,6) NULL,
        SKU             varchar(60)   COLLATE DATABASE_DEFAULT NOT NULL,
        Producto        varchar(250)  COLLATE DATABASE_DEFAULT NULL,
        Nombre          varchar(250)  COLLATE DATABASE_DEFAULT NULL,
        Peso            decimal(18,6) NULL,
        Importe         decimal(18,6) NULL,
        FechaVenta      date          NOT NULL,
        FechaProduccion date          NULL
    );

    -- 2) SQL dinámico SOLO para traer OPENQUERY con fechas
    SET @sql = N'
INSERT INTO #q
(
  Sucursal, DOC, Cliente, ClienteID, Clasificacion,
  Unidad, SKU, Producto, Nombre, Peso, Importe,
  FechaVenta, FechaProduccion
)
SELECT
  CAST(qx.Sucursal      AS varchar(30))  COLLATE DATABASE_DEFAULT,
  CAST(qx.DOC           AS varchar(120)) COLLATE DATABASE_DEFAULT,
  CAST(qx.Cliente       AS varchar(250)) COLLATE DATABASE_DEFAULT,
  CAST(qx.ClienteID     AS varchar(50))  COLLATE DATABASE_DEFAULT,
  CAST(qx.Clasificacion AS varchar(150)) COLLATE DATABASE_DEFAULT,
  qx.Unidad,
  CAST(qx.SKU           AS varchar(60))  COLLATE DATABASE_DEFAULT,
  CAST(qx.Producto      AS varchar(250)) COLLATE DATABASE_DEFAULT,
  CAST(qx.Nombre        AS varchar(250)) COLLATE DATABASE_DEFAULT,
  qx.Peso,
  qx.Importe,
  qx.FechaVenta,
  qx.FechaProduccion
FROM OPENQUERY([Meat_TIF], ''
;WITH PrRef AS (
    SELECT mr.MovimientoId, MAX(mr.Referencia) AS ProduccionId
    FROM TIF_CommerciaNet.dbo.MovimientoReferencia mr WITH (NOLOCK)
    WHERE mr.Tipo = ''''pr''''
    GROUP BY mr.MovimientoId
),
MovDet AS (
    SELECT
        ''''TIF'''' AS Sucursal,
        (d.EmpresaId + ''''.'''' + d.SucursalId + ''''.'''' + d.OperacionId + ''''.'''' + d.Folio) AS DOC,
        CASE WHEN NULLIF(LTRIM(RTRIM(c.Nombre)), '''''''') IS NULL THEN c.RazonSocial ELSE c.Nombre END AS Cliente,
        c.CodigoId AS ClienteID,
        ISNULL(tc.Nombre, ''''Cliente Sin Clasificacion'''') AS Clasificacion,
        m.ArticuloId AS SKU,
        a.Nombre AS Producto,
        l.Nombre AS Nombre,
        CONVERT(date, pr.FechaProduccion) AS FechaProduccion,
        CAST(m.Unidad AS decimal(18,6)) * o.FactorEfectivo AS Unidad,
        CAST(m.FactorUnidad AS decimal(18,6)) * o.FactorEfectivo AS Peso,
        CAST(m.PublicoInicial AS decimal(18,6)) * CAST(m.FactorUnidad AS decimal(18,6)) * o.FactorEfectivo AS Importe,
        CONVERT(date, d.FechaDocumento) AS FechaVenta
    FROM TIF_CommerciaNet.dbo.Movimiento m WITH (NOLOCK)
    INNER JOIN TIF_CommerciaNet.dbo.Documento d WITH (NOLOCK)
        ON  m.Folio = d.Folio
        AND m.OperacionId = d.OperacionId
        AND m.SucursalId = d.SucursalId
        AND m.EmpresaId = d.EmpresaId
    INNER JOIN TIF_CommerciaNet.dbo.Operacion o WITH (NOLOCK)
        ON d.OperacionId = o.OperacionId
    INNER JOIN TIF_CommerciaNet.dbo.Cliente c WITH (NOLOCK)
        ON d.ClienteProveedorId = c.ClienteId
    LEFT JOIN TIF_CommerciaNet.dbo.TipoCliente tc WITH (NOLOCK)
        ON c.Clasificacion = tc.TipoClienteId
    LEFT JOIN TIF_CommerciaNet.dbo.Articulo a WITH (NOLOCK)
        ON m.ArticuloId = a.ArticuloId
    LEFT JOIN TIF_CommerciaNet.dbo.Linea l WITH (NOLOCK)
        ON a.LineaId = l.LineaId
    LEFT JOIN PrRef r
        ON r.MovimientoId = (m.EmpresaId + ''''.'''' + m.SucursalId + ''''.'''' + m.OperacionId + ''''.'''' + m.Folio + ''''.'''' + CONVERT(varchar(20), m.RenglonId))
    LEFT JOIN TIF_meat.dbo.Produccion pr WITH (NOLOCK)
        ON pr.ProduccionId = r.ProduccionId
       AND pr.UltimoProcesoId <> 29
    WHERE
        m.OperacionId = ''''VREM''''
        AND d.Estatus <> ''''Z0''''
        AND d.FechaDocumento >= ''''' + CONVERT(varchar(10), @FechaDesde, 120) + N'''''
        AND d.FechaDocumento <  DATEADD(day, 1, ''''' + CONVERT(varchar(10), @Hasta, 120) + N''''' )
        AND m.ArticuloId NOT LIKE ''''RD0%''''
),
VentasAgg AS (
    SELECT
        Sucursal, DOC, Cliente, ClienteID, Clasificacion,
        SKU, Producto, Nombre, FechaVenta, FechaProduccion,
        SUM(Unidad)   AS Unidad,
        SUM(Peso)     AS Peso,
        SUM(Importe)  AS Importe
    FROM MovDet
    GROUP BY
        Sucursal, DOC, Cliente, ClienteID, Clasificacion,
        SKU, Producto, Nombre, FechaVenta, FechaProduccion
)
SELECT
  Sucursal, DOC, Cliente, ClienteID, Clasificacion,
  Unidad, SKU, Producto, Nombre, Peso, Importe,
  FechaVenta, FechaProduccion
FROM VentasAgg
'') AS qx;
';

    EXEC sys.sp_executesql @sql;

    -- 3) Inserta SOLO lo nuevo (ya sin SQL dinámico)
    INSERT INTO dbo.VentasHistoricas
    (
      Sucursal, DOC, Cliente, ClienteID, Clasificacion,
      Unidad, SKU, Producto, Nombre, Peso, Importe,
      FechaVenta, FechaProduccion, LastSyncAt
    )
    SELECT
      q.Sucursal, q.DOC, q.Cliente, q.ClienteID, q.Clasificacion,
      q.Unidad, q.SKU, q.Producto, q.Nombre, q.Peso, q.Importe,
      q.FechaVenta, q.FechaProduccion, SYSUTCDATETIME()
    FROM #q q
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.VentasHistoricas vh
        WHERE vh.Sucursal = q.Sucursal
          AND vh.DOC      = q.DOC
          AND vh.SKU      = q.SKU
          AND vh.FechaVenta = q.FechaVenta
      AND ISNULL(vh.FechaProduccion, DATEFROMPARTS(1900,1,1))
    = ISNULL(q.FechaProduccion,  DATEFROMPARTS(1900,1,1))
    );
END
GO

  --EXEC dbo.Sync_VentasHistoricas_TIF
  --@FechaDesde = '2026-01-01',
  -- @FechaHasta = NULL;



--FIN PLANTA TIF









EXEC dbo.Sync_VentasHistoricas_P1
  @FechaDesde = '2026-01-01',
 @FechaHasta = NULL;


  
  EXEC dbo.Sync_VentasHistoricas_TIF
  @FechaDesde = '2026-01-01',
   @FechaHasta = NULL;
