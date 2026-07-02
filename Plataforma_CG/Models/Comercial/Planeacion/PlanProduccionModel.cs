namespace Plataforma_CG.Models.Comercial.Planeacion
{
    public class PlanProduccionModel
    {
        public int Id { get; set; }
        public string Fecha { get; set; }
        public int Canales { get; set; }
        public double PesoPromedio { get; set; }
        public double PesoTotal { get; set; }
        public string fk_Clasificacion { get; set; }
        
    }
}
