/* =========================================================
   1) VISTA UNIFICADA DE USUARIOS (AD + SQL)
   ========================================================= */
IF OBJECT_ID('dbo.vwUsuariosChat', 'V') IS NOT NULL
    DROP VIEW dbo.vwUsuariosChat;
GO

CREATE VIEW dbo.vwUsuariosChat
AS
    SELECT
        UAD.UsuarioAD     AS UsuarioId,    -- ID único para el chat
        UAD.Nombre        AS Nombre,
        'AD'              AS Origen,
        1                 AS Activo,
        UAD.PerfilId,
        UAD.EsVendedor,
        UAD.VendedorId
    FROM dbo.UsuariosAD UAD

    UNION

    SELECT
        USQ.Usuario       AS UsuarioId,
        USQ.Nombre        AS Nombre,
        'SQL'             AS Origen,
        USQ.Activo        AS Activo,
        USQ.PerfilId,
        USQ.EsVendedor,
        USQ.VendedorId
    FROM dbo.UsuarioSQL USQ;
GO


/* =========================================================
   2) TABLA DE ÁREAS DE CHAT
   ========================================================= */

IF OBJECT_ID('dbo.ChatAreas', 'U') IS NOT NULL
    DROP TABLE dbo.ChatAreas;
GO

CREATE TABLE dbo.ChatAreas (
    IdArea INT IDENTITY(1,1) PRIMARY KEY,
    Nombre NVARCHAR(80) NOT NULL,
    -- correo / login del responsable (debe existir en vwUsuariosChat.UsuarioId)
    ResponsableUsuarioId NVARCHAR(200) NULL,
    Activo BIT NOT NULL DEFAULT 1
);
GO

CREATE INDEX IX_ChatAreas_Responsable
    ON dbo.ChatAreas (ResponsableUsuarioId);
GO


/* =========================================================
   3) TABLA DE CONVERSACIONES
   ========================================================= */
IF OBJECT_ID('dbo.ChatConversaciones', 'U') IS NOT NULL
    DROP TABLE dbo.ChatConversaciones;
GO

CREATE TABLE dbo.ChatConversaciones (
    IdConversacion INT IDENTITY(1,1) PRIMARY KEY,

    -- Usuario que inició el chat (correo / login)
    UsuarioId NVARCHAR(200) NOT NULL,

    -- Área con la que está hablando
    IdArea INT NOT NULL,

    FechaInicio DATETIME NOT NULL DEFAULT(GETDATE()),
    Cerrada BIT NOT NULL DEFAULT(0),

    CONSTRAINT FK_ChatConversaciones_Areas
        FOREIGN KEY (IdArea) REFERENCES dbo.ChatAreas(IdArea)
);
GO

CREATE INDEX IX_ChatConversaciones_Usuario
    ON dbo.ChatConversaciones (UsuarioId);

CREATE INDEX IX_ChatConversaciones_Area
    ON dbo.ChatConversaciones (IdArea);
GO


/* =========================================================
   4) TABLA DE MENSAJES
   ========================================================= */
IF OBJECT_ID('dbo.ChatMensajes', 'U') IS NOT NULL
    DROP TABLE dbo.ChatMensajes;
GO

CREATE TABLE dbo.ChatMensajes (
    IdMensaje INT IDENTITY(1,1) PRIMARY KEY,

    IdConversacion INT NOT NULL,

    -- Quién envió el mensaje (usuario final o usuario del área)
    AutorUsuarioId NVARCHAR(200) NOT NULL,

    Texto NVARCHAR(MAX) NOT NULL,
    Fecha DATETIME NOT NULL DEFAULT(GETDATE()),
    Leido BIT NOT NULL DEFAULT(0),

    CONSTRAINT FK_ChatMensajes_Conversaciones
        FOREIGN KEY (IdConversacion) REFERENCES dbo.ChatConversaciones(IdConversacion)
);
GO

CREATE INDEX IX_ChatMensajes_Conversacion
    ON dbo.ChatMensajes (IdConversacion);

CREATE INDEX IX_ChatMensajes_Autor
    ON dbo.ChatMensajes (AutorUsuarioId);
GO


/* =========================================================
   5) SEMILLA BÁSICA DE ÁREAS (AJUSTA CORREOS)
   ========================================================= */
INSERT INTO dbo.ChatAreas (Nombre, ResponsableUsuarioId, Activo)
VALUES 
  ('Presupuestos', 'jose.gonzalez@carnesg.net', 1),
  ('Precios',      'marco.romo@carnesg.net',    1),
  ('Logística',    'oscar.galicia@carnesg.net', 1);
GO


/* =========================================================
   6) VISTA RESUMEN OPCIONAL PARA CONSULTAS RÁPIDAS
   ========================================================= */
IF OBJECT_ID('dbo.vwChatConversacionesResumen', 'V') IS NOT NULL
    DROP VIEW dbo.vwChatConversacionesResumen;
GO

CREATE VIEW dbo.vwChatConversacionesResumen
AS
    SELECT
        c.IdConversacion,
        c.UsuarioId,
        uc.Nombre          AS NombreUsuario,
        c.IdArea,
        a.Nombre           AS Area,
        a.ResponsableUsuarioId,
        ur.Nombre          AS NombreResponsable,
        c.FechaInicio,
        c.Cerrada,
        -- último mensaje
        MAX(m.Fecha)       AS UltimoMensajeFecha
    FROM dbo.ChatConversaciones c
    LEFT JOIN dbo.ChatAreas a
        ON c.IdArea = a.IdArea
    LEFT JOIN dbo.ChatMensajes m
        ON m.IdConversacion = c.IdConversacion
    LEFT JOIN dbo.vwUsuariosChat uc
        ON c.UsuarioId = uc.UsuarioId
    LEFT JOIN dbo.vwUsuariosChat ur
        ON a.ResponsableUsuarioId = ur.UsuarioId
    GROUP BY
        c.IdConversacion, c.UsuarioId, uc.Nombre,
        c.IdArea, a.Nombre, a.ResponsableUsuarioId, ur.Nombre,
        c.FechaInicio, c.Cerrada;
GO
