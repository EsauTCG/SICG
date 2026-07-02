namespace Plataforma_CG.Models
{
    public class KpiCatalogo
    {
        public int Id { get; set; }
        public string Titulo { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Categoria { get; set; } = "General";
        public string EmbedUrl { get; set; } = "";
        public bool Activo { get; set; } = true;
        public string? ImagenUrl { get; set; }
        public DateTime FechaAlta { get; set; } = DateTime.Now;
    }
}
