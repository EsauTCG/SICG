using Plataforma_CG.Models;

namespace Plataforma_CG.Services
{
    public interface IAutoSapSettingsStore
    {
        AutoSapSettings Get(string source);
        List<AutoSapSettings> GetAll();
        void Set(AutoSapSettings s);
    }
}
