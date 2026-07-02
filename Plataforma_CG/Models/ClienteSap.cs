using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    public class ClienteSap
    {
        [Key]
        public string Cliente { get; set; } = "";
        public string Nombrecliente { get; set; } = "";
        public string U_MT_Clasificacion { get; set; } = "";
        [Column("U_CANAL")]
        public string U_CANAL { get; set; } = "";

        public int? PriceListNum { get; set; }
        public string? PriceListName { get; set; }

        // ← nuevos
        public int? VendedorId { get; set; }
        public string? VendedorNombre { get; set; }

        public bool AplicaPresupuesto { get; set; } = true;
        public DateTime FechaModificacion { get; set; } = DateTime.Now;

        

    }
}
