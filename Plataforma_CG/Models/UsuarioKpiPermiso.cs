namespace Plataforma_CG.Models
{
    public class UsuarioKpiPermiso
    {
        public int Id { get; set; }

        // Ej: "AD:juan.perez" o "SQL:jperez"
        public string UsuarioKey { get; set; } = "";

        public int KpiCatalogoId { get; set; }
        public KpiCatalogo Kpi { get; set; } = default!;
    }
}
