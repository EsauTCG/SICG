USE [SIGO]
GO

/****** Object:  Table [dbo].[Log_Excepciones_AutoArticulos]    Script Date: 11/05/2026 05:01:55 p. m. ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[Log_Excepciones_AutoArticulos](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Fecha] [datetime2](7) NOT NULL,
	[UsuarioId] [int] NOT NULL,
	[Supervisor] [nvarchar](max) NOT NULL,
	[ArticuloIngresado] [nvarchar](max) NOT NULL,
	[CategoriaId] [int] NOT NULL,
 CONSTRAINT [PK_Log_Excepciones_AutoArticulos] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[Log_Excepciones_AutoArticulos]  WITH CHECK ADD  CONSTRAINT [FK_Log_Excepciones_AutoArticulos_CatCategorias_AutoArticulos_CategoriaId] FOREIGN KEY([CategoriaId])
REFERENCES [dbo].[CatCategorias_AutoArticulos] ([Id])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[Log_Excepciones_AutoArticulos] CHECK CONSTRAINT [FK_Log_Excepciones_AutoArticulos_CatCategorias_AutoArticulos_CategoriaId]
GO

ALTER TABLE [dbo].[Log_Excepciones_AutoArticulos]  WITH CHECK ADD  CONSTRAINT [FK_Log_Excepciones_AutoArticulos_CatUsuarios_AutoArticulos_UsuarioId] FOREIGN KEY([UsuarioId])
REFERENCES [dbo].[CatUsuarios_AutoArticulos] ([Id])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[Log_Excepciones_AutoArticulos] CHECK CONSTRAINT [FK_Log_Excepciones_AutoArticulos_CatUsuarios_AutoArticulos_UsuarioId]
GO

