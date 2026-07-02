--EXEC dbo.ActualizarKilosCajaDesdeInventario;

CREATE  PROCEDURE dbo.ActualizarKilosCajaDesdeInventario
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Promedios AS (
        SELECT
            i.ProductoCodigo,
            Promedio = CAST(
                ROUND(
                    SUM(ISNULL(i.Kg, 0)) * 1.0 / NULLIF(SUM(ISNULL(i.Cajas, 0)), 0),
                    2
                ) AS DECIMAL(18,2)
            )
        FROM dbo.InventarioSigo i
        where colonia like '%ventas%' and  Colonia <> 'VENTAS 2'
        GROUP BY i.ProductoCodigo
        HAVING SUM(ISNULL(i.Cajas, 0)) <> 0   -- evita división entre 0
    )
    UPDATE a
        SET a.U_KilosCaja = p.Promedio
    FROM dbo.ArticuloSap a
    INNER JOIN Promedios p
        ON p.ProductoCodigo = a.ProductoCodigo;
END;
GO
