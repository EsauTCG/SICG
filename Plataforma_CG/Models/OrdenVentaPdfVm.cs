namespace Plataforma_CG.Models
{
    public class OrdenVentaPdfVm
    {

        public int Id { get; set; }
        public string Consecutivo { get; set; }
        public DateTime? FechaRegistro { get; set; }
        public DateTime? FechaEntrega { get; set; }

        public string Cliente { get; set; }
        public string ClienteNombre { get; set; }
        public string Vendedor { get; set; }
        public string Serie { get; set; }
        public int Estatus { get; set; }

        public decimal KgTotales { get; set; }
        public decimal Importe { get; set; }
        public string Observacion { get; set; }

        public int? SubpedidoId { get; set; }
        public string DocumentoSAP { get; set; }

        public string SubFolio { get; set; }
        public string Almacen { get; set; }
        public decimal TotalPesoSap { get; set; }
        public decimal TotalImporteSap { get; set; }

        public decimal Subtotal { get; set; }
        public decimal Total { get; set; }

        public string DireccionCliente { get; set; }

        public List<OrdenVentaPdfLineaVm> Lineas { get; set; } = new();
    }

    public class OrdenVentaPdfLineaVm
    {
        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }
        public decimal Peso { get; set; }
        public int Cajas { get; set; }
        public decimal Precio { get; set; }
        public decimal Kg { get; set; }
        public decimal Importe { get; set; }
    }
}