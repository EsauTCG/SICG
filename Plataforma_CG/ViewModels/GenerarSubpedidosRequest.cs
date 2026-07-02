using System.Text.Json.Serialization;

namespace Plataforma_CG.ViewModels
{
    public class GenerarSubpedidosRequest
    {

        // Lo envías como "ordenId" desde JS
        [JsonPropertyName("ordenId")]
        public int OrdenId { get; set; }

        // Lo envías como "forzarRegeneracion" desde JS
        [JsonPropertyName("forzarRegeneracion")]
        public bool ForzarRegeneracion { get; set; } = false;
    }
}
