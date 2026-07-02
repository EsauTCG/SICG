using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("PlantillaGrupo")]
    public class PlantillaGrupo
    {
        [Key]
        public int PlantillaGrupoId { get; set; } // ✅ si NO tienes este ID, dime y lo ajusto a PK compuesta

        public int PlantillaId { get; set; }
        public int GrupoId { get; set; }

        public int OrdenGrupo { get; set; }

        public bool Activo { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        [ForeignKey(nameof(PlantillaId))]
        public Plantilla? Plantilla { get; set; }

        [ForeignKey(nameof(GrupoId))]
        public SkuGrupo? Grupo { get; set; }
    }
}
