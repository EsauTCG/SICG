namespace Plataforma_CG.Models.Comercial.Ventas
{
    public class DocumentoClienteModel
    {
        public int Id { get; set; }
        public int fk_Cliente { get; set; }
        public int fk_TipoDocumento { get; set; }
        public string NombreDocumento { get; set; }
    }
}
