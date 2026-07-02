SELECT *
FROM Embarque
WHERE Consecutivo = 'EMB-00001002';

--

SELECT 
    e.Consecutivo,
    eo.Id AS EmbarqueOrdenId,
    eo.OrdenId
FROM Embarque e
JOIN EmbarqueOrdenes eo ON eo.EmbarqueId = e.Id
WHERE e.Consecutivo = 'EMB-00001002';

--


SELECT 
    e.Consecutivo,
    qr.Token,
    qr.Estado,
    qr.FechaGeneracion,
    qr.FechaValidacion
FROM Embarque e
JOIN EmbarqueQR qr ON qr.EmbarqueId = e.Id
WHERE e.Consecutivo = 'EMB-00001002';


--


SELECT 
    e.Consecutivo,
    e.FechaCreacion,
    e.UsuarioGenera,
    e.Estatus,
    eo.OrdenId,
    qr.Token,
    qr.Estado
FROM Embarque e
LEFT JOIN EmbarqueOrdenes eo ON eo.EmbarqueId = e.Id
LEFT JOIN EmbarqueQR qr ON qr.EmbarqueId = e.Id
WHERE e.Consecutivo = 'EMB-00001002';


-- VISTA --

CREATE VIEW VW_EmbarqueCompleto
AS
SELECT 
    e.Id AS EmbarqueId,
    e.Consecutivo,
    e.FechaCreacion,
    e.UsuarioGenera,
    e.Estatus,
    eo.OrdenId,
    qr.Token,
    qr.Estado,
    qr.FechaGeneracion,
    qr.FechaValidacion
FROM Embarque e
LEFT JOIN EmbarqueOrdenes eo ON eo.EmbarqueId = e.Id
LEFT JOIN EmbarqueQR qr ON qr.EmbarqueId = e.Id;


SELECT *
FROM VW_EmbarqueCompleto
WHERE Consecutivo = 'EMB-00001004';

