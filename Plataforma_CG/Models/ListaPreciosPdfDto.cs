namespace Plataforma_CG.Models
{
    public class ListaPreciosPdfDto
    {
        public EmpresaPdfDto Empresa { get; set; } = new();
        public string VigenciaTexto { get; set; } = "VIGENCIA DE PRECIOS";
        public string PlantaTexto { get; set; } = "CARNES G";
        public List<GrupoPreciosPdfDto> Grupos { get; set; } = new();
    }

    public class EmpresaPdfDto
    {
        public string NombreComercial { get; set; } = "CARNES G";
        public string RazonSocial { get; set; } = "CARNES G SA DE CV";
        public string Direccion1 { get; set; } = "Dirección de la planta";
        public string Direccion2 { get; set; } = "Ciudad, Estado, C.P.";
        public string Telefonos { get; set; } = "TEL: (000) 000-0000";
        public string Celular { get; set; } = "Celular: (000) 000-0000";
        public string VentasTexto { get; set; } = "VENTAS - CARNES G";
        public string EmailVentas { get; set; } = "ventas@carnesg.com";
        public string CertificacionTexto { get; set; } = "CONTAMOS CON CERTIFICACIÓN / PRODUCTO EMPACADO AL ALTO VACÍO";
        public byte[]? LogoBytes { get; set; }
        public byte[]? SelloBytes { get; set; }
    }

    public class GrupoPreciosPdfDto
    {
        public string Titulo { get; set; } = string.Empty;
        public List<ItemPrecioPdfDto> Items { get; set; } = new();
    }

    public class ItemPrecioPdfDto
    {
        public string Codigo { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public decimal? Precio { get; set; }
    }
}
