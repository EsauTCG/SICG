using Plataforma_CG.Models.Comercial.Ventas;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Ventas
{
    public class AccesoVisitas
    {
        SqlConnection _conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public List<VisitasModel>Listar(int idProspecto)
        {
            var lista = new List<VisitasModel>();
            string query = $"select * from VisitasProspectos where fk_Prospecto={idProspecto}";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new VisitasModel
                        {
                            Id= Convert.ToInt32(dr["Id"]),
                            fk_Prospecto = Convert.ToInt32(dr["fk_Prospecto"]),
                            FechaHora = Convert.ToDateTime(dr["FechaHora"]).ToString("yyyy-MM-dd HH:mm"),
                            Ubicacion = dr["Ubicacion"].ToString(),
                            Usuario = dr["Usuario"].ToString(),
                            RutaFotoVisita = dr["RutaFotoVisita"].ToString()
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
        public bool InsertarVisita(VisitasModel model)
        {
            var res = false;
            string query =$@"insert into VisitasProspectos (fk_Prospecto,FechaHora,Ubicacion,Usuario,RutaFotoVisita) 
 values({model.fk_Prospecto},GETDATE(),'{model.Ubicacion}','{model.Usuario}','{model.RutaFotoVisita}')";
            SqlCommand cmd= new SqlCommand(query,_conn);
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
