CREATE TABLE EstatusOrden (
    EstatusId INT PRIMARY KEY,        -- 1, 2, 3, 5, 6
    Nombre NVARCHAR(50) NOT NULL,     -- Nombre descriptivo
    Descripcion NVARCHAR(200) NULL    -- Detalle del estatus
);

-- Insertar los estatus definidos
INSERT INTO EstatusOrden (EstatusId, Nombre, Descripcion)
VALUES
    (0, 'Cancelado', 'Orden cancelada'),
    (1, 'Directo a administración', 'Orden creada y no requiere autorización'),
    (2, 'En autorización', 'Orden pendiente de autorización'),
    (3, 'Autorizado / Administración de pedidos', 'Orden autorizada y lista para administración'),
	(4, 'Completado', 'Orden Completada para Enviar y generar Subpedido'),
    (5, 'Enviado a SAP', 'Orden enviada a SAP para integración'),
    (6, 'Creacion de Embarque', 'Creacion de QR para embarque y salida de caseta')
    



--ALTER TABLE OrdenVenta
--ADD EstatusId INT NOT NULL DEFAULT 1
--CONSTRAINT FK_OrdenVenta_Estatus FOREIGN KEY REFERENCES EstatusOrden(EstatusId);


select * from EstatusOrden


