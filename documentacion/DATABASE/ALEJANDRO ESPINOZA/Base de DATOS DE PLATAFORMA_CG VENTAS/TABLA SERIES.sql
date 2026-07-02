

CREATE TABLE series (
    Id INT PRIMARY KEY IDENTITY(1,1),
    SerieId INT NOT NULL,
    NombreSerie VARCHAR(50),
	Sucursal VARCHAR(50),
	Canal varchar(50),
	AlmacenTransitoId Varchar(50) NULL,
	Sucursalid VARCHAR(50)
    CONSTRAINT UQ_SerieId UNIQUE (SerieId)
);






select * from series


--insert into series values (84,'Planta1')
--insert into series values (85,'TIF')
--insert into series values (86,'Lagos')
--insert into series values (87,'Merida')
--insert into series values (88,'Mty')
--insert into series values (128,'MXL')
--insert into series values (143,'CDMX')
--insert into series values (145,'TIJUANA')



--update series set Sucursal = 'MATRIZ'where SERIEID = '84' 
--update series set Sucursal = 'MATRIZ'where SERIEID = '85' 
--update series set Sucursal = 'LAGOS'where SERIEID = '86' 
--update series set Sucursal = 'MERSERIEIDA'where SERIEID = '87' 
--update series set Sucursal = 'MONTERREY'where SERIEID = '88' 
--update series set Sucursal =  'MEXICALI' where SERIEID ='128'
--update series set Sucursal =  'CIUDAD DE MEXICO' where SERIEID ='143'
--update series set Sucursal =  'TIJUANA' where SERIEID ='14



   -- update series set canal = '-' where Id = 1
   -- update series set canal = '-' where Id = 2
	  --update series set canal = '-' where Id = 3
	  --  update series set canal = 'CEDIS-MDA' where Id = 4
		 -- update series set canal = 'CEDIS-MTY' where Id = 5
		 --   update series set canal = 'CEDIS-MXL' where Id = 6
			--  update series set canal = 'CEDIS-CDMX' where Id = 7
			--    update series set canal = 'CEDIS-TJN' where Id = 8

--			update series set AlmacenTransitoId = 'TM' where Id = 4
--update series set AlmacenTransitoId = 'MT' where Id = 5
--update series set AlmacenTransitoId = 'TMX' where Id = 6
--update series set AlmacenTransitoId = 'TCDMX' where Id = 7
--update series set AlmacenTransitoId = 'TTN' where Id = 8




--update series set sucursalid = 'SUC01' where Id = 1
--update series set sucursalid = 'SUC02' where Id = 2
--update series set sucursalid = 'SUC03' where Id = 4
--update series set sucursalid = 'SUC04' where Id = 5
--update series set sucursalid = 'SUC05' where Id = 6
--update series set sucursalid = 'SUC06' where Id = 7
--update series set sucursalid = 'SUC07' where Id = 8
--update series set sucursalid = 'SUC08' where Id = 9
--update series set sucursalid = 'SUC09' where Id = 10