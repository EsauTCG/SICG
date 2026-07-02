using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.Services.ControlPrecios;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
// Asegurate de importar tu carpeta de filtros
using Plataforma_CG.Filters;

namespace Plataforma_CG.Controllers
{
    [Authorize]
    public class ControlPreciosController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        private static readonly Dictionary<string, string> MapaCanal =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "ACTIVO", "ACTIVO" },
                { "INACTIVO", "SPOT" },
                { "MAYOREO", "SPOT" },
                { "ESTRATEGICO", "ESTRATEGICO" },
                { "DESARROLLO", "ACTIVO" },
                { "DETALLE", "SPOT" },
            };

        private static string MapDemandaRotacion(int? rotacion) => rotacion switch
        {
            1 => "BAJA",
            2 => "MEDIA",
            3 => "ALTA",
            _ => "BAJA"
        };

        private static string MapCanal(string? clasificacion)
        {
            if (string.IsNullOrWhiteSpace(clasificacion)) return "SPOT";
            var parte = clasificacion.Split('-')[0].Trim();
            return MapaCanal.TryGetValue(parte, out var c) ? c : "SPOT";
        }

        private static string SafeMapCanal(string? clasificacion)
        {
            try
            {
                return MapCanal(clasificacion);
            }
            catch
            {
                return "SPOT";
            }
        }


        private static void CellBody(QuestPDF.Fluent.TableDescriptor table, string? text, bool bold = false)
        {
            var cell = table.Cell().BorderBottom(1).BorderColor("#DDDDDD").Padding(2);
            var txt = cell.Text(text ?? "-").FontSize(6.5f);
            if (bold) txt.Bold();
        }

        public ControlPreciosController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public IActionResult Index() => View();


        [HttpGet]
        public async Task<IActionResult> ObtenerProductos(
    string? sku = null,
    string? producto = null,
    string? vendedor = null,
    string? canal = null,
    string? demanda = null,
    [FromQuery] List<string>? vendedores = null,
    [FromQuery] List<string>? canales = null,
    [FromQuery] List<string>? clientes = null,
    string modoProductos = "todos",
    int topCliente = 20,
    int pagina = 1,
    int tamano = 50)
        {
            var resultado = await ObtenerProductosInternoAsync(
                sku, producto, vendedor, canal, demanda,
                vendedores, canales, clientes,
                modoProductos, topCliente,
                pagina, tamano);

            return Json(new
            {
                datos = resultado.Datos.Select(x => new
                {
                    sku = x.Sku,
                    descripcion = x.Descripcion,
                    demanda = x.Demanda,
                    precioBase = x.PrecioBase,
                    vendedor = x.Vendedor,
                    clientes = (x.Clientes ?? new List<PrecioClienteTag>())
                        .Select(c => new
                        {
                            codigoCliente = c.CodigoCliente,
                            nombre = c.Nombre,
                            canal = c.Canal
                        })
                        .ToList(),
                    precioSpot = x.PrecioSpot == null ? null : new
                    {
                        canal = "SPOT",
                        descPermitido = x.PrecioSpot.Descuento,
                        precioFinal = x.PrecioSpot.PrecioFinal,
                        status = x.PrecioSpot.EsNoVender
                            ? "NO VENDER"
                            : (x.PrecioSpot.Descuento == 0 ? "SIN DESCUENTO" : "PERMITIDO")
                    },
                    precioActivo = x.PrecioActivo == null ? null : new
                    {
                        canal = "ACTIVO",
                        descPermitido = x.PrecioActivo.Descuento,
                        precioFinal = x.PrecioActivo.PrecioFinal,
                        status = x.PrecioActivo.EsNoVender
                            ? "NO VENDER"
                            : (x.PrecioActivo.Descuento == 0 ? "SIN DESCUENTO" : "PERMITIDO")
                    },
                    precioEstrategico = x.PrecioEstrategico == null ? null : new
                    {
                        canal = "ESTRATEGICO",
                        descPermitido = x.PrecioEstrategico.Descuento,
                        precioFinal = x.PrecioEstrategico.PrecioFinal,
                        status = x.PrecioEstrategico.EsNoVender
                            ? "NO VENDER"
                            : (x.PrecioEstrategico.Descuento == 0 ? "SIN DESCUENTO" : "PERMITIDO")
                    }
                }),
                total = resultado.Total,
                pagina,
                tamano,
                totalPaginas = (int)Math.Ceiling((double)resultado.Total / tamano)
            });
        }

        private async Task<PrecioProductoPdfResult> ObtenerProductosInternoAsync(
          string? sku,
          string? producto,
          string? vendedor,
          string? canal,
          string? demanda,
          List<string>? vendedores,
          List<string>? canales,
          List<string>? clientes,
          string modoProductos = "todos",
          int topCliente = 20,
          int pagina = 1,
          int tamano = 50)
        {
            tamano = Math.Min(tamano, 5000);
            pagina = pagina <= 0 ? 1 : pagina;
            topCliente = topCliente <= 0 ? 20 : topCliente;

            var vendedorIdUsuario = await ObtenerVendedorIdUsuarioActualAsync();

            var vendedoresFiltro = (vendedores ?? new List<string>())
                .Append(vendedor)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var canalesFiltro = (canales ?? new List<string>())
                .Append(canal)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!.Trim().ToUpper())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var reglas = await _db.Set<ReglaComercial>()
                .AsNoTracking()
                .ToListAsync();

            var demandaMap = await _db.Set<DemandaProducto>()
                .AsNoTracking()
                .Select(d => new { d.ProductoCodigo, d.Demanda })
                .ToDictionaryAsync(d => d.ProductoCodigo, d => d.Demanda);

            // Clientes permitidos
            List<string>? clientesPermitidos = null;

            if (vendedorIdUsuario.HasValue)
            {
                clientesPermitidos = await _db.ClienteSap
                    .AsNoTracking()
                    .Where(c => c.VendedorId == vendedorIdUsuario.Value)
                    .Select(c => c.Cliente)
                    .Distinct()
                    .ToListAsync();
            }

            var query = _db.CatalogoPrecioSap
                .AsNoTracking()
                .Join(
                    _db.ArticuloSap.AsNoTracking(),
                    c => c.ProductoCodigo,
                    a => a.ProductoCodigo,
                    (c, a) => new
                    {
                        c.ProductoCodigo,
                        a.ProductoNombre,
                        a.Rotacion,
                        c.Precio,
                        c.Cliente
                    });

            if (clientesPermitidos != null)
            {
                if (clientesPermitidos.Count == 0)
                {
                    return new PrecioProductoPdfResult
                    {
                        Datos = new List<PrecioProductoPdfRow>(),
                        Total = 0
                    };
                }

                query = query.Where(x => clientesPermitidos.Contains(x.Cliente));
            }

            if (!string.IsNullOrWhiteSpace(sku))
            {
                var skuTerm = sku.Trim().ToLower();
                query = query.Where(x => x.ProductoCodigo.ToLower().Contains(skuTerm));
            }

            if (!string.IsNullOrWhiteSpace(producto))
            {
                var prodTerm = producto.Trim().ToLower();
                query = query.Where(x => x.ProductoNombre.ToLower().Contains(prodTerm));
            }

            if (clientes != null && clientes.Any())
            {
                var clientesFiltro = clientes
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim())
                    .Distinct()
                    .ToList();

                if (clientesPermitidos != null)
                {
                    clientesFiltro = clientesFiltro
                        .Where(c => clientesPermitidos.Contains(c))
                        .ToList();
                }

                if (clientesFiltro.Any())
                {
                    query = query.Where(x => clientesFiltro.Contains(x.Cliente));
                }
                else if (clientesPermitidos != null)
                {
                    return new PrecioProductoPdfResult
                    {
                        Datos = new List<PrecioProductoPdfRow>(),
                        Total = 0
                    };
                }
            }

            if (vendedoresFiltro.Any())
            {
                var clientesVendedorQuery = _db.ClienteSap
                    .AsNoTracking()
                    .Where(c =>
                        !string.IsNullOrWhiteSpace(c.VendedorNombre) &&
                        vendedoresFiltro.Contains(c.VendedorNombre));

                if (clientesPermitidos != null)
                {
                    clientesVendedorQuery = clientesVendedorQuery
                        .Where(c => clientesPermitidos.Contains(c.Cliente));
                }

                var clientesVendedor = await clientesVendedorQuery
                    .Select(c => c.Cliente)
                    .Distinct()
                    .ToListAsync();

                if (clientesVendedor.Any())
                {
                    query = query.Where(x => clientesVendedor.Contains(x.Cliente));
                }
                else
                {
                    return new PrecioProductoPdfResult
                    {
                        Datos = new List<PrecioProductoPdfRow>(),
                        Total = 0
                    };
                }
            }

            if (canalesFiltro.Any())
            {
                var clientesCanalQuery = _db.ClienteSap
                    .AsNoTracking()
                    .Select(c => new
                    {
                        c.Cliente,
                        c.U_MT_Clasificacion
                    });

                if (clientesPermitidos != null)
                {
                    clientesCanalQuery = clientesCanalQuery
                        .Where(c => clientesPermitidos.Contains(c.Cliente));
                }

                var clientesCanal = await clientesCanalQuery.ToListAsync();

                var clientesDelCanal = clientesCanal
                    .Where(c => canalesFiltro.Contains(MapCanal(c.U_MT_Clasificacion)))
                    .Select(c => c.Cliente)
                    .Distinct()
                    .ToList();

                if (clientesDelCanal.Any())
                {
                    query = query.Where(x => clientesDelCanal.Contains(x.Cliente));
                }
                else
                {
                    return new PrecioProductoPdfResult
                    {
                        Datos = new List<PrecioProductoPdfRow>(),
                        Total = 0
                    };
                }
            }

            var skusGrouped = await query
                .GroupBy(x => new { x.ProductoCodigo, x.ProductoNombre, x.Rotacion })
                .Select(g => new
                {
                    Sku = g.Key.ProductoCodigo,
                    Nombre = g.Key.ProductoNombre,
                    Rotacion = g.Key.Rotacion,
                    PrecioBase = g.Min(x => x.Precio),
                    Clientes = g.Select(x => x.Cliente).Distinct().ToList()
                })
                .ToListAsync();

            var skusConDemanda = skusGrouped
                .Select(x => new
                {
                    Sku = x.Sku,
                    Nombre = x.Nombre,
                    PrecioBase = x.PrecioBase,
                    Clientes = x.Clientes,
                    Demanda = demandaMap.TryGetValue(x.Sku, out var dem)
                        ? dem
                        : MapDemandaRotacion(x.Rotacion)
                })
                .ToList();

            if (!string.IsNullOrWhiteSpace(demanda))
            {
                var demandaFiltro = demanda.Trim().ToUpper();

                if (demandaFiltro != "TODAS")
                {
                    skusConDemanda = skusConDemanda
                        .Where(x => string.Equals(
                            (x.Demanda ?? string.Empty).Trim(),
                            demandaFiltro,
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
            }

            if (string.Equals(modoProductos, "topCliente", StringComparison.OrdinalIgnoreCase))
            {
                var clientesFiltro = (clientes ?? new List<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim())
                    .Distinct()
                    .ToList();

                if (clientesPermitidos != null)
                {
                    clientesFiltro = clientesFiltro
                        .Where(c => clientesPermitidos.Contains(c))
                        .ToList();
                }

                if (clientesFiltro.Count != 1)
                {
                    return new PrecioProductoPdfResult
                    {
                        Datos = new List<PrecioProductoPdfRow>(),
                        Total = 0
                    };
                }

                var clienteCodigo = clientesFiltro.First();

                var topSkusCliente = await _db.Set<VentasHistoricas>()
                    .AsNoTracking()
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.ClienteID) &&
                        !string.IsNullOrWhiteSpace(x.SKU) &&
                        x.ClienteID == clienteCodigo)
                    .GroupBy(x => x.SKU)
                    .Select(g => new
                    {
                        Sku = g.Key,
                        KgVendidos = g.Sum(x => x.Peso)
                    })
                    .OrderByDescending(x => x.KgVendidos)
                    .Take(topCliente)
                    .ToListAsync();

                if (topSkusCliente.Any())
                {
                    var topSkuOrden = topSkusCliente
                        .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                        .Select((x, i) => new
                        {
                            Sku = x.Sku!.Trim(),
                            Pos = i
                        })
                        .ToDictionary(x => x.Sku, x => x.Pos);

                    skusConDemanda = skusConDemanda
                        .Where(x => !string.IsNullOrWhiteSpace(x.Sku) && topSkuOrden.ContainsKey(x.Sku.Trim()))
                        .OrderBy(x => topSkuOrden[x.Sku.Trim()])
                        .ToList();
                }
                else
                {
                    skusConDemanda = skusConDemanda
                        .Where(x => false)
                        .ToList();
                }
            }

            var total = skusConDemanda.Count;

            var paginados = (string.Equals(modoProductos, "topCliente", StringComparison.OrdinalIgnoreCase)
                    ? skusConDemanda
                    : skusConDemanda.OrderBy(x => x.Sku).ToList())
                .Skip((pagina - 1) * tamano)
                .Take(tamano)
                .ToList();

            var codigosCliente = paginados
                .SelectMany(x => x.Clientes)
                .Distinct()
                .ToList();

            var clientesDbQuery = _db.ClienteSap
                .AsNoTracking()
                .Where(c => codigosCliente.Contains(c.Cliente));

            if (clientesPermitidos != null)
            {
                clientesDbQuery = clientesDbQuery
                    .Where(c => clientesPermitidos.Contains(c.Cliente));
            }

            var clientesDb = await clientesDbQuery
                .Select(c => new
                {
                    c.Cliente,
                    c.Nombrecliente,
                    c.U_MT_Clasificacion,
                    c.VendedorNombre
                })
                .ToListAsync();

            var datos = paginados.Select(x =>
            {
                var clientesAsociados = x.Clientes
                    .Where(cc => clientesPermitidos == null || clientesPermitidos.Contains(cc))
                    .Select(cc =>
                    {
                        var cli = clientesDb.FirstOrDefault(c => c.Cliente == cc);

                        return new PrecioClienteTag
                        {
                            CodigoCliente = cc,
                            Cliente = cc,
                            Nombre = cli?.Nombrecliente ?? cc,
                            Canal = SafeMapCanal(cli?.U_MT_Clasificacion)
                        };
                    })
                    .OrderBy(c => c.Nombre)
                    .ToList();

                var primerCliente = clientesDb
                    .FirstOrDefault(c => x.Clientes.Contains(c.Cliente));

                var precioSpotDto = CalcularPrecioCanal(reglas, x.Demanda, "SPOT", x.PrecioBase);
                var precioActivoDto = CalcularPrecioCanal(reglas, x.Demanda, "ACTIVO", x.PrecioBase);
                var precioEstrDto = CalcularPrecioCanal(reglas, x.Demanda, "ESTRATEGICO", x.PrecioBase);

                return new PrecioProductoPdfRow
                {
                    Sku = x.Sku,
                    Descripcion = x.Nombre,
                    Demanda = x.Demanda,
                    PrecioBase = x.PrecioBase,
                    Vendedor = primerCliente?.VendedorNombre ?? "—",
                    Clientes = clientesAsociados,
                    PrecioSpot = new PrecioCanalPdf
                    {
                        Descuento = precioSpotDto.DescPermitido,
                        PrecioFinal = precioSpotDto.PrecioFinal,
                        EsNoVender = string.Equals(precioSpotDto.Status, "NO VENDER", StringComparison.OrdinalIgnoreCase)
                    },
                    PrecioActivo = new PrecioCanalPdf
                    {
                        Descuento = precioActivoDto.DescPermitido,
                        PrecioFinal = precioActivoDto.PrecioFinal,
                        EsNoVender = string.Equals(precioActivoDto.Status, "NO VENDER", StringComparison.OrdinalIgnoreCase)
                    },
                    PrecioEstrategico = new PrecioCanalPdf
                    {
                        Descuento = precioEstrDto.DescPermitido,
                        PrecioFinal = precioEstrDto.PrecioFinal,
                        EsNoVender = string.Equals(precioEstrDto.Status, "NO VENDER", StringComparison.OrdinalIgnoreCase)
                    }
                };
            }).ToList();

            return new PrecioProductoPdfResult
            {
                Datos = datos,
                Total = total
            };
        }

        // -------------------------------------------------------------
        // ENDPOINTS CON NUEVO SISTEMA DE PERMISOS [RevisarPermiso]
        // -------------------------------------------------------------

        [HttpGet]
        [RevisarPermiso("REGLAS_COMERCIALES", "LEER")]
        public async Task<IActionResult> ObtenerReglas()
        {
            var reglas = await _db.Set<ReglaComercial>()
                .AsNoTracking()
                .OrderBy(r => r.Demanda).ThenBy(r => r.Canal)
                .Select(r => new ReglaComercialDto
                {
                    Id = r.Id,
                    Demanda = r.Demanda,
                    Canal = r.Canal,
                    DescuentoMonto = r.DescuentoMonto
                })
                .ToListAsync();

            return Json(reglas);
        }

        [HttpPost]
        [RevisarPermiso("REGLAS_COMERCIALES", "ESCRIBIR")]
        public async Task<IActionResult> GuardarReglas([FromBody] GuardarReglasRequest req)
        {
            if (req?.Reglas is null || !req.Reglas.Any())
                return BadRequest(new { ok = false, mensaje = "Sin reglas recibidas." });

            foreach (var dto in req.Reglas)
            {
                var regla = await _db.Set<ReglaComercial>()
                    .FirstOrDefaultAsync(r => r.Demanda == dto.Demanda && r.Canal == dto.Canal);

                if (regla is null)
                {
                    regla = new ReglaComercial
                    {
                        Demanda = dto.Demanda,
                        Canal = dto.Canal
                    };
                    _db.Set<ReglaComercial>().Add(regla);
                }

                regla.DescuentoMonto = dto.DescuentoMonto;
                regla.FechaModificacion = DateTime.Now;
                regla.ModificadoPor = req.Usuario ?? User.Identity?.Name;
            }

            await _db.SaveChangesAsync();
            return Json(new { ok = true });
        }


        [HttpGet]
        public IActionResult ObtenerAutorizaciones() => Json(new List<AutorizacionDto>());

        [HttpGet]
        public IActionResult ObtenerReporte() => Json(new List<ReporteAutorizacionDto>());

        [HttpGet]
        public async Task<IActionResult> ObtenerFiltros()
        {
            try
            {
                var vendedorId = await ObtenerVendedorIdUsuarioActualAsync();

                var vendedores = await _db.ClienteSap
                    .AsNoTracking()
                    .Where(c => !string.IsNullOrWhiteSpace(c.VendedorNombre))
                    .Select(c => c.VendedorNombre!)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToListAsync();

                var clientesQuery = _db.ClienteSap
                    .AsNoTracking()
                    .Where(c => !string.IsNullOrWhiteSpace(c.Cliente));

                if (vendedorId.HasValue)
                    clientesQuery = clientesQuery.Where(c => c.VendedorId == vendedorId.Value);

                var clientesRaw = await clientesQuery
                    .Select(c => new
                    {
                        c.Cliente,
                        c.Nombrecliente,
                        c.U_MT_Clasificacion
                    })
                    .ToListAsync();

                var clientes = clientesRaw
                    .Select(c => new
                    {
                        cliente = c.Cliente,
                        nombreCliente = string.IsNullOrWhiteSpace(c.Nombrecliente)
                            ? c.Cliente
                            : c.Nombrecliente,
                        canal = SafeMapCanal(c.U_MT_Clasificacion)
                    })
                    .Distinct()
                    .OrderBy(c => c.nombreCliente)
                    .ToList();

                var canales = new[]
                {
            new { valor = "SPOT", nombre = "SPOT" },
            new { valor = "ACTIVO", nombre = "ACTIVO" },
            new { valor = "ESTRATEGICO", nombre = "ESTRATÉGICO" }
        };

                return Json(new { vendedores, clientes, canales });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = true, mensaje = ex.Message });
            }
        }

        private static PrecioCanalDto CalcularPrecioCanal(
            List<ReglaComercial> reglas,
            string demanda,
            string canal,
            decimal pBase)
        {
            var regla = reglas.FirstOrDefault(r =>
                r.Demanda.Equals(demanda, StringComparison.OrdinalIgnoreCase) &&
                r.Canal.Equals(canal, StringComparison.OrdinalIgnoreCase));

            if (regla is null || regla.DescuentoMonto is null)
                return new PrecioCanalDto
                {
                    Canal = canal,
                    Status = "NO VENDER",
                    DescPermitido = null,
                    PrecioFinal = null
                };

            var desc = regla.DescuentoMonto.Value;
            var final = Math.Max(0, pBase - desc);

            return new PrecioCanalDto
            {
                Canal = canal,
                DescPermitido = desc,
                PrecioFinal = final,
                Status = desc == 0 ? "SIN DESCUENTO" : "PERMITIDO"
            };
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerEstadoDemanda()
        {
            var ultimoCalculo = await _db.Set<DemandaProducto>()
                .AsNoTracking()
                .OrderByDescending(d => d.FechaCalculo)
                .Select(d => new
                {
                    d.FechaCalculo,
                    d.FechaDesde,
                    d.FechaHasta,
                    d.PeriodoDias,
                    d.Temporada,
                    d.UmbralBaja,
                    d.UmbralAlta
                })
                .FirstOrDefaultAsync();

            if (ultimoCalculo == null)
                return Json(new { calculado = false });

            var totales = await _db.Set<DemandaProducto>()
                .AsNoTracking()
                .GroupBy(d => d.Demanda)
                .Select(g => new { Demanda = g.Key, Total = g.Count() })
                .ToListAsync();

            return Json(new
            {
                calculado = true,
                fechaCalculo = ultimoCalculo.FechaCalculo.ToString("dd/MM/yyyy HH:mm"),
                fechaDesde = ultimoCalculo.FechaDesde.ToString("dd/MM/yyyy"),
                fechaHasta = ultimoCalculo.FechaHasta.ToString("dd/MM/yyyy"),
                periodoDias = ultimoCalculo.PeriodoDias,
                temporada = ultimoCalculo.Temporada,
                umbralBaja = ultimoCalculo.UmbralBaja,
                umbralAlta = ultimoCalculo.UmbralAlta,
                totalBaja = totales.FirstOrDefault(t => t.Demanda == "BAJA")?.Total ?? 0,
                totalMedia = totales.FirstOrDefault(t => t.Demanda == "MEDIA")?.Total ?? 0,
                totalAlta = totales.FirstOrDefault(t => t.Demanda == "ALTA")?.Total ?? 0,
                totalSkus = totales.Sum(t => t.Total)
            });
        }

        [HttpPost]
        public async Task<IActionResult> RecalcularDemanda([FromBody] RecalcularDemandaRequest? req)
        {
            var periodoDias = req?.PeriodoDias ?? 90;
            var usuario = req?.Usuario ?? User.Identity?.Name ?? "Sistema";

            try
            {
                await _db.Database.ExecuteSqlRawAsync(
                    $"EXEC [dbo].[sp_RecalcularDemanda] @PeriodoDias = {periodoDias}, @CalcPor = N'{usuario}'");

                var resumen = await _db.Set<DemandaProducto>()
                    .AsNoTracking()
                    .GroupBy(d => d.Demanda)
                    .Select(g => new { Demanda = g.Key, Total = g.Count() })
                    .ToListAsync();

                var umbral = await _db.Set<DemandaProducto>()
                    .AsNoTracking()
                    .OrderByDescending(d => d.FechaCalculo)
                    .Select(d => new { d.UmbralBaja, d.UmbralAlta, d.FechaDesde, d.FechaHasta, d.Temporada })
                    .FirstOrDefaultAsync();

                var totalSkus = resumen.Sum(r => r.Total);

                return Json(new
                {
                    ok = true,
                    totalSkus,
                    totalBaja = resumen.FirstOrDefault(r => r.Demanda == "BAJA")?.Total ?? 0,
                    totalMedia = resumen.FirstOrDefault(r => r.Demanda == "MEDIA")?.Total ?? 0,
                    totalAlta = resumen.FirstOrDefault(r => r.Demanda == "ALTA")?.Total ?? 0,
                    umbralBaja = umbral?.UmbralBaja,
                    umbralAlta = umbral?.UmbralAlta,
                    fechaDesde = umbral?.FechaDesde.ToString("dd/MM/yyyy"),
                    fechaHasta = umbral?.FechaHasta.ToString("dd/MM/yyyy"),
                    temporada = umbral?.Temporada,
                    mensaje = $"Demanda recalculada: {totalSkus} SKUs procesados."
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerTopDemanda(string demanda = "ALTA", int top = 20, string? sku = null)
        {
            var query = _db.Set<DemandaProducto>()
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(sku))
            {
                sku = sku.Trim().ToUpper();

                var listaSku = await query
                    .Where(d =>
                        d.ProductoCodigo.ToUpper().Contains(sku) ||
                        (d.ProductoNombre != null && d.ProductoNombre.ToUpper().Contains(sku)))
                    .OrderByDescending(d => d.KgTotales)
                    .Select(d => new
                    {
                        productoCodigo = d.ProductoCodigo,
                        productoNombre = d.ProductoNombre,
                        demanda = d.Demanda,
                        kgTotales = d.KgTotales,
                        cajasTotales = d.Pedidos,
                        fechaDesde = d.FechaDesde,
                        fechaHasta = d.FechaHasta
                    })
                    .ToListAsync();

                return Json(listaSku);
            }

            var demandaFiltro = string.IsNullOrWhiteSpace(demanda) ? "ALTA" : demanda.Trim().ToUpper();

            var lista = await query
                .Where(d => d.Demanda == demandaFiltro)
                .OrderByDescending(d => d.KgTotales)
                .Take(top)
                .Select(d => new
                {
                    productoCodigo = d.ProductoCodigo,
                    productoNombre = d.ProductoNombre,
                    demanda = d.Demanda,
                    kgTotales = d.KgTotales,
                    cajasTotales = d.Pedidos,
                    fechaDesde = d.FechaDesde,
                    fechaHasta = d.FechaHasta
                })
                .ToListAsync();

            return Json(lista);
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerHistorialDemanda(int meses = 12)
        {
            var demandaMap = await _db.Set<DemandaProducto>()
                .AsNoTracking()
                .Select(d => new { d.ProductoCodigo, d.Demanda })
                .ToDictionaryAsync(d => d.ProductoCodigo, d => d.Demanda);

            var fechaDesde = DateTime.Today.AddMonths(-meses);

            var sql = $@"
                SELECT
                    YEAR(FechaVenta)                         AS Anio,
                    MONTH(FechaVenta)                        AS Mes,
                    FORMAT(FechaVenta, 'MMM yyyy', 'es-MX') AS Periodo,
                    SKU                                      AS Sku,
                    SUM(ISNULL(Peso, 0))                    AS KgTotales
                FROM [dbo].[VentasHistoricas]
                WHERE FechaVenta >= '{fechaDesde:yyyy-MM-dd}'
                  AND SKU IS NOT NULL
                  AND SKU <> ''
                  AND ISNULL(Clasificacion, '') NOT LIKE '%SIN CLASIFICACION%'
                GROUP BY
                    YEAR(FechaVenta),
                    MONTH(FechaVenta),
                    FORMAT(FechaVenta, 'MMM yyyy', 'es-MX'),
                    SKU
                ORDER BY Anio, Mes;";

            var filas = await _db.Database
                .SqlQueryRaw<VentaMensualSku>(sql)
                .ToListAsync();

            var resultado = filas
                .GroupBy(f => new { f.Anio, f.Mes, f.Periodo })
                .OrderBy(g => g.Key.Anio)
                .ThenBy(g => g.Key.Mes)
                .Select(g =>
                {
                    decimal kgBaja = 0, kgMedia = 0, kgAlta = 0;
                    int sBaja = 0, sMedia = 0, sAlta = 0;

                    foreach (var fila in g)
                    {
                        var dem = demandaMap.TryGetValue(fila.Sku, out var d) ? d : "BAJA";

                        switch ((dem ?? "BAJA").ToUpper())
                        {
                            case "ALTA":
                                kgAlta += fila.KgTotales;
                                sAlta++;
                                break;
                            case "MEDIA":
                                kgMedia += fila.KgTotales;
                                sMedia++;
                                break;
                            default:
                                kgBaja += fila.KgTotales;
                                sBaja++;
                                break;
                        }
                    }

                    return new HistorialDemandaMes
                    {
                        Anio = g.Key.Anio,
                        Mes = g.Key.Mes,
                        Periodo = g.Key.Periodo,
                        KgBaja = Math.Round(kgBaja, 1),
                        KgMedia = Math.Round(kgMedia, 1),
                        KgAlta = Math.Round(kgAlta, 1),
                        KgTotal = Math.Round(kgBaja + kgMedia + kgAlta, 1),
                        SkusBaja = sBaja,
                        SkusMedia = sMedia,
                        SkusAlta = sAlta
                    };
                })
                .ToList();

            return Json(resultado);
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerHistorialSku(string sku, int meses = 24)
        {
            if (string.IsNullOrWhiteSpace(sku))
                return Json(new List<object>());

            var skuSafe = sku.Trim().Replace("'", "''");
            var fechaDesde = DateTime.Today.AddMonths(-meses);

            var sql = $@"
                SELECT
                    YEAR(FechaVenta)                         AS Anio,
                    MONTH(FechaVenta)                        AS Mes,
                    FORMAT(FechaVenta, 'MMM yyyy', 'es-MX') AS Periodo,
                    '{skuSafe}'                              AS Sku,
                    SUM(ISNULL(Peso, 0))                    AS KgTotales
                FROM [dbo].[VentasHistoricas]
                WHERE SKU = '{skuSafe}'
                  AND FechaVenta >= '{fechaDesde:yyyy-MM-dd}'
                GROUP BY
                    YEAR(FechaVenta),
                    MONTH(FechaVenta),
                    FORMAT(FechaVenta, 'MMM yyyy', 'es-MX')
                ORDER BY Anio, Mes;";

            var filas = await _db.Database
                .SqlQueryRaw<VentaMensualSku>(sql)
                .ToListAsync();

            return Json(filas.Select(f => new
            {
                f.Anio,
                f.Mes,
                f.Periodo,
                kgTotales = Math.Round(f.KgTotales, 1)
            }));
        }

        [HttpGet]
        public async Task<IActionResult> ExportarPdfListaPrecios(
     string? sku = null,
     string? producto = null,
     string? vendedor = null,
     string? canal = null,
     string? demanda = null,
     [FromQuery] List<string>? vendedores = null,
     [FromQuery] List<string>? canales = null,
     [FromQuery] List<string>? clientes = null,
     string modoProductos = "todos",
     int topCliente = 20)
        {
            var resultado = await ObtenerProductosInternoAsync(
                sku, producto, vendedor, canal, demanda,
                vendedores, canales, clientes,
                modoProductos, topCliente,
                pagina: 1, tamano: 5000);

            var model = new ListaPreciosPdfDto
            {
                Empresa = new EmpresaPdfDto
                {
                    NombreComercial = "CARNES G",
                    RazonSocial = "CARNES G SA DE CV",
                    Direccion1 = "Camino Alcala S/N Col. Centro",
                    Direccion2 = "San Juan De Los Lagos Jal.",
                    Telefonos = "TEL: (395) 689 00 50",
                    Celular = "Celular: (81) 0000-0000",
                    VentasTexto = "VENTAS - CARNES G",
                    EmailVentas = "ventas.as@carnesg.net",
                    CertificacionTexto = "CONTAMOS CON CERTIFICACIÓN / PRODUCTO EMPACADO AL ALTO VACÍO",
                    LogoBytes = LeerArchivoWebRoot("images/logoPDF.png"),
                    SelloBytes = LeerArchivoWebRoot("images/logoTIF.png")
                },
                VigenciaTexto = $"VIGENCIA DE PRECIOS: DEL {DateTime.Today:dd} AL {DateTime.Today.AddDays(6):dd} DE {DateTime.Today:MMMM yyyy}".ToUpper(),
                PlantaTexto = "C.G. SAN JUAN DE LOS LAGOS JAL.",
                Grupos = MapearGruposPdf(resultado.Datos)
            };

            var document = new ListaPreciosCarnesgPdf(model);
            var pdf = document.GeneratePdf();

            return File(pdf, "application/pdf", $"ListaPrecios_CarnesG_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        }

        private byte[]? LeerArchivoWebRoot(string rutaRelativa)
        {
            var fullPath = Path.Combine(_env.WebRootPath, rutaRelativa.Replace("/", Path.DirectorySeparatorChar.ToString()));
            return System.IO.File.Exists(fullPath)
                ? System.IO.File.ReadAllBytes(fullPath)
                : null;
        }

        private static void WriteCanalCells(QuestPDF.Fluent.TableDescriptor table, PrecioCanalPdf? canal)
        {
            if (canal == null || canal.EsNoVender)
            {
                CellBody(table, "NO VENDER");
                CellBody(table, "NO VENDER", true);
                return;
            }

            CellBody(table, FormatoMoneda(canal.Descuento));
            CellBody(table, FormatoMoneda(canal.PrecioFinal), true);
        }

        private List<GrupoPreciosPdfDto> MapearGruposPdf(List<PrecioProductoPdfRow> productos)
        {
            return productos
                .GroupBy(x => ObtenerGrupoPdf(x))
                .OrderBy(g => g.Key)
                .Select(g => new GrupoPreciosPdfDto
                {
                    Titulo = g.Key,
                    Items = g.OrderBy(x => x.Sku)
                             .Select(x => new ItemPrecioPdfDto
                             {
                                 Codigo = x.Sku ?? "",
                                 Producto = (x.Descripcion ?? "").ToUpper(),
                                 Precio = x.PrecioBase
                             })
                             .ToList()
                })
                .ToList();
        }

        private string ObtenerGrupoPdf(PrecioProductoPdfRow x)
        {
            var desc = (x.Descripcion ?? "")
                .Trim()
                .ToUpper();

            // Normalizar separadores comunes
            desc = desc.Replace(".", " ")
                       .Replace("-", " ")
                       .Replace("/", " ");

            // Compactar espacios
            while (desc.Contains("  "))
                desc = desc.Replace("  ", " ");

            // PRIORIDAD 1: CERDO
            if (ContienePalabra(desc, "CERDO"))
                return "CERDO";

            // PRIORIDAD 2: NOVILLO / NOV
            if (ContienePalabra(desc, "NOVILLO") || ContienePalabra(desc, "NOV"))
                return "NOVILLO";

            // PRIORIDAD 3:  RES
            if (ContienePalabra(desc, "RES"))
                return "RES";

            // PRIORIDAD 4: VG / VACA GORDA
            if (ContienePalabra(desc, "VACA GORDA") || ContienePalabra(desc, "VG"))
                return "VACA GORDA";

            return "GENERAL";
        }

        private bool ContienePalabra(string texto, string palabra)
        {
            return (" " + texto + " ").Contains(" " + palabra + " ");
        }

        private static PrecioCanalPdf GetCanalPrecio(PrecioProductoPdfRow item, string canal)
        {
            return canal switch
            {
                "SPOT" => item.PrecioSpot ?? new PrecioCanalPdf { EsNoVender = true },
                "ACTIVO" => item.PrecioActivo ?? new PrecioCanalPdf { EsNoVender = true },
                _ => item.PrecioEstrategico ?? new PrecioCanalPdf { EsNoVender = true }
            };
        }

        private static string FormatoMoneda(decimal? value)
        {
            if (value == null) return "-";
            return string.Format(CultureInfo.GetCultureInfo("es-MX"), "{0:C}", value.Value);
        }


        private async Task<int?> ObtenerVendedorIdUsuarioActualAsync()
        {
            var raw = (User?.Identity?.Name ?? "").Trim();

            var usuario = await _db.UsuarioSQL
                .AsNoTracking()
                .Where(u => u.Nombre == raw || u.Usuario == raw)
                .Select(u => new
                {
                    u.EsVendedor,
                    u.VendedorId
                })
                .FirstOrDefaultAsync();

            if (usuario != null && usuario.EsVendedor == true && usuario.VendedorId.HasValue)
                return usuario.VendedorId.Value;

            return null;
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerPerfilUsuario()
        {
            var raw = (User?.Identity?.Name ?? "").Trim();

            var usuarioSql = await _db.UsuarioSQL
                .AsNoTracking()
                .Where(u => u.Nombre == raw || u.Usuario == raw)
                .Select(u => new
                {
                    u.Usuario,
                    u.Nombre,
                    u.EsVendedor,
                    u.VendedorId
                })
                .FirstOrDefaultAsync();

            if (usuarioSql != null && usuarioSql.EsVendedor == true && usuarioSql.VendedorId.HasValue)
            {
                return Json(new
                {
                    tieneVendedor = true,
                    vendedorId = usuarioSql.VendedorId.Value,
                    nombre = string.IsNullOrWhiteSpace(usuarioSql.Nombre) ? raw : usuarioSql.Nombre,
                    usuario = usuarioSql.Usuario
                });
            }

            return Json(new
            {
                tieneVendedor = false,
                vendedorId = (int?)null,
                nombre = raw,
                usuario = raw
            });
        }

        // Endpoint consultado por JavaScript para saber si oculta o muestra botones
        [HttpGet]
        public async Task<IActionResult> ObtenerPermisosReglas()
        {
            var login = (User?.Identity?.Name ?? "").Trim();

            var permiso = await (
                from u in _db.UsuarioSQL
                join p in _db.Perfiles on u.PerfilId equals p.Id
                join ppm in _db.PerfilPermisoModulo on p.Id equals ppm.PerfilId
                join m in _db.ModulosSistema on ppm.ModuloId equals m.Id
                where (u.Usuario == login || u.Nombre == login)
                      && m.Clave == "REGLAS_COMERCIALES"
                      && ppm.Activo
                      && m.Activo
                select new
                {
                    ppm.PuedeLeer,
                    ppm.PuedeEscribir,
                    ppm.PuedeEliminar
                }
            ).FirstOrDefaultAsync();

            if (permiso == null)
            {
                return Json(new { puedeLeer = false, puedeEscribir = false, puedeEliminar = false });
            }

            return Json(new
            {
                puedeLeer = permiso.PuedeLeer,
                puedeEscribir = permiso.PuedeEscribir,
                puedeEliminar = permiso.PuedeEliminar
            });
        }
    }
}