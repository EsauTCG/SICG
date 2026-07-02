USE CarnesG;

INSERT INTO Usuarios (Nombre, Correo, Contraseña)
VALUES ('Chivas', 'chivas@gmail.com', '123456');

INSERT INTO Denuncias (IdUsuario, Fecha, Descripcion, Estado)
VALUES (1, GETDATE(), 'Denuncia de prueba', 'Pendiente');

