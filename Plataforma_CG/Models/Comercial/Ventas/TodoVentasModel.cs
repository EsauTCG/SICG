using Plataforma_CG.Models.Comercial.Planeacion;
using Plataforma_CG.Models.Comercial.Ventas;

namespace Plataforma_CG.Models
{
    public class TodoVentasModel
    {
        public ChoferModel _Chofer { get; set; }
        public List<ChoferModel> _ListaChofer { get; set; }
        public int _ConteoChofer { get; set; }
        public ClienteModel _Cliente { get; set; }
        public List<ClienteModel> _ListaCliente { get; set; }
        public List<ZonasModel> _ListaZonas { get; set; }
        public int _ConteoCLienteAct { get; set; }
        public ProspectoModel _Prospecto { get; set; }
        public List<ProspectoModel> _ListaProspecto { get; set; }
        public VisitasModel _Visitas { get; set; }
        public List<VisitasModel> _ListaVisitas { get; set; }
        public ClasificacionModel _Clasificacion { get; set; }
        public List<ClasificacionModel> _ListaClasificacion { get; set; }
        //public PlaneacionModel _Planeacion { get; set; }
        //public List<PlaneacionModel> _ListPlaneacion { get; set; }
        //public PlanProdModel _PlanProd { get; set; }
        //public List<PlanProdModel> _ListaPlanProd { get; set; }
        //public PlantaModel _Planta { get; set; }
        //public List<PlantaModel> _ListaPlanta { get; set; }
    }
}
