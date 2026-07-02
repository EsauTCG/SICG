using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("SkuGrupo")]
    public class SkuGrupo
    {
        [Key]
        public int GrupoId { get; set; }

        [Required, StringLength(30)]
        public string MasterSku { get; set; } = "";

        [StringLength(200)]
        public string? NombreGrupo { get; set; }

        public bool Activo { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<SkuGrupoItem> Items { get; set; } = new List<SkuGrupoItem>();
        public ICollection<PlantillaGrupo> PlantillaGrupos { get; set; } = new List<PlantillaGrupo>();
    }
}
