namespace Plataforma_CG.Models
{
    public class AdminDeleteByFilterRequest
    {
        public string Tipo { get; set; }              // CEDIS | VENDEDOR | CLIENTE
        public int Mes { get; set; }
        public int Anio { get; set; }

        public string Sku { get; set; }
        public string Canal { get; set; }
        public int? VendedorId { get; set; }
        public string Cliente { get; set; }

        public bool DeleteAllInScope { get; set; }    // true => borrar todo lo filtrado
        public string Reason { get; set; }
    }
}
