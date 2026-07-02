select 
d.Nombrecliente,
b.ProductoCodigo,
b.ProductoNombre,
b.Cajas,
b.Peso,
a.FechaEntrega AS FechaEmbarcar
from OrdenVenta a
inner join OrdenVentaProducto b on a.Id = b.PedidoId
inner join ArticuloSap c on b.ProductoCodigo = c.ProductoCodigo
inner join ClienteSap d on a.Cliente = d.Cliente
UNION ALL
select 
a.Sucursal,
c.ProductoCodigo,
c.ProductoNombre,
b.Cajas,
b.CantidadKg,
A.FechaSolicitud AS FechaEmbarcar
from Transferencias a 
inner join TransferenciaDetalles b on a.Id = b.TransferenciaId
inner join ArticuloSap c on b.ProductoCodigo = c.ProductoCodigo




;WITH Demanda AS (
    SELECT
        d.Nombrecliente        AS Cliente,
        b.ProductoCodigo,
        b.ProductoNombre,
        CAST(b.Cajas AS decimal(18,2)) AS CajasSolicitadas,
        CAST(b.Peso  AS decimal(18,3)) AS KgSolicitados,
        a.FechaEntrega         AS FechaEmbarcar
    FROM OrdenVenta a
    INNER JOIN OrdenVentaProducto b ON a.Id = b.PedidoId
    INNER JOIN ArticuloSap c        ON b.ProductoCodigo = c.ProductoCodigo
    INNER JOIN ClienteSap d         ON a.Cliente = d.Cliente
    UNION ALL
    SELECT
        a.Sucursal             AS Cliente,
        c.ProductoCodigo,
        c.ProductoNombre,
        CAST(b.Cajas      AS decimal(18,2)) AS CajasSolicitadas,
        CAST(b.CantidadKg AS decimal(18,3)) AS KgSolicitados,
        a.FechaSolicitud       AS FechaEmbarcar
    FROM Transferencias a
    INNER JOIN TransferenciaDetalles b ON a.Id = b.TransferenciaId
    INNER JOIN ArticuloSap c           ON b.ProductoCodigo = c.ProductoCodigo
),
DemandaPorSKU AS (
    SELECT
        ProductoCodigo,
        MAX(ProductoNombre)      AS ProductoNombre,
        SUM(CajasSolicitadas)    AS CajasSolicitadas,
        SUM(KgSolicitados)       AS KilosSolicitados
    FROM Demanda
    WHERE FechaEmbarcar >= '2026-01-29'
      AND FechaEmbarcar <  '2026-01-31'
    GROUP BY ProductoCodigo
),
InventarioPorSKU AS (
    SELECT
        ProductoCodigo,
        SUM(CAST(Cajas AS decimal(18,2))) AS CajasDisponibles,
        SUM(CAST(Kg    AS decimal(18,3))) AS KilosDisponibles
    FROM InventarioSigo
    GROUP BY ProductoCodigo
)
SELECT
    d.ProductoCodigo AS SKU,
    d.ProductoNombre AS [PRODUCTO DISPONIBLE],

    d.CajasSolicitadas AS [CAJAS SOLICITADAS],
    d.KilosSolicitados AS [KILOS SOLICITADOS],

    ISNULL(i.CajasDisponibles, 0) AS [CAJAS DISPONIBLES],
    ISNULL(i.KilosDisponibles, 0) AS [KILOS DISPONIBLES],

    (ISNULL(i.CajasDisponibles, 0) - d.CajasSolicitadas) AS [CAJAS CONFIRMADAS],
    (ISNULL(i.KilosDisponibles, 0) - d.KilosSolicitados) AS [KILOS CONFIRMADOS]
FROM DemandaPorSKU d
LEFT JOIN InventarioPorSKU i
    ON i.ProductoCodigo = d.ProductoCodigo
ORDER BY [CAJAS CONFIRMADAS] ASC, d.ProductoCodigo;



