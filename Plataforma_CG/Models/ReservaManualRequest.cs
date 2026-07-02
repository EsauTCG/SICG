namespace Plataforma_CG.Models
{
    public class ReservaManualRequest
    {
        public string Referencia { get; set; }
        public string Source { get; set; }
        public List<ManualReserveLineDto> Lineas { get; set; }
    }
}
