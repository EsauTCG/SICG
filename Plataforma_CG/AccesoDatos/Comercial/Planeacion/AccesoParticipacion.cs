using Plataforma_CG.Models.Comercial.Planeacion;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Planeacion
{
    public class AccesoParticipacion
    {
        SqlConnection _conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public List<ParticipacionModel> Consultar(int clasi)
        {
            var lista = new List<ParticipacionModel>();
            string query = $"select * from Participacion where fk_Clasificacion={clasi}";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new ParticipacionModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            ProductoCodigo = Convert.ToString(dr["ProductoCodigo"]),
                            fk_Clasificacion = Convert.ToInt32(dr["fk_Clasificacion"]),
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
    }
}
