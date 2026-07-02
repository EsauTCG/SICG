namespace Plataforma_CG.ViewModels
{
 
    public class OrdenParaMapaView
    {
        public string? Consecutivo { get; set; }   // puede venir como texto en BD
        public string NombreCliente { get; set; } = "";
        public string Direccion { get; set; } = "";
        public string Vendedor { get; set; } = "";
        public string ProductoCodigo { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
        public decimal? KilosCaja { get; set; }   // puede venir como texto en BD
        public int? Cajas { get; set; }   // puede venir como texto en BD
    }
}
