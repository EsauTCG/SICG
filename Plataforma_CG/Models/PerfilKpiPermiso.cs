namespace Plataforma_CG.Models
{
    public class PerfilKpiPermiso
    {
        public int Id { get; set; }

        public int PerfilId { get; set; }
        public Perfil Perfil { get; set; } = null!;

        public int KpiCatalogoId { get; set; }
        public KpiCatalogo KpiCatalogo { get; set; } = null!;
    }
}
