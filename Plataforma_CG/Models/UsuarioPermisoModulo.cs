using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    [Table("UsuarioPermisoModulo")]
    public class UsuarioPermisoModulo
    {
        public int Id { get; set; }

        // Login del usuario (Identity.Name): "cristo.vazquez" / "DOMINIO\user"
        [Column("UsuarioKey")]
        public string UsuarioKey { get; set; } = "";

        public int ModuloId { get; set; }

        public bool PuedeLeer { get; set; }
        public bool PuedeEscribir { get; set; }
        public bool PuedeEliminar { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }
}
