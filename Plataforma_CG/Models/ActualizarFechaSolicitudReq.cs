namespace Plataforma_CG.Models
{
    public class ActualizarFechaSolicitudReq
    {
        public int transferenciaId { get; set; }
        public string fechaSolicitud { get; set; } // "yyyy-MM-dd"
    }
}
