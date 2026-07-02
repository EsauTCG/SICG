namespace Plataforma_CG.Models
{
    public class PresupuestoCedisView
    {
        public int Id { get; set; }
        public string U_MASTER { get; set; } = "";
        public string ProductoCodigo { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
        public string Cedis { get; set; } = "";
        public int Presupuesto { get; set; }
    }
}
