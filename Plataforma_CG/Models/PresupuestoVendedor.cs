namespace Plataforma_CG.Models
{
    public class PresupuestoVendedor
    {
        public int Id { get; set; }
     
        public int VendedorId { get; set; }


        public int Anio { get; set; }
        public int Mes { get; set; }

        public string ProductoCodigo { get; set; } = "";
        public string Master { get; set; } = "SIN_MASTER";

        public decimal Objetivo { get; set; }
        public decimal PresupuestoAsignado { get; set; }

        public string? Comentario { get; set; }
        public string CreadoPor { get; set; } = "";
        public DateTime CreadoEn { get; set; }
    }
}
