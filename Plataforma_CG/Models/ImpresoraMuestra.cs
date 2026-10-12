using System.ComponentModel.DataAnnotations;

namespace Plataforma_CG.Models
{
    public class ImpresoraMuestra
    {
        [Key]
        public int Id { get; set; }
        public string Planta { get; set; } = "P1";
        public string Nombre { get; set; } = "";
        public string IP { get; set; } = "";
        public bool Activo { get; set; } = true;
    }
}
