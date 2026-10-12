using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("TransferenciaScanEtiqueta")]
    public class TransferenciaScanEtiqueta
    {
        [Key]
        public int Id { get; set; }

        public int TransferenciaId { get; set; }

        [MaxLength(30)]
        public string Sku { get; set; } = "";

        [MaxLength(80)]
        public string CodigoEtiqueta { get; set; } = "";

        [Column(TypeName = "decimal(18,4)")]
        public decimal Kg { get; set; }

        public DateTime Fecha { get; set; }

        [MaxLength(120)]
        public string Usuario { get; set; } = "";

        [MaxLength(50)]
        public string TarimaCodigo { get; set; } = "";
    }
}
