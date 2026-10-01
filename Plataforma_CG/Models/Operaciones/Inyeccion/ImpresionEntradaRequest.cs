namespace Plataforma_CG.Models.Operaciones.Inyeccion
{
    public sealed class ImpresionEntradaRequest
    {
        public Guid SolicitudGuid { get; set; }
        public Guid CapturaGuid { get; set; }
        public bool EsReimpresion { get; set; }
        public string IpImpresora { get; set; } = string.Empty;
        public EntradaModel Entrada { get; set; } = new();
        public string ProductoSKU { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public string Lote { get; set; } = string.Empty;
    }
}
