namespace Plataforma_CG.ViewModels
{
    public sealed class SurtidoAutorizarKgVM
    {
        public int SolicitudSurtidoId { get; set; }
        public long ColaId { get; set; }
        public string Pedido { get; set; } = "";
        public string Planta { get; set; } = "";
        public string SKU { get; set; } = "";
        public int Cajas { get; set; }
        public decimal KgPlaneados { get; set; }
        public decimal KgReales { get; set; }
        public decimal DiferenciaPorcentaje { get; set; }
        public decimal ToleranciaPorcentaje { get; set; }
        public string Motivo { get; set; } = "";
        public string Contrasena { get; set; } = "";
        public bool YaAutorizada { get; set; }
        public string Error { get; set; } = "";
        public string Mensaje { get; set; } = "";
    }
}
