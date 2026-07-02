using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using System.Data;

namespace Plataforma_CG.Controllers
{
    public class CalendarioController : Controller
    {
        private readonly AppDbContext _db;
        private readonly string _connString;

        public CalendarioController(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _connString = config.GetConnectionString("DefaultConnection") ?? "";
        }

        public IActionResult Index()
        {
            return View("~/Views/Calendario/Index.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetPedidosLogistica(DateTime fechaInicio, DateTime fechaFin)
        {
            if (string.IsNullOrWhiteSpace(_connString))
                return StatusCode(500, "No hay ConnectionString configurado: DefaultConnection.");

            var lista = new List<PedidoLogisticaDto>();

            var sql = @"
SELECT
    p.Id,
    p.Consecutivo,
    p.Serie,
    s.NombreSerie,
    s.Sucursal,
    s.sucursalId AS SucursalId,

    p.FechaEntrega,
    p.FechaEmbarque,
    CONVERT(VARCHAR(5), p.HoraEmbarque, 108) AS HoraEmbarque,

    Cliente = COALESCE(
        NULLIF(LTRIM(RTRIM(cs.NombreCliente)), ''),
        NULLIF(LTRIM(RTRIM(cs.Nombrecliente)), ''),
        p.Cliente
    ),
    p.Vendedor,
    p.Ruta,
    p.Presentacion,
    p.Observacion,

    ISNULL(kg.KgProgramados, 0) AS Saldo,
    ISNULL(p.OtrosPedidos, 0) AS OtrosPedidos,
    ISNULL(p.Credito, 0) AS Credito,

    ISNULL(l.Fletera, '') AS Fletera,
    ISNULL(l.EspacioTarimas, 0) AS EspacioTarimas,
    CONVERT(VARCHAR(5), l.HoraLlegadaUnidad, 108) AS HoraLlegadaUnidad,
    ISNULL(l.EstatusLogistico, 'PENDIENTE FLETERA') AS EstatusLogistico,
    ISNULL(l.ObservacionLogistica, '') AS ObservacionLogistica,
    ISNULL(l.MotivoCancelacion, '') AS MotivoCancelacion,
    ISNULL(l.MotivoCancelacionFletera, '') AS MotivoCancelacionFletera,
    CAST(ISNULL(l.Cancelado, 0) AS BIT) AS Cancelado,
    CAST(ISNULL(l.CanceladoFletera, 0) AS BIT) AS CanceladoFletera
FROM dbo.OrdenVenta p
INNER JOIN dbo.series s 
    ON UPPER(LTRIM(RTRIM(s.NombreSerie))) = UPPER(LTRIM(RTRIM(p.Serie)))
LEFT JOIN dbo.ClienteSap cs
    ON UPPER(LTRIM(RTRIM(cs.Cliente))) = UPPER(LTRIM(RTRIM(p.Cliente)))
OUTER APPLY (
    SELECT
        KgProgramados = SUM(CAST(ISNULL(op.Peso, 0) AS DECIMAL(18,4)))
    FROM dbo.OrdenVentaProducto op
    WHERE op.PedidoId = p.Id
) kg
LEFT JOIN dbo.LogisticaOrdenVenta l
    ON l.OrdenVentaId = p.Id
WHERE
    p.FechaEntrega >= @FechaInicio
    AND p.FechaEntrega < DATEADD(DAY, 1, @FechaFin)
    AND UPPER(LTRIM(RTRIM(s.Sucursal))) = 'MATRIZ'
    AND p.Estatus = 0
ORDER BY
    p.FechaEntrega,
    p.Ruta,
    COALESCE(
        NULLIF(LTRIM(RTRIM(cs.NombreCliente)), ''),
        NULLIF(LTRIM(RTRIM(cs.Nombrecliente)), ''),
        p.Cliente
    );";

            await using var cn = new SqlConnection(_connString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
            cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new PedidoLogisticaDto
                {
                    Id = GetInt(rd, "Id"),
                    Consecutivo = GetString(rd, "Consecutivo"),
                    Serie = GetString(rd, "Serie"),
                    NombreSerie = GetString(rd, "NombreSerie"),
                    Sucursal = GetString(rd, "Sucursal"),
                    SucursalId = GetString(rd, "SucursalId"),

                    FechaEntrega = GetDateTime(rd, "FechaEntrega"),
                    FechaEmbarque = GetNullableDateTime(rd, "FechaEmbarque"),
                    HoraEmbarque = GetString(rd, "HoraEmbarque"),

                    Cliente = GetString(rd, "Cliente"),
                    Vendedor = GetString(rd, "Vendedor"),
                    Ruta = GetString(rd, "Ruta"),
                    Presentacion = GetString(rd, "Presentacion"),
                    Observacion = GetString(rd, "Observacion"),

                    Saldo = GetDecimal(rd, "Saldo"),
                    OtrosPedidos = GetDecimal(rd, "OtrosPedidos"),
                    Credito = GetDecimal(rd, "Credito"),

                    Fletera = GetString(rd, "Fletera"),
                    EspacioTarimas = GetInt(rd, "EspacioTarimas"),
                    HoraLlegadaUnidad = GetString(rd, "HoraLlegadaUnidad"),
                    EstatusLogistico = GetString(rd, "EstatusLogistico"),
                    ObservacionLogistica = GetString(rd, "ObservacionLogistica"),
                    MotivoCancelacion = GetString(rd, "MotivoCancelacion"),
                    MotivoCancelacionFletera = GetString(rd, "MotivoCancelacionFletera"),
                    Cancelado = GetBool(rd, "Cancelado"),
                    CanceladoFletera = GetBool(rd, "CanceladoFletera")
                });
            }

            return Json(lista);
        }

        [HttpPost]
        public async Task<IActionResult> GuardarLogistica([FromBody] GuardarLogisticaDto model)
        {
            if (model == null || model.OrdenVentaId <= 0)
                return BadRequest(new { ok = false, mensaje = "Orden de venta inválida." });

            if (string.IsNullOrWhiteSpace(_connString))
                return StatusCode(500, new { ok = false, mensaje = "No hay ConnectionString configurado: DefaultConnection." });

            var usuario = User?.Identity?.Name ?? "Sistema";

            TimeSpan? horaLlegada = null;

            if (!string.IsNullOrWhiteSpace(model.HoraLlegadaUnidad))
            {
                if (TimeSpan.TryParse(model.HoraLlegadaUnidad, out var hora))
                    horaLlegada = hora;
            }

            var sql = @"
IF EXISTS (SELECT 1 FROM dbo.LogisticaOrdenVenta WHERE OrdenVentaId = @OrdenVentaId)
BEGIN
    UPDATE dbo.LogisticaOrdenVenta
    SET
        Fletera = @Fletera,
        EspacioTarimas = @EspacioTarimas,
        HoraLlegadaUnidad = @HoraLlegadaUnidad,
        EstatusLogistico = @EstatusLogistico,
        ObservacionLogistica = @ObservacionLogistica,
        MotivoCancelacion = @MotivoCancelacion,
        MotivoCancelacionFletera = @MotivoCancelacionFletera,
        Cancelado = @Cancelado,
        CanceladoFletera = @CanceladoFletera,
        UsuarioModificacion = @Usuario,
        FechaModificacion = GETDATE()
    WHERE OrdenVentaId = @OrdenVentaId;
END
ELSE
BEGIN
    INSERT INTO dbo.LogisticaOrdenVenta
    (
        OrdenVentaId,
        Fletera,
        EspacioTarimas,
        HoraLlegadaUnidad,
        EstatusLogistico,
        ObservacionLogistica,
        MotivoCancelacion,
        MotivoCancelacionFletera,
        Cancelado,
        CanceladoFletera,
        UsuarioRegistro,
        FechaRegistro
    )
    VALUES
    (
        @OrdenVentaId,
        @Fletera,
        @EspacioTarimas,
        @HoraLlegadaUnidad,
        @EstatusLogistico,
        @ObservacionLogistica,
        @MotivoCancelacion,
        @MotivoCancelacionFletera,
        @Cancelado,
        @CanceladoFletera,
        @Usuario,
        GETDATE()
    );
END";

            await using var cn = new SqlConnection(_connString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@OrdenVentaId", SqlDbType.Int).Value = model.OrdenVentaId;
            cmd.Parameters.Add("@Fletera", SqlDbType.VarChar, 150).Value = ToDb(model.Fletera);
            cmd.Parameters.Add("@EspacioTarimas", SqlDbType.Int).Value = ToDb(model.EspacioTarimas);
            cmd.Parameters.Add("@HoraLlegadaUnidad", SqlDbType.Time).Value = horaLlegada.HasValue ? horaLlegada.Value : DBNull.Value;
            cmd.Parameters.Add("@EstatusLogistico", SqlDbType.VarChar, 50).Value = string.IsNullOrWhiteSpace(model.EstatusLogistico) ? "PENDIENTE FLETERA" : model.EstatusLogistico;
            cmd.Parameters.Add("@ObservacionLogistica", SqlDbType.VarChar).Value = ToDb(model.ObservacionLogistica);
            cmd.Parameters.Add("@MotivoCancelacion", SqlDbType.VarChar).Value = ToDb(model.MotivoCancelacion);
            cmd.Parameters.Add("@MotivoCancelacionFletera", SqlDbType.VarChar).Value = ToDb(model.MotivoCancelacionFletera);
            cmd.Parameters.Add("@Cancelado", SqlDbType.Bit).Value = model.Cancelado;
            cmd.Parameters.Add("@CanceladoFletera", SqlDbType.Bit).Value = model.CanceladoFletera;
            cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 100).Value = usuario;

            await cmd.ExecuteNonQueryAsync();

            return Json(new { ok = true, mensaje = "Logística guardada correctamente." });
        }

        private static object ToDb(object? value)
        {
            if (value == null)
                return DBNull.Value;

            if (value is string s && string.IsNullOrWhiteSpace(s))
                return DBNull.Value;

            return value;
        }

        private static string GetString(SqlDataReader rd, string column)
        {
            var i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? "" : Convert.ToString(rd.GetValue(i)) ?? "";
        }

        private static int GetInt(SqlDataReader rd, string column)
        {
            var i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? 0 : Convert.ToInt32(rd.GetValue(i));
        }

        private static decimal GetDecimal(SqlDataReader rd, string column)
        {
            var i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? 0m : Convert.ToDecimal(rd.GetValue(i));
        }

        private static bool GetBool(SqlDataReader rd, string column)
        {
            var i = rd.GetOrdinal(column);
            return !rd.IsDBNull(i) && Convert.ToBoolean(rd.GetValue(i));
        }

        private static DateTime GetDateTime(SqlDataReader rd, string column)
        {
            var i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? DateTime.MinValue : Convert.ToDateTime(rd.GetValue(i));
        }

        private static DateTime? GetNullableDateTime(SqlDataReader rd, string column)
        {
            var i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? null : Convert.ToDateTime(rd.GetValue(i));
        }
    }
}