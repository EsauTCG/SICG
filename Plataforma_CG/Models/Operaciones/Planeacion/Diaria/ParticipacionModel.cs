using Plataforma_CG.Models.Operaciones.Planeacion.Extra;

namespace Plataforma_CG.Models.Operaciones.Planeacion.Diaria
{
    public class ParticipacionModel
    {
        public int Id { get; set; }
        public string ProductoCodigo { get; set; }
        public string Nombre { get; set; }
        public int fk_Clasificacion { get; set; }
        public double Porcentaje { get; set; }
        public int fk_SubClas { get; set; }
        public string LineaCodigo { get; set; }
        public double PartSub { get; set; }
        public string Master { get; set; }
        public bool EsExtra { get; set; }
        public string ProductoCodigoConvertido { get; set; }

        public decimal PorcentajeInyeccion { get; set; }

        public decimal KgInyeccion { get; set; }
        public PlanInyModel Iny { get; set; }
    }
}
