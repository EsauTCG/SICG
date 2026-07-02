namespace Plataforma_CG.Services
{
    public record EntregaPendiente(string Referencia, string Source);

    public interface IEntregasQueryService
    {
        Task<List<EntregaPendiente>> GetPendientesAsync(string source, CancellationToken ct);
    }
}
