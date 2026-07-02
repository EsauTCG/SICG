using Plataforma_CG.Models.Operaciones.Planeacion.Extra;
using System.Data.SqlClient;
using System.Drawing;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;

namespace Plataforma_CG.AccesoDatos.Operaciones.Planeacion
{
    public class AccesoPlanExtra
    {
        private string _cadena = new Conexion().GetCadenaSQLSIGO();
        private string _cadenatif = new Conexion().GetCadenaSQLTIFVentas();
        SqlConnection _conn;
        public object ConsultarEstatusSolicitud()
        {
            _conn = new SqlConnection(_cadenatif);
            string productocodigo = "", nombre = "", porcentaje = "";
            string query = $@"select * from TIF_Meat.dbo.Estatus";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            var lista = new List<object>();
            using (var dr = cmd.ExecuteReader())
            {
                while (dr.Read())
                {
                    lista.Add(new
                    {
                        estatusId =
           Convert.ToInt32(dr["EstatusId"]),
                        nombre =
           dr["Nombre"]?.ToString() ?? ""


           
                    });

                }
            }
            _conn.Close();
            return lista;
        }
        public PlanInyModel CalcularPlan(string sku)
        {
            _conn = new SqlConnection(_cadenatif);
            var plan = new PlanInyModel();
            string query = $@"
select a.ArticuloId as 'SKU', 
a.Nombre, 
ISNULL(b.Porcentaje,0) as 'Porcentaje' 
from TIF_CommerciaNet.dbo.Articulo a 
left join TIF_Inyeccion.dbo.Recetas b on b.SKU=a.ARticuloId
where a.ArticuloId='{sku}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            using (var dr = cmd.ExecuteReader())
            {
                while (dr.Read())
                {
                    plan.SKU = dr["SKU"].ToString();
                    plan.Nombre = dr["Nombre"].ToString();
                    plan.Porcentaje = Convert.ToInt32(dr["Porcentaje"]);
                    plan.Tipo = "Natural";
                    if (plan.Porcentaje > 0)
                    {
                        plan.Tipo = "Mejorado";
                    }

                }
            }
            _conn.Close();


            return plan;
        }
        public PlanEtiModel ConsultarEtiquetas(string sku)
        {
            _conn = new SqlConnection(_cadenatif);
            var plan = new PlanEtiModel();
            string query = $@"select b.ArticuloId,b.Nombre as 'Producto', b.Etiquetacion, a.Nombre,A.INTERFACE AS DiasCaducidad from TIF_CommerciaNet.dbo.Colector a 
inner join TIF_CommerciaNet.dbo.Articulo b on b.Etiquetacion = a.ColectorId 
where a.sistemaid = 'ETI' and 
b.ArticuloId='{sku}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            using (var dr = cmd.ExecuteReader())
            {
                while (dr.Read())
                {
                    //plan.Etiquetacion = dr["SKU"].ToString();
                    plan.Nombre = dr["Nombre"].ToString();
                    plan.Etiquetacion = Convert.ToInt32(dr["Etiquetacion"]);
                    plan.DiasCaducidad = Convert.ToString(dr["DiasCaducidad"]);

                }
            }
            _conn.Close();


            return plan;
        }
        public object ObtenerProductosExtra()
        {
            _conn = new SqlConnection(_cadena);
            string productocodigo = "", nombre = "", porcentaje = "";
            string query = $@"select a.ProductoCodigo,b.ProductoNombre,a.Porcentaje from Participacion a
inner join ArticuloSap b on b.ProductoCodigo=a.ProductoCodigo
group by a.ProductoCodigo,b.ProductoNombre,a.Porcentaje";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            var lista = new List<object>();
            using (var dr = cmd.ExecuteReader())
            {
                while (dr.Read())
                {
                    lista.Add(new
                    {
                        ProductoCodigo =
           dr["ProductoCodigo"]?.ToString() ?? "",

                        Nombre =
           dr["ProductoNombre"]?.ToString() ?? "",

                        Porcentaje =
           Convert.ToDecimal(
               dr["Porcentaje"] ?? 0
           )
                    });

                }
            }
            _conn.Close();
            return lista;
        }
        public object ObtenerCatalogoConversion(string sku)
        {
            _conn = new SqlConnection(_cadena);
            string productocodigo = "", nombre = "", porcentaje = "";
            string query = $@"select a.SkuOrigen,a.SkuDestino,b.ProductoNombre from SkuConversion a 
inner join ArticuloSap b on b.ProductoCodigo=a.SkuDestino and 
SkuOrigen='{sku}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            var lista = new List<object>();
            using (var dr = cmd.ExecuteReader())
            {
                while (dr.Read())
                {
                    lista.Add(new
                    {
                        SkuOrigen = Convert.ToString(dr["SkuOrigen"]),
                        SkuDestino = Convert.ToString(dr["SkuDestino"]),
                        ProductoNombre= Convert.ToString(dr["ProductoNombre"])
                    });

                }
            }
            _conn.Close();
            return lista;
        }
        public object ObtenerSolicitudes(string fecha)
        {
            _conn = new SqlConnection(_cadena);
            string query = $@"select c.ProveedorId,c.Nombre,a.SolicitudProduccionId from MEAT_TIF.TIF_Meat.dbo.SolicitudProduccion a
inner join MEAT_TIF.TIF_Meat.dbo.SolicitudReferencia b on b.SolicitudProduccionId=a.SolicitudProduccionId
inner join MEAT_TIF.TIF_CommerciaNET.dbo.Proveedor c on c.ProveedorId= b.Referencia
inner join Clasificacion d on d.Plantilla=c.ProveedorId
where CONVERT(date,a.FechaProduccion) = CONVERT(date,'{fecha}')";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            var lista = new List<object>();
            using (var dr = cmd.ExecuteReader())
            {
                while (dr.Read())
                {
                    lista.Add(new
                    {
                        id = Convert.ToString(dr["ProveedorId"]),
                        nombre = Convert.ToString(dr["Nombre"]),
                        solicitudId = Convert.ToString(dr["SolicitudProduccionId"])
                    });
                }
            }
            _conn.Close();
            return lista;
        }
        public object ObtenerDetalleSolicitud(string solicitudid)
        {
            _conn = new SqlConnection(_cadenatif);

            var productos = new List<object>();

            string tipoNombre = "";
            string estatus = "";
            string estatusId = "";
            string comentarios = "";
            string fecha = "";

            _conn.Open();

            try
            {
                // Encabezado
                string queryHeader = @"
            SELECT TOP 1
            sp.SolicitudProduccionId,
            sr.Referencia as Comentarios,
			tsp.TipoSolicitudProduccionId,
            es.EstatusId,
            es.Nombre AS Estatus,
            sp.FechaProduccion
        FROM TIF_MEAT.dbo.SolicitudProduccion sp
        LEFT JOIN TIF_MEAT.dbo.TipoSolicitudProduccion tsp
            ON tsp.TipoSolicitudProduccionId = sp.TipoSolicitudProduccionId
        LEFT JOIN TIF_MEAT.dbo.Estatus es
            ON es.EstatusId = sp.EstatusId
			inner join TIF_Meat.dbo.SolicitudReferencia sr on sr.SolicitudProduccionId=sp.SolicitudProduccionId
        WHERE sp.SolicitudProduccionId = @Id and sr.TipoReferenciaId=15";

                using (var cmd = new SqlCommand(queryHeader, _conn))
                {
                    cmd.Parameters.AddWithValue("@Id", solicitudid);

                    using (var dr = cmd.ExecuteReader())
                    {
                        if (dr.Read())
                        {
                            tipoNombre = Convert.ToString(dr["TipoSolicitudProduccionId"]);
                            estatusId = Convert.ToString(dr["EstatusId"]);
                            estatus = Convert.ToString(dr["Estatus"]);
                            comentarios = Convert.ToString(dr["Comentarios"]);
                            fecha = Convert.ToDateTime(dr["FechaProduccion"])
                                .ToString("yyyy-MM-dd");
                        }
                    }
                }

                // Detalle
                string queryDetalle = @"
        SELECT
            SolicitudProduccionId,
            Articulo,
            Cantidad
        FROM TIF_MEAT.dbo.SolicitudProduccionDetalle
        WHERE SolicitudProduccionId = @Id";

                using (var cmd = new SqlCommand(queryDetalle, _conn))
                {
                    cmd.Parameters.AddWithValue("@Id", solicitudid);

                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            productos.Add(new
                            {
                                articulo = Convert.ToString(dr["Articulo"]),
                                cantidad = Convert.ToDecimal(dr["Cantidad"])
                            });
                        }
                    }
                }
            }
            finally
            {
                _conn.Close();
            }

            return new
            {
                id = solicitudid,
                tipoNombre,
                estatusId,
                estatus,
                comentarios,
                fecha,
                productos
            };
        }
        //        public object ObtenerDetalleSolicitud(string solicitudid)
        //        {
        //            _conn = new SqlConnection(_cadena);
        //            string query = $@"select a.SolicitudProduccionId,a.Articulo,a.Cantidad,a.FechaHora from MEAT_TIF.TIF_MEAT.dbo.SolicitudProduccionDetalle a
        //where SolicitudProduccionId={solicitudid}";
        //            SqlCommand cmd = new SqlCommand(query, _conn);
        //            _conn.Open();
        //            var lista = new List<object>();
        //            using (var dr = cmd.ExecuteReader())
        //            {
        //                while (dr.Read())
        //                {
        //                    lista.Add(new
        //                    {
        //                        solicitudId = Convert.ToString(dr["SolicitudProduccionId"]),
        //                        articulo = Convert.ToString(dr["Articulo"]),
        //                        cantidad = Convert.ToString(dr["Cantidad"])
        //                    });
        //                }
        //            }
        //            _conn.Close();
        //            return lista;
        //        }
        public object ObtenerTipoSolicitud()
        {
            _conn = new SqlConnection(_cadena);
            string query = $"select a.Plantilla,b.Nombre from Clasificacion a inner join Meat_tif.tif_CommerciaNet.dbo.Proveedor b on b.ProveedorId=a.Plantilla";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            var lista = new List<object>();
            using (var dr = cmd.ExecuteReader())
            {
                while (dr.Read())
                {
                    lista.Add(new
                    {
                        id = Convert.ToString(dr["Plantilla"]),
                        nombre = Convert.ToString(dr["Nombre"])
                    });
                }
            }
            _conn.Close();
            return lista;
        }
        public object ObtenerSolicitudSKUs()
        {
            _conn = new SqlConnection(_cadena);
            string query = "select ArticuloId,Nombre from MEAT_TIF.TIF_CommerciaNet.dbo.Articulo where ArticuloId like 'RY%'";
            SqlCommand cmd = new SqlCommand(query,_conn);
            var list = new List<object>();
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        list.Add(new
                        {
                            sku = Convert.ToString(dr["ArticuloId"]),
                            nombre = Convert.ToString(dr["Nombre"])
                        });
                    }
                }
            }
            catch (Exception)
            {

            }
            _conn.Close();
            return list;
        }
        public string CrearSolicitud(string fecha)
        {
            string res = "";

            _conn = new SqlConnection(_cadenatif);

            fecha = fecha.Replace("-", "");

            string query =
                @"INSERT INTO TIF_Meat.dbo.SolicitudProduccion
        (
            [Articulo],
            [Cantidad],
            [ProcesoId],
            [EstatusId],
            [FechaProduccion],
            [FechaProgramada],
            [FechaHora],
            [FechaHoraServer],
            [TipoSolicitudProduccionId]
        )

        OUTPUT INSERTED.SolicitudProduccionId

        VALUES
        (
            '',
            0,
            8,
            1,
            CONVERT(DATETIME,@Fecha),
            GETDATE(),
            GETDATE(),
            NULL,
            2
        )";
            
            SqlCommand cmd =
                new SqlCommand(query, _conn);

            cmd.Parameters.AddWithValue(
                "@Fecha",
                fecha
            );

            _conn.Open(); 

            try
            {
                res =
                    cmd.ExecuteScalar()
                    ?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                res = ex.Message;
            }

            _conn.Close();

            return res;
        }
        public int InsertarSolicitud(string SolicitudId,
            string tipoId,
    string referencia
    
)
        {
            _conn = new SqlConnection(_cadena);

            int id = 0;

            string query = @"
    INSERT INTO MEAT_TIF.TIF_Meat.dbo.SolicitudReferencia
    (
        SolicitudProduccionId,
        TipoReferenciaId,
        Referencia,
        FechaHora
    )
    VALUES
    (
        @SolicitudId,
        @TipoId,
        @TipoNombre,
        GETDATE()
    )";

            SqlCommand cmd =
                new SqlCommand(query, _conn);

            cmd.Parameters.AddWithValue(
                "@SolicitudId",
                SolicitudId
            );
            cmd.Parameters.AddWithValue(
    "@TipoId",
    tipoId
);
            cmd.Parameters.AddWithValue(
    "@TipoNombre",
    referencia
);
            _conn.Open();
            cmd.ExecuteNonQuery();
            _conn.Close();

            return id;
        }

        public void InsertarSolicitudDetalle(
            string solicitudId,
            string sku,
            decimal cantidad
        )
        {
            _conn = new SqlConnection(
                _cadena
            );

            string query = @"
    INSERT INTO MEAT_TIF.TIF_Meat.dbo.SolicitudProduccionDetalle
    (
        SolicitudProduccionId,
        Articulo,
        Cantidad,
FechaHora
    )
    VALUES
    (
        @SolicitudId,
        @SKU,
        @Cantidad,
GETDATE()
    )";

            SqlCommand cmd =
                new SqlCommand(query, _conn);

            cmd.Parameters.AddWithValue(
                "@SolicitudId",
                solicitudId
            );

            cmd.Parameters.AddWithValue(
                "@SKU",
                sku
            );

            cmd.Parameters.AddWithValue(
                "@Cantidad",
                cantidad
            );

            _conn.Open();

            cmd.ExecuteNonQuery();

            _conn.Close();
        }
    }
}


