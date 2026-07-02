using Plataforma_CG.Models.Comercial.Planeacion;
using System.Data.SqlClient;
using static Azure.Core.HttpHeader;

namespace Plataforma_CG.AccesoDatos.Comercial.Planeacion
{
    public class AccesoPlaneacion
    {
        private SqlConnection _conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());

        public List<PlanProduccionModel> ListarFecha(string fecha)
        {
            var lista = new List<PlanProduccionModel>();
            string query = $"select * from PlanProduccion where Fecha = '{Convert.ToDateTime(fecha).ToString("yyyy-MM-dd")}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new PlanProduccionModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            Fecha = Convert.ToString(dr["Fecha"]),
                            Canales = Convert.ToInt32(dr["Canales"]),
                            PesoPromedio = Convert.ToDouble(dr["PesoPromedio"]),
                            PesoTotal = Convert.ToDouble(dr["PesoTotal"]),
                            fk_Clasificacion = Convert.ToString(dr["fk_Clasificacion"])
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
        public List<PlanProduccionModel> ListarTodo()
        {
            var lista = new List<PlanProduccionModel>();
            string query = $"select * from PlanProduccion";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new PlanProduccionModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            Fecha = Convert.ToString(dr["Fecha"]),
                            Canales = Convert.ToInt32(dr["Canales"]),
                            PesoPromedio = Convert.ToDouble(dr["PesoPromedio"]),
                            PesoTotal = Convert.ToDouble(dr["PesoTotal"]),
                            fk_Clasificacion = Convert.ToString(dr["fk_Clasificacion"])
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
        public PlanProduccionModel ConsultarId(int id)
        {
            return ListarTodo().Where(item => item.Id == id).FirstOrDefault();
        }
        public string Insertar(PlanProduccionModel model)
        {
            var res = "";
            string query = "insert into PlanProduccion" +
                "([Fecha],[Canales],[PesoPromedio],[PesoTotal],[fk_Clasificacion])" +
                "values" +
                $"('{model.Fecha}'," +
                $"{model.Canales}," +
                $"{model.PesoPromedio}," +
                $"{model.PesoTotal}," +
                $"'{model.fk_Clasificacion}');" +
                $"SELECT SCOPE_IDENTITY();";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res= cmd.ExecuteScalar().ToString();
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public bool Modificar(PlanProduccionModel model)
        {
            bool res = false;
            string query = "update PlanProduccion " +
                "set " +
                $"[Canales]={model.Canales}," +
                $"[PesoPromedio]={model.PesoPromedio}," +
                $"[PesoTotal]={model.PesoTotal} " +
                $"where Id={model.Id}";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery()>0;
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
    }
}
