namespace Plataforma_CG.ViewModels
{
    public class SeleccionarTransferenciaVM
    {
        public string Buscar { get; set; } = "";
        public List<TransferenciaListadoVM> Resultados { get; set; } = new();
    }
}
