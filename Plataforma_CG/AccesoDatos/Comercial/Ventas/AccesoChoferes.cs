using Plataforma_CG.Models;
using System.Data.SqlClient;
namespace Plataforma_CG.AccesoDatos.Comercial.Ventas
{
    public class AccesoChoferes
    {
        public List<ChoferModel> Listar()
        {
            var lista = new List<ChoferModel>();
            var cn = new Conexion();
            using (var conexion = new SqlConnection(cn.GetCadenaSQLVentas()))
            {
                conexion.Open();
                SqlCommand cmd = new SqlCommand("select * from Chofer", conexion);
                //cmd.CommandType = CommandType.StoredProcedure;

                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new ChoferModel()
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            Nombre = dr["Nombre"].ToString(),
                            NoUnidad = dr["NoUnidad"].ToString(),
                            Proveedor = dr["Proveedor"].ToString(),
                            Telefono = dr["Telefono"].ToString(),
                            KgCapac = dr["kgCapac"].ToString()



                        });
                    }
                }
            }
            return lista;
        }
        public bool Guardar(ChoferModel chof)
        {
            bool res = false;
            try
            {

                var cn = new Conexion();
                using (var conexion = new SqlConnection(cn.GetCadenaSQLVentas()))
                {
                    conexion.Open();
                    SqlCommand cmd = new SqlCommand("INSERT INTO [dbo].[Chofer]" +
                        $"VALUES('{chof.Nombre}'," +
                        $"'{chof.NoUnidad}'," +
                        $"'{chof.Proveedor}'," +
                        $"'{chof.Telefono}'," +
                        $"'{chof.KgCapac}'," +
                        $"0)", conexion);

                    cmd.ExecuteNonQuery();
                    conexion.Close();
                }
                res = true;
            }
            catch (Exception ex)
            {

            }

            return res;
        }
    }
}
