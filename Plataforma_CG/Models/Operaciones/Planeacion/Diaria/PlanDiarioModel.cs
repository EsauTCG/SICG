namespace Plataforma_CG.Models.Operaciones.Planeacion.Diaria
{
    public class PlanDiarioModel
    {
        public int PlaneacionId { get; set; }
        public string ProductoCodigo { get; set; }
        public double Porcentaje { get; set; }
        public double KgLote { get; set; }
        public int Canales { get; set; }
        public string ProductoCodigoConvertido { get; set; }
        public int PorcentajeInyeccion { get; set; }
        public decimal KgInyeccion { get; set; }
        public List<PlanSubClasModel> Participaciones { get; set; }
    }
}
