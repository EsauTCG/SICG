namespace Plataforma_CG.Models
{
    public class PerfilPermisoModulo
    {
        public int Id { get; set; }
        public int PerfilId { get; set; }
        public int ModuloId { get; set; }
        public bool PuedeLeer { get; set; }
        public bool PuedeEscribir { get; set; }
        public bool PuedeEliminar { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }
}
