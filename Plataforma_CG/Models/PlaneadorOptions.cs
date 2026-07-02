namespace Plataforma_CG.Models
{
    public class PlaneadorOptions
    {
        public List<ProgramacionCfg> Programaciones { get; set; } = new();
    }

    public class ProgramacionCfg
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string TipoPlan { get; set; } = "ALL"; // VG | NOV | RES | ALL
    }
}
