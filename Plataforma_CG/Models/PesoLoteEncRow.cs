namespace Plataforma_CG.Models
{
    public class PesoLoteEncRow
    {
        public int LoteId { get; set; }
        public string NombreLote { get; set; }
        public string Tipo { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public DateTime FechaProduccionMin { get; set; }
        public DateTime FechaProduccionMax { get; set; }
        public string Proceso { get; set; }
        public string Solicitante { get; set; }
        public string Autoriza { get; set; }
        public string Estacion { get; set; }
        public string Accion { get; set; }
    }
}
