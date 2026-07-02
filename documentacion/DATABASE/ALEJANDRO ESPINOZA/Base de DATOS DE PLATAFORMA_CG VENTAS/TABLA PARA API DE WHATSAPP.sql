CREATE TABLE WhatsAppAPI (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Nombre NVARCHAR(100) NOT NULL,
    Phone NVARCHAR(30) NOT NULL,
    ApiKey NVARCHAR(100) NOT NULL,
    Activo BIT NOT NULL DEFAULT 1,
    OrdenRotacion INT NOT NULL DEFAULT 1,
    FechaAlta DATETIME NOT NULL DEFAULT GETDATE(),
    FechaModificacion DATETIME NULL
);

select * from WhatsAppAPI



--INSERT INTO WhatsAppAPI (Nombre, Phone, ApiKey, Activo, OrdenRotacion, FechaAlta)
--VALUES ('Telefono principal', '5213951146202', '7739413', 1, 1, GETDATE());

--/WhatsAppTest/Enviar?telefono=5213951146202&mensaje=Hola prueba WhatsApp



CREATE TABLE WhatsAppDestino (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Nombre NVARCHAR(120) NOT NULL,
    Telefono NVARCHAR(30) NOT NULL,
    TipoDestino NVARCHAR(20) NOT NULL,      -- CEDIS / VENDEDOR
    Canal NVARCHAR(100) NULL,               -- para CEDIS
    VendedorId INT NULL,                    -- para VENDEDOR
    Activo BIT NOT NULL DEFAULT 1,
    HoraEnvio TIME NULL,
    DiaEnvio NVARCHAR(20) NULL,             -- LUNES, MARTES, etc
    FechaAlta DATETIME NOT NULL DEFAULT GETDATE(),
    FechaModificacion DATETIME NULL
);
GO

ALTER TABLE WhatsAppDestino
ADD CONSTRAINT CK_WhatsAppDestino_TipoDestino
CHECK (TipoDestino IN ('CEDIS', 'VENDEDOR'));
GO

ALTER TABLE WhatsAppDestino
ADD CONSTRAINT CK_WhatsAppDestino_DestinoDatos
CHECK (
    (TipoDestino = 'CEDIS' AND Canal IS NOT NULL AND VendedorId IS NULL)
    OR
    (TipoDestino = 'VENDEDOR' AND VendedorId IS NOT NULL AND Canal IS NULL)
);
GO

select * from WhatsAppDestino where Id = 1
--update WhatsAppDestino set Telefono = '5213951146202x' where Id = 1


INSERT INTO WhatsAppDestino
    (Nombre, Telefono, TipoDestino, Canal, Activo, DiaEnvio, HoraEnvio)
VALUES
    ('Merida', '5213951146202', 'CEDIS', 'CEDIS-MDA', 1, 'LUNES', '08:00');

INSERT INTO WhatsAppDestino
    (Nombre, Telefono, TipoDestino, VendedorId, Activo, DiaEnvio, HoraEnvio)
VALUES
    ('Vendedor 28', '5213951146202', 'VENDEDOR', 28, 1, 'LUNES', '08:00');

USE [SIGO]
GO
/****** Object:  StoredProcedure [dbo].[sp_WhatsAppReporte]    Script Date: 31/03/2026 11:48:52 a. m. ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[sp_WhatsAppReporte]
    @TipoReporte NVARCHAR(20),      -- CEDIS / VENDEDOR
    @Canal NVARCHAR(100) = NULL,
    @VendedorId INT = NULL,
    @Mes INT,
    @Anio INT
AS
BEGIN
    SET NOCOUNT ON;

    SET @TipoReporte = UPPER(LTRIM(RTRIM(ISNULL(@TipoReporte, ''))));
    SET @Canal = UPPER(LTRIM(RTRIM(ISNULL(@Canal, ''))));

    IF @TipoReporte NOT IN ('CEDIS', 'VENDEDOR')
    BEGIN
        RAISERROR('El parametro @TipoReporte debe ser CEDIS o VENDEDOR.', 16, 1);
        RETURN;
    END

    IF @TipoReporte = 'CEDIS' AND @Canal = ''
    BEGIN
        RAISERROR('Para CEDIS debes enviar @Canal.', 16, 1);
        RETURN;
    END

    IF @TipoReporte = 'VENDEDOR' AND @VendedorId IS NULL
    BEGIN
        RAISERROR('Para VENDEDOR debes enviar @VendedorId.', 16, 1);
        RETURN;
    END

    DROP TABLE IF EXISTS #productos;
    DROP TABLE IF EXISTS #clientes;
    DROP TABLE IF EXISTS #presupuestos_cedis;
    DROP TABLE IF EXISTS #presupuestos_vendedor;
    DROP TABLE IF EXISTS #ov;
    DROP TABLE IF EXISTS #ov_con_surtido;
    DROP TABLE IF EXISTS #ov_peso_agg;
    DROP TABLE IF EXISTS #ov_surtido_agg;
    DROP TABLE IF EXISTS #ov_pendiente_sku;
    DROP TABLE IF EXISTS #tr_surtido_agg;
    DROP TABLE IF EXISTS #consumo_cedis_base;
    DROP TABLE IF EXISTS #consumo_vendedor_base;
    DROP TABLE IF EXISTS #todo_cedis;
    DROP TABLE IF EXISTS #todo_vendedor;
    DROP TABLE IF EXISTS #venta_real_base;
    DROP TABLE IF EXISTS #venta_real_cedis;
    DROP TABLE IF EXISTS #venta_real_vendedor;
    DROP TABLE IF EXISTS #todo_cedis_venta_real_extra;
    DROP TABLE IF EXISTS #todo_vendedor_venta_real_extra;
    DROP TABLE IF EXISTS #surtido_ov_cedis;
    DROP TABLE IF EXISTS #surtido_transferencias_cedis;
    DROP TABLE IF EXISTS #surtido_cedis_base;
    DROP TABLE IF EXISTS #surtido_vendedor_base;
    DROP TABLE IF EXISTS #surtido_real_cedis;
    DROP TABLE IF EXISTS #surtido_real_vendedor;
    DROP TABLE IF EXISTS #base_final;

    SELECT
        SKU = UPPER(LTRIM(RTRIM(a.ProductoCodigo))),
        ProductoNombre = COALESCE(NULLIF(LTRIM(RTRIM(a.ProductoNombre)), ''), a.ProductoCodigo)
    INTO #productos
    FROM dbo.ArticuloSap a;

    CREATE UNIQUE CLUSTERED INDEX IX_tmp_productos ON #productos (SKU);

    SELECT
        Cliente        = UPPER(LTRIM(RTRIM(cs.Cliente))),
        NombreCliente  = COALESCE(NULLIF(LTRIM(RTRIM(cs.NombreCliente)), ''), cs.Cliente),
        VendedorId     = cs.VendedorId,
        VendedorNombre = LTRIM(RTRIM(cs.VendedorNombre)),
        U_CANAL        = UPPER(LTRIM(RTRIM(cs.U_CANAL)))
    INTO #clientes
    FROM dbo.ClienteSap cs;

    CREATE CLUSTERED INDEX IX_tmp_clientes_cliente ON #clientes (Cliente);
    CREATE INDEX IX_tmp_clientes_vendedor ON #clientes (VendedorId);
    CREATE INDEX IX_tmp_clientes_canal ON #clientes (U_CANAL);

    SELECT
        o.Id,
        Cliente    = UPPER(LTRIM(RTRIM(o.Cliente))),
        o.VendedorId,
        o.Estatus,
        o.Serie,
        FechaDate = TRY_CONVERT(date, o.FechaEntrega)
    INTO #ov
    FROM dbo.OrdenVenta o
    INNER JOIN dbo.Series ser
        ON o.Serie = ser.NombreSerie
    WHERE o.FechaEntrega IS NOT NULL
      AND o.Estatus BETWEEN 1 AND 5
      AND ser.Sucursal = 'MATRIZ';

    CREATE CLUSTERED INDEX IX_tmp_ov ON #ov (Id);
    CREATE INDEX IX_tmp_ov_cliente_fecha ON #ov (Cliente, FechaDate);
    CREATE INDEX IX_tmp_ov_vendedor_fecha ON #ov (VendedorId, FechaDate);

    SELECT DISTINCT o.Id
    INTO #ov_con_surtido
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat;

    CREATE UNIQUE CLUSTERED INDEX IX_tmp_ov_con_surtido ON #ov_con_surtido (Id);

    SELECT
        PedidoId = op.PedidoId,
        SKU      = UPPER(LTRIM(RTRIM(op.ProductoCodigo))),
        KgPedido = SUM(CAST(op.Peso AS DECIMAL(18,4)))
    INTO #ov_peso_agg
    FROM dbo.OrdenVentaProducto op
    GROUP BY op.PedidoId, UPPER(LTRIM(RTRIM(op.ProductoCodigo)));

    CREATE CLUSTERED INDEX IX_tmp_ov_peso_agg ON #ov_peso_agg (PedidoId, SKU);

    SELECT
        PedidoId = o.Id,
        SKU      = UPPER(LTRIM(RTRIM(sd.Articulo))),
        KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
    INTO #ov_surtido_agg
    FROM dbo.OrdenVenta o
    JOIN dbo.Subpedido sp ON sp.OrdenVentaId = o.Id
    JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
    JOIN dbo.SurtidoDetalle sd ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
    WHERE se.FechaValidacion IS NOT NULL
    GROUP BY o.Id, UPPER(LTRIM(RTRIM(sd.Articulo)));

    CREATE CLUSTERED INDEX IX_tmp_ov_surtido_agg ON #ov_surtido_agg (PedidoId, SKU);

    SELECT
        ov.Id,
        ov.Cliente,
        ov.VendedorId,
        ov.Estatus,
        ov.FechaDate,
        p.SKU,
        KgPendiente = CAST(
            CASE
                WHEN ov.Estatus = 5 AND os.Id IS NOT NULL THEN 0
                ELSE CASE
                    WHEN (p.KgPedido - ISNULL(sa.KgSurtido, 0)) < 0 THEN 0
                    ELSE (p.KgPedido - ISNULL(sa.KgSurtido, 0))
                END
            END
        AS DECIMAL(18,4))
    INTO #ov_pendiente_sku
    FROM #ov ov
    JOIN #ov_peso_agg p ON p.PedidoId = ov.Id
    LEFT JOIN #ov_surtido_agg sa ON sa.PedidoId = ov.Id AND sa.SKU = p.SKU
    LEFT JOIN #ov_con_surtido os ON os.Id = ov.Id;

    CREATE INDEX IX_tmp_ov_pendiente_cliente ON #ov_pendiente_sku (Cliente, SKU, FechaDate);
    CREATE INDEX IX_tmp_ov_pendiente_vendedor ON #ov_pendiente_sku (VendedorId, SKU, FechaDate);

    SELECT
        TransferenciaId,
        SKU = UPPER(LTRIM(RTRIM(Sku))),
        KgSurtido = SUM(CAST(KgSurtido AS DECIMAL(18,4)))
    INTO #tr_surtido_agg
    FROM dbo.TransferenciaSurtido
    GROUP BY TransferenciaId, UPPER(LTRIM(RTRIM(Sku)));

    CREATE CLUSTERED INDEX IX_tmp_tr_surtido_agg ON #tr_surtido_agg (TransferenciaId, SKU);

    SELECT
        ArticuloCodigo = UPPER(LTRIM(RTRIM(b.Articulo))),
        Mes            = MONTH(a.FechaValidacion),
        Anio           = YEAR(a.FechaValidacion),
        VendedorId     = cs.VendedorId,
        U_CANAL        = UPPER(LTRIM(RTRIM(cs.U_CANAL))),
        KgVendidos     = SUM(CAST(b.Kg AS DECIMAL(18,4)))
    INTO #venta_real_base
    FROM dbo.SurtidoEncabezado a
    INNER JOIN dbo.SurtidoDetalle b
        ON a.SolicitudSurtidoId = b.SolicitudSurtidoId
    LEFT JOIN dbo.ClienteSap cs
        ON cs.Cliente = a.CodigoSap
    WHERE a.FechaValidacion IS NOT NULL
    GROUP BY
        UPPER(LTRIM(RTRIM(b.Articulo))),
        MONTH(a.FechaValidacion),
        YEAR(a.FechaValidacion),
        cs.VendedorId,
        UPPER(LTRIM(RTRIM(cs.U_CANAL)));

    CREATE INDEX IX_tmp_venta_real_base_canal
        ON #venta_real_base (U_CANAL, ArticuloCodigo, Mes, Anio);

    CREATE INDEX IX_tmp_venta_real_base_vendedor
        ON #venta_real_base (VendedorId, ArticuloCodigo, Mes, Anio);

    SELECT
        U_CANAL,
        ArticuloCodigo,
        Mes,
        Anio,
        KgVendidos = SUM(KgVendidos)
    INTO #venta_real_cedis
    FROM #venta_real_base
    WHERE ISNULL(U_CANAL, '') LIKE 'CEDIS%'
    GROUP BY
        U_CANAL,
        ArticuloCodigo,
        Mes,
        Anio;

    CREATE INDEX IX_tmp_venta_real_cedis
        ON #venta_real_cedis (U_CANAL, ArticuloCodigo, Mes, Anio);

    SELECT
        VendedorId,
        ArticuloCodigo,
        Mes,
        Anio,
        KgVendidos = SUM(KgVendidos)
    INTO #venta_real_vendedor
    FROM #venta_real_base
    WHERE VendedorId IS NOT NULL
    GROUP BY
        VendedorId,
        ArticuloCodigo,
        Mes,
        Anio;

    CREATE INDEX IX_tmp_venta_real_vendedor
        ON #venta_real_vendedor (VendedorId, ArticuloCodigo, Mes, Anio);

    CREATE TABLE #base_final
    (
        Origen NVARCHAR(20),
        MesConsulta INT,
        AnioConsulta INT,
        Canal NVARCHAR(100) NULL,
        VendedorId INT NULL,
        VendedorNombre NVARCHAR(200) NULL,
        ProductoCodigo NVARCHAR(50),
        ProductoNombre NVARCHAR(255) NULL,
        PresupuestoAsignado DECIMAL(18,4),
        KgPedidosMes DECIMAL(18,4),
        KgVendidos DECIMAL(18,4)
    );

    IF @TipoReporte = 'CEDIS'
    BEGIN
        SELECT
            Canal = UPPER(LTRIM(RTRIM(pc.Canal))),
            SKU   = UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
            Mes   = pc.Mes,
            Anio  = pc.Anio,
            Presupuesto = SUM(pc.PresupuestoAsignado)
        INTO #presupuestos_cedis
        FROM dbo.PresupuestoCedis pc
        WHERE pc.Mes = @Mes
          AND pc.Anio = @Anio
          AND UPPER(LTRIM(RTRIM(pc.Canal))) = @Canal
        GROUP BY
            UPPER(LTRIM(RTRIM(pc.Canal))),
            UPPER(LTRIM(RTRIM(pc.ProductoCodigo))),
            pc.Mes,
            pc.Anio;

        CREATE CLUSTERED INDEX IX_tmp_presupuestos_cedis ON #presupuestos_cedis (Canal, SKU, Mes, Anio);

        SELECT
            Canal,
            SKU,
            Mes,
            Anio,
            Kg = SUM(Kg)
        INTO #consumo_cedis_base
        FROM
        (
            SELECT
                Canal = cli.U_CANAL,
                SKU   = ovp.SKU,
                Mes   = MONTH(ovp.FechaDate),
                Anio  = YEAR(ovp.FechaDate),
                Kg    = SUM(ovp.KgPendiente)
            FROM #ov_pendiente_sku ovp
            JOIN #clientes cli ON cli.Cliente = ovp.Cliente
            WHERE cli.U_CANAL LIKE 'CEDIS%'
            GROUP BY cli.U_CANAL, ovp.SKU, MONTH(ovp.FechaDate), YEAR(ovp.FechaDate)

            UNION ALL

            SELECT
                Canal = UPPER(LTRIM(RTRIM(s.Canal))),
                SKU   = UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
                Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
                Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
                Kg    = SUM(
                            CASE
                                WHEN (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0)) < 0 THEN 0
                                ELSE (CAST(td.CantidadKg AS DECIMAL(18,4)) - ISNULL(tsa.KgSurtido, 0))
                            END
                       )
            FROM dbo.Transferencias t
            JOIN dbo.TransferenciaDetalles td ON td.TransferenciaId = t.Id
            JOIN dbo.Series s ON s.Sucursal = t.Sucursal
            LEFT JOIN #tr_surtido_agg tsa
                ON tsa.TransferenciaId = t.Id
               AND tsa.SKU = UPPER(LTRIM(RTRIM(td.ProductoCodigo)))
            WHERE t.FechaSolicitud IS NOT NULL
              AND t.Estatus BETWEEN 1 AND 4
              AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
            GROUP BY
                UPPER(LTRIM(RTRIM(s.Canal))),
                UPPER(LTRIM(RTRIM(td.ProductoCodigo))),
                MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
                YEAR(TRY_CONVERT(date, t.FechaSolicitud))
        ) X
        WHERE Mes = @Mes
          AND Anio = @Anio
          AND UPPER(LTRIM(RTRIM(Canal))) = @Canal
        GROUP BY Canal, SKU, Mes, Anio;

        CREATE CLUSTERED INDEX IX_tmp_consumo_cedis_base ON #consumo_cedis_base (Canal, SKU, Mes, Anio);

        SELECT
            'CEDIS' AS Origen,
            pc.Mes,
            pc.Anio,
            pc.Canal,
            CAST(NULL AS INT) AS VendedorId,
            pc.SKU,
            pc.Presupuesto,
            ISNULL(cc.Kg, 0) AS Kg
        INTO #todo_cedis
        FROM #presupuestos_cedis pc
        LEFT JOIN #consumo_cedis_base cc
            ON cc.Canal = pc.Canal
           AND cc.SKU   = pc.SKU
           AND cc.Mes   = pc.Mes
           AND cc.Anio  = pc.Anio;

        CREATE INDEX IX_tmp_todo_cedis ON #todo_cedis (Mes, Anio, Canal, SKU);

        SELECT
            'CEDIS' AS Origen,
            vr.Mes,
            vr.Anio,
            vr.U_CANAL AS Canal,
            CAST(NULL AS INT) AS VendedorId,
            vr.ArticuloCodigo AS SKU,
            CAST(0 AS DECIMAL(18,4)) AS Presupuesto,
            CAST(0 AS DECIMAL(18,4)) AS Kg
        INTO #todo_cedis_venta_real_extra
        FROM #venta_real_cedis vr
        WHERE ISNULL(vr.U_CANAL, '') LIKE 'CEDIS%'
          AND vr.Mes = @Mes
          AND vr.Anio = @Anio
          AND UPPER(LTRIM(RTRIM(vr.U_CANAL))) = @Canal
          AND NOT EXISTS
          (
              SELECT 1
              FROM #todo_cedis tc
              WHERE tc.Canal = vr.U_CANAL
                AND tc.SKU   = vr.ArticuloCodigo
                AND tc.Mes   = vr.Mes
                AND tc.Anio  = vr.Anio
          );

        CREATE INDEX IX_tmp_todo_cedis_venta_real_extra ON #todo_cedis_venta_real_extra (Mes, Anio, Canal, SKU);

        SELECT
            Canal = cli.U_CANAL,
            SKU   = UPPER(LTRIM(RTRIM(sd.Articulo))),
            Mes   = MONTH(se.FechaValidacion),
            Anio  = YEAR(se.FechaValidacion),
            KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
        INTO #surtido_ov_cedis
        FROM dbo.OrdenVenta o
        JOIN dbo.Subpedido sp ON sp.OrdenVentaId = o.Id
        JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
        JOIN dbo.SurtidoDetalle sd ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
        JOIN #clientes cli ON cli.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
        WHERE o.Estatus <> 0
          AND se.FechaValidacion IS NOT NULL
          AND cli.U_CANAL LIKE 'CEDIS%'
        GROUP BY cli.U_CANAL, UPPER(LTRIM(RTRIM(sd.Articulo))), MONTH(se.FechaValidacion), YEAR(se.FechaValidacion);

        CREATE CLUSTERED INDEX IX_tmp_surtido_ov_cedis ON #surtido_ov_cedis (Canal, SKU, Mes, Anio);

        SELECT
            Canal = UPPER(LTRIM(RTRIM(s.Canal))),
            SKU   = UPPER(LTRIM(RTRIM(ts.Sku))),
            Mes   = MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
            Anio  = YEAR(TRY_CONVERT(date, t.FechaSolicitud)),
            KgSurtido = SUM(CAST(ts.KgSurtido AS DECIMAL(18,4)))
        INTO #surtido_transferencias_cedis
        FROM dbo.TransferenciaSurtido ts
        JOIN dbo.Transferencias t ON t.Id = ts.TransferenciaId
        JOIN dbo.Series s ON s.Sucursal = t.Sucursal
        WHERE t.FechaSolicitud IS NOT NULL
          AND t.Estatus >= 5
          AND ts.KgSurtido > 0
          AND UPPER(LTRIM(RTRIM(s.Canal))) LIKE 'CEDIS%'
        GROUP BY
            UPPER(LTRIM(RTRIM(s.Canal))),
            UPPER(LTRIM(RTRIM(ts.Sku))),
            MONTH(TRY_CONVERT(date, t.FechaSolicitud)),
            YEAR(TRY_CONVERT(date, t.FechaSolicitud));

        CREATE CLUSTERED INDEX IX_tmp_surtido_transferencias_cedis ON #surtido_transferencias_cedis (Canal, SKU, Mes, Anio);

        SELECT
            Canal,
            SKU,
            Mes,
            Anio,
            KgSurtido = SUM(KgSurtido)
        INTO #surtido_cedis_base
        FROM
        (
            SELECT * FROM #surtido_ov_cedis
            UNION ALL
            SELECT * FROM #surtido_transferencias_cedis
        ) x
        WHERE Mes = @Mes
          AND Anio = @Anio
          AND UPPER(LTRIM(RTRIM(Canal))) = @Canal
        GROUP BY Canal, SKU, Mes, Anio;

        CREATE CLUSTERED INDEX IX_tmp_surtido_cedis_base ON #surtido_cedis_base (Canal, SKU, Mes, Anio);

        SELECT Canal, SKU, Mes, Anio, KgSurtido
        INTO #surtido_real_cedis
        FROM #surtido_cedis_base;

        CREATE CLUSTERED INDEX IX_tmp_surtido_real_cedis ON #surtido_real_cedis (Canal, SKU, Mes, Anio);

        INSERT INTO #base_final
        (
            Origen, MesConsulta, AnioConsulta, Canal, VendedorId, VendedorNombre,
            ProductoCodigo, ProductoNombre, PresupuestoAsignado, KgPedidosMes, KgVendidos
        )
        SELECT
            t.Origen,
            t.Mes AS MesConsulta,
            t.Anio AS AnioConsulta,
            t.Canal,
            CAST(NULL AS INT) AS VendedorId,
            CAST(NULL AS NVARCHAR(100)) AS VendedorNombre,
            t.SKU AS ProductoCodigo,
            prd.ProductoNombre,
            CAST(t.Presupuesto AS DECIMAL(18,4)) AS PresupuestoAsignado,
            CAST(t.Kg AS DECIMAL(18,4)) AS KgPedidosMes,
            CAST(ISNULL(vr.KgVendidos, 0) AS DECIMAL(18,4)) AS KgVendidos
        FROM
        (
            SELECT * FROM #todo_cedis
            UNION ALL
            SELECT * FROM #todo_cedis_venta_real_extra
        ) t
        LEFT JOIN #productos prd
            ON prd.SKU = t.SKU
        LEFT JOIN #venta_real_cedis vr
            ON vr.U_CANAL = t.Canal
           AND vr.ArticuloCodigo = t.SKU
           AND vr.Mes = t.Mes
           AND vr.Anio = t.Anio
        WHERE t.Mes = @Mes
          AND t.Anio = @Anio
          AND UPPER(LTRIM(RTRIM(t.Canal))) = @Canal;

        SELECT
            Canal,
            Objetivo = SUM(PresupuestoAsignado),
            Vendido = SUM(KgVendidos),
            AvancePct = CAST(
                CASE
                    WHEN SUM(PresupuestoAsignado) <= 0 THEN 0
                    ELSE (SUM(KgVendidos) * 100.0) / SUM(PresupuestoAsignado)
                END
            AS DECIMAL(18,2))
        FROM #base_final
        GROUP BY Canal;

        SELECT TOP 10
            Canal,
            ProductoCodigo,
            ProductoNombre,
            PresupuestoAsignado,
            Vendido = KgVendidos,
            DisponibleVenta = CASE
                WHEN (PresupuestoAsignado - KgVendidos) < 0 THEN 0
                ELSE (PresupuestoAsignado - KgVendidos)
            END
        FROM #base_final
        WHERE PresupuestoAsignado > 0
          AND KgVendidos = 0
        ORDER BY PresupuestoAsignado DESC, ProductoCodigo;
    END
    ELSE
    BEGIN
        SELECT
            VendedorId,
            SKU = UPPER(LTRIM(RTRIM(pv.ProductoCodigo))),
            Mes = pv.Mes,
            Anio = pv.Anio,
            Presupuesto = SUM(pv.PresupuestoAsignado)
        INTO #presupuestos_vendedor
        FROM dbo.PresupuestoVendedor pv
        WHERE pv.Mes = @Mes
          AND pv.Anio = @Anio
          AND pv.VendedorId = @VendedorId
        GROUP BY pv.VendedorId, UPPER(LTRIM(RTRIM(pv.ProductoCodigo))), pv.Mes, pv.Anio;

        CREATE CLUSTERED INDEX IX_tmp_presupuestos_vendedor ON #presupuestos_vendedor (VendedorId, SKU, Mes, Anio);

        SELECT
            ovp.VendedorId,
            SKU  = ovp.SKU,
            Mes  = MONTH(ovp.FechaDate),
            Anio = YEAR(ovp.FechaDate),
            Kg   = SUM(ovp.KgPendiente)
        INTO #consumo_vendedor_base
        FROM #ov_pendiente_sku ovp
        WHERE ovp.VendedorId = @VendedorId
        GROUP BY ovp.VendedorId, ovp.SKU, MONTH(ovp.FechaDate), YEAR(ovp.FechaDate);

        CREATE CLUSTERED INDEX IX_tmp_consumo_vendedor_base ON #consumo_vendedor_base (VendedorId, SKU, Mes, Anio);

        SELECT
            'VENDEDOR' AS Origen,
            pv.Mes,
            pv.Anio,
            CAST(NULL AS NVARCHAR(100)) AS Canal,
            pv.VendedorId,
            pv.SKU,
            pv.Presupuesto,
            ISNULL(cv.Kg, 0) AS Kg
        INTO #todo_vendedor
        FROM #presupuestos_vendedor pv
        LEFT JOIN #consumo_vendedor_base cv
            ON cv.VendedorId = pv.VendedorId
           AND cv.SKU = pv.SKU
           AND cv.Mes = pv.Mes
           AND cv.Anio = pv.Anio;

        CREATE INDEX IX_tmp_todo_vendedor ON #todo_vendedor (Mes, Anio, VendedorId, SKU);

        SELECT
            'VENDEDOR' AS Origen,
            vr.Mes,
            vr.Anio,
            CAST(NULL AS NVARCHAR(100)) AS Canal,
            vr.VendedorId,
            vr.ArticuloCodigo AS SKU,
            CAST(0 AS DECIMAL(18,4)) AS Presupuesto,
            CAST(0 AS DECIMAL(18,4)) AS Kg
        INTO #todo_vendedor_venta_real_extra
        FROM #venta_real_vendedor vr
        WHERE vr.VendedorId = @VendedorId
          AND vr.Mes = @Mes
          AND vr.Anio = @Anio
          AND NOT EXISTS
          (
              SELECT 1
              FROM #todo_vendedor tv
              WHERE tv.VendedorId = vr.VendedorId
                AND tv.SKU = vr.ArticuloCodigo
                AND tv.Mes = vr.Mes
                AND tv.Anio = vr.Anio
          );

        CREATE INDEX IX_tmp_todo_vendedor_venta_real_extra ON #todo_vendedor_venta_real_extra (Mes, Anio, VendedorId, SKU);

        SELECT
            cl.VendedorId,
            SKU = UPPER(LTRIM(RTRIM(sd.Articulo))),
            Mes = MONTH(se.FechaValidacion),
            Anio = YEAR(se.FechaValidacion),
            KgSurtido = SUM(CAST(sd.Kg AS DECIMAL(18,4)))
        INTO #surtido_vendedor_base
        FROM dbo.OrdenVenta o
        JOIN dbo.Subpedido sp ON sp.OrdenVentaId = o.Id
        JOIN dbo.SurtidoEncabezado se ON se.SolicitudSurtidoId = sp.U_DocMeat
        JOIN dbo.SurtidoDetalle sd ON sd.SolicitudSurtidoId = se.SolicitudSurtidoId
        JOIN #clientes cl ON cl.Cliente = UPPER(LTRIM(RTRIM(o.Cliente)))
        WHERE o.Estatus <> 0
          AND se.FechaValidacion IS NOT NULL
          AND cl.VendedorId = @VendedorId
        GROUP BY cl.VendedorId, UPPER(LTRIM(RTRIM(sd.Articulo))), MONTH(se.FechaValidacion), YEAR(se.FechaValidacion);

        CREATE CLUSTERED INDEX IX_tmp_surtido_vendedor_base ON #surtido_vendedor_base (VendedorId, SKU, Mes, Anio);

        SELECT VendedorId, SKU, Mes, Anio, KgSurtido
        INTO #surtido_real_vendedor
        FROM #surtido_vendedor_base;

        CREATE CLUSTERED INDEX IX_tmp_surtido_real_vendedor ON #surtido_real_vendedor (VendedorId, SKU, Mes, Anio);

        INSERT INTO #base_final
        (
            Origen, MesConsulta, AnioConsulta, Canal, VendedorId, VendedorNombre,
            ProductoCodigo, ProductoNombre, PresupuestoAsignado, KgPedidosMes, KgVendidos
        )
        SELECT
            t.Origen,
            t.Mes AS MesConsulta,
            t.Anio AS AnioConsulta,
            CAST(NULL AS NVARCHAR(100)) AS Canal,
            t.VendedorId,
            ISNULL(c.VendedorNombre, '-') AS VendedorNombre,
            t.SKU AS ProductoCodigo,
            prd.ProductoNombre,
            CAST(t.Presupuesto AS DECIMAL(18,4)) AS PresupuestoAsignado,
            CAST(t.Kg AS DECIMAL(18,4)) AS KgPedidosMes,
            CAST(ISNULL(vr.KgVendidos, 0) AS DECIMAL(18,4)) AS KgVendidos
        FROM
        (
            SELECT * FROM #todo_vendedor
            UNION ALL
            SELECT * FROM #todo_vendedor_venta_real_extra
        ) t
        LEFT JOIN #productos prd
            ON prd.SKU = t.SKU
        LEFT JOIN #venta_real_vendedor vr
            ON vr.VendedorId = t.VendedorId
           AND vr.ArticuloCodigo = t.SKU
           AND vr.Mes = t.Mes
           AND vr.Anio = t.Anio
        LEFT JOIN
        (
            SELECT DISTINCT VendedorId, VendedorNombre
            FROM #clientes
            WHERE VendedorId IS NOT NULL
        ) c
            ON c.VendedorId = t.VendedorId
        WHERE t.Mes = @Mes
          AND t.Anio = @Anio
          AND t.VendedorId = @VendedorId;

        SELECT
            VendedorId,
            MAX(VendedorNombre) AS VendedorNombre,
            Objetivo = SUM(PresupuestoAsignado),
            Vendido = SUM(KgVendidos),
            AvancePct = CAST(
                CASE
                    WHEN SUM(PresupuestoAsignado) <= 0 THEN 0
                    ELSE (SUM(KgVendidos) * 100.0) / SUM(PresupuestoAsignado)
                END
            AS DECIMAL(18,2))
        FROM #base_final
        GROUP BY VendedorId;

        SELECT TOP 10
            VendedorId,
            VendedorNombre,
            ProductoCodigo,
            ProductoNombre,
            PresupuestoAsignado,
            Vendido = KgVendidos,
            DisponibleVenta = CASE
                WHEN (PresupuestoAsignado - KgVendidos) < 0 THEN 0
                ELSE (PresupuestoAsignado - KgVendidos)
            END
        FROM #base_final
        WHERE PresupuestoAsignado > 0
          AND KgVendidos = 0
        ORDER BY PresupuestoAsignado DESC, ProductoCodigo;
    END
END
GO



EXEC dbo.sp_WhatsAppReporte
    @TipoReporte = 'CEDIS',
    @Canal = 'CEDIS-MDA',
    @Mes = 3,
    @Anio = 2026;

    EXEC dbo.sp_WhatsAppReporte
    @TipoReporte = 'VENDEDOR',
    @VendedorId = 28,
    @Mes = 3,
    @Anio = 2026;