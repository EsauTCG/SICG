namespace Plataforma_CG.Models
{
    public class VentasHistoricas
    {
        public string? Sucursal { get; set; }
        public string? DOC { get; set; }
        public string? Cliente { get; set; }
        public string? ClienteID { get; set; }
        public string? Clasificacion { get; set; }
        public decimal Unidad { get; set; }
        public string? SKU { get; set; }
        public string? Producto { get; set; }
        public string? Nombre { get; set; }
        public decimal Peso { get; set; }
        public decimal Importe { get; set; }
        public DateTime? FechaVenta { get; set; }
        public DateTime? FechaProduccion { get; set; }
        public decimal? Costo { get; set; }
        public DateTime? LastSyncAt { get; set; }
    }
}
