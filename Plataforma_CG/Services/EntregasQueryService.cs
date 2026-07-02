using Plataforma_CG.Services;
using Plataforma_CG.ViewModels;

namespace Plataforma_CG.Services
{
    public class EntregasQueryService : IEntregasQueryService
    {
        private readonly IEntregasSapService _data;

        public EntregasQueryService(IEntregasSapService data)
        {
            _data = data;
        }

        public async Task<List<EntregaPendiente>> GetPendientesAsync(string source, CancellationToken ct)
        {
            // Ventana: últimos 2 días + hoy (ajústalo si quieres)
            var d1 = DateTime.Today.AddDays(-2).Date;
            var d2 = DateTime.Today.AddDays(1).Date.AddDays(1); // exclusivo (mañana +1 día)

            // OJO: tu controller manda d2 exclusivo.
            // Aquí le pasamos: d1 inclusive, d2 exclusivo.
            var rows = await _data.ListarAsync(d1, d2, source);

            // Pendientes/fallidas: NO están en SAP (EnviadoSap != true)
            var refs = (rows ?? new List<EntregaSapRowVM>())
                .Where(x => x != null)
                .Where(x => x.EnviadoSap != true) // incluye null (—) y false (❌)
                .Select(x => (x.ReferenciaDocMeat ?? "").Trim())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(r => new EntregaPendiente(r, source))
                .ToList();

            return refs;
        }
    }
}
