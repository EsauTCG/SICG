namespace Plataforma_CG.Models.Operaciones.Inyeccion
{
    public sealed class InyeccionImpresionLogModel
    {
        public long Id { get; set; }
        public Guid SolicitudGuid { get; set; }
        public Guid CapturaGuid { get; set; }
        public int EntradaId { get; set; }
        public string Folio { get; set; } = string.Empty;
        public bool EsReimpresion { get; set; }
        public DateTime FechaSolicitudUtc { get; set; }
        public DateTime? FechaResultadoUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string SKU { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public long LoteId { get; set; }
        public string Lote { get; set; } = string.Empty;
        public string Plantilla { get; set; } = string.Empty;
        public decimal Peso { get; set; }
        public decimal Tara { get; set; }
        public string TipoPeso { get; set; } = string.Empty;
        public string ImpresoraIp { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string Mensaje { get; set; } = string.Empty;
        public string PayloadZpl { get; set; } = string.Empty;
        public string PayloadSha256 { get; set; } = string.Empty;
    }
}
