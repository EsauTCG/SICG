namespace Plataforma_CG.Models.Operaciones.Planeacion
{
    public class SemanaClasificacionModel
    {
        public DateTime FechaInicioSemana { get; set; }
        public DateTime FechaFinSemana { get; set; }

        public List<DetalleClasificacionSemana> Clasificaciones { get; set; }

    }
    public class DetalleClasificacionSemana
    {
        public int fk_SubClas { get; set; }
        public string Clasificacion { get; set; }
        public int TotalCanales { get; set; }
        public double PesoPromedio { get; set; }
    }
}
