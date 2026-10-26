using Plataforma_CG.Controllers.Operaciones.Inyeccion;
using Plataforma_CG.Models.Operaciones.Pieles;
using System.Data;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Operaciones.Pieles
{
    public class AccesoPieles
    {
        private string _cadena = new Conexion().GetCadenaSQLSIGO();
        private string _cadenatif = new Conexion().GetCadenaSQLTIFVentas();
        SqlConnection _conn;

        public List<LotePielesModel> ConsultarLotes()
        {
            var lista = new List<LotePielesModel>();
            _conn = new SqlConnection(_cadenatif);
            string query = $@"select b.LoteId,b.Nombre as 'Lote',b.FechaProduccion from Produccion a 
inner join Lote b on b.LoteId=a.LoteId
where a.CodigoEtiqueta like '%PIET%' and a.Estatus=1 
group by b.LoteId,b.Nombre,b.FechaProduccion order by FechaProduccion asc;";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new LotePielesModel
                        {
                            LoteId = Convert.ToInt64(dr["LoteId"]),
                            Lote = Convert.ToString(dr["Lote"]),
                            FechaProduccion = Convert.ToString(dr["FechaProduccion"])
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
        public List<PielesModel> ConsultarPieles()
        {
            var lista = new List<PielesModel>();
            string query = $@"select b.LoteId,b.Nombre as 'Lote',a.ProduccionId,a.CodigoEtiqueta,a.PesoNeto,a.FechaProduccion  from Produccion a 
inner join Lote b on b.LoteId=a.LoteId
where a.CodigoEtiqueta like '%PIET%' and a.Estatus=1
order by a.FechaProduccion;";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {

                    }
                }
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return lista;
        }
        public List<PielesModel> ConsultarPielesPorLote(long loteid)
        {
            var lista = new List<PielesModel>();
            _conn = new SqlConnection(_cadenatif);
            string query = $@"select b.LoteId,b.Nombre as 'Lote',a.ProduccionId,a.CodigoEtiqueta,a.PesoNeto,a.FechaProduccion  from Produccion a 
inner join Lote b on b.LoteId=a.LoteId
where a.LoteId={loteid} and a.Estatus=1 
order by a.FechaProduccion";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new PielesModel
                        {
                            LoteId = Convert.ToInt64(dr["LoteId"]),
                            Lote = Convert.ToString(dr["Lote"]),
                            ProduccionId = Convert.ToInt64(dr["ProduccionId"]),
                            CodigoEtiqueta = Convert.ToString(dr["CodigoEtiqueta"]),
                            PesoNeto = Convert.ToDouble(dr["PesoNeto"]),
                            FechaProduccion = Convert.ToString(dr["FechaProduccion"])
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
        public List<OrdenesPielesModel> ConsultarOrdenesPieles()
        {
            var lista = new List<OrdenesPielesModel>();
            _conn = new SqlConnection(_cadenatif);
            string query = $@"SELECT
CONVERT(date,ss.FechaSurtido) FechaSurtido,
ISNULL(SUM(sdl.Unidades),0) Capturadas,
RIGHT(srf.Referencia,9) Folio,
src.Referencia Cliente,
ss.SolicitudSurtidoId SolicitudSurtidoId,
SUM(ssd.Cantidad) Unidades
FROM
TIF_meat.dbo.SolicitudSurtido ss       
inner join TIF_meat.dbo.SolicitudSurtidoDetalle ssd on ssd.SolicitudSurtidoId = ss.SolicitudSurtidoId     
left join TIF_meat.dbo.SurtidoReferencia srC on srC.SolicitudSurtidoId = ss.SolicitudSurtidoId and srC.TipoReferenciaId = 6     
left join TIF_meat.dbo.SurtidoReferencia srF on srF.SolicitudSurtidoId = ss.SolicitudSurtidoId and srF.TipoReferenciaId = 9     
left join TIF_meat.dbo.SurtidoReferencia srD on srD.SolicitudSurtidoId = ss.SolicitudSurtidoId and srD.TipoReferenciaId = 12     
left join (SELECT SolicitudSurtidoId,Articulo,Almacen,SUM(Unidades) as Unidades FROM TIF_meat.dbo.SurtidoDetalleLote GROUP BY SolicitudSurtidoId,Articulo,Almacen) as  sdl on sdl.SolicitudSurtidoId = ss.SolicitudSurtidoId and sdl.Articulo = ssd.Articulo and sdl.Almacen = ssd.Almacen
where ss.ProcesoId = 37       AND (srD.Referencia IS NULL OR srD.Referencia = '')       AND ss.EstatusId = 1
group by ss.SolicitudSurtidoId,     srf.Referencia,     src.Referencia,      CONVERT(date,ss.FechaSurtido)
order by ss.SolicitudSurtidoId desc";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr= cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new OrdenesPielesModel
                        {
                            FechaSurtido = dr["FechaSurtido"].ToString(),
                            Capturadas = Convert.ToInt32(dr["Capturadas"]),
                            Folio = Convert.ToString(dr["Folio"]),
                            Cliente = Convert.ToString(dr["Cliente"]),
                            SolicitudSurtidoId = Convert.ToInt64(dr["SolicitudSurtidoId"]),
                            Unidades = Convert.ToInt32(dr["Unidades"])
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
        public string InsertarPielesPedido(GuardarPielesSurtidoModel model,string usuario)
        {
            string resultado = "";
            var solsurt= model.SolicitudSurtidoId;
            var lista = new List<PielesModel>();
            bool res = false;
            _conn = new SqlConnection(_cadenatif);
            foreach (var item in model.Pieles)
            {
                try
                {
                    string query = $"insert into SalidaEmbarque (SolicitudSurtidoId,ProduccionId,Tarima,FechaHora) values " +
                        $"(" +
                        $"{solsurt}, " +
                        $"{item.ProduccionId}, " +
                        $"'{item.Lote}', " +
                        $"GETDATE()" +
                        $");";
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
                }
                catch (Exception)
                {
                }
                if (res)
                {
                    string query2 = $@"update Produccion set Estatus=1, UltimoProcesoId=37 where ProduccionId={item.ProduccionId}";
                    SqlCommand cmd = new SqlCommand(query2, _conn);
                    _conn.Open();
                    try
                    {
                        res = cmd.ExecuteNonQuery() > 0;
                    }
                    catch (Exception)
                    {
                    }
                    _conn.Close();
                }
            }
            if (res)
            {
                string query2 = $@"insert into SurtidoDetalleLote select a.SolicitudSurtidoId,b.LoteId,c.Articulo,'TIFPIE', COUNT(a.ProduccionId),COUNT(a.ProduccionId),GETDATE(),GETDATE() from SalidaEmbarque a
inner join Lote b on b.Nombre=a.Tarima 
inner join Produccion c on c.ProduccionId= a.ProduccionId 
where a.SolicitudSurtidoId={solsurt} group by a.SolicitudSurtidoId,b.LoteId,c.Articulo;";
                SqlCommand cmd = new SqlCommand(query2, _conn);
                _conn.Open();
                try
                {
                    res = cmd.ExecuteNonQuery() > 0;
                }
                catch (Exception)
                {
                }

                _conn.Close();


                try
                {
                    string query3 = "EXEC [meat_SalidaEmbarquePiel] @Tipo, @SolicitudSurtido";

                    using (SqlCommand cmd2 = new SqlCommand(query3, _conn))
                    {
                        cmd2.Parameters.AddWithValue("@Tipo", 37);
                        cmd2.Parameters.AddWithValue("@SolicitudSurtido", solsurt);

                        if (_conn.State != ConnectionState.Open)
                            _conn.Open();

                        object valor = cmd2.ExecuteScalar();

                        if (valor != null && valor != DBNull.Value)
                        {
                            resultado = valor.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Manejar el error
                    resultado = "";
                }
                finally
                {
                    if (_conn.State == ConnectionState.Open)
                        _conn.Close();
                }
                string query4 = $@"Insert into RegistroPieles values({solsurt},'{resultado}','{usuario}',GETDATE())";
                _conn.Open();
                cmd = new SqlCommand(query4,_conn);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception)
                {
                }
                _conn.Close();
            }

            return resultado;   
        }
        public List<PielesRegistroModel> ConsultarPielesRegistro()
        {
            var lista = new List<PielesRegistroModel>();
            _conn = new SqlConnection(_cadenatif);
            string query = $@"select * from RegistroPieles";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new PielesRegistroModel
                        {
                            SurtidoId= Convert.ToInt32(dr["SurtidoId"]),
                            ReferenciaSalida = dr["ReferenciaSalida"].ToString(),
                            Usuario = dr["Usuario"].ToString(),
                            FechaHora= dr["FechaHora"].ToString()
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
    }
}
