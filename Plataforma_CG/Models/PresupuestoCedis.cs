using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models
{
    public class PresupuestoCedis
    {
        public int Id { get; set; }
        public string Canal { get; set; } = null!;
        public int Mes { get; set; }

        
        public int Anio { get; set; }

        public string ProductoCodigo { get; set; } = null!;
        public string? Master { get; set; }
        public decimal Objetivo { get; set; }
        public decimal PresupuestoAsignado { get; set; }
        public string? Comentario { get; set; }
        public string? CreadoPor { get; set; }
        public DateTime CreadoEn { get; set; }
    }
}

