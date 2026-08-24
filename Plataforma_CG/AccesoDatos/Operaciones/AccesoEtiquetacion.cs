using Plataforma_CG.Models.Operaciones.Etiquetas;
using Plataforma_CG.Models.Operaciones.Planeacion.Extra;
using System.Data.SqlClient;
using System.Drawing;

namespace Plataforma_CG.AccesoDatos.Operaciones
{
    public class AccesoEtiquetacion
    {
        SqlConnection _conn;
        private string _cadena = new Conexion().GetCadenaSQLSIGO();
        private string _cadenatif = new Conexion().GetCadenaSQLTIFVentas();
        public List<PlanEtiModel> ConsultarEtiquetacion(string busq)
        {
            _conn = new SqlConnection(_cadenatif);
            var plan = new List<PlanEtiModel>();
            string query = $@"select top 100 b.ArticuloId,b.Nombre as 'Producto', b.Etiquetacion, a.Nombre,A.INTERFACE AS DiasCaducidad from TIF_CommerciaNet.dbo.Colector a 
inner join TIF_CommerciaNet.dbo.Articulo b on b.Etiquetacion = a.ColectorId 
where a.sistemaid = 'ETI' and 
(b.ArticuloId like '%{busq}%' or b.Nombre like '%{busq}%')";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            using (var dr = cmd.ExecuteReader())
            {
                while (dr.Read())
                {
                    plan.Add(new PlanEtiModel
                    {
                        SKU = dr["ArticuloId"].ToString(), // SKU
                        Producto = dr["Producto"].ToString(), // Nombre del producto
                        Nombre = dr["Nombre"].ToString(), //Nombre de la etiquetación
                        Etiquetacion = Convert.ToInt32(dr["Etiquetacion"]), //Id de la etiquetación
                        DiasCaducidad = Convert.ToString(dr["DiasCaducidad"]) //Días de caducidad
                    });
                }
            }
            _conn.Close();


            return plan;
        }
        public List<EtiquetasModel> ConsultarEtiquetas()
        {
            _conn = new SqlConnection(_cadenatif);
            var plan = new List<EtiquetasModel>();
            string query = $@"select ColectorId,Nombre,ISNULL(Interface,30) as 'Caducidad'  from TIF_CommerciaNET.dbo.Colector where SistemaId='ETI';";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        plan.Add(new EtiquetasModel
                        {
                            ColectorId = Convert.ToInt32(dr["ColectorId"]),
                            Nombre = dr["Nombre"].ToString(),
                            Caducidad = Convert.ToInt32(dr["Caducidad"])
                        });
                    }
                }
            }
            catch (Exception)
            {


            }
            _conn.Close();


            return plan;
        }
        public bool ModificarEtiquetacion(string sku, int etiqueta)
        {
            _conn = new SqlConnection(_cadenatif);
            var plan = false;
            string query = $@"update TIF_CommerciaNet.dbo.Articulo set Etiquetacion={etiqueta} where ArticuloId='{sku}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            
            _conn.Open();
            try
            {
                plan = cmd.ExecuteNonQuery()>0;
            }
            catch (Exception)
            {
            }
            _conn.Close();


            return plan;
        }
        public bool LogEtiquetas(string sku, int oldeti, int neweti,string usr)
        {
            _conn = new SqlConnection(_cadena);
            var res = false;
            string query = $"insert into logEtiq ([Sucursal],[ArticuloId],[Usuario],[EtiqOrigen],[EtiqNuevo],[FechaHora]) values('TIF','{sku}','{usr}',{oldeti},{neweti},GETDATE())";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
    }
}
