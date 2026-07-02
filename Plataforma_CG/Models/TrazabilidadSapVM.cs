namespace Plataforma_CG.Models
{
    public class TrazabilidadSapVM
    {
        public string Articulo { get; set; } = "";
        public string Almacen { get; set; } = "";
        public string Lote { get; set; } = "";

        public decimal KgSolicitadosJson { get; set; }
        public decimal CantidadSap { get; set; }
        public decimal ComprometidoSap { get; set; }
        public decimal DisponibleSap { get; set; }

        public decimal KgFaltantes { get; set; }
        public decimal KgSobrantes { get; set; }

        public string Estatus { get; set; } = "";
    }
}