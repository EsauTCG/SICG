USE [SIGO]
GO

/****** Object:  Table [dbo].[RelPermisos_AutoArticulos]    Script Date: 11/05/2026 05:02:37 p. m. ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[RelPermisos_AutoArticulos](
	[UsuarioId] [int] NOT NULL,
	[CategoriaId] [int] NOT NULL,
 CONSTRAINT [PK_RelPermisos_AutoArticulos] PRIMARY KEY CLUSTERED 
(
	[UsuarioId] ASC,
	[CategoriaId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

ALTER TABLE [dbo].[RelPermisos_AutoArticulos]  WITH CHECK ADD  CONSTRAINT [FK_RelPermisos_AutoArticulos_CatCategorias_AutoArticulos_CategoriaId] FOREIGN KEY([CategoriaId])
REFERENCES [dbo].[CatCategorias_AutoArticulos] ([Id])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[RelPermisos_AutoArticulos] CHECK CONSTRAINT [FK_RelPermisos_AutoArticulos_CatCategorias_AutoArticulos_CategoriaId]
GO

ALTER TABLE [dbo].[RelPermisos_AutoArticulos]  WITH CHECK ADD  CONSTRAINT [FK_RelPermisos_AutoArticulos_CatUsuarios_AutoArticulos_UsuarioId] FOREIGN KEY([UsuarioId])
REFERENCES [dbo].[CatUsuarios_AutoArticulos] ([Id])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[RelPermisos_AutoArticulos] CHECK CONSTRAINT [FK_RelPermisos_AutoArticulos_CatUsuarios_AutoArticulos_UsuarioId]
GO

