;WITH CTE_Presupuestos AS
(
    SELECT
        c.U_Canal AS CanalVta,
        c.U_MT_Clasificacion AS Estatus,
        c.Nombrecliente AS RazonSocial,
        d.U_MASTER AS Master,
        a.Presupuesto
    FROM Presupuestos a
    INNER JOIN ArticuloSap b ON a.ProductoCodigo = b.ProductoCodigo
    INNER JOIN ClienteSap c ON a.ClienteId = c.Cliente
    INNER JOIN ArticuloSap d ON a.ProductoCodigo = d.ProductoCodigo
    WHERE a.Mes = '10' AND a.Año = '2025'
)
SELECT
    Master,
    MAX(CanalVta) AS CanalVta,        -- toma solo un valor representativo
    MAX(Estatus) AS EstatusClientes,  -- toma solo un valor representativo
    MAX(RazonSocial) AS Cliente,      -- toma solo un valor representativo
    FORMAT(ROUND(SUM(Presupuesto), 0), 'N0') AS PresupuestoTotal
FROM CTE_Presupuestos
GROUP BY Master
ORDER BY Master;



select 
C.U_Canal as 'Canal(vta)',
C.U_MT_Clasificacion as Estatus,
C.Nombrecliente as RazonSocial,
a.ProductoCodigo,
d.ProductoNombre,
FORMAT(ROUND(a.Presupuesto, 0), 'N0') AS Presupuesto
from Presupuestos a
inner join ArticuloSap b on a.ProductoCodigo = b.ProductoCodigo
inner join ClienteSap c on a.ClienteId = c.cliente
inner join ArticuloSap d on a.ProductoCodigo = d.ProductoCodigo
where a.Mes = '11' and a.Año = '2025'