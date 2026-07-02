namespace Plataforma_CG.Models.Operaciones.Planeacion
{
    public class PlaneacionMensualModel
    {
        public int Id { get; set; }
        public string Fecha { get; set; }
        public string SkuClasificacion { get; set; }
        public string NombreClasificacion { get; set; }
        public int Canales { get; set; }
        public double PesoPromedio { get; set; }
        public double PesoTotal { get; set; }
        public double Porcentaje { get; set; }
    }
}
