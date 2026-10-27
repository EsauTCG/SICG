using Plataforma_CG.Models.Operaciones.Pieles;
using Plataforma_CG.Controllers.Operaciones.Inyeccion;
using System.Data;
using System.Data.SqlClient;
using Plataforma_CG.Models.Operaciones.Consumibles;

namespace Plataforma_CG.AccesoDatos.Operaciones.Consumibles
{
    public class AccesoConsumibles
    {

        private string _cadena = new Conexion().GetCadenaSQLSIGO();
        private string _cadenatif = new Conexion().GetCadenaSQLTIFVentas();
        SqlConnection _conn;

        public List<ConsumiblesModel> ConsultarConsumibles(string tipo, string fechain, string fechafin)
        {
            var lista = new List<ConsumiblesModel>();
            _conn = new SqlConnection(_cadenatif);
            string query = $@"WITH EmpaquesPorProduccion AS
(
    SELECT
        a.ProduccionId,
        a.Articulo,
        MAX(CASE
            WHEN c.TipoEmpaqueId = 1 THEN c.Nombre
        END) AS Caja,
        MAX(CASE
            WHEN c.TipoEmpaqueId = 2 THEN c.Nombre
        END) AS Bolsa,
        MAX(CASE
            WHEN c.TipoEmpaqueId = 1 THEN b.Unidades
        END) AS Cajas,
        MAX(CASE
            WHEN c.TipoEmpaqueId = 2 THEN b.Unidades
        END) AS Bolsas,
        MAX(a.PesoNeto) AS PesoNeto
    FROM Produccion a
    INNER JOIN EmpaqueLog b
        ON b.ProduccionId = a.ProduccionId
    INNER JOIN Empaque c
        ON c.EmpaqueId = b.EmpaqueId
    WHERE
        a.CodigoEtiqueta LIKE '%{tipo}%'
        AND CONVERT(date, a.FechaProduccion)
            BETWEEN '{fechain}' AND '{fechafin}'
        AND UltimoProcesoId<>29 
    GROUP BY
        a.ProduccionId,
        a.Articulo
)
SELECT
    Articulo,
    Caja,
    Bolsa,
    SUM(COALESCE(Cajas, 0)) AS TotalCajas,
    MAX(Bolsas) AS [Bolsas/Caja],
    MAX(Cajas) AS [Pza/Bolsa],
    SUM(
        COALESCE(Cajas, 0) *
        COALESCE(Bolsas, 0)
    ) AS TotalPzas,
    SUM(COALESCE(PesoNeto, 0)) AS TotalKG
FROM EmpaquesPorProduccion
GROUP BY
    Articulo,
    Caja,
    Bolsa
ORDER BY
    Articulo,
    Caja,
    Bolsa;";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new ConsumiblesModel
                        {
                            Articulo = Convert.ToString(dr["Articulo"]),
                            Caja = Convert.ToString(dr["Caja"]),
                            Bolsa = Convert.ToString(dr["Bolsa"]),
                            TotalCajas = Convert.ToInt32(dr["TotalCajas"]),
                            BolsasCaja = Convert.ToInt32(dr["Bolsas/Caja"]),
                            PzaBolsa = Convert.ToInt32(dr["Pza/Bolsa"]),
                            TotalPzas = Convert.ToInt32(dr["TotalPzas"]),
                            TotalKG = Convert.ToDouble(dr["TotalKG"])
                        });
                    }
                }
            }
            catch (Exception)
            {


            }
            _conn.Close();
            return lista;
        }
        public string ConsultarNombre(string producto)
        {
            var res = "";
            _conn = new SqlConnection(_cadena);
            string query = $@"select ProductoNombre from ArticuloSap where ProductoCodigo='{producto}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = cmd.ExecuteScalar().ToString();
            }
            catch (Exception)
            {


            }
            _conn.Close();
            return res;
        }
        public double ConsultarPlanIndi(string sku, string fechain, string fechafin)
        {
            var res = 0.0;

            using (SqlConnection conn = new SqlConnection(_cadena))
            {
                string query = @$"select a.ProductoCodigo, SUM(KgLote) as 'Peso' from PlanDiario a
inner join PlaneacionProduccion b  on b.PlaneacionId=a.PlaneacionId
where CONVERT(date,b.FechaPlan) between '{fechain}' and '{fechafin}'
and a.ProductoCodigo = '{sku}'
and a.PorcentajeInyeccion>0 and a.PorcentajeInyeccion is not null
group by a.ProductoCodigo";
                conn.Open();
                try
                {
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {

                       

                        using (SqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                res = Convert.ToDouble(dr["Peso"]);
                            }
                        }
                    }
                }
                catch (Exception)
                {

                    
                }
                conn.Close();
            }

            return res;
        }
        //        public List<(string SKU, double PesoPlan, double PesoIny)> ConsultarPlan(string sku,string fechain,string fechafin)
        //        {
        //            var lista = new List<(string SKU, double PesoPlan, double PesoIny)>();

        //            using (SqlConnection conn = new SqlConnection(_cadena))
        //            {
        //                string query = @$"select a.ProductoCodigo, SUM(KgLote) from PlanDiario a
        //inner join PlaneacionProduccion b  on b.PlaneacionId=a.PlaneacionId
        //where CONVERT(date,b.FechaPlan) between '{fechain}' and '{fechafin}'
        //and a.ProductoCodigo = '{sku}'
        //and a.PorcentajeInyeccion>0 and a.PorcentajeInyeccion is not null
        //group by a.ProductoCodigo";

        //                using (SqlCommand cmd = new SqlCommand(query, conn))
        //                {

        //                    conn.Open();

        //                    using (SqlDataReader dr = cmd.ExecuteReader())
        //                    {
        //                        while (dr.Read())
        //                        {
        //                            lista.Add((
        //                                SKU: Convert.ToString(dr["SKU"]),
        //                                PesoPlan: Convert.ToDouble(dr["PesoPlan"]),
        //                                PesoIny: ConsultarInyeccion(dr["SKU"].ToString(),fechain,fechafin)
        //                            ));
        //                        }
        //                    }
        //                }
        //            }

        //            return lista;
        //        }
        public (double Peso, double Porcentaje) ConsultarInyeccion(
    string sku,
    string fechain,
    string fechafin)
        {
            var res = (Peso: 0.0, Porcentaje: 0.0);

            using (SqlConnection conn = new SqlConnection(_cadenatif))
            {
                string query = @" use TIF_Inyeccion;
            SELECT
    b.SKU,
    COALESCE(SUM(a.Peso), 0.0) AS Peso,
    b.Porcentaje AS Porcentaje
FROM Entradas a
RIGHT JOIN Recetas b
    ON b.SKU = a.SKU
    AND a.FechaHora >= @FechaInicio
    AND a.FechaHora < @FechaFin
WHERE b.SKU = @SKU
GROUP BY
    b.SKU,
    b.Porcentaje;";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value =
                        DateTime.Parse(fechain).Date;

                    cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value =
                        DateTime.Parse(fechafin).Date;

                    cmd.Parameters.Add("@SKU", SqlDbType.VarChar).Value = sku;

                    conn.Open();

                    using (SqlDataReader dr = cmd.ExecuteReader())
                    {
                        if (dr.Read())
                        {
                            res = (
                                Peso: dr["Peso"] == DBNull.Value
                                    ? 0.0
                                    : Convert.ToDouble(dr["Peso"]),

                                Porcentaje: dr["Porcentaje"] == DBNull.Value
                                    ? 0.0
                                    : Convert.ToDouble(dr["Porcentaje"])
                            );
                        }
                    }
                }
            }

            return res;
        }
    }
}
