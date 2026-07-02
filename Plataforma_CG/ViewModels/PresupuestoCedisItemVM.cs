namespace Plataforma_CG.ViewModels
{
    public class PresupuestoCedisItemVM
    {

        public string ProductoCodigo { get; set; } = default!;
        public string? Master { get; set; }
        public decimal Objetivo { get; set; }
        public decimal Presupuesto { get; set; }          // <- viene del input .pre
        public string? Comentario { get; set; }
    }
}
