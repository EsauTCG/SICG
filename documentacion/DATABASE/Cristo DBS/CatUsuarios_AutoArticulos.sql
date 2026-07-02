USE [SIGO]
GO

/****** Object:  Table [dbo].[CatUsuarios_AutoArticulos]    Script Date: 11/05/2026 05:01:04 p. m. ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[CatUsuarios_AutoArticulos](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Nombre] [nvarchar](max) NOT NULL,
	[Departamento] [nvarchar](max) NOT NULL,
	[TokenGafete] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_CatUsuarios_AutoArticulos] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

