using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    public class Presupuesto
    {
        public int Id { get; set; }

        [Column("ClienteId")]
        public string? ClienteId { get; set; }   // Puede venir NULL

        [Column("ProductoCodigo")]
        public string ProductoCodigo { get; set; } = string.Empty; // NOT NULL en DB

        public decimal Objetivo { get; set; }     // NOT NULL en DB
        [Column("Presupuesto")]
        public decimal PresupuestoAsignado { get; set; }  // NOT NULL en DB

        public int Mes { get; set; }
        public int Año { get; set; }

        [Column("FechaCreacion")]
        public DateTime FechaRegistro { get; set; } = DateTime.Now;

        public string? Usuario { get; set; }      // NULL permitido
        public string? Comentario { get; set; }   // NULL permitido
    }
}
