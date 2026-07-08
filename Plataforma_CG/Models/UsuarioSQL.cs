using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models
{
    [Table("UsuarioSQL")]
    public class UsuarioSQL
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "El usuario es obligatorio")]
        public string Usuario { get; set; }
        [Required(ErrorMessage = "La contraseña es obligatoria")]
        public string Password { get; set; } = string.Empty;
        public string Nombre { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; }
        [Required(ErrorMessage = "Debe seleccionar un perfil")]
        public int PerfilId { get; set; }  
        [ForeignKey("PerfilId")]
        public Perfil? Perfil { get; set; }
        public bool EsVendedor { get; set; }
        public int? VendedorId { get; set; }
        public bool IgnoraFiltroSerieTransferencias { get; set; } = false;
        public ICollection<UsuarioSerie> UsuarioSeries { get; set; } = new List<UsuarioSerie>();

        [NotMapped]
        public List<int> SeriesSeleccionadasIds { get; set; } = new();


        public string? AlmacenesPermitidos { get; set; }

        [NotMapped]
        public List<string> AlmacenesSeleccionados { get; set; } = new();
        

        // 🔐 CAMPOS SOLO PARA EDICIÓN
        [NotMapped]
        [DataType(DataType.Password)]
        public string? NuevaPassword { get; set; }
        [NotMapped]
        [DataType(DataType.Password)]
        [Compare("NuevaPassword", ErrorMessage = "Las contraseñas no coinciden")]
        public string? ConfirmarPassword { get; set; }

    }
}