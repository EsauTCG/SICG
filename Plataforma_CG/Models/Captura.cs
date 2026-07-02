namespace Plataforma_CG.Models
{
    public class Captura
    {
        public string? loteId { get; set; }
        public int IdCaptura { get; set; }
        public string? Lote { get; set; }
        public string? Producto { get; set; }
        public string? ProductoSeleccionado { get; set; }
        public string? Programacion { get; set; }
        public string? SKU { get; set; }
        public string? Porcentaje { get; set; }
        public string? Velocidad { get; set; }
        public string? Modo { get; set; }
        public string? Presion { get; set; }
        public string? Altura { get; set; }
        public string? Avance { get; set; }
        public string? Tara { get; set; }
        public string? IpBascula { get; set; }
        public string? ComandoBascula { get; set; }
        public string? IpImpresora { get; set; }
        public string? PesoActual { get; set; }
        public string? PorcentajeActual { get; set; }
        public string? VelocidadActual { get; set; }
        public DateTime? FechaCaptura { get; set; }
        public string? UsuarioCorreo { get; set; }
        public string? ModoCaptura { get; set; } // "Manual" o "Automático"
    }
}