using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using System.Text.Json;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Plataforma_CG.Controllers
{
    [Authorize] 
    [Route("tif")]
    public class TifController : Controller
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;

        public TifController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _http = httpClientFactory.CreateClient("TifApi");
            _config = config;
        }

        // ✅ Sirve la vista
        [HttpGet("pruebas")]
        public IActionResult Pruebas() => View();

        // =========================
        // ======= ENDPOINTS ========
        // =========================

        [HttpGet("pruebas/listar")]
        public async Task<IActionResult> ConsultarPruebasTif()
        {
            var res = await _http.GetAsync("/Pruebas/ConsultarPruebasTIF");
            return await PassthroughJson(res);
        }

        [HttpGet("recetas")]
        public async Task<IActionResult> ListarRecetas()
        {
            var res = await _http.GetAsync("/Recetas/ListarPorcRec");
            return await PassthroughJson(res);
        }

        [HttpGet("lotes")]
        public async Task<IActionResult> ConsultarLotesTif()
        {
            var res = await _http.GetAsync("/Lotes/ConsultarLotesTIF");
            return await PassthroughJson(res);
        }

        // ✅ CAMBIO: insertar prueba ya no debe ser GET, mejor POST
        public record NuevaPruebaRequest(string lote);

        [HttpPost("pruebas")]
        [ValidateAntiForgeryToken] // ✅ seguridad
        public async Task<IActionResult> InsertarPrueba([FromBody] NuevaPruebaRequest req)
        {
            if (req is null || string.IsNullOrWhiteSpace(req.lote))
                return BadRequest("Lote requerido.");

            // ✅ Usa el usuario logueado (ya no hardcode “Admin”)
            var usr = User?.Identity?.Name ?? "Sistema";

            // Tu API actual espera querystring (?lote=...&usr=...)
            var url = $"/Pruebas/InsertarPruebasTIF?lote={Uri.EscapeDataString(req.lote)}&usr={Uri.EscapeDataString(usr)}";
            var res = await _http.GetAsync(url);

            // La API devuelve "true"/"false" como texto
            var txt = await res.Content.ReadAsStringAsync();
            return Content(txt, "text/plain");
        }

        [HttpGet("grasa")]
        public async Task<IActionResult> ListarGrasa([FromQuery] int pruebaid)
        {
            var res = await _http.GetAsync($"/Grasa/ListarGrasa?pruebaid={pruebaid}");
            return await PassthroughJson(res);
        }

        [HttpPost("grasa")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarGrasa([FromBody] object body)
        {
            var res = await _http.PostAsJsonAsync("/Grasa/GuardarGrasa", body);
            var txt = await res.Content.ReadAsStringAsync();
            return Content(txt, "text/plain");
        }

        [HttpGet("recorte")]
        public async Task<IActionResult> ConsultarRecorte([FromQuery] int pruebaid)
        {
            var res = await _http.GetAsync($"/Recorte/ConsultarRecorte?pruebaid={pruebaid}");
            return await PassthroughJson(res);
        }

        [HttpPost("recorte")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarRecorte([FromBody] object body)
        {
            var res = await _http.PostAsJsonAsync("/Recorte/GuardarRecorte", body);
            var txt = await res.Content.ReadAsStringAsync();
            return Content(txt, "text/plain");
        }

        [HttpGet("hueso")]
        public async Task<IActionResult> ListarHueso([FromQuery] int pruebaid)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var res = await _http.GetAsync($"/Hueso/ListarHueso?pruebaid={pruebaid}", cts.Token);
                return await PassthroughJson(res);
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, "Tiempo de espera agotado al consultar hueso en la API interna TIF.");
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(503, $"No se pudo conectar con la API interna TIF: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error inesperado al consultar hueso: {ex.Message}");
            }
        }

        [HttpPost("hueso")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarHueso([FromBody] JsonElement body)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                int idBody = ObtenerIntJson(body, "id", "Id");
                int fkPrueba = ObtenerIntJson(body, "fk_Prueba", "fk_Prueba", "Fk_Prueba");
                int fkHueso = ObtenerIntJson(body, "fk_Hueso", "fk_Hueso", "Fk_Hueso");

                if (fkPrueba <= 0)
                    return BadRequest("Prueba inválida.");

                if (fkHueso <= 0)
                    return BadRequest("Hueso inválido.");

                int idExistente = 0;

                // Consulta primero lo que ya existe en la API.
                var resConsulta = await _http.GetAsync($"/Hueso/ListarHueso?pruebaid={fkPrueba}", cts.Token);
                var jsonConsulta = await resConsulta.Content.ReadAsStringAsync(cts.Token);

                if (resConsulta.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(jsonConsulta))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(jsonConsulta);

                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var h in doc.RootElement.EnumerateArray())
                            {
                                int fkHuesoApi = ObtenerIntJson(h, "fk_Hueso", "Fk_Hueso");
                                int idApi = ObtenerIntJson(h, "id", "Id");

                                if (fkHuesoApi == fkHueso && idApi > 0)
                                {
                                    idExistente = idApi;
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Si por alguna razón no se puede leer la consulta,
                        // no tronamos el guardado; se continúa con el body original.
                    }
                }

                var bodyData = new
                {
                    id = idExistente > 0 ? idExistente : idBody,
                    fk_Prueba = fkPrueba,
                    fk_Hueso = fkHueso,
                    porcObjetivo = ObtenerDecimalJson(body, "porcObjetivo", "PorcObjetivo"),
                    kgEntrada = ObtenerDecimalJson(body, "kgEntrada", "KgEntrada"),
                    kgSalida = ObtenerDecimalJson(body, "kgSalida", "KgSalida"),
                    porcPartic = ObtenerDecimalJson(body, "porcPartic", "PorcPartic"),
                    difPorc = ObtenerDecimalJson(body, "difPorc", "DifPorc")
                };

                var res = await _http.PostAsJsonAsync("/Hueso/GuardarHueso", bodyData, cts.Token);
                var txt = await res.Content.ReadAsStringAsync(cts.Token);

                if (!res.IsSuccessStatusCode)
                {
                    return StatusCode(
                        (int)res.StatusCode,
                        string.IsNullOrWhiteSpace(txt)
                            ? "La API interna TIF respondió con error."
                            : txt
                    );
                }

                return Content(txt, "text/plain");
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, "Tiempo de espera agotado al comunicarse con la API interna TIF.");
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(503, $"No se pudo conectar con la API interna TIF: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error inesperado al guardar hueso: {ex.Message}");
            }
        }

       

        // =========================
        // ======= HELPERS =========
        // =========================

        private static int ObtenerIntJson(JsonElement elemento, params string[] nombres)
        {
            foreach (var nombre in nombres)
            {
                if (elemento.TryGetProperty(nombre, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var valor))
                        return valor;

                    if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out valor))
                        return valor;
                }
            }

            return 0;
        }

        private static decimal ObtenerDecimalJson(JsonElement elemento, params string[] nombres)
        {
            foreach (var nombre in nombres)
            {
                if (elemento.TryGetProperty(nombre, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var valor))
                        return valor;

                    if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out valor))
                        return valor;
                }
            }

            return 0;
        }

        private static async Task<IActionResult> PassthroughJson(HttpResponseMessage res)
        {
            var contentType = res.Content.Headers.ContentType?.ToString() ?? "application/json";
            var payload = await res.Content.ReadAsStringAsync();

            // Respeta status code
            return new ContentResult
            {
                Content = payload,
                ContentType = contentType,
                StatusCode = (int)res.StatusCode
            };
        }

        [HttpGet("reportes/excel")]
        public async Task<IActionResult> DescargarReporteExcel([FromQuery] int pruebaid)
        {
            if (pruebaid <= 0)
                return BadRequest("Prueba inválida.");

            var opciones = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var resHueso = await _http.GetAsync($"/Hueso/ListarHueso?pruebaid={pruebaid}");
            var resRecorte = await _http.GetAsync($"/Recorte/ConsultarRecorte?pruebaid={pruebaid}");
            var resGrasa = await _http.GetAsync($"/Grasa/ListarGrasa?pruebaid={pruebaid}");
            var resPruebas = await _http.GetAsync("/Pruebas/ConsultarPruebasTIF");

            if (!resHueso.IsSuccessStatusCode || !resRecorte.IsSuccessStatusCode || !resGrasa.IsSuccessStatusCode)
                return BadRequest("No se pudo generar el reporte.");

            var jsonHueso = await resHueso.Content.ReadAsStringAsync();
            var jsonRecorte = await resRecorte.Content.ReadAsStringAsync();
            var jsonGrasa = await resGrasa.Content.ReadAsStringAsync();
            var jsonPruebas = await resPruebas.Content.ReadAsStringAsync();

            var huesos = JsonSerializer.Deserialize<List<HuesoReporteDto>>(jsonHueso, opciones) ?? new List<HuesoReporteDto>();
            var recorte = JsonSerializer.Deserialize<RecorteReporteDto>(jsonRecorte, opciones) ?? new RecorteReporteDto();
            var grasa = JsonSerializer.Deserialize<GrasaReporteDto>(jsonGrasa, opciones) ?? new GrasaReporteDto();

            string lote = $"Prueba_{pruebaid}";
            string usuario = "";
            string fecha = "";
            string hora = "";

            try
            {
                using var doc = JsonDocument.Parse(jsonPruebas);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idProp) && idProp.GetInt32() == pruebaid)
                        {
                            if (item.TryGetProperty("lote", out var loteProp))
                                lote = loteProp.GetString() ?? lote;

                            if (item.TryGetProperty("realiza", out var realizaProp))
                                usuario = realizaProp.GetString() ?? "";

                            if (item.TryGetProperty("fecha", out var fechaProp))
                                fecha = fechaProp.GetString() ?? "";

                            if (item.TryGetProperty("hora", out var horaProp))
                                hora = horaProp.GetString() ?? "";

                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            using var wb = new XLWorkbook();

            var wsResumen = wb.Worksheets.Add("Resumen");
            var wsHueso = wb.Worksheets.Add("Hueso");
            var wsRecorte = wb.Worksheets.Add("Recorte");
            var wsGrasa = wb.Worksheets.Add("Grasa");

            // RESUMEN
            wsResumen.Cell("A1").Value = "Reporte TIF";
            wsResumen.Cell("A2").Value = "Prueba";
            wsResumen.Cell("B2").Value = pruebaid;
            wsResumen.Cell("A3").Value = "Lote";
            wsResumen.Cell("B3").Value = lote;
            wsResumen.Cell("A4").Value = "Usuario";
            wsResumen.Cell("B4").Value = usuario;
            wsResumen.Cell("A5").Value = "Fecha";
            wsResumen.Cell("B5").Value = fecha;
            wsResumen.Cell("A6").Value = "Hora";
            wsResumen.Cell("B6").Value = hora;

            wsResumen.Cell("A8").Value = "Sección";
            wsResumen.Cell("B8").Value = "% Objetivo";
            wsResumen.Cell("C8").Value = "% Participación";
            wsResumen.Cell("D8").Value = "% Diferencia";
            wsResumen.Cell("E8").Value = "Kg Entrada";
            wsResumen.Cell("F8").Value = "Kg Salida";

            const decimal OBJETIVO_HUESO_TOTAL = 5.5m;

            var totalObjHueso = OBJETIVO_HUESO_TOTAL;
            var totalPartHueso = huesos.Sum(x => x.PorcPartic);
            var totalDifHueso = totalPartHueso - totalObjHueso;
            var totalEntHueso = huesos.Sum(x => x.KgEntrada);
            var totalSalHueso = huesos.Sum(x => x.KgSalida);

            wsResumen.Cell("A9").Value = "Hueso Total";
            wsResumen.Cell("B9").Value = totalObjHueso;
            wsResumen.Cell("C9").Value = totalPartHueso;
            wsResumen.Cell("D9").Value = totalDifHueso;
            wsResumen.Cell("E9").Value = totalEntHueso;
            wsResumen.Cell("F9").Value = totalSalHueso;

            wsResumen.Cell("A10").Value = "Recorte";
            wsResumen.Cell("B10").Value = recorte.PorcObjetivo;
            wsResumen.Cell("C10").Value = recorte.PorcPartic;
            wsResumen.Cell("D10").Value = recorte.DifPorc;
            wsResumen.Cell("E10").Value = recorte.KgEntrada;
            wsResumen.Cell("F10").Value = recorte.KgSalida;

            wsResumen.Cell("A11").Value = "Grasa";
            wsResumen.Cell("B11").Value = grasa.PorcObjetivo;
            wsResumen.Cell("C11").Value = grasa.PorcPartic;
            wsResumen.Cell("D11").Value = grasa.DifPorc;
            wsResumen.Cell("E11").Value = grasa.KgEntrada;
            wsResumen.Cell("F11").Value = grasa.KgSalida;

            // HUESO
            wsHueso.Cell("A1").Value = "Hueso";
            wsHueso.Cell("A2").Value = "Tipo";
            wsHueso.Cell("B2").Value = "% Objetivo";
            wsHueso.Cell("C2").Value = "% Participación";
            wsHueso.Cell("D2").Value = "% Diferencia";
            wsHueso.Cell("E2").Value = "Kg Entrada";
            wsHueso.Cell("F2").Value = "Kg Salida";

            int filaH = 3;
            foreach (var h in huesos)
            {
                wsHueso.Cell(filaH, 1).Value = h.NombreHueso;
                wsHueso.Cell(filaH, 2).Value = h.PorcObjetivo;
                wsHueso.Cell(filaH, 3).Value = h.PorcPartic;
                wsHueso.Cell(filaH, 4).Value = h.DifPorc;
                wsHueso.Cell(filaH, 5).Value = h.KgEntrada;
                wsHueso.Cell(filaH, 6).Value = h.KgSalida;
                filaH++;
            }

            wsHueso.Cell(filaH, 1).Value = "TOTAL";
            wsHueso.Cell(filaH, 5).Value = totalEntHueso;
            wsHueso.Cell(filaH, 6).Value = totalSalHueso;

            // RECORTE
            wsRecorte.Cell("A1").Value = "Recorte";
            wsRecorte.Cell("A2").Value = "% Objetivo";
            wsRecorte.Cell("B2").Value = "% Participación";
            wsRecorte.Cell("C2").Value = "% Diferencia";
            wsRecorte.Cell("D2").Value = "Kg Entrada";
            wsRecorte.Cell("E2").Value = "Kg Salida";

            wsRecorte.Cell("A3").Value = recorte.PorcObjetivo;
            wsRecorte.Cell("B3").Value = recorte.PorcPartic;
            wsRecorte.Cell("C3").Value = recorte.DifPorc;
            wsRecorte.Cell("D3").Value = recorte.KgEntrada;
            wsRecorte.Cell("E3").Value = recorte.KgSalida;

            // GRASA
            wsGrasa.Cell("A1").Value = "Grasa";
            wsGrasa.Cell("A2").Value = "% Objetivo";
            wsGrasa.Cell("B2").Value = "% Participación";
            wsGrasa.Cell("C2").Value = "% Diferencia";
            wsGrasa.Cell("D2").Value = "Kg Entrada";
            wsGrasa.Cell("E2").Value = "Kg Salida";

            wsGrasa.Cell("A3").Value = grasa.PorcObjetivo;
            wsGrasa.Cell("B3").Value = grasa.PorcPartic;
            wsGrasa.Cell("C3").Value = grasa.DifPorc;
            wsGrasa.Cell("D3").Value = grasa.KgEntrada;
            wsGrasa.Cell("E3").Value = grasa.KgSalida;

            foreach (var ws in wb.Worksheets)
            {
                ws.Columns().AdjustToContents();

                var usedRange = ws.RangeUsed();
                if (usedRange != null)
                {
                    usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                }
            }

            wsResumen.Range("A1:B1").Merge().Style.Font.SetBold().Font.SetFontSize(16);
            wsResumen.Range("A8:F8").Style.Font.SetBold();
            wsHueso.Range("A2:F2").Style.Font.SetBold();
            wsRecorte.Range("A2:E2").Style.Font.SetBold();
            wsGrasa.Range("A2:E2").Style.Font.SetBold();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;

            var nombre = $"Reporte_TIF_{lote}_Prueba_{pruebaid}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                nombre
            );
        }

        [HttpGet("resumen/lote")]
        public async Task<IActionResult> ObtenerResumenPorLote([FromQuery] string lote)
        {
            if (string.IsNullOrWhiteSpace(lote))
                return BadRequest("Lote requerido.");

            var opciones = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var resPruebas = await _http.GetAsync("/Pruebas/ConsultarPruebasTIF");
            if (!resPruebas.IsSuccessStatusCode)
                return BadRequest("No se pudieron consultar las pruebas.");

            var jsonPruebas = await resPruebas.Content.ReadAsStringAsync();
            var pruebas = JsonSerializer.Deserialize<List<PruebaDto>>(jsonPruebas, opciones) ?? new List<PruebaDto>();

            var pruebasLote = pruebas
                .Where(x => string.Equals((x.Lote ?? "").Trim(), lote.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!pruebasLote.Any())
                return NotFound("No se encontraron pruebas para ese lote.");

            var huesosAcumulados = new Dictionary<int, HuesoReporteDto>();
            RecorteReporteDto? recorteTotal = null;
            GrasaReporteDto? grasaTotal = null;

            foreach (var prueba in pruebasLote)
            {
                var resHueso = await _http.GetAsync($"/Hueso/ListarHueso?pruebaid={prueba.Id}");
                var resRecorte = await _http.GetAsync($"/Recorte/ConsultarRecorte?pruebaid={prueba.Id}");
                var resGrasa = await _http.GetAsync($"/Grasa/ListarGrasa?pruebaid={prueba.Id}");

                var jsonHueso = await resHueso.Content.ReadAsStringAsync();
                var jsonRecorte = await resRecorte.Content.ReadAsStringAsync();
                var jsonGrasa = await resGrasa.Content.ReadAsStringAsync();

                var huesos = JsonSerializer.Deserialize<List<HuesoReporteDto>>(jsonHueso, opciones) ?? new List<HuesoReporteDto>();
                var recorte = JsonSerializer.Deserialize<RecorteReporteDto>(jsonRecorte, opciones);
                var grasa = JsonSerializer.Deserialize<GrasaReporteDto>(jsonGrasa, opciones);

                foreach (var h in huesos)
                {
                    if (!huesosAcumulados.ContainsKey(h.Fk_Hueso))
                    {
                        huesosAcumulados[h.Fk_Hueso] = new HuesoReporteDto
                        {
                            Fk_Hueso = h.Fk_Hueso,
                            NombreHueso = h.NombreHueso,
                            PorcObjetivo = h.PorcObjetivo,
                            KgEntrada = 0,
                            KgSalida = 0,
                            PorcPartic = 0,
                            DifPorc = 0
                        };
                    }

                    huesosAcumulados[h.Fk_Hueso].KgEntrada += h.KgEntrada;
                    huesosAcumulados[h.Fk_Hueso].KgSalida += h.KgSalida;
                    huesosAcumulados[h.Fk_Hueso].PorcPartic += h.PorcPartic;
                    huesosAcumulados[h.Fk_Hueso].DifPorc += h.DifPorc;
                }

                if (recorte != null)
                {
                    if (recorteTotal == null)
                    {
                        recorteTotal = new RecorteReporteDto
                        {
                            Fk_Receta = recorte.Fk_Receta,
                            PorcObjetivo = recorte.PorcObjetivo,
                            KgEntrada = 0,
                            KgSalida = 0,
                            PorcPartic = 0,
                            DifPorc = 0
                        };
                    }

                    recorteTotal.KgEntrada += recorte.KgEntrada;
                    recorteTotal.KgSalida += recorte.KgSalida;
                    recorteTotal.PorcPartic += recorte.PorcPartic;
                    recorteTotal.DifPorc += recorte.DifPorc;
                }

                if (grasa != null)
                {
                    if (grasaTotal == null)
                    {
                        grasaTotal = new GrasaReporteDto
                        {
                            PorcObjetivo = grasa.PorcObjetivo,
                            KgEntrada = 0,
                            KgSalida = 0,
                            PorcPartic = 0,
                            DifPorc = 0
                        };
                    }

                    grasaTotal.KgEntrada += grasa.KgEntrada;
                    grasaTotal.KgSalida += grasa.KgSalida;
                    grasaTotal.PorcPartic += grasa.PorcPartic;
                    grasaTotal.DifPorc += grasa.DifPorc;
                }
            }

            return Json(new
            {
                lote = lote,
                totalPruebas = pruebasLote.Count,
                pruebas = pruebasLote.Select(p => new { p.Id, p.Lote, p.Realiza, p.Fecha, p.Hora }).ToList(),
                hueso = huesosAcumulados.Values.OrderBy(x => x.Fk_Hueso).ToList(),
                recorte = recorteTotal ?? new RecorteReporteDto(),
                grasa = grasaTotal ?? new GrasaReporteDto()
            });
        }

        [HttpGet("reportes/excel-lote")]
        public async Task<IActionResult> DescargarReporteExcelLote([FromQuery] string lote)
        {
            if (string.IsNullOrWhiteSpace(lote))
                return BadRequest("Lote inválido.");

            var opciones = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var resPruebas = await _http.GetAsync("/Pruebas/ConsultarPruebasTIF");
            if (!resPruebas.IsSuccessStatusCode)
                return BadRequest("No se pudieron consultar las pruebas.");

            var jsonPruebas = await resPruebas.Content.ReadAsStringAsync();
            var pruebas = JsonSerializer.Deserialize<List<PruebaDto>>(jsonPruebas, opciones) ?? new List<PruebaDto>();

            var pruebasLote = pruebas
                .Where(x => string.Equals((x.Lote ?? "").Trim(), lote.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!pruebasLote.Any())
                return NotFound("No se encontraron pruebas para ese lote.");

            var huesosAcumulados = new Dictionary<int, HuesoReporteDto>();
            RecorteReporteDto? recorteTotal = null;
            GrasaReporteDto? grasaTotal = null;

            foreach (var prueba in pruebasLote)
            {
                var resHueso = await _http.GetAsync($"/Hueso/ListarHueso?pruebaid={prueba.Id}");
                var resRecorte = await _http.GetAsync($"/Recorte/ConsultarRecorte?pruebaid={prueba.Id}");
                var resGrasa = await _http.GetAsync($"/Grasa/ListarGrasa?pruebaid={prueba.Id}");

                var jsonHueso = await resHueso.Content.ReadAsStringAsync();
                var jsonRecorte = await resRecorte.Content.ReadAsStringAsync();
                var jsonGrasa = await resGrasa.Content.ReadAsStringAsync();

                var huesos = JsonSerializer.Deserialize<List<HuesoReporteDto>>(jsonHueso, opciones) ?? new List<HuesoReporteDto>();
                var recorte = JsonSerializer.Deserialize<RecorteReporteDto>(jsonRecorte, opciones);
                var grasa = JsonSerializer.Deserialize<GrasaReporteDto>(jsonGrasa, opciones);

                foreach (var h in huesos)
                {
                    if (!huesosAcumulados.ContainsKey(h.Fk_Hueso))
                    {
                        huesosAcumulados[h.Fk_Hueso] = new HuesoReporteDto
                        {
                            Fk_Hueso = h.Fk_Hueso,
                            NombreHueso = h.NombreHueso,
                            PorcObjetivo = h.PorcObjetivo,
                            KgEntrada = 0,
                            KgSalida = 0,
                            PorcPartic = 0,
                            DifPorc = 0
                        };
                    }

                    huesosAcumulados[h.Fk_Hueso].KgEntrada += h.KgEntrada;
                    huesosAcumulados[h.Fk_Hueso].KgSalida += h.KgSalida;
                    huesosAcumulados[h.Fk_Hueso].PorcPartic += h.PorcPartic;
                    huesosAcumulados[h.Fk_Hueso].DifPorc += h.DifPorc;
                }

                if (recorte != null)
                {
                    if (recorteTotal == null)
                    {
                        recorteTotal = new RecorteReporteDto
                        {
                            Fk_Receta = recorte.Fk_Receta,
                            PorcObjetivo = recorte.PorcObjetivo,
                            KgEntrada = 0,
                            KgSalida = 0,
                            PorcPartic = 0,
                            DifPorc = 0
                        };
                    }

                    recorteTotal.KgEntrada += recorte.KgEntrada;
                    recorteTotal.KgSalida += recorte.KgSalida;
                    recorteTotal.PorcPartic += recorte.PorcPartic;
                    recorteTotal.DifPorc += recorte.DifPorc;
                }

                if (grasa != null)
                {
                    if (grasaTotal == null)
                    {
                        grasaTotal = new GrasaReporteDto
                        {
                            PorcObjetivo = grasa.PorcObjetivo,
                            KgEntrada = 0,
                            KgSalida = 0,
                            PorcPartic = 0,
                            DifPorc = 0
                        };
                    }

                    grasaTotal.KgEntrada += grasa.KgEntrada;
                    grasaTotal.KgSalida += grasa.KgSalida;
                    grasaTotal.PorcPartic += grasa.PorcPartic;
                    grasaTotal.DifPorc += grasa.DifPorc;
                }
            }

            using var wb = new XLWorkbook();

            var wsResumen = wb.Worksheets.Add("Resumen");
            var wsHueso = wb.Worksheets.Add("Hueso");
            var wsRecorte = wb.Worksheets.Add("Recorte");
            var wsGrasa = wb.Worksheets.Add("Grasa");
            var wsPruebas = wb.Worksheets.Add("Pruebas");

            wsResumen.Cell("A1").Value = "Reporte TIF por Lote";
            wsResumen.Cell("A2").Value = "Lote";
            wsResumen.Cell("B2").Value = lote;
            wsResumen.Cell("A3").Value = "Total de pruebas";
            wsResumen.Cell("B3").Value = pruebasLote.Count;

            wsResumen.Cell("A5").Value = "Sección";
            wsResumen.Cell("B5").Value = "% Objetivo";
            wsResumen.Cell("C5").Value = "% Participación";
            wsResumen.Cell("D5").Value = "% Diferencia";
            wsResumen.Cell("E5").Value = "Kg Entrada";
            wsResumen.Cell("F5").Value = "Kg Salida";

            const decimal OBJETIVO_HUESO_TOTAL = 5.5m;

            var huesosLista = huesosAcumulados.Values.OrderBy(x => x.Fk_Hueso).ToList();
            var totalObjHueso = OBJETIVO_HUESO_TOTAL;
            var totalPartHueso = huesosLista.Sum(x => x.PorcPartic);
            var totalDifHueso = totalPartHueso - totalObjHueso;
            var totalEntHueso = huesosLista.Sum(x => x.KgEntrada);
            var totalSalHueso = huesosLista.Sum(x => x.KgSalida);

            wsResumen.Cell("A6").Value = "Hueso Total";
            wsResumen.Cell("B6").Value = totalObjHueso;
            wsResumen.Cell("C6").Value = totalPartHueso;
            wsResumen.Cell("D6").Value = totalDifHueso;
            wsResumen.Cell("E6").Value = totalEntHueso;
            wsResumen.Cell("F6").Value = totalSalHueso;

            wsResumen.Cell("A7").Value = "Recorte";
            wsResumen.Cell("B7").Value = recorteTotal?.PorcObjetivo ?? 0;
            wsResumen.Cell("C7").Value = recorteTotal?.PorcPartic ?? 0;
            wsResumen.Cell("D7").Value = recorteTotal?.DifPorc ?? 0;
            wsResumen.Cell("E7").Value = recorteTotal?.KgEntrada ?? 0;
            wsResumen.Cell("F7").Value = recorteTotal?.KgSalida ?? 0;

            wsResumen.Cell("A8").Value = "Grasa";
            wsResumen.Cell("B8").Value = grasaTotal?.PorcObjetivo ?? 0;
            wsResumen.Cell("C8").Value = grasaTotal?.PorcPartic ?? 0;
            wsResumen.Cell("D8").Value = grasaTotal?.DifPorc ?? 0;
            wsResumen.Cell("E8").Value = grasaTotal?.KgEntrada ?? 0;
            wsResumen.Cell("F8").Value = grasaTotal?.KgSalida ?? 0;

            wsHueso.Cell("A1").Value = "Hueso por lote";
            wsHueso.Cell("A2").Value = "Tipo";
            wsHueso.Cell("B2").Value = "% Objetivo";
            wsHueso.Cell("C2").Value = "% Participación";
            wsHueso.Cell("D2").Value = "% Diferencia";
            wsHueso.Cell("E2").Value = "Kg Entrada";
            wsHueso.Cell("F2").Value = "Kg Salida";

            int filaH = 3;
            foreach (var h in huesosLista)
            {
                wsHueso.Cell(filaH, 1).Value = h.NombreHueso;
                wsHueso.Cell(filaH, 2).Value = h.PorcObjetivo;
                wsHueso.Cell(filaH, 3).Value = h.PorcPartic;
                wsHueso.Cell(filaH, 4).Value = h.DifPorc;
                wsHueso.Cell(filaH, 5).Value = h.KgEntrada;
                wsHueso.Cell(filaH, 6).Value = h.KgSalida;
                filaH++;
            }

            wsHueso.Cell(filaH, 1).Value = "TOTAL";
            wsHueso.Cell(filaH, 5).Value = totalEntHueso;
            wsHueso.Cell(filaH, 6).Value = totalSalHueso;

            wsRecorte.Cell("A1").Value = "Recorte por lote";
            wsRecorte.Cell("A2").Value = "% Objetivo";
            wsRecorte.Cell("B2").Value = "% Participación";
            wsRecorte.Cell("C2").Value = "% Diferencia";
            wsRecorte.Cell("D2").Value = "Kg Entrada";
            wsRecorte.Cell("E2").Value = "Kg Salida";

            wsRecorte.Cell("A3").Value = recorteTotal?.PorcObjetivo ?? 0;
            wsRecorte.Cell("B3").Value = recorteTotal?.PorcPartic ?? 0;
            wsRecorte.Cell("C3").Value = recorteTotal?.DifPorc ?? 0;
            wsRecorte.Cell("D3").Value = recorteTotal?.KgEntrada ?? 0;
            wsRecorte.Cell("E3").Value = recorteTotal?.KgSalida ?? 0;

            wsGrasa.Cell("A1").Value = "Grasa por lote";
            wsGrasa.Cell("A2").Value = "% Objetivo";
            wsGrasa.Cell("B2").Value = "% Participación";
            wsGrasa.Cell("C2").Value = "% Diferencia";
            wsGrasa.Cell("D2").Value = "Kg Entrada";
            wsGrasa.Cell("E2").Value = "Kg Salida";

            wsGrasa.Cell("A3").Value = grasaTotal?.PorcObjetivo ?? 0;
            wsGrasa.Cell("B3").Value = grasaTotal?.PorcPartic ?? 0;
            wsGrasa.Cell("C3").Value = grasaTotal?.DifPorc ?? 0;
            wsGrasa.Cell("D3").Value = grasaTotal?.KgEntrada ?? 0;
            wsGrasa.Cell("E3").Value = grasaTotal?.KgSalida ?? 0;

            wsPruebas.Cell("A1").Value = "Pruebas incluidas";
            wsPruebas.Cell("A2").Value = "Id";
            wsPruebas.Cell("B2").Value = "Lote";
            wsPruebas.Cell("C2").Value = "Usuario";
            wsPruebas.Cell("D2").Value = "Fecha";
            wsPruebas.Cell("E2").Value = "Hora";

            int filaP = 3;
            foreach (var p in pruebasLote)
            {
                wsPruebas.Cell(filaP, 1).Value = p.Id;
                wsPruebas.Cell(filaP, 2).Value = p.Lote;
                wsPruebas.Cell(filaP, 3).Value = p.Realiza;
                wsPruebas.Cell(filaP, 4).Value = p.Fecha;
                wsPruebas.Cell(filaP, 5).Value = p.Hora;
                filaP++;
            }

            foreach (var ws in wb.Worksheets)
            {
                ws.Columns().AdjustToContents();

                var usedRange = ws.RangeUsed();
                if (usedRange != null)
                {
                    usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                }
            }

            wsResumen.Range("A1:B1").Merge().Style.Font.SetBold().Font.SetFontSize(16);
            wsResumen.Range("A5:F5").Style.Font.SetBold();
            wsHueso.Range("A2:F2").Style.Font.SetBold();
            wsRecorte.Range("A2:E2").Style.Font.SetBold();
            wsGrasa.Range("A2:E2").Style.Font.SetBold();
            wsPruebas.Range("A2:E2").Style.Font.SetBold();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;

            var nombre = $"Reporte_TIF_Lote_{lote}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                nombre
            );
        }

        public class HuesoReporteDto
        {
            public int Id { get; set; }
            public int Fk_Hueso { get; set; }
            public string NombreHueso { get; set; } = "";
            public decimal PorcObjetivo { get; set; }
            public decimal KgEntrada { get; set; }
            public decimal KgSalida { get; set; }
            public decimal PorcPartic { get; set; }
            public decimal DifPorc { get; set; }
        }

        public class RecorteReporteDto
        {
            public int Id { get; set; }
            public int Fk_Receta { get; set; }
            public decimal PorcObjetivo { get; set; }
            public decimal KgEntrada { get; set; }
            public decimal KgSalida { get; set; }
            public decimal PorcPartic { get; set; }
            public decimal DifPorc { get; set; }
        }

        public class GrasaReporteDto
        {
            public int Id { get; set; }
            public decimal PorcObjetivo { get; set; }
            public decimal KgEntrada { get; set; }
            public decimal KgSalida { get; set; }
            public decimal PorcPartic { get; set; }
            public decimal DifPorc { get; set; }
        }

        public class PruebaDto
        {
            public int Id { get; set; }
            public string Lote { get; set; } = "";
            public string Fecha { get; set; } = "";
            public string Hora { get; set; } = "";
            public string Realiza { get; set; } = "";
        }
    }
}