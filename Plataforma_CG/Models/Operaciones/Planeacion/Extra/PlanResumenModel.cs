namespace Plataforma_CG.Models.Operaciones.Planeacion.Extra
{
    public class PlanResumenModel
    {
        public string ProductoCodigo { get; set; }

        public string ProductoCodigoConvertido { get; set; }

        public decimal KgNatural { get; set; }

        public decimal PorcentajeInyeccion { get; set; }

        public decimal KgInyeccion { get; set; }

        public int fk_Clasificacion { get; set; }

        public decimal Porcentaje { get; set; }

        public string LineaCodigo { get; set; }

        public string Master { get; set; }
    }
}
