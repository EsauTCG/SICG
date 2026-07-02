
namespace Plataforma_CG.ViewModels
{
    public class SkuOpcionVm
    {
        public string Sku { get; set; } = "";
        public string ProductoNombre { get; set; } = "";

        // ✅ % inyección normalizado a 0..1 (0.30 = 30%)
        public decimal InyPct { get; set; } = 0m;

        public string? Manejo { get; set; }

        public string? U_MASTER { get; set; }



    }

    public class PlanRowVm
    {
        public string Sku { get; set; } = "";
        public string Producto { get; set; } = "";

        public decimal VG1 { get; set; }
        public decimal VG2 { get; set; }
        public decimal VR { get; set; }

        public int CanalesAprox { get; set; }
     

        // ✅ % de la fila (0.0682 = 6.82%)
        public decimal RendPct { get; set; }

        // ✅ aquí quedará el cálculo del Excel
        public decimal? KgLote { get; set; }

        // Inyección
        public string SkuDeshuese { get; set; } = "";
        public string SkuInyeccion { get; set; } = "";
        public List<SkuOpcionVm> SkuOpcionesDetalle { get; set; } = new();

        // Empaque
        public string Presentacion { get; set; } = "";
        public string Etiquetado { get; set; } = "";
        public string Almacen { get; set; } = "";

        // (opcional) si lo necesitas
        public decimal InyPct { get; set; }

        public string Observaciones { get; set; } = "";


        public string Manejo { get; set; } = "";  // viene de presentacion.nombre


        public string? MasterSku { get; set; }   // ej: "V005"
        public int Nivel { get; set; }           // 0 = Master, 1.. = Derivado

        public int GrupoId { get; set; }         // ✅ ESTE ES EL IMPORTANTE



        public void SincronizarDesdeMaster(PlanRowVm master)
        {
            if (master == null) return;

            VG1 = Math.Clamp(master.VG1, 0, 1);
            VG2 = Math.Clamp(master.VG2, 0, 1);
            VR = Math.Clamp(master.VR, 0, 1);
        }

        public void Recalcular(
            int[] topCanales,
            decimal[] topKgCanal)
        {
            if (topCanales.Length < 3 || topKgCanal.Length < 3)
                throw new InvalidOperationException("Top arrays inválidos");

            // Canales aproximados
            CanalesAprox = (int)Math.Round(
                (topCanales[0] * VG1) +
                (topCanales[1] * VG2) +
                (topCanales[2] * VR)
            );

            // Kg lote (lógica Excel)
            var kgBase =
                (topKgCanal[0] * topCanales[0] * VG1) +
                (topKgCanal[1] * topCanales[1] * VG2) +
                (topKgCanal[2] * topCanales[2] * VR);

            KgLote = Math.Round(kgBase * RendPct, 2);
        }

    }
}
