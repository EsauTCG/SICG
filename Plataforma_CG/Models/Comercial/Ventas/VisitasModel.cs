namespace Plataforma_CG.Models.Comercial.Ventas
{
    public class VisitasModel
    {
        public int Id { get; set; }
        public int fk_Prospecto { get; set; }
        public string FechaHora { get; set; }
        public string Ubicacion { get; set; }
        public string Usuario { get; set; }
        public string RutaFotoVisita { get; set; }
        public IFormFile Foto { get; set; }
    }
}
