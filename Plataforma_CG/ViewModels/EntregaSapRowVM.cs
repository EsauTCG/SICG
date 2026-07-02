namespace Plataforma_CG.ViewModels
{
    public class EntregaSapRowVM
    {

        public int PedidoId { get; set; }
        public int SolicitudSurtidoId { get; set; }
        public string ReferenciaDocMeat { get; set; } = ""; // Tipo 12
        public string Remision { get; set; } = "";          // Tipo 9

        public string? Cliente { get; set; }// Tipo 6
        public DateTime? FechaDocumento { get; set; }        // la que uses

        public bool? EnviadoSap { get; set; }        // null = sin intento
        public DateTime? FechaEnvioSap { get; set; } // opcional
        public string? MsgSap { get; set; }          // opcional

        public bool EnSap { get; set; }   // true = ya está en SAP (enviado)
    }

}
