namespace Plataforma_CG.Models
{
    public class PrecioLineasHistorico
    {
        public int Id { get; set; }
        public DateTime FechaRegistro { get; set; }

        public int OrdenVentaId { get; set; }
        public string OrdenVentaConsecutivo { get; set; }

        public int LineaId { get; set; }

        public string ClienteId { get; set; }
        public string ClienteNombre { get; set; }

        public string ProductoCodigo { get; set; }
        public string ProductoNombre { get; set; }

        public decimal PrecioLista { get; set; }
        public decimal PrecioOVAntes { get; set; }
        public decimal PrecioAutorizado { get; set; }
        public decimal Diferencia { get; set; }

        public string Usuario { get; set; }
        public string Fuente { get; set; }   // ej. "AUTORIZACION_PRECIO"
        public string Motivo { get; set; }   // opcional
    }
}
