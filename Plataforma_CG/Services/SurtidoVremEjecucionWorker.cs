using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plataforma_CG.Services;

/// <summary>
/// Ejecutor DEV TIF, posterior a la autorización; no usa SAP por sí mismo.
/// La versión instalada de Meat.meat_SalidaEmbarque DEBE haber sido auditada
/// para excluir todas las llamadas SAP, directas e indirectas.
/// La huella aprobada impide ejecutar otra versión del SP por accidente.
/// No marca la cola completada: eso corresponde al conciliador existente.
/// No reintenta si hay evidencia de un intento anterior.
/// </summary>
public sealed class SurtidoVremEjecucionWorker : BackgroundService
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<SurtidoVremEjecucionWorker> _log;
    public SurtidoVremEjecucionWorker(IConfiguration cfg, ILogger<SurtidoVremEjecucionWorker> log)
    { _cfg = cfg; _log = log; }

    private sealed class Candidato
    {
        public long ColaId { get; set; }
        public long CapturaId { get; set; }
        public int SolicitudSurtidoId { get; set; }
        public string Pedido { get; set; } = "";
        public string Planta { get; set; } = "";
        public string CodigoAlmacen { get; set; } = "";
    }

    private sealed class Preflight
    {
        public int Cajas { get; set; }
        public decimal KgReales { get; set; }
        public decimal KgPlaneados { get; set; }
        public decimal DiferenciaPorcentaje { get; set; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_cfg.GetValue<bool>("SurtidoVremEjecucion:Habilitado"))
                    await EjecutarCicloAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogError(ex, "Falló ciclo de ejecución VREM; no se reintenta a ciegas."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(
                _cfg.GetValue<int?>("SurtidoVremEjecucion:IntervaloSegundos") ?? 20, 15, 3600)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task EjecutarCicloAsync(CancellationToken ct)
    {
        var cs = _cfg.GetConnectionString("CadenaSQLSIGO") ??
                 throw new InvalidOperationException("Falta CadenaSQLSIGO.");
        await using var sigo = new SqlConnection(cs);
        await sigo.OpenAsync(ct);

        var minimo = Math.Max(7, _cfg.GetValue<int?>("SurtidoVremEjecucion:ColaIdMinima") ?? 7);
        int maximo = Math.Clamp(_cfg.GetValue<int?>("SurtidoVremEjecucion:MaximoPorCiclo") ?? 1, 1, 5);
        var candidatas = await sigo.QueryAsync<Candidato>(new CommandDefinition(@"
SELECT TOP (@Maximo)
 q.Id ColaId, c.Id CapturaId, c.SolicitudSurtidoId, c.Pedido, c.Planta, c.CodigoAlmacen
FROM dbo.SurtidoIntegracionCola q
JOIN dbo.SurtidoCaptura c ON c.Id=q.SurtidoCapturaId
JOIN dbo.SurtidoIntegracionEvaluacion e ON e.ColaId=q.Id AND e.Estado='REVISION_COMERCIAL'
WHERE q.Id>=@Minimo AND q.Tipo='SALIDA_EMBARQUE'
  AND q.Estatus='PENDIENTE' AND c.Estatus='VALIDADO'
  AND c.Planta='TIF'
  -- Si hay una autorización pendiente o rechazada, no ejecutar.
  -- Si no hay autorización, el preflight SQL comprobará la tolerancia.
  AND (EXISTS (SELECT 1 FROM dbo.SurtidoAutorizacionKg a
               WHERE a.ColaId=q.Id AND a.SolicitudSurtidoId=c.SolicitudSurtidoId
                 AND a.FolioVPED=c.Pedido AND a.Estado='APROBADA')
       OR NOT EXISTS (SELECT 1 FROM dbo.SurtidoAutorizacionKg a WHERE a.ColaId=q.Id))
  AND NOT EXISTS(SELECT 1 FROM dbo.
v WHERE v.ColaId=q.Id)
ORDER BY q.Id;", new { Minimo = minimo, Maximo = maximo }, cancellationToken: ct));

        foreach (var c in candidatas)
        {
            ct.ThrowIfCancellationRequested();
            try { await EjecutarUnaAsync(c, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _log.LogError(ex, "VREM cola {Cola}: detenida. Revisar estado externo ANTES de cualquier reintento.", c.ColaId);
            }
        }
    }

    private async Task EjecutarUnaAsync(Candidato c, CancellationToken ct)
    {
        // En una primera fase sólo la planta TIF DEV, misma instancia para SIGO/Meat/CommerciaNET.
        // No se utiliza la cadena de conexión de producción por ningún motivo.
        var cs = _cfg.GetConnectionString("CadenaMeatTIF") ??
                 throw new InvalidOperationException("Falta CadenaMeatTIF.");
        var builder = new SqlConnectionStringBuilder(cs);
        if (!builder.InitialCatalog.Equals("TIF_Meat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La cadena Meat TIF no apunta a TIF_Meat.");
        await using var meat = new SqlConnection(builder.ConnectionString);
        await meat.OpenAsync(ct);
        var maquina = await meat.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'));", cancellationToken: ct));
        if (!string.Equals(maquina, "WIN-SERVERDEVSIGO", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ejecución real VREM bloqueada: servidor no es WIN-SERVERDEVSIGO.");

        // SIGO y Meat deben residir en la misma máquina DEV: la validación SQL
        // posterior usa nombres de bases de tres partes en la instancia local.
        await using (var sigoVerificacion = new SqlConnection(
            _cfg.GetConnectionString("CadenaSQLSIGO") ??
            throw new InvalidOperationException("Falta CadenaSQLSIGO.")))
        {
            await sigoVerificacion.OpenAsync(ct);
            var origenSigo = await sigoVerificacion.QuerySingleAsync<dynamic>(
                new CommandDefinition(@"SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')) AS Maquina,
                                            DB_NAME() AS Base;", cancellationToken: ct));
            if (!string.Equals(Convert.ToString(origenSigo.Maquina), maquina, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Convert.ToString(origenSigo.Base), "SIGO", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CadenaSQLSIGO no apunta a SIGO de la misma máquina DEV.");
        }

        string? definicion = await meat.ExecuteScalarAsync<string>(new CommandDefinition(@"
SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.meat_SalidaEmbarque'));", cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(definicion))
            throw new InvalidOperationException("No puede leerse definición del procedimiento Meat.");
        string hashReal = Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(definicion)));
        string hashAprobado = (_cfg["SurtidoVremEjecucion:HashSha256ProcedimientoAprobado"] ?? "").Trim();
        if (hashAprobado.Length != 64 || !hashReal.Equals(hashAprobado, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La definición de meat_SalidaEmbarque no coincide con la revisión DEV sin SAP aprobada. Hash observado: " + hashReal);
        // Validación adicional (no sustituye auditoría de código del SP y procedimientos indirectos).
        int dependenciaSap = await meat.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT COUNT(*) FROM sys.sql_expression_dependencies
WHERE referencing_id=OBJECT_ID('dbo.meat_SalidaEmbarque')
AND (referenced_entity_name LIKE '%SAP%' OR referenced_database_name IN ('Meat','CommerciaNET'));",
            cancellationToken: ct));
        if (dependenciaSap > 0) throw new InvalidOperationException("Procedimiento Meat tiene dependencias SAP o de bases no DEV.");

        int usuarioMeat = _cfg.GetValue<int>("SurtidoVremEjecucion:UsuarioIntegracionMeatId");
        string device = (_cfg["SurtidoVremEjecucion:DeviceId"] ?? "").Trim();
        if (usuarioMeat <= 0 || device.Length < 3)
            throw new InvalidOperationException("Configura usuario Meat y DeviceId autorizados.");

        // AppLock de SESIÓN, retenido en la misma conexión durante toda la operación
        // incluida la ejecución potencialmente irreversible del SP.
        string recurso = "SIGO_VREM_DEV_SOLICITUD_" + c.SolicitudSurtidoId;
        int lockResult = await meat.ExecuteScalarAsync<int>(new CommandDefinition(@"
DECLARE @r int;
EXEC @r=sys.sp_getapplock @Resource=@Recurso, @LockMode='Exclusive',
@LockOwner='Session', @LockTimeout=0;
SELECT @r;", new { Recurso = recurso }, cancellationToken: ct));
        if (lockResult < 0) { _log.LogWarning("Solicitud {Id}: otro ejecutor posee el bloqueo.", c.SolicitudSurtidoId); return; }

        try
        {
            // Toda esta validación es fail-closed: no insertar logs ante cualquier diferencia.
            var previo = await meat.QuerySingleAsync<Preflight>(new CommandDefinition(ValidacionSql,
                new { c.ColaId, c.CapturaId, c.SolicitudSurtidoId, c.Pedido, c.CodigoAlmacen,
                    UsuarioIntegracion = usuarioMeat,
                    Tolerancia = _cfg.GetValue<decimal?>("SurtidoIntegracion:ToleranciaKgPorcentaje") ?? 0m,
                    ExcepcionHabilitada = _cfg.GetValue<bool>("SurtidoIntegracion:HabilitarAutorizacionExcepcional"),
                    MaxExcepcion = _cfg.GetValue<decimal?>("SurtidoIntegracion:MaximoDiferenciaAutorizablePorcentaje") ?? 0m },
                commandTimeout: 90, cancellationToken: ct));
            _log.LogInformation("VREM DEV verificada. Cola={Cola}, cajas={Cajas}, kg={Kg}, difPct={Pct}",
                c.ColaId, previo.Cajas, previo.KgReales, previo.DiferenciaPorcentaje);

            if (!_cfg.GetValue<bool>("SurtidoVremEjecucion:PermitirEscriturasIrreversibles"))
            {
                _log.LogWarning("Cola {Cola}: prevalidación correcta; modo DIAGNOSTICO, no genera remisión.", c.ColaId);
                return;
            }

            // La prevalidación de solo lectura puede ejecutarse sin aislamiento.
            // Las escrituras siguen BLOQUEADAS si no está verificado.
            if (!_cfg.GetValue<bool>("SurtidoVremEjecucion:AislamientoSapVerificado"))
                throw new InvalidOperationException(
                    "Prevalidación correcta, pero generación bloqueada: aislamiento SAP no verificado. " +
                    "No se insertaron ProduccionLog ni se ejecutó SalidaEmbarque.");

            // Revalidar bajo lock, dentro de la transacción de inserción de logs.
            // No se reintenta luego de crear logs: el SP puede haber ejecutado parcialmente.
            await using (var tx = (SqlTransaction)await meat.BeginTransactionAsync(ct))
            {
                try
                {
                    await meat.QuerySingleAsync<Preflight>(new CommandDefinition(ValidacionSql,
                        new { c.ColaId, c.CapturaId, c.SolicitudSurtidoId, c.Pedido, c.CodigoAlmacen,
                            UsuarioIntegracion = usuarioMeat,
                            Tolerancia = _cfg.GetValue<decimal?>("SurtidoIntegracion:ToleranciaKgPorcentaje") ?? 0m,
                            ExcepcionHabilitada = _cfg.GetValue<bool>("SurtidoIntegracion:HabilitarAutorizacionExcepcional"),
                            MaxExcepcion = _cfg.GetValue<decimal?>("SurtidoIntegracion:MaximoDiferenciaAutorizablePorcentaje") ?? 0m },
                        tx, 90, cancellationToken: ct));
                    int insertados = await meat.ExecuteAsync(new CommandDefinition(@"
INSERT dbo.ProduccionLog
(ProduccionLogId,ProduccionId,ProcesoId,CodigoEtiqueta,Almacen,Articulo,
 Unidades,Peso,UsuarioId,DeviceId,FechaHora,FechaHoraServer)
SELECT @SolicitudSurtidoId,m.ProduccionId,17,m.CodigoEtiqueta,p.Almacen,m.SKU,
       1,m.KgSIGO,@UsuarioIntegracion,@DeviceId,GETDATE(),NULL
FROM SIGO.dbo.SurtidoIntegracionManifiesto m
JOIN dbo.Produccion p ON p.ProduccionId=m.ProduccionId
WHERE m.ColaId=@ColaId AND m.SolicitudSurtidoId=@SolicitudSurtidoId;",
                        new { c.ColaId,c.SolicitudSurtidoId,UsuarioIntegracion=usuarioMeat,DeviceId=device }, tx, 90, cancellationToken: ct));
                    if (insertados != previo.Cajas)
                        throw new InvalidOperationException("No se insertó la totalidad del manifiesto.");
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    try { await tx.RollbackAsync(CancellationToken.None); }
                    catch (Exception rollbackEx)
                    {
                        _log.LogError(rollbackEx,
                            "Cola {Cola}: no se pudo confirmar rollback; revisar estado de ProduccionLog antes de reintentar.",
                            c.ColaId);
                    }
                    throw;
                }
            }

            _log.LogWarning("Cola {Cola}: logs de embarque registrados. NO volver a insertar ni reintentar automáticamente.", c.ColaId);
            // El SP no acepta los objetos por caja; consume los ProduccionLog precargados.
            // No ejecutar ningún SP de SAP aquí.
            await meat.ExecuteAsync(new CommandDefinition(@"
EXEC dbo.meat_SalidaEmbarque
  @ProcesoId=17,@ProduccionLogId=@SolicitudSurtidoId,@DesactivaTarimas=0;",
                new { c.SolicitudSurtidoId }, commandTimeout: 240, cancellationToken: ct));

            _log.LogInformation("Cola {Cola}: SP Meat ejecutado. El conciliador verificará VREM, referencias y cajas.", c.ColaId);
        }
        finally
        {
            try
            {
                await meat.ExecuteAsync(new CommandDefinition(@"
EXEC sys.sp_releaseapplock @Resource=@Recurso,@LockOwner='Session';",
                    new { Recurso = recurso }, commandTimeout: 10,
                    cancellationToken: CancellationToken.None));
            }
            catch (Exception ex)
            {
                // Nunca ocultar con un fallo de limpieza el error original del SP.
                _log.LogWarning(ex, "Cola {Cola}: la sesión SQL liberará el applock al cerrarse.", c.ColaId);
            }
        }
    }

    private const string ValidacionSql = @"
SET NOCOUNT ON;
IF CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')) <> 'WIN-SERVERDEVSIGO'
 THROW 51901,'Equipo distinto a DEV.',1;
IF DB_NAME() <> 'TIF_Meat'
 THROW 51902,'Base Meat no esperada.',1;
IF OBJECT_ID('SIGO.dbo.SurtidoAutorizacionKg','U') IS NULL
 THROW 51903,'Tablas SIGO no accesibles en esta instancia.',1;
IF OBJECT_ID('TIF_CommerciaNET.dbo.Documento','U') IS NULL
 THROW 51904,'CommerciaNET TIF no accesible en esta instancia.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.Usuario WHERE UsuarioId=@UsuarioIntegracion)
 THROW 51905,'Usuario de integración Meat no válido.',1;
IF @Tolerancia<0 OR @Tolerancia>10 OR @MaxExcepcion<@Tolerancia OR @MaxExcepcion>25
 THROW 51906,'Configuración comercial inválida.',1;
IF NOT EXISTS(
 SELECT 1 FROM SIGO.dbo.SurtidoCaptura c
 JOIN SIGO.dbo.SurtidoIntegracionCola q ON q.SurtidoCapturaId=c.Id
 JOIN SIGO.dbo.SurtidoIntegracionEvaluacion e ON e.ColaId=q.Id
 WHERE q.Id=@ColaId AND c.Id=@CapturaId AND c.SolicitudSurtidoId=@SolicitudSurtidoId
 AND c.Pedido=@Pedido AND c.Planta='TIF' AND c.CodigoAlmacen=@CodigoAlmacen
 AND c.Estatus='VALIDADO' AND q.Estatus='PENDIENTE' AND q.Tipo='SALIDA_EMBARQUE'
 AND e.Estado='REVISION_COMERCIAL'
 AND NOT EXISTS(SELECT 1 FROM SIGO.dbo.SurtidoIntegracionVremConfirmada v WHERE v.ColaId=q.Id)
)
 THROW 51907,'Captura/cola/evaluación no preparadas.',1;
IF NOT EXISTS (SELECT 1 FROM dbo.SolicitudSurtido ss
 JOIN dbo.SurtidoReferencia sr ON sr.SolicitudSurtidoId=ss.SolicitudSurtidoId
 AND sr.TipoReferenciaId=9 AND sr.Referencia=CONCAT('CARNG.SUC02.VPED.',@Pedido)
 WHERE ss.SolicitudSurtidoId=@SolicitudSurtidoId AND ss.ProcesoId=17 AND ss.EstatusId=1)
 THROW 51908,'Solicitud Meat o referencia VPED inválidas.',1;
IF EXISTS(SELECT 1 FROM dbo.ProduccionLog WHERE ProduccionLogId=@SolicitudSurtidoId AND ProcesoId IN(17,37))
 THROW 51909,'Existen logs previos de embarque: revisar manualmente, prohibido reintentar.',1;
IF EXISTS(SELECT 1 FROM dbo.SalidaEmbarque WHERE SolicitudSurtidoId=@SolicitudSurtidoId)
 THROW 51910,'Existen salidas previas: prohibido reintentar.',1;
IF EXISTS(
 SELECT 1 FROM TIF_CommerciaNET.dbo.Documento d
 WHERE d.EmpresaId='CARNG' AND d.SucursalId='SUC02' AND d.OperacionId='VREM'
 AND (EXISTS(SELECT 1 FROM TIF_CommerciaNET.dbo.DocumentoReferencia r
  WHERE r.DocumentoId=CONCAT(d.EmpresaId,'.',d.SucursalId,'.',d.OperacionId,'.',d.Folio)
  AND ((r.Tipo='PL' AND r.Referencia=CONVERT(varchar(20),@SolicitudSurtidoId))
    OR (r.Tipo='BO' AND r.Referencia=@Pedido)))
  OR EXISTS(SELECT 1 FROM TIF_CommerciaNET.dbo.DocumentoRelacionado r
  WHERE r.DocumentoId=CONCAT(d.EmpresaId,'.',d.SucursalId,'.',d.OperacionId,'.',d.Folio)
  AND r.Tipo='SE' AND r.DocumentoRelacionadoId=CONCAT('CARNG.SUC02.VPED.',@Pedido))))
 THROW 51911,'Ya existe VREM ligada por PL, BO o SE.',1;
IF EXISTS(SELECT 1 FROM SIGO.dbo.SurtidoIntegracionManifiesto m
 WHERE m.ColaId=@ColaId AND m.SolicitudSurtidoId=@SolicitudSurtidoId
 AND (m.ProduccionId<=0 OR m.KgSIGO<=0 OR m.KgVremEstimados<=0 OR m.SKU='' OR m.CodigoEtiqueta=''))
 THROW 51912,'Manifiesto contiene valores inválidos.',1;
IF EXISTS(SELECT 1 FROM SIGO.dbo.SurtidoIntegracionManifiesto m
 WHERE m.ColaId=@ColaId GROUP BY m.ProduccionId HAVING COUNT(*)>1)
 THROW 51913,'Manifiesto contiene producciones duplicadas.',1;
IF EXISTS(SELECT 1 FROM SIGO.dbo.SurtidoIntegracionManifiesto m
 LEFT JOIN dbo.Produccion p ON p.ProduccionId=m.ProduccionId
 LEFT JOIN SIGO.dbo.SurtidoCapturaDetalle cd ON cd.SurtidoCapturaId=@CapturaId
   AND cd.Activo=1 AND cd.ProduccionId=m.ProduccionId
 WHERE m.ColaId=@ColaId AND m.SolicitudSurtidoId=@SolicitudSurtidoId
 AND (p.ProduccionId IS NULL OR p.Estatus<>1 OR p.Almacen<>@CodigoAlmacen
  OR p.Articulo<>m.SKU OR p.CodigoEtiqueta<>m.CodigoEtiqueta
  OR ABS(p.PesoNeto-m.KgSIGO)>.001 OR cd.ProduccionId IS NULL
  OR cd.CodigoEtiqueta<>m.CodigoEtiqueta OR cd.SKU<>m.SKU
  OR ABS(cd.PesoNeto-m.KgSIGO)>.001
  OR EXISTS(SELECT 1 FROM dbo.SalidaEmbarque se WHERE se.ProduccionId=m.ProduccionId)
  OR EXISTS(SELECT 1 FROM dbo.ProduccionLog pl WHERE pl.ProduccionId=m.ProduccionId AND pl.ProcesoId IN(17,37))))
 THROW 51914,'Manifiesto, captura e inventario Meat distintos o ya embarcados.',1;
DECLARE @Cajas int,@Kg decimal(18,3),@Planeados decimal(18,3),@Sku varchar(50),@Pct decimal(9,4);
SELECT @Cajas=COUNT(*),@Kg=SUM(m.KgSIGO),@Sku=MIN(m.SKU)
FROM SIGO.dbo.SurtidoIntegracionManifiesto m
WHERE m.ColaId=@ColaId AND m.SolicitudSurtidoId=@SolicitudSurtidoId;
IF @Cajas<=0 OR @Kg IS NULL OR
 (SELECT COUNT(DISTINCT SKU) FROM SIGO.dbo.SurtidoIntegracionManifiesto WHERE ColaId=@ColaId)<>1
 THROW 51915,'Solo se admite una solicitud con un SKU válido.',1;
IF (SELECT COUNT(DISTINCT ProduccionId) FROM SIGO.dbo.SurtidoCapturaDetalle
 WHERE SurtidoCapturaId=@CapturaId AND Activo=1)<>@Cajas
 THROW 51916,'La captura activa no coincide con el manifiesto.',1;
IF (SELECT SUM(Cantidad) FROM dbo.SolicitudSurtidoDetalle
 WHERE SolicitudSurtidoId=@SolicitudSurtidoId AND Articulo=@Sku AND Almacen=@CodigoAlmacen)<>@Cajas
 THROW 51917,'Cajas distintas a las solicitadas en Meat.',1;
SELECT @Planeados=CAST(SUM(CONVERT(decimal(18,3),m.Unidad)*CONVERT(decimal(18,3),m.FactorUnidad)) AS decimal(18,3))
FROM TIF_CommerciaNET.dbo.Movimiento m
JOIN TIF_CommerciaNET.dbo.Documento d ON d.EmpresaId=m.EmpresaId AND d.SucursalId=m.SucursalId
 AND d.OperacionId=m.OperacionId AND d.Folio=m.Folio
WHERE m.EmpresaId='CARNG' AND m.SucursalId='SUC02' AND m.OperacionId='VPED'
 AND m.Folio=@Pedido AND m.ArticuloId=@Sku AND m.EmpaqueId='KG' AND d.AlmacenId=@CodigoAlmacen;
IF @Planeados IS NULL OR @Planeados<=0
 THROW 51918,'Kg VPED no válidos.',1;
IF EXISTS(SELECT 1 FROM TIF_CommerciaNET.dbo.Movimiento
 WHERE EmpresaId='CARNG' AND SucursalId='SUC02' AND OperacionId='VPED'
 AND Folio=@Pedido AND (ArticuloId<>@Sku OR EmpaqueId<>'KG' OR Unidad<=0 OR FactorUnidad<=0))
 THROW 51919,'VPED contiene productos/empaques incompatibles.',1;
SET @Pct=ROUND(ABS(@Planeados-@Kg)*100.0/@Planeados,4);
IF @Pct>@Tolerancia AND @ExcepcionHabilitada=0
 THROW 51920,'Diferencia requiere excepción no habilitada.',1;
IF @Pct>@Tolerancia AND @Pct>@MaxExcepcion
 THROW 51921,'Diferencia excede máximo excepcional.',1;
-- Autorizar solamente cuando la diferencia supera la tolerancia configurada.
-- Dentro de tolerancia, una autorización no es requisito para crear VREM.
IF @Pct>@Tolerancia AND NOT EXISTS(
 SELECT 1 FROM SIGO.dbo.SurtidoAutorizacionKg a
 JOIN SIGO.dbo.UsuarioSQL u ON u.Id=a.UsuarioAutorizadorId AND u.Activo=1 AND u.Usuario=a.UsuarioAutorizador
 JOIN SIGO.dbo.PerfilPermisoModulo ppm ON ppm.PerfilId=u.PerfilId AND ppm.Activo=1 AND ppm.PuedeEscribir=1
 JOIN SIGO.dbo.ModulosSistema ms ON ms.Id=ppm.ModuloId AND ms.Activo=1 AND ms.Clave='SURTIDO_AUTORIZAR_DIF_KG'
 WHERE a.ColaId=@ColaId AND a.SolicitudSurtidoId=@SolicitudSurtidoId
 AND a.FolioVPED=@Pedido AND a.Planta='TIF' AND a.SKU=@Sku AND a.Estado='APROBADA'
 AND a.Cajas=@Cajas AND ABS(a.KgReales-@Kg)<=.001 AND ABS(a.KgPlaneados-@Planeados)<=.001
 AND ABS(a.DiferenciaPorcentaje-@Pct)<=.01 AND a.ToleranciaPorcentaje<=@Tolerancia
 AND LEN(LTRIM(RTRIM(a.Motivo)))>=10)
 THROW 51922,'Autorización comercial inválida, incompleta o revocada.',1;
SELECT @Cajas AS Cajas,@Kg AS KgReales,@Planeados AS KgPlaneados,@Pct AS DiferenciaPorcentaje;";
}
