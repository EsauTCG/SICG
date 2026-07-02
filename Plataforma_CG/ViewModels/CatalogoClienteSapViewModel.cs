namespace Plataforma_CG.ViewModels
{
    public class CatalogoClienteSapViewModel
    {


        public string? CardCode { get; set; }
        public string? CardName { get; set; }
        public string? U_MT_Clasificacion { get; set; }
        public string? U_CANAL { get; set; }

        public int? PriceListNum { get; set; }
        public string? PriceListName { get; set; }

        // 🔹 Nuevos campos del vendedor
        public int? SlpCode { get; set; }          // ID del vendedor
        public string? SalesPersonName { get; set; } // Nombre del vendedor

    }
}
