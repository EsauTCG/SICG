namespace Plataforma_CG.Models.Operaciones.Planeacion.Extra
{
    public class SolicitudGuardarModel
    {
        public string Fecha { get; set; }
        public string TipoId { get; set; }
        public string TipoNombre { get; set; }
        public string Comentarios { get; set; }
        public List<SolicitudProductoModel> Productos { get; set; }
    }
}
