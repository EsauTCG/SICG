-- ===============================================
-- Carga masiva de conversiones desde Excel
-- Generado automáticamente
-- Incluye conversión a sí mismo (Prioridad=0)
-- ===============================================

SET NOCOUNT ON;

DECLARE @Motivo nvarchar(150) = N'Catálogo inicial (Excel)';

IF OBJECT_ID('tempdb..#Conv') IS NOT NULL DROP TABLE #Conv;
CREATE TABLE #Conv (
    SkuOrigen  nvarchar(50) NOT NULL,
    SkuDestino nvarchar(50) NOT NULL,
    Prioridad int NULL
);

INSERT INTO #Conv (SkuOrigen, SkuDestino, Prioridad) VALUES
(N'D023',N'D023',0),(N'D023',N'Y010',1),(N'D027',N'D027',0),(N'D027',N'D031',1),(N'D031',N'D023',1),(N'D031',N'D031',0),(N'D033',N'D027',1),(N'D033',N'D033',0),(N'N023',N'N023',0),(N'N023',N'Y014',2),(N'N023',N'Y049',1),(N'N036',N'N036',0),(N'N036',N'V117',2),(N'N036',N'Y015',1),(N'N041',N'N041',0),(N'N049',N'N049',0),(N'N049',N'S104',2),(N'N049',N'Y018',1),(N'N057',N'N057',0),(N'N057',N'Y013',1),(N'N096',N'N096',0),(N'N096',N'V011',3),(N'N096',N'V122',2),(N'N096',N'V186',1),(N'N097',N'N097',0),(N'N097',N'V020',1),(N'N099',N'N099',0),(N'N099',N'V027',1),(N'N137',N'N136',1),(N'N137',N'N137',0),(N'N140',N'N135',1),(N'N140',N'N140',0),(N'S069',N'D033',1),(N'S069',N'S069',0),(N'S070',N'S069',1),(N'S070',N'S070',0),(N'S081',N'S070',1),(N'S081',N'S081',0),(N'S104',N'S081',1),(N'S104',N'S104',0),(N'V001',N'V001',0),(N'V001',N'V311',1),(N'V005',N'V005',0),(N'V005',N'V014',2),(N'V005',N'V301',1),(N'V013',N'V013',0),(N'V013',N'V039',1),(N'V013',N'V051',3),(N'V013',N'V303',2),(N'V017',N'V013',2),(N'V017',N'V017',0),(N'V017',N'V307',1),(N'V019',N'V006',1),(N'V019',N'V019',0),(N'V019',N'V209',2),(N'V020',N'V020',0),(N'V020',N'V163',3),(N'V020',N'V304',1),(N'V020',N'Y013',2),(N'V024',N'V008',1),(N'V024',N'V024',0),(N'V024',N'V306',2),(N'V027',N'V027',0),(N'V027',N'V034',1),(N'V028',N'V010',1),(N'V028',N'V028',0),(N'V028',N'V302',2),(N'V029',N'V029',0),(N'V029',N'V032',1),(N'V030',N'V030',0),(N'V031',N'N057',3),(N'V031',N'V031',0),(N'V031',N'V122',1),(N'V031',N'V149',2),(N'V044',N'V044',0),(N'V044',N'V309',1),(N'V044',N'Y064',2),(N'V045',N'V045',0),(N'V045',N'V052',1),(N'V045',N'V308',3),(N'V045',N'Y065',2),(N'V051',N'V051',0),(N'V051',N'V139',1),(N'V054',N'V054',0),(N'V054',N'V310',1),(N'V054',N'Y063',2),(N'V059',N'V059',0),(N'V068',N'V068',0),(N'V069',N'V069',0),(N'V069',N'V122',2),(N'V069',N'Y026',1),(N'V071',N'N023',2),(N'V071',N'V071',0),(N'V071',N'V181',1),(N'V074',N'V074',0),(N'V076',N'V031',1),(N'V076',N'V076',0),(N'V076',N'V122',2),(N'V076',N'V149',3),(N'V077',N'V071',2),(N'V077',N'V077',0),(N'V077',N'V078',1),(N'V087',N'V087',0),(N'V093',N'V093',0),(N'V101',N'V101',0),(N'V117',N'N049',4),(N'V117',N'V031',1),(N'V117',N'V117',0),(N'V117',N'V122',2),(N'V117',N'Y082',3),(N'V127',N'N108',2),(N'V127',N'V003',1),(N'V127',N'V127',0),(N'V127',N'V305',3),(N'V127',N'V401',4),(N'V139',N'N097',1),(N'V139',N'V139',0),(N'V144',N'V033',1),(N'V144',N'V071',2),(N'V144',N'V144',0),(N'V146',N'V146',0),(N'V146',N'Y022',1),(N'V161',N'N036',1),(N'V161',N'V161',0),(N'V163',N'V146',1),(N'V163',N'V163',0),(N'V167',N'V086',1),(N'V167',N'V167',0),(N'V172',N'N099',1),(N'V172',N'V172',0),(N'V182',N'V182',0),(N'Y010',N'V172',1),(N'Y010',N'Y010',0),(N'Y013',N'V031',1),(N'Y013',N'V122',2),(N'Y013',N'V161',3),(N'Y013',N'Y013',0),(N'Y014',N'V076',1),(N'Y014',N'Y014',0),(N'Y022',N'V071',1),(N'Y022',N'Y022',0);

-- Validación: SKUs que NO existen en ArticuloSap (si sale algo, corrige catálogo)
SELECT DISTINCT x.Sku
FROM (
    SELECT SkuOrigen AS Sku FROM #Conv
    UNION ALL
    SELECT SkuDestino AS Sku FROM #Conv
) x
WHERE NOT EXISTS (SELECT 1 FROM dbo.ArticuloSap a WHERE a.ProductoCodigo = x.Sku);

-- Inserción: solo agrega los que no existan (respeta UNIQUE)
MERGE dbo.SkuConversion AS t
USING (SELECT SkuOrigen, SkuDestino, Prioridad FROM #Conv) AS s
ON t.SkuOrigen = s.SkuOrigen AND t.SkuDestino = s.SkuDestino
WHEN NOT MATCHED THEN
    INSERT (SkuOrigen, SkuDestino, Prioridad, Motivo, Activo, UsuarioAlta)
    VALUES (s.SkuOrigen, s.SkuDestino, NULLIF(s.Prioridad,0), @Motivo, 1, SUSER_SNAME());

-- (Opcional) Si quieres actualizar Prioridad/Motivo cuando estén en NULL:
-- UPDATE t
-- SET t.Prioridad = COALESCE(t.Prioridad, NULLIF(s.Prioridad,0)),
--     t.Motivo    = COALESCE(t.Motivo, @Motivo)
-- FROM dbo.SkuConversion t
-- JOIN #Conv s ON s.SkuOrigen=t.SkuOrigen AND s.SkuDestino=t.SkuDestino;

-- Limpieza
DROP TABLE #Conv;