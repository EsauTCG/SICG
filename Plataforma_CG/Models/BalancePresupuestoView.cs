namespace Plataforma_CG.Models
{
    public class BalancePresupuestoView
    {
        public int Id { get; set; }  // 🔹 Necesario para actualizar
        public string U_MASTER { get; set; }
        public string CanalVta { get; set; }
        public string Estatus { get; set; }
        public string RazonSocial { get; set; }
        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }
        public int Presupuesto { get; set; }

        // Nuevo: "GENERAL" (default) o "CEDIS"
        public string? Fuente { get; set; }

        public int? ClasificacionId { get; set; }
        public string ClasificacionNombre { get; set; }
    }
}
