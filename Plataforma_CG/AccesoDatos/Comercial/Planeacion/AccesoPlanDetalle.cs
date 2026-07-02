using Plataforma_CG.Models.Comercial.Planeacion;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Planeacion
{
    public class AccesoPlanDetalle
    {
        SqlConnection _conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public List<PlanDetalleModel> Consultar(int fk_Plan)
        {
            var lista = new List<PlanDetalleModel>();
            string query = $"select * from PlanDetalle where fk_Plan={fk_Plan}";
            SqlCommand cmd= new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new PlanDetalleModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            ProductoCodigo = Convert.ToString(dr["ProductoCodigo"]),
                            Porcentaje = Convert.ToDouble(dr["Porcentaje"]),
                            Peso = Convert.ToDouble(dr["Peso"]),
                            fk_Plan = Convert.ToInt32(dr["fk_Plan"])
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
        public bool Insertar(PlanDetalleModel model)
        {
            var res = false;
            string query = "insert into PlanDetalle " +
                $"values(" +
                $"'{model.ProductoCodigo}', " +
                $"{model.Porcentaje}, " +
                $"{model.Peso}, " +
                $"{model.fk_Plan})";
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
        public bool Eliminar(int fk_Plan)
        {
            var res = false;
            string query = $"delete from PlanDetalle where fk_Plan={fk_Plan}";
            SqlCommand cmd = new SqlCommand (query, _conn);
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
