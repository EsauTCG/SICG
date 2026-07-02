namespace Plataforma_CG.Models.Operaciones.Planeacion.Diaria
{
    public class PlanSubClasModel
    {
        public int PlanId { get; set; }
        public int fk_SubClas { get; set; }
        public string ProductoCodigo { get; set; }
        public double PartSub { get; set; }
    }
}
