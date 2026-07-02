namespace Plataforma_CG.Services
{
    public interface IPrintClient
    {
        Task<PrintRestResponse> PrintAsync(string baseSvcUrl, string innerJsonRequest);
    }
}
