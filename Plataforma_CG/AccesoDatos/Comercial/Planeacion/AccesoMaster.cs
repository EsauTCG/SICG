using Plataforma_CG.Models.Comercial.Planeacion;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Planeacion
{
    public class AccesoMaster
    {
        private SqlConnection _conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public List<MasterModel> Consultar()
        {
            var lista = new List<MasterModel>();
            string query = "select * from Masters";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new MasterModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            Nombre = Convert.ToString(dr["Nombre"])
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
        public List<MasterModel> ConsultaClasificacion(int clasi)
        {
            var lista = new List<MasterModel>();
            string query = $@"select a.Id, a.Nombre from Masters a
                inner join ClasMaster b on b.fk_Master=a.Id
                where b.fk_Clasificacion={clasi}";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new MasterModel
                        {
                            Id=Convert.ToInt32(dr["Id"]),
                            Nombre = Convert.ToString(dr["Nombre"])
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
    }
}
