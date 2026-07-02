namespace Plataforma_CG.Services
{
    public interface ISapServiceLayerClient
    {
        Task<(bool ok, string? error)> EnsureLoginAsync();
        Task<(bool ok, string response, string? error)> PostJsonAsync(string relativeUrl, string json);

        // ✅ AGREGA ESTO
        Task<(bool ok, string? response, string? error, int statusCode)> GetAsync(string endpoint);

    }
}
