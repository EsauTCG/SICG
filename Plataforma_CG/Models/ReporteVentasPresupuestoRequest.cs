namespace Plataforma_CG.Models
{
    public class ReporteVentasPresupuestoRequest
    {
        public int Anio { get; set; }
        public List<int> Meses { get; set; } = new List<int>();
        public List<string> Vendedores { get; set; } = new List<string>();
        public List<string> Clientes { get; set; } = new List<string>();
    }
}
