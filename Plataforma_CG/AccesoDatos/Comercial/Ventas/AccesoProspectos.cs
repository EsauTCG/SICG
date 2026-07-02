using Elfie.Serialization;
using Plataforma_CG.Models;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Ventas
{
    public class AccesoProspectos
    {
        SqlConnection conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public List<ProspectoModel> Listar()
        {
            var lista = new List<ProspectoModel>();
            string query = "select * from prospectos";
            SqlCommand cmd = new SqlCommand(query, conn);
            conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new ProspectoModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            NombreComercial = Convert.ToString(dr["NombreComercial"]),
                            PersonaAtendio = Convert.ToString(dr["PersonaAtendio"]),
                            PerfilPersonal = Convert.ToString(dr["PerfilPersonal"]),
                            Ubicacion = Convert.ToString(dr["Ubicacion"]),
                            TipoCanal = Convert.ToString(dr["TipoCanal"]),
                            TipoProducto = Convert.ToString(dr["TipoProducto"]),
                            RutaFotoFachada = Convert.ToString(dr["RutaFotoFachada"]),
                            Usuario = Convert.ToString(dr["Usuario"]),
                            ListaPrecios = Convert.ToString(dr["ListaPrecios"]),
                            TopTenPrecios = Convert.ToString(dr["TopTenPrecios"]),
                            PrecioBajoLista = Convert.ToInt32(dr["PrecioBajoLista"]),
                            Credito = Convert.ToInt32(dr["Credito"]),
                            MetodoPago = Convert.ToString(dr["MetodoPago"]),
                            OtrasMarcas = Convert.ToString(dr["OtrasMarcas"]),
                            FacilitaRebanado = Convert.ToInt32(dr["FacilitaRebanado"]),
                            VolumenCompra = Convert.ToString(dr["VolumenCompra"]),
                            PeriodicidadCompra = Convert.ToString(dr["PeriodicidadCompra"]),
                            AudioPath = Convert.ToString(dr["AudioPath"]),
                            CantidadVisitas = Convert.ToInt32(dr["CantidadVisitas"]),
                            NumeroTiendas = Convert.ToInt32(dr["NumeroTiendas"]),
                            FechaHora = Convert.ToDateTime(dr["FechaHora"]),
                            UltimaVisita = Convert.ToDateTime(dr["UltimaVisita"]),
                        });
                    }
                }
            }
            catch (Exception)
            {
            }
            conn.Close();
            return lista;
        }
        public ProspectoModel ConsultarId(int id)
        {
            return Listar().Where(item => item.Id == id).FirstOrDefault();
        }
        public bool Insertar(ProspectoModel model)
        {
            bool res = false;
            string query = $"insert into Prospectos (NombreComercial,PersonaAtendio,PerfilPersonal,Ubicacion,TipoCanal,TipoProducto,RutaFotoFachada,Usuario,ListaPrecios,TopTenPrecios,PrecioBajoLista,Credito,MetodoPago,OtrasMarcas,FacilitaRebanado,VolumenCompra,PeriodicidadCompra,AudioPath,CantidadVisitas,NumeroTiendas,FechaHora,UltimaVisita)" +
                $"values " +
                $"('{model.NombreComercial}', " +
                $"'{model.PersonaAtendio}', " +
                $"'{model.PerfilPersonal}', " +
                $"'{model.Ubicacion}', " +
                $"'{model.TipoCanal}', " +
                $"'{model.TipoProducto}', " +
                $"'{model.RutaFotoFachada}', " +
                $"'{model.Usuario}', " +
                $"'{model.ListaPrecios}', " +
                $"'{model.TopTenPrecios}', " +
                $"{model.PrecioBajoLista}, " +
                $"{model.Credito}, " +
                $"'{model.MetodoPago}', " +
                $"'{model.OtrasMarcas}', " +
                $"{model.FacilitaRebanado}, " +
                $"'{model.VolumenCompra}', " +
                $"'{model.PeriodicidadCompra}', " +
                $"'{model.AudioPath}', " +
                $"{model.CantidadVisitas}, " +
                $"{model.NumeroTiendas}, " +
                $"GETDATE(), " +
                $"CONVERT(DATE,GETDATE()))";
            SqlCommand cmd = new SqlCommand(query, conn);
            conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception)
            {
            }
            conn.Close();
            return res;
        }
        public bool Modificar(ProspectoModel model)
        {
            bool res = false;
            string query = $"Update Prospectos " +
                $"set NombreComercial ='{model.NombreComercial}', " +
                $"PersonaAtendio='{model.PersonaAtendio}', " +
                $"PerfilPersonal='{model.PerfilPersonal}', " +
                $"Ubicacion='{model.Ubicacion}', " +
                $"TipoCanal='{model.TipoCanal}', " +
                $"TipoProducto='{model.TipoProducto}', " +
                $"RutaFotoFachada='{model.RutaFotoFachada}', " +
                //$", Usuario='{model.Usuario}', " +
                $"ListaPrecios='{model.ListaPrecios}', " +
                //$"TopTenPrecios='{model.TopTenPrecios}', " +
                //$"PrecioBajoLista={model.PrecioBajoLista}, " +
                //$"Credito={model.Credito}, " +
                //$"MetodoPago='{model.MetodoPago}', " +
                //$"OtrasMarcas='{model.OtrasMarcas}', " +
                //$"FacilitaRebanado={model.FacilitaRebanado}, " +
                //$"VolumenCompra='{model.VolumenCompra}', " +
                //$"PeriodicidadCompra='{model.PeriodicidadCompra}', " +
                $"AudioPath='{model.AudioPath}' " +
                //$"CantidadVisitas={model.CantidadVisitas}, " +
                //$"NumeroTiendas={model.NumeroTiendas}, " +
                //$"UltimaVisita='{Convert.ToDateTime(model.UltimaVisita).ToString("yyyy-MM-dd")}' " +
                $"where Id={model.Id}";
            SqlCommand cmd = new SqlCommand(query, conn);
            conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception)
            {
            }
            conn.Close();
            return res;
        }
    }
}
