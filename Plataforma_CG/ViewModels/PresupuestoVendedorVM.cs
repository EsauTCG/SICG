namespace Plataforma_CG.ViewModels
{
    public class PresupuestoVendedorSaveVM
    {
        
        public int VendedorId { get; set; }         // <-- INT
        public int Mes { get; set; }
        public int Anio { get; set; }
        public List<PresupuestoVendedorItemVM> Items { get; set; } = new();
    }

    public class PresupuestoVendedorItemVM
    {
        public string ProductoCodigo { get; set; } = "";
        public string? Master { get; set; }
        public decimal Objetivo { get; set; }
        public decimal Presupuesto { get; set; }
        public string? Comentario { get; set; }
    }
}
