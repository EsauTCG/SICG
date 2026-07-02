USE [SIGO]
GO

/****** Object:  Table [dbo].[ClasificacionProduccion]    Script Date: 17/03/2026 03:37:11 p. m. ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[ClasificacionProduccion](
	[ClasificacionId] [int] NOT NULL,
	[Nombre] [nvarchar](50) NOT NULL,
 CONSTRAINT [PK_ClasificacionProduccion] PRIMARY KEY CLUSTERED 
(
	[ClasificacionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_ClasificacionProduccion_Nombre] UNIQUE NONCLUSTERED 
(
	[Nombre] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO




--INSERT INTO ClasificacionProduccion (Id, Descripcion)
--VALUES
--(0, 'N.PROD'),
--(1, 'LINEA'),
--(2, 'B.PED'),
--(3, 'STOCK LIMITADO'),
--(99, 'POR DEFINIR');

