-- ============================================================
-- TABLA: ReglaComercial
-- Guarda la matriz de descuentos (Demanda x Canal)
-- null en DescuentoMonto = NO VENDER
-- 0 en DescuentoMonto   = SIN DESCUENTO
-- >0 en DescuentoMonto  = descuento permitido en pesos
-- ============================================================

CREATE TABLE [dbo].[ReglaComercial] (
    [Id]              INT              NOT NULL IDENTITY(1,1),
    [Demanda]         NVARCHAR(10)     NOT NULL,   -- 'BAJA' | 'MEDIA' | 'ALTA'
    [Canal]           NVARCHAR(20)     NOT NULL,   -- 'SPOT' | 'ACTIVO' | 'ESTRAT\u00c9GICO'
    [DescuentoMonto]  DECIMAL(18,2)    NULL,       -- NULL = NO VENDER
    [FechaModificacion] DATETIME2(0)   NOT NULL DEFAULT GETDATE(),
    [ModificadoPor]   NVARCHAR(150)    NULL,
    CONSTRAINT [PK_ReglaComercial] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_ReglaComercial_DemandaCanal] UNIQUE ([Demanda], [Canal])
);

-- ============================================================
-- Datos iniciales (misma lógica que tenías en el mock)
-- ============================================================
INSERT INTO [dbo].[ReglaComercial] ([Demanda], [Canal], [DescuentoMonto]) VALUES
('BAJA',  'SPOT',         1.00),
('BAJA',  'ACTIVO',       2.00),
('BAJA',  'ESTRATEGICO',  3.00),
('MEDIA', 'SPOT',         0.00),
('MEDIA', 'ACTIVO',       1.00),
('MEDIA', 'ESTRATEGICO',  2.00),
('ALTA',  'SPOT',         NULL),   -- NO VENDER
('ALTA',  'ACTIVO',       0.00),
('ALTA',  'ESTRATEGICO',  1.00);
