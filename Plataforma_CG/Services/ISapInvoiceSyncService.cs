using System.Threading;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public interface ISapInvoiceSyncService
    {
        Task<int> SincronizarInvoicesClienteAsync(string cardCode, string sqlConnectionString, CancellationToken ct = default);

        // 👇 nuevo método para sincronizar TODOS los clientes
        Task<int> SincronizarInvoicesDeTodosLosClientesAsync(string sqlConnectionString, CancellationToken ct = default);
    }
}
