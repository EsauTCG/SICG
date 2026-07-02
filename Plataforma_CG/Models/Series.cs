namespace Plataforma_CG.Models
{
    public class Series
    {
        public int Id { get; set; }
        public int SerieId { get; set; }        // ← este es el ID real que manda SAP
        public string NombreSerie { get; set; } // ← este es el texto que traes de OrdenVenta

        public string Sucursal { get; set; }

        public string Canal { get; set; }

        public string AlmacenTransitoId { get; set; }
    }
}
