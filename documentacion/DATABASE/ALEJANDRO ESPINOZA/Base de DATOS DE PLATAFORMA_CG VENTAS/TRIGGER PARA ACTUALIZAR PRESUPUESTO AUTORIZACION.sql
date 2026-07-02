 CREATE TRIGGER trg_OrdenVentaProducto_UpdatePresupuesto
ON OrdenVentaProducto
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Pedidos afectados (INSERT / UPDATE)
    ;WITH Cambios AS (
        SELECT DISTINCT PedidoId
        FROM inserted
        UNION
        SELECT DISTINCT PedidoId
        FROM deleted
    ),
    Estado AS (
        SELECT
            OV.Id,
            -- ¿Hay alguna línea activa NO autorizada?
            CASE 
                WHEN EXISTS (
                    SELECT 1
                    FROM OrdenVentaProducto P
                    WHERE P.PedidoId = OV.Id
                      AND ISNULL(P.Eliminado, 0) = 0
                      AND ISNULL(P.AutorizacionPresupuestoLinea, 0) = 0
                )
                THEN 0       -- aún hay alguna línea sin autorizar
                ELSE 1       -- todas las líneas activas tienen AutorizacionPresupuestoLinea = 1
            END AS NuevoAutPresupuesto
        FROM OrdenVenta OV
        INNER JOIN Cambios C
            ON C.PedidoId = OV.Id
    )
    UPDATE OV
    SET
        AutorizacionPresupuesto = E.NuevoAutPresupuesto,
        Estatus =
            CASE
                -- Si aún hay pendientes de presupuesto, no toques el estatus
                WHEN E.NuevoAutPresupuesto = 0 THEN OV.Estatus
                -- Si YA hay presupuesto global y también precio + crédito, pasa a 3
                WHEN E.NuevoAutPresupuesto = 1
                 AND OV.AutorizacionPrecio = 1
                 AND OV.AutorizacionCredito = 1
                    THEN 3
                ELSE OV.Estatus
            END
    FROM OrdenVenta OV
    INNER JOIN Estado E
        ON E.Id = OV.Id;
END;
GO
