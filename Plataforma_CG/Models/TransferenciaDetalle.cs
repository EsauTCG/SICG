using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    public class TransferenciaDetalle
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Transferencia")]
        public int TransferenciaId { get; set; }

        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CantidadKg { get; set; }

        [NotMapped]
        public decimal? KgConfirmadas { get; set; }

        [NotMapped]
        public decimal? cajasConfirmadas { get; set; }

        public string Nota { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Cajas { get; set; }

        public bool AutorizacionPresupuestoLinea { get; set; }

        [NotMapped]        
        public decimal KgPorCaja { get; set; } // viene de ArticuloSap.U_CAJAS


        public Transferencia Transferencia { get; set; }
    }
}
