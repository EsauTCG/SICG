--crear schema par que aparezcan las vistas en reportes
IF NOT EXISTS (
    SELECT 1 
    FROM sys.schemas 
    WHERE name = 'rpt'
)
BEGIN
    EXEC('CREATE SCHEMA rpt');
END;


--cambiar vistas al schema rpt

IF NOT EXISTS (
    SELECT 1
    FROM sys.schemas
    WHERE name = 'rpt'
)
BEGIN
    EXEC('CREATE SCHEMA rpt');
END;
GO

ALTER SCHEMA rpt TRANSFER dbo.vw_DetallesClientes;
GO

EXEC sp_rename 'rpt.v_DetallesClientes', 'DetallesClientes';
GO