using Plataforma_CG.Models;
using Plataforma_CG.Models.Comercial.Planeacion.Semanal;
using System.Data.SqlClient;

namespace Plataforma_CG.AccesoDatos.Comercial.Planeacion.Semanal
{
    public class AccesoPlanSemanal
    {
        private SqlConnection _conn = new SqlConnection(new Conexion().GetCadenaSQLVentas());
        public TodoSemanalModel ListarTodo(int plan)
        {
            var lista = new TodoSemanalModel();
            lista._ListaPlanSemanal = new List<PlanSemanalModel>();
            string query = $"select * from PlanSemanal where fk_Plan={plan}";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista._ListaPlanSemanal.Add(new PlanSemanalModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            FechaIn = Convert.ToString(dr["FechaIn"]),
                            FechaFin = Convert.ToString(dr["FechaFin"]),
                            Canales = Convert.ToInt32(dr["Canales"]),
                            PesoPromedio = Convert.ToDouble(dr["PesoPromedio"]),
                            PesoTotal = Convert.ToDouble(dr["PesoTotal"]),
                            fk_Plan = Convert.ToInt32(dr["fk_Plan"])
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
        public TodoSemanalModel Listar(int plan, string fechain,string fechafin)
        {
            var lista = new TodoSemanalModel();
            fechain = Convert.ToDateTime(fechain).ToString("yyyyMMdd");
            fechafin = Convert.ToDateTime(fechafin).ToString("yyyyMMdd");
            string query = $"select * from PlanSemanal where fk_Plan={plan} and CONVERT(DATE,FechaIn)='{fechain}' and CONVERT(DATE,FechaFin)='{fechafin}'";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        lista._PlanSemanal = new PlanSemanalModel
                        {
                            Id = Convert.ToInt32(dr["Id"]),
                            FechaIn = Convert.ToString(dr["FechaIn"]),
                            FechaFin = Convert.ToString(dr["FechaFin"]),
                            Canales = Convert.ToInt32(dr["Canales"]),
                            PesoPromedio = Convert.ToDouble(dr["PesoPromedio"]),
                            PesoTotal = Convert.ToDouble(dr["PesoTotal"]),
                            fk_Plan = Convert.ToInt32(dr["fk_Plan"])
                        };
                    }
                }
            }
            catch (Exception)
            {
            }
            _conn.Close();
            return lista;
        }
        public int Insertar(PlanSemanalModel model)
        {
            int res = 0;
            string query = $"insert into PlanSemanal([FechaIn],[FechaFin],[Canales],[PesoPromedio],[PesoTotal],[fk_Plan]) " +
                $"values('{model.FechaIn}','{model.FechaFin}',{model.Canales},{model.PesoPromedio},{model.PesoTotal},{model.fk_Plan});" +
                $"SELECT SCOPE_IDENTITY();";
            Borrar(model.fk_Plan,model.FechaIn,model.FechaFin);
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
        public bool Borrar(int plan, string fechain, string fechafin)
        {
            bool res = false;
            string query = $"delete from PlanSemanal where fk_Plan={plan} and FechaIn='{fechain}' and FechaFin='{fechafin}'";
            SqlCommand cmd = new SqlCommand(query,_conn);
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
        public bool Actualizar(PlanSemanalModel model)
        {
            bool res = false;
            string query = $"update PlanSemanal" +
                $"set Canales={model.Canales}, " +
                $"PesoPromedio={model.PesoPromedio}, " +
                $"PesoTotal={model.PesoTotal} " +
                $"where Id={model.Id}";
            SqlCommand cmd = new SqlCommand(query, _conn);
            _conn.Open();
            try
            {
                if (cmd.ExecuteNonQuery() > 0)
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
        public List<SemanasModel> Semanas(int ano, int mes)
        {
            var lista = new List<SemanasModel>();
            DateTime primerDia = new DateTime(ano, mes, 1);
            DateTime ultimoDia = primerDia.AddMonths(1).AddDays(-1);

            DateTime inicioSemana = primerDia;

            // Si el primer día no es domingo, la primera semana termina el sábado de esa semana
            DayOfWeek diaSemana = primerDia.DayOfWeek;
            int diasHastaSabado = DayOfWeek.Saturday - diaSemana;
            if (diasHastaSabado < 0) diasHastaSabado += 7; // seguridad
            DateTime finSemana = inicioSemana.AddDays(diasHastaSabado);


            // Asegurarnos de no pasar del último día del mes
            if (finSemana > ultimoDia)
                finSemana = ultimoDia;
            if (inicioSemana != finSemana)
            {
                lista.Add(new SemanasModel { Inicio = inicioSemana, Fin = finSemana });
            }
            // Avanzar a la siguiente semana
            inicioSemana = finSemana.AddDays(1);

            while (inicioSemana < ultimoDia)
            {
                // Fin de semana normalmente sería sábado
                finSemana = inicioSemana.AddDays(6);

                if (finSemana > ultimoDia)
                    finSemana = ultimoDia;
                if (inicioSemana != finSemana)
                {
                    lista.Add(new SemanasModel { Inicio = inicioSemana, Fin = finSemana });
                }
                inicioSemana = finSemana.AddDays(1);
            }

            return lista;
        }
    }
}
