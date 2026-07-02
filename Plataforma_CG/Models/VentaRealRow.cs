namespace Plataforma_CG.Models
{
    public class VentaRealRow
    {

        public string U_MASTER { get; set; }
        public string ArticuloCodigo { get; set; }   // SKU
        public string Producto { get; set; }         // Nombre
        public decimal KgVendidos { get; set; }

        public int Dia { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }
        public int? ClasificacionId { get; set; }
        public string ClasificacionNombre { get; set; }
    }
}
