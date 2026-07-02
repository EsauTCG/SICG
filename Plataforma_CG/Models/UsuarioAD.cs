using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("UsuariosAD")]
    public class UsuarioAD
    {
        public int Id { get; set; }
        public string? UsuarioAd { get; set; }
        public string? Nombre { get; set; }
        public string? Puesto { get; set; }
        public bool EsVendedor { get; set; }
        public int? VendedorId { get; set; }


        public int? PerfilId { get; set; }

        [ForeignKey("PerfilId")]
        public Perfil? Perfil { get; set; }
    }
}
