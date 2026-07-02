namespace Plataforma_CG.ViewModels
{
    public class TransferenciaScanEtiquetaDetalleVM
    {
        public int TransferenciaId { get; set; }
        public string Sku { get; set; }
        public string CodigoEtiqueta { get; set; }
        public decimal Kg { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public string TarimaCodigo { get; set; }
    }
}
