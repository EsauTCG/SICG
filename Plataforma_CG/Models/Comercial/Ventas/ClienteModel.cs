namespace Plataforma_CG.Models.Comercial.Ventas
{
    public class ClienteModel
    {
        public long Codigo { get; set; }
        public string RazonSocial { get; set; }
        public string Direccion { get; set; }
        public int Estatus { get; set; }
        public string Concepto { get; set; }
        public string Telefono { get; set; }
        public string RFC { get; set; }
        public string Apellido { get; set; }
        public string Clasificacion { get; set; }
        public string CodigoSap { get; set; }
        public int fk_Zona { get; set; }
    }
}
