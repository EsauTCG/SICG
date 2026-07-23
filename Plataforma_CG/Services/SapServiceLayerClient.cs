using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Plataforma_CG.Data;
using Plataforma_CG.Models;
using Plataforma_CG.ViewModels;
using System;
using System.Data;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using static System.Net.WebRequestMethods;

namespace Plataforma_CG.Services
{
    public class SapServiceLayerClient : ISapServiceLayerClient
    {

        private readonly AppDbContext _context; // <- Aquí es donde se define
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        //public SapServiceLayerClient(HttpClient httpClient, IConfiguration config)
        //{
        //    _httpClient = httpClient;
        //    _config = config;

        //}


        public async Task<(bool ok, string? error)> EnsureLoginAsync()
        {
            try
            {
                if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                    await LoginAsync();

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.GetBaseException().Message);
            }
        }

        public async Task<(bool ok, string response, string? error)> PostJsonAsync(string relativeUrl, string json)
        {
            try
            {
                var (ok, err) = await EnsureLoginAsync();
                if (!ok)
                    return (false, "", err);

                // Si llega "/DeliveryNotes" => "DeliveryNotes"
                var url = (relativeUrl ?? "").Trim();
                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    url = url.TrimStart('/');

                var content = new StringContent(json ?? "", Encoding.UTF8, "application/json");

                var resp = await _httpClient.PostAsync(url, content);

                // Si expiró sesión, reintenta 1 vez
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await LoginAsync();
                    content = new StringContent(json ?? "", Encoding.UTF8, "application/json");
                    resp = await _httpClient.PostAsync(url, content);
                }

                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return (false, body, $"SAP SL {(int)resp.StatusCode} {resp.ReasonPhrase}");

                return (true, body, null);
            }
            catch (Exception ex)
            {
                return (false, "", ex.GetBaseException().Message);
            }
        }




        public SapServiceLayerClient(HttpClient httpClient, IConfiguration config, AppDbContext context)
        {
            _httpClient = httpClient;
            _config = config;
            _context = context;

        }

        // =========================
        // 🔹 Login al Service Layer
        // =========================
        public async Task LoginAsync()
        {
            var settings = _config.GetSection("SapServiceLayer");
            var payload = new
            {
                UserName = settings["UserName"],
                Password = settings["Password"],
                CompanyDB = settings["CompanyDB"]
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{settings["BaseUrl"]}/Login", content);

            response.EnsureSuccessStatusCode();

            //var cookies = response.Headers.GetValues("Set-Cookie");
            //var sessionCookie = cookies.FirstOrDefault(c => c.StartsWith("B1SESSION"));

            //if (!string.IsNullOrEmpty(sessionCookie))
            //{
            //    var sessionId = sessionCookie.Split(';')[0];
            //    _httpClient.DefaultRequestHeaders.Remove("Cookie"); // Limpia cookies previas
            //    _httpClient.DefaultRequestHeaders.Add("Cookie", sessionId);
            //}

            var cookies = response.Headers.GetValues("Set-Cookie");
            var sessionCookie = cookies.FirstOrDefault(c => c.StartsWith("B1SESSION"));
            var routeCookie = cookies.FirstOrDefault(c => c.StartsWith("ROUTEID"));

            if (!string.IsNullOrEmpty(sessionCookie))
            {
                var sessionId = sessionCookie.Split(';')[0];
                _httpClient.DefaultRequestHeaders.Remove("Cookie");
                _httpClient.DefaultRequestHeaders.Add("Cookie", sessionId + (routeCookie != null ? "; " + routeCookie.Split(';')[0] : ""));
            }

        }

        private async Task<HttpResponseMessage> GetWithReLoginAsync(string url)
        {
            var response = await _httpClient.GetAsync(url); // <- llamamos al HttpClient, no a nosotros mismos
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await LoginAsync();
                response = await _httpClient.GetAsync(url);
            }
            return response;
        }


        //======================================
        // OBTIENE CLIENTES (BUSINESSPARTNERS) DE SAP
        //======================================

  
        public async Task<List<ClienteViewModel>> ObtenerTodosClientesAsync()
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var settings = _config.GetSection("SapServiceLayer");
            var clientes = new List<ClienteViewModel>();
            int skip = 0;
            int batchSize = 1; // tamaño máximo recomendado por SAP
            bool moreRecords = true;

            while (moreRecords)
            {
                var url = $"{settings["BaseUrl"]}/BusinessPartners?$filter=CardType eq 'C'&$select=CardCode,CardName&$top={batchSize}&$skip={skip}";
                //var response = await _httpClient.GetAsync(url);
                var response = await GetWithReLoginAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("value", out var value))
                    break;

                var batch = value.EnumerateArray()
                    .Select(x => new ClienteViewModel
                    {
                        CardCode = x.GetProperty("CardCode").GetString(),
                        CardName = x.GetProperty("CardName").GetString()
                    })
                    .ToList();

                if (!batch.Any())
                    break;

                clientes.AddRange(batch);
                skip += batch.Count;

                moreRecords = batch.Count == batchSize;
            }

            return clientes;
        }

        //======================================
        // BUSQUEDA FILTRADA POR NOMBRE PARA AUTOCOMPLETADO
        //======================================

        //https://172.120.80.3:50000/b1s/v1/BusinessPartners? TESTEAR EN POSTMAN EL CATALOGO DE CLIENTES

        
        public async Task<List<ClienteViewModel>> BuscarClientesPorNombreAsync(string term)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var settings = _config.GetSection("SapServiceLayer");
            var baseUrl = settings["BaseUrl"].TrimEnd('/');

            var filtro = term.Replace("'", "''");

            // 1️⃣ Traer clientes que coincidan con el término
            var url = $"{baseUrl}/BusinessPartners" +
                $"?$filter=CardType eq 'C' and (contains(CardName,'{filtro}') or contains(CardForeignName,'{filtro}'))" +
                "&$select=CardCode,CardName,CardForeignName,CreditLimit,CurrentAccountBalance,OpenDeliveryNotesBalance,OpenOrdersBalance";

            //var response = await _httpClient.GetAsync(url);
            var response = await GetWithReLoginAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var clientes = new List<ClienteViewModel>();

            if (doc.RootElement.TryGetProperty("value", out var value))
            {
                foreach (var x in value.EnumerateArray())
                {
                    string cardCode = x.GetProperty("CardCode").GetString();

                    decimal entregas = x.TryGetProperty("OpenDeliveryNotesBalance", out var dnotes) ? dnotes.GetDecimal() : 0;
                    decimal pedidos = x.TryGetProperty("OpenOrdersBalance", out var orders) ? orders.GetDecimal() : 0;

                    // 2️⃣ Traer saldo vencido de facturas
                    decimal saldoVencido = 0;
                    try
                    {
                        string hoy = DateTime.Today.ToString("yyyy-MM-dd");
                        var facturasUrl = $"{baseUrl}/Invoices?" +
                            $"$filter=CardCode eq '{cardCode}' and DocTotal gt PaidToDate and DocDueDate lt '{hoy}'&" +
                            "$select=DocTotal,PaidToDate";

                        var facturasResponse = await _httpClient.GetAsync(facturasUrl);
                        facturasResponse.EnsureSuccessStatusCode();
                        var facturasJson = await facturasResponse.Content.ReadAsStringAsync();
                        using var facturasDoc = JsonDocument.Parse(facturasJson);

                        if (facturasDoc.RootElement.TryGetProperty("value", out var facturasArray))
                        {
                            foreach (var f in facturasArray.EnumerateArray())
                            {
                                var docTotal = f.GetProperty("DocTotal").GetDecimal();
                                var paidToDate = f.GetProperty("PaidToDate").GetDecimal();
                                saldoVencido += docTotal - paidToDate;
                            }
                        }
                    }
                    catch
                    {
                        saldoVencido = 0;
                    }

                    clientes.Add(new ClienteViewModel
                    {
                        CardCode = cardCode,
                        CardName = x.GetProperty("CardName").GetString(),
                        CardFName = x.TryGetProperty("CardForeignName", out var fname) ? fname.GetString() : string.Empty,
                        CreditLimit = x.TryGetProperty("CreditLimit", out var credito) ? credito.GetDecimal() : 0,
                        CurrentAccountBalance = x.TryGetProperty("CurrentAccountBalance", out var saldo) ? saldo.GetDecimal() : 0,
                        TotalPendiente = entregas + pedidos,
                        SaldoVencido = saldoVencido // 🔹 Nuevo campo agregado
                    });
                }
            }

            return clientes;
        }


        //======================================
        // BUSCAS CATALOGO DE ARTICULOS DESDE SAP
        //======================================

        //https://localhost:7171/Pedidos/BuscarProductosAutocomplete?term=N023

        //public async Task<List<ProductoViewModel>> BuscarProductosAsync(string term)
        //{
        //    if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
        //        await LoginAsync();

        //    var settings = _config.GetSection("SapServiceLayer");

        //    // Buscar por ItemCode o ItemName con similitud
        //    var filter = $"contains(ItemCode,'{term}') or contains(ItemName,'{term}')";
        //    var url = $"{settings["BaseUrl"]}/Items?$select=ItemCode,ItemName&$filter={filter}&$top=20";

        //    //var response = await _httpClient.GetAsync(url);
        //    var response = await GetWithReLoginAsync(url);
        //    response.EnsureSuccessStatusCode();

        //    var json = await response.Content.ReadAsStringAsync();
        //    using var doc = JsonDocument.Parse(json);

        //    if (!doc.RootElement.TryGetProperty("value", out var value))
        //        return new List<ProductoViewModel>();

        //    return value.EnumerateArray()
        //        .Select(x => new ProductoViewModel
        //        {
        //            ItemCode = x.GetProperty("ItemCode").GetString(),
        //            ItemName = x.GetProperty("ItemName").GetString()
        //        })
        //        .ToList();
        //}



        //public async Task<List<ProductoViewModel>> BuscarProductosAsync(string term)
        //{
        //    term = (term ?? "").Trim();
        //    if (term.Length < 1)
        //        return new List<ProductoViewModel>();

        //    // Buscar por ProductoCodigo o ProductoNombre con similitud (TOP 20)
        //    var query = _context.ArticuloSap.AsNoTracking()
        //        // opcional: excluir "(SIN MASTER)" si aplicaba esa regla en SAP
        //        //.Where(a => a.U_MASTER == null || a.U_MASTER != "(SIN MASTER)")
        //        .Where(a =>
        //            (a.ProductoCodigo != null && a.ProductoCodigo.Contains(term)) ||
        //            (a.ProductoNombre != null && a.ProductoNombre.Contains(term))
        //        )
        //        // prioriza los que empiezan con el término, luego ordena por código
        //        .OrderByDescending(a => a.ProductoCodigo.StartsWith(term))
        //        .ThenByDescending(a => a.ProductoNombre.StartsWith(term))
        //        .ThenBy(a => a.ProductoCodigo)
        //        .Take(20);

        //    var lista = await query
        //        .Select(a => new ProductoViewModel
        //        {
        //            ItemCode = a.ProductoCodigo,
        //            ItemName = a.ProductoNombre
        //        })
        //        .ToListAsync();

        //    return lista;
        //}


        public async Task<List<ProductoViewModel>> BuscarProductosAsync(string term)
        {
            term = (term ?? "").Trim();
            if (term.Length < 1)
                return new List<ProductoViewModel>();

            // Buscar por ProductoCodigo o ProductoNombre con similitud (TOP 20)
            var query = _context.ArticuloSap.AsNoTracking()
                //.Where(a => a.U_MASTER == null || a.U_MASTER != "(SIN MASTER)")
                .Where(a =>
                    (a.ProductoCodigo != null && a.ProductoCodigo.Contains(term)) ||
                    (a.ProductoNombre != null && a.ProductoNombre.Contains(term))
                )
                .OrderByDescending(a => a.ProductoCodigo.StartsWith(term))
                .ThenByDescending(a => a.ProductoNombre.StartsWith(term))
                .ThenBy(a => a.ProductoCodigo)
                .Take(20);

            var lista = await query
                .Select(a => new ProductoViewModel
                {
                    ItemCode = a.ProductoCodigo,
                    ItemName = a.ProductoNombre,

                    // 👇 AQUÍ ES LA CLAVE:
                    // Usa U_KilosCaja, NO KilosCaja
                    // Si U_KilosCaja es decimal? (nullable), usa ?? 0m
                    KilosCaja = a.U_KilosCaja ?? 0m
                    // Si U_KilosCaja es decimal normal (no admite ??), deja solo:
                    // KilosCaja = a.U_KilosCaja
                })
                .ToListAsync();

            return lista;
        }







        //======================================
        // OBTIENES PROPIEDADES DE DOCUMENTACION EN CLIENTES SAP
        //======================================

        //https://localhost:7171/Pedidos/ObtenerPropiedadesCliente?cardCode=C000001

        public async Task<List<ClientePropiedadViewModel>> ObtenerPropiedadesClienteAsync(string cardCode)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var settings = _config.GetSection("SapServiceLayer");
            var baseUrl = settings["BaseUrl"].TrimEnd('/');

            var url = $"{baseUrl}/BusinessPartners('{cardCode}')";
            //var response = await _httpClient.GetAsync(url);
            var response = await GetWithReLoginAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var propiedades = new List<ClientePropiedadViewModel>();

            // 🔹 Diccionario de alias
            var nombres = new Dictionary<string, string>
                {
 { "Properties1", "Carta libre de EEB" },
 { "Properties2", "Carta libre de clembuterol" },
 { "Properties3", "Microbiologicos" },
 { "Properties4", "Constancia de limpieza" },
 { "Properties5", "Certificacion de desinfeccion" },
 { "Properties6", "Ficha tecnica" },
 { "Properties7", "Carta garantia" },
 { "Properties8", "Carta libre de residuos toxicos" },
 { "Properties9", "Aviso de movilizacion" },
 { "Properties10", "Hoja de trabajo" },
 { "Properties11", "Orden de compra" },
 { "Properties12", "Certificado de desinfeccion y fumigacion" },
 { "Properties13", "Factura" },
 { "Properties14", "Aviso de movilizacion tif y hoja de trabajo" },
 { "Properties15", "Certificado de calidad" },                    
 // agrega más según tu configuración en SAP
                };

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.StartsWith("Properties"))
                {
                    var valorStr = prop.Value.GetString()?.Trim().ToUpper();
                    if (valorStr == "TYES" || valorStr == "Y" || valorStr == "TRUE")
                    {
                        propiedades.Add(new ClientePropiedadViewModel
                        {
                            Nombre = nombres.ContainsKey(prop.Name)
                                        ? nombres[prop.Name]               // usa alias del diccionario
                                        : prop.Name.Replace("Properties", "Grupo"), // fallback
                            Valor = true
                        });
                    }
                }
                else if (prop.Name.StartsWith("U_"))
                {
                    var valorStr = prop.Value.GetString()?.Trim().ToUpper();
                    if (valorStr == "Y" || valorStr == "TRUE" || valorStr == "S")
                    {
                        propiedades.Add(new ClientePropiedadViewModel
                        {
                            Nombre = prop.Name.Substring(2),
                            Valor = true
                        });
                    }
                }
            }

            return propiedades;
        }


        //https://localhost:7171/Comercial/ObtenerVendedorCliente?cardCode=C000025

        //======================================
        // DEVUELVE EL ID DEL VENDEDOR DE UN CLIENTE SAP
        //======================================

       
        public async Task<int?> ObtenerVendedorClienteAsync(string cardCode)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var settings = _config.GetSection("SapServiceLayer");
            var baseUrl = settings["BaseUrl"].TrimEnd('/');

            var url = $"{baseUrl}/BusinessPartners('{cardCode}')?$select=SalesPersonCode";
            //var response = await _httpClient.GetAsync(url);
            var response = await GetWithReLoginAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("SalesPersonCode", out var vendedorProp))
                return vendedorProp.GetInt32();

            return null; // No tiene vendedor asignado
        }


        //======================================
        // DEVUELVE EL NOMBRE DEL VENDEDOR A PARTIR DE UN ID
        //======================================
       
        public async Task<string> ObtenerNombreVendedorAsync(int vendedorId)
        {
            if (vendedorId <= 0)
                return "Vendedor no asignado";

            var settings = _config.GetSection("SapServiceLayer");
            var baseUrl = settings["BaseUrl"].TrimEnd('/');

            var url = $"{baseUrl}/SalesPersons({vendedorId})?$select=SalesEmployeeName";
            //var response = await _httpClient.GetAsync(url);
            var response = await GetWithReLoginAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("SalesEmployeeName", out var nombreProp))
                return nombreProp.GetString() ?? "Vendedor no asignado";

            return "Vendedor no asignado";
        }

        //======================================
        // OBTENER DIRECCIONES DEL CLIENTE DESDE SAP
        //======================================
        //https://localhost:7171/Comercial/ObtenerDireccionesCliente?cardCode=C000001
        public async Task<List<string>> ObtenerDireccionesClienteAsync(string cardCode)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var baseUrl = _config["SapServiceLayer:BaseUrl"].TrimEnd('/');
            var url = $"{baseUrl}/BusinessPartners('{cardCode}')";
            //var response = await _httpClient.GetAsync(url);
            var response = await GetWithReLoginAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var direcciones = new List<string>();

            // Revisamos BPAddresses
            if (doc.RootElement.TryGetProperty("BPAddresses", out JsonElement bpAddresses) && bpAddresses.ValueKind == JsonValueKind.Array)
            {
                foreach (var addr in bpAddresses.EnumerateArray())
                {
                    if (addr.TryGetProperty("AddressName", out JsonElement name))
                    {
                        var valor = name.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(valor))
                            direcciones.Add(valor);
                    }
                }
            }

            return direcciones;
        }

        //// =========================
        //// 🔹 Obtener precio por cliente/artículo
        //// =========================

        ////https://localhost:7171/Comercial/ObtenerPrecioArticuloCliente?cardCode=C000025&itemCode=N023
        //public async Task<ProductoViewModel?> ObtenerPrecioArticuloPorClienteAsync(string cardCode, string itemCode)
        //{
        //    if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
        //        await LoginAsync();

        //    var settings = _config.GetSection("SapServiceLayer");

        //    // 1️⃣ Obtener PriceList del cliente
        //    var clienteUrl = $"{settings["BaseUrl"]}/BusinessPartners('{cardCode}')?$select=PriceListNum";
        //    var clienteResponse = await _httpClient.GetAsync(clienteUrl);
        //    clienteResponse.EnsureSuccessStatusCode();
        //    var clienteJson = await clienteResponse.Content.ReadAsStringAsync();
        //    using var clienteDoc = JsonDocument.Parse(clienteJson);

        //    if (!clienteDoc.RootElement.TryGetProperty("PriceListNum", out var priceListNumProp))
        //        return null;

        //    var priceListNum = priceListNumProp.GetInt32();

        //    // 2️⃣ Obtener artículo completo (incluye ItemPrices y U_KilosCaja)
        //    var itemUrl = $"{settings["BaseUrl"]}/Items('{Uri.EscapeDataString(itemCode)}')";
        //    var itemResponse = await _httpClient.GetAsync(itemUrl);
        //    itemResponse.EnsureSuccessStatusCode();

        //    var itemJson = await itemResponse.Content.ReadAsStringAsync();
        //    using var itemDoc = JsonDocument.Parse(itemJson);

        //    string code = itemDoc.RootElement.GetProperty("ItemCode").GetString();
        //    string name = itemDoc.RootElement.GetProperty("ItemName").GetString();
        //    decimal price = 0;
        //    decimal kilosCaja = 0;

        //    // 🔹 Obtener U_KilosCaja si existe
        //    if (itemDoc.RootElement.TryGetProperty("U_KilosCaja", out var kilosProp))
        //        kilosCaja = kilosProp.GetDecimal();

        //    // 3️⃣ Buscar el precio en la lista correcta
        //    if (itemDoc.RootElement.TryGetProperty("ItemPrices", out var pricesArray) && pricesArray.ValueKind == JsonValueKind.Array)
        //    {
        //        foreach (var p in pricesArray.EnumerateArray())
        //        {
        //            if (p.GetProperty("PriceList").GetInt32() == priceListNum)
        //            {
        //                price = p.GetProperty("Price").GetDecimal();
        //                break;
        //            }
        //        }
        //    }

        //    return new ProductoViewModel
        //    {
        //        ItemCode = code,
        //        ItemName = name,
        //        Precio = price,
        //        KilosCaja = kilosCaja // 🔹 Nuevo campo agregado
        //    };
        //}



        // =========================
        // 🔹 Obtener precio por cliente/artículo (LOCAL)
        // =========================
        public async Task<ProductoViewModel?> ObtenerPrecioArticuloPorClienteAsync(string cardCode, string itemCode)
        {
            // Normaliza claves
            var cli = (cardCode ?? "").Trim().ToUpper();
            var code = (itemCode ?? "").Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(cli) || string.IsNullOrWhiteSpace(code))
                return null;

            // 1) Artículo (nombre y kilos/caja) desde ArticuloSap
            var art = await _context.ArticuloSap
                .AsNoTracking()
                .Where(a => a.ProductoCodigo == code)
                .Select(a => new { a.ProductoNombre, a.U_KilosCaja })
                .FirstOrDefaultAsync();

            if (art == null)
                return null; // no existe localmente el SKU

            // 2) Precio específico del cliente desde CatalogoPrecioSap
            var precioRow = await _context.CatalogoPrecioSap
                .AsNoTracking()
                .Where(p => p.ProductoCodigo == code && p.Cliente == cli)
                .OrderByDescending(p => p.FechaModificacion)   // toma el más reciente
                .FirstOrDefaultAsync();

            var precio = precioRow?.Precio ?? 0m;

            return new ProductoViewModel
            {
                ItemCode = code,
                ItemName = art.ProductoNombre ?? "",
                Precio = precio,
                KilosCaja = art.U_KilosCaja ?? 0m
            };
        }




        // =========================
        // 🔹 Obtener facturas con SKUs reales
        // =========================
        public async Task<List<DocumentoVentaViewModel>> GetInvoicesAll(string cardCode)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var facturas = new List<DocumentoVentaViewModel>();
            var baseUrl = _config["SapServiceLayer:BaseUrl"].TrimEnd('/');

            // 🔹 Rango: 1 de enero del año pasado → hoy (ajústalo a tu gusto)
            var fechaInicio = new DateTime(DateTime.Today.Year - 1, 1, 1);
            var fechaFin = DateTime.Today;

            // B1 SL acepta fechas entre comillas: 'YYYY-MM-DD'
            string fi = fechaInicio.ToString("yyyy-MM-dd");
            string ff = fechaFin.ToString("yyyy-MM-dd");

            int skip = 0;
            const int batchSize = 1; // 100–500 va bien

            while (true)
            {
                // Cabeceras (sin expand). Select solo lo que necesitas.
                var url =
                    $"{baseUrl}/Invoices?" +
                    $"$filter=CardCode eq '{cardCode}' and DocDate ge '{fi}' and DocDate le '{ff}' " +
                    "&$select=DocEntry,DocDate,CardCode" +
                    "&$orderby=DocDate asc, DocEntry asc " +
                    $"&$skip={skip}&$top={batchSize}";

                var resp = await GetWithReLoginAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    throw new Exception($"SAP Error {resp.StatusCode}: {err}");
                }

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("value", out var value) || value.GetArrayLength() == 0)
                    break;

                foreach (var inv in value.EnumerateArray())
                {
                    int docEntry = inv.GetProperty("DocEntry").GetInt32();
                    var docDate = inv.GetProperty("DocDate").GetDateTime();
                    var card = inv.GetProperty("CardCode").GetString() ?? cardCode;

                    // 🔹 Llamada por documento para obtener las líneas
                    var linesUrl = $"{baseUrl}/Invoices({docEntry})?$select=DocumentLines";
                    var linesResp = await GetWithReLoginAsync(linesUrl);
                    if (!linesResp.IsSuccessStatusCode)
                    {
                        var err = await linesResp.Content.ReadAsStringAsync();
                        throw new Exception($"SAP Lines Error {linesResp.StatusCode}: {err}");
                    }

                    var linesJson = await linesResp.Content.ReadAsStringAsync();
                    using var linesDoc = JsonDocument.Parse(linesJson);

                    if (linesDoc.RootElement.TryGetProperty("DocumentLines", out var lines) &&
                        lines.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var line in lines.EnumerateArray())
                        {
                            string sku = line.TryGetProperty("ItemCode", out var skuProp) && skuProp.ValueKind != JsonValueKind.Null
                                ? skuProp.GetString() ?? ""
                                : "";

                            decimal kilos = line.TryGetProperty("Quantity", out var qty) && qty.ValueKind != JsonValueKind.Null
                                ? qty.GetDecimal()
                                : 0m;

                            int lineNum = line.TryGetProperty("LineNum", out var ln) && ln.ValueKind != JsonValueKind.Null
                                ? ln.GetInt32()
                                : 0;

                            facturas.Add(new DocumentoVentaViewModel
                            {
                                DocEntry = docEntry,
                                DocDate = docDate,
                                CardCode = card,
                                SKU = sku,
                                Kilos = Math.Round(kilos, 2, MidpointRounding.AwayFromZero),
                                LineNum = lineNum
                            });
                        }
                    }
                }

                skip += batchSize;
            }

            return facturas;
        }

        // ===========================================================================
        // 🔹 Obtener facturas con SKUs reales SIN FILTRAR CLIENTES
        // ===========================================================================
        public async Task<List<DocumentoVentaViewModel>> GetInvoicesAllAsync(
         DateTime? desde = null, DateTime? hasta = null)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var baseUrl = _config["SapServiceLayer:BaseUrl"].TrimEnd('/');
            var facturas = new List<DocumentoVentaViewModel>();

            // Rango por defecto: desde 2000-01-01 a hoy (ajústalo a tu gusto)
            var d1 = (desde ?? new DateTime(2000, 1, 1)).ToString("yyyy-MM-dd");
            var d2 = (hasta ?? DateTime.Today).ToString("yyyy-MM-dd");

            int skip = 0;
            const int batchSize = 200; // sube de 1 a algo sano (100–500)

            while (true)
            {
                var url =
                    $"{baseUrl}/Invoices?" +
                    $"$filter=DocDate ge '{d1}' and DocDate le '{d2}' " +
                    "&$select=DocEntry,DocDate,CardCode" +
                    "&$orderby=DocEntry asc " +
                    $"&$skip={skip}&$top={batchSize}";

                var resp = await GetWithReLoginAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    throw new Exception($"SAP Error {resp.StatusCode}: {err}");
                }

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.GetArrayLength() == 0)
                    break;

                foreach (var inv in arr.EnumerateArray())
                {
                    int docEntry = inv.GetProperty("DocEntry").GetInt32();
                    var docDate = inv.GetProperty("DocDate").GetDateTime();
                    var card = inv.TryGetProperty("CardCode", out var cc) && cc.ValueKind == JsonValueKind.String ? cc.GetString() ?? "" : "";

                    // Líneas del documento
                    var linesUrl = $"{baseUrl}/Invoices({docEntry})?$select=DocumentLines";
                    var linesResp = await GetWithReLoginAsync(linesUrl);
                    linesResp.EnsureSuccessStatusCode();

                    var linesJson = await linesResp.Content.ReadAsStringAsync();
                    using var linesDoc = JsonDocument.Parse(linesJson);

                    if (linesDoc.RootElement.TryGetProperty("DocumentLines", out var lines) &&
                        lines.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var line in lines.EnumerateArray())
                        {
                            string sku = line.TryGetProperty("ItemCode", out var skuProp) && skuProp.ValueKind == JsonValueKind.String
                                ? skuProp.GetString() ?? ""
                                : "";
                            decimal kilos = line.TryGetProperty("Quantity", out var qty) && qty.ValueKind == JsonValueKind.Number
                                ? qty.GetDecimal()
                                : 0m;
                            int lineNum = line.TryGetProperty("LineNum", out var ln) && ln.ValueKind == JsonValueKind.Number
                                ? ln.GetInt32()
                                : 0;

                            facturas.Add(new DocumentoVentaViewModel
                            {
                                DocEntry = docEntry,
                                DocDate = docDate,
                                CardCode = card,
                                SKU = sku,
                                Kilos = Math.Round(kilos, 2, MidpointRounding.AwayFromZero),
                                LineNum = lineNum
                            });
                        }
                    }
                }

                skip += batchSize;
                if (arr.GetArrayLength() < batchSize) break;
            }

            return facturas;
        }



         //===================================================
         //🔹 SINCRONIZADOR DE FACTURAS DE CLIENTE A SQL LOCAL
         //===================================================
        //public async Task<int> SincronizarInvoicesClienteAsync(string cardCode, string sqlConnectionString)
        //{
        //    // 1) Traer del SAP (tu método optimizado con $expand y LineNum)
        //    var list = await GetInvoicesAll(cardCode); // devuelve List<DocumentoVentaDto>

        //    // 2) Armar DataTable para TVP
        //    var tvp = new DataTable();
        //    tvp.Columns.Add("sap_doc_entry", typeof(int));
        //    tvp.Columns.Add("sap_line_num", typeof(int));
        //    tvp.Columns.Add("card_code", typeof(string));
        //    tvp.Columns.Add("sku", typeof(string));
        //    tvp.Columns.Add("kilos", typeof(decimal));
        //    tvp.Columns.Add("doc_date", typeof(DateTime));

        //    foreach (var x in list)
        //    {
        //        var row = tvp.NewRow();
        //        row["sap_doc_entry"] = x.DocEntry;
        //        row["sap_line_num"] = x.LineNum;
        //        row["card_code"] = x.CardCode ?? cardCode;
        //        row["sku"] = x.SKU ?? "";
        //        row["kilos"] = Math.Round(x.Kilos, 4, MidpointRounding.AwayFromZero);
        //        row["doc_date"] = x.DocDate.Date;
        //        tvp.Rows.Add(row);
        //    }

        //    // 3) Ejecutar SP con TVP (upsert + rebuild)
        //    using var con = new SqlConnection(sqlConnectionString);
        //    await con.OpenAsync();

        //    using var cmd = new SqlCommand("dbo.Invoices_UpsertAndRebuild", con)
        //    {
        //        CommandType = CommandType.StoredProcedure
        //    };
        //    cmd.Parameters.Add(new SqlParameter("@CardCode", SqlDbType.NVarChar, 50) { Value = cardCode });
        //    var p = cmd.Parameters.AddWithValue("@Lote", tvp);
        //    p.SqlDbType = SqlDbType.Structured;
        //    p.TypeName = "dbo.TvpInvoiceLine";

        //    var affected = await cmd.ExecuteNonQueryAsync();
        //    return affected; // filas afectadas en total (orientativo)
        //}






        // =========================
        // 🔹 Obtener notas de crédito con SKUs reales
        // =========================
        //private async Task<List<DocumentoVentaViewModel>> GetCreditNotes(string cardCode)
        //{
        //    if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
        //        await LoginAsync();

        //    var baseUrl = _config["SapServiceLayer:BaseUrl"].TrimEnd('/');
        //    var url = $"{baseUrl}/CreditNotes?$filter=CardCode eq '{Uri.EscapeDataString(cardCode)}'&$select=DocEntry,DocDate";

        //      //var response = await _httpClient.GetAsync(url);
        //var response = await GetWithReLoginAsync(url);
        //    response.EnsureSuccessStatusCode();
        //    var json = await response.Content.ReadAsStringAsync();
        //    using var doc = JsonDocument.Parse(json);

        //    var creditNotes = new List<DocumentoVentaViewModel>();

        //    if (doc.RootElement.TryGetProperty("value", out var value))
        //    {
        //        foreach (var x in value.EnumerateArray())
        //        {
        //            int docEntry = x.GetProperty("DocEntry").GetInt32();
        //            DateTime docDate = x.GetProperty("DocDate").GetDateTime();

        //            // 🔹 Obtener líneas de la NC por separado
        //            var linesUrl = $"{baseUrl}/CreditNotes({docEntry})?$select=DocumentLines";
        //            var linesResponse = await _httpClient.GetAsync(linesUrl);
        //            linesResponse.EnsureSuccessStatusCode();
        //            var linesJson = await linesResponse.Content.ReadAsStringAsync();
        //            using var linesDoc = JsonDocument.Parse(linesJson);

        //            if (linesDoc.RootElement.TryGetProperty("DocumentLines", out var lines))
        //            {
        //                foreach (var line in lines.EnumerateArray())
        //                {
        //                    var lineType = line.TryGetProperty("LineType", out var lt) ? lt.GetString() : "I";
        //                    if (lineType != "I" && lineType != "S") continue;

        //                    var sku = line.GetProperty("ItemCode").GetString();
        //                    decimal kilos = line.TryGetProperty("Quantity", out var qty) ? qty.GetDecimal() : 0;

        //                    // 🔹 Obtener el BaseEntry (la factura a la que aplica)
        //                    int baseEntry = line.TryGetProperty("BaseEntry", out var be) && be.ValueKind != JsonValueKind.Null
        //                        ? be.GetInt32()
        //                        : docEntry; // fallback a la NC misma si no hay BaseEntry

        //                    // 🔹 Obtener fecha de la factura original si existe
        //                    DateTime fechaOriginal = docDate; // fallback
        //                    if (baseEntry != docEntry)
        //                    {
        //                        var invoiceUrl = $"{baseUrl}/Invoices({baseEntry})?$select=DocDate";
        //                        var invoiceResponse = await _httpClient.GetAsync(invoiceUrl);
        //                        if (invoiceResponse.IsSuccessStatusCode)
        //                        {
        //                            var invoiceJson = await invoiceResponse.Content.ReadAsStringAsync();
        //                            using var invoiceDoc = JsonDocument.Parse(invoiceJson);
        //                            if (invoiceDoc.RootElement.TryGetProperty("DocDate", out var dd) && dd.ValueKind != JsonValueKind.Null)
        //                                fechaOriginal = dd.GetDateTime();
        //                        }
        //                    }

        //                    creditNotes.Add(new DocumentoVentaViewModel
        //                    {
        //                        DocEntry = docEntry,
        //                        DocDate = fechaOriginal, // usar la fecha de la factura original
        //                        SKU = sku,
        //                        Kilos = kilos
        //                    });
        //                }
        //            }
        //        }
        //    }

        //    return creditNotes;
        //}

        // =========================
        // 🔹 Obtener entregas (ejemplo vacío, implementar según tu fuente)
        // =========================
        //private async Task<List<DocumentoVentaViewModel>> GetDeliveries(string cardCode)
        //{
        //    await Task.CompletedTask;
        //    return new List<DocumentoVentaViewModel>();
        //}



        //https://localhost:7171/Comercial/ObtenerPresupuesto?cardCode=C000176

        // =========================
        // 🔹 Calcular presupuesto
        // =========================
        public async Task<List<PresupuestoArticuloViewModel>> CalcularPresupuestoDetalle(string cardCode)
        {
            try
            {
                // 🔹 Traer facturas activas (solo DocumentStatus == bost_Open)
                var facturas = (await GetInvoicesAll(cardCode))
                                ?.Where(f => f.CANCELED == "bost_Open" && !string.IsNullOrEmpty(f.SKU))
                                .ToList() ?? new List<DocumentoVentaViewModel>();

                if (!facturas.Any())
                    return new List<PresupuestoArticuloViewModel>();

                // 🔹 Obtener SKUs únicos
                var todosLosSkus = facturas.Select(f => f.SKU).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // 🔹 Últimos 24 meses
                var hoy = DateTime.Today;
                var mesesUltimos24 = Enumerable.Range(0, 24)
                    .Select(i => new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-i))
                    .OrderBy(d => d)
                    .ToList();

                // 🔹 Agrupar facturas por SKU + año + mes para acelerar sumas
                var facturasAgrupadas = facturas
                    .GroupBy(f => new { f.SKU, Año = f.DocDate.Year, Mes = f.DocDate.Month })
                    .ToDictionary(
                        g => (g.Key.SKU, g.Key.Año, g.Key.Mes),
                        g => g.Sum(f => f.Kilos)
                    );

                var resultadoCompleto = new List<PresupuestoArticuloViewModel>();

                foreach (var sku in todosLosSkus)
                {
                    foreach (var mes in mesesUltimos24)
                    {
                        facturasAgrupadas.TryGetValue(
                            (sku, mes.Year, mes.Month),
                            out var totalKilos
                        );

                        resultadoCompleto.Add(new PresupuestoArticuloViewModel
                        {
                            SKU = sku,
                            TotalKilos = Math.Round(totalKilos, 2, MidpointRounding.AwayFromZero),
                            Fecha = mes
                        });
                    }
                }

                return resultadoCompleto;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al calcular presupuesto para {cardCode}: {ex.Message}", ex);
            }
        }



        //======================================
        // OBTIENE CODIGO DE CLIENTE EN SAP 
        //======================================
        public async Task<ClienteViewModel?> ObtenerClientePorCodigoAsync(string cardCode)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var settings = _config.GetSection("SapServiceLayer");
            var baseUrl = settings["BaseUrl"].TrimEnd('/');

            var url = $"{baseUrl}/BusinessPartners?$filter=CardType eq 'C' and CardCode eq '{cardCode}'" +
                      "&$select=CardCode,CardName,CreditLimit,CurrentAccountBalance,OpenDeliveryNotesBalance,OpenOrdersBalance";

            //var response = await _httpClient.GetAsync(url);
            var response = await GetWithReLoginAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("value", out var value) || value.GetArrayLength() == 0)
                return null;

            var x = value[0];

            decimal entregas = x.TryGetProperty("OpenDeliveryNotesBalance", out var dnotes) ? dnotes.GetDecimal() : 0;
            decimal pedidos = x.TryGetProperty("OpenOrdersBalance", out var orders) ? orders.GetDecimal() : 0;

            return new ClienteViewModel
            {
                CardCode = x.GetProperty("CardCode").GetString(),
                CardName = x.GetProperty("CardName").GetString(),
                CreditLimit = x.TryGetProperty("CreditLimit", out var credito) ? credito.GetDecimal() : 0,
                CurrentAccountBalance = x.TryGetProperty("CurrentAccountBalance", out var saldo) ? saldo.GetDecimal() : 0,
                TotalPendiente = entregas + pedidos
            };
        }


        //======================================
        // CATALOGO DE ARTICULOS DESDE SAP
        //======================================

        public async Task<List<CatalogoSkuSapViewmodel>> ObtenerTodosProductosAsync()
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var settings = _config.GetSection("SapServiceLayer");
            var productos = new List<CatalogoSkuSapViewmodel>();
            int skip = 0;
            int batchSize = 1; // recomendado (ajusta si tu SL lo requiere)
            bool more = true;

            // helper: parsea decimales robusto (U_KilosCaja)
            static decimal? ParseDecimal(JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.Number)
                {
                    if (el.TryGetDecimal(out var d)) return d;
                }
                else if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                                         System.Globalization.CultureInfo.InvariantCulture, out var d))
                        return d;
                }
                return null;
            }

            // ✅ helper: parsea enteros robusto (U_Clas_Prod, U_PRESENT, U_PorcInye)
            static int? ParseInt(JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.Number)
                {
                    if (el.TryGetInt32(out var i)) return i;

                    // por si viene 12.0 pero conceptualmente es int
                    if (el.TryGetDecimal(out var d)) return (int)d;
                }
                else if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();

                    if (int.TryParse(s, out var i)) return i;

                    // por si viene "12.0"
                    if (decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                                         System.Globalization.CultureInfo.InvariantCulture, out var d))
                        return (int)d;
                }
                return null;
            }

            while (more)
            {
                //           var url =
                //$"{settings["BaseUrl"].TrimEnd('/')}/Items" +
                //"?$select=ItemCode,ItemName,U_MASTER,U_TipoporSKU,U_KilosCaja,U_Clas_Prod,U_PRESENT,U_PorcInye" +
                //"&$filter=U_TipoporSKU ge '1' and Valid eq 'tYES' and SalesItem eq 'tYES'" +
                //$"&$top={batchSize}&$skip={skip}";

                var url =
  $"{settings["BaseUrl"].TrimEnd('/')}/Items" +
  "?$select=ItemCode,ItemName,U_MASTER,U_TipoporSKU,U_KilosCaja,U_Clas_Prod,U_PRESENT,U_PorcInye" +
  "&$filter=U_TipoporSKU ge '1' and Valid eq 'tYES'" +
  $"&$top={batchSize}&$skip={skip}";

                var response = await GetWithReLoginAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    break;

                var batch = new List<CatalogoSkuSapViewmodel>(arr.GetArrayLength());

                foreach (var x in arr.EnumerateArray())
                {
                    // U_MASTER / U_TipoporSKU pueden venir null
                    string? uMaster = x.TryGetProperty("U_MASTER", out var um) && um.ValueKind == JsonValueKind.String
                        ? um.GetString()
                        : null;

                    string? tipoSKU = x.TryGetProperty("U_TipoporSKU", out var ts) && ts.ValueKind == JsonValueKind.String
                        ? ts.GetString()
                        : null;

                    decimal? kilosCaja = null;
                    if (x.TryGetProperty("U_KilosCaja", out var kc))
                        kilosCaja = ParseDecimal(kc);

                    // ✅ NUEVOS INT
                    int? clasProd = null;
                    if (x.TryGetProperty("U_Clas_Prod", out var cp))
                        clasProd = ParseInt(cp);

                    int? present = null;
                    if (x.TryGetProperty("U_PRESENT", out var pr))
                        present = ParseInt(pr);

                    int? porcInye = null;
                    if (x.TryGetProperty("U_PorcInye", out var pi))
                        porcInye = ParseInt(pi);

                    batch.Add(new CatalogoSkuSapViewmodel
                    {
                        ItemCode = x.GetProperty("ItemCode").GetString(),
                        ItemName = x.GetProperty("ItemName").GetString(),
                        U_MASTER = uMaster,
                        U_TipoporSKU = tipoSKU,
                        U_KilosCaja = kilosCaja,

                        // ✅ asignación final
                        U_Clas_Prod = clasProd,
                        U_PRESENT = present,
                        U_PorcInye = porcInye
                    });
                }

                if (batch.Count == 0) break;

                productos.AddRange(batch);
                skip += batch.Count;
                more = batch.Count == batchSize;
            }

            return productos;
        }



        // -------------------------
        // Sincronización con SQL INSERTAR CATALOGO DE SKU DE SAP A BASE DE DATOS LOCAL ARTICULOSAP
        // NOTA: NO sincroniza U_KilosCaja / kg promedio desde SAP.
        // -------------------------
        public async Task SincronizarItemsAsync()
        {
            var itemsSap = await ObtenerTodosProductosAsync();

            // Filtrar SIN MASTER por si el filtro OData no aplicó
            itemsSap = itemsSap
                .Where(x => string.IsNullOrWhiteSpace(x.U_MASTER) ||
                            !x.U_MASTER.Trim().Equals("(SIN MASTER)", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Diccionarios case-insensitive
            var dicSap = itemsSap
                .GroupBy(x => (x.ItemCode ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

            var locales = await _context.ArticuloSap.ToListAsync();

            var dicLocales = locales
                .GroupBy(l => (l.ProductoCodigo ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

            var nuevos = new List<ArticuloSap>();
            var eliminados = new List<ArticuloSap>();
            int actualizados = 0;

            // Helper: define si “viene con valor” desde SAP
            static bool HasValue(int? v) => v.HasValue;
            // static bool HasValue(int? v) => v.HasValue && v.Value != 0; // Usa esto si 0 = sin dato

            foreach (var kv in dicSap)
            {
                var sapItem = kv.Value;
                var codigo = (sapItem.ItemCode ?? "").Trim().ToUpper();

                if (string.IsNullOrWhiteSpace(codigo))
                    continue;

                if (!dicLocales.TryGetValue(kv.Key, out var local))
                {
                    // INSERTAR NUEVO
                    // IMPORTANTE:
                    // No tomar U_KilosCaja desde SAP.
                    // Se deja en 0 para que puedas capturarlo/modificarlo localmente.
                    nuevos.Add(new ArticuloSap
                    {
                        ProductoCodigo = codigo,
                        ProductoNombre = sapItem.ItemName ?? "",
                        U_MASTER = sapItem.U_MASTER ?? "",
                        U_TipoporSKU = sapItem.U_TipoporSKU ?? "",

                        // NO sincronizar kg promedio desde SAP
                        U_KilosCaja = 0,

                        // Campos INT nuevos
                        U_Clas_Prod = sapItem.U_Clas_Prod,
                        U_PRESENT = sapItem.U_PRESENT,
                        U_PorcInye = sapItem.U_PorcInye,

                        FechaModificacion = DateTime.Now
                    });
                }
                else
                {
                    bool actualizo = false;

                    if (local.ProductoNombre != (sapItem.ItemName ?? ""))
                    {
                        local.ProductoNombre = sapItem.ItemName ?? "";
                        actualizo = true;
                    }

                    if (local.U_MASTER != (sapItem.U_MASTER ?? ""))
                    {
                        local.U_MASTER = sapItem.U_MASTER ?? "";
                        actualizo = true;
                    }

                    if (local.U_TipoporSKU != (sapItem.U_TipoporSKU ?? ""))
                    {
                        local.U_TipoporSKU = sapItem.U_TipoporSKU ?? "";
                        actualizo = true;
                    }

                    // =====================================================
                    // NO ACTUALIZAR U_KilosCaja
                    // =====================================================
                    // Antes tenías esto:
                    //
                    // if (local.U_KilosCaja != sapItem.U_KilosCaja)
                    // {
                    //     local.U_KilosCaja = sapItem.U_KilosCaja;
                    //     actualizo = true;
                    // }
                    //
                    // Se quita para que SAP NO pise el kg promedio local.
                    // =====================================================

                    // SOLO ACTUALIZAR SI SAP TRAE VALOR
                    if (HasValue(sapItem.U_Clas_Prod) && local.U_Clas_Prod != sapItem.U_Clas_Prod)
                    {
                        local.U_Clas_Prod = sapItem.U_Clas_Prod;
                        actualizo = true;
                    }

                    if (HasValue(sapItem.U_PRESENT) && local.U_PRESENT != sapItem.U_PRESENT)
                    {
                        local.U_PRESENT = sapItem.U_PRESENT;
                        actualizo = true;
                    }

                    if (HasValue(sapItem.U_PorcInye) && local.U_PorcInye != sapItem.U_PorcInye)
                    {
                        local.U_PorcInye = sapItem.U_PorcInye;
                        actualizo = true;
                    }

                    if (actualizo)
                    {
                        local.FechaModificacion = DateTime.Now;
                        actualizados++;
                    }
                }
            }

            // Eliminados
            foreach (var local in locales)
            {
                var code = (local.ProductoCodigo ?? "").Trim();

                var fueraDeSap = !dicSap.ContainsKey(code);

                var esSinMaster = !string.IsNullOrWhiteSpace(local.U_MASTER) &&
                                  local.U_MASTER.Trim().Equals("(SIN MASTER)", StringComparison.OrdinalIgnoreCase);

                if (fueraDeSap || esSinMaster)
                    eliminados.Add(local);
            }

            if (nuevos.Count > 0)
                _context.ArticuloSap.AddRange(nuevos);

            if (eliminados.Count > 0)
                _context.ArticuloSap.RemoveRange(eliminados);

            await _context.SaveChangesAsync();

            Console.WriteLine(
                $"✅ Sincronización Artículos: Insertados={nuevos.Count}, Actualizados={actualizados}, Eliminados={eliminados.Count}. U_KilosCaja no se sincronizó desde SAP."
            );
        }




        //======================================
        // CATÁLOGO DE CLIENTES DESDE SAP 
        //======================================
        public async Task<List<CatalogoClienteSapViewModel>> ObtenerCatTodosClientesAsync()
        {
            // Asegura sesión en Service Layer
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var baseUrl = _config.GetSection("SapServiceLayer")["BaseUrl"].TrimEnd('/');

            // ==========================================
            // 1) Traer clientes (paginado)
            // ==========================================
            var clientes = new List<CatalogoClienteSapViewModel>();

            int bpSkip = 0;
            const int bpBatch = 1; // ajusta a tu SL (200–1000)

            while (true)
            {
                var url =
                    $"{baseUrl}/BusinessPartners?" +
                    "$filter=CardType eq 'C' and Valid eq 'tYES' " +                   
                    "&$select=CardCode,CardName,U_MT_Clasificacion,U_CANAL,SalesPersonCode,PriceListNum " +
                    "&$orderby=CardCode " +
                    $"&$top={bpBatch}&$skip={bpSkip}";

                var response = await GetWithReLoginAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("value", out var value) ||
                    value.ValueKind != JsonValueKind.Array ||
                    value.GetArrayLength() == 0)
                {
                    break;
                }

                var batch = value.EnumerateArray()
                    .Select(x =>
                    {
                        int? slp = null;

                        int? priceListNum = null;

                        if (x.TryGetProperty("PriceListNum", out var pl) && pl.ValueKind != JsonValueKind.Null)
                        {
                            if (pl.ValueKind == JsonValueKind.Number && pl.TryGetInt32(out var pli))
                                priceListNum = pli;
                            else if (pl.ValueKind == JsonValueKind.String && int.TryParse(pl.GetString(), out var pls))
                                priceListNum = pls;
                        }


                        if (x.TryGetProperty("SalesPersonCode", out var sp) && sp.ValueKind != JsonValueKind.Null)
                        {
                            if (sp.ValueKind == JsonValueKind.Number && sp.TryGetInt32(out var vi)) slp = vi;
                            else if (sp.ValueKind == JsonValueKind.String && int.TryParse(sp.GetString(), out var vs)) slp = vs;
                        }

                        return new CatalogoClienteSapViewModel
                        {
                            CardCode = x.GetProperty("CardCode").GetString(),
                            CardName = x.GetProperty("CardName").GetString(),
                            U_MT_Clasificacion = x.TryGetProperty("U_MT_Clasificacion", out var clasif) ? clasif.GetString() : null,
                            U_CANAL = x.TryGetProperty("U_CANAL", out var canal) ? canal.GetString() : null,

                            SlpCode = slp,
                            SalesPersonName = null,
                            PriceListNum = priceListNum,
                            PriceListName = null
                        };
                    })
                    .ToList();

                clientes.AddRange(batch);
                bpSkip += value.GetArrayLength();

                if (value.GetArrayLength() < bpBatch) break;
            }

            // Si no hay clientes, salimos
            if (clientes.Count == 0)
                return clientes;

            // ==========================================
            // 2) Traer vendedores (paginado)
            //    ¡Clave! Antes traías sólo la primera página.
            // ==========================================
            var map = new Dictionary<int, string>();
            int spSkip = 0;
            const int spBatch = 1; // ajusta a tu SL

            while (true)
            {
                var spUrl =
                    $"{baseUrl}/SalesPersons?" +
                    "$select=SalesEmployeeCode,SalesEmployeeName" +
                    "&$orderby=SalesEmployeeCode " +
                    $"&$top={spBatch}&$skip={spSkip}";

                var spResp = await GetWithReLoginAsync(spUrl);
                spResp.EnsureSuccessStatusCode();

                var spJson = await spResp.Content.ReadAsStringAsync();
                using var spDoc = JsonDocument.Parse(spJson);

                if (!spDoc.RootElement.TryGetProperty("value", out var spVal) ||
                    spVal.ValueKind != JsonValueKind.Array ||
                    spVal.GetArrayLength() == 0)
                {
                    break;
                }

                foreach (var it in spVal.EnumerateArray())
                {
                    if (!it.TryGetProperty("SalesEmployeeCode", out var codeProp)) continue;
                    if (!it.TryGetProperty("SalesEmployeeName", out var nameProp)) continue;

                    int code;
                    if (codeProp.ValueKind == JsonValueKind.Number && codeProp.TryGetInt32(out var c1)) code = c1;
                    else if (codeProp.ValueKind == JsonValueKind.String && int.TryParse(codeProp.GetString(), out var c2)) code = c2;
                    else continue;

                    var name = nameProp.ValueKind == JsonValueKind.String ? (nameProp.GetString() ?? "") : "";

                    if (!map.ContainsKey(code))
                        map[code] = name;
                }

                spSkip += spVal.GetArrayLength();
                if (spVal.GetArrayLength() < spBatch) break;
            }

            // ==========================================
            // 3) Asignar nombre al cliente
            // ==========================================
            if (map.Count > 0)
            {
                foreach (var c in clientes)
                {
                    if (c.SlpCode.HasValue && map.TryGetValue(c.SlpCode.Value, out var nombre))
                        c.SalesPersonName = nombre;
                }
            }

            return clientes;
        }




        //======================================
        // SINCRONIZAR CLIENTES SAP → SQL LOCAL
        //======================================
        public async Task SincronizarClientesAsync()
        {
            var clientesSap = await ObtenerCatTodosClientesAsync(); // ← ya trae VendedorId/VendedorNombre
            var listasPrecio = await _context.ListaPreciosSap.ToDictionaryAsync(x => x.PriceListNum, x => x.PriceListName);


            foreach (var item in clientesSap)
            {
                var priceListName = "";

                if (item.PriceListNum.HasValue &&
                    listasPrecio.TryGetValue(item.PriceListNum.Value, out var nombreLista))
                {
                    priceListName = nombreLista ?? "";
                }

                var existente = await _context.ClienteSap
                    .FirstOrDefaultAsync(x => x.Cliente == item.CardCode);

                if (existente == null)
                {
                    var nuevo = new ClienteSap
                    {
                        Cliente = item.CardCode ?? "",
                        Nombrecliente = item.CardName ?? "",
                        U_MT_Clasificacion = item.U_MT_Clasificacion ?? "",
                        U_CANAL = item.U_CANAL ?? "",
                        // 👇 nuevos
                        VendedorId = item.SlpCode,
                        VendedorNombre = item.SalesPersonName ?? "",

                        PriceListNum = item.PriceListNum,
                        PriceListName = priceListName,

                        FechaModificacion = DateTime.Now
                    };

                    _context.ClienteSap.Add(nuevo);
                }
                else
                {
                    bool actualizo = false;

                    if (existente.Nombrecliente != (item.CardName ?? ""))
                    { existente.Nombrecliente = item.CardName ?? ""; actualizo = true; }

                    if ((existente.U_MT_Clasificacion ?? "") != (item.U_MT_Clasificacion ?? ""))
                    { existente.U_MT_Clasificacion = item.U_MT_Clasificacion ?? ""; actualizo = true; }

                    if ((existente.U_CANAL ?? "") != (item.U_CANAL ?? ""))
                    { existente.U_CANAL = item.U_CANAL ?? ""; actualizo = true; }

                    // 👇 comparar y actualizar vendedor
                    if (existente.VendedorId != item.SlpCode)
                    { existente.VendedorId = item.SlpCode; actualizo = true; }

                    if ((existente.VendedorNombre ?? "") != (item.SalesPersonName ?? ""))
                    { existente.VendedorNombre = item.SalesPersonName ?? ""; actualizo = true; }

                    if (existente.PriceListNum != item.PriceListNum)
                    {
                        existente.PriceListNum = item.PriceListNum;
                        actualizo = true;
                    }

                    if ((existente.PriceListName ?? "") != priceListName)
                    {
                        existente.PriceListName = priceListName;
                        actualizo = true;
                    }

                    if (actualizo)
                        existente.FechaModificacion = DateTime.Now;
                }
            }

            await _context.SaveChangesAsync();
        }



        //==============================================
        // CATALOGO DE PRECIO POR CLIENTE Y KG PROMEDIO
        //==============================================
        public async Task<List<CatProductoPriceViewModel>> CATPrecioArticuloClienteAsync()
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var settings = _config.GetSection("SapServiceLayer");
            var baseUrl = settings["BaseUrl"].TrimEnd('/');
            var productos = new List<CatProductoPriceViewModel>();

            // 1) Obtener SOLO CLIENTES con su lista de precios (PAGINADO)
            var clientesPorLista = new Dictionary<int, List<string>>();

            int skipBP = 0;
            int topBP = 1;          // trae de 1000 en 1000
            bool moreBP = true;

            while (moreBP)
            {
                var clientesUrl = $"{baseUrl}/BusinessPartners" +
                                  "?$select=CardCode,PriceListNum,CardType,Valid" +
                                  "&$filter=CardType eq 'cCustomer' and Valid eq 'tYES'" +
                                  $"&$top={topBP}&$skip={skipBP}";

                var clientesResponse = await GetWithReLoginAsync(clientesUrl);
                clientesResponse.EnsureSuccessStatusCode();

                var clientesJson = await clientesResponse.Content.ReadAsStringAsync();
                using var clientesDoc = JsonDocument.Parse(clientesJson);

                if (!clientesDoc.RootElement.TryGetProperty("value", out var clientesArray) ||
                    clientesArray.GetArrayLength() == 0)
                    break;

                foreach (var cliente in clientesArray.EnumerateArray())
                {
                    var cardCode = cliente.GetProperty("CardCode").GetString();

                    if (!cliente.TryGetProperty("PriceListNum", out var plProp) ||
                         plProp.ValueKind != JsonValueKind.Number)
                        continue; // clientes sin lista por defecto

                    int listNum = plProp.GetInt32();

                    if (!clientesPorLista.TryGetValue(listNum, out var lista))
                        clientesPorLista[listNum] = lista = new List<string>();

                    if (!string.IsNullOrWhiteSpace(cardCode))
                        lista.Add(cardCode);
                }

                skipBP += topBP;
                moreBP = clientesArray.GetArrayLength() == topBP;
            }

            Console.WriteLine($"Listas con clientes: {clientesPorLista.Count}");

            // 2) Nombres de listas de precios (si tienes muchas, páginalas igual)
            var nombresListas = new Dictionary<int, string>();
            {
                var listasUrl = $"{baseUrl}/PriceLists?$select=PriceListNo,PriceListName&$top=1000&$skip=0";
                var listasResponse = await GetWithReLoginAsync(listasUrl);
                listasResponse.EnsureSuccessStatusCode();

                var listasJson = await listasResponse.Content.ReadAsStringAsync();
                using var listasDoc = JsonDocument.Parse(listasJson);

                if (listasDoc.RootElement.TryGetProperty("value", out var listasArray))
                {
                    foreach (var lista in listasArray.EnumerateArray())
                    {
                        if (!lista.TryGetProperty("PriceListNo", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
                            continue;

                        int id = idProp.GetInt32();
                        string name = lista.TryGetProperty("PriceListName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                            ? nameProp.GetString() ?? ""
                            : "";
                        nombresListas[id] = name;
                    }
                }
            }

            // 3) Items con precios (PAGINADO + filtro ENCODEADO)
            int skip = 0;
            int top = 1; // sube el tamaño para traer más rápido
            bool more = true;

            // Si quieres filtrar Items:
            string filtro = "U_TipoporSKU ge '1' and U_MASTER ne '(SIN MASTER)'";
            string filtroEncoded = Uri.EscapeDataString(filtro); // <-- importante

            while (more)
            {
                var url = $"{baseUrl}/Items?$select=ItemCode,ItemPrices&$filter={filtroEncoded}&$top={top}&$skip={skip}";
                // Si no quieres filtrar:
                // var url = $"{baseUrl}/Items?$select=ItemCode,ItemPrices&$top={top}&$skip={skip}";

                var response = await GetWithReLoginAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("value", out var itemsArray) ||
                    itemsArray.GetArrayLength() == 0)
                    break;

                foreach (var item in itemsArray.EnumerateArray())
                {
                    string itemCode = item.GetProperty("ItemCode").GetString() ?? "";

                    if (item.TryGetProperty("ItemPrices", out var pricesArray) &&
                        pricesArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var priceEntry in pricesArray.EnumerateArray())
                        {
                            if (!priceEntry.TryGetProperty("PriceList", out var plProp) ||
                                 plProp.ValueKind != JsonValueKind.Number)
                                continue;

                            int priceListNum = plProp.GetInt32();
                            decimal precio = priceEntry.TryGetProperty("Price", out var priceProp) && priceProp.ValueKind == JsonValueKind.Number
                                ? priceProp.GetDecimal()
                                : 0m;

                            // Si quieres TODO, no descartes:
                            // if (precio <= 1) continue;

                            if (!clientesPorLista.TryGetValue(priceListNum, out var clientes))
                                continue; // esa lista no la usa ningún cliente activo

                            string priceListName = nombresListas.TryGetValue(priceListNum, out var n) ? n : "";

                            foreach (var cliente in clientes)
                            {
                                productos.Add(new CatProductoPriceViewModel
                                {
                                    ItemCode = itemCode,
                                    Precio = precio,
                                    CardCode = cliente,
                                    PriceListNum = priceListNum,
                                    PriceListName = priceListName
                                });
                            }
                        }
                    }
                }

                skip += top;
                more = itemsArray.GetArrayLength() == top;
            }

            Console.WriteLine($"Productos recolectados: {productos.Count}");
            return productos;
        }



        //=============================================================
        // CATALOGO DE PRECIO POR CLIENTE Y KG PROMEDIO SAP → SQL LOCAL
        //=============================================================

        public async Task<int> SincronizarCatalogoPrecioAsync()
        {
            try
            {
                // (Opcional) aumenta timeout mientras afinas índices
                _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));

                // 1) Trae catálogo actual desde SAP
                var nuevos = await CATPrecioArticuloClienteAsync();
                if (nuevos == null || nuevos.Count == 0) return 0;

                var ahora = DateTime.Now;

                // 2) Normaliza y proyecta
                var nuevosNorm = nuevos
                    .Select(p => new CatalogoPrecioSap
                    {
                        ProductoCodigo = (p.ItemCode ?? "").Trim().ToUpper(),
                        Cliente = (p.CardCode ?? "").Trim().ToUpper(),
                        PriceListNum = p.PriceListNum,
                        PriceListName = p.PriceListName ?? "",
                        Precio = p.Precio,
                        FechaModificacion = ahora
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.ProductoCodigo) && !string.IsNullOrWhiteSpace(x.Cliente))
                    .ToList();

                if (nuevosNorm.Count == 0) return 0;

                // ⚠️ Si SAP manda duplicados por (Producto,Cliente,Lista), nos quedamos con el último
                string KeyOf(string prod, string cli, int pl) => $"{prod}||{cli}||{pl}";

                var dictNuevos = nuevosNorm
                    .GroupBy(x => KeyOf(x.ProductoCodigo, x.Cliente, x.PriceListNum))
                    .ToDictionary(g => g.Key, g => g.Last()); // elige la última ocurrencia

                var listasPresentes = dictNuevos.Values.Select(x => x.PriceListNum).Distinct().ToList();

                // 3) Carga existentes relevantes desde SQL
                var existentes = await _context.CatalogoPrecioSap
                    .Where(x => listasPresentes.Contains(x.PriceListNum))
                    .Select(x => new
                    {
                        x.Id,
                        x.ProductoCodigo,
                        x.Cliente,
                        x.PriceListNum,
                        x.PriceListName,
                        x.Precio
                    })
                    .AsNoTracking()
                    .ToListAsync();

                var dictExistentes = existentes
                    .GroupBy(e => KeyOf(e.ProductoCodigo, e.Cliente, e.PriceListNum))
                    .ToDictionary(g => g.Key, g => g.Last());

                // 4) Determina inserts / updates / deletes
                var aInsertar = new List<CatalogoPrecioSap>();
                var aActualizar = new List<CatalogoPrecioSap>();

                foreach (var (key, nuevo) in dictNuevos)
                {
                    if (!dictExistentes.TryGetValue(key, out var ex))
                    {
                        aInsertar.Add(nuevo);
                    }
                    else
                    {
                        bool cambiaPrecio = ex.Precio != nuevo.Precio;
                        bool cambiaNombre = (ex.PriceListName ?? "") != (nuevo.PriceListName ?? "");
                        if (cambiaPrecio || cambiaNombre)
                        {
                            aActualizar.Add(new CatalogoPrecioSap
                            {
                                Id = ex.Id,            // ⚠️ requerido si tu PK es Identity
                                ProductoCodigo = nuevo.ProductoCodigo,
                                Cliente = nuevo.Cliente,
                                PriceListNum = nuevo.PriceListNum,
                                PriceListName = nuevo.PriceListName,
                                Precio = nuevo.Precio,
                                FechaModificacion = ahora
                            });
                        }
                    }
                }

                var clavesNuevas = new HashSet<string>(dictNuevos.Keys);
                var clavesAEliminar = dictExistentes.Keys.Where(k => !clavesNuevas.Contains(k)).ToList();

                using var tx = await _context.Database.BeginTransactionAsync();

                // 5) DELETES (lotes)
                if (clavesAEliminar.Count > 0)
                {
                    const int loteDel = 1000;
                    for (int i = 0; i < clavesAEliminar.Count; i += loteDel)
                    {
                        var bloque = clavesAEliminar.Skip(i).Take(loteDel).ToList();

                        // Rompe cada clave, con TryParse seguro
                        var triples = new List<(string prod, string cli, int pl)>(bloque.Count);
                        foreach (var k in bloque)
                        {
                            var parts = k.Split("||");
                            if (parts.Length != 3) continue;
                            if (!int.TryParse(parts[2], out var pl)) continue;
                            triples.Add((parts[0], parts[1], pl));
                        }
                        if (triples.Count == 0) continue;

                        // EF Core 7+ (fast path)
                        var soportaExecuteDelete = true;
                        try
                        {
                            foreach (var t in triples)
                            {
                                // Una llamada por triple. Si son muchos, considera EFCore.BulkExtensions
                                await _context.CatalogoPrecioSap
                                    .Where(x => x.ProductoCodigo == t.prod && x.Cliente == t.cli && x.PriceListNum == t.pl)
                                    .ExecuteDeleteAsync();
                            }
                        }
                        catch (NotSupportedException)
                        {
                            soportaExecuteDelete = false;
                        }

                        // Fallback EF Core < 7: RemoveRange por batch
                        if (!soportaExecuteDelete)
                        {
                            // Trae entidades a borrar en este bloque
                            var toDel = new List<CatalogoPrecioSap>();
                            foreach (var t in triples)
                            {
                                var tmp = await _context.CatalogoPrecioSap
                                    .Where(x => x.ProductoCodigo == t.prod && x.Cliente == t.cli && x.PriceListNum == t.pl)
                                    .ToListAsync();
                                toDel.AddRange(tmp);
                            }

                            if (toDel.Count > 0)
                            {
                                _context.CatalogoPrecioSap.RemoveRange(toDel);
                                await _context.SaveChangesAsync();
                            }
                        }
                    }
                }

                // 6) INSERTS (lotes)
                if (aInsertar.Count > 0)
                {
                    const int loteIns = 1000;
                    for (int i = 0; i < aInsertar.Count; i += loteIns)
                    {
                        var bloque = aInsertar.Skip(i).Take(loteIns).ToList();
                        await _context.CatalogoPrecioSap.AddRangeAsync(bloque);
                        await _context.SaveChangesAsync();
                    }
                }

                // 7) UPDATES (lotes)
                if (aActualizar.Count > 0)
                {
                    const int loteUpd = 1000;
                    for (int i = 0; i < aActualizar.Count; i += loteUpd)
                    {
                        var bloque = aActualizar.Skip(i).Take(loteUpd).ToList();
                        foreach (var row in bloque)
                        {
                            // Attach por Id
                            _context.CatalogoPrecioSap.Attach(row);
                            _context.Entry(row).Property(x => x.PriceListName).IsModified = true;
                            _context.Entry(row).Property(x => x.Precio).IsModified = true;
                            _context.Entry(row).Property(x => x.FechaModificacion).IsModified = true;
                        }
                        await _context.SaveChangesAsync();
                    }
                }

                await tx.CommitAsync();

                var totalCambios = aInsertar.Count + aActualizar.Count + clavesAEliminar.Count;
                Console.WriteLine($"Insertados: {aInsertar.Count}, Actualizados: {aActualizar.Count}, Eliminados: {clavesAEliminar.Count}");
                return totalCambios;
            }
            catch (DbUpdateException dbex)
            {
                var baseMsg = dbex.GetBaseException()?.Message ?? "Sin base exception";
                throw new Exception($"Fallo al guardar en SQL: {dbex.Message} | Base: {baseMsg}", dbex);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? "Sin inner exception";
                throw new Exception($"Error al sincronizar catálogo de precios: {ex.Message} | Inner: {inner}", ex);
            }
        }


        public async Task<(bool ok, string? response, string? error, int statusCode)> GetAsync(string endpoint)
        {
            try
            {
                // endpoint puede venir como "DeliveryNotes?$select=..."
                var resp = await _httpClient.GetAsync(endpoint);

                // si la sesión caducó, re-log y reintenta
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    await EnsureLoginAsync(); // SIN force
                    resp = await _httpClient.GetAsync(endpoint);
                }

                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return (false, body, body, (int)resp.StatusCode);

                return (true, body, null, (int)resp.StatusCode);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message, 0);
            }
        }


        // ============================================================================
        // PROVEEDORES SAP
        // Pegar DENTRO de la clase SapServiceLayerClient.
        // ============================================================================

        private static string SapString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value) ||
                value.ValueKind == JsonValueKind.Null ||
                value.ValueKind == JsonValueKind.Undefined)
                return string.Empty;

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        private static int? SapInt(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value) ||
                value.ValueKind == JsonValueKind.Null ||
                value.ValueKind == JsonValueKind.Undefined)
                return null;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return number;

            if (int.TryParse(value.ToString(), out var parsed))
                return parsed;

            return null;
        }

        private static decimal SapDecimal(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value) ||
                value.ValueKind == JsonValueKind.Null ||
                value.ValueKind == JsonValueKind.Undefined)
                return 0m;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                return number;

            return decimal.TryParse(
                value.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                    ? parsed
                    : 0m;
        }

        private static bool SapYes(JsonElement element, string property)
        {
            var value = SapString(element, property);

            return value.Equals("tYES", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<int, string>> ObtenerGruposProveedorSapAsync(
            string baseUrl,
            CancellationToken ct)
        {
            var result = new Dictionary<int, string>();

            try
            {
                var url =
                    $"{baseUrl}/BusinessPartnerGroups" +
                    "?$select=Code,Name,Type" +
                    "&$orderby=Code";

                using var response = await GetWithReLoginAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("value", out var rows) ||
                    rows.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (var row in rows.EnumerateArray())
                {
                    var code = SapInt(row, "Code");
                    var name = SapString(row, "Name");
                    var type = SapString(row, "Type");

                    if (!code.HasValue)
                        continue;

                    // Algunas versiones devuelven el enum como nombre y otras como valor.
                    bool esGrupoProveedor =
                        string.IsNullOrWhiteSpace(type) ||
                        type.Equals("bbpgt_VendorGroup", StringComparison.OrdinalIgnoreCase) ||
                        type.Equals("S", StringComparison.OrdinalIgnoreCase);

                    if (esGrupoProveedor)
                        result[code.Value] = name;
                }
            }
            catch
            {
                // El catálogo principal puede sincronizarse aunque falle el nombre del grupo.
            }

            return result;
        }

        private async Task<Dictionary<int, string>> ObtenerCondicionesPagoSapAsync(
            string baseUrl,
            CancellationToken ct)
        {
            var result = new Dictionary<int, string>();

            try
            {
                var url =
                    $"{baseUrl}/PaymentTermsTypes" +
                    "?$select=GroupNumber,PaymentTermsGroupName" +
                    "&$orderby=GroupNumber";

                using var response = await GetWithReLoginAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("value", out var rows) ||
                    rows.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (var row in rows.EnumerateArray())
                {
                    var code = SapInt(row, "GroupNumber");
                    var name = SapString(row, "PaymentTermsGroupName");

                    if (code.HasValue)
                        result[code.Value] = name;
                }
            }
            catch
            {
                // No bloquear la sincronización del proveedor.
            }

            return result;
        }

        public async Task<List<CatalogoProveedorSapViewModel>> ObtenerCatTodosProveedoresAsync(
            CancellationToken ct = default)
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                await LoginAsync();

            var baseUrl = (_config.GetSection("SapServiceLayer")["BaseUrl"] ?? "")
                .TrimEnd('/');

            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException(
                    "No está configurado SapServiceLayer:BaseUrl.");

            var grupos = await ObtenerGruposProveedorSapAsync(baseUrl, ct);
            var condiciones = await ObtenerCondicionesPagoSapAsync(baseUrl, ct);

            var proveedores = new List<CatalogoProveedorSapViewModel>();

            int skip = 0;
            const int batchSize = 1;

            while (true)
            {
                // 'S' es el valor del enum proveedor. SAP también lo representa como cSupplier.
                var url =
                    $"{baseUrl}/BusinessPartners?" +
                    "$filter=CardType eq 'S'" +
                    "&$select=" +
                    "CardCode,CardName,CardForeignName,FederalTaxID," +
                    "Phone1,Cellular,EmailAddress,Currency,GroupCode," +
                    "PayTermsGrpCode,CurrentAccountBalance,Valid,Frozen," +
                    "Address,ZipCode,City,County,Country" +
                    "&$orderby=CardCode" +
                    $"&$top={batchSize}&$skip={skip}";

                using var response = await GetWithReLoginAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("value", out var rows) ||
                    rows.ValueKind != JsonValueKind.Array ||
                    rows.GetArrayLength() == 0)
                    break;

                foreach (var row in rows.EnumerateArray())
                {
                    var groupCode = SapInt(row, "GroupCode");
                    var paymentCode = SapInt(row, "PayTermsGrpCode");

                    proveedores.Add(new CatalogoProveedorSapViewModel
                    {
                        CardCode = SapString(row, "CardCode"),
                        CardName = SapString(row, "CardName"),
                        CardForeignName = SapString(row, "CardForeignName"),
                        FederalTaxID = SapString(row, "FederalTaxID"),
                        Phone1 = SapString(row, "Phone1"),
                        Cellular = SapString(row, "Cellular"),
                        EmailAddress = SapString(row, "EmailAddress"),
                        Currency = SapString(row, "Currency"),
                        GroupCode = groupCode,
                        GroupName =
                            groupCode.HasValue &&
                            grupos.TryGetValue(groupCode.Value, out var groupName)
                                ? groupName
                                : string.Empty,
                        PayTermsGrpCode = paymentCode,
                        PaymentTermsName =
                            paymentCode.HasValue &&
                            condiciones.TryGetValue(paymentCode.Value, out var paymentName)
                                ? paymentName
                                : string.Empty,
                        CurrentAccountBalance = SapDecimal(row, "CurrentAccountBalance"),
                        Address = SapString(row, "Address"),
                        ZipCode = SapString(row, "ZipCode"),
                        City = SapString(row, "City"),
                        County = SapString(row, "County"),
                        Country = SapString(row, "Country"),
                        Active = SapYes(row, "Valid"),
                        Frozen = SapYes(row, "Frozen")
                    });
                }

                int received = rows.GetArrayLength();
                skip += received;

                if (received < batchSize)
                    break;
            }

            return proveedores;
        }

        public async Task<(int totalSap, int insertados, int actualizados, int fueraDeSap)>
            SincronizarProveedoresAsync(CancellationToken ct = default)
        {
            var proveedoresSap = await ObtenerCatTodosProveedoresAsync(ct);

            var proveedoresLocales = await _context.ProveedorSap
                .ToDictionaryAsync(x => x.Proveedor, StringComparer.OrdinalIgnoreCase, ct);

            var encontradosSap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int insertados = 0;
            int actualizados = 0;

            foreach (var item in proveedoresSap)
            {
                var codigo = (item.CardCode ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(codigo))
                    continue;

                encontradosSap.Add(codigo);

                if (!proveedoresLocales.TryGetValue(codigo, out var local))
                {
                    local = new ProveedorSap
                    {
                        Proveedor = codigo
                    };

                    _context.ProveedorSap.Add(local);
                    proveedoresLocales[codigo] = local;
                    insertados++;
                }
                else
                {
                    actualizados++;
                }

                local.NombreProveedor = (item.CardName ?? string.Empty).Trim();
                local.NombreExtranjero = (item.CardForeignName ?? string.Empty).Trim();
                local.RFC = (item.FederalTaxID ?? string.Empty).Trim();
                local.Telefono = (item.Phone1 ?? string.Empty).Trim();
                local.Celular = (item.Cellular ?? string.Empty).Trim();
                local.Correo = (item.EmailAddress ?? string.Empty).Trim();
                local.Moneda = (item.Currency ?? string.Empty).Trim();
                local.GrupoId = item.GroupCode;
                local.GrupoNombre = (item.GroupName ?? string.Empty).Trim();
                local.CondicionPagoId = item.PayTermsGrpCode;
                local.CondicionPagoNombre = (item.PaymentTermsName ?? string.Empty).Trim();
                local.SaldoCuenta = item.CurrentAccountBalance;
                local.Direccion = (item.Address ?? string.Empty).Trim();
                local.Ciudad = (item.City ?? string.Empty).Trim();
                local.Estado = (item.County ?? string.Empty).Trim();
                local.Pais = (item.Country ?? string.Empty).Trim();
                local.CodigoPostal = (item.ZipCode ?? string.Empty).Trim();
                local.Activo = item.Active && !item.Frozen;
                local.Congelado = item.Frozen;
                local.ExisteEnSap = true;
                local.FechaModificacion = DateTime.Now;
            }

            int fueraDeSap = 0;

            foreach (var local in proveedoresLocales.Values)
            {
                if (encontradosSap.Contains(local.Proveedor))
                    continue;

                if (local.ExisteEnSap || local.Activo)
                {
                    local.ExisteEnSap = false;
                    local.Activo = false;
                    local.FechaModificacion = DateTime.Now;
                    fueraDeSap++;
                }
            }

            await _context.SaveChangesAsync(ct);

            return (
                totalSap: proveedoresSap.Count,
                insertados,
                actualizados,
                fueraDeSap);
        }






    }
}
