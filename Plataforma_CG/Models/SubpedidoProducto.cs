namespace Plataforma_CG.Models
{


    public class SubpedidoProducto
    {
        public int Id { get; set; }
        public int SubpedidoId { get; set; }                  // FK a Subpedido

        public int? OrdenVentaProductoId { get; set; }        // (opcional) referencia a la línea original
        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }
        public decimal KilosCaja { get; set; }
        public decimal Precio { get; set; }
        public int Cajas { get; set; }
        public string? Almacen { get; set; }

        public Subpedido? Subpedido { get; set; }
    }
}
