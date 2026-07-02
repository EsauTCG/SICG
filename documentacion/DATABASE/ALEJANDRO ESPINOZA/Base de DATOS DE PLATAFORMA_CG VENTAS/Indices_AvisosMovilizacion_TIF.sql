/*
================================================================================
 Script: Indices_AvisosMovilizacion_TIF.sql
 Objetivo: Mejorar rendimiento de dbo.vw_AvisosMovilizacion_TIF
 Base principal: TIF_MEAT
 Base catálogo:   TIF_CommerciaNET

 IMPORTANTE:
 1) Ejecutar en una ventana de mantenimiento si las tablas son grandes.
 2) Validar espacio disponible en disco/log antes de ejecutar.
 3) Si algún índice ya existe con otro nombre, SQL Server puede indicar que ya
    existe un índice equivalente; en ese caso revisar antes de duplicarlo.
 4) Después de ejecutar, probar:
       SELECT *
       FROM CadenaMeatTIF.dbo.vw_AvisosMovilizacion_TIF
       WHERE solicitud_surtido_id = 17079;
================================================================================
*/

SET NOCOUNT ON;
GO

/* =============================================================================
   BASE: TIF_MEAT
============================================================================= */

USE TIF_MEAT;
GO

PRINT 'Creando índices para TIF_MEAT...';
GO

/* -----------------------------------------------------------------------------
   1) SalidaEmbarque
   Uso en vista:
   TIF_MEAT.dbo.SalidaEmbarque se
   ON se.SolicitudSurtidoId = ss.SolicitudSurtidoId

   También aporta ProduccionId para encadenar contra Produccion.
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_SalidaEmbarque_SolicitudSurtidoId_ProduccionId'
      AND object_id = OBJECT_ID('dbo.SalidaEmbarque')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_SalidaEmbarque_SolicitudSurtidoId_ProduccionId
    ON dbo.SalidaEmbarque (SolicitudSurtidoId, ProduccionId);
END
GO


/* -----------------------------------------------------------------------------
   2) SolicitudSurtido
   Uso en vista:
   TIF_MEAT.dbo.SolicitudSurtido ss
   JOIN por SolicitudSurtidoId
   Lee FechaSurtido para FechaVenta.
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_SolicitudSurtido_SolicitudSurtidoId_FechaSurtido'
      AND object_id = OBJECT_ID('dbo.SolicitudSurtido')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_SolicitudSurtido_SolicitudSurtidoId_FechaSurtido
    ON dbo.SolicitudSurtido (SolicitudSurtidoId)
    INCLUDE (FechaSurtido);
END
GO


/* -----------------------------------------------------------------------------
   3) SurtidoReferencia
   Uso en vista:
   LEFT JOIN TIF_MEAT.dbo.SurtidoReferencia sr
     ON sr.SolicitudSurtidoId = ss.SolicitudSurtidoId
    AND sr.TipoReferenciaId IN (6, 9)

   Lee Referencia para venta y cliente.
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_SurtidoReferencia_Solicitud_Tipo'
      AND object_id = OBJECT_ID('dbo.SurtidoReferencia')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_SurtidoReferencia_Solicitud_Tipo
    ON dbo.SurtidoReferencia (SolicitudSurtidoId, TipoReferenciaId)
    INCLUDE (Referencia);
END
GO


/* -----------------------------------------------------------------------------
   4) Produccion
   Uso en vista:
   TIF_MEAT.dbo.Produccion pr
   JOIN por ProduccionId.
   Lee Articulo, FechaProduccion, PesoNeto, LoteId, CodigoEtiqueta.
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Produccion_ProduccionId_Avisos'
      AND object_id = OBJECT_ID('dbo.Produccion')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Produccion_ProduccionId_Avisos
    ON dbo.Produccion (ProduccionId)
    INCLUDE (Articulo, FechaProduccion, PesoNeto, LoteId, CodigoEtiqueta);
END
GO


/* -----------------------------------------------------------------------------
   5) ProduccionEtiquetacionLog
   Uso en vista:
   TIF_MEAT.dbo.ProduccionEtiquetacionLog pel
   JOIN por ProduccionId.
   ROW_NUMBER por CodigoEtiqueta ordenando FechaHoraEvento DESC.

   Índice filtrado porque la vista usa:
   WHERE pel.CodigoEtiqueta IS NOT NULL
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_ProduccionEtiquetacionLog_Produccion_Codigo_Fecha'
      AND object_id = OBJECT_ID('dbo.ProduccionEtiquetacionLog')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_ProduccionEtiquetacionLog_Produccion_Codigo_Fecha
    ON dbo.ProduccionEtiquetacionLog (ProduccionId, CodigoEtiqueta, FechaHoraEvento DESC)
    INCLUDE (EtiquetacionId)
    WHERE CodigoEtiqueta IS NOT NULL;
END
GO


/* -----------------------------------------------------------------------------
   6) Caja
   Uso en vista:
   OUTER APPLY contra TIF_MEAT.dbo.Caja c
   WHERE c.ProduccionId = pt.ProduccionId
   MAX(c.FechaCaducidad)
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Caja_ProduccionId_FechaCaducidad'
      AND object_id = OBJECT_ID('dbo.Caja')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Caja_ProduccionId_FechaCaducidad
    ON dbo.Caja (ProduccionId)
    INCLUDE (FechaCaducidad);
END
GO


/* -----------------------------------------------------------------------------
   7) SolicitudReferencia
   Uso en vista:
   OUTER APPLY contra TIF_MEAT.dbo.SolicitudReferencia sr
   WHERE sr.SolicitudProduccionId = pt.LoteId
     AND sr.TipoReferenciaId = 47

   Lee Referencia para FechaSacrificio.
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_SolicitudReferencia_SolicitudProduccion_Tipo'
      AND object_id = OBJECT_ID('dbo.SolicitudReferencia')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_SolicitudReferencia_SolicitudProduccion_Tipo
    ON dbo.SolicitudReferencia (SolicitudProduccionId, TipoReferenciaId)
    INCLUDE (Referencia);
END
GO


/* -----------------------------------------------------------------------------
   8) ProduccionLogistica
   Uso en vista:
   OUTER APPLY contra TIF_MEAT.dbo.ProduccionLogistica
   WHERE pl.SolicitudProduccionId = pt.LoteId

   También se usa varias veces en la cadena de canal/caja/retazo.
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_ProduccionLogistica_SolicitudProduccion_Produccion'
      AND object_id = OBJECT_ID('dbo.ProduccionLogistica')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_ProduccionLogistica_SolicitudProduccion_Produccion
    ON dbo.ProduccionLogistica (SolicitudProduccionId, ProduccionId);
END
GO


/* -----------------------------------------------------------------------------
   9) Lote por LoteId
   Uso en vista:
   LEFT JOIN TIF_MEAT.dbo.Lote l
   ON l.LoteId = pt.LoteId

   Lee Nombre y TipoLoteId.
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Lote_LoteId_Avisos'
      AND object_id = OBJECT_ID('dbo.Lote')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Lote_LoteId_Avisos
    ON dbo.Lote (LoteId)
    INCLUDE (Nombre, TipoLoteId);
END
GO


/* -----------------------------------------------------------------------------
   10) Lote por Nombre
   Uso en vista:
   EXISTS contra Lote:
   WHERE lRet.LoteId = pt.LoteId
     AND lRet.Nombre LIKE N'RETT%'

   Este índice ayuda si se consulta Nombre con patrón inicial RETT%.
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Lote_Nombre_LoteId_TipoLoteId'
      AND object_id = OBJECT_ID('dbo.Lote')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Lote_Nombre_LoteId_TipoLoteId
    ON dbo.Lote (Nombre, LoteId)
    INCLUDE (TipoLoteId);
END
GO


/* =============================================================================
   ACTUALIZAR ESTADÍSTICAS TIF_MEAT
============================================================================= */

PRINT 'Actualizando estadísticas en TIF_MEAT...';
GO

UPDATE STATISTICS dbo.SolicitudSurtido;
UPDATE STATISTICS dbo.SalidaEmbarque;
UPDATE STATISTICS dbo.SurtidoReferencia;
UPDATE STATISTICS dbo.Produccion;
UPDATE STATISTICS dbo.ProduccionEtiquetacionLog;
UPDATE STATISTICS dbo.Caja;
UPDATE STATISTICS dbo.SolicitudReferencia;
UPDATE STATISTICS dbo.ProduccionLogistica;
UPDATE STATISTICS dbo.Lote;
GO


/* =============================================================================
   BASE: TIF_CommerciaNET
============================================================================= */

USE TIF_CommerciaNET;
GO

PRINT 'Creando índices para TIF_CommerciaNET...';
GO

/* -----------------------------------------------------------------------------
   11) Articulo
   Uso en vista:
   INNER JOIN TIF_CommerciaNET.dbo.Articulo art
   ON pt.SKU = art.ArticuloId

   Lee Nombre.
----------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Articulo_ArticuloId_Nombre'
      AND object_id = OBJECT_ID('dbo.Articulo')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Articulo_ArticuloId_Nombre
    ON dbo.Articulo (ArticuloId)
    INCLUDE (Nombre);
END
GO

PRINT 'Actualizando estadísticas en TIF_CommerciaNET...';
GO

UPDATE STATISTICS dbo.Articulo;
GO


/* =============================================================================
   PRUEBA SUGERIDA
   Cambia el ID 17079 por una solicitud real que esté lenta.
============================================================================= */

-- USE CadenaMeatTIF;
-- GO
--
-- SET STATISTICS IO ON;
-- SET STATISTICS TIME ON;
--
-- SELECT *
-- FROM dbo.vw_AvisosMovilizacion_TIF
-- WHERE solicitud_surtido_id = 17079;
--
-- SET STATISTICS IO OFF;
-- SET STATISTICS TIME OFF;
-- GO


/* =============================================================================
   RECOMENDACIÓN ADICIONAL EN C#
   Evitar:
       WHERE CONVERT(nvarchar(50), solicitud_surtido_id) IN @Solicitudes
   Usar:
       WHERE solicitud_surtido_id IN @Solicitudes
   y pasar @Solicitudes como List<int>.
============================================================================= */

PRINT 'Script finalizado.';
GO
