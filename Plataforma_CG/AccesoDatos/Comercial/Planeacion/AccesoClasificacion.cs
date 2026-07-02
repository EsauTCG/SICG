using Humanizer;
using Plataforma_CG.Models.Comercial.Planeacion;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Planeacion
{
    public class AccesoClasificacion
    {
        SqlConnection _conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public List<ClasificacionModel> Consultar()
        {
            var lista = new List<ClasificacionModel>();
            string query = "select * from Clasificacion";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new ClasificacionModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            SKU = Convert.ToString(dr["SKU"]),
                            Nombre = Convert.ToString(dr["Nombre"]),
                            PesoPromedio = Convert.ToDouble(dr["PesoPromedio"])
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
