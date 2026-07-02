namespace Plataforma_CG.Services
{
    public interface ISapDireccionesSyncService
    {
        // 🔹 Un solo cliente
        Task<int> SincronizarDireccionesClienteDesdeSapAsync(string cardCode);

        // 🔹 Clientes existentes en tu BD
        Task<int> SincronizarDireccionesClientesDesdeSapAsync();

        // 🔹 TODOS los clientes desde SAP
        Task<int> SincronizarDireccionesTodosClientesAsync();
    }
}