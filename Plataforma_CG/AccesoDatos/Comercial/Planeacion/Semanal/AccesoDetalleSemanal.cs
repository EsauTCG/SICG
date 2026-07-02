using Plataforma_CG.Models;
using Plataforma_CG.Models.Comercial.Planeacion.Semanal;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Planeacion.Semanal
{
    public class AccesoDetalleSemanal
    {
        private SqlConnection _conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public TodoSemanalModel Listar(int semana)
        {
            var lista = new TodoSemanalModel();
            lista._ListaDetalleSemanal = new List<DetalleSemanal>();
            string query = $"select * from DetalleSemanal where fk_Semana={semana}";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista._ListaDetalleSemanal.Add(new DetalleSemanal
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            ProductoCodigo = Convert.ToString(dr["ProductoCodigo"]),
                            Porcentaje = Convert.ToDouble(dr["Porcentaje"]),
                            Peso = Convert.ToDouble(dr["Peso"]),
                            fk_Semana = Convert.ToInt32(dr["fk_Semana"])
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
        public bool Insertar(DetalleSemanal model)
        {
            bool res = false;
            string query = $"insert into DetalleSemanal ([ProductoCodigo],[Porcentaje],[Peso],[fk_Semana]) values" +
                $"('{model.ProductoCodigo}'," +
                $"{model.Porcentaje}," +
                $"{model.Peso}," +
                $"{model.fk_Semana});";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                if (cmd.ExecuteNonQuery()>0)
                {
                    res = true;
                }
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public bool Eliminar(int semana)
        {
            bool res = false;
            string query = $"delete from DetalleSemanal where fk_Semana={semana}";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                if (cmd.ExecuteNonQuery()>0)
                {
                    res = true;
                }
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
    }
}
