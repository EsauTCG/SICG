using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models
{


    //TABLA DIRECTA DE SQL SERVER ARTICULOSAP
    public class ArticuloSap
    {
        
        [Key]
        public string ProductoCodigo { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
        public string U_MASTER { get; set; } = "";
        public string U_TipoporSKU { get; set; } = "";

        public decimal? U_KilosCaja { get; set; }   // ⬅️ nuevo

        // ✅ NUEVOS
        public int? U_Clas_Prod { get; set; }
        public int? U_PRESENT { get; set; }
        public int? U_PorcInye { get; set; }

        public DateTime FechaModificacion { get; set; } = DateTime.Now;

        public int? Rotacion { get; set; }
    }
}
