using Plataforma_CG.ViewModels;

namespace Plataforma_CG.Services
{
    public static class PlaneadorProduccionService
    {
        public static void Procesar(PlaneadorProduccionVm vm)
        {
            if (vm.Rows == null || !vm.Rows.Any())
                return;

            var grupos = vm.Rows
                .GroupBy(r => r.GrupoId);

            foreach (var grupo in grupos)
            {
                var master = grupo.FirstOrDefault(r => r.Nivel == 0);

                foreach (var row in grupo)
                {
                    if (master != null && row.Nivel != 0)
                        row.SincronizarDesdeMaster(master);

                    row.Recalcular(vm.TopCanales, vm.TopKgCanal);
                }
            }
        }
    }
}
