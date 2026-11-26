namespace Plataforma_CG.ViewModels
{
    /// <summary>
    /// Datos del catálogo que se muestran en la cabecera de la ficha.
    /// </summary>
    public class ClienteDetalleResumenVm
    {
        public string Cliente { get; set; } = "";
        public string Nombrecliente { get; set; } = "";
        public string U_MT_Clasificacion { get; set; } = "";
        public string U_CANAL { get; set; } = "";
        public int? PriceListNum { get; set; }
        public string? PriceListName { get; set; }
        public int? VendedorId { get; set; }
        public string? VendedorNombre { get; set; }
        public bool AplicaPresupuesto { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    /// <summary>
    /// Dirección activa del cliente (tabla DireccionesCliente).
    /// </summary>
    public class DireccionClienteDetalleVm
    {
        public string AliasDireccion { get; set; } = "";
        public bool EsPrincipal { get; set; }
        public string Cedis { get; set; } = "";
        public string? Ruta { get; set; }
        public string Calle { get; set; } = "";
        public string? Colonia { get; set; }
        public string Ciudad { get; set; } = "";
        public string Estado { get; set; } = "";
        public string? CodigoPostal { get; set; }
        public string Pais { get; set; } = "";
        public string? SapAddressType { get; set; }
        public string? SapAddressCode { get; set; }
    }

    /// <summary>
    /// SKU facturado al cliente en la ventana de meses solicitada.
    /// </summary>
    public class ProductoClienteVm
    {
        public string Sku { get; set; } = "";
        public string Master { get; set; } = "";
        public string Producto { get; set; } = "";
        public decimal Kilos { get; set; }
        public DateTime? UltimaCompra { get; set; }
    }

    /// <summary>
    /// Pedido de venta del cliente (Fase 1.5 de la ficha).
    /// </summary>
    public class PedidoClienteDetalleVm
    {
        public int IdOrdenVenta { get; set; }
        public int PedidoVentaId { get; set; }
        public string Consecutivo { get; set; } = "";
        public string? Serie { get; set; }
        public DateTime? FechaRegistro { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public DateTime? FechaEmbarque { get; set; }
        public int? Estado { get; set; }
        public string EstadoTexto { get; set; } = "";
        public string Vendedor { get; set; } = "";
        public string AlmacenSurtir { get; set; } = "";
        public string? ObservacionGestion { get; set; }
        public decimal? Saldo { get; set; }
        public decimal? Credito { get; set; }
        public decimal TotalImporte { get; set; }
        public decimal TotalPeso { get; set; }
        public List<ProductoPedidoClienteDetalleVm> Productos { get; set; } = new();
    }

    /// <summary>
    /// Renglón del pedido de venta. En PedidoVentaProducto, KilosCaja
    /// guarda los kilos totales del renglón (ver ComercialController).
    /// </summary>
    public class ProductoPedidoClienteDetalleVm
    {
        public string Sku { get; set; } = "";
        public string Producto { get; set; } = "";
        public int Cajas { get; set; }
        public decimal Precio { get; set; }
        public decimal Kilos { get; set; }
        public string Almacen { get; set; } = "";
    }
}
