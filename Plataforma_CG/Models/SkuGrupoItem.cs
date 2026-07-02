using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("SkuGrupoItem")]
    public class SkuGrupoItem
    {
        [Key]
        //public int SkuGrupoItemId { get; set; } // ✅ si NO tienes este ID, dime y lo ajusto a PK compuesta

        public int GrupoId { get; set; }

        [Required, StringLength(30)]
        public string Sku { get; set; } = "";

        [StringLength(30)]
        public string? ParentSku { get; set; }

        public int Nivel { get; set; }
        public int Orden { get; set; }

        [StringLength(30)]
        public string? TipoRelacion { get; set; }

        public bool Activo { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        [ForeignKey(nameof(GrupoId))]
        public SkuGrupo? Grupo { get; set; }
    }
}
