/*
namespace Plataforma_CG.ViewModels
{
    public class SkuOpcionVm
    {
        public string Sku { get; set; } = "";
        public string ProductoNombre { get; set; } = "";

        // ✅ % inyección normalizado a 0..1 (0.30 = 30%)
        public decimal InyPct { get; set; } = 0m;

        public string? Manejo { get; set; }



    }

    public class PlanRowVm
    {
        // Identidad
        public string Grupo { get; set; }
        public int Nivel { get; set; } // 0 = master

        // Entradas
        public decimal Col1 { get; set; }
        public decimal Col2 { get; set; }
        public decimal Col3 { get; set; }

        public int TopC1 { get; set; }
        public int TopC2 { get; set; }
        public int TopC3 { get; set; }

        public decimal TopKg1 { get; set; }
        public decimal TopKg2 { get; set; }
        public decimal TopKg3 { get; set; }

        public decimal Rendimiento { get; set; }

        // Calculados
        public int Canales { get; private set; }
        public int Piezas { get; private set; }
        public decimal KgLote { get; private set; }

        public string Observaciones { get; set; }

        // ===============================
        // Dominio
        // ===============================
        public void Recalcular()
        {
            Canales = (int)Math.Round(
                (TopC1 * Col1) +
                (TopC2 * Col2) +
                (TopC3 * Col3)
            );

            Piezas = Canales;

            var kgBase =
                (TopKg1 * TopC1 * Col1) +
                (TopKg2 * TopC2 * Col2) +
                (TopKg3 * TopC3 * Col3);

            KgLote = Math.Round(kgBase * Rendimiento, 2);
        }

        public void SincronizarDesdeMaster(PlanRowVm master)
        {
            Col1 = 1 - master.Col1;
            Col2 = 1 - master.Col2;
            Col3 = 1 - master.Col3;
        }
    }
}
*/