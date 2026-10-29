using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("ArticuloConsumiblesSap")]
    public class ArticuloConsumiblesSap
    {
        [Key]
        public string ProductoCodigo { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
        public string UnidadMedidaInventario { get; set; } = "";
        public string U_MASTER { get; set; } = "";
        public string U_TipoporSKU { get; set; } = "";
        public decimal? U_KilosCaja { get; set; }
        public int? U_Clas_Prod { get; set; }
        public int? U_PRESENT { get; set; }
        public int? U_PorcInye { get; set; }
        public int? ItemsGroupCode { get; set; }
        public decimal? Costo { get; set; }
        public decimal? Stock { get; set; }
        public decimal? Disponible { get; set; }
        public DateTime FechaModificacion { get; set; } = DateTime.Now;
        public bool Activo { get; set; } = true;
    }
}
