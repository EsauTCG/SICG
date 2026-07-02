--select top 100  * from ArticuloSap
--select top 100 * from CatalogoPrecioSap
--select top 100  * from clientesap
--select top 100 * from SurtidoEncabezado
--select top 100 * from surtidodetalle
--select top 100 * from TransferenciaSurtido
--select top 100 * from InventarioSigo
--select top 100 * from InventarioAlmacenado_Meat
--select * from ClasificacionProduccion



--CREATE TABLE PrecioCompetenciaSemana (
--    Id INT IDENTITY(1,1) PRIMARY KEY,
--    FechaRegistro DATETIME NOT NULL DEFAULT GETDATE(),
--    FechaCorte DATE NULL,
--    Sku NVARCHAR(50) NOT NULL,
--    Denes DECIMAL(18,2) NULL,
--    Tc DECIMAL(18,2) NULL,
--    Freasa DECIMAL(18,2) NULL,
--    Comentarios NVARCHAR(300) NULL,
--    UsuarioRegistro NVARCHAR(100) NULL
--);
--GO
--insert into PrecioCompetenciaSemana values ('20260418 18:06:35.173',	'20260418',	'N003',	NULL,	'95.00',	NULL,'',		'JOSE ALEJANDRO ESPINOZA' )

--select * from PrecioCompetenciaSemana
--update PrecioCompetenciaSemana set FechaCorte = '2026-04-13 18:58:19.060' where Id = 2

DECLARE @pFechaCorte DATE = '2026-04-20';
DECLARE @FechaCorte DATE = @pFechaCorte;

DECLARE @pSku VARCHAR(50) = NULL;
DECLARE @pProducto VARCHAR(200) = NULL;
DECLARE @pClasificacion VARCHAR(50) = NULL;

-- Inventario lunes a lunes
DECLARE @FechaInvActual DATE   = @FechaCorte;
DECLARE @FechaInvAnterior DATE = DATEADD(DAY, -7, @FechaCorte);

-- Ventas
DECLARE @DesdeSemana7 DATE  = DATEADD(DAY, -6, @FechaCorte);
DECLARE @DesdeSemana14 DATE = DATEADD(DAY, -13, @FechaCorte);
DECLARE @HastaSemana14 DATE = DATEADD(DAY, -7, @FechaCorte);
DECLARE @DesdeSemana15 DATE = DATEADD(DAY, -20, @FechaCorte);
DECLARE @HastaSemana15 DATE = DATEADD(DAY, -14, @FechaCorte);

DECLARE @ColInvAnterior SYSNAME = N'inventario ' + CONVERT(VARCHAR(10), @FechaInvAnterior, 103);
DECLARE @ColInvActual   SYSNAME = N'inventario ' + CONVERT(VARCHAR(10), @FechaInvActual, 103);

DECLARE @sql NVARCHAR(MAX) = N'
WITH Productos AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
        ProductoNombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), ''''), a.ProductoCodigo),
        U_MASTER = UPPER(LTRIM(RTRIM(ISNULL(a.U_MASTER, '''')))),
        ClasificacionNombre = ISNULL(cp.Nombre, ''POR DEFINIR'')
    FROM dbo.ArticuloSap a
    LEFT JOIN dbo.ClasificacionProduccion cp
        ON a.U_Clas_Prod = cp.ClasificacionId
),
InventarioFechas AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(iam.Sku))),
        InventarioAnterior = SUM(CASE WHEN iam.FechaInventario = @FechaInvAnterior THEN CAST(ISNULL(iam.PesoNeto, 0) AS DECIMAL(18,4)) ELSE 0 END),
        InventarioActual   = SUM(CASE WHEN iam.FechaInventario = @FechaInvActual   THEN CAST(ISNULL(iam.PesoNeto, 0) AS DECIMAL(18,4)) ELSE 0 END)
    FROM dbo.InventarioAlmacenado_Meat iam
    WHERE ISNULL(iam.Sku, '''') <> ''''
      AND iam.FechaInventario IN (@FechaInvAnterior, @FechaInvActual)
    GROUP BY UPPER(LTRIM(RTRIM(iam.Sku)))
),
PrecioActual AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(c.ProductoCodigo))),
        PrecioLista = MAX(CAST(ISNULL(c.Precio, 0) AS DECIMAL(18,4)))
    FROM dbo.CatalogoPrecioSap c
    WHERE ISNULL(c.ProductoCodigo, '''') <> ''''
    GROUP BY UPPER(LTRIM(RTRIM(c.ProductoCodigo)))
),
VentaOV AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
        FechaMov = CAST(se.FechaValidacion AS DATE),
        Kg = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
    FROM dbo.SurtidoEncabezado se
    INNER JOIN dbo.SurtidoDetalle sd
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    WHERE se.FechaValidacion IS NOT NULL
    GROUP BY
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        CAST(se.FechaValidacion AS DATE)
),
VentaTR AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
        FechaMov = CAST(t.FechaSolicitud AS DATE),
        Kg = SUM(CAST(ISNULL(ts.KgSurtido, 0) AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    INNER JOIN dbo.Transferencias t
        ON t.Id = ts.TransferenciaId
    WHERE t.FechaSolicitud IS NOT NULL
      AND ts.KgSurtido > 0
    GROUP BY
        UPPER(LTRIM(RTRIM(ts.Sku))),
        CAST(t.FechaSolicitud AS DATE)
),
VentaReal AS (
    SELECT SKU, FechaMov, Kg FROM VentaOV
    UNION ALL
    SELECT SKU, FechaMov, Kg FROM VentaTR
),
Semanas AS (
    SELECT
        vr.SKU,
        KgSemana7  = SUM(CASE WHEN vr.FechaMov >= @DesdeSemana7  AND vr.FechaMov <= @FechaCorte    THEN vr.Kg ELSE 0 END),
        KgSemana14 = SUM(CASE WHEN vr.FechaMov >= @DesdeSemana14 AND vr.FechaMov <= @HastaSemana14 THEN vr.Kg ELSE 0 END),
        KgSemana15 = SUM(CASE WHEN vr.FechaMov >= @DesdeSemana15 AND vr.FechaMov <= @HastaSemana15 THEN vr.Kg ELSE 0 END)
    FROM VentaReal vr
    GROUP BY vr.SKU
),
PedidosOV AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        KgPendiente = SUM(
            CAST(
                CASE
                    WHEN o.Estatus = 5 AND os.Id IS NOT NULL THEN 0
                    ELSE CASE
                        WHEN (CAST(op.Peso AS DECIMAL(18,4)) - ISNULL(sa.KgSurtido, 0)) < 0 THEN 0
                        ELSE (CAST(op.Peso AS DECIMAL(18,4)) - ISNULL(sa.KgSurtido, 0))
                    END
                END
            AS DECIMAL(18,4))
        )
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.Series ser
        ON o.Serie = ser.NombreSerie
    INNER JOIN dbo.OrdenVentaProducto op
        ON op.PedidoId = o.Id
    LEFT JOIN (
        SELECT DISTINCT o2.Id
        FROM dbo.OrdenVenta o2
        JOIN dbo.Subpedido sp2
            ON sp2.OrdenVentaId = o2.Id
        JOIN dbo.SurtidoEncabezado se2
            ON se2.SolicitudSurtidoId = sp2.U_DocMeat
    ) os
        ON os.Id = o.Id
    LEFT JOIN (
        SELECT
            PedidoId = o3.Id,
            SKU = UPPER(LTRIM(RTRIM(sd3.Articulo))),
            KgSurtido = SUM(CAST(sd3.Kg AS DECIMAL(18,4)))
        FROM dbo.OrdenVenta o3
        JOIN dbo.Subpedido sp3
            ON sp3.OrdenVentaId = o3.Id
        JOIN dbo.SurtidoEncabezado se3
            ON se3.SolicitudSurtidoId = sp3.U_DocMeat
        JOIN dbo.SurtidoDetalle sd3
            ON sd3.SolicitudSurtidoId = se3.SolicitudSurtidoId
        WHERE se3.FechaValidacion IS NOT NULL
        GROUP BY
            o3.Id,
            UPPER(LTRIM(RTRIM(sd3.Articulo)))
    ) sa
        ON sa.PedidoId = o.Id
       AND sa.SKU = UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
    WHERE o.FechaEntrega IS NOT NULL
      AND TRY_CONVERT(DATE, o.FechaEntrega) >= @FechaCorte
      AND o.Estatus BETWEEN 1 AND 5
      AND ser.Sucursal = ''MATRIZ''
    GROUP BY
        UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
),
PedidosTR AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
        KgPendiente = SUM(
            CAST(
                CASE
                    WHEN (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0)) < 0 THEN 0
                    ELSE (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0))
                END
            AS DECIMAL(18,4))
        )
    FROM dbo.Transferencias t
    JOIN dbo.TransferenciaDetalles td
        ON td.TransferenciaId = t.Id
    LEFT JOIN (
        SELECT
            ts.TransferenciaId,
            SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
            KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
        FROM dbo.TransferenciaSurtido ts
        GROUP BY
            ts.TransferenciaId,
            UPPER(LTRIM(RTRIM(ts.Sku)))
    ) tsa
        ON tsa.TransferenciaId = t.Id
       AND tsa.SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
    WHERE t.FechaSolicitud IS NOT NULL
      AND TRY_CONVERT(DATE, t.FechaSolicitud) >= @FechaCorte
      AND t.Estatus BETWEEN 1 AND 4
    GROUP BY
        UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
),
PedidosFuturo AS (
    SELECT
        SKU,
        Pedidos = SUM(KgPendiente)
    FROM (
        SELECT SKU, KgPendiente FROM PedidosOV
        UNION ALL
        SELECT SKU, KgPendiente FROM PedidosTR
    ) x
    GROUP BY SKU
),
Competencia AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(pc.Sku))),
        Denes = MAX(CAST(ISNULL(pc.Denes, 0) AS DECIMAL(18,4))),
        Tc = MAX(CAST(ISNULL(pc.Tc, 0) AS DECIMAL(18,4))),
        Freasa = MAX(CAST(ISNULL(pc.Freasa, 0) AS DECIMAL(18,4))),
        Comentarios = MAX(ISNULL(pc.Comentarios, ''''))
    FROM dbo.PrecioCompetenciaSemana pc
    WHERE (@pFechaCorte IS NULL OR pc.FechaCorte = @pFechaCorte)
    GROUP BY UPPER(LTRIM(RTRIM(pc.Sku)))
),
Base AS (
    SELECT
        SKU = COALESCE(p.SKU, inv.SKU, pa.SKU, s.SKU, c.SKU, pf.SKU),
        ProductoNombre = ISNULL(p.ProductoNombre, ''''),
        U_MASTER = ISNULL(p.U_MASTER, ''''),
        ClasificacionNombre = ISNULL(p.ClasificacionNombre, ''POR DEFINIR''),
        InventarioAnterior = ISNULL(inv.InventarioAnterior, 0),
        InventarioActual = ISNULL(inv.InventarioActual, 0),
        PrecioLista = ISNULL(pa.PrecioLista, 0),
        KgSemana7 = ISNULL(s.KgSemana7, 0),
        KgSemana14 = ISNULL(s.KgSemana14, 0),
        KgSemana15 = ISNULL(s.KgSemana15, 0),
        Pedidos = ISNULL(pf.Pedidos, 0),
        Denes = ISNULL(c.Denes, 0),
        Tc = ISNULL(c.Tc, 0),
        Freasa = ISNULL(c.Freasa, 0),
        Comentarios = ISNULL(c.Comentarios, '''')
    FROM Productos p
    FULL OUTER JOIN InventarioFechas inv
        ON p.SKU = inv.SKU
    FULL OUTER JOIN PrecioActual pa
        ON COALESCE(p.SKU, inv.SKU) = pa.SKU
    FULL OUTER JOIN Semanas s
        ON COALESCE(p.SKU, inv.SKU, pa.SKU) = s.SKU
    FULL OUTER JOIN Competencia c
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, s.SKU) = c.SKU
    FULL OUTER JOIN PedidosFuturo pf
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, s.SKU, c.SKU) = pf.SKU
),
Calc AS (
    SELECT
        [CLASIFICACION] = ClasificacionNombre,
        [master] = CASE WHEN NULLIF(U_MASTER, '''') IS NOT NULL THEN U_MASTER ELSE ''GENERAL'' END,
        [SKU] = SKU,
        [SKU Prod] = ProductoNombre,
        [Inv. Inicial Refer.] = CAST(
            InventarioActual + KgSemana7 + KgSemana14 + KgSemana15 + Pedidos
        AS DECIMAL(18,2)),
        InvAnteriorValor = CAST(InventarioAnterior AS DECIMAL(18,2)),
        InvActualValor = CAST(InventarioActual AS DECIMAL(18,2)),
        [Inventario Ideal] = CAST(
            CASE
                WHEN (KgSemana7 + KgSemana14 + KgSemana15) > 0
                    THEN ((KgSemana7 + KgSemana14 + KgSemana15) / 23.0) * 14.0
                ELSE 0
            END
        AS DECIMAL(18,2)),
        [PEDIDOS] = CAST(Pedidos AS DECIMAL(18,2)),
        KgNetosVenta = CAST(
            KgSemana7 + KgSemana14 + KgSemana15
        AS DECIMAL(18,2)),
        [DÍAS DE INVENTARIO] = CAST(
            CASE
                WHEN ((KgSemana7 + KgSemana14 + KgSemana15) / 23.0) > 0
                    THEN InventarioActual / ((KgSemana7 + KgSemana14 + KgSemana15) / 23.0)
                ELSE 0
            END
        AS DECIMAL(18,4)),
        [Semana 7] = CAST(KgSemana7 AS DECIMAL(18,2)),
        [Semana 14] = CAST(KgSemana14 AS DECIMAL(18,2)),
        [Semana 15] = CAST(KgSemana15 AS DECIMAL(18,2)),
        [pp-venta real] = CAST(PrecioLista AS DECIMAL(18,2)),
        [DENES] = CAST(Denes AS DECIMAL(18,2)),
        [TC] = CAST(Tc AS DECIMAL(18,2)),
        [FREASA] = CAST(Freasa AS DECIMAL(18,2)),
        [PROM] = CAST(
            CASE
                WHEN
                    (CASE WHEN Denes > 0 THEN 1 ELSE 0 END +
                     CASE WHEN Tc > 0 THEN 1 ELSE 0 END +
                     CASE WHEN Freasa > 0 THEN 1 ELSE 0 END) > 0
                THEN
                    (ISNULL(NULLIF(Denes,0),0) + ISNULL(NULLIF(Tc,0),0) + ISNULL(NULLIF(Freasa,0),0)) /
                    NULLIF(
                        (CASE WHEN Denes > 0 THEN 1 ELSE 0 END +
                         CASE WHEN Tc > 0 THEN 1 ELSE 0 END +
                         CASE WHEN Freasa > 0 THEN 1 ELSE 0 END), 0
                    )
                ELSE 0
            END
        AS DECIMAL(18,2)),
        [COMENTARIOS] = Comentarios
    FROM Base
)
SELECT
    [CLASIFICACION],
    [master],
    [SKU],
    [SKU Prod],
    [Inv. Inicial Refer.],
    InvAnteriorValor AS ' + QUOTENAME(@ColInvAnterior) + N',
    InvActualValor   AS ' + QUOTENAME(@ColInvActual) + N',
    [Inventario Ideal],
    [PEDIDOS],
    KgNetosVenta AS [Kg Netos venta],
    [DÍAS DE INVENTARIO],
    [Semana 7]  AS [Semana1Valor],
    [Semana 14] AS [Semana2Valor],
    [Semana 15] AS [Semana3Valor],
    [pp-venta real],
    [DIF INV] = CAST(InvActualValor - [Inventario Ideal] AS DECIMAL(18,2)),
    [DENES],
    [TC],
    [FREASA],
    [PROM],
    [DIF PRECIO VS COMP] = CAST(
        CASE
            WHEN [PROM] > 0 THEN ([pp-venta real] / [PROM]) - 1
            ELSE 0
        END
    AS DECIMAL(6,4)),
    [RECOMENDACIÓN] =
        CASE
            WHEN [PEDIDOS] >= InvActualValor THEN ''SUBIR''
            WHEN [PEDIDOS] > (InvActualValor * 0.8) THEN ''MANTENER''
            WHEN [DÍAS DE INVENTARIO] > 30
              OR InvActualValor > ([Inventario Ideal] * 2) THEN
                CASE
                    WHEN [PEDIDOS] < (InvActualValor * 0.1)
                      OR KgNetosVenta < (InvActualValor * 0.3) THEN ''BAJAR''
                    ELSE ''MANTENER''
                END
            WHEN (
                CASE
                    WHEN [PROM] > 0 THEN ([pp-venta real] / [PROM]) - 1
                    ELSE 0
                END
            ) > 0.01 THEN ''BAJAR''
            ELSE ''MANTENER''
        END,
    [COMENTARIOS]
FROM Calc
WHERE (@pSku IS NULL OR @pSku = '''' OR [SKU] LIKE ''%'' + @pSku + ''%'')
  AND (@pProducto IS NULL OR @pProducto = '''' OR [SKU Prod] LIKE ''%'' + @pProducto + ''%'')
  AND (@pClasificacion IS NULL OR @pClasificacion = '''' OR [CLASIFICACION] = @pClasificacion)
ORDER BY [master], [SKU];';

EXEC sp_executesql
    @sql,
    N'@pFechaCorte DATE,
      @FechaCorte DATE,
      @FechaInvActual DATE,
      @FechaInvAnterior DATE,
      @DesdeSemana7 DATE,
      @DesdeSemana14 DATE,
      @HastaSemana14 DATE,
      @DesdeSemana15 DATE,
      @HastaSemana15 DATE,
      @pSku VARCHAR(50),
      @pProducto VARCHAR(200),
      @pClasificacion VARCHAR(50)',
    @pFechaCorte = @pFechaCorte,
    @FechaCorte = @FechaCorte,
    @FechaInvActual = @FechaInvActual,
    @FechaInvAnterior = @FechaInvAnterior,
    @DesdeSemana7 = @DesdeSemana7,
    @DesdeSemana14 = @DesdeSemana14,
    @HastaSemana14 = @HastaSemana14,
    @DesdeSemana15 = @DesdeSemana15,
    @HastaSemana15 = @HastaSemana15,
    @pSku = @pSku,
    @pProducto = @pProducto,
    @pClasificacion = @pClasificacion;


    ------OPCION DE PRECIOS EN LA SEMANA ---------

DECLARE @pFechaCorte DATE = '2026-04-13';
DECLARE @FechaCorte DATE = @pFechaCorte;

DECLARE @pSku VARCHAR(50) = NULL;
DECLARE @pProducto VARCHAR(200) = NULL;
DECLARE @pClasificacion VARCHAR(50) = NULL;

-- Inventario lunes a lunes
DECLARE @FechaInvActual DATE   = @FechaCorte;
DECLARE @FechaInvAnterior DATE = DATEADD(DAY, -7, @FechaCorte);

-- Ventas
DECLARE @DesdeSemana7 DATE  = DATEADD(DAY, -6, @FechaCorte);
DECLARE @DesdeSemana14 DATE = DATEADD(DAY, -13, @FechaCorte);
DECLARE @HastaSemana14 DATE = DATEADD(DAY, -7, @FechaCorte);
DECLARE @DesdeSemana15 DATE = DATEADD(DAY, -20, @FechaCorte);
DECLARE @HastaSemana15 DATE = DATEADD(DAY, -14, @FechaCorte);

DECLARE @ColInvAnterior SYSNAME = N'inventario ' + CONVERT(VARCHAR(10), @FechaInvAnterior, 103);
DECLARE @ColInvActual   SYSNAME = N'inventario ' + CONVERT(VARCHAR(10), @FechaInvActual, 103);

DECLARE @sql NVARCHAR(MAX) = N'
WITH Productos AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
        ProductoNombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), ''''), a.ProductoCodigo),
        U_MASTER = UPPER(LTRIM(RTRIM(ISNULL(a.U_MASTER, '''')))),
        ClasificacionNombre = ISNULL(cp.Nombre, ''POR DEFINIR'')
    FROM dbo.ArticuloSap a
    LEFT JOIN dbo.ClasificacionProduccion cp
        ON a.U_Clas_Prod = cp.ClasificacionId
),
InventarioFechas AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(iam.Sku))),
        InventarioAnterior = SUM(CASE WHEN iam.FechaInventario = @FechaInvAnterior THEN CAST(ISNULL(iam.PesoNeto, 0) AS DECIMAL(18,4)) ELSE 0 END),
        InventarioActual   = SUM(CASE WHEN iam.FechaInventario = @FechaInvActual   THEN CAST(ISNULL(iam.PesoNeto, 0) AS DECIMAL(18,4)) ELSE 0 END)
    FROM dbo.InventarioAlmacenado_Meat iam
    WHERE ISNULL(iam.Sku, '''') <> ''''
      AND iam.FechaInventario IN (@FechaInvAnterior, @FechaInvActual)
    GROUP BY UPPER(LTRIM(RTRIM(iam.Sku)))
),
PrecioActual AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(c.ProductoCodigo))),
        PrecioLista = MAX(CAST(ISNULL(c.Precio, 0) AS DECIMAL(18,4)))
    FROM dbo.CatalogoPrecioSap c
    WHERE ISNULL(c.ProductoCodigo, '''') <> ''''
    GROUP BY UPPER(LTRIM(RTRIM(c.ProductoCodigo)))
),

/* =========================
   KG REALES VENDIDOS
   ========================= */
VentaOV AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
        FechaMov = CAST(se.FechaValidacion AS DATE),
        Kg = SUM(CAST(ISNULL(sd.Kg, 0) AS DECIMAL(18,4)))
    FROM dbo.SurtidoEncabezado se
    INNER JOIN dbo.SurtidoDetalle sd
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    WHERE se.FechaValidacion IS NOT NULL
    GROUP BY
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        CAST(se.FechaValidacion AS DATE)
),
VentaTR AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
        FechaMov = CAST(t.FechaSolicitud AS DATE),
        Kg = SUM(CAST(ISNULL(ts.KgSurtido, 0) AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    INNER JOIN dbo.Transferencias t
        ON t.Id = ts.TransferenciaId
    WHERE t.FechaSolicitud IS NOT NULL
      AND ts.KgSurtido > 0
    GROUP BY
        UPPER(LTRIM(RTRIM(ts.Sku))),
        CAST(t.FechaSolicitud AS DATE)
),
VentaReal AS (
    SELECT SKU, FechaMov, Kg FROM VentaOV
    UNION ALL
    SELECT SKU, FechaMov, Kg FROM VentaTR
),
SemanasKg AS (
    SELECT
        vr.SKU,
        KgSemana7  = SUM(CASE WHEN vr.FechaMov >= @DesdeSemana7  AND vr.FechaMov <= @FechaCorte    THEN vr.Kg ELSE 0 END),
        KgSemana14 = SUM(CASE WHEN vr.FechaMov >= @DesdeSemana14 AND vr.FechaMov <= @HastaSemana14 THEN vr.Kg ELSE 0 END),
        KgSemana15 = SUM(CASE WHEN vr.FechaMov >= @DesdeSemana15 AND vr.FechaMov <= @HastaSemana15 THEN vr.Kg ELSE 0 END)
    FROM VentaReal vr
    GROUP BY vr.SKU
),

/* =========================
   PRECIOS REALES DE VENTA
   Semana 7 / 14 / 15 ya no son kg
   sino precios de venta por SKU
   ========================= */
VentasPrecioOV AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        FechaVenta = CAST(o.FechaEntrega AS DATE),
        PrecioVenta = CAST(ISNULL(op.Precio, 0) AS DECIMAL(18,4))
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.OrdenVentaProducto op
        ON op.PedidoId = o.Id
    WHERE ISNULL(op.ProductoCodigo, '''') <> ''''
      AND o.FechaEntrega IS NOT NULL
      AND ISNULL(op.Precio, 0) > 0
),
SemanasPrecio AS (
    SELECT
        vp.SKU,
        Semana7  = CAST(AVG(CASE WHEN vp.FechaVenta >= @DesdeSemana7  AND vp.FechaVenta <= @FechaCorte    THEN vp.PrecioVenta END) AS DECIMAL(18,4)),
        Semana14 = CAST(AVG(CASE WHEN vp.FechaVenta >= @DesdeSemana14 AND vp.FechaVenta <= @HastaSemana14 THEN vp.PrecioVenta END) AS DECIMAL(18,4)),
        Semana15 = CAST(AVG(CASE WHEN vp.FechaVenta >= @DesdeSemana15 AND vp.FechaVenta <= @HastaSemana15 THEN vp.PrecioVenta END) AS DECIMAL(18,4))
    FROM VentasPrecioOV vp
    GROUP BY vp.SKU
),
UltimoPrecioVenta AS (
    SELECT
        q.SKU,
        q.PrecioVenta AS PpVentaReal
    FROM (
        SELECT
            vp.SKU,
            vp.PrecioVenta,
            vp.FechaVenta,
            rn = ROW_NUMBER() OVER (
                PARTITION BY vp.SKU
                ORDER BY vp.FechaVenta DESC, vp.PrecioVenta DESC
            )
        FROM VentasPrecioOV vp
        WHERE vp.FechaVenta <= @FechaCorte
    ) q
    WHERE q.rn = 1
),

PedidosOV AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        KgPendiente = SUM(
            CAST(
                CASE
                    WHEN o.Estatus = 5 AND os.Id IS NOT NULL THEN 0
                    ELSE CASE
                        WHEN (CAST(op.Peso AS DECIMAL(18,4)) - ISNULL(sa.KgSurtido, 0)) < 0 THEN 0
                        ELSE (CAST(op.Peso AS DECIMAL(18,4)) - ISNULL(sa.KgSurtido, 0))
                    END
                END
            AS DECIMAL(18,4))
        )
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.Series ser
        ON o.Serie = ser.NombreSerie
    INNER JOIN dbo.OrdenVentaProducto op
        ON op.PedidoId = o.Id
    LEFT JOIN (
        SELECT DISTINCT o2.Id
        FROM dbo.OrdenVenta o2
        JOIN dbo.Subpedido sp2
            ON sp2.OrdenVentaId = o2.Id
        JOIN dbo.SurtidoEncabezado se2
            ON se2.SolicitudSurtidoId = sp2.U_DocMeat
    ) os
        ON os.Id = o.Id
    LEFT JOIN (
        SELECT
            PedidoId = o3.Id,
            SKU = UPPER(LTRIM(RTRIM(sd3.Articulo))),
            KgSurtido = SUM(CAST(sd3.Kg AS DECIMAL(18,4)))
        FROM dbo.OrdenVenta o3
        JOIN dbo.Subpedido sp3
            ON sp3.OrdenVentaId = o3.Id
        JOIN dbo.SurtidoEncabezado se3
            ON se3.SolicitudSurtidoId = sp3.U_DocMeat
        JOIN dbo.SurtidoDetalle sd3
            ON sd3.SolicitudSurtidoId = se3.SolicitudSurtidoId
        WHERE se3.FechaValidacion IS NOT NULL
        GROUP BY
            o3.Id,
            UPPER(LTRIM(RTRIM(sd3.Articulo)))
    ) sa
        ON sa.PedidoId = o.Id
       AND sa.SKU = UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
    WHERE o.FechaEntrega IS NOT NULL
      AND TRY_CONVERT(DATE, o.FechaEntrega) >= @FechaCorte
      AND o.Estatus BETWEEN 1 AND 5
      AND ser.Sucursal = ''MATRIZ''
    GROUP BY
        UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
),
PedidosTR AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
        KgPendiente = SUM(
            CAST(
                CASE
                    WHEN (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0)) < 0 THEN 0
                    ELSE (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0))
                END
            AS DECIMAL(18,4))
        )
    FROM dbo.Transferencias t
    JOIN dbo.TransferenciaDetalles td
        ON td.TransferenciaId = t.Id
    LEFT JOIN (
        SELECT
            ts.TransferenciaId,
            SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
            KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
        FROM dbo.TransferenciaSurtido ts
        GROUP BY
            ts.TransferenciaId,
            UPPER(LTRIM(RTRIM(ts.Sku)))
    ) tsa
        ON tsa.TransferenciaId = t.Id
       AND tsa.SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
    WHERE t.FechaSolicitud IS NOT NULL
      AND TRY_CONVERT(DATE, t.FechaSolicitud) >= @FechaCorte
      AND t.Estatus BETWEEN 1 AND 4
    GROUP BY
        UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
),
PedidosFuturo AS (
    SELECT
        SKU,
        Pedidos = SUM(KgPendiente)
    FROM (
        SELECT SKU, KgPendiente FROM PedidosOV
        UNION ALL
        SELECT SKU, KgPendiente FROM PedidosTR
    ) x
    GROUP BY SKU
),
Competencia AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(pc.Sku))),
        Denes = MAX(CAST(ISNULL(pc.Denes, 0) AS DECIMAL(18,4))),
        Tc = MAX(CAST(ISNULL(pc.Tc, 0) AS DECIMAL(18,4))),
        Freasa = MAX(CAST(ISNULL(pc.Freasa, 0) AS DECIMAL(18,4))),
        Comentarios = MAX(ISNULL(pc.Comentarios, ''''))
    FROM dbo.PrecioCompetenciaSemana pc
    WHERE (@pFechaCorte IS NULL OR pc.FechaCorte = @pFechaCorte)
    GROUP BY UPPER(LTRIM(RTRIM(pc.Sku)))
),
Base AS (
    SELECT
        SKU = COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU, sp.SKU, upv.SKU, c.SKU, pf.SKU),
        ProductoNombre = ISNULL(p.ProductoNombre, ''''),
        U_MASTER = ISNULL(p.U_MASTER, ''''),
        ClasificacionNombre = ISNULL(p.ClasificacionNombre, ''POR DEFINIR''),
        InventarioAnterior = ISNULL(inv.InventarioAnterior, 0),
        InventarioActual = ISNULL(inv.InventarioActual, 0),
        PrecioLista = ISNULL(pa.PrecioLista, 0),
        KgSemana7 = ISNULL(sk.KgSemana7, 0),
        KgSemana14 = ISNULL(sk.KgSemana14, 0),
        KgSemana15 = ISNULL(sk.KgSemana15, 0),
        PrecioSemana7 = ISNULL(sp.Semana7, 0),
        PrecioSemana14 = ISNULL(sp.Semana14, 0),
        PrecioSemana15 = ISNULL(sp.Semana15, 0),
        PpVentaReal = ISNULL(upv.PpVentaReal, 0),
        Pedidos = ISNULL(pf.Pedidos, 0),
        Denes = ISNULL(c.Denes, 0),
        Tc = ISNULL(c.Tc, 0),
        Freasa = ISNULL(c.Freasa, 0),
        Comentarios = ISNULL(c.Comentarios, '''')
    FROM Productos p
    FULL OUTER JOIN InventarioFechas inv
        ON p.SKU = inv.SKU
    FULL OUTER JOIN PrecioActual pa
        ON COALESCE(p.SKU, inv.SKU) = pa.SKU
    FULL OUTER JOIN SemanasKg sk
        ON COALESCE(p.SKU, inv.SKU, pa.SKU) = sk.SKU
    FULL OUTER JOIN SemanasPrecio sp
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU) = sp.SKU
    FULL OUTER JOIN UltimoPrecioVenta upv
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU, sp.SKU) = upv.SKU
    FULL OUTER JOIN Competencia c
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU, sp.SKU, upv.SKU) = c.SKU
    FULL OUTER JOIN PedidosFuturo pf
        ON COALESCE(p.SKU, inv.SKU, pa.SKU, sk.SKU, sp.SKU, upv.SKU, c.SKU) = pf.SKU
),
Calc AS (
    SELECT
        [CLASIFICACION] = ClasificacionNombre,
        [master] = CASE WHEN NULLIF(U_MASTER, '''') IS NOT NULL THEN U_MASTER ELSE ''GENERAL'' END,
        [SKU] = SKU,
        [SKU Prod] = ProductoNombre,
        [Inv. Inicial Refer.] = CAST(
            InventarioActual + KgSemana7 + KgSemana14 + KgSemana15 + Pedidos
        AS DECIMAL(18,2)),
        InvAnteriorValor = CAST(InventarioAnterior AS DECIMAL(18,2)),
        InvActualValor = CAST(InventarioActual AS DECIMAL(18,2)),
        [Inventario Ideal] = CAST(
            CASE
                WHEN (KgSemana7 + KgSemana14 + KgSemana15) > 0
                    THEN ((KgSemana7 + KgSemana14 + KgSemana15) / 23.0) * 14.0
                ELSE 0
            END
        AS DECIMAL(18,2)),
        [PEDIDOS] = CAST(Pedidos AS DECIMAL(18,2)),
        KgNetosVenta = CAST(
            KgSemana7 + KgSemana14 + KgSemana15
        AS DECIMAL(18,2)),
        [DÍAS DE INVENTARIO] = CAST(
            CASE
                WHEN ((KgSemana7 + KgSemana14 + KgSemana15) / 23.0) > 0
                    THEN InventarioActual / ((KgSemana7 + KgSemana14 + KgSemana15) / 23.0)
                ELSE 0
            END
        AS DECIMAL(18,4)),

        /* AQUÍ YA VAN PRECIOS, NO KG */
        [Semana 7] = CAST(PrecioSemana7 AS DECIMAL(18,2)),
        [Semana 14] = CAST(PrecioSemana14 AS DECIMAL(18,2)),
        [Semana 15] = CAST(PrecioSemana15 AS DECIMAL(18,2)),

        [pp-venta real] = CAST(
            CASE
                WHEN PpVentaReal > 0 THEN PpVentaReal
                ELSE PrecioLista
            END
        AS DECIMAL(18,2)),

        [DENES] = CAST(Denes AS DECIMAL(18,2)),
        [TC] = CAST(Tc AS DECIMAL(18,2)),
        [FREASA] = CAST(Freasa AS DECIMAL(18,2)),
        [PROM] = CAST(
            CASE
                WHEN
                    (CASE WHEN Denes > 0 THEN 1 ELSE 0 END +
                     CASE WHEN Tc > 0 THEN 1 ELSE 0 END +
                     CASE WHEN Freasa > 0 THEN 1 ELSE 0 END) > 0
                THEN
                    (ISNULL(NULLIF(Denes,0),0) + ISNULL(NULLIF(Tc,0),0) + ISNULL(NULLIF(Freasa,0),0)) /
                    NULLIF(
                        (CASE WHEN Denes > 0 THEN 1 ELSE 0 END +
                         CASE WHEN Tc > 0 THEN 1 ELSE 0 END +
                         CASE WHEN Freasa > 0 THEN 1 ELSE 0 END), 0
                    )
                ELSE 0
            END
        AS DECIMAL(18,2)),
        [COMENTARIOS] = Comentarios
    FROM Base
)
SELECT
    [CLASIFICACION],
    [master],
    [SKU],
    [SKU Prod],
    [Inv. Inicial Refer.],
    InvAnteriorValor AS ' + QUOTENAME(@ColInvAnterior) + N',
    InvActualValor   AS ' + QUOTENAME(@ColInvActual) + N',
    [Inventario Ideal],
    [PEDIDOS],
    KgNetosVenta AS [Kg Netos venta],
    [DÍAS DE INVENTARIO],
    [Semana 7]  AS [Semana1Valor],
    [Semana 14] AS [Semana2Valor],
    [Semana 15] AS [Semana3Valor],
    [pp-venta real],
    [DIF INV] = CAST(InvActualValor - [Inventario Ideal] AS DECIMAL(18,2)),
    [DENES],
    [TC],
    [FREASA],
    [PROM],
    [DIF PRECIO VS COMP] = CAST(
        CASE
            WHEN [PROM] > 0 THEN ([pp-venta real] / [PROM]) - 1
            ELSE 0
        END
    AS DECIMAL(6,4)),
    [RECOMENDACIÓN] =
        CASE
            WHEN [PEDIDOS] >= InvActualValor THEN ''SUBIR''
            WHEN [PEDIDOS] > (InvActualValor * 0.8) THEN ''MANTENER''
            WHEN [DÍAS DE INVENTARIO] > 30
              OR InvActualValor > ([Inventario Ideal] * 2) THEN
                CASE
                    WHEN [PEDIDOS] < (InvActualValor * 0.1)
                      OR KgNetosVenta < (InvActualValor * 0.3) THEN ''BAJAR''
                    ELSE ''MANTENER''
                END
            WHEN (
                CASE
                    WHEN [PROM] > 0 THEN ([pp-venta real] / [PROM]) - 1
                    ELSE 0
                END
            ) > 0.01 THEN ''BAJAR''
            ELSE ''MANTENER''
        END,
    [COMENTARIOS]
FROM Calc
WHERE (@pSku IS NULL OR @pSku = '''' OR [SKU] LIKE ''%'' + @pSku + ''%'')
  AND (@pProducto IS NULL OR @pProducto = '''' OR [SKU Prod] LIKE ''%'' + @pProducto + ''%'')
  AND (@pClasificacion IS NULL OR @pClasificacion = '''' OR [CLASIFICACION] = @pClasificacion)
ORDER BY [master], [SKU];';

EXEC sp_executesql
    @sql,
    N'@pFechaCorte DATE,
      @FechaCorte DATE,
      @FechaInvActual DATE,
      @FechaInvAnterior DATE,
      @DesdeSemana7 DATE,
      @DesdeSemana14 DATE,
      @HastaSemana14 DATE,
      @DesdeSemana15 DATE,
      @HastaSemana15 DATE,
      @pSku VARCHAR(50),
      @pProducto VARCHAR(200),
      @pClasificacion VARCHAR(50)',
    @pFechaCorte = @pFechaCorte,
    @FechaCorte = @FechaCorte,
    @FechaInvActual = @FechaInvActual,
    @FechaInvAnterior = @FechaInvAnterior,
    @DesdeSemana7 = @DesdeSemana7,
    @DesdeSemana14 = @DesdeSemana14,
    @HastaSemana14 = @HastaSemana14,
    @DesdeSemana15 = @DesdeSemana15,
    @HastaSemana15 = @HastaSemana15,
    @pSku = @pSku,
    @pProducto = @pProducto,
    @pClasificacion = @pClasificacion;




    CREATE TABLE dbo.CompetidorPrecio
(
    CompetidorId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Nombre NVARCHAR(150) NOT NULL,
    Activo BIT NOT NULL CONSTRAINT DF_CompetidorPrecio_Activo DEFAULT(1),
    FechaRegistro DATETIME2 NOT NULL CONSTRAINT DF_CompetidorPrecio_FechaRegistro DEFAULT(SYSDATETIME())
);

CREATE UNIQUE INDEX UX_CompetidorPrecio_Nombre
ON dbo.CompetidorPrecio(Nombre);


CREATE TABLE dbo.PrecioCompetenciaDetalle
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    FechaRegistro DATETIME2 NOT NULL CONSTRAINT DF_PrecioCompetenciaDetalle_FechaRegistro DEFAULT(SYSDATETIME()),
    FechaCorte DATE NOT NULL,

    CompetidorId INT NOT NULL,
    SkuPropio NVARCHAR(50) NOT NULL,

    CodigoCompetencia NVARCHAR(80) NULL,
    NombreCompetencia NVARCHAR(300) NOT NULL,

    PrecioCompetencia DECIMAL(18,4) NOT NULL,
    NuestroPrecio DECIMAL(18,4) NULL,

    UsuarioRegistro NVARCHAR(150) NULL,

    CONSTRAINT FK_PrecioCompetenciaDetalle_Competidor
        FOREIGN KEY (CompetidorId)
        REFERENCES dbo.CompetidorPrecio(CompetidorId)
);

CREATE INDEX IX_PrecioCompetenciaDetalle_FechaSku
ON dbo.PrecioCompetenciaDetalle(FechaCorte, SkuPropio);

CREATE INDEX IX_PrecioCompetenciaDetalle_CompetidorFecha
ON dbo.PrecioCompetenciaDetalle(CompetidorId, FechaCorte);



--TVP_ significa Table-Valued Parameter, en español: parámetro tipo tabla.

--En SQL Server se usa para mandar muchos registros al mismo tiempo desde C# hacia un stored procedure.

--En tu caso, en lugar de mandar SKU por SKU, mandas una tabla completa con varios SKUs y precios.


CREATE TYPE dbo.TVP_PrecioPublicoSku AS TABLE (
    ProductoCodigo NVARCHAR(50) NOT NULL,
    Precio DECIMAL(18,4) NOT NULL,
    Master NVARCHAR(100) NULL,
    ProductoNombre NVARCHAR(250) NULL,
    Clasificacion NVARCHAR(100) NULL
);
GO


CREATE TABLE dbo.CatalogoPrecioSapLote (
    LoteId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    PriceListNum INT NOT NULL,
    PriceListName NVARCHAR(100) NOT NULL,
    FechaCorte DATE NOT NULL,
    FechaUso DATE NOT NULL,
    AlcanceClientes VARCHAR(30) NOT NULL,
    Canal NVARCHAR(50) NULL,
    FechaGuardado DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    Usuario NVARCHAR(150) NULL,
    TotalSkus INT NOT NULL DEFAULT 0,
    TotalClientes INT NOT NULL DEFAULT 0,
    TotalRegistros INT NOT NULL DEFAULT 0,
    RegistrosInsertados INT NOT NULL DEFAULT 0,
    RegistrosActualizados INT NOT NULL DEFAULT 0,
    RegistrosSinCambio INT NOT NULL DEFAULT 0,
    Estatus VARCHAR(20) NOT NULL DEFAULT 'GUARDADO'
);
GO

CREATE TABLE dbo.CatalogoPrecioSapHistorico (
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    LoteId UNIQUEIDENTIFIER NOT NULL,
    ProductoCodigo NVARCHAR(50) NOT NULL,
    Cliente NVARCHAR(50) NOT NULL,
    PriceListNum INT NOT NULL,
    PriceListName NVARCHAR(100) NOT NULL,
    PrecioAnterior DECIMAL(18,4) NULL,
    PrecioNuevo DECIMAL(18,4) NOT NULL,
    FechaPrecioAnterior DATETIME2 NULL,
    FechaGuardado DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    FechaCorte DATE NOT NULL,
    FechaUso DATE NOT NULL,
    Usuario NVARCHAR(150) NULL,
    Accion VARCHAR(20) NOT NULL,
    Master NVARCHAR(100) NULL,
    ProductoNombre NVARCHAR(250) NULL,
    Clasificacion NVARCHAR(100) NULL
);
GO

IF COL_LENGTH('dbo.CatalogoPrecioSapHistorico', 'Comentarios') IS NULL
BEGIN
    ALTER TABLE dbo.CatalogoPrecioSapHistorico
    ADD Comentarios NVARCHAR(500) NULL;
END;
GO

CREATE UNIQUE INDEX UX_CatalogoPrecioSap_Producto_Cliente_Lista
ON dbo.CatalogoPrecioSap (ProductoCodigo, Cliente, PriceListNum);
GO


---SP

USE [SIGO]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[sp_GuardarPrecioPublicoDesdeAnalisis]
    @PriceListNum INT = 1,
    @FechaCorte DATE,
    @FechaUso DATE,
    @Usuario NVARCHAR(150) = NULL,
    @AlcanceClientes VARCHAR(30) = 'LISTA_CLIENTE',
    @Canal NVARCHAR(50) = NULL,
    @Skus dbo.TVP_PrecioPublicoSku READONLY,
    @Clientes dbo.TVP_ClientePrecio READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LoteId UNIQUEIDENTIFIER = NEWID();
    DECLARE @PriceListName NVARCHAR(100);

    SELECT @PriceListName = PriceListName
    FROM dbo.ListaPreciosSap
    WHERE PriceListNum = @PriceListNum
      AND Activo = 1;

    IF @PriceListName IS NULL
    BEGIN
        RAISERROR('La lista de precios no existe o no está activa.', 16, 1);
        RETURN;
    END;

    IF NOT EXISTS (SELECT 1 FROM @Skus)
    BEGIN
        RAISERROR('No se recibieron SKUs para guardar.', 16, 1);
        RETURN;
    END;

    DECLARE @Target TABLE (
        ProductoCodigo NVARCHAR(50) NOT NULL,
        Cliente NVARCHAR(50) NOT NULL,
        PriceListNum INT NOT NULL,
        PriceListName NVARCHAR(100) NOT NULL,
        PrecioNuevo DECIMAL(18,4) NOT NULL,
        Master NVARCHAR(100) NULL,
        ProductoNombre NVARCHAR(250) NULL,
        Clasificacion NVARCHAR(100) NULL
    );

    /*
        1. Guarda la lista seleccionada.
        Ejemplo:
        Si @PriceListNum = 1, sólo afecta clientes con ClienteSap.PriceListNum = 1.
    */
    ;WITH SkusLimpios AS (
        SELECT
            ProductoCodigo = UPPER(LTRIM(RTRIM(ProductoCodigo))),
            Precio,
            Master,
            ProductoNombre,
            Clasificacion,
            rn = ROW_NUMBER() OVER (
                PARTITION BY UPPER(LTRIM(RTRIM(ProductoCodigo)))
                ORDER BY UPPER(LTRIM(RTRIM(ProductoCodigo)))
            )
        FROM @Skus
        WHERE Precio > 0
          AND ISNULL(LTRIM(RTRIM(ProductoCodigo)), '') <> ''
    )
    INSERT INTO @Target (
        ProductoCodigo,
        Cliente,
        PriceListNum,
        PriceListName,
        PrecioNuevo,
        Master,
        ProductoNombre,
        Clasificacion
    )
    SELECT
        s.ProductoCodigo,
        c.Cliente,
        @PriceListNum,
        @PriceListName,
        s.Precio,
        s.Master,
        s.ProductoNombre,
        s.Clasificacion
    FROM SkusLimpios s
    INNER JOIN dbo.ClienteSap c
        ON c.PriceListNum = @PriceListNum
       --AND c.U_MT_Clasificacion = 'ACTIVO'
    WHERE s.rn = 1;

    /*
        2. Si se guarda Precio Público, genera automáticamente
        las demás listas activas que tengan Factor diferente de 0.

        Ejemplo:
        Precio Público = 20
        LISTA 2.5 con Factor = 2.5
        Resultado = 22.5

        Pero sólo para clientes que tengan ClienteSap.PriceListNum = lista correspondiente.
    */
    IF @PriceListNum = 1
    BEGIN
        ;WITH SkusLimpios AS (
            SELECT
                ProductoCodigo = UPPER(LTRIM(RTRIM(ProductoCodigo))),
                Precio,
                Master,
                ProductoNombre,
                Clasificacion,
                rn = ROW_NUMBER() OVER (
                    PARTITION BY UPPER(LTRIM(RTRIM(ProductoCodigo)))
                    ORDER BY UPPER(LTRIM(RTRIM(ProductoCodigo)))
                )
            FROM @Skus
            WHERE Precio > 0
              AND ISNULL(LTRIM(RTRIM(ProductoCodigo)), '') <> ''
        )
        INSERT INTO @Target (
            ProductoCodigo,
            Cliente,
            PriceListNum,
            PriceListName,
            PrecioNuevo,
            Master,
            ProductoNombre,
            Clasificacion
        )
        SELECT
            s.ProductoCodigo,
            c.Cliente,
            lp.PriceListNum,
            lp.PriceListName,
            CAST(s.Precio + ISNULL(lp.Factor, 0) AS DECIMAL(18,4)) AS PrecioNuevo,
            s.Master,
            s.ProductoNombre,
            s.Clasificacion
        FROM SkusLimpios s
        INNER JOIN dbo.ListaPreciosSap lp
            ON lp.Activo = 1
           AND lp.PriceListNum <> 1
           AND ISNULL(lp.Factor, 0) <> 0
        INNER JOIN dbo.ClienteSap c
            ON c.PriceListNum = lp.PriceListNum
           --AND c.U_MT_Clasificacion = 'ACTIVO'
        WHERE s.rn = 1;
    END;

    IF NOT EXISTS (SELECT 1 FROM @Target)
    BEGIN
        RAISERROR('No hay clientes activos con esta lista de precios asignada.', 16, 1);
        RETURN;
    END;

    DECLARE @Snapshot TABLE (
        ProductoCodigo NVARCHAR(50) NOT NULL,
        Cliente NVARCHAR(50) NOT NULL,
        PriceListNum INT NOT NULL,
        PriceListName NVARCHAR(100) NOT NULL,
        PrecioAnterior DECIMAL(18,4) NULL,
        PrecioNuevo DECIMAL(18,4) NOT NULL,
        FechaPrecioAnterior DATETIME2 NULL,
        Existe BIT NOT NULL,
        Master NVARCHAR(100) NULL,
        ProductoNombre NVARCHAR(250) NULL,
        Clasificacion NVARCHAR(100) NULL
    );

    INSERT INTO @Snapshot (
        ProductoCodigo,
        Cliente,
        PriceListNum,
        PriceListName,
        PrecioAnterior,
        PrecioNuevo,
        FechaPrecioAnterior,
        Existe,
        Master,
        ProductoNombre,
        Clasificacion
    )
    SELECT
        t.ProductoCodigo,
        t.Cliente,
        t.PriceListNum,
        t.PriceListName,
        cp.Precio AS PrecioAnterior,
        t.PrecioNuevo,
        cp.FechaModificacion AS FechaPrecioAnterior,
        CASE WHEN cp.Id IS NULL THEN 0 ELSE 1 END AS Existe,
        t.Master,
        t.ProductoNombre,
        t.Clasificacion
    FROM @Target t
    LEFT JOIN dbo.CatalogoPrecioSap cp
        ON cp.ProductoCodigo = t.ProductoCodigo
       AND cp.Cliente = t.Cliente
       AND cp.PriceListNum = t.PriceListNum;

    INSERT INTO dbo.CatalogoPrecioSap (
        ProductoCodigo,
        Cliente,
        PriceListNum,
        PriceListName,
        Precio,
        FechaModificacion
    )
    SELECT
        s.ProductoCodigo,
        s.Cliente,
        s.PriceListNum,
        s.PriceListName,
        s.PrecioNuevo,
        SYSDATETIME()
    FROM @Snapshot s
    WHERE s.Existe = 0;

    UPDATE cp
    SET
        cp.PriceListName = s.PriceListName,
        cp.Precio = s.PrecioNuevo,
        cp.FechaModificacion = SYSDATETIME()
    FROM dbo.CatalogoPrecioSap cp
    INNER JOIN @Snapshot s
        ON s.ProductoCodigo = cp.ProductoCodigo
       AND s.Cliente = cp.Cliente
       AND s.PriceListNum = cp.PriceListNum
    WHERE s.Existe = 1
      AND ISNULL(cp.Precio, -999999) <> ISNULL(s.PrecioNuevo, -999999);

    INSERT INTO dbo.CatalogoPrecioSapLote (
        LoteId,
        PriceListNum,
        PriceListName,
        FechaCorte,
        FechaUso,
        AlcanceClientes,
        Canal,
        Usuario,
        TotalSkus,
        TotalClientes,
        TotalRegistros,
        RegistrosInsertados,
        RegistrosActualizados,
        RegistrosSinCambio,
        Estatus
    )
    SELECT
        @LoteId,
        @PriceListNum,
        @PriceListName,
        @FechaCorte,
        @FechaUso,
        'LISTA_CLIENTE',
        NULL,
        @Usuario,
        (SELECT COUNT(DISTINCT ProductoCodigo) FROM @Target),
        (SELECT COUNT(DISTINCT Cliente) FROM @Target),
        (SELECT COUNT(*) FROM @Snapshot),
        ISNULL(SUM(CASE WHEN Existe = 0 THEN 1 ELSE 0 END), 0),
        ISNULL(SUM(CASE WHEN Existe = 1 AND ISNULL(PrecioAnterior, -999999) <> ISNULL(PrecioNuevo, -999999) THEN 1 ELSE 0 END), 0),
        ISNULL(SUM(CASE WHEN Existe = 1 AND ISNULL(PrecioAnterior, -999999) = ISNULL(PrecioNuevo, -999999) THEN 1 ELSE 0 END), 0),
        'GUARDADO'
    FROM @Snapshot;

    INSERT INTO dbo.CatalogoPrecioSapHistorico (
        LoteId,
        ProductoCodigo,
        Cliente,
        PriceListNum,
        PriceListName,
        PrecioAnterior,
        PrecioNuevo,
        FechaPrecioAnterior,
        FechaGuardado,
        FechaCorte,
        FechaUso,
        Usuario,
        Accion,
        Master,
        ProductoNombre,
        Clasificacion
    )
    SELECT
        @LoteId,
        ProductoCodigo,
        Cliente,
        PriceListNum,
        PriceListName,
        PrecioAnterior,
        PrecioNuevo,
        FechaPrecioAnterior,
        SYSDATETIME(),
        @FechaCorte,
        @FechaUso,
        @Usuario,
        CASE
            WHEN Existe = 0 THEN 'INSERT'
            WHEN Existe = 1 AND ISNULL(PrecioAnterior, -999999) <> ISNULL(PrecioNuevo, -999999) THEN 'UPDATE'
            ELSE 'SIN_CAMBIO'
        END,
        Master,
        ProductoNombre,
        Clasificacion
    FROM @Snapshot;

    SELECT
        @LoteId AS LoteId,
        @PriceListNum AS PriceListNum,
        @PriceListName AS PriceListName,
        @FechaCorte AS FechaCorte,
        @FechaUso AS FechaUso,
        ISNULL(SUM(CASE WHEN Existe = 0 THEN 1 ELSE 0 END), 0) AS Insertados,
        ISNULL(SUM(CASE WHEN Existe = 1 AND ISNULL(PrecioAnterior, -999999) <> ISNULL(PrecioNuevo, -999999) THEN 1 ELSE 0 END), 0) AS Actualizados,
        ISNULL(SUM(CASE WHEN Existe = 1 AND ISNULL(PrecioAnterior, -999999) = ISNULL(PrecioNuevo, -999999) THEN 1 ELSE 0 END), 0) AS SinCambio,
        COUNT(*) AS TotalHistorico
    FROM @Snapshot;
END;
GO

CREATE TYPE dbo.TVP_ClientePrecio AS TABLE (
    Cliente NVARCHAR(50) NOT NULL
);
GO
