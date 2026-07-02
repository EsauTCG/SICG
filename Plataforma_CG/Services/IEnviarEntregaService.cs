namespace Plataforma_CG.Services
{
    public interface IEnviarEntregaService
    {
        Task<(bool ok, string msg)> EnviarAsync(string referencia, string source, CancellationToken ct);
    }
}
