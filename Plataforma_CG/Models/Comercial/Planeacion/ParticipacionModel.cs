namespace Plataforma_CG.Models.Comercial.Planeacion
{
    public class ParticipacionModel
    {
        public int Id { get; set; }
        public string ProductoCodigo { get; set; }
        public int fk_Clasificacion { get; set; }
        public double Porcentaje { get; set; }
    }
}
