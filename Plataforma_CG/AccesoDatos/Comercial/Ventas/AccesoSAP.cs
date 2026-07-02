using Plataforma_CG.Models.Comercial.Ventas;
using System.Data;
using System.Data.Odbc;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Ventas
{
    public class AccesoSAP
    {
        SqlConnection _conn = new SqlConnection();
        /*public DataSet TablasSap()
        {
            var lista = new DataSet();

            using (var conn = new OdbcConnection(new Conexion().GetCadenaODBC()))
            {
                conn.Open();

                var sql = @"SELECT * FROM ""PROD_CARNESG"".""OCRD""";


                using (var cmd = new OdbcCommand(sql, conn))
                {
                    using (var sad = new OdbcDataAdapter(cmd))
                    {
                        sad.Fill(lista);
                    }
                }
            }
            return lista;
        }*/
        public List<ClienteModel> ClientesSap()
        {
            var lista = new List<ClienteModel>();

            using (var conn = new OdbcConnection(new Conexion().GetCadenaODBC()))
            {
                conn.Open();

                //var sql = @"SELECT ""CardName"" FROM ""PROD_CARNESG"".""OCRD""";
                var sql = @" SELECT 
                    ""CardCode"",
                    ""CardName"",
                    (""Address"" || ' ' || ""ZipCode"") AS ""Direccion"",
                    CASE WHEN ""CreditLine"" > 0 THEN 'CREDITO' ELSE 'CONTADO' END AS ""Concepto"",
                    ""Phone1"",
                    ""LicTradNum"",
                    ""CardFName"",  -- si no lo usas, puedes ignorarlo en el mapeo
                    CASE 
                    WHEN ""U_MT_Clasificacion"" IS NULL OR ""U_MT_Clasificacion"" = '' THEN 'NA' 
                    ELSE ""U_MT_Clasificacion"" 
                    END AS ""Clasificacion"",
                    CASE WHEN ""validFor"" = 'Y' THEN 1 ELSE 0 END AS ""Estatus""
                    FROM ""PROD_CARNESG"".""OCRD"" 
                    WHERE ""CardType"" = 'C'";


                using (var cmd = new OdbcCommand(sql, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new ClienteModel
                            {
                                CodigoSap = reader["CardCode"]?.ToString(),
                                RazonSocial = reader["CardName"]?.ToString(),
                                Direccion = reader["Direccion"]?.ToString(),
                                Concepto = reader["Concepto"]?.ToString(),
                                Telefono = reader["Phone1"]?.ToString(),
                                RFC = reader["LicTradNum"]?.ToString(),
                                Apellido = reader["CardFName"] as string ?? string.Empty, // si decides omitirlo, deja ""
                                Clasificacion = reader["Clasificacion"]?.ToString(),
                                Estatus = reader["Estatus"] != DBNull.Value ? Convert.ToInt32(reader["Estatus"]) : 0,
                                


                            });
                        }
                    }
                }
            }
            return lista;
        }


    }
}
