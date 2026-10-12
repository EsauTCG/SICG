/*
VISTAS SQL - REPORTE PRESUPUESTOS MES
Objetivo: sustituir consultas largas del controller por vistas consumibles desde C# / Dapper / EF.

Notas:
1) Las vistas NO reciben parámetros. Filtra desde el SELECT del controller con WHERE MesConsulta, AnioConsulta, Canal, VendedorId, SKU, etc.
2) En las OV se conserva la regla correcta: Series.Sucursal = 'MATRIZ'.
3) Las transferencias se mantienen separadas y siguen apareciendo.
4) Se centraliza ClienteSap con AplicaPresupuesto = 1 en rpt.vw_ClientesPresupuesto.
5) Ejecutar en la base de datos donde existen las tablas dbo.OrdenVenta, dbo.Series, dbo.ClienteSap, etc.
*/

/* =========================================================
   0) CATÁLOGO CLIENTES PRESUPUESTABLES
   ========================================================= */

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'rpt')
BEGIN
    EXEC('CREATE SCHEMA rpt');
END
GO

CREATE OR ALTER VIEW rpt.vw_ClientesPresupuesto
AS
SELECT
    Cliente        = UPPER(LTRIM(RTRIM(cs.Cliente))),
    NombreCliente  = COALESCE(NULLIF(LTRIM(RTRIM(cs.NombreCliente)), ''), cs.Cliente),
    VendedorId     = cs.VendedorId,
    VendedorNombre = LTRIM(RTRIM(cs.VendedorNombre)),
    U_CANAL        = UPPER(LTRIM(RTRIM(cs.U_CANAL))),
    AplicaPresupuesto = ISNULL(cs.AplicaPresupuesto, 0)
FROM dbo.ClienteSap cs
WHERE ISNULL(cs.AplicaPresupuesto, 0) = 1;
GO

/* =========================================================
   0.1) CATÁLOGO PRODUCTOS
   ========================================================= */
CREATE OR ALTER VIEW rpt.vw_ProductosPresupuesto
AS
SELECT
    SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
    ProductoNombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), ''), a.ProductoCodigo),
    U_MASTER = UPPER(LTRIM(RTRIM(a.U_MASTER))),
    ClasificacionId = ISNULL(a.U_Clas_Prod, 99),
    ClasificacionNombre = ISNULL(cp.Nombre, 'POR DEFINIR')
FROM dbo.ArticuloSap a
LEFT JOIN dbo.ClasificacionProduccion cp
    ON a.U_Clas_Prod = cp.ClasificacionId;
GO

/* =========================================================
   1) REPORTE PRINCIPAL PRESUPUESTOS MES
   Consumo equivalente a /Comercial/ReportePresupuestosMES
   ========================================================= */
CREATE OR ALTER VIEW rpt.vw_ReportePresupuestosMes
AS
WITH
productos AS (
    SELECT * FROM rpt.vw_ProductosPresupuesto
),
clientes AS (
    SELECT * FROM rpt.vw_ClientesPresupuesto
),
vendedores AS (
    SELECT DISTINCT VendedorId, VendedorNombre
    FROM clientes
    WHERE VendedorId IS NOT NULL
),
canal_vendedores AS (
    SELECT DISTINCT
        Canal      = UPPER(LTRIM(RTRIM(c.U_CANAL))),
        VendedorId = c.VendedorId
    FROM clientes c
    WHERE c.VendedorId IS NOT NULL
      AND UPPER(LTRIM(RTRIM(c.U_CANAL))) LIKE 'CEDIS%'
),
plan_prod AS (
    SELECT
        SKU  = UPPER(LTRIM(RTRIM(pd.ProductoCodigo))),
        Mes  = pp.Mes,
        Anio = pp.Anio,
        PlanProduccion = SUM(CAST(pd.Peso AS DECIMAL(18,4)))
    FROM dbo.PlanDetalle pd WITH (NOLOCK)
    INNER JOIN dbo.PlanProduccion pp WITH (NOLOCK)
        ON pp.Id = pd.fk_Plan
    GROUP BY
        UPPER(LTRIM(RTRIM(pd.ProductoCodigo))),
        pp.Mes,
        pp.Anio
),
producido_real AS (
    SELECT
        SKU  = UPPER(LTRIM(RTRIM(p.ArticuloCodigo))),
        Mes  = MONTH(p.FechaProduccion),
        Anio = YEAR(p.FechaProduccion),
        Producido = SUM(CAST(p.KgProducidos AS DECIMAL(18,4)))
    FROM dbo.ProduccionSigo p WITH (NOLOCK)
    WHERE p.FechaProduccion IS NOT NULL
    GROUP BY
        UPPER(LTRIM(RTRIM(p.ArticuloCodigo))),
        MONTH(p.FechaProduccion),
        YEAR(p.FechaProduccion)
),
presupuestos_normales AS (
    SELECT
        Cliente = UPPER(LTRIM(RTRIM(p.ClienteId))),
        SKU     = UPPER(LTRIM(RTRIM(p.ProductoCodigo))),
        Mes     = p.Mes,
        Anio    = p.Año,
        Presupuesto = SUM(CAST(p.Presupuesto AS DECIMAL(18,4)))
    FROM dbo.Presupuestos p
    INNER JOIN clientes c
        ON c.Cliente = UPPER(LTRIM(RTRIM(p.ClienteId)))
    GROUP BY
        UPPER(LTRIM(RTRIM(p.ClienteId))),
        UPPER(LTRIM(RTRIM(p.ProductoCodigo))),
        p.Mes,
        p.Año
),
ov AS (
    SELECT
        o.Id,
        Cliente    = UPPER(LTRIM(RTRIM(o.Cliente))),
        o.VendedorId,
        o.Estatus,
        o.Serie,
        FechaDate = TRY_CONVERT(date, o.FechaEntrega)
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.Series ser
        ON o.Serie = ser.NombreSerie
    INNER JOIN clientes c
        ON c.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.FechaEntrega IS NOT NULL
      AND o.Estatus BETWEEN 1 AND 5
      AND UPPER(LTRIM(RTRIM(ISNULL(ser.Sucursal, '')))) = 'MATRIZ'
),
ov_con_surtido AS (
    SELECT DISTINCT
        o.Id
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp
        ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se
        ON se.SolicitudSurtidoId = sp.U_DocMeat
),
ov_peso_agg AS (
    SELECT
        PedidoId = op.PedidoId,
        SKU      = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        KgPedido = SUM(CAST(op.Peso AS DECIMAL(18,4)))
    FROM dbo.OrdenVentaProducto op
    GROUP BY
        op.PedidoId,
        UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
),
ov_surtido_agg AS (
    SELECT
        PedidoId = o.Id,
        SKU      = UPPER(LTRIM(RTRIM(sd.Articulo))),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp
        ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se
        ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    WHERE se.FechaValidacion IS NOT NULL
    GROUP BY
        o.Id,
        UPPER(LTRIM(RTRIM(sd.Articulo)))
),
ov_pendiente_sku AS (
    SELECT
        ov.Id,
        ov.Cliente,
        ov.VendedorId,
        ov.Estatus,
        ov.FechaDate,
        p.SKU,
        KgPendiente =
            CAST(
                CASE
                    WHEN ov.Estatus = 5 AND os.Id IS NOT NULL THEN 0
                    ELSE CASE
                        WHEN (p.KgPedido - ISNULL(sa.KgSurtido, 0)) < 0 THEN 0
                        ELSE (p.KgPedido - ISNULL(sa.KgSurtido, 0))
                    END
                END
            AS DECIMAL(18,4))
    FROM ov
    JOIN ov_peso_agg p
        ON p.PedidoId = ov.Id
    LEFT JOIN ov_surtido_agg sa
        ON sa.PedidoId = ov.Id
       AND sa.SKU = p.SKU
    LEFT JOIN ov_con_surtido os
        ON os.Id = ov.Id
),
consumo_cliente AS (
    SELECT
        Cliente = ovp.Cliente,
        SKU     = ovp.SKU,
        Mes     = MONTH(ovp.FechaDate),
        Anio    = YEAR(ovp.FechaDate),
        Kg      = SUM(ovp.KgPendiente)
    FROM ov_pendiente_sku ovp
    GROUP BY
        ovp.Cliente,
        ovp.SKU,
        MONTH(ovp.FechaDate),
        YEAR(ovp.FechaDate)
),
todo_normal AS (
    SELECT
        'CLIENTE' AS Origen,
        pn.Mes,
        pn.Anio,
        pn.Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        CAST(NULL AS INT) AS VendedorId,
        pn.SKU,
        pn.Presupuesto,
        ISNULL(cc.Kg, 0) AS Kg
    FROM presupuestos_normales pn
    LEFT JOIN consumo_cliente cc
        ON cc.Cliente = pn.Cliente
       AND cc.SKU     = pn.SKU
       AND cc.Mes     = pn.Mes
       AND cc.Anio    = pn.Anio

    UNION ALL

    SELECT
        'CLIENTE' AS Origen,
        cc.Mes,
        cc.Anio,
        cc.Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        CAST(NULL AS INT) AS VendedorId,
        cc.SKU,
        CAST(0 AS DECIMAL(18,4)) AS Presupuesto,
        cc.Kg
    FROM consumo_cliente cc
    LEFT JOIN presupuestos_normales pn
        ON pn.Cliente = cc.Cliente
       AND pn.SKU     = cc.SKU
       AND pn.Mes     = cc.Mes
       AND pn.Anio    = cc.Anio
    WHERE pn.Cliente IS NULL
),
presupuestos_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(pc.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
        Mes   = pc.Mes,
        Anio  = pc.Anio,
        Presupuesto = SUM(CAST(pc.PresupuestoAsignado AS DECIMAL(18,4)))
    FROM dbo.PresupuestoCedis pc
    GROUP BY
        UPPER(LTRIM(RTRIM(pc.Canal))),
        UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
        pc.Mes,
        pc.Anio
),
tr_surtido_agg AS (
    SELECT
        ts.TransferenciaId,
        SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
        KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    GROUP BY
        ts.TransferenciaId,
        UPPER(LTRIM(RTRIM(ts.Sku)))
),
consumo_cedis_base AS (
    SELECT Canal, SKU, Mes, Anio, Kg = SUM(Kg)
    FROM
    (
        SELECT
            Canal = cli.U_CANAL,
            SKU   = ovp.SKU,
            Mes   = MONTH(ovp.FechaDate),
            Anio  = YEAR(ovp.FechaDate),
            Kg    = SUM(ovp.KgPendiente)
        FROM ov_pendiente_sku ovp
        JOIN clientes cli
            ON cli.Cliente = ovp.Cliente
        WHERE cli.U_CANAL LIKE 'CEDIS%'
        GROUP BY
            cli.U_CANAL,
            ovp.SKU,
            MONTH(ovp.FechaDate),
            YEAR(ovp.FechaDate)

        UNION ALL

        SELECT
            Canal = UPPER(LTRIM(RTRIM(s.Canal))),
            SKU   = UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
            Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
            Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
            Kg    = SUM(
                        CASE
                            WHEN (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0)) < 0 THEN 0
                            ELSE (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0))
                        END
                   )
        FROM dbo.Transferencias t
        JOIN dbo.TransferenciaDetalles td
            ON td.TransferenciaId = t.Id
        JOIN dbo.Series s
            ON s.Sucursal = t.Sucursal
        LEFT JOIN tr_surtido_agg tsa
            ON tsa.TransferenciaId = t.Id
           AND tsa.SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
        WHERE t.FechaSolicitud IS NOT NULL
          AND t.Estatus BETWEEN 1 AND 4
          AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
        GROUP BY
            UPPER(LTRIM(RTRIM(s.Canal))),
            UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
            MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
            YEAR(TRY_CONVERT(date, t.FechaSolicitud))
    ) X
    GROUP BY Canal, SKU, Mes, Anio
),
todo_cedis AS (
    SELECT
        'CEDIS' AS Origen,
        pc.Mes,
        pc.Anio,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        pc.Canal,
        CAST(NULL AS INT) AS VendedorId,
        pc.SKU,
        pc.Presupuesto,
        ISNULL(cc.Kg, 0) AS Kg
    FROM presupuestos_cedis pc
    LEFT JOIN consumo_cedis_base cc
        ON cc.Canal = pc.Canal
       AND cc.SKU   = pc.SKU
       AND cc.Mes   = pc.Mes
       AND cc.Anio  = pc.Anio

    UNION ALL

    SELECT
        'CEDIS' AS Origen,
        cc.Mes,
        cc.Anio,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        cc.Canal,
        CAST(NULL AS INT) AS VendedorId,
        cc.SKU,
        CAST(0 AS DECIMAL(18,4)) AS Presupuesto,
        cc.Kg
    FROM consumo_cedis_base cc
    LEFT JOIN presupuestos_cedis pc
        ON pc.Canal = cc.Canal
       AND pc.SKU   = cc.SKU
       AND pc.Mes   = cc.Mes
       AND pc.Anio  = cc.Anio
    WHERE pc.Canal IS NULL
),
presupuestos_vendedor AS (
    SELECT
        VendedorId,
        SKU = UPPER(LTRIM(RTRIM(pv.ProductoCodigo))),
        Mes = pv.Mes,
        Anio = pv.Anio,
        Presupuesto = SUM(CAST(pv.PresupuestoAsignado AS DECIMAL(18,4)))
    FROM dbo.PresupuestoVendedor pv
    GROUP BY
        pv.VendedorId,
        UPPER(LTRIM(RTRIM(pv.ProductoCodigo))),
        pv.Mes,
        pv.Anio
),
pres_vendedor_x_canal AS (
    SELECT
        cv.Canal,
        pv.SKU,
        pv.Mes,
        pv.Anio,
        PresTotalCanal = SUM(CAST(pv.Presupuesto AS DECIMAL(18,4)))
    FROM presupuestos_vendedor pv
    JOIN canal_vendedores cv
        ON cv.VendedorId = pv.VendedorId
    GROUP BY
        cv.Canal,
        pv.SKU,
        pv.Mes,
        pv.Anio
),
consumo_vendedor_normal AS (
    SELECT
        ovp.VendedorId,
        SKU  = ovp.SKU,
        Mes  = MONTH(ovp.FechaDate),
        Anio = YEAR(ovp.FechaDate),
        Kg   = SUM(ovp.KgPendiente)
    FROM ov_pendiente_sku ovp
    JOIN clientes c
        ON c.Cliente = ovp.Cliente
       AND ISNULL(c.U_CANAL, '') NOT LIKE 'CEDIS%'
    WHERE ovp.VendedorId IS NOT NULL
    GROUP BY
        ovp.VendedorId,
        ovp.SKU,
        MONTH(ovp.FechaDate),
        YEAR(ovp.FechaDate)
),
consumo_vendedor_desde_cedis AS (
    SELECT
        VendedorId = pv.VendedorId,
        SKU        = pv.SKU,
        Mes        = pv.Mes,
        Anio       = pv.Anio,
        Kg = SUM(
                CASE
                    WHEN ISNULL(pxc.PresTotalCanal, 0) <= 0 THEN 0
                    ELSE (cb.Kg * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal))
                END
            )
    FROM presupuestos_vendedor pv
    JOIN canal_vendedores cv
        ON cv.VendedorId = pv.VendedorId
    JOIN pres_vendedor_x_canal pxc
        ON pxc.Canal = cv.Canal
       AND pxc.SKU   = pv.SKU
       AND pxc.Mes   = pv.Mes
       AND pxc.Anio  = pv.Anio
    JOIN consumo_cedis_base cb
        ON cb.Canal = cv.Canal
       AND cb.SKU   = pv.SKU
       AND cb.Mes   = pv.Mes
       AND cb.Anio  = pv.Anio
    GROUP BY
        pv.VendedorId,
        pv.SKU,
        pv.Mes,
        pv.Anio
),
consumo_vendedor_total AS (
    SELECT VendedorId, SKU, Mes, Anio, Kg = SUM(Kg)
    FROM
    (
        SELECT * FROM consumo_vendedor_normal
        UNION ALL
        SELECT * FROM consumo_vendedor_desde_cedis
    ) x
    GROUP BY VendedorId, SKU, Mes, Anio
),
todo_vendedor AS (
    SELECT
        'VENDEDOR' AS Origen,
        pv.Mes,
        pv.Anio,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        CAST(NULL AS NVARCHAR(100)) AS Canal,
        pv.VendedorId,
        pv.SKU,
        pv.Presupuesto,
        ISNULL(cv.Kg, 0) AS Kg
    FROM presupuestos_vendedor pv
    LEFT JOIN consumo_vendedor_total cv
        ON cv.VendedorId = pv.VendedorId
       AND cv.SKU        = pv.SKU
       AND cv.Mes        = pv.Mes
       AND cv.Anio       = pv.Anio
),
surtido_cliente AS (
    SELECT
        Cliente = UPPER(LTRIM(RTRIM(o.Cliente))),
        SKU     = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Mes     = MONTH(se.FechaValidacion),
        Anio    = YEAR(se.FechaValidacion),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp
        ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se
        ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    JOIN clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.Estatus <> 0
      AND se.FechaValidacion IS NOT NULL
      AND ISNULL(cli.U_CANAL, '') NOT LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(o.Cliente))),
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        MONTH(se.FechaValidacion),
        YEAR(se.FechaValidacion)
),
surtido_ov_cedis AS (
    SELECT
        Canal = cli.U_CANAL,
        SKU   = UPPER(LTRIM(RTRIM(sd.Articulo))),
        Mes   = MONTH(se.FechaValidacion),
        Anio  = YEAR(se.FechaValidacion),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp
        ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se
        ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    JOIN clientes cli
        ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.Estatus <> 0
      AND se.FechaValidacion IS NOT NULL
      AND cli.U_CANAL LIKE 'CEDIS%'
    GROUP BY
        cli.U_CANAL,
        UPPER(LTRIM(RTRIM(sd.Articulo))),
        MONTH(se.FechaValidacion),
        YEAR(se.FechaValidacion)
),
surtido_transferencias_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(s.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(ts.Sku))),
        Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
        Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
        KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    JOIN dbo.Transferencias t
        ON t.Id = ts.TransferenciaId
    JOIN dbo.Series s
        ON s.Sucursal = t.Sucursal
    WHERE t.FechaSolicitud IS NOT NULL
      AND t.Estatus >= 5
      AND ts.KgSurtido > 0
      AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
    GROUP BY
        UPPER(LTRIM(RTRIM(s.Canal))),
        UPPER(LTRIM(RTRIM(ts.Sku))),
        MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
        YEAR(TRY_CONVERT(date, t.FechaSolicitud))
),
surtido_cedis_base AS (
    SELECT Canal, SKU, Mes, Anio, KgSurtido = SUM(KgSurtido)
    FROM
    (
        SELECT * FROM surtido_ov_cedis
        UNION ALL
        SELECT * FROM surtido_transferencias_cedis
    ) x
    GROUP BY Canal, SKU, Mes, Anio
),
surtido_vendedor_normal AS (
    SELECT
        cl.VendedorId,
        sc.SKU,
        sc.Mes,
        sc.Anio,
        KgSurtido = SUM(sc.KgSurtido)
    FROM surtido_cliente sc
    JOIN clientes cl
        ON cl.Cliente = sc.Cliente
    WHERE cl.VendedorId IS NOT NULL
    GROUP BY
        cl.VendedorId,
        sc.SKU,
        sc.Mes,
        sc.Anio
),
surtido_vendedor_desde_cedis AS (
    SELECT
        VendedorId = pv.VendedorId,
        SKU        = pv.SKU,
        Mes        = pv.Mes,
        Anio       = pv.Anio,
        KgSurtido  = SUM(
                        CASE
                            WHEN ISNULL(pxc.PresTotalCanal, 0) <= 0 THEN 0
                            ELSE (sb.KgSurtido * (CAST(pv.Presupuesto AS DECIMAL(18,4)) / pxc.PresTotalCanal))
                        END
                     )
    FROM presupuestos_vendedor pv
    JOIN canal_vendedores cv
        ON cv.VendedorId = pv.VendedorId
    JOIN pres_vendedor_x_canal pxc
        ON pxc.Canal = cv.Canal
       AND pxc.SKU   = pv.SKU
       AND pxc.Mes   = pv.Mes
       AND pxc.Anio  = pv.Anio
    JOIN surtido_cedis_base sb
        ON sb.Canal = cv.Canal
       AND sb.SKU   = pv.SKU
       AND sb.Mes   = pv.Mes
       AND sb.Anio  = pv.Anio
    GROUP BY
        pv.VendedorId,
        pv.SKU,
        pv.Mes,
        pv.Anio
),
surtido_vendedor_total AS (
    SELECT VendedorId, SKU, Mes, Anio, KgSurtido = SUM(KgSurtido)
    FROM
    (
        SELECT * FROM surtido_vendedor_normal
        UNION ALL
        SELECT * FROM surtido_vendedor_desde_cedis
    ) x
    GROUP BY VendedorId, SKU, Mes, Anio
),
venta_real_base AS (
    SELECT
        ArticuloCodigo = UPPER(LTRIM(RTRIM(b.Articulo))),
        Mes            = MONTH(a.FechaValidacion),
        Anio           = YEAR(a.FechaValidacion),
        VendedorId     = cs.VendedorId,
        Vendedor       = UPPER(LTRIM(RTRIM(cs.VendedorNombre))),
        U_CANAL        = UPPER(LTRIM(RTRIM(cs.U_CANAL))),
        KgVendidos     = SUM(CAST(b.Kg AS DECIMAL(18,4)))
    FROM dbo.SurtidoEncabezado a
    INNER JOIN dbo.SurtidoDetalle b
        ON a.SolicitudSurtidoId = b.SolicitudSurtidoId
    INNER JOIN clientes cs
        ON cs.Cliente = UPPER(LTRIM(RTRIM(a.CodigoSap)))
    WHERE a.FechaValidacion IS NOT NULL
    GROUP BY
        UPPER(LTRIM(RTRIM(b.Articulo))),
        MONTH(a.FechaValidacion),
        YEAR(a.FechaValidacion),
        cs.VendedorId,
        UPPER(LTRIM(RTRIM(cs.VendedorNombre))),
        UPPER(LTRIM(RTRIM(cs.U_CANAL)))
),
todo_cedis_venta_real_extra AS (
    SELECT
        'CEDIS' AS Origen,
        vr.Mes,
        vr.Anio,
        CAST(NULL AS NVARCHAR(50)) AS Cliente,
        vr.U_CANAL AS Canal,
        CAST(NULL AS INT) AS VendedorId,
        vr.ArticuloCodigo AS SKU,
        CAST(0 AS DECIMAL(18,4)) AS Presupuesto,
        CAST(0 AS DECIMAL(18,4)) AS Kg
    FROM venta_real_base vr
    WHERE ISNULL(vr.U_CANAL, '') LIKE 'CEDIS%'
      AND NOT EXISTS
      (
          SELECT 1
          FROM todo_cedis tc
          WHERE tc.Canal = vr.U_CANAL
            AND tc.SKU   = vr.ArticuloCodigo
            AND tc.Mes   = vr.Mes
            AND tc.Anio  = vr.Anio
      )
),
t_base AS (
    SELECT * FROM todo_normal
    UNION ALL
    SELECT * FROM todo_cedis
    UNION ALL
    SELECT * FROM todo_cedis_venta_real_extra
    UNION ALL
    SELECT * FROM todo_vendedor
),
meses_distintos AS (
    SELECT DISTINCT Mes, Anio
    FROM t_base
),
dias_laborables AS (
    SELECT
        m.Mes,
        m.Anio,
        DiasMesLaborables = SUM(
            CASE
                WHEN (DATEDIFF(day, '19000101', cal.D) % 7) = 6 THEN 0
                ELSE 1
            END
        ),
        DiasLaborados = SUM(
            CASE
                WHEN cutoff.CutoffDate IS NULL THEN 0
                WHEN cal.D <= cutoff.CutoffDate
                 AND (DATEDIFF(day, '19000101', cal.D) % 7) <> 6
                THEN 1
                ELSE 0
            END
        )
    FROM meses_distintos m
    CROSS APPLY
    (
        SELECT
            StartDate = DATEFROMPARTS(m.Anio, m.Mes, 1),
            EndDate   = EOMONTH(DATEFROMPARTS(m.Anio, m.Mes, 1))
    ) rng
    CROSS APPLY
    (
        SELECT
            CutoffDate =
                CASE
                    WHEN CONVERT(date, GETDATE()) < rng.StartDate THEN NULL
                    WHEN CONVERT(date, GETDATE()) > rng.EndDate   THEN rng.EndDate
                    ELSE CONVERT(date, GETDATE())
                END
    ) cutoff
    CROSS APPLY
    (
        SELECT TOP (DATEDIFF(day, rng.StartDate, rng.EndDate) + 1)
               n = ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1
        FROM sys.all_objects
    ) nums
    CROSS APPLY
    (
        SELECT D = DATEADD(day, nums.n, rng.StartDate)
    ) cal
    GROUP BY
        m.Mes,
        m.Anio
)
SELECT
    t.Origen,
    t.Mes  AS MesConsulta,
    t.Anio AS AnioConsulta,
    ISNULL(t.Cliente, '-') AS ClienteCodigo,
    ISNULL(cl.NombreCliente, '-') AS NombreCliente,
    ISNULL(t.Canal, '-') AS Canal,
    ISNULL(t.VendedorId, 0) AS VendedorId,
    ISNULL(COALESCE(vend.VendedorNombre, cl.VendedorNombre), '-') AS VendedorNombre,
    t.SKU AS ProductoCodigo,
    prd.ProductoNombre,
    prd.U_MASTER AS U_MASTER,
    ISNULL(prd.ClasificacionId, 99) AS ClasificacionId,
    ISNULL(prd.ClasificacionNombre, 'POR DEFINIR') AS ClasificacionNombre,
    CAST(t.Presupuesto AS DECIMAL(18,4)) AS PresupuestoAsignado,
    CAST(t.Kg AS DECIMAL(18,4)) AS KgPedidosMes,
    CAST(
        CASE
            WHEN t.Origen = 'CLIENTE'  THEN ISNULL(src.KgSurtido, 0)
            WHEN t.Origen = 'CEDIS'    THEN ISNULL(srd.KgSurtido, 0)
            WHEN t.Origen = 'VENDEDOR' THEN ISNULL(srv.KgSurtido, 0)
            ELSE 0
        END
    AS DECIMAL(18,4)) AS KgSurtidoReal,
    CAST(
        CASE
            WHEN (
                t.Presupuesto
                - ISNULL(t.Kg, 0)
                - CASE
                    WHEN t.Origen = 'CLIENTE'  THEN ISNULL(src.KgSurtido, 0)
                    WHEN t.Origen = 'CEDIS'    THEN ISNULL(srd.KgSurtido, 0)
                    WHEN t.Origen = 'VENDEDOR' THEN ISNULL(srv.KgSurtido, 0)
                    ELSE 0
                  END
            ) < 0 THEN 0
            ELSE (
                t.Presupuesto
                - ISNULL(t.Kg, 0)
                - CASE
                    WHEN t.Origen = 'CLIENTE'  THEN ISNULL(src.KgSurtido, 0)
                    WHEN t.Origen = 'CEDIS'    THEN ISNULL(srd.KgSurtido, 0)
                    WHEN t.Origen = 'VENDEDOR' THEN ISNULL(srv.KgSurtido, 0)
                    ELSE 0
                  END
            )
        END
    AS DECIMAL(18,4)) AS DisponibleVenta,
    CAST(ISNULL(pp.PlanProduccion, 0) AS DECIMAL(18,4)) AS PlanProduccion,
    CAST(ISNULL(pr.Producido, 0) AS DECIMAL(18,4)) AS Producido,
    CAST(
        CASE
            WHEN ISNULL(dl.DiasLaborados, 0) <= 0 THEN 0
            ELSE (ISNULL(pr.Producido, 0) / NULLIF(CAST(dl.DiasLaborados AS DECIMAL(18,4)), 0))
                 * CAST(ISNULL(dl.DiasMesLaborables, 0) AS DECIMAL(18,4))
        END
    AS DECIMAL(18,4)) AS TendenciaProduccion
FROM t_base t
LEFT JOIN productos prd
    ON prd.SKU = t.SKU
LEFT JOIN clientes cl
    ON cl.Cliente = t.Cliente
LEFT JOIN vendedores vend
    ON vend.VendedorId = t.VendedorId
LEFT JOIN surtido_cliente src
    ON t.Origen = 'CLIENTE'
   AND src.Cliente = t.Cliente
   AND src.SKU     = t.SKU
   AND src.Mes     = t.Mes
   AND src.Anio    = t.Anio
LEFT JOIN surtido_cedis_base srd
    ON t.Origen = 'CEDIS'
   AND srd.Canal = t.Canal
   AND srd.SKU   = t.SKU
   AND srd.Mes   = t.Mes
   AND srd.Anio  = t.Anio
LEFT JOIN surtido_vendedor_total srv
    ON t.Origen = 'VENDEDOR'
   AND srv.VendedorId = t.VendedorId
   AND srv.SKU        = t.SKU
   AND srv.Mes        = t.Mes
   AND srv.Anio       = t.Anio
LEFT JOIN plan_prod pp
    ON pp.SKU  = t.SKU
   AND pp.Mes  = t.Mes
   AND pp.Anio = t.Anio
LEFT JOIN producido_real pr
    ON pr.SKU  = t.SKU
   AND pr.Mes  = t.Mes
   AND pr.Anio = t.Anio
LEFT JOIN dias_laborables dl
    ON dl.Mes  = t.Mes
   AND dl.Anio = t.Anio;
GO

/* =========================================================
   2) INVENTARIO INICIAL POR MES
   Consumo equivalente a /Comercial/InventarioInicialAyer
   ========================================================= */
CREATE OR ALTER VIEW rpt.vw_InventarioInicialMes
AS
SELECT
    Mes = MONTH(DATEADD(MONTH, 1, FechaInventario)),
    Anio = YEAR(DATEADD(MONTH, 1, FechaInventario)),
    Sku = UPPER(LTRIM(RTRIM(Sku))),
    InvInicial = SUM(CAST(PesoNeto AS DECIMAL(18,4)))
FROM dbo.InventarioAlmacenado_Meat
WHERE CodigoEtiqueta NOT LIKE '%SACT%'
GROUP BY
    YEAR(DATEADD(MONTH, 1, FechaInventario)),
    MONTH(DATEADD(MONTH, 1, FechaInventario)),
    UPPER(LTRIM(RTRIM(Sku)));
GO

/* =========================================================
   3) INVENTARIO ACTUAL
   Consumo equivalente a /Comercial/InventarioActual
   ========================================================= */
CREATE OR ALTER VIEW rpt.vw_InventarioActual
AS
SELECT
    Sku = UPPER(LTRIM(RTRIM(ProductoCodigo))),
    InvActual = SUM(CAST(kg AS DECIMAL(18,4)))
FROM dbo.InventarioSigo
WHERE colonia LIKE '%ventas%'
GROUP BY UPPER(LTRIM(RTRIM(ProductoCodigo)));
GO

/* =========================================================
   4) DETALLE DE PEDIDOS PENDIENTES
   Consumo equivalente a /Comercial/PedidosDetalle
   ========================================================= */
CREATE OR ALTER VIEW rpt.vw_PedidosDetallePresupuesto
AS
WITH
clientes AS (
    SELECT * FROM rpt.vw_ClientesPresupuesto
),
ov AS (
    SELECT
        o.Id,
        Cliente = UPPER(LTRIM(RTRIM(o.Cliente))),
        o.VendedorId,
        o.Estatus,
        o.Serie,
        FechaDate = TRY_CONVERT(date, o.FechaEntrega)
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.Series ser
        ON o.Serie = ser.NombreSerie
    INNER JOIN clientes c
        ON c.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
    WHERE o.FechaEntrega IS NOT NULL
      AND o.Estatus BETWEEN 1 AND 5
      AND UPPER(LTRIM(RTRIM(ISNULL(ser.Sucursal, '')))) = 'MATRIZ'
),
ov_con_surtido AS (
    SELECT DISTINCT o.Id
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp
        ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se
        ON se.SolicitudSurtidoId = sp.U_DocMeat
),
ov_peso_agg AS (
    SELECT
        PedidoId = op.PedidoId,
        SKU      = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        KgPedido = SUM(CAST(op.Peso AS DECIMAL(18,4)))
    FROM dbo.OrdenVentaProducto op
    GROUP BY op.PedidoId, UPPER(LTRIM(RTRIM(op.ProductoCodigo)))
),
ov_surtido_agg AS (
    SELECT
        PedidoId = o.Id,
        SKU      = UPPER(LTRIM(RTRIM(sd.Articulo))),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp
        ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se
        ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd
        ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    WHERE se.FechaValidacion IS NOT NULL
    GROUP BY o.Id, UPPER(LTRIM(RTRIM(sd.Articulo)))
),
ov_pendiente_sku AS (
    SELECT
        ov.Id,
        ov.Cliente,
        ov.VendedorId,
        ov.Estatus,
        ov.Serie,
        ov.FechaDate,
        p.SKU,
        KgPedido   = p.KgPedido,
        KgSurtido  = ISNULL(sa.KgSurtido, 0),
        KgPendiente =
            CAST(
                CASE
                    WHEN ov.Estatus = 5 AND os.Id IS NOT NULL THEN 0
                    ELSE CASE
                        WHEN (p.KgPedido - ISNULL(sa.KgSurtido, 0)) < 0 THEN 0
                        ELSE (p.KgPedido - ISNULL(sa.KgSurtido, 0))
                    END
                END
            AS DECIMAL(18,4))
    FROM ov
    JOIN ov_peso_agg p
        ON p.PedidoId = ov.Id
    LEFT JOIN ov_surtido_agg sa
        ON sa.PedidoId = ov.Id
       AND sa.SKU      = p.SKU
    LEFT JOIN ov_con_surtido os
        ON os.Id = ov.Id
),
tr_surtido_agg AS (
    SELECT
        ts.TransferenciaId,
        SKU = UPPER(LTRIM(RTRIM(ts.Sku))),
        KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
    FROM dbo.TransferenciaSurtido ts
    GROUP BY ts.TransferenciaId, UPPER(LTRIM(RTRIM(ts.Sku)))
),
tr_pendiente AS (
    SELECT
        Tipo = 'TRANSFERENCIA',
        DocumentoId = t.Id,
        Serie = CAST(NULL AS NVARCHAR(100)),
        Fecha = TRY_CONVERT(date, t.FechaSolicitud),
        Estatus = CAST(t.Estatus AS INT),
        Cliente = CAST(NULL AS NVARCHAR(50)),
        RazonSocial = CAST(NULL AS NVARCHAR(250)),
        Canal = UPPER(LTRIM(RTRIM(s.Canal))),
        VendedorId = CAST(NULL AS INT),
        Vendedor = CAST(NULL AS NVARCHAR(250)),
        SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
        KgPedido = SUM(CAST(td.CantidadKg AS DECIMAL(18,4))),
        KgSurtido = SUM(ISNULL(tsa.KgSurtido, 0)),
        KgPendiente = SUM(
            CASE
                WHEN (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0)) < 0 THEN 0
                ELSE (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0))
            END
        )
    FROM dbo.Transferencias t
    JOIN dbo.TransferenciaDetalles td
        ON td.TransferenciaId = t.Id
    JOIN dbo.Series s
        ON s.Sucursal = t.Sucursal
    LEFT JOIN tr_surtido_agg tsa
        ON tsa.TransferenciaId = t.Id
       AND tsa.SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
    WHERE t.FechaSolicitud IS NOT NULL
      AND t.Estatus BETWEEN 1 AND 4
      AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
    GROUP BY
        t.Id,
        t.Estatus,
        TRY_CONVERT(date, t.FechaSolicitud),
        UPPER(LTRIM(RTRIM(s.Canal))),
        UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
)
SELECT
    Tipo = 'OV',
    DocumentoId = ovp.Id,
    Serie = ovp.Serie,
    Fecha = ovp.FechaDate,
    Estatus = ovp.Estatus,
    Cliente = ovp.Cliente,
    RazonSocial = cs.NombreCliente,
    Canal = UPPER(LTRIM(RTRIM(cs.U_CANAL))),
    VendedorId = ovp.VendedorId,
    Vendedor = cs.VendedorNombre,
    SKU = ovp.SKU,
    KgPedido = ovp.KgPedido,
    KgSurtido = ovp.KgSurtido,
    KgPendiente = ovp.KgPendiente,
    Mes = MONTH(ovp.FechaDate),
    Anio = YEAR(ovp.FechaDate)
FROM ov_pendiente_sku ovp
LEFT JOIN clientes cs
    ON cs.Cliente = ovp.Cliente
WHERE ovp.KgPendiente > 0

UNION ALL

SELECT
    Tipo,
    DocumentoId,
    Serie,
    Fecha,
    Estatus,
    Cliente,
    RazonSocial,
    Canal,
    VendedorId,
    Vendedor,
    SKU,
    KgPedido,
    KgSurtido,
    KgPendiente,
    Mes = MONTH(Fecha),
    Anio = YEAR(Fecha)
FROM tr_pendiente
WHERE KgPendiente > 0;
GO

/* =========================================================
   5) VENTA REAL / SURTIDO VALIDADO
   Consumo equivalente a /Comercial/VentaRealResumen
   ========================================================= */
CREATE OR ALTER VIEW rpt.vw_VentaRealResumen
AS
SELECT
    ArticuloCodigo = UPPER(LTRIM(RTRIM(b.Articulo))),
    Mes = MONTH(a.FechaValidacion),
    Anio = YEAR(a.FechaValidacion),
    cs.VendedorId AS VendedorId,
    Vendedor = UPPER(LTRIM(RTRIM(cs.VendedorNombre))),
    U_CANAL = UPPER(LTRIM(RTRIM(cs.U_CANAL))),
    KgVendidos = SUM(CAST(b.Kg AS DECIMAL(18,4)))
FROM dbo.SurtidoEncabezado a
INNER JOIN dbo.SurtidoDetalle b
    ON a.SolicitudSurtidoId = b.SolicitudSurtidoId
INNER JOIN rpt.vw_ClientesPresupuesto cs
    ON cs.Cliente = UPPER(LTRIM(RTRIM(a.CodigoSap)))
WHERE a.FechaValidacion IS NOT NULL
GROUP BY
    UPPER(LTRIM(RTRIM(b.Articulo))),
    MONTH(a.FechaValidacion),
    YEAR(a.FechaValidacion),
    cs.VendedorId,
    UPPER(LTRIM(RTRIM(cs.VendedorNombre))),
    UPPER(LTRIM(RTRIM(cs.U_CANAL)));
GO

/* =========================================================
   6) DEVOLUCIONES MEAT
   Consumo equivalente a /Comercial/DevolucionesMeat
   ========================================================= */
CREATE OR ALTER VIEW rpt.vw_DevolucionesMeat
AS
SELECT
    Anio = YEAR(b.FechaDevolucion),
    Mes = MONTH(b.FechaDevolucion),
    Sku = UPPER(LTRIM(RTRIM(b.Articulo))),
    Kg = SUM(CAST(ISNULL(b.Peso, 0) AS DECIMAL(18,4))),
    ClienteId = UPPER(LTRIM(RTRIM(b.CodigoSap))),
    Cliente = b.Cliente,
    c.VendedorId,
    c.VendedorNombre,
    Canal = ISNULL(c.U_CANAL, '')
FROM dbo.DevolucionMeat b
INNER JOIN rpt.vw_ClientesPresupuesto c
    ON UPPER(LTRIM(RTRIM(b.CodigoSap))) = c.Cliente
WHERE b.FechaDevolucion IS NOT NULL
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Subpedido a
      WHERE a.U_DocMeat = b.SolicitudSurtidoId
  )
GROUP BY
    YEAR(b.FechaDevolucion),
    MONTH(b.FechaDevolucion),
    UPPER(LTRIM(RTRIM(b.Articulo))),
    UPPER(LTRIM(RTRIM(b.CodigoSap))),
    b.Cliente,
    c.VendedorId,
    c.VendedorNombre,
    ISNULL(c.U_CANAL, '');
GO

/* =========================================================
   7) EJEMPLOS DE CONSUMO
   ========================================================= */

-- Reporte principal, filtrado por mes/año:
-- SELECT *
-- FROM rpt.vw_ReportePresupuestosMes
-- WHERE AnioConsulta = 2026
--   AND MesConsulta = 7;

-- Reporte principal por vendedor:
-- SELECT *
-- FROM rpt.vw_ReportePresupuestosMes
-- WHERE AnioConsulta = 2026
--   AND MesConsulta = 7
--   AND VendedorId = 28;

-- Reporte principal por canal CEDIS:
-- SELECT *
-- FROM rpt.vw_ReportePresupuestosMes
-- WHERE AnioConsulta = 2026
--   AND MesConsulta = 7
--   AND Canal = 'CEDIS-MDA';

-- Inventario inicial del mes:
-- SELECT *
-- FROM rpt.vw_InventarioInicialMes
-- WHERE Anio = 2026
--   AND Mes = 7;

-- Inventario actual:
-- SELECT *
-- FROM rpt.vw_InventarioActual;

-- Detalle pedidos de un SKU:
-- SELECT *
-- FROM rpt.vw_PedidosDetallePresupuesto
-- WHERE Anio = 2026
--   AND Mes = 7
--   AND SKU = 'CAMBIA_SKU';

-- Venta real:
-- SELECT *
-- FROM rpt.vw_VentaRealResumen
-- WHERE Anio = 2026
--   AND Mes = 7;

-- Devoluciones:
-- SELECT *
-- FROM rpt.vw_DevolucionesMeat
-- WHERE Anio = 2026
--   AND Mes = 7;
