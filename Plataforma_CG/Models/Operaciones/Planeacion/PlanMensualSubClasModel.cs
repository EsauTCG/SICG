namespace Plataforma_CG.Models.Operaciones.Planeacion
{
    public class PlanMensualSubClasModel
    {
        public int PlanMensualId { get; set; }
        public int fk_SubClas { get; set; }
        public string ProductoCodigo { get; set; }
        public decimal PartSub { get; set; }
    }
}
