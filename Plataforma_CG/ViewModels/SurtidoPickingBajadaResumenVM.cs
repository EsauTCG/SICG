namespace Plataforma_CG.ViewModels
{
    // Resumen de solo lectura para la pantalla de montacarguista.
    public sealed class SurtidoPickingBajadaResumenVM
    {
        public string Planta { get; set; } = "";
        public string CodigoAlmacen { get; set; } = "";
        public int SolicitudSurtidoId { get; set; }
        public string TarimaCodigo { get; set; } = "";
        public string SKU { get; set; } = "";
        public int Cajas { get; set; }
        public decimal Kg { get; set; }
        public string UltimoOperador { get; set; } = "";
        public System.DateTime UltimaBajada { get; set; }
        public bool Validado { get; set; }
    }
}
