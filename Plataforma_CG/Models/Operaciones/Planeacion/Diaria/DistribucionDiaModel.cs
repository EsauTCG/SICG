namespace Plataforma_CG.Models.Operaciones.Planeacion.Diaria
{
    public class DistribucionDiaModel
    {
        public string Clasificacion { get; set; }
        public string SubClasificacion { get; set; }

        public string Fecha { get; set; }

        public int Cantidad { get; set; }
    }
    public class DistribucionDiaRequest
    {
        public List<DistribucionDiaModel> Distribucion { get; set; }
    }

}
