namespace Plataforma_CG.Models.Operaciones.Planeacion.Diaria
{
    public class TotalDiarioModel
    {
        public string Fecha { get; set; }

        public List<CanalPlaneacionModel> Canales { get; set; }

        public List<ParticipacionModel> Participaciones { get; set; }
        public string TipoPlan { get; set; }
    }
}
