namespace Plataforma_CG.Models
{
    public class ProductoVentaModel
    {
        public int Id { get; set; }
        public long fk_Orden { get; set; }
        public string SKU { get; set; }
        public double Peso { get; set; }
        public double Precio { get; set; }
        public int Cajas { get; set; }
        public string Importe { get; set; }
    }
}
