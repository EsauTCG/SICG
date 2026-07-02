/* ============================================================
   SKU CONVERSION - TODO AJUSTADO A dbo.ArticuloSap.ProductoCodigo
   (Tu ArticuloSap.ProductoCodigo = NVARCHAR(50) porque max_length=100 bytes)
   Collation: Modern_Spanish_CI_AS
   ============================================================ */

-- (Opcional) Verifica tipo real del catálogo
SELECT 
  'ArticuloSap.ProductoCodigo' AS Col,
  t.name AS Tipo,
  c.max_length,
  c.collation_name
FROM sys.columns c
JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.ArticuloSap')
  AND c.name = 'ProductoCodigo';
GO


/* ============================================================
   1) BORRAR OBJETOS SI YA EXISTEN (seguro para re-ejecutar)
   ============================================================ */
IF OBJECT_ID('dbo.sp_SkuConversion_AddPair', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_SkuConversion_AddPair;
GO

IF OBJECT_ID('dbo.sp_SkuConversion_GetDestinos', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_SkuConversion_GetDestinos;
GO

IF OBJECT_ID('dbo.sp_SkuConversion_GetOrigenes', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_SkuConversion_GetOrigenes;
GO

IF OBJECT_ID('dbo.SkuConversion', 'U') IS NOT NULL
    DROP TABLE dbo.SkuConversion;
GO


/* ============================================================
   2) CREAR TABLA (AJUSTADA: NVARCHAR(50))
   ============================================================ */
CREATE TABLE dbo.SkuConversion
(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,

    -- ✅ Debe coincidir EXACTO con ArticuloSap.ProductoCodigo (nvarchar(50))
    SkuOrigen  nvarchar(50) COLLATE Modern_Spanish_CI_AS NOT NULL,
    SkuDestino nvarchar(50) COLLATE Modern_Spanish_CI_AS NOT NULL,

    Factor decimal(18,6) NULL,
    Prioridad int NULL,
    Motivo nvarchar(150) NULL,
    Activo bit NOT NULL CONSTRAINT DF_SkuConversion_Activo DEFAULT(1),

    FechaAlta datetime2(0) NOT NULL CONSTRAINT DF_SkuConversion_Fecha DEFAULT(sysdatetime()),
    UsuarioAlta nvarchar(80) NULL,

    CONSTRAINT UQ_SkuConversion UNIQUE (SkuOrigen, SkuDestino),

    CONSTRAINT FK_SkuConversion_Origen
        FOREIGN KEY (SkuOrigen) REFERENCES dbo.ArticuloSap(ProductoCodigo),

    CONSTRAINT FK_SkuConversion_Destino
        FOREIGN KEY (SkuDestino) REFERENCES dbo.ArticuloSap(ProductoCodigo)
);
GO


/* ============================================================
   3) ÍNDICES
   ============================================================ */
CREATE INDEX IX_SkuConversion_Origen
ON dbo.SkuConversion(SkuOrigen)
INCLUDE (SkuDestino, Activo, Prioridad, Factor);
GO

CREATE INDEX IX_SkuConversion_Destino
ON dbo.SkuConversion(SkuDestino)
INCLUDE (SkuOrigen, Activo);
GO


/* ============================================================
   4) STORED PROCEDURES (AJUSTADAS A NVARCHAR(50))
   ============================================================ */

CREATE PROCEDURE dbo.sp_SkuConversion_AddPair
    @SkuA nvarchar(50),
    @SkuB nvarchar(50),
    @Factor decimal(18,6) = NULL,
    @Prioridad int = NULL,
    @Motivo nvarchar(150) = NULL,
    @Usuario nvarchar(80) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Usuario IS NULL SET @Usuario = SUSER_SNAME();

    -- Inserta A -> B (incluye A->A si mandas iguales)
    MERGE dbo.SkuConversion AS t
    USING (SELECT @SkuA AS Origen, @SkuB AS Destino) s
    ON t.SkuOrigen = s.Origen AND t.SkuDestino = s.Destino
    WHEN NOT MATCHED THEN
        INSERT (SkuOrigen, SkuDestino, Factor, Prioridad, Motivo, Activo, UsuarioAlta)
        VALUES (s.Origen, s.Destino, @Factor, @Prioridad, @Motivo, 1, @Usuario);

    -- Si son diferentes, también inserta el reverso B -> A
    IF @SkuA <> @SkuB
    BEGIN
        MERGE dbo.SkuConversion AS t
        USING (SELECT @SkuB AS Origen, @SkuA AS Destino) s
        ON t.SkuOrigen = s.Origen AND t.SkuDestino = s.Destino
        WHEN NOT MATCHED THEN
            INSERT (SkuOrigen, SkuDestino, Factor, Prioridad, Motivo, Activo, UsuarioAlta)
            VALUES (s.Origen, s.Destino, @Factor, @Prioridad, @Motivo, 1, @Usuario);
    END
END;
GO


CREATE PROCEDURE dbo.sp_SkuConversion_GetDestinos
    @SkuOrigen nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.SkuDestino,
        a.ProductoNombre,
        c.Factor,
        c.Prioridad,
        c.Motivo
    FROM dbo.SkuConversion c
    JOIN dbo.ArticuloSap a
        ON a.ProductoCodigo = c.SkuDestino
    WHERE c.SkuOrigen = @SkuOrigen
      AND c.Activo = 1
    ORDER BY ISNULL(c.Prioridad, 999), c.SkuDestino;
END;
GO


CREATE PROCEDURE dbo.sp_SkuConversion_GetOrigenes
    @SkuDestino nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.SkuOrigen,
        a.ProductoNombre,
        c.Factor,
        c.Prioridad
    FROM dbo.SkuConversion c
    JOIN dbo.ArticuloSap a
        ON a.ProductoCodigo = c.SkuOrigen
    WHERE c.SkuDestino = @SkuDestino
      AND c.Activo = 1
    ORDER BY ISNULL(c.Prioridad, 999), c.SkuOrigen;
END;
GO


/* ============================================================
   5) EJEMPLOS DE USO (INSERT / CONSULTA)
   ============================================================ */

-- A) Insertar pareja bidireccional (A<->B)
-- EXEC dbo.sp_SkuConversion_AddPair @SkuA=N'V005', @SkuB=N'V051', @Prioridad=1, @Motivo=N'Conversión permitida';

-- B) Insertar A -> A (se puede convertir en sí mismo)
-- EXEC dbo.sp_SkuConversion_AddPair @SkuA=N'N137', @SkuB=N'N137', @Prioridad=0, @Motivo=N'Mismo SKU';

-- C) Ver destinos disponibles para un SKU
-- EXEC dbo.sp_SkuConversion_GetDestinos @SkuOrigen=N'V005';

-- D) Ver orígenes que pueden convertirse a un destino
-- EXEC dbo.sp_SkuConversion_GetOrigenes @SkuDestino=N'V051';
