using Plataforma_CG.Models;
using Plataforma_CG.ViewModels;

namespace Plataforma_CG.Services
{
    public interface IEntregasSapService
    {
        Task<List<EntregaSapRowVM>> ListarAsync(DateTime desde, DateTime hasta, string source);
        Task<string> BuildJsonAsync(string referenciaDocMeat, string source);
        Task<string> BuildReserveInvoiceJsonAsync(string referencia, string source);

       
        Task<string> BuildReserveInvoiceJsonManualAsync(
            string referenciaDocMeat,
            string source,
            List<ManualReserveLineDto> lines
        );

        Task<int?> TryGetEntregaDocEntryAsync(string referencia, string source);


    }
}
