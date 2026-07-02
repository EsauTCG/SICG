namespace Plataforma_CG.Models
{
    public class GuardarLogisticaDto
    {
        public int OrdenVentaId { get; set; }

        public string Fletera { get; set; }
        public int? EspacioTarimas { get; set; }
        public string HoraLlegadaUnidad { get; set; }

        public string EstatusLogistico { get; set; }
        public string ObservacionLogistica { get; set; }
        public string MotivoCancelacion { get; set; }
        public string MotivoCancelacionFletera { get; set; }

        public bool Cancelado { get; set; }
        public bool CanceladoFletera { get; set; }
    }
}
