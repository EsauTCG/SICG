namespace Plataforma_CG.Models.Operaciones.Planeacion
{
    public class PlanMensualProduccionModel
    {
        public int PlaneacionMensualId { get; set; }

        public string ProductoCodigo { get; set; }

        public string ProductoCodigoConvertido { get; set; }

        public double Porcentaje { get; set; }

        public double KgLote { get; set; }

        public int Canales { get; set; }

        public int PorcentajeInyeccion { get; set; }

        public decimal KgInyeccion { get; set; }

        public List<PlanMensualSubClasModel>
            Participaciones
        {
            get;
            set;
        }
    }
}
