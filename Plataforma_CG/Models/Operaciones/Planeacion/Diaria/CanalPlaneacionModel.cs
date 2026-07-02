namespace Plataforma_CG.Models.Operaciones.Planeacion.Diaria
{
    public class CanalPlaneacionModel
    {
        public int PlaneacionId { get; set; }
        public int fk_SubClas { get; set; }
        public string Nombre { get; set; }
        public int NoCanalCompleta { get; set; }
        public double KgCanalCompleta { get; set; }
    }
}
