USE [SIGO]
GO

/****** Object:  Table [dbo].[InventarioSistemas]    Script Date: 16/04/2026 05:41:11 p. m. ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[InventarioSistemas](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[IdArticuloSap] [nvarchar](50) NOT NULL,
	[Nombre] [nvarchar](200) NOT NULL,
	[TipoArticulo] [nvarchar](50) NOT NULL,
	[Marca] [nvarchar](100) NOT NULL,
	[Modelo] [nvarchar](100) NOT NULL,
	[Proveedor] [nvarchar](200) NOT NULL,
	[Costo] [decimal](18, 2) NOT NULL,
	[FechaCompra] [datetime2](7) NULL,
	[DiasGarantia] [int] NOT NULL,
	[NumeroSerie] [nvarchar](100) NOT NULL,
	[Asignacion] [nvarchar](200) NOT NULL,
	[FechaEntrada] [datetime2](7) NULL,
	[FechaSalida] [datetime2](7) NULL,
	[TiempoVida] [nvarchar](50) NOT NULL,
	[Ubicacion] [nvarchar](100) NOT NULL,
	[Planta] [nvarchar](50) NOT NULL,
	[Stock] [int] NOT NULL,
	[StockMinimo] [int] NOT NULL,
	[FotoUsuario] [nvarchar](max) NOT NULL,
	[DocumentoComodato] [nvarchar](max) NOT NULL,
	[FirmaDigital] [nvarchar](max) NOT NULL,
	[HistorialAsignaciones] [nvarchar](max) NOT NULL,
	[IP] [nvarchar](100) NULL,
 CONSTRAINT [PK_InventarioSistemas] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO


-----------------2-------------------------------


USE [SIGO]
GO

/****** Object:  Table [dbo].[MovimientoInventario]    Script Date: 16/04/2026 05:41:31 p. m. ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[MovimientoInventario](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ArticuloSap] [nvarchar](50) NOT NULL,
	[NombreArticulo] [nvarchar](200) NOT NULL,
	[TipoMovimiento] [nvarchar](20) NOT NULL,
	[Cantidad] [int] NOT NULL,
	[Fecha] [datetime2](7) NOT NULL,
	[Referencia] [nvarchar](200) NOT NULL,
 CONSTRAINT [PK_MovimientoInventario] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO




----------------------------3----------------------------------------------------------

USE [SIGO]
GO

/****** Object:  Table [dbo].[RegistroHistorial]    Script Date: 16/04/2026 05:41:52 p. m. ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[RegistroHistorial](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[InventarioSistemasId] [int] NOT NULL,
	[FechaHora] [nvarchar](50) NOT NULL,
	[Nota] [nvarchar](500) NOT NULL,
	[FotoBase64] [nvarchar](max) NOT NULL,
	[DocumentoBase64] [nvarchar](max) NOT NULL,
	[FirmaBase64] [nvarchar](max) NOT NULL,
	[FotoRuta] [nvarchar](500) NULL,
	[DocumentoRuta] [nvarchar](500) NULL,
	[FirmaRuta] [nvarchar](500) NULL,
 CONSTRAINT [PK_RegistroHistorial] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[RegistroHistorial]  WITH CHECK ADD  CONSTRAINT [FK_RegistroHistorial_InventarioSistemas_InventarioSistemasId] FOREIGN KEY([InventarioSistemasId])
REFERENCES [dbo].[InventarioSistemas] ([Id])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[RegistroHistorial] CHECK CONSTRAINT [FK_RegistroHistorial_InventarioSistemas_InventarioSistemasId]
GO


