namespace Plataforma_CG.Models.Operaciones.Planeacion.Diaria
{
    public class PlaneacionProduccionModel
    {
        public int PlaneacionId { get; set; }
        public string FechaPlan { get; set; }
        public string TipoPlan { get; set; }
        public string Estatus { get; set; }
        public int Version { get; set; }
        public string Notas { get; set; }
        public int ProgramacionId { get; set; } //No se usa, se eliminará después esta columna.
        public string NombreProgramacion { get; set; } //Tampoco se usa, también se eliminará de la tabla en la base de datos. 
        public string CreadoPor { get; set; }
        public string FechaCreacion { get; set; }
        public string FechaActualizacion { get; set; }
    }
}
