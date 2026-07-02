namespace Plataforma_CG.Models
{
    public class BalancePlanProduccionView
    {
        public int Id { get; set; }  // 🔹 Necesario para actualizar
        public string U_MASTER { get; set; }
        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }
        public decimal Plan { get; set; }
        public int? ClasificacionId { get; set; }
        public string ClasificacionNombre { get; set; }
    }
}
