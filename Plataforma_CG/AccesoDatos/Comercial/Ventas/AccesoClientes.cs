using Plataforma_CG.Models.Comercial.Ventas;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Ventas
{
    public class AccesoClientes
    {
        public List<ClienteModel> Listar()
        {
            var lista = new List<ClienteModel>();
            var cn = new Conexion();
            using (var conexion = new SqlConnection(cn.GetCadenaSQLVentas()))
            {
                conexion.Open();
                SqlCommand cmd = new SqlCommand("select distinct * from Clientes", conexion);
                //cmd.CommandType = CommandType.StoredProcedure;

                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new ClienteModel()
                        {
                            Codigo = Convert.ToInt64(dr["Codigo"]),
                            RazonSocial = dr["RazonSocial"].ToString(),
                            Direccion = dr["Direccion"].ToString(),
                            Estatus = Convert.ToInt32(dr["Estatus"]),
                            Concepto = dr["Concepto"].ToString(),
                            Telefono = dr["Telefono"].ToString(),
                            RFC = dr["RFC"].ToString(),
                            Apellido = dr["Apellido"].ToString(),
                            Clasificacion = dr["Clasificacion"].ToString(),
                            CodigoSap = dr["CodigoSap"].ToString(),
                            fk_Zona = Convert.ToInt32(dr["fk_Zona"])


                        });
                    }
                }
            }
            return lista;
        }
        public List<ClienteModel> ListarActivo()
        {
            return Listar().Where(item => item.Estatus == 1).ToList();
        }
    }
}
