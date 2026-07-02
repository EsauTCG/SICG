namespace Plataforma_CG.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("Plantilla")]
    public class Plantilla
    {
        [Key]
        public int PlantillaId { get; set; }

        [Required, StringLength(20)]
        public string Codigo { get; set; } = "";

        [StringLength(120)]
        public string? Nombre { get; set; }

        public bool Activo { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<PlantillaGrupo> PlantillaGrupos { get; set; } = new List<PlantillaGrupo>();
    }

}
