using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models
{
    public class PedidoVentaProducto
    {
        public int Id { get; set; }

        // FK
        public int PedidoVentaId { get; set; }
        public PedidoVenta PedidoVenta { get; set; } = default!;

        // Datos del producto
        [MaxLength(50)]
        public string ProductoCodigo { get; set; } = string.Empty;

        [MaxLength(200)]
        public string ProductoNombre { get; set; } = string.Empty;

        public decimal KilosCaja { get; set; }     // equivalente a Peso por caja
        public decimal Precio { get; set; }
        public int Cajas { get; set; }


        [MaxLength(50)]
        public string Almacen { get; set; } = string.Empty;

        // Derivado
        public decimal Importe => Precio * Cajas;
    }
}
