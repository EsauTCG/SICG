namespace Plataforma_CG.Models.Comercial.Ventas
{
    public class VendClienteModel
    {
        public int Id { get; set; }
        public int idVendedor { get; set; }
        public string NombreVendedor { get; set; }
        public int CodigoCLiente { get; set; }
        public string RazonSocial { get; set; }
        public string CodigoSap { get; set; }
    }
}
