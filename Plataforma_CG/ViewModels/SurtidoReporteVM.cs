using System;
using System.Collections.Generic;

namespace Plataforma_CG.ViewModels
{
    public sealed class SurtidoReporteVM
    {
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public string Almacen { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string Pedido { get; set; } = "";
        public string Formato { get; set; } = "CONCENTRADO";
        public List<string> ClientesSeleccionados { get; set; } = new();
        public List<int> SolicitudesSeleccionadas { get; set; } = new();
        public List<SurtidoReporteOpcionVM> ClientesDisponibles { get; set; } = new();
        public List<SurtidoReporteOpcionVM> SolicitudesDisponibles { get; set; } = new();
        public bool CatalogoLimitado { get; set; }
        public string? Error { get; set; }
        public bool LimiteAlcanzado { get; set; }
        public List<SurtidoReporteAlmacenVM> Almacenes { get; set; } = new();
        public List<SurtidoReportePedidoVM> Pedidos { get; set; } = new();
        public List<SurtidoReporteCajaVM> Cajas { get; set; } = new();
        public int TotalCajas { get; set; }
        public decimal TotalKg { get; set; }
    }
    public sealed class SurtidoReporteOpcionVM
    {
        public string Valor { get; set; } = "";
        public string Texto { get; set; } = "";
    }
    public sealed class SurtidoReporteAlmacenVM
    {
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
    }
    public sealed class SurtidoReportePedidoVM
    {
        public long CapturaId { get; set; }
        public int SolicitudSurtidoId { get; set; }
        public string Pedido { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string Planta { get; set; } = "";
        public string CodigoAlmacen { get; set; } = "";
        public string Estatus { get; set; } = "";
        public string IntegracionEstatus { get; set; } = "SIN COLA";
        public string ValidadoPor { get; set; } = "";
        public DateTime? FechaValidacion { get; set; }
        public DateTime? FechaIntegracion { get; set; }
        public int Cajas { get; set; }
        public decimal Kg { get; set; }
        public decimal CajasSolicitadas { get; set; }
        public bool SolicitadasDisponibles { get; set; } = true;
        public List<SurtidoReporteSkuVM> SkusSolicitados { get; set; } = new();
    }
    public sealed class SurtidoReporteCajaVM
    {
        public long CapturaId { get; set; }
        public long ProduccionId { get; set; }
        public string CodigoEtiqueta { get; set; } = "";
        public string SKU { get; set; } = "";
        public decimal PesoNeto { get; set; }
        public string Tarima { get; set; } = "";
        public string Lote { get; set; } = "";
        public DateTime? FechaProduccion { get; set; }
        public DateTime FechaCaptura { get; set; }
        public string UsuarioCaptura { get; set; } = "";
        public string EstadoIntegracion { get; set; } = "";
    }
}

namespace Plataforma_CG.ViewModels
{
    public sealed class SurtidoReporteSkuVM
    {
        public string SKU {get;set;} = "";
        public decimal CajasSolicitadas {get;set;}
    }
}
