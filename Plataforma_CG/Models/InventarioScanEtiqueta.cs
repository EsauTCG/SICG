namespace Plataforma_CG.Models
{
    public class InventarioScanEtiqueta
    {
        public int Id { get; set; }
        public string Almacen { get; set; }
        public string CodigoEtiqueta { get; set; }
        public string Sku { get; set; }
        public decimal Kg { get; set; }
        public string Origen { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
    }
}
