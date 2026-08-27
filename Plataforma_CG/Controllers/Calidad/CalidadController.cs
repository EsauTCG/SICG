using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Plataforma_CG.Filters;
using Plataforma_CG.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace Plataforma_CG.Controllers
{
    public class CalidadController : Controller
    {
        private readonly IConfiguration _configuration;

        // Inyectamos la configuración para leer la cadena de conexión del appsettings.json
        public CalidadController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IActionResult Cuadrantes()
        {
            var listaCanales = new List<CanalViewModel>();
            string connectionStringTif = _configuration.GetConnectionString("CadenaMeatTIF");
            string connectionStringSigo = _configuration.GetConnectionString("DefaultConnection");

            string query = @"
    SELECT 
        a.Arete,
        a.ConsecutivoDia,
        c.Nombre as Lote,
        d.Referencia as Proveedor
    FROM Canal a
    INNER JOIN Produccion b ON a.ProduccionId = b.ProduccionId
    INNER JOIN Lote c ON b.LoteId = c.LoteId
    INNER JOIN SolicitudReferencia d ON c.LoteId = d.SolicitudProduccionId AND d.TipoReferenciaId = '3'
WHERE CONVERT(Date, c.FechaProduccion) = CONVERT(Date, GETDATE())
    ORDER BY a.ConsecutivoDia ASC";

            using (SqlConnection connection = new SqlConnection(connectionStringTif))
            {
                SqlCommand command = new SqlCommand(query, connection);
                try
                {
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string consecutivo = reader["ConsecutivoDia"]?.ToString() ?? "0";

                            // Toma la fecha actual en formato yyyyMMdd
                            string fechaFormato = DateTime.Today.ToString("yyyyMMdd");

                            var canal = new CanalViewModel
                            {
                                Id = $"{consecutivo}-{fechaFormato}",
                                Arete = reader["Arete"]?.ToString() ?? "",
                                Provider = reader["Proveedor"]?.ToString() ?? "Sin Proveedor",
                                Status = "Pendiente",

                                // Toma la fecha actual en formato dd/MM/yyyy
                                Date = DateTime.Today.ToString("dd/MM/yyyy"),

                                Shift = "Mañana",
                                Lot = reader["Lote"]?.ToString() ?? "Sin Lote",
                                Records = new List<RegistroViewModel>()
                            };

                            listaCanales.Add(canal);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Error al conectar con TIF_Meat: " + ex.Message);
                }
            }

            if (!listaCanales.Any())
                return View(listaCanales);

            try
            {
                using (var conn = new SqlConnection(connectionStringSigo))
                {
                    conn.Open();

                    var estatusMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    using (var cmd = new SqlCommand("SELECT CanalId, Estatus FROM PCC1B_Estatus", conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string key = rdr["CanalId"]?.ToString()?.Trim() ?? "";
                            string val = rdr["Estatus"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(key))
                                estatusMap[key] = val;
                        }
                    }

                    using (var cmd = new SqlCommand(@"SELECT CanalId, Arete, Vista, Hallazgos, Cuadrantes, 
                        CuadrantesVerdes, CuadrantesAmarillos, CuadrantesRojos,
                        AccionCorrectiva, Reinspeccion, VerificacionC, VerificacionCumple, 
                        Observaciones, Inspector, FechaCaptura 
                        FROM PCC1B_Registros", conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string canalIdDb = rdr["CanalId"]?.ToString()?.Trim() ?? "";
                            var canal = listaCanales.FirstOrDefault(c => c.Id == canalIdDb);
                            if (canal == null) continue;

                            string hallazgos = rdr["Hallazgos"]?.ToString() ?? "";
                            string cuadrantes = rdr["Cuadrantes"]?.ToString() ?? "";
                            string verdes = rdr["CuadrantesVerdes"]?.ToString() ?? "";
                            string amarillos = rdr["CuadrantesAmarillos"]?.ToString() ?? "";
                            string rojos = rdr["CuadrantesRojos"]?.ToString() ?? "";

                            canal.Records.Add(new RegistroViewModel
                            {
                                Id = Guid.NewGuid().ToString(),
                                Side = rdr["Vista"]?.ToString() ?? "",
                                Findings = string.IsNullOrEmpty(hallazgos)
                                    ? new List<string>()
                                    : hallazgos.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                                Quadrants = string.IsNullOrEmpty(cuadrantes)
                                    ? new List<int>()
                                    : cuadrantes.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(int.Parse).ToList(),

                                CuadrantesVerdes = string.IsNullOrEmpty(verdes) ? new List<int>() : verdes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
                                CuadrantesAmarillos = string.IsNullOrEmpty(amarillos) ? new List<int>() : amarillos.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
                                CuadrantesRojos = string.IsNullOrEmpty(rojos) ? new List<int>() : rojos.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),

                                CorrectiveAction = rdr["AccionCorrectiva"]?.ToString() ?? "",
                                Reinspection = rdr["Reinspeccion"]?.ToString() ?? "",
                                VerificationChannel = rdr["VerificacionC"]?.ToString() ?? "",
                                VerificationComplies = rdr["VerificacionCumple"] != DBNull.Value && (bool)rdr["VerificacionCumple"],
                                Observation = rdr["Observaciones"]?.ToString() ?? "",
                                Inspector = rdr["Inspector"]?.ToString() ?? "",
                                Datetime = rdr["FechaCaptura"] != DBNull.Value
                                    ? ((DateTime)rdr["FechaCaptura"]).ToString("dd/MM/yyyy hh:mm tt") : ""
                            });
                        }
                    }

                    foreach (var canal in listaCanales)
                    {
                        string key = canal.Id?.Trim() ?? "";
                        if (estatusMap.TryGetValue(key, out string estatus))
                            canal.Status = estatus;
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar estatus desde SIGO: " + ex.Message);
            }

            return View(listaCanales);
        }

    


        [HttpPost]
        public IActionResult GuardarMonitoreo([FromBody] RegistroViewModel modelo, string canalId, string arete, string estatusGeneral, string verdes = "", string amarillos = "", string rojos = "")
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    // MERGE: 1 sola fila por CanalId y Vista. ¡Y guardando los colores!
                    string sqlMerge = @"
                IF EXISTS (SELECT 1 FROM PCC1B_Registros WHERE CanalId = @CanalId AND Vista = @Side)
                BEGIN
                    UPDATE PCC1B_Registros 
                    SET Hallazgos = @FindingsStr, 
                        Cuadrantes = @QuadrantsStr, 
                        CuadrantesVerdes = @Verdes,
                        CuadrantesAmarillos = @Amarillos,
                        CuadrantesRojos = @Rojos,
                        AccionCorrectiva = @CorrectiveAction, 
                        Reinspeccion = @Reinspection, 
                        VerificacionC = @VerificationChannel, 
                        VerificacionCumple = @VerificationComplies, 
                        Observaciones = @Observation, 
                        Inspector = @Inspector, 
                        FechaCaptura = GETDATE()
                    WHERE CanalId = @CanalId AND Vista = @Side
                END
                ELSE
                BEGIN
                    INSERT INTO PCC1B_Registros 
                    (CanalId, Arete, Vista, Hallazgos, Cuadrantes, CuadrantesVerdes, CuadrantesAmarillos, CuadrantesRojos, AccionCorrectiva, Reinspeccion, VerificacionC, VerificacionCumple, Observaciones, Inspector, FechaCaptura) 
                    VALUES 
                    (@CanalId, @Arete, @Side, @FindingsStr, @QuadrantsStr, @Verdes, @Amarillos, @Rojos, @CorrectiveAction, @Reinspection, @VerificationChannel, @VerificationComplies, @Observation, @Inspector, GETDATE())
                END";

                    var parametros = new DynamicParameters();
                    parametros.Add("CanalId", canalId ?? "");
                    parametros.Add("Arete", arete ?? "");
                    parametros.Add("Side", modelo.Side ?? "");
                    parametros.Add("FindingsStr", string.Join(",", modelo.Findings ?? new List<string>()));
                    parametros.Add("QuadrantsStr", string.Join(",", modelo.Quadrants ?? new List<int>()));
                    parametros.Add("Verdes", verdes ?? "");
                    parametros.Add("Amarillos", amarillos ?? "");
                    parametros.Add("Rojos", rojos ?? "");
                    parametros.Add("CorrectiveAction", modelo.CorrectiveAction ?? "");
                    parametros.Add("Reinspection", modelo.Reinspection ?? "");
                    parametros.Add("VerificationChannel", modelo.VerificationChannel ?? "");
                    parametros.Add("VerificationComplies", modelo.VerificationComplies);
                    parametros.Add("Observation", modelo.Observation ?? "");
                    parametros.Add("Inspector", User.Identity?.Name ?? "Sistema");

                    conn.Execute(sqlMerge, parametros);

                    // Actualiza estatus general
                    string mergeEstatus = @"
                IF EXISTS (SELECT 1 FROM PCC1B_Estatus WHERE CanalId = @CanalId)
                    UPDATE PCC1B_Estatus SET Estatus = @Estatus, FechaActualizacion = GETDATE() WHERE CanalId = @CanalId
                ELSE
                    INSERT INTO PCC1B_Estatus (CanalId, Estatus, FechaActualizacion) VALUES (@CanalId, @Estatus, GETDATE())";

                    conn.Execute(mergeEstatus, new { CanalId = canalId ?? "", Estatus = estatusGeneral ?? "En inspección" });
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult ActualizarEstatusCanal(string canalId, string estatusGeneral)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    string mergeEstatus = @"
                IF EXISTS (SELECT 1 FROM PCC1B_Estatus WHERE CanalId = @CanalId)
                    UPDATE PCC1B_Estatus SET Estatus = @Estatus, FechaActualizacion = GETDATE() WHERE CanalId = @CanalId
                ELSE
                    INSERT INTO PCC1B_Estatus (CanalId, Estatus, FechaActualizacion) VALUES (@CanalId, @Estatus, GETDATE())";

                    var paramEstatus = new DynamicParameters();
                    paramEstatus.Add("CanalId", canalId ?? "");
                    paramEstatus.Add("Estatus", estatusGeneral ?? "");
                    conn.Execute(mergeEstatus, paramEstatus);
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult GuardarRevisionAleatoria(string canalId, bool revisionCorrecta, string hallazgos, string cuadrantes, string observaciones)
        {
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    string sql = @"
                UPDATE PCC1B_Estatus 
                SET RevisionRealizada = 1,
                    RevisionCorrecta = @RevisionCorrecta,
                    RevisionHallazgos = @Hallazgos,
                    RevisionCuadrantes = @Cuadrantes,
                    RevisionObservaciones = @Observaciones,
                    RevisionInspector = @Inspector,
                    RevisionFecha = GETDATE(),
                    FechaActualizacion = GETDATE()
                WHERE CanalId = @CanalId";

                    var parametros = new
                    {
                        CanalId = canalId ?? "",
                        RevisionCorrecta = revisionCorrecta,
                        Hallazgos = hallazgos ?? "",
                        Cuadrantes = cuadrantes ?? "",
                        Observaciones = observaciones ?? "",
                        Inspector = User.Identity?.Name ?? "Auditor"
                    };

                    conn.Execute(sql, parametros);
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult Historico(DateTime? fecha, int pagina = 1)
        {
            int registrosPorPagina = 100;
            int offset = (pagina - 1) * registrosPorPagina;

            // Si no se recibe fecha, asigna la fecha actual
            DateTime fechaFiltro = fecha ?? DateTime.Today;
            ViewBag.FechaSeleccionada = fechaFiltro.ToString("yyyy-MM-dd");
            ViewBag.PaginaActual = pagina;

            string fechaSql = fechaFiltro.ToString("yyyy-MM-dd");
            string fechaId = fechaFiltro.ToString("yyyyMMdd");

            var listaCanales = new List<CanalViewModel>();

            try
            {
                string connectionStringSigo = _configuration.GetConnectionString("DefaultConnection");
                using (var conn = new SqlConnection(connectionStringSigo))
                {
                    conn.Open();

                    // 1. Traer directamente los 50 registros de la página actual de forma  rápida
                    string queryCanales = @"
                SELECT 
                    a.Arete,
                    a.ConsecutivoDia,
                    c.Nombre as Lote,
                    d.Referencia as Proveedor,
                    ISNULL(est.Estatus, 'Pendiente') AS Status,
                    ISNULL(est.RevisionRealizada, 0) AS RevisionRealizada,
                    est.RevisionCorrecta,
                    est.RevisionHallazgos,
                    est.RevisionCuadrantes,
                    est.RevisionObservaciones,
                    est.RevisionInspector
                FROM TIF_Meat.dbo.Canal a
                INNER JOIN TIF_Meat.dbo.Produccion b ON a.ProduccionId = b.ProduccionId
                INNER JOIN TIF_Meat.dbo.Lote c ON b.LoteId = c.LoteId
                INNER JOIN TIF_Meat.dbo.SolicitudReferencia d ON c.LoteId = d.SolicitudProduccionId AND d.TipoReferenciaId = '3'
                LEFT JOIN SIGO.dbo.PCC1B_Estatus est ON est.CanalId = (CAST(a.ConsecutivoDia AS VARCHAR(50)) + '-' + @FechaId)
                WHERE c.FechaProduccion >= @FechaFiltro 
                  AND c.FechaProduccion < DATEADD(day, 1, @FechaFiltro)
                ORDER BY a.ConsecutivoDia ASC
                OFFSET @Offset ROWS FETCH NEXT @RegistrosPorPagina ROWS ONLY";

                    var canalesRaw = conn.Query(queryCanales, new { FechaFiltro = fechaSql, FechaId = fechaId, Offset = offset, RegistrosPorPagina = registrosPorPagina }).ToList();

                    // Si la consulta trae menos de 50 registros, sabemos exactamente que es la última página
                    ViewBag.TieneMasPaginas = canalesRaw.Count == registrosPorPagina;

                    var idsPagina = canalesRaw.Select(r => $"{r.ConsecutivoDia}-{fechaId}").ToList();

                    var registrosRows = new List<dynamic>();
                    if (idsPagina.Any())
                    {
                        // 1. Agregamos las columnas de colores al SELECT del Histórico
                        string queryRegistros = @"
                    SELECT CanalId, Arete, Vista, Hallazgos, Cuadrantes, 
                        CuadrantesVerdes, CuadrantesAmarillos, CuadrantesRojos,
                        AccionCorrectiva, Reinspeccion, VerificacionC, VerificacionCumple, 
                        Observaciones, Inspector, FechaCaptura 
                    FROM SIGO.dbo.PCC1B_Registros
                    WHERE CanalId IN @IdsPagina";

                        registrosRows = conn.Query(queryRegistros, new { IdsPagina = idsPagina }).ToList();
                    }

                    var registrosAgrupados = registrosRows
                        .GroupBy(r => (string)r.CanalId)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    foreach (var row in canalesRaw)
                    {
                        string consecutivo = row.ConsecutivoDia?.ToString() ?? "0";
                        string idCanal = $"{consecutivo}-{fechaId}";

                        var canal = new CanalViewModel
                        {
                            Id = idCanal,
                            Arete = row.Arete?.ToString() ?? "",
                            Provider = row.Proveedor?.ToString() ?? "Sin Proveedor",
                            Status = row.Status?.ToString(),
                            Date = fechaFiltro.ToString("dd/MM/yyyy"),
                            Lot = row.Lote?.ToString() ?? "Sin Lote",
                            Records = new List<RegistroViewModel>(),

                            RevisionRealizada = row.RevisionRealizada != null && (bool)row.RevisionRealizada,
                            RevisionCorrecta = row.RevisionCorrecta != null ? (bool?)row.RevisionCorrecta : null,
                            RevisionHallazgos = row.RevisionHallazgos?.ToString() ?? "",
                            RevisionCuadrantes = row.RevisionCuadrantes?.ToString() ?? "",
                            RevisionObservaciones = row.RevisionObservaciones?.ToString() ?? "",
                            RevisionInspector = row.RevisionInspector?.ToString() ?? ""
                        };

                        if (registrosAgrupados.TryGetValue(idCanal, out var registrosCanal))
                        {
                            foreach (var reg in registrosCanal)
                            {
                                // Leemos de forma segura las nuevas columnas
                                string hallazgos = (string)(reg.Hallazgos ?? "");
                                string cuadrantes = (string)(reg.Cuadrantes ?? "");
                                string verdes = (string)(reg.CuadrantesVerdes ?? "");
                                string amarillos = (string)(reg.CuadrantesAmarillos ?? "");
                                string rojos = (string)(reg.CuadrantesRojos ?? "");

                                canal.Records.Add(new RegistroViewModel
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Side = (string)(reg.Vista ?? ""),
                                    Findings = string.IsNullOrEmpty(hallazgos) ? new List<string>() : hallazgos.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                                    Quadrants = string.IsNullOrEmpty(cuadrantes) ? new List<int>() : cuadrantes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),

                                    // 2. Llenamos las listas de colores para mandarlas a la Vista Histórica
                                    CuadrantesVerdes = string.IsNullOrEmpty(verdes) ? new List<int>() : verdes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
                                    CuadrantesAmarillos = string.IsNullOrEmpty(amarillos) ? new List<int>() : amarillos.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
                                    CuadrantesRojos = string.IsNullOrEmpty(rojos) ? new List<int>() : rojos.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),

                                    CorrectiveAction = (string)(reg.AccionCorrectiva ?? ""),
                                    Reinspection = (string)(reg.Reinspeccion ?? ""),
                                    VerificationChannel = (string)(reg.VerificacionC ?? ""),
                                    VerificationComplies = reg.VerificacionCumple != null && (bool)reg.VerificacionCumple,
                                    Observation = (string)(reg.Observaciones ?? ""),
                                    Inspector = (string)(reg.Inspector ?? ""),
                                    Datetime = reg.FechaCaptura != null ? ((DateTime)reg.FechaCaptura).ToString("dd/MM/yyyy hh:mm tt") : ""
                                });
                            }
                        }
                        listaCanales.Add(canal);
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar historico: " + ex.Message);
            }

            return View(listaCanales);
        }

        [HttpGet]
        public IActionResult CuadrantesData()
        {
            var listaCanales = new List<CanalViewModel>();
            try
            {
                string connectionStringTif = _configuration.GetConnectionString("CadenaMeatTIF");
                string connectionStringSigo = _configuration.GetConnectionString("DefaultConnection");

                // Tu consulta optimizada y rapida
                string query = @"
                SELECT 
                    a.Arete,
                    a.ConsecutivoDia,
                    c.Nombre as Lote,
                    d.Referencia as Proveedor
                FROM Canal a
                INNER JOIN Produccion b ON a.ProduccionId = b.ProduccionId
                INNER JOIN Lote c ON b.LoteId = c.LoteId
                INNER JOIN SolicitudReferencia d ON c.LoteId = d.SolicitudProduccionId AND d.TipoReferenciaId = '3'
                WHERE CONVERT(Date, c.FechaProduccion) = CONVERT(Date, GETDATE())
                ORDER BY a.ConsecutivoDia ASC";

                using (SqlConnection connection = new SqlConnection(connectionStringTif))
                {
                    SqlCommand command = new SqlCommand(query, connection);
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string consecutivo = reader["ConsecutivoDia"]?.ToString() ?? "0";
                            string fechaFormato = DateTime.Today.ToString("yyyyMMdd");

                            listaCanales.Add(new CanalViewModel
                            {
                                Id = $"{consecutivo}-{fechaFormato}",
                                Arete = reader["Arete"]?.ToString() ?? "",
                                Provider = reader["Proveedor"]?.ToString() ?? "Sin Proveedor",
                                Status = "Pendiente",
                                Date = DateTime.Today.ToString("dd/MM/yyyy"),
                                Shift = "Mañana",
                                Lot = reader["Lote"]?.ToString() ?? "Sin Lote",
                                Records = new List<RegistroViewModel>()
                            });
                        }
                    }
                }

                using (var conn = new SqlConnection(connectionStringSigo))
                {
                    conn.Open();

                    var estatusMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    using (var cmd = new SqlCommand("SELECT CanalId, Estatus FROM PCC1B_Estatus", conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string key = rdr["CanalId"]?.ToString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(key)) estatusMap[key] = rdr["Estatus"]?.ToString() ?? "";
                        }
                    }

                    using (var cmd = new SqlCommand(@"SELECT Id, CanalId, Arete, Vista, Hallazgos, Cuadrantes, 
                        CuadrantesVerdes, CuadrantesAmarillos, CuadrantesRojos, 
                        AccionCorrectiva, Reinspeccion, VerificacionC, VerificacionCumple, 
                        Observaciones, Inspector, FechaCaptura 
                        FROM PCC1B_Registros", conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            try
                            {
                                string canalIdDb = rdr["CanalId"]?.ToString()?.Trim() ?? "";
                                var canal = listaCanales.FirstOrDefault(c => c.Id == canalIdDb);
                                if (canal == null) continue;

                                string hallazgos = rdr["Hallazgos"]?.ToString() ?? "";
                                string cuadrantes = rdr["Cuadrantes"]?.ToString() ?? "";
                                string verdes = rdr["CuadrantesVerdes"]?.ToString() ?? "";
                                string amarillos = rdr["CuadrantesAmarillos"]?.ToString() ?? "";
                                string rojos = rdr["CuadrantesRojos"]?.ToString() ?? "";

                                bool verificationComplies = false;
                                var vcVal = rdr["VerificacionCumple"];
                                if (vcVal != DBNull.Value && vcVal != null)
                                {
                                    string vcStr = vcVal.ToString().Trim().ToLower();
                                    verificationComplies = (vcStr == "1" || vcStr == "true");
                                }

                                canal.Records.Add(new RegistroViewModel
                                {
                                    Id = rdr["Id"]?.ToString() ?? Guid.NewGuid().ToString(),
                                    Side = rdr["Vista"]?.ToString() ?? "",
                                    Findings = string.IsNullOrEmpty(hallazgos) ? new List<string>() : hallazgos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList(),
                                    Quadrants = string.IsNullOrEmpty(cuadrantes) ? new List<int>() : cuadrantes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => { int.TryParse(s.Trim(), out int v); return v; }).Where(v => v > 0).ToList(),
                                    CuadrantesVerdes = string.IsNullOrEmpty(verdes) ? new List<int>() : verdes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => { int.TryParse(s.Trim(), out int v); return v; }).Where(v => v > 0).ToList(),
                                    CuadrantesAmarillos = string.IsNullOrEmpty(amarillos) ? new List<int>() : amarillos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => { int.TryParse(s.Trim(), out int v); return v; }).Where(v => v > 0).ToList(),
                                    CuadrantesRojos = string.IsNullOrEmpty(rojos) ? new List<int>() : rojos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => { int.TryParse(s.Trim(), out int v); return v; }).Where(v => v > 0).ToList(),
                                    CorrectiveAction = rdr["AccionCorrectiva"]?.ToString() ?? "",
                                    Reinspection = rdr["Reinspeccion"]?.ToString() ?? "",
                                    VerificationChannel = rdr["VerificacionC"]?.ToString() ?? "",
                                    VerificationComplies = verificationComplies,
                                    Observation = rdr["Observaciones"]?.ToString() ?? "",
                                    Inspector = rdr["Inspector"]?.ToString() ?? "",
                                    Datetime = rdr["FechaCaptura"] != DBNull.Value ? Convert.ToDateTime(rdr["FechaCaptura"]).ToString("dd/MM/yyyy hh:mm tt") : ""
                                });
                            }
                            catch { continue; }
                        }
                    }

                    foreach (var canal in listaCanales)
                    {
                        if (estatusMap.TryGetValue(canal.Id, out string estatus))
                            canal.Status = estatus;
                    }
                }

                return Json(listaCanales); // Mandamos la lista limpia
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

    }
}