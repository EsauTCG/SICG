--ENCABEZADO
WITH base AS (
    SELECT
        c.LoteId,
        c.Nombre AS NombreLote,

        EventTime = COALESCE(a.FechaHora, b.FechaProduccion),

        Tipo = CASE 
                    WHEN a.FechaHora IS NULL THEN 'AUTOMATICO' 
                    ELSE 'MANUAL' 
               END,

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
    WHERE c.LoteId = 61108
),
marcado AS (
    SELECT *,
        Cambio = CASE 
                    WHEN LAG(Tipo) OVER (PARTITION BY LoteId ORDER BY EventTime) = Tipo 
                    THEN 0 ELSE 1 
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
    NombreLote,
    Tipo,
    Desde = MIN(EventTime),
    Hasta = MAX(EventTime),

    Proceso     = COALESCE(MAX(Proceso), '-'),
    Solicitante = COALESCE(MAX(Solicitante), '-'),
    Autoriza    = COALESCE(MAX(Autoriza), '-'),
    Estacion    = COALESCE(MAX(CONVERT(varchar(100), Estacion)), '-'),
    Accion      = COALESCE(MAX(Accion), '-')
FROM grupos
GROUP BY
    LoteId, NombreLote, Tipo, Grupo
ORDER BY Desde;






--DETALLADO
select
    b.ProduccionId,
    b.Articulo,
    d.Nombre,
    b.LoteId,
    c.Nombre as Lote,
    isnull(a.Nombre,'-') as Proceso,
    isnull(a.UsuarioNombreLogIn,'-') as Solicitante,
    isnull(a.UsuarioNombre,'-') as Autoriza,   
    isnull(a.Estacion,'-') as Estacion,
    isnull(a.Accion,'-') as Accion,
    isnull(a.ValorAnterior,0) as ValorAnterior,
    isnull(a.ValorActual,0)as ValorActual,
    b.FechaProduccion,
    a.FechaHora as FechaSolicitud,
    case 
        when a.FechaHora is not null then 'PESO MANUAL'
        else 'PESO AUTOMATICO'
    end as TipoPeso
from Produccion b
left join AUD_ProduccionCaracteristica a 
    on a.ProduccionId = b.ProduccionId
inner join Lote c 
    on b.LoteId = c.LoteId
inner join TIF_CommerciaNET.dbo.Articulo d 
    on b.Articulo = d.ArticuloId
where c.LoteId = 61108
order by b.FechaProduccion, a.FechaHora;
