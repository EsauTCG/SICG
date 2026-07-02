using Plataforma_CG.Models;


public class OrdenVentaProducto
{
    public int Id { get; set; }

    // 🔑 debe coincidir con la FK real en SQL
    public int PedidoId { get; set; }

    // 🔑 navegación hacia la orden
    public OrdenVenta Pedido { get; set; }

    public string ProductoCodigo { get; set; }
    public string ProductoNombre { get; set; }
    public decimal Peso { get; set; }
    public decimal Precio { get; set; }
    public int Cajas { get; set; }   


    // 👇 NUEVO
    public bool Eliminado { get; set; } = false;
    public DateTime? EliminadoFecha { get; set; }
    public string? EliminadoUsuario { get; set; }

    // ⚠️ este lo calcula SQL, pero si quieres mostrarlo en C# puedes tenerlo también
    public decimal Importe => Peso * Precio;

    // NUEVO: autorización de presupuesto POR LÍNEA
    public bool AutorizacionPresupuestoLinea { get; set; }  // default false

    public bool AutorizacionPrecioLinea { get; set; }  // default false
}