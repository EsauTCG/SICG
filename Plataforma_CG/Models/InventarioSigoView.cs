namespace Plataforma_CG.Models
{
    public class InventarioSigoView
    {
        public string? ProductoCodigo { get; set; } = string.Empty;
        public decimal? Kg { get; set; }
        public int? Cajas { get; set; }
        public string? Almacen { get; set; } = string.Empty;
        public string? AlmacenId { get; set; } = string.Empty;
        public string? Sucursal { get; set; } = string.Empty;

    }
}
