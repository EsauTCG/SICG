namespace Plataforma_CG.Models
{
    public class SapDocMeatPayload
    {

        public int? SubpedidoId { get; set; }    // preferido
        public string? DocumentoSAP { get; set; } // fallback si no mandan Id
        public string? U_DocMeat { get; set; }    // valor que envía SAP
    }
}
