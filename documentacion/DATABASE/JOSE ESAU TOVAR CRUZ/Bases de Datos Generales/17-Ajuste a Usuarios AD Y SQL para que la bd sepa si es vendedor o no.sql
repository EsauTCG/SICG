ALTER TABLE UsuarioSQL
ADD CONSTRAINT CK_UsuarioSQL_Vendedor
CHECK (EsVendedor = 1 OR VendedorId IS NULL);

ALTER TABLE UsuariosAD
ADD CONSTRAINT CK_UsuariosAD_Vendedor
CHECK (EsVendedor = 1 OR VendedorId IS NULL);
