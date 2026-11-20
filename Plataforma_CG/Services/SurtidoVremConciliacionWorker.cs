using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plataforma_CG.Services;

// Conciliador de remisiones YA EXISTENTES. No crea VREM, no llama SAP, no cambia Meat.
// Funciona con SIGO, Meat y CommerciaNET en servidores separados.
public sealed class SurtidoVremConciliacionWorker : BackgroundService
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<SurtidoVremConciliacionWorker> _log;
    public SurtidoVremConciliacionWorker(IConfiguration cfg, ILogger<SurtidoVremConciliacionWorker> log)
    { _cfg=cfg; _log=log; }

    private sealed class Cola
    {
        public long ColaId {get;set;}
        public int SolicitudSurtidoId {get;set;}
        public string Pedido {get;set;}="";
        public string Planta {get;set;}="";
        public string CodigoAlmacen {get;set;}="";
        public string CapturaEstado {get;set;}="";
    }
    private sealed class Caja
    {
        public int ProduccionId {get;set;}
        public string CodigoEtiqueta {get;set;}="";
        public string SKU {get;set;}="";
        public decimal KgSIGO {get;set;}
    }
    private sealed class EstadoMeat
    {
        public int ProduccionId {get;set;}
        public string CodigoEtiqueta {get;set;}="";
        public int Estatus {get;set;}
        public int UltimoProcesoId {get;set;}
        public int LogsProcesados {get;set;}
    }
    private sealed class Remision
    {
        public string DocumentoId {get;set;}="";
        public string Referencia {get;set;}="";
        public string Pedido {get;set;}="";
        public string Bo {get;set;}="";
        public string AlmacenId {get;set;}="";
        public string TransaccionEstatus {get;set;}="";
    }
    private sealed class Linea
    {
        public string ProduccionId {get;set;}="";
        public string CodigoEtiqueta {get;set;}="";
        public string SKU {get;set;}="";
        public decimal Kg {get;set;}
    }

    private string Conn(string key)
    {
        var cadena = _cfg.GetConnectionString(key);
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException($"Falta ConnectionStrings:{key}");
        return cadena;
    }

    // La planta determina el servidor y credenciales por la cadena Meat existente.
    // Para CommerciaNET se mantiene esa MISMA instancia y solo cambia la base.
    // No se admiten plantas desconocidas ni se intenta un fallback entre plantas.
    private string PorPlanta(string planta, bool comercial)
    {
        var plantaNormalizada = (planta ?? "").Trim().ToUpperInvariant();
        string claveMeat;
        string baseComercial;

        switch (plantaNormalizada)
        {
            case "TIF":
            case "TIF776":
            case "TIF 776":
                claveMeat = "CadenaMeatTIF";
                baseComercial = "TIF_CommerciaNET";
                break;

            case "P1":
            case "PLANTA1":
            case "PLANTA 1":
                claveMeat = "CadenaMeatP1";
                baseComercial = "CommerciaNET";
                break;

            default:
                throw new InvalidOperationException(
                    $"Planta '{planta}' sin conexión configurada para conciliar VREM.");
        }

        var cadenaMeat = Conn(claveMeat);
        if (!comercial)
            return cadenaMeat;

        var builder = new SqlConnectionStringBuilder(cadenaMeat)
        {
            InitialCatalog = baseComercial
        };
        return builder.ConnectionString;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("SurtidoVremConciliacionWorker iniciado. Habilitado={Habilitado}", _cfg.GetValue<bool>("SurtidoConciliacionVrem:Habilitado"));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_cfg.GetValue<bool>("SurtidoConciliacionVrem:Habilitado"))
                    await CicloAsync(stoppingToken);
            }
            catch (OperationCanceledException) when(stoppingToken.IsCancellationRequested) {break;}
            catch(Exception ex) {_log.LogError(ex,"Conciliación VREM SIGO falló");}
            try {await Task.Delay(TimeSpan.FromSeconds(60),stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {break;}
        }
    }

    private async Task CicloAsync(CancellationToken ct)
    {
        await using var sigo = new SqlConnection(Conn("CadenaSQLSIGO"));
        await sigo.OpenAsync(ct);
        var lockId=await sigo.ExecuteScalarAsync<int>(new CommandDefinition(@"
DECLARE @r int;
EXEC @r=sys.sp_getapplock @Resource='SIGO_VREM_CONCILIAR',
@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0;
SELECT @r;",cancellationToken:ct));
        if(lockId<0) { _log.LogWarning("VREM conciliacion: no se obtuvo bloqueo de proceso"); return; }
        try
        {
            var colas=(await sigo.QueryAsync<Cola>(new CommandDefinition(@"
SELECT TOP(20) q.Id AS ColaId,c.SolicitudSurtidoId,c.Pedido,c.Planta,
 c.CodigoAlmacen,c.Estatus AS CapturaEstado
FROM dbo.SurtidoIntegracionCola q
JOIN dbo.SurtidoCaptura c ON c.Id=q.SurtidoCapturaId
WHERE q.Tipo='SALIDA_EMBARQUE' AND q.Estatus='PENDIENTE'
  AND c.Estatus='VALIDADO'
ORDER BY q.Id;",cancellationToken:ct))).ToList();
            _log.LogInformation("VREM conciliacion: {Cantidad} colas pendientes encontradas", colas.Count);
            foreach(var t in colas)
            {
                try {await ConciliarAsync(sigo,t,ct);}
                catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                catch(Exception ex){_log.LogWarning(ex,"Sin conciliación para cola {ColaId}",t.ColaId);}
            }
        }
        finally
        {
            try{await sigo.ExecuteAsync("EXEC sys.sp_releaseapplock @Resource='SIGO_VREM_CONCILIAR',@LockOwner='Session';");}
            catch(Exception ex){_log.LogWarning(ex,"Applock se liberará al cerrar conexión");}
        }
    }

    private async Task ConciliarAsync(SqlConnection sigo,Cola t,CancellationToken ct)
    {
        _log.LogInformation("VREM: verificando cola {ColaId} solicitud {SolicitudId} planta {Planta}", t.ColaId, t.SolicitudSurtidoId, t.Planta);
        var cajas=(await sigo.QueryAsync<Caja>(new CommandDefinition(@"
SELECT ProduccionId,CodigoEtiqueta,SKU,KgSIGO FROM dbo.SurtidoIntegracionManifiesto
WHERE ColaId=@ColaId AND SolicitudSurtidoId=@SolicitudSurtidoId;",
            new{t.ColaId,t.SolicitudSurtidoId},cancellationToken:ct))).ToList();
        if(cajas.Count==0 || cajas.Any(x=>x.ProduccionId<=0) ||
           cajas.Select(x=>x.ProduccionId).Distinct().Count()!=cajas.Count) { _log.LogWarning("VREM cola {ColaId} no conciliada: Manifiesto sin cajas, IDs invalidos o duplicados",t.ColaId); return; }

        await using var meat=new SqlConnection(PorPlanta(t.Planta,false));
        await meat.OpenAsync(ct);
        string? pedidoCompleto=await meat.QueryFirstOrDefaultAsync<string>(new CommandDefinition(@"
SELECT TOP(1) Referencia FROM dbo.SurtidoReferencia
WHERE SolicitudSurtidoId=@SolicitudSurtidoId AND TipoReferenciaId=9;",
            new{t.SolicitudSurtidoId},cancellationToken:ct));
        if(string.IsNullOrWhiteSpace(pedidoCompleto)){ _log.LogWarning("VREM cola {ColaId} no conciliada: Meat no devolvio referencia VPED para la solicitud",t.ColaId); return; }
        var partes=pedidoCompleto.Trim().Split('.');
        if(partes.Length!=4 || partes[2]!="VPED" || partes[3]!=t.Pedido.Trim()){ _log.LogWarning("VREM cola {ColaId} no conciliada: Referencia VPED no coincide con SIGO",t.ColaId); return; }
        int estadoSolicitud=await meat.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT EstatusId FROM dbo.SolicitudSurtido WHERE SolicitudSurtidoId=@SolicitudSurtidoId;",
           new{t.SolicitudSurtidoId},cancellationToken:ct));
        if(estadoSolicitud!=3){ _log.LogWarning("VREM cola {ColaId} no conciliada: Solicitud Meat no esta cerrada en EstatusId=3",t.ColaId); return; }
        var ids=cajas.Select(x=>x.ProduccionId).ToArray();
        var producciones=(await meat.QueryAsync<EstadoMeat>(new CommandDefinition(@"
SELECT p.ProduccionId,p.CodigoEtiqueta,p.Estatus,p.UltimoProcesoId,
(SELECT COUNT(*) FROM dbo.ProduccionLog l
 WHERE l.ProduccionId=p.ProduccionId AND l.ProduccionLogId=@SolicitudSurtidoId
 AND l.ProcesoId=17 AND l.FechaHoraServer IS NOT NULL) LogsProcesados
FROM dbo.Produccion p WHERE p.ProduccionId IN @Ids;",
           new{Ids=ids,t.SolicitudSurtidoId},cancellationToken:ct))).ToList();
        if(producciones.Count!=cajas.Count || producciones.Any(p=>p.Estatus!=0 ||
             p.UltimoProcesoId!=17 || p.LogsProcesados!=1 ||
             cajas.Single(x=>x.ProduccionId==p.ProduccionId).CodigoEtiqueta!=p.CodigoEtiqueta)){ _log.LogWarning("VREM cola {ColaId} no conciliada: Producciones Meat: faltan cajas, estatus, ultimo proceso, logs 17 o etiquetas no coinciden",t.ColaId); return; }

        await using var com=new SqlConnection(PorPlanta(t.Planta,true));
        await com.OpenAsync(ct);
        var remisiones=(await com.QueryAsync<Remision>(new CommandDefinition(@"
SELECT d.EmpresaId+'.'+d.SucursalId+'.'+d.OperacionId+'.'+d.Folio AS DocumentoId,
  pl.Referencia, se.DocumentoRelacionadoId AS Pedido,bo.Referencia AS Bo,
  d.AlmacenId,d.TransaccionEstatus
FROM dbo.Documento d
JOIN dbo.DocumentoReferencia pl ON pl.DocumentoId=
 d.EmpresaId+'.'+d.SucursalId+'.'+d.OperacionId+'.'+d.Folio
 AND pl.Tipo='PL' AND pl.Referencia=@Solicitud
LEFT JOIN dbo.DocumentoReferencia bo ON bo.DocumentoId=pl.DocumentoId AND bo.Tipo='BO'
LEFT JOIN dbo.DocumentoRelacionado se ON se.DocumentoId=pl.DocumentoId AND se.Tipo='SE'
WHERE d.EmpresaId=@Empresa AND d.SucursalId=@Sucursal AND d.OperacionId='VREM';",
          new{Solicitud=t.SolicitudSurtidoId.ToString(),Empresa=partes[0],Sucursal=partes[1]},cancellationToken:ct))).ToList();
        if(remisiones.Count!=1){ _log.LogWarning("VREM cola {ColaId} no conciliada: CommerciaNET: no encontro exactamente una VREM con PL de la solicitud",t.ColaId); return; }
        var r=remisiones[0];
        if(r.Pedido!=pedidoCompleto.Trim() || r.Bo!=partes[3] ||
           r.AlmacenId!=t.CodigoAlmacen || r.TransaccionEstatus!="99"){ _log.LogWarning("VREM cola {ColaId} no conciliada: CommerciaNET: BO, SE, almacen o TransaccionEstatus de VREM no coinciden",t.ColaId); return; }
        // Evitar que el mismo pedido tenga otra VREM con BO/SE pero PL diferente.
        int colisiones=await com.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT COUNT(DISTINCT d.EmpresaId+'.'+d.SucursalId+'.'+d.OperacionId+'.'+d.Folio)
FROM dbo.Documento d
LEFT JOIN dbo.DocumentoReferencia bo ON bo.DocumentoId=
 d.EmpresaId+'.'+d.SucursalId+'.'+d.OperacionId+'.'+d.Folio AND bo.Tipo='BO'
LEFT JOIN dbo.DocumentoRelacionado se ON se.DocumentoId=
 d.EmpresaId+'.'+d.SucursalId+'.'+d.OperacionId+'.'+d.Folio AND se.Tipo='SE'
WHERE d.EmpresaId=@Empresa AND d.SucursalId=@Sucursal AND d.OperacionId='VREM'
AND (bo.Referencia=@Folio OR se.DocumentoRelacionadoId=@Pedido);",
          new{Empresa=partes[0],Sucursal=partes[1],Folio=partes[3],Pedido=pedidoCompleto.Trim()},cancellationToken:ct));
        if(colisiones!=1){ _log.LogWarning("VREM cola {ColaId} no conciliada: CommerciaNET: hay mas o menos de una VREM para BO/SE",t.ColaId); return; }
        var lineas=(await com.QueryAsync<Linea>(new CommandDefinition(@"
SELECT pr.Referencia AS ProduccionId, et.Referencia AS CodigoEtiqueta,
 m.ArticuloId AS SKU,CONVERT(decimal(18,3),m.Unidad*m.FactorUnidad) AS Kg
FROM dbo.Movimiento m
JOIN dbo.MovimientoReferencia pr ON pr.MovimientoId=
 m.EmpresaId+'.'+m.SucursalId+'.'+m.OperacionId+'.'+m.Folio+'.'+CONVERT(varchar(20),m.RenglonId)
 AND pr.Tipo='PR'
JOIN dbo.MovimientoReferencia et ON et.MovimientoId=pr.MovimientoId AND et.Tipo='ET'
WHERE m.EmpresaId=@Empresa AND m.SucursalId=@Sucursal
 AND m.OperacionId='VREM' AND m.Folio=@Folio;",
          new{Empresa=partes[0],Sucursal=partes[1],Folio=r.DocumentoId.Split('.').Last()},cancellationToken:ct))).ToList();
        if(lineas.Count!=cajas.Count || lineas.Select(x=>x.ProduccionId).Distinct().Count()!=cajas.Count){ _log.LogWarning("VREM cola {ColaId} no conciliada: CommerciaNET: referencias PR/ET faltantes, duplicadas o numero de cajas diferente",t.ColaId); return; }
        foreach(var caja in cajas)
        {
            var l=lineas.SingleOrDefault(x=>x.ProduccionId==caja.ProduccionId.ToString());
            if(l==null || l.CodigoEtiqueta!=caja.CodigoEtiqueta || l.SKU!=caja.SKU ||
               Math.Abs(l.Kg-Math.Round(caja.KgSIGO,2,MidpointRounding.AwayFromZero))>0.011m){ _log.LogWarning("VREM cola {ColaId} no conciliada: CommerciaNET: SKU, etiqueta, ProduccionId o KG por caja diferentes del manifiesto",t.ColaId); return; }
        }
        // Cambiar SOLO estado de la cola y guardar folio en tabla de conciliación con llave única.
        // La operación SQL es idempotente y no realiza escrituras fuera de SIGO.
        await sigo.ExecuteAsync(new CommandDefinition(@"
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
IF EXISTS(SELECT 1 FROM dbo.SurtidoIntegracionCola WITH(UPDLOCK,HOLDLOCK)
          WHERE Id=@ColaId AND Estatus='PENDIENTE')
BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.SurtidoIntegracionVremConfirmada WHERE ColaId=@ColaId)
    INSERT dbo.SurtidoIntegracionVremConfirmada(ColaId,SolicitudSurtidoId,DocumentoVrem,Cajas,KgReales,KgVrem)
    VALUES(@ColaId,@SolicitudSurtidoId,@Documento,@Cajas,@KgReales,@KgVrem);
  UPDATE dbo.SurtidoIntegracionCola SET Estatus='COMPLETADO',Error=NULL WHERE Id=@ColaId;
  UPDATE dbo.SurtidoIntegracionEvaluacion SET Estado='VREM_CONCILIADA',
   Detalle=LEFT(CONCAT('VREM ',@Documento,'; verificados PR/ET, Meat proceso 17 y solicitud 3.'),2000),
   FechaRevision=SYSDATETIME() WHERE ColaId=@ColaId;
END;
COMMIT;
END TRY BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK;
THROW;
END CATCH;",
          new{t.ColaId,t.SolicitudSurtidoId,Documento=r.DocumentoId,Cajas=cajas.Count,
           KgReales=cajas.Sum(x=>x.KgSIGO),KgVrem=lineas.Sum(x=>x.Kg)},cancellationToken:ct));
        _log.LogInformation("Conciliada cola {ColaId}, VREM {Vrem}",t.ColaId,r.DocumentoId);
    }
}
