using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("UsuarioSerie")]
    public class UsuarioSerie
    {
        public int Id { get; set; }

        [Required]
        public int UsuarioId { get; set; }

        [Required]
        public int SerieId { get; set; }

        public DateTime FechaAsignacion { get; set; } = DateTime.Now;

        [ForeignKey(nameof(UsuarioId))]
        public UsuarioSQL? Usuario { get; set; }

        [ForeignKey(nameof(SerieId))]
        public Series? Serie { get; set; }
    }
}