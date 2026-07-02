-- ----------------------------------------------------
-- INSERTS INICIALES PARA PROBAR EL SIDEBAR (ADMIN = 1)
-- ----------------------------------------------------
SET NOCOUNT ON;
BEGIN TRAN;

-- Variables para categorías
DECLARE @catMercados INT, @catComercial INT, @catAdmin INT, @catOperaciones INT, @catAutoriz INT, @catConfig INT;

-- Insertar categorías
INSERT INTO SidebarCategorias (Nombre, Icono) VALUES ('Mercados', 'fas fa-chart-pie');
SET @catMercados = SCOPE_IDENTITY();

INSERT INTO SidebarCategorias (Nombre, Icono) VALUES ('Área Comercial', 'fas fa-store');
SET @catComercial = SCOPE_IDENTITY();

INSERT INTO SidebarCategorias (Nombre, Icono) VALUES ('Administración', 'fas fa-briefcase');
SET @catAdmin = SCOPE_IDENTITY();

INSERT INTO SidebarCategorias (Nombre, Icono) VALUES ('Operaciones', 'fas fa-project-diagram');
SET @catOperaciones = SCOPE_IDENTITY();

INSERT INTO SidebarCategorias (Nombre, Icono) VALUES ('Autorizaciones', 'fas fa-clipboard-check');
SET @catAutoriz = SCOPE_IDENTITY();

INSERT INTO SidebarCategorias (Nombre, Icono) VALUES ('Configuración', 'fas fa-cogs');
SET @catConfig = SCOPE_IDENTITY();

-- Variables para módulos raíz
DECLARE @modMercados INT, @modComercial INT, @modAdmin INT, @modOperaciones INT, @modAutoriz INT, @modConfig INT;

-- Insertar módulos principales (raíz)
INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Mercados', 'fas fa-chart-pie', NULL, NULL, 10, 1, @catMercados);
SET @modMercados = SCOPE_IDENTITY();

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Área Comercial', 'fas fa-store', NULL, NULL, 20, 1, @catComercial);
SET @modComercial = SCOPE_IDENTITY();

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Administración', 'fas fa-briefcase', NULL, NULL, 30, 1, @catAdmin);
SET @modAdmin = SCOPE_IDENTITY();

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Operaciones', 'fas fa-project-diagram', NULL, NULL, 40, 1, @catOperaciones);
SET @modOperaciones = SCOPE_IDENTITY();

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Autorizaciones', 'fas fa-clipboard-check', NULL, NULL, 50, 1, @catAutoriz);
SET @modAutoriz = SCOPE_IDENTITY();

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Configuración', 'fas fa-cogs', NULL, NULL, 60, 1, @catConfig);
SET @modConfig = SCOPE_IDENTITY();

-- Insertar submódulos para MERCADOS
INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Carnes', 'fas fa-drumstick-bite', NULL, @modMercados, 10, 1, @catMercados);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Granos', 'fas fa-warehouse', NULL, @modMercados, 20, 1, @catMercados);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('USD/MXN', 'fas fa-money-bill-wave', NULL, @modMercados, 30, 1, @catMercados);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Materias Primas', 'fas fa-seedling', '/Mercados/MateriaPrima', @modMercados, 40, 1, @catMercados);


-- Insertar submódulos para ÁREA COMERCIAL
INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Artículos', 'fas fa-boxes', '/Comercial/Cat_Articulo', @modComercial, 10, 1, @catComercial);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Clientes', 'fas fa-user-tie', '/Comercial/Cat_Clientes', @modComercial, 20, 1, @catComercial);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Precios', 'fas fa-dollar-sign', '/Comercial/Cat_Precio', @modComercial, 30, 1, @catComercial);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Orden Venta', 'fas fa-shopping-cart', '/Comercial/Comercial', @modComercial, 40, 1, @catComercial);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Pedidos Venta', 'fas fa-clipboard-list', '/Comercial/admin_ventas', @modComercial, 50, 1, @catComercial);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Facturación', 'fas fa-file-invoice', NULL, @modComercial, 60, 1, @catComercial);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Prospectos', 'fas fa-user-plus', '/Comerc/Prospecto', @modComercial, 70, 1, @catComercial);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Balance Master', 'fas fa-chart-pie', '/Comercial/Balance_Master', @modComercial, 80, 1, @catComercial);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Presupuesto / Clientes', 'fas fa-file-invoice-dollar', '/Comercial/Presupuestos', @modComercial, 90, 1, @catComercial);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Presupuesto / Cedis', 'fas fa-file-invoice-dollar', '/Comercial/PresupuestosCedis', @modComercial, 100, 1, @catComercial);


-- Insertar submódulos para ADMINISTRACIÓN
INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Compras Consumibles', 'fas fa-shopping-basket', NULL, @modAdmin, 10, 1, @catAdmin);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Admón. y Finanzas', 'fas fa-file-invoice-dollar', NULL, @modAdmin, 20, 1, @catAdmin);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Reporteadores', 'fas fa-chart-bar', NULL, @modAdmin, 30, 1, @catAdmin);


-- Insertar submódulos para OPERACIONES
INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Inyección', 'fas fa-industry', '/Operaciones/Inyecciones', @modOperaciones, 10, 1, @catOperaciones);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Factor Crítico', 'fas fa-exclamation-triangle', '/Operaciones/FactorCritico', @modOperaciones, 20, 1, @catOperaciones);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Estudios', 'fas fa-microscope', '/Operaciones/Estudios', @modOperaciones, 30, 1, @catOperaciones);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Planeación', 'fas fa-calendar-alt', '/Comerc/Planeacion', @modOperaciones, 40, 1, @catOperaciones);


-- Insertar submódulos para AUTORIZACIONES
INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Presupuesto', 'fas fa-file-invoice-dollar', '/Autorizaciones/aut_presupuesto', @modAutoriz, 10, 1, @catAutoriz);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Precio', 'fas fa-tags', '/Autorizaciones/aut_precio', @modAutoriz, 20, 1, @catAutoriz);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Crédito', 'fas fa-credit-card', '/Autorizaciones/aut_credito', @modAutoriz, 30, 1, @catAutoriz);


-- Insertar submódulos para CONFIGURACIÓN
INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Perfiles', 'fas fa-user-shield', '/Permisos/PermisosConfiguracion', @modConfig, 10, 1, @catConfig);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Usuarios AD', 'fas fa-users-cog', '/UsuariosAD/UsuariosADConfiguracion', @modConfig, 20, 1, @catConfig);

INSERT INTO SidebarModulos (Nombre, Icono, Url, PadreId, Orden, Activo, CategoriaId)
VALUES ('Usuarios Locales', 'fas fa-users-cog', '/Usuarios/UsuariosConfiguracion', @modConfig, 30, 1, @catConfig);


-- Asignar permisos: dar visibilidad a TODOS los módulos activos al perfil ADMINISTRADOR (PerfilId = 1)
-- Evitar duplicados: insertar solo los que no existan
INSERT INTO SidebarPermisos (PerfilId, ModuloId)
SELECT 1 AS PerfilId, m.Id AS ModuloId
FROM SidebarModulos m
WHERE m.Activo = 1
  AND NOT EXISTS (
        SELECT 1 FROM SidebarPermisos sp
        WHERE sp.PerfilId = 1 AND sp.ModuloId = m.Id
  );

COMMIT;
SET NOCOUNT OFF;

-- Mostrar lo que se insertó para verificar
SELECT sc.Id AS CategoriaId, sc.Nombre AS CategoriaNombre, sc.Icono AS CategoriaIcono
FROM SidebarCategorias sc
ORDER BY sc.Id;

SELECT m.Id, m.Nombre, m.Icono, m.Url, m.PadreId, m.Orden, m.Activo, m.CategoriaId
FROM SidebarModulos m
ORDER BY m.CategoriaId, m.PadreId, m.Orden;

SELECT sp.Id, sp.PerfilId, sp.ModuloId
FROM SidebarPermisos sp
WHERE sp.PerfilId = 1
ORDER BY sp.ModuloId;
