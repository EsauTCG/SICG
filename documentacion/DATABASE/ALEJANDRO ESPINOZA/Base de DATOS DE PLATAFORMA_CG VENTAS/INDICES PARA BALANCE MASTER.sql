-- ArticuloSap: lo unes por ProductoCodigo y lees U_MASTER/Rotacion
CREATE INDEX IX_ArticuloSap_Producto
ON dbo.ArticuloSap (ProductoCodigo)
INCLUDE (U_MASTER, Rotacion);

-- También ayuda para el GROUP BY / ORDER BY por U_MASTER
CREATE INDEX IX_ArticuloSap_U_MASTER
ON dbo.ArticuloSap (U_MASTER)
INCLUDE (ProductoCodigo, Rotacion);

-- Presupuestos: filtras por Mes/Año y te unes por ProductoCodigo
CREATE INDEX IX_Presupuestos_MesAno_Producto
ON dbo.Presupuestos (Mes, Año, ProductoCodigo)
INCLUDE (Presupuesto);

-- InventarioSigo: agregas por ProductoCodigo
CREATE INDEX IX_InventarioSigo_Producto
ON dbo.InventarioSigo (ProductoCodigo)
INCLUDE (Kg);

-- PlanDetalle: te unes por ProductoCodigo y fk_Plan y agregas Peso
CREATE INDEX IX_PlanDetalle_Producto_fkPlan
ON dbo.PlanDetalle (ProductoCodigo, fk_Plan)
INCLUDE (Peso);

-- PlanProduccion: como NO quieres cambiar MONTH()/YEAR(), 
-- crea columnas calculadas PERSISTED e indícelas
ALTER TABLE dbo.PlanProduccion
ADD Dia as Day(Fecha)PERSISTED,
ADD Mes AS MONTH(Fecha) PERSISTED,
    Anio AS YEAR(Fecha)  PERSISTED;

CREATE INDEX IX_PlanProduccion_Mes_Anio_Id
ON dbo.PlanProduccion (Mes, Anio, Id);


select * from planproduccion


