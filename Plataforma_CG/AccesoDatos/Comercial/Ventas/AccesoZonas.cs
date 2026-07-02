using Plataforma_CG.Models.Comercial.Ventas;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Ventas
{
    public class AccesoZonas
    {
        public List<ZonasModel> Listar()
        {
            var lista = new List<ZonasModel>();
            var cn = new Conexion();
            using (var conexion = new SqlConnection(cn.GetCadenaSQLVentas()))
            {
                conexion.Open();
                SqlCommand cmd = new SqlCommand("select * from Zonas", conexion);

                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new ZonasModel()
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            Nombre = dr["Nombre"].ToString(),
                            Factor = Convert.ToDouble(dr["Factor"])

                        });
                    }
                }
            }
            return lista;
        }
    }
}
