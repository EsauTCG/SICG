using Plataforma_CG.Models.Comercial.Planeacion.Semanal;

namespace Plataforma_CG.Models
{
    public class TodoSemanalModel
    {
        public PlanSemanalModel _PlanSemanal { get; set; }
        public List<PlanSemanalModel> _ListaPlanSemanal { get; set; }
        public DetalleSemanal _DetalleSemanal { get; set; }
        public List<DetalleSemanal> _ListaDetalleSemanal { get; set; }
        public SemanasModel _Semanas { get; set; }
        public List<SemanasModel> _ListaSemanas { get; set; }
    }
}
