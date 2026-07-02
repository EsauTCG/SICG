namespace Plataforma_CG.Models
{
    public class DemandaDetalleDto
    {
        public string Origen { get; set; } = "";
        public string Cliente { get; set; } = "";
        public string Ruta { get; set; } = "";
        public string ProductoCodigo { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
        public decimal Cajas { get; set; }
        public decimal Kg { get; set; }
        public DateTime FechaEmbarcar { get; set; }
    }
}
