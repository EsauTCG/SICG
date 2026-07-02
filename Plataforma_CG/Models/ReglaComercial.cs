using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    public class ReglaComercial
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(10)]
        public string Demanda { get; set; } = string.Empty;   // BAJA | MEDIA | ALTA

        [Required, MaxLength(20)]
        public string Canal { get; set; } = string.Empty;     // SPOT | ACTIVO | ESTRATÉGICO

        // NULL = NO VENDER | 0 = SIN DESCUENTO | >0 = descuento en $
        [Column(TypeName = "decimal(18,2)")]
        public decimal? DescuentoMonto { get; set; }

        public DateTime FechaModificacion { get; set; } = DateTime.Now;

        [MaxLength(150)]
        public string? ModificadoPor { get; set; }
    }
}
