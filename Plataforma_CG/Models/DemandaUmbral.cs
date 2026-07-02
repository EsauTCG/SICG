using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("DemandaUmbral")]
    public class DemandaUmbral
    {
        [Key]
        public int Id { get; set; }

        public int PeriodoDias { get; set; } = 90;

        [Column(TypeName = "decimal(18,3)")]
        public decimal UmbralBaja { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal UmbralAlta { get; set; }

        public DateTime FechaCalculo { get; set; } = DateTime.Now;

        public int TotalSkus { get; set; }
    }
}
