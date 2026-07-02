namespace Plataforma_CG.Models.Operaciones.Inyeccion
{
    public class PermisosModel
    {
        public int usuarioId { get; set; }
        public string nombre { get; set; }
        public int fk_Permiso { get; set; }
        public string descripcion { get; set; }
    }
}
