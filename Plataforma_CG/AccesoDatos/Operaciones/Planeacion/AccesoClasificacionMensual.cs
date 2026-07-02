using Plataforma_CG.Models.Operaciones.Planeacion;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Models.Comercial.Planeacion;

namespace Plataforma_CG.AccesoDatos.Operaciones.Planeacion
{
    public class AccesoClasificacionMensual
    {
        private string _cadena = new Conexion().GetCadenaSQLSIGO();
        SqlConnection _conn;
        public List<PlaneacionMensualModel> ListarPlaneacionMensual(string ano, string mes, string clasi)
        {
            _conn = new SqlConnection(_cadena);
            var lista = new List<PlaneacionMensualModel>();
            string query = @$"SELECT  
    ISNULL(a.Id, 0)                AS Id,
    ISNULL(a.Fecha, '{ano}-{mes}-1')  AS Fecha,
    b.fk_Clasificacion                          AS Clasificacion,
    b.Nombre                       AS SubClas,
    ISNULL(a.Canales, 0)           AS Canales,
    ISNULL(a.PesoPromedio, b.PesoPromedio) AS PesoPromedio,
    ISNULL(a.PesoTotal, 0)         AS PesoTotal,
    ISNULL(a.Porcentaje,0) as Porcentaje
FROM SubClasif b
LEFT JOIN PlanProduccion a
    ON a.fk_Clasificacion = b.Nombre
   AND a.Fecha = '{ano}-{mes}-1'
   inner join Clasificacion c 
   on c.SKU=b.fk_Clasificacion
    where c.SKU='{clasi}'
ORDER BY b.fk_Clasificacion;";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new PlaneacionMensualModel
                        {
                            Id = Convert.ToInt32(dr["ID"]),
                            Fecha = Convert.ToString(dr["Fecha"]),
                            SkuClasificacion = Convert.ToString(dr["Clasificacion"]),
                            NombreClasificacion = Convert.ToString(dr["SubClas"]),
                            Canales = Convert.ToInt32(dr["Canales"]),
                            PesoPromedio = Convert.ToDouble(dr["PesoPromedio"]),
                            PesoTotal = Convert.ToDouble(dr["PesoTotal"]),
                            Porcentaje = Convert.ToDouble(dr["Porcentaje"])
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
        public List<SubClasMensualModel> ListarSub(string anio, string mes)
        {
            _conn = new SqlConnection(_cadena);
            var lista = new List<SubClasMensualModel>();
            string query = $"select * from Clasificacion";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new SubClasMensualModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            SkuClasificacion = Convert.ToString(dr["SKU"]),
                            Nombre = Convert.ToString(dr["Nombre"]),
                            DetalleMensual = ListarPlaneacionMensual(anio, mes, Convert.ToString(dr["SKU"])),
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
        public bool Guardar(PlaneacionMensualModel model)
        {
            bool res = false;
            _conn = new SqlConnection(_cadena);
            PlanProduccionModel md = new PlanProduccionModel();
            md.Id = model.Id;
            md.Fecha = Convert.ToDateTime(model.Fecha).ToString("yyyy-MM-dd");
            md.Canales = model.Canales;
            md.PesoPromedio = model.PesoPromedio;
            md.PesoTotal = model.PesoTotal;
            md.fk_Clasificacion = model.NombreClasificacion;
            string query = $"insert into PlanProduccion " +
                $"([Fecha], " +
                $"[Canales], " +
                $"[PesoPromedio], " +
                $"[PesoTotal], " +
                $"[fk_Clasificacion]," +
                $"[Porcentaje]) " +
                $"values('{md.Fecha}', " +
                $"{md.Canales}, " +
                $"'{md.PesoPromedio}', " +
                $"'{md.PesoTotal}', " +
                $"'{md.fk_Clasificacion}', " +
                $"'{model.Porcentaje}')";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception)
            {

            }
            _conn.Close();
            return res;
        }
        public bool Modificar(PlaneacionMensualModel model)
        {
            bool res = false;
            _conn = new SqlConnection(_cadena);
            PlanProduccionModel md = new PlanProduccionModel();
            md.Id = model.Id;
            md.Fecha = Convert.ToDateTime(model.Fecha).ToString("yyyy-MM-dd");
            md.Canales = model.Canales;
            md.PesoPromedio = model.PesoPromedio;
            md.PesoTotal = model.PesoTotal;
            md.fk_Clasificacion = model.SkuClasificacion;
            string query = $"update PlanProduccion set " +
                $"Canales={md.Canales}, " +
                $"PesoPromedio='{md.PesoPromedio}', " +
                $"PesoTotal='{md.PesoTotal}', " +
                $"Porcentaje='{model.Porcentaje}' " +
                $"where Id={md.Id}";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public bool InsertarSemanaClas(SemanaClasificacionModel model)
        {
            _conn = new SqlConnection(_cadena);
            bool res = false;
            var fechaIn = model.FechaInicioSemana.ToString("yyyy-MM-dd");
            var fechaFin = model.FechaFinSemana.ToString("yyyy-MM-dd");
            EliminarSemanal(fechaIn,fechaFin);
            foreach (var item in model.Clasificaciones)
            {
                string query = $"insert into SemanaClasificacion ([FechaInicioSemana],[FechaFinSemana],[Clasificacion],[TotalCanales]) " +
    $"values ('{fechaIn}','{fechaFin}','{item.Clasificacion}',{item.TotalCanales})";
                SqlCommand cmd = new SqlCommand(query, _conn);
                _conn.Open();
                try
                {
                    res = cmd.ExecuteNonQuery() > 0;
                }
                catch (Exception)
                {

                }
                _conn.Close();
            }

            return res;
        }
        
        public bool EliminarSemanal(string fechain,string fechafin)
        {
            _conn = new SqlConnection(_cadena);
            bool res = false;
            string query = $"delete from SemanaClasificacion where FechaInicioSemana='{fechain}' and FechaFinSemana='{fechafin}'";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public List<SubclasificacionSemanalDTO> ContarSemanal(string fechain, string fechafin, string clas)
        {
            var lista = new List<SubclasificacionSemanalDTO>();

            using (SqlConnection conn = new SqlConnection(_cadena))
            {
                //        string query = @"
                //SELECT 
                //    b.Id AS SubClasificacionId,
                //    b.Nombre AS SubClasificacion,
                //    ISNULL(SUM(a.TotalCanales), 0) AS Canales
                //FROM SubClasif b
                //INNER JOIN Clasificacion c 
                //    ON c.Nombre = b.fk_Clasificacion
                //LEFT JOIN SemanaClasificacion a 
                //    ON a.Clasificacion = b.Nombre
                //    AND a.FechaInicioSemana = @fechain
                //    AND a.FechaFinSemana = @fechafin
                //WHERE c.Nombre = @clas
                //GROUP BY b.Id, b.Nombre
                //ORDER BY b.Nombre";
                string query = @"SELECT 
    b.Id AS SubClasificacionId,
    b.Nombre AS SubClasificacion,
    ISNULL(SUM(a.TotalCanales), 0) AS Canales,

    ISNULL(AVG(dm.PesoPromedio), 0) AS PesoPromedio

FROM SubClasif b

INNER JOIN Clasificacion c 
    ON c.Nombre = b.fk_Clasificacion

LEFT JOIN SemanaClasificacion a 
    ON a.Clasificacion = b.Nombre
    AND a.FechaInicioSemana = @fechain
    AND a.FechaFinSemana = @fechafin

-- 🔥 AQUÍ ESTÁ LA CLAVE
LEFT JOIN PlanProduccion dm
    ON dm.fk_Clasificacion = b.Nombre
    AND dm.Mes = MONTH(@fechain)
    AND dm.Anio = YEAR(@fechain)

WHERE c.Nombre = @clas

GROUP BY b.Id, b.Nombre

ORDER BY b.Nombre";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@fechain", fechain);
                cmd.Parameters.AddWithValue("@fechafin", fechafin);
                cmd.Parameters.AddWithValue("@clas", clas);

                conn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new SubclasificacionSemanalDTO
                        {
                            SubClasificacionId = Convert.ToInt32(dr["SubClasificacionId"]),
                            SubClasificacion = dr["SubClasificacion"].ToString(),
                            Canales = Convert.ToInt32(dr["Canales"]),
                            PesoPromedio = Convert.ToDouble(dr["PesoPromedio"])
                        });
                    }
                }
                conn.Close();
            }

            return lista;
        }
    }
}
