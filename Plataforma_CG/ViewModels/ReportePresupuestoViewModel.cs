namespace Plataforma_CG.ViewModels
{
    public class ReportePresupuestoViewModel
    {
        public int Id { get; set; }
        public string ClienteId { get; set; }
        public string Nombrecliente { get; set; }
        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }
        public decimal Presupuesto { get; set; }
        public int Mes { get; set; }
        public int Año { get; set; }
    }
}
