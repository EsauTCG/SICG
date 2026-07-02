SELECT 
    COUNT(DISTINCT PRICELISTNAME) AS TotalListasPrecios
FROM CatalogoPrecioSap;

SELECT DISTINCT
    PriceListNum,
    PriceListName
FROM CatalogoPrecioSap
ORDER BY PriceListNum;


CREATE TABLE ListaPreciosSap (
    PriceListNum INT NOT NULL PRIMARY KEY,
    PriceListName VARCHAR(100) NOT NULL,
    Activo BIT NOT NULL DEFAULT 1,
    FechaCreacion DATETIME NOT NULL DEFAULT GETDATE()
);

IF COL_LENGTH('dbo.ListaPreciosSap', 'Factor') IS NULL
BEGIN
    ALTER TABLE dbo.ListaPreciosSap
    ADD Factor DECIMAL(18,4) NOT NULL
        CONSTRAINT DF_ListaPreciosSap_Factor DEFAULT (0);
END;
GO

INSERT INTO ListaPreciosSap (PriceListNum, PriceListName)
SELECT DISTINCT
    PriceListNum,
    PriceListName
FROM CatalogoPrecioSap
ORDER BY PriceListNum;

SELECT *
FROM ListaPreciosSap
ORDER BY PriceListNum;