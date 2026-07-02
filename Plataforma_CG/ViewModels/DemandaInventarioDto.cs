namespace Plataforma_CG.ViewModels
{
    public sealed class DemandaInventarioDto
    {
        public string SKU { get; set; } = "";
        public string Producto { get; set; } = "";
        public decimal CajasSolicitadas { get; set; }
        public decimal KilosSolicitados { get; set; }
        public decimal CajasDisponibles { get; set; }
        public decimal KilosDisponibles { get; set; }
        public decimal CajasConfirmadas { get; set; }
        public decimal KilosConfirmados { get; set; }
    }
}
