namespace Plataforma_CG.Models.ReporteRetrabajo
{
    public class ReporteDecomisosTIFResultados
    {
        public List<ResumenDecomisosTIF> Resumen { get; set; } = new();

        public List<DetalleNonatosTIF> DetalleNonatos { get; set; } = new();
    }
}
