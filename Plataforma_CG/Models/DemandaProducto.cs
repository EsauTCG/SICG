using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("DemandaProducto")]
    public class DemandaProducto
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string ProductoCodigo { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? ProductoNombre { get; set; }

        [Required, MaxLength(10)]
        public string Demanda { get; set; } = "BAJA";   // BAJA | MEDIA | ALTA

        [Column(TypeName = "decimal(18,3)")]
        public decimal KgTotales { get; set; }

        public int CajasTotales { get; set; }

        public int Pedidos { get; set; }

        public int PeriodoDias { get; set; } = 90;

        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }

        [MaxLength(20)]
        public string? Temporada { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal? UmbralBaja { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal? UmbralAlta { get; set; }

        public DateTime FechaCalculo { get; set; } = DateTime.Now;

        [MaxLength(150)]
        public string? CalcPor { get; set; }
    }
}
