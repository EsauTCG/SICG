using Plataforma_CG.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    public interface IIntegracionSapService
    {
        Task<IntegracionSapIndexVM> ListarAsync(IntegracionSapFiltroVM filtro);
        Task<IntegracionSapRowVM?> ObtenerAsync(int integracionId, string source, string tipo);
        Task<IntegracionSapResultadoVM> EnviarAsync(
            int integracionId,
            string source,
            string tipo,
            string usuario,
            bool forzar = false);
        Task<List<IntegracionSapResultadoVM>> EnviarLoteAsync(
            IEnumerable<int> integracionIds,
            string source,
            string tipo,
            string usuario,
            bool forzar = false);
    }
}
