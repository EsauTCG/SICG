using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Office2010.Excel;
using Humanizer;
using ImageMagick;
using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Operaciones.Planeacion.Extra;
using System.Data.SqlClient;
using System.Numerics;
using System.Security.Claims;

namespace Plataforma_CG.AccesoDatos.Operaciones.Planeacion
{
    public class AccesoReporteSemanal
    {
        private SqlConnection _conn;
        private string _cadena = new Conexion().GetCadenaSQLSIGO();
        private string _cadenatif = new Conexion().GetCadenaSQLTIFVentas();

        public List<PlanResumenModel> ConsultarReporte(string fechain, string fechafin,int clasid)
        {
            var lista = new List<PlanResumenModel>();
            _conn = new SqlConnection(_cadena);
            string query = $"" +
    $"WITH PlanDiarioAgrupado AS " +
    $"( " +
    $"    SELECT " +
    $"        pd.ProductoCodigo, " +
    $"        ISNULL( " +
    $"            pd.ProductoCodigoConvertido, " +
    $"            pd.ProductoCodigo " +
    $"        ) AS ProductoCodigoConvertido, " +
    $"        SUM(ISNULL(pd.KgLote, 0)) AS KgNatural, " +
    $"        MAX( " +
    $"            ISNULL(pd.PorcentajeInyeccion, 0) " +
    $"        ) AS PorcentajeInyeccion, " +
    $"        SUM( " +
    $"            ISNULL(pd.KgInyeccion, pd.KgLote) " +
    $"        ) AS KgInyeccion " +
    $"    FROM PlanDiario pd " +
    $"    INNER JOIN PlaneacionProduccion ppd " +
    $"        ON ppd.PlaneacionId = pd.PlaneacionId " +
    $"inner join Clasificacion cls on cls.SKU=ppd.TipoPlan and cls.Id=@clasid " +
    $"    WHERE " +
    $"        ppd.FechaPlan BETWEEN '{fechain}' AND '{fechafin}' " +
    $"    GROUP BY " +
    $"        pd.ProductoCodigo, " +
    $"        ISNULL( " +
    $"            pd.ProductoCodigoConvertido, " +
    $"            pd.ProductoCodigo " +
    $"        ) " +
    $") " +
    $"SELECT " +
    $"    p.ProductoCodigo, " +
    $"    pd.ProductoCodigoConvertido, " +
    $"    pd.KgNatural, " +
    $"    pd.PorcentajeInyeccion, " +
    $"    pd.KgInyeccion, " +
    $"    p.fk_Clasificacion, " +
    $"    AVG(ISNULL(p.Porcentaje, 0)) AS Porcentaje, " +
    $"    ISNULL(p.LineaCodigo, '') AS LineaCodigo, " +
    $"    m.Nombre AS Master " +
    $"FROM Participacion p " +
    $"INNER JOIN PlanDiarioAgrupado pd " +
    $"    ON pd.ProductoCodigo = p.ProductoCodigo " +
    $"LEFT JOIN MasterProd mp " +
    $"    ON mp.SKU = p.ProductoCodigo " +
    $"LEFT JOIN Masters m " +
    $"    ON m.Id = mp.MasterID " +
    $"WHERE " +
    $"    p.fk_Clasificacion = @clasid " +
    $"GROUP BY " +
    $"    p.ProductoCodigo, " +
    $"    pd.ProductoCodigoConvertido, " +
    $"    pd.KgNatural, " +
    $"    pd.PorcentajeInyeccion, " +
    $"    pd.KgInyeccion, " +
    $"    p.fk_Clasificacion, " +
    $"    ISNULL(p.LineaCodigo, ''), " +
    $"    m.Nombre " +
    $"ORDER BY " +
    $"    p.ProductoCodigo, " +
    $"    pd.ProductoCodigoConvertido;";
            SqlCommand cmd = new SqlCommand(query, _conn);
            cmd.Parameters.AddWithValue("@clasid", clasid);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new PlanResumenModel
                        {
                            ProductoCodigo = dr["ProductoCodigo"].ToString(),
                            ProductoCodigoConvertido = dr["ProductoCodigoConvertido"].ToString(),
                            KgNatural = Convert.ToDecimal(dr["KgNatural"]),
                            PorcentajeInyeccion = Convert.ToDecimal(dr["PorcentajeInyeccion"]),
                            KgInyeccion = Convert.ToDecimal(dr["KgInyeccion"]),
                            fk_Clasificacion = Convert.ToInt32(dr["fk_Clasificacion"]),
                            Porcentaje = Convert.ToDecimal(dr["Porcentaje"]),
                            LineaCodigo = dr["LineaCodigo"].ToString(),
                            Master = dr["Master"].ToString()
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
        public double ConsultarProduccion(string fechain, string fechafin, string sku, int clasid)
        {
            string plantilla = "";
            if (clasid==8)
            {
                plantilla = "514";
            }
            else
            {
                plantilla = "513";
            }
            var res = 0.0;
            _conn = new SqlConnection(_cadenatif);
            string query = $" select " +
$" p.Articulo," +
$" SUM(p.PesoNeto) as 'Peso'" +
$" from TIF_Meat.dbo.SolicitudReferencia b" +
$" inner join TIF_CommerciaNET.dbo.Proveedor a on a.ProveedorId = b.Referencia and b.TipoReferenciaId = 2" +
$" inner join TIF_Meat.dbo.Lote c on b.SolicitudProduccionId=c.LoteId" +
$" inner join TIF_Meat.dbo.Produccion p on p.LoteId=b.SolicitudProduccionId" +
$" where c.TipoLoteId in (7,8) and a.ProveedorId='{plantilla}' and CONVERT(date,p.FechaProduccion) between '{fechain}' and '{fechafin}' and p.Articulo='{sku}'" +
$" Group by p.Articulo";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr= cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        res = Convert.ToDouble(dr["Peso"]);
                    }
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
