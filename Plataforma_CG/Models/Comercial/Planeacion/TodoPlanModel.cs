using Plataforma_CG.Models.Comercial.Planeacion;

namespace Plataforma_CG.Models
{
    public class TodoPlanModel
    {
        public ClasificacionModel _Clasificacion { get; set; }
        public List<ClasificacionModel> _ListaClasificacion { get; set; }
        public ClasMasterModel _ClasMaster { get; set; }
        public List<ClasMasterModel> _ListaClasMaster { get; set; }
        public MasterModel _Master { get; set; }
        public List<MasterModel> _ListaMaster { get; set; }
        public ParticipacionModel _Participacion { get; set; }
        public List<ParticipacionModel> _ListaParticipacion { get; set; }
        public PlanProduccionModel _PlanPro { get; set; }
        public List<PlanProduccionModel> _ListaPlanPro { get; set; }
        public PlanDetalleModel _PlanDetalle { get; set; }
        public List<PlanDetalleModel> _ListaPlanDetalle { get; set; }
        public ProductosModel _Productos { get; set; }
        public List<ProductosModel> _ListaProductos { get; set; }
        public TodoSemanalModel _TodSemanal { get; set; }
    }
}
