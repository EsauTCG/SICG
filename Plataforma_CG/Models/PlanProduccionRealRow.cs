namespace Plataforma_CG.Models
{
    public class PlanProduccionRealRow
    {
        public string U_MASTER { get; set; }
        public string ArticuloCodigo { get; set; }
        public string Producto { get; set; }
        public decimal KgProducidos { get; set; }
        public int Dia { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }
        public int? ClasificacionId { get; set; }
        public string ClasificacionNombre { get; set; }
    }
}
