-- Opcional: parámetros (NO se usan como filtro; la consulta trae todo)
-- DECLARE @CardCode NVARCHAR(50) = NULL;
-- DECLARE @FechaEntrega DATE = NULL;

WITH
-- Normaliza órdenes con fecha válida
ov AS (
    SELECT
        o.Id,
        o.Cliente,
        o.Estatus,
        FechaDate = TRY_CONVERT(date, o.FechaEntrega)
    FROM dbo.OrdenVenta o
),
-- Consumo agregado por CLIENTE / SKU / MES / AÑO
consumo_cliente AS (
    SELECT
        Cliente = UPPER(ov.Cliente),
        SKU     = UPPER(op.ProductoCodigo),
        Mes     = MONTH(ov.FechaDate),
        Anio    = YEAR(ov.FechaDate),
        Kg      = SUM(CAST(op.Peso AS DECIMAL(18,4)))
    FROM ov
    JOIN dbo.OrdenVentaProducto op ON ov.Id = op.PedidoId
    WHERE ov.FechaDate IS NOT NULL
      AND ov.Estatus <> 0
      AND (op.Eliminado IS NULL OR op.Eliminado = 0)
    GROUP BY UPPER(ov.Cliente), UPPER(op.ProductoCodigo), MONTH(ov.FechaDate), YEAR(ov.FechaDate)
),
-- Consumo agregado por CANAL / SKU / MES / AÑO
consumo_canal AS (
    SELECT
        Canal   = UPPER(LTRIM(RTRIM(cli.U_CANAL))),
        SKU     = UPPER(op.ProductoCodigo),
        Mes     = MONTH(ov.FechaDate),
        Anio    = YEAR(ov.FechaDate),
        Kg      = SUM(CAST(op.Peso AS DECIMAL(18,4)))
    FROM ov
    JOIN dbo.OrdenVentaProducto op ON ov.Id = op.PedidoId
    JOIN dbo.ClienteSap cli ON ov.Cliente = cli.Cliente
    WHERE ov.FechaDate IS NOT NULL
      AND ov.Estatus <> 0
      AND (op.Eliminado IS NULL OR op.Eliminado = 0)
    GROUP BY UPPER(LTRIM(RTRIM(cli.U_CANAL))), UPPER(op.ProductoCodigo), MONTH(ov.FechaDate), YEAR(ov.FechaDate)
),
-- Presupuestos por CLIENTE / SKU / MES / AÑO
presupuestos_normales AS (
    SELECT
        Cliente = UPPER(LTRIM(RTRIM(p.ClienteId))),
        SKU     = UPPER(LTRIM(RTRIM(p.ProductoCodigo))),
        Mes     = p.Mes,
        Anio    = p.Año,
        Presupuesto = SUM(p.Presupuesto)
    FROM dbo.Presupuestos p
    GROUP BY UPPER(LTRIM(RTRIM(p.ClienteId))), UPPER(LTRIM(RTRIM(p.ProductoCodigo))), p.Mes, p.Año
),
-- Presupuestos por CANAL / SKU / MES / AÑO
presupuestos_cedis AS (
    SELECT
        Canal = UPPER(LTRIM(RTRIM(pc.Canal))),
        SKU   = UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
        Mes   = pc.Mes,
        Anio  = pc.Anio,
        Presupuesto = SUM(pc.PresupuestoAsignado)
    FROM dbo.PresupuestoCedis pc
    GROUP BY UPPER(LTRIM(RTRIM(pc.Canal))), UPPER(LTRIM(RTRIM(pc.ProductoCodigo))), pc.Mes, pc.Anio
),
-- Catálogo de productos
productos AS (
    SELECT
        SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
        ProductoNombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), ''), a.ProductoCodigo)
    FROM dbo.ArticuloSap a
),
-- Catálogo de clientes (código + nombre)
clientes AS (
    SELECT
        Cliente       = UPPER(LTRIM(RTRIM(cs.Cliente))),
        NombreCliente = COALESCE(NULLIF(LTRIM(RTRIM(cs.NombreCliente)), ''), cs.Cliente)
    FROM dbo.ClienteSap cs
),
-- UNION 1: todo NORMAL (por cliente), incluyendo consumo sin presupuesto y presupuesto sin consumo
todo_normal AS (
    SELECT
        origen             = 'CLIENTE',
        Mes                = COALESCE(pn.Mes, cc.Mes),
        Anio               = COALESCE(pn.Anio, cc.Anio),
        Cliente            = COALESCE(pn.Cliente, cc.Cliente),
        Canal              = CAST(NULL AS NVARCHAR(100)),
        SKU                = COALESCE(pn.SKU, cc.SKU),
        Presupuesto        = ISNULL(pn.Presupuesto, 0),
        Kg                 = ISNULL(cc.Kg, 0)
    FROM presupuestos_normales pn
    FULL OUTER JOIN consumo_cliente cc
      ON cc.Cliente = pn.Cliente
     AND cc.SKU     = pn.SKU
     AND cc.Mes     = pn.Mes
     AND cc.Anio    = pn.Anio
),
-- UNION 2: todo CEDIS (por canal), incluyendo consumo sin presupuesto y presupuesto sin consumo
todo_cedis AS (
    SELECT
        origen             = 'CEDIS',
        Mes                = COALESCE(pc.Mes, ca.Mes),
        Anio               = COALESCE(pc.Anio, ca.Anio),
        Cliente            = CAST(NULL AS NVARCHAR(50)),
        Canal              = COALESCE(pc.Canal, ca.Canal),
        SKU                = COALESCE(pc.SKU, ca.SKU),
        Presupuesto        = ISNULL(pc.Presupuesto, 0),
        Kg                 = ISNULL(ca.Kg, 0)
    FROM presupuestos_cedis pc
    FULL OUTER JOIN consumo_canal ca
      ON ca.Canal = pc.Canal
     AND ca.SKU   = pc.SKU
     AND ca.Mes   = pc.Mes
     AND ca.Anio  = pc.Anio
)
-- Resultado final: TODO lo existente en BD para ambos esquemas
SELECT
    origen,
    mesConsulta   = t.Mes,
    anioConsulta  = t.Anio,
    clienteCodigo = t.Cliente,
    nombreCliente = cl.NombreCliente,
    canal         = t.Canal,
    productoCodigo      = t.SKU,
    productoNombre      = prd.ProductoNombre,
    presupuestoAsignado = t.Presupuesto,
    kgPedidosMes        = t.Kg,
    presupuestoDisponible = t.Presupuesto - t.Kg
FROM (
    SELECT * FROM todo_normal
    UNION ALL
    SELECT * FROM todo_cedis
) t
LEFT JOIN productos prd ON prd.SKU = t.SKU
LEFT JOIN clientes  cl  ON cl.Cliente = t.Cliente
-- SIN WHERE: trae TODO (presupuestos y/o consumos), con o sin catálogos
ORDER BY origen, anioConsulta, mesConsulta, ISNULL(canal, ''), productoCodigo;
