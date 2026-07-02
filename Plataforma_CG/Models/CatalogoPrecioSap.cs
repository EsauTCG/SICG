namespace Plataforma_CG.Models
{
    public class CatalogoPrecioSap
    {
        public int Id { get; set; }
        public string ProductoCodigo { get; set; }
        public string Cliente { get; set; }
        public int PriceListNum { get; set; }
        public string PriceListName { get; set; }
        public decimal Precio { get; set; }       
       
        public DateTime FechaModificacion { get; set; }
    }
}
