WITH Base AS (
    SELECT 
        b.U_MASTER,
        MAX(b.Rotacion) AS Rotacion,
        MAX(c.Presupuesto) AS Presupuesto,
        SUM(a.Kg) AS Inventario,
        d.valor AS Factor,
        MAX(e.Peso) AS PlanProduccion
    FROM ArticuloSap b
    LEFT JOIN InventarioSigo a 
        ON a.ProductoCodigo = b.ProductoCodigo
    LEFT JOIN Presupuestos c 
        ON b.ProductoCodigo = c.ProductoCodigo 
        AND c.Mes = {0} 
        AND c.Año = {1}  
    CROSS JOIN Factor d
    LEFT JOIN (
        SELECT e.*
        FROM PlanDetalle e
        INNER JOIN PlanProduccion f 
            ON e.fk_Plan = f.Id
        WHERE 
		  MONTH(f.Fecha) = {0} AND 
		  YEAR(f.Fecha) = {1} 
    ) e ON e.ProductoCodigo = b.ProductoCodigo
    WHERE b.U_MASTER <> '(SIN MASTER)'
    GROUP BY b.U_MASTER, d.valor
)
SELECT 
    U_MASTER,
    CAST(Rotacion AS int) AS Rotacion,
    CAST(ROUND(ISNULL(PlanProduccion,0),0) AS int) AS PlanProduccion,
    CAST(ROUND(ISNULL(Inventario,0),0) AS int) AS Inventario,
    CAST(ROUND(ISNULL(Presupuesto,0) / 30.4 * (Rotacion * Factor),0) AS int) AS InvIdeal,
    CAST(ROUND(ISNULL(PlanProduccion,0),0) + ROUND(ISNULL(Inventario,0),0) - ROUND(ISNULL(Presupuesto,0) / 30.4 * (Rotacion * Factor),0) AS int) AS Disponible,
    CAST(ROUND(ISNULL(Presupuesto,0),0) AS int) AS Presupuesto,
    CAST(ROUND(ISNULL(PlanProduccion,0),0) + ROUND(ISNULL(Inventario,0),0) - ROUND(ISNULL(Presupuesto,0) / 30.4 * (Rotacion * Factor),0) - ROUND(ISNULL(Presupuesto,0),0) AS int) AS GAP,
    CAST(
        CASE WHEN ISNULL(Presupuesto,0) = 0 THEN 0
             ELSE ROUND(((ROUND(ISNULL(PlanProduccion,0),0) + ROUND(ISNULL(Inventario,0),0) - ROUND(ISNULL(Presupuesto,0) / 30.4 * (Rotacion * Factor),0)) * 1.0 / ROUND(ISNULL(Presupuesto,0),0)) * 100,0)
        END AS int
    ) AS Porcentaje
FROM Base
ORDER BY U_MASTER;


---GAP CONCENTRADO

-- GAP agrupado solo por U_MASTER
SELECT 
    b.U_MASTER,
    MAX(b.Rotacion) AS Rotacion, -- Usamos MAX para mantener una sola fila por U_MASTER
    '86,801' AS PlanProduccion,
    FORMAT(ROUND(SUM(a.Kg), 0), 'N0') AS Inventario,
    FORMAT(ROUND(max(c.Presupuesto / 30.4 * (b.Rotacion * d.valor)), 0), 'N0') AS InvIdeal,
    FORMAT(86801 + SUM(a.Kg) - ROUND(MAX(c.Presupuesto / 30.4 * (b.Rotacion * d.valor)), 0), 'N0') AS Disponible,	
    FORMAT(ROUND(MAX(c.Presupuesto), 0), 'N0') AS Presupuesto,
    FORMAT((86801 + SUM(a.Kg)) - ROUND(MAX(c.Presupuesto / 30.4 * (b.Rotacion * d.valor)), 0) - ROUND(MAX(c.Presupuesto), 0), 'N0') AS GAP,
    FORMAT(((86801 + SUM(a.Kg) - ROUND(MAX(c.Presupuesto / 30.4 * (b.Rotacion * d.valor)), 0)) * 1.0 / ROUND(MAX(c.Presupuesto), 0)) * 100, 'N0') + ' %' AS Porcentaje
FROM InventarioSigo a
INNER JOIN ArticuloSap b ON a.ProductoCodigo = b.ProductoCodigo
INNER JOIN Presupuestos c ON b.ProductoCodigo = c.ProductoCodigo
CROSS JOIN Factor d
WHERE b.U_MASTER <> '(SIN MASTER)' 
  AND c.Mes = '10' 
  AND c.Año = '2025'

--    AND c.Mes = {0}
--AND c.Año = {1}
GROUP BY b.U_MASTER

-- GAP CONCENTRADO








--GAP
select 
b.U_MASTER,
b.Rotacion,
'86,801' as PlanProduccion,
FORMAT(ROUND(SUM(a.Kg), 0), 'N0') AS Inventario,
FORMAT(ROUND(c.Presupuesto / 30.4 * (b.Rotacion * d.valor), 0), 'N0') AS 'inv Ideal',
FORMAT(86801 + SUM(a.Kg) - ROUND(c.Presupuesto / 30.4 * (b.Rotacion *  d.valor), 0), 'N0') AS Disponible,
FORMAT(ROUND(c.Presupuesto, 0), 'N0') as 'Presupuesto',
FORMAT((86801 + SUM(a.Kg)) - ROUND(c.Presupuesto / 30.4 * (b.Rotacion *  d.valor), 0) - ROUND(c.Presupuesto, 0),'N0') AS GAP,
FORMAT(((86801 + SUM(a.Kg) - ROUND(c.Presupuesto / 30.4 * (b.Rotacion * d.valor), 0)) * 1.0 / ROUND(c.Presupuesto, 0)) * 100,'N0') + ' %' AS [% Cum]
from InventarioSigo a
inner join ArticuloSap b on a.ProductoCodigo = b.ProductoCodigo
inner join Presupuestos c on  b.ProductoCodigo = c.ProductoCodigo
CROSS JOIN Factor d
where b.U_MASTER <> '(SIN MASTER)'  and c.Mes = '10' and Año = '2025' 
GROUP BY b.U_MASTER,b.Rotacion,c.Presupuesto,d.valor
--GAP


select * from ArticuloSap where ProductoCodigo ='V101'

--UPDATE ArticuloSap SET Rotacion = 1 where ProductoCodigo ='V101'


SELECT SUM(Kg) FROM InventarioSigo where ProductoCodigo = 'V101'


SELECT * FROM ArticuloSap
SELECT * FROM ClienteSap

SELECT * FROM Presupuestos
SELECT * FROM FACTOR
select * from OrdenVenta
--update FACTOR set valor = 3

--insert into factor values (1)


EXEC ActualizarInventarioSigo


SELECT * FROM InventarioSigo