using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Plataforma_CG.Services;

/// <summary>
/// SIGO -> Meat/CommerciaNET por planta. SOLO LECTURA en sistemas externos y auditoría en SIGO.
/// No llama meat_SalidaEmbarque y NO cambia el estado de la cola.
/// Diseñado para bases en servidores/instancias diferentes.
/// </summary>
public sealed class SurtidoIntegracionWorker : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SurtidoIntegracionWorker> _log;
    private readonly IOptionsMonitor<SurtidoIntegracionOptions> _options;

    public SurtidoIntegracionWorker(
        IConfiguration config,
        ILogger<SurtidoIntegracionWorker> log,
        IOptionsMonitor<SurtidoIntegracionOptions> options)
    {
        _config = config;
        _log = log;
        _options = options;
    }

    private string Cadena(string nombre) =>
        _config.GetConnectionString(nombre) is { Length: > 0 } valor
            ? valor
            : throw new InvalidOperationException($"Falta ConnectionStrings:{nombre}.");

    private string CadenaMeat(string? planta)
    {
        string nombre = (planta ?? "").Trim().ToUpperInvariant() switch
        {
            "TIF" or "TIF776" or "TIF 776" => "CadenaMeatTIF",
            "P1" or "PLANTA1" or "PLANTA 1" => "CadenaMeatP1",
            _ => throw new InvalidOperationException(
                $"Planta '{planta}' desconocida. Se bloquea la evaluación para evitar usar otro Meat.")
        };
        return Cadena(nombre);
    }

    // La base comercial se elige por planta y nunca por una cadena SQL de SIGO.
    // Si existe una cadena explícita, se usa; de lo contrario se construye una
    // conexión al MISMO servidor de Meat, cambiando solo InitialCatalog.
    // Verificar en produccion que Meat y CommerciaNET residen en la misma instancia.
    private string CadenaComercial(string planta)
    {
        var p = (planta ?? "").Trim().ToUpperInvariant();
        string nombre = p switch
        {
            "TIF" or "TIF776" or "TIF 776" => "CadenaCommerciaTIF",
            "P1" or "PLANTA1" or "PLANTA 1" => "CadenaCommerciaP1",
            _ => throw new InvalidOperationException($"Planta no configurada para comercio: {planta}")
        };
        string? configurada = _config.GetConnectionString(nombre);
        if (!string.IsNullOrWhiteSpace(configurada)) return configurada;

        // Solo se admite derivación cuando se habilita explícitamente.
        if (!_options.CurrentValue.CommerciaEnMismaInstanciaQueMeat)
            throw new InvalidOperationException($"Falta ConnectionStrings:{nombre}; no se asumira servidor comercial.");
        var csb = new SqlConnectionStringBuilder(CadenaMeat(planta))
        {
            InitialCatalog = p.StartsWith("TIF", StringComparison.OrdinalIgnoreCase)
                ? "TIF_CommerciaNET" : "CommerciaNET"
        };
        return csb.ConnectionString;
    }

    private sealed class DocumentoComercial
    {
        public string EmpresaId { get; set; } = "";
        public string SucursalId { get; set; } = "";
        public string OperacionId { get; set; } = "";
        public string Folio { get; set; } = "";
        public string Estatus { get; set; } = "";
    }
    private sealed class MovimientoComercial
    {
        public string ArticuloId { get; set; } = "";
        public string EmpaqueId { get; set; } = "";
        public double Unidad { get; set; }
        public double FactorUnidad { get; set; }
    }
    private sealed class RemisionEncontrada
    {
        public string DocumentoId { get; set; } = "";
        public string TipoReferencia { get; set; } = "";
        public string Referencia { get; set; } = "";
        public string? Bo { get; set; }
        public string? Pl { get; set; }
        public string? PedidoSe { get; set; }
    }

    private sealed class Trabajo
    {
        public long ColaId { get; set; }
        public long CapturaId { get; set; }
        public int SolicitudSurtidoId { get; set; }
        public string Pedido { get; set; } = "";
        public string Planta { get; set; } = "";
        public string CodigoAlmacen { get; set; } = "";
    }
    private sealed class Caja
    {
        public int ProduccionId { get; set; }
        public string SKU { get; set; } = "";
        public string CodigoEtiqueta { get; set; } = "";
        public decimal PesoNeto { get; set; }
    }
    private sealed class DetalleMeat
    {
        public string Articulo { get; set; } = "";
        public string Almacen { get; set; } = "";
        public int Cantidad { get; set; }
    }
    private sealed class ManifiestoPrevio
    {
        public int ProduccionId { get; set; }
        public decimal KgSIGO { get; set; }
        public decimal KgMeat { get; set; }
    }

    private sealed class ProduccionMeat
    {
        public int ProduccionId { get; set; }
        public string CodigoEtiqueta { get; set; } = "";
        public string Articulo { get; set; } = "";
        public string Almacen { get; set; } = "";
        public int Estatus { get; set; }
        public decimal PesoNeto { get; set; }
        public int Salidas { get; set; }
        public int ProcesosEmbarque { get; set; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opciones = _options.CurrentValue;
            try
            {
                if (opciones.HabilitarEvaluacionAutomatica)
                    await EvaluarPendientesAsync(Math.Clamp(opciones.MaximoPorCiclo, 1, 50), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogError(ex, "No se pudo evaluar la cola SIGO/Meat."); }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(opciones.IntervaloSegundos, 15, 3600)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task EvaluarPendientesAsync(int maximo, CancellationToken ct)
    {
        await using var sigo = new SqlConnection(Cadena("CadenaSQLSIGO"));
        await sigo.OpenAsync(ct);

        // ColaIdMinima protege las colas previas al despliegue.
        // En DEV usar 7: procesa la 7 y todas las posteriores; no toca 1..6.
        long colaMinima = Math.Max(1, _config.GetValue<long>("SurtidoIntegracion:ColaIdMinima"));
        if (!_config.GetValue<bool>("SurtidoIntegracion:HabilitarEvaluacionAutomatica")) return;

        // El applock de sesión evita que varias instancias de IIS evalúen a la vez.
        int lockResultado = await sigo.ExecuteScalarAsync<int>(new CommandDefinition(
            "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='SIGO_MEAT_EVALUADOR_V1'," +
            " @LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @r;",
            cancellationToken: ct));
        if (lockResultado < 0) return;

        try
        {
            var trabajos = (await sigo.QueryAsync<Trabajo>(new CommandDefinition(@"
SELECT TOP (@Maximo) q.Id AS ColaId, q.SurtidoCapturaId AS CapturaId,
       c.SolicitudSurtidoId, c.Pedido, c.Planta, c.CodigoAlmacen
FROM dbo.SurtidoIntegracionCola q
JOIN dbo.SurtidoCaptura c ON c.Id=q.SurtidoCapturaId
WHERE q.Tipo='SALIDA_EMBARQUE' AND q.Estatus='PENDIENTE'
  AND c.Estatus='VALIDADO'
  AND q.Id >= @ColaMinima
  AND NOT EXISTS (SELECT 1 FROM dbo.SurtidoIntegracionVremConfirmada v WHERE v.ColaId=q.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.SurtidoIntegracionEvaluacion e WHERE e.ColaId=q.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.SurtidoAutorizacionKg a WHERE a.ColaId=q.Id)
ORDER BY q.Id;", new { Maximo = maximo, ColaMinima = colaMinima }, cancellationToken: ct))).ToList();

            foreach (var t in trabajos)
            {
                try { await EvaluarUnoAsync(sigo, t, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Evaluación falló para cola {ColaId}, planta {Planta}", t.ColaId, t.Planta);
                    await GuardarAsync(sigo, t, "ERROR_EVALUACION", null, null,
                        "Error de conexión o comprobación; consultar logs. No se ejecutó Meat ni VREM.", ct);
                }
            }
        }
        finally
        {
            try
            {
                await sigo.ExecuteAsync(new CommandDefinition(
                    "EXEC sys.sp_releaseapplock @Resource='SIGO_MEAT_EVALUADOR_V1',@LockOwner='Session';",
                    cancellationToken: CancellationToken.None));
            }
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo liberar applock; se libera al cerrar la conexión."); }
        }
    }

    private async Task EvaluarUnoAsync(SqlConnection sigo, Trabajo t, CancellationToken ct)
    {
        var razones = new List<string>();
        var avisos = new List<string>();
        var cajas = (await sigo.QueryAsync<Caja>(new CommandDefinition(@"
SELECT ProduccionId, SKU, CodigoEtiqueta, PesoNeto
FROM dbo.SurtidoCapturaDetalle
WHERE SurtidoCapturaId=@CapturaId AND Activo=1;",
            new { t.CapturaId }, cancellationToken: ct))).ToList();

        if (cajas.Count == 0) razones.Add("Sin cajas activas en la captura.");
        if (cajas.Any(x => x.ProduccionId <= 0)) razones.Add("Existen ProduccionId inválidos.");
        if (cajas.GroupBy(x => x.ProduccionId).Any(g => g.Count() > 1))
            razones.Add("Hay ProduccionId duplicados en la captura.");

        string cadenaMeat;
        try { cadenaMeat = CadenaMeat(t.Planta); }
        catch (InvalidOperationException ex)
        {
            await GuardarAsync(sigo, t, "BLOQUEADO", cajas.Select(x => x.ProduccionId).Distinct().Count(),
                cajas.Sum(x => x.PesoNeto), ex.Message, ct);
            return;
        }

        await using var meat = new SqlConnection(cadenaMeat);
        await meat.OpenAsync(ct);

        // Conectado DIRECTAMENTE a la base Meat indicada por la cadena; no usa TIF_Meat.dbo.*.
        var solicitudExiste = await meat.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT COUNT(*) FROM dbo.SolicitudSurtido
WHERE SolicitudSurtidoId=@SolicitudSurtidoId AND ProcesoId=17;",
            new { t.SolicitudSurtidoId }, cancellationToken: ct));
        if (solicitudExiste != 1) razones.Add("Solicitud no existe en Meat de la planta o no es proceso 17.");

        var referencia = await meat.QueryFirstOrDefaultAsync<string>(new CommandDefinition(@"
SELECT Referencia FROM dbo.SurtidoReferencia
WHERE SolicitudSurtidoId=@SolicitudSurtidoId AND TipoReferenciaId=9;",
            new { t.SolicitudSurtidoId }, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(referencia) ||
            !string.Equals(referencia.Trim(), t.Pedido.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !referencia.Trim().EndsWith("." + t.Pedido.Trim(), StringComparison.OrdinalIgnoreCase))
            razones.Add("Referencia VPED de Meat no coincide con el pedido SIGO.");

        var detalles = (await meat.QueryAsync<DetalleMeat>(new CommandDefinition(@"
SELECT Articulo, Almacen, Cantidad FROM dbo.SolicitudSurtidoDetalle
WHERE SolicitudSurtidoId=@SolicitudSurtidoId;",
            new { t.SolicitudSurtidoId }, cancellationToken: ct))).ToList();
        if (detalles.Count == 0) razones.Add("Solicitud sin detalle en Meat.");

        foreach (var grupo in cajas.GroupBy(x => x.SKU.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var solicitado = detalles.Where(x =>
                string.Equals(x.Articulo.Trim(), grupo.Key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Almacen.Trim(), t.CodigoAlmacen.Trim(), StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Cantidad);
            if (solicitado <= 0) razones.Add($"SKU {grupo.Key} no está solicitado en este almacén.");
            if (grupo.Select(x => x.ProduccionId).Distinct().Count() > solicitado)
                razones.Add($"SKU {grupo.Key} excede las cajas solicitadas: {grupo.Count()}/{solicitado}.");
        }

        // Consultas por lotes para no superar el límite de parámetros SQL.
        var ids = cajas.Select(x => x.ProduccionId).Where(x => x > 0).Distinct().ToArray();
        var encontrados = new List<ProduccionMeat>();
        foreach (var lote in ids.Chunk(400))
        {
            encontrados.AddRange(await meat.QueryAsync<ProduccionMeat>(new CommandDefinition(@"
SELECT p.ProduccionId,p.CodigoEtiqueta,p.Articulo,p.Almacen,p.Estatus,p.PesoNeto,
       (SELECT COUNT(*) FROM dbo.SalidaEmbarque se WHERE se.ProduccionId=p.ProduccionId) AS Salidas,
       (SELECT COUNT(*) FROM dbo.ProduccionLog pl WHERE pl.ProduccionId=p.ProduccionId
         AND pl.ProcesoId IN (17,37)) AS ProcesosEmbarque
FROM dbo.Produccion p WHERE p.ProduccionId IN @Ids;",
                new { Ids = lote }, cancellationToken: ct)));
        }
        var porId = encontrados.GroupBy(x => x.ProduccionId).ToDictionary(g => g.Key, g => g.First());
        foreach (var caja in cajas)
        {
            if (!porId.TryGetValue(caja.ProduccionId, out var p))
            {
                razones.Add($"Caja {caja.ProduccionId} no existe en Meat.");
                continue;
            }
            if (p.Estatus != 1 || !string.Equals(p.Almacen.Trim(), t.CodigoAlmacen.Trim(), StringComparison.OrdinalIgnoreCase))
                razones.Add($"Caja {p.ProduccionId} no está disponible en almacén {t.CodigoAlmacen}.");
            if (!string.Equals(p.Articulo.Trim(), caja.SKU.Trim(), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(p.CodigoEtiqueta.Trim(), caja.CodigoEtiqueta.Trim(), StringComparison.OrdinalIgnoreCase))
                razones.Add($"Caja {p.ProduccionId} tiene SKU/etiqueta diferente.");
            if (Math.Abs(p.PesoNeto - caja.PesoNeto) > 0.01m)
                razones.Add($"Caja {p.ProduccionId} tiene peso diferente.");
            if (p.Salidas > 0 || p.ProcesosEmbarque > 0)
                razones.Add($"Caja {p.ProduccionId} ya tiene evidencia de embarque en Meat.");
        }

        // Fase 2: comprobar el VPED y todas las rutas que podrían asociar
        // una VREM: PL (solicitud), BO (folio VPED) y SE (pedido completo).
        // No se interpreta EmpaqueId KG como caja ni se crea ninguna VREM.
        if (razones.Count == 0)
        {
            try
            {
                await AuditarComercialAsync(t, detalles, cajas, razones, avisos, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Nunca registrar la cadena de conexion ni sus credenciales.
                string diagnostico = ResumirErrorComercial(ex);
                _log.LogError(ex,
                    "Auditoria comercial FALLIDA. Solicitud={SolicitudId}, Cola={ColaId}, Planta={Planta}, Diagnostico={Diagnostico}",
                    t.SolicitudSurtidoId, t.ColaId, t.Planta, diagnostico);
                razones.Add(diagnostico);
            }
        }
        var estado = razones.Count == 0 ? "REVISION_COMERCIAL" : "BLOQUEADO_COMERCIAL";
        var detalle = razones.Count == 0
            ? string.Join(" | ", avisos.Concat(new[] { "Prevalidacion comercial terminada. AUN FALTA preparar ProduccionLog, conciliar apartados y verificar SAP; NO se genero VREM." }))
            : string.Join(" | ", razones.Take(15));
        if (detalle.Length > 2000) detalle = detalle[..2000];
        if (razones.Count == 0)
            await GuardarConManifiestoAsync(sigo, t, cajas, porId, estado, detalle, ct);
        else
            await GuardarAsync(sigo, t, estado, ids.Length, cajas.Sum(x => x.PesoNeto), detalle, ct);
    }

    /// <summary>
    /// Error breve para la tabla de diagnostico. No incluye texto SQL arbitrario
    /// ni cadenas de conexion; la excepcion completa queda en ILogger.
    /// </summary>
    private static string ResumirErrorComercial(Exception ex)
    {
        if (ex is SqlException sql)
        {
            return $"ERROR_SQL_COMERCIAL: Numero={sql.Number}, Estado={sql.State}, " +
                   $"Procedimiento={sql.Procedure ?? ""}, Linea={sql.LineNumber}. " +
                   "Ver registro de la aplicacion SIGO-DEV; NO se genero VREM.";
        }
        if (ex is InvalidOperationException configEx &&
            configEx.Message.StartsWith("Falta ConnectionStrings:", StringComparison.OrdinalIgnoreCase))
        {
            // La clave de configuracion no es informacion secreta.
            return "ERROR_CONFIG_COMERCIAL: " + configEx.Message +
                   " Definir cadena explicita o habilitar derivacion en DEV. NO se genero VREM.";
        }
        return "ERROR_AUDITORIA_COMERCIAL: " + ex.GetType().Name +
               ". Ver logs de SIGO-DEV; NO se genero VREM.";
    }

    private async Task AuditarComercialAsync(
        Trabajo t, List<DetalleMeat> detalles, List<Caja> cajas,
        List<string> razones, List<string> avisos, CancellationToken ct)
    {
        // Se interpreta en porcentaje: 3m = 3%. Predeterminado 0% (bloqueo).
        decimal tolerancia = _config.GetValue<decimal?>("SurtidoIntegracion:ToleranciaKgPorcentaje") ?? 0m;
        if (tolerancia < 0m || tolerancia > 10m)
        {
            razones.Add("ToleranciaKgPorcentaje fuera del rango permitido de 0 a 10.");
            return;
        }

        var refPedido = $"CARNG.SUC02.VPED.{t.Pedido.Trim()}";
        // La referencia real y la sucursal se obtienen siempre de Meat.
        await using var meat = new SqlConnection(CadenaMeat(t.Planta));
        await meat.OpenAsync(ct);
        string? referenciaMeat = await meat.QueryFirstOrDefaultAsync<string>(new CommandDefinition(@"
SELECT Referencia FROM dbo.SurtidoReferencia
WHERE SolicitudSurtidoId=@Id AND TipoReferenciaId=9;",
           new { Id = t.SolicitudSurtidoId }, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(referenciaMeat))
        { razones.Add("Solicitud sin referencia VPED en Meat."); return; }
        refPedido = referenciaMeat.Trim();
        var partes = refPedido.Split('.');
        if (partes.Length != 4 || !partes[2].Equals("VPED", StringComparison.OrdinalIgnoreCase))
        { razones.Add($"Referencia comercial invalida: {refPedido}"); return; }
        string folio = partes[3];
        if (!folio.Equals(t.Pedido.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !refPedido.EndsWith("." + t.Pedido.Trim(), StringComparison.OrdinalIgnoreCase))
        { razones.Add("El folio VPED no coincide con SIGO."); return; }

        string conexionComercial = CadenaComercial(t.Planta);
        await using var com = new SqlConnection(conexionComercial);
        await com.OpenAsync(ct);
        _log.LogInformation(
            "Auditoria comercial conectada. Solicitud={SolicitudId}, Cola={ColaId}, Planta={Planta}, Base={Base}, Servidor={Servidor}",
            t.SolicitudSurtidoId, t.ColaId, t.Planta, com.Database, com.DataSource);
        var doc = await com.QueryFirstOrDefaultAsync<DocumentoComercial>(new CommandDefinition(@"
SELECT EmpresaId,SucursalId,OperacionId,Folio,Estatus FROM dbo.Documento
WHERE EmpresaId=@Empresa AND SucursalId=@Sucursal AND OperacionId='VPED' AND Folio=@Folio;",
          new { Empresa = partes[0], Sucursal = partes[1], Folio = folio }, cancellationToken: ct));
        if (doc is null) razones.Add($"No existe documento VPED {refPedido} en CommerciaNET de {t.Planta}.");

        var movimientos = (await com.QueryAsync<MovimientoComercial>(new CommandDefinition(@"
SELECT ArticuloId,EmpaqueId,Unidad,FactorUnidad
FROM dbo.Movimiento
WHERE EmpresaId=@Empresa AND SucursalId=@Sucursal AND OperacionId='VPED' AND Folio=@Folio;",
         new { Empresa = partes[0], Sucursal = partes[1], Folio = folio }, cancellationToken: ct))).ToList();
        foreach (var d in detalles.Where(x => x.Almacen.Trim().Equals(t.CodigoAlmacen.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            var m = movimientos.Where(x => x.ArticuloId.Trim().Equals(d.Articulo.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (m.Count == 0) { razones.Add($"VPED sin renglon de SKU {d.Articulo}."); continue; }
            // Este chequeo NO afirma equivalencia de cantidades comerciales y cajas;
            // exige posterior conciliacion especifica de peso variable antes de ejecutar.
            if (m.Any(x => x.FactorUnidad <= 0 || x.Unidad <= 0))
                razones.Add($"VPED {d.Articulo} contiene Unidad/FactorUnidad no positivos.");
            // La solicitud Meat se cuenta en CAJAS, el movimiento VPED se compara en KG.
            // La coincidencia se exige en este modo de PRUEBA; la tolerancia comercial
            // definitiva debe venir de una regla empresarial explícita, no se inventa.
            if (m.Any(x => !x.EmpaqueId.Equals("KG", StringComparison.OrdinalIgnoreCase)))
                razones.Add($"SKU {d.Articulo}: este adaptador de prueba solo verifica empaque KG.");
            // Las cajas son unidades fisicas; el peso comercial es Unidad * FactorUnidad.
            decimal kgVped = m.Sum(x => (decimal)x.Unidad * (decimal)x.FactorUnidad);
            decimal kgCapturados = cajas
                .Where(c => c.SKU.Trim().Equals(d.Articulo.Trim(), StringComparison.OrdinalIgnoreCase))
                .Sum(c => c.PesoNeto);
            if (kgVped <= 0m)
            {
                razones.Add($"SKU {d.Articulo}: cantidad comercial no positiva.");
                continue;
            }
            decimal diferencia = Math.Abs(kgVped - kgCapturados);
            decimal porcentaje = diferencia * 100m / kgVped;
            string resumen = $"SKU {d.Articulo}: KG VPED={kgVped:0.000}, KG reales={kgCapturados:0.000}, " +
                             $"diferencia={diferencia:0.000} ({porcentaje:0.00}%), " +
                             $"tolerancia configurada={tolerancia:0.00}%.";
            bool permitirExcepcion = _config.GetValue<bool>(
                "SurtidoIntegracion:HabilitarAutorizacionExcepcional");
            decimal maxAutorizable = permitirExcepcion
                ? (_config.GetValue<decimal?>(
                    "SurtidoIntegracion:MaximoDiferenciaAutorizablePorcentaje") ?? tolerancia)
                : tolerancia;
            if (maxAutorizable < tolerancia || maxAutorizable > 25m)
            {
                razones.Add("Configuracion de excepcion comercial invalida; se bloquea.");
                continue;
            }
            if (porcentaje > maxAutorizable)
                razones.Add(resumen + " BLOQUEO: excede el maximo autorizable.");
            else if (porcentaje > tolerancia)
                avisos.Add(resumen + " REQUIERE autorizacion expresa del supervisor; NO se genera VREM.");
            else
                avisos.Add(resumen + " Dentro de tolerancia; pendiente conciliacion comercial.");
        }
        // Revisar TODAS las vías, no sólo PL: la búsqueda puede encontrar documentos antiguos.
        var remisiones = (await com.QueryAsync<RemisionEncontrada>(new CommandDefinition(@"
SELECT DISTINCT d.EmpresaId+'.'+d.SucursalId+'.'+d.OperacionId+'.'+d.Folio AS DocumentoId,
       'VREM' AS TipoReferencia, '' AS Referencia,
       bo.Referencia AS Bo,pl.Referencia AS Pl,se.DocumentoRelacionadoId AS PedidoSe
FROM dbo.Documento d
LEFT JOIN dbo.DocumentoReferencia pl ON pl.DocumentoId=d.EmpresaId+'.'+d.SucursalId+'.'+d.OperacionId+'.'+d.Folio AND pl.Tipo='PL'
LEFT JOIN dbo.DocumentoReferencia bo ON bo.DocumentoId=d.EmpresaId+'.'+d.SucursalId+'.'+d.OperacionId+'.'+d.Folio AND bo.Tipo='BO'
LEFT JOIN dbo.DocumentoRelacionado se ON se.DocumentoId=d.EmpresaId+'.'+d.SucursalId+'.'+d.OperacionId+'.'+d.Folio AND se.Tipo='SE'
WHERE d.OperacionId='VREM' AND
     (pl.Referencia=@Pl OR bo.Referencia=@Bo OR se.DocumentoRelacionadoId=@Pedido);",
         new { Pl = t.SolicitudSurtidoId.ToString(), Bo = folio, Pedido = refPedido }, cancellationToken: ct))).ToList();
        foreach (var r in remisiones)
        {
            bool coincide = string.Equals(r.Pl, t.SolicitudSurtidoId.ToString(), StringComparison.OrdinalIgnoreCase)
                 && string.Equals(r.Bo, folio, StringComparison.OrdinalIgnoreCase)
                 && string.Equals(r.PedidoSe, refPedido, StringComparison.OrdinalIgnoreCase);
            razones.Add(coincide
                 ? $"VREM {r.DocumentoId} YA EXISTE con PL/BO/SE correctos; conciliar cajas antes de marcar cola."
                 : $"COLISION VREM {r.DocumentoId}: PL={r.Pl}, BO={r.Bo}, SE={r.PedidoSe}. Bloquear automatizacion.");
        }
    }


    /// <summary>
    /// Prepara manifiesto y evaluacion EN UNA MISMA transaccion de SIGO.
    /// Nunca ejecuta SalidaEmbarque ni escribe en Meat o CommerciaNET.
    /// Si la cola ya tiene manifiesto, no lo modifica: se verifica para evitar duplicados.
    /// </summary>
    private static async Task GuardarConManifiestoAsync(
        SqlConnection sigo, Trabajo t, List<Caja> cajas,
        Dictionary<int, ProduccionMeat> porId,
        string estado, string detalle, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await sigo.BeginTransactionAsync(ct);
        try
        {
            int valida = await sigo.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT COUNT(*) FROM dbo.SurtidoIntegracionCola q WITH(UPDLOCK,HOLDLOCK)
JOIN dbo.SurtidoCaptura c WITH(UPDLOCK,HOLDLOCK) ON c.Id=q.SurtidoCapturaId
WHERE q.Id=@ColaId AND q.Estatus='PENDIENTE' AND q.Tipo='SALIDA_EMBARQUE'
  AND c.Id=@CapturaId AND c.SolicitudSurtidoId=@SolicitudSurtidoId
  AND c.Estatus='VALIDADO'
  AND NOT EXISTS (SELECT 1 FROM dbo.SurtidoIntegracionVremConfirmada v WHERE v.ColaId=q.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.SurtidoIntegracionEvaluacion e WHERE e.ColaId=q.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.SurtidoAutorizacionKg a WHERE a.ColaId=q.Id);",
                new { t.ColaId, t.CapturaId, t.SolicitudSurtidoId }, tx, cancellationToken: ct));
            if (valida != 1)
                throw new InvalidOperationException("Cola o captura cambió de estado; no se prepara manifiesto.");

            var existentes = (await sigo.QueryAsync<ManifiestoPrevio>(
                new CommandDefinition(@"
SELECT ProduccionId,KgSIGO,KgMeat FROM dbo.SurtidoIntegracionManifiesto WITH(UPDLOCK,HOLDLOCK)
WHERE ColaId=@ColaId;", new { t.ColaId }, tx, cancellationToken: ct))).ToList();

            if (existentes.Count == 0)
            {
                // Una caja no puede quedar asignada en manifiestos activos de dos colas.
                var idsCajas = cajas.Select(x => x.ProduccionId).ToArray();
                var asignaciones = await sigo.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT COUNT(*) FROM dbo.SurtidoIntegracionManifiesto m WITH(UPDLOCK,HOLDLOCK)
JOIN dbo.SurtidoIntegracionCola q ON q.Id=m.ColaId
WHERE m.ProduccionId IN @Ids AND m.ColaId<>@ColaId AND q.Estatus='PENDIENTE';",
                    new { Ids = idsCajas, t.ColaId }, tx, cancellationToken: ct));
                if (asignaciones > 0)
                    throw new InvalidOperationException("Una caja ya figura en otra cola pendiente.");
                foreach (var caja in cajas)
                {
                    if (!porId.TryGetValue(caja.ProduccionId, out var p))
                        throw new InvalidOperationException("Caja ausente de Meat al preparar el manifiesto.");
                    await sigo.ExecuteAsync(new CommandDefinition(@"
INSERT INTO dbo.SurtidoIntegracionManifiesto
(ColaId,CapturaId,SolicitudSurtidoId,ProduccionId,CodigoEtiqueta,SKU,
 KgSIGO,KgMeat,KgVremEstimados,EmpaqueSalida,Estado,FechaPreparacion)
VALUES(@ColaId,@CapturaId,@SolicitudSurtidoId,@ProduccionId,@CodigoEtiqueta,@SKU,
 @KgSIGO,@KgMeat,@KgVremEstimados,'KG','PENDIENTE_APARTADO_SAP',SYSDATETIME());",
                        new {
                            t.ColaId, t.CapturaId, t.SolicitudSurtidoId,
                            caja.ProduccionId, caja.CodigoEtiqueta, caja.SKU,
                            KgSIGO = caja.PesoNeto, KgMeat = p.PesoNeto,
                            KgVremEstimados = Math.Round(p.PesoNeto, 2, MidpointRounding.AwayFromZero)
                        }, tx, cancellationToken: ct));
                }
            }
            else
            {
                // No hacer upsert de datos previamente auditados. Detenerse si cambiaron.
                if (existentes.Count != cajas.Count || existentes.Select(x=>x.ProduccionId).Distinct().Count()!=cajas.Count)
                    throw new InvalidOperationException("Manifiesto preexistente incompleto o duplicado.");
                foreach (var caja in cajas)
                {
                    var match = existentes.SingleOrDefault(x => x.ProduccionId == caja.ProduccionId);
                    if (match is null || Math.Abs(match.KgSIGO - caja.PesoNeto) > 0.001m ||
                        Math.Abs(match.KgMeat - porId[caja.ProduccionId].PesoNeto) > 0.001m)
                        throw new InvalidOperationException("Manifiesto preexistente diferente de la captura/Meat.");
                }
            }

            // Comprobar cantidad fisica antes de confirmar evaluación.
            int totalManifiesto = await sigo.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.SurtidoIntegracionManifiesto WHERE ColaId=@ColaId;",
                new { t.ColaId }, tx, cancellationToken: ct));
            if (totalManifiesto != cajas.Count)
                throw new InvalidOperationException("Numero de cajas del manifiesto distinto de la captura.");

            await sigo.ExecuteAsync(new CommandDefinition(@"
INSERT INTO dbo.SurtidoIntegracionEvaluacion
(ColaId,CapturaId,SolicitudSurtidoId,Estado,Cajas,Kg,Detalle)
VALUES(@ColaId,@CapturaId,@SolicitudSurtidoId,@Estado,@Cajas,@Kg,@Detalle);",
                new { t.ColaId,t.CapturaId,t.SolicitudSurtidoId,Estado=estado,
                    Cajas=cajas.Count,Kg=cajas.Sum(x=>x.PesoNeto),Detalle=detalle },tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
    private static Task<int> GuardarAsync(SqlConnection sigo, Trabajo t, string estado,
        int? cajas, decimal? kg, string detalle, CancellationToken ct) =>
        sigo.ExecuteAsync(new CommandDefinition(@"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS(SELECT 1 FROM dbo.SurtidoIntegracionCola WITH(UPDLOCK,HOLDLOCK)
          WHERE Id=@ColaId AND Estatus='PENDIENTE')
   AND NOT EXISTS(SELECT 1 FROM dbo.SurtidoIntegracionVremConfirmada WHERE ColaId=@ColaId)
   AND NOT EXISTS(SELECT 1 FROM dbo.SurtidoIntegracionEvaluacion WHERE ColaId=@ColaId)
BEGIN
    INSERT INTO dbo.SurtidoIntegracionEvaluacion
        (ColaId,CapturaId,SolicitudSurtidoId,Estado,Cajas,Kg,Detalle)
    VALUES(@ColaId,@CapturaId,@SolicitudSurtidoId,@Estado,@Cajas,@Kg,@Detalle);
END;
COMMIT TRANSACTION;",
            new { t.ColaId, t.CapturaId, t.SolicitudSurtidoId, Estado = estado, Cajas = cajas, Kg = kg, Detalle = detalle },
            cancellationToken: ct));
}
