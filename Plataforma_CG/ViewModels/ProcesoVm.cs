namespace Plataforma_CG.ViewModels
{
    public class ProcesoVm
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }
        public string Factor { get; set; }
    }

}
