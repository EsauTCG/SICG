using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("ProveedorSap")]
    public class ProveedorSap
    {
        [Key]
        [MaxLength(30)]
        public string Proveedor { get; set; } = string.Empty;

        [MaxLength(200)]
        public string NombreProveedor { get; set; } = string.Empty;

        [MaxLength(200)]
        public string NombreExtranjero { get; set; } = string.Empty;

        [MaxLength(30)]
        public string RFC { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Telefono { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Celular { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Correo { get; set; } = string.Empty;

        [MaxLength(10)]
        public string Moneda { get; set; } = string.Empty;

        public int? GrupoId { get; set; }

        [MaxLength(150)]
        public string GrupoNombre { get; set; } = string.Empty;

        public int? CondicionPagoId { get; set; }

        [MaxLength(150)]
        public string CondicionPagoNombre { get; set; } = string.Empty;

        public decimal SaldoCuenta { get; set; }

        [MaxLength(250)]
        public string Direccion { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Ciudad { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Estado { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Pais { get; set; } = string.Empty;

        [MaxLength(20)]
        public string CodigoPostal { get; set; } = string.Empty;

        public bool Activo { get; set; }

        public bool Congelado { get; set; }

        public bool ExisteEnSap { get; set; } = true;

        public DateTime FechaModificacion { get; set; }
    }
}
