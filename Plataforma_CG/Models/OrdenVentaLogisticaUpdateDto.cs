namespace Plataforma_CG.Models
{
    public class OrdenVentaLogisticaUpdateDto
    {

        public int Id { get; set; }
        public DateTime? FechaEmbarque { get; set; }
        public string HoraEmbarque { get; set; } // "HH:mm"
        public string AlmacenSurtir { get; set; }
    }
}
