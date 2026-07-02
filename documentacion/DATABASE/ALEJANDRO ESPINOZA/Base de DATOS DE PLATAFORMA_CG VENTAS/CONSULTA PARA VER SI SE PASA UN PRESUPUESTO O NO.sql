---1) Diagnóstico por OV y línea

--👉 Con esto, por cada línea vas a ver en la columna DiagnosticoBase exactamente qué condición la está sacando:

--Estatus != 2

--OV ya tiene AutorizacionPresupuesto = 1

--Línea eliminada

--Línea ya autorizada

--Cliente CEDIS + Serie ≠ MATRIZ

--O bien “OK: pasa filtros base…”

DECLARE @Consecutivo VARCHAR(50) = 'OV-00001027';  -- <-- PON AQUÍ LA OV QUE QUIERES REVISAR

SELECT
    ov.Id                  AS OrdenVentaId,
    ov.Consecutivo,
    ov.Estatus,
    ov.AutorizacionPresupuesto,
    ov.FechaEntrega,

    li.Id                  AS LineaId,
    li.ProductoCodigo,
    li.ProductoNombre,
    li.Peso                AS KilosLinea,
    li.AutorizacionPresupuestoLinea,
    li.Eliminado,

    cli.Cliente            AS ClienteId,
    cli.Nombrecliente      AS ClienteNombre,
    cli.U_CANAL            AS CanalCliente,

    se.Sucursal            AS SucursalSerie,
    se.Canal               AS CanalSerie,

    -- 🔎 Diagnóstico de por qué SÍ/NO pasa a baseLines
    CASE 
        WHEN ov.Estatus = 0 
            THEN 'NO: OV cancelada (Estatus = 0)'

        WHEN ov.Estatus <> 2 
            THEN 'NO: OV con Estatus distinto de 2 (se exige Estatus = 2)'

        WHEN ISNULL(ov.AutorizacionPresupuesto,0) = 1 
            THEN 'NO: OV ya tiene AutorizacionPresupuesto = 1'

        WHEN ISNULL(li.Eliminado,0) = 1 
            THEN 'NO: Línea marcada como Eliminada'

        WHEN ISNULL(li.AutorizacionPresupuestoLinea,0) = 1 
            THEN 'NO: Línea ya tiene AutorizacionPresupuestoLinea = 1'

        WHEN UPPER(ISNULL(cli.U_CANAL,'')) LIKE 'CEDIS%' 
             AND UPPER(ISNULL(se.Sucursal,'')) <> 'MATRIZ'
            THEN 'NO: Cliente CEDIS + Serie distinta de MATRIZ (se excluye en regla)'

        ELSE 'OK: Pasa filtros base de ObtenerProductosExcedidos (revisar excedente/presupuesto)'
    END AS DiagnosticoBase
FROM OrdenVenta ov
JOIN OrdenVentaProducto li ON ov.Id = li.PedidoId
LEFT JOIN ClienteSap  cli  ON ov.Cliente = cli.Cliente
LEFT JOIN Series      se   ON ov.Serie   = se.NombreSerie
WHERE ov.Consecutivo = @Consecutivo;



--🔁 2) Ver si realmente pasaría al baseLines del C#

--Esta segunda consulta es básicamente el baseLines de tu método, pero filtrado para una OV específica.


--Si esta consulta no regresa filas, ya sabes que alguna de esas condiciones está fallando.

--Con la Consulta 1 ves exactamente cuál y en qué línea.


DECLARE @Consecutivo VARCHAR(50) = 'OV-00001027';  -- <-- PON AQUÍ LA OV QUE QUIERES REVISAR


;WITH baseLines AS
(
    SELECT
        ov.Id              AS OrdenVentaId,
        ov.Consecutivo,
        ov.Cliente,
        cli.Nombrecliente  AS ClienteNombre,
        cli.U_CANAL        AS CanalCliente,
        se.Sucursal        AS SucursalSerie,
        se.Canal           AS CanalSerie,

        li.Id              AS LineaId,
        li.ProductoCodigo,
        li.ProductoNombre,
        li.Peso            AS KilosLinea,
        ov.FechaEntrega
    FROM OrdenVenta ov
    JOIN OrdenVentaProducto li ON ov.Id = li.PedidoId
    JOIN ClienteSap  cli       ON ov.Cliente = cli.Cliente
    JOIN Series      se        ON ov.Serie   = se.NombreSerie
    WHERE ov.Consecutivo = @Consecutivo
      AND ov.Estatus <> 0
      AND ov.Estatus  = 2
      AND ISNULL(ov.AutorizacionPresupuesto,0) = 0
      AND (li.Eliminado IS NULL OR li.Eliminado = 0)
      AND ISNULL(li.AutorizacionPresupuestoLinea,0) = 0
)
SELECT *
FROM baseLines;
