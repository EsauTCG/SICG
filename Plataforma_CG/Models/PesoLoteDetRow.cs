namespace Plataforma_CG.Models
{
    public class PesoLoteDetRow
    {
        public int ProduccionId { get; set; }
        public int LoteId { get; set; }
        public string NombreLote { get; set; }
        public string Articulo { get; set; }
        public string NombreArticulo { get; set; }
        public string Proceso { get; set; }
        public string Solicitante { get; set; }
        public string Autoriza { get; set; }
        public string Estacion { get; set; }
        public string Accion { get; set; }
        public decimal ValorAnterior { get; set; }
        public decimal ValorActual { get; set; }
        public DateTime FechaProduccion { get; set; }
        public DateTime? FechaSolicitud { get; set; }
        public string TipoPeso { get; set; }
    }
}
