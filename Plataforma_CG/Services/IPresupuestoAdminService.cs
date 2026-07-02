using Plataforma_CG.Models;

namespace Plataforma_CG.Services
{
    public interface IPresupuestoAdminService
    {
        Task<List<AdminPresupuestoRowDto>> Listar(string tipo, int mes, int anio,
            string sku, string canal, int? vendedorId, string cliente);

        Task<int> EliminarPorIds(string tipo, List<long> rowIds, string deletedBy, string reason);

        Task<int> EliminarPorFiltro(AdminDeleteByFilterRequest req, string deletedBy);
    }
}
