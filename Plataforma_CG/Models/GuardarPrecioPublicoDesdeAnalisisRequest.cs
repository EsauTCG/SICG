namespace Plataforma_CG.Models
{
    public class GuardarPrecioPublicoDesdeAnalisisRequest
    {
        public int PriceListNum { get; set; } = 1;
        public DateTime FechaCorte { get; set; }
        public DateTime FechaUso { get; set; }
        public string AlcanceClientes { get; set; } = "ACTIVOS";
        public string? Canal { get; set; }

        public List<string> Clientes { get; set; } = new();
        public List<GuardarPrecioPublicoSkuDto> Registros { get; set; } = new();
    }

    public class GuardarPrecioPublicoSkuDto
    {
        public string ProductoCodigo { get; set; } = "";
        public decimal Precio { get; set; }
        public string? Master { get; set; }
        public string? ProductoNombre { get; set; }
        public string? Clasificacion { get; set; }

        public string? Comentarios { get; set; }
    }
}
