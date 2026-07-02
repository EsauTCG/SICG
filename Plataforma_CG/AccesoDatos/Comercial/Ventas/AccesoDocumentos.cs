using Plataforma_CG.Models.Comercial.Ventas;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Ventas
{
    public class AccesoDocumentos
    {
        SqlConnection conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public List<DocumentoClienteModel> DocClie(int cliente)
        {
            var lista = new List<DocumentoClienteModel>();
            string query = "";
            return lista;
        }
    }
}
