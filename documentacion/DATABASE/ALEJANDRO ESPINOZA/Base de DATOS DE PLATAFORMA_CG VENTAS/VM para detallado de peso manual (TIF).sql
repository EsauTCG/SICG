CREATE OR ALTER VIEW dbo.vw_PesoLote_Detallado
AS
SELECT
    b.ProduccionId,
    b.Articulo,
    d.Nombre AS NombreArticulo,
    b.LoteId,
    c.Nombre AS NombreLote,
    ISNULL(a.Nombre,'-') AS Proceso,
    ISNULL(a.UsuarioNombreLogIn,'-') AS Solicitante,
    ISNULL(a.UsuarioNombre,'-') AS Autoriza,
    ISNULL(a.Estacion,'-') AS Estacion,
    ISNULL(a.Accion,'-') AS Accion,
    ISNULL(a.ValorAnterior,0) AS ValorAnterior,
    ISNULL(a.ValorActual,0) AS ValorActual,
    b.FechaProduccion,
    a.FechaHora AS FechaSolicitud,
    CASE
        WHEN a.FechaHora IS NOT NULL THEN 'PESO MANUAL'
        ELSE 'PESO AUTOMATICO'
    END AS TipoPeso
FROM Produccion b
LEFT JOIN AUD_ProduccionCaracteristica a
    ON a.ProduccionId = b.ProduccionId
INNER JOIN Lote c
    ON b.LoteId = c.LoteId
INNER JOIN TIF_CommerciaNET.dbo.Articulo d
    ON b.Articulo = d.ArticuloId;
GO
