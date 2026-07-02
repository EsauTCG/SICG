namespace Plataforma_CG.Models
{
    public class OrdenVentaModel
    {
        public long Id { get; set; }
        public string Codigo { get; set; }
        public string Serie { get; set; }
        public string ClienteSAP { get; set; }
        public string Vendedor { get; set; }
        public string Ruta { get; set; }
        public string Presentacion { get; set; }
        public string Observacion { get; set; }
        public string FechaEntrega { get; set; }
        public string FechaCreacion { get; set; }
        public int Estado { get; set; }
    }
}
