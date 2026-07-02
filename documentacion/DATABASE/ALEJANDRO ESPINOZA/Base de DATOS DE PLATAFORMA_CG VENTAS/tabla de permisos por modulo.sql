CREATE TABLE ModulosSistema (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Clave NVARCHAR(100) NOT NULL UNIQUE,
    Nombre NVARCHAR(150) NOT NULL,
    Activo BIT NOT NULL DEFAULT 1,
    FechaCreacion DATETIME NOT NULL DEFAULT GETDATE()
);

CREATE TABLE PerfilPermisoModulo (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PerfilId INT NOT NULL,
    ModuloId INT NOT NULL,
    PuedeLeer BIT NOT NULL DEFAULT 0,
    PuedeEscribir BIT NOT NULL DEFAULT 0,
    PuedeEliminar BIT NOT NULL DEFAULT 0,
    Activo BIT NOT NULL DEFAULT 1,
    FechaCreacion DATETIME NOT NULL DEFAULT GETDATE(),
    FechaModificacion DATETIME NULL,

    CONSTRAINT FK_PerfilPermisoModulo_Perfiles
        FOREIGN KEY (PerfilId) REFERENCES Perfiles(Id),

    CONSTRAINT FK_PerfilPermisoModulo_ModulosSistema
        FOREIGN KEY (ModuloId) REFERENCES ModulosSistema(Id),

    CONSTRAINT UQ_PerfilPermisoModulo UNIQUE (PerfilId, ModuloId)
);

INSERT INTO ModulosSistema (Clave, Nombre)
VALUES 
('REGLAS_COMERCIALES', 'Reglas Comerciales'),
('MODO_PRESUPUESTO', 'Presupuestos'),
('EDIT_PRESUPUESTO', 'Balance Master'),
('EDIT_PRODUCCION', 'Balance Master');

INSERT INTO PerfilPermisoModulo
(
    PerfilId,
    ModuloId,
    PuedeLeer,
    PuedeEscribir,
    PuedeEliminar,
    Activo
)
SELECT
    p.Id,
    m.Id,
    1,
    1,
    1,
    1
FROM Perfiles p
INNER JOIN ModulosSistema m
    ON m.Clave = 'REGLAS_COMERCIALES'
WHERE p.Nombre = 'Administrador';

INSERT INTO PerfilPermisoModulo
(
    PerfilId,
    ModuloId,
    PuedeLeer,
    PuedeEscribir,
    PuedeEliminar,
    Activo
)
SELECT
    p.Id,
    m.Id,
    1,
    0,
    0,
    1
FROM Perfiles p
INNER JOIN ModulosSistema m
    ON m.Clave = 'REGLAS_COMERCIALES'
WHERE p.Nombre = 'Ventas';




SELECT
    u.Usuario,
    u.Nombre AS NombreUsuario,
    u.PerfilId,
    p.Nombre AS Perfil,
    m.Nombre as Vista,
    m.Clave AS Modulo,
    ppm.PuedeLeer,
    ppm.PuedeEscribir,
    ppm.PuedeEliminar,
    ppm.Activo
FROM UsuarioSQL u
INNER JOIN Perfiles p
    ON u.PerfilId = p.Id
INNER JOIN PerfilPermisoModulo ppm
    ON ppm.PerfilId = p.Id
INNER JOIN ModulosSistema m
    ON m.Id = ppm.ModuloId
WHERE u.Usuario = 'admin';

select * from PerfilPermisoModulo a
inner join ModulosSistema b on a.ModuloId = b.Id
where a.Id = 1



--update PerfilPermisoModulo set PuedeLeer = 0 where Id = 4

--update ModulosSistema set Nombre = 'Control de Precios' where Id = 1


select * from ModulosSistema
select * from PerfilPermisoModulo 
select * from perfiles


--INSERT INTO PerfilPermisoModulo
--    (PerfilId, ModuloId, PuedeLeer, PuedeEscribir, PuedeEliminar, Activo, FechaCreacion, FechaModificacion)
--VALUES
--    (1, 2, 1, 1, 0, 1, GETDATE(), NULL);

