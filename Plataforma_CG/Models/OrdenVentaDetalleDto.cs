namespace Plataforma_CG.Models
{
    public class OrdenVentaDetalleDto
    {
        public int DetalleId { get; set; }
        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }
        public int? Cajas { get; set; }
        public decimal? KilosCaja { get; set; }   // peso por caja (derivado)
        public decimal? Precio { get; set; }
        public decimal? Importe { get; set; }     // Peso * Precio
        public decimal? Presupuesto { get; set; } // no existe en entidad -> 0
        public decimal? VariacionPresupuesto { get; set; } // no existe -> 0
        public string Almacen { get; set; }   // 👈 NECESARIO PARA VISTA/PDF
    }
}
