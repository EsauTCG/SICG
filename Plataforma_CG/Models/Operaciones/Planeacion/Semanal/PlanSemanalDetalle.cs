namespace Plataforma_CG.Models.Operaciones.Planeacion.Semanal
{
    public class PlanSemanalDetalle
    {
        public string ProductoCodigo { get; set; }
        public double Porcentaje { get; set; }
        public double Peso { get; set; }
        public string FechaInicio { get; set; }
        public string FechaFin { get; set; }
        public int SubClas { get; set; }
        public double PartSub { get; set; }
    }
}
