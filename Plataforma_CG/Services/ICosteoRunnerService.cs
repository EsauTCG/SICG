using Plataforma_CG.ViewModels;

namespace Plataforma_CG.Services
{
    public interface ICosteoRunnerService
    {
        Task<List<object>> EjecutarAsync(CosteoFiltroVM model, bool esAutomatico);
    }
}