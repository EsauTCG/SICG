namespace Plataforma_CG.Models.Comercial.Planeacion
{
    public class PlanDetalleModel
    {
        public int Id { get; set; }
        public string ProductoCodigo { get; set; }
        public double Porcentaje { get; set; }
        public double Peso { get; set; }
        public int fk_Plan { get; set; }
    }
}
