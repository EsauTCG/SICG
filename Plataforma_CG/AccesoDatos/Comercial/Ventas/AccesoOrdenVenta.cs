using Plataforma_CG.Models;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Ventas
{
    public class AccesoOrdenVenta
    {
        private SqlConnection _conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public List<OrdenVentaModel> ListarOrden()
        {
            var lista = new List<OrdenVentaModel>();
            string query = "select * from OrdenVenta";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new OrdenVentaModel
                        {
                            Id = Convert.ToInt64(dr["Id"]),
                            Codigo = dr["Codigo"].ToString(),
                            Serie = dr["Codigo"].ToString(),
                            ClienteSAP = dr["ClienteSAP"].ToString(),
                            Vendedor = dr["Vendedor"].ToString(),
                            Ruta = dr["Ruta"].ToString(),
                            Presentacion = dr["Presentacion"].ToString(),
                            Observacion = dr["Observacion"].ToString()
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
        public OrdenVentaModel ConsultarOrdenId(long id)
        {
            return ListarOrden().Where(item=>item.Id==id).FirstOrDefault();
        }
        public List<ProductoVentaModel> ListarProdVen(long ordenId)
        {
            var lista = new List<ProductoVentaModel>();
            string query = "select * from ProductoVenta";
            _conn.Open();
            try
            {
                
            }
            catch (Exception)
            {

            }
            _conn.Close();
            return lista;
        }
    }
}
