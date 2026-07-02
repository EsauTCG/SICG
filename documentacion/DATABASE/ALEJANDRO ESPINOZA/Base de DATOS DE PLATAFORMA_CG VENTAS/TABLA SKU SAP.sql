CREATE TABLE ArticuloSap (
    ProductoCodigo NVARCHAR(50) PRIMARY KEY,
    ProductoNombre NVARCHAR(200),
    U_MASTER NVARCHAR(50),
    U_TipoporSKU NVARCHAR(50),
	 U_KilosCaja DECIMAL(18,4) NULL,      -- ⬅️ nuevo campo
	Rotacion INT,
    U_KilosCaja DECIMAL(18,4) NULL,
    U_Clas_Prod int NULL,
    U_PRESENT   int NULL,
    U_PorcInye  int  NULL,
    FechaModificacion DATETIME DEFAULT GETDATE()
)


CREATE INDEX IX_ArticuloSap_ProductoCodigo
ON dbo.ArticuloSap (ProductoCodigo)
INCLUDE (ProductoNombre, U_MASTER, U_TipoporSKU, U_KilosCaja, Rotacion, FechaModificacion);


ALTER TABLE dbo.ArticuloSap
ADD U_KilosCaja DECIMAL(18,4) NULL;  -- ⬅️ nuevo campo


--ALTER TABLE ArticuloSap
--ADD Rotacion INT DEFAULT 0;


SELECT * FROM OrdenVentaProducto a
INNER JOIN ArticuloSap B ON a.ProductoCodigo = B.ProductoCodigo


select *  
from Presupuestos a
inner join ArticuloSap b on a.ProductoCodigo = b.ProductoCodigo


select * from ArticuloSap
select * from OrdenVenta
select * from OrdenVentaProducto
select * from Presupuestos



--sp_help 'OrdenVentaProducto'



CREATE TABLE dbo.ClasificacionProduccion (
    ClasificacionId INT NOT NULL,
    Nombre NVARCHAR(50) NOT NULL,

    CONSTRAINT PK_ClasificacionProduccion
        PRIMARY KEY (ClasificacionId),

    CONSTRAINT UQ_ClasificacionProduccion_Nombre
        UNIQUE (Nombre)
);



CREATE TABLE dbo.Presentacion (
    PresentacionId INT NOT NULL,
    Nombre NVARCHAR(50) NOT NULL,

    CONSTRAINT PK_Presentacion
        PRIMARY KEY (PresentacionId),

    CONSTRAINT UQ_Presentacion_Nombre
        UNIQUE (Nombre)
);


CREATE TABLE dbo.PorcInyeccion (
    PorcentajeId INT NOT NULL,
    Valor INT NOT NULL,

    CONSTRAINT PK_PorcInyeccion
        PRIMARY KEY (PorcentajeId),

    CONSTRAINT CK_PorcInyeccion_Valor
        CHECK (Valor BETWEEN 0 AND 100)
);


insert into ClasificacionProduccion values (0,'N.PROD')
insert into ClasificacionProduccion values (1,'LINEA')
insert into ClasificacionProduccion values (2,'B.PED')
insert into ClasificacionProduccion values (3,'STOCK LIMITADO')
insert into ClasificacionProduccion values (99,'POR DEFINIR')





insert into Presentacion values (0,'FRESCO')
insert into Presentacion values (1,'CONGELADO')
insert into Presentacion values (99,'POR DEFINIR')



insert into PorcInyeccion values (0,'0')
insert into PorcInyeccion values (1,'15')
insert into PorcInyeccion values (2,'20')
insert into PorcInyeccion values (3,'25')
insert into PorcInyeccion values (4,'30')
insert into PorcInyeccion values (5,'35')
insert into PorcInyeccion values (99,'99')