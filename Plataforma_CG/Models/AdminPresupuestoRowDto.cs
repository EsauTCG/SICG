namespace Plataforma_CG.Models
{
    public class AdminPresupuestoRowDto
    {
        public long RowId { get; set; }           // id de la fila detalle
        public string Tipo { get; set; }          // CEDIS | VENDEDOR | CLIENTE

        public int Mes { get; set; }
        public int Anio { get; set; }

        public string Canal { get; set; }         // CEDIS (si aplica)
        public int? VendedorId { get; set; }      // VENDEDOR (si aplica)
        public string Cliente { get; set; }       // CLIENTE (si aplica)

        public string Sku { get; set; }
        public int Objetivo { get; set; }
        public int Presupuesto { get; set; }
        public string Comentario { get; set; }
    }
}
