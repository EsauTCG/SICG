using Microsoft.EntityFrameworkCore;


namespace Plataforma_CG.Models
{
    public class Permiso
    {
        public int Id { get; set; }

        public int PerfilId { get; set; }
        public Perfil Perfil { get; set; } // ← propiedad de navegación

        public int VistaId { get; set; }
        public Vista Vista { get; set; }
    }
}
