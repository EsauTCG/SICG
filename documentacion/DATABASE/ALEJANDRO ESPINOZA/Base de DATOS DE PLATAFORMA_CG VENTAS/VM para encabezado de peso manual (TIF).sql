CREATE  VIEW dbo.vw_PesoLote_Encabezado
AS
WITH base AS (
    SELECT
        c.LoteId,
        c.Nombre AS NombreLote,
        b.FechaProduccion,
        EventTime = COALESCE(a.FechaHora, b.FechaProduccion),
        Tipo = CASE WHEN a.FechaHora IS NULL THEN 'AUTOMATICO' ELSE 'MANUAL' END,
        Proceso     = a.Nombre,
        Solicitante = a.UsuarioNombreLogIn,
        Autoriza    = a.UsuarioNombre,
        Estacion    = a.Estacion,
        Accion      = a.Accion
    FROM Produccion b
    LEFT JOIN AUD_ProduccionCaracteristica a
        ON a.ProduccionId = b.ProduccionId
    INNER JOIN Lote c
        ON b.LoteId = c.LoteId
),
marcado AS (
    SELECT *,
        Cambio = CASE
                    WHEN LAG(Tipo) OVER (PARTITION BY LoteId ORDER BY EventTime) = Tipo THEN 0
                    ELSE 1
                 END
    FROM base
),
grupos AS (
    SELECT *,
        Grupo = SUM(Cambio) OVER (
                    PARTITION BY LoteId
                    ORDER BY EventTime
                    ROWS UNBOUNDED PRECEDING
                )
    FROM marcado
)
SELECT
    LoteId,
    NombreLote,
    Tipo,
    Desde = MIN(EventTime),
    Hasta = MAX(EventTime),
    FechaProduccionMin = MIN(FechaProduccion),
    FechaProduccionMax = MAX(FechaProduccion),
    Proceso     = COALESCE(MAX(Proceso), '-'),
    Solicitante = COALESCE(MAX(Solicitante), '-'),
    Autoriza    = COALESCE(MAX(Autoriza), '-'),
    Estacion    = COALESCE(MAX(CONVERT(varchar(100), Estacion)), '-'),
    Accion      = COALESCE(MAX(Accion), '-')
FROM grupos
GROUP BY LoteId, NombreLote, Tipo, Grupo;
GO
