using Plataforma_CG.Models;

namespace Plataforma_CG.Services
{
    public interface IPresupuestoSettingsService
    {
        Task<PresupuestoModo> GetModoAsync();
        Task<PresupuestoModo> SetModoAsync(PresupuestoModo modo, string? updatedBy);
    }
}
