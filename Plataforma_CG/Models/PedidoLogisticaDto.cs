namespace Plataforma_CG.Models
{
    public class PedidoLogisticaDto
    {
        public int Id { get; set; }
        public string Consecutivo { get; set; }
        public string Serie { get; set; }
        public string NombreSerie { get; set; }
        public string Sucursal { get; set; }
        public string SucursalId { get; set; }

        public DateTime FechaEntrega { get; set; }
        public DateTime? FechaEmbarque { get; set; }
        public string HoraEmbarque { get; set; } = "";

        public string Cliente { get; set; }
        public string Vendedor { get; set; }
        public string Ruta { get; set; }
        public string Presentacion { get; set; }
        public string Observacion { get; set; }

        public decimal Saldo { get; set; }
        public decimal OtrosPedidos { get; set; }
        public decimal Credito { get; set; }

        public string Fletera { get; set; }
        public int EspacioTarimas { get; set; }
        public string HoraLlegadaUnidad { get; set; }
        public string EstatusLogistico { get; set; }
        public string ObservacionLogistica { get; set; }
        public string MotivoCancelacion { get; set; }
        public string MotivoCancelacionFletera { get; set; }
        public bool Cancelado { get; set; }
        public bool CanceladoFletera { get; set; }
    }
}
