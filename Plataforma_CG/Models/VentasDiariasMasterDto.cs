namespace Plataforma_CG.Models
{
    public class VentasDiariasMasterDto
    {
        public string U_MASTER { get; set; }
        public string ArticuloCodigo { get; set; }
        public string Producto { get; set; }
        public decimal KgVendidos { get; set; }
        public int Dia { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }
        public int? VendedorId { get; set; }
        public string U_CANAL { get; set; }
        public string VendedorNombre { get; set; }
    }
}
