namespace Plataforma_CG.Models.Operaciones.Inyeccion
{
    public sealed class CapturaEntradaRequest
    {
        public Guid CapturaGuid { get; set; }
        public DateTimeOffset CapturadaUtc { get; set; }
        public EntradaModel Entrada { get; set; } = new();
        public string ProductoSKU { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public string Lote { get; set; } = string.Empty;
    }
}
