namespace Plataforma_CG.Models.Comercial.Planeacion.Semanal
{
    public class PlanSemanalModel
    {
        public int Id { get; set; }
        public string FechaIn { get; set; }
        public string FechaFin { get; set; }
        public int Canales { get; set; }
        public double PesoPromedio { get; set; }
        public double PesoTotal { get; set; }
        public int fk_Plan { get; set; }
    }
}
