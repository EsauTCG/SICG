namespace Plataforma_CG.Models
{
    public class ReporteVentasPresupuestoDetalleRow
    {
        public string ClienteId { get; set; }
        public string RazonSocial { get; set; }
        public string VendedorNombre { get; set; }
        public string Mes { get; set; }          // "2026-02"
        public decimal Venta { get; set; }
        public decimal Presupuesto { get; set; }
        public decimal? Cump { get; set; }
        public decimal? Tend { get; set; }
    }
}
