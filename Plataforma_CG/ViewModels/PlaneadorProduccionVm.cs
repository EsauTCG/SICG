namespace Plataforma_CG.ViewModels
{
    public class PlaneadorProduccionVm
    {
        public string Plan { get; set; } = "VG"; // "VG" o "NOV"
        public string Fecha { get; set; }
        public int[] TopCanales { get; set; } = new int[3];
        public decimal[] TopKgCanal { get; set; } = new decimal[3];

        public List<PlanRowVm> Rows { get; set; } = new();
        public List<(string Id, string Nombre)> Programaciones { get; set; } = new();


        // ✅ NUEVO: catálogo completo desde SkuConversion
        public List<SkuOpcionVm> SkuConversionOpciones { get; set; } = new();

        // Labels de columnas (para cambiar VG1/VG2/VR cuando sea NOV)
        public string Col1Label { get; set; } = "VG1";
        public string Col2Label { get; set; } = "VG2";
        public string Col3Label { get; set; } = "VR";
    }
}
