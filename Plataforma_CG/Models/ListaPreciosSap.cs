using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("ListaPreciosSap")]
    public class ListaPreciosSap
    {
        [Key]
        public int PriceListNum { get; set; }

        public string PriceListName { get; set; } = "";

        public bool Activo { get; set; }

        public decimal Factor { get; set; }

        public DateTime FechaCreacion { get; set; }
    }
}