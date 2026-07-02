SELECT        a.TransferenciaId,a.Consecutivo AS Pedido, a.Destino,d.ProductoCodigo, d.ProductoNombre, COUNT(c.CodigoEtiqueta) AS Cajas, SUM(ROUND(c.Kg, 2)) AS Peso, ISNULL(c.TarimaCodigo, N'Sin Tarima') AS Tarima, CONVERT(date, a.FechaSolicitud) 
                         AS Fecha
FROM            dbo.PedidosTransferencia AS a INNER JOIN
                         dbo.TransferenciaScanEtiqueta AS c ON a.TransferenciaId = c.TransferenciaId INNER JOIN
                         dbo.ArticuloSap AS d ON c.Sku = d.ProductoCodigo                         
GROUP BY d.ProductoCodigo, c.TarimaCodigo, d.ProductoNombre, a.Consecutivo, a.FechaSolicitud , a.Destino , a.TransferenciaId


