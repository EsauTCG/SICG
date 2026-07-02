namespace Plataforma_CG.Models
{
    public class Mercado
    {
        public string Nombre { get; set; }
        public decimal Precio { get; set; }
        public decimal Cambio { get; set; }
        public string Tendencia { get; set; } // "up", "down", "none"
        public DateTime Fecha { get; set; } // <-- agregar aquí

        public decimal Volumen { get; set; } // <-- agregar aquí
    }
}
