------PASO 1 -----

-- Detalle de líneas (base de verdad local)
CREATE TABLE dbo.sap_invoice_lines (
    id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    sap_doc_entry INT           NOT NULL,
    sap_line_num  INT           NOT NULL,
    card_code     NVARCHAR(50)  NOT NULL,
    sku           NVARCHAR(100) NOT NULL,
    kilos         DECIMAL(18,4) NOT NULL,
    doc_date      DATE          NOT NULL,
    last_seen_utc DATETIME2(0)  NOT NULL CONSTRAINT DF_sil_last_seen DEFAULT SYSUTCDATETIME(),
    is_deleted    BIT           NOT NULL CONSTRAINT DF_sil_isdel DEFAULT 0
);
CREATE UNIQUE INDEX UX_sil_doc_line ON dbo.sap_invoice_lines(sap_doc_entry, sap_line_num);
CREATE INDEX IX_sil_card_date ON dbo.sap_invoice_lines(card_code, doc_date);
CREATE INDEX IX_sil_sku_date  ON dbo.sap_invoice_lines(sku, doc_date);

-- Agregado mensual para consultas rápidas (lo que leerá la vista)
CREATE TABLE dbo.sales_monthly (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    card_code   NVARCHAR(50)  NOT NULL,
    sku         NVARCHAR(100) NOT NULL,
    [year]      INT           NOT NULL,
    [month]     INT           NOT NULL,  -- 1..12
    kilos       DECIMAL(18,4) NOT NULL,
    updated_utc DATETIME2(0)  NOT NULL CONSTRAINT DF_sm_updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UX_sm UNIQUE (card_code, sku, [year], [month])
);
CREATE INDEX IX_sm_card_ym ON dbo.sales_monthly(card_code, [year], [month]);
CREATE INDEX IX_sm_sku_ym  ON dbo.sales_monthly(sku, [year], [month]);



------PASO 2 -----

CREATE TYPE dbo.TvpInvoiceLine AS TABLE
(
    sap_doc_entry INT           NOT NULL,
    sap_line_num  INT           NOT NULL,
    card_code     NVARCHAR(50)  NOT NULL,
    sku           NVARCHAR(100) NOT NULL,
    kilos         DECIMAL(18,4) NOT NULL,
    doc_date      DATE          NOT NULL
);
GO



------PASO 3 -----

CREATE  PROCEDURE dbo.Invoices_UpsertAndRebuild
    @CardCode NVARCHAR(50),
    @Lote dbo.TvpInvoiceLine READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIME2(0) = SYSUTCDATETIME();

    ------------------------------------------------------------
    -- 1) UPSERT LÍNEAS
    ------------------------------------------------------------
    MERGE dbo.sap_invoice_lines AS t
    USING @Lote AS s
       ON t.sap_doc_entry = s.sap_doc_entry
      AND t.sap_line_num  = s.sap_line_num
    WHEN MATCHED THEN
        UPDATE SET
            t.card_code     = s.card_code,
            t.sku           = s.sku,
            t.kilos         = s.kilos,
            t.doc_date      = s.doc_date,
            t.is_deleted    = 0,
            t.last_seen_utc = @now
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (sap_doc_entry, sap_line_num, card_code, sku, kilos, doc_date, last_seen_utc, is_deleted)
        VALUES (s.sap_doc_entry, s.sap_line_num, s.card_code, s.sku, s.kilos, s.doc_date, @now, 0)
    -- Marca como borrado lo que ya no está en el lote (solo para ese cliente)
    WHEN NOT MATCHED BY SOURCE AND t.card_code = @CardCode THEN
        UPDATE SET t.is_deleted = 1, t.last_seen_utc = @now
    ;
    ------------------------------------------------------------

    ------------------------------------------------------------
    -- 2) REBUILD AGREGADO MENSUAL PARA ÚLTIMOS 24 MESES
    ------------------------------------------------------------
    DECLARE @from DATE = DATEFROMPARTS(YEAR(DATEADD(MONTH,-24,GETUTCDATE())), MONTH(DATEADD(MONTH,-24,GETUTCDATE())), 1);
    DECLARE @to   DATE = EOMONTH(GETUTCDATE()); -- fin de mes actual

    ;WITH agg AS
    (
        SELECT 
            card_code,
            sku,
            YEAR(doc_date) AS [year],
            MONTH(doc_date) AS [month],
            SUM(kilos) AS kilos
        FROM dbo.sap_invoice_lines
        WHERE is_deleted = 0
          AND card_code = @CardCode
          AND doc_date >= @from
          AND doc_date <= @to
        GROUP BY card_code, sku, YEAR(doc_date), MONTH(doc_date)
    )
    MERGE dbo.sales_monthly AS sm
    USING agg AS a
       ON sm.card_code = a.card_code
      AND sm.sku       = a.sku
      AND sm.[year]    = a.[year]
      AND sm.[month]   = a.[month]
    WHEN MATCHED THEN
        UPDATE SET sm.kilos = a.kilos, sm.updated_utc = @now
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (card_code, sku, [year], [month], kilos, updated_utc)
        VALUES (a.card_code, a.sku, a.[year], a.[month], a.kilos, @now)
    -- Limpieza de buckets que quedaron sin respaldo (en la ventana de 24 meses para ese cliente):
    WHEN NOT MATCHED BY SOURCE 
         AND sm.card_code = @CardCode
         AND (DATEFROMPARTS(sm.[year], sm.[month], 1) BETWEEN @from AND @to)
    THEN DELETE;
END
GO


    ------------------------------------------------------------
    -- CONSULTA
    ------------------------------------------------------------

SELECT 
    m.Card_Code,
    m.Sku,
    m.Year,
    m.Month,
    m.Kilos   AS KilosMensual,
    SUM(l.Kilos) AS KilosDetalle
FROM sales_monthly m
INNER JOIN sap_invoice_lines l
    ON m.Card_Code = l.Card_Code
   AND m.Sku = l.SKU
   AND m.Year = YEAR(l.Doc_Date)
   AND m.Month = MONTH(l.Doc_Date)
   where m.sku = 'N003'
GROUP BY 
    m.Card_Code, m.Sku, m.Year, m.Month, m.Kilos
ORDER BY 
    m.Card_Code, m.Sku, m.Year, m.Month;




	SELECT 
    m.Card_Code,
	b.Nombrecliente,
	b.U_CANAL,
    m.Sku,
    m.Year,
    m.Month,
    m.Kilos   AS KilosMensual,
    SUM(l.Kilos) AS KilosDetalle
FROM sales_monthly m
INNER JOIN sap_invoice_lines l
    ON m.Card_Code = l.Card_Code
   AND m.Sku = l.SKU
   AND m.Year = YEAR(l.Doc_Date)
   AND m.Month = MONTH(l.Doc_Date)
inner join ClienteSap b on m.card_code = b.Cliente
GROUP BY 
    m.Card_Code, m.Sku, m.Year, m.Month, m.Kilos,b.Nombrecliente,b.U_CANAL
ORDER BY 
    m.Card_Code, m.Sku, m.Year, m.Month;