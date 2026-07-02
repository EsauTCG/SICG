namespace Plataforma_CG.ViewModels
{
    public class TransferenciaEtiquetaVM
    {
        public int Id { get; set; }

        public string CodigoEtiqueta { get; set; } = "";
        public string Sku { get; set; } = "";

        public decimal Kg { get; set; }

        public DateTime Fecha { get; set; }

        public string TarimaCodigo { get; set; }

        public string Usuario { get; set; } = "";
    }
}
