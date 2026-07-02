USE [SIGO]
GO
/****** Object:  StoredProcedure [dbo].[Reporte_Ventas_Presupuesto]    Script Date: 24/02/2026 05:30:13 p. m. ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[Reporte_Ventas_Presupuesto]
    @Anio  int,
    @Meses varchar(100)
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH M AS (
        SELECT DISTINCT TRY_CAST(LTRIM(RTRIM(value)) AS int) AS MesNum
        FROM STRING_SPLIT(@Meses, ',')
        WHERE TRY_CAST(LTRIM(RTRIM(value)) AS int) BETWEEN 1 AND 12
    )
    SELECT DATEFROMPARTS(@Anio, MesNum, 1) AS Mes
    INTO #MesesSel
    FROM M;

    IF NOT EXISTS (SELECT 1 FROM #MesesSel) RETURN;

    DECLARE @MesIni date = (SELECT MIN(Mes) FROM #MesesSel);
    DECLARE @MesFin date = EOMONTH((SELECT MAX(Mes) FROM #MesesSel));
    DECLARE @Hoy date = CONVERT(date, GETDATE());

    ;WITH Meses AS (
        SELECT Mes FROM #MesesSel
    ),
    Ventas AS (
        SELECT
            a.ClienteID,
            DATEFROMPARTS(YEAR(a.FechaVenta), MONTH(a.FechaVenta), 1) AS Mes,
            SUM(a.Peso) AS Venta
        FROM dbo.VentasHistoricas a
        WHERE a.FechaVenta >= @MesIni
          AND a.FechaVenta <  DATEADD(day, 1, @MesFin)
        GROUP BY a.ClienteID, DATEFROMPARTS(YEAR(a.FechaVenta), MONTH(a.FechaVenta), 1)
    ),
    PresupuestoCliente AS (
        SELECT
            p.ClienteId AS ClienteID,
            DATEFROMPARTS(p.[Año], p.Mes, 1) AS Mes,
            SUM(p.Presupuesto) AS Presupuesto
        FROM dbo.Presupuestos p
        INNER JOIN Meses m
            ON m.Mes = DATEFROMPARTS(p.[Año], p.Mes, 1)
        WHERE p.[Año] = @Anio
        GROUP BY p.ClienteId, DATEFROMPARTS(p.[Año], p.Mes, 1)
    ),
    Dias AS (
        SELECT
            m.Mes,
            CASE
                WHEN m.Mes = DATEFROMPARTS(YEAR(@Hoy), MONTH(@Hoy), 1) THEN @Hoy
                ELSE EOMONTH(m.Mes)
            END AS FechaCorte,
            SUM(CASE WHEN DATENAME(WEEKDAY, DATEADD(DAY, n.n, m.Mes)) = 'Sunday' THEN 0 ELSE 1 END) AS DiasHabMes,
            SUM(CASE
                    WHEN DATEADD(DAY, n.n, m.Mes) <=
                         CASE
                            WHEN m.Mes = DATEFROMPARTS(YEAR(@Hoy), MONTH(@Hoy), 1) THEN @Hoy
                            ELSE EOMONTH(m.Mes)
                         END
                     AND DATENAME(WEEKDAY, DATEADD(DAY, n.n, m.Mes)) <> 'Sunday'
                    THEN 1 ELSE 0
                END) AS DiasHabTrans
        FROM Meses m
        CROSS APPLY (
            SELECT TOP (31) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n
            FROM sys.all_objects
        ) n
        WHERE DATEADD(DAY, n.n, m.Mes) <= EOMONTH(m.Mes)
        GROUP BY m.Mes
    ),
    BaseMes AS (
        SELECT
            cs.Cliente AS ClienteID,
            cs.Nombrecliente AS RazonSocial,
            cs.VendedorNombre AS VendedorNombre,
            m.Mes
        FROM dbo.ClienteSap cs
        CROSS JOIN Meses m
    ),
    KPI AS (
        SELECT
            b.ClienteID,
            b.RazonSocial,
            b.VendedorNombre,
            b.Mes,
            ISNULL(v.Venta, 0) AS Venta,
            ISNULL(p.Presupuesto, 0) AS Presupuesto,
            CASE WHEN ISNULL(p.Presupuesto,0) = 0 THEN NULL
                 ELSE 100.0 * ISNULL(v.Venta,0) / NULLIF(p.Presupuesto,0) END AS Cump,
            CASE
                WHEN d.DiasHabTrans IS NULL OR d.DiasHabTrans = 0 THEN NULL
                ELSE (1.0 * ISNULL(v.Venta,0) / NULLIF(d.DiasHabTrans,0)) * d.DiasHabMes
            END AS Tend
        FROM BaseMes b
        LEFT JOIN Ventas v
            ON v.ClienteID = b.ClienteID
           AND v.Mes = b.Mes
        LEFT JOIN PresupuestoCliente p
            ON p.ClienteID = b.ClienteID
           AND p.Mes = b.Mes
        LEFT JOIN Dias d
            ON d.Mes = b.Mes
    )
    SELECT
        ClienteID,
        RazonSocial,
        VendedorNombre,
        Mes,
        Venta,
        Presupuesto,
        Cump,
        Tend
    FROM KPI
    WHERE NOT (Venta = 0 AND Presupuesto = 0)
    ORDER BY RazonSocial, Mes;
END

--EXEC dbo.Reporte_Ventas_Presupuesto
--  @Anio  = 2026,
--  @Meses = '2,3';