using DocumentFormat.OpenXml.Drawing.Diagrams;
using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Plataforma_CG.AccesoDatos.Operaciones.Planeacion;
using Plataforma_CG.AccesoDatos.Operaciones.Planeacion;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.Models.Operaciones.Planeacion;
using Plataforma_CG.Models.Operaciones.Planeacion.Diaria;
using Plataforma_CG.Models.Operaciones.Planeacion.Extra;
using Plataforma_CG.Models.Operaciones.Planeacion.Semanal;
using Plataforma_CG.Services;
using Plataforma_CG.ViewModels;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using static Plataforma_CG.ViewModels.PlaneadorPdfDto;



namespace Plataforma_CG.Controllers
{
    public class OperacionesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly string _connString;
        private readonly PlaneadorOptions _planeadorOpts;

        public OperacionesController(AppDbContext db, IConfiguration config, IOptions<PlaneadorOptions> planeadorOpts)
        {
            _db = db;
            _connString = config.GetConnectionString("DefaultConnection") ?? "";
            _planeadorOpts = planeadorOpts.Value;
        }

        public async Task<IActionResult> Inyecciones()
        {
            return View("~/Views/Operaciones/Inyecciones.cshtml");
        }

        public async Task<IActionResult> FactorCritico()
        {
            return View("~/Views/Operaciones/FactorCritico.cshtml");
        }

        public async Task<IActionResult> MapaCanales()
        {
            return View("~/Views/Operaciones/MapaCanales.cshtml");
        }

        public async Task<IActionResult> Estudios()
        {
            var modelo = new CanalEstudioViewModel
            {
                TipoGanado = "Novillo Hembra",
                TipoCanal = "C/H",
                FechaEstudio = new DateTime(2025, 8, 8),
                RendimientoTotal = 0.9982,
                MermaChiller = -3.2,
                MermaEstudio = -0.51,
                Productos = new List<ProductoEstudio>
                {
                    new ProductoEstudio { Nombre = "Diezmillo C/H", Kg = 15.38, PorcentajeReal = 0.054, PorcentajeObjetivo = 0.059 },
                    new ProductoEstudio { Nombre = "Paleta C/H", Kg = 25.94, PorcentajeReal = 0.105, PorcentajeObjetivo = 0.103 },
                    new ProductoEstudio { Nombre = "Pescuezo C/H", Kg = 18.90, PorcentajeReal = 0.076, PorcentajeObjetivo = 0.075 },
                    new ProductoEstudio { Nombre = "Pulpa Blanca", Kg = 18.04, PorcentajeReal = 0.073, PorcentajeObjetivo = 0.072 },
                    new ProductoEstudio { Nombre = "T-Bone", Kg = 18.46, PorcentajeReal = 0.055, PorcentajeObjetivo = 0.056 }
                }
            };

            return View("~/Views/Operaciones/Estudios.cshtml", modelo);
        }

        public async Task<IActionResult> PlaneadorProduccion(string plan = "VG")
        {

            //return View("~/Views/Operaciones/PlaneadorProduccion.cshtml", vm);
            return View("~/Views/Operaciones/Planeacion/Mensual.cshtml");
        }
        public IActionResult PlaneacionMensual(string anio, string mes)
        {
            AccesoClasificacionMensual acm = new AccesoClasificacionMensual();
            //return View("~/Views/Operaciones/PlaneadorProduccion.cshtml", vm);
            List<SubClasMensualModel> model = new List<SubClasMensualModel>();
            var lista = acm.ListarSub(anio, mes);
            foreach (var item in lista)
            {
                item.CalcCanales();
                item.CalcTotal();
                item.CalcPromedio();

                model.Add(item);
            }
            return PartialView("~/Views/Operaciones/PlaneadorMensual.cshtml", model);
        }
        [HttpPost]
        public IActionResult GuardarPlanMensual([FromBody] List<PlaneacionMensualModel> model)
        {
            AccesoClasificacionMensual acm = new AccesoClasificacionMensual();
            foreach (var item in model)
            {
                if (item.Id == 0 || string.IsNullOrEmpty(item.Id.ToString()))
                {
                    acm.Guardar(item);
                }
                else
                {
                    acm.Modificar(item);
                }
            }
            return Ok(true);
        }
        [HttpPost]
        public IActionResult GuardarDistribucion([FromBody] List<DistribucionDiaRequest> lista)
        {
            CanalPlaneacionModel cpm;
            foreach (var item in lista)
            {
                foreach (var item2 in item.Distribucion)
                {
                    int pld = apd.ConsultarPlan(item2.Fecha, item2.Clasificacion).PlaneacionId;
                    if (pld == 0)
                    {
                        pld = apd.InsertarPlan(new PlaneacionProduccionModel
                        {
                            FechaPlan = item2.Fecha,
                            TipoPlan = item2.Clasificacion,
                            Notas = "",
                            CreadoPor = User.Identity.Name
                        });
                    }
                    int subc = apd.ConsultarSubId(item2.SubClasificacion);
                    double pprom = apd.ConsultarPesoProm(item2.SubClasificacion);
                    cpm = new CanalPlaneacionModel
                    {
                        PlaneacionId = pld,
                        fk_SubClas = subc,
                        KgCanalCompleta = pprom,
                        NoCanalCompleta = item2.Cantidad

                    };
                    apd.InsertarCanales(cpm);


                }
            }

            return Ok(true);
        }
        [HttpGet]
        public IActionResult ObtenerDistribucionDia(string clasificacion, string fecha)
        {
            var cantidad = apd.ContarTotales(clasificacion, fecha);

            return Ok(cantidad);
        }
        [HttpGet]
        public IActionResult ObtenerResumenMensual(string anio, string mes)
        {
            AccesoClasificacionMensual acm = new AccesoClasificacionMensual();
            List<SubClasMensualModel> model = new List<SubClasMensualModel>();
            var lista = acm.ListarSub(anio, mes);
            foreach (var item in lista)
            {
                item.CalcCanales();
                model.Add(item);
            }
            var resumen = new List<object>();
            foreach (var item in model)
            {
                resumen.Add(new
                {
                    Clasificacion = item.SkuClasificacion,
                    Canales = item.Canales,
                    Detalle = item.DetalleMensual.Select(d => new
                    {
                        d.Id,
                        d.NombreClasificacion,
                        d.Porcentaje
                    })
                });
            }

            return Json(resumen);
        }
        [HttpPost]
        public IActionResult GuardarDistribucionSemanal([FromBody] SemanaClasificacionModel model)
        {
            if (model == null)
                return BadRequest();


            AccesoClasificacionMensual acm = new AccesoClasificacionMensual();
            acm.InsertarSemanaClas(model);

            return Ok();
        }
        [HttpGet]
        public IActionResult ObtenerSemanal(string fechain, string fechafin, string clas)
        {
            AccesoClasificacionMensual acm = new AccesoClasificacionMensual();
            var canales = acm.ContarSemanal(fechain, fechafin, clas);
            return Ok(canales);
        }
        public async Task<IActionResult> PlantillaSemanal(string plan = "NOV", string fechain = "", string fechafin = "")
        {
            AccesoClasificacionMensual acm = new AccesoClasificacionMensual();

            var clas = apd.IdCanal(plan);

            // 🔥 Obtener subclasificaciones reales
            var can = acm.ContarSemanal(fechain, fechafin, plan);

            // 🔥 Mapear correctamente
            var clasificaciones = can.Select(x => new DetalleClasificacionSemana
            {
                fk_SubClas = x.SubClasificacionId,   // 👈 CLAVE
                Clasificacion = x.SubClasificacion,
                TotalCanales = x.Canales,
                PesoPromedio = x.PesoPromedio
            }).ToList();

            var canales = new List<SemanaClasificacionModel>
    {
        new SemanaClasificacionModel
        {
            FechaInicioSemana = DateTime.Parse(fechain),
            FechaFinSemana = DateTime.Parse(fechafin),
            Clasificaciones = clasificaciones
        }
    };

            var model = new TotalSemanalModel
            {
                FechaIn = fechain,
                FechaFin = fechafin,
                Canales = canales,
                Participaciones = apd.ListarParticipacionSem(clas, fechain, fechafin),
                TipoPlan = plan
            };

            return PartialView("~/Views/Operaciones/Planeacion/Semanal.cshtml", model);
        }
        AccesoPlanDiarios apd = new AccesoPlanDiarios();
        AccesoPlanExtra ape = new AccesoPlanExtra();
        public async Task<IActionResult> PlaneacionDia(string plan = "NOV", string fecha = "")
        {
            TotalDiarioModel model = new TotalDiarioModel();
            var can = apd.ConsultarPlan(fecha, plan);
            can.TipoPlan = plan;
            AccesoClasificacionMensual acm = new AccesoClasificacionMensual();
            DateTime fc = Convert.ToDateTime(fecha);

            if (can.PlaneacionId == 0)
            {
                can.PlaneacionId = apd.InsertarPlan(new PlaneacionProduccionModel
                {
                    FechaPlan = fecha,
                    TipoPlan = plan,
                    Notas = "",
                    CreadoPor = User.Identity.Name
                });
                apd.InsertarCanCero(can);
            }
            try
            {
                if (can.PlaneacionId != 0)
                {
                    var asi = apd.ListarCanales(can.PlaneacionId);
                    if (asi.Count == 0)
                    {
                        apd.InsertarCanCero(can);
                        asi = apd.ListarCanales(can.PlaneacionId);
                    }
                    var clas = apd.IdCanal(plan);
                    model = new TotalDiarioModel
                    {
                        Fecha = fecha,
                        Canales = asi,
                        Participaciones = apd.ListarParticipacion(clas, can.PlaneacionId),
                        TipoPlan = plan
                    };

                }
            }
            catch (Exception)
            {
            }

            return PartialView("~/Views/Operaciones/PlaneadorProduccion.cshtml", model);
            //return View("~/Views/Operaciones/Planeacion/Mensual.cshtml");
        }

        [HttpPost]
        public IActionResult ObtenerInyecciones([FromBody] List<string> productos)
        {
            var resultado = productos
                .Distinct()
                .Select(x => new
                {
                    ProductoCodigo = x,
                    Iny = ape.CalcularPlan(x)
                })
                .ToList();

            return Json(resultado);
        }
        [HttpGet]
        public IActionResult ObtenerInyeccion(string sku)
        {
            var resultado = new
            {
                ProductoCodigo = sku,
                Iny = ape.CalcularPlan(sku)
            };
                

            return Json(resultado);
        }
        [HttpPost]
        public IActionResult ObtenerEtiquetacion([FromBody] List<string> productos)
        {

            var resultado = productos
                .Distinct()
                .Select(x => new
                {
                    ProductoCodigo = x,
                    Etiq = ape.ConsultarEtiquetas(x)
                })
                .ToList();

            return Json(resultado);
        }
        
        [HttpGet]
        public IActionResult ObtenerEtiquetacionSku( string sku)
        {
            var resultado = new
            {
                ProductoCodigo = sku,
                Etiq = ape.ConsultarEtiquetas(sku)
            };
            return Json(resultado);
        }
        [HttpGet]
        public IActionResult ObtenerConversiones(string sku)
        {

            var resultado =ape.ObtenerCatalogoConversion(sku);

            return Json(resultado);
        }

        [HttpGet]
        public IActionResult ObtenerDatosProcesoInyeccion(string sku)
        {
            sku = (sku ?? "").Trim();

            if (string.IsNullOrWhiteSpace(sku))
            {
                return Json(new
                {
                    ok = false,
                    message = "SKU vacío."
                });
            }

            object? inyRaw = null;
            object? etiqRaw = null;
            object? convRaw = null;

            try
            {
                inyRaw = ape.CalcularPlan(sku);
            }
            catch { }

            try
            {
                etiqRaw = ape.ConsultarEtiquetas(sku);
            }
            catch { }

            try
            {
                convRaw = ape.ObtenerCatalogoConversion(sku);
            }
            catch { }

            var iny = PrimerRegistro(inyRaw);
            var etiq = PrimerRegistro(etiqRaw);
            var conv = PrimerRegistro(convRaw);

            string producto = PrimeroConValor(
                TomarValor(iny, "nombre", "Nombre", "producto", "Producto"),
                TomarValor(conv, "productoNombre", "ProductoNombre", "nombre", "Nombre"),
                TomarValor(etiq, "nombre", "Nombre", "producto", "Producto")
            );

            string porcentaje = PrimeroConValor(
                TomarValor(iny, "porcentaje", "Porcentaje")
            );

            string modo = PrimeroConValor(
                TomarValor(iny, "tipo", "Tipo", "modo", "Modo")
            );

            string etiquetacion = PrimeroConValor(
                TomarValor(etiq, "etiquetacion", "Etiquetacion", "etiquetación", "Etiquetación")
            );

            string diasCaducidad = PrimeroConValor(
                TomarValor(etiq, "diasCaducidad", "DiasCaducidad", "díasCaducidad", "DíasCaducidad")
            );

            string skuDestino = PrimeroConValor(
                TomarValor(conv, "skuDestino", "SkuDestino", "SKUDestino")
            );

            return Json(new
            {
                ok = true,

                sku = sku,
                skuDestino = skuDestino,

                producto = producto,
                porcentaje = porcentaje,
                modo = modo,

                etiquetacion = etiquetacion,
                diasCaducidad = diasCaducidad,

                velocidad = "",
                presion = "",
                altura = "",
                avance = "",
                tara = ""
            });
        }

        private static object? PrimerRegistro(object? source)
        {
            if (source == null)
                return null;

            if (source is string)
                return source;

            if (source is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    return item;

                return null;
            }

            return source;
        }

        private static string PrimeroConValor(params string?[] values)
        {
            foreach (var value in values)
            {
                var txt = LimpiarTexto(value);

                if (!string.IsNullOrWhiteSpace(txt))
                    return txt;
            }

            return "";
        }

        private static string LimpiarTexto(object? value)
        {
            if (value == null || value == DBNull.Value)
                return "";

            var txt = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? "";

            if (txt.Equals("undefined", StringComparison.OrdinalIgnoreCase))
                return "";

            if (txt.Equals("null", StringComparison.OrdinalIgnoreCase))
                return "";

            return txt;
        }

        private static string TomarValor(object? source, params string[] nombres)
        {
            if (source == null)
                return "";

            source = PrimerRegistro(source);

            if (source == null)
                return "";

            if (source is System.Data.DataRow row)
            {
                foreach (var nombre in nombres)
                {
                    foreach (System.Data.DataColumn col in row.Table.Columns)
                    {
                        if (string.Equals(col.ColumnName, nombre, StringComparison.OrdinalIgnoreCase))
                            return LimpiarTexto(row[col]);
                    }
                }

                return "";
            }

            if (source is System.Collections.IDictionary dict)
            {
                foreach (var nombre in nombres)
                {
                    foreach (var key in dict.Keys)
                    {
                        if (string.Equals(Convert.ToString(key), nombre, StringComparison.OrdinalIgnoreCase))
                            return LimpiarTexto(dict[key]);
                    }
                }

                return "";
            }

            var tipo = source.GetType();
            var props = tipo.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var nombre in nombres)
            {
                foreach (var prop in props)
                {
                    if (string.Equals(prop.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    {
                        var value = prop.GetValue(source);
                        return LimpiarTexto(value);
                    }
                }
            }

            return "";
        }


        [HttpGet]
        public IActionResult DebugDatosProcesoInyeccion(string sku)
        {
            sku = (sku ?? "").Trim();

            object? inyRaw = null;
            object? etiqRaw = null;
            object? convRaw = null;

            try
            {
                inyRaw = ape.CalcularPlan(sku);
            }
            catch (Exception ex)
            {
                inyRaw = new { error = ex.Message };
            }

            try
            {
                etiqRaw = ape.ConsultarEtiquetas(sku);
            }
            catch (Exception ex)
            {
                etiqRaw = new { error = ex.Message };
            }

            try
            {
                convRaw = ape.ObtenerCatalogoConversion(sku);
            }
            catch (Exception ex)
            {
                convRaw = new { error = ex.Message };
            }

            return Json(new
            {
                sku,
                inyRaw,
                etiqRaw,
                convRaw
            });
        }


        [HttpPost]
        public IActionResult GuardarPlanDiario([FromBody] List<PlanDiarioModel> lista)
        {
            if (lista == null || !lista.Any())
                return BadRequest("Lista vacía");
            apd.borrarPlanDiario(lista[0].PlaneacionId);
            int planid = lista[0].PlaneacionId;
            foreach (var item in lista)
            {
                if (item.PlaneacionId==0)
                {
                    item.PlaneacionId = planid;
                }
                apd.InsertarPlanDiario(item);
                foreach (var item2 in item.Participaciones)
                {
                    if (item2.PlanId==0)
                    {
                        item2.PlanId = planid;
                    }
                    apd.InsertarSubDia(item2);
                }
            }

            return Json(new { success = true });
        }
        [HttpGet]
        public IActionResult ObtenerSolicitudes(string fecha)
        {
            var lista = ape.ObtenerSolicitudes(fecha);
            return Json(lista);
        }
        [HttpGet]
        public IActionResult ObtenerSolicitudDetalle(string id)
        {
            var lista = ape.ObtenerDetalleSolicitud(id);
            return Json(lista);
        }
        [HttpGet]
        public IActionResult ObtenerTiposSolicitud()
        {
            var lista = ape.ObtenerTipoSolicitud();
            return Json(lista);
        }
        [HttpGet]
        public IActionResult ObtenerSolicitudSkus()
        {
            var lista = ape.ObtenerSolicitudSKUs();
            return Json(lista);
        }
        [HttpPost]
        public IActionResult GuardarSolicitud(
    [FromBody] SolicitudGuardarModel model
)
        {
            if (model == null)
            {
                return BadRequest(
                    "Modelo vacío"
                );
            }

            if (
                model.Productos == null ||
                !model.Productos.Any()
            )
            {
                return BadRequest(
                    "No hay productos"
                );
            }

            try
            {
                // Ejemplo:
                // guardar encabezado

                string solicitudId = ape.CrearSolicitud(model.Fecha);
                    ape.InsertarSolicitud(
                        solicitudId,"2",model.TipoId
                    );
                ape.InsertarSolicitud(
                        solicitudId, "3", model.TipoNombre
                    );
                ape.InsertarSolicitud(
                        solicitudId, "15", model.Comentarios
                    );

                // guardar detalle

                foreach (var item in model.Productos.Where(i=>i.Cantidad>0))
                {
                    ape.InsertarSolicitudDetalle(
                        solicitudId,
                        item.SKU,
                        item.Cantidad
                    );
                }

                return Json(new
                {
                    success = true
                });
            }
            catch (Exception ex)
            {
                return BadRequest(
                    ex.Message
                );
            }
        }
        [HttpPost]
        [Route("Operaciones/PlaneadorProduccionPdf")]
        public IActionResult PlaneadorProduccionPdf([FromBody] PlaneadorPdfRequest req)
        {
            if (req == null || req.Rows == null) return BadRequest();
            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "logoPDF.png");
            byte[]? logoBytes = System.IO.File.Exists(logoPath) ? System.IO.File.ReadAllBytes(logoPath) : null;

            var pdfBytes = PlaneadorPdfBuilder.Build(req, logoBytes);

            var safePlan = string.IsNullOrWhiteSpace(req.PlanTexto) ? "Plan" : req.PlanTexto;
            var fileName = $"Plan_{safePlan}_{req.Mode}_{DateTime.Now:yyyyMMdd}.pdf".Replace(" ", "_");

            return File(pdfBytes, "application/pdf", fileName);
        }
        [HttpGet]
        public IActionResult SkuSem(string sku, string fechaIn, string fechaFin)
        {
            var cantidad = apd.SkuSemanal(sku, fechaIn, fechaFin);

            return Ok(cantidad);
        }
        [HttpPost]
        public IActionResult GuardarPlanSemanal(
    [FromBody] List<PlanSemanalDetalle> lista)
        {
            try
            {
                foreach (var item in lista)
                {
                    apd.BorrarDetalleSemanal(item);
                    apd.InsertarSemanal(item);
                }
            }
            catch (Exception)
            {
            }
            return Ok(new { ok = true });
        }

        [HttpGet]
        public IActionResult ObtenerCatalogoExtra()
        {
            var lista = ape.ObtenerProductosExtra();

            return Json(lista);
        }
        [HttpGet]
        public IActionResult ObtenerEstatusSolicitud()
        {
            var res=ape.ConsultarEstatusSolicitud();
            return Json(res);
        }
        [HttpPost]
        [Route("Operaciones/PlaneadorProduccionGuardar")]
        public async Task<IActionResult> PlaneadorProduccionGuardar([FromBody] PlaneadorSaveDto dto)
        {
            if (dto == null || dto.Rows == null || dto.Rows.Count == 0)
                return BadRequest("No hay renglones para guardar.");

            // ✅ Normaliza tipo plan (AHORA soporta 4)
            var tipo = (dto.TipoPlan ?? "").Trim().ToUpperInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "VG", "NOV", "VR", "SUB" };

            if (!allowed.Contains(tipo))
                return BadRequest("TipoPlan inválido. Valores permitidos: VG, NOV, VR, SUB.");

            dto.TipoPlan = tipo;

            if (string.IsNullOrWhiteSpace(_connString))
                return StatusCode(500, "No hay ConnectionString configurado (DefaultConnection).");

            // ✅ Validar programación
            if (!dto.ProgramacionId.HasValue || dto.ProgramacionId.Value <= 0)
                return BadRequest("Selecciona una programación antes de guardar.");

            var prog = _planeadorOpts.Programaciones
                .FirstOrDefault(x => x.Id == dto.ProgramacionId.Value);

            if (prog == null)
                return BadRequest("La programación seleccionada no es válida.");

            // NO confiar en lo que mande el front, lo tomamos de appsettings
            dto.NombreProgramacion = prog.Nombre ?? "";

            decimal ParsePct01(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return 0m;
                s = s.Replace("%", "").Trim();

                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d / 100m;
                if (decimal.TryParse(s, NumberStyles.Any, new CultureInfo("es-MX"), out d)) return d / 100m;
                return 0m;
            }

            decimal ParseDec(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return 0m;

                // quita separador de miles (si viene "1,234.56")
                s = s.Replace(",", "");

                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
                if (decimal.TryParse(s, NumberStyles.Any, new CultureInfo("es-MX"), out d)) return d;
                return 0m;
            }

            int ParseInt(string s)
            {
                if (int.TryParse((s ?? "").Trim(), out var i)) return i;
                return 0;
            }

            byte ParseByte(string s)
            {
                if (byte.TryParse((s ?? "").Trim(), out var b)) return b;
                return 0;
            }

            await using var cn = new SqlConnection(_connString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                // =========================================================
                // ✅ VALIDAR DUPLICADO (antes de insertar)
                // =========================================================
                var sqlExists = @"
SELECT TOP 1 PlaneacionId
FROM dbo.PlaneacionProduccion
WHERE FechaPlan = @FechaPlan
  AND TipoPlan  = @TipoPlan
  AND Estatus   = 'BORRADOR';";

                int? existenteId = null;

                await using (var cmdE = new SqlCommand(sqlExists, cn, tx))
                {
                    cmdE.Parameters.Add("@FechaPlan", SqlDbType.Date).Value = dto.FechaPlan.Date;
                    cmdE.Parameters.Add("@TipoPlan", SqlDbType.VarChar, 10).Value = dto.TipoPlan;

                    var obj = await cmdE.ExecuteScalarAsync();
                    if (obj != null && obj != DBNull.Value)
                        existenteId = Convert.ToInt32(obj);
                }

                if (existenteId.HasValue)
                {
                    await tx.RollbackAsync();
                    return BadRequest($"Ya existe una planeación BORRADOR para {dto.FechaPlan:yyyy-MM-dd} ({dto.TipoPlan}). Folio: {existenteId.Value}");
                }

                // =========================================================
                // 1) Encabezado
                // =========================================================
                var sqlHead = @"
INSERT INTO dbo.PlaneacionProduccion
(FechaPlan, TipoPlan, Estatus, Version, Notas, ProgramacionId, NombreProgramacion, CreadoPor, FechaCreacion)
VALUES
(@FechaPlan, @TipoPlan, @Estatus, @Version, @Notas, @ProgramacionId, @NombreProgramacion, @CreadoPor, SYSDATETIME());

SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int planeacionId;

                await using (var cmd = new SqlCommand(sqlHead, cn, tx))
                {
                    cmd.Parameters.Add("@FechaPlan", SqlDbType.Date).Value = dto.FechaPlan.Date;
                    cmd.Parameters.Add("@TipoPlan", SqlDbType.VarChar, 10).Value = dto.TipoPlan;
                    cmd.Parameters.Add("@Estatus", SqlDbType.VarChar, 20).Value = "BORRADOR";
                    cmd.Parameters.Add("@Version", SqlDbType.Int).Value = 1;

                    cmd.Parameters.Add("@Notas", SqlDbType.NVarChar, 400).Value =
                        string.IsNullOrWhiteSpace(dto.PlanTexto) ? (object)DBNull.Value : dto.PlanTexto;

                    cmd.Parameters.Add("@ProgramacionId", SqlDbType.Int).Value = dto.ProgramacionId.Value;

                    cmd.Parameters.Add("@NombreProgramacion", SqlDbType.NVarChar, 120).Value =
                        string.IsNullOrWhiteSpace(dto.NombreProgramacion) ? (object)DBNull.Value : dto.NombreProgramacion;

                    cmd.Parameters.Add("@CreadoPor", SqlDbType.NVarChar, 80).Value =
                        (object?)(User?.Identity?.Name ?? "SYS") ?? DBNull.Value;

                    var idObj = await cmd.ExecuteScalarAsync();
                    planeacionId = Convert.ToInt32(idObj);
                }

                // =========================================================
                // 2) Líneas
                // =========================================================
                var sqlLine = @"
INSERT INTO dbo.PlaneacionProduccionLinea
(PlaneacionId, GroupKey, Nivel, Orden,
 SkuDeshuese, SkuInyeccion,
 VG1, VG2, VR, RendPct,
 Etiquetado, Almacen,
 KgLoteCalc, CanalesCalc, SubtotalCalc, PiezasCalc,
 Observaciones)
VALUES
(@PlaneacionId, @GroupKey, @Nivel, @Orden,
 @SkuDeshuese, @SkuInyeccion,
 @VG1, @VG2, @VR, @RendPct,
 @Etiquetado, @Almacen,
 @KgLoteCalc, @CanalesCalc, @SubtotalCalc, @PiezasCalc,
 @Observaciones);";

                int ordenFallback = 1;

                foreach (var r in dto.Rows)
                {
                    await using var cmdL = new SqlCommand(sqlLine, cn, tx);

                    cmdL.Parameters.Add("@PlaneacionId", SqlDbType.Int).Value = planeacionId;

                    // ✅ GroupKey ideal: MasterSku (dataset.group) / si no viene, usa desSku
                    var gk = (r.GroupKey ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(gk)) gk = (r.DesSku ?? "").Trim();

                    cmdL.Parameters.Add("@GroupKey", SqlDbType.VarChar, 60).Value =
                        string.IsNullOrWhiteSpace(gk) ? (object)DBNull.Value : gk;

                    // ✅ Nivel/Orden: si vienen desde front, úsalos; si no, fallback
                    var nivel = r.Nivel.HasValue ? (byte)Math.Max(0, Math.Min(255, r.Nivel.Value)) : (byte)0;
                    var ord = r.Orden.HasValue && r.Orden.Value > 0 ? r.Orden.Value : ordenFallback++;

                    cmdL.Parameters.Add("@Nivel", SqlDbType.TinyInt).Value = nivel;
                    cmdL.Parameters.Add("@Orden", SqlDbType.Int).Value = ord;

                    cmdL.Parameters.Add("@SkuDeshuese", SqlDbType.VarChar, 30).Value =
                        string.IsNullOrWhiteSpace(r.DesSku) ? (object)DBNull.Value : r.DesSku;

                    cmdL.Parameters.Add("@SkuInyeccion", SqlDbType.VarChar, 30).Value =
                        string.IsNullOrWhiteSpace(r.InySku) ? (object)DBNull.Value : r.InySku;

                    cmdL.Parameters.Add("@VG1", SqlDbType.Decimal).Value = ParseDec(r.Col1);
                    cmdL.Parameters["@VG1"].Precision = 5; cmdL.Parameters["@VG1"].Scale = 4;

                    cmdL.Parameters.Add("@VG2", SqlDbType.Decimal).Value = ParseDec(r.Col2);
                    cmdL.Parameters["@VG2"].Precision = 5; cmdL.Parameters["@VG2"].Scale = 4;

                    cmdL.Parameters.Add("@VR", SqlDbType.Decimal).Value = ParseDec(r.Col3);
                    cmdL.Parameters["@VR"].Precision = 5; cmdL.Parameters["@VR"].Scale = 4;

                    cmdL.Parameters.Add("@RendPct", SqlDbType.Decimal).Value = ParsePct01(r.RendPct);
                    cmdL.Parameters["@RendPct"].Precision = 9; cmdL.Parameters["@RendPct"].Scale = 6;

                    cmdL.Parameters.Add("@Etiquetado", SqlDbType.NVarChar, 60).Value =
                        string.IsNullOrWhiteSpace(r.Etiquetado) ? (object)DBNull.Value : r.Etiquetado;

                    cmdL.Parameters.Add("@Almacen", SqlDbType.NVarChar, 60).Value =
                        string.IsNullOrWhiteSpace(r.Almacen) ? (object)DBNull.Value : r.Almacen;

                    cmdL.Parameters.Add("@KgLoteCalc", SqlDbType.Decimal).Value = ParseDec(r.KgLote);
                    cmdL.Parameters["@KgLoteCalc"].Precision = 18; cmdL.Parameters["@KgLoteCalc"].Scale = 2;

                    cmdL.Parameters.Add("@CanalesCalc", SqlDbType.Int).Value = ParseInt(r.Canales);

                    cmdL.Parameters.Add("@SubtotalCalc", SqlDbType.Decimal).Value = ParseDec(r.Subtotal);
                    cmdL.Parameters["@SubtotalCalc"].Precision = 18; cmdL.Parameters["@SubtotalCalc"].Scale = 2;

                    cmdL.Parameters.Add("@PiezasCalc", SqlDbType.Int).Value = ParseInt(r.Piezas);

                    cmdL.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 400).Value =
                        string.IsNullOrWhiteSpace(r.Observaciones) ? (object)DBNull.Value : r.Observaciones;

                    await cmdL.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                return Json(new { ok = true, planeacionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                if (ex is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
                    return BadRequest($"Ya existe una planeación BORRADOR para {dto.FechaPlan:yyyy-MM-dd} ({dto.TipoPlan}).");

                return StatusCode(500, ex.Message);
            }
        }
    }
}
