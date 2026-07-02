namespace Plataforma_CG.Models
{
    public class BalanceMasterView
    {
        public string U_MASTER { get; set; } = "";
        public int? Rotacion { get; set; }
        public int? PlanProduccion { get; set; }
        public int? Inventario { get; set; }
        public int? InvIdeal { get; set; }
        public int? Disponible { get; set; }
        public int? Presupuesto { get; set; }
        public int? GAP { get; set; }
        public int? Porcentaje { get; set; }
        public int? ClasificacionId { get; set; }
        public string ClasificacionNombre { get; set; }

    }
}
