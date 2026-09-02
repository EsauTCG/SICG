using Plataforma_CG.Models;
using Plataforma_CG.Models.Operaciones.Etiquetas;
using Plataforma_CG.Models.Operaciones.Planeacion.Extra;
using Plataforma_CG.ViewModels;
using System.Data.SqlClient;
using System.Drawing;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Plataforma_CG.AccesoDatos.Operaciones
{
    public class AccesoEtiquetacion
    {
        SqlConnection _conn;
        private string _cadena = new Conexion().GetCadenaSQLSIGO();
        private string _cadenap1 = new Conexion().GetCadenaSQLP1();
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
        public List<PlanEtiModel> ConsultarEtiquetacionP1(string busq)
        {
            _conn = new SqlConnection(_cadenap1);
            var plan = new List<PlanEtiModel>();
            string query = $@"select top 100 b.ArticuloId,b.Nombre as 'Producto', b.Etiquetacion, a.Nombre,A.INTERFACE AS DiasCaducidad from CommerciaNet.dbo.Colector a 
inner join CommerciaNet.dbo.Articulo b on b.Etiquetacion = a.ColectorId 
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
        #region EtiquetasTIF
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
                plan = cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception)
            {
            }
            _conn.Close();


            return plan;
        }
        public bool LogEtiquetas(string sku, int oldeti, int neweti, string usr,string ubic)
        {
            _conn = new SqlConnection(_cadena);
            var res = false;
            string query = $"insert into logEtiq ([Sucursal],[ArticuloId],[Usuario],[EtiqOrigen],[EtiqNuevo],[FechaHora]) values('{ubic}','{sku}','{usr}',{oldeti},{neweti},GETDATE())";
            SqlCommand cmd = new SqlCommand(query, _conn);
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
        #endregion
        #region EtiquetasP1
        public List<EtiquetasModel> ConsultarEtiquetasP1()
        {
            _conn = new SqlConnection(_cadenap1);
            var plan = new List<EtiquetasModel>();
            string query = $@"select ColectorId,Nombre,ISNULL(Interface,30) as 'Caducidad'  from CommerciaNET.dbo.Colector where SistemaId='ETI';";
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
        public bool ModificarEtiquetacionP1(string sku, int etiqueta)
        {
            _conn = new SqlConnection(_cadenap1);
            var plan = false;
            string query = $@"update CommerciaNet.dbo.Articulo set Etiquetacion={etiqueta} where ArticuloId='{sku}'";
            SqlCommand cmd = new SqlCommand(query, _conn);

            _conn.Open();
            try
            {
                plan = cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception)
            {
            }
            _conn.Close();


            return plan;
        }
        public bool LogEtiquetasP1(string sku, int oldeti, int neweti, string usr)
        {
            _conn = new SqlConnection(_cadena);
            var res = false;
            string query = $"insert into logEtiq ([Sucursal],[ArticuloId],[Usuario],[EtiqOrigen],[EtiqNuevo],[FechaHora]) values('P1','{sku}','{usr}',{oldeti},{neweti},GETDATE())";
            SqlCommand cmd = new SqlCommand(query, _conn);
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
        public List<LogEtiquetacionModel> ReporteEtiquetas(string suc, string fechain, string fechafin, List<EtiquetasModel> etq)
        {
            _conn = new SqlConnection(_cadena);
            
            var res = new List<LogEtiquetacionModel>();
            string query= $"select a.Sucursal,a.ArticuloId,b.ProductoNombre,a.EtiqOrigen,a.EtiqNuevo,a.FechaHora from LogEtiq a" +
$" inner join ArticuloSap b on b.ProductoCodigo = a.ArticuloId" +
$" where a.Sucursal = '{suc}'" +
$" and CONVERT(date, a.FechaHora) between '{fechain}' and '{fechafin}'";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        res.Add(new LogEtiquetacionModel
                        {
                            Sucursal = dr["Sucursal"].ToString(),
                            ArticuloId = dr["ArticuloId"].ToString(),
                            ProductoNombre = dr["ProductoNombre"].ToString(),
                            EtiqOrigen = Convert.ToInt32(dr["EtiqOrigen"]),
                            NomOrigen = etq.Where(i=>i.ColectorId== Convert.ToInt32(dr["EtiqOrigen"])).FirstOrDefault().Nombre,
                            EtiqNuevo = Convert.ToInt32(dr["EtiqNuevo"]),
                            NomNuevo= etq.Where(i => i.ColectorId == Convert.ToInt32(dr["EtiqNuevo"])).FirstOrDefault().Nombre,
                            FechaHora = Convert.ToDateTime(dr["FechaHora"])
                        });
                    }
                }
            }
            catch (Exception)
            {

            }
            _conn.Close();
            return res;
        }
        #endregion
    }
}
