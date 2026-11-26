using Dapper;
using Plataforma_CG.Models.ReporteRetrabajo;
using System.Data.SqlClient;

namespace Plataforma_CG.Services.ReporteRetrabajo
{
    public class DecomisosViscerasTIFService
    {
        private readonly string _connectionString;

        public DecomisosViscerasTIFService(
            IConfiguration configuration)
        {
            _connectionString =
                configuration.GetConnectionString("CadenaMeatTIF")
                ?? throw new InvalidOperationException(
                    "No se encontró la conexión de PyMeat.");
        }

        private const string SqlSacrificio = @"
SELECT
    CONVERT(DATE, B.FechaHora) AS Fecha,
    COUNT(DISTINCT A.CodigoEtiqueta) AS CantidadAnimales,
    CAST(SUM(PP.Peso) AS DECIMAL(18,3)) AS KgPie
FROM Produccion A
INNER JOIN CanalDetalle B
    ON B.ProduccionId = A.ProduccionId
INNER JOIN PesoProducto PP
    ON PP.ProduccionId = A.ProduccionId
   AND PP.TipoPesoId = 1
WHERE B.FechaHora >= @FechaInicio
  AND B.FechaHora < DATEADD(DAY, 1, @FechaFin)
GROUP BY
    CONVERT(DATE, B.FechaHora)
ORDER BY
    Fecha;";

        public async Task<IEnumerable<SacrificioDiarioTIF>>
            ConsultarSacrificioAsync(
                DateTime fechaInicio,
                DateTime fechaFin)
        {
            using var conexion =
                new SqlConnection(_connectionString);

            return await conexion.QueryAsync<SacrificioDiarioTIF>(
                SqlSacrificio,
                new
                {
                    FechaInicio = fechaInicio.Date,
                    FechaFin = fechaFin.Date
                },
                commandTimeout: 120);
        }
    }
}
