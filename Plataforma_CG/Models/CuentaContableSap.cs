using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("CuentaContableSap")]
    public class CuentaContableSap
    {
        [Key]
        [MaxLength(50)]
        public string CodigoCuenta { get; set; } = "";

        [MaxLength(200)]
        public string NombreCuenta { get; set; } = "";

        public DateTime FechaSincronizacion { get; set; } = DateTime.Now;

        public bool Activa { get; set; } = true;
    }
}
