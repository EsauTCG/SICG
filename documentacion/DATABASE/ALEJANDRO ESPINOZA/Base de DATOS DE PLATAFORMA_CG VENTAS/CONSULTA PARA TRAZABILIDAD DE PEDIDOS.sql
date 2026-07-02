-- ================= VENDEDOR =================
WITH Vendedor AS (
    SELECT
        ov.Id          AS OrdenVentaId,
        ov.Consecutivo AS ConsecutivoOV,
        c.Cliente      AS ClienteId,
        c.Nombrecliente,
        ov.Vendedor,
        ovp.ProductoCodigo,
        ovp.ProductoNombre,
        ovp.Peso       AS Kg_Vendedor,
        ovp.Cajas      AS Cajas
    FROM OrdenVenta ov
    INNER JOIN OrdenVentaProducto ovp
        ON ov.Id = ovp.PedidoId
    INNER JOIN ClienteSap c
        ON ov.Cliente = c.Cliente
    WHERE ov.Estatus >= 5
),

-- ================= ADM. VENTAS =================
AdminVentas AS (
    SELECT
        pv.Id                     AS PedidoVentaId,
        pv.OrdenVentaConsecutivo,
        pvp.ProductoCodigo,
        pvp.ProductoNombre,
        pvp.KilosCaja             AS Kg_Admin,
        pvp.Cajas                 AS Cajas
    FROM PedidoVenta pv
    INNER JOIN PedidoVentaProducto pvp
        ON pv.Id = pvp.PedidoVentaId
    INNER JOIN OrdenVenta ov
        ON ov.Consecutivo = pv.OrdenVentaConsecutivo
    WHERE ov.Estatus >= 5
),

-- ================= SUBPEDIDOS =================
Subpedidos AS (
    SELECT
        sp.Id           AS SubpedidoId,
        sp.ConsecutivoOV,
        sp.U_DocMeat,
        sp.DocumentoSAP,
        spp.ProductoCodigo,
        spp.ProductoNombre,
        spp.KilosCaja   AS Kg_Subpedido,
        spp.Cajas       AS Cajas
    FROM Subpedido sp
    INNER JOIN SubpedidoProductos spp
        ON sp.Id = spp.SubpedidoId
    INNER JOIN OrdenVenta ov
        ON ov.Consecutivo = sp.ConsecutivoOV
    WHERE ov.Estatus >= 5
),

-- ================= MEAT (TABLAS LOCALES) =================
Meat AS (
    SELECT
        se.SolicitudSurtidoId,
        sd.Articulo COLLATE Modern_Spanish_CI_AS AS ProductoCodigo,
        SUM(sd.Kg)                               AS Kg_Meat,
        COUNT(*)                                 AS Cajas
    FROM surtidoencabezado se
    INNER JOIN surtidodetalle sd
        ON se.SolicitudSurtidoId = sd.SolicitudSurtidoId
    WHERE se.SolicitudSurtidoId IN (
        SELECT DISTINCT sp.U_DocMeat
        FROM Subpedido sp
        INNER JOIN OrdenVenta ov
            ON ov.Consecutivo = sp.ConsecutivoOV
        WHERE ov.Estatus >= 5
    )
    GROUP BY
        se.SolicitudSurtidoId,
        sd.Articulo COLLATE Modern_Spanish_CI_AS
)

-- ================= CONSOLIDADO =================
SELECT
    sb.ConsecutivoOV,
    v.ClienteId,
    v.Nombrecliente AS NombreCliente,
    v.Vendedor,

    -- Producto
    COALESCE(
        v.ProductoCodigo,
        av.ProductoCodigo,
        sb.ProductoCodigo,
        m.ProductoCodigo
    ) COLLATE Modern_Spanish_CI_AS  AS ProductoCodigo,

    COALESCE(
        v.ProductoNombre,
        av.ProductoNombre,
        sb.ProductoNombre
    ) COLLATE Modern_Spanish_CI_AS  AS ProductoNombre,

    -- Kilos en cada “versión”
    v.Kg_Vendedor,
    av.Kg_Admin,
    sb.Kg_Subpedido,
    m.Kg_Meat,

    -- Cajas en cada “versión”
    v.Cajas      AS Cajas_Vendedor,
    av.Cajas     AS Cajas_Admin,
    sb.Cajas     AS Cajas_Subpedido,
    m.Cajas      AS Cajas_Meat,

    -- Llave MEAT
    sb.U_DocMeat AS DocMeat
    -- ,m.SolicitudSurtidoId AS SolicitudSurtidoId   -- si quieres verla, descomenta

FROM Subpedidos sb
LEFT JOIN Vendedor v
    ON  v.ConsecutivoOV = sb.ConsecutivoOV
    AND v.ProductoCodigo COLLATE Modern_Spanish_CI_AS =
        sb.ProductoCodigo COLLATE Modern_Spanish_CI_AS
LEFT JOIN AdminVentas av
    ON  av.OrdenVentaConsecutivo = sb.ConsecutivoOV
    AND av.ProductoCodigo COLLATE Modern_Spanish_CI_AS =
        sb.ProductoCodigo COLLATE Modern_Spanish_CI_AS
LEFT JOIN Meat m
    ON  m.SolicitudSurtidoId = sb.U_DocMeat
    AND m.ProductoCodigo COLLATE Modern_Spanish_CI_AS =
        sb.ProductoCodigo COLLATE Modern_Spanish_CI_AS
ORDER BY
    sb.ConsecutivoOV;
