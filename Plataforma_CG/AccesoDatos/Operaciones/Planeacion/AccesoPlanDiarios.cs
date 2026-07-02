using AspNetCoreGeneratedDocument;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using Microsoft.Data.SqlClient;
using Plataforma_CG.Models.Operaciones.Planeacion.Diaria;
using Plataforma_CG.Models.Operaciones.Planeacion.Semanal;
using System.Data;

namespace Plataforma_CG.AccesoDatos.Operaciones.Planeacion
{
    public class AccesoPlanDiarios
    {
        private string _cadena = new Conexion().GetCadenaSQLSIGO();
        private string _cadenap1 = new Conexion().GetCadenaSQLP1();
        SqlConnection _conn;
        public PlaneacionProduccionModel ConsultarPlan(string fecha, string tipo)
        {
            _conn = new SqlConnection(_cadena);
            var dato = new PlaneacionProduccionModel();
            string query = $"select * from PlaneacionProduccion where FechaPlan='{fecha}' and TipoPlan = '{tipo.ToUpper()}'";
            var cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        dato.PlaneacionId = Convert.ToInt32(dr["PlaneacionId"]);
                        dato.FechaPlan = Convert.ToString(dr["FechaPlan"]);
                        dato.TipoPlan = Convert.ToString(dr["TipoPlan"]);
                        dato.Estatus = Convert.ToString(dr["Estatus"]);
                        dato.Version = Convert.ToInt32(dr["Version"]);
                        dato.Notas = Convert.ToString(dr["Notas"]);
                        dato.ProgramacionId = Convert.ToInt32(dr["ProgramacionId"]);
                        dato.NombreProgramacion = Convert.ToString(dr["NombreProgramacion"]);
                        dato.CreadoPor = Convert.ToString(dr["CreadoPor"]);
                        dato.FechaCreacion = Convert.ToString(dr["FechaCreacion"]);
                        dato.FechaActualizacion = Convert.ToString(dr["FechaActualizacion"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            _conn.Close();
            return dato;
        }
        public int ConsultarSubId(string nom)
        {
            int res = 0;
            _conn = new SqlConnection(_cadena);
            string query = $"select Id from SubClasif where Nombre='{nom}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public double ConsultarPesoProm(string nom)
        {
            double res = 0.0;
            _conn = new SqlConnection(_cadena);
            string query = $"select PesoPromedio from PlanProduccion where fk_Clasificacion='{nom}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = Convert.ToDouble(cmd.ExecuteScalar());
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public int InsertarPlan(PlaneacionProduccionModel model)
        {
            _conn = new SqlConnection(_cadena);
            var res = 0;
            string query = $"insert into PlaneacionProduccion (FechaPlan,TipoPlan,Notas,CreadoPor) values('{model.FechaPlan}','{model.TipoPlan}','{model.Notas}','{model.CreadoPor}'); select SCOPE_IDENTITY();";
            var cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                res = Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public bool InsertarCanCero(PlaneacionProduccionModel model)
        {
            _conn = new SqlConnection(_cadena);
            var res = false;
            string query = $"insert into CanalPlaneacion select {model.PlaneacionId}, a.Id,0,a.PesoPromedio from SubClasif a where a.fk_Clasificacion='{model.TipoPlan}'";
            var cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery()>0;
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public List<CanalPlaneacionModel> ListarCanales(int planid)
        {
            _conn = new SqlConnection(_cadena);
            var lista = new List<CanalPlaneacionModel>();
            string query = $"select * from CanalPlaneacion where PlaneacionId={planid}";
            var cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new CanalPlaneacionModel
                        {
                            PlaneacionId = Convert.ToInt32(dr["PlaneacionId"]),
                            fk_SubClas = Convert.ToInt32(dr["fk_SubClas"]),
                            Nombre=NombreCanal(Convert.ToInt32(dr["fk_SubClas"])),
                            NoCanalCompleta = Convert.ToInt32(dr["NoCanalCompleta"]),
                            KgCanalCompleta = Convert.ToDouble(dr["KgCanalCompleta"])
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
        public int IdCanal(string sku)
        {
            int res = 0;
            _conn = new SqlConnection(_cadena);
            string query = $"select Id from Clasificacion where Nombre='{sku}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception)
            {

            }
            _conn.Close();
            return res;
        }
        public string NombreCanal(int fksub)
        {
            _conn = new SqlConnection(_cadena);
            var res = "";
            string query = $"select Nombre from SubClasif where Id={fksub}";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = Convert.ToString(cmd.ExecuteScalar());
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public bool InsertarCanales(CanalPlaneacionModel model)
        {
            var res = false;
            _conn = new SqlConnection(_cadena);
            string query = @"
IF EXISTS (
    SELECT 1 
    FROM CanalPlaneacion 
    WHERE PlaneacionId = @PlaneacionId 
    AND fk_SubClas = @fk_SubClas
)
BEGIN
    UPDATE CanalPlaneacion
    SET NoCanalCompleta = @NoCanalCompleta,
        KgCanalCompleta = @KgCanalCompleta
    WHERE PlaneacionId = @PlaneacionId
    AND fk_SubClas = @fk_SubClas
END
ELSE
BEGIN
    INSERT INTO CanalPlaneacion
    (PlaneacionId, fk_SubClas, NoCanalCompleta, KgCanalCompleta)
    VALUES
    (@PlaneacionId, @fk_SubClas, @NoCanalCompleta, @KgCanalCompleta)
END";

            var cmd = new SqlCommand(query,_conn);
            cmd.Parameters.AddWithValue("@PlaneacionId", model.PlaneacionId);
cmd.Parameters.AddWithValue("@fk_SubClas", model.fk_SubClas);
cmd.Parameters.AddWithValue("@NoCanalCompleta", model.NoCanalCompleta);
cmd.Parameters.AddWithValue("@KgCanalCompleta", model.KgCanalCompleta);
            _conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery()>0;
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public List<ParticipacionModel> ListarParticipacion(int clasid,int planid)
        {
            var lista = new List<ParticipacionModel>();
            _conn= new SqlConnection(_cadena);
            //            string query = @"
            //SELECT 
            //    p.Id,
            //    p.ProductoCodigo,
            //    p.fk_Clasificacion,
            //    p.Porcentaje,
            //    p.fk_SubClas,
            //    p.LineaCodigo,
            //    COALESCE(ps.PartSub, p.PartSub) AS PartSub
            //    ,m.Nombre as 'Master' 
            //FROM Participacion p
            //LEFT JOIN PlanSubClas ps
            //    ON ps.ProductoCodigo = p.ProductoCodigo
            //    AND ps.fk_SubClas = p.fk_SubClas
            //    AND ps.PlanId = @plid
            //left join MasterProd mp
            //	on mp.SKU=p.ProductoCodigo
            //	left join Masters m 
            //	on m.Id=mp.MasterID
            //WHERE p.fk_Clasificacion = @clasid
            //order by m.Id";
            string query = @"SELECT 
    p.Id,
    p.ProductoCodigo,

    ISNULL(
        pd.ProductoCodigoConvertido,
        p.ProductoCodigo
    ) AS ProductoCodigoConvertido,

    ISNULL(pd.PorcentajeInyeccion, 0) AS PorcentajeInyeccion,
    ISNULL(pd.KgInyeccion, 0) AS KgInyeccion,

    p.fk_Clasificacion,
    p.Porcentaje,
    p.fk_SubClas,

    ISNULL(p.LineaCodigo, '') AS LineaCodigo,

    COALESCE(ps.PartSub, p.PartSub) AS PartSub,

    m.Nombre AS Master,

    CAST(0 AS bit) AS EsExtra

FROM Participacion p

LEFT JOIN PlanSubClas ps
    ON ps.ProductoCodigo = p.ProductoCodigo
    AND ps.fk_SubClas = p.fk_SubClas
    AND ps.PlanId = @plid

LEFT JOIN PlanDiario pd
    ON pd.PlaneacionId = @plid
    AND pd.ProductoCodigo = p.ProductoCodigo

LEFT JOIN MasterProd mp
    ON mp.SKU = p.ProductoCodigo

LEFT JOIN Masters m
    ON m.Id = mp.MasterID

WHERE p.fk_Clasificacion = @clasid


UNION ALL


SELECT
    p.Id,
    p.ProductoCodigo,

    ISNULL(
        pd.ProductoCodigoConvertido,
        p.ProductoCodigo
    ) AS ProductoCodigoConvertido,

    ISNULL(pd.PorcentajeInyeccion, 0) AS PorcentajeInyeccion,
    ISNULL(pd.KgInyeccion, 0) AS KgInyeccion,

    p.fk_Clasificacion,
    p.Porcentaje,

    ps.fk_SubClas,

    '' AS LineaCodigo,

    ps.PartSub,

    'EXTRA' AS Master,

    CAST(1 AS bit) AS EsExtra

FROM PlanSubClas ps

INNER JOIN Participacion p
    ON p.ProductoCodigo = ps.ProductoCodigo

LEFT JOIN PlanDiario pd
    ON pd.PlaneacionId = ps.PlanId
    AND pd.ProductoCodigo = ps.ProductoCodigo

WHERE ps.PlanId = @plid
    AND p.fk_Clasificacion <> @clasid
    AND NOT EXISTS (
        SELECT 1
        FROM Participacion p2
        WHERE p2.ProductoCodigo = ps.ProductoCodigo
          AND p2.fk_Clasificacion = @clasid
    )


UNION ALL


SELECT
    pBase.Id,
    pBase.ProductoCodigo,

    ISNULL(
        pd.ProductoCodigoConvertido,
        pBase.ProductoCodigo
    ) AS ProductoCodigoConvertido,

    ISNULL(pd.PorcentajeInyeccion, 0) AS PorcentajeInyeccion,
    ISNULL(pd.KgInyeccion, 0) AS KgInyeccion,

    pBase.fk_Clasificacion,
    pBase.Porcentaje,

    ps.fk_SubClas,

    ISNULL(pBase.LineaCodigo, '') AS LineaCodigo,

    ps.PartSub,

    m.Nombre AS Master,

    CAST(0 AS bit) AS EsExtra

FROM PlanSubClas ps

INNER JOIN Participacion pBase
    ON pBase.ProductoCodigo = ps.ProductoCodigo
    AND pBase.fk_Clasificacion = @clasid

LEFT JOIN Participacion pMatch
    ON pMatch.ProductoCodigo = ps.ProductoCodigo
    AND pMatch.fk_SubClas = ps.fk_SubClas
    AND pMatch.fk_Clasificacion = @clasid

LEFT JOIN PlanDiario pd
    ON pd.PlaneacionId = @plid
    AND pd.ProductoCodigo = ps.ProductoCodigo

LEFT JOIN MasterProd mp
    ON mp.SKU = pBase.ProductoCodigo

LEFT JOIN Masters m
    ON m.Id = mp.MasterID

WHERE ps.PlanId = @plid
    AND pMatch.Id IS NULL


ORDER BY Master, ProductoCodigo;";
            SqlCommand cmd = new SqlCommand(query, _conn);
            cmd.Parameters.AddWithValue("@clasid", clasid);
            cmd.Parameters.AddWithValue("@plid", planid);
            
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista.Add(new ParticipacionModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            ProductoCodigo = Convert.ToString(dr["ProductoCodigo"]),
                            Nombre = NombreProducto(Convert.ToString(dr["ProductoCodigo"])),
                            fk_Clasificacion = Convert.ToInt32(dr["fk_Clasificacion"]),
                            Porcentaje = Convert.ToDouble(dr["Porcentaje"]),
                            fk_SubClas = Convert.ToInt32(dr["fk_SubClas"]),
                            LineaCodigo = Convert.ToString(dr["LineaCodigo"]),
                            PartSub = Convert.ToDouble(dr["PartSub"]),
                            Master = Convert.ToString(dr["Master"]),
                            EsExtra = Convert.ToBoolean(dr["EsExtra"]),
                            ProductoCodigoConvertido = Convert.ToString(dr["ProductoCodigoConvertido"]),
                            PorcentajeInyeccion = Convert.ToDecimal(dr["PorcentajeInyeccion"]),
                            KgInyeccion = Convert.ToDecimal(dr["KgInyeccion"])
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
        public List<ParticipacionModel> ListarParticipacionSem(int clasid, string fechaIn, string fechaFin)
        {
            var lista = new List<ParticipacionModel>();

            using (SqlConnection conn = new SqlConnection(_cadena))
            {
                //                string query = @"

                //SELECT 
                //    ds.ProductoCodigo,


                //    COALESCE(
                //        p.fk_Clasificacion,
                //        p2.fk_Clasificacion
                //    ) AS fk_Clasificacion,


                //    COALESCE(
                //        p.Porcentaje,
                //        p2.Porcentaje
                //    ) AS Porcentaje,

                //    ds.SubClas AS fk_SubClas,


                //    COALESCE(
                //        p.LineaCodigo,
                //        p2.LineaCodigo
                //    ) AS LineaCodigo,

                //    ds.PartSub

                //FROM DetalleSemanal ds

                //INNER JOIN SemanaClasificacion sc
                //    ON sc.FechaInicioSemana = @FechaInicio
                //    AND sc.FechaFinSemana = @FechaFin


                //LEFT JOIN Participacion p
                //    ON p.ProductoCodigo = ds.ProductoCodigo
                //    AND p.fk_SubClas = ds.SubClas


                //OUTER APPLY (
                //    SELECT TOP 1 *
                //    FROM Participacion px
                //    WHERE px.ProductoCodigo = ds.ProductoCodigo
                //) p2

                //WHERE COALESCE(p.fk_Clasificacion, p2.fk_Clasificacion) = @clasid

                //UNION


                //SELECT 
                //    p.ProductoCodigo,
                //    p.fk_Clasificacion,
                //    p.Porcentaje,
                //    p.fk_SubClas,
                //    p.LineaCodigo,
                //    p.PartSub

                //FROM Participacion p



                // WHERE p.fk_Clasificacion = @clasid
                //AND NOT EXISTS (
                //    SELECT 1
                //    FROM DetalleSemanal ds
                //    INNER JOIN SemanaClasificacion sc
                //        ON sc.FechaInicioSemana = @FechaInicio
                //        AND sc.FechaFinSemana = @FechaFin
                //    WHERE ds.ProductoCodigo = p.ProductoCodigo
                //);
                //";
                string query = @"
WITH DS AS (
    SELECT 
        ds.ProductoCodigo,
        ds.SubClas,
        MAX(ds.PartSub) AS PartSub
    FROM DetalleSemanal ds
    INNER JOIN SemanaClasificacion sc
        ON sc.FechaInicioSemana = @FechaInicio
        AND sc.FechaFinSemana = @FechaFin
    GROUP BY ds.ProductoCodigo, ds.SubClas
),

P AS (
    SELECT *
    FROM Participacion
    WHERE fk_Clasificacion = @clasid
)

SELECT 
    ds.ProductoCodigo,
    p.fk_Clasificacion,
    p.Porcentaje,
    ds.SubClas AS fk_SubClas,
    p.LineaCodigo,
    ds.PartSub
FROM DS ds
LEFT JOIN P p
    ON p.ProductoCodigo = ds.ProductoCodigo
    AND p.fk_SubClas = ds.SubClas

UNION ALL

SELECT 
    p.ProductoCodigo,
    p.fk_Clasificacion,
    p.Porcentaje,
    p.fk_SubClas,
    p.LineaCodigo,
    p.PartSub
FROM P p
WHERE NOT EXISTS (
    SELECT 1
    FROM DS ds
    WHERE ds.ProductoCodigo = p.ProductoCodigo
      AND ds.SubClas = p.fk_SubClas
)
order by ProductoCodigo
";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    // 🔹 Parámetros bien tipados
                    cmd.Parameters.Add("@clasid", SqlDbType.Int).Value = clasid;
                    cmd.Parameters.Add("@FechaInicio", SqlDbType.NVarChar).Value = fechaIn;
                    cmd.Parameters.Add("@FechaFin", SqlDbType.NVarChar).Value = fechaFin;

                    conn.Open();

                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            lista.Add(new ParticipacionModel
                            {
                                //Id = Convert.ToInt32(dr["Id"]),
                                ProductoCodigo = dr["ProductoCodigo"]?.ToString(),
                                Nombre = NombreProducto(dr["ProductoCodigo"]?.ToString()),
                                fk_Clasificacion = Convert.ToInt32(dr["fk_Clasificacion"]),
                                Porcentaje = Convert.ToDouble(dr["Porcentaje"]),
                                fk_SubClas = Convert.ToInt32(dr["fk_SubClas"]),
                                LineaCodigo = dr["LineaCodigo"]?.ToString(),

                                // 🔹 Manejo seguro de NULL
                                PartSub = dr["PartSub"] != DBNull.Value
                                            ? Convert.ToDouble(dr["PartSub"])
                                            : 0
                            });
                        }
                    }
                }
            }

            return lista;
        }
        public double SkuSemanal(string sku, string fechaIn, string fechaFin)
        {
                var res = 0.0;


            using (SqlConnection conn = new SqlConnection(_cadena))
            {
                string query = @"
select SUM(KgLote) as Suma from PlanDiario a
inner join PlaneacionProduccion b on b.PlaneacionId=a.PlaneacionId
where a.ProductoCodigo=@productocod  and b.FechaPlan between @FechaInicio and @FechaFin";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    // 🔹 Parámetros bien tipados
                    cmd.Parameters.Add("@productocod", SqlDbType.NVarChar).Value = sku;
                    cmd.Parameters.Add("@FechaInicio", SqlDbType.NVarChar).Value = fechaIn;
                    cmd.Parameters.Add("@FechaFin", SqlDbType.NVarChar).Value = fechaFin;

                    conn.Open();

                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            res = Convert.ToDouble(dr["Suma"]);
                        }
                    }
                }
            }

            return res;
        }
        public bool InsertarSemanal(PlanSemanalDetalle model)
        {
            _conn = new SqlConnection(_cadena);
            bool res = false;
            BorrarSemanal(model.FechaInicio,model.FechaFin,model.SubClas.ToString());
            string query = $"Insert into DetalleSemanal ([ProductoCodigo],[Porcentaje],[Peso],[FechaInicio],[FechaFin],[SubClas],[PartSub]) " +
                $"values " +
                $"('{model.ProductoCodigo}','{model.Porcentaje}','{model.Peso}','{model.FechaInicio}','{model.FechaFin}','{model.SubClas}','{model.PartSub}')";
            SqlCommand cmd = new SqlCommand(query,_conn);
            _conn.Open();
            try
            {
                res = cmd.ExecuteNonQuery()>0;
            }
            catch (Exception)
            {

                throw;
            }
            _conn.Close();
            return res;
        }
        public bool BorrarDetalleSemanal(PlanSemanalDetalle model)
        {
            _conn = new SqlConnection(_cadena);
            bool res = false;
            string query = $"delete from DetalleSemanal where FechaInicio='{model.FechaInicio}' and FechaFin='{model.FechaFin}' and SubClas='{model.SubClas}' and ProductoCodigo='{model.ProductoCodigo}'";
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
        public bool BorrarSemanal(string fechaIn, string fechaFin, string SubClas)
        {
            _conn= new SqlConnection(_cadena);
            bool res = false;
            string query = $"delete from SemanaClasificacion where FechaInicio='{fechaIn}' and FechaFin='{fechaFin}' and SubClas='{SubClas}'";
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
        public int ContarTotalesNP(string clasificacion, string fecha)
        {
            int res = 0;
            _conn= new SqlConnection(_cadena);
            string query = $"select SUM(NoCanalCompleta) from PlaneacionProduccion a " +
                $"inner join CanalPlaneacion b on b.PlaneacionId=a.PlaneacionId " +
                $"where a.FechaPlan='{fecha}' and a.TipoPlan = '{clasificacion}' ";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                res = Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return res;
        }
        public List<SubclasificacionDia> ContarTotales(string clasificacion, string fecha)
        {
            var lista = new List<SubclasificacionDia>();

            using (var conn = new SqlConnection(_cadena))
            {
                string query = @"
SELECT  
    sc.Id AS SubClasificacionId,
    sc.Nombre AS SubClasificacion,
    ISNULL(SUM(b.NoCanalCompleta), 0) AS Canales
FROM SubClasif sc
INNER JOIN Clasificacion c 
    ON c.Nombre = sc.fk_Clasificacion
LEFT JOIN (
    SELECT  
        b.fk_SubClas,
        b.NoCanalCompleta
    FROM PlaneacionProduccion a
    INNER JOIN CanalPlaneacion b  
        ON b.PlaneacionId = a.PlaneacionId
    WHERE a.FechaPlan = @fecha
      AND a.TipoPlan = @clasificacionNombre
) b 
    ON b.fk_SubClas = sc.Id
WHERE c.Nombre = @clasificacionNombre
GROUP BY sc.Id, sc.Nombre
ORDER BY sc.Nombre
        ";

                var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@fecha", fecha);
                cmd.Parameters.AddWithValue("@clasificacionNombre", clasificacion);

                conn.Open();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new SubclasificacionDia
                        {
                            SubClasificacionId = Convert.ToInt32(reader["SubClasificacionId"]),
                            SubClasificacion = reader["SubClasificacion"].ToString(),
                            Canales = Convert.ToInt32(reader["Canales"])
                        });
                    }
                }
            }

            return lista;
        }
        public string NombreProducto(string sku)
        {
            SqlConnection _conn2 = new SqlConnection(_cadena);
            var res = "";
            string query = $"select ProductoNombre from ArticuloSap where ProductoCodigo='{sku}'";
            SqlCommand cmd = new SqlCommand(query,_conn2);
            _conn2.Open();
            try
            {
                res = Convert.ToString(cmd.ExecuteScalar());
            }
            catch (Exception)
            {
            }
            _conn2.Close();
            return res;
        }
        public bool borrarPlanDiario(int planid)
        {
            var res = false;
            _conn = new SqlConnection(_cadena);
            string query = $"delete from PlanDiario where PlaneacionId={planid}; delete from PlanSubClas where PlanId={planid};";
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
        public bool InsertarPlanDiario(PlanDiarioModel model)
        {
            var res = false;
            _conn = new SqlConnection(_cadena);
            string query =$"INSERT INTO PlanDiario(PlaneacionId,ProductoCodigo,Porcentaje,KgLote,Canales,ProductoCodigoConvertido,PorcentajeInyeccion,KgInyeccion) VALUES " +
                $"({model.PlaneacionId}," +
                $"'{model.ProductoCodigo}'," +
                $"{model.Porcentaje}," +
                $"{model.KgLote}," +
                $"{model.Canales}," +
                $"'{model.ProductoCodigoConvertido}'," +
                $"{model.PorcentajeInyeccion}," +
                $"{model.KgInyeccion})";
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
        public bool InsertarSubDia(PlanSubClasModel model)
        {
            var res = false;
            _conn = new SqlConnection(_cadena);
            string query = "";
            if (model.ProductoCodigo=="N023")
            {
                query = $"INSERT INTO PlanSubClas (PlanId, fk_SubClas, ProductoCodigo, PartSub) " +
    $"VALUES ({model.PlanId}, {model.fk_SubClas}, '{model.ProductoCodigo}', {model.PartSub});";
            }
            else
            {
                query = $"INSERT INTO PlanSubClas (PlanId, fk_SubClas, ProductoCodigo, PartSub) " +
    $"VALUES ({model.PlanId}, {model.fk_SubClas}, '{model.ProductoCodigo}', {model.PartSub});";
            }
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

        //public double PesoPlanDiario(int planid, string producto)
        //{
        //    _conn = new SqlConnection(_cadena);
        //    string query = $"select KgLote from PlanDiario where PlaneacionId={planid} and ProductoCodigo='{producto}'";

        //}

    }
}
